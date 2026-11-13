# Phase 38.8: Adopt the 38.7 contracts, and patch two advisories

Step report, written 2026-11-13 on top of `4b762c4` (38.7b), not committed.
Evidence: `C:\Users\mouay\Desktop\nabra-frontend-evidence\p38-8\` (`<evidence>` below).

## 1. Summary

- A visitor sees each of their messages once, and the inbox shows a customer's new message, and "The AI has not replied to the last message yet.", as soon as it is stored, before the AI answers.
- Each conversation needing a person says why: the AI could not reply, it went back to the AI unanswered, or the team owes a reply. A taken-over conversation names who handles it. The "new message" sound now also plays for conversations a member took over.
- The customers screens name people exactly as the inbox does. Indexing finishes by itself, even after a reload, and the knowledge list and editor update without one.
- The time-zone question tells every manager how many scheduled reports will move. Every refusal code 38.7b added is worded in English and Arabic, and the Word-archive refusal names its limit with its unit.
- Angular is patched (framework 22.2.5, tooling 22.2.6, `zod` 4.6.6): `npm audit --omit=dev` reports 0 vulnerabilities, down from 2 high.

## 2. Starting point

- Branch `master`, HEAD `4b762c4` ("feat: name every remaining refusal…", 38.7b) on `c757f27` (38.7a) on `413615a`.
- `git status --short`: empty (clean worktree), as the prompt says.
- The Nabra dev server was running (`cmd 34808 → npm 21736 → cmd 4884 → ng 32488 → esbuild 3924`). Only that tree was stopped before `npm install` (`taskkill /T /F /PID 34808`). The other node processes (SAMA ERP on 7000, SAMA web on 3217, the MCP servers) were left running. The dev server was restarted afterwards with `npm start` (detached), as `stack.md` says.

## 3. Changes by area

### `git status --short` at the end

```
 M docs/phase-reports/PHASE_38_SUMMARY.md
 M frontend/e2e/knowledge-index.e2e.ts
 M frontend/e2e/needs-a-person.e2e.ts
 M frontend/e2e/slow-reply.e2e.ts
 M frontend/e2e/support/fixtures.ts
 M frontend/e2e/take-over.e2e.ts
 M frontend/package-lock.json
 M frontend/package.json
 M frontend/playwright.config.ts
 M frontend/src/app/core/api/api-error.ts
 M frontend/src/app/core/api/tenant-api.ts
 M frontend/src/app/core/auth/auth-api.ts
 M frontend/src/app/core/auth/auth-service.spec.ts
 M frontend/src/app/core/auth/auth-service.ts
 M frontend/src/app/core/i18n/arabic-render.spec.ts
 M frontend/src/app/core/problems/problem-words.ts
 M frontend/src/app/core/realtime/workspace-realtime.spec.ts
 M frontend/src/app/core/realtime/workspace-realtime.ts
 M frontend/src/app/core/sounds/inbox-signal-rules.spec.ts
 M frontend/src/app/core/sounds/inbox-signal-rules.ts
 M frontend/src/app/core/sounds/inbox-signals.spec.ts
 M frontend/src/app/core/sounds/inbox-signals.ts
 M frontend/src/app/features/chat/hosted-chat/hosted-chat.spec.ts
 M frontend/src/app/features/chat/hosted-chat/hosted-chat.ts
 M frontend/src/app/features/conversations/conversation-context/conversation-context.spec.ts
 M frontend/src/app/features/conversations/conversation-detail/conversation-detail.html
 M frontend/src/app/features/conversations/conversation-detail/conversation-detail.scss
 M frontend/src/app/features/conversations/conversation-detail/conversation-detail.spec.ts
 M frontend/src/app/features/conversations/conversation-list/conversation-list.html
 M frontend/src/app/features/conversations/conversation-list/conversation-list.scss
 M frontend/src/app/features/conversations/conversation-list/conversation-list.spec.ts
 M frontend/src/app/features/conversations/conversation-list/conversation-list.ts
 M frontend/src/app/features/conversations/conversation-problems.spec.ts
 M frontend/src/app/features/conversations/conversation-problems.ts
 M frontend/src/app/features/conversations/conversation-words.ts
 M frontend/src/app/features/conversations/conversations-api.ts
 M frontend/src/app/features/conversations/customer-label.ts
 M frontend/src/app/features/customers/customer-detail/customer-detail.html
 M frontend/src/app/features/customers/customer-detail/customer-detail.spec.ts
 M frontend/src/app/features/customers/customer-detail/customer-detail.ts
 M frontend/src/app/features/customers/customer-list/customer-list.html
 M frontend/src/app/features/customers/customer-list/customer-list.spec.ts
 M frontend/src/app/features/customers/customer-list/customer-list.ts
 M frontend/src/app/features/customers/customer-words.spec.ts
 M frontend/src/app/features/customers/customer-words.ts
 M frontend/src/app/features/customers/customers-api.ts
 M frontend/src/app/features/customers/customers.routes.ts
 M frontend/src/app/features/embed/embed-chat/embed-chat.spec.ts
 M frontend/src/app/features/embed/embed-chat/embed-chat.ts
 M frontend/src/app/features/knowledge/knowledge-editor/knowledge-editor.html
 M frontend/src/app/features/knowledge/knowledge-editor/knowledge-editor.spec.ts
 M frontend/src/app/features/knowledge/knowledge-editor/knowledge-editor.ts
 M frontend/src/app/features/knowledge/knowledge-list/knowledge-list.spec.ts
 M frontend/src/app/features/knowledge/knowledge-list/knowledge-list.ts
 M frontend/src/app/features/knowledge/knowledge-problems.spec.ts
 M frontend/src/app/features/knowledge/knowledge-problems.ts
 M frontend/src/app/features/knowledge/knowledge-words.ts
 M frontend/src/app/features/notifications/notifications.routes.ts
 M frontend/src/app/features/reports/reports.routes.ts
 M frontend/src/app/features/workspace-settings/workspace-settings-page/workspace-settings-page.html
 M frontend/src/app/features/workspace-settings/workspace-settings-page/workspace-settings-page.spec.ts
 M frontend/src/app/features/workspace-settings/workspace-settings-page/workspace-settings-page.ts
 M frontend/src/locale/messages.ar.json
 M frontend/src/locale/messages.json
