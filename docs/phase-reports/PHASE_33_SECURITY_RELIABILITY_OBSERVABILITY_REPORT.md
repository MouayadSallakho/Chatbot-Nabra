# Phase 33 — Security, reliability and observability hardening

Baseline: `1f67259` (Phase 32, accepted and checkpointed). Working tree left
unstaged and uncommitted for review. Phases 26B–29 remain deferred; Phase 34 not
started. No VPS was accessed and nothing was deployed.

Design reference: [security-reliability-observability.md](../architecture/security-reliability-observability.md).

## 1. Outcome

Hardening only; no feature was added. The six registered debts (A–F) are closed
or decided, and review found and fixed five further defects:

| # | Defect | Found by |
| --- | --- | --- |
| A | First sign-in after an API restart answered 400 (key ring in the container filesystem; every deploy also signed everybody out) | Chrome reproduction on `1f67259` |
| — | Provider settlement could overwrite a reconciled `Unknown` | code review |
| — | `UPDATE … WHERE id IN (SELECT … LIMIT n FOR UPDATE SKIP LOCKED)` settled 5 rows with a batch of 2 | new PostgreSQL test |
| — | SignalR admin connections gave up after ~42 s and the inbox missed changes made while disconnected | review, then Chrome |
| — | `/health/ready` took 15 s to fail against an unresponsive database | Chrome (paused database) |
| — | A billing API test read the checkout intent between two saves (flaky) | full-suite run |

## 2. What changed

| Area | Change |
| --- | --- |
| Antiforgery / sessions | Data Protection key ring persisted in PostgreSQL (`data_protection_keys`), encrypted under `OMNIAI_SECRET_MASTER_KEY`; stable `antiforgery.invalid` refusal code; client renews the token before sign-in/registration and after a refusal, never replaying |
| Provider usage | `ProviderUsageReconciler` (stale Started → Unknown); `xmin` concurrency token (model only) makes settlement monotonic; partial index `ix_ai_provider_usages_open` |
| Knowledge indexing | `KnowledgeIndexAttempt` + `knowledge_index_attempts`; attempt committed before the first embedding request; failure and alert in one statement; `KnowledgeAttemptReconciler`; `INotificationOccurrenceRecorder` removed |
| Reports | Orphan-file sweep (key shape, inside root, no symlinks, > 6 h, unreferenced); missing-file reconciliation to Expired, also on download |
| Workers | `StaleWorkReconciliationWorker`; per-work-item `Activity`; metrics hooks |
| Config | `ReliabilityOptions.Validate` against the real provider/indexing bounds |
| Health | `/health/live`, `/health/ready` (PostgreSQL via a bounded data source; Redis when configured) |
| Headers | `SecurityHeadersMiddleware` (route-aware), HSTS outside Development |
| Correlation | explicit `ActivityTrackingOptions`, console scopes, worker activities; ProblemDetails `traceId` |
| Metrics | `Meter("OmniAI")`, six low-cardinality instruments |
| Realtime | `ADMIN_RECONNECT_POLICY`; conversation listeners re-read on reconnect |
| Accessibility | `ScrollRegion` directive on every table wrapper and the widget install snippet |

Files: 56 modified, 24 added (see `git status --short`).

## 3. Debt register

| Debt | Result |
| --- | --- |
| A — stale antiforgery after restart | Fixed; root cause, fix and Chrome before/after in §6 |
| B — provider rows stuck Started | Fixed; 15 min default, validated ≥ 2 × timeout + 5 min; monotonic; bounded; quota unchanged |
| C — Knowledge failure durability | Fixed; durable attempt, transactional alert, reconciler, zero-cost unchanged path preserved, no retry, fixed codes only |
| D — report artifacts | Fixed; orphan sweep and missing-file reconciliation |
| E — SignalR consolidation | Decided against (architecture doc §12); recovery fixed instead |
| F — table/overflow accessibility | Fixed; shared `ScrollRegion` pattern |

## 4. Tests

### Backend (PostgreSQL 17 + pgvector, Redis, in Docker)

New or changed:

