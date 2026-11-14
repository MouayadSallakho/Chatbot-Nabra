# Phase 39.1: what the redesign needs from the backend

Display names, a greeting per language, mark all read, `filter=mine`, the masked number on the customer directory, the email preferences schema, and "Answered by the AI" (BA39-1). The approved plan is section 3.5 of `PHASE_39_0_REDESIGN_PLAN_REPORT.md`, with the owner's decisions in its section 12.

Written 2026-11-14 on top of `e79b5e6`. The session limit interrupted this step at about 22:44 on 13 November, and it was resumed from the worktree, which was intact. Nothing is committed.

## 1. Summary

- **Names.** Each person can give themselves a display name, and the inbox, transcripts, assignees, team and invitations show it beside the email. Visitors still read "Support".
- **Greetings.** A website chat can greet English and Arabic visitors in their own language. The existing greeting stays the fallback.
- **Mark all as read.** One request marks every conversation in the workspace as read for the caller, and the caller's other tabs update. A count says beforehand how many conversations it will mark.
- **"Mine".** The inbox can list the conversations assigned to the caller or taken over by them.
- **Directory and overview.** The customer directory shows the same masked WhatsApp number as the inbox. The overview can say how many conversations the AI answered with nobody stepping in, for this period and the one before.

## 2. Starting point

| Item | Value |
| --- | --- |
| Branch | `master` |
| HEAD | `e79b5e6 docs: plan the Calm Glass redesign (Phase 39.0)` |
| Worktree at the start | clean (`git status --short` empty) |
| Resumed | 2026-11-14, from the same worktree; `git status --short` matched the work in progress, and the evidence folder, Docker and the dev stack were intact (ready 200, dev server 200) |

## 3. Changes by area

`git status --short` (69 modified files, 1,235 insertions and 109 deletions in tracked files, plus these new files):

```text
 M backend/src/OmniAI.Api/{Analytics/AnalyticsContracts, Auth/AuthContracts, Auth/AuthEndpoints, Auth/MeEndpoints,
     Composition/CompositionRoot, Conversations/ConversationEndpoints, Customers/CustomerEndpoints,
     ErrorHandling/DomainProblemDetailsHandler, Http/OpenApiContract, Notifications/NotificationEndpoints,
     Realtime/RealtimeGroups, Team/TeamContracts, Team/TeamEndpoints, WebChat/EmbeddedChatEndpoints,
     WebChat/HostedChatEndpoints, WebChat/WebChatEndpoints, WebChat/WidgetContracts}.cs
 M backend/src/OmniAI.Application/{Accounts/AccountPorts, Analytics/WorkspaceAnalytics,
     Conversations/ConversationInboxExceptions, Conversations/Persistence/IConversationInboxStore,
     Conversations/Persistence/IConversationRepository, Conversations/ReadConversationsHandler,
     Customers/CustomerContracts, Customers/CustomerHandlers, Customers/Persistence/ICustomerRepository,
     Invitations/ListTenantInvitations/ListTenantInvitationsHandler, Invitations/TenantInvitationView,
     Members/TenantMemberView, Members/TenantMembers, Security/IAccountDirectory,
     WebChat/Embedded/EmbeddedWebChatHandler, WebChat/Hosted/StartWebChatConversationHandler,
     WebChat/UpdateWebChatWidget/UpdateWebChatWidgetHandler, WebChat/WebChatWidgetView}.cs
 M backend/src/OmniAI.Domain/{DomainValidationException, WebChat/WebChatWidgetSettings}.cs
 M backend/src/OmniAI.Infrastructure/{Accounts/AccountEmailDispatcher, Accounts/AccountServices,
     Analytics/WorkspaceAnalyticsReader, Identity/IdentityAccountDirectory, Identity/OmniAiUser,
     Persistence/Configurations/IdentityConfiguration, Persistence/Configurations/WebChatConfiguration,
     Persistence/Migrations/OmniAiDbContextModelSnapshot, Persistence/Repositories/ConversationInboxStore,
     Persistence/Repositories/ConversationRepository, Persistence/Repositories/CustomerRepository,
     Persistence/Repositories/TenantMemberRepository}.cs
 M backend/tests/... (AuthEndpointsTests, OpenApiDocumentContractTests, InboxHubClient, InboxStateApiTests,
     ConversationInboxDoubles, InboxStateTests, TeamTestDoubles, WorkspaceAnalyticsInsightsTests,
     MigrationSchemaTests, WebChatWidgetSchemaTests)
 M docs/architecture/{analytics-usage-foundation, authentication-tenant-membership,
     conversation-runtime-website-chat, database-model-authority, embeddable-web-chat-widget,
     human-handoff-realtime-inbox, operational-notifications, problem-codes, team-members-invitations,
     unified-customer-identity}.md
?? backend/src/OmniAI.Api/Realtime/SignalRConversationReadRealtime.cs
?? backend/src/OmniAI.Api/WebChat/PageLanguage.cs
?? backend/src/OmniAI.Application/Conversations/IConversationReadRealtime.cs
?? backend/src/OmniAI.Application/Conversations/MarkAllConversationsReadHandler.cs
?? backend/src/OmniAI.Domain/Users/MemberDisplayName.cs
?? backend/src/OmniAI.Infrastructure/Persistence/Migrations/20261113192144_AddUserDisplayName{,.Designer}.cs
?? backend/src/OmniAI.Infrastructure/Persistence/Migrations/20261113192259_AddWebChatGreetingPerLanguage{,.Designer}.cs
?? backend/tests/OmniAI.Api.IntegrationTests/Accounts/DisplayNameApiTests.cs
?? backend/tests/OmniAI.Api.IntegrationTests/Analytics/AnsweredByAiApiTests.cs
?? backend/tests/OmniAI.Api.IntegrationTests/WebChat/GreetingPerLanguageApiTests.cs
?? backend/tests/OmniAI.Api.IntegrationTests/WebChat/MarkAllReadAndMineApiTests.cs
?? backend/tests/OmniAI.Api.IntegrationTests/WhatsApp/CustomerReferenceApiTests.cs
?? backend/tests/OmniAI.Application.Tests/Conversations/MarkAllConversationsReadTests.cs
?? backend/tests/OmniAI.Domain.Tests/Users/MemberDisplayNameTests.cs
?? backend/tests/OmniAI.Domain.Tests/WebChat/WebChatGreetingPerLanguageTests.cs
?? docs/phase-reports/PHASE_39_1_REDESIGN_BACKEND_REPORT.md
```