?? frontend/src/app/core/realtime/knowledge-realtime.ts
?? frontend/src/app/features/chat/merge-turn.spec.ts
?? frontend/src/app/features/chat/merge-turn.ts
?? frontend/src/app/features/customers/customer-problems.spec.ts
?? frontend/src/app/features/customers/customer-problems.ts
?? frontend/src/app/features/notifications/notification-problems.spec.ts
?? frontend/src/app/features/notifications/notification-problems.ts
?? frontend/src/app/features/reports/report-problems.spec.ts
?? frontend/src/app/features/reports/report-problems.ts
```

Nothing under `backend/` or `deploy/`. The `omniai_p36_live` stack was not touched.

### 3.1 The package patch (done first, gated on its own)

| Package | Before | After | Range in `package.json` |
| --- | --- | --- | --- |
| `@angular/common`, `compiler`, `core`, `forms`, `localize`, `platform-browser`, `router` | 22.1.5 | **22.2.5** | `^22.2.5` |
| `@angular/compiler-cli` | 22.1.5 | **22.2.5** | `^22.2.5` |
| `@angular/build` | 22.1.7 | **22.2.6** | `^22.2.6` |
| `@angular/cli` | 22.1.7 | **22.2.6** | `^22.2.6` |
| `zod` (transitive: `@angular/cli`, `@angular/forms`) | 4.4.3 | **4.6.6** | not a direct dependency; **no `overrides` entry** (it moved with `@angular/cli` 22.2.6, which pins 4.6.6) |

How it was installed:
- `npm install pkg@x` refused (ERESOLVE): the lock's 22.1.5 peers conflicted with the new versions.
- So exact versions went into `package.json` first, and only the ten upgraded packages' own lock entries were removed so npm could resolve them again.
- Then the ranges were raised to `^`. The second install changed nothing but the lock's root entry, which I checked.

The lock before and after: 284 package paths changed (104 versions moved, 54 added, 126 removed).
- `<evidence>\patch\lock-reach.mjs` checked each one: **all 284 are reachable from the ten Angular packages**.
- **No other direct dependency moved** (`@microsoft/signalr` 10.0.11, `bootstrap` 5.3.8, `rxjs` 7.8.2, `tslib` 2.8.1, `@playwright/test` 1.64.1, `jsdom` 28.1.0, `prettier` 3.9.6, `typescript` 6.0.3, `vitest` 4.1.11, all unchanged).
- In the production tree, besides the eight framework packages, these moved:
  - `zod` 4.4.3 → 4.6.6;
  - `@babel/*` 8.0.x → 8.0.6–8.1.2 (patch and minor, from `@angular/compiler-cli`);
  - `picomatch` 4.0.5 → 4.0.7;
  - added: `flru`, `obug`, `verkit` and a beta `@jridgewell/gen-mapping` nested under `@babel/generator`.
- The development side follows `@angular/build` 22.2.6 and `@angular/cli` 22.2.6:
  - `vite` 8.1.5 → 8.3.5, `rolldown` 1.2.0 → 1.2.16, `sass` 1.101.0 → 1.104.1 (plus `sass-embedded`), `oxc-parser` 0.142 → 0.156;
  - the CLI's MCP SDK was replaced by `@modelcontextprotocol/server` 2.3.1, which dropped the express stack.
- The full list is `<evidence>\patch\lock-diff.md`; the before and after files are `package*.before/after.json` beside it.

### 3.2 Adopted contracts

| Screen | Endpoint, event or code it now uses | Fields read | Tests |
| --- | --- | --- | --- |
| Hosted chat, embedded widget | `POST …/public/{webchat,embed}/conversations/{id}/messages` answer, merged **by id** with the transcript the `conversationChanged` re-read brought (38.7a §7.1) | `customerMessage.id`, `reply.id` | `merge-turn.spec.ts` (7); `hosted-chat.spec.ts` and `embed-chat.spec.ts`: answer before the re-read, re-read before the answer, replayed answer, one polite live region (7) |
| Inbox list, conversation (BA-13) | `conversationChanged` on the stored message; no code change, re-read per event as before | — | e2e `slow-reply` (rewritten, §3.5); live §5 |
| Inbox list, conversation heading, sounds (BA-2) | `GET …/conversations`, `GET …/conversations/{id}`, handoff writes | `takenOverBy {userId,email}` | `conversation-list.spec` (row), `conversation-detail.spec` (2), `inbox-signal-rules.spec` (+5), `inbox-signals.spec` (+2), e2e `take-over` |
| Needs-a-person view (BA-7) | the same reads | `needsPerson`, `needsPersonReason` | `conversation-list.spec` (reasons incl. unknown and null), `arabic-render.spec`, e2e `needs-a-person` |
| Customers list and page, merge (BA-1) | `GET …/customers`, `GET …/customers/{id}` | `labelCode`, `shortCode` (through the inbox's `customerLabel`) | `customer-words.spec` (4, replacing `customerName`'s), `customer-list.spec` (+1), `customer-detail.spec` (merge) |
| Knowledge list and editor (BA-11, BA-10) | hub `SubscribeKnowledge`/`UnsubscribeKnowledge`, event `knowledgeChanged(documentId)`; attempt code `timed_out` | `lastIndexAttempt` | `workspace-realtime.spec` (+5), `knowledge-list.spec` (+6), `knowledge-editor.spec` (+8), e2e `knowledge-index` (rewritten) |
| Knowledge upload (BA-12) | `knowledge.file.archive_too_large` | `limit`, `limitUnit` | `knowledge-problems.spec` (+3), `arabic-render.spec` |
| Workspace settings (BA-9) | `GET /api/tenants/{id}`, read again just before asking; the schedule list is no longer read | `scheduleCount` | `workspace-settings-page.spec` (rewritten zone cases, +2), `arabic-render.spec` (plural forms) |
| Inbox, customers, notifications, reports (BA-4) | 46 new codes | `code` | one spec per catalogue (`conversation-`, `customer-`, `notification-`, `report-problems.spec`), every code, plus Arabic samples and a "never the English `detail`" sweep in `arabic-render.spec` |
| Sign-in check (BA-5) | `GET /api/auth/session` | `user` now optional (absent when signed out) | `auth-service.spec` (+1, the live shape) |
| Billing (BA-4.3) | `GET …/billing` | `fallbackPlan` | live only (no code change) |
| Widget (BA-3) | `v1.js` `?lang=` | — | live only (no code change) |

#### What changed, per item

**Item 2, the visitor's transcript.**
- `features/chat/merge-turn.ts` adds a turn's messages that are not already in the list, in their transcript order. When nothing is new it returns the same array, so nothing re-renders.
- `hosted-chat.ts` and `embed-chat.ts` use it in place of the two appends.
- No optimistic bubble existed before, and none was added: while sending, the text stays in the composer with "Sending…" and the typing line, as before.

**Item 3, the inbox.**
- **The sound rule.** In `core/sounds/inbox-signal-rules.ts`, a conversation is "mine" when `assignedTo` or `takenOverBy` is the member. The "new message" sound also needs the row's `messageCount` to have grown since the last read, with the customer writing last.
  - Before this step, any move of `lastActivityAt` with the customer last counted, and an assignment, a takeover or a return moves it too.
  - With BA-13's two announcements per turn, that guarantees one sound per message.
  - The reader's memory per row (and the cross-tab snapshot) is now `{at, count}`.
- **"Handled by".** The heading says "Handled by <email>" (`conversation.handledBy`, email isolated left to right). Rows say it too, on one line that ends in an ellipsis if it must (`.row-handled`).
- **Reasons.** In the needs-a-person view each row shows its reason in the warning badge: the badge's glyph and the words (`needsPersonWord`).
  - `PersonHandling` is exactly the case rows could already infer, so it keeps "Waiting for the team".
  - An unknown reason, or none, keeps that wording too.
  - The "all" view keeps today's badge.

**Item 4, customers.**
- `customerName()` and its three messages are gone.
- The list, the page, the merge candidates and the merge confirmation all use `asLabelled()` with the inbox's `customerLabel`, `customerLabelPlain` and `CustomerLabel`.
- `labelCode` and `shortCode` are added to `CustomerRow` and `Customer`.

**Item 5, knowledge.**
- `WorkspaceRealtime` holds the knowledge group on the page's one connection while any knowledge screen listens and the member has `knowledge.view`. It leaves the group with the last listener, and after a reconnect it joins again and tells listeners to re-read.
- A refusal on a live connection is not asked again.
- `KnowledgeRealtime` filters by workspace, as `ConversationRealtime` does.
- The list re-reads its page quietly: one read at a time, with exactly one more if events arrived meanwhile, and the rows are kept on failure.
- The editor re-reads its document, except while its own request is out.
  - With unsaved text it takes only the state, and keeps the text and the version it was read at.
  - So a save still meets the stale-version check.
- The "Indexing now" sentence no longer asks for a reload (`knowledge.state.indexing.live`). The in-progress refusal says the result appears on the page. `timed_out` is worded.

**Item 6, settings, codes and types.**
- **The zone question.** The settings page reads `scheduleCount` from a fresh workspace read just before asking. It does not adopt that read's `ETag`.
  - Zero schedules: no question.
  - A count: a counted sentence with all six Arabic forms.
  - No count, or the read failed: today's sentence. `ReportsApi` is gone from the page.
- **The new codes** are in three new catalogues, registered by their routes (customers, notifications, reports) so the startup JavaScript does not carry them, and in the conversation catalogue.
- **`limitUnit`** joins `ApiError` and `ProblemFacts`; only the six documented units are kept.

### 3.3 The types against the OpenAPI document (BA-4.1, 4.2, 5)

- **How:** `<evidence>\scratch\types-vs-openapi.mjs` compared 81 client interfaces with their schemas in the live document (`<evidence>\openapi\v1.json`).
- **The result** before (`types-before.txt`, run against HEAD's sources) and after (`types-after.txt`):

| Difference | Fix |
| --- | --- |
| `ConversationSummary` and `ConversationDetail` did not read `takenOverBy`, `needsPerson`, `needsPersonReason`; the detail did not read `unread` | Added (`HandlingFacts`, and `unread` on the detail) |
| `CustomerRow` and `Customer` did not read `labelCode`, `shortCode` | Added |
| `TenantDetails` did not read `scheduleCount` | Added, optional |
| `SessionAnswer.user` typed as always present; the document marks it optional, and live it is **absent** when signed out (`{"authenticated":false}`) | `user?:`, read as `answer.user ?? null` (no behaviour change: it was only read when authenticated) |
| `EmailPreferences` booleans typed non-null; the document marks them nullable | **Not changed**: the schema is shared with the request body, where `null` is refused (`notifications.preferences.incomplete`); responses carry booleans. Listed in §7 |
| `ProblemDetails.limitUnit` | Added to `ApiError` (`LimitUnit`, the six values of the schema's enum) |

- **Already in line:**
  - the invitation create answer (`201 CreatedInvitationResponse`);
  - the role writes (`RoleResponse` without `memberCount`, already optional on `Role`);
  - `GET /api/me/tenants` (`WorkspaceResponse` equals `Tenant`);
  - the invitation `status` values;
  - the dashboard's optional `previous`.
- No client was generated (rule 3).

### 3.4 The Phase 38 summary

`PHASE_38_SUMMARY.md` §5 has a "Status (38.8)" column: every ask closed with the step(s) that closed it, BA-6 and BA-8 open with their "when".

### 3.5 End-to-end

- **`slow-reply`, rewritten:**
  - The inbox opens before the visitor writes. The new row appears live with the message, from the stored announcement alone.
  - Opened, it says the AI has not replied. All of that is checked before the reply (`< 10 s`, nothing answered on either side).
  - Then the reply ends both waits. The visitor sees their message once. No API polling.
- **Why it waited 1.9 hours in 38.7b.**
  - In the cut-off run (`nabra-backend-evidence\p38-7b\rev1\e2e-run`), the ar-rtl journey failed after 5 s at line 51, because the typing line never showed. That run started soon after an interrupted one, so the likeliest first cause is the public chat's start limit.
  - The `finally { await close() }` then waited on `context.close()`, which has no timeout of its own, and Playwright's teardown after the 60 s test timeout waited too.
  - Nothing above the journey had a limit (no `globalTimeout`, no `actionTimeout`, no `navigationTimeout`), so the worker sat until the session ended and Windows killed it (`0xC0000142`).
- **Every wait is now bounded:**
  - the journey's waits have explicit limits (`STEP_MS` = 15 s and the reply's 25 s);
  - `openVisitor` bounds `newContext` and `newPage` (`within`), and its `close` gives up after 15 s (`closeWithin`);
  - a refused start fails at once, by name;
  - the config adds `actionTimeout: 15 s`, `navigationTimeout: 20 s` and `globalTimeout: 15 min` as a backstop for every journey.
- **`knowledge-index`, rewritten.**
  - A second tab holds the knowledge list. The journey waits, from the tab's own hub frames, until `SubscribeKnowledge` completed.
  - The editor then indexes and reloads as soon as the request is out (BA-10).
  - The list shows "Indexed" without a reload (BA-11), and the reloaded editor says how it ended.
- **Extended:**
  - `take-over`: the heading names the member (the address isolated), and nobody once the conversation is returned.
  - `needs-a-person`: the row says "The AI could not reply" / «تعذّر على الذكاء الاصطناعي الرد».
- **Every journey keeps its two languages:** 23 × 2.
- **The widget language** has no journey: the suite has no host-page support, so it was checked in Chrome (§5).

### 3.6 Screenshots (`<evidence>\chrome\shots\`)

| What | Before | After |
| --- | --- | --- |
| Inbox, needs-a-person rows | `nabra-frontend-evidence\p38-6\chrome\shots\needs-person-*-{390,1920}.png` | `matrix\needs-person-{en,ar}-{light,dark}-{390,1920}.png`, `fail-needs-person-{en,ar}.png` |
| Inbox, open conversation | `p38-6\chrome\shots\conversation-*.png` | `slow-hosted-en-inbox-waiting.png`, `slow-embed-ar-inbox-waiting.png`, `takeover-owner-sees-support-en.png`, `takeover-support-heading-ar.png`, `takeover-support-list-ar.png` |
| Visitor chat, hosted and embedded | `p38-6\chrome\shots\hosted-chat-*.png` | `slow-hosted-en-visitor-{waiting,replied}.png`, `slow-embed-ar-visitor-{waiting,replied}.png` |
| Customers | none (the route was not in 38.6's matrix) | `customers-list-{en,ar}-390.png`, `customer-{named,anonymous}-{en,ar}-390.png`, `probe--customers-{en,ar}-390.png`, `matrix\customers*` |
| Knowledge | `p38-6\chrome\shots\knowledge*.png` | `index-list-updated-live.png`, `index-editor-after-reload.png`, `archive-{parts,expands}-{en,ar}.png` |
| Workspace settings | `p38-6\chrome\shots\workspace-settings-*.png` | `zone-question-{en,ar}.png` |
| Billing | `p38-6\chrome\shots\billing-*.png` | `billing-{en,ar}.png` |
| Widget on host pages | — | `widget-host-{ar,en,none}-browser-{en-US,ar-SA}.png` |

## 4. Decisions

1. **The framework version: 22.2.5.**
   - 22.2.5 was published on 6 November 2026 at 19:39–19:51 UTC. At the start (13 November, 16:25 UTC) it was seven calendar days old, but 6 days 20½ hours by the clock.
   - The prompt names 22.2.5 as the framework target, and 22.2.6 (12 November) was excluded, so I used 22.2.5. The strict seven-day choice would have been 22.2.4, which also fixes the advisories.
   - The tooling's 22.2.6 (4 November) is well past seven days; 22.2.7 (12 November) is not.
2. **No `overrides` for `zod`:** it resolved to 4.6.6 with the CLI.
3. **The "new message" sound counts messages, not announcements.** It needs `messageCount` to grow. Besides BA-13's double announcement, this also stops the sound an assignment, a takeover or a return used to cause with the customer last. It is a small, deliberate change of behaviour.
4. **The needs-a-person reason** uses the warning badge's own glyph and the words. A second icon per reason would have doubled the glyph and needed a size literal (rule 8). `PersonHandling` keeps "Waiting for the team", the case's existing words.
5. **"Handled by" in Arabic.** The heading is a full sentence, «تمّ استلام المحادثة من قِبل …», and the row is compact, «استلام المحادثة: …» (the transcript event's form). Both are gender-neutral and built on the glossary's «استلام المحادثة». Marked for your review (§8).
6. **The directory has no masked number,** so an unnamed WhatsApp customer shows its short code on the customers screens, where the inbox shows `••••1234`. It is the honest version; the ask is in §7.
7. **The zone question re-reads the workspace for the count, but keeps its version.** The 38.7b note asks for a fresh read, because a schedule change does not move the `ETag`. Adopting that read's version would let a zone save skip a change somebody else made meanwhile.
   - When the read carries no count or fails, it asks without a number.
   - The prompt said "no longer reads the schedule list", and 38.7b §7.3 said "when absent, keep today's behaviour (ask)". Both hold: it asks, and reads no list.
8. **The live knowledge read keeps unsaved text.** With edits in the box, only the state is taken. The next save then meets the stale-version check (412), as before 38.8, rather than silently writing over a version the reader never saw.
9. **The knowledge list re-reads its page** on an event; it does not patch one row. That is one request per event, the same rule as the inbox (38.0).
10. **The download refusal codes are worded but rarely seen.** Downloads are plain links, so the browser shows a 409 or 410 itself. Words exist for any screen that reads them; fetching downloads is a Phase 39 note (§9).
11. **`globalTimeout`, `actionTimeout` and `navigationTimeout`** were added to `playwright.config.ts` for the whole suite, not only `slow-reply`, because the cause (no limit above a journey) is the suite's.
12. **Live checks used Playwright** driving the installed Chrome with temporary profiles, not the Chrome DevTools MCP. Signing in through the MCP would put the demo password into a tool argument (rule 10), as in 38.6.
13. **Differences from the 38.7 reports (rule 20):**
    - 38.7b §7.2's example said "more than 512 parts"; the screens say "files" (the prompt's word, and what a reader recognises).
    - The bytes are said as the upload limit is: "64 MB", «64 م.ب.».

## 5. Verification

| Check | Result |
| --- | --- |
| `npm audit --omit=dev`, before | **2 high**: `@angular/router` 22.0.0–22.2.0 (GHSA-ff3f-86qr-9cv3, GHSA-62vg-58rm-qff7, GHSA-57xq-rjx2-v5xh), `zod` < 4.6.6 (GHSA-mrhx-c7xc-m3w5) (`<evidence>\patch\audit-omit-dev-before.txt`) |
| `npm audit --omit=dev`, after | **PASS: 0 vulnerabilities** (exit 0; `audit-omit-dev-after.txt`) |
| `npm audit` with development dependencies (information) | Before: 18 (3 critical, 8 high, 7 moderate). After: **4**, all development-only and not moved by this patch: `undici` (high), `vitest` and `@vitest/mocker` (moderate), `fast-uri` (moderate) |
| Packages whose resolved version changed | The ten approved, plus 274 transitive paths, all reachable from them (`lock-reach.txt`: 0 outside); no other direct dependency (§3.1) |
| Gates on the patch alone (before any other change) | `format:check` PASS; `test:ci` **120 files / 1,766 tests** PASS (free 9.42 GB); `build` PASS, 0 warnings, startup JS **141,159** gzip; `i18n:check` 2,392 messages, 0 errors, 0 warnings |
| `npm run format:check` (final) | **PASS** |
| `npm run test:ci` (final) | **PASS: 124 files, 1,874 tests, 0 failures** (free 7.21 GB). One earlier full run was started at **5.67 GB, under the 6 GB floor**, by mistake; it passed (124/1,874) and is **not counted**. It was repeated at 6.33 GB (pass), and the final run is the one above (`<evidence>\gates\memory.txt`) |
| `npm run build` (final) | **PASS: 0 warnings**, budgets unchanged and met (initial 313.77 kB raw / 73.71 kB transfer). Startup JavaScript (loader + application chunk, gzip -9): **141,423** (38.6: 145,199; 38.0 baseline 138,907, **+1.8 %**, under the +10 % ceiling of 152,797). The new catalogues load with their routes |
| `npm run i18n:check` (final) | **PASS: 2,448 messages, 0 errors, 0 warnings** |
| `npm run e2e`, run 1 (17:09–17:11 UTC) | **PASS: 46 passed** (23 × en-ltr, ar-rtl), 0 failed, 0 flaky, **2.4 min**, full run (free 7.08 GB) |
| `npm run e2e`, run 2 (17:26:45 UTC, 15 min after run 1) | **PASS: 46 passed**, 0 failed, 0 flaky, **2.3 min**, full run incl. the throttling journey (free 6.98 GB). Included the last code change; no retries |
| Backend gates | **NOT RUN**: no file under `backend/` changed (this step is frontend only) |
| Chrome matrix | **PASS: 14 touched routes × 2 languages × 2 themes × 4 widths (390, 768, 1280, 1920) = 224 loads, 0 problems**: no page or main overflow, no console error or warning, one `h1` each, no unnamed control, no axe WCAG 2.0/2.1/2.2 A and AA violation (contrast included, both themes), right `dir` (`<evidence>\chrome\matrix-{en,ar}.json`). Routes: conversations, needs-a-person, a conversation, customers, a customer, knowledge, a knowledge document, workspace settings, notification preferences, notifications, reports, schedules, billing, hosted chat |
| Keyboard and focus | **PASS** (en, ar): Arrow Down moves between rows; Enter opens the conversation and focuses its heading; the zone question takes focus and Escape returns it to "Save time zone" without saving. The e2e `take-over` keeps focus through takeover and return |
| Contrast, reduced motion, RTL | Contrast: in the axe runs above. Reduced motion: **no motion was added** (nothing to check beyond 38.6). RTL: Arabic pages `dir="rtl"`; codes, emails and short codes in `<bdi dir="ltr">`; the Arabic row "Handled by" fixed to one line after the first look (§9) |
| `[[slow]]`, hosted, en (inbox open) | **PASS**: row live at **0.27 s**, "not replied yet" at **0.49 s**, reply at **11.7 s**, inbox line gone at 11.7 s. Visitor page: **872 painted frames, 0 with a duplicate**, own rows max 1. Inbox reads: 5 list pages (first, one per announcement, and those caused by opening and marking read), 3 needs-a-person count reads, 2 detail, 2 transcript. No console error |
| `[[slow]]`, embedded, ar (Arabic host page) | **PASS**: row at **0.47 s**, line at **0.90 s**, Arabic reply at **11.6 s**; **874 frames, 0 duplicates** |
| `[[fail]]` | **PASS**: the row says "The AI could not reply" / «تعذّر على الذكاء الاصطناعي الرد» in the warning badge (glyph and words) |
| Takeover by the support agent, seen by the owner | **PASS**: the owner's open conversation names the support agent live ("Handled by <support>"); the support agent, on the list, heard `needs-person` for the next message and **`new-message`** for the one after (a conversation they took over, not assigned to them). The conversation was then returned to the AI through the screen |
| Indexing, reload at once | **PASS**: the open list went "Needs index" → "Indexing now" → "Indexed" with **no reload** (3 list reads: first, started, settled); the reloaded editor read "Indexed", attempt `Succeeded` |
| Time-zone question, manager without `reports.view` | **PASS**: the support agent, given the "Zone manager (38.7b check)" role (`workspace.manage`, no `reports.view`), was asked "1 scheduled report moves…" / «ينتقل تقرير مجدول واحد…»; **the schedule list was never read**; Escape cancelled; nothing saved. Their roles were restored to "Support Agent" (200) |
| Customers | **PASS** (en, ar, 390 px): unnamed "Visitor KVZMQE" / «زائر KVZMQE» (the inbox's words and code), the code isolated left to right; named "Nguyễn Thị Hoa" as typed; no overflow (`scrollWidth` 390). **A WhatsApp customer: NOT RUN**, the dev data has none (needs Meta); proved by specs |
| Billing (BA-4.3) | **PASS**: the fallback column "Free" with finite limits (2 members, 1 agent, 10 documents …), en and ar. A finite *current* plan is not reachable on the dev stack (38.7b §8.3) |
| Widget language (BA-3) | **PASS**, 6 cases: an English browser on an Arabic host opens `lang=ar`, `dir=rtl`, «اكتب رسالتك…»; an Arabic browser on an English host opens English; no `lang` follows the browser (English → English, Arabic → Arabic) |
| Archive limit (BA-12), in the upload page | **PASS**: 600 parts → `entries` 512 → "Once opened, this Word document holds more than 512 files…" / «…على أكثر من 512 ملفًا…»; 70 MB → `bytes` 67108864 → "larger than 64 MB" / «64 م.ب.». Nothing was created |
| Mailpit | NOT RUN: this step changes nothing that sends email |
| Static security scan (`security_scan.py --min-severity medium --skip-tests`) | 0 leads over 302 files (a heuristic, not a proof). No `innerHTML` or `bypassSecurityTrust*` added; the XSRF header is unchanged; no token or link logged |
| Secret scan (`<evidence>\scratch\secret-scan.mjs`) | **PASS**: 738 files (evidence, `frontend/src`, `frontend/e2e`, the summary, the config), 0 with the demo password, 0 with an invitation link. `<evidence>\tmp\` (cookie jars) **deleted** |
| Provider canary | **PASS** at the start, before each e2e run, and at the end (`<evidence>\live\canary-*.txt`): every provider variable absent, SMTP host loopback, `OMNIAI_DEV_AI_STANDIN` present (info) |

## 6. See it live

The dev API (`127.0.0.1:5027`, AI stand-in on) and the dev server (`http://localhost:4200`) are running. Mailpit is at `http://127.0.0.1:8026`. Sign in with the accounts in `omniai-redesign-evidence\p36-01\demo-accounts.md` (passwords there, not here).

1. **The live inbox (BA-13) and one message per frame.**
   - As the **Owner**, open `/conversations`. In another browser, open `http://localhost:4200/chat/9veAaKzu0Ee-isA7jrQzYw` and write something ending in `[[slow]]`.
   - Within a second the row appears with your message. Open it: "The AI has not replied to the last message yet." About eleven seconds later the reply arrives on both sides.
   - On the visitor's page your message is never shown twice.
2. **Why it needs a person (BA-7).** As the visitor, write a message containing `[[fail]]`, then open **Needs a person**: the row says "The AI could not reply". Rows taken over say "Waiting for the team"; one returned to the AI unanswered says so.
3. **Who handles it (BA-2).**
   - As the **Support agent**, open a conversation and press **Take over**: the heading says "Handled by …", and the Owner's open copy says the same within a second.
   - Go back to the list, switch sounds on (Notifications → preferences), and have the visitor write twice: the second message plays the "new message" sound.
   - Return the conversation to the AI afterwards.
4. **Customers (BA-1).** `/customers` and any unnamed customer: "Visitor" and its short code, the same words and code as the inbox (for example "Visitor KVZMQE", in Arabic «زائر KVZMQE»).
5. **Indexing ends by itself (BA-10, BA-11).** Open `/knowledge` in one tab and a document that needs an index in another. Press **Index** and reload that tab at once: the list in the first tab changes to "Indexed" by itself.
6. **The archive limit (BA-12).** Upload a `.docx` with more than 512 parts (`<evidence>\scratch\live-checks.mjs archive` builds one) on `/knowledge/upload`: "…holds more than 512 files…".
7. **The time-zone question (BA-9).** As the **Owner**, on `/workspace-settings`, choose another zone and press **Save time zone**: "1 scheduled report moves…". Cancel unless you mean it. A manager without `reports.view` is asked the same way.
8. **Billing (BA-4.3).** `/billing`: the **Free** fallback column with finite limits.
9. **The widget's language (BA-3).** Serve `nabra-backend-evidence\p38-7a\host-pages\` on `127.0.0.1:8099` (the allowlisted origin) and open `ar.html`, `en.html` and `none.html`: Arabic, English, and your browser's language.

## 7. Backend asks

1. **The masked number on the customer directory.** `GET …/customers` and `…/customers/{id}` carry no `reference` (the masked WhatsApp number the conversation reads give). So an unnamed WhatsApp customer reads "WhatsApp customer" with its short code on the customers screens, and with its masked number (`••••1234`) in the inbox. Shipped honestly with the short code.
2. **`EmailPreferencesContract` is nullable in the document.** The one schema serves the request (where `null` is refused) and the response (always `true`/`false`). Splitting it, or marking the response non-null, would let a generated client type it exactly. Documentation only.

No contract was missing, wrong or undocumented for what this step built.

## 8. New Arabic for my review

61 new or changed strings (60 new, 1 changed: `problem.knowledge.indexing.in_progress`, which no longer asks for a reload). Four were removed: `customers.unnamed.several`, `.whatsApp`, `.visitor` (the client's own label rule) and `knowledge.state.indexing.sentence`.

| Key | English | Arabic | Where |
| --- | --- | --- | --- |
| `conversation.handledBy` | Handled by {$START_TAG_BDI}{$INTERPOLATION}{$CLOSE_TAG_BDI} | تمّ استلام المحادثة من قِبل {$START_TAG_BDI}{$INTERPOLATION}{$CLOSE_TAG_BDI} | app/features/conversations/conversation-detail/conversation-detail.html |
| `inbox.handledBy` | Handled by {$START_TAG_BDI}{$INTERPOLATION}{$CLOSE_TAG_BDI} | استلام المحادثة: {$START_TAG_BDI}{$INTERPOLATION}{$CLOSE_TAG_BDI} | app/features/conversations/conversation-list/conversation-list.html |
| `inbox.reason.aiFailed` | The AI could not reply | تعذّر على الذكاء الاصطناعي الرد | app/features/conversations/conversation-words.ts |
| `inbox.reason.returned` | Returned to the AI unanswered | تمّت إعادتها إلى الذكاء الاصطناعي دون رد | app/features/conversations/conversation-words.ts |
| `problem.conversations.reply.notSent` | This reply could not be sent. Reload the page and try again. What you wrote is still in the box. | تعذّر إرسال هذا الرد. أعد تحميل الصفحة وحاول مرة أخرى. ما كتبته باقٍ في المربع. | app/features/conversations/conversation-problems.ts |
| `problem.conversations.id.invalid` | This conversation could not be found. Open it again from the inbox. | تعذّر العثور على هذه المحادثة. افتحها مرة أخرى من صندوق المحادثات. | app/features/conversations/conversation-problems.ts |
| `problem.conversations.reply.not_taken_over` | The AI is answering this conversation. Choose Take over first, then reply. | يردّ الذكاء الاصطناعي على هذه المحادثة. اختر «استلام المحادثة» أولًا، ثم ردّ. | app/features/conversations/conversation-problems.ts |
| `problem.conversations.message.required` | Write a reply before sending it. | اكتب ردًّا قبل إرساله. | app/features/conversations/conversation-problems.ts |
| `problem.conversations.message.too_long` | This reply is too long. Shorten it and send it again. | هذا الرد أطول من المسموح به. اختصره وأرسله مرة أخرى. | app/features/conversations/conversation-problems.ts |
| `problem.conversations.message.control_characters` | This reply has characters that cannot be sent. Paste it as plain text and try again. | يحتوي هذا الرد على أحرف لا يمكن إرسالها. الصقه كنص عادي وحاول مرة أخرى. | app/features/conversations/conversation-problems.ts |
| `problem.conversations.assignee.not_member` | That person is not a member of this workspace. Choose somebody else. | هذا الشخص ليس عضوًا في مساحة العمل هذه. اختر شخصًا آخر. | app/features/conversations/conversation-problems.ts |
| `problem.conversations.assignee.cannot_reply` | That member cannot reply to customers, so the customer would keep waiting. Choose somebody who can reply. | لا يستطيع هذا العضو الرد على العملاء، فسيبقى العميل منتظرًا. اختر عضوًا يستطيع الرد. | app/features/conversations/conversation-problems.ts |
| `problem.conversations.closed` | This conversation is closed, so it cannot be changed. | هذه المحادثة مغلقة، فلا يمكن تغييرها. | app/features/conversations/conversation-problems.ts |
| `problem.customers.page.invalid` | This page of customers could not be shown. Go back to the first page. | تعذّر عرض هذه الصفحة من العملاء. عد إلى الصفحة الأولى. | app/features/customers/customer-problems.ts |
| `problem.customers.id.invalid` | This customer could not be found. Open them again from the customer list. | تعذّر العثور على هذا العميل. افتحه مرة أخرى من قائمة العملاء. | app/features/customers/customer-problems.ts |
| `problem.customers.display_name.too_long` | This name is too long. Shorten it and save again. | هذا الاسم أطول من المسموح به. اختصره واحفظه مرة أخرى. | app/features/customers/customer-problems.ts |
| `problem.customers.display_name.control_characters` | A name has to be one line of plain text. | يجب أن يكون الاسم سطرًا واحدًا من نص عادي. | app/features/customers/customer-problems.ts |
| `problem.customers.merge.target_required` | Choose the customer to combine with first. | اختر أولًا العميل الذي تريد الدمج معه. | app/features/customers/customer-problems.ts |
| `problem.customers.merge.self` | A customer cannot be combined with itself. Choose another one. | لا يمكن دمج عميل مع نفسه. اختر عميلًا آخر. | app/features/customers/customer-problems.ts |
| `problem.customers.merge.not_in_workspace` | One of these customers is no longer in this workspace. Reload the page and choose again. | أحد هذين العميلين لم يعد في مساحة العمل هذه. أعد تحميل الصفحة واختر من جديد. | app/features/customers/customer-problems.ts |
| `problem.customers.merged` | This customer has already been combined with another one. Reload the page to see where their history went. | دُمج هذا العميل بالفعل مع عميل آخر. أعد تحميل الصفحة لترى أين انتقل سجله. | app/features/customers/customer-problems.ts |
| `knowledge.state.indexing.live` | Indexing is running. It carries on if you leave this page, and the result appears here when it ends. | تجري الفهرسة الآن. تستمر حتى لو غادرت هذه الصفحة، وتظهر النتيجة هنا عند انتهائها. | app/features/knowledge/knowledge-editor/knowledge-editor.html |
| `problem.knowledge.file.archive_entries.zero` | Once opened, this Word document holds more than {$limit} files. Split it, or save a simpler copy. | يحتوي مستند Word هذا عند فتحه على أكثر من {$limit} ملف. قسّمه، أو احفظ نسخة أبسط منه. | app/features/knowledge/knowledge-problems.ts |
| `problem.knowledge.file.archive_entries.one` | Once opened, this Word document holds more than {$limit} file. Split it, or save a simpler copy. | يحتوي مستند Word هذا عند فتحه على أكثر من ملف واحد. قسّمه، أو احفظ نسخة أبسط منه. | app/features/knowledge/knowledge-problems.ts |
| `problem.knowledge.file.archive_entries.two` | Once opened, this Word document holds more than {$limit} files. Split it, or save a simpler copy. | يحتوي مستند Word هذا عند فتحه على أكثر من ملفين. قسّمه، أو احفظ نسخة أبسط منه. | app/features/knowledge/knowledge-problems.ts |
| `problem.knowledge.file.archive_entries.few` | Once opened, this Word document holds more than {$limit} files. Split it, or save a simpler copy. | يحتوي مستند Word هذا عند فتحه على أكثر من {$limit} ملفات. قسّمه، أو احفظ نسخة أبسط منه. | app/features/knowledge/knowledge-problems.ts |
| `problem.knowledge.file.archive_entries.many` | Once opened, this Word document holds more than {$limit} files. Split it, or save a simpler copy. | يحتوي مستند Word هذا عند فتحه على أكثر من {$limit} ملفًا. قسّمه، أو احفظ نسخة أبسط منه. | app/features/knowledge/knowledge-problems.ts |
| `problem.knowledge.file.archive_entries.other` | Once opened, this Word document holds more than {$limit} files. Split it, or save a simpler copy. | يحتوي مستند Word هذا عند فتحه على أكثر من {$limit} ملف. قسّمه، أو احفظ نسخة أبسط منه. | app/features/knowledge/knowledge-problems.ts |
| `problem.knowledge.file.archive_bytes` | Once opened, this Word document is larger than {$size}. Split it, or save a simpler copy. | يتجاوز حجم مستند Word هذا عند فتحه {$size}. قسّمه، أو احفظ نسخة أبسط منه. | app/features/knowledge/knowledge-problems.ts |
| `knowledge.failure.timed_out` | Indexing took longer than allowed. Nothing was indexed. | استغرقت الفهرسة وقتًا أطول من المسموح به، فلم يُفهرَس شيء. | app/features/knowledge/knowledge-words.ts |
| `problem.knowledge.indexing.in_progress` | This document is being indexed right now, so nothing was sent. The result appears on this page when it ends. | تجري فهرسة هذا المستند الآن، فلم يُرسَل شيء. تظهر النتيجة في هذه الصفحة عند انتهائها. | app/features/knowledge/knowledge-problems.ts |
| `problem.notifications.page.invalid` | This page of notifications could not be shown. Go back to the first page. | تعذّر عرض هذه الصفحة من الإشعارات. عد إلى الصفحة الأولى. | app/features/notifications/notification-problems.ts |
| `problem.notifications.preferences.incomplete` | These preferences could not be saved. Reload the page and try again. Your choices are still selected. | تعذّر حفظ هذه التفضيلات. أعد تحميل الصفحة وحاول مرة أخرى. ما زالت اختياراتك محددة. | app/features/notifications/notification-problems.ts |
| `problem.reports.notRequested` | This report could not be requested. Reload the page and try again. | تعذّر طلب هذا التقرير. أعد تحميل الصفحة وحاول مرة أخرى. | app/features/reports/report-problems.ts |
| `problem.reports.notFound` | This report could not be found. Reload the page. | تعذّر العثور على هذا التقرير. أعد تحميل الصفحة. | app/features/reports/report-problems.ts |
| `problem.reports.file.gone` | This report's file is no longer kept. Generate the report again. | لم يعد ملف هذا التقرير محفوظًا. أنشئ التقرير مرة أخرى. | app/features/reports/report-problems.ts |
| `problem.reports.type.invalid` | This report is not one Nabra offers. Reload the page and choose again. | هذا التقرير ليس من التقارير التي تقدّمها نبرة. أعد تحميل الصفحة واختر من جديد. | app/features/reports/report-problems.ts |
| `problem.reports.format.invalid` | Choose CSV, Excel or PDF. | اختر CSV أو Excel أو PDF. | app/features/reports/report-problems.ts |
| `problem.reports.format.unsupported` | This report is not offered in that format. Choose another format. | هذا التقرير غير متاح بهذه الصيغة. اختر صيغة أخرى. | app/features/reports/report-problems.ts |
| `problem.reports.frequency.invalid` | Choose weekly or monthly. | اختر أسبوعيًا أو شهريًا. | app/features/reports/report-problems.ts |
| `problem.reports.range.invalid` | Choose a period: the last 7, 30 or 90 days, or two dates. | اختر فترة: آخر 7 أو 30 أو 90 يومًا، أو تاريخين. | app/features/reports/report-problems.ts |
| `problem.reports.range.dates_required` | Choose both a start date and an end date. | اختر تاريخ البداية وتاريخ النهاية كليهما. | app/features/reports/report-problems.ts |
| `problem.reports.period.future` | The period cannot end after today. Choose an earlier end date. | لا يمكن أن تنتهي الفترة بعد اليوم. اختر تاريخ نهاية أبكر. | app/features/reports/report-problems.ts |
| `problem.reports.period.reversed` | The end date is before the start date. Swap them and try again. | تاريخ النهاية قبل تاريخ البداية. بدّل بينهما وحاول مرة أخرى. | app/features/reports/report-problems.ts |
| `problem.reports.period.too_long` | This period is longer than a report can cover. Choose a shorter one. | هذه الفترة أطول مما يغطيه التقرير. اختر فترة أقصر. | app/features/reports/report-problems.ts |
| `problem.reports.period.too_early` | This period starts before the earliest date reports cover. Choose a later start date. | تبدأ هذه الفترة قبل أقدم تاريخ تغطيه التقارير. اختر تاريخ بداية لاحقًا. | app/features/reports/report-problems.ts |
| `problem.reports.page.invalid` | This page of reports could not be shown. Go back to the first page. | تعذّر عرض هذه الصفحة من التقارير. عد إلى الصفحة الأولى. | app/features/reports/report-problems.ts |
| `problem.reports.file.not_ready` | This report is still being prepared. Try the download again in a moment. | ما زال هذا التقرير قيد الإعداد. حاول التنزيل مرة أخرى بعد قليل. | app/features/reports/report-problems.ts |
| `problem.reports.file.failed` | This report could not be prepared, so there is no file. Generate it again. | تعذّر إعداد هذا التقرير، فلا يوجد ملف. أنشئه مرة أخرى. | app/features/reports/report-problems.ts |
| `problem.reports.schedule.id.invalid` | This schedule could not be found. Reload the page. | تعذّر العثور على هذه الجدولة. أعد تحميل الصفحة. | app/features/reports/report-problems.ts |
| `problem.reports.schedule.name.required` | Give this schedule a name. | أعطِ هذه الجدولة اسمًا. | app/features/reports/report-problems.ts |
| `problem.reports.schedule.name.too_long` | This name is too long. Shorten it and save again. | هذا الاسم أطول من المسموح به. اختصره واحفظه مرة أخرى. | app/features/reports/report-problems.ts |
| `problem.reports.schedule.name.control_characters` | A name has to be one line of plain text. | يجب أن يكون الاسم سطرًا واحدًا من نص عادي. | app/features/reports/report-problems.ts |
| `problem.reports.schedule.limit_reached` | This workspace has as many schedules as it may have. Remove one before adding another. | بلغت مساحة العمل هذه أقصى عدد مسموح به من الجدولات. احذف واحدة قبل إضافة أخرى. | app/features/reports/report-problems.ts |
| `problem.reports.schedule.enabled_required` | This schedule could not be switched on or off. Reload the page and try again. | تعذّر تشغيل هذه الجدولة أو إيقافها. أعد تحميل الصفحة وحاول مرة أخرى. | app/features/reports/report-problems.ts |
| `workspaceSettings.zone.confirm.count.zero` | No scheduled report moves. Days in analytics and new reports are counted in the new zone from now on. Reports already generated keep their zone. | لا ينتقل أي تقرير مجدول. وتُحسب من الآن الأيام في التحليلات والتقارير الجديدة بالمنطقة الجديدة. وتحتفظ التقارير المُنشأة من قبل بمنطقتها. | app/features/workspace-settings/workspace-settings-page/workspace-settings-page.ts |
| `workspaceSettings.zone.confirm.count.one` | {$count} scheduled report moves to the next start of a day in the new zone, and days in analytics and new reports are counted in it from now on. Reports already generated keep their zone. | ينتقل تقرير مجدول واحد إلى أقرب بداية يوم في المنطقة الجديدة، وتُحسب بها من الآن الأيام في التحليلات والتقارير الجديدة. وتحتفظ التقارير المُنشأة من قبل بمنطقتها. | app/features/workspace-settings/workspace-settings-page/workspace-settings-page.ts |
| `workspaceSettings.zone.confirm.count.two` | {$count} scheduled reports move to the next start of a day in the new zone, and days in analytics and new reports are counted in it from now on. Reports already generated keep their zone. | ينتقل تقريران مجدولان إلى أقرب بداية يوم في المنطقة الجديدة، وتُحسب بها من الآن الأيام في التحليلات والتقارير الجديدة. وتحتفظ التقارير المُنشأة من قبل بمنطقتها. | app/features/workspace-settings/workspace-settings-page/workspace-settings-page.ts |
| `workspaceSettings.zone.confirm.count.few` | {$count} scheduled reports move to the next start of a day in the new zone, and days in analytics and new reports are counted in it from now on. Reports already generated keep their zone. | تنتقل {$count} تقارير مجدولة إلى أقرب بداية يوم في المنطقة الجديدة، وتُحسب بها من الآن الأيام في التحليلات والتقارير الجديدة. وتحتفظ التقارير المُنشأة من قبل بمنطقتها. | app/features/workspace-settings/workspace-settings-page/workspace-settings-page.ts |
| `workspaceSettings.zone.confirm.count.many` | {$count} scheduled reports move to the next start of a day in the new zone, and days in analytics and new reports are counted in it from now on. Reports already generated keep their zone. | ينتقل {$count} تقريرًا مجدولًا إلى أقرب بداية يوم في المنطقة الجديدة، وتُحسب بها من الآن الأيام في التحليلات والتقارير الجديدة. وتحتفظ التقارير المُنشأة من قبل بمنطقتها. | app/features/workspace-settings/workspace-settings-page/workspace-settings-page.ts |
| `workspaceSettings.zone.confirm.count.other` | {$count} scheduled reports move to the next start of a day in the new zone, and days in analytics and new reports are counted in it from now on. Reports already generated keep their zone. | ينتقل {$count} تقرير مجدول إلى أقرب بداية يوم في المنطقة الجديدة، وتُحسب بها من الآن الأيام في التحليلات والتقارير الجديدة. وتحتفظ التقارير المُنشأة من قبل بمنطقتها. | app/features/workspace-settings/workspace-settings-page/workspace-settings-page.ts |

## 9. Risks and open issues

1. **The 22.2.5 cooldown.** At the start it was 6 days 20½ hours old by the clock (§4.1). If you count by hours, 22.2.4 was the strict choice; both fix the advisories.
2. **One `test:ci` run started under the memory floor** (5.67 GB) by my mistake. It was not counted, and the gate was repeated above 6 GB.
3. **A knowledge event during the list's first load is ignored.** If the attempt ends while the first read is in flight, that read can show "Indexing now" until the next event or visit. It is rare, and the editor is not affected.
4. **The fast stand-in makes "Indexing now" short.** It was seen live on the list (§5), but only for a moment; a real provider holds it longer.
5. **No WhatsApp, unnamed-merged or finite-current-plan data on the dev stack**, so those cases are proved by specs only (as in 38.7a and 38.7b).
6. **The suite's new limits** (`actionTimeout` 15 s, `navigationTimeout` 20 s, `globalTimeout` 15 min) fail a slow step sooner than before. Both runs passed with room; a much slower machine might need them raised.
7. **For Phase 39 (the redesign), not done here (rule 8):**
   - A busy row can now hold three lines under the preview: the reason badge, "Handled by" and "Assigned to". The redesign could merge who handles and who is assigned.
   - "Handled by" sits in the handling bar beside the voice chip. The redesign may want it in the conversation's meta line.
   - A merge candidate shows two codes: the label's short code and the record's "ref.". One may be enough now that the label carries a code.
   - Report downloads are plain links, so a refusal (not ready, expired) is shown by the browser, not by Nabra. Fetching the file through the client would let the new words show.
   - The Arabic row form «استلام المحادثة: …» and the heading form «تمّ استلام المحادثة من قِبل …» differ on purpose (§4.5); pick one if you prefer.

## 10. Proposed checkpoint commit message

```
feat: patch Angular for the router and zod advisories, show each visitor message once, show the inbox live with who handles a conversation and why it needs a person, label customers like the inbox, let indexing end by itself, count the schedules a zone change moves, and word every new refusal

- Angular framework 22.1.5 -> 22.2.5 and tooling 22.1.7 -> 22.2.6
  (zod 4.4.3 -> 4.6.6 with them); npm audit --omit=dev: 0
- Hosted chat and widget merge a turn's messages by id (BA-13
  de-duplication)
- Inbox: takenOverBy in the heading and rows; the new-message sound
  counts takeovers and fires once per message; each needs-a-person
  row says its reason (BA-2, BA-7)
- Customers use the inbox's label from labelCode and shortCode;
  customerName() removed (BA-1)
- Knowledge: one connection joins the knowledge group on the
  knowledge screens; list and editor re-read on knowledgeChanged; no
  reload wording; timed_out worded; the archive limit names its unit
  (BA-10, BA-11, BA-12)
- Workspace settings ask with scheduleCount for every manager (BA-9)
- Every code 38.7b added worded in English and Arabic (BA-4); types
  checked against the OpenAPI document (BA-5)
- e2e: slow-reply waits on the open inbox and every wait is bounded;
  knowledge-index proves BA-10/11; take-over and needs-a-person
  extended; suite-wide action, navigation and global timeouts
- PHASE_38_SUMMARY.md: every backend ask marked with the step that
  closed it

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

## Review (2026-11-13)

The owner accepted this report with these rulings:

1. **Framework 22.2.5 stays.** The prompt named it, it has the fixes, and it missed the seven-day rule by hours, not days. No downgrade to 22.2.4 (§4.1, §9.1).
2. **The 61 Arabic strings are accepted as they are,** both "Handled by" forms included (§4.5, §8, §9.7). A native-speaker pass on all the Arabic comes before launch.
3. **§7's two backend asks go to Phase 39.1.** §9's redesign notes (§9.7) and the knowledge list's first-load gap (§9.3) go to the Phase 39 plan.
4. **The four development-only advisories** (`undici`, `vitest`, `@vitest/mocker`, `fast-uri`; §5) wait for the launch hardening, because none of them ships to production.
