# Production readiness

What a production deployment of OmniAI relies on, and why each rule is there.
It covers architecture and invariants only. The procedures are in
[production-deployment.md](../operations/production-deployment.md),
[backup-restore.md](../operations/backup-restore.md) and the
[operations runbook](../operations/operations-runbook.md). The evidence is in the
[Phase 35 report](../phase-reports/PHASE_35_PRODUCTION_READINESS_FINAL_RELEASE_REPORT.md).

## 1. Shape

A single origin, several identical API instances, one PostgreSQL, one Redis.

- **One origin.** The edge serves the SPA, `/api`, `/hubs` and `/widget` under
  one host.
  - Cookies are same-origin (`SameSite=Lax`, `Secure`), so the API has no CORS
    at all.
  - Customer sites embed the chat through an iframe document from this origin.
  - The widget loader builds URLs from `Host`, so the edge preserves it.
- **Instances are interchangeable.** The only per-instance state is in-flight
  work.
  - Every durable effect is in PostgreSQL.
  - Every cross-instance coordination is in PostgreSQL: advisory locks, claims
    with leases and tokens, unique indexes.
  - Every shared volatile counter is in Redis: rate limits, tickets and the
    SignalR backplane.
  - Report artifacts are on a volume both instances mount.
- **Workers run in every instance.** Reports, notifications, email, billing,
  WhatsApp and reconciliation all do. Each one claims work with
  `FOR UPDATE SKIP LOCKED` and settles it with a compare-and-set on its own
  claim, so N instances never do one item twice. An instance that dies leaves
  its items to a lease or a reconciler.

## 2. Database

### 2.1 Connection budget

There are three bounded Npgsql pools per instance: application 25,
conversation lock 15, readiness 2. Startup refuses
`MaxInstances × (App + Lock + 2) > Database:Budget:Connections` outside
Development, and the runtime role's `CONNECTION LIMIT` enforces the same number.
Phase 34 showed what happens without it: the default of 100 per pool, times two
pools, exhausted `max_connections = 100` with a single instance.

### 2.2 Timeouts

| Class | Value | Why |
| --- | --- | --- |
| Connect | 5 s | a dead database is reported in seconds, not the 15 s default |
| Command | 15 s | nothing interactive should take longer; a stuck statement frees its connection |
| Cancellation | 2 s | a cancelled command cannot hold the request for the old 30 s |
| Long command | 120 s | report generation only, scoped to its unit of work |
| Lock wait | 60 s | the conversation advisory lock; it was unbounded (`CommandTimeout = 0`) before |

### 2.3 Roles

- **`omniai_owner`** owns the schema and is used only by the migrator.
- **`omniai_app`** is DML only. It cannot create, alter, drop or truncate
  anything. The runtime never needs DDL; its only locks are advisory locks.
- Default privileges grant each new table to `omniai_app`, so migrations stay
  purely model-generated.
- The exception is EF's `__EFMigrationsHistory`. After every migration run,
  the `migrator-privileges` step leaves `omniai_app` with `SELECT` only there:
  readiness reads it, and nothing at runtime may write it.

### 2.4 Schema authority and migrations

The EF model is the source; migrations are generated output, never edited
([database-model-authority.md](database-model-authority.md)). The history is
the Phase 33 `Initial` baseline plus one generated Phase 35 migration.

- Migrations run **once per release, before the APIs, as the owner**. They run
  from a bundle built from the same model, never from an API instance and never
  over HTTP.
- Readiness requires every migration the build knows to be applied. A newer
  schema is accepted, so rolling deploys and rollbacks work within expand-only
  releases.

## 3. Correctness under multiple instances and crashes

