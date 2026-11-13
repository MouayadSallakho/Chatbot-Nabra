# Phase 38.7b: the backend asks from Phase 38, part B

Refusal codes, schedules, the documentation and dev data: BA-4, BA-12, BA-9, BA-4.3, BA-4.1, BA-4.2 and BA-5. Written 2026-11-12 and 2026-11-13 on top of `c757f27` (38.7a). Not committed.

Evidence folder (outside the repository): `C:\Users\mouay\Desktop\nabra-backend-evidence\p38-7b\` (`<evidence>` below).

Skills loaded and used: `frontend-pro-max:dotnet-backend-architect`, `frontend-pro-max:api-contracts-integration` and `frontend-pro-max:enterprise-system-architecture`. None was missing.

**Interrupted once.** The plan's session limit stopped the work on 2026-11-12 in the middle of item 9. It was resumed on 2026-11-13 from the uncommitted worktree, with nothing discarded: HEAD was still `c757f27`, Docker Desktop had closed (the machine had not restarted), and the dev API and dev server were down. Both were restarted with the `stack.md` commands, and the canary was run again before any traffic.

---

## 1. Summary

- Every refusal in customers, conversations, notifications, and reports and schedules now carries its own code, so 38.8 can tell people in English and Arabic what went wrong (for example "take the conversation over first", "that range is not available", "this report is still being generated") instead of the generic sentence.
- A refused DOCX now says whether its limit counts parts or bytes (`limitUnit`), so the sentence can give the number with its unit. Every other file limit says its unit too.
- A workspace manager learns how many report schedules a time-zone change would move, even without permission to see reports; nobody else learns anything.
- The development stack shows a finite fallback plan (Free), so the billing page's fallback column can be checked live.
- The API's OpenAPI document now describes every write's real answer, the problem members, the invitation `status` filter and the optional fields, and a test keeps it that way. Nothing the current app reads changed: its 46 end-to-end journeys pass unchanged.

## 2. Starting point

- **Branch and commit:** `master` at `c757f27` (38.7a, "feat: announce the stored message, …"), on top of `413615a`.
- **Worktree:** clean (`git status --short` empty).
- **Last five commits:** `c757f27`, `413615a`, `311c807`, `98753c9`, `942e269`.

## 3. Changes by area

### `git status --short`

```
 M backend/src/OmniAI.Api/AiAgents/AiAgentEndpoints.cs
 M backend/src/OmniAI.Api/Auth/AuthEndpoints.cs
 M backend/src/OmniAI.Api/Auth/MeEndpoints.cs
 M backend/src/OmniAI.Api/Authorization/RoleEndpoints.cs
 M backend/src/OmniAI.Api/Billing/BillingEndpoints.cs
 M backend/src/OmniAI.Api/Conversations/ConversationEndpoints.cs
 M backend/src/OmniAI.Api/Customers/CustomerEndpoints.cs
 M backend/src/OmniAI.Api/ErrorHandling/DomainProblemDetailsHandler.cs
 M backend/src/OmniAI.Api/Knowledge/KnowledgeEndpoints.cs
 M backend/src/OmniAI.Api/Knowledge/KnowledgeFileEndpoints.cs
 M backend/src/OmniAI.Api/Notifications/NotificationEndpoints.cs
 M backend/src/OmniAI.Api/Program.cs
 M backend/src/OmniAI.Api/Reports/ReportEndpoints.cs
 M backend/src/OmniAI.Api/Team/TeamEndpoints.cs
 M backend/src/OmniAI.Api/Tenants/TenantEndpoints.cs
 M backend/src/OmniAI.Api/Tenants/TenantResponse.cs
 M backend/src/OmniAI.Api/WebChat/EmbeddedChatEndpoints.cs
 M backend/src/OmniAI.Api/WebChat/HostedChatEndpoints.cs
 M backend/src/OmniAI.Api/WebChat/WebChatEndpoints.cs
 M backend/src/OmniAI.Api/WhatsApp/WhatsAppEndpoints.cs
 M backend/src/OmniAI.Application/Conversations/ConversationInboxExceptions.cs
 M backend/src/OmniAI.Application/Customers/CustomerHandlers.cs
 M backend/src/OmniAI.Application/Knowledge/Files/KnowledgeFileIngestion.cs
 M backend/src/OmniAI.Application/Notifications/NotificationHandlers.cs
 M backend/src/OmniAI.Application/Reports/ReportHandlers.cs
 M backend/src/OmniAI.Application/Reports/ReportPorts.cs
 M backend/src/OmniAI.Application/Reports/ReportScheduleHandlers.cs
 M backend/src/OmniAI.Application/Tenants/GetTenantById/GetTenantByIdHandler.cs
 M backend/src/OmniAI.Application/Tenants/Persistence/ITenantRepository.cs
 M backend/src/OmniAI.Application/Tenants/TenantView.cs
 M backend/src/OmniAI.Domain/Conversations/ClientMessageId.cs
 M backend/src/OmniAI.Domain/Conversations/Conversation.cs
 M backend/src/OmniAI.Domain/Conversations/ConversationHandlingEvent.cs
 M backend/src/OmniAI.Domain/Conversations/ConversationHandlingEventId.cs
 M backend/src/OmniAI.Domain/Conversations/ConversationId.cs
 M backend/src/OmniAI.Domain/Conversations/ConversationTurn.cs
 M backend/src/OmniAI.Domain/Conversations/ConversationTurnId.cs
 M backend/src/OmniAI.Domain/Conversations/HumanReply.cs
 M backend/src/OmniAI.Domain/Conversations/HumanReplyId.cs
 M backend/src/OmniAI.Domain/Conversations/Message.cs
 M backend/src/OmniAI.Domain/Conversations/MessageId.cs
 M backend/src/OmniAI.Domain/Customers/Customer.cs
 M backend/src/OmniAI.Domain/Customers/CustomerDisplayName.cs
 M backend/src/OmniAI.Domain/Customers/CustomerId.cs
 M backend/src/OmniAI.Domain/Customers/CustomerShortCode.cs
 M backend/src/OmniAI.Domain/Notifications/Notification.cs
 M backend/src/OmniAI.Domain/Notifications/NotificationActionPath.cs
 M backend/src/OmniAI.Domain/Notifications/NotificationEnums.cs
 M backend/src/OmniAI.Domain/Notifications/NotificationIds.cs
 M backend/src/OmniAI.Domain/Notifications/NotificationOccurrence.cs
 M backend/src/OmniAI.Domain/Notifications/NotificationPreferences.cs
 M backend/src/OmniAI.Domain/Reports/ReportArtifact.cs
 M backend/src/OmniAI.Domain/Reports/ReportExecution.cs
 M backend/src/OmniAI.Domain/Reports/ReportIds.cs
 M backend/src/OmniAI.Domain/Reports/ReportPeriod.cs
 M backend/src/OmniAI.Domain/Reports/ReportSchedule.cs
 M backend/src/OmniAI.Domain/Reports/ReportScheduleCalendar.cs
 M backend/src/OmniAI.Infrastructure/Knowledge/Files/CsvTextReader.cs
 M backend/src/OmniAI.Infrastructure/Knowledge/Files/DocxTextReader.cs
 M backend/src/OmniAI.Infrastructure/Knowledge/Files/KnowledgeFileExtractor.cs
 M backend/src/OmniAI.Infrastructure/Knowledge/Files/PdfTextReader.cs
 M backend/src/OmniAI.Infrastructure/Persistence/Repositories/CustomerRepository.cs
 M backend/src/OmniAI.Infrastructure/Persistence/Repositories/TenantRepository.cs
 M backend/tests/OmniAI.Api.IntegrationTests/ErrorHandling/ProblemCodeTests.cs
 M backend/tests/OmniAI.Api.IntegrationTests/Knowledge/KnowledgeFileApiFixture.cs
 M backend/tests/OmniAI.Api.IntegrationTests/Knowledge/KnowledgeFileRefusalApiTests.cs
 M backend/tests/OmniAI.Api.IntegrationTests/Tenants/WorkspaceRenameApiTests.cs
 M backend/tests/OmniAI.Application.Tests/Tenants/CreateTenantHandlerTests.cs
 M backend/tests/OmniAI.Application.Tests/Tenants/GetTenantByIdHandlerTests.cs
 M backend/tests/OmniAI.Application.Tests/Tenants/InMemoryTenantRepository.cs
 M backend/tests/OmniAI.Infrastructure.IntegrationTests/Knowledge/KnowledgeFileFixtures.cs
 M backend/tests/OmniAI.Infrastructure.IntegrationTests/Knowledge/KnowledgeFileRefusalCodeTests.cs
 M backend/tests/OmniAI.Infrastructure.IntegrationTests/Reports/WorkspaceTimeZoneStoreTests.cs
 M docs/architecture/knowledge-file-ingestion.md
 M docs/architecture/problem-codes.md
 M docs/architecture/tenant-onboarding-vertical-slice.md