| Suite | Tests |
| --- | --- |
| `StaleWorkReconciliationTests` (Infrastructure) | 15: stale → Unknown with no invented tokens or cost; young left running; settled never moves; batch bound (provider and knowledge); late settlement refused by xmin; quota count unchanged; explicit failure with one alert; expected refusal without alert; abandoned unwritten → Unknown/interrupted + exactly one alert across two passes; abandoned written → Succeeded without alert; inside threshold untouched; settled-without-completion refused by the database; cross-workspace attempt refused by the composite FK; threshold validation |
| `IndexKnowledgeDocumentTests` (Application) | +6: attempt committed before the provider call; unchanged document records no attempt; provider failure → Failed + code + alert; refusal before any call records nothing; unexpected failure stores a fixed code, never exception text (canary); a failure to record never hides the real error |
| `ReportGenerationTests` (Infrastructure) | +2: only an old, unreferenced, key-shaped file is removed (young, referenced, foreign-named and in-progress files kept; idempotent); vanished file → Expired, row removed |
| `MigrationSchemaTests` | 20 migrations; no physical `xmin` on `ai_provider_usages` and the system one is the token; Phase 33 indexes, checks, FK and key table exist |
| `RestartRecoveryTests` (API) | 4, each over two sequential hosts on one database: a token issued before a restart validates after it; a session survives a restart; a token is still bound to its own browser after a restart; the persisted key ring is encrypted |
| `OperationalSurfaceTests` (API) | 14: liveness one word, no-store; readiness healthy; readiness 503 without a database with no reason and no canary in the body; readiness independent of every provider; private-API headers (3 routes); public widget/embed routes get no framing policy (3 routes); no CORS grant; refusal carries `antiforgery.invalid` and `traceId`, never the reason; recovery by fresh token with nothing replayed |
| `BillingLifecycleApiTests` | race fixed: waits for the intent to settle rather than reading between two saves |

Existing suites re-prove the Development live-traffic guards
(`LiveProviderOptInTests`, `MetaLiveProviderOptInTests`, `OperationalEmailTests`),
every antiforgery refusal (`AntiforgeryTests`, `ProductionProxyCsrfTests`), the
hosted/embed boundaries, and the report path-traversal and parser regressions.

Totals (final run): 3,342 passed, 0 failed — see §8.

### Frontend (Vitest)

59 files, 741 tests, all passing. New: token renewal before sign-in, renewal
when the renewal itself fails, one shared renewal for concurrent callers;
interceptor renews once without replay, keeps the session and workspace, renews
once for concurrent refusals, ignores ordinary validation errors; the
antiforgery message; reconnect policy (steps, ceiling, bounded rate, give-up);
`ScrollRegion` (focusable named region only while overflowing, follows a resize).
Sign-in and registration page specs updated for the renewal.

## 5. Restart and recovery verification

| Scenario | How | Result |
| --- | --- | --- |
| A — antiforgery / session | Chrome + `RestartRecoveryTests`; container recreated | token and session survive; key loss → one clean sign-out, first sign-in succeeds |
| B — provider Started | rows left Started, new reconciler instance | Unknown, once, bounded |
| C — Knowledge attempt | attempt left Started, with and without the index written | Succeeded / Unknown + one alert |
| D — report artifacts | orphaned file, vanished file | removed / Expired |
| E — realtime | Chrome: 11 s restart and 81 s outage (`docker stop` → `start`) | both hub connections back (+18 s, +98 s); list and bell refetched |
| Graceful shutdown | `docker stop -t 30` | exit code 0 |
| Database outage | Chrome, `docker pause` on PostgreSQL | live 200 throughout; ready 503 in 3.05 s; ready 200 on return |

## 6. Chrome verification (Windows Chrome, local only)

Driven over the DevTools protocol by dependency-free Node scripts (the Chrome MCP
servers failed to connect this session). Synthetic account
`p33.repro@omniai.example`, synthetic database `omniai_p32`.

1. **Reproduction on `1f67259`** (baseline backend and frontend extracted with
   `git archive`): open sign-in → recreate the API container → sign in once:
   `POST /api/auth/login 400` "Antiforgery validation failed"; server log "The
   antiforgery token could not be decrypted"; key ring warned as stored in
   `/root/.aspnet/DataProtection-Keys`.
2. **Same flow, Phase 33:** `GET /api/auth/csrf 204` → `POST /api/auth/login 200`
   → `/workspace`. Persisted key row holds `<omniaiEncryptedKey>`, no `<masterKey>`.
3. **Stale token on an authenticated mutation** (browser-side token replaced):
   `POST /api/tenants 400` with `code: antiforgery.invalid` → exactly one
   `GET /api/auth/csrf` → no replay → still signed in, message "…Nothing was
   changed. Try again." → the reader's retry `201`.
4. **Key ring deleted + restart:** `me 401` → sign-in screen → first sign-in 200.
5. **Phase 18 embed:** agent + website chat prepared through the app's own API;
   widget loaded on a third-party origin (`127.0.0.1:8099`): iframe
   `localhost:4200/embed/{key}`, `GET /api/public/embed/{key} 200`, launcher
   visible.
