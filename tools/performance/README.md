# Performance, load and failure-drill tooling (Phase 34)

**Non-production tooling.** Everything here generates load or injects failures.
It is for a developer's local stack only.

## Safety rules

- Every tool targets **loopback only**. The allow-list is hard-coded
  (`lib/target.mjs` for Node, `k6/lib.js` for k6). A non-loopback host is refused
  before the first request. Widening it is a code change that needs review.
- Accounts are synthetic, on the reserved `omniai.example` domain. The default
  password is a synthetic value shared with `seed/seed.mjs`. Override it with
  `SEED_PASSWORD` / `-e PASSWORD=`.
- Cookies, antiforgery tokens and public-chat credentials stay in memory. They
  are never printed or written to disk.
- Drills that need a secret, such as `meta-ambiguity.mjs`, read the local
  synthetic value from the environment and never print it.
- No live provider is contacted. AI and embeddings exist only in the in-process
  scenarios, over deterministic sockets. Meta, Stripe and SMTP are local stubs
  or sinks. Never set `OMNIAI_LIVE_OPENAI`, `OMNIAI_LIVE_META` or
  `OMNIAI_LIVE_EMAIL` for these runs.
- Write results **outside the repository** (`OMNIAI_PERF_RESULTS`). Never commit
  raw k6 output, samples, generated reports or dumps.

## Contents

| Path | What it does |
| --- | --- |
| `seed/seed.mjs` | Synthetic data through the product's own APIs: workspaces, members (real invitations), agent, website chat, knowledge documents, thousands of conversations from distinct simulated visitors, customer display names |
| `k6/admin-reads.js` | Weighted authenticated Admin reads; one session per VU. The latency trend excludes sign-in |
| `k6/admin-writes.js` | Reversible writes with antiforgery and `If-Match`. A 412 from a lost race is expected |
| `k6/public-webchat.js` | Public website-chat ingest: one visitor per VU, its own address, credential and antiforgery token, paced below the per-client budget |
| `run-stages.sh` | Closed-loop stages with a warm-up. k6 runs in the API's network namespace with its own CPU limit. Each stage is sampled and summarised |
| `sample-resources.sh` / `summarise.mjs` | Container CPU/memory, PostgreSQL connections, lock waits and longest statement, Redis clients/memory/ops. Aggregates only |
| `endpoint-profile.mjs` | Serial latency per Admin endpoint, to attribute a knee to a query |
| `limiter-contention.mjs` | Public-chat protection boundaries hit concurrently across one or two instances |
| `report-load.mjs` / `report-drill.mjs` | Report idempotency, backlog, hashes, content checks, cross-tenant probe; submit and status helpers for drills |
| `fanout.mjs` | One operational alert to many members: in-app, SignalR and email accounting |
| `signalr-load.mjs` / `lib/signalr.mjs` | Dependency-free SignalR JSON-protocol client; N authenticated hub connections with per-connection event counts |
| `rolling-restart.mjs` | Session, antiforgery and hub failover across two instances while one restarts |
| `probe.mjs` | Timeline of liveness, readiness, an authenticated read and a protected public write during failure drills, with a leak check on every failure body |
| `smtp-stub.mjs` | Loopback SMTP endpoint that accepts, hangs after DATA, or answers slowly |
| `meta-ambiguity.mjs` | WhatsApp replies at once against a Meta stub that never answers |

The in-process scenarios live in
`backend/tests/OmniAI.Api.IntegrationTests/Performance/`:

- `ConversationConcurrencyLoadTests` and `KnowledgeLoadTests` are part of every test run.
- `ConversationCapacityStageTests` runs only with `OMNIAI_PERF_CAPACITY=1`.
- `CrashBoundaryHold` runs only with `OMNIAI_PERF_CRASH=1`, and exists to be killed.

## Typical use

```bash
export OMNIAI_PERF_RESULTS=/some/scratch/dir        # outside the repository
OMNIAI_PERF_BASE=http://127.0.0.1:5034 node seed/seed.mjs "$OMNIAI_PERF_RESULTS/seed.json"
bash run-stages.sh admin-reads.js "1 5 10 25 50" 30 15
OMNIAI_PERF_BASE=http://127.0.0.1:5034 node probe.mjs 120 500   # while you pause a dependency
```

A seed of thousands of conversations needs
`PublicChatProtection:ConversationStartsPerEndpoint` raised on the local API
while seeding only (default 60 per 5 minutes). Take measurements after restarting
with the defaults.

See `docs/architecture/performance-failure-recovery.md` for the method and
`docs/phase-reports/PHASE_34_PERFORMANCE_LOAD_FAILURE_RECOVERY_REPORT.md` for the
measured results.
