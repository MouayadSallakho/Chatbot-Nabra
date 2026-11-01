# Phase 37.4: Inbox

Who the customer is on every conversation row, the newest transcript page first, what happened in a transcript besides its messages, a "needs a person" list, and unread state per member. Evidence is in `C:\Users\mouay\Desktop\nabra-backend-evidence\p37-4\`.

## 1. Summary

- Every conversation row and detail now says who the conversation is with: the name someone gave the customer, or a stable six-character code ("Visitor 7KQ2MX" / «زائر 7KQ2MX»). WhatsApp conversations also show the masked number.
- A long conversation can be opened at its newest messages and read backwards, page by page. The oldest-first read is unchanged.
- The transcript can show where the AI failed to answer, with a stable reason code. It can also show who took the conversation over or handed it back to the AI, and when. Customers never see any of it.
- The inbox can list only the conversations that need a person: open, the customer wrote last, and either a person is handling it or the AI's last turn failed.
- Unread state is per member. The list shows which conversations hold something that member has not seen. Opening a conversation (`POST …/read`) or replying marks it read.

## 2. Starting point

- Branch `master`, HEAD `c4a9992` ("feat: reset passwords, write to everyone in their language, email invitations"), worktree clean. That is the 37.3 checkpoint, as "Starting point" requires.
- The dev stack was stopped. `p36-stack.sh up` started it, and the provider canary passed before any traffic.

## 3. Changes by area

`git status --short` (backend and docs only; no frontend, `deploy/` or package change):

```
 M backend/src/OmniAI.Api/Composition/CompositionRoot.cs
 M backend/src/OmniAI.Api/Conversations/ConversationEndpoints.cs
 M backend/src/OmniAI.Api/ErrorHandling/DomainProblemDetailsHandler.cs
 M backend/src/OmniAI.Application/Conversations/ConversationHandoffHandler.cs
 M backend/src/OmniAI.Application/Conversations/ConversationInboxExceptions.cs
 M backend/src/OmniAI.Application/Conversations/Persistence/IConversationInboxStore.cs
 M backend/src/OmniAI.Application/Conversations/Persistence/IConversationRepository.cs
 M backend/src/OmniAI.Application/Conversations/ReadConversationsHandler.cs
 M backend/src/OmniAI.Application/Conversations/ReplyToConversationHandler.cs
 M backend/src/OmniAI.Application/Customers/CustomerLabels.cs
 M backend/src/OmniAI.Domain/Conversations/Conversation.cs
 M backend/src/OmniAI.Infrastructure/Persistence/Configurations/ConversationConfiguration.cs
 M backend/src/OmniAI.Infrastructure/Persistence/Migrations/OmniAiDbContextModelSnapshot.cs
 M backend/src/OmniAI.Infrastructure/Persistence/OmniAiDbContext.cs
 M backend/src/OmniAI.Infrastructure/Persistence/PersistenceConverters.cs
 M backend/src/OmniAI.Infrastructure/Persistence/PersistenceSchema.cs
 M backend/src/OmniAI.Infrastructure/Persistence/Repositories/ConversationInboxStore.cs
 M backend/src/OmniAI.Infrastructure/Persistence/Repositories/ConversationRepository.cs
 M backend/tests/OmniAI.Api.IntegrationTests/WhatsApp/WhatsAppEndToEndApiTests.cs
 M backend/tests/OmniAI.Application.Tests/Conversations/ConversationInboxDoubles.cs
 M backend/tests/OmniAI.Application.Tests/WebChat/WebChatTestDoubles.cs
 M backend/tests/OmniAI.Infrastructure.IntegrationTests/Persistence/MigrationSchemaTests.cs
 M docs/architecture/human-handoff-realtime-inbox.md
 M docs/architecture/problem-codes.md
 M docs/architecture/unified-customer-identity.md
