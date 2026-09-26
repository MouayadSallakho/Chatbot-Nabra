# Phase 34 — Performance, load, failure and recovery validation

> Status: COMPLETE — waiting for review. It was written as each scenario
> finished, so the interrupted session (§3) resumed from recorded evidence.

Baseline: `1b9be2f` (Phase 33.5, accepted and checkpointed). Working tree left
unstaged and uncommitted. Nothing was deployed; no VPS was accessed. Phases
26B–29 remain deferred.

**Everything below is local capacity evidence for one documented machine and
configuration. None of it is a production SLA.**

## 1. Environment profile

| | |
| --- | --- |
| Host | Windows 11 Home 10.0.26200; Intel Core i5-1135G7 (4 cores / 8 threads, 2.4 GHz base); 31.7 GB RAM; NVMe SSD (Kingston SNV2S 1 TB) |
| Docker | Docker Desktop 29.4.1 (WSL2 kernel 6.6.87.2), VM: 8 CPUs / 15.45 GiB, overlayfs, cgroup v2 |
| API | .NET 10.0.11 (SDK 10.0.400), **Release** build, `ASPNETCORE_ENVIRONMENT=Development` (HTTP on loopback; production-only transport/HSTS rules not exercised), Server GC, container limit **2 CPUs / 2 GiB**, `--stop-timeout 30`, no Kestrel limits configured, Npgsql pools at defaults (100 each) unless stated |
| PostgreSQL | 17.11 + pgvector 0.8.6 (`pgvector/pgvector:pg17`), container limit **2 CPUs / 2 GiB**, `max_connections=100`, `shared_buffers=128MB`, `synchronous_commit=on`, fdatasync ≈ 785 ops/s (1.27 ms) on the Docker volume |
| Redis | 7.4.11, container limit 1 CPU / 256 MiB, `maxmemory=0`, `noeviction` |
| Load generator | k6 v1.3.0 (`grafana/k6:1.3.0@sha256:3ddc8b1a…`), container limit 2 CPUs, running **inside the API's network namespace** (loopback, no Docker port-proxy hop); Node 24.19 for the SignalR / protocol-level tools |
| Stubs / sinks | Mailpit v1.21 (SMTP sink), local Stripe and Meta stubs (never the real services) |
| Other load on the host | two unrelated local containers (portainer, postgres 18, mailpit) idle throughout; the Phase 33.5 stack was stopped for the duration and restarted afterwards |

Idle baseline (API A alone, 60 s): API 1.2 % CPU / 121 MiB, PostgreSQL 78 MiB,
~5.9 database transactions per second from background workers.

## 2. Synthetic data profile

Fresh database `omniai_p34` built only from `20260925225105_Initial`; seeded
through the product's own APIs by `tools/performance/seed/seed.mjs`
(synthetic `@omniai.example` identities, invented text).

| Data | Count |
| --- | --- |
| Workspaces | 3 (A: 7,001 conversations; B: 2,750; probe: 12) |
| Users / memberships | 10 / 10 (owners, supervisors, analysts, viewers via real invitations) |
| Conversations = customers = web-chat sessions | 9,763 |
| Customer messages / turns | 24,375 (AI turns `Failed`: no provider is configured out-of-process) |
| Customers with display names | 952 |
| Knowledge documents / indexing attempts | 50 / 7 (attempts failed truthfully: no embedding provider) |
| Notifications / occurrences | 19 / 7 |
| Database size | 35 MB |

The in-process suites (§4) create their own disposable databases.

## 3. Tooling and method

- `tools/performance/` (non-production, loopback-only by a hard-coded allow-list):
  k6 scenarios, a staged runner with warm-up, a resource sampler
  (container CPU/memory, PostgreSQL connections / lock waits / longest active
  statement, Redis clients/memory/ops), a summariser, a serial endpoint profiler,
  the seeder, and a dependency-free SignalR JSON-protocol client.
- `backend/tests/OmniAI.Api.IntegrationTests/Performance/`: in-process scenarios
  over the real application, real PostgreSQL/Redis and the **real OpenAI adapter
  and usage ledger** with only the socket replaced by a deterministic handler
  that answers after a deliberate latency and records overlap. This is the only
  way to exercise AI turns locally without a live provider: the production
  composition root never registers a deterministic generator.
- Closed-loop stages (fixed VUs/concurrency, 30 s k6 / 15 s in-process), a
  separate warm-up, and a latency trend that excludes sign-in.

### Harness defects found and corrected (not product findings)

1. **k6 cookie jar reset.** The first staged read run answered almost entirely
   401: k6 clears each VU's cookie jar between iterations by default, so the
   session survived one iteration only. Fixed with `noCookiesReset: true` in the
   three scenarios that hold cookie state (Admin reads and writes keep a session;
   the public visitor keeps its credential and antiforgery cookies). A 5-VU
   sanity run then gave 750/750 × 200. The 401 run is not used as evidence.
2. **Marker collision.** The first cross-conversation run reported overlap
   inside one conversation: the harness keyed conversations by the first 12 hex
   digits of a time-ordered (v7) UUID, which collide within a millisecond.
   Keyed by the full id; the invariant then held.
3. **Interrupted session.** VS Code closed during the staged read run; Docker
   Desktop stopped with it. The partial run (warm-up only) was discarded, orphan
   processes were checked (none), the stack restarted from its persisted volume,
   and the read baseline was re-run cleanly.

## 4. Load and concurrency results

### 4.1 Authenticated Admin reads (k6, owner of workspace A, weighted mix)

| VUs | req/s | p50 ms | p95 ms | p99 ms | errors | API CPU | PG CPU (limit 200 %) | PG conns |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | 43.9 | 7.6 | 93 | 155 | 0 | 39 % | 80 % | 11 |
| 5 | 78.0 | 14.5 | 283 | 408 | 0 | 66 % | 210 % | 11 |
| 10 | 70.8 | 80 | 613 | 800 | 0 | 67 % | 205 % | 13 |
| 25 | 73.6 | 111 | 1,507 | 1,917 | 0 | 81 % | 207 % | 28 |
| 50 | 71.1 | 203 | 3,058 | 4,049 | 0 | 201 % | 201 % | 54 |
| 100 | 60.6 | 499 | 6,314 | 10,139 | 0.3 % (503) | 197 % | 201 % | 78 |
| 200 | 45.8 | 2,693 | 10,514 | 14,843 | 0.3 % (503) | 215 % | 196 % | 98 |

k6 used < 10 % CPU throughout (not the bottleneck). **Knee: PostgreSQL CPU**,
saturated from 5 VUs. Last healthy stage 1 VU; first degraded 5 VUs (p95 ×3).
The 503s at 100–200 VUs are connection waits past the pool timeout, reported
truthfully after the §5.2 fix (they were 500s before it).

Serial per-endpoint cost (from the host, +~13 ms port-proxy overhead): analytics
dashboard 78–86 ms, customers list 37–49 ms, conversations list 30–35 ms,
everything else ~15 ms.