Nothing under `frontend/` or `deploy/` changed. No package was added, upgraded or removed.

### BA39-1.1 A display name for each member (plan 3.5.1, note 7)

- **Model.** `OmniAiUser.DisplayName` maps to `users.display_name varchar(80) null`. The migration is `AddUserDisplayName`, generated, with no backfill. The name belongs to the account, not to a workspace.
- **Rules.** They live in the new domain value object `MemberDisplayName`:
  - The name is trimmed, and every inner run of whitespace becomes one space.
  - A blank name clears it.
  - It may have up to 80 UTF-16 code units.
  - Control characters (`Cc`) and format characters (`Cf`) are refused, except ZWNJ and ZWJ.
- **Edit.** `PUT /api/me/display-name` answers 204. It works on the caller's own account, with antiforgery, no `If-Match` and no permission. There is no route that names another account. The value is written by `IAccountDisplayNames` in one `ExecuteUpdate`.
- **Reads.**
  - `CurrentUserResponse` (on `me` and `session`) gains `displayName`.
  - `WorkspaceMemberResponse` gains `displayName`. That one record covers `assignedTo`, `takenOverBy`, transcript `sentBy`, event `actor` and the assignees list.
  - `MemberSummaryResponse` and `MemberResponse` gain `displayName`.
  - `InvitationResponse` gains `invitedByDisplayName`.
  - Each read takes the name in the same query that reads the email. For invitations that query is the new `IAccountDirectory.FindPeopleAsync`, one statement per page, which replaces `FindEmailsAsync` there.
  - The email stays everywhere.

### BA39-1.2 A greeting per language (plan 3.5.2, note 9)

- **Model.** `WebChatWidgetSettings` gains `GreetingEn` and `GreetingAr`, on the columns `widget_greeting_en` and `widget_greeting_ar` (`varchar(300) null`). The migration is `AddWebChatGreetingPerLanguage`, generated, with no backfill. `GreetingFor(language)` returns that language's own greeting, else the fallback.
- **Admin.**
  - `GET` and `PUT …/widget` carry `widget.greetings { en, ar }`.
  - The request gains an optional `greetings`. If it is absent or null, the stored greetings are kept. If it is present, both are set, and null or blank clears one.
  - Permission and `If-Match` are unchanged.
  - Refusals keep their codes and add `field`: `greeting`, `greetings.en` or `greetings.ar`.
- **Public.** The hosted and embedded config reads take an optional `lang`, read by `PageLanguage`:
  - `en` or `ar`, in any letter case and with spaces trimmed, picks that language. Anything else, or nothing, gives the fallback.
  - An unusual `lang` is never refused.
  - No member is added to either response.

### BA39-1.3 Mark all read (plan 3.5.3, note 6)

- **Routes.**
  - `POST …/conversations/read-all` answers `{ marked }`.
  - `GET …/conversations/unread-count` answers `{ count }`.
  - Both need `conversations.view`, through the new `MarkAllConversationsReadHandler`.
- **Statement.** It is one `INSERT … SELECT … CROSS JOIN LATERAL (newest message) … ON CONFLICT DO UPDATE … WHERE newer`:
  - It is idempotent, and a marker never moves back.
  - Conversations without messages are skipped.
  - `marked` equals the number of conversations that were unread.
  - `unread-count` uses the list's own unread rule in one statement.