?? backend/src/OmniAI.Application/Conversations/MarkConversationReadHandler.cs
?? backend/src/OmniAI.Domain/Conversations/ConversationHandlingEvent.cs
?? backend/src/OmniAI.Domain/Conversations/ConversationHandlingEventId.cs
?? backend/src/OmniAI.Domain/Customers/CustomerShortCode.cs
?? backend/src/OmniAI.Infrastructure/Persistence/Configurations/ConversationHandlingEventConfiguration.cs
?? backend/src/OmniAI.Infrastructure/Persistence/Configurations/ConversationReadConfiguration.cs
?? backend/src/OmniAI.Infrastructure/Persistence/ConversationReadRow.cs
?? backend/src/OmniAI.Infrastructure/Persistence/Migrations/20261101095238_AddConversationHandlingIndex.cs (+ .Designer.cs)
?? backend/src/OmniAI.Infrastructure/Persistence/Migrations/20261101095356_AddConversationHandlingEvents.cs (+ .Designer.cs)
?? backend/src/OmniAI.Infrastructure/Persistence/Migrations/20261101095544_AddConversationReads.cs (+ .Designer.cs)
?? backend/tests/OmniAI.Api.IntegrationTests/WebChat/InboxStateApiTests.cs
?? backend/tests/OmniAI.Application.Tests/Conversations/InboxStateTests.cs
?? backend/tests/OmniAI.Domain.Tests/Conversations/ConversationHandlingEventTests.cs
?? backend/tests/OmniAI.Domain.Tests/Customers/CustomerShortCodeTests.cs
?? backend/tests/OmniAI.Infrastructure.IntegrationTests/Persistence/InboxReadModelTests.cs
?? docs/phase-reports/PHASE_37_4_INBOX_REPORT.md
```

### 3.1 Who the customer is (row 9, D-4)

- List items and the detail gain `customer: { id, displayName, labelCode, reference, shortCode }`, under `conversations.view` alone (D-4).
- `labelCode` is the existing label rule as a code (`CustomerLabels.CodeFor`). `CustomerLabels.For` now derives its English sentence from the same code, so the two cannot disagree.
- `reference` appears on WhatsApp conversations only. It is read from `whatsapp_contacts` by conversation and masked on the value object (`••••1234`) straight after the read. The raw `wa_id` leaves no method.
- `shortCode` (`CustomerShortCode`, Domain) is six Crockford base32 characters from the last 30 bits of the customer id. Those are the random end of a UUIDv7, never its leading timestamp. Nothing is stored, so nothing needs a backfill. It is a label, never a key.
- Merged customers: conversations name their canonical customer, because a merge moves conversations in the same transaction.
- Cost: a fixed number of statements per page (customers, their channels, and WhatsApp numbers only when needed).

### 3.2 Newest page first (row 10)

- `GET …/messages?tail=true` returns the newest `limit` messages; `before={sequence}` returns the `limit` before it. Pages stay ascending inside.
- New `previousCursor` (the next `before`, or null) on every page. `nextCursor` keeps its meaning.
- `after` with `tail` or `before` is `400 transcript.cursor.conflict`. Without the new parameters the page is exactly the old one.

### 3.3 What happened in the transcript (rows 11, 12, M4)

- **Handoff history.** `Conversation.TakeOverByHuman(actor, at, afterSequence)` and `ReturnToAi(…)` return a `ConversationHandlingEvent` only when handling actually changes.
- **Safe ordering.** The arguments are checked before handling flips, so a bad argument never leaves the conversation changed with nothing recorded.
- **Placement.** The handoff handler reads the newest sequence under the conversation lock, so an in-flight AI reply is already stored. It then saves the conversation and the event in one transaction (`SaveHandlingChangeAsync`).
- **Reading the events.** `include=events` adds `events: [{ id, kind, at, afterSequence, actor, reason }]` for the page's span:
  - `AiTurnFailed` comes from failed turns, has no actor, and carries `reason: "generation_failed"`.
  - `TakenOver` and `ReturnedToAi` carry `actor: { userId, email }`.
- **Never public.** Turns completed without a reply (a person was handling the conversation) are never marked. The public transports have no field for events and ignore `include`.

### 3.4 Needs a person (row 13, D-5, M6)

- `filter=needs-person` lists conversations that are open, where the customer wrote the last message, and one of the following holds (`total` counts the filtered set):
  - a person handles it;
  - the AI's turn for that message failed;
  - **(Revision 1)** the AI handles it, but the turn for that message completed without a reply. The customer wrote while a person handled the conversation, and it was returned to the AI unanswered.
- The third branch widens the plan's D-5 definition at the owner's request (see Revision 1).
- Any other value is `400 conversations.filter.invalid`.
- It is one predicate over the newest message (the existing backward index) and its turn (the existing unique index). M6 adds the handling index.

### 3.5 Unread (row 14, M5)

- **The flag.** `unread` on list items and the detail, plus `lastReadSequence` on the detail. Unread means the newest message is beyond the reader's marker, or the reader has no marker and there is a message.
- **The endpoint.** `POST …/{id}/read { sequence }` → 204.
  - Needs `conversations.view`, and moves only the caller's own marker.
  - One SQL upsert: monotonic (`GREATEST`) and clamped to the newest message, so no `If-Match` is needed.
  - Missing or negative is `400 conversations.read.sequence.invalid`. Another workspace's conversation or a non-member gets 404; a member without `conversations.view` gets 403.
- **Replying marks it read** for the sender, up to the reply. This runs last, after the delivery is queued and the announcement sent, so it can never block a delivery; a replayed key repeats it.

### 3.6 Contract table

| Endpoint | Change | New fields or parameters | Permission | Backward compatible |
| --- | --- | --- | --- | --- |
| `GET /api/tenants/{t}/conversations` | Customer and unread on each item; opt-in filter | `customer{id,displayName,labelCode,reference,shortCode}`, `unread`; query `filter=needs-person` | `conversations.view` (unchanged) | Yes: fields are added, and without `filter` the same rows and total come back. An unknown `filter` is now 400 (it used to be ignored); the current frontend never sends one |
| `GET /api/tenants/{t}/conversations/{id}` (and the bodies of `take-over`, `return-to-ai`, `assignment`, which return the detail) | Customer, unread, read marker | `customer{…}`, `unread`, `lastReadSequence` | `conversations.view` | Yes: added fields only; ETag unchanged |
| `GET /api/tenants/{t}/conversations/{id}/messages` | Newest-first reading; events | query `tail`, `before`, `include=events`; fields `previousCursor` (always), `events[]` (only with `include`) | `conversations.view` | Yes: without the new parameters the same items and `nextCursor`; `previousCursor` is an added field; `events` is absent. `after` with `tail`/`before`, or an unknown `include`, is a new 400 on parameters nobody sends today |
| `POST /api/tenants/{t}/conversations/{id}/read` | New | body `{ sequence }` → 204 | `conversations.view` | Yes: new endpoint |
| `POST …/take-over`, `POST …/return-to-ai` | Also store a handling event when handling changes | none | `conversations.view` + `conversations.handoff` (unchanged) | Yes: same statuses, `If-Match`/412/428 unchanged |
| `POST …/{id}/messages` (reply) | Also marks the conversation read for the sender | none | `conversations.view` + `conversations.reply` (unchanged) | Yes: same response; idempotency key unchanged |
| Problem responses | Four new codes (`problem-codes.md`) | `conversations.filter.invalid`, `transcript.cursor.conflict`, `transcript.include.invalid`, `conversations.read.sequence.invalid` | — | Yes: new refusals only on new parameters |
| Public chat transports | None | none | — | Unchanged; a test proves `include=events` changes nothing there |

OpenAPI: the generated document (`<evidence>\live\openapi-v1.json`) lists the new parameters, the `read` route and every new field. The changed endpoints carry `WithSummary`, `Produces` and `ProducesProblem`. Architecture documents updated:
- `human-handoff-realtime-inbox.md` (new §14, §12 note, limitations);
- `unified-customer-identity.md` (new C9a);
- `problem-codes.md` (Conversations section).

### 3.7 Migrations (generated with `dotnet ef migrations add`, none edited, in the plan's order)

| Name | Model change | Rendered SQL | Tested |
| --- | --- | --- | --- |
| M6 `20261101095238_AddConversationHandlingIndex` | `ix_conversations_tenant_handling_activity (tenant_id, handling, last_activity_at)` | `<evidence>\sql\AddConversationHandlingIndex.sql` | Fresh database (whole chain) and copy of `omniai_p36`; then applied to `omniai_p36` |
| M4 `20261101095356_AddConversationHandlingEvents` | Table `conversation_handling_events` exactly as plan §3.3 (tenant FK Restrict without convention index; conversation FK Cascade; composite actor-membership FK Restrict without index; `after_sequence >= 0` check; `ix_conversation_handling_events_conversation`) | `<evidence>\sql\AddConversationHandlingEvents.sql` | Same; constraint probes refused a negative sequence and a non-member actor |
| M5 `20261101095544_AddConversationReads` | Table `conversation_reads` exactly as plan §3.3 (`pk_conversation_reads (conversation_id, user_id)`, conversation FK Cascade, composite member FK Cascade, `last_read_sequence >= 0`, `ix_conversation_reads_member`) | `<evidence>\sql\AddConversationReads.sql` | Same; probes refused a negative marker and a non-member reader |

Also in `<evidence>\sql\`: `p37-4-all-idempotent.sql` and two `pg_dump` backups of `omniai_p36` (before the seed, and before the migration). The copy kept every row (970 conversations, 3,915 messages and 1,965 turns, before and after). `has-pending-model-changes`: "No changes have been made to the model since the last migration."

**How three ordered migrations were generated from one code change:** the repository code needs all three tables to compile. So `OnModelCreating` briefly carried `builder.Ignore<ConversationHandlingEvent>()` and `builder.Ignore<ConversationReadRow>()` while M6 was generated. The first line was removed before M4 and the second before M5. Both lines are gone; the generated files are untouched.

## 4. Decisions

- **Unread is the plan's definition (newest message of any sender beyond the marker).** The prompt's sub-bullet says "customer messages the reader has not seen" but also "as the approved plan defines it", so rule 19 applies. The plan's flag is a superset: an unseen customer message always makes the conversation unread. A colleague's or the AI's newer message does too.
- **One failure reason code, `generation_failed`.**
  - A turn stores only that it failed. The provider error is deliberately never persisted, and the plan approves no column for a cause.
  - Finer codes (provider unavailable, invalid response, knowledge unavailable) would need a new column on `conversation_turns`. That migration is not in the plan, so it is not built (see §8).
- **Event shape:** the plan's `{ kind, at, afterSequence, actor }`, plus:
  - `reason`, which the prompt asks for;
  - `id`, so a client can de-duplicate an event served on both sides of a forward page boundary.
- **Newest-first is the plan's `tail`/`before`**, not the prompt's example `order=desc`. Pages stay ascending, so the current merge-by-sequence code needs no change.
- **`tail=true` with `before`** reads before the cursor; only `after` with either is refused.
- **Validated before authorisation:** an unknown `filter`/`include` and conflicting cursors are refused before membership is checked. The answer is the same for anybody, so it reveals nothing.
- **`previousCursor` on forward reads** costs one indexed `EXISTS`, and only when `after` is present. The default first page asks nothing more.
- **The read marker** lives in the inbox store as one SQL upsert, on an Infrastructure row (`ConversationReadRow`), not a Domain entity: its only rule is "forward, clamped", and only one statement can enforce it without a race. It takes no lock and announces nothing (private to one member).
- **The WhatsApp reference comes from `whatsapp_contacts` by conversation** (the plan), not from the customer's identities: it is the number that conversation runs on.
- **Seeding for latency:** 900 website-chat conversations were added to Northwind through the real public API with the local seeding limits, as the prompt allows. Each had 1–3 customer messages; 90 ended in a `[[fail]]` turn and 90 were taken over by the owner. The baseline was taken *after* the seed, so before and after use identical data (961 conversations).
- **Deferred:** finer failure reasons; the D-5 gap in §8; anything in the frontend (Phase 38).

## 5. Verification

Backend gates ran in Docker as in the Phase 33.5 report §11: `mcr.microsoft.com/dotnet/sdk:10.0` with the repository mounted, and a throwaway `pgvector/pgvector:pg17` and `redis:7-alpine` through `OMNIAI_TEST_POSTGRES` / `OMNIAI_TEST_REDIS`, all removed afterwards. Free memory before the backend tests 8.5 GB, before `test:ci` 9.0 GB, before `build` 9.0 GB. Every gate ran alone.

| Check | Result |
| --- | --- |
| `dotnet restore` (NuGet audit on) | PASS |
| `dotnet build OmniAI.slnx -c Release` | PASS, **0 warnings, 0 errors** |
| `dotnet list package --vulnerable --include-transitive` | PASS, no vulnerable packages in any of the 8 projects |
| Tests, `OmniAI.Api.IntegrationTests` | PASS, 969 passed, 2 skipped |
| Tests, `OmniAI.Application.Tests` | PASS, 820 passed |
| Tests, `OmniAI.Domain.Tests` | PASS, 838 passed |
| Tests, `OmniAI.Infrastructure.IntegrationTests` | PASS, 1,122 passed, 1 skipped |
| Total | **3,749 passed, 3 skipped, 0 failed** (37.3: 3,682); 0 leftover test databases |
| `DatabaseModelAuthorityTests` | PASS (in the run above, and in the targeted run) |
| M6, M4, M5 on a fresh database | PASS, the whole chain of 9 migrations |
| M6, M4, M5 on a copy of `omniai_p36` | PASS, 3 applied forward; row counts unchanged; 4 constraint probes refused |
| `omniai_p36` migrated (after `pg_dump`) | PASS, 9 migrations applied |
| Frontend `npm run format:check` | PASS |
| Frontend `npm run test:ci` | PASS, 96 files, 1,166 tests |
| Frontend `npm run build` | PASS |
| Frontend `npm run i18n:check` | PASS, 2,075 messages, 0 errors, 0 warnings |
| API checks against the dev stack (`<evidence>\live\api-checks.json`) | PASS, **28/28**. Covers: the customer on every row, Crockford codes, stable codes; the 232-message conversation read newest-first and paged back to the start (3 pages, every message once, in order); stand-in `[[fail]]` → 503 → `AiTurnFailed` after its message with `generation_failed`; takeover and return events with the actor; idempotent press 200, stale 412, missing 428; the public transcript without events; needs-person 125 of 962, every row open and customer-last; read 204, clamped, monotonic, re-unread on a new customer message; the four 400 codes; the analyst (no `conversations.view`) 403 on all four new reads; another workspace 404 through ours and theirs, with its own filtered list empty |
| Chrome (chrome-devtools-direct MCP, isolated context), English | PASS. Inbox (962 conversations, paging, live updates) and transcript render as before. **Take over → reply → Return to AI** with the product's buttons; from the same session, `include=events` showed `TakenOver` and `ReturnedToAi` with the owner, and the reply set `unread=false` with `lastReadSequence` equal to the reply's sequence. Screenshot `en-01-inbox-transcript-after-reply.png` |
| Chrome, Arabic (`lang=ar`, `dir=rtl`) | PASS. Inbox and transcript render right to left. Takeover and return through «استلام المحادثة» / «إعادتها إلى الذكاء الاصطناعي» produced their events; `filter=bogus` → 400 `conversations.filter.invalid`. The only console error was that deliberate 400. Screenshots `ar-01-…`, `ar-02-…` |
| p95 latency, 961 Northwind conversations, 300 sequential reads after 30 warm-up (`<evidence>\latency\`) | See the table below |
| Provider canary | PASS, before any traffic, after the seed restart, after the rebuild and at the end: every provider secret, `OMNIAI_LIVE_*` and the SMTP credentials absent; SMTP host loopback |

**p95 latency (ms)**, before = the 37.3 build on the same seeded data:

| Read | Before | After |
| --- | --- | --- |
| `GET /conversations` (page 1, 20) | 32.6 | 21.2 |
| `GET /conversations` (page 1, 50) | 31.5 | 28.7 |
| `GET /conversations?filter=needs-person` (page 1, 20) | — (new) | 33.5 |
| `GET …/{232-message}/messages` (limit 100) | 26.5 | 22.0 |
| `…/messages?tail=true` (limit 100) | — (new) | 21.3 |
| `…/messages?tail=true&include=events` (limit 100) | — (new) | 30.0 |

The "after" figures are run 3. Runs 1 and 2 started right after the API container was recreated. Only the first variant measured in each of those runs had slow outliers (p95 113 and 134 ms, p99 above 300 ms); every other variant in those runs matched run 3. An interleaved warm probe confirmed steady state: 20 rows p95 30.2, 50 rows 29.0, needs-person 34.2. All runs are kept in `<evidence>\latency\`. `EXPLAIN ANALYZE` of the needs-person count: 2.1 ms, using the backward index on messages and the turn's unique index (`explain-needs-person-count.txt`). No N+1: a list page costs 5 statements for 2 or 9 rows, and the events read costs the same with 1 or 6 failures (Infrastructure tests).

## 6. See it live

- API: `http://127.0.0.1:5027`, and the dev server at `http://localhost:4200` (both left running). Sign in as `p36.owner@omniai.example` (password in `demo-accounts.md`) and choose **Northwind Operations**.
- The current inbox at `http://localhost:4200/conversations` looks as before; Phase 38 draws the new fields. To see the new contracts, call them from the signed-in browser's devtools console (same origin), with `<t>` = the Northwind tenant id from `GET /api/me/tenants` and `<id>` = the conversation id in the address bar:
  - `GET /api/tenants/<t>/conversations?filter=needs-person`: about 125 conversations, each with `customer` and `unread`.
  - `GET /api/tenants/<t>/conversations/<id>/messages?tail=true&include=events`: the newest page and its events. The conversation `01a196ab-9bb1-786d-ae28-fa3da811bd79` (the "P37.4 check" one at the top of the list) has a failed turn and several takeovers.
  - `POST /api/tenants/<t>/conversations/<id>/read` with `{ "sequence": … }` and the `X-XSRF-TOKEN` header, then re-read the detail: `unread` turns false.