**Customers list scales with the workspace (IMPORTANT, not a blocker).** It is
ordered by each customer's latest conversation activity, computed by a
correlated subquery, so PostgreSQL evaluates it for every customer of the
workspace before taking a page: 11.6 ms at 2,750 customers, 25.0 ms at 7,001
(≈ 3.6 µs per customer; ~360 ms per page projected at 100k). No index removes
this (the sort key is an aggregate over another table); it needs a stored
last-activity value, which is a model/semantics change and is not made in this
phase.

### 4.1b Authenticated Admin writes (k6, antiforgery and If-Match on every write)

Mix: customer rename (each VU owns one customer), notification-preference save
(all VUs share one account, so 412 is the correct answer to a lost race),
mark-all-read.

| VUs | req/s | p50 ms | p95 ms | p99 ms | 412 (lost race) | 5xx | API CPU (limit 200 %) | PG CPU |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | 62 | 14.7 | 37 | 63 | 0 | 0 | 172 % | 36 % |
| 5 | 124 | 39 | 93 | 130 | 4.5 % | 0 | 216 % | 147 % |
| 10 | 168 | 54 | 143 | 195 | 7.2 % | 0 | 243 % | 185 % |
| 25 | 276 | 73 | 187 | 255 | 10.3 % | 0 | 203 % | 190 % |
| 50 | 276 | 116 | 281 | 387 | 11.4 % | 0 | 204 % | 191 % |

Knee at 25 VUs (throughput plateau); API CPU is at its limit from 5 VUs and the
database follows. No 428, no 5xx, no antiforgery refusal; every 412 is a
preference save that lost an optimistic-concurrency race, as designed.

### 4.1c Logging default (out-of-process confirmation of §5.3)

With EF logging at the shipped `Information`, the read stages at 1 and 10 VUs
produced 199,052 log lines for ~3,500 requests (> 50 lines per request).
Throughput there barely moved (47.4 / 67.8 req/s vs 43.9 / 70.8) because that
workload is PostgreSQL-bound; the cost appears where the application or a lock is
the bottleneck (§4.3). `appsettings.json` now sets
`Microsoft.EntityFrameworkCore: Warning`.

### 4.1d Public website chat ingest (k6, real public API)

One visitor per VU with its own address (X-Forwarded-For via the trusted local
proxy), credential cookie and antiforgery token, one message per 3 s (below the
30/min per-client budget), conversations switched to human handling by the owner
so messages are stored without the (absent) AI runtime. Setup needs one start per
visitor, so **only** `ConversationStartsPerEndpoint` was raised for this run;
every other protection limit was at its default. Resource peaks include the setup
phase.

| Visitors | msg/s | p50 ms | p95 ms | p99 ms | errors, default pools | errors, `Maximum Pool Size=40` |
| --- | --- | --- | --- | --- | --- | --- |
| 10 | 5.0 | 39 | 93 | 99 | 0 | 0 |
| 50 | 20.8 | 57 | 446 | 459 | 0 | 0 |
| 100 | 36.9 | 35 | 330 | 396 | 0.5 % (503) | 0 |
| 200 | 59.2 | 93 | 690 | 777 | **24.7 % (503, 53300)** | 0 |
| 400 | 72.5 | 255 | 1,794 | 2,101 | **26.9 % (503)** | 0.1 % |

(The latency columns are the bounded-pool run.) With default pools the 200-visitor
stage collapsed at < 1 % CPU on every container: up to **87 server connections
sat idle holding a conversation's advisory lock** while their requests waited
for an EF connection the server could no longer grant — the same pool-budget
defect as §5.1, here on the human-handling path with no knowledge search
involved. Knee with bounded pools: healthy through 100 visitors, first
sustained degradation at 200 (p95 ×2), saturated at 400 (72.5 of 133 msg/s
offered).

**Local test-environment note:** the local connection string uses the
`postgres` superuser, so the API also consumes PostgreSQL's reserved superuser
slots (the monitoring `psql` itself was refused during exhaustion). Production
must use a non-superuser role (Phase 35).

### 4.1e Redis limiter contention (two API instances, production-default limits)

`tools/performance/limiter-contention.mjs`, requests alternated between
instance A and instance B (shared PostgreSQL and Redis, shared Data Protection
ring — the same visitor cookies and antiforgery token were valid on both):

| Boundary | Offered at once | Result | Persisted |
| --- | --- | --- | --- |
| One client, `RequestsPerClient` 30/min | 60 sends | **30 × 200, 30 × 429**, every 429 with `Retry-After ≥ 1` | exactly 30 messages |
| One workspace, `GenerationsPerWorkspaceBurst` 30/min (AI mode) | 40 turns | 30 × 503 (AI unavailable, the attempt counted) + 7 × 429 + 3 × 503 `53300` | exactly 30 turns / 30 messages; refused requests stored nothing |
| One endpoint, `ConversationStartsPerEndpoint` 60/5 min | 75 starts | **60 × 200, 15 × 429** | exactly 60 conversations, 60 sessions |

No overshoot at any boundary and no process-local authority: the Lua
read-then-increment is atomic across instances. The three `53300` refusals were
instance B's pools against a server already holding instance A's (two instances
× two pools × 40 > 100) — refused, truthfully, before anything was stored; it is
the multi-instance form of the Phase 35 pool budget.

Credentials: `web_chat_sessions` holds `access_token_hash` (bytea); a freshly
issued 43-character credential appears nowhere in session, conversation or
customer data. Absolute 24 h session semantics and lazy (first-send) creation are
covered by the existing Phase 16/18 suites and were not changed.

### 4.1f Reports: idempotency, backlog, artifacts (two API instances, both running workers)

`tools/performance/report-load.mjs` against workspace A (7,001 customers,
24k messages, 90-day range), with four formula-like customer display names
planted first (`=HYPERLINK(…)`, `+1+1`, `-2+3`, `@SUM(A1)`).

| Check | Result |
| --- | --- |
| Same `clientRequestId` submitted 10× at once | 10 × 202, **1 execution** |
| Backlog of 60 distinct requests (every type × format) at once | 60 × 202, submit p50 483 ms / p95 506 ms; peak queue 60; **drained in 33 s** |
| Outcomes | 57 Completed, 3 Failed `report_too_large` (PDF over the 2,000-row cap — truthful) |
| Duplicate execution | none: every execution `attempts = 1`; 58 artifacts for 58 completed executions |
| Queue wait (requested → started) | p50 18.6 s, p95 27.4 s (3 s idle poll, batch 4, 2 instances) |
| Generation time | p50 1.2 s, p95 9.0 s, max 13.1 s (largest: ~750 KB PDFs) |
| Artifact sizes | CSV 62 B – 1.0 MB, XLSX 3 KB – 490 KB, PDF 754–760 KB; store grew to 17 MB / 58 files |
| Hashes | 57/57 downloads match `report_artifacts.sha256` |
| CSV formula protection | all four canaries prefixed with `'`; 0 unprotected cells |
| XLSX | 0 formula (`<f>`) elements; the HYPERLINK text is a literal string |
| PDF | `%PDF-1.4` … `%%EOF`, `ToUnicode` maps present |
| Cross-tenant | workspace B's owner: 404 via its own routes and via A's download route |
| API | peak 166 % CPU / 188 MiB (A), 102 % / 170 MiB (B) |

