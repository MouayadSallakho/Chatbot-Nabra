# Security, reliability and observability hardening

Phase 33. Hardening only: no feature was added. This document is the design
reference for what the platform guarantees when a process restarts, a worker
dies mid-item, a dependency is slow, or a tab outlives the server that served it.

## 1. Principles

- **Truthful over optimistic.** A record says what is known. Work whose outcome
  cannot be known becomes `Unknown`, never `Failed` or `Succeeded` by guess.
- **No automatic retry of paid or ambiguous work.** Nothing here re-sends a
  provider request, an email or a WhatsApp message whose first attempt may have
  landed.
- **Recovery never weakens a control.** CSRF, authentication and authorisation are
  unchanged; recovery paths go through them.
- **Built-in facilities first.** ASP.NET Core Data Protection, Health Checks,
  `System.Diagnostics.Metrics`, `Activity`. No package was added.

## 2. Antiforgery and sessions across restarts

### 2.1 The defect (reproduced in Chrome against `1f67259`)

The Data Protection key ring lived in the process's filesystem
(`/root/.aspnet/DataProtection-Keys`). A new container had a new ring, so:

- the antiforgery token an open tab held could no longer be decrypted, and its
  first sign-in answered `400 Antiforgery validation failed`
  (log: *"The antiforgery token could not be decrypted"*);
- every session cookie was silently invalidated by every deploy.

A second, independent cause: the token is bound to the identity it was issued
for, so a tab whose session expired while it stayed open also held a token that
could no longer validate.

### 2.2 The fix

| Layer | Change |
| --- | --- |
| Key ring | `DataProtectionSetup`: `SetApplicationName("OmniAI")`, keys persisted in `data_protection_keys` through `PostgresXmlRepository`. Every instance and every restart shares one ring. |
| Key encryption | When `OMNIAI_SECRET_MASTER_KEY` is configured, each key's XML is encrypted with AES-GCM under it (`MasterKeyXmlEncryptor`, context `dataprotection.key-ring`). The table holds `<omniaiEncryptedKey>` only; a database dump alone cannot forge sessions or tokens. Without a master key (a Development machine using the ephemeral key) keys are stored unencrypted, because encrypting under a per-process key would make the ring unreadable after restart. |
| Refusal contract | `AntiforgeryEndpointFilter` adds `code: "antiforgery.invalid"` to its 400 ProblemDetails. The reason stays in the log (event 1100) and never reaches the body. |
| Client | `AuthService.login` / `register` fetch a fresh token (safe GET) immediately before posting. `sessionExpiryInterceptor` recognises `antiforgery.invalid`, starts one de-duplicated renewal, and rethrows without replaying the request; the status keeps its meaning and nobody is signed out. `toApiError` explains the refusal as "nothing was changed, try again" rather than "check the highlighted fields". |

What did **not** change: every unsafe request still needs a valid token bound to
its session; there is no exemption, no Origin-only trust, no stale-token
acceptance, and a token from one browser is still refused in another after a
restart (`RestartRecoveryTests`).

## 3. Provider operations left Started

A process that dies between committing the ledger row and settling it leaves an
`ai_provider_usages` row `Started` forever.

- `ProviderUsageReconciler` settles rows `Started` for longer than
  `Reliability:ProviderOperationStaleAfter` (default 15 min) as `Unknown`, with
  `completed_at = now`. No token count, cost or model is written; the provider is
  never called.
- Startup refuses a threshold below **2 × provider timeout + 5 min** (OpenAI and
  embedding clients both have `maxRetries: 0`, so one operation lasts at most one
  timeout; default 30 s).
- Quota is unchanged: Phase 24 counts rows whatever their status.
- **Monotonic.** The ledger's own settlement now uses PostgreSQL's `xmin` as a
  concurrency token — Npgsql's documented `uint` + `IsRowVersion()` mapping. EF's
  differ emits a column operation for it like any mapped property; Npgsql's
  migrations SQL generator emits no DDL for a system column, so no physical
  column exists and none is ever attempted (asserted from the model itself). A finaliser that loaded a row before the
  reconciler settled it fails its update (`DbUpdateConcurrencyException`,
  swallowed) instead of overwriting the recorded outcome.
