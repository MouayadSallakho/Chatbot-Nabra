# Performance, load, failure and recovery

Phase 34. This document holds the principles for load-testing and operating the
platform. The numbers from one laptop live in the phase report, not here.

## 1. Principles

- **Correctness before throughput.** A scenario passes when the invariants hold
  under contention, not when a number is large. Throughput is recorded as
  evidence about one environment.
- **No performance marketing.** Results are always tied to hardware,
  configuration, scenario, synthetic provider latency and data volume. Write
  "on environment E, scenario X stayed healthy through Y and first degraded at Z".
  Never write "supports N users".
- **Measure the product, not the harness.** Record the load generator's CPU
  separately. Give the system under test its own container limits. Exclude
  harness-only costs (sign-in, setup) from measured latency.
- **Never weaken a control to measure.** Antiforgery, authentication, tenant
  authorisation, optimistic concurrency and constraints stay on. Public-chat
  protection stays at its defaults, with two exceptions. A setup-only limit (for
  example conversation starts, to open enough conversations) may be raised for
  that run and stated in the results. A pipeline capacity test may raise limits
  inside an isolated test fixture only, and says so.
- **No live providers.** AI, embeddings, Meta, Stripe and SMTP are exercised
  through deterministic sockets, stubs or sinks. A deterministic AI or embedding
  socket goes *under* the real adapter, so the ledger, quota and attempt records
  still run.

## 2. Workload taxonomy

Report each class separately, never as one average:

| Class | Meaning |
| --- | --- |
| Baseline | 1 client, warm |
| Normal concurrency | clearly below the knee |
| High concurrency | approaching the knee |
| Saturation | at or past the knee: queues grow, latency climbs |
| Failure | a dependency deliberately made unavailable |
| Recovery | the time and state after the dependency returns |
| Soak | moderate load, well below the knee, for a sustained interval |

Areas: authenticated reads and writes, public website chat, the limiter,
same-conversation and cross-conversation turns, tenant isolation, provider
ledger and quota, reports, notifications and email, SignalR, Knowledge and RAG.

## 3. Capacity terminology

- **Stage.** A fixed closed-loop concurrency held for a measured interval after a
  warm-up.
- **Knee.** The first stage showing sustained degradation: p95/p99 multiplying,
  CPU at its limit, pool waits or exhaustion, lock queues, growing backlogs,
  timeouts or unexpected 5xx.
- For each major scenario, record the **last healthy stage**, the **first
  degraded stage** and the **root bottleneck**.
- **Local envelope.** A conservative operating range below the measured knee on
  the documented environment. It is not an SLA.
- **Variance.** Repeat the important stages. Report a range, not a single number,
  when runs differ.

## 4. Saturation indicators

| Indicator | What to look at |
| --- | --- |
| CPU | per container (API, PostgreSQL, Redis, load generator) against its limit |
| PostgreSQL connections | total, active, idle, idle in transaction; advisory locks held; backends waiting on `Lock` |
| Transactions | `xact_commit` rate; statements per request; slow statements (temporary `log_min_duration_statement` on a local server only) |
| Pools | Npgsql "pool exhausted" and server `53300` (both now surface as 503, §6) |
| Thread pool | threads and queued work items (the in-process host exposes them) |
| Queues | pending count, oldest age, claim rate, drain time |
| Realtime | open connections, reconnects, events per connection (must equal the changes made) |

## 5. The connection budget

A turn in flight holds a **dedicated conversation-lock connection** for the
whole provider call: it is a session-level advisory lock. It also uses EF
connections briefly for its own reads and writes, and one more for the ledger's
own scope. The lock data source and EF are **separate Npgsql pools** built from
the same connection string, so a `Maximum Pool Size` applies to each of them.

Rule for any deployment:

```
instances × (2 × Maximum Pool Size + 2 readiness)  +  tooling / migrations / admin
    <  PostgreSQL max_connections − superuser_reserved_connections
```