- **Realtime.** There is a new port, `IConversationReadRealtime`, implemented by `SignalRConversationReadRealtime`:
  - `conversationsRead` goes to the caller's member group, with the workspace id as the only payload, and only when `marked > 0`.
  - It never fails the write: the transport logs and swallows its own errors, and the handler swallows anything that escapes.

### BA39-1.4 The masked number on the customer directory (plan 3.5.4, note 18)

- `CustomerRowResponse` and `CustomerResponse` gain `reference`. It is the newest WhatsApp identity masked by `WhatsAppCustomerId.Masked` (`••••1234`), or null.
- The list reads it in one statement per page through `ix_whatsapp_customer_identities_customer`. The detail takes it from the identities it already reads, with no extra query.
- There is no migration, and the permission is unchanged.

### BA39-1.5 The email preferences schema (plan 3.5.5, note 19)

- `EmailPreferencesContract` is split into `EmailPreferencesRequest` (`bool?`) and `EmailPreferencesResponse` (`bool`, required).
- The JSON on the wire and the behaviour are unchanged.

### BA39-1.6 `filter=mine` (plan 3.5.6, Q1, BA39-5)

- `ConversationListFilter.Mine` keeps the conversations assigned to the reader or taken over by the reader.
- It uses the same takeover expression that `takenOverBy` publishes.
- Any letter case is accepted, and `total` counts the filtered set. Any other value is still `400 conversations.filter.invalid`; only its English detail now names `mine` too.

### BA39-1.7 "Answered by the AI" (Q7 (c), BA39-1)

- **Built.** It is one aggregate over stored data, with no migration and no new event.
- **Field.** `summary.conversationsAnsweredByAi`, and with `compare=previous` also `previous.summary.conversationsAnsweredByAi`.
- **Definition.** The conversations started in the period in which:
  - the AI replied;
  - no person replied;
  - no person took over, meaning there is no `TakenOver` event and the conversation is not with a person now.
- **Cost.** One more counting statement per period.
- **Exactness.** See section 4, decision 6.

### Supporting changes

- `DomainValidationException` gains optional `Field`, `Limit` and `LimitUnit`. The problem writer publishes them as `field`, `limit` and `limitUnit`, and only when they are set.
- The OpenAPI document gains:
  - the `field` member on `ProblemDetails`;
  - the declared bodies of the member list and member detail (`.Produces`, metadata only);
  - the new routes and schemas.
- `problem-codes.md` and nine architecture docs describe each change (listed above).

### Contract table

| Endpoint or event | Change | New fields, parameters or codes | Permission | Why it is backward compatible |
| --- | --- | --- | --- | --- |
| `PUT /api/me/display-name` | new route | body `{ displayName }`; 204; `account.display_name.too_long` (with `limit`, `limitUnit`), `account.display_name.control_characters` | signed in, own account | a new route |
| `GET /api/auth/me`, `GET /api/auth/session` | new member | `displayName` (nullable) | unchanged | added beside `email`; nothing removed |
| `WorkspaceMemberResponse`: `assignedTo`, `takenOverBy`, `sentBy`, `actor`, `GET …/conversations/assignees` | new member | `displayName` (nullable) | `conversations.view` | added; `email` kept |
| `GET …/members`, `GET …/members/{id}`, `PUT …/members/{id}/roles` | new member | `displayName` (nullable); bodies declared in the document | `members.view` / unchanged | added |
| `GET …/invitations`, `POST …/invitations` | new member | `invitedByDisplayName` (nullable) | unchanged | added beside `invitedByEmail` |
| `GET` / `PUT …/website-chat/{id}/widget` | new members | `widget.greetings { en, ar }`; optional request `greetings`; `field` on `website-chat.greeting.*` | `website-chat.view` / `.manage`, `If-Match` | an absent `greetings` keeps the stored values; existing codes unchanged |
| `GET /api/public/webchat/{key}`, `GET /api/public/embed/{key}` | new parameter | optional `lang=en\|ar` | anonymous, unchanged limits | no `lang` gives today's answer; no new member |
| `POST …/conversations/read-all` | new route | `{ marked }` | `conversations.view` | a new route |
| `GET …/conversations/unread-count` | new route | `{ count }` | `conversations.view` | a new route |
| `conversationsRead` (hub `/hubs/workspace-inbox`) | new event | payload: the workspace id | the member's own group | a new event name; clients that don't know it ignore it |
| `GET …/conversations?filter=mine` | new filter value | `mine` | `conversations.view` | `needs-person` and refusals unchanged; `mine` was a 400 before |
| `GET …/customers`, `GET …/customers/{id}` | new member | `reference` (nullable, masked) | `customers.view` | added |
| `EmailPreferencesContract` | schema split | `EmailPreferencesRequest`, `EmailPreferencesResponse` | unchanged | same JSON; only the schema names in the document changed |
| `GET …/analytics/dashboard` | new member | `summary.conversationsAnsweredByAi` (and in `previous.summary`) | `analytics.view` | added |
| Problem bodies | new member | `field`; `limit`/`limitUnit` on the display-name refusal | n/a | optional members; codes unchanged |

