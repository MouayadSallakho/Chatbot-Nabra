# Phase 35 — Production readiness and final release hardening

> Status: **ACCEPTED — OmniAI V1 Core Engineering Release Candidate.** This is
> not a production deployment; the deployment and external-validation gates are
> in §17. Written as each item was verified, across several sessions (§0). One
> incident is disclosed in §16.

Baseline: `bfa20d2` (Phase 34, accepted and checkpointed). Working tree left
unstaged and uncommitted. Nothing was deployed; no VPS was accessed; no Git
remote was configured; `OMNIAI_LIVE_*` never set; no live Meta, Stripe or external SMTP traffic.
**One unintended live OpenAI episode occurred and is disclosed in §16.** Phases
26B–29 remain deferred.

## 0. Session log

| Session | What happened |
| --- | --- |
| 1 | Connection budget, timeout policy, readiness schema gate, email pre-dispatch boundary, billing lease (partial). Context compacted mid-billing. |
| 2 | Recovered from the worktree (HEAD `bfa20d2`, diff intact). Billing lease completed; a real stale-owner defect found and fixed (§4.3). First Phase 35 migration generated. VS Code closed. |
| 3 | Recovered again from the worktree; Docker Desktop had stopped and was restarted; the Phase 34 test PostgreSQL/Redis restarted. Billing gate re-run green. Turn reconciler, Knowledge single flight, customer directory. Migration regenerated normally to include them (§5). Production configuration validator, deployment assets, production-like stack and drills (§9–§15). **Live-provider incident found and contained (§16).** |
| 4 | Resumed after an API connectivity error, from the worktree. Fixed a half-applied edit (compile error), finished the SMTP revocation proof, documentation, final gates. Docker Desktop had stopped again and was restarted; the drill stack came back from its restart policy. |

## 1. Phase 34 handoff — disposition

| # | Phase 34 item | Phase 35 disposition | Section |
| --- | --- | --- | --- |
| 1 | BLOCKER connection budget | explicit per-pool limits + aggregate startup validation | §2 |
| 2 | Timeouts | explicit connect / command / cancellation / long-command / lock-wait policy | §2 |
| 10 | Email `Sending` before dispatch | per-row pre-dispatch boundary | §3 |
| 11 | Billing claim lease | durable lease + claim token, fenced apply and settle | §4 |
| 12 | Turns left `Generating` | provider-free reconciler | §6 |
| 13 | Customers list linear in workspace | bounded query, no schema change | §8 |
| 14 | Duplicate paid Knowledge indexing | database single flight | §7 |

(Operations items 3–9 are covered by the production-readiness documents; see
§§ to be completed below.)

## 2. PostgreSQL connection budget and timeouts

`backend/src/OmniAI.Infrastructure/Persistence/DatabaseConnectionPolicy.cs`
(new). Three pools per instance, each explicit:

| Pool | Key | Default |
| --- | --- | --- |
| EF application | `Database:Pools:ApplicationPoolSize` | 25 |
| Conversation advisory lock | `Database:Pools:ConversationLockPoolSize` | 15 |
| Readiness probe | fixed | 2 |

Startup validation (outside Development, required):
`Database:Budget:MaxInstances × (App + Lock + 2) ≤ Database:Budget:Connections`,
where `Connections` is the operator's allowance
(`max_connections − superuser_reserved − ops tooling`). Errors name keys, never
values. Every pool string is rewritten with `MaxPoolSize`, `MinPoolSize=0` and
the timeout policy, so a connection string cannot silently reintroduce the
100-per-pool default.

Timeouts: connect 5 s, command 15 s, cancellation 2 s, long command (report
execution) 120 s, conversation lock wait 60 s (the lock command previously had
`CommandTimeout = 0`, i.e. unbounded; now bounded by the lock data source).

Readiness additionally requires every migration known to this build to be
applied (a schema ahead of the build is allowed, for rolling deployments).

Verification: §11.1 (two instances at `max_connections=100` under saturating
load: peak 82 runtime connections, zero `53300`, zero failed requests) and §11.2
(database outage bounded to ~20 s, truthful 503, recovery in 0.3 s).

## 3. Operational email: pre-dispatch boundary

Claim keeps rows `Pending` with a claim token; `BeginDispatchAsync` moves one
row to `Sending` immediately before the SMTP call, only if still `Pending` with
the same token. A crash before dispatch leaves a reclaimable `Pending` row; a
stale `Sending` row becomes `Unknown` and is never resent. No schema change.

Verification: `OperationalEmailTests` + `NotificationHarness`, 52/52 green,
three consecutive runs (session 1), including crash boundaries A (claimed, not
begun → reclaimed and sent once), B (begun, send outcome unknown → `Unknown`,
never resent) and C (worker crash between claim and dispatch → no loss).

## 4. Billing events: claim ownership and lease

### 4.1 Design

- `billing_events.claim_token uuid NULL` (EF concurrency token) and
  `claimed_at timestamptz NULL`, both model-generated (§5).
- Claim: one bounded `MATERIALIZED` CTE with `FOR UPDATE SKIP LOCKED`; claimable
  when `Pending`, or `Claimed` with `claimed_at` older than the lease (or NULL —
  rows claimed before this migration). Each claim writes a new
  `gen_random_uuid()` token and `attempts + 1`.
- `Billing:Workers:ClaimLease`, default 15 min, validated at startup:
  minimum = 2 provider round trips at their worst (Stripe's 80 s request timeout
  × 3 tries) + 2 min for database work = **10 min**; maximum 6 h. Worker polling
  (5 s) and the 30 s stop grace are far inside it.
- The worker carries the token granted by its claim (read from the rows the
  claim wrote) into each event's own unit of work and refuses to act if the
  reloaded row's token differs.
- **Apply fence.** The subscription change runs in its own transaction under the
  workspace advisory lock; inside it, `BillingEventOwnership.FenceAsync` runs
  `SELECT … WHERE id = … AND status = 'Claimed' AND claim_token = … FOR UPDATE`.
  No row → `BillingEventOwnershipLostException`, rollback, nothing written. The
  row lock is held to commit, so a reclaim that arrives mid-write skips the row.
  The provider call stays outside any transaction (unchanged).
- **Settle fence.** The token is the row's concurrency token: a stale owner's
  save matches no row → `BillingEventOwnershipLostException`.
