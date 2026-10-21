#!/usr/bin/env bash
# Staged closed-loop k6 run with a warm-up and per-stage resource sampling.
# NON-PRODUCTION. k6 runs in the network namespace of a LOCAL container (default
# omniai_p34_edge) and targets loopback only; lib.js refuses anything else.
#
# Usage: run-stages.sh <script.js> "<vu stages>" [stage-seconds] [warmup-seconds] [-- extra k6 args]
#   e.g. run-stages.sh admin-reads.js "1 5 10 25 50 100" 30 15
# Env:  OMNIAI_PERF_RESULTS  output directory (required; keep it outside the repository)
#       OMNIAI_PERF_NETNS    container whose network namespace k6 joins (omniai_p34_edge)
#       OMNIAI_PERF_BASE     loopback base inside that namespace (http://127.0.0.1:8080)
#       K6_CPUS              CPU limit for the load generator container (2)
#       SEED_PASSWORD        the synthetic accounts' password (required by the k6 scenarios;
#                            no committed default). Passed to k6 by name only.
set -euo pipefail
SCRIPT=${1:?k6 script}
STAGES=${2:?vu stages}
SECONDS_PER_STAGE=${3:-30}
WARMUP=${4:-15}
shift $(( $# >= 4 ? 4 : $# ))
[ "${1:-}" = "--" ] && shift
OUT=${OMNIAI_PERF_RESULTS:?set OMNIAI_PERF_RESULTS outside the repository}
NETNS=${OMNIAI_PERF_NETNS:-omniai_p34_edge}
BASE=${OMNIAI_PERF_BASE:-http://127.0.0.1:8080}
HERE=$(cd "$(dirname "$0")" && pwd)
NAME=$(basename "$SCRIPT" .js)
IMAGE=grafana/k6:1.3.0
EXTRA=("$@")
# The value reaches the container by name (docker -e NAME), never on a command
# line. Without it the scenarios stop in their init stage, before any request.
PASS_ENV=()
if [ -n "${SEED_PASSWORD:-}" ]; then
  export PASSWORD="$SEED_PASSWORD"
  PASS_ENV=(-e PASSWORD)
fi
mkdir -p "$OUT"

k6run() { # vus seconds tag
  MSYS_NO_PATHCONV=1 docker run --rm --name "omniai_perf_k6_$3" --network "container:$NETNS" \
    --cpus "${K6_CPUS:-2}" -v "$(cygpath -m "$HERE/k6" 2>/dev/null || echo "$HERE/k6"):/scripts:ro" \
    -v "$(cygpath -m "$OUT" 2>/dev/null || echo "$OUT"):/out" \
    "${PASS_ENV[@]}" \
    "$IMAGE" run --quiet --vus "$1" --duration "${2}s" -e "BASE=$BASE" -e "STAGE_VUS=$1" "${EXTRA[@]}" \
    --summary-export "/out/$NAME-$3.json" "/scripts/$SCRIPT" >/dev/null 2>"$OUT/$NAME-$3.stderr" || true
}

echo "warm-up ${WARMUP}s"
k6run 5 "$WARMUP" warmup

for VUS in $STAGES; do
  bash "$HERE/sample-resources.sh" "$OUT/$NAME-vu$VUS.resources.csv" 2 &
  SAMPLER=$!
  (sleep 3; docker stats --no-stream --format '{{.Name}} {{.CPUPerc}}' "omniai_perf_k6_vu$VUS" 2>/dev/null > "$OUT/$NAME-vu$VUS.k6cpu") &
  k6run "$VUS" "$SECONDS_PER_STAGE" "vu$VUS"
  kill "$SAMPLER" 2>/dev/null || true
  wait 2>/dev/null || true
  node "$HERE/summarise.mjs" "$OUT/$NAME-vu$VUS.json" "$OUT/$NAME-vu$VUS.resources.csv" "$VUS" "$(cat "$OUT/$NAME-vu$VUS.k6cpu" 2>/dev/null || true)"
done