- Try a `[[fail]]` message on the hosted chat (`http://localhost:4200/chat/9veAaKzu0Ee-isA7jrQzYw`), then read the conversation with `include=events`.

## 7. Frontend follow-ups (what Phase 38 adopts)

| Adopt | Contract |
| --- | --- |
| Lead rows with the customer: `displayName`, else the label by `labelCode` with `shortCode` ("Visitor 7KQ2MX" / «زائر 7KQ2MX», "WhatsApp customer ••••1234"); drop the client copy of the label rule | `customer{ id, displayName, labelCode: Named\|WhatsAppCustomer\|AnonymousVisitor\|UnnamedCustomer, reference, shortCode }` on list items and detail |
| Open long transcripts at the newest page and load older pages on scroll | `GET …/messages?tail=true&limit=n`, then `before={previousCursor}` until null; pages stay ascending |
| Failed-turn and handoff dividers from the server, not inferred | `include=events` → `events[{ id, kind: AiTurnFailed\|TakenOver\|ReturnedToAi, at, afterSequence, actor{userId,email}\|null, reason }]`; de-duplicate by `id` |
| A "Needs a person" view with its own count | `GET …/conversations?filter=needs-person` (`total` is the filtered count) |
| Unread dots, and mark read when a transcript is seen | `unread` on rows and detail, `lastReadSequence` on detail; `POST …/conversations/{id}/read { sequence }` → 204 |
| Word the four new refusals by code | `conversations.filter.invalid`, `transcript.cursor.conflict`, `transcript.include.invalid`, `conversations.read.sequence.invalid` |