## 4. Decisions

1. **`field`, `limit` and `limitUnit` come from the domain exception.** They are optional init properties on `DomainValidationException`, so the value object names its own bound and the handler names its field. I rejected writing a separate `Results.Problem` in the endpoint: it would have duplicated the rule.
2. **One read for the inviter's address and name.** `IAccountDirectory.FindPeopleAsync` returns both together. I rejected a second `FindDisplayNamesAsync` call because it would add a round trip per page.
3. **`conversationsRead` uses its own port**, `IConversationReadRealtime`, modelled on `INotificationRealtime`. Widening `IConversationRealtimeNotifier`, which every channel uses, would have touched every caller and its test doubles. The event goes to the member group that `SubscribeNotifications` joins. The frontend's one connection already calls it (`workspace-realtime.ts:325`), so 39.4a only adds a handler.
4. **The customer list uses one more statement per page, not a join.** The plan said "one join". The directory's page is assembled from separate per-page statements (ids, names, activity, channels), and the reference follows that pattern: one statement per page through an existing index, so still no N+1. The p95 of the page is flat (18.7 → 19.1 ms).
5. **An odd `lang` is never refused.** A refusal on a public read could tell a valid key from an invalid one. An unknown or over-long `lang` (more than 16 characters) is read as "no language" and gets the fallback.
6. **BA39-1 is built** (owner decision Q7 (c)), because every input is stored data:
   - AI replies and person replies are messages;
   - takeovers are handling events, or the conversation being with a person now.

   It needs one aggregate, no migration and no new event. **It is exact on every database that was created with the `AddConversationHandlingEvents` migration (Phase 37.4) applied, which includes every deployment.** The one gap exists only in development databases that held conversations before that migration: a conversation taken over and handed back before it, with no reply from the person, left no record, so it counts as answered by the AI. The dev database `omniai_p36` is such a database. Its "answered by the AI" figure (822 of 1,020 started in the last 30 days) may therefore include a few conversations like that.
7. **The display name collapses whitespace before it applies the control-character rule.** A pasted line break becomes a space instead of a refusal. That is the plan's order. Tabs and line breaks are whitespace, while invisible marks such as U+200E are refused.
8. **Deferred: the reply POST's own `sentBy`.** `POST …/conversations/{id}/messages` has always answered `sentBy: null`, and it still does. The transcript read names the author, with the display name. Filling the POST's answer is a behaviour change outside the plan, so it is listed in section 8.
9. **Section 6 of the prompt (the public name)** does not apply, as the operator note says. `brand.md` keeps the name, and no backend text changed for it.

## 5. Verification