Currency separation needs provider-cost rows, which do not exist out-of-process
(no provider); it remains covered by the Phase 31 suites.

### 4.1g Notification fan-out and operational email

`tools/performance/fanout.mjs`: workspace B, 40 members with `knowledge.manage`
(each opted in to Knowledge email and connected to `/hubs/workspace-inbox` with
`SubscribeNotifications`) plus 5 members without it; trigger = one real indexing
attempt, which fails truthfully (503, no embedding provider) and raises the
Knowledge alert.

| Check | Result |
| --- | --- |
| In-app notification | all 40 eligible notified within **827 ms**, exactly 1 each; the 5 ineligible: 0 |
| SignalR `notificationsChanged` | exactly 1 per eligible connection, 0 to ineligible ones; payload `["<workspace guid>"]` only — no title, body or PII |
| Hub connection latency | max 62 ms for 45 authenticated connections |
| Email | 40 delivered to the local sink in **4.2 s**, 40 distinct recipients, one subject; 40 × `Sent`, `attempts = 1`; no duplicate email or notification |
| Outbox row | carries no address or body (resolved at send time, after the membership/permission/preference re-check) |

### 4.1h SignalR: connection capacity and invalidation burst (two instances, Redis backplane)

`tools/performance/signalr-load.mjs`: N authenticated `/hubs/workspace-inbox`
connections (several per account, like tabs), half on each instance, each
subscribed to its workspace's conversation and notification groups; 10 % belong
to workspace B as the isolation control. While held, 50 public visitors produced
real changes in workspace A for 40 s (k6, ~18 stored messages/s, plus the 50
setup take-overs). Both instances ran `Maximum Pool Size=20` (two instances × two
pools must fit under 100 server connections).

| Connections | opened / failed | connect p50 / p95 ms | closed while held | events per A connection (min = max) | B connections | foreign-workspace events | API memory (peak) |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 100 | 100 / 0 | 98 / 246 | 0 | 700 = 700 | 0 events | 0 | 159 MiB |
| 250 | 250 / 0 | 106 / 182 | 0 | 718 = 718 | 0 events | 0 | 218 MiB |
| 500 | 500 / 0 | 86 / 112 | 0 | 715 = 715 | 0 events | 0 | 238 MiB |
| 1,000 | 1,000 / 0 | 79 / 126 | 0 | 707 = 707 | 0 events | 0 | 276 MiB |

- Every A connection received **exactly** one `conversationChanged` per change
  (e.g. 650 stored messages + 50 take-overs = 700), on whichever instance the
  change happened — no loss, no duplication, no multiplication; the backplane
  delivers across instances.
- The ingest running during the burst kept p95 at 231–856 ms (no degradation
  attributable to fan-out).
- Payloads are ids only (§4.1g).
- The ramp stopped at 1,000: nothing degraded, and a larger count would be a
  statement about this laptop, not the product. The Phase 33.5 "bounded duplicate
  requests" are a browser-client refetch behaviour; they are checked in the
  Chrome section, not by headless clients.

### 4.2 Conversation turns: correctness under contention (in-process, real lock/ledger)

`ConversationConcurrencyLoadTests` (7 scenarios, all passing, production-default
protection limits; synthetic provider latency 100–400 ms):

| Scenario | Result |
| --- | --- |
| 8 distinct turns at once on one conversation | never two generations in flight (max overlap 1); 8 calls; transcript strictly Customer/AiAgent alternating in ingestion order; each reply sequence = its customer message + 1; 8 ledger rows `Succeeded` (48 tokens each); `LastActivityAt` = max(`SentAt`); p50 1.15 s / max 2.3 s (= serial) |
| 10 concurrent retries of one `clientMessageId` | exactly 1 provider call, 1 turn, 2 messages, 1 ledger row; others 200 (replay) or 409 (in progress); no 5xx |
| 24 conversations × 4 workspaces at once | 24 in flight together (no workspace/app-wide serialisation), per-conversation overlap 1; wall 605–685 ms for 400 ms latency |
| 2 workspaces × 10 conversations × 2 turns | each workspace's instructions canary seen exactly 20 times, never mixed; ledger 20/20 per workspace; each inbox lists only its own conversations; cross-workspace reads 404; a visitor credential cannot read another conversation |
| 12 provider timeouts at once | 12 calls (no retry), 12 ledger rows `Unknown`, no tokens/cost; a resend of an attempted message 409 without a call |
| 26 turns racing a 20-operation plan | exactly 20 calls and 20 ledger rows; 6 × 409 `entitlement.…exhausted`; Warning, Critical and Exhausted each recorded once; 3 notifications to the owner, no duplicates |
| 30 turns from one visitor into one conversation | 10 × 200 (per-conversation budget) + 20 × 429, provider max overlap 1; 29 requests queued on the conversation's advisory lock at once |

### 4.3 Conversation turns: capacity (in-process, opt-in `ConversationCapacityStageTests`)

Closed loop, one conversation per virtual user, 10 conversations per workspace,
200 ms synthetic provider latency, 15 s stages, protection limits raised **in
this fixture only** so the pipeline rather than the limiter is measured. The
test host and its load generator share the test container's CPUs (4, one run 6).

Final configuration (after the §5 fixes, `Maximum Pool Size=40`, EF logging at
Warning):

| Concurrency | turns/s | p50 ms | p95 ms | p99 ms | errors | provider in flight | server conns (peak) |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 10 | 39.8 | 245 | 285 | 334 | 0 | 10 | 22 |
| 25 | 97.4 | 248 | 291 | 375 | 0 | 25 | 51 |
| 50 | 126.8 | 354 | 586 | 639 | 0 | 40 | 81 |
| 75 | 132.9 | 548 | 656 | 883 | 0 | 40 | 81 |
| 100 | 134.4 | 702 | 949 | 1,040 | 0 | 40 | 82 |
| 150 | 132.4 | 1,052 | 1,398 | 1,469 | 0 | 40 | 81 |

Knee between 25 and 50 in flight: generations in flight plateau at **40 = the
lock pool's size**, after which extra load queues (latency) instead of failing.
Integrity after 10,396 turns: 0 `Generating`, 0 ledger `Started`, 0 completed
without reply, 0 customer messages without a turn.

**One workspace only** (all conversations in one workspace, same configuration):
10 → 40.5, 25 → 86.6, 50 → 91.2, 100 → 92.0 turns/s; provider in flight plateaus
at ~27, below the lock pool's 40. That plateau is the per-workspace quota
reservation (`pg_advisory_xact_lock` on `entitlement:{tenant}:MonthlyProviderOperations`,
held for the count + insert of each Started row): **≈ 90 provider operations/s
per workspace on this machine.** By design (it is what makes the quota exact),
and ~180× above the default per-workspace generation budget (30 / min), so it is
recorded, not changed.