## 8. Risks and open issues

- **A D-5 gap, for your decision.** *Closed in Revision 1, at the owner's request.* Suppose a person takes a conversation over, the customer writes, and it is handed back to the AI without a reply. The customer's message is then unanswered for ever: the AI never answers retroactively. Yet it is not "needs a person", because handling is AI and that turn did not fail (it completed without a reply). The approved definition excludes it, and I did not widen it.
  - The fix is a small predicate change (also count "the last customer message's turn completed without a reply, and handling is AI") and needs no migration.
- **Failure reasons are one code.** Finer, still provider-free codes need a `failure_code` column on `conversation_turns`, which is a migration outside the approved list.
- **The needs-a-person filter is linear in a workspace's conversations,** at about 2 µs each (2.1 ms at 961). That is roughly 200 ms at 100,000 conversations. A stored "awaiting a person" flag would make it index-only later, at the cost of a migration and a write on every turn.
- **Short codes can collide.** 30 bits give about a 50% chance of one shared code among roughly 38,000 customers of one workspace. The code is a label, documented as never a key.
- **Day-one unread.** Every conversation with a message reads as unread for every member until opened (no backfill, by rule 8). Today that is the 970 dev conversations.
- **Boundary events.** On forward pages an event exactly at the boundary is served twice by design; Phase 38 must de-duplicate by `id`.
- **History starts at 37.4.** Earlier handoffs (including the 90 seeded takeovers) have no events, and none is invented.
- **Seeded data.** `omniai_p36` now holds 900 more synthetic conversations, made through the API; backups from before the seed and before the migration are in `<evidence>\sql\`.
- **A disclosure, rule 10.** Early in this step, a `grep` I ran on `demo-accounts.md` printed the synthetic local demo password into my own tool output. That password is already stored in that file. It appears in no report, kept log, screenshot or commit, and the secret scan of the evidence folder and every changed file found it nowhere.

## 9. Proposed checkpoint commit message

```
feat: show who a conversation is with, read it newest first, and mark what happened and what is unread

