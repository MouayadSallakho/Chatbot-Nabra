# Phase 38.7a: the backend asks from Phase 38, part A

Conversations, knowledge, realtime and the widget: BA-13, BA-2, BA-7, BA-1, BA-10, BA-11, BA-3, plus one owner-approved package patch. Written 2026-11-12 on top of `413615a`. Not committed.

Evidence folder (outside the repository): `C:\Users\mouay\Desktop\nabra-backend-evidence\p38-7a\` (`<evidence>` below).

Skills loaded and used: `frontend-pro-max:dotnet-backend-architect`, `frontend-pro-max:api-contracts-integration` and `frontend-pro-max:enterprise-system-architecture`. None was missing.

---

## 1. Summary

- An open inbox now shows a customer's message as soon as it is stored, with no reply yet, instead of nothing until the AI answers or fails. The visitor also sees their own message about 0.6 s after sending instead of with the reply.
- Every conversation row and detail says who took it over (`takenOverBy`), and whether and why it needs a person (`needsPerson`, `needsPersonReason`, always equal to the needs-a-person view).
- The customer directory speaks the inbox's label: the same `labelCode` and `shortCode` as the conversation reads.
- Indexing a document finishes even if the tab that pressed Index is reloaded. Knowledge viewers get a live `knowledgeChanged` signal when it starts and when it ends.
- The embedded widget opens in the host page's language (`ar`/`en`). The SignalR Redis package went from 10.0.11 to 10.0.15 to close a high-severity advisory, with your approval.

## 2. Starting point

- **Branch and commit:** `master` at `413615a` ("test: complete the end-to-end journeys and close Phase 38 with its summary").
- **Worktree:** clean (`git status --short` empty).
- **Last five commits:** `413615a`, `311c807`, `98753c9`, `942e269`, `91d6f36`.

## 3. Changes by area

### `git status --short`

```
 M backend/Directory.Packages.props
 M backend/src/OmniAI.Api/Composition/CompositionRoot.cs
 M backend/src/OmniAI.Api/Conversations/ConversationEndpoints.cs
 M backend/src/OmniAI.Api/Customers/CustomerEndpoints.cs
 M backend/src/OmniAI.Api/Knowledge/KnowledgeEndpoints.cs
 M backend/src/OmniAI.Api/Realtime/RealtimeGroups.cs
 M backend/src/OmniAI.Api/Realtime/WorkspaceInboxHub.cs
 M backend/src/OmniAI.Api/Widget/v1.js
 M backend/src/OmniAI.Application/Conversations/CustomerTurnRunner.cs
 M backend/src/OmniAI.Application/Conversations/Persistence/IConversationRepository.cs
 M backend/src/OmniAI.Application/Customers/CustomerContracts.cs
 M backend/src/OmniAI.Application/Customers/CustomerHandlers.cs
 M backend/src/OmniAI.Application/Knowledge/IndexKnowledgeDocument/IndexKnowledgeDocumentHandler.cs
 M backend/src/OmniAI.Domain/Knowledge/KnowledgeIndexAttempt.cs
 M backend/src/OmniAI.Infrastructure/DependencyInjection/ReliabilityServiceCollectionExtensions.cs
 M backend/src/OmniAI.Infrastructure/Persistence/Repositories/ConversationRepository.cs
 M backend/src/OmniAI.Infrastructure/Reliability/StaleWorkReconcilers.cs
 M backend/tests/OmniAI.Api.IntegrationTests/Customers/CustomerApiTests.cs
 M backend/tests/OmniAI.Api.IntegrationTests/Realtime/FailedTurnRealtimeApiTests.cs
 M backend/tests/OmniAI.Api.IntegrationTests/Realtime/InboxHubClient.cs
 M backend/tests/OmniAI.Api.IntegrationTests/WebChat/WebChatWidgetApiTests.cs
 M backend/tests/OmniAI.Api.IntegrationTests/WhatsApp/WhatsAppEndToEndApiTests.cs
 M backend/tests/OmniAI.Application.Tests/Knowledge/IndexKnowledgeDocumentTests.cs
 M backend/tests/OmniAI.Application.Tests/Knowledge/KnowledgeTestDoubles.cs
 M backend/tests/OmniAI.Application.Tests/WebChat/FailedTurnAnnouncementTests.cs
 M backend/tests/OmniAI.Application.Tests/WhatsApp/WhatsAppRuntimeReuseTests.cs
 M backend/tests/OmniAI.Infrastructure.IntegrationTests/Persistence/InboxReadModelTests.cs
 M backend/tests/OmniAI.Infrastructure.IntegrationTests/Reliability/StaleWorkReconciliationTests.cs
 M backend/tests/Shared/RecordingAiTextGenerator.cs
 M docs/architecture/embeddable-web-chat-widget.md
 M docs/architecture/human-handoff-realtime-inbox.md
 M docs/architecture/knowledge-base-rag.md
 M docs/architecture/unified-customer-identity.md
