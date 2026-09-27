# Production deployment

How OmniAI is installed, configured, released and rolled back. The assets are in
[`deploy/`](../../deploy/). The design rationale is in
[production-readiness.md](../architecture/production-readiness.md), and the
evidence is in the
[Phase 35 report](../phase-reports/PHASE_35_PRODUCTION_READINESS_FINAL_RELEASE_REPORT.md).

> Phase 35 validated this topology **locally only**. Nothing was deployed.
> Before the first real release, run the same drills on the target host.

## 1. Topology

```
            :443 / :80
               │
        ┌──────▼──────┐   fixed 172.30.35.10, the only trusted proxy
        │    edge     │   nginx: TLS, static Angular SPA, /api /hubs /widget proxy
        └──┬───────┬──┘
     ┌─────▼──┐ ┌──▼─────┐   fixed .21 / .22; stop grace 30 s; read-only rootfs
     │ api-a  │ │ api-b  │   ASP.NET Core, non-root, Production
     └──┬──┬──┘ └──┬──┬──┘
        │  └───────┼──┼──────────── reports volume (shared by both)
   ┌────▼──────────▼┐ ┌▼──────┐     data network: internal, no route out
   │ postgres 17 +  │ │ redis │
   │ pgvector       │ │       │
   └────────────────┘ └───────┘
   migrator: runs once per release, before the APIs, as the owner role
```

| Service | Image | State |
| --- | --- | --- |
| `edge` | `deploy/edge.Dockerfile` (nginx-unprivileged + built SPA) | stateless; TLS files mounted read-only |
| `api-a`, `api-b` | `deploy/api.Dockerfile` | stateless apart from the shared `reports` volume |
| `migrator` | `deploy/migrator.Dockerfile` (EF migrations bundle) | none |
| `postgres` | `pgvector/pgvector:pg17` | **`pgdata` volume: the system of record** |
| `redis` | `redis:7.4-alpine` | ephemeral by design (limits, tickets, backplane) |

The report store writes to a filesystem, so both APIs share one volume. That
works on one host. **Scaling out to more than one host needs a shared
filesystem, or a different `IReportArtifactStore`.**

## 2. Database roles

Created on first initialisation by `deploy/postgres/10-omniai-roles.sh`:

| Role | Used by | Rights |
| --- | --- | --- |
| `postgres` (superuser) | operators, backups, the init script | everything; never used by the application |
| `omniai_owner` | the migrator only | owns the database and the `public` schema: DDL |
| `omniai_app` | the API | `CONNECT`, schema `USAGE`, and `SELECT/INSERT/UPDATE/DELETE` on tables (plus sequences and functions) through default privileges; `CONNECTION LIMIT` = the budget; no DDL, no `TRUNCATE`, no role or database creation |

Default privileges grant every table a future migration creates to
`omniai_app` automatically. A migration therefore never needs a hand-written
`GRANT`, and the schema stays model-generated.

## 3. Configuration

Start from [`deploy/.env.example`](../../deploy/.env.example). In production,
inject the values from a secret store rather than keeping them in a file.

**Outside Development the API refuses to start unless it has:**

| Setting | Why |
| --- | --- |
| `ConnectionStrings__OmniAiDatabase` | the database, as `omniai_app` |
| `ConnectionStrings__OmniAiProtectionRedis` | public chat protection, realtime tickets, and the multi-instance SignalR backplane |
| `OMNIAI_SECRET_MASTER_KEY` | base64 of exactly 32 bytes; encrypts the Data Protection key ring and stored channel credentials |
| `PUBLIC_CHAT_NETWORK_KEY_SECRET` | at least 32 characters; keys the network identity that public chat limits count by |
| `Plans__DefaultPlanCode` | the plan a new workspace starts on; use a **finite** plan (`free`/`starter`), never `development` |
| `Reports__Storage__RootPath` | absolute path of the persistent report volume |
| `Database__Budget__Connections`, `Database__Budget__MaxInstances` | the connection budget (§4) |

`ForwardedHeaders__KnownProxies__0` must be the edge's address. Without it,
every request arriving through the proxy is refused as insecure transport.

The messages name the missing keys and never their values.

Optional integrations stay off until configured. While unset, each one refuses
truthfully:

| Integration | Credentials, in `deploy/providers.env` | Other settings, in `.env` |
| --- | --- | --- |
| OpenAI | `OPENAI_API_KEY` | — |
| Stripe | `STRIPE_SECRET_KEY`, `STRIPE_WEBHOOK_SECRET` | `OMNIAI_BILLING_FALLBACK_PLAN_CODE`, `OMNIAI_PUBLIC_BASE_URL` |
| WhatsApp | `OMNIAI_WHATSAPP_APP_SECRET`, `OMNIAI_WHATSAPP_VERIFY_TOKEN` | — |
| SMTP | `Email__Smtp__*` | `OMNIAI_PUBLIC_BASE_URL` (email links) |

