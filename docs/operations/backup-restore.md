# Backup and restore

How OmniAI's state is backed up and restored, and what a restore guarantees.
Scripts: [`deploy/scripts/backup.sh`](../../deploy/scripts/backup.sh),
[`deploy/scripts/restore.sh`](../../deploy/scripts/restore.sh). Everything here
was proven in the Phase 35 disaster-recovery drill, which was local and used
synthetic data (see the
[report](../phase-reports/PHASE_35_PRODUCTION_READINESS_FINAL_RELEASE_REPORT.md)).

## 1. What must be backed up

| State | Where | Backed up by | If lost |
| --- | --- | --- | --- |
| **Everything durable**: workspaces, users, conversations, knowledge, ledger, billing, notifications, the Data Protection key ring | PostgreSQL `pgdata` | `backup.sh`: `pg_dump` in custom format, a consistent snapshot taken while the application runs | total loss; this is the system of record |
| **`OMNIAI_SECRET_MASTER_KEY`** | your secret store | **separately, never beside the dump** | every session is invalidated, and stored channel credentials (WhatsApp tokens) become unreadable and must be reconnected |
| Report artifacts | `reports` volume | `backup.sh --with-reports` (optional, see §2) | downloads of past reports fail truthfully; re-run the reports |
| TLS certificate and key | `OMNIAI_TLS_DIR` | your certificate process | re-issue |
| Redis | — | **not backed up**, by design | nothing durable: rate-limit windows and realtime tickets restart empty |

The dump already contains the Data Protection key ring, encrypted with the
master key. That is exactly why the key must never be stored next to the dump:
together they are the secret. Apart, the dump alone cannot decrypt sessions or
channel credentials.

## 2. Report artifacts: the decision

Artifacts are **derived and short-lived**. They are regenerable from the
database, and retention is 30 days. The database row, not the file, is the
record. When a file is missing, the existing missing-artifact reconciliation
reports it truthfully instead of serving anything wrong.

**Decision for V1.0: back up the volume on a best-effort basis
(`--with-reports`, daily is enough), and treat it as non-critical.** The recovery
point for artifacts may lag the database's. Users re-run any report whose file
is gone.

## 3. Backing up

```sh
deploy/scripts/backup.sh deploy/.env /secure/backups/omniai --with-reports
```

The script writes:
- `omniai-<UTC>.dump` (mode 600), plus a `.sha256`, a `.toc` listing that proves
  the archive is readable, and a `.meta` recording the latest migration;
- with `--with-reports`, `omniai-reports-<UTC>.tar.gz` plus its `.sha256`, taken
  from the volume actually mounted on the running API (never a guessed name).

Take one before every release. Keep the files off the database host, and test a
restore regularly (§5).

## 4. Restoring (disaster recovery)

Always restore into a **new, empty** stack. **Never restore over a live
database.**

```sh
# 1. fresh stack, same .env (same master key!), only postgres
docker compose -f deploy/compose.production.yml --env-file deploy/.env up -d postgres
# 2. restore; refuses if an API is running or the database is not empty
deploy/scripts/restore.sh deploy/.env /secure/backups/omniai/omniai-<UTC>.dump
# 3. optional: artifacts into the new reports volume (owned by uid 1654)
docker run --rm -v <project>_reports:/data -v /secure/backups/omniai:/b alpine:3.20 \
  sh -c 'tar -C /data -xzf /b/omniai-reports-<UTC>.tar.gz && chown -R 1654 /data'
# 4. everything else; the migrator runs first and must report "No migrations were applied"
docker compose -f deploy/compose.production.yml --env-file deploy/.env up -d
```

The restore runs in one transaction (`--single-transaction --exit-on-error`), so
it either completes or leaves the database empty.

Restoring a backup taken on an **older** release is fine. The migrator brings it
forward, one generated migration at a time.

### What the Phase 35 drill proved

1. Back up while running.
2. Destroy the stack including its volumes.
3. Start a fresh `postgres`, which initialises its roles and extension.
4. Restore, then restore the artifacts.
5. Start the stack.

| Check | Result |
| --- | --- |
| Time from destroying the stack to both APIs ready | ~28 s (1.7 MB database) |
| Row counts, all 49 tables | identical |
| Migrator after the restore | "No migrations were applied" |
| **A session cookie issued before the loss** | still valid afterwards (200), so the key ring and master key restored together |
| Three report artifacts (CSV, PDF, XLSX) | byte-identical (SHA-256) |

The drill also found a real defect before it could ship. The first version of
`backup.sh` guessed the report volume's name, and would silently have archived an
empty volume. It now reads the volume that is actually mounted, and refuses when
there is none.

## 5. Routine restore test

Monthly, and after any change to the backup scripts, restore the latest backup
into a throwaway stack and run the §4 checks. Then compare row counts, confirm
the migrator is a no-op, and sign in once.

## 6. Recovery objectives (starting point)

| Objective | V1.0 guidance |
| --- | --- |
| RPO, database | backup interval (daily minimum, plus one before every release). For a tighter RPO, add WAL archiving or point-in-time recovery on managed PostgreSQL; that is not in the V1.0 scripts. |
| RPO, artifacts | best effort (§2) |
| RTO | minutes for this data size. Restore time grows with the database; measure it with the routine test. |