?? backend/src/OmniAI.Api/Http/OpenApiContract.cs
?? backend/tests/OmniAI.Api.IntegrationTests/ErrorHandling/AreaProblemCodeApiTests.cs
?? backend/tests/OmniAI.Api.IntegrationTests/Http/
?? backend/tests/OmniAI.Api.IntegrationTests/Tenants/WorkspaceScheduleCountApiTests.cs
?? docs/phase-reports/PHASE_38_7B_BACKEND_ASKS_REPORT.md
```

**What did not change:**
- no model change and no migration; no package added, upgraded or removed;
- nothing under `frontend/` or `deploy/`;
- no route, status code or response body the current frontend reads (only `code` values that were generic, new optional members, and documentation);
- the `omniai_p36_live` stack and every other project's containers were left alone.

Outside the repository: the dev stack's `p36-stack.sh` (`api` command) and `stack.md` (BA-4.3), with the originals kept in `<evidence>\stack-before\`.

### BA-4: every remaining area names its refusals

**Inventory first.** A scanner that mirrors `ProblemCodeTests` (`<evidence>\scratch\inventory.py`) found **89** `DomainValidationException` / `DomainStateException` throw sites in the four areas with no code, plus these refusals at the HTTP edge or in Application types:

| Area | Route | Refusal before | Now |
| --- | --- | --- | --- |
| Customers | `GET …/customers` | 400 `validation.failed` (page) | `customers.page.invalid` |
| Customers | any `…/customers/{id}` | 400 `validation.failed` (all-zero id) | `customers.id.invalid` |
| Customers | `PUT …/customers/{id}/display-name` | 400 `validation.failed` (name too long, control characters) | `customers.display_name.too_long`, `.control_characters` |
| Customers | `POST …/customers/{id}/merge` | 400 `validation.failed` (no target; itself; not both in the workspace) | `customers.merge.target_required`, `.self`, `.not_in_workspace` |
| Customers | rename, merge | 409 `state.conflict` (merged away) | `customers.merged` (still with `customerId`, `canonicalCustomerId`) |
| Conversations | any `…/conversations/{id}` | 400 `validation.failed` (all-zero id) | `conversations.id.invalid` |
| Conversations | `POST …/conversations/{id}/messages` | 409 `state.conflict` (the AI handles it) | `conversations.reply.not_taken_over` |
| Conversations | the same | 400 `validation.failed` (client message id; text) | `conversations.client_message_id.*`, `conversations.message.*` |
| Conversations | `PUT …/conversations/{id}/assignment` | 422 `validation.failed` (not a member; cannot reply) | `conversations.assignee.not_member`, `.cannot_reply` |
| Conversations | take over, return, assign, reply | 409 `state.conflict` (closed) | `conversations.closed` (no route closes a conversation today) |
| Notifications | `GET …/notifications` | 400 `validation.failed` (page) | `notifications.page.invalid` |
| Notifications | `PUT …/notifications/preferences` | 400 `validation.failed` (a category missing) | `notifications.preferences.incomplete` |
| Reports | `POST …/reports`, schedule writes | 400 `validation.failed` (type, format, format not offered, frequency) | `reports.type.invalid`, `.format.invalid`, `.format.unsupported`, `.frequency.invalid` |
| Reports | `POST …/reports` | 400 `validation.failed` (request id; range; period) | `reports.client_request_id.required`, `reports.range.invalid` / `.conflict` / `.dates_required`, `reports.period.future` / `.reversed` / `.too_long` / `.too_early` |
| Reports | `POST …/reports` | 409 `state.conflict` (request id reused) | `reports.client_request_id.reused` |
| Reports | `GET …/reports/executions`, `…/{id}` | 400 `validation.failed` (page; all-zero id) | `reports.page.invalid`, `reports.id.invalid` |
| Reports | `GET …/executions/{id}/download` | 409 `state.conflict` (not ready; failed); 410 with no code (expired; missing) | `reports.file.not_ready`, `.failed`, `.expired`, `.missing` |
| Schedules | schedule create and update | 400 `validation.failed` (name) | `reports.schedule.name.required` / `.too_long` / `.control_characters` |
| Schedules | `POST …/reports/schedules` | 409 `state.conflict` (limit) | `reports.schedule.limit_reached` |
| Schedules | `PUT …/schedules/{id}/enabled` | 400 `validation.failed` (no `enabled`) | `reports.schedule.enabled_required` |
| Schedules | `…/schedules/{id}` | 400 `validation.failed` (all-zero id) | `reports.schedule.id.invalid` |

**How:**
- **At the throw site,** named like the existing ones. All 89 domain sites carry `{ Code = "…" }` (`<evidence>\scratch\e01_codes.py` placed them, keyed by file, line and message fragment; `e02_format.py` put them in the repository's block form).
- **One type, several rules,** as `ProtectedMembershipException` does: `ConversationAssigneeIneligibleException`, `CustomerMergeNotAllowedException` (now factories `Self()` and `NotInWorkspace(…)`) and `ReportNotDownloadableException` carry a `Code`. `ConversationNotHandledByHumanException` maps to its one code in `DomainProblemDetailsHandler.AreaCode`.
- **At the edge,** `CustomerEndpoints` and `ReportEndpoints` attach the code with `extensions:` (constants `PageInvalid`, `MergeTargetRequired`, `Merged`, `ClientRequestIdReused`, `ScheduleLimitReached`, `ScheduleEnabledRequired`).
- `ReportRequestParsing` used one generic `Parse<T>(value, refusal)`; it is now a `TryParse<T>` with each of the three callers throwing its own coded refusal, so every throw site names its rule.
- **Internal codes.** Rules no request can break (a record without its workspace, an instant that is not a time, an empty id the routes refuse first) are coded too, and listed in the "Internal consistency codes" table, as 37.5a did for the other areas. Two are worth naming: a blank customer name **clears** the name (it is not refused, so `customers.display_name.required` is internal), and an all-zero notification id stays a **404** ("not yours").
- **Nothing else in the body changed:** status, `title`, English `detail` and the existing `customerId` / `canonicalCustomerId` members are as before (the 37.5a guarantee).

**Backward compatible: the current frontend never branches on the generic codes.**
- `frontend/src/app/core/problems/core-problems.ts` (lines 88–89) says so: `validation.failed` and `state.conflict` are deliberately not in its catalogue.
- `grep -rn "validation\.failed\|state\.conflict" frontend/src --include=*.ts` (excluding specs) finds only that comment. The only feature-level `code` branches are auth (`auth.throttled`, `auth.reset.invalid`, `antiforgery.invalid`), entitlements (`entitlement.*`) and knowledge files (`hasProblemWording`); none is in these areas.
- `toApiError` (`core/api/api-error.ts`) words a refusal by `problemWording(code)` and falls back to the generic sentence for its status when the code has no wording, exactly as for `validation.failed`. A new code therefore reads the same as before until 38.8 words it.
- `frontend/e2e` asserts no generic code.

**The guarantee, extended (`ProblemCodeTests`):**
- `AreaFolders` gains the eight Domain and Application folders of the four areas. A new `DomainValidationException` or `DomainStateException` there without a code fails `Every_domain_refusal_in_these_areas_names_its_rule`.
- New `Every_endpoint_refusal_in_these_areas_names_its_rule`: in the four areas' endpoint files, a `Results.Problem` with 400, 409 or 422 and no `extensions:` fails.
- `Every_published_code_is_explained_in_the_catalogue` now also collects the edge constants and the codes of the three multi-rule types, so a code missing from `problem-codes.md` fails.
- `A_refusal_outside_these_areas_still_carries_the_generic_code` moved its example from the customer merge (now coded) to an unknown analytics `range`.
- New `AreaProblemCodeApiTests` (5 tests): one per area, through real routes with customers and conversations made by a real visitor's chat, each code with its unchanged status; plus the generic code that stays.
- `problem-codes.md`: new Customers, Notifications and Reports-and-schedules sections, the conversation table extended, the internal codes, and the paragraph naming the areas on generic codes rewritten (no area is left; an unnamed refusal elsewhere still gets the generic code).

### BA-12: the unit of a file refusal's limit

- **`limitUnit`** travels beside **every** `limit` on a knowledge file refusal, not only the archive's: `bytes`, `pages`, `characters`, `rows`, `columns` or `entries` (`KnowledgeFileLimitUnits`, kept apart from `KnowledgeFileRefusals`, whose constants the catalogue test reads as codes).
- **Always together:** `KnowledgeFileRejectedException` refuses a limit without its unit (or a unit without a limit, or an unknown unit), so no future refusal can drop it. The 11 parser throw sites and the upload endpoint's own 413 pass it.
- `knowledge.file.archive_too_large` is `entries` for too many parts and `bytes` for too much expansion.
- An entitlement refusal's `limit` gets no `limitUnit`: its unit is the entitlement it names (documented).
- **Tests:** `KnowledgeFileRefusalCodeTests` (Infrastructure: every limited refusal's unit, the archive in entries and in bytes, the constructor's rule) and `KnowledgeFileRefusalApiTests` (through a real upload: both archive refusals with their unit, the 413 in bytes, and a refusal without a bound carrying neither). The API fixture's archive bounds were lowered (16 parts, 512 KB) so a test reaches them with a few kilobytes.
- Documented in `problem-codes.md` and `knowledge-file-ingestion.md`.

### BA-9: how many schedules a time-zone change moves

- **The choice:** `scheduleCount` on `GET /api/tenants/{id}`, not a dry run of `PUT …/time-zone` (§4).
- **Who sees it:** only a caller holding `workspace.manage`. Everyone else, including a member with `reports.view`, gets **no member at all** (`JsonIgnore(WhenWritingNull)`), never a zero. A non-member still gets the read's `404`. The create, rename and zone-change responses carry no count.
- **What it counts:** `ITenantRepository.CountSchedulesToRezoneAsync`, the same predicate `ChangeTimeZoneAsync` locks: this workspace's schedules, enabled or not, whose next run is still to come.
- **One permission read:** `GetTenantByIdHandler` now reads the caller's effective permissions once (`EffectivePermissionsAsync`) and answers both questions; a member without `workspace.view` still gets `403` with the same `PermissionDeniedException`. The count is one statement, only for a manager.
- **The `ETag`** still names the workspace row; adding a schedule changes the count and not the tag.
- **Tests:** `GetTenantByIdHandlerTests` (+5: a manager without `reports.view` gets the count at the handler's clock; zero; a viewer gets none and the store is not even asked; `workspace.view` still required; another workspace's count never shows); `WorkspaceTimeZoneStoreTests` (+1, PostgreSQL: counts upcoming and switched-off, not the due one nor another workspace's, then changes the zone and checks exactly those moved); `WorkspaceScheduleCountApiTests` (5, real routes: a manager without `reports.view` is refused the schedules and given the count; zero; a `reports.view` member learns nothing; no count crosses workspaces; the ETag is unchanged by a schedule and a rename carries no count). `WorkspaceRenameApiTests` pinned the workspace body's member set: it now lists `scheduleCount` for its owner, by design.
- Documented in `tenant-onboarding-vertical-slice.md` (Phase 38.7b section).

### BA-4.3: plan limits on the dev stack

- **Done by configuration only:** the dev stack's `api` command now passes `Billing__FallbackPlanCode=free`. `free` is a finite plan the dev database already held (2 members, 1 agent, 10 documents, 1 website chat, 0 WhatsApp connections, 200 operations a month). No SQL, no migration, no seed.
- **Every workspace** shows it as its fallback (the fallback is a deployment setting).
- **The current plan stays `development` (unlimited)** for every workspace; new workspaces still start there (`Plans:DefaultPlanCode` unchanged). A finite *current* plan cannot be reached this way (§4, §8).
- Billing still cannot sell (no provider key, no return address), so checkout and the portal refuse as before; only the read shows the fallback.
- `stack.md` gained a "Plans on the dev stack" section; `P38_FALLBACK_PLAN=` (empty) turns it off.

### BA-4.1, BA-4.2, BA-5: the OpenAPI document

All metadata; no route, status or body changed.
- **Invitations:** `POST …/invitations` declares `201` with `CreatedInvitationResponse` (the real type; the asks list called it `CreateInvitationResponse`), and `POST …/invitations/{id}/revoke` declares its real `204`.
- **Roles:** `POST …/roles` declares `201` `RoleResponse`; `PUT …/roles/{id}/name` and `…/permissions` declare `200` `RoleResponse` (which has no `memberCount`).
- **Every write and customer route** now declares its real success: 52 operations in 16 endpoint files (`.Produces<T>(…)` after each `.WithName`, from a research pass that read each handler's `Results.Ok` / `Created` / `CreatedAtRoute` / `NoContent`; `<evidence>\scratch\e03_produces.py`). Several were documented as `200` but answer `201` (creates) or `204` (logout, revoke, mark read); they now say so.
- **`GET /api/me/tenants`** declares an array of `WorkspaceResponse`; `…/permissions` an array of strings.
- **New `OpenApiContract`** (document, schema and operation transformers, registered by `AddNabraOpenApi`):
  - the `ProblemDetails` schema lists `code`, `codes`, `limit`, `limitUnit` (with its six values), `entitlement`, `current`, `periodFrom`, `periodTo`, `traceId`, `customerId` and `canonicalCustomerId`, all optional;
  - a member written only when it has a value (`JsonIgnore(WhenWritingNull)`) is not listed as required: the dashboard's `previous` and the workspace's `scheduleCount` (and two older such members);
  - the invitation list's `status` query parameter (read by hand so an unknown value gets its code) is declared, with its four values;
  - the title reads "Nabra API" (rule 17).
- **Test:** `OpenApiDocumentContractTests` (9) reads the document from the test host and checks each item, plus a general guard: no write may declare a success with a body but no schema again.

### Contract table

| Endpoint or event | Change | New fields, parameters or codes | Permission | Why it is backward compatible |
| --- | --- | --- | --- | --- |
| Customer routes (`GET …/customers`, `GET/PUT/POST …/customers/{id}…`) | Refusals carry specific codes | `customers.*` (8 codes; see §3 BA-4) | as before (`customers.view` / `customers.manage`) | Same statuses, titles, details and members; the client words an unknown code generically, exactly as `validation.failed` / `state.conflict` |
| Conversation routes (reply, take over, return, assign, any `{id}`) | Refusals carry specific codes | `conversations.*` (11 public codes) | as before | The same |
| `GET …/notifications`, `PUT …/notifications/preferences` | Refusals carry specific codes | `notifications.page.invalid`, `notifications.preferences.incomplete` | as before | The same |
| `POST …/reports`, `GET …/reports/executions…`, schedule routes | Refusals carry specific codes; the 410s gain a code | `reports.*` (25 codes) | as before | The same; a 410 had no code and gains one (additive member) |
| Knowledge file refusals (`POST …/knowledge/files`, `PUT …/knowledge/{id}/source`) | `limitUnit` beside every `limit` | `limitUnit`: `bytes` \| `pages` \| `characters` \| `rows` \| `columns` \| `entries` | as before | New member only; `limit` and the codes unchanged |
| `GET /api/tenants/{id}` | New optional member | `scheduleCount` (integer), only for `workspace.manage` holders | `workspace.view` to read; `workspace.manage` for the member | Absent for everyone else; other responses carry no new member; `ETag` meaning unchanged |
| `/openapi/v1.json` (Development only) | Metadata | success schemas of 52 writes and the customer routes; `ProblemDetails` extension members; invitations `status`; `previous` and `scheduleCount` optional; `/api/me/tenants` schema; title "Nabra API" | none (Development) | Documentation only; no route, status or body changed |
| Dev stack only (`p36-stack.sh api`) | Configuration | `Billing__FallbackPlanCode=free` | n/a | The billing read's `fallbackPlan` was already in the contract (null before); new workspaces' plan unchanged |

## 4. Decisions

1. **BA-9: `scheduleCount` on the workspace read, not a dry run.**
   - The settings page already reads the workspace for the `ETag` it must send, so the count arrives with no extra request.
   - A dry run would make `PUT …/time-zone` a write route that sometimes writes nothing, and would still need `If-Match` and a chosen zone just to ask a question.
   - The count is what a change *made now* re-times, whatever zone is chosen: every schedule whose next run is still to come. Whether one lands on the same instant in the new zone is not knowable without the zone, and the page only needs "are there any, and how many".
   - Absent (not `0`, not `null`) for anyone without `workspace.manage`, so a member cannot tell "no schedules" from "not allowed to know".
2. **BA-12: the unit on every file `limit`, not only the archive's.** One rule for clients ("a `limit` always comes with its `limitUnit`") is simpler than "one code needs it", and the constructor enforces it so no later refusal can drop it. Entitlement `limit`s were left without a unit: their unit is the entitlement, and adding one there was not asked.
3. **BA-4: internal rules get codes too.** The scan in `ProblemCodeTests` requires a code at every throw site in the area folders, as for the 37.5a/37.5b areas. Rules no request can break got codes listed under "Internal consistency codes" instead of being left out of the scan.
4. **BA-4: two refusals deliberately not changed.** A blank customer name keeps clearing the name (200), and an all-zero notification id keeps answering 404 ("not yours"). Both are existing behaviour the frontend may rely on; the domain codes behind them are listed as internal.
5. **BA-4: `ReportRequestParsing`.** The shared `Parse<T>(value, refusal)` became `TryParse<T>` with each caller throwing its own coded refusal, so the throw-site scan sees a literal code at each site. Same accepted values (names only, no numbers), same sentences.
6. **BA-4.3: a finite *current* plan was not made.** By configuration alone it cannot be: a workspace's current plan changes only through a provider subscription (no offers, no Stripe secret, rule 9) or through the plan new workspaces start on, which this step must not change. What it would take, in order of preference:
   - a Development-only seed command that assigns an existing plan to one named dev workspace through `IBillingSubscriptionService`'s plan-assignment path (code, reviewed like any other; no SQL);
   - or a local Stripe test-mode stub on `Stripe:ApiBase` plus a signed test webhook (a provider secret on the dev stack, which rule 9 rules out today);
   - or starting the API once with `Plans__DefaultPlanCode=starter`, creating one workspace, and switching back (rejected: it changes the plan new workspaces start on, even if briefly).
   Left for your decision (§8).
7. **Item 13: every write, not only the named ones.** The asks named invitations and roles; BA-5 asked for "the writes … that declare none". All 52 schema-less writes and the customer routes were declared from their handlers' real results, and a test now refuses any write with an undescribed success. Reads that still declare no schema (members, website-chat and WhatsApp reads, plan usage, notification preferences, unread count) were left; they are listed in §8.
8. **The document's title** is now "Nabra API" (it read "OmniAI.Api | v1"), for rule 17. Development only.
9. **The flaky assertion was not fixed** (§5, §8): it is outside this step's areas, and the fix belongs in a maintenance step.

## 5. Verification

Free memory was checked before each heavy gate and never went under 6 GB (`<evidence>\gates-memory.txt`, `e2e-memory.txt`).

| Check | Result |
| --- | --- |
| Starting point | **PASS**: `master` at `c757f27`, clean. On resumption (2026-11-13) HEAD was still `c757f27` with the 33 files of the interrupted work, nothing lost |
| Free memory before the backend gates | run 1: 9.62 GB; run 2: 8.69 GB |
| Restore, NuGet audit **on** (no `-p:NuGetAudit=false`, no suppression) | **PASS** both runs: exit 0 (`<evidence>\gates\backend-gates-run1.log`, `-run2.log`) |
| `dotnet list package --vulnerable --include-transitive` | **PASS**: no vulnerable package in any of the 8 projects. SignalR.StackExchangeRedis 10.0.15, MessagePack 2.5.303, Pkcs 10.0.13 |
| `dotnet build OmniAI.slnx -c Release` | **PASS**: **0 warnings**, 0 errors (both runs) |
| Tests run 1 (`-m:1`, throwaway PostgreSQL 17 + pgvector and Redis 7) | **All tests passed; the run exited 1.** Api.IntegrationTests 1,148 passed, 2 skipped. Application.Tests 915. Domain.Tests 915. Infrastructure.IntegrationTests 1,310 passed, 1 skipped. Total **4,288 passed, 3 skipped, 0 failed** (38.7a: 4,257; 31 new). The exit 1 was a **class cleanup failure** in `AiAgentLiveOpenAiSmokeTests` (its fixture's `DROP DATABASE … WITH (FORCE)` hit a transient `NpgsqlException`), which left 1 test database in the throwaway server. The class is untouched by this step; alone it passes with no cleanup failure (`<evidence>\gates\one-test.log`) |
| Tests run 2 (same, for a clean run) | **1 FAIL, not related to this step.** Api.IntegrationTests 1,147 passed, **1 failed**, 2 skipped; the other three projects as run 1. 0 leftover databases. The failure: `AiAgentOpenAiPreviewApiTests.A_failure_body_leaks_nothing_about_the_provider` asserts the problem body never contains `"401"`, and that run's random `traceId` contained the digits (`…9518a2401f…`). The assertion dates from commit `83328bf` and is unchanged; about 1 run in 90 hits it. The class re-run alone: **17/17 passed** (`<evidence>\gates\rerun-AiAgentOpenAiPreviewApiTests.log`) |
| Throwaway test containers and networks | removed after every run (`docker ps` shows none) |
| Dev API rebuild (`p36-stack.sh build`, then `api`) | **PASS**: published with the audit on; ready on `127.0.0.1:5027`, stand-in on, `Billing__FallbackPlanCode=free` set |
| Provider canary | **PASS** before any traffic, on the old build after the restart and on the rebuilt API (`<evidence>\live\canary-before.txt`, `canary.txt`) |
| Live BA-4, every area (`<evidence>\live\live-before.json` → `live-after.json`) | **PASS**: 20 refusals that answered `validation.failed` / `state.conflict` on the 38.7a build now answer their codes with the **same statuses**: `customers.page.invalid`, `.id.invalid`, `.merge.target_required`, `.merge.self`, `.display_name.too_long`; `conversations.id.invalid`, `.reply.not_taken_over` (409), `.client_message_id.invalid`, `.assignee.not_member` (422); `notifications.page.invalid`, `.preferences.incomplete`; `reports.range.invalid`, `.range.conflict`, `.range.dates_required`, `.period.reversed`, `.type.invalid`, `.page.invalid`; `reports.schedule.name.required`, `reports.frequency.invalid`, `reports.schedule.enabled_required`. A refused rename left the customer's `ETag` unchanged |
| Live, unchanged elsewhere | **PASS**: unknown analytics `range` still `400 validation.failed`; analyst merging a Northwind customer `403` (missing permission); analyst on Al Noor `404` (not a member); a Northwind customer read under Al Noor by the owner `404` |
| Live BA-12 | **PASS**: a 68 KB DOCX with 600 parts → `422 knowledge.file.archive_too_large`, `limit` 512, `limitUnit` `entries`; a 71 KB DOCX expanding to 70 MB → same code, `limit` 67,108,864, `limitUnit` `bytes`. Nothing was created. Analyst uploading to Al Noor → `404` |
| Live BA-9 | **PASS**: owner (manager) `scheduleCount` 1 of 2 schedules (the disabled monthly one's next run, 1 Nov, is past, so a change would not move it; the weekly one, 16 Nov, would: `<evidence>\live\schedules-next-runs.txt`). Analyst (no `workspace.manage`): no `scheduleCount` member at all (`id, name, slug, status, timeZone`). Analyst on Al Noor `404`. **Manager without `reports.view`** (a custom role "Zone manager (38.7b check)" with `workspace.view` + `workspace.manage`, created through the API and given to the synthetic support account for the check): count 1, schedules list `403`; support's own roles then put back and verified |
| Live BA-4.3 | **PASS** (fallback) / **NOT DONE** (finite current plan, §4.6): Northwind's billing read shows `currentPlan` `development` and `fallbackPlan` `free` with members 2, AI agents 1, knowledge documents 10, website chats 1, WhatsApp connections 0, monthly provider operations 200 (before: `fallbackPlan` `null`) |
| Live OpenAPI (`<evidence>\openapi\openapi-before.json`, `openapi-after.json`) | **PASS**: title "Nabra API"; invitation create `201` `CreatedInvitationResponse` only; role create `201`, rename and permissions `200`, all `RoleResponse` without `memberCount`; `ProblemDetails` lists `code, codes, limit, limitUnit (bytes, characters, columns, entries, pages, rows), entitlement, current, periodFrom, periodTo, traceId, customerId, canonicalCustomerId`; invitations `status` query parameter; `previous` and `scheduleCount` not required; `/api/me/tenants` an array of `WorkspaceResponse`; **0** writes with an undescribed success (before: 52) |
| Free memory before `e2e` | 6.14 GB |
| `npm run e2e` (current frontend, unchanged, rebuilt API, after the canary) | **PASS**: **46 passed** (23 × en-ltr, ar-rtl), 0 unexpected, 0 flaky, 3.5 min (`<evidence>\e2e-run1.log`, `e2e-run1\e2e-results.json`). One full run; the previous full run (38.7a Rev 1) was a day earlier, so the throttling journey ran too |
| `format:check`, `test:ci`, `build`, `i18n:check` | **NOT RUN**: no file under `frontend/` changed |
| Secret scan (evidence and the change set) | **PASS**: no demo password, cookie, token, reset or invitation link. The hits are `Password=$PW` / `$P36_PG_PASSWORD` variable names in the scripts, the scanner's own pattern list, and a `using Microsoft.AspNetCore.Identity;` line. `<evidence>\tmp\` (e2e session states) deleted |

**Latency** on the seeded dev data: 300 interleaved reads per variant after a 30-read warm-up, owner of Northwind (`<evidence>\latency\`). No list endpoint's query changed in 38.7b; the workspace read gained a permission read and, for a manager, one count. "After 1" was taken right after the rebuild; "after 2" a few minutes later, when the untouched conversation list had also settled.

| Endpoint | p95 before | p95 after 1 | p95 after 2 | p50 before → after 2 |
| --- | --- | --- | --- | --- |
| `GET /api/tenants/{id}` (manager: with the count) | 25.1 ms | 27.1 ms | 24.5 ms | 11.3 → 11.8 |
| `GET …/customers` (20) | 27.0 ms | 41.3 ms | 26.5 ms | 21.6 → 19.6 |
| `GET …/conversations` (20), unchanged control | 37.2 ms | 42.4 ms | 32.9 ms | 23.9 → 18.7 |
| `GET …/notifications` (20) | 27.5 ms | 34.5 ms | 27.1 ms | 23.4 → 15.0 |
| `GET …/reports/executions` (20) | 34.7 ms | 38.8 ms | 28.2 ms | 18.3 → 17.4 |

No regression once warm. The count uses `ix_report_schedules_tenant_name`; no index was added.

## 6. See it live

The dev API (`127.0.0.1:5027`) and the dev server (`localhost:4200`) are running. Sign in as the owner from `demo-accounts.md`; open DevTools → Network.

1. **The new codes (BA-4).** Each request below answers a problem body whose `code` is the new one (its status is unchanged):
   - Customers page: `GET /api/tenants/{northwind}/customers?pageSize=1000` → `customers.page.invalid`.
   - Reply to a conversation the AI handles (in the inbox, an AI conversation's composer, or `POST …/conversations/{id}/messages`) → `409 conversations.reply.not_taken_over`.
   - Assign a conversation to a random id (`PUT …/assignment` with `{ "member": "<any uuid>" }` and the conversation's ETag) → `422 conversations.assignee.not_member`.
   - Reports: `POST …/reports` with `"range": "1y"` → `reports.range.invalid`; a schedule named " " → `reports.schedule.name.required`.
   - Today's screens still show the generic sentence for these; 38.8 words them.
2. **The archive's unit (BA-12).** Upload a DOCX with more than 512 parts, or one that expands past 64 MB, on the knowledge page: the `422` body carries `limit` and `limitUnit` (`entries` or `bytes`). `<evidence>\scratch\live-checks.mjs` builds both (section `archive`).
3. **The schedule count (BA-9).** `GET /api/tenants/{northwind}` as the owner shows `scheduleCount`; as the analyst it is absent. To see a manager without `reports.view`, give someone the "Zone manager (38.7b check)" role in Northwind.
4. **The fallback plan (BA-4.3).** Open the billing page: the fallback column shows **Free** with its finite limits; the current plan is still Development (unlimited).
5. **The document.** `http://127.0.0.1:5027/openapi/v1.json`: title "Nabra API"; `ProblemDetails` with the extension members; `POST /api/tenants/{tenantId}/invitations` → `201`; `GET …/invitations` with its `status` parameter.