**Provider credentials live in `deploy/providers.env`** (template:
[`providers.env.example`](../../deploy/providers.env.example)), which the API
containers read through `env_file`.

> **Why a separate file.** Compose resolves `${VAR}` from the *calling shell*
> before `--env-file`. When provider keys were interpolated, a key that merely
> existed in the deploying user's environment reached the containers. In
> Production the Development-only `OMNIAI_LIVE_*` guard does not apply, so that
> switched live provider traffic on. It happened in the Phase 35 local drill.
> `env_file` values are passed literally and cannot be overridden from the
> shell. Even so, deploy from a clean service account, never a developer shell.

`OMNIAI_LIVE_*` are Development-only switches and are ignored in production.

## 4. Connection budget

Each API instance opens three bounded pools:
- the EF application pool (`Database__Pools__ApplicationPoolSize`, default 25);
- the conversation-lock pool (`…ConversationLockPoolSize`, default 15);
- the readiness pool (2).

Startup refuses any configuration where:

```
MaxInstances × (App + Lock + 2)  >  Database:Budget:Connections
```

`Connections` is the runtime role's allowance:
`max_connections − superuser_reserved_connections − (migrator + backup + operator)`.
With the shipped values that is `100 − 3 − 7 = 90`, and `2 × (25 + 15 + 2) = 84`
fits. **Set `MaxInstances` to the most instances that will ever run at once,
including the overlap during a rolling deployment.** The role's own
`CONNECTION LIMIT` is set to the same number, so the database enforces the
budget too.

Timeouts are fixed by the policy rather than by the connection string:

| Timeout | Value |
| --- | --- |
| connect | 5 s |
| command | 15 s |
| cancellation | 2 s |
| report generation (long command) | 120 s |
| conversation-lock wait | 60 s |

## 5. TLS and the edge

Mount `tls.crt` (the full chain) and `tls.key` at `OMNIAI_TLS_DIR`. Plain HTTP
only redirects (308), and HSTS is one year.

The edge sets the SPA's CSP. Its script hashes are computed from the built
`index.html` at image build time, so the policy allows only this build's inline
code. `frame-ancestors 'none'` applies everywhere except `/embed/`, which customer
sites frame. **Which sites may use a widget is enforced by the API**, from each
endpoint's allowed origins.

The API sets its own headers on `/api`. The API has no CORS because none is
needed: everything is same-origin, and embeds go through the iframe.

`/health*` and `/openapi*` are not public (404 at the edge). Probe health from
the private network.

Two edge rules exist because the Phase 35 browser pass broke without them:

- **API responses are never compressed at the edge.** nginx weakens a strong
  `ETag` (`W/"…"`) when it gzips. The API refuses weak entity tags for
  `If-Match`, so every optimistic-concurrency write failed with 400. Static SPA
  files are still compressed.
- **`/hubs/` has client affinity**, a consistent hash of the client address.
  SignalR's negotiate and connect requests must reach the same instance, and
  round-robin produced "connection could not be found" 404s. Events still reach
  every client through the Redis backplane.

**SMTP relay certificates must be revocation-checkable.** The email client
verifies the relay's certificate *including revocation*. A relay whose CA
publishes no CRL or OCSP is refused: `SslHandshakeException`, and the email
stays `Pending` for retry. Public relays qualify. A private relay's CA must
publish a CRL the API can reach. Revocation is never disabled.

## 6. First installation

```sh
docker build -f deploy/api.Dockerfile      -t omniai-api:$V .
docker build -f deploy/migrator.Dockerfile -t omniai-migrator:$V .
docker build -f deploy/edge.Dockerfile     -t omniai-edge:$V .
# deploy/.env from .env.example, with generated secrets; TLS files in place
# deploy/providers.env from providers.env.example, only for integrations you enable
docker compose -f deploy/compose.production.yml --env-file deploy/.env up -d
```

Compose starts the services in order:
1. `postgres` initialises and creates the roles and the `vector` extension.
2. `migrator` applies every migration as `omniai_owner`.
3. `migrator-privileges` leaves `omniai_app` with **read-only** access to
   `__EFMigrationsHistory` (readiness reads it; nothing at runtime may write it).
4. The APIs start only after both have **succeeded**.
5. Each API reports healthy once `/health/ready` passes, which requires every
   migration of its build to be applied and Redis to answer.

Then create the first account through the normal sign-up flow.

## 7. Releasing a new version (rolling)

