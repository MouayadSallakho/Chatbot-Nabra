# Phase 37.1: Release blockers

The patched packages, the failed-turn announcement, the brand, and repository hygiene. Evidence is in `C:\Users\mouay\Desktop\nabra-backend-evidence\p37-1\`.

## 1. Summary

- When the AI cannot answer, the customer's message now appears in an open inbox right away, on website chat and WhatsApp. Before, it appeared only after a reload.
- Every email subject, heading, footer and sender name, every PDF's properties, and the website chat loader now say **Nabra**. The loader's frame title is «الدردشة» on Arabic websites and "Chat" elsewhere.
- Websites that already embed the chat keep working unchanged, because the install snippet is byte-for-byte the same.
- The API and migrator images build again from the unchanged `deploy/` files. A clean restore passes the NuGet vulnerability audit, and the vulnerable-package list is empty.
- The older synthetic password is gone from the repository's files. The performance tools now require it from the environment.

## 2. Starting point

- Branch `master`, HEAD `a188e9f` ("docs: amend M3 with the workspace of a security event"), worktree clean.
- The 37.0 plan report is committed (`b193bbb`, amended by `a188e9f`).

**Two conflicts between the plan and the prompt were raised before any code was written (rule 19). Your answers:**
- MessagePack **2.5.303**, the lowest release that clears the advisory, instead of the plan's 2.5.307.
- The snippet keeps **`data-omniai-key`**: no `data-nabra-key`, and no attribute, global or bridge name changes.

## 3. Changes by area

```
 M backend/Directory.Packages.props
 M backend/src/OmniAI.Api/Widget/v1.js
 M backend/src/OmniAI.Application/Conversations/CustomerTurnRunner.cs
 M backend/src/OmniAI.Application/Notifications/NotificationCatalog.cs
 M backend/src/OmniAI.Application/Notifications/OperationalEmailRenderer.cs
 M backend/src/OmniAI.Infrastructure/Notifications/Email/OperationalEmailOptions.cs
 M backend/src/OmniAI.Infrastructure/Reports/Rendering/PdfReportRenderer.cs
 M backend/tests/OmniAI.Api.IntegrationTests/AiAgents/AiAgentApiFixture.cs
 M backend/tests/OmniAI.Application.Tests/Notifications/NotificationTemplateTests.cs
 M backend/tests/OmniAI.Application.Tests/WebChat/WebChatTestDoubles.cs
 M backend/tests/OmniAI.Application.Tests/WhatsApp/WhatsAppRuntimeReuseTests.cs
 M backend/tests/OmniAI.Infrastructure.IntegrationTests/Notifications/OperationalEmailTests.cs
 M backend/tests/OmniAI.Infrastructure.IntegrationTests/Reports/PdfUnicodeTests.cs
 M docs/architecture/embeddable-web-chat-widget.md
 M docs/architecture/human-handoff-realtime-inbox.md
 M docs/architecture/operational-notifications.md
 M docs/architecture/reports-exports-scheduling.md
 M docs/phase-reports/PHASE_36_1_BASELINE_REPORT.md
 M frontend/src/app/features/embed/widget-loader.spec.ts
 M tools/performance/README.md
 M tools/performance/{endpoint-profile,fanout,limiter-contention,meta-ambiguity,probe,
                      report-drill,report-load,rolling-restart,signalr-load}.mjs
 M tools/performance/k6/lib.js
 M tools/performance/run-stages.sh
 M tools/performance/seed/seed.mjs