?? backend/src/OmniAI.Api/Realtime/SignalRKnowledgeNotifier.cs
?? backend/src/OmniAI.Application/Knowledge/KnowledgeRealtime.cs
?? backend/tests/OmniAI.Api.IntegrationTests/Knowledge/KnowledgeIndexingLiveApiTests.cs
?? backend/tests/OmniAI.Api.IntegrationTests/WebChat/HandlingFactsApiTests.cs
?? backend/tests/OmniAI.Application.Tests/Customers/CustomerLabelsTests.cs
?? backend/tests/OmniAI.Application.Tests/Knowledge/DetachedIndexAttemptTests.cs
?? docs/phase-reports/PHASE_38_7A_BACKEND_ASKS_REPORT.md
```

**What did not change:**
- no model change and no migration;
- nothing under `frontend/` or `deploy/`;
- no problem code added or changed;
- the `omniai_p36_live` stack and every other project's containers were left alone.

### BA-13: the customer's message is announced as soon as it is stored

`CustomerTurnRunner.RunAsync` now calls `AnnounceQuietlyAsync`, with no cancellation token, on the AI path:
- **When:** right after `BeginTurnAsync` commits the message, and before the provider request is built (building it calls the embedding provider).
- **So:** one AI turn now announces twice: *stored*, then *settled* (the reply, or the failure).
- **Unchanged:** the event and its payload (`conversationChanged(conversationId)`), and the groups (`tenant:{id}` and `conversation:{id}`). The human path keeps its single announcement after it settles.
- **Never announced:** a replay (settled, failed or in progress) and a duplicate refused by the unique index both leave before this point.
- **Tests:**
  - `FailedTurnAnnouncementTests` (Application, 8 tests): order for a reply and for a failure, checked against what was committed at each announcement; one announcement on the human path; none on a replayed answer, a replayed failure or an in-progress duplicate; a throwing notifier changes neither a reply nor a failure.
  - `FailedTurnRealtimeApiTests` (API): over the real hub, the first `conversationChanged` arrives while the provider is held, and a read then shows only the customer's message; the second arrives with the reply.
  - Two existing tests that counted one announcement per failed turn now count two, by design: `FailedTurnRealtimeApiTests.A_failed_ai_turn_reaches_an_open_inbox` and `WhatsAppRuntimeReuseTests.A_failed_reply_is_announced_so_the_inbox_shows_the_message`.

### BA-2: `takenOverBy`

- **The field:** `{ userId, email }` on list items and the detail, or `null`.
- **When set:** a person handles the conversation now, and its newest handling event is a takeover. "Newest" is by `after_sequence`, then `occurred_at`, then id. A return is newer than the takeover it ends, so it reads `null`.
- **When null:** in AI mode, and for a takeover from before Phase 37.4, which recorded no event.
- **Named like the transcript:** the account's email, exactly as transcript event actors are named, including someone who has left the workspace.
- **Read in the page's own statement:** a correlated subquery through `ix_conversation_handling_events_conversation`, with the workspace in the predicate as well as the conversation.
- **Naming:** the assignee and the taker are named by one users query for the page, and only when there is someone to name.
- **Statements:** this replaced the separate assignee lookup, so a page with nobody assigned or taken over now costs **4 statements instead of 5** (`InboxReadModelTests` asserts it, whatever the page length).

### BA-7: `needsPerson` and `needsPersonReason`

- **One definition:** a single expression, `ConversationRepository.WithHandlingFacts`, computes a reason code beside every conversation. Both `filter=needs-person` (`reason <> 0`) and the flag read it, so they cannot disagree.
- **The reasons:** `PersonHandling`, `AiTurnFailed`, `ReturnedUnanswered`, or `null`. The flag is true exactly when the reason is not null.
- **Precedence:** when two branches hold, **`PersonHandling` wins**: a person handles the conversation now, so their reply is what is awaited. The two turn branches are exclusive (a turn is failed or completed, never both).
- **Indexes:** the same as the filter used before. Nothing was added to `messages` or `conversation_turns`.
- **Tests:**
  - `InboxReadModelTests` (PostgreSQL): every branch, a closed conversation, an answered one, an empty one, a takeover with no event, and "failed, then taken over"; the flag against the filter on the same rows; the detail against the row.
  - `HandlingFactsApiTests` (API, 4 tests): take over, return, then take over by someone else, on the list and the detail; another workspace's takeovers never show and its reads find nothing; a member without `conversations.view` gets neither read; every reason through the real routes, with the flag against the filter.

### BA-1: `labelCode` and `shortCode` on the customer directory

- **Fields:** `GET …/customers` items and `GET …/customers/{id}` carry both, from the same `CustomerLabels.CodeFor` (over the channels read exactly as the conversation reads read them) and `CustomerShortCode`. The English `label` stays.
- **Documented:** both reads now declare their response bodies in OpenAPI (metadata only).
- **Tests:**
  - `CustomerLabelsTests` (new; the rule had no test): every code and its English label; the short-code shape.
  - `CustomerApiTests`: `Named` and `AnonymousVisitor`, row and detail, equal to the inbox's, and for a directory-only reader.
  - `WhatsAppEndToEndApiTests`: `WhatsAppCustomer`, then `UnnamedCustomer` after a merge with a website visitor, equal to the inbox's.

### BA-10: indexing finishes even when its request is cancelled

**The choice:** a server-side bounded deadline, not a background queue, because it is the smaller and safer change. `POST …/knowledge/{id}/index` now calls `IndexKnowledgeDocumentHandler.HandleDetachedAsync`:
- the work runs under the host's shutdown token (`IHostApplicationLifetime.ApplicationStopping`) and the server's own deadline, never the request's token;
- the attempt record, the `knowledge.indexing.in_progress` single flight, the alerts, `If-Match`, the response to a caller who is still there, and `KnowledgeAttemptReconciler` are all unchanged.

**The deadline** is `KnowledgeIndexingLimits.AttemptTimeout`:
- the bound the reliability settings are already validated against (embedding timeout × the most batches a document can need), plus 2 minutes;
- `ReliabilityOptions` requires the stale threshold to exceed that bound by 5 minutes, so the reconciler never settles a running attempt;
- registered by `AddOmniAiReliability`.

**How each stop is recorded:**

| Stop | Attempt | Alert | Caller still waiting |
| --- | --- | --- | --- |
| The server's deadline | `Failed` / **`timed_out`** (new attempt code, not a problem code) | yes | the existing **504** "Knowledge indexing timed out" (`EmbeddingTimeoutException`) |
| The host shutting down | `Unknown` / `interrupted` (what the reconciler records) | yes | nobody; the process is stopping |
| A caller's own token (`HandleAsync`, not the route) | `Unknown` / `cancelled`, as before | no | unchanged |

**Tests:**
- `DetachedIndexAttemptTests` (Application, 9 tests): a detached success; past the deadline (`timed_out`, alert, 504, nothing written); shutdown (`interrupted`); a caller's own token (`cancelled`); the announcements; the limits.
- `KnowledgeIndexingLiveApiTests` (API, real host, a provider the test holds open):
  - the request is abandoned while the provider is held;
  - the work's token is not cancelled;
  - a second request meanwhile is `409 knowledge.indexing.in_progress`;
  - released, the attempt settles `Succeeded` and the document reads `Ready`;
  - a caller who waits gets the same `200` body and `ETag` as before.

### BA-11: `knowledgeChanged`

- **The event:** `knowledgeChanged(documentId)` (`RealtimeEvents.KnowledgeChanged`) on `/hubs/workspace-inbox`, to the new group `knowledge:{tenantId}` (`RealtimeGroups.ForKnowledge`, its own prefix).
- **When:**
  - when an attempt starts: its record is committed and the single flight is held;
  - when it settles: success or failure, from the request;
  - when the reconciler settles a stale one: after its statement commits, one announcement per settled attempt.
- **Joining:** `SubscribeKnowledge(tenantId)` checks membership and `knowledge.view`. It reads the three-valued decision, so an outsider and a member without the permission both get "No such workspace.", like `Subscribe`. `UnsubscribeKnowledge(tenantId)` needs nothing.
- **Best effort:** `IKnowledgeRealtimeNotifier` with `KnowledgeRealtimeAnnouncements.AnnounceQuietlyAsync` never throws, and `SignalRKnowledgeNotifier` logs and swallows.
- **Payload:** the id only, on the same single connection.
- **Tests:**
  - `KnowledgeIndexingLiveApiTests`: a viewer hears start (while held) and settle; a member without `knowledge.view` and another workspace's owner are refused and hear nothing; the inbox subscription carries no knowledge event.
  - `StaleWorkReconciliationTests`: the reconciler announces each settled attempt once, and a throwing announcer changes neither the settlement, its count nor the alert.
  - `DetachedIndexAttemptTests`: start and settle; nothing without an attempt; a throwing notifier changes nothing.

### BA-3: the widget follows the host page's language

`v1.js` adds `?lang=ar` or `?lang=en` to the frame URL from the host page's own `<html lang>`:
- **Matching:** a fixed table, `FRAME_LANGUAGES`, read the way the frame title already reads it: `ar` or `ar-…`, and `en` or `en-…`.
- **Otherwise nothing:** for any other value or no `lang`, the frame follows the visitor's browser, as before. There is no fallback to `navigator.language`.
- **Unchanged:** the title's rule, old snippets (both key attributes), and no new attribute.
- **Caching:** `/widget/v1.js` is `public, max-age=3600` with a strong content `ETag`. A visitor keeps an old loader for at most an hour, then revalidates it for a `304`. Until then the frame follows their browser, which was the old behaviour.
- **Tests:** `WebChatWidgetApiTests`:
  - 8 language cases (`ar`, `ar-SA`, `AR-eg`, `en-GB`, `en`, `fr`, `arabic`, none), applied from the table in the served script;
  - the address is built from that word and nothing else, and both key attributes are still read;
  - the cache header and the `304`.
  - The solution has no JavaScript engine (adding one would be a package), so the served file itself was also run in Node and in Chrome (§5).

### The package patch (owner-approved, 2026-11-12)

- **The change:** `Microsoft.AspNetCore.SignalR.StackExchangeRedis` from 10.0.11 to **10.0.15** in `backend/Directory.Packages.props`; nothing else.
- **The reason:** GHSA-wr8v-9x5q-ggcm / CVE-2026-114147 (high; ASP.NET Core denial of service), published 2026-11-11. NuGet's audit (warnings as errors) then refused to restore at `413615a` too, which blocked every gate. 10.0.15 (published 2026-11-10) is the first fixed version.
- **Audit:** on, with no suppression.
- **Images:** `mcr.microsoft.com/dotnet/sdk:10.0` and `aspnet:10.0` were pulled fresh (pull only, nothing deleted). The tests and the dev API now run ASP.NET Core **10.0.15** (the dev API ran **10.0.12** before; SDK 10.0.404).

### Contract table

| Endpoint or event | Change | New fields, parameters or codes | Permission | Why it is backward compatible |
| --- | --- | --- | --- | --- |
| `conversationChanged` (both hubs) | Also sent when an AI turn's customer message is stored | none (same name, payload, groups) | as before | Clients already re-read on every event; one more re-read. See §8 for the visitor page's one-frame duplicate |
| `GET /api/tenants/{t}/conversations` | Fields added; `filter=needs-person` now evaluated from the same expression | `takenOverBy {userId,email}\|null`, `needsPerson` (bool), `needsPersonReason` (`PersonHandling`\|`AiTurnFailed`\|`ReturnedUnanswered`\|null) | `conversations.view` | New fields only; the filter returns the same set (proved against the same rows); no query per row |
| `GET /api/tenants/{t}/conversations/{id}` | Fields added | the same three | `conversations.view` | New fields only; `ETag` unchanged |
| `GET /api/tenants/{t}/customers` | Fields added; response body declared in OpenAPI | `labelCode`, `shortCode` | `customers.view` | New fields only; `label` stays |
| `GET /api/tenants/{t}/customers/{id}` | Fields added; response body declared | `labelCode`, `shortCode` | `customers.view` | New fields only |
| `POST /api/tenants/{t}/knowledge/{id}/index` | Runs detached from the request, bounded by the server's deadline | attempt code `timed_out` (on `lastIndexAttempt.failureCode`) | `knowledge.manage` + `If-Match` | Same statuses and bodies for a caller who stays. An unknown attempt code is worded generically by the current client (`indexFailureReason`). The 504 already existed |
| Hub method `SubscribeKnowledge(tenantId)` / `UnsubscribeKnowledge(tenantId)` | New | group `knowledge:{tenantId}` | membership + `knowledge.view` | Opt-in; the current client never calls it |
| Event `knowledgeChanged(documentId)` | New | payload: document id only | as subscribed | Only sent to the new group, which no current client joins |
| `/widget/v1.js` | Frame URL may carry `?lang=ar\|en` | query parameter on `/embed/{key}` | public | The frame already reads `?lang=` (38.3); other values add nothing |
| Package | `Microsoft.AspNetCore.SignalR.StackExchangeRedis` 10.0.11 → 10.0.15 | none | n/a | Patch release of the same major and minor; the backplane and MessagePack wire are unchanged; the full suite, including the two-instance backplane tests, passes |

OpenAPI: the new response fields appear in the generated schemas. The two customer reads now declare their bodies, and the conversation and index route summaries describe the new behaviour. A test that pins the document is item 13 of 38.7b.

## 4. Decisions

1. **BA-10: deadline, not a queue.**
   - Rejected: moving indexing to a background job. It would duplicate the durable attempt record, change the response contract (the caller would get 202 and no result), and need a worker and claim rules.
   - The deadline keeps every guarantee and only removes the browser from the work's token.
2. **The server's own timeout is `Failed` / `timed_out`, with the alert.**
   - It is not the person's cancellation, and running past a limit that covers the longest legitimate provider work is operational.
   - Shutdown records `Unknown` / `interrupted` with the alert, which is what the reconciler would record a moment later anyway.
   - The 504 for a waiting caller is the existing "Knowledge indexing timed out", so no problem code was needed.
3. **BA-7: one expression for the filter and the flag**, through a nominal `HandledConversation` projection that the count, the filter, the page and the detail all compose over.
   - Rejected: keeping the old predicate and a second CASE expression with a test that they agree. That is two definitions.
   - p95 shows no regression (§5).
4. **`PersonHandling` wins over a failed or unanswered turn:** a person who took the conversation over owns the next reply.
5. **BA-2: newest event by transcript position first.** `after_sequence` is read under the conversation lock, so it orders events independently of instance clocks; time and then id break ties. A return clears the field even though an older takeover exists.
6. **`SubscribeKnowledge` reads the three-valued decision**, so a member without the permission gets "No such workspace." instead of SignalR's generic error. `Subscribe` has the generic-error behaviour for that case (its documentation says "one message for both"). It was left as it is, because the current frontend relies on its behaviour.
7. **BA-3 has no `navigator.language` fallback** in the URL (the title keeps it): the frame already decides from the browser, so adding the browser's language would only make "no `lang`" impossible to tell apart.
8. **BA-13 goes to both groups, as item 1 says.** The Chrome check found a one-frame duplicate on the visitor's page at settlement (§5, §8). The change was kept as specified and the choice is put to you (§8, §7).
9. **Deferred:** nothing from 38.7a's list. A JavaScript engine for the `v1.js` tests was not added (it would be a package); the served file was executed in Node and Chrome as evidence instead.

## 5. Verification

Free memory is shown before each heavy gate. The 6 GB floor was respected throughout (twice I dropped the Docker VM page cache once and then waited for 6 GB before a trial run).

| Check | Result |
| --- | --- |
| Starting point | **PASS**: `master` at `413615a`, clean |
| Free memory before the backend gates | 8.39 GB |
| Restore, NuGet audit **on** (no `-p:NuGetAudit=false`, no suppression) | **PASS**: exit 0 (`<evidence>\gates\backend-gates-run1.log`) |
| `dotnet list package --vulnerable --include-transitive` | **PASS**: no vulnerable package in any of the 8 projects. Resolved: SignalR.StackExchangeRedis 10.0.15, MessagePack 2.5.303, Pkcs 10.0.13 |
| `dotnet build OmniAI.slnx -c Release` | **PASS**: **0 warnings**, 0 errors |
| Tests, `OmniAI.Api.IntegrationTests` (`-m:1`, PostgreSQL 17 + pgvector, Redis 7, throwaway) | **PASS**: 1,125 passed, 2 skipped |
| Tests, `OmniAI.Application.Tests` | **PASS**: 910 passed |
| Tests, `OmniAI.Domain.Tests` | **PASS**: 915 passed |
| Tests, `OmniAI.Infrastructure.IntegrationTests` | **PASS**: 1,307 passed, 1 skipped |
| Total | **4,257 passed, 3 skipped, 0 failed** (37.7: 4,214; 43 new). 0 leftover test databases; throwaway containers and network removed |
| Images | **PASS**: `sdk:10.0` and `aspnet:10.0` pulled; ASP.NET Core 10.0.15, SDK 10.0.404. Nothing deleted |
| Dev API rebuild (`p36-stack.sh build`, then `api`) | **PASS**: published with the audit on; ready on `127.0.0.1:5027`, stand-in on |
| ASP.NET Core runtime in `omniai-p36-api` | before **10.0.12**, after **10.0.15** (`<evidence>\live\runtime-*.txt`) |
| Provider canary | **PASS**: every provider secret and `OMNIAI_LIVE_*` absent, no SMTP credentials, SMTP host loopback, `OMNIAI_DEV_AI_STANDIN` listed as info (`<evidence>\live\canary.txt`) |
| Live BA-13, `[[slow]]` (open inbox connection, hosted chat) | **PASS**: first `conversationChanged` at **1.2 s**, read shows `["Customer"]`; second at **13.7 s**, read `["Customer","AiAgent"]`; exactly 2 |
| Live BA-13, `[[fail]]` | **PASS**: first at **55 ms** (`["Customer"]`), second at **1.1 s** after the `503`; still `["Customer"]`; exactly 2 |
| Live BA-13, visitor page in Chrome | **PARTIAL**: no flicker or duplicate during the wait. The visitor's message now appears at 0.6 s, and the BA-13 re-read put it in once. At settlement, **one painted frame (~17 ms) showed the customer's message twice** (8 rows, then 7), in all three runs. Cause: the hosted page appends the send's answer without checking ids (§8). Final state correct |
| Live BA-2 / BA-7 (Northwind, real routes) | **PASS**: AI mode → nobody, `needsPerson` false; owner takes over, customer writes → `takenOverBy` owner, `PersonHandling`; returned → `null`, `ReturnedUnanswered`; support takes over → `takenOverBy` support; a `[[fail]]` turn → `AiTurnFailed`. List and detail equal every time |
| Live BA-7, flag against filter | **PASS**: all **1,010** Northwind conversations: filter total 146, flagged 146, same set, 0 flags without a reason (143 `AiTurnFailed`, 3 `PersonHandling` at that moment) |
| Live refusals, conversations | **PASS**: analyst (no `conversations.view`) list `403`, detail `403`; analyst on Al Noor (not a member) `404`; a Northwind id read under Al Noor by the owner `404` |
| Live BA-1 | **PASS** for named (`Named`, `J282QF`) and website-only (`AnonymousVisitor`, row = detail = inbox). All 1,010 directory rows compared with the inbox: **0 mismatches**. **WhatsApp-only: NOT RUN live**: the dev data has no WhatsApp customer, and making one would need a signed Meta webhook (no secret exists here, by rule 9). `WhatsAppEndToEndApiTests` proves it |
| Live BA-10 | **PASS**: the index request was aborted **5 ms** after sending. The attempt started on the server ~170 ms later, after the abort, and settled `Succeeded`, document `Ready`; the next read shows it |
| Live BA-11 | **PASS**: owner (`knowledge.view`) heard `knowledgeChanged` at start (173 ms) and settle (240 ms); analyst refused for Northwind (no `knowledge.view`) and for Al Noor (not a member) with "No such workspace.", heard 0 |
| Live BA-3, Chrome network panel (host pages on `127.0.0.1:8099`) | **PASS**: `lang="ar"` → `/embed/9veAaKzu0Ee-isA7jrQzYw?lang=ar` (title «الدردشة»); `lang="en"` → `?lang=en` (title "Chat"); `lang="fr"` and no `lang` → no parameter. Screenshot `live\shots\ba3-host-ar.png` |
| BA-3, the served file run in Node | **PASS**: `ar`, `ar-SA` → `?lang=ar`; `en-GB` → `?lang=en`; `fr`, none → nothing (`live\v1js-node-source.txt`) |
| Free memory before `e2e` | 8.44 GB |
| `npm run e2e` (current frontend, unchanged, rebuilt API, after the canary) | **PASS**: **46 passed** (23 × en-ltr, ar-rtl), 0 failed, 0 flaky, 0 retries, 2.9 min (`<evidence>\e2e-run1.log`, `e2e-run1\e2e-results.json`). One full run, so the throttling journey was not repeated |
| `format:check`, `test:ci`, `build`, `i18n:check` | **NOT RUN**: no file under `frontend/` changed |
| Secret scan (evidence and changed files) | **PASS**: no demo password, cookie, token or connection string; the 12 pattern hits are package names, an existing made-up `sk-live-canary` test string and the scripts' `$PW` variable. `<evidence>\tmp\` (e2e session states) deleted |

**p95 latency** on the seeded dev data: 300 interleaved reads per variant after a 30-read warm-up (`<evidence>\latency\`).
- Before: the `413615a` build on ASP.NET Core 10.0.12, with 1,005 conversations.
- After: the rebuilt API on 10.0.15, with 1,011 conversations.

| Endpoint | p95 before | p95 after | p50 before → after |
| --- | --- | --- | --- |
| `GET …/conversations` (page 1, 20) | 25.7 ms | 20.7 ms | 16.2 → 16.3 |
| `GET …/conversations` (page 1, 50) | 24.8 ms | 22.9 ms | 16.4 → 16.3 |
| `GET …/conversations?filter=needs-person` (page 1, 20) | 30.0 ms | 28.1 ms | 17.7 → 17.0 |
| `GET …/customers` (page 1, 20) | 25.7 ms | 25.5 ms | 10.8 → 10.9 |
| `GET …/customers` (page 1, 100) | 19.7 ms | 19.2 ms | 16.4 → 16.3 |

No regression; no index was added. The runtime also changed between the two runs, so the small gains are not attributed to the query.

## 6. See it live

The dev API (`127.0.0.1:5027`) and the dev server (`localhost:4200`) are running. Sign in as the owner from `demo-accounts.md`.

1. **The stored message (BA-13).**
   - Open the inbox at `http://localhost:4200`.
   - In another window, open `http://localhost:4200/chat/9veAaKzu0Ee-isA7jrQzYw` and send a message containing `[[slow]]`.
   - Within about a second the conversation moves to the top of the inbox, with the customer's message and no reply ("The AI has not replied yet" when you open it). About 11 s later the reply arrives.
   - With `[[fail]]` the message arrives at once and the failure about a second later.
   - On the visitor's page, your own message now shows before the reply.