1. **Back up** (see [backup-restore.md](backup-restore.md)).
2. Build and tag the new images.
3. **Migrate first**, once:
   `docker compose … run --rm migrator` with the new migrator image.
   Migrations must be **expand-only** while the old version is still serving:
   add nullable columns, tables and indexes; never drop, rename or tighten in
   the same release. The Phase 35 migration qualifies.
   - An index build on a large table blocks writes to that table while it runs.
     Plan it for low traffic.
   - If the migrator fails, **stop**. Each migration runs in its own
     transaction, so a failed one leaves the schema at the previous migration.
     The running APIs are unaffected. Fix it and re-run.
4. **Replace `api-a`**: `docker compose … up -d --no-deps api-a` with the new
   image.
   - Compose stops the old container, which gets 30 s to finish in-flight
     requests.
   - The edge sends new requests to `api-b` in the meantime.
5. **Wait until `api-a` is healthy** (`docker compose ps`; its healthcheck probes
   `/health/ready`).
6. Replace `api-b` the same way, then confirm both are healthy.
7. The edge needs no change: the APIs keep their addresses.

`deploy/scripts/rolling-deploy.sh` performs steps 3–6 and stops at the first
failure.

### Readiness-aware routing

Open-source nginx has no active health checks, so the edge image runs a small
**readiness watcher** (`deploy/edge/readiness-watcher.sh`) beside nginx:

- It probes each instance's `/health/ready` every second on the private
  network, with a 4 s timeout. `/health/live` is never used for routing.
- After **2 consecutive failures** the instance is marked `down` in the
  rendered upstreams: it gets no new requests, and in-flight ones finish.
- After **2 consecutive successes** it is routed to again.
- Each change is validated with `nginx -t` and applied with a graceful reload.
  Old workers close lingering connections, such as hub sockets, within 30 s, so
  clients reconnect through the new routing.
- **If every instance is unready, all stay routed.** That is a dependency
  outage, and the APIs' own truthful 503s beat a blanket 502.
- An operator can drain an instance on purpose, whatever its readiness:
  `docker compose … exec edge touch /tmp/omniai/drain/172.30.35.21:8080`.
  Remove the file to return it to readiness-based routing.

Underneath, passive failover still applies. An instance that refuses a
connection or times out is skipped for 10 s, and idempotent requests, or ones
never sent, move to the other instance. A request already sent with a
non-idempotent method is **never replayed**. The client sees the error and can
retry, and the product's idempotency keys make that retry safe.

The rollout script drains each instance at the edge **before** stopping it, and
routes to it again only after its readiness passes. Phase 35 measured **zero
failed requests** across a full rolling release under load: 25,577 reads and
2,384 chat requests.

**An instance that loses its database or Redis while the other stays ready** is
drained automatically. In the Phase 35 drill it was marked down 8 s after its
database connection was cut, and re-added 4 s after it came back. The failures
were 22 of 9,800 requests, all but one started before the drain: requests
already on their way to the partitioned instance, which fail truthfully (503)
in up to 15 s. While it was drained, 1 of 4,886 requests failed. No manual
action is needed. The manual drain above is the operator fallback.

## 8. Rollback

Rollback is safe when the release's migrations were expand-only. Redeploy the
previous API image with the same rolling procedure. The older build ignores
columns and indexes it does not know about. Readiness accepts a schema that is
ahead of the build, but never one that is behind.

**Never** roll back the schema with `migrations remove` or `database update` to
an earlier migration on a live database. If a migration itself must be undone,
restore the pre-release backup
([backup-restore.md](backup-restore.md) §4). That means accepting the loss of
writes made since the backup, so it is a decision for an owner, not a routine
step.

For the Phase 35 release: its migration adds two nullable columns and two
partial indexes, so rolling back to `bfa20d2` needs no schema change. While both
versions run, an old instance's billing claims carry no lease timestamp, so new
instances treat them as stale. During that overlap, billing events can be
processed twice, exactly as before Phase 35. This is harmless because the apply
is idempotent.

## 9. Resources (starting point, not an SLA)

| Service | Local drill limits | Scale on |
| --- | --- | --- |
| api (each) | 2 CPU, 2 GiB | CPU, then the connection budget |
| postgres | 2 CPU, 2 GiB, `shared_buffers` 256 MB | CPU (admin analytics and reads) |
| redis | 1 CPU, 384 MiB, `maxmemory` 256 MB, `noeviction` | memory |
| edge | 1 CPU, 256 MiB | connections |

The Phase 34 local envelopes are
[there](../phase-reports/PHASE_34_PERFORMANCE_LOAD_FAILURE_RECOVERY_REPORT.md#9-local-capacity-envelopes-not-a-production-sla).

## 10. Operations

Monitoring, alerts, logs, incidents and secret rotation are covered in the
[operations runbook](operations-runbook.md).