- Transient failure: the event is handed back to `Pending` (token-guarded,
  `BillingEvent.Release`, which does not count a second attempt) so it retries
  on the next pass instead of waiting out the lease; `MaxAttempts` still bounds
  it. If the hand-back fails, the lease recovers it.
- Unchanged: provider-event-id uniqueness, snapshot-driven idempotent apply,
  unknown Stripe price grants nothing.

### 4.2 Proof (real PostgreSQL, synthetic time, no Stripe)

`backend/tests/OmniAI.Infrastructure.IntegrationTests/Billing/BillingEventLeaseTests.cs`:

| Gate | Test |
| --- | --- |
| A, B — A claims; B cannot claim 1 min and 1 s before expiry | `A_second_worker_cannot_take_an_event_whose_lease_is_still_running` |
| C, D, F — after expiry B reclaims with a new token; A's apply is refused inside the transaction (subscription still Active, 0 past-due occurrences); B applies once (PastDue, exactly 1 occurrence); event Processed, token cleared | `A_worker_whose_lease_lapsed_changes_nothing_and_the_new_owner_applies_once` |
| D, E — stale settle refused while B owns it; refused again after B settled (the detached re-save of §4.3); B's result intact | `A_stale_owner_cannot_settle_or_overwrite_the_new_owners_result` |
| fence holds the row: reclaim past the lease skips it while A's fenced write is open; claimable after commit | `A_reclaim_cannot_land_while_the_owner_is_inside_its_fenced_write` |
| hand-back retries without waiting out the lease; attempts counted once per claim | `An_event_handed_back_after_a_transient_failure_is_retried_without_waiting_out_the_lease` |
| G — redelivery not stored twice; a follow-up event moves nothing, notifies nobody again | `A_replayed_provider_event_stays_idempotent` |
| lease below the minimum refused, naming the key | `The_lease_cannot_be_configured_shorter_than_the_slowest_legitimate_processing` |
| H — unknown price grants nothing | existing `BillingSubscriptionServiceTests.A_price_this_platform_does_not_sell_grants_nothing` |

Targeted gate (session 3, after recovery), Release build, filter
`Billing|DatabaseModelAuthority`: Domain 31/31, Application 6/6,
Infrastructure 83/83, API 37/37. Also green three consecutive times in session 2.

### 4.3 Defect found by the tests: stale re-attach bypassed the settle fence

- **Reproduction.** `A_stale_owner_cannot_settle_or_overwrite_the_new_owners_result`:
  A's first stale save was refused; the store then detached the entity. A second
  save of the same stale instance **succeeded** and overwrote B's `Processed`
  result.
- **Root cause.** `BillingEventInbox.SaveAsync` attached a detached event and
  marked it Modified. Re-attached, its *original* token became whatever it held
  now — `null` after the failed settle — so the concurrency predicate became
  `claim_token IS NULL`, which matches a row another worker has already settled.
