# Operations runbook

Day-2 operation of an OmniAI deployment: what to watch, what to alert on, and
what to do. It covers the topology in
[production-deployment.md](production-deployment.md). The design is in
[production-readiness.md](../architecture/production-readiness.md).

## 1. Observability contract

### Health

| Probe | Meaning | Use |
| --- | --- | --- |
| `GET /health/live` | the process runs; it checks nothing else | restart only when this fails |
| `GET /health/ready` | the database answers, with **every migration of this build applied**, and Redis answers PING; each check has a 3 s timeout | route traffic, gate a rolling release, alert |

Both are anonymous and private. The edge returns 404 for them publicly, so probe
the instances directly (the compose healthcheck does). **Never restart an
instance because readiness failed.** Readiness is about dependencies, and a
restart does not fix them.

### Logs

Console only, one JSON-free line per event, scoped with the trace, span and
parent ids. Collect stdout and stderr from every container.

The logging contract, enforced by design and checked by the Phase 35 canary
scan:
- **Never logged:** passwords, the master key, secrets, provider keys, cookies,
  antiforgery tokens, realtime tickets (the edge's access log drops query
  strings), message or knowledge content, email addresses, and SMTP transcripts.
- **Identifiers only:** workspace, conversation, event and attempt ids.
- **Exception types, not messages**, wherever a message could carry provider
  text. Examples are the billing hand-back and the SMTP connect failure.
- `Microsoft.EntityFrameworkCore` stays at `Warning`, so no SQL statements are
  logged.

### Metrics

The platform emits counters and histograms on the .NET `Meter` named `OmniAI`:

| Instrument | What it counts or measures |
| --- | --- |
| `omniai.worker.items` | worker items, by worker and outcome |
| `omniai.reconciliation.settled` | reconciled items, by kind: `provider_usage`, `knowledge_attempt`, `conversation_turn`, `report_orphan`, `report_missing_file` |
| `omniai.report.generation.duration` | report generation time |
| `omniai.provider.operations` | provider operations, by outcome |
| `omniai.notification.emails` | emails, by outcome |
| `omniai.knowledge.index.attempts` | indexing attempts, by outcome |

**Classification:**
- Metrics *instrumentation* is ready: the platform emits the instruments above.
- An external metrics exporter and monitoring backend is a **production
  deployment requirement (important)**, and it is not part of this release.
- No alert described in §2 is automated by the product. Each one is a check an
  operator or their monitoring system must run.

**V1.0 wires no exporter.** Read them with `dotnet-counters monitor --counters
OmniAI` in the container, or add an OpenTelemetry/OTLP exporter as a follow-up.
Until then, alert from health, logs and the SQL in §2, which reads the same
durable state the metrics describe.

## 2. Alerts

