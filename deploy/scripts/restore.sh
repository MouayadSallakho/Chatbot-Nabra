#!/usr/bin/env bash
# OmniAI restore into an EMPTY database of a freshly initialised stack.
#
#   deploy/scripts/restore.sh <env-file> <dump-file>
#
# Preconditions (checked): the stack's postgres was just initialised (roles and
# extension exist, schema empty) and no API is running against it. The env file
# must carry the SAME OMNIAI_SECRET_MASTER_KEY as the backed-up deployment.
#
# Never restore over a live database: restore to a new stack, verify, then
# switch traffic. See docs/operations/backup-restore.md.
set -euo pipefail

ENV_FILE=${1:?usage: restore.sh <env-file> <dump-file>}
DUMP=${2:?usage: restore.sh <env-file> <dump-file>}
HERE=$(cd "$(dirname "$0")/.." && pwd)
# Git Bash on Windows: hand Docker a Windows path (no-op on Linux).
if command -v cygpath >/dev/null 2>&1; then HERE=$(cygpath -m "$HERE"); export MSYS_NO_PATHCONV=1; fi
COMPOSE=(docker compose -f "$HERE/compose.production.yml" --env-file "$ENV_FILE")
DB=$(grep -E '^OMNIAI_DB=' "$ENV_FILE" | cut -d= -f2-)

if [[ -f "$DUMP.sha256" ]]; then
  (cd "$(dirname "$DUMP")" && sha256sum -c "$(basename "$DUMP").sha256")
fi

running=$("${COMPOSE[@]}" ps --status running -q api-a api-b 2>/dev/null || true)
if [[ -n $running ]]; then
  echo "An API is running against this stack; stop it before restoring." >&2
  exit 1
fi

tables=$("${COMPOSE[@]}" exec -T postgres psql -U postgres -d "$DB" -At -c \
  "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public'")
if [[ $tables != 0 ]]; then
  echo "The target database is not empty ($tables tables); refusing to restore over it." >&2
  exit 1
fi

"${COMPOSE[@]}" exec -T postgres pg_restore -U postgres -d "$DB" \
  --exit-on-error --single-transaction --no-comments < "$DUMP"

echo "restored. Next: run the migrator (applies nothing if the dump is current), then start the APIs."