## 7. For 38.8

Ready to copy into the 38.8 work. Each item is additive; nothing in the current client has to change for it to keep working.

1. **Word the new codes, English and Arabic.** Register them in the area catalogues (`ProblemCatalogue`), with the generic sentence staying the fallback for any code not worded. The full list, with status and meaning, is in `docs/architecture/problem-codes.md`:
   - Conversations: `conversations.reply.not_taken_over` (409), `conversations.assignee.not_member` / `.cannot_reply` (422), `conversations.message.required` / `.too_long` / `.control_characters`, `conversations.client_message_id.required` / `.too_long` / `.invalid`, `conversations.id.invalid`, `conversations.closed` (409).
   - Customers: `customers.page.invalid`, `customers.id.invalid`, `customers.display_name.too_long` / `.control_characters`, `customers.merge.target_required` / `.self` / `.not_in_workspace`, `customers.merged` (409, with `customerId` and `canonicalCustomerId` as before).
   - Notifications: `notifications.page.invalid`, `notifications.preferences.incomplete`.
   - Reports and schedules: `reports.type.invalid`, `reports.format.invalid`, `reports.format.unsupported`, `reports.frequency.invalid`, `reports.client_request_id.required` / `.reused` (409), `reports.range.invalid` / `.conflict` / `.dates_required`, `reports.period.future` / `.reversed` / `.too_long` / `.too_early`, `reports.page.invalid`, `reports.id.invalid`, `reports.file.not_ready` / `.failed` (409), `reports.file.expired` / `.missing` (410), `reports.schedule.id.invalid`, `reports.schedule.name.required` / `.too_long` / `.control_characters`, `reports.schedule.limit_reached` (409), `reports.schedule.enabled_required`.