?? backend/tests/OmniAI.Api.IntegrationTests/Realtime/FailedTurnRealtimeApiTests.cs
?? backend/tests/OmniAI.Api.IntegrationTests/Realtime/InboxHubClient.cs
?? backend/tests/OmniAI.Application.Tests/WebChat/FailedTurnAnnouncementTests.cs
?? backend/tests/OmniAI.Infrastructure.IntegrationTests/Deployment/PublicNameTests.cs
?? tools/performance/lib/env.mjs
?? docs/phase-reports/PHASE_37_1_RELEASE_BLOCKERS_REPORT.md
```

**Outside the repository (allowed by this step):**
- `p36-01\p36-stack.sh` and `p36-01\stack.md`: the `-p:NuGetAudit=false` workaround is removed.
- `p36-01\widget-host\index.html`: accepts `?lang=` so the same host page can be English or Arabic.

### 3.1 Patched packages

`backend/Directory.Packages.props` has a new group, "Security pins (transitive)". Each pin carries a comment naming its parent package and advisories. Transitive pinning was already on.

| Package | From | To | Parent | Advisories cleared |
| --- | --- | --- | --- | --- |
| `System.Security.Cryptography.Pkcs` | 10.0.0 | **10.0.13** | MimeKit 4.18.1 | GHSA-846f-f764-76mx (high), GHSA-2fmv-rv2c-59x7 (medium); 10.0.0–10.0.12 vulnerable |
| `MessagePack` | 2.5.302 | **2.5.303** | Microsoft.AspNetCore.SignalR.StackExchangeRedis 10.0.11 | GHSA-qhrr-8q5h-9q3h (medium); everything below 2.5.303 is vulnerable |

- **How the versions were chosen:** from the GitHub Advisory Database ranges on 2026-10-21. Both are the first fixed release in their own major line, so no major version changed.
- **Advisories:** none suppressed, and `NuGetAudit` is not set anywhere in the repository.
- **Runtime image:** `mcr.microsoft.com/dotnet/aspnet:10.0` is Ubuntu 24.04.5 with `libicu74` and `tzdata 2026c` (`Asia/Riyadh` present). The built `omniai-p37-api-check` image had 12 `libicu*` libraries and the zoneinfo files.
  - Nothing in `deploy/` needs to change for 37.3 or 37.6.
  - `backend/Directory.Build.props` keeps `InvariantGlobalization=true`, as the plan intends. Arabic text comes from code catalogues, digits are Western, dates are not culture-formatted, and IANA time zones work with tzdata alone. A later step may turn it off only if it truly needs culture data, and only after asking you. (Reworded in Revision 1.)

### 3.2 Failed AI turn announced

- **Where:** `CustomerTurnRunner.AnswerAsync`, in the `catch` block.
- **Order:** `FailTurnAsync` commits, then `Announce(turn, CancellationToken.None)` runs, then `throw;`.
- **Best effort:** `Announce` goes through the existing never-throwing `AnnounceQuietlyAsync`. Neither a broken backplane nor an abandoned request can replace the original exception.
- **Logging:** the shipped `SignalRConversationNotifier` logs a transport failure as warning 4100.
- **Replays:** a replay of the same client message id is refused with `WebChatTurnAlreadyFailedException` inside `ReplayAsync`, before this code runs, so it is never announced twice.
- **Unchanged:** the lock, the replay rules and idempotency.
- **Channels:** website chat (hosted and embedded) and WhatsApp share the runner, so both are covered.

### 3.3 The brand

| Where | Before | After |
| --- | --- | --- |
| `NotificationCatalog.Subject` | `OmniAI: {title}` | `Nabra: {title}` |
| `OperationalEmailRenderer` header (text and HTML) | `OmniAI` | `Nabra` |
| Footer | "…in your OmniAI notification preferences…" | "…in your Nabra notification preferences…" |
| Fallback action | "Open OmniAI" | "Open Nabra" |
| `Email:Smtp:FromName` default | `OmniAI` | `Nabra` |
| PDF `Creator` / `Producer` | `OmniAI reports` / `OmniAI` | `Nabra reports` / `Nabra` |
| `Widget/v1.js` text (comments a site owner can read) | OmniAI | Nabra |
| Loader frame title | `Chat` always | the host page's `<html lang>` decides, falling back to `navigator.language`. `ar` or `ar-*` gives «الدردشة» (written as `\u` escapes); anything else gives "Chat". |

**Unchanged on purpose:**
- the loader's attribute (`data-omniai-key`), its global (`__omniaiWidgetLoaded`) and its bridge messages (`omniai:v1:*`);
- the frame's geometry, sandbox, `allow`, referrer policy and origin checks;
- the install snippet the API returns;
- namespaces, the `omniai.session` cookie, the `X-OmniAI-*` headers, the database, Docker and environment variables.

**`deploy/`:** no user-facing "OmniAI" value was found; the secret env files were not opened.
- `providers.env.example` has no `Email__Smtp__FromName`, so a deployment gets the new default `Nabra`.
- The rest is comments and internal names (compose project `omniai`, image names, roles, nginx upstreams, the `omniai-admin` build folder).
- For your decision, optional: add `Email__Smtp__FromName=` to `providers.env.example` with a comment, so operators see they can set it.

**Guard:** `PublicNameTests` scans the following and fails on any mention of the code name ("OmniAI", "Omni AI" or "Omni-AI", any case) once known internal-name patterns are removed:
- every C# string literal under `backend/src`, excluding migrations;
- the whole loader, comments included;
- JSON settings.

Two bare `"OmniAI"` literals are allowed by file: the Data Protection application name and the meter name. Run against HEAD's source, the same rules flag exactly the 13 old values.

### 3.4 Repository hygiene

`git grep -l` (value never printed) found it in 12 files: the 36.1 report and 11 tools.

**The 36.1 report:** the value was in **§9 "See it live", line 192**, not §6 (the 37.0 plan had already noted this). The line now points to `C:\Users\mouay\Desktop\omniai-redesign-evidence\p36-01\demo-accounts.md`.

**The tools:**
- The 9 `tools/performance/*.mjs` and `seed/seed.mjs` call `seedPassword()` from the new `tools/performance/lib/env.mjs`. It runs before any connection is opened, and stops with exit code 2 and a clear message when `SEED_PASSWORD` is missing.
- `k6/lib.js` throws in the k6 init stage when `PASSWORD` is missing.
- `run-stages.sh` forwards `SEED_PASSWORD` to the k6 container by name only (`docker -e PASSWORD`), so the value never appears on a command line.
- The README is updated.

**Git history:** the value remains in Git history. It was synthetic and local only, and history is not rewritten.

### 3.5 Contract table

| Endpoint / surface | Change | New fields or parameters | Permission | Backward compatible |
| --- | --- | --- | --- | --- |
| `/hubs/workspace-inbox` and `/hubs/public-chat`, event `conversationChanged(conversationId)` | Also raised once after a failed AI turn | none | unchanged (session + `conversations.view` / realtime ticket) | Yes. Same event and payload. Clients already refetch through the authorised HTTP reads. |
| `GET /widget/v1.js` | Frame title follows the host page's language; comments say Nabra | none | anonymous (unchanged) | Yes. Attribute, global, messages, sandbox and geometry are unchanged. `widget-loader.spec.ts` passes. |
| `GET /api/tenants/{tenantId}/website-chat/{id}/widget` (install snippet) | none (verified live) | none | unchanged | Yes, byte-identical |
| Operational emails (SMTP, not HTTP) | Subject prefix, heading, footer, fallback action and default sender name say Nabra | none | n/a | Yes, values only |
| `GET …/reports/executions/{id}/download` (PDF) | PDF `Creator` / `Producer` metadata | none | unchanged | Yes, values only |

- **OpenAPI:** no HTTP request or response shape changed, so the generated document is unchanged. (It is mapped in Development only, and its title comes from the internal assembly name.)
- **Architecture docs** were updated to match: `human-handoff-realtime-inbox.md` (§9 failed-turn announcement, §11 MessagePack and the two-instance proof), `operational-notifications.md`, `embeddable-web-chat-widget.md` and `reports-exports-scheduling.md`.

### 3.6 Migrations

None. The approved plan lists none for 37.1. `omniai_p36` was not migrated, so no `pg_dump` was needed.

## 4. Decisions

- **MessagePack 2.5.303 and keeping `data-omniai-key`.** Both were your answers to the two plan–prompt conflicts in §2.
- **A protocol-level hub client in the tests, rather than `Microsoft.AspNetCore.SignalR.Client`.** That package would have been an addition, which this step does not allow. `InboxHubClient` speaks the documented JSON hub protocol over TestServer's WebSocket, carrying the session cookie the real sign-in issued, so nothing between test and server is a double.
- **Backplane proof by two in-process hosts.** I chose this over a temporary second dev container. `AiAgentApiFixture` gained `CreateSecondInstance()`, which uses the same configuration method, database and Redis. A failed turn raised on instance A reaches a member connected only to instance B, which exercises MessagePack on both the write and the read side.
- **Announce with `CancellationToken.None` through `AnnounceQuietlyAsync`,** as the plan says. The Application layer stays silent and the SignalR notifier logs. A non-SignalR implementation that throws would therefore not be logged; that is the existing contract of `RealtimeAnnouncements`.
- **Loader Arabic word as `\u` escapes,** so it survives a host page that declares another character set. The page's language wins over the browser's. `arn` (Mapudungun) is not treated as Arabic.
- **Rejected:** adding `data-nabra-key` (your decision); renaming any internal name; writing the Chrome sign-in helper inside the repository (the MCP can only read files in its workspace roots), so a one-shot loopback helper was used instead (§5).
- **Deferred:**
  - Arabic email bodies (37.3).
  - The hosted chat page's own interface text, which stays English even for an Arabic workspace's agent. This existed before this step and is not in it.

## 5. Verification

| Check | Result |
| --- | --- |
| Clean restore, NuGet audit **on** (`dotnet restore OmniAI.slnx --force --no-http-cache`, sdk 10.0.400) | **PASS**, exit 0, no NU19xx |
| Resolved versions | **PASS**: Pkcs 10.0.13, MessagePack 2.5.303 in every project that has them |
| `dotnet list package --vulnerable --include-transitive` | **PASS**: "no vulnerable packages" for all 8 projects |
| `dotnet build OmniAI.slnx -c Release` | **PASS**: **0 warnings, 0 errors** (run 2; run 1 failed on a broken string literal in a new test, fixed) |
| Tests, `-m:1`, throwaway PostgreSQL 17 + pgvector and Redis 7 (removed afterwards; 0 leftover test databases) | **PASS**, details in the next rows |
| — `OmniAI.Api.IntegrationTests` | **PASS**: 897 passed, 2 skipped (37.0: 893). +4 realtime tests. |
| — `OmniAI.Application.Tests` | **PASS**: 732 passed (37.0: 714). +4 failed-turn, +1 WhatsApp, +13 "every email names Nabra". |
| — `OmniAI.Domain.Tests` | **PASS**: 797 passed (unchanged) |
| — `OmniAI.Infrastructure.IntegrationTests` | **PASS**: 1,053 passed, 1 skipped (37.0: 1,031). +21 name guard, +1 PDF properties. |
| — Total | **3,479 passed, 3 skipped, 0 failed** |
| `DatabaseModelAuthorityTests` | **PASS** (inside the Infrastructure run) |
| Migrations on a fresh database and on a copy of the dev database | **NOT RUN**: this step has no migration |
| `deploy/api.Dockerfile`, unchanged, `--no-cache`, tag `omniai-p37-api-check` | **PASS**; image removed afterwards |
| `deploy/migrator.Dockerfile`, unchanged, `--no-cache`, tag `omniai-p37-migrator-check` | **PASS**; image removed afterwards. `:p36` images untouched. |
| Backplane with the pinned MessagePack: two hosts, one Redis, client on the other host | **PASS** (`A_failed_turn_on_one_instance_reaches_an_inbox_on_the_other`) |
| Failed-turn tests: once, after the failure is committed, failing announcer, replay | **PASS** (Application: 4 + WhatsApp 1; API: an open inbox receives it; another workspace hears nothing and cannot subscribe; a member without `conversations.view` cannot subscribe; replay not re-announced) |
| One email of each kind rendered by tests, Nabra in subject, sender and body | **PASS**: all 13 kinds (subject, text and HTML header, footer, no "omniai" anywhere). The sender display name `Nabra` was checked on a real message through the loopback SMTP sink. |
| Frontend `format:check` | **PASS** |
| Frontend `test:ci` | **PASS**: 93 files, 1,114 tests (37.0: 1,105; +9 frame-title specs) |
| Frontend `build` | **PASS** |
| Frontend `i18n:check` | **PASS**: 2,035 messages, 0 errors, 0 warnings |
| Dev API rebuilt with the stack script (audit on, no workaround) and recreated | **PASS** |
| API checks against the dev stack | **PASS** for what changed: hosted send 503 with the message kept and messages read back 200; loader 200 with the new code; snippet read 200, unchanged |
| Dev-stack refusals for missing permission and another workspace | **NOT RUN** live: no new endpoint or field in this step. The unchanged hub refusals are proven in the new API integration tests. |
| Chrome, en: failed turn reaches the open inbox without a reload | **PASS**: same document, row appeared; no console errors |
| Chrome, ar (RTL, Arabic workspace and agent): same | **PASS**: screenshot `live/inbox-ar-failed-turn-live.png` |
| Chrome: loader frame title on the local widget host page | **PASS**: English page "Chat"; Arabic page «الدردشة» with an en-US browser. Sandbox, `allow`, referrer policy and size unchanged; opening still works (400 px panel). |
| Chrome: widget settings page, en and ar | **PASS**: renders in both, snippet unchanged, no console errors |
| Tools without `SEED_PASSWORD` | **PASS**: all 10 Node tools exit 2 before connecting. k6: **NOT RUN** (k6 not installed; the init-stage throw was reviewed in code). |
| p95 of changed list endpoints | **NOT RUN (n/a)**: no list endpoint changed in this step |
| Provider canary (before any traffic) | **PASS**: all provider variables absent, every `OMNIAI_LIVE_*` absent, `Email__Smtp__*` absent (`gates/canary.txt`) |
| Secrets | The password value is in no repository file and no evidence file. The temporary sign-in helper and `p37-1\tmp\` were removed. |

Logs are in `p37-1\gates\`:
- `backend-gates-run1.log` and `backend-gates-run2.log`, with the raw restore, build and test logs in `run2\`;
- the image build logs;
- `fe-*.log`;
- `dev-build.log`;
- `canary.txt`.

The live notes are in `p37-1\live\live-checks.md`.

## 6. See it live

The dev API is on http://127.0.0.1:5027 and the dev server on http://localhost:4200. Accounts are in `C:\Users\mouay\Desktop\omniai-redesign-evidence\p36-01\demo-accounts.md`.

1. **A failed reply still reaches the inbox.**
   1. Sign in as the owner, choose Northwind, and open http://localhost:4200/conversations.
   2. In a private window, open http://localhost:4200/chat/9veAaKzu0Ee-isA7jrQzYw and send a message.
   3. The chat reports that no answer could be produced (there is no AI provider locally), and the message appears at the top of the open inbox without a reload.
   4. In Arabic: switch the app to العربية, choose شركة النور, and use http://localhost:4200/chat/o-M5OubU2TJkyTCA-CRvYg.
2. **The frame title follows the website's language.**
   1. Serve the host page with `python -m http.server 8099 --bind 127.0.0.1` in `p36-01\widget-host`.
   2. Open http://127.0.0.1:8099/?key=9veAaKzu0Ee-isA7jrQzYw. The frame is titled "Chat".
   3. Open the same URL with `&lang=ar` added. The frame is titled «الدردشة», and the widget opens as before.
3. **The install snippet is unchanged.** Open Channels → Website chat → an agent. The snippet still carries `data-omniai-key`.
4. **Emails and PDFs:** no SMTP in this step. The rendered subjects, bodies and sender are proven by the tests above. A PDF report downloaded from Reports shows Creator "Nabra reports" in the reader's document properties.

## 7. Frontend follow-ups

- **Required for Phase 38:** nothing.
- **Inbox:** it already refetches on `conversationChanged`, which now also fires after a failed AI turn. Showing a failed-turn divider waits for the `events` contract of 37.4.
- **Widget settings, optional:** a note that the snippet attribute keeps its historical name. Because `data-omniai-key` is unchanged, there is no new snippet to advertise.

## 8. Risks and open issues

- **The old synthetic password stays in Git history** (synthetic, local only). History is not rewritten.
- **`InvariantGlobalization=true` stays on.** The plan needs no culture data: Arabic text comes from code catalogues, digits are Western, dates are not culture-formatted, and IANA time zones need only tzdata, which the image has. A later step may turn it off only if it truly needs culture data, and only after asking you. (Reworded in Revision 1.)
- **Silent announcer failures outside SignalR:** a future announcer that is not the SignalR one and fails would not be logged, because `AnnounceQuietlyAsync` is silent by design.
- **Hosted chat page language:** its interface text stays English for an Arabic workspace's agent. This existed before 37.1; I propose it for the step that owns the hosted page.
- **Guard heuristics:** `PublicNameTests` reads C# literals line by line. A multi-line raw string literal could carry a user-facing "OmniAI" past it. None exists today.
- **k6 not run:** the k6 change was reviewed but not executed, because k6 is not installed.
- **Line endings:** files edited through scripts have LF endings in the working copy (Git warns that it will convert them). Git normalises them on commit, so the diff and the build are unaffected.
- **Optional `deploy/` addition, for your decision:** `Email__Smtp__FromName=` in `providers.env.example`.

## 9. Proposed checkpoint commit message

```
fix: clear the release blockers, and say Nabra where people read

- Pin System.Security.Cryptography.Pkcs 10.0.13 and MessagePack 2.5.303
  (GHSA-846f-f764-76mx, GHSA-2fmv-rv2c-59x7, GHSA-qhrr-8q5h-9q3h); a clean
  restore passes the NuGet audit and the deploy/ images build unchanged.
- Announce a failed AI turn once, after it is committed, on both channels;
  best effort, never replacing the failure, never on a replay.
- Nabra in email subjects, bodies and sender, PDF properties and the
  widget loader; the frame title follows the host page's language. The
  snippet, attribute, global and bridge names are unchanged.
- PublicNameTests keeps user-facing backend text free of the code name.
- Remove the old synthetic password from the 36.1 report and the
  performance tools; the tools now require SEED_PASSWORD.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

## Revision 1 (2026-10-21)

### R1.1 What changed

| Area | Change | Files |
| --- | --- | --- |
| Install snippet | The snippet the API returns now carries the key as **`data-nabra-key`**, replacing the earlier answer in §2 that kept `data-omniai-key` | `backend/src/OmniAI.Api/WebChat/WidgetLoaderEndpoints.cs` |
| Loader | `v1.js` reads `data-nabra-key` first and falls back to `data-omniai-key`, so every snippet pasted before 37.1 keeps working without edits. When one tag carries both, the newer name wins, and a page with one tag of each still loads one widget. The `__omniaiWidgetLoaded` global and the `omniai:v1:*` bridge messages keep their names, because nobody installing the chat reads them | `backend/src/OmniAI.Api/Widget/v1.js` |
| Tests | The snippet API test expects `data-nabra-key` and asserts that the snippet contains no "omniai" in any case. The widget settings spec shows the new snippet. 5 new loader specs: the new attribute, the old attribute, both on one tag, an empty new attribute, one tag of each on a page | `WebChatWidgetApiTests.cs`, `website-chat-widget.spec.ts`, `widget-loader.spec.ts` |
| Documentation | The install example and "Names in the loader" describe both attributes and their precedence | `docs/architecture/embeddable-web-chat-widget.md` |
| `InvariantGlobalization` | Reworded where it is described, with no change to `Directory.Build.props`: it stays on, and a later step may turn it off only if it truly needs culture data, and only after asking you | this report, §3.1 and §8 |
| `deploy/providers.env.example` | The optional item from §3.3 and §8: a commented `# Email__Smtp__FromName=Nabra` with one line saying the default is Nabra. Only the example file changed; no secret env file was opened | `deploy/providers.env.example` |

**Outside the repository:** `p36-01\widget-host\nabra-key.html` is a second host page that embeds the widget with `data-nabra-key`. `index.html` keeps `data-omniai-key`, and both accept `?lang=`.

**This revision supersedes these earlier statements:**
- §1, third bullet ("the install snippet is byte-for-byte the same");
- §2, the second answer;
- §3.3, the attribute and the snippet in "Unchanged on purpose", and the optional `deploy/` item;
- the snippet row of §3.5;
- §4, the `data-omniai-key` decision and the rejected `data-nabra-key`;
- §6, step 3;
- §7, the widget-settings note;
- §8, the optional `deploy/` item.

### R1.2 Contract change

| Endpoint / surface | Change | Permission | Backward compatible |
| --- | --- | --- | --- |
| `GET /api/tenants/{tenantId}/website-chat/{id}/widget`, `installSnippet` | The value now carries `data-nabra-key="…"`; the response shape is unchanged | unchanged | Yes. The field and its type are the same, and the frontend shows the value as it comes. Sites with the old snippet keep working, because the loader reads both attributes |
| `GET /widget/v1.js` | Reads `data-nabra-key`, then `data-omniai-key` | anonymous (unchanged) | Yes. Geometry, sandbox, `allow`, referrer policy, global and bridge messages are unchanged |

OpenAPI is unchanged: no request or response shape changed.

### R1.3 Verification

| Check | Result |
| --- | --- |
| Backend gates (`gates\backend-gates-rev1.log`, 08:18): clean restore with the NuGet audit **on** | **PASS**, exit 0 |
| Resolved versions | **PASS**: Pkcs 10.0.13, MessagePack 2.5.303 |
| `dotnet list package --vulnerable --include-transitive` | **PASS**: no vulnerable packages in any of the 8 projects |
| `dotnet build OmniAI.slnx -c Release` | **PASS**: 0 warnings, 0 errors |
| Tests, `-m:1`, throwaway PostgreSQL and Redis | **PASS**: Api.IntegrationTests 897 passed, 2 skipped; Application 732; Domain 797; Infrastructure.IntegrationTests 1,053 passed, 1 skipped. **3,479 passed, 3 skipped, 0 failed**, 0 leftover test databases. The API count is unchanged, because the snippet test was updated in place |
| Frontend `format:check` | **PASS** |
| Frontend `test:ci` | **PASS**: 93 files, **1,119** tests (before the revision: 1,114; +5 loader specs) |
| Frontend `build` | **PASS**: 0 warnings, initial total 310.86 kB raw / 72.91 kB transfer |
| Frontend `i18n:check` | **PASS**: 2,035 messages, 0 errors, 0 warnings |
| Dev API rebuilt with `p36-stack.sh build` (audit on) and recreated with `api` | **PASS** (`gates\dev-build-rev1.log`) |
| Provider canary before any traffic | **PASS** after the dev stack came back, and again after the API was recreated: every provider variable, every `OMNIAI_LIVE_*` and `Email__Smtp__*` absent (`gates\canary-rev1-up.txt`, `gates\canary-rev1.txt`). The 36.12 live APIs, which restart with Docker, passed too |
| Install snippet from the dev API | **PASS**: sign-in 200, widget read 200; the snippet carries `data-nabra-key` with Northwind's key and no `data-omniai-key` (`live\rev1\snippet-check.json`) |
| Chrome (MCP): host page with `data-nabra-key`, en | **PASS**: one frame for the same key, title "Chat", sandbox and referrer policy unchanged, 84×84 closed, opens to 400×634 |
| Chrome: host page with `data-omniai-key`, en | **PASS**: the same, with no console message |
| Chrome: `data-nabra-key`, Arabic host page (`lang=ar`, `dir=rtl`) | **PASS**: frame title «الدردشة», opens to 400×634, no console message |
| Chrome: `data-omniai-key`, Arabic host page | **PASS**: the same |
| Chrome: widget settings page with the new snippet | **NOT RUN** in the browser after the drop. The API read above returns the value the page shows, and `website-chat-widget.spec.ts` proves the page renders it |

Evidence: `p37-1\live\rev1\live-checks-rev1.md`, the four screenshots `{nabra,omniai}-key-{en,ar}-open.png`, `snippet-check.json`, and the `fe-rev1-*.log` files in `gates\`.

**Connection drop.** The session stopped after the backend gates had finished green. On resuming, Docker Desktop was started again and the dev stack was brought back with the `stack.md` commands. The canary ran before any traffic, then the API was rebuilt and the dev server restarted. The backend gates were not re-run.

### R1.4 See it live (replaces §6, step 3)

1. Serve the host pages with `python -m http.server 8099 --bind 127.0.0.1` in `p36-01\widget-host`.
2. Open http://127.0.0.1:8099/nabra-key.html?key=9veAaKzu0Ee-isA7jrQzYw: the new snippet's attribute. Then open http://127.0.0.1:8099/?key=9veAaKzu0Ee-isA7jrQzYw: the old one. Both load the same widget and open it.
3. Add `&lang=ar` to either URL: the frame is titled «الدردشة».
4. Channels → Website chat → an agent: the snippet now carries `data-nabra-key`.

### R1.5 Risks

- **The old attribute must stay readable for as long as old snippets exist on customer sites.** Dropping `data-omniai-key` from the loader would silently break every site installed before 37.1. Removing it needs a deprecation plan of its own.
- The other items in §8 stand, except the optional `deploy/` addition, which this revision made.

### R1.6 Proposed checkpoint commit message (replaces §9)

```
fix: clear the release blockers, and say Nabra where people read

- Pin System.Security.Cryptography.Pkcs 10.0.13 and MessagePack 2.5.303
  (GHSA-846f-f764-76mx, GHSA-2fmv-rv2c-59x7, GHSA-qhrr-8q5h-9q3h); a clean
  restore passes the NuGet audit and the deploy/ images build unchanged.
- Announce a failed AI turn once, after it is committed, on both channels;
  best effort, never replacing the failure, never on a replay.
- Nabra in email subjects, bodies and sender, PDF properties and the
  widget loader; the frame title follows the host page's language.
- The install snippet carries data-nabra-key; the loader reads it first and
  still accepts data-omniai-key, so every site already installed keeps
  working. The loader's global and bridge names are unchanged.
- providers.env.example shows the optional Email__Smtp__FromName.
- PublicNameTests keeps user-facing backend text free of the code name.
- Remove the old synthetic password from the 36.1 report and the
  performance tools; the tools now require SEED_PASSWORD.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```