6. **Headers** on the live API: private routes carry `DENY` and
   `frame-ancestors 'none'`; `/widget/v1.js` only nosniff/referrer/permissions.
   The probe document's own CSP blocks script-initiated fetches — as intended.
7. **SignalR recovery**: §5 E.
8. **Tables** at 390 / 768 / 1440 / 1920 in light and dark on Reports, Overview
   and Analytics: no page-level horizontal overflow anywhere; the generated
   reports table overflows at 390 and 768 and there is a named region reached by
   Tab, visibly focused (2 px ring) and scrolled by the arrow keys (0 → 80 px); at
   1440 and 1920 it fits and adds no tab stop.
9. **Health**: §5.

## 7. Reviews

- **Retry/ambiguity matrix, workers, shutdown:** architecture doc §6.
- **Configuration validation:** thresholds validated at startup; existing
  validations unchanged.
- **Secret/PII logging:** every structured log template reviewed; ids, counts,
  statuses and fixed words only. Health failures log the check name and elapsed
  time; the connection string never appears (verified in the container log).
- **ProblemDetails:** authored messages only; `traceId` present; antiforgery
  reason withheld; health bodies one word.
- **CORS / cookies / proxy / public endpoints:** no CORS policy (tested);
  cookie flags, forwarded-header trust and transport refusal unchanged and
  covered by existing suites; public surfaces unchanged.
- **Idempotency and I/O in transactions:** reconcilers are single statements; no
  provider, SMTP or file I/O runs inside a database transaction; the attempt row
  commits before the provider call.
- **Provider HTTP clients:** OpenAI/embeddings `maxRetries: 0`, 30 s; Meta 30 s,
  no resilience handler; SMTP 30 s; Stripe library retries idempotency-keyed.
- **Database:** only what Phase 33 queries need (§14 of the architecture doc).
- **Retention:** architecture doc §15.
- **Failure classification:** fixed codes (§4 of the architecture doc).

## 8. Builds, audits and gates

| Gate | Result |
| --- | --- |
| `dotnet restore` | up to date |
| `dotnet build -c Release` | 0 warnings, 0 errors |
| `dotnet test` | see final run below |
| `dotnet list package --vulnerable --include-transitive` | no vulnerable packages in any of the 8 projects |
| `npm ci` | clean (allow-scripts notices only) |
| `npm run build` | succeeds; one pre-existing budget warning (`embed-chat.scss` 4.07 kB vs 4.00 kB, unchanged since `f989b92`) |
| `npm run test:ci` | 59 files, 741 tests passed |
| `npm run format:check` | all files formatted |
| `npm audit` | could not complete: npm's request to the advisory endpoint failed with an empty reason on three attempts although the registry answered `curl`. The same bulk advisory query was then posted directly (`/-/npm/v1/security/advisories/bulk`, payload from `package-lock.json`, 464 packages): **0 advisories**. `npm audit fix` was not run |
| lint | no `lint` script exists in `frontend/package.json` |

Full backend run (isolated, nothing else on the host):
**3,342 passed, 0 failed, 0 skipped** — Domain 797, Application 714, API integration 860, Infrastructure integration 971 (real PostgreSQL 17 + pgvector and Redis). Build 0 warnings, 0 errors.

An earlier full run executed concurrently with `npm ci` and the Angular build
starved the Docker VM (statement timeouts on test-database cleanup; 42 leaked
`omniai_test_*` databases, removed); a second hit PostgreSQL's 100-connection
limit once (`53300`) while the development API was also connected. Neither was a
product defect; both are recorded here rather than omitted.

## 9. Migration and packages

One migration: `20260925214406_AddReliabilityHardening` —
`data_protection_keys`, `knowledge_index_attempts` (checks, composite FK, two
indexes), `ix_ai_provider_usages_open`, and the `xmin` row-version token on
`ai_provider_usages`. **Generated by `dotnet ef migrations add` and not edited.**

*Remediation (review of the first submission).* The first version had its
scaffolded `xmin` operation removed by hand. That was unnecessary and has been
undone: EF's model differ is provider-agnostic and emits a column operation for
every mapped property, but Npgsql's migrations SQL generator recognises `xmin` as
a PostgreSQL system column and emits no DDL for it. Rendered as SQL, the
regenerated migration's Up and Down contain no `xmin` at all; Phases 30–32
already relied on this unedited. The ProviderUsage token now uses Npgsql's
documented mapping (`uint` + `IsRowVersion()`). The migration was regenerated
through `database update` (revert) → `migrations remove` → `migrations add` →
`database update`. Model-level tests derive the from-scratch DDL through EF's own
differ and Npgsql's generator and assert no `xmin` DDL, and a PostgreSQL test
proves version A → reconciled Unknown (version B) → stale settlement refused.

