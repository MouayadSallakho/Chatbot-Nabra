# Phase 37.5a: Screens — knowledge, agents, channels and the public chat

What the redesigned knowledge, agent and channel screens and the public chat pages asked the backend for (plan rows 15–22, 20a and 28a), plus the stable error codes for these areas. Evidence is in `C:\Users\mouay\Desktop\nabra-backend-evidence\p37-5a\`.

## 1. Summary

- **Knowledge.** A document's last indexing outcome now survives a reload: indexing now, succeeded, or failed and why. The list also says how large each uploaded file was. A refused file says exactly which rule refused it ("more than 500 pages", "password-protected", "not UTF-8") through a stable code a screen can word in English or Arabic.
- **AI agents.** An agent shows the channels it answers on: website chat and WhatsApp, with their status. Each person sees only the channel kinds they are allowed to see.
- **The workspace name is public.** The hosted chat page and the website widget now show whose chat it is. The owner (or anybody granted the new "Manage workspace" permission) can rename the workspace, safely against a colleague's edit; each rename is audited, and visitors see the new name at once. The hosted page also gets the widget's greeting, colour and theme.
- **The widget's corner can follow the page's reading direction.** "Start" and "End" place the launcher bottom-left on an English site and bottom-right on an Arabic one.
- **Every refusal a person can correct now carries a stable code**, so the redesigned screens can explain it in the reader's language. Nothing changes for today's screens.

## 2. Starting point

- Your message had no first line naming the part. I built **37.5a**, because the worktree stood exactly at the 37.4 checkpoint, and 37.5b needs an accepted 37.5a.
- Branch `master`, HEAD `7b3cf92` ("feat: show who a conversation is with, read it newest first, and mark what happened and what is unread"), worktree clean (`git status --short` printed nothing). That is the 37.4 checkpoint, as "Starting point" requires.
- The dev stack was running the 37.4 build (`omniai-p36-api` inside `omniai-p37-netns`, Mailpit, `omniai_pg25`, `omniai_redis18`, dev server on :4200). The provider canary passed before any traffic (`<evidence>\live\canary-start.txt`).

**Your rulings during this step (rule 3, asked before any frontend change):**
- Row 22: build it end to end. `embed-chat.ts` now forwards `Start`/`End` to the loader instead of turning them into `Right`.
- `workspace.manage`: add its English and Arabic wording to the role editor now (`permission-words.ts` and the locale files).
- Memory: after you closed apps, free memory read 4.5–5.0 GB. At your choice I dropped the Docker VM's page cache with one short privileged `alpine:3` container (`echo 3 > /proc/sys/vm/drop_caches`; nothing else was touched), and free memory rose to 6.5 GB before the first gate.

## 3. Changes by area

`git status --short` (135 entries; the full listing is `<evidence>\live\git-status-final.txt`). The modified files, by folder:

| Folder | Files | What changed |
| --- | --- | --- |
| `backend/src/OmniAI.Domain` | 38 | `Code` on the two Domain exceptions; a code on every throw site in `AiAgents` (4), `Knowledge` (11), `WebChat` (10), `WhatsApp` (8) and `Tenants/TenantName`; `Start`/`End` corners; `workspace.manage`; the `workspace.renamed` event kind |
| `backend/src/OmniAI.Application` | 20 | the knowledge and agent read models (`lastIndexAttempt`, `sourceByteLength`, `channels`); file-refusal codes and limits; the versioned tenant read; the public chat views; codes on the Application throw sites |
| `backend/src/OmniAI.Infrastructure` | 10 | the list projection, `FindLatestAsync`, the rename with its audit row, the public-key read (workspace name and widget), codes on the 28 parser refusals, PDF encryption, DI |
| `backend/src/OmniAI.Api` | 19 | the problem handler (`AreaCode`, codes, `limit`), `Program.cs` (the fallback hook), the detail responses, the rename endpoint and the tenant `ETag`, the public reads, coded endpoint refusals, OpenAPI metadata, `v1.js` |
| `backend/tests` | 12 | the four pinned shapes this step changes on purpose (§5), and test doubles for the new port members |
| `docs/architecture` | 9 | see §3.6 |
| `frontend` | 8 | `embed-chat.ts`/`.html`/spec, `embed-chat-api.ts`, `embed-bridge.ts` (Start/End forwarded); `permission-words.ts`, `messages.json`, `messages.ar.json` (the `workspace.manage` wording) — the two changes you approved |

New files:

```
?? backend/src/OmniAI.Api/ErrorHandling/ProblemCodes.cs
?? backend/src/OmniAI.Application/AiAgents/AiAgentChannels.cs
?? backend/src/OmniAI.Application/Tenants/RenameTenant/RenameTenantHandler.cs
?? backend/src/OmniAI.Application/Tenants/TenantVersionMismatchException.cs
?? backend/src/OmniAI.Infrastructure/Persistence/Repositories/AiAgentChannelReader.cs
?? backend/tests/OmniAI.Api.IntegrationTests/AiAgents/AiAgentChannelsApiTests.cs
?? backend/tests/OmniAI.Api.IntegrationTests/ErrorHandling/ProblemCodeTests.cs
?? backend/tests/OmniAI.Api.IntegrationTests/Knowledge/KnowledgeFileRefusalApiTests.cs
?? backend/tests/OmniAI.Api.IntegrationTests/Knowledge/KnowledgeReadOutcomeApiTests.cs
?? backend/tests/OmniAI.Api.IntegrationTests/Tenants/WorkspaceRenameApiTests.cs
?? backend/tests/OmniAI.Api.IntegrationTests/WebChat/PublicChatAppearanceApiTests.cs
?? backend/tests/OmniAI.Application.Tests/AiAgents/AiAgentChannelReadTests.cs
?? backend/tests/OmniAI.Application.Tests/AiAgents/InMemoryAiAgentChannelReader.cs
?? backend/tests/OmniAI.Application.Tests/Tenants/RenameTenantHandlerTests.cs
?? backend/tests/OmniAI.Domain.Tests/Authorization/WorkspaceManagePermissionTests.cs
?? backend/tests/OmniAI.Domain.Tests/WebChat/WebChatLauncherCornerTests.cs
?? backend/tests/OmniAI.Infrastructure.IntegrationTests/Knowledge/KnowledgeFileRefusalCodeTests.cs
?? backend/tests/OmniAI.Infrastructure.IntegrationTests/Persistence/ScreensReadModelTests.cs
?? docs/phase-reports/PHASE_37_5A_SCREENS_REPORT.md
```

No migration. No `deploy/` change. No package added, upgraded or removed. The frontend changes are the two you approved.

### 3.1 The problem-code mechanism (row 28a)

- `DomainValidationException` and `DomainStateException` gain an optional `Code` (an `init` property, so every existing constructor still compiles). The throw site names its rule: `throw new DomainValidationException("…") { Code = "ai-agents.name.required" };`.
- **All 106 throw sites in the knowledge, agent, website-chat and WhatsApp folders (Domain and Application), and the 3 in `TenantName`, carry a code.** 50 of them are internal invariants no request can reach, such as an empty identity, a missing workspace or an unreal instant. They are coded too, under 19 codes, and listed separately in the catalogue.
- The problem handler publishes that code. It gives each Application refusal type of these areas its one code (`AreaCode`). A knowledge file refusal gets its code plus a numeric `limit`.
- **The guarantee:** `ProblemCodes.ApplyFallback` runs on every problem body through `CustomizeProblemDetails`. A 400 or 422 without a code gets `validation.failed`, a 409 `state.conflict`, a 412 `precondition.failed`. A specific code is never overwritten, and nothing else changes: status, `title`, `detail`, `errors`.
- The endpoint-level refusals of these areas are coded too: the upload's 400, 413 and 415; "AI agent is required" for website chat and WhatsApp; and `If-Match` 428 and malformed-400, which every area shares.
- `docs/architecture/problem-codes.md` lists every code.
- `ProblemCodeTests` fails when any of these happens:
  - a throw site in these folders has no code;
  - a file refusal has no code;
  - a published code is missing from the catalogue;
  - a code does not reach the wire.

### 3.2 Knowledge (rows 15, 16, 17)

- **Row 15, `lastIndexAttempt`:** `{ status, failureCode, startedAt, completedAt } | null` on the list items and on `GET …/knowledge/{id}`.
  - The newest attempt by `started_at` wins; the version-7 id breaks a tie.
  - The list reads it in its single statement, as a per-row probe on `ix_knowledge_index_attempts_document`. The detail makes one indexed read (`IKnowledgeIndexAttemptStore.FindLatestAsync`).
  - Readiness is unchanged.
- **Row 16, `sourceByteLength`:** on the list items, a per-row probe on `ux_knowledge_sources_document` in the same statement.
- **Writes keep their exact shape.** The detail read now uses a separate `KnowledgeDocumentDetailResponse`, so a create, update, index or upload response never claims "never attempted".
- **Row 17, file refusals:** `KnowledgeFileRejectedException` gains `Code` and `Limit`, and all 28 parser throw sites pass one of `KnowledgeFileRefusals`.
  - Statuses and English details are unchanged.
  - A password-protected PDF (PdfPig's `PdfDocumentEncryptedException`) is now told apart from a damaged one by its code alone.

### 3.3 Agents (row 18)

- **`channels: [{ kind: "WebChat" | "WhatsApp", id, status }]`** on agent list items and on `GET …/ai-agents/{id}`.
- **Permissions are resolved once.** `EffectivePermissionsAsync` makes the same two statements the single `ai-agents.view` check made, and it also decides which kinds are visible: website chat with `website-chat.view`, WhatsApp with `whatsapp.view`.
- **One statement per visible kind for the whole page** (`IAiAgentChannelReader`), scoped by the workspace. A reader with neither permission causes no channel read at all.
- **Identity and status only.** No key, number or credential.
- **The refusals are unchanged.** Writes keep their seven fields: the detail read uses `AiAgentDetailResponse`.

### 3.4 The workspace name and its rename (rows 20, 20a; decision D-3)

- **New permission `workspace.manage`** (group Workspace, "Manage workspace"). It is compile-time, so the Owner holds it with no data step.
- **`GET /api/tenants/{id}` sends a strong `ETag`** (`xmin`). The body is unchanged.
- **New `PUT /api/tenants/{id}/name` `{ name }`.** The order of checks:
  1. `If-Match` (428 without it);
  2. the name, under the creation rules (400 `workspace.name.invalid`);
  3. authorisation (404 for a non-member, 403 without `workspace.manage`);
  4. version (412 when stale).

  Success is 200 with the workspace and a new `ETag`. Renaming to the current name is a no-op and records nothing.
- **Audit `workspace.renamed`:** actor, workspace and time, never the names. It is written in the **same `SaveChanges`** as the rename, so a stale rename writes neither. That is stronger than the best-effort account events.

### 3.5 Public chat (rows 19, 20, 21, 22)

- **Hosted read** `GET /api/public/webchat/{key}` gains `workspaceName`, `greeting`, `accentColor` and `theme`. The **embedded read** gains `workspaceName`.
  - Both come from the existing single public-key statement, which now also joins `tenants` (name only, by the endpoint's own tenant) and projects the widget columns.
  - Nothing else about the workspace is published.
- **Admin origin refusals coded:** `website-chat.origin.invalid` and `website-chat.origin.too_many`, plus the widget's other rules.
- **The public refusals stay uncoded.** They are the same generic 404 as before, byte-identical apart from the trace id (§5).
- **`Start`/`End` corners:**
  - new enum values beside `Left`/`Right` (stored by name, no check constraint, no migration);
  - `v1.js` resolves them from `<html dir>`, else from the computed direction, on every layout;
  - `embed-chat.ts` forwards them;
  - old values behave as before.

### 3.6 Contract table

| Endpoint | Change | New fields or parameters | Permission | Backward compatible |
| --- | --- | --- | --- | --- |
| `GET /api/tenants/{t}/knowledge` | Last outcome and file size on each item | `lastIndexAttempt{status,failureCode,startedAt,completedAt}\|null`, `sourceByteLength` | `knowledge.view` (unchanged) | Yes: added fields; same items, order, paging |
| `GET /api/tenants/{t}/knowledge/{id}` | Last outcome | `lastIndexAttempt` | `knowledge.view` | Yes: added field; `ETag` unchanged |
| Knowledge writes (create, update, index, enable, disable, upload, replace) | None | none | unchanged | Yes: their responses keep the exact shape |
| `POST …/knowledge/files`, `PUT …/knowledge/{id}/source` | Coded refusals | `code` (`knowledge.file.*`), `limit` | `knowledge.manage` | Yes: same statuses, titles and details; extension members added |
| `GET /api/tenants/{t}/ai-agents` | Channels per item | `channels[{kind,id,status}]` | `ai-agents.view`; each kind only with `website-chat.view` / `whatsapp.view` | Yes: added field; same refusals and statement count for the permission check |
| `GET /api/tenants/{t}/ai-agents/{id}` | Channels | `channels` | as above | Yes: added field; `ETag` unchanged |
| Agent writes | None | none | unchanged | Yes: seven fields, as before |
| `GET /api/tenants/{id}` | Entity tag | header `ETag` | `workspace.view` | Yes: header only; body unchanged |
| `PUT /api/tenants/{id}/name` | **New** | body `{ name }`; `If-Match`; → 200 + `ETag` | **new `workspace.manage`** | Yes: new endpoint; nothing calls it today |
| `GET /api/tenants/{t}/permissions`, `GET /api/me/tenants/{t}/permissions` | One more catalogue entry | `workspace.manage` (group Workspace) | `roles.view` / membership | Yes: an added entry; the role editor shows it in both languages |
| `GET /api/public/webchat/{key}` (anonymous) | Appearance and workspace name | `workspaceName`, `greeting`, `accentColor`, `theme` | none (public) | Yes: added fields; unknown key still the same generic 404; limits unchanged |
| `GET /api/public/embed/{key}` (anonymous) | Workspace name; new corner values | `workspaceName`; `launcherPosition` may be `Start`/`End` | none (public) | Yes: added field; existing widgets keep `Left`/`Right`; refusals unchanged and uncoded |
| `GET`/`PUT …/website-chat/{id}/widget` | Two more corners; coded refusals | `launcherPosition: Start\|End`; `code` on 400 | `website-chat.view` / `.manage` | Yes: old values unchanged; same statuses and details |
| `/widget/v1.js` | Resolves `Start`/`End` from the host page's direction | none | public | Yes: `Left`/`Right` and unknown values behave as before |
| Every problem response with 400, 409, 412 or 422 | A `code` is always present | `code`; for knowledge files `limit` | — | Yes: an added member; status, `title`, `detail` and `errors` unchanged. The current client branches only on codes it already knows |
| 428 and a malformed `If-Match` (all conditional writes) | Coded | `precondition.required`, `precondition.if_match.invalid` | — | Yes: added member |

**OpenAPI.** The changed reads and the new `PUT` carry `WithSummary`, `Produces` and `ProducesProblem`. The new fields appear through their records. The generated document is kept as `<evidence>\live\openapi-v1.json` (§5).

**Architecture documents updated:**
- `problem-codes.md`: the guarantee, and the knowledge, agent, website chat, public chat, WhatsApp, workspace and internal sections;
- `knowledge-base-rag.md`;
- `knowledge-file-ingestion.md`;
- `ai-agent-vertical-slice.md`;
- `embeddable-web-chat-widget.md`: §8 corners and name, §9 codes, and §10, whose "no body" wording I corrected;
- `conversation-runtime-website-chat.md`;
- `tenant-onboarding-vertical-slice.md`;
- `tenant-rbac.md`;
- `authentication-tenant-membership.md`: D33, `workspace.renamed`.

### 3.7 Migrations

None. The approved plan lists no migration for 37.5a (§3.3), and none was needed:
- the new enum values are stored by name with no check constraint;
- `workspace.manage` is compile-time;
- `security_events.tenant_id` already exists (M3, 37.3).

`DatabaseModelAuthorityTests` stays green (§5), which proves the model and the migration chain still agree.

## 4. Decisions

- **37.5a was chosen from the worktree**, because your message did not name the part.
- **Reads carry the new fields; writes keep their shape.** `lastIndexAttempt` and `channels` appear on the GET list and detail only, through separate detail records. Rejected: adding the fields to the shared response and filling them with `null`/`[]` on writes. That would tell a client "never indexed" or "no channels" when the truth is "not read here".
- **The generic codes are global, through one hook** (`CustomizeProblemDetails`), not per area. The plan's "a client always has a code" holds everywhere at once. 37.5b replaces the generic code of its areas with specific ones; `validation.failed` keeps its meaning, "no more specific code yet".
- **The rename audit is transactional, not best effort.** A name visitors see should never change without its record.
- **The rename needs `workspace.manage` alone**, as the plan says. Its response shows the workspace's name, slug and status, which `workspace.view` would show. Rejected: requiring both, which the plan did not ask for.
- **`knowledge.file.header_required` added** beside the plan's list. A CSV without a header row had no fitting code; calling it `damaged` would word it wrongly.
- **`knowledge.file.encrypted` is detected only for PDF**, through PdfPig's own exception. An encrypted DOCX is an OLE container, indistinguishable from a renamed `.doc`, so it stays `type_mismatch`.
- **No `website-chat.origin.duplicate`.** Duplicates are merged and never refused today; adding a refusal would change current behaviour (rule 4).
- **The public 404 is not empty.** The prompt and the widget document say "bodiless", but since `UseStatusCodePages` the pipeline writes a generic problem body (`type`, `title`, `status`, `traceId`). I kept it exactly as it was, uncoded, and corrected the documents' wording instead of changing behaviour.
- **The embed page's open animation for `Start`/`End`** grows from the corner its own language suggests, because the frame cannot read the host page's direction. At worst the animation starts from the other corner. The frame's position is always the loader's, and correct.
- **Not tested at the parser level:** the CSV "damaged" path. CsvHelper runs with `BadDataFound = null` and tolerates every malformed input I tried, so I could not produce that refusal honestly. The site is coded, and the source scan covers it.

## 5. Verification

The backend gates ran in Docker as in the Phase 33.5 report §11:
- `mcr.microsoft.com/dotnet/sdk:10.0` with the repository mounted;
- a throwaway `pgvector/pgvector:pg17` and `redis:7-alpine` on a private network, through `OMNIAI_TEST_POSTGRES` / `OMNIAI_TEST_REDIS`;
- all of them removed afterwards.

Every gate ran alone. **Free memory** before each memory-gated command:
- backend run 1: 6.5 GB, after the Docker VM cache drop you chose (§2);
- backend run 2: 6.4 GB;
- backend run 3: 7.0 GB;
- `test:ci`: 7.0 GB;
- `build`: 7.0 GB.

The figures below are from **run 3, on the final code**. Run 1 failed four tests that pin a shape this step changes on purpose:
- the agent detail's seven fields;
- the agent list item's six fields;
- the hosted view's "agent name and nothing else";
- the permission catalogue's exact list.

I moved each pin to the new contract, with a line saying why, and runs 2 and 3 were green. Logs are in `<evidence>\gates\`.

| Check | Result |
| --- | --- |
| `dotnet restore` (NuGet audit on) | PASS |
| `dotnet build OmniAI.slnx -c Release` | PASS, **0 warnings, 0 errors** |
| `dotnet list package --vulnerable --include-transitive` | PASS, no vulnerable packages in any of the 8 projects |
| Tests, `OmniAI.Api.IntegrationTests` | PASS, 1,041 passed, 2 skipped |
| Tests, `OmniAI.Application.Tests` | PASS, 838 passed |
| Tests, `OmniAI.Domain.Tests` | PASS, 849 passed |
| Tests, `OmniAI.Infrastructure.IntegrationTests` | PASS, 1,151 passed, 1 skipped |
| Total | **3,879 passed, 3 skipped, 0 failed** (37.4: 3,751; 128 new). 0 leftover test databases |
| `DatabaseModelAuthorityTests` | PASS (in the run above): the model and the migration chain still agree |
| Migrations on a fresh database and on a copy of `omniai_p36` | NOT RUN: this step adds no migration (§3.7), so there is nothing to apply. `omniai_p36` was not migrated and needed no `pg_dump` |
| Problem-code guarantee (`ProblemCodeTests`) | PASS. The source scan finds every throw site in these areas coded (106 in the area folders, 3 in `TenantName`, 28 parser refusals); every published code is in `problem-codes.md`; the codes reach the wire |
| No N+1 (`ScreensReadModelTests`) | PASS. A knowledge page costs the same statements for 2 and for 9 documents. Agent channels cost 0, 1, 1 and 2 statements for none, web chat, WhatsApp and both, for a page of 5 agents. The public-key read stays one statement |
| Frontend `npm run format:check` | PASS |
| Frontend `npm run test:ci` | PASS, 96 files, 1,169 tests (37.4: 1,166) |
| Frontend `npm run build` | PASS (initial total 313.56 kB raw, 73.66 kB transfer) |
| Frontend `npm run i18n:check` | PASS, 2,077 messages, 0 errors, 0 warnings |
| API checks against the dev stack (`<evidence>\live\api-checks.json`) | PASS, **22/22**. See the list below |
| Public refusals before and after (`<evidence>\live\public-404\`) | PASS. Unknown key, malformed key, unapproved website and missing origin, on the hosted and the embedded read: status and body identical to the 37.4 build apart from the per-request trace id; no `code`, no `detail` |
| OpenAPI (`<evidence>\live\openapi-v1.json`) | PASS. The rename `PUT`, `KnowledgeDocumentDetailResponse`, `KnowledgeIndexAttemptResponse`, `AiAgentDetailResponse`, `AiAgentChannelResponse`, `RenameTenantRequest` and the new fields of the two public responses are all in the generated document |
| Chrome, English and Arabic (`<evidence>\live\chrome\`) | PASS, 17 of 18 rows. See the Chrome note below |
| p95 latency, before and after | See the table below |
| Provider canary | PASS: at the start, after each rebuild, at the end, and after the restart (`canary-*.txt`). Every provider secret, `OMNIAI_LIVE_*` and the SMTP credentials absent; SMTP host loopback |

**The API checks** ran with the demo owner, support and analyst accounts, a stranger account with its own workspace created through the API, and signed out. They cover:
- the new knowledge fields;
- a stand-in index showing `Succeeded`;
- an upload's size;
- four file refusals keeping their statuses with codes;
- an agent's `WebChat`/`Active` and `WhatsApp`/`Draft` channels on the list and the detail;
- rename 428, 400, 200 and stale 412;
- the new name on both public reads at once, then the name put back, with +2 audit rows, each with its actor;
- `Start` through the admin PUT and the embedded read, then restored;
- both origin codes;
- the support account: knowledge 200, agents 403, rename 403;
- the analyst account: workspace 200 with an `ETag`, knowledge 403, rename 403;
- another workspace: 404 through ours and theirs;
- a refusal outside these areas carrying `validation.failed`;
- signed out: rename 401, and the generic 404.

The two check documents were disabled afterwards.

**Chrome.** The Chrome DevTools MCP could not start, because another session's Chrome held its profile, and I did not close that browser. These checks ran with **Playwright in Google Chrome (channel `chrome`, a temporary profile), not the MCP**, as in 37.3.
- **English and Arabic (`dir=rtl`):** the knowledge list and a document, the agent list and editor, the website chat detail and widget settings, and the role editor all render as before, and their reads carry the new fields.
- **The role editor** offers "Manage workspace" / «إدارة مساحة العمل» with its description.
- **The hosted chat page** renders in English (Northwind) and, with Arabic chosen, right to left (Al Noor), with the workspace's name in its read.
- **`Start` puts the launcher bottom-left on a left-to-right host page and bottom-right on a right-to-left one**, measured on the frame (`widget-start-ltr.png`, `widget-start-rtl.png`). The widget was restored to `Right` afterwards.
- **The one failed row** is "no console errors". It saw three errors, none from this step:
  - the signed-out sign-in page's two 401s from the session read, which Phase 37.5b's session check is planned to remove;
  - the synthetic widget host page's own `favicon.ico` 404 (its folder holds only two HTML files).

  A re-run would reproduce them exactly, so I did not re-run it.

**p95 latency (ms)**, 300 sequential reads after 30 warm-up, three runs each, on the seeded dev data (Northwind: 15 documents before and 17 after, the two check documents; 5 agents). Before is the 37.4 build, after is the final build:

| Read | Before (runs 1 / 2 / 3) | Before, median | After (runs 1 / 2 / 3) | After, median |
| --- | --- | --- | --- | --- |
| `GET /knowledge` (page 1, 20) | 73.1 / 38.0 / 31.9 | **38.0** | 22.0 / 19.7 / 21.6 | **21.6** |
| `GET /knowledge/{id}` | 70.0 / 24.8 / 20.2 | **24.8** | 22.3 / 17.6 / 18.2 | **18.2** |
| `GET /ai-agents` (page 1, 20) | 29.2 / 32.3 / 44.1 | **32.3** | 17.5 / 20.4 / 18.5 | **18.5** |
| `GET /ai-agents/{id}` | 36.3 / 20.4 / 23.5 | **23.5** | 22.5 / 21.8 / 16.8 | **21.8** |
| `GET /website-chat` | 30.9 / 21.5 / 29.9 | **29.9** | 17.1 / 18.8 / 16.4 | **17.1** |
| `GET /api/tenants/{id}` | 51.7 / 41.5 / 22.6 | **41.5** | 16.7 / 16.7 / 16.8 | **16.7** |
| `GET /api/public/webchat/{key}` (anonymous) | 30.4 / 22.9 / 16.8 | **22.9** | 17.0 / 16.6 / 16.7 | **16.7** |
| `GET /api/public/embed/{key}` (anonymous) | 24.8 / 20.4 / 19.1 | **20.4** | 16.9 / 17.8 / 16.5 | **16.9** |

Read this as **no regression, not a speed-up**. The "before" runs met a busier machine than the "after" runs: other projects' containers were active, and memory was tight before the cache drop. Every read sits on the same 15 ms loopback floor at p50. The structural evidence is the statement counts above.

## 6. See it live

The API is on `http://127.0.0.1:5027` and the dev server on `http://localhost:4200`, both left running after the restart. Sign in as `p36.owner@omniai.example` (password in `demo-accounts.md`) and choose **Northwind Operations**. Today's screens look as before; Phase 38 draws the new fields. To see the new contracts, call them from the signed-in tab's devtools console (same origin). Use `<t>` for the Northwind id from `GET /api/me/tenants`, and send the `X-XSRF-TOKEN` header (from the `XSRF-TOKEN` cookie) on writes.