2. **Who handles it, and why (BA-2, BA-7).**
   - Open DevTools → Network on the inbox and read the `conversations` response.
   - Each item now has `takenOverBy` (`null` or `{ userId, email }`), `needsPerson` and `needsPersonReason`.
   - Take a conversation over: `takenOverBy` names you, and once the customer writes, `needsPersonReason` is `PersonHandling`.
   - Return it before answering: `takenOverBy` is `null` and the reason is `ReturnedUnanswered`.
   - Every row of `?filter=needs-person` has `needsPerson: true`.
3. **The directory's label (BA-1).** `GET /api/tenants/{northwind}/customers` (the Customers page's request): each item has `labelCode` and `shortCode`, the same as that customer's `customer` object on a conversation row.
4. **Indexing a document (BA-10, BA-11).**
   - Open a knowledge document that needs an index, press Index, and reload the tab at once.
   - The next read shows the attempt `Succeeded`; before this step it would have read "Indexing stopped".
   - On the inbox page's hub connection (DevTools → Network → WS → `workspace-inbox`), invoke `SubscribeKnowledge` with the workspace id (or run `scratch\live-checks.mjs index`). Pressing Index then sends two `knowledgeChanged` frames carrying only the document id.
5. **The widget's language (BA-3).**
   - Serve `<evidence>\host-pages` with `python -m http.server 8099 --bind 127.0.0.1` and open `http://127.0.0.1:8099/ar.html`, then `en.html`, `fr.html` and `none.html`.
   - In Network, the `/embed/…` document carries `?lang=ar`, `?lang=en`, or nothing.
