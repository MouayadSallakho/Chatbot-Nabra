#!/usr/bin/env bash
# Rolling release of OmniAI on the single-host compose topology.
#
#   deploy/scripts/rolling-deploy.sh <env-file>
#
# The env file names the NEW images (OMNIAI_API_IMAGE, OMNIAI_MIGRATOR_IMAGE,
# OMNIAI_EDGE_IMAGE). Steps, stopping at the first failure:
#   1. migrate once, as the owner role (expand-only migrations; see
#      docs/operations/production-deployment.md section 7);
#   2. drain api-a at the edge, replace it, wait until it is healthy (readiness:
#      database with every migration applied, Redis), route to it again;
#   3. replace api-b, the same;
#   4. replace the edge if its image changed.
# Take a backup before running this. Nothing here prints a secret.
set -euo pipefail

ENV_FILE=${1:?usage: rolling-deploy.sh <env-file>}
HERE=$(cd "$(dirname "$0")/.." && pwd)
# Git Bash on Windows: hand Docker a Windows path (no-op on Linux).
if command -v cygpath >/dev/null 2>&1; then HERE=$(cygpath -m "$HERE"); export MSYS_NO_PATHCONV=1; fi
COMPOSE=(docker compose -f "$HERE/compose.production.yml")
# Optional site-specific override file (e.g. monitoring sidecars), same as `up`.
[[ -n ${OMNIAI_COMPOSE_OVERRIDE:-} ]] && COMPOSE+=(-f "$OMNIAI_COMPOSE_OVERRIDE")
COMPOSE+=(--env-file "$ENV_FILE")
HEALTH_TIMEOUT=${OMNIAI_HEALTH_TIMEOUT_SECONDS:-120}

log() { printf '%s %s\n' "$(date -u +%H:%M:%SZ)" "$*"; }

wait_healthy() {
  local service=$1 waited=0 state
  while :; do
    state=$(docker inspect -f '{{.State.Health.Status}}' "$("${COMPOSE[@]}" ps -q "$service")" 2>/dev/null || echo missing)
    [[ $state == healthy ]] && { log "$service healthy after ${waited}s"; return 0; }
    if (( waited >= HEALTH_TIMEOUT )); then
      log "$service NOT healthy after ${HEALTH_TIMEOUT}s (state: $state); stopping the rollout"
      "${COMPOSE[@]}" logs --tail 50 "$service" >&2 || true
      return 1
    fi
    sleep 2; waited=$((waited + 2))
  done
}

# The edge's fixed address for each instance (compose.production.yml).
address() { case $1 in api-a) echo 172.30.35.21:8080 ;; api-b) echo 172.30.35.22:8080 ;; esac; }

# Take an instance out of routing at the edge BEFORE stopping it, so no new
# request is sent to a process that is shutting down (see readiness-watcher.sh).
edge_drain() { # $1 service, $2 on|off
  local a; a=$(address "$1")
  if [[ $2 == on ]]; then
    "${COMPOSE[@]}" exec -T edge sh -c "mkdir -p /tmp/omniai/drain && touch /tmp/omniai/drain/$a"
    for _ in $(seq 1 30); do
      "${COMPOSE[@]}" exec -T edge grep -q "server $a .* down;" /tmp/omniai/upstreams.conf && { log "$1 drained at the edge"; sleep 2; return 0; }
      sleep 1
    done
    log "$1 could not be drained at the edge; stopping the rollout"; return 1
  else
    "${COMPOSE[@]}" exec -T edge rm -f "/tmp/omniai/drain/$a"
    for _ in $(seq 1 30); do
      "${COMPOSE[@]}" exec -T edge grep -q "server $a .* down;" /tmp/omniai/upstreams.conf || { log "$1 routed again"; return 0; }
      sleep 1
    done
    log "$1 is healthy but not yet routed (readiness watcher)"; return 0
  fi
}

log "1/4 migrating"
"${COMPOSE[@]}" run --rm migrator
"${COMPOSE[@]}" run --rm migrator-privileges
log "migrations applied; runtime role has read-only migration history"

for service in api-a api-b; do
  edge_drain "$service" on
  log "replacing $service (in-flight requests get the 30 s stop grace)"
  "${COMPOSE[@]}" up -d --no-deps --force-recreate "$service"
  wait_healthy "$service"
  edge_drain "$service" off
done

log "4/4 edge"
"${COMPOSE[@]}" up -d --no-deps edge

log "release complete"
"${COMPOSE[@]}" ps