- **Knowledge:** `GET /api/tenants/<t>/knowledge`. Each item now has `lastIndexAttempt` and `sourceByteLength`; "P37.5a check — returns window" shows `Succeeded`. Upload a file named `scan.pdf` holding plain text on `/knowledge/upload` to see the refusal's `code` (`knowledge.file.type_mismatch`) in the network panel.
- **Agents:** `GET /api/tenants/<t>/ai-agents`. "Northwind Concierge" lists `WebChat`/`Active` and `WhatsApp`/`Draft`. Sign in as `p36.support@omniai.example` to see the 403 that support has always had there.
- **The new permission:** open the role editor at `/roles/new`. "Manage workspace" is in the Workspace area; switch the language to Arabic to see «إدارة مساحة العمل».
- **Renaming the workspace:**
  1. `GET /api/tenants/<t>`, and read its `ETag`.
  2. `PUT /api/tenants/<t>/name` with `{ "name": "Northwind Retail" }`, `If-Match: <ETag>` and the XSRF header.
  3. Open `http://localhost:4200/chat/9veAaKzu0Ee-isA7jrQzYw` and read `GET /api/public/webchat/9veAaKzu0Ee-isA7jrQzYw` in the network panel: `workspaceName` has changed.
  4. Send the same `PUT` with the old tag to see `412 precondition.failed`.
  5. Rename back to "Northwind Operations" with the new tag.