- Multi-instance safe: candidates are chosen in a `MATERIALIZED` CTE with
  `FOR UPDATE SKIP LOCKED`, bounded by `BatchSize`, and the update repeats
  `status = 'Started'`. The shorter `WHERE id IN (SELECT … LIMIT n FOR UPDATE SKIP
  LOCKED)` form was found **not** to be bounded (PostgreSQL may rescan it; a test
  settled 5 rows with a batch of 2) and is not used.
- Served by the partial index `ix_ai_provider_usages_open (started_at) WHERE
  status = 'Started'`.

## 4. Knowledge indexing attempts

Phase 32 alerted on failed indexing best-effort outside any transaction; a crash
lost the alert and left no record. Phase 33 makes the attempt durable.

```
unchanged document ──► no attempt, no provider call (zero cost, unchanged)
changed document ──► chunks computed ──► attempt Started (committed)
    └─► embed batches ─► MarkIndexed + ReplaceIndex ─► Succeeded
          │ explicit failure ─► Failed + code  (+ alert, same statement)
          │ cancelled        ─► Unknown / cancelled
          └ process dies     ─► reconciler, after KnowledgeAttemptStaleAfter:
                                  index written since start? ─► Succeeded (no alert)
                                  otherwise                  ─► Unknown / interrupted (+ one alert)
```

| Failure | Status / code | Alert |
| --- | --- | --- |
| Embedding unavailable / timeout / invalid response | Failed / `provider_unavailable`, `provider_timeout`, `provider_response_invalid` | yes |
| Plan quota exhausted | Failed / `quota_exhausted` | no (expected refusal) |
| Document changed meanwhile | Failed / `document_changed` | no |
| Request cancelled | Unknown / `cancelled` | no |
| Anything else | Failed / `indexing_error` | yes |
| Abandoned (reconciler) | Unknown / `interrupted` | yes, once |

- Only fixed codes are stored — never content, exception text, secrets or stack
  traces (tested with a canary exception message).
- The alert is the existing `KnowledgeIndexingFailed` occurrence, keyed
  `{documentId}:{attemptId}`, inserted by the same CTE that settles the attempt,
  so the failure and its alert cannot be separated; `ON CONFLICT DO NOTHING` plus
  the conditional `status = 'Started'` make it exactly once whoever settles.
- A failure to record the outcome never hides the real error; the row stays
  `Started` and the reconciler settles it.
- Nothing retries the embedding. A member re-indexes through the normal screen.
- Threshold `Reliability:KnowledgeAttemptStaleAfter` (default 30 min), refused
  below `ceil(MaxChunks / MaxBatchSize) × embedding timeout + 5 min`
  (≈ 6.5 min + 5 at defaults).