2. **`limitUnit` (BA-12).** `ApiError` gains `limitUnit?: 'bytes' | 'pages' | 'characters' | 'rows' | 'columns' | 'entries'` beside `limit` (read it next to `problemLimit` in `core/api/api-error.ts`). `knowledge.file.archive_too_large` can then say "more than 512 parts" or "expands to more than 64 MB"; format bytes as MB in the reader's language.
3. **`scheduleCount` (BA-9).** `TenantResponse` gains `scheduleCount?: number` (absent unless the reader holds `workspace.manage`). In `workspace-settings-page.ts` `scheduleCount(tenantId)`: when the workspace read carries the member, ask only if it is `> 0` and drop the `…/reports/schedules` read; when it is absent, keep today's behaviour (ask). Re-read the workspace before asking: a schedule change does not move the `ETag`.
4. **Billing (BA-4.3).** Nothing to build: the fallback column can now be checked live on the dev stack (Free, finite limits).
5. **Typed client (BA-5).** The hand-written types can be checked against `/openapi/v1.json`: every write's response, the problem members, the invitation `status` parameter, and `previous` / `scheduleCount` as optional. Note the numbers are still typed `integer | string` (the API's web JSON defaults); that is unchanged and worth a decision before generating a client.

## 8. Risks and open issues