- **The corner that follows the page:**
  1. Set the Northwind widget's `launcherPosition` to `Start` with `PUT /api/tenants/<t>/website-chat/<endpoint>/widget`. Send the whole widget body from its `GET`, with `If-Match`.
  2. Serve the host page: `cd C:\Users\mouay\Desktop\omniai-redesign-evidence\p36-01\widget-host && python -m http.server 8099 --bind 127.0.0.1`.
  3. Open `http://127.0.0.1:8099/?key=9veAaKzu0Ee-isA7jrQzYw`: the launcher is bottom-left. Add `&lang=ar` and it moves bottom-right.
  4. Put `Right` back afterwards.
- **Coded refusals:** the same widget `PUT` with `"allowedOrigins": ["https://shop.example.com/path"]` answers `400 website-chat.origin.invalid`; an unknown public key still answers the generic 404 with no code.

## 7. Frontend follow-ups (what Phase 38 adopts)

| Adopt | Contract |
| --- | --- |
| Show "Failed" (and why) after a reload; "indexing now" for `Started` | `lastIndexAttempt: { status: Started\|Succeeded\|Failed\|Unknown, failureCode, startedAt, completedAt } \| null` on `GET …/knowledge` items and `GET …/knowledge/{id}` |
| File size in the knowledge list | `sourceByteLength: number \| null` on list items |
| Word file refusals by code, with the bound | `code` (`knowledge.file.*`) and `limit` on 400/413/415/422 from `POST …/knowledge/files` and `PUT …/source` |
| Agent channels from the agent read; drop the two channel-list reads | `channels: [{ kind: WebChat\|WhatsApp, id, status }]` on `GET …/ai-agents` items and `GET …/ai-agents/{id}` |
| A "Workspace name" setting for `workspace.manage` holders; say in the widget settings that visitors see it | `GET /api/tenants/{id}` → `ETag`; `PUT /api/tenants/{id}/name { name }` with `If-Match` → 200 + `ETag`; 400 `workspace.name.invalid`, 403, 404, 412 `precondition.failed`, 428 `precondition.required` |
| Hosted page: the workspace's name, greeting, accent and theme | `workspaceName`, `greeting`, `accentColor`, `theme` on `GET /api/public/webchat/{key}` |
| Widget: the workspace's name | `workspaceName` on `GET /api/public/embed/{key}` |
| `Start`/`End` corners in the widget settings (the embed page already forwards them) | `launcherPosition: Left\|Right\|Start\|End` on the widget PUT/GET and the embedded read |
| Word knowledge, agent, channel and workspace refusals by code; generic wording for `validation.failed`/`state.conflict` | `problem-codes.md` |