6. **The patched runtime.** `docker exec omniai-p36-api ls /usr/share/dotnet/shared/Microsoft.AspNetCore.App/` prints `10.0.15`.

## 7. For 38.8

Ready to copy into the 38.8 work. Each item is additive; nothing in the current client has to change for it to keep working.

1. **Visitor transcript de-duplication (do first).**
   - `features/chat/hosted-chat/hosted-chat.ts` `send()` (line 304) and the identical append in `features/embed/embed-chat/embed-chat.ts` (line 311): merge `turn.customerMessage` and `turn.reply` into `messages` **by `id`** (skip ids already present), instead of appending.
   - Since BA-13 the conversation's group hears "stored" during the wait and re-reads, so the customer's message is usually already there when the POST answers.
   - Today that shows it twice for one frame until the settlement re-read replaces the list. A list that receives the settlement re-read *before* the POST answer keeps the duplicate (possible before 38.7a too).
2. **BA-13, inbox.**
   - Nothing to wire: `conversationChanged` already triggers the re-read. An open inbox now shows the message and the "not replied yet" line during the wait.
   - The `slow-reply` journey can wait on the open inbox instead of reopening the conversation after `messageCount` changes.
3. **BA-2:** `takenOverBy: { userId, email } | null` on `ConversationSummary` and `ConversationDetail` types. The "new message" sound can count conversations where `takenOverBy.userId === me` as well as `assignedTo.userId === me` (owner decision 7d).
4. **BA-7:** `needsPerson: boolean` and `needsPersonReason: 'PersonHandling' | 'AiTurnFailed' | 'ReturnedUnanswered' | null`. Word each reason in English and Arabic; treat an unknown value as the generic "Waiting for the team". `needsPerson` equals membership in `filter=needs-person`.
5. **BA-1:** `labelCode: 'Named' | 'WhatsAppCustomer' | 'AnonymousVisitor' | 'UnnamedCustomer'` and `shortCode: string` on `GET …/customers` items and `GET …/customers/{id}`. The customers screens can drop `customerName()`'s own rule and use the inbox's label helper.
6. **BA-10:**
   - the hint can stop asking people not to reload while indexing;
   - word the new `lastIndexAttempt.failureCode` `timed_out` ("Indexing took longer than allowed; nothing was indexed");
   - a 504 from `…/index` still means "timed out".
