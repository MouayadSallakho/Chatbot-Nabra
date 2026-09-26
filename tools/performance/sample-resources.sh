#!/usr/bin/env bash
# Samples container, PostgreSQL and Redis resource usage once per interval while a
# load scenario runs. NON-PRODUCTION: it only talks to local Docker containers by
# name and prints aggregate counts -- never query text, connection strings,
# credentials or row contents.
#
# Usage: sample-resources.sh <out.csv> [interval-seconds] [pg-container] [redis-container] [database]
# Stop it with Ctrl+C (or kill); each line is flushed as it is written.
set -u
OUT=${1:?output csv path}
INTERVAL=${2:-1}
PG=${3:-omniai_p34_pg}
REDIS=${4:-omniai_p34_redis}
DB=${5:-omniai_p34}
CONTAINERS=${SAMPLE_CONTAINERS:-"omniai_p34_api_a omniai_p34_api_b $PG $REDIS"}

echo "ts,container_stats,pg_total,pg_active,pg_idle,pg_idle_in_tx,pg_waiting_lock,pg_longest_active_ms,pg_xact_commit,pg_deadlocks,redis_clients,redis_used_mb,redis_ops_per_sec" > "$OUT"

while true; do
  TS=$(date -u +%Y-%m-%dT%H:%M:%SZ)
  # name=cpu%/mem joined with '|' so the CSV stays one line per sample
  RUNNING=$(for c in $CONTAINERS; do docker inspect -f "{{if .State.Running}}{{.Name}}{{end}}" "$c" 2>/dev/null; done | tr -d / | xargs)
  STATS=$(docker stats --no-stream --format '{{.Name}}={{.CPUPerc}}/{{.MemUsage}}' $RUNNING 2>/dev/null \
    | sed 's/ \/ [0-9.]*[GM]iB//; s/ //g' | paste -sd'|' -)
  PGROW=$(docker exec "$PG" psql -U postgres -d "$DB" -tA -F, -c "
    select count(*),
           count(*) filter (where state = 'active'),
           count(*) filter (where state = 'idle'),
           count(*) filter (where state like 'idle in transaction%'),
           count(*) filter (where wait_event_type = 'Lock'),
           coalesce(max(extract(epoch from (now() - query_start)) * 1000)
                    filter (where state = 'active' and pid <> pg_backend_pid()), 0)::bigint,
           (select xact_commit from pg_stat_database where datname = current_database()),
           (select deadlocks from pg_stat_database where datname = current_database())
    from pg_stat_activity where datname = current_database() and pid <> pg_backend_pid();" 2>/dev/null || echo ",,,,,,,")
  REDISROW=$(docker exec "$REDIS" redis-cli info 2>/dev/null | tr -d '\r' | awk -F: '
    /^connected_clients:/ {c=$2} /^used_memory:/ {m=$2/1048576} /^instantaneous_ops_per_sec:/ {o=$2}
    END {printf "%s,%.2f,%s", c, m, o}')
  echo "$TS,$STATS,$PGROW,$REDISROW" >> "$OUT"
  sleep "$INTERVAL"
done