| Work item | Claim / ownership | Crash before the external call | Crash after it |
| --- | --- | --- | --- |
| Report execution | claim token + lease | reclaimed after the lease | token prevents the stale owner from settling |
| Notification dispatch | claim + lease | reclaimed | recipient-level uniqueness |
| Operational email | Pending claim with token → `Sending` *immediately* before SMTP (Phase 35) | still `Pending`, reclaimed and sent once | `Sending` past its lease → `Unknown`, **never resent** |
| Billing event | claim token + `claimed_at` lease (Phase 35) | reclaimed after the lease | a stale owner is fenced from both the subscription change (row lock + token check in its transaction) and the settlement (token as the concurrency token) |
| AI provider operation | `Started` row before dispatch | reconciler → `Unknown` (nothing invented) | the same |
| Knowledge indexing | one `Started` attempt per document (Phase 35 single flight) | reconciler → `Unknown`/interrupted with one alert | the same; if the index was written, `Succeeded` |
| Conversation turn | turn row + advisory lock per conversation | reconciler → `Failed` (Phase 35) | reply and completion are one transaction |
| WhatsApp outbound | claim + timeout | reclaimed | `Unknown`, never resent |

None of these retries paid or ambiguous work automatically, and none depends on
process memory.

## 4. Security boundary

- **Transport.** TLS ends at the edge. The API trusts `X-Forwarded-Proto` and
  `X-Forwarded-For` only from the edge's fixed address, and refuses anything
  else as insecure transport outside Development.
  - `Secure` cookies, HSTS for one year, and HTTP 308 to HTTPS.
- **Headers.**
  - The API sets its own CSP / `X-Frame-Options: DENY` / `nosniff` /
    `no-referrer` / `Permissions-Policy` on non-public paths.
  - The edge sets the SPA's CSP, with script hashes computed from the built
    `index.html`.
  - Everything is `frame-ancestors 'none'` except the `/embed/` document.
- **Embedding.** Framing is allowed for the embed document. Whether a site may
  use a widget is enforced per endpoint by the API: exact origins, re-checked on
  every embed request and realtime ticket.
- **No debug surface in Production.** OpenAPI is mapped in Development only.
  There is no developer exception page and no test endpoint; `/health*` and
  `/openapi*` return 404 at the edge.
- **Fail fast on missing essentials.** Outside Development, startup refuses to
  run without the database, Redis, the master key, the network key secret, a
  default plan and report storage. Previously the missing master key silently
  meant an **unencrypted** Data Protection key ring. Messages name keys, never
  values.
- **Secrets.** They come only from the environment or a secret store; tests
  assert that no committed settings file holds one. The master key must be
  backed up apart from database backups.
- **Containers.**
  - Non-root; read-only root filesystem for the API, Redis and the migrator.
  - `cap_drop: ALL`, `no-new-privileges`, and memory, CPU and PID limits.
  - The data network is internal, with no route out.

## 5. Dependencies and their failure modes

| Dependency | If it is unavailable | Readiness |
| --- | --- | --- |
| PostgreSQL | requests fail in seconds with 503 (Phase 34 §5.2); workers back off | unhealthy |
| Redis | public writes fail closed (503); Admin keeps working; realtime degrades | unhealthy |
| Report volume | reports settle `Failed`, with no path leaked | unaffected |
| OpenAI / embeddings | turns and indexing fail truthfully; nothing is retried | unaffected |
| SMTP | email `Unknown` or retried per the outbox rules | unaffected |
| Stripe | events stay queued and are retried within `MaxAttempts` | unaffected |
| Meta | outbound `Unknown`, never resent | unaffected |

Liveness (`/health/live`) never depends on anything, so an orchestrator must
not restart instances because readiness failed.

## 6. Known limits (accepted for V1.0)

- Single host. Scaling out to more hosts needs shared report storage and
  network-level health routing.
- Readiness-aware routing comes from a small watcher inside the edge that
  re-renders nginx upstreams from `/health/ready`. It is not a load balancer
  with native active checks. Requests already in flight to an instance as it
  becomes unready can still fail during the ~8 s detection window.
- A document whose indexing attempt was left `Started` by a crash cannot be
  indexed again until the reconciler settles it (30 minutes).
- Meta WhatsApp live validation (Phases 26B–29) is deferred. See the report's
  provider matrix.
