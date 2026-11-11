# Phase 38.5: Insights and reports (time zone, comparisons, purposes, report codes and languages)

## 1. Summary

- A workspace manager can now choose the workspace's **time zone** on Workspace settings. Every day in Analytics, the Overview, report periods and schedules is counted and labelled in that zone, and saving asks first when report schedules would move.
- Analytics and the Overview say how each figure **moved against the previous period**, in words with an arrow ("So far, down 20%…"), name the days compared, and say **how far back the data goes**; a workspace with no conversation yet has its own honest empty state.
- AI provider usage is broken down **by purpose** (replies, knowledge searches, indexing, previews, search tests), one line per currency, with unpriced calls said in words.
- Reports and schedules offer the **file language** ("English file" / "Arabic file", starting in the reader's language), tag each generated report with it, offer only the periods and formats each report lists, and notifications name reports from their code in the reader's language. The server's English refusal text is no longer shown to anyone.
- On phones, the Overview's "Needs attention" rows keep the status word beside its glyph; the title gives way instead.

## 2. Starting point

- Branch `master`, HEAD `98753c9` ("feat: rename the workspace safely, page invitations by status, choose the invitation language, and count role holders"), the 38.4 commit. `git status --short` was empty. Matches "Phase 38.4 accepted".
- Dev stack: API `omniai-p36-api` (up 2 h), Mailpit, Postgres and Redis running; dev server on 4200. The API was recreated with `bash p36-stack.sh api` (stand-in on) and the provider canary passed before any traffic (§5).
- Skills loaded before building: `frontend-pro-max:angular-enterprise`, `enterprise-erp-ux`, `api-contracts-integration`, `web-performance` (all four present).

## 3. Changes by area

`git status --short` at the end (no file under `backend/` or `deploy/` changed):

```
 M frontend/e2e/support/api.ts
 M frontend/src/app/core/api/api-error.spec.ts
 M frontend/src/app/core/api/api-error.ts
 M frontend/src/app/core/api/tenant-api.spec.ts
 M frontend/src/app/core/api/tenant-api.ts
 M frontend/src/app/core/i18n/arabic-render.spec.ts
 M frontend/src/app/core/notifications/notification-api.ts
 M frontend/src/app/core/notifications/notification-words.spec.ts
 M frontend/src/app/core/notifications/notification-words.ts
 M frontend/src/app/features/ai-agents/agent-create/agent-create.spec.ts
 M frontend/src/app/features/ai-agents/agent-editor/agent-editor.spec.ts
 M frontend/src/app/features/ai-agents/agent-editor/agent-preview.spec.ts
 M frontend/src/app/features/analytics/analytics-api.ts
 M frontend/src/app/features/analytics/analytics-dashboard/analytics-dashboard.html
 M frontend/src/app/features/analytics/analytics-dashboard/analytics-dashboard.scss
 M frontend/src/app/features/analytics/analytics-dashboard/analytics-dashboard.spec.ts
 M frontend/src/app/features/analytics/analytics-dashboard/analytics-dashboard.ts
 M frontend/src/app/features/analytics/analytics-words.spec.ts
 M frontend/src/app/features/analytics/analytics-words.ts
 M frontend/src/app/features/analytics/provider-usage/provider-usage.html
 M frontend/src/app/features/analytics/provider-usage/provider-usage.scss
 M frontend/src/app/features/analytics/provider-usage/provider-usage.spec.ts
 M frontend/src/app/features/analytics/provider-usage/provider-usage.ts
 M frontend/src/app/features/billing/billing-page/billing-page.spec.ts
 M frontend/src/app/features/conversations/conversation-problems.spec.ts
 M frontend/src/app/features/design-lab/design-lab.ts
 M frontend/src/app/features/invitations/accept-invitation/accept-invitation.spec.ts
 M frontend/src/app/features/knowledge/knowledge-editor/knowledge-editor.spec.ts
 M frontend/src/app/features/knowledge/knowledge-problems.spec.ts
 M frontend/src/app/features/knowledge/knowledge-upload/knowledge-upload.spec.ts
 M frontend/src/app/features/overview/overview.html
 M frontend/src/app/features/overview/overview.scss
 M frontend/src/app/features/overview/overview.spec.ts
 M frontend/src/app/features/overview/overview.ts
 M frontend/src/app/features/reports/report-schedules/report-schedules.html
 M frontend/src/app/features/reports/report-schedules/report-schedules.scss
 M frontend/src/app/features/reports/report-schedules/report-schedules.spec.ts
 M frontend/src/app/features/reports/report-schedules/report-schedules.ts
 M frontend/src/app/features/reports/reports-api.ts
 M frontend/src/app/features/reports/reports-page/reports-page.html
 M frontend/src/app/features/reports/reports-page/reports-page.scss
 M frontend/src/app/features/reports/reports-page/reports-page.spec.ts
 M frontend/src/app/features/reports/reports-page/reports-page.ts
 M frontend/src/app/features/reports/reports-words.spec.ts
 M frontend/src/app/features/reports/reports-words.ts
 M frontend/src/app/features/roles/role-create/role-create.spec.ts
 M frontend/src/app/features/team/invite-member/invite-member.spec.ts
 M frontend/src/app/features/workspace-settings/workspace-settings-page/workspace-settings-page.html
 M frontend/src/app/features/workspace-settings/workspace-settings-page/workspace-settings-page.spec.ts
 M frontend/src/app/features/workspace-settings/workspace-settings-page/workspace-settings-page.ts
 M frontend/src/app/features/workspace-setup/workspace-problems.ts
 M frontend/src/app/features/workspace-setup/workspace-setup.spec.ts
 M frontend/src/locale/GLOSSARY.md
 M frontend/src/locale/messages.ar.json
 M frontend/src/locale/messages.json
 M frontend/src/styles/_charts.scss
?? frontend/e2e/arabic-report.e2e.ts
?? frontend/e2e/time-zone.e2e.ts
?? frontend/src/app/core/i18n/time-zone.spec.ts
?? frontend/src/app/core/i18n/time-zone.ts
?? frontend/src/app/features/reports/report-titles.ts
```

### 3.1 What changed and why

**E1, the time zone (workspace settings).**
- `core/api/tenant-api.ts`: `TenantDetails` (`Tenant` + `timeZone`) for the versioned read and its writes; new `changeTimeZone(tenantId, timeZone, version)` → `PUT …/time-zone { timeZone }` with `If-Match`, returning the new `ETag`.
- `features/workspace-settings/…/workspace-settings-page.{ts,html}`: a "Time zone" card. For `workspace.manage`: a "Find a time zone" filter (`type="search"`, matches "new york" to `America/New_York`) above a native `<select dir="ltr">` with UTC first, then `Intl.supportedValuesOf('timeZone')` grouped by region in `<optgroup>`s; the saved and chosen zones always stay listed. "Use this browser's zone: …" only chooses it. The hint says what a change does. "Saved time zone: …" and "Unsaved changes" while it differs; the unsaved-changes guard covers both sections. Save sends `If-Match`; the tag it gets back is used by the next save in either section. A 412/428 shows its own stale notice, keeps the choice, and "Reload latest version" reads the workspace beside it. Saving asks first (native `<dialog>`, focus on "Keep the current zone") when the workspace has schedules or the page cannot tell; schedules are read only at Save. Everyone else reads the zone as text (`dir="ltr"`).
- `features/workspace-setup/workspace-problems.ts`: `workspace.time_zone.invalid` worded in the shared registry.
- The name card's stale title now says "This workspace was changed somewhere else." (a 412 can come from either card).

**E2, dates in the workspace zone.**
- New `core/i18n/time-zone.ts`: `zonePhrase` ("in UTC" / «بالتوقيت العالمي», "in Asia/Riyadh time" / «بتوقيت Asia/Riyadh»), `zoneLabel` (headings), the IANA id isolated (LRI…PDI); `formatInZone`, `localDate`, `isLocalMidnight`, `todayIn`, `formatLocalDate`, `nextLocalDate`, `browserZone`, `supportedZones`; `displayZone` formats an instant in UTC (and says UTC) if a browser ever lacks the zone, instead of throwing.
- Every place that printed "UTC" itself, and what it does now:

| Place (38.0 §3.10) | Before | Now |
| --- | --- | --- |
| `analytics-words.ts` period sentence | "counted in UTC days … until {time} UTC" | "whole days in {zone}. Today is counted until {time}", the time read in `period.timeZone` |
| `analytics-words.ts` day labels | from date parts, "UTC" in comments | unchanged formatting (local dates), `localDay`; the running day is the zone's today (`localDate(period.to, zone)`, `isLocalMidnight`), not the UTC date of the read |
| `analytics-api.ts` comments | "always UTC" | the zone the response names |
| `reports-page.html` range hint | "Whole days in UTC, including today" | "Whole days {catalogue zone}, including today" |
| `reports-page.html` column | "Period (UTC)" | "Period", with each report's own zone under its dates ("UTC", "Asia/Riyadh time") |
| `reports-page.ts` custom range | UTC arithmetic, no "today" | calendar arithmetic (`nextLocalDate`); the date fields' `max` and a check use the zone's today |
| `reports-api.ts` `periodLabel` | from date parts | unchanged (local dates), via `formatLocalDate` |
| `report-schedules.html` note | "All times are UTC." | "Days and times on this page are {zone}." |
| `report-schedules.html` column | "Next run (UTC)" | "Next run ({zone})" |
| `report-schedules.ts` next run, "Ready · … UTC" | formatted in UTC | formatted in the workspace zone |
| Overview strip | through `analytics-words.ts` | follows; plus "Whole days {zone}. Data since …" under the chart |
| Plan quota months (`plan-usage-page`, `quota-row`, `notification-words` "UTC") | UTC | **unchanged**: quota months stay UTC by contract |

**E3, previous-period deltas.** `analytics-api.ts` sends `compare=previous` only from the two screens that show it (Analytics, Overview), on the same dashboard read. `analytics-words.ts`: `figureChange` (up, down, same; "So far, …" while the period is running; "No earlier data to compare" when the workspace had no activity before the period, "None in the previous period" when it had), `summaryChanges`, `comparisonSentence` ("Compared with Oct 29, 2026 – Nov 4, 2026."). Each figure shows an `aria-hidden` glyph (↑ ↓ =) beside the words; no colour says whether a change is good. `analytics.compare.invalid` cannot be produced by this client (it only sends `previous`) and keeps the generic words, as planned (§3.5).

**E4, data since.** `dataSinceSentence` under the period on Analytics and under the week on the Overview; with `firstActivityOn: null`, Analytics shows "No conversations yet" instead of "No activity in this period", and provider usage is still reported below.

**E5, usage by purpose.** `provider-usage.{ts,html}`: a "Calls by purpose" table, all five purposes in the fixed order (an unknown purpose is kept, named by its code, isolated), calls with "Failed: n" / "Outcome unknown: n" lines, input and output tokens, and the estimated cost one line per currency (never summed), plus "3 calls not priced" in all six Arabic plural forms. A note says tester searches before 37.6 count under knowledge searches for replies.

**E6, report options by code.** `ReportCatalogEntry.parameters` read; the period choices and formats come from each report's `range` and `format` values, worded by code (`rangeLabel`, `formatLabel`); an unknown value is shown as its code; a period the chosen report does not offer is not left selected.

**E7, notification report names.** `AppNotification.parameters?.reportType`; `notification-words.ts` names the report with the Reports page's own words (`report-titles.ts`). The regular expression on the English body is deleted; no parameters or an unknown type gives the sentence without a name.

**E8, report file language.** `ReportRequest.language`, `SaveReportSchedule.language` always sent; `ReportExecution.language`/`timeZone`, `ReportSchedule.language` read. "File language: English file / Arabic file" on the report form (the reader's language by default, owner decision §4.3 #4) and the schedule form (a new schedule starts in the reader's language; an edit opens and keeps the schedule's own); a "File language" column on schedules; a small neutral tag on each generated report; the download link's name ends with the file language. `language.unsupported` is worded by the core registry.

**E10, the overview on phones.** The tone badge's word is no longer `visually-hidden-below-md`; the title truncates. Rows stay 48 px.

**Removing the English `detail` fallback** (plan §3.5, owner decision §4.3 #7c). `core/api/api-error.ts`: `readableMessage` returns our words for the kind (and the non-AI 503 sentence); `detail` is never read. Thirteen older specs that expected the server's English for an **uncoded** refusal now expect the generic words and assert the English is absent (agent create/editor/preview, workspace setup, accept invitation, knowledge editor/upload, role create, invite member, billing, conversation/knowledge catalogues).

**Smaller.**
- `features/reports/report-titles.ts`: the eight report names on their own, so the bell carries only them (it first pulled the whole reports-words module into the startup JavaScript, §5).
- `_charts.scss`: `.figure-change` (shared by Analytics and Overview). Loading placeholders resized for the extra line (measured: within 0.3 rem of the loaded figures at 390 and 1280 px).
- `design-lab.ts` (development only): sample `byPurpose` rows.
- `GLOSSARY.md`: "Time zone | المنطقة الزمنية".
- E9 (stand-in provider label): not adopted, per the plan (§3.4).

### 3.2 Adopted contracts

| Screen | Endpoint, event or code | Fields read or sent | Tests |
| --- | --- | --- | --- |
| Workspace settings | `GET /api/tenants/{id}` (ETag); `PUT /api/tenants/{id}/time-zone { timeZone }` + `If-Match` → 200 + ETag; 400 `workspace.time_zone.invalid`; 412/428; `GET …/reports/schedules` (count, at Save, with `reports.view`) | `timeZone`; `items.length` | `workspace-settings-page.spec.ts` (+12, incl. `zoneGroups`), `tenant-api.spec.ts` (+1), `arabic-render.spec.ts` (+5 for 38.5); e2e `time-zone` |
| Analytics | `GET …/analytics/dashboard?range=…&compare=previous` | `period.timeZone`, `period.from/to`, `previous.period/summary`, `firstActivityOn`, `aiUsage.byPurpose[]` | `analytics-dashboard.spec.ts` (+11), `analytics-words.spec.ts` (+4), `time-zone.spec.ts` (9), `arabic-render.spec.ts`; e2e `time-zone` |
| Provider usage (on Analytics) | same read | `byPurpose[] { purpose, calls, failed, unresolved, inputTokens, outputTokens, unratedCalls, ratedCost[] }` | `provider-usage.spec.ts` (+5) |
| Overview | `GET …/analytics/dashboard?range=7d&compare=previous` | `previous.summary`, `period.timeZone`, `firstActivityOn` | `overview.spec.ts` (+3) |
| Reports | `GET …/reports/catalog`; `POST …/reports { …, language }`; `GET …/reports/executions`; 400 `language.unsupported` | `timeZone`, `reports[].parameters[] { code, values }`; executions `timeZone`, `language` | `reports-page.spec.ts` (+10), `reports-words.spec.ts` (+3); e2e `arabic-report`, `time-zone` |
| Report schedules | `GET …/catalog`; `POST/PUT …/schedules { …, language }` | catalogue `timeZone`, `parameters`; schedule `language`, `nextRunAt` | `report-schedules.spec.ts` (+5) |
| Notifications (bell, page) | `GET …/notifications` | `items[].parameters.reportType` | `notification-words.spec.ts` (3 new, replacing 2), `arabic-render.spec.ts` |
| Every screen | refusals | `code` only; `detail` never | `api-error.spec.ts` (+7 cases, 4 rewritten) |

### 3.3 Screenshots (`<evidence>\p38-5\chrome\shots\`)

- **Before** (HEAD code on port 4201) and **after**, same names: `before\` and `after\` × `workspace-settings`, `analytics`, `overview`, `reports`, `schedules`, `notifications` at `en-light-1280` and `ar-dark-390`, plus `overview-en-light-390`.
- E10: `before\e10-attention-{en,ar}-390.png` → `after\e10-attention-{en,ar}-390.png`.
- Northwind in Asia/Riyadh: `zone\analytics-riyadh-{en,ar}.png`, `zone\overview-riyadh-{en,ar}.png` (+ `-top`), `zone\schedules-riyadh-{en,ar}.png`, `zone\reports-generated-{en,ar}.png`.
- `after\analytics-compare-{en,ar}.png`, `after\overview-change-ar-zoom.png`, `after\workspace-settings-readonly-{en,ar}.png`, `after\notifications-{en,ar}-light-1280.png`; the matrix's 390 and 1920 px captures `after\matrix-*`.
- The Arabic and English PDFs' first pages: `<evidence>\p38-5\pdf-images\{ar,en}-conversation-activity-1.png`.

## 4. Decisions

- **The zone helper is its own module** (`core/i18n/time-zone.ts`), not `locale-format.ts` as the plan said: its sentences then load with the screens that use them and add nothing to the startup JavaScript. Same behaviour, at least as safe.
- **Searchable and native.** The prompt asks for a searchable list, the plan for a native `<select>`: a "Find a time zone" filter shortens a native select, so both hold (keyboard, screen readers and phones keep the platform control).
- **When saving asks.** Only a known count of zero schedules saves without asking; no `reports.view`, or a failed read, asks. The count is read at Save, not on every visit to the page.
- **Comparison wording: "the previous period"**, with the compared dates said once ("Compared with …"), instead of the plan's example "vs the previous 30 days". One sentence per direction instead of nine per range, and natural Arabic without counted nouns. A previous figure of zero is split in two honest sentences ("No earlier data to compare" when the workspace had nothing before the period; "None in the previous period" when it had).
- **No colour on changes**: whether a rise is good depends on the figure; the word says the direction, the glyph repeats it (`aria-hidden`).
- **E10:** the status word is kept and the title truncates; the action name stays hidden below 768 px, as before (the chevron marks the link and the row keeps its fixed 48 px height from 36.12 R1). The plan allowed the action to wrap; wrapping would break the fixed-height panel, so I did not.
- **Custom periods** are capped at the workspace zone's today (`max` on the date fields and a check: "Choose dates up to today…"), matching the server's "the future is judged by the local today".
- **Report names for the bell** split into `report-titles.ts` (startup JavaScript, §5).
- **Uncoded refusals** now read generically in English too. In the live API the screens touched by the 13 updated specs send codes for most of these refusals (names, slugs, files, invitations), which the catalogues word; the ones without codes are BA-4 (§7).
- **Rejected:** localised zone names (plan §4.2: the files use the id); a zone list shipped in the bundle (it comes from `Intl`); asking the comparison on every dashboard read (only the two screens that show it ask).
- **Deferred:** nothing from 38.5's items.

## 5. Verification

Each frontend gate ran alone, on the final code, with free memory read just before.

| Check | Result |
| --- | --- |
| `npm run format:check` | **PASS**: "All matched files use Prettier code style!" (`gates\format-check.log`) |
| `npm run test:ci` | **PASS**: **120 files, 1,766 tests, 0 failures** (38.4: 119 / 1,690), 8.22 GB free before (`gates\test-ci.log`). Earlier runs during the step failed on the specs that expected English `detail` and on two of my own assertions; all fixed, none retried |
| `npm run build` | **PASS**: 0 warnings, budgets unchanged and met; initial total 314.47 kB raw / 73.86 kB transfer (`gates\build.log`, 7.95 GB free). **Startup JavaScript** (loader + application chunk, same tool as 38.0): **145,212 B gzip** / 438,235 raw / 128,530 brotli, 43 files (`gates\startup-js.txt`). Against 38.4's 144,853: **+359 B**, inside this step's +500 B share; against the 38.0 baseline 138,907: **+4.5 %** (ceiling 152,797). It is **212 B over the plan's cumulative 145,000 target**. The first build measured 146,503 B because the bell's report names pulled all of `reports-words`; splitting the names out removed 1,291 B |
| `npm run i18n:check` | **PASS**: **2,392 messages, 0 errors, 0 warnings** (38.4: 2,335) (`gates\i18n-check.log`) |
| `npm run e2e` | **PASS**: **42 journeys, 42 passed, 0 failed, 2.4 min**, no retries, after the canary, 8.33 GB free (`gates\e2e.log`, results in `<evidence>\p38-5\e2e\`). New: `time-zone` and `arabic-report`, each passing in en-ltr and ar-rtl. The run created one synthetic account and workspace, as each run does |
| Backend gates | **NOT RUN**: no file under `backend/` changed |
| Chrome matrix | **PASS**: 6 routes (`/workspace-settings`, `/analytics`, `/overview`, `/reports`, `/reports/schedules`, `/notifications`) × en/ar × light/dark × 390/768/1280/1920 = **96 page loads: 0 with page overflow, every page one `h1`, 0 axe findings** (button-name, link-name, label, select-name, aria-input-field-name, color-contrast, duplicate-id-aria, aria-allowed-attr, nested-interactive, target-size), **0 console errors or warnings** (`chrome\matrix.json`). Playwright library driving the installed Chrome with a temporary profile, not the Chrome DevTools MCP: signing in through the MCP would put the demo password in tool arguments (rule 10). The matrix ran before the last two edits (placeholder heights; the report-names split), neither of which changes a loaded page; every gate and e2e ran after them |
| Zone change, labels follow | **PASS** on Northwind through the page: Save asked first (Northwind has 2 schedules), PUT 200, toast. In Asia/Riyadh: Analytics "…whole days in Asia/Riyadh time. Today is counted until 4:31 PM" / «…أيام كاملة بتوقيت Asia/Riyadh. يُحسب اليوم حتى 4:31 م»; Overview "Whole days in Asia/Riyadh time. Data since Oct 18, 2026."; Reports hint and Schedules note and heading in the zone; the enabled schedule moved from 2026-11-16 00:00 UTC to 2026-11-15 21:00 UTC, shown as "Nov 16, 2026, 12:00 AM". **Put back to UTC** through the page (asked again, 200); schedule back at 2026-11-16 00:00 UTC; Analytics says UTC again (`chrome\results.json` → `zone`, `files`) |
| The comparison | **PASS**: 7 days (data in both periods): "So far, down 96 % / 95 % / 96 % / 50 % on the previous period", "Compared with Oct 29, 2026 – Nov 4, 2026."; 30 and 90 days: Northwind's data starts on 2026-10-18, so "No earlier data to compare". Arabic: «حتى الآن، أقل بنسبة 96% من الفترة السابقة», the percentage right where an Arabic reader starts the line (`after\overview-change-ar-zoom.png`) |
| Report files in each language | **PASS**: six reports generated **from the screen** in Asia/Riyadh (Conversation activity, CSV, Excel, PDF × English, Arabic), each row tagged "English file" / "Arabic file" and "Asia/Riyadh time", each downloaded by its link (`<evidence>\p38-5\files\{en,ar}\`). The **Arabic PDF's period line** reads «الفترة (بتوقيت Asia/Riyadh)» «من 2026-10-13 إلى 2026-11-11 شاملةً», right to left, days headed «التاريخ (بتوقيت Asia/Riyadh)» (`pdf-images\ar-conversation-activity-1.png`); English "Period (Asia/Riyadh) 2026-10-13 to 2026-11-11 inclusive", generated at "(UTC)" (`pdf-images\en-page1.txt`). Rendered with poppler in a throwaway `alpine:3` container (`--rm`), as 37.7 did |
| Requests, analytics page | **PASS**: 8 API requests before and after (38.0 had 7; the needs-a-person read since 38.2b is the 8th); **one** dashboard read, now `?range=30d&compare=previous`, 5,862 → 6,175 B. The Overview: 10 before and after, its one read `?range=7d&compare=previous`. No other screen asks for the comparison |
| E11, the reports screen | Time from opening `/reports` to the first generated-report row, 9 interleaved runs each, HEAD code (4201) against this step's (4200), dev servers. Run 1: before p50 **523** / p95 542 ms; after p50 **503** / p95 1,021 ms. Run 2: before p50 **631** / p95 706; after p50 **504** / p95 969. p50 equal or better; each "after" set has one slow run (≈1 s) that, with nine runs, is its p95; the page makes the same two reads as before. **The executions read** (20 warm-ups, 200 sequential reads, 20 of 34 rows, as 37.7 measured): p50 **22.4 / 19.0 ms**, p95 **33.0 / 29.4 ms**, max 89 / 60 (37.7: 15.4 / 27.6 at 20 rows). No API change in this step; there is no 38.0 reports baseline, so 37.7 is the reference (`chrome\e11-run1.json`, `results.json` → `e11`) |
| E10 | **PASS**: at 390 px the status word is visible in every row in both languages (before: hidden in every row), titles truncate, rows stay 48 px (`results.json` → `e10`) |
| Keyboard | **PASS**: filter → Tab → select (ArrowDown chose Asia/Riyadh) → Tab → Save → Enter opens the dialog with focus on "Keep the current zone"; Escape closes it, focus returns to Save, nothing saved. Arrow keys move the file-language radios. Focus visible on each stop |
| Contrast, reduced motion, RTL | **PASS**: axe `color-contrast` 0 in all 96 loads; with `prefers-reduced-motion: reduce` the analytics refresh transition is 1e-05 s; RTL: mirrored layouts in the Arabic captures, zone ids isolated left to right, the Arabic PDF mirrored |
| Read-only and permissions | **PASS**: the analyst (no `workspace.manage`) sees "Time zone" / «المنطقة الزمنية» and "UTC" as text, no select; unit tests cover the no-`reports.view` dialog path and the reports/schedules permission states |
| Notifications by code | **PASS**: «اكتمل إنشاء تقريرك «نشاط المحادثات» وهو جاهز للتنزيل.» on the Arabic page, from `parameters.reportType` |
| AI stand-in, Mailpit | Stand-in on (canary info line); the purposes table shows the stand-in's calls (USD, $0, word counts). Mailpit **NOT RUN**: no email in this step |
| Provider canary | **PASS** before any traffic and again before e2e (`gates\canary.txt`, `gates\canary-e2e.txt`): every listed secret and `OMNIAI_LIVE_*` absent, SMTP host loopback, `OMNIAI_DEV_AI_STANDIN` info |
| Secrets | **PASS**: `scratch\secret-scan.mjs` over the evidence folder, the frontend sources, e2e and this report: 657 files, 0 with the demo password, 0 with an invitation link. `<evidence>\p38-5\tmp` (cookie jars, e2e state) and the HEAD export **deleted** (junction removed with `cmd /c rmdir` first; the real `node_modules` checked intact) |

## 6. See it live

Dev server `http://localhost:4200`, API with the stand-in, both left running. Sign in as the demo **owner** (`p36.owner@omniai.example`) or the **analyst** (`p36.analyst@omniai.example`) from `omniai-redesign-evidence\p36-01\demo-accounts.md`; Northwind is on UTC.

- `/workspace-settings`: type "riyadh", choose Asia/Riyadh, Save time zone. Northwind has schedules, so it asks first; confirm. Then open the pages below, and put it back to UTC the same way. As the analyst, the zone is text.
- `/analytics`: the period says the zone; each figure says how it moved; "Compared with …" and "Data since …". Try 7 days (data in both periods) and 30 days ("No earlier data to compare"). Scroll to AI provider usage → "Calls by purpose".
- `/overview`: the week's changes and "Whole days … Data since …"; at a phone width, "Needs attention" keeps the status word.
- `/reports`: "File language", the period hint in the zone, the language tag and zone under each generated report; generate an Arabic PDF and open it.
- `/reports/schedules`: "Days and times on this page are …", "Next run (…)", the File language column and form field.
- `/notifications` in Arabic: report names in Arabic.

## 7. Backend asks

- **BA-5 (documentation, not blocking):** the OpenAPI document lists `previous` as **required** on `AnalyticsDashboardResponse`, while `analytics-usage-foundation.md` §8 and the live API omit it without `compare=previous` (live: absent). The client types it optional and follows the documentation. `GET /api/me/tenants` has no response schema in the document.
- **BA-9 (new, small):** to say how many schedules a zone change moves, the page reads the schedule list, which needs `reports.view`; a manager without it is always asked. A `scheduleCount` on the tenant read (or a dry-run answer on `PUT …/time-zone`) would let every `workspace.manage` holder see the real consequence.
- **BA-4 (unchanged):** with the `detail` fallback gone, refusals the API sends **without a code** now read generically in English as in Arabic (the 13 updated specs show which shapes). Codes for those remaining areas would give them specific words again.

## 8. New Arabic for your review

All 65 new messages (no existing Arabic was reworded; eight replaced messages were removed from the catalogue: `analytics.period.sentence`, `reports.range.hint`, `reports.column.period`, `reports.download.name`, `schedules.deliveryNote`, `schedules.column.nextRun`, `schedules.last.ready`, `workspaceSettings.stale.title`). Glossary row added: "Time zone | المنطقة الزمنية".

| Id | English | Arabic | Where |
| --- | --- | --- | --- |
| `zone.phrase.utc` | in UTC | بالتوقيت العالمي | Every zone label (analytics, overview, reports, schedules) |
| `zone.phrase.named` | in {$zone} time | بتوقيت {$zone} | Every zone label (analytics, overview, reports, schedules) |
| `zone.label.utc` | UTC | بالتوقيت العالمي | Every zone label (analytics, overview, reports, schedules) |
| `zone.label.named` | {$zone} time | بتوقيت {$zone} | Every zone label (analytics, overview, reports, schedules) |
| `analytics.never.heading` | No conversations yet | لا محادثات بعد | Analytics |
| `analytics.never.body` | Numbers appear here once customers start conversations on a connected channel. Any provider usage is still reported below. | تظهر الأرقام هنا حين يبدأ العملاء محادثات على قناة متصلة. ويبقى استخدام المزوّدين، إن وُجد، معروضًا أدناه. | Analytics |
| `analytics.period.whole` | {$from} – {$to}, whole days {$zone}. | من {$from} إلى {$to}، أيام كاملة {$zone}. | Analytics |
| `analytics.period.running` | {$from} – {$to}, whole days {$zone}. Today is counted until {$time}. | من {$from} إلى {$to}، أيام كاملة {$zone}. يُحسب اليوم حتى {$time}. | Analytics |
| `analytics.compare.period` | Compared with {$from} – {$to}. | مقارنةً بالفترة من {$from} إلى {$to}. | Analytics |
| `analytics.since.none` | No conversation has started in this workspace yet. | لم تبدأ أي محادثة في مساحة العمل هذه بعد. | Analytics |
| `analytics.since` | Data since {$date}. | البيانات منذ {$date}. | Analytics |
| `analytics.change.zeroBefore` | None in the previous period | لا شيء في الفترة السابقة | Analytics |
| `analytics.change.noEarlier` | No earlier data to compare | لا بيانات سابقة للمقارنة | Analytics |
| `analytics.change.sameSoFar` | So far, the same as the previous period | حتى الآن، مثل الفترة السابقة | Analytics |
| `analytics.change.same` | The same as the previous period | مثل الفترة السابقة | Analytics |
| `analytics.change.upSoFar` | So far, up {$percent} on the previous period | حتى الآن، أعلى بنسبة {$percent} من الفترة السابقة | Analytics |
| `analytics.change.up` | Up {$percent} on the previous period | أعلى بنسبة {$percent} من الفترة السابقة | Analytics |
| `analytics.change.downSoFar` | So far, down {$percent} on the previous period | حتى الآن، أقل بنسبة {$percent} من الفترة السابقة | Analytics |
| `analytics.change.down` | Down {$percent} on the previous period | أقل بنسبة {$percent} من الفترة السابقة | Analytics |
| `usage.purposes.heading` | Calls by purpose | الاستدعاءات بحسب الغرض | Analytics › AI provider usage, calls by purpose |
| `usage.purposes.note` | Why each call was made. Searches from the knowledge test made before this breakdown existed are counted under knowledge searches for replies. | سبب كل استدعاء. عمليات البحث من اختبار المعرفة التي أُجريت قبل إضافة هذا التفصيل محسوبة ضمن البحث في المعرفة للردود. | Analytics › AI provider usage, calls by purpose |
| `usage.purposes.region` | Calls by purpose table | جدول الاستدعاءات بحسب الغرض | Analytics › AI provider usage, calls by purpose |
| `usage.purposes.caption` | Provider calls by purpose, with estimated cost | استدعاءات المزوّدين بحسب الغرض، مع التكلفة التقديرية | Analytics › AI provider usage, calls by purpose |
| `usage.purposes.purpose` | Purpose | الغرض | Analytics › AI provider usage, calls by purpose |
| `usage.purpose.conversationGeneration` | Replies to customers | الردود على العملاء | Analytics › AI provider usage, calls by purpose |
| `usage.purpose.conversationRetrieval` | Knowledge searches for replies | البحث في المعرفة للردود | Analytics › AI provider usage, calls by purpose |
| `usage.purpose.knowledgeIndex` | Indexing knowledge documents | فهرسة مستندات المعرفة | Analytics › AI provider usage, calls by purpose |
| `usage.purpose.agentPreview` | Agent previews | معاينة الوكلاء | Analytics › AI provider usage, calls by purpose |
| `usage.purpose.retrievalTest` | Knowledge search tests | اختبار البحث في المعرفة | Analytics › AI provider usage, calls by purpose |
| `usage.purpose.unpriced.zero` | No call is unpriced | لا استدعاء غير مسعَّر | Analytics › AI provider usage, calls by purpose |
| `usage.purpose.unpriced.one` | {$count} call not priced | استدعاء واحد غير مسعَّر | Analytics › AI provider usage, calls by purpose |
| `usage.purpose.unpriced.two` | {$count} calls not priced | استدعاءان غير مسعَّرين | Analytics › AI provider usage, calls by purpose |
| `usage.purpose.unpriced.few` | {$count} calls not priced | {$count} استدعاءات غير مسعَّرة | Analytics › AI provider usage, calls by purpose |
| `usage.purpose.unpriced.many` | {$count} calls not priced | {$count} استدعاءً غير مسعَّر | Analytics › AI provider usage, calls by purpose |
| `usage.purpose.unpriced.other` | {$count} calls not priced | {$count} استدعاء غير مسعَّر | Analytics › AI provider usage, calls by purpose |
| `overview.week.zone` | Whole days {$zone}. | أيام كاملة {$zone}. | Overview › Last 7 days |
| `schedules.deliveryNoteZone` | Generated reports are available in Reports. The file is never emailed or sent anywhere else; if you turn on Reports in your notification preferences, you get an email that links here. Weekly schedules run every Monday for the previous seven days; monthly schedules run on the first of the month for the previous calendar month. | تتوفر التقارير المُنشأة في صفحة التقارير. لا يُرسَل الملف بالبريد الإلكتروني ولا إلى أي مكان آخر، وإن فعّلت التقارير في تفضيلات الإشعارات تصلك رسالة فيها رابط إلى هنا. تعمل الجدولة الأسبوعية كل يوم اثنين للأيام السبعة السابقة، والشهرية في أول كل شهر للشهر الميلادي السابق. | Report schedules |
| `reports.field.language` | File language | لغة الملف | Reports and Report schedules forms; schedules column |
| `schedules.zoneNote` | Days and times on this page are {$zone}. | الأيام والأوقات في هذه الصفحة {$zone}. | Report schedules |
| `schedules.column.nextRunZone` | Next run ({$zone}) | التشغيل التالي ({$zone}) | Report schedules |
| `schedules.last.readyAt` | Ready · {$when} | جاهز · {$when} | Report schedules |
| `reports.column.periodPlain` | Period | الفترة | Reports |
| `reports.range.hintZone` | Whole days {$zone}, including today. At most {$max} days. | أيام كاملة {$zone}، ومنها اليوم. {$max} يومًا على الأكثر. | Reports |
| `reports.download.nameLanguage` | Download {$title}, {$period}, {$format}, {$language} | تنزيل {$title}، {$period}، {$format}، {$language} | Reports |
| `reports.error.future` | Choose dates up to today. Days are counted in the workspace's time zone. | اختر تواريخ حتى اليوم. تُحسب الأيام بالمنطقة الزمنية لمساحة العمل. | Reports |
| `reports.language.en` | English file | ملف إنجليزي | Reports |
| `reports.language.ar` | Arabic file | ملف عربي | Reports |
| `workspaceSettings.stale.changed` | This workspace was changed somewhere else. | غُيّرت مساحة العمل هذه في مكان آخر. | Workspace settings |
| `workspaceSettings.zone.heading` | Time zone | المنطقة الزمنية | Workspace settings |
| `workspaceSettings.zone.lede` | Days in analytics, report periods and report schedules are counted in this zone. | تُحسب الأيام في التحليلات وفترات التقارير وجدولة التقارير بهذه المنطقة. | Workspace settings |
| `workspaceSettings.zone.stale.body` | Your time zone has not been saved and is still selected. Reload the latest version to see the saved one; your choice stays, so you can save it again or change it. | لم تُحفظ المنطقة الزمنية التي اخترتها، وما زالت محددة. حمّل أحدث نسخة لترى المنطقة المحفوظة؛ يبقى اختيارك، فتستطيع حفظه مرة أخرى أو تغييره. | Workspace settings |
| `workspaceSettings.zone.find` | Find a time zone | ابحث عن منطقة زمنية | Workspace settings |
| `workspaceSettings.zone.noMatch` | No time zone matches. Try a city or a region, such as Riyadh or Europe. | لا توجد منطقة زمنية مطابقة. جرّب اسم مدينة أو منطقة بالإنجليزية، مثل Riyadh أو Europe. | Workspace settings |
| `workspaceSettings.zone.findHint` | Type a city or a region to shorten the list below. | اكتب اسم مدينة أو منطقة بالإنجليزية لتقصير القائمة أدناه. | Workspace settings |
| `workspaceSettings.zone.label` | Time zone | المنطقة الزمنية | Workspace settings |
| `workspaceSettings.zone.hint` | A change counts days in the new zone from the next time a page reads them, and moves every scheduled report to the next start of a day in the new zone. Reports already generated keep the zone they were made in. | بعد التغيير تُحسب الأيام بالمنطقة الجديدة في المرة التالية التي تقرؤها فيها أي صفحة، وتنتقل كل جدولة تقارير إلى أقرب بداية يوم في المنطقة الجديدة. وتحتفظ التقارير المُنشأة من قبل بالمنطقة التي أُنشئت بها. | Workspace settings |
| `workspaceSettings.zone.saved.label` | Saved time zone: | المنطقة الزمنية المحفوظة: | Workspace settings |
| `workspaceSettings.zone.useBrowser` | Use this browser's zone: | استخدم منطقة هذا المتصفح: | Workspace settings |
| `workspaceSettings.zone.save` | Save time zone | حفظ المنطقة الزمنية | Workspace settings |
| `workspaceSettings.zone.confirm.body` | Every report schedule moves to the next start of a day in the new zone, and days in analytics and new reports are counted in it from now on. Reports already generated keep their zone. | تنتقل كل جدولة تقارير إلى أقرب بداية يوم في المنطقة الجديدة، وتُحسب بها من الآن الأيام في التحليلات والتقارير الجديدة. وتحتفظ التقارير المُنشأة من قبل بمنطقتها. | Workspace settings |
| `workspaceSettings.zone.confirm.action` | Change time zone | تغيير المنطقة الزمنية | Workspace settings |
| `workspaceSettings.zone.confirm.cancel` | Keep the current zone | الإبقاء على المنطقة الحالية | Workspace settings |
| `workspaceSettings.zone.confirm.heading` | Change the time zone to {$zone}? | هل تريد تغيير المنطقة الزمنية إلى {$zone}؟ | Workspace settings |
| `workspaceSettings.zone.saved` | The time zone is now {$zone}. | أصبحت المنطقة الزمنية الآن {$zone}. | Workspace settings |
| `problem.workspace.time_zone.invalid` | Nabra does not recognise that time zone. Choose one from the list. | لا تتعرّف نبرة على هذه المنطقة الزمنية. اختر واحدة من القائمة. | Workspace settings (refusal) |

## 9. Risks and open issues

- **Startup JavaScript is 212 B over the plan's 145,000 B target** (145,212; +359 B this step, inside its share and rule 15). The remaining growth is the eight report names the bell now needs.
- **E11's tail:** each "after" set of nine had one ≈1 s run; p50 is equal or better and the page's reads are unchanged. Dev-server numbers, not a production measurement.
- **Browser and server zone lists differ at the edges.** The server judges every name; an alias the server returns is kept in the list; a browser without a zone formats instants in UTC and says so.
- **Northwind data added through the real API:** 7 Conversation activity reports (6 in Asia/Riyadh, both languages, and one English CSV from a first attempt). Its zone went to Asia/Riyadh and back to UTC twice (one run stopped part-way and was resumed); the enabled schedule moved and came back to 2026-11-16 00:00 UTC. The 2 e2e workspaces each hold one Arabic report and went to Riyadh and back.
- **Uncoded refusals** (BA-4) now say less in English than before, by decision 7c.
- The matrix and captures ran before the two last edits (§5); neither changes a loaded page.

## 10. Proposed checkpoint commit message

```
feat: count days in the workspace time zone, compare with the previous period, and write reports in the reader's language

- Workspace settings: a searchable time-zone field (If-Match, stale notice,
  asks before moving schedules); workspace.time_zone.invalid by code.
- One zone helper: analytics, overview, report periods and schedules label
  and count local days of the workspace zone; plan quotas stay UTC.
- Analytics and overview: changes against the previous period in words
  ("so far" for today), the compared dates, "data since", and an empty
  state for a workspace with no conversation yet; one dashboard read.
- Provider usage by purpose, one line per currency, unpriced calls in words.
- Reports and schedules: options by code, "English file"/"Arabic file"
  (the reader's language by default; edits keep the schedule's own), a
  language tag on each report; notifications name reports by code.
- The overview keeps the status word on phones.
- Refusals never show the server's English detail, in either language.
- e2e: time-zone, arabic-report.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

## Revision 1 (2026-11-11)

**Note (owner, Arabic review).** «غُيّرت مساحة العمل هذه» reads as if the workspace itself was replaced. The stale notice now says the workspace's settings were edited elsewhere. Arabic only: no English, code or other message changed; no test asserted the old Arabic.

**New §8 row** (replaces the `workspaceSettings.stale.changed` row):

| Id | English | Arabic | Where |
| --- | --- | --- | --- |
| `workspaceSettings.stale.changed` | This workspace was changed somewhere else. | عُدّلت إعدادات مساحة العمل هذه في مكان آخر. | Workspace settings (stale notice on the name and time-zone cards) |