| Alert | Signal | Suggested threshold | First action |
| --- | --- | --- | --- |
| Instance unready | edge log `omniai-readiness: … NOT READY` (drained automatically) | any, if it lasts > 5 min | [§3.1](#instance-unready) |
| All instances unready | the same, on every instance | 30 s, **page** | database or Redis outage ([§3.2](#database-outage), [§3.3](#redis-outage)) |
| Liveness failing | `/health/live` ≠ 200 | 3 probes | restart that instance |
| Connections near budget | `SELECT count(*) FROM pg_stat_activity WHERE usename='omniai_app'` | > 80 % of `Database:Budget:Connections` | look for load or a stuck statement |
| Connection exhaustion | log line with `53300`, or a 503 naming it | any | budget misconfigured; [§3.4](#connection-budget) |
| 5xx rate at the edge | status ≥ 500 in the edge access log | > 1 % over 5 min | see which upstream (`$upstream_addr`) |
| Email relay unreachable | log "Operational email could not reach the SMTP relay" | any, sustained 10 min | [§3.5](#email) |
| Email outcome unknown | `SELECT count(*) FROM notification_emails WHERE status='Unknown' AND created_at > now()-interval '1 hour'` | > 0 | never resend blindly; [§3.5](#email) |
| Email failed for good | `status='Failed'` in the last hour | > 0 | read `failure_code` |
| WhatsApp delivery unknown | in-app `WhatsAppDeliveryUnknown` occurrences | > 0 | check Meta status; nothing is resent automatically |
| Reconciler settling work | log "Reconciled {Count} stale {Kind} records" | > 0 | a process died mid-work; confirm it was expected (a crash or deploy) |
| Billing ownership lost | log "was claimed by another worker" | repeated | a worker exceeded its 15-minute lease; look for provider latency |
| Report queue age | `SELECT min(requested_at) FROM report_executions WHERE status='Pending'` | older than 1 min | workers stuck or all instances down |
| Knowledge indexing failed | `KnowledgeIndexingFailed` occurrences | > 0 | provider or quota; the workspace was told |
| TLS certificate expiry | edge certificate `notAfter` | < 21 days | renew ([§4](#secret-inventory-and-rotation)) |

## 3. Incidents

### 3.1 Instance unready while the others are ready {#instance-unready}

**Handled automatically.** The edge's readiness watcher drains an instance
after 2 failed readiness probes (about 8 s) and re-adds it after 2 good ones
(about 4 s after it recovers). The edge log records each transition:
`omniai-readiness: … NOT READY` / `routing to: … (drained: …)`.

1. Investigate the drained instance: its logs, its network path to the
   database and Redis, and its pools.
2. If you need it out of routing while you work, whatever its readiness:
   `docker compose -f deploy/compose.production.yml --env-file deploy/.env exec edge touch /tmp/omniai/drain/172.30.35.21:8080`.
   `rm` that file afterwards.
3. Restarting it is optional: `up -d --no-deps api-a`, then wait for healthy.
   It is routed again automatically.

If the watcher itself is not running (no `omniai-readiness` lines at all),
routing falls back to passive failover. Recreate the edge container.

### 3.2 Database outage {#database-outage}

Every instance is unready. Requests fail within about 5 s on connect, and about
20 s when a query was already in flight, with a truthful 503. The Phase 35 drill
paused the database for 25 s: first 200 came 0.3 s after resume, with no restart.
Do not restart the APIs. Restore the database. If it is lost, use
[backup-restore.md](backup-restore.md).

### 3.3 Redis outage {#redis-outage}

Public chat writes fail closed (503), and realtime tickets and cross-instance
events stop. The Admin keeps working. Restart Redis; it holds nothing durable.

### 3.4 Connection budget {#connection-budget}

The APIs refuse to start when `MaxInstances × (App + Lock + 2)` exceeds the
budget. Fix the numbers; never raise the pools past the budget. The runtime
role's `CONNECTION LIMIT` equals the budget; change both together:

```sql
ALTER ROLE omniai_app CONNECTION LIMIT 90;
```

### 3.5 Email {#email}

- `connect_failed` means nothing was sent, and the email is retried up to its
  attempt limit. Check the relay host and port, credentials, and **whether the
  relay's certificate can be revocation-checked** (see production-deployment
  §5).
- `Unknown` means the relay may have accepted it. It is never resent
  automatically; tell the recipient if it matters.

### 3.6 Stuck work after a crash

Nothing to do by hand:
- conversation turns → `Failed` after 15 min;
- provider operations → `Unknown` after 15 min;
- indexing attempts → settled after 30 min, with one alert;
- report, email, billing and WhatsApp claims → reclaimed after their leases.

A document whose indexing attempt was interrupted cannot be re-indexed until
its attempt settles (30 min).

## 4. Secret inventory and rotation {#secret-inventory-and-rotation}

| Secret | Where | Rotate by | Impact |
| --- | --- | --- | --- |
| `OMNIAI_SECRET_MASTER_KEY` | `.env` / secret store; **backed up separately** | only when compromised; no dual-key rotation in V1.0 | a new key cannot read the old key ring or stored channel credentials. Every session is logged out, and every WhatsApp connection must be reconnected. Plan it as a maintenance window. |
| `PUBLIC_CHAT_NETWORK_KEY_SECRET` | `.env` | replace, then rolling restart | rate-limit identities reset (harmless) |
| `omniai_app` password | `.env` + database | `ALTER ROLE omniai_app PASSWORD '…'` as the superuser, update `.env`, rolling restart; open connections keep working until recycled | none if done in that order |
| `omniai_owner` password | `.env` + database | `ALTER ROLE`, then update `.env` | the next migrator run only |
| `postgres` superuser password | `.env` + database | `ALTER ROLE postgres PASSWORD '…'` | operators, backups |
| Redis password | `.env` | update `.env`, recreate `redis`, then roll the APIs | public chat 503 for seconds; counters reset |
| TLS certificate and key | `OMNIAI_TLS_DIR` | replace files, then `docker compose … exec edge nginx -s reload` | none |
| `OPENAI_API_KEY`, Stripe, WhatsApp and SMTP credentials | `deploy/providers.env` | edit, then rolling restart | none; Stripe's webhook secret must match the dashboard |

Rules:
- Never paste a secret into a terminal history, ticket, chat or document.
- Generate secrets on the host straight into the file.
- The development master key exposed during Phase 34 was local only, was never
  used anywhere else, and has been replaced. No deployment may ever use a key
  that has appeared in any output.

## 5. Routine

| Frequency | Task |
| --- | --- |
| Before each release | backup; read the release's migration SQL; confirm it is expand-only |
| Daily | backup (database, plus reports on a best-effort basis) |
| Monthly | restore test ([backup-restore.md §5](backup-restore.md)); dependency updates (`dotnet list package --vulnerable`, `npm audit`) |
| Quarterly | rotate database and Redis passwords; review alert thresholds against real traffic |