- Conversation rows and detail carry the customer: name, label code,
  masked WhatsApp number and a six-character code from the random end of
  the customer id (D-4, conversations.view)
- Transcripts read from the newest end with tail=true and before=, pages
  still ascending; previousCursor on every page; after with either is 400
- include=events adds failed AI turns (reason generation_failed), takeovers
  and returns to the AI with who and where; never on public transports
- filter=needs-person lists open conversations the customer spoke last in
  that a person handles or whose last AI turn failed (D-5)
- Per-member unread state and POST .../read (monotonic, clamped); a reply
  marks the conversation read for its sender
- Migrations M6 AddConversationHandlingIndex, M4
  AddConversationHandlingEvents, M5 AddConversationReads, generated and
  unedited; four new problem codes

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

## Revision 1 (2026-11-01)

### R1.1 The note

Close the D-5 gap now: a customer message must never stay unanswered without appearing anywhere. Widen `filter=needs-person` to also count an open conversation where the customer wrote last, the AI handles it, and the turn for that last customer message completed without a reply. No migration. This widens the plan's D-5 definition **at the owner's request**.

### R1.2 What changed

No migration, no contract shape change, no other behaviour change. The rest of the step is as reported above.

- **`ConversationRepository.NeedingAPerson`.** The turn test for the last customer message is now `Failed` **or** (`Completed` with no reply message). A turn completes without a reply only while a person handles the conversation. While a person still handles it, the first branch already counts the conversation, so the new branch needs no handling condition of its own. It is the same single predicate on the same indexes.
- **Tests.**
  - API `InboxStateApiTests.A_message_left_unanswered_by_a_return_to_the_ai_needs_a_person_until_somebody_answers`:
    - two gap conversations (take over, the customer writes, return to the AI unanswered) appear in the filter, and `total` counts them;
    - one leaves when the AI answers a new customer message; the other leaves when a person takes it back (it stays in, on the first branch) and replies;
    - the first definition's categories stay where they were: AI-answered out, failed turn in, person-replied out.
  - Infrastructure `InboxReadModelTests.A_message_left_unanswered_by_a_return_to_the_ai_needs_a_person_until_closed`: the same case is in the filter while open and out once closed.
  - The original needs-a-person and statement-count tests are unchanged and pass.
