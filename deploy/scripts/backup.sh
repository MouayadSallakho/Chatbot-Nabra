#!/usr/bin/env bash
# OmniAI backup: a consistent PostgreSQL dump (custom format) and, optionally,
# the report artifact volume.
#
#   deploy/scripts/backup.sh <env-file> <output-dir> [--with-reports]
#
# What is NOT here, deliberately: OMNIAI_SECRET_MASTER_KEY. The dump contains the
# Data Protection key ring and channel credentials encrypted with it; keeping the
# key beside the dump would make the dump the secret. Back it up separately, in
# your secret store. A restore without the same key logs everybody out and cannot
# read stored channel credentials.
#
# pg_dump takes a consistent snapshot while the application keeps running.
set -euo pipefail

ENV_FILE=${1:?usage: backup.sh <env-file> <output-dir> [--with-reports]}
OUT=${2:?usage: backup.sh <env-file> <output-dir> [--with-reports]}
WITH_REPORTS=${3:-}
HERE=$(cd "$(dirname "$0")/.." && pwd)
# Git Bash on Windows: hand Docker a Windows path (no-op on Linux).
if command -v cygpath >/dev/null 2>&1; then HERE=$(cygpath -m "$HERE"); export MSYS_NO_PATHCONV=1; fi
COMPOSE=(docker compose -f "$HERE/compose.production.yml" --env-file "$ENV_FILE")
DB=$(grep -E '^OMNIAI_DB=' "$ENV_FILE" | cut -d= -f2-)
STAMP=$(date -u +%Y%m%dT%H%M%SZ)

mkdir -p "$OUT"
umask 077

DUMP="$OUT/omniai-$STAMP.dump"
"${COMPOSE[@]}" exec -T postgres pg_dump -U postgres --format=custom --compress=6 "$DB" > "$DUMP"

# Verify the archive is readable and record what it holds.
"${COMPOSE[@]}" exec -T postgres pg_restore --list < "$DUMP" > "$DUMP.toc"
sha256sum "$DUMP" > "$DUMP.sha256"
"${COMPOSE[@]}" exec -T postgres psql -U postgres -d "$DB" -At -c \
  "SELECT 'migration ' || max(\"MigrationId\") FROM \"__EFMigrationsHistory\"" > "$DUMP.meta"

if [[ $WITH_REPORTS == --with-reports ]]; then
  # The volume actually mounted at the report path of a running API -- never a
  # guessed name, which would silently archive an empty new volume.
  API=$("${COMPOSE[@]}" ps -q api-a)
  VOLUME=$(docker inspect -f '{{range .Mounts}}{{if eq .Destination "/var/lib/omniai/reports"}}{{.Name}}{{end}}{{end}}' "$API")
  if [[ -z $VOLUME ]]; then
    echo "No report volume is mounted on api-a; refusing to archive a guess." >&2
    exit 1
  fi
  OUT_ABS=$(cd "$OUT" && pwd)
  command -v cygpath >/dev/null 2>&1 && OUT_ABS=$(cygpath -m "$OUT_ABS")
  docker run --rm -v "$VOLUME:/data:ro" -v "$OUT_ABS:/backup" \
    alpine:3.20 tar -C /data -czf "/backup/omniai-reports-$STAMP.tar.gz" .
  sha256sum "$OUT/omniai-reports-$STAMP.tar.gz" > "$OUT/omniai-reports-$STAMP.tar.gz.sha256"
fi

printf 'backup written: %s (%s bytes)\n' "$DUMP" "$(wc -c < "$DUMP")"