7. **BA-11:**
   - On the existing `WorkspaceRealtime` connection, invoke `SubscribeKnowledge(tenantId)` on the knowledge screens (and `UnsubscribeKnowledge` when leaving).
   - Handle `knowledgeChanged(documentId: string)` by re-reading the list item or the document.
   - A refusal ("No such workspace.", with SignalR's prefix) means no `knowledge.view`: don't retry.
   - Re-read after every reconnect, as for conversations.
8. **BA-3:** nothing to build; prove it on an English and an Arabic host page, as §6.5.

## 8. Risks and open issues

1. **The visitor page's one-frame duplicate (BA-13).** Measured in Chrome three times: one painted frame (~17 ms) with the customer's message twice, at the moment the reply arrives. The list is `aria-live="polite"` (additions), so a screen reader may also announce the duplicate. Your choice:
   - **(a) Keep it as built** (item 1: same groups). The visitor sees their message 11 s earlier, and 38.8 adds the de-duplication in §7.1, about one line. *Recommended.*
   - **(b) Workspace only.** Send the stored announcement to the workspace group only. The visitor page then behaves exactly as before 38.7a (no duplicate, but no early message either). Small and additive: one notifier method with an audience, and tests.
2. **The production images and runtime.** The advisory also covers the ASP.NET Core runtime ≤ 10.0.14.
   - `deploy/` and the `omniai_p36_live` stack were not touched. Their images (and any `sdk`/`aspnet` tag they pin) still run whatever runtime they were built with.
   - Rebuild them on a 10.0.15+ runtime as part of the launch hardening.
3. **A stale comment in `Directory.Packages.props`.** It still says 10.0.11 "matches the ASP.NET Core version every other Microsoft package here is pinned to". The SignalR package is now 10.0.15 while `Microsoft.AspNetCore.OpenApi`, `Identity.EntityFrameworkCore` and `Mvc.Testing` stay at 10.0.11. Left as instructed (change nothing else); a maintenance step can align the comment, or the packages.
4. **`Subscribe` refusal text.** A member without `conversations.view` still gets SignalR's generic error from `Subscribe` (it throws `PermissionDeniedException`), contrary to its own comment. Unchanged, because the current client relies on today's behaviour; worth aligning when the frontend can follow.
5. **The deadline is long by design.** It is the provider bound plus 2 minutes (several minutes with the shipped embedding defaults). A provider that hangs keeps one attempt's single flight held that long; the reconciler is the safety net for a process that dies.
6. **No WhatsApp customer in the dev data.** BA-1's `WhatsAppCustomer` and `UnnamedCustomer` are proved by tests only; live proof needs a WhatsApp conversation, which needs Meta.
7. **No JavaScript engine in the backend tests.** The `v1.js` rule is tested on the served text plus .NET's ECMAScript regex mode. Behaviour was executed in Node and Chrome as evidence, not in CI. Adding Jint (or a Node step) would need your approval.

## 9. Proposed checkpoint commit message

Not committed.

```
feat: announce the stored message, say who handles a conversation and why it needs a person, label the directory like the inbox, let indexing outlive its request with a live signal, and open the widget in the host page's language

Phase 38.7a, the backend asks BA-13, BA-2, BA-7, BA-1, BA-10, BA-11
and BA-3. Additive; no migration; the frontend at 413615a is unchanged
and its end-to-end suite passes (46/46).

- BA-13: CustomerTurnRunner announces conversationChanged right after
  the customer's message is committed on the AI path (quietly, no
  token), then again when the turn settles. Replays never announce.
- BA-2/BA-7: conversation list items and the detail carry takenOverBy
  (newest handling event, named like the transcript), needsPerson and
  needsPersonReason (PersonHandling | AiTurnFailed |
  ReturnedUnanswered), from one expression shared with
  filter=needs-person; no index added, 4 statements per page.
- BA-1: the customer directory carries labelCode and shortCode from
  CustomerLabels.CodeFor and CustomerShortCode; label stays.
- BA-10: POST …/knowledge/{id}/index runs detached from the request,
  under the host's shutdown token and a server deadline below the
  reconciler's threshold; timed_out (Failed, alert, 504) and
  interrupted (Unknown, alert) are recorded honestly.
- BA-11: knowledgeChanged(documentId) on the workspace hub to members
  who SubscribeKnowledge with knowledge.view, at start and settle,
  including reconciler settlements; best effort.
- BA-3: v1.js adds ?lang=ar|en from the host page's <html lang>.
- Security: Microsoft.AspNetCore.SignalR.StackExchangeRedis 10.0.11 ->
  10.0.15 for GHSA-wr8v-9x5q-ggcm (CVE-2026-114147, high); NuGet audit
  stays on with no suppression (owner-approved).

Backend: 4,257 passed, 3 skipped; build 0 warnings; no vulnerable
packages.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

---

## Revision 1 (2026-11-12)

The owner accepted this report with one fix and one choice.

1. **The stale comment (§8.3), rewritten.**
   - The comment above `Microsoft.AspNetCore.SignalR.StackExchangeRedis` in `backend/Directory.Packages.props` now says what is true. The package is 10.0.15 for GHSA-wr8v-9x5q-ggcm (CVE-2026-114147). The other Microsoft ASP.NET Core packages stay at 10.0.11 until the dependency update of the launch hardening.
   - Only the comment changed: no version and no other line.
   - The MessagePack comment further down still says that package "brought by … 10.0.11 at 2.5.302". That is historical, and it was left alone, as instructed. The dependency update can reword it.
2. **The owner's choice on §8.1: (a).**
   - BA-13 stays as built: the stored message is announced to both groups.
   - 38.8 adds the visitor transcript de-duplication (§7.1) as its first item.
   - §8.1 is closed; the one-frame duplicate stays until 38.8.

**Re-run at the checkpoint, one gate at a time:**

| Check | Result |
| --- | --- |
| Worktree | The files of §3, plus the comment. Nothing under `frontend/` or `deploy/` |
| Change set | No evidence, scratch scripts, OpenAPI copies, env files, keys, certificates, e2e state, traces, `test-results`, `playwright-report`, `node_modules`, `dist`, `.angular`, `bin`, `obj` or IDE files. Credential scan of the 40 files: the only hits are a package name and the existing made-up `sk-live-canary` test string (no demo password, token, cookie, reset or invitation link) |
| Free memory before the backend tests | 8.39 GB |
| Restore, NuGet audit **on** | **PASS**: exit 0 (`<evidence>\gates\backend-gates-rev1.log`) |
| `dotnet list package --vulnerable --include-transitive` | **PASS**: none in any of the 8 projects. SignalR.StackExchangeRedis 10.0.15, MessagePack 2.5.303, Pkcs 10.0.13 |
| `dotnet build OmniAI.slnx -c Release` | **PASS**: **0 warnings**, 0 errors |
| Tests (`-m:1`, throwaway PostgreSQL 17 + pgvector and Redis 7) | **PASS**: Api.IntegrationTests 1,125 passed, 2 skipped. Application.Tests 910 passed. Domain.Tests 915 passed. Infrastructure.IntegrationTests 1,307 passed, 1 skipped. Total **4,257 passed, 3 skipped, 0 failed**. 0 leftover test databases; throwaway containers removed |
| Dev API | Not rebuilt, because a comment does not change the build output. Still the build from §5, on ASP.NET Core 10.0.15 |
| Provider canary | **PASS** (`<evidence>\live\canary-rev1.txt`) |
| Free memory before `e2e` | 7.51 GB |
| `npm run e2e` | **PASS**: **46 passed** (23 × en-ltr, ar-rtl), 0 failed, 0 flaky, 3.3 min (`<evidence>\e2e-rev1.log`, `e2e-rev1\e2e-results.json`). A full run, 20 minutes after the first one ended, so the throttling journey ran too. `<evidence>\tmp\` deleted |
| `format:check`, `test:ci`, `build`, `i18n:check` | **NOT RUN**: no file under `frontend/` changed |