| Check | Result |
| --- | --- |
| Backend restore with the NuGet audit on | **PASS**: restore exit 0, no NU19xx |
| `dotnet build OmniAI.slnx -c Release` | **PASS**: 0 warnings, 0 errors |
| `dotnet list package --vulnerable --include-transitive` | **PASS**: no vulnerable packages in any of the 8 projects |
| `dotnet test`, run 2 (`-m:1`, sdk:10.0, throwaway pgvector pg17 + redis 7, removed; 0 leftover databases) | **PASS**: Api.IntegrationTests 1,192 passed, 2 skipped; Application.Tests 920; Domain.Tests 939; Infrastructure.IntegrationTests 1,310 passed, 1 skipped. **4,361 passed, 3 skipped, 0 failed** (38.7b: 4,288) |
| `dotnet test`, run 3 (same command, after the report was drafted, to see whether run 1's 503 cluster recurs) | **PASS**: identical counts, 4,361 passed, 3 skipped, 0 failed, 0 leftover databases (`gates\backend-gates-run3.log`) |
| `dotnet test`, run 1 (same command, before run 2) | **FAIL**, then explained. (a) 14 tests in `HostedChatApiTests`, `PublicChatProtectionApiTests`, `WorkspaceGenerationBudgetApiTests` and `ClientNetworkIdentityApiTests` got `503` within one 4-second window early in the run; even the antiforgery bootstrap was refused. None of them touches a changed contract, and all passed in run 2. I read it as a start-up burst on the shared throwaway Redis or PostgreSQL. (b) 3 tests pinned exactly what this step changes approvedly: `me` now has four fields, the migration list two more entries, and the comparison four counting statements. They were updated (section 3) before run 2. Logs: `gates\backend-gates-run1.log`, `gates\run1\test.log` |
| Migrations generated, never edited | **PASS**: `dotnet ef migrations add` in the SDK container; the rendered SQL is one `ALTER TABLE … ADD` per column (`sql\AddUserDisplayName.sql`, `sql\AddWebChatGreetingPerLanguage.sql`) |
| Migrations on a fresh database | **PASS**: all 15 applied, in order |
| Migrations on a copy of the dev database | **PASS**: 13 → 15; the row counts are unchanged (97 users, 55 endpoints, 2 with a greeting, 1,218 conversations); the new columns are nullable and empty |
| `has-pending-model-changes` | **PASS**: no changes |
| `DatabaseModelAuthorityTests` and `MigrationSchemaTests` | **PASS** (in run 2) |
| `pg_dump` of `omniai_p36` before migrating | **PASS**: `sql\omniai_p36-before-p39-1.dump`, 1,288,200 bytes |
| Dev database migrated forward | **PASS**: `p36-stack.sh migrate`; status reports 15 migrations, ready 200 |
| Dev API rebuilt and recreated (`build`, `api`) | **PASS**: exit 0; stand-in on, fallback plan free |
| Provider canary | **PASS** before any traffic: at the resume, after the rebuild, and before the e2e run. Every provider variable absent, SMTP host loopback, stand-in listed as info |
| Live API checks (`scratch\live-checks.mjs`, `live\live-checks.json`) | **PASS**: 58 of 58. Covers each new field, route, code and event, with the refusals for a missing permission (support without `website-chat.manage`: 403; analyst without `conversations.view`: 403 on both new routes) and for another workspace (a new synthetic rival: 404 on every Northwind route). `conversationsRead` was heard on the owner's real hub connection (one event, payload = the workspace id), not on a colleague's. Runs 1 to 3 had one failure each, in my own check: a "no six-digit run" scan matched digits inside UUIDs, microsecond timestamps and an all-digit short code. Their logs are kept as `live-checks-run1..3.log` |
| `npm run e2e`, run 1 (unchanged frontend, rebuilt API) | **PASS**: 46 of 46 in 3.0 min (23:32 to 23:35 UTC) |
| `npm run e2e`, run 2 (16 min after run 1 ended) | **PASS**: 46 of 46 in 2.7 min (23:51 to 23:54 UTC), throttling journey included, canary run just before (§5.1) |
| `format:check`, `test:ci`, `build`, `i18n:check` | **NOT RUN**: no file under `frontend/` changed, so nothing they check changed |
| Free memory before each heavy gate | backend run 1: 14.68 GB; run 2: 12.96 GB; run 3: 12.29 GB; e2e run 1: 12.43 GB; e2e run 2: 11.94 GB. Never under 6 GB, so the page cache was never dropped. `test:ci` and `build` were not run |

**p95 latency on the seeded dev data** (owner, Northwind, 200 interleaved rounds after a warm-up; `latency\latency-before.json` on the old build, `latency-after.json` on the rebuilt API; Northwind had 1,018 conversations before and 1,020 after):

| Read | Before p95 (ms) | After p95 (ms) |
| --- | --- | --- |
| `GET …/conversations` (page 1, 20) | 29.2 | 24.0 |
| `GET …/conversations?filter=needs-person` | 23.7 | 27.2 |
| `GET …/conversations/assignees` | 25.9 | 25.3 |
| `GET …/customers` (page 1, 20) | 18.7 | 19.1 |
| `GET …/members` (page 1, 20) | 28.6 | 27.2 |
| `GET …/invitations` (page 1, 20) | 24.2 | 24.8 |
| `GET …/analytics/dashboard?range=30d&compare=previous` | 56.1 | 60.6 (one more counting statement in each period) |
| `GET …/conversations?filter=mine` (new) | n/a | 30.1 |
| `GET …/conversations/unread-count` (new) | n/a | 18.6 |
| `POST …/conversations/read-all`, live (new; 20 runs, each marks 0) | n/a | 28.6 |
| `read-all` statement, worst case on a **copy of the dev database** (all 1,018 Northwind conversations unread, 20 runs, each rolled back) | n/a | **21.7** (p50 16.4); `ix_conversations_tenant_started` plus a backward index-only scan of `ix_messages_conversation_ingestion` per conversation; plan in `latency\readall-statement-copy.log` |

### 5.1 The end-to-end runs

The suite ran twice, unchanged (`frontend/` untouched at `e79b5e6`), against the rebuilt dev API on 15 migrations. `NABRA_E2E_OUT` was `<evidence>\p39-1\e2e-run1` and `e2e-run2`, and `NABRA_E2E_STATE_DIR` was `<evidence>\p39-1\tmp\e2e-state`, deleted at the end. The provider canary ran before each run.

| Run | Window (UTC, 13 Nov; 14 Nov local time) | Result |
| --- | --- | --- |
| 1 | 23:32:29 to 23:35:28 | 46 passed (3.0 min), en-LTR and ar-RTL |
| 2 | 23:51:49 to 23:54:35 (16 min after run 1) | 46 passed (2.7 min), including `sign-in-throttled` in both languages |

Logs: `gates\e2e-run1.log` and `gates\e2e-run2.log`. No journey needed a change: no contract of this step changed anything the current frontend reads.

## 6. See it live

The dev stack runs the rebuilt API: http://localhost:4200 (dev server) and http://127.0.0.1:5027 (API). Sign in as the **Owner** `p36.owner@omniai.example`, the **Support agent** `p36.support@omniai.example` or the **Analyst** `p36.analyst@omniai.example`. The passwords are in `omniai-redesign-evidence\p36-01\demo-accounts.md` and are not copied here. The frontend is unchanged, so the new fields show only in the network panel or in direct API calls, until the redesign steps adopt them.

- **Display name.** In the browser console of a signed-in tab, send `PUT /api/me/display-name` with `{"displayName":"  Your   Name "}` (with the `X-XSRF-TOKEN` header, as the app does). The answer is 204.
  - `GET /api/auth/me` then shows `"displayName":"Your Name"` beside `email`.
  - `GET /api/tenants/{id}/members` and `GET /api/tenants/{id}/conversations/assignees` show it on your row.
  - A name of 81 characters answers `400` with `code: account.display_name.too_long`, `limit: 80` and `limitUnit: "characters"`.
  - `null` clears the name. The owner's name is null today: the live checks set it and then cleared it.
- **Greeting per language.** `GET /api/tenants/{id}/website-chat/{endpointId}/widget` now has `widget.greetings: { en: null, ar: null }`.
  - After a `PUT` with `greetings`, open http://localhost:4200/api/public/webchat/9veAaKzu0Ee-isA7jrQzYw?lang=ar to read the Arabic greeting, `?lang=en` for English, and no `lang` for today's greeting.
  - A greeting of 301 characters answers `400 website-chat.greeting.too_long` with `field: "greetings.ar"` (or `greetings.en`, or `greeting`).
- **Mark all read.**
  - `GET /api/tenants/{id}/conversations/unread-count` answers `{"count":n}`.
  - `POST …/conversations/read-all` answers `{"marked":n}`, and a second call answers `{"marked":0}`.
  - With two tabs open, the frontend's hub connection receives `conversationsRead` (DevTools → Network → WS → `/hubs/workspace-inbox` → Messages); the tab does not use it yet. The analyst, who lacks `conversations.view`, gets `403` on both routes.
- **Mine.** `GET …/conversations?filter=mine` shows the owner's 9 conversations (assigned or taken over). `filter=everything` still answers `400 conversations.filter.invalid`.
- **Customer reference.** Each `GET …/customers` row and `GET …/customers/{id}` has `"reference"`. On the dev data it is `null` for all 1,021 customers, because the dev stack has no WhatsApp customers (WhatsApp refuses without Meta). The masked `••••1234` form is proven by `CustomerReferenceApiTests`, through the real webhook path.
- **Answered by the AI.** `GET …/analytics/dashboard?range=30d&compare=previous` shows `summary.conversationsAnsweredByAi` (822 of 1,020 on the dev data; see decision 6) and the same member under `previous.summary`.
- **The document.** http://127.0.0.1:5027/openapi/v1.json (a copy is in `openapi\openapi-after.json`) contains `EmailPreferencesRequest` and `EmailPreferencesResponse` (all five members required booleans), `MarkAllConversationsReadResponse`, `ConversationUnreadCountResponse`, the `lang` parameter on both public reads, and `field` on `ProblemDetails`.

## 7. For the redesign steps

| Step | Adopt | Contract |
| --- | --- | --- |
| 39.4a (inbox list) | "Mark all as read" with the count in the confirmation | `GET …/conversations/unread-count` → `{ count }`, then `POST …/conversations/read-all` → `{ marked }`; 403 and 404 as on every inbox route; no `If-Match` |
| 39.4a | Other tabs refresh | handle `conversationsRead` (payload: the workspace id) on the existing connection, which already joins the member group (`workspace-realtime.ts:325`); re-read the list and the unread state |
| 39.4a | The "Mine" tab | `GET …/conversations?filter=mine` (page and `total` as for `needs-person`) |
| 39.4a / 39.4b | Names on chips, bubbles, takeover lines, the "handled by" line | `displayName ?? email` from `WorkspaceMemberResponse` (`assignedTo`, `takenOverBy`, `sentBy`, `actor`, assignees). Never a gendered verb (Q8). Visitors keep "Support" / «الدعم» (Q5) |
| 39.5b (channels, widget, hosted page) | Greeting in English and in Arabic, with today's as the fallback | `widget.greetings { en, ar }`; send `greetings` on save; word `website-chat.greeting.*` by `field`. Send the page language: `/api/public/webchat/{key}?lang=` and `/api/public/embed/{key}?lang=` (the loader and the hosted page) |
| 39.6a (customers, team) | Masked number in the directory and on merge candidates | `reference` on `CustomerRowResponse` and `CustomerResponse` (null for web visitors) |
| 39.6a | Team list, member page and invitations with names | `displayName` on `MemberSummaryResponse` and `MemberResponse`; `invitedByDisplayName` |
| 39.6a | The member's own display name (profile dialog) | `PUT /api/me/display-name`; read `displayName` from `GET /api/auth/session`; word `account.display_name.too_long` (with `limit`, `limitUnit`) and `account.display_name.control_characters` |
| 39.6b (notifications) | Preferences types | `EmailPreferencesResponse` (booleans) for the read, `EmailPreferencesRequest` for the save |
| 39.7a (overview) | "Answered by the AI" ring, with this and the previous period | `summary.conversationsAnsweredByAi` and `previous.summary.conversationsAnsweredByAi` out of `conversationsStarted` (with `compare=previous`). Q7 (c) is met, so the tile ships as "Answered by the AI" and not fallback (a); its sentence says what it counts: "conversations the AI answered with nobody stepping in" |

## 8. Risks and open issues

1. **A transient cluster of 503s in backend run 1.** 14 public-chat tests got 503 within 4 seconds, all at start-up, and passed in runs 2 and 3. This step adds five test classes, so five more test hosts start in parallel on one throwaway Redis and PostgreSQL. If the cluster comes back at the checkpoint, the fix is in the test infrastructure (a higher `max_connections` on the throwaway PostgreSQL, or a shared collection for the new classes), not in the product.
2. **"Answered by the AI" on old dev data** (decision 6). It is exact on every deployment, and may slightly overcount on `omniai_p36` for conversations from before Phase 37.4.
3. **The reply POST still answers `sentBy: null`** (decision 8). A client that appends its own reply from the POST answer shows no author until it re-reads. 39.4b should re-read the transcript or use the signed-in member's own name. Filling `sentBy` in the POST's answer is a small backend change for a later step if wanted.
4. **What the live checks left on the dev data:**
   - The owner's read markers in Northwind are all at the newest message: `read-all` did what it does.
   - 4 synthetic visitor conversations ("Phase 39.1 live check: is anyone there?"), one per run of the check script.
   - 4 synthetic rival accounts (`p39-1.rival.<time>@omniai.example`), each with a workspace "P39.1 Rival (live check)".

   The owner's display name and the widget's greetings were put back as they were.
5. **No WhatsApp customers on the dev stack.** The masked `reference` can't be seen live; integration tests prove it through the real webhook path.
6. **The `conversationsRead` event reaches only connections that called `SubscribeNotifications`.** The current frontend does; any other client must too.

## 9. Proposed checkpoint commit message

```text
feat: name each member, greet visitors in their language, mark every conversation read, list mine, show the masked number on customers, type the email preferences exactly, and count conversations the AI answered alone (Phase 39.1)

Add a per-account display name (users.display_name, AddUserDisplayName) with
PUT /api/me/display-name and its two codes, read beside the email in me,
session, every conversation person, the team and invitations; English and
Arabic greetings beside the fallback (AddWebChatGreetingPerLanguage), with
greetings on the widget, field on its refusals and lang on both public
reads; POST conversations/read-all and GET conversations/unread-count in one
statement each, announced to the caller's own tabs as conversationsRead;
filter=mine; reference on the customer directory; EmailPreferencesRequest
and EmailPreferencesResponse; and summary.conversationsAnsweredByAi.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

## Revision 1 (2026-11-14)

The first checkpoint stopped at its backend gate. One test failed: `KnowledgeIndexingLiveApiTests.An_abandoned_index_request_still_settles_and_leaves_its_index`, at line 72. The test read the document, saw its last attempt `Succeeded`, and then found `readiness: "NeedsIndex"` where it expected `"Ready"` (`gates\checkpoint\test.log`). The test came with 38.7a and had passed in every run until then, including runs 2 and 3 of this step. It was not retried. This revision finds the cause and fixes it.

### The cause: a window in the product, on the read side

**How an attempt ends** (`IndexKnowledgeDocumentHandler.RunAsync`, Phase 38.7a). The order is already correct:

1. `IKnowledgeIndexStore.ReplaceIndexAsync` writes the chunks and the document's index fingerprint in one transaction. Readiness is derived from that fingerprint, so this commit makes the document `Ready`.
2. After that commit, `IKnowledgeIndexAttemptStore.SucceedAsync` sets the attempt to `Succeeded` in a separate statement.
3. Only then is `knowledgeChanged` announced.

So readiness is stored first, and the event goes out after both writes.

**How the document is read** (`GetKnowledgeDocumentByIdHandler`, `GET /api/tenants/{id}/knowledge/{documentId}`). The read is three separate statements, each under read committed: the document, then its source, then the latest attempt. That is the same order the writer commits in. If the writer's two commits land between the reader's first and last statement, the reader gets the document from before the index and the attempt from after it settled. The answer then says `Succeeded` next to "needs an index", a state that never existed in the database.

**Why it failed only sometimes.** The writer's two commits are a few milliseconds apart, right after the provider is released, and the test polls every 100 ms. One of those polls has to start its document statement before the index commit and its attempt statement after the `Succeeded` commit. The checkpoint run failed 34 seconds in, while the other test hosts in the run were still starting on the shared throwaway PostgreSQL. Each statement takes longest at that point, so the window was widest.

**Who could see it.** Any client could get this answer from this route, not only the test. That includes the knowledge screen when it re-reads after the "started" announcement. The screen corrected itself on the next announcement, but the API answer was wrong, so this is the product-window case, not test timing. No other route reads both values: `FindLatestAsync` has a single caller, and the list carries no attempt.

### The fix

`GetKnowledgeDocumentByIdHandler` now reads the latest attempt **before** the document, which is the reverse of the writer's order. Any outcome the read sees was committed after the index. The document statement starts later still, so it sees the index too. A settlement that lands between the two reads can now only give `Started` with `Ready`. That answer is true: the index is written and the outcome is not recorded yet. The settlement announcement follows it, and the next read shows `Succeeded` with `Ready`. A failed or interrupted attempt never changes readiness, so it can't give a false pair in either order. `Succeeded` with `NeedsIndex` still happens when the text was edited after a successful index, because that state is real.

The authorization check still runs first. The attempt read is scoped by the workspace as well as the document, so the earlier read reveals nothing. A missing document costs one extra indexed read before the 404. There is no migration, no contract change and no frontend change. The writer is unchanged.

**The test that fails in the window.** `KnowledgeDocumentReadTests.A_read_that_straddles_a_settlement_never_shows_succeeded_beside_needs_index` (Application.Tests). Its double answers each statement from what is committed at that moment. It lands the writer's two commits, in the writer's order, right after the reader's first statement, whichever store that statement reads. Then it asserts that a `Succeeded` attempt comes with `Ready`. On the unfixed handler it failed the same way the checkpoint did: `Expected: Ready, Actual: NeedsIndex` (`rev1\before-fix.log`). With the fix it passes, together with every other Knowledge test: 92 + 99 + 60 + 160 passed, 0 failed (`rev1\after-fix-knowledge.log`).

### Files

| File | Change |
| --- | --- |
| `backend/src/OmniAI.Application/Knowledge/GetKnowledgeDocumentById/GetKnowledgeDocumentByIdHandler.cs` | Modified: the attempt is read before the document, with the reason in a comment (13 lines added, 6 removed) |
| `backend/tests/OmniAI.Application.Tests/Knowledge/KnowledgeDocumentReadTests.cs` | New: the test above, with its own double |

With these two files, the worktree has 70 modified files and 19 new ones, 89 in all: the files in section 3 plus these two. Nothing under `frontend/` or `deploy/` changed.

### Gate results (checkpoint, after Revision 1)

The gates ran one at a time, never two together.

| Check | Result |
| --- | --- |
| Free memory before the backend tests | 11.64 GB (`gates\memory-before-backend-rev1.txt`). The page cache was not dropped |
| Backend restore with the NuGet audit on | **PASS**: exit 0, no NU19xx |
| `dotnet list package --vulnerable --include-transitive` | **PASS**: no vulnerable packages in any of the 8 projects |
| `dotnet build OmniAI.slnx -c Release` | **PASS**: 0 warnings, 0 errors |
| `dotnet test`, once (`-m:1`, sdk:10.0, throwaway pgvector pg17 + Redis 7, removed afterwards; 0 leftover databases) | **PASS**: Api.IntegrationTests 1,192 passed, 2 skipped; Application.Tests 921; Domain.Tests 939; Infrastructure.IntegrationTests 1,310 passed, 1 skipped. **4,362 passed, 3 skipped, 0 failed**. The new test is the one more. The 503 start-up cluster did not come back, so `max_connections` was left as it was (`gates\backend-gates-rev1.log`, `gates\rev1\test.log`) |
| Dev API runs this worktree (`p36-stack.sh build`, then `api`) | **PASS**: both exit 0; the stand-in is on, the fallback plan is free, ready 200 (`gates\dev-build-rev1.log`, `gates\dev-api-rev1.log`) |
| Provider canary | **PASS**, before any traffic (`gates\canary-rev1.txt`) |
| Free memory before `e2e` | 11.24 GB (`gates\memory-before-e2e-rev1.txt`) |
| `npm run e2e`, one full run (the last full run, run 2 above, ended at 23:54:35 UTC on 13 Nov, 1 h 12 min earlier, so nothing was excluded) | **PASS**: 46 of 46 in 2.4 min (01:06:29 to 01:08:52 UTC, 14 Nov), en-LTR and ar-RTL (`gates\e2e-rev1.log`). `NABRA_E2E_OUT` was `<evidence>\p39-1\e2e-rev1`. `NABRA_E2E_STATE_DIR` was `<evidence>\p39-1\tmp\e2e-state-rev1`, deleted afterwards |
| `format:check`, `test:ci`, `build`, `i18n:check` | **NOT RUN**: no file under `frontend/` changed |

The 23 journeys each passed in both languages:

- agent preview reply
- Arabic report
- deep link opens at the newest messages
- hosted page with `?lang=ar` and with `?lang=en`
- invitation language
- knowledge indexing ends by itself
- needs a person
- one connection
- rename the workspace
- sign-in throttled
- sign-in
- the five signed-out pages without a 401
- slow reply
- the two sound tests
- take over
- time zone
- widget corners