- **Docs.** `human-handoff-realtime-inbox.md` §14.4 now lists the three branches and records the widening at the owner's request. §3.4 and §8 of this report are updated, and the commit message is replaced below.

### R1.3 Verification

| Check | Result |
| --- | --- |
| `dotnet restore` (audit on) / vulnerable packages | PASS / PASS, none in any of the 8 projects |
| `dotnet build OmniAI.slnx -c Release` | PASS, **0 warnings, 0 errors** |
| Tests: Api / Application / Domain / Infrastructure | PASS: 970 (+2 skipped) / 820 / 838 / 1,123 (+1 skipped). **3,751 passed, 3 skipped, 0 failed**; 0 leftover test databases; throwaway containers removed |
| Frontend `format:check` / `test:ci` / `build` / `i18n:check` | PASS / PASS (96 files, 1,166 tests) / PASS / PASS (2,075 messages, 0 errors) |
| Free memory | 7.2 GB before the backend tests. Before `test:ci` it was **5.5 GB, under the 6 GB rule**, so I waited about 150 s until 6.5 GB, then started. 7.1 GB before `build`. Every gate ran alone |
| Dev API rebuilt (`build`, `api`) and canary | PASS, after the restart and again at the end |
| Live gap case, AI stand-in (`<evidence>\live\rev1-live.json`) | PASS, **7/7**. Two conversations (stand-in answered, take-over, customer wrote, returned to the AI unanswered) entered the filter while handled by the AI, and `total` went 125 → 127 with the earlier set unchanged. One left after the stand-in answered a new message; the other left after a person took it back and replied. `total` went back to 125 |
| Existing data | The widening added no existing conversation: `total` was 125 on the same data before and after |
| p95, `GET /conversations?filter=needs-person` (page 1, 20), same seeded data (962 conversations: the 961 plus the 37.4 API-check conversation), warm, 300 reads | **31.6 ms** (before Revision 1: 33.5). Interleaved warm probe 30.7 (before: 34.2). `EXPLAIN ANALYZE` of the widened count: 3.8 ms, same indexes (backward index scan on messages, unique turn lookup); files `latency-after-rev1.json`, `probe-rev1.txt`, `explain-needs-person-count-rev1.txt` |

The other reads, measured in the same run, are unchanged within noise: list 20 rows 31.6, 50 rows 32.5, transcript 21.8, `tail` 22.2, `tail` with events 20.6.

### R1.4 Proposed checkpoint commit message (replaces §9)

```
feat: show who a conversation is with, read it newest first, and mark what happened and what is unread

- Conversation rows and detail carry the customer: name, label code,
  masked WhatsApp number and a six-character code from the random end of
  the customer id (D-4, conversations.view)
- Transcripts read from the newest end with tail=true and before=, pages
  still ascending; previousCursor on every page; after with either is 400
- include=events adds failed AI turns (reason generation_failed), takeovers
  and returns to the AI with who and where; never on public transports
- filter=needs-person lists open conversations the customer spoke last in
  that a person handles, whose last AI turn failed, or that went back to
  the AI with that message unanswered (D-5, widened at the owner's request)
- Per-member unread state and POST .../read (monotonic, clamped); a reply
  marks the conversation read for its sender
- Migrations M6 AddConversationHandlingIndex, M4
  AddConversationHandlingEvents, M5 AddConversationReads, generated and
  unedited; four new problem codes

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```