### 4.4 Knowledge indexing and RAG (in-process, real embedding adapter and ledger)

`KnowledgeLoadTests` (3 scenarios, passing), deterministic hashed bag-of-words
embeddings (1,536 dimensions) behind the real `EmbeddingClient`:

| Scenario | Result |
| --- | --- |
| 24 documents (~5,000 characters, 4 chunks each) indexed at once, 150 ms embedding latency | all 200 in **474–525 ms**; 24 attempts `Succeeded`; 24 embedding calls (96 inputs, 18 in flight) = 24 ledger rows `Succeeded`; 96 chunks; re-indexing an unchanged document: no attempt, no call |
| One document indexed 4× at once | 1 × 200 + 3 × 412; attempts 1 `Succeeded` + 3 `Failed/document_changed`, none left `Started`; no duplicate chunk ordinal; ledger = calls; **4 paid batches for one version** (see §8 IMPORTANT) |
| 15 + 15 concurrent RAG turns in two workspaces (canaries in each workspace's knowledge, plus a disabled and an unindexed document) | 30 × 200; each workspace's 15 provider requests carried its own canary, never the other's; the disabled and unindexed canaries never reached the provider; largest provider request 8,587 characters (bounded context); p50 1.39 s with 200 ms chat + 50 ms embedding latency — each RAG turn makes two quota reservations under the per-workspace lock |

Vector search itself never appeared among slow statements (> 10 ms) in any run.

Repeatability at 10 in flight across nine runs: 24.7–30.8 turns/s with the
shipped logging defaults, 39.3–40.8 with EF at Warning.

## 5. Defects found under load, and fixes

### 5.1 Request connection held for the whole AI turn (PRODUCT, fixed)

`KnowledgeSearchStore` opened the request's EF connection on the `DbConnection`
directly; EF does not close a connection it did not open, so every AI turn kept
it checked out through the provider call, while also holding its
conversation-lock connection and asking for a third (the ledger's own scope).

- Default pools: at 50 in flight the process asked PostgreSQL for more than its
  100 connections (peak 97 usable) — `53300 too many clients`, 22–34 % of turns
  failed, and 9–11 ledger settlements could not be written (left `Started` for
  the reconciler → `Unknown`: truthful, but real cost figures lost).
- Bounded pools (40): **pool starvation** (mutually blocked pool demand). Turns
  held connections from one Npgsql pool while waiting for a connection from the
  other, so at 50 in flight every turn stalled until the 15 s pool timeout.
  **This was not a PostgreSQL deadlock:** `pg_stat_database.deadlocks` stayed 0,
  the server log recorded no `deadlock detected`, and no `40P01` appeared. The
  failures were client-side pool-acquisition timeouts and, with default pools,
  server connection exhaustion (`53300`).
- **Fix:** open/close through EF's reference-counted
  `OpenConnectionAsync`/`CloseConnectionAsync` (stays open inside a caller's
  transaction). Regression tests: connection closed after both search methods;
  left open and enlisted inside a transaction.
- Durable state stayed consistent in every overloaded run (no stuck turn, no
  reply without a turn, no fabricated usage).

### 5.2 Database unavailability answered 500 (PRODUCT, fixed)

Connection exhaustion, pool timeouts and refused connections reached the
default handler as 500 "server error". Now classified by `DatabaseUnavailability`
as **503 "Service temporarily unavailable"** with a fixed sentence and the
`traceId`; the log records only the SQLSTATE or client-side cause. Deliberately
not recognised: deadlock (40P01), serialization failure (40001), cancelled
statement (57014), constraint/schema errors — those can be the code's fault.
13 tests (classification matrix + an end-to-end request against an unreachable
database whose body contains no host, port, user, database or SQLSTATE).

### 5.3 Shipped logging default logged every SQL statement (CONFIGURATION DEFAULT, fixed)

`appsettings.json` sets `Default: Information` and does not lower
`Microsoft.EntityFrameworkCore`, so every executed command is logged at
Information. Measured in-process at 25 in flight: 20–26 turns/s and p95
1.4–1.8 s with the default, 91–97 turns/s and p95 ~0.3 s with EF at Warning — the
synchronous console logger stretched every transaction, including the
per-workspace entitlement lock. (Every local stack since Phase 18 has set
`Logging__LogLevel__Microsoft.EntityFrameworkCore=Warning` explicitly.)

### 5.5 Redis failures escaping as 500 on public protected paths (PRODUCT, fixed)

Found by the Redis drill (§6.2):

- **Protection check.** With Redis *paused* (accepting TCP, never answering) the
  Lua call surfaced a bare `IOException` (over a `SocketException`) and, in a
  second run, a `TaskCanceledException` raised by the client abandoning the
  stalled connection. Neither was in the store-failure list, so a public start
  answered 500 instead of the Phase 16 contract's 503. Fail-closed still held —
  the check runs before anything is written — but the answer and the operator's
  signal were wrong. `IsProtectionStoreFailure` now also recognises `IOException`
  and an `OperationCanceledException` **only when the caller's own token is not
  cancelled** (an abandoned request is never relabelled as an outage).
- **Realtime tickets.** `RedisRealtimeTickets.IssueAsync` had no failure
  translation at all: with Redis *stopped* it answered 500. It now raises the
  existing `RealtimeUnavailableException` (503 "Live updates unavailable", the
  same answer an unconfigured deployment gives). Redemption is unchanged: it runs
  in the hub handshake, where a failure closes that connection and the client's
  reconnect handles it.
- Regression tests: `ProtectionStoreFailureTests` (7: every store failure incl.
  the two new ones; an abandoned request is not an outage; programming errors are
  not disguised), `RealtimeTicketTests.An_unreachable_store_refuses_a_ticket_as_live_updates_unavailable`.
- Re-verified: two further Redis-pause rounds with 0 unhandled exceptions; ticket
  issuance 3 × 503 during a Redis stop.

### 5.6 Public chat told a visitor "Your message was received" when nothing was stored (PRODUCT, fixed)

Found in the Chrome Redis drill (§7.3). The hosted and embedded chats map every
503 to one kind and showed "Sorry — no answer came back just now. Your message
was received, so you can try sending it again in a moment." Only one 503
("AI generation unavailable") comes after the message is stored. A protection
outage, a withdrawn chat and database unavailability answer 503 with nothing
stored, and the page's own transcript re-read then contradicted the banner.

For 503 the copy is now "Sorry — no answer came back just now. You can try
sending it again in a moment." It is true in every 503 case and still names no
cause (the Phase 18 rule). 502 and 504, which only happen after storing, keep
the "received" wording, because it is true and it discourages a costly resend.
Regression specs: the hosted chat never claims receipt on a 503, and still does
on a 504; the embedded chat has the same guard.

### 5.7 Load scenarios exhausting the shared test server (TEST INFRASTRUCTURE, fixed)

Run in parallel with the other API test classes (each with its own host and
pools), the load scenarios pushed the shared test PostgreSQL past its limit and
other suites' requests failed (503 after §5.2). They now run in a
non-parallel collection (`LoadScenarios`). This is the Phase 33 "tests near the
100-connection limit" observation: its cause is test-host parallelism, not a
product leak (see §6).

## 6. Failure and recovery drills (local Phase 34 stack only)

`tools/performance/probe.mjs` samples liveness, readiness, an authenticated read
and (optionally) a protected public write every 300–500 ms, prints each change of
state, and checks every failure body for leaks (host, port, SQLSTATE, driver
names, stack frames, connection-string words).

### 6.1 PostgreSQL outage

| Phase | live | ready | Admin read | Notes |
| --- | --- | --- | --- | --- |
| `docker pause` (TCP accepted, no answer) | 200 | 503 in **3.05 s** (bounded) | 503 after **15 s** (Npgsql connect timeout) | readiness honours its budget; ordinary requests wait the full connect timeout |
| `docker unpause` | 200 | 200 within 0.6 s | 200 | no manual step |
| `docker stop` (refused) | 200 | 503 in 14 ms | 503 in 28 ms | |
| `docker start` | 200 | 200 1.8 s after start | 200 | no application restart, no data repair |

0 of 196 failure bodies leaked anything. Workers did not storm: each logged one
failure per poll interval (~21 per 100 s for the 2–5 s workers); the API wrote
~75 log lines/s during the outage, mostly multi-line EF/driver stack traces of
those bounded failures.

### 6.2 Redis outage

| Phase | live | ready | Admin read | protected public write | conversations created |
| --- | --- | --- | --- | --- | --- |
| `docker stop` | 200 | 503 | **200** | **503 immediately** | 0 during the outage |
| `docker start` | 200 | 200 after 4.3 s | 200 | 200 after 3.7 s | — |
| `docker pause` | 200 | 503 in 3.0 s | 200 | 503 after ~7 s (client 5 s timeout); one 500 before §5.5 | 0 beyond one already in flight |
| `docker unpause` | 200 | 200 after ~0.5–2.5 s | 200 | 200 | — |

Fail-closed held throughout: no protected public write was served without the
limiter, and none reached any provider. No API restart was needed to recover
(`AbortOnConnectFail=false`). Realtime tickets: 503 "Live updates unavailable"
after §5.5.

### 6.3 Graceful shutdown (`docker stop -t 30`) during a report backlog

30 reports submitted; with 4 `Running`, both instances were stopped with a 30 s
grace: **both exited 0 in 461 ms**. Workers honour the stopping token at once —
they do not finish the item — so the 4 stayed `Running` under their claim and 26
`Pending`; no unfinished (`incoming/`) file was left. After restart (claims aged
past the 10-minute lease by a local timestamp update, instead of waiting), all 30
completed: 26 × attempt 1, 4 × attempt 2, **exactly one artifact each**. The 30 s
grace is therefore not what protects report work (the lease is); it remains the
right setting for HTTP draining (Phase 35).

### 6.4 API process kill (`docker kill`, SIGKILL) mid-generation

- Killed with 4 `Running` (exit 137). Restart to ready **6 s**.
- After the lease: all 30 completed (27 × attempt 1, 3 × attempt 2), 30 artifacts
  for 30 executions, no unfinished or orphaned file (artifact files +29 = the
  29 completed after the kill).
- The ageing update also caught claims the restarted instance had just taken;
  their claim tokens kept any stale worker from settling them (no duplicate
  artifact, no overwritten result).
- **Sessions survived:** the probe's signed-in session read 200 as soon as the
  API answered again, with no new sign-in (Data Protection ring in PostgreSQL).
- The finalised-but-uncommitted orphan window was not hit by the kill; the orphan
  sweep's semantics (key shape, > 6 h, unreferenced) remain covered by the
  Phase 33 suite and were not shortened.

### 6.5 Report storage unavailable (report volume re-mounted read-only)

- 5 reports: each attempted 3 times (released twice, then
  `Failed / generation_failed`) — bounded local retry, then a truthful failure.
- Previously generated artifacts still downloaded (200).
- No path in any client response (execution body; failed download 409). The
  physical path appears only in the operator log's exception text (15 lines).
- Write access restored: 5 new reports all `Completed`.

### 6.6 Operational email: SMTP failure, ambiguity and worker crash (local SMTP stub)

`tools/performance/smtp-stub.mjs` (loopback only; modes accept / hang / slow);
trigger = one Knowledge failure in workspace B → 40 emails to the opted-in
members; `Email:Smtp:Timeout` 5 s for the drills.

| Case | Result |
| --- | --- |
| A. Connection refused (nothing listening) | `Pending/connect_failed` retried with back-off (≈1 min, then 2 min); after 3 attempts all 40 `Failed/attempts_exhausted`; nothing sent |
| B. Message accepted, no reply (hang after DATA) | each email `Unknown/outcome_unknown` after the timeout; **never resent** (stub DATA count = emails attempted, no duplicate); drained one timeout at a time (~12/min at 5 s, i.e. ~2/min per worker at the 30 s default) |
| C. SIGKILL with a claimed batch `Sending` (relay answering after 8 s) | after restart and the lease: the 9 `Sending` rows → `Unknown/abandoned_while_sending`, never resent; the 30 `Pending` continued → final 31 `Sent` + 9 `Unknown`; relay received 32 messages (31 + the one in flight at the kill) |

**Finding (IMPORTANT): claimed-but-unattempted emails are lost on a crash.** The
worker marks its whole claimed batch `Sending` before dispatching the first; in
case C, 8 of the 9 abandoned rows had never been attempted, yet they are
reconciled as `Unknown` and not delivered. No duplicate is possible (correct for
the ambiguous one), but up to batch − 1 pre-dispatch emails per crashed worker
are dropped. Recommended fix (not made: it changes the outbox state machine):
move a row to `Sending` individually immediately before its dispatch, keeping the
batch claim as a lease only.

### 6.7 Provider and Knowledge work killed at the Started boundary

A gated in-process host (`CrashBoundaryHold`, `OMNIAI_PERF_CRASH=1`) held one AI
turn and one indexing attempt inside their provider calls (1 h synthetic
latency) and, once the rows were committed, was **SIGKILLed** (`docker kill` of
its container). State left behind: 1 `TextGeneration` and 1 `Embedding` ledger
row `Started`, 1 attempt `Started`, 1 turn `Generating`. The real API was then
started against that database and the rows aged past the stale thresholds by a
local timestamp update (15 / 30 minutes; no clock endpoint was added):

| After the reconciler | Result |
| --- | --- |
| Provider operations | both `Unknown`, `completed_at` set, tokens and cost **null** (nothing invented); still 2 rows, so the monthly quota still counts them; no provider was called and no new operation was created |
| Knowledge attempt | `Unknown / interrupted`; **exactly one** `KnowledgeIndexingFailed` occurrence and one notification, unchanged after two more passes |
| Conversation turn | still `Generating` — **no reconciler exists for turns** (IMPORTANT, §8). The chat clients mint a new `clientMessageId` per send and never auto-retry with the same one, so the effect is one stale row per crashed turn; a resend with the same id would answer 409 "in progress" indefinitely |

A late settlement from the dead process cannot happen; the `xmin` guard against
one that races the reconciler is covered by `StaleWorkReconciliationTests`. The
drill database was dropped afterwards.

### 6.8 WhatsApp outbound ambiguity under concurrent replies (local Meta stub only)

`tools/performance/meta-ambiguity.mjs`: a synthetic WhatsApp connection on a
synthetic number, 20 customers' inbound messages via correctly signed webhooks,
the owner takes every conversation over and sends 20 replies **at once**; the
local Meta stub (scratchpad, never committed) records each send and never
answers; `WhatsApp:Timeout` 5 s.

- 20 × 200 replies; the stub received **exactly 20 send attempts to 20 distinct
  recipients**, still 20 a minute later — no resend, no duplicate attempt from
  the concurrency.
- All 20 deliveries `Unknown`, `attempts = 1`; 20 `WhatsAppDeliveryUnknown`
  occurrences (one per delivery).
- No live Meta; the stub was returned to its accepting mode afterwards.

### 6.9 Billing events: idempotency and the multi-instance claim (local only)

- Stored-once under concurrent redelivery, reconciliation locking, a live
  subscription refusing a second purchase, and an unknown Price granting nothing
  are re-proved by the existing suites in the full regression run
  (`BillingWebhookApiTests`, `BillingLifecycleApiTests`,
  `BillingSubscriptionServiceTests`). No checkout was load-tested: the fresh
  database has no offers, and hammering checkout is out of scope.
- **Finding (IMPORTANT): billing-event claims have no lease.** The claim treats
  `Claimed` rows as immediately claimable again. Reproduced with the product's
  exact statement on four synthetic rows (with both APIs stopped, rows removed
  afterwards): instance A claims events 1–2 and is still processing; instance B's
  next poll claims **the same events 1–2 again** (attempts 2) while events 3–4
  stay `Pending`. With two or more instances an in-flight Stripe event can
  therefore be processed twice concurrently, consuming its 8-attempt budget and
  delaying the rest of the queue for as long as processing takes. Subscription
  state is not duplicated — the apply re-reads the provider and is idempotent
  under the per-tenant `pg_advisory_xact_lock` — but the claim also uses the
  `WHERE id IN (SELECT … LIMIT n FOR UPDATE SKIP LOCKED)` form that Phase 33 found
  to be unbounded. The fix (a claim timestamp/lease, like reports and email) is a
  model change with a migration and is left to Phase 35 with this evidence.

### 6.10 Rolling restart (two instances, shared PostgreSQL and Redis)

`tools/performance/rolling-restart.mjs`, instance A stopped with a 30 s grace
(exit 0 in 427 ms) and started again (ready in 3 s) while the drill ran:

| Check | Result |
| --- | --- |
| Session issued by A, used on B | 200; a write on B carrying A's antiforgery token: 200 (one Data Protection ring) |
| Session on A after its restart | 200, no new sign-in |
| Steady stream of human replies (through A, else B) | 133 / 133 × 200 — none lost across the stop/start |
| Hub connections | 40 (20 per instance); the 20 on A closed and **all 20 failed over to B** (0 failed attempts) and kept receiving events; per-connection event counts 177–181 (the few missed during the ~1–3 s reconnect are what the Phase 33 client re-reads on reconnect) |
| Worker ownership | report claims across the restarts (§6.3–6.4) never produced a duplicate execution or artifact; no process-local lock is authoritative anywhere (the conversation lock is PostgreSQL's) |

This was two containers on one host without a real load balancer: the client
chose the surviving instance itself. Behaviour behind the production proxy is a
Phase 35 check.

### 6.11 Soak (30 minutes, moderate mixed load, two instances)

Well below every knee: 2 Admin-read VUs, 1 Admin-write VU, 50 public visitors
(one message per 3 s each), 200 hub connections across both instances, one
report per minute. Data grew during the run from 41,881 to 71,430 messages
(+29.5k) and from 53 MB to 67 MB.

| 5-min window | public p50 / p95 ms | reads p50 / p95 ms | writes p50 / p95 ms | 5xx / timeouts |
| --- | --- | --- | --- | --- |
| 0–5 | 39 / 221 (warm-up) | 13.6 / 316 | 13.5 / 43 | 0 |
| 5–10 | 33 / 86 | 11.6 / 300 | 12.5 / 36 | 0 |
| 10–15 | 37 / 85 | 13.2 / 376 | 13.6 / 37 | 0 |
| 15–20 | 36 / 86 | 13.0 / 409 | 13.6 / 36 | 0 |
| 20–25 | 37 / 87 | 13.1 / 435 | 14.0 / 39 | 0 |
| 25–30 | 35 / 80 | 12.8 / 453 | 13.6 / 37 | 0 |

- **Zero errors** in ~118k requests; 29 / 29 reports completed; 200 / 200 hub
  connections stayed open; A connections received equal event counts
  (29,022–29,029, the few-event spread being the subscription ramp), B
  connections 0, no foreign-workspace event.
- Memory (observed 30 min only): API A 150 → 219 MiB (first-third average 202,
  last-third 220); API B 158 → 188 MiB (220 → 185). No monotonic growth.
  PostgreSQL connections 10–56, 0 deadlocks, 0 idle-in-transaction at the end.
- **Read p95 rose 300 → 453 ms.** That is data growth, not degradation: a serial
  re-profile after the soak put the analytics dashboard at 227–285 ms p50 (78–86
  before) with 3× the messages. Each dashboard request runs about five message
  aggregations of 45–85 ms, joining conversations to messages by workspace and
  date range. The plan is a sequential scan of `messages`, which is **correct
  for this synthetic data**: every message was created within the day, so a
  7-day range selects ~90 % of rows and no index helps. With long-lived history
  the question changes (see §8). No index was added.

## 7. Chrome recovery checks (Windows Chrome, local only)

Admin SPA served in the production configuration (`ng serve --configuration
production`, proxy to API A from a scratchpad file; no repository change).
Synthetic accounts only.

### 7.1 API killed and restarted with the Admin open

On Conversations: `docker kill` of API A, 20 s down, restart (ready 5 s), no
reload.

- Same document afterwards (a page marker survived).
- The realtime banner returned to "Live updates on" after about 10 negotiations
  across the two admin hub connections during the ~28 s outage, which is the
  bounded reconnect policy.
- Exactly one refetch each of the conversation list and the unread count, with
  no request storm.
- The session was still valid, and in-app navigation to Customers, Reports and
  Overview loaded normally with nothing left busy.
- The console was clean (no error or warning) across the whole session.

### 7.2 PostgreSQL paused with the Admin open

Navigating to Customers with PostgreSQL paused:

- The page showed its loading state, then "Could not load customers — This
  service is temporarily unavailable. Nothing was changed. Try again in a
  moment." with a Try again button. No stack, host or SQLSTATE appeared.
- **The first request took 60.1 s** before its 503. It had taken a pooled
  connection that was already open to the frozen server, so it waited out the
  30 s command timeout plus the cancellation that follows. A second request
  followed (18 s), so this was 2 requests, not a storm.
- After unpause, Try again loaded the list in the same document.
- Phase 35: bound `Timeout`, `Command Timeout` and `Cancellation Timeout` in the
  connection string (§9).

### 7.3 Redis stopped under a public website chat

Visitor in its own browser context, conversation handled by a person:

- **Before:** send → stored ("A support team member will reply here").
- **Redis stopped:** send refused with 503 after the client's backlog timeout,
  and nothing was stored. The visitor was told the message had been received,
  which was false; fixed in §5.6. After the fix the copy is truthful and the
  text stays in the box.
- **Redis started:** the kept text sent normally, with no API restart, and the
  transcript holds exactly one copy.
- **Protection working as designed:** the first AI-mode send of the session was
  refused by workspace B's generation budget, which earlier seeding had used up,
  with a clear "please wait" message.

## 8. Findings, classified

| Class | Finding | Evidence | Status |
| --- | --- | --- | --- |
| **BLOCKER (Phase 35 configuration)** | Default Npgsql pools (100 each, two per instance) exceed PostgreSQL's default 100 connections for even one instance. Under load: `53300` connection exhaustion, ledger settlements refused, pool starvation with bounded pools (no PostgreSQL deadlock) | §4.1d, §5.1, §4.3 | budget rule in the architecture doc §5; must be configured before V1.0 |
| Fixed | Knowledge search held the request's connection through the AI call | §5.1 | fixed + tests |
| Fixed | Database unavailability answered 500 | §5.2 | fixed + tests |
| Fixed | EF logged every SQL statement at Information by default | §5.3, §4.1c | fixed (appsettings) |
| Fixed | Redis failures escaped as 500 on the protection check and ticket issuance | §5.5 | fixed + tests |
| Fixed | Public chat claimed "message received" when nothing was stored | §5.6 | fixed + specs |
| IMPORTANT | Operational email: a crash turns claimed-but-unattempted emails into `Unknown` (up to batch − 1 per crashed worker lost, never duplicated) | §6.6 C | recommended before V1.0; outbox state change |
| IMPORTANT | Billing events: no claim lease, so a second instance re-claims in-flight events (duplicate concurrent processing and attempts consumed; no state duplication found). The claim also uses the unbounded `IN (… LIMIT …)` form | §6.9 | Phase 35 (needs a model change and migration) |
| IMPORTANT | No reconciler for conversation turns left `Generating` by a crash (one stale row per crashed turn; same-id resend 409 forever) | §6.7 | recommended |
| IMPORTANT | An unresponsive (paused) database holds requests up to ~60 s (command timeout + cancel) and ~15 s on connect | §6.1, §7.2 | Phase 35 connection-string timeouts |
| IMPORTANT | Customers list cost grows linearly with the workspace's customers (~3.6 µs each; ~360 ms per page projected at 100k) | §4.1 | needs a stored last-activity value (model change); not done |
| IMPORTANT | Concurrent duplicate index requests for one document version each pay for an embedding batch | §4.4 | a Started-attempt guard would stop it |
| OPTIMIZATION | Per-workspace quota reservation caps one workspace at ~90 provider operations/s here (lock held for count + insert) | §4.3 | ~180× above the default generation budget; leave |
| OPTIMIZATION | Analytics dashboard runs ~5 aggregations per request whose cost grows with messages in range | §6.11 | re-measure with realistic history before indexing |
| OPTIMIZATION | ~16 statements per public message, incl. ~4 `DISCARD ALL` pool resets | §4.1d | consider `No Reset On Close` only with care; not needed at measured rates |
| ENVIRONMENT | Local connection string uses the `postgres` superuser, which consumes the reserved slots | §4.1d | production must use a role |
| ENVIRONMENT | Admin reads are bounded by a 2-CPU PostgreSQL container from 5 VUs | §4.1 | laptop limit |
| ENVIRONMENT | In-process capacity numbers share the test container's CPUs between host and load generator | §4.3 | stated with each table |

No correctness blocker was found in the product: across every overload, crash
and outage, no tenant data leaked, nothing was duplicated, no paid or ambiguous
operation was retried, no public write failed open, and durable state was never
corrupted.

## 9. Local capacity envelopes (NOT a production SLA)

Environment §1 (4-core/8-thread laptop; API 2 CPU / 2 GiB; PostgreSQL 2 CPU /
2 GiB; synthetic data §2). Envelopes sit below the first degraded stage.

| Scenario | Last healthy | First degraded | Root bottleneck | Stay below (local) |
| --- | --- | --- | --- | --- |
| Admin reads (mix, one workspace, 7k customers) | 1 VU (~44 req/s) | 5 VUs (p95 ×3) | PostgreSQL CPU (dashboard aggregations, customers list) | ~3 concurrent readers per 2 PostgreSQL CPUs |
| Admin writes (reversible, If-Match) | 10 VUs (~168 req/s, p95 143 ms) | 25 VUs (plateau ~276 req/s) | API CPU, then PostgreSQL | ~10 concurrent writers |
| Public ingest (human-handled, bounded pools 40) | 100 visitors (~37 msg/s, p95 330 ms) | 200 visitors (p95 ×2) | database round trips per message (~16 statements) | ~100 active visitors / ~35 msg/s per instance |
| Public ingest, default pools | 50 visitors | 100–200 visitors (503 `53300`) | connection budget | configure pools first |
| AI turns, multi-workspace (in-process, 200 ms provider, pools 40, EF Warning) | 25 in flight (~97 turns/s) | 50 in flight (p95 ×2) | lock pool = 40 generations in flight | ≤ 25–30 in flight per instance |
| AI turns, one workspace | 25 in flight (~87 turns/s) | 50 in flight | per-workspace quota reservation (~90 ops/s) | far above the default 30 / min budget |
| SignalR workspace connections (two instances) | 1,000 (no degradation) | not reached | — | ≥ 1,000 tested; not pushed further |
| Report backlog | 60 queued → drained in 33 s | not reached | 3 s poll × batch 4 × instances | — |

## 10. Correctness invariants re-proved

Tenant isolation (instructions, ledger, inboxes, reports, notifications,
SignalR, RAG retrieval); same-conversation advisory-lock serialisation with the
real lock; different conversations concurrent; global ingestion order and reply
adjacency; `LastActivityAt` high-water mark; `clientMessageId` idempotency;
report `clientRequestId` idempotency; ledger Started-before-dispatch, no retry,
`Unknown` for timeouts and for the reconciler, nothing fabricated, quota
counting unchanged; exactly-once usage thresholds under concurrency; Redis
limiter atomic across instances and fail-closed; report claims and tokens
(no duplicate execution or artifact across stop, kill and lease expiry); email
and WhatsApp ambiguity (`Unknown`, never resent); notification fan-out exactly
once per eligible recipient; Data Protection shared across instances and
restarts; no live provider traffic.

## 11. Phase 35 handoff (only items with evidence behind them)

**Must configure before V1.0 (blocker):**

1. **Connection budget.** Set `Maximum Pool Size` in
   `ConnectionStrings__OmniAiDatabase` so that
   `instances × (2 × MaxPool + 2) + tooling < max_connections − reserved`.
   Example: 2 instances, `max_connections = 100` → `Maximum Pool Size ≤ 22`.
   Evidence §4.1d, §4.3, §4.1e. Connect as a **non-superuser** role.

**Configure (important):**

2. **Timeouts** in the same connection string, so an unresponsive database
   fails requests in seconds rather than ~60 s: `Timeout` (connect, default 15),
   `Command Timeout` (default 30) and `Cancellation Timeout`. Evidence §6.1, §7.2.
3. **Stop grace 30 s** (`stop_grace_period: 30s`) for HTTP draining. Workers stop
   at once, and leases or reconcilers recover their items (§6.3).
4. **Health routing.** Readiness on `/health/ready` (fails in 3 s with the
   database paused, immediately when refused, and on Redis loss); liveness on
   `/health/live` (never failed in any drill). Do not restart on readiness
   failure (§6.1–6.2).
5. **Redis is critical** for public chat, the realtime backplane and tickets.
   Its loss is fail-closed (503) for public writes, while Admin keeps working.
   Monitor it as a paging dependency (§6.2).
6. **SMTP timeout.** A hanging relay drains email one timeout at a time
   (~2 per minute per worker at 30 s). Choose `Email:Smtp:Timeout` with that in
   mind, and alert on `Unknown` email outcomes (§6.6).
7. **Logging.** Keep `Microsoft.EntityFrameworkCore` at `Warning` (now the
   shipped default). Watch the 503 log lines, which name the SQLSTATE (§5.2–5.3).
8. **Report storage.** Measured artifacts are CSV ≤ 1 MB, XLSX ≤ 0.5 MB and
   PDF ~0.75 MB per report at this data volume, with a 30-day retention.
   Size the volume from expected reports × 30 days. A read-only or failed volume
   yields truthful `Failed` reports and no path leak (§4.1f, §6.5).
9. **Alerts worth defining.** Connections above 80 % of the budget; any 503 with
   `SQLSTATE 53300`; readiness flapping; reconciler settlements greater than 0;
   email or WhatsApp `Unknown` rate; report queue age above ~1 minute.

**Recommended fixes, owner to decide (important):**

10. Email outbox: move rows to `Sending` one by one, immediately before dispatch
    (§6.6).
11. Billing events: add a claim lease or timestamp, and the bounded `MATERIALIZED`
    claim form (§6.9).
12. A reconciler for conversation turns left `Generating` (§6.7).
13. Customers list ordering: store a last-activity value on the customer (§4.1).
14. Knowledge: refuse a second index of the same version while an attempt is
    `Started` (§4.4).

## 12. Quality gates (final runs, after every fix)

| Gate | Result |
| --- | --- |
| `dotnet restore OmniAI.slnx` | up to date |
| `dotnet build OmniAI.slnx -c Release` | **0 warnings, 0 errors** |
| `dotnet test OmniAI.slnx -c Release` | **3,394 passed, 0 failed, 2 skipped**: Domain 797, Application 714, API integration 883 (+2 skipped: the opt-in capacity and crash-boundary scenarios), Infrastructure integration 1,000. PostgreSQL 17 + pgvector and Redis in Docker |
| `dotnet list package --vulnerable --include-transitive` | no vulnerable packages in any of the 8 projects |
| `npm ci` | clean (allow-scripts notices only) |
| `npm run build` | succeeds; initial transfer **108.18 kB** (Phase 33.5: 108.22 kB); the one pre-existing budget warning (`embed-chat.scss` 4.07 kB vs 4.00 kB) |
| `npm run test:ci` | **63 files, 784 tests passed** |
| `npm run format:check` | all files formatted |
| `npm audit` | **0 vulnerabilities**; `npm audit fix` was not run |
| lint | there is no `lint` script in `frontend/package.json` |

New tests this phase: `ConversationConcurrencyLoadTests` (7),
`KnowledgeLoadTests` (3), `DatabaseUnavailabilityTests` (13),
`ProtectionStoreFailureTests` (7), a realtime-ticket outage test, two
`KnowledgeSearchStoreTests`, and three chat specs. The opt-in capacity and crash
scenarios are skipped by default.

**Test-suite connection pressure (the Phase 33 observation).** During the full
run, the parallel suites' disposable databases peaked at **80 server connections**,
with 0 refused, while no API was connected. The earlier near-miss came from test
parallelism plus a connected development API, not from a product leak. The load
scenarios now run in a non-parallel collection (§5.7).

## 13. Cleanup

- Removed: Phase 34 API, stub, mail-sink, Redis, SMTP-stub and test containers;
  the report-artifact volume; the drill's test database; about 442 MB of raw
  results (k6 CSV/JSON, samples, logs, sample artifacts, the dumps used only for
  the credential check); the synthetic billing rows used in §6.9.
- Kept for review, stopped: the `omniai_p34_pg` container, its
  `omniai_p34_pgdata` volume (synthetic database `omniai_p34`) and the
  `omniai_p34` network.
- Restored: the Phase 33.5 stack, which was stopped for the duration of the
  measurements.
- No Chrome profile, screenshot, dump or result file is in the repository.

## 14. Migration, packages, external traffic

- **Migrations: none added.** The database still has exactly
  `20260925225105_Initial`. No schema change was justified: the one slow query
  pattern (customers list) is not fixable by an index, and the analytics scans
  are correct for the data.
- **Packages added: none** (backend or frontend). k6 ran from a pinned container
  image and was never added as a dependency; the SignalR load client is
  dependency-free.
- **Live traffic: none.** Email NO, Meta NO, OpenAI NO, Stripe NO.
  `OMNIAI_LIVE_OPENAI`, `OMNIAI_LIVE_META` and `OMNIAI_LIVE_EMAIL` were never set.

## 15. Limitations

- All numbers come from one laptop, with Docker Desktop's port proxy and
  virtualised disk. They are not a production SLA.
- AI and embedding load ran in-process only: the composition root has no local
  provider, and none was added. Those tables include the test host's own CPU.
- There was no real load balancer or TLS; the rolling restart is two containers,
  with the client choosing the surviving instance.
- Synthetic data is young (every message created the same day), so range
  filters select most rows. Long-history query behaviour is not measured.
- The soak lasted 30 minutes, so nothing is claimed about memory beyond that.
- The billing claim race was shown with the product's exact SQL on synthetic
  rows, not end to end: the fresh database has no billing offers.
- The orphan-file window (finalised but not committed) was not hit by a kill.
  Its sweep remains covered by the Phase 33 tests.