- **Fix.** A detached event carries no claim proof, so `SaveAsync` refuses it
  (`BillingEventOwnershipLostException`: "A billing event can only be settled by
  the unit of work that loaded it under its claim."). Every production caller
  already saves a tracked entity; nothing legitimate used the attach path.
- **Regression.** The same test's second refusal; the gate above.

## 5. Schema: one Phase 35 migration, model-generated

`20260926215858_ProductionReadinessHardening` (generated with
`dotnet ef migrations add`, never edited; no `migrationBuilder.Sql`):

- `billing_events.claim_token`, `billing_events.claimed_at` (nullable);
- `ux_knowledge_index_attempts_in_flight` unique `(document_id, tenant_id)`
  `WHERE status = 'Started'`;
- `ix_conversation_turns_generating (created_at) WHERE status = 'Generating'`.

Model defect caught by reading the generated output: the first regeneration
dropped `ix_knowledge_index_attempts_document`, because an unnamed second
`HasIndex` on the same columns reconfigures the first. The generated migration
was not touched; the migration was removed, both indexes were named in the model
(`HasIndex(…, name)`), and it was generated again. The full index is needed: the
partial one cannot serve the composite foreign key's cascade.

Migration count: 2 (the Initial baseline + this one).

## 6. Conversation turns left `Generating`

Audit: a turn is `Generating` from the moment its customer message is stored
(inside the conversation lock) until the reply and the completion commit in one
transaction. It stays `Generating` for ever if the process dies, **or if the
request is cancelled** (client disconnect / shutdown): the runner settles only
non-cancellation failures. A `Generating` turn therefore never has a reply.

`ConversationTurnReconciler` (in the existing stale-work worker, isolated like
the other two): bounded `MATERIALIZED` + `FOR UPDATE SKIP LOCKED`, repeats
`status = 'Generating'`, sets `Failed` (or `Completed` if a reply is somehow
named), `updated_at = GREATEST(updated_at, now)`. No provider call, nothing
regenerated; a same-id resend is then answered "already attempted" instead of
"in progress" for ever. Provider cost truth stays with the ledger reconciler
(`Unknown`). `Reliability:ConversationTurnStaleAfter` default 15 min, validated
≥ 2 × provider timeout + 5 min (one retrieval embedding + one generation).

Tests: `ConversationTurnReconciliationTests` (stale → Failed, customer message
kept, no AI message; young left alone; settled never move and a second pass
changes nothing; one batch per pass; two instances at once settle each turn
exactly once; threshold validation). All green (§18).

## 7. Knowledge: single-flight paid indexing

Partial unique index (§5) + `KnowledgeIndexAttemptStore.StartAsync` maps its
violation to `KnowledgeIndexingInProgressException` → **409** "Indexing already
in progress", before any embedding request. The handler re-reads the document
after its attempt is admitted: an attempt that finished meanwhile has already
written its index (it settles only after writing), so the stale version is
refused as `document_changed` with nothing sent. An attempt a crash left
`Started` holds its document until the stale-attempt reconciler settles it
(30 min default).

Tests: `KnowledgeIndexSingleFlightTests` (second attempt refused; 8 racing, 1
admitted; different documents concurrent; settled attempt releases the
document); Phase 34 `Indexing_one_document_several_times_at_once…` now asserts
exactly one `200`, the rest `409`/`412`, one `Succeeded`, and **provider inputs
= one version's chunks**. `One_knowledge_pass_settles_at_most_one_batch` now
uses four documents (four `Started` attempts on one document is no longer a
possible state). All green (§18).

## 8. Customer directory scaling

Root cause (Phase 34 §4.1): ordered by `MAX(conversations.last_activity_at)` per
customer, so every page aggregated every customer of the workspace.

Fix without schema or write-path change: walking conversations newest first on
the existing `ix_conversations_tenant_last_activity`, each customer first
appears at their latest activity; a widening prefix finds the `skip + take`-th
customer's activity, and every conversation at or after it is aggregated
exactly (ties included). Order is identical to the old sort (nulls last, id
tie-break compared as PostgreSQL orders `uuid`); page aggregates are computed
for the page's ids only. A stored last-activity column was rejected: an extra
update per message and xmin conflicts with admin renames of active customers.

Tests: `CustomerDirectoryOrderTests` (every page equals the Phase 34 query for
page sizes 1/7/20/100 over ties, multi-conversation, history-less and merged
customers; whole-history counts), green. Scale (`CustomerDirectoryScaleTests`,
`OMNIAI_PERF_SCALE=1`; one workspace plus a 20k-customer neighbour; two
conversations per customer; `VACUUM ANALYZE` between sizes; median of 7):

| Customers | new page 1 | new page 10 | old page 1 | old page 10 |
| --- | --- | --- | --- | --- |
| first attempt (with a canonical-customer semi-join in the walk) | 45 → 136 → 173 ms | 120 → 163 ms | 158 → 490 → 884 ms | 167 → 1,207 ms |
| final, 10k (idle host) | 79 ms | 82 ms | 145 ms | 83 ms |
| final, 50k | 54 ms | 76 ms | 723 ms | 774 ms |
| final, 100k | 28 ms | 36 ms | 753 ms | 626 ms |
| (same test during the parallel frontend build, 10k → 100k) | 117 → 141 → 122 ms | 83 → 165 ms | 165 → 466 → 528 ms | 197 → 520 ms |

The first attempt still grew with the workspace: a "just in case" semi-join to
canonical customers let the planner hash every customer before taking the
window. It was removed (a merge moves every conversation to the survivor in the
same transaction, so a conversation always names a canonical customer; rows are
still read from canonical customers only). The same pages are asserted equal to
the old query at every size. No index was added.

## 9. Production configuration fail-fast

`backend/src/OmniAI.Api/Composition/ProductionConfiguration.cs` (new), called
first in `AddOmniAi`. **Outside Development, startup refuses to run** unless it
has:

- `ConnectionStrings:OmniAiDatabase` and `ConnectionStrings:OmniAiProtectionRedis`;
- `OMNIAI_SECRET_MASTER_KEY` and `PUBLIC_CHAT_NETWORK_KEY_SECRET`;
- `Plans:DefaultPlanCode` and `Reports:Storage:RootPath`.

All missing keys are listed in one message, which names keys and never values.
It adds to the existing format checks and the connection-budget check.

**Defect closed:** before this, a Production deployment without the master key
started, passed readiness, and wrote the Data Protection key ring to PostgreSQL
**unencrypted**. Missing Redis or the network key only surfaced at the first
public chat, and a missing default plan at the first workspace creation.

Tests: `ProductionConfigurationTests` covers:
- a complete configuration is accepted in Production and Staging;
- each key missing, empty or blank is refused and named, and no present value
  leaks into the message;
- every missing key is reported at once;
- Development keeps its local fallbacks.

`ProductionApiFixture` now supplies a random per-fixture master key (never a
committed one) and a private temporary report directory, exactly as a
deployment would.

Optional integrations (OpenAI, Stripe, Meta, SMTP) are deliberately not
required. Without them each one refuses truthfully (proven in §16).

## 10. Deployment assets (`deploy/`, new)

| Asset | What it does |
| --- | --- |
| `api.Dockerfile` | SDK publish → `aspnet:10.0`; non-root (uid 1654); `Production`; port 8080; NuGet cache mount |
| `migrator.Dockerfile` | EF migrations bundle compiled from the same model → `runtime-deps`; non-root; connection from `OMNIAI_DESIGN_CONNECTION` (never argv); unpacks into the `/tmp` tmpfs |
| `edge.Dockerfile` + `edge/` | Angular production build → `nginx-unprivileged` (uid 101). The CSP script hashes are computed from the built `index.html` (`csp-hashes.mjs`); server blocks are an envsubst template; proxy settings are included per location (`omniai-proxy.conf`) |
| `compose.production.yml` | edge → api-a/api-b → postgres (pg17 + pgvector) and redis. The migrator runs first; APIs depend on its success. Fixed addresses; `data` network is `internal`; read-only root filesystem (api, redis, migrator); `cap_drop: ALL`; `no-new-privileges`; CPU, memory and PID limits; 30 s stop grace (60 s for postgres); healthcheck on `/health/ready` with no curl in the image |
| `postgres/10-omniai-roles.sh` | creates `omniai_owner` (DDL, migrator only) and `omniai_app` (DML only, `CONNECTION LIMIT` = budget); the database; `vector`; default privileges |
| `.env.example`, `providers.env.example` | placeholders only; provider credentials are kept out of compose interpolation (§16) |
| `scripts/rolling-deploy.sh`, `backup.sh`, `restore.sh` | procedures in `docs/operations/` |
| `/.dockerignore` | no `.git`, `bin/obj`, `node_modules`, env files or keys in any build context |

Images built locally: api 301 MB, migrator 107 MB, edge 22 MB.

Defects found while bringing the stack up (all fixed and re-verified):
1. **Migrator:** the self-contained bundle unpacks itself to `$HOME`, which the
   read-only root filesystem forbids. It now uses `DOTNET_BUNDLE_EXTRACT_BASE_DIR`
   on the tmpfs.
2. **Redis:** with every capability dropped, the image's entrypoint could not
   switch from root to `redis`. It now starts as `redis`.
3. **Migrator build:** the bundle needed an explicit `linux-x64` restore.
4. **Npgsql:** probed GSSAPI (`libgssapi_krb5.so.2` missing). Now
   `GSS Encryption Mode=Disable`, since there is no Kerberos.
5. **Scripts:** Git-Bash path translation broke Docker paths; the scripts now
   normalise with `cygpath` (a no-op on Linux).

## 11. Production-like local stack: evidence

Local only: project `omniai_p35`, loopback ports 8080/8443/8444/8445. Local TLS
material came from a drill CA with 7-day certificates, kept in the session
scratchpad and never in the repository. Secrets were freshly generated into a
scratchpad env file and never printed. **The master key is new;** the Phase 34
value was not reused.

**Fresh installation from zero** (empty volumes):
1. `postgres` initialised its roles and extension.
2. The migrator applied `Initial` and `ProductionReadinessHardening` as
   `omniai_owner`.
3. Both APIs became healthy in Production.
4. Accounts, workspaces and data were then created through the edge by the
   Phase 34 seeder.

Drill-only settings, never production:
- Public-chat per-client limits were raised, because the edge correctly
  overwrites `X-Forwarded-For`, so every simulated visitor is one client.
- The two synthetic workspaces were put on the unlimited `development` plan by
  operator SQL, because the seed needs more members than `starter` allows. The
  refusal, `entitlement.members.exhausted`, was itself observed.

The per-workspace generation budget was **not** raised: the seed's AI messages
past it were refused 429 as designed.

### 11.1 Connection budget, two instances, `max_connections = 100` (Phase 34 blocker)

The load: 120 admin-read VUs and 120 public-chat visitors, concurrently for
90 s, through the edge to both APIs. The human-handled messages still take the
conversation lock. Connections were sampled every 1 s:

| Pool | Peak | Ceiling |
| --- | --- | --- |
| Application | 50 | 2 × 25 |
| Conversation lock | 30 | 2 × 15 |
| Readiness | 2 | — |
| `omniai_app` total | 82 | budget 84 |
| All clients | 83 | 100 |

| Traffic | Requests | Rate | Failed | p95 |
| --- | --- | --- | --- | --- |
| Admin reads | 43,194 | 477/s | **0** | 724 ms |
| Public chat | 10,883 | 61/s | **0** | 121 ms |

There were **no `53300`** errors. The pools saturate at exactly their
configured ceilings, and excess load waits instead of exhausting PostgreSQL.
**Blocker closed.**

### 11.2 PostgreSQL outage (`docker pause` for 25 s under 4 authenticated readers)

| Phase | Result |
| --- | --- |
| Before the pause | 200s, ≤ 107 ms |
| During | requests held by the frozen server answered a **truthful 503 after ~20 s** (15 s command timeout + cancellation round trips) |
| After resume | first 200 +0.3 s; no restart; both instances healthy |

Phase 34 measured up to ~60 s here, and before its §5.2 fix the answer was 500.

### 11.3 Runtime database role

As `omniai_app`, each of these was refused:
- `CREATE TABLE`, `CREATE SCHEMA`, `CREATE INDEX`, `CREATE EXTENSION`;
- `ALTER TABLE`, `DROP TABLE`, `TRUNCATE`;
- `CREATE ROLE`, `CREATE DATABASE`.

`GRANT` granted nothing. Reads, writes and advisory locks worked. The schema and
every table are owned by `omniai_owner`. The role has no superuser, createdb or
createrole attribute, and a connection limit of 90. The whole product (§11.4
onwards) ran as this role.

Default privileges also gave `omniai_app` DML on EF's `__EFMigrationsHistory`.
**Resolved in §21.2**: the runtime role now has `SELECT` only on it.

### 11.4 Migration failure

The real migrator ran against a scratch database containing a conflicting
`plans` table:
- exit 1, naming `42P07: relation "plans" already exists`;
- the migration **rolled back completely**, leaving only the pre-existing table
  and EF's empty history table;
- the scratch database was dropped afterwards.

On the first bring-up, a failing migrator (defect 1 in §10) also demonstrated
the dependency: **the APIs never started.**

### 11.5 TLS, headers and hidden surfaces (edge)

- HTTP returns 308 to HTTPS (query preserved). TLS 1.3 is negotiated; TLS 1.1
  is refused.
- **SPA:** the CSP has exactly this build's script hashes, `object-src 'none'`
  and `frame-ancestors 'none'`, with `X-Frame-Options: DENY`, `nosniff`,
  `Referrer-Policy: no-referrer`, a `Permissions-Policy`, HSTS for one year and
  `Cache-Control: no-cache`. Hashed assets are `immutable`.
- **`/embed/`:** `frame-ancestors *`, no `X-Frame-Options`,
  `Referrer-Policy: strict-origin`.
- **API:** its own `default-src 'none'; frame-ancestors 'none'`, DENY and
  `nosniff`, plus HSTS set by the edge.
- `/health`, `/health/ready`, `/openapi/v1.json`: 404. Unknown `/api/*`: 404.
- `Server: nginx`, with no version.

Defects found and fixed:
6. **`/embed/` was not frameable.** The `try_files` fallback is an internal
   redirect, so the response carried the `location /` headers. Customer sites
   could not have framed the widget. Now served with `rewrite … break`.
7. **The WebSocket location dropped `Host`, `X-Forwarded-Proto` and
   `X-Forwarded-For`.** nginx discards all inherited `proxy_set_header` once a
   location sets any. The API would have refused every SignalR connection as
   insecure transport. Now included per location.
8. **HSTS was missing on proxied API responses for loopback hosts.** ASP.NET
   excludes them. The edge now sets HSTS uniformly on proxied responses.

### 11.6 Rolling deployment under load

`rolling-deploy.sh` ran (migrate as a no-op, then api-a, then api-b) during
150 s of 20 admin-read VUs plus 20 chat visitors. It took 38 s. **Zero failed
requests:** 31,546 reads (p95 403 ms) and 2,588 chat requests (p95 357 ms).

The edge failed over 20 requests (8 GET, 12 POST) that hit a refused connection
on the instance being replaced. A refused connection means the request was never
sent, so passing it on is safe. No already-sent request was replayed.

### 11.7 Unhealthy instance (honest) — superseded by §21.3

`api-b` was disconnected from its database and Redis network for 45 s while it
kept accepting connections:
- 56 of 12,940 requests surfaced as 503s, all from `api-b`.
- 46 of them were POSTs, which are correctly never replayed.
- 10 were GETs that took 16–17 s, longer than the edge's 10 s retry window,
  because name resolution failed slowly.
- Docker marked the instance unhealthy after about 20 s. The edge does not use
  Docker health.

This is documented as passive-failover behaviour, and the runbook requires
draining an instance that is unready while others are ready. It is not claimed
as seamless.

### 11.8 Backup and restore (disaster recovery on the drill stack)

1. Back up while running (`--with-reports`).
2. `down -v`, destroying every volume.
3. Start a fresh `postgres`.
4. Restore: checksum verified, one transaction.
5. Restore the report volume.
6. `up`: the migrator reported "No migrations were applied".

| Check | Result |
| --- | --- |
| Destroy to both APIs healthy | ~28 s |
| Row counts, all 49 tables | identical |
| A session cookie issued before the loss | 200 afterwards (key ring + master key) |
| CSV/PDF/XLSX artifacts | byte-identical |

Defect found and fixed:
9. **`backup.sh --with-reports` guessed the volume name** and would have
   archived a new, empty volume. It now reads the volume mounted on the running
   API and refuses if there is none.

### 11.9 Production-build Chrome pass (headless Chrome 153 over CDP)

Chrome trusted only the drill certificate's key (SPKI pin).

- The real sign-in form worked. Cookies:

  | Cookie | Flags |
  | --- | --- |
  | `omniai.session` | `Secure`, `HttpOnly`, `Lax` |
  | antiforgery | `Secure`, `HttpOnly`, `Lax` |
  | `XSRF-TOKEN` | `Secure`, `Lax`, readable by design |

- Eleven routes (overview, conversations, customers, knowledge, channels,
  analytics, reports, plan usage, notifications, team, AI agents): **zero failing
  API calls, and zero CSP violations.**
- **Writes:** widget update 200 and customer rename 200. Replaying the old ETag
  gives 412, as it should.
- **Embed:** the widget snippet's URL was derived from the preserved `Host`.
  - From the allowed origin `https://127.0.0.1:8444`, the iframe loaded and the
    public embed API answered 200.
  - From the disallowed origin `https://127.0.0.1:8445`, it was refused with a
    non-disclosing 404.
- **Rolling restart with the Admin open:** the page kept working and stayed
  signed in. The hub socket closed (1006) when its instance restarted, then
  reconnected (101).

Defects found and fixed:
10. **Every `If-Match` write failed through the edge (400, weak entity tag).**
    nginx gzip turns a strong `ETag` into `W/"…"`. API responses are no longer
    compressed at the edge; static files still are.
11. **SignalR broke across two instances.** Round-robin sent negotiate and
    connect to different instances ("connection could not be found", 404).
    `/hubs/` now uses a consistent client-address hash, and every connection
    upgrades.

### 11.10 Operational email over verified STARTTLS

The SMTP sink required STARTTLS. Its certificate, from the drill CA, carries a
CRL distribution point on the private network.

- The first attempts failed `connect_failed`. The client checks revocation, and
  a local CA without a CRL cannot be checked.
- **Revocation was not disabled.** The drill CA now publishes a real CRL.

With the CRL in place:

| Case | Result |
| --- | --- |
| Valid certificate | email `Sent` on attempt 1; the API fetched the CRL itself (log: `api-b` → `GET /ca.crl` 200) |
| **Certificate revoked** in a republished CRL | STARTTLS refused (`SslHandshakeException`); nothing delivered; email kept `Pending` |
| Fresh certificate, new serial | the pending email was delivered on its own retry, as attempt 2 |

An email whose attempts ran out during the earlier failures ended
`Failed/attempts_exhausted`, as designed.

Email is opt-in: the owner's default preferences send none, which is correct.
The drill opted in through the real preferences API.

Found and fixed:
12. **`connect_failed` gave operators no cause** (the sender logged nothing). It
    now logs one warning with the configured host, port and exception **type**:
    no addresses, content, credentials or server text. Tests: 52/52
    notification and email tests green.

### 11.11 Log canary scan

The scan covered 75,564 saved drill log lines plus every drill container's
current log. It found **0** occurrences of any of these:
- the six generated secrets or the host's OpenAI key;
- the synthetic password;
- `omniai.session=`, `Set-Cookie`, `Authorization:`, `X-XSRF-TOKEN`, `Bearer `,
  `sk-`, `ticket=`, `X-OmniAI-Embed-Session`, `Password=`;
- synthetic email addresses.

## 12. Redis, report storage, static delivery, containers, resources

| Area | Detail |
| --- | --- |
| Redis | required in Production (§9) |
| Redis settings | password, no persistence (`appendonly no`, `save ""`), `noeviction`, `maxmemory` 256 MB |
| Redis contents | rate-limit counters, tickets, backplane; nothing durable |
| Redis loss | public writes fail closed (Phase 34 §6.2) |
| Report storage | one named volume shared by both instances on a host |
| Report storage beyond one host | needs a shared filesystem or a different store (documented limit) |
| Static delivery | Angular production build served by the edge; hashed assets immutable; `index.html` `no-cache`; SPA fallback |
| Containers | non-root; read-only root filesystem where possible; capabilities dropped; `no-new-privileges`; limits |
| Stop grace | 30 s, proven by the rolling drill |
| Resource guidance | `production-deployment.md` §9 |

## 13. Rollback

Rollback is safe within an expand-only release. Redeploy the previous API image
with the same rolling procedure. Readiness accepts a schema that is ahead of the
build. The Phase 35 migration only adds (two nullable columns and two partial
indexes), so a rollback to `bfa20d2` needs no schema change.

While both versions run, old billing claims carry no lease timestamp, so they
are treated as stale. The effect is idempotent double processing, exactly as
before Phase 35.

The schema is never rolled back on a live database. A bad migration is
recovered by restoring the pre-release backup.

## 14. Documentation (new)

| Document | Content |
| --- | --- |
| `docs/architecture/production-readiness.md` | shape, database policy, crash and multi-instance matrix, security boundary, dependency failure modes, known limits |
| `docs/operations/production-deployment.md` | topology, roles, configuration and fail-fast, budget, edge rules, install, rolling release, rollback, resources |
| `docs/operations/backup-restore.md` | what to back up, the artifact decision, procedure, drill evidence, objectives |
| `docs/operations/operations-runbook.md` | health, logging contract, metrics, alerts (SQL checked against the schema), incidents, secret inventory and rotation |

## 15. External provider readiness

| Provider | Code status | Local proof in Phase 35 | Live proof | Release classification |
| --- | --- | --- | --- | --- |
| OpenAI (chat + embeddings) | complete (Phases 11–23), ledger before dispatch, no retries, `Unknown` on ambiguity | truthful refusal without a key (503, turn `Failed`, ledger unchanged) | the §16 incident shows the adapter works against the real API; that was never authorised and is not claimed as validation | **Ready to configure**; validate once, deliberately, on the target deployment |
| Stripe (billing) | complete (Phase 25), signed webhooks, idempotent inbox, now with lease | in-process with a fake provider; claim, lease, fence and settle proven | none (forbidden) | **Ready to configure**; a live test-mode validation is required before charging |
| SMTP | complete (Phase 32), plus Phase 35 dispatch boundary and diagnostics | real STARTTLS with verified revocation; sent / refused / retried | none (forbidden) | **Ready to configure** with a relay whose certificate can be revocation-checked |
| Meta WhatsApp | code present (Phases 20–25); real validation in Phases 26B–29 | not exercised (deferred) | none | **Deferred:** Phases 26B–29 stay out of V1.0. The WhatsApp channel must not be offered to customers until they are done; the webhook refuses everything while its secrets are unset. |

## 16. Incident: live OpenAI traffic during the drill

| | |
| --- | --- |
| What | 60 text generations (6,238 tokens) and 3 embeddings (733 tokens) were sent to the real OpenAI API with the machine owner's key, 2026-09-26 23:05:47–23:06:14 UTC, while the Phase 34 seeder populated the local drill stack |
| Why | The Windows user environment defines `OPENAI_API_KEY`. `compose.production.yml` mapped it as `OPENAI_API_KEY: ${OPENAI_API_KEY:-}`, and compose interpolation prefers the calling shell over `--env-file`. In Production mode the Development-only `OMNIAI_LIVE_OPENAI` guard does not apply, by design. |
| Data sent | only the seeder's invented text: sample questions ("Hi, where is my order?") and generated policy paragraphs. No real customer data, no secrets |
| Found by | the usage ledger during the email check (`ai_provider_usages`: `Succeeded` rows on a stack with no configured key) |
| Contained | both APIs stopped at once. The ledger, which writes `Started` before every call, shows no call outside that 27-second window. Stripe, Meta and SMTP had no live credentials. |
| Fixed, drill | every drill compose invocation strips provider variables (`env -u …`); containers were verified key-free before any further traffic |
| Fixed, product assets | provider credentials moved out of interpolation into `deploy/providers.env`, read with `env_file`, which a shell cannot override. Proven: with the key present in the shell and no stripping, the resolved configuration of both APIs carries no `OPENAI_API_KEY` |
| Truthful behaviour without a key | an AI-mode message is refused 503 "AI text generation is not available on this deployment"; the turn is `Failed`; no ledger row |
| Recorded | project memory, so future sessions strip provider variables before any Production-mode stack |
| Containment re-verified (§21.1) | hostile-shell canaries never reach a container, at `compose config` level and inside running containers; the provider ledger stayed at **0 rows** through every later drill |

This broke the phase rule "Do NOT contact live OpenAI". It is disclosed here,
not minimised. The owner may want to review usage on that key.

## 17. Release readiness matrix

| Area | Status | Evidence |
| --- | --- | --- |
| Connection budget (Phase 34 blocker) | **Closed** | §2, §11.1 |
| Database timeouts / outage | Ready | §2, §11.2 |
| Email dispatch boundary | Ready | §3, §11.10 |
| Billing ownership / lease | Ready | §4 |
| Stuck conversation turns | Ready | §6 |
| Knowledge single flight | Ready | §7 |
| Customer list scaling | Ready (bounded) | §8 |
| Schema / migration policy | Ready: one generated migration, model-authoritative | §5, §18 |
| Runtime role separation | Ready; migration history is read-only for the runtime role | §11.3, §21.2 |
| Migration procedure + failure | Ready | §11.4 |
| Production configuration fail-fast | Ready | §9 |
| TLS / proxy / headers / embed / CORS | Ready after fixes 6–11 | §11.5, §11.9 |
| Rolling deployment | Ready | §11.6 |
| Unhealthy-instance routing | **Ready: automatic readiness-aware draining** (edge readiness watcher); manual drain is a fallback | §21.3 |
| Backup / restore | Ready | §11.8 |
| Metrics instrumentation | **Ready** (`OmniAI` meter) | runbook §1 |
| External metrics exporter / monitoring backend | **Production deployment requirement (important)**; not wired; no alert is automated by the product | runbook §1–2 |
| Health, logs, trace ids, SQL runbook checks | Ready | runbook |
| Secrets / rotation | Ready; master-key rotation is a maintenance event | runbook §4 |
| Providers | OpenAI, Stripe, SMTP ready to configure; **Meta deferred** | §15 |
| Topology | **V1 target: one Docker host, two API instances, one shared local report volume.** Multi-host needs shared durable `ReportArtifactStore` storage and is **not supported** | architecture §6 |
| Provider secret provisioning | Ready: explicit `providers.env` only; the shell cannot inject | §16, §21.1 |

### Blocker classification

| Class | Items |
| --- | --- |
| Release blockers remaining | **none in the product.** Before first production use: a deliberate live validation of each configured provider on the target host; repeating these drills on that host; publishing a trusted TLS certificate |
| Important, accepted for V1.0 | external metrics exporter / monitoring backend (a deployment requirement); single host only; master-key rotation logs everyone out and requires reconnecting WhatsApp credentials (no dual-key rotation); in-flight requests to an instance that becomes unready can fail during the ~8 s detection window |
| Deferred by decision | Meta WhatsApp real validation (Phases 26B–29) |

## 18. Quality gates and model authority

Final runs, after every fix, on the final source:

| Gate | Result |
| --- | --- |
| `dotnet restore` + Release build (solution) | succeeded, **0 warnings, 0 errors** (warnings are errors in this repo) |
| Domain tests | 797 passed |
| Application tests | 714 passed |
| Infrastructure integration (PostgreSQL 17 + pgvector, Redis) | 1,025 passed, 1 skipped (the opt-in scale test, run separately in §8) |
| API integration | 893 passed, 2 skipped (the opt-in Phase 34 capacity and crash drills) |
| **Backend total** | **3,429 passed, 0 failed** |
| `dotnet list package --vulnerable --include-transitive` | no vulnerable package in any project |
| Frontend `npm ci` | ok |
| Frontend production build | ok |
| Frontend `test:ci` | 784 tests / 63 files passed. The first run hit a Vitest *worker exit* while the backend suite loaded the machine; there were no frontend changes in Phase 35; the rerun on an idle machine was green. |
| Frontend `format:check` | clean |
| `npm audit` (production and all) | 0 vulnerabilities |
| Independent code review of the whole Phase 35 diff | no findings at its confidence bar. It traced the billing fence, email boundary, pool separation, the reconciler vs the lock, pagination order, and the nginx semantics. |
| Log canary scan | 0 hits (§11.11) |
| Container images | api, migrator and edge built from the final source |

Phase 34 regression set: the Phase 34 in-process performance and correctness
scenarios are part of the API suite above and pass, including the Knowledge
concurrency test updated for single flight. The out-of-process admin-read and
public-chat load (§11.1) ran at 477 and 61 req/s with zero failures on the
production topology. That is above the Phase 34 local envelope, which was set by
a Development-mode single instance.

**Model authority.** The EF model is the source. `20260926215858_ProductionReadinessHardening`
was generated by `dotnet ef migrations add`, regenerated with `remove` + `add`
when the model changed, and never edited. It has no `migrationBuilder.Sql`. The
snapshot changes only by the four additions. The rendered SQL is exactly:

```sql
ALTER TABLE billing_events ADD claim_token uuid;
ALTER TABLE billing_events ADD claimed_at timestamp with time zone;
CREATE UNIQUE INDEX ux_knowledge_index_attempts_in_flight ON knowledge_index_attempts (document_id, tenant_id) WHERE status = 'Started';
CREATE INDEX ix_conversation_turns_generating ON conversation_turns (created_at) WHERE status = 'Generating';
```

This runs in one transaction. `DatabaseModelAuthorityTests` pass.

**Current-model scratch-Initial verification** was performed at checkpoint time,
after every Phase 35 model change, in a throwaway copy of the backend inside a
disposable container. The repository was mounted read-only.

1. The migrations were deleted in the copy.
2. `dotnet ef migrations add ScratchInitial` generated a clean Initial from the
   current model. It was not edited, and contains 0 `migrationBuilder.Sql`.
3. It was applied to a fresh PostgreSQL 17 + pgvector 0.8.6 database.
4. The real two-migration history was applied to a second fresh database.

Results:

| Check | Result |
| --- | --- |
| Full schemas (`pg_dump --schema-only`) | **identical**: 907 lines each; the only differing lines are pg_dump's random `
estrict` session tokens |
| Seeded plans | identical |
| `billing_events.claim_token` (uuid) and `claimed_at` (timestamptz) | nullable, present |
| `ux_knowledge_index_attempts_in_flight`, `ix_knowledge_index_attempts_document`, `ix_conversation_turns_generating` | present |
| `conversations.customer_id` | NOT NULL |
| Physical `xmin` columns | 0 |
| `knowledge_chunks.embedding` | `vector`, with its cosine index |
| Tenant-scoped foreign keys | 42 |
| Data Protection, reports, notifications, billing, knowledge and turn tables | present |
| Totals | 49 tables, 24 check constraints |

Also verified:
- `dotnet ef migrations has-pending-model-changes` on the repository: "No changes
  have been made to the model since the last migration".
- The migration Designer's target model and the model snapshot are identical.
- `DatabaseModelAuthorityTests` and `MigrationSchemaTests` passed: 35/35.

The scratch migration, both databases and the copy were then removed. The
repository keeps its two-migration history. The fresh installation in §11 is a
separate proof: an empty database, the real history applied by the bundle, and
the product working on it.

Schema-pinning tests updated for intentional Phase 35 facts, not weakened:
- the migration test now asserts that every known migration is applied in order
  (Initial first, exactly one Phase 35 migration);
- the index count for `conversation_turns`/`messages` goes from 5 to 6, with the
  new partial index asserted by name and filter;
- `billing_events.claim_token` is added to the credential-shaped column
  inventory, with its justification.

## 19. Cleanup

- Drill stack `omniai_p35`: all 9 containers, its volumes (including the synthetic
  database and reports) and networks removed. Nothing named `omniai_p35` remains.
- Drill images (`omniai-api:p35`, `omniai-migrator:p35`, `omniai-edge:p35`) and
  the helper images pulled this phase (`curlimages/curl`, `alpine/openssl`)
  removed. Base images (.NET SDK/runtime, pgvector, nginx, node, redis, mailpit,
  k6) remain as cache.
- Scratchpad secrets deleted: the drill env and providers files, the drill CA
  and its private keys, certificates, the CRL, database dumps, the Chrome
  profile and the collected logs. Only non-secret drill scripts remain in the
  session scratchpad (outside the repository).
- The Phase 34 test PostgreSQL and Redis (`omniai_p34_pg`, `omniai_p34_redis`)
  were restarted for the test runs and stopped again, as Phase 34 left them.
  Unrelated containers were not touched.
- Repository: no env file, key, certificate or dump in the tree (tracked or
  ignored). Nothing staged or committed.

## 20. Limitations

- Every drill ran on one Windows laptop (Docker Desktop, WSL2). Numbers are
  local evidence, not an SLA, and must be re-measured on the target host.
- TLS used a local drill CA. Browser trust was provided with an SPKI pin, not a
  system-wide trust change.
- No live provider was validated on purpose; the §16 traffic was unintended.
- The rolling drill replaced containers with the same image. A real release
  also changes code, which the expand-only migration rule covers.

## 21. Final acceptance remediation (after review)

The review asked for five things before acceptance:
- verify the provider-secret containment;
- make unhealthy-instance draining automatic;
- remove the runtime role's writes on the migration history;
- classify monitoring honestly;
- re-run the gates.

**No OpenAI, Meta, Stripe or external email traffic** occurred during this
pass. The provider ledger stayed at 0 rows, and all email went to the local
sink.

### 21.1 Provider-secret containment (synthetic canaries only)

Only `OMNIAI_*` deployment settings are interpolated from the shell. No provider
credential is a `${…}` in `compose.production.yml`.

In every case below, the calling shell had every provider variable set to a
synthetic `HOST-CANARY-*` value: `OPENAI_API_KEY`, `STRIPE_*`, `OMNIAI_WHATSAPP_*`,
`Email__Smtp__*` and `OMNIAI_SMTP_PASSWORD`. The real host key was never read or
printed.

| Case (`docker compose config`) | api-a / api-b | migrator, edge, postgres, redis |
| --- | --- | --- |
| A. no providers file | **no provider variables** | none |
| B. explicit synthetic providers file (OpenAI, Stripe, Meta, SMTP) | exactly the file's `FILE-CANARY-*` values, never the host canary | none |
| C. file with SMTP only | SMTP only; **no OpenAI key** despite the host canary | none |

**Running containers:** the drill stack was started with host canaries in every
provider variable. `printenv` inside both APIs found `OPENAI_API_KEY`,
`STRIPE_SECRET_KEY`, `OMNIAI_WHATSAPP_APP_SECRET` and `Email__Smtp__Password`
all absent.

Git and build safety:
- `deploy/providers.env` and `deploy/.env` are ignored by the `*.env` rule, and
  no such file is tracked.
- With canary files present, a probe build using the repository's
  `.dockerignore` received only `deploy/.env.example` and
  `deploy/providers.env.example`, and no canary value.
- A full-filesystem scan of the api, migrator and edge images found no env file,
  key or certificate outside the OS trust store.

Production still uses OpenAI when explicitly configured: no live switch was
added (§16 design unchanged).

### 21.2 Runtime role and the migration history

A new one-shot compose step, `migrator-privileges`, runs after every migration
as the owner:

```sql
REVOKE ALL ON "__EFMigrationsHistory" FROM omniai_app;
GRANT SELECT ON "__EFMigrationsHistory" TO omniai_app;
```

The APIs depend on its success, and `rolling-deploy.sh` runs it after the
migrator. Readiness needs `SELECT`. Nothing at runtime writes the history.

Proof on a fresh database: the owner applied both migrations, and the step
exited 0. As `omniai_app`:

| Operation | Result |
| --- | --- |
| `SELECT` on the migration history | ok (2 rows) |
| `INSERT`, `UPDATE`, `DELETE`, `TRUNCATE` on the migration history | **permission denied** |
| `CREATE TABLE`, `CREATE SCHEMA`, `ALTER TABLE`, `ALTER SCHEMA`, `DROP TABLE`, `DROP INDEX`, `TRUNCATE` an application table | denied |
| Vector similarity query on `knowledge_chunks` | ok |
| DML, advisory locks | ok |

Grants on the history table: `omniai_app|SELECT` only.

The whole application ran as this role:
- sign-in (browser);
- 270 conversations and 815 messages, with turns `Completed`, and `Failed`
  truthfully where AI was requested without a provider;
- 6 Knowledge documents, and indexing attempts `Failed/provider_unavailable`
  with their alerts;
- a report `Completed` and downloaded;
- a `ReportCompleted` notification, and its email `Sent` over verified STARTTLS
  to the local sink.

Live embedding and RAG retrieval could not be exercised without a provider: the
composition has no local embedding provider and the endpoint is not
configurable. The vector SQL path was exercised directly instead.

### 21.3 Automatic draining of an unready instance

**Design.** A readiness watcher (`deploy/edge/readiness-watcher.sh`) is started
by the edge image's entrypoint. The proxy technology is unchanged, and no
orchestrator was added.

- It probes `/health/ready` for each instance every 1 s, with a 4 s timeout.
- After 2 failures the instance is marked `down` in both upstreams (API and
  hubs). After 2 successes it is re-added.
- Changes are validated with `nginx -t` and applied by graceful reload.
  `worker_shutdown_timeout 30s` bounds lingering connections.
- If every instance is unready, all stay routed.
- `/health/live` is untouched, and never used for routing.
- An operator drain mark (`/tmp/omniai/drain/<address>`) lets
  `rolling-deploy.sh` take an instance out of routing **before** stopping it.

**Drill.** Load was 10 admin-read VUs plus 10 chat visitors. The production Admin
was open in Chrome. `api-a` was disconnected from its database and Redis
network for 30 s, then reconnected. This is the final edge image; an earlier
run, before the drain mark was added, gave the same figures.

| Measure | Result |
| --- | --- |
| A `/health/live` / `/health/ready`; B ready | 200 / **503** within 3 s; B 200 throughout |
| Health detection and drain | **8 s** after the cut (the watcher logs `NOT READY` and then `routing to: 172.30.35.22:8080`) |
| Re-add after reconnection | **4 s** |
| Requests in the window | 9,800 |
| User-visible failures | **23**: 22 started before the drain (11 GET and 11 POST, truthful 503s from the partitioned instance taking up to 15 s; POSTs never replayed) and **1** of the 4,886 that started while A was drained |
| New requests reaching A while drained | 58, on HTTP/2 connections held by old workers finishing their connections after the reload; 57 succeeded |

For comparison, the §11.7 drill with passive failover only had 56 failures
spread across the whole 45 s outage.

**Chrome during the drill:**
- no page reload was needed; still signed in (`/api/auth/me` 200 at the end);
- in-app navigation continued: 188 API calls with 1 transient 503, and the
  next navigation succeeded;
- the hub socket pinned to A was closed by the old worker 30 s after the drain,
  and reconnected at once (101). That produced one console message ("WebSocket
  closed with status code: 1006"), with no retry storm;
- no host, IP or topology detail appears in any message.

**Rolling release, re-run with the edge drain.** Zero failed requests: 25,577
reads and 2,384 chat, with no 5xx at the edge. Before the drain mark, one POST
reached an instance in the moment it was being stopped, giving 1 × 502. That
race is why the drain mark was added.

### 21.4 Documentation updated

The following were updated for the design above:
- `production-deployment.md`: readiness-aware routing, the manual drain command,
  the rollout drain, `migrator-privileges`, and `providers.env` in the install
  steps;
- `operations-runbook.md`: an automatic §3.1, the alert, and the metrics
  classification;
- `production-readiness.md`: roles and limits.

The master-key consequence (every session logged out, WhatsApp credentials to
reconnect) and the single-host storage limit are stated in the runbook and the
architecture document respectively.

### 21.5 Final gates (after these fixes)

| Gate | Result |
| --- | --- |
| `dotnet restore OmniAI.slnx` + Release build | succeeded, **0 warnings, 0 errors** |
| Domain / Application tests | 797 / 714 passed |
| Infrastructure integration | **1,031 passed**, 1 skipped (opt-in scale); includes the new `DeploymentConfigurationTests` (6) |
| API integration | **893 passed**, 2 skipped (opt-in capacity and crash drills); includes `ProductionConfigurationTests` (10) |
| **Backend total** | **3,435 passed, 0 failed, 3 skipped** |
| `dotnet list package --vulnerable --include-transitive` | 8/8 projects: no vulnerable packages |
| Frontend `npm ci` / `npm run build` | ok / ok |
| Frontend `npm run test:ci` | **784 tests, 63 files, all passed** |
| Frontend `npm run format:check` | clean |
| `npm audit` | 0 vulnerabilities (`npm audit fix` not run) |
| Frontend lint | there is no `lint` script in `frontend/package.json`; none was run |
| Provider traffic during this pass | provider ledger **0 rows**; one email, to the local STARTTLS sink (synthetic `omniai.example` recipient); no Meta or Stripe traffic |
| Migrations | 2 (Initial + `ProductionReadinessHardening`); no migration added by this pass |
| Packages added | none (NuGet or npm) |