## 8. Risks and open issues

- **The generic codes reach 37.5b's areas now.** Roles, invitations, team and billing answer `validation.failed`/`state.conflict` until 37.5b names their rules. The current frontend reads only a few specific codes, so nothing changes for it.
- **An old cached loader** (`v1.js` before 37.5a) would place a `Start`/`End` widget on the right. The loader is served by the same API, so it updates with the deployment.
- **The current admin widget screen** offers only Left/Right. A widget set to `Start`/`End` through the API shows no matching option there until Phase 38.
- **Arabic copy** for the new permission («إدارة مساحة العمل») needs a native reading, as in earlier steps.
- **The CSV "damaged" refusal** has no generated test case (§4).
- **Memory.** The gates needed the Docker VM's cache dropped first (§2). Later steps on this machine may need the same.

## 9. Proposed checkpoint commit message

```
feat: say what indexing did, which channels an agent answers on, and whose chat it is

Phase 37.5a: what the redesigned knowledge, agent and channel screens and
the public chat pages asked the backend for (plan rows 15-22, 20a, 28a).

- Knowledge: the last indexing outcome (lastIndexAttempt) on the list and
  the detail, the source file's size on the list, and a stable code (with
  the bound it broke) on every file refusal, encrypted PDFs told apart.
- Agents: the channels each agent answers on, only the kinds the reader
  may see, one statement per kind for a whole page.
- Workspace (D-3): the hosted and embedded chat reads show the workspace's
  name; GET /api/tenants/{id} sends an ETag; PUT /api/tenants/{id}/name with
  If-Match, guarded by the new workspace.manage permission, audited as
  workspace.renamed in the same transaction. The hosted page also gets the
  widget's greeting, accent and theme.
- Widget: Start and End corners, resolved by the loader from the host
  page's direction; the embed page forwards them. Admin origin refusals are
  coded; the public refusals stay one uncoded 404.
- Problem codes: every throw site in these areas names its rule, and every
  400, 409, 412 and 422 carries a code, generic where none is specific yet.
  Catalogue in problem-codes.md, held by a source-scanning test.

Every change is additive; no migration, no package, no deploy/ change.
Frontend: the embed page forwards Start/End, and the role editor words
workspace.manage in English and Arabic (owner rulings).

Gates: backend 0 warnings, 3,879 passed, 3 skipped, no vulnerable
packages; frontend format, 1,169 tests, build and i18n green; API checks
22/22; public 404s identical before and after.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```