An isolated clean Initial migration (scratch copy, repository untouched) builds
and applies with no `xmin` DDL, but it does **not** reproduce the full schema of
the real migration chain: 35 hand-written `migrationBuilder.Sql` statements in 11
migrations of Phases 12–30 created objects the model does not declare, and a
Phase 30 `AlterColumn` without `oldNullable` left `conversations.customer_id`
nullable in every chain-built database although the model requires it. That is
pre-existing and outside this remediation; see §11.

No historical migration was edited. **No package was added** (backend or frontend).

## 10. External traffic

| | |
| --- | --- |
| Live email | NO (no SMTP server beyond local sinks; none used this phase) |
| Live Meta | NO |
| Live OpenAI | NO |
| Live Stripe | NO (local stub configuration only) |

`OMNIAI_LIVE_OPENAI`, `OMNIAI_LIVE_META` and `OMNIAI_LIVE_EMAIL` were never set.

## 11. Remaining warnings and debt

- **Model/schema drift (pre-existing, blocker for "clean Initial migration").**
  Not declared in the EF model, so a regenerated Initial migration would omit
  them: the `vector` extension; the whole `knowledge_chunks` table with its HNSW
  index, unique `(document, ordinal)`, ordinal check and composite FK; foreign
  keys `fk_knowledge_sources_document`, `fk_conversations_assigned_member`,
  `fk_human_replies_sender_member`, `fk_tenant_memberships_user`,
  `fk_notifications_recipient_user`, `fk_notification_emails_recipient_user`,
  `fk_notification_preferences_user`; checks
  `ck_knowledge_documents_index_complete`, `ck_web_chat_sessions_embedded_origin`;
  partial indexes `ix_conversations_assigned_to`, `ux_whatsapp_contacts_current`;
  the `pk_conversation_human_replies` name. And one real defect:
  `conversations.customer_id` is nullable in chain-built databases but required
  by the model. Needs its own remediation (declare each object in the model, plus
  a migration strategy for existing databases).
- Pre-existing Angular budget warning on `embed-chat.scss` (+72 bytes).
- The embed iframe runs the SPA's `/api/auth/me` probe and logs one harmless 401
  in the customer's devtools (pre-existing).
- Without a master key (Development) the persisted key ring is unencrypted.
- The framework logs an Error with stack for an undecryptable cookie token;
  after persistence this means genuine key loss.
- The public customer chat keeps SignalR's default reconnect (ticket-based,
  Phase 18 contract).
- The local PostgreSQL's default `max_connections = 100` is close to the parallel
  test suites' peak.
- `npm audit` itself could not reach the advisory endpoint from this machine.
- Docker's default 10 s stop grace is shorter than the host's 30 s shutdown.

## 12. Phase 34 handoff scenarios

These are the checks a deployment phase should run against a real environment,
in this order:

1. **Master key present before first boot.** Start with
   `OMNIAI_SECRET_MASTER_KEY` set; confirm `data_protection_keys` rows contain
   `omniaiEncryptedKey` only. Rotating or losing that key invalidates every
   session and token — document its backup with the database backup.
2. **Rolling restart keeps users signed in.** With two instances behind the proxy,
   sign in, restart one instance, then the other; the session and an open tab's
   next mutation must both succeed.
3. **Readiness wiring.** Point the orchestrator's readiness at `/health/ready` and
   liveness at `/health/live`; pause the database and confirm the instance leaves
   rotation within ~3 s and is not restarted.
4. **Stop grace.** Configure a 30 s stop grace period; `docker stop` during a
   report generation must exit 0 and the report must complete or be reclaimed.
5. **Kill -9 mid-work.** Kill an instance during an indexing attempt and during an
   AI reply; after 30 / 15 minutes the attempt and the usage row must be settled
   (Unknown), with exactly one Knowledge alert and no second provider call.
6. **Headers through the real proxy.** Confirm HSTS on HTTPS responses, the
   private-API framing policy, and that the widget still loads on a real customer
   origin through TLS.
7. **Realtime across a deploy.** Keep an inbox open through a full deploy; it
   must reconnect and refetch without a reload.
8. **Metrics listener.** Attach `dotnet-counters` (or the chosen exporter) to the
   `OmniAI` meter and confirm only the documented tags appear.
9. **Correlation.** Trigger a 500 and a report failure; follow each from its
   `traceId` to every log line it wrote.
10. **Storage loss drill.** Delete one report file on the server; within one
    retention pass its execution must show Expired and offer no Download.