- Schema: `knowledge_index_attempts` with a status check, a
  `(status = 'Started') = (completed_at IS NULL)` check, a composite FK to
  `knowledge_documents (id, tenant_id)` (an attempt can never name another
  workspace's document), a partial index on open attempts, and
  `(document_id, tenant_id)` serving the FK.

## 5. Report artifacts

- **Orphans.** A worker that finalizes a file and dies before committing its row
  leaves bytes nothing references. `ReportRetention.SweepOrphansAsync` removes a
  file only if all hold: its name is the store's own key shape (`^[0-9a-f]{32}$`),
  it is inside the store's `artifacts` directory and not a symbolic link, it was
  last written more than `OrphanGrace` (6 h) ago, and no `report_artifacts` row
  references it at the moment of deciding. Unfinished writes (`incoming/`) are
  never candidates; they have their own 1-day sweep. Keyset-paged, idempotent.
- **Missing files.** Storage lost or replaced outside the platform leaves rows
  advertising a Download that can only fail. `ReconcileMissingAsync` marks such
  executions `Expired` (the existing "no longer available" state) and removes the
  row; the download handler does the same the moment it meets a missing file.
  Nothing is regenerated.

## 6. Background workers

### 6.1 Retry and ambiguity matrix

| Work | Attempts | Ambiguous outcome | Abandoned (process died) |
| --- | --- | --- | --- |
| OpenAI text generation | 1 (SDK `maxRetries: 0`, 30 s timeout) | timeout → ledger Unknown | Started → Unknown (§3) |
| OpenAI embeddings | 1 per batch, same client policy | timeout → attempt Failed/`provider_timeout` | attempt reconciled (§4); ledger rows (§3) |
| WhatsApp delivery (Meta) | ≤ 5, only when the failure proves nothing was sent; 30 s timeout; no transport retry handler | → Unknown, never resent | Sending → Unknown (sweep) |
| WhatsApp inbound processing | ≤ 5, idempotent by provider message id | n/a (internal) | claim expires (5 min) → back to queue |
| Stripe webhook events | ≤ 8 with back-off, idempotent by event id | provider state re-read, never assumed | claim expires → retried |
| Stripe API calls | Stripe.net library default network retries, every POST idempotency-keyed | the key makes a retry the same request | checkout intent stays Pending; a live subscription refuses a second purchase |
| Report generation | ≤ 3, claim lease | n/a (internal, no provider) | lease expires → reclaimed; orphan file swept (§5) |
| Notification dispatch | ≤ 5, handed back on failure | n/a (internal) | claim expires → re-dispatched; per-recipient uniqueness prevents duplicates |
| Operational email (SMTP) | ≤ 3, only before DATA is accepted; 30 s timeout | after DATA → Unknown, never resent | Sending → Unknown (sweep) |
| Stale-work reconciliation | every minute, bounded batches | never calls a provider | idempotent; SKIP LOCKED |

No Polly or retry framework was added: every retry above is a decision against a
durable row, which a transparent handler cannot make.

### 6.2 Isolation and shutdown

- Each pass is wrapped; one failing item or reconciler never stops the worker or
  its neighbour. Full batches loop immediately; otherwise the worker sleeps its
  interval through `ReportWorkerDelay` (`TimeProvider`-driven, cancellable).
- Every worker observes the host's stopping token. A `docker stop` was verified to
  exit with code 0. Work interrupted by shutdown is settled by the paths in the
  matrix, never left ambiguous for ever.
- **Deployment note:** the .NET host allows 30 s to stop; Docker's default grace is
  10 s. Set `stop_grace_period: 30s` (or `--stop-timeout 30`) so in-flight items
  finish rather than being killed.

## 7. Configuration validation

Startup refuses unsafe configuration rather than running with it: stale
thresholds inside the real request bounds (`ReliabilityOptions.Validate`), plus
the existing validations for WhatsApp transport, email, billing, reports and
notifications. The Development live-traffic guards are unchanged and re-proved by
their suites: `OMNIAI_LIVE_OPENAI`, `OMNIAI_LIVE_META` and `OMNIAI_LIVE_EMAIL` must
be exactly `1` for a Development host to reach a real provider.

## 8. Health probes

| Path | Checks | Answers |
| --- | --- | --- |
| `/health` (Phase 1), `/health/live` | nothing — the process answering is the answer | `200 {"status":"Healthy"}` |
| `/health/ready` | PostgreSQL `SELECT 1`; Redis `PING` only when Redis is configured; 3 s timeout each | `200 {"status":"Healthy"}` / `503 {"status":"Unhealthy"}` |

**Bounded for real.** A health-check timeout only cancels a token. Against a
database that accepts TCP but does not answer (verified by pausing the container
in Chrome), readiness first took 15 s (Npgsql's connect timeout), then 9 s
(Npgsql answers cancellation by opening a second connection to send a cancel
request). The database check therefore uses its own data source (`Timeout=3`,
`CommandTimeout=3`, `CancellationTimeout=-1`, pool ≤ 2, `ApplicationName =
omniai-readiness`) and stops waiting at its budget while the query ends on its
own bounded timeout: **3.05 s** measured, liveness unaffected, healthy again the
moment the database returns.

Never OpenAI, Meta, Stripe or SMTP: a third party's outage is a contained refusal
on the feature that needs it, not a reason to pull the instance. Bodies carry one
word — no check names, exception text, durations or hosts — and `no-store`.
Probes are anonymous.

## 9. Security headers, CORS, cookies, proxy

`SecurityHeadersMiddleware` fills in (never overrides):

| Route | Headers |
| --- | --- |
| everything | `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`, `Permissions-Policy: camera=(), microphone=(), geolocation=(), payment=(), usb=()` |
| private API, hubs, probes | also `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'`, `X-Frame-Options: DENY` |
| `/widget/v1.js`, `/api/public/*`, `/hubs/public-chat` | no framing policy (Phase 18 embed; verified in Chrome from a third-party origin) |

HSTS (built-in `UseHsts`) outside Development, only on real HTTPS requests.

Reviewed and unchanged: no CORS policy exists, so no cross-origin read is granted
to anybody (tested); session cookie `HttpOnly`, `SameSite=Lax`, `Secure` outside
Development; forwarded headers trusted only from configured proxies, scheme pass
before address pass; non-HTTPS refused outside Development.

## 10. Errors and correlation

- ProblemDetails bodies carry `traceId` (the request's W3C trace id). Error details
  follow Phase 11/13 rules: only authored messages, never exception text.
- Logging states `ActivityTrackingOptions = TraceId | SpanId | ParentId`, and the
  console formatter includes scopes, so every log line carries the trace id of the
  request that wrote it.
- Background work items start their own `Activity` (`OmniAiTracing.StartWork`,
  fixed names such as `omniai.worker.report-generation`), so all of one item's log
  lines — including its provider calls — share one trace id.
- Log audit: all 36+ structured messages carry only internal ids, counts, statuses
  and fixed words; none carries an email address, phone number, message text,
  token, key or secret.

## 11. Metrics

A static `Meter("OmniAI")`; nothing is exported until a deployment attaches a
listener (`dotnet-counters monitor --counters OmniAI` works today).

| Instrument | Tags |
| --- | --- |
| `omniai.worker.items` | `worker`, `outcome` |
| `omniai.reconciliation.settled` | `kind` (`provider_usage`, `knowledge_attempt`, `report_orphan`, `report_missing_file`) |
| `omniai.report.generation.duration` (ms) | `outcome` |
| `omniai.provider.operations` | `status` |
| `omniai.notification.emails` | `status` |
| `omniai.knowledge.index.attempts` | `status` |

Every tag value is a fixed word from code. No tenant, user, customer, address or
provider id is ever a dimension.

## 12. Realtime recovery

- SignalR's default reconnect gives up after ~42 s; a deploy routinely takes
  longer, which left the inbox and notification bell permanently not live.
  `ADMIN_RECONNECT_POLICY`: 0, 2, 5, 10, 20 s, then every 30 s, giving up after
  15 minutes (then stale-but-correct until reload). Verified in Chrome across an
  81 s outage.
- On reconnect, conversation listeners are now told `null` ("anything may have
  changed") and re-read through the API; pushes sent while disconnected are never
  replayed. The notification connection already refetched.
- **Consolidation (debt E) — decided against.** The inbox and notification
  connections share the hub but not a lifecycle: the notification connection lives
  with the shell, the conversation one only while an inbox screen is open, and each
  re-subscribes its own group. Merging them would couple those lifecycles and the
  subscription bookkeeping for the saving of one socket per tab on inbox screens.
  Not clearly lower-risk, so unchanged.
- The public customer chat keeps its own ticket-based connection unchanged
  (tickets expire, and its recovery is a Phase 18 contract).

## 13. Accessible scroll regions

`ScrollRegion` (`[appScrollRegion]="label"`) is the shared pattern for any
`overflow-x` container (tables, the widget install snippet). While — and only
while — the content overflows, the container is `tabindex="0"`, `role="region"`
and named, so it can be reached and scrolled with the keyboard (WCAG 2.1.1, axe
`scrollable-region-focusable`) and is visibly focused by the global
`:focus-visible` ring. When the content fits it adds no tab stop and no landmark.
Measured with `ResizeObserver`, so rotation and late rows are noticed.

## 14. Database review

Phase 33 added exactly what its queries need: `knowledge_index_attempts` (with
checks, composite FK and two indexes), `data_protection_keys`, and the partial
index `ix_ai_provider_usages_open`. The `xmin` token on `ai_provider_usages` is a
model change only. No other index was added: none was indicated by a query this
phase introduced, and Phase 22's rule stands — an index is added when a
measurement asks for it.

## 15. Retention

| Data | Retention |
| --- | --- |
| Report files | 30 days (Phase 31); orphans after 6 h; unfinished writes after 1 day |
| Notifications | 90 days (Phase 32) |
| Knowledge indexing attempts | kept with their document (cascade on delete); small, one row per paid attempt |
| Provider usage | kept (financial evidence, Phase 23) |
| Data Protection keys | managed by the framework (90-day key lifetime; old keys kept to decrypt what they issued) |

## 16. Known limits

- The whole SPA runs its startup `/api/auth/me` probe even on the anonymous embed
  route, so a customer's devtools show one harmless 401 from the iframe
  (pre-existing).
- Without `OMNIAI_SECRET_MASTER_KEY` (Development only) the persisted key ring is
  unencrypted.
- The framework logs an Error with stack when a cookie token cannot be decrypted;
  after key-ring persistence this happens only on genuine key loss.