1. **A flaky assertion in the suite** (`AiAgentOpenAiPreviewApiTests.A_failure_body_leaks_nothing_about_the_provider`): about 1 run in 90 fails because the random `traceId` contains "401". Pre-existing; proposed fix for a maintenance step: remove `traceId` from the body before the substring checks (or match `"status":401`). Left unchanged here.
2. **A transient teardown error** (`DROP DATABASE … WITH (FORCE)`) under the full run's load left one throwaway database once (run 1). The throwaway server is removed after each run, so nothing leaks; the fixture could retry the drop.
3. **No finite current plan on the dev stack** (§4.6). Billing's finite *current* limits are still proved by specs only. Your choice: a Development-only seed command (recommended), a Stripe test-mode stub (needs a provider secret), or leave it.
4. **Synthetic data added on the dev database:** the custom role "Zone manager (38.7b check)" in Northwind (assigned to nobody now). Kept, so §6.3 can be repeated; remove it with the other synthetic data before launch.
5. **Reads without a response schema** remain in the document (members list and detail, assignable roles, website-chat and WhatsApp reads, plan usage, notification preferences and unread count, the public chat's message reads). Out of BA-5's scope; a follow-up can declare them the same way.
6. **`conversations.closed` is not reachable today** (no route closes a conversation). It is documented as such; 38.8 can word it without a live check.
7. **`scheduleCount` can be stale by design** between a schedule change and the next workspace read, because it is not part of the `ETag`. The zone change itself always moves the right schedules; only the question before it could be off by one.

## 9. Proposed checkpoint commit message

```
feat: name every remaining refusal, give the archive limit its unit, tell managers how many schedules a zone change moves, show a fallback plan on the dev stack, and describe every write in the API document

Phase 38.7b, the backend asks BA-4, BA-12, BA-9, BA-4.3, BA-4.1, BA-4.2
and BA-5. Additive; no migration; the frontend at 413615a is unchanged
and its end-to-end suite passes (46/46).

- BA-4: customer, conversation, notification and report/schedule
  refusals carry specific codes (statuses unchanged); 89 domain throw
  sites coded; ProblemCodeTests scans the four areas' folders and their
  endpoint refusals; problem-codes.md lists every code.
- BA-12: every knowledge file limit carries limitUnit (bytes, pages,
  characters, rows, columns, entries); archive_too_large says entries
  or bytes.
- BA-9: GET /api/tenants/{id} carries scheduleCount for
  workspace.manage holders only, counting what a zone change moves;
  absent for everyone else; the ETag is unchanged.
- BA-4.1/4.2/5: OpenAPI declares the real success of every write
  (201 invitations and role create, role bodies, 204s), the problem
  extension members, the invitations status parameter, optional
  previous/scheduleCount and /api/me/tenants; a test pins it.

Backend: 4,288 passed, 3 skipped; build 0 warnings; no vulnerable
packages.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

## Revision 1 (2026-11-13)

The owner accepted this report with two test fixes and two decisions. Both fixes change test code only: no file under `backend/src`, `frontend/` or `deploy/` changed.

1. **The flaky assertion (§8.1), fixed.**
   - `backend/tests/OmniAI.Api.IntegrationTests/AiAgents/AiAgentOpenAiPreviewApiTests.cs`: `A_failure_body_leaks_nothing_about_the_provider` now parses the problem body and leaves out `traceId` before the substring checks. Every other member is still checked, exactly as the API wrote it.
   - The per-request trace id is random hex, so it could contain "401" by chance (about 1 run in 90). The test still catches any provider detail anywhere else in the body.
   - This supersedes §4.9 and closes §8.1.
2. **The transient teardown error (§8.2), fixed by a bounded retry wherever a test drops its database.**
   - `backend/tests/Shared/PostgresTestDatabase.cs`: a new `public static DropDatabaseAsync(maintenanceConnectionString, databaseName)` runs `DROP DATABASE IF EXISTS … WITH (FORCE)` with at most 3 attempts, 250 ms apart. It retries only an `NpgsqlException` whose `IsTransient` is true. The last attempt's error is thrown unchanged, so a real problem still fails the run. The fixture's own teardown uses it.
   - `backend/tests/OmniAI.Infrastructure.IntegrationTests/Persistence/DatabaseModelAuthorityTests.cs`: the second place a test drops a database (its model database) now uses the same helper.
   - After the change, `DROP DATABASE` appears in exactly one place under `backend/tests` (the helper). This closes §8.2.
3. **The owner's decision on §8.3: leave it.** The dev stack keeps no finite *current* plan until the real-provider phase. The fallback plan (Free) stays as built (§4.6, `stack.md`).
4. **The owner's decision on §8.4: keep the role.** The synthetic role "Zone manager (38.7b check)" stays in Northwind, assigned to nobody, until the pre-launch cleanup of synthetic data.

**Re-run at the checkpoint, one gate at a time:**

| Check | Result |
| --- | --- |
| Worktree | HEAD `c757f27` on `master`. The files of §3, plus the three test files above. Nothing under `frontend/` or `deploy/` |
| Change set | 84 files, all `.cs` or `.md`. No evidence, scratch scripts, OpenAPI copies, env files, keys, certificates, e2e state, traces, `test-results`, `playwright-report`, `node_modules`, `dist`, `.angular`, `bin`, `obj` or IDE files. Credential scan: the only hits are a `using Microsoft.AspNetCore.Identity;` line, the made-up `sk-leaked-probe` test string, and this report naming those patterns. No demo password, token, cookie, reset or invitation link. No control characters in any changed file |
| Free memory before the backend tests | 8.54 GB |
| Restore, NuGet audit **on** | **PASS**: exit 0 (`<evidence>\gates\backend-gates-rev1.log`) |
| `dotnet list package --vulnerable --include-transitive` | **PASS**: none in any of the 8 projects. SignalR.StackExchangeRedis 10.0.15, MessagePack 2.5.303, Pkcs 10.0.13 |
| `dotnet build OmniAI.slnx -c Release` | **PASS**: **0 warnings**, 0 errors |
| Tests (`-m:1`, throwaway PostgreSQL 17 + pgvector and Redis 7) | **PASS**, exit 0: Api.IntegrationTests 1,148 passed, 2 skipped. Application.Tests 915 passed. Domain.Tests 915 passed. Infrastructure.IntegrationTests 1,310 passed, 1 skipped. Total **4,288 passed, 3 skipped, 0 failed**, with no class cleanup failure. 0 leftover test databases; throwaway containers and network removed. No file under `backend/` changed after this log |
| Dev API | Not rebuilt: only test files changed, and they are not part of the publish. Still the build from §5 |
| Provider canary | **PASS** (`<evidence>\rev1\canary.txt`, `canary-2.txt`) |
| Free memory before `e2e` | 5.69 GB, so the Docker VM's page cache was dropped once (`alpine:3`, `drop_caches`): 6.20 GB, then 6.43 GB at the start (`<evidence>\e2e-memory.txt`) |
| `npm run e2e`, first attempt | **Not counted.** It was started in the background by the interrupted checkpoint session. One worker hung for 1.9 h in `slow-reply` (ar-rtl). When the session stopped, the remaining workers exited with 0xC0000142: 40 passed, 5 failed, 1 did not run (`<evidence>\rev1\e2e-run.log`). Kept as a record |
| `npm run e2e`, run in the foreground | **PASS**: **46 passed** (23 × en-ltr, ar-rtl), 0 unexpected, 0 flaky, 0 skipped, 2.8 min (`<evidence>\rev1\e2e-run2.log`, `e2e-run2\e2e-results.json`). A full run, more than two hours after the last throttling journey, so that journey ran too. `<evidence>\tmp\` (e2e session state) deleted |
| `format:check`, `test:ci`, `build`, `i18n:check` | **NOT RUN**: no file under `frontend/` changed |

**Commit message:** the one in §9, with one bullet added for these two fixes and "the frontend at `c757f27`" in place of "at `413615a`".