Npgsql's default (100 per pool) against PostgreSQL's default (100) cannot
satisfy this for even one instance. The symptom under load is `53300` on
unrelated requests, or, with a bounded pool, waits up to the pool timeout.
Choose `Maximum Pool Size` explicitly. Generations in flight per instance then
plateau at that size, and further load queues instead of failing. Connect as a
non-superuser role so the reserved slots stay available for operators.

## 6. Failure principles

- **Truthful status.** A dependency the platform cannot reach is a **503**:
  database connection-level unavailability (SQLSTATE class 08, class 53,
  57P01–57P03, pool exhausted, connect timeout or refused), the protection
  store, live updates. Deadlocks, serialization failures, cancelled statements
  and constraint violations are *not* disguised as unavailability. They stay
  visible as errors.
- **Fail closed** for public, abusable writes. With no answer from the
  protection store, nothing is written and no provider is reached.
- **No leaks.** Failure bodies carry a fixed sentence and a `traceId`. Hosts,
  ports, users, SQLSTATEs, paths and stack traces go only to the operator log,
  and only as fixed codes or exception text there.
- **Liveness is not readiness.** Liveness never consults a dependency. Readiness
  checks PostgreSQL and the configured Redis, within a bounded 3 s budget each,
  and never a third-party provider.
- **Recovery without manual repair.** Every drill must end with the system
  serving again once the dependency returns, with no application restart and no
  data fix-up.

## 7. Worker and backlog semantics

| Work | Claim | On a crash | Retry |
| --- | --- | --- | --- |
| Report generation | SKIP LOCKED + claim token + 10 min lease | reclaimed after the lease; the token stops a stale worker from settling | ≤ 3, local file work only |
| Notification dispatch | SKIP LOCKED + lease | re-dispatched; per-recipient uniqueness | ≤ 5 |
| Operational email | the batch is claimed into `Sending` | `Sending` older than the lease → `Unknown`, never resent | ≤ 3, only before DATA is accepted |
| WhatsApp delivery | SKIP LOCKED, one attempt | `Unknown`, never resent | only when the failure proves nothing was sent |
| Billing events | SKIP LOCKED, no lease (Phase 34 finding) | re-claimed on the next poll | ≤ 8, idempotent apply |
| Provider operations / Knowledge attempts | durable Started row before dispatch | reconciler → `Unknown` (and one Knowledge alert) | never |

- Workers honour the host's stopping token immediately. They do not finish the
  current item; the lease or reconciler recovers it. The 30 s stop grace is for
  draining HTTP requests.
- A worker that fails a pass logs one error and waits its normal interval. It
  never retries in a tight loop.
- Drills may age a lease or a Started timestamp with a local SQL update to avoid
  waiting out production intervals. Never add a clock endpoint, and never shorten
  a production threshold.

## 8. External-operation retry policy (unchanged, re-proved)

Nothing automatically re-sends paid or ambiguous work: an AI or embedding
request, an email accepted past DATA, or a WhatsApp send without a definitive
answer. Concurrency must not create a second attempt. The same message retried
concurrently is generated once. N concurrent replies produce N sends, never
more. Duplicate *requests* from people are separate operations and are
accounted as such: for example, several concurrent index requests for one
document version each pay for their own embedding batch.

## 9. Measuring in production safely

- Attach listeners, don't add endpoints. Use the `OmniAI` meter
  (`dotnet-counters monitor --counters OmniAI`) and PostgreSQL's own views
  (`pg_stat_activity`, `pg_stat_database`, `pg_locks`).
- Watch these signals: connections by state against the budget in §5, 503 rate
  by cause (the log line names the SQLSTATE), queue ages, reconciler settlement
  counts, email and WhatsApp `Unknown` rates, and hub connection counts.
- Never enable request-body logging, never run load against production, and
  never use real customer data for a test.
- Keep EF Core at `Warning`. Command logging at `Information` writes every SQL
  statement and measurably throttles lock-bound paths.

## 10. The harness

`tools/performance/README.md` describes the tools. It covers the loopback
allow-list, synthetic identities, seeding, staged k6 runs, the samplers, the
drills and the in-process scenarios under
`backend/tests/OmniAI.Api.IntegrationTests/Performance/`.
