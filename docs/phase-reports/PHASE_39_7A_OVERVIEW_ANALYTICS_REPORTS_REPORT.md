# Phase 39.7a: the overview, analytics and reports (frontend)

Part a of Phase 39.7, as the approved plan splits it (`PHASE_39_0_REDESIGN_PLAN_REPORT.md` §3.6): the bento overview, the analytics charts, the report and schedule forms, and downloads through the client. The report files' renderer is 39.7b and is not touched here. Evidence: `C:\Users\mouay\Desktop\nabra-frontend-evidence\p39-7a\`.

## 1. Summary

- The overview is now the board's bento: a serif greeting by the reader's display name, then tiles from real data only: conversations with the change against the previous 7 days, **Answered by the AI** (the 39.1 figure) as a ring, a gold **Needs a person now** tile with the first three waiting conversations and why, right now, what else needs attention, channels, AI usage this month, and messages per day.
- On phones every waiting or attention row is two lines: who or what, then a chip with its word. Nothing is cut off (Phase 38 note 10).
- Analytics and reports sit on Calm Glass panels. The period being read is drawn in emerald, team lines in gold-800, and every figure uses tabular numbers.
- A report now downloads through the app. A ready file saves under its server name; a refused one ("still being prepared", "no longer kept", "could not be prepared") is said in Nabra's words in English or Arabic beside its row.
- File languages and schedule states are chips with words. The report forms' radio groups are one Tab stop each. Every skeleton holds its loaded size.

## 2. Starting point

- Branch `master`, HEAD `4a6085f` (Phase 39.6b), worktree clean (`git status --short` empty), checked at the start.
- The dev stack was up (API `omniai-p36-api` with the AI stand-in, Mailpit, dev server on :4200). The provider canary passed before any traffic (`gates\canary-start.txt`).
- Skills loaded and used (rule 16): `frontend-pro-max:angular-enterprise`, `frontend-pro-max:enterprise-erp-ux`, `frontend-pro-max:ui-ux-design-lead`, `frontend-pro-max:web-performance`.
- The 21st.dev MCP ("magic") did not connect: its API key is missing. The tiles were built from our own primitives and the boards, as the owner's decision allows (plan §12).
- Chrome checks used Playwright driving the installed Google Chrome with temporary contexts, as in 39.6a/b. The passwords were read into the process only, and the cookie jars were kept in `p39-7a\tmp\`, now deleted.

## 3. Changes by area

`git status --short` at the end:

```
 M docs/design/MASTER.md
 M frontend/e2e/arabic-report.e2e.ts
 M frontend/e2e/support/node.d.ts
 M frontend/src/app/core/i18n/arabic-render.spec.ts
 M frontend/src/app/features/analytics/analytics-api.ts
 M frontend/src/app/features/analytics/analytics-dashboard/analytics-dashboard.html
 M frontend/src/app/features/analytics/analytics-dashboard/analytics-dashboard.spec.ts
 M frontend/src/app/features/analytics/analytics-dashboard/analytics-dashboard.ts
 M frontend/src/app/features/analytics/analytics-words.spec.ts
 M frontend/src/app/features/overview/overview.html
 M frontend/src/app/features/overview/overview.scss
 M frontend/src/app/features/overview/overview.spec.ts
 M frontend/src/app/features/overview/overview.ts
 M frontend/src/app/features/reports/report-schedules/report-schedules.html
 M frontend/src/app/features/reports/report-schedules/report-schedules.scss
 M frontend/src/app/features/reports/report-schedules/report-schedules.spec.ts
 M frontend/src/app/features/reports/report-schedules/report-schedules.ts
 M frontend/src/app/features/reports/reports-api.ts
 M frontend/src/app/features/reports/reports-nav/reports-nav.ts
 M frontend/src/app/features/reports/reports-page/reports-page.html
 M frontend/src/app/features/reports/reports-page/reports-page.scss
 M frontend/src/app/features/reports/reports-page/reports-page.spec.ts
 M frontend/src/app/features/reports/reports-page/reports-page.ts
 M frontend/src/app/layout/app-shell/app-shell.spec.ts
 M frontend/src/app/shared/charts/chart-model.ts
 M frontend/src/app/shared/charts/charts.spec.ts
 M frontend/src/locale/messages.ar.json
 M frontend/src/locale/messages.json
 M frontend/src/styles/_charts.scss
 M frontend/src/styles/_tokens.scss
?? frontend/src/app/features/overview/overview-words.spec.ts
?? frontend/src/app/features/overview/overview-words.ts
?? frontend/src/app/features/overview/tiles/        (answered, attention, channels, conversations, needs-person, usage tiles + _tile.scss + tiles.spec.ts)
?? frontend/src/app/features/reports/reports-download.spec.ts
?? frontend/src/app/features/reports/save-file.ts
```

Nothing under `backend/` or `deploy/` changed. No package was added or changed.

### 3.1 The overview (plan §3.2.4, board Overview)

- **Greeting.** The page's one `h1` is the greeting, "Good morning/afternoon/evening, <display name>", in `--text-display`: Instrument Serif in English, IBM Plex Sans Arabic 600 in Arabic. It is worded from the reader's own clock: Arabic says «صباح الخير» until noon and «مساء الخير» after it. The name is isolated (U+2068…U+2069) so an Arabic name reads correctly on an English page and the other way round. A reader with no display name gets the greeting alone, never an email. Above it, in quiet text: the reader's day (Latin digits, Gregorian) and the workspace's name.
- **Bento grid** (`.bento`): 1 column on a phone, 2 from 768 px, 4 from 1280 px, with 14 px gaps (`--bento-gap`). Each tile is a component in `features/overview/tiles/` (they keep the 4 kB style budget and the lazy chunk):
  - **Conversations** (2×2): the figure (`--text-figure-hero`), its change against the 7 days before in words with ↑/↓/= repeating them, the per-day line in the new `current` chart role with its summary sentence and "View as table", the zone and "Data since".
  - **Answered by the AI**: `summary.conversationsAnsweredByAi` / `conversationsStarted` (39.1, BA39-1, owner Q7 (c)). The 116 px ring is decorative, and the share is real text in its middle. Under it: "25 of 40", the sentence "Conversations the AI answered with nobody stepping in.", and "The 7 days before: 64%" from `previous.summary`. A period with no conversation says so and draws no arc.
  - **Needs a person now** (gold, 1×2): one read of the inbox's own view, `GET …/conversations?filter=needs-person&page=1&pageSize=3`. It shows the total, three two-line rows (the inbox's customer label, then the reason chip: coral "The AI could not reply", gold for the rest), "and 149 more in the inbox", and the one action, "Open the inbox". Rows are read again only when the inbox's shared count changes. The page never polls on its own. The tile is not offered, and nothing is read, without `conversations.view`.
  - **Right now**: the voice split, unchanged in meaning.
  - **Needs attention**: billing, limits, failed AI replies, undelivered WhatsApp messages, unread notifications, and any source that did not answer. Each row: the title, then the severity as a status chip (word and glyph), then the detail. On a phone the detail leaves the screen but stays in the row's name. Three rows, then "+N more". "All clear" is a tick chip. Waiting conversations moved to the gold tile, so they are no longer listed here.
  - **Channels**: one split pill (hidden from assistive technology) and a legend line per channel: name, share and "n conversations" (all six Arabic plural forms).
  - **AI usage this month** (deep tile): the plan-usage `monthlyProviderOperations` entitlement, read with `analytics.view` as before. A capped plan shows the meter and the state chip; "No monthly limit on this plan" otherwise; then "View plan & usage". All colours inside the tile are the dark look's, re-pointed on its host.
  - **Messages per day** (full width from 1280 px): the column chart and the three senders' figures with their changes.
  - **Shortcuts** stay under the bento as soft tiles.
- **Skeletons.** While a source is out, every tile draws its skeleton at its loaded size. The sizes were measured in Chrome on Northwind per layout and per language (§5).

### 3.2 Analytics

- `.page-calm` panels. The conversations-per-day line and the channel bars use the `current` role (emerald-700 light, emerald-300 dark). Team lines stay gold-800 (`--chart-team`), never gold-500.
- Headline figures are tabular (`.figure-value`). Direction was already said in words with a glyph (38.5) and is unchanged.
- The workspace's time zone is used as before (period sentence, comparison, days).

### 3.3 Reports and schedules

- **Downloads go through the client** (note 16):
  - `ReportsApi.download()` reads the file (`responseType: 'blob'`, `observe: 'response'`) and takes its name from `Content-Disposition`: `filename*` first, then `filename`. Path segments and control characters are dropped.
  - A refusal's blob is parsed back into its problem (`problemFromBlob`), so `toApiError` words it by `code` with 38.8's sentences. An unknown code gets the generic wording, and the English `detail` is never shown.
  - `saveFile()` clicks a hidden `download` link for an object URL, removes it and revokes the URL after 10 s. No markup is written and no `innerHTML` is used.
  - The button keeps `data-testid="download"` and its full accessible name, says "Downloading…" with `aria-busy` while it reads, and announces "<title> downloaded." after.
  - A refusal shows under the button (`role="alert"`, `data-testid="download-error"`), and the list is read again so the row says where the report is.
- **Language tag**: each generated report's and each schedule's file language is a small neutral chip. A schedule's state is a chip: On is a tick, Off a hollow ring.
- **Forms**: the choice controls from 39.2b were already in place. Each radio group now has a `name` equal to its `formControlName` (`type`, `format`, `language`, `frequency`). Before, every radio was its own Tab stop, 8 in the catalogue alone; now arrows move within a group.
- **The tabs** "Generate and download" and "Schedules" are 44 px targets (they were 42 px in English on touch).
- `.page-calm`. The generated list and the schedules list are one panel each (radius 24).

### 3.4 Tokens and shared parts

- `_tokens.scss`:
  - `--gold-50` (raw);
  - `--text-figure-hero`;
  - `--bento-gap`;
  - the gold tile's `--tile-need-*` (light and dark);
  - the deep tile's `--tile-deep-*` (dark look values; `--tile-deep-success-fg` is emerald-100 because emerald-300 reads 4.22:1 on the dark look's tint).
- `_charts.scss` / `chart-model.ts`: the `current` tone; `.figure-value` tabular; the role comment now uses the Calm Glass names.
- `analytics-api.ts`: `AnalyticsSummary.conversationsAnsweredByAi` (required; OpenAPI marks it required).
- MASTER.md: 8 decision-log rows, §13.7 (contrast) and §14.7 (components).

### 3.5 Screens

| Screen | What changed | Components and tokens it now uses | Tests |
| --- | --- | --- | --- |
| Overview | Bento from real data, serif greeting, gold needs-a-person tile with two-line rows, attention rows in the new shape, ring, channels split, deep usage tile, full-width messages chart, loading sizes held | `AnsweredTile`, `NeedsPersonTile`, `AttentionTile`, `ChannelsTile`, `ConversationsTile`, `UsageTile`, `app-chip`, `app-customer-label`, `app-meter`, `app-area-chart` (`current`), `app-column-chart`, `app-voice-split`; `--text-display`, `--text-figure-hero`, `--bento-gap`, `--tile-need-*`, `--tile-deep-*`, `--chart-current`, `--radius-panel`, `--color-selected` | `overview.spec.ts` (31), `tiles.spec.ts` (14), `overview-words.spec.ts` (8), `app-shell.spec.ts`, `arabic-render.spec.ts`; e2e sign-in, sound-test |
| Analytics | Calm panels, `current` for conversations and channels, tabular figures | `.page-calm`, `app-segmented`, charts; `--chart-current`, `--chart-team` | `analytics-dashboard.spec.ts` (+1), `charts.spec.ts` (+1); e2e time-zone |
| Reports | Client downloads with worded refusals, language chips, named radio groups, 44 px tabs, calm list panel | `app-radio`, `app-segmented`, `app-chip`, `saveFile`, `ReportsApi.download`; `--status-danger-fg`, `--radius-panel` | `reports-page.spec.ts` (4 new download cases), `reports-download.spec.ts` (8); e2e arabic-report (now through the client), time-zone |
| Report schedules | State and language chips, named radio groups, calm list panel | `app-chip` (success, draft), `app-radio`, `app-segmented` | `report-schedules.spec.ts` (+1) |

### 3.6 Screenshots

`C:\Users\mouay\Desktop\nabra-frontend-evidence\p39-7a\shots\`, named `<screen>-<width>-<lang>-<theme>-<before|after>.png`:

- **Before** (24, HEAD `4a6085f`): overview, analytics, analytics-7d, reports, report-schedules, report-schedule-form × {1440 full, 390} × {en-light, ar-dark}.
- **After** (64):
  - the same six, plus overview-analyst, overview-support, analytics-support, reports-analyst and report-schedules-analyst, × {1440 full, 390} × {en-light, ar-dark} (44);
  - `overview-full-1440-{en-light,ar-dark}-after.png`, the whole bento at a 1900 px tall viewport (the shell scrolls inside `main`, so a "full page" capture stops at the viewport);
  - `reports-download-refused-1440-{en-light,ar-dark}-after.png`, the worded refusal;
  - the four modes for overview, analytics, reports and schedules at 1280 (`*-1280-en-light-{transparency-media,transparency-app,reduced-motion,forced-colors}-after.png`, 16).

Suggested pairs: `overview-1440-en-light-before.png` with `overview-full-1440-en-light-after.png`, and `reports-1440-en-light-before.png` with `reports-download-refused-1440-en-light-after.png`.

## 4. Decisions

1. **Tiles as components**, not one template: each holds its own styles, so every component stylesheet keeps the 4 kB budget, which was not raised. The overview's own stylesheet was at 5.40 kB and then 4.15 kB, and was trimmed to fit. Each tile has its own spec, and all of it stays in the overview's lazy chunk (13,194 B gzip).
2. **The needs-a-person rows come from the inbox's own view** (one GET of 3 rows), not from the shared InboxSignals read. That read is pooled across tabs and shares only the count, and widening it would grow the startup JavaScript. The overview re-reads only when that shared count changes, and not on the first count it reports, which is the one it has just read.
3. **"Needs attention" stays, as a tile beside the gold one.** The board has no such tile, but its items (billing, limits, failed replies, undelivered messages, unread notifications, unchecked sources) are existing behaviour (rule 4). Waiting conversations moved out of it into the gold tile.
4. **"Right now", "Messages per day" and Shortcuts are kept.** They are not on the board but are today's behaviour. Messages per day runs the full width under the four tiles; the three senders' figures and changes replace the old sparklines (the chart shows the days).
5. **The greeting's time of day and the day line use the reader's own clock.** They are about the reader. The data's days stay the workspace's zone, which the conversations tile says.
6. **The deep tile re-points the dark look's colours on its host** (text, link, focus, meter, status chips, skeleton), so the meter and the chip inside it need no second implementation.
7. **Skeleton geometry is held by measured loading sizes per layout and per language** (`:host([aria-busy='true'])`, `:lang(ar)`), as the old overview did, and not by guessing inner blocks. Measured differences after: 0–4 px from 1280 to 1920 px; within 10 px at 768; within 20 px in the phone's messages tile.
8. **Radio groups get names.** This is a keyboard fix found in this step's check. Angular requires `name` to equal `formControlName`.
9. **`AnalyticsSummary.conversationsAnsweredByAi` is required in the type**, as in the OpenAPI document. The four fixtures that build a summary name it.

**Departures from the specification or the board**

| Where | Board / specification | Shipped | Why |
| --- | --- | --- | --- |
| Overview period selector (Today · 7 days · 30 days) | shown | not shipped; the overview stays on the last 7 days | the API has no "today" range, and a selector here is a new behaviour the plan does not list (rule 4) |
| Conversations tile: dashed line for the week before | gold dashed line | not drawn; the change is said in words with its glyph | the API returns the previous period's totals, not its days (BA39-7a-1) |
| First reply tile | shown | left out | BA39-2 (plan §3.2.4) |
| AI usage unit | "replies" | "AI provider operations" | the plan's limit counts provider operations (replies, previews and indexing), so "replies" would be wrong |
| Needs-a-person reasons | coloured text | chips with a dot and a word | plan note 10 ("reason chip under it"); calm-glass §6 chips |
| Answered by the AI | ring and one sentence | also "25 of 40" and "The 7 days before: 64%" | the plan names both periods (39.1 handoff) |
| Tile titles | 14 px 600 secondary | `--text-title-3` (16/24 600) in secondary | the type scale has no 14 px heading step; no new size was added |
| Deep tile in the dark look | not drawn (light board only) | emerald-800 with a line | emerald-900 is the dark panel itself; the tile needs to read as distinct |
| Channels legend | inline | one line per channel | the tile is one column wide beside AI usage; long Arabic names wrapped mid-item |

**Deferred:** nothing of 39.7a's items. The report files (fonts, headings, alignment) are 39.7b.

## 5. Verification

| Check | Result |
| --- | --- |
| `npm run format:check` | **PASS**: all matched files use Prettier style (`gates\format-check.txt`) |
| `npm run test:ci` | **PASS**: 145 files, 2,215 tests, 0 failures, 28.1 s (39.6b: 142 / 2,170). Free memory before: 7.81 GB (`gates\test-ci.txt`) |
| `npm run build` | **PASS**: 0 warnings, budgets met (no budget raised; the overview stylesheet was trimmed under 4 kB). Initial 340.01 kB raw / 76.91 kB transfer. **Startup JavaScript 148,821 B gzip** (loader + application chunk closure, `p39-6b\scratch\startup-js.mjs`; brotli 131,073). 39.6b reported 148,686 B (+135 B, +0.09%); 38.0 baseline 138,907 B (+7.1%); ceiling 152,797 B. The overview's new code is in its lazy chunk (13,194 B gzip). Free memory before: 7.77 GB (`gates\build.txt`, `gates\startup-js.json`) |
| `npm run i18n:check` | **PASS**: 2,563 messages, 0 errors, 0 warnings (39.6b: 2,543). 34 new Arabic messages; 14 orphaned ones the old overview used were removed from `messages.ar.json` |
| `npm run e2e` | **PASS, twice, full suite**: 46 of 46 (23 journeys × en-ltr and ar-rtl), 0 failures, no retries. Run 1 at 21:14 (3.0 min), run 2 at 21:55 (3.0 min), 41 minutes apart, so the throttling journey ran in both. `arabic-report` now clicks the Download button, waits for the browser's download, checks the saved name against the server's `Content-Disposition`, and checks the file's BOM and Arabic heading. `NABRA_E2E_OUT` / `NABRA_E2E_STATE_DIR` under `p39-7a\e2e\`. Free memory before: 7.42 / 8.07 GB (`gates\e2e-run1.txt`, `e2e-run2.txt`) |
| Backend gates | **NOT RUN**: nothing under `backend/` changed (the report renderer is 39.7b) |
| Chrome matrix | **PASS**: overview, analytics, analytics-7d, reports, report-schedules, the schedule form, and the analyst and support variants (11 screens) × 390/768/1280/1920 × en/ar × light/dark = **176 loads, 0 problems**, plus a final 48-load re-run of the three overview screens on the final code, 0 problems. Checked: page overflow 0, one `h1`, 0 unnamed controls, 0 console errors, ≤ 3 blurred layers, no blur on rows, no target under 24 px, none under 44 px on touch, axe (WCAG 2.2 AA) 0 violations at 1280, `dir` correct. This run found and fixed: the deep tile painted white by a parent rule (axe contrast), a 20 px "View plan & usage" link, and 42 px report tabs on touch (`chrome\matrix-after-*.json`) |
| Modes | **PASS**: reduced transparency (media query and in-app setting), reduced motion and forced colours on the four main screens (24 loads): no blur while reduced, nothing animating under reduced motion, no overflow, one `h1`, no unnamed controls, no console errors (`chrome\modes-after.json`) |
| Keyboard | **PASS**: Tab from the heading through each screen in both languages. Every stop shows a ring and is visible. Overview order: chart toggle, chart, the three waiting rows, "Open the inbox", the attention rows, the messages chart, "View plan & usage", the shortcuts. The reports form's radio groups are now one stop each (`chrome\keyboard-after.json`) |
| Skeletons | **PASS**: the API reads held back in Chrome and every tile's box compared with its loaded box, at 390/768/1280/1440/1920 × en/ar: 0–4 px from 1280; ≤ 10 px at 768; ≤ 20 px at 390 (the messages tile, whose summary wraps differently) (`chrome\skeletons-after.json`) |
| Downloads (live) | **PASS**: a ready PDF saved through the button in English and Arabic under the server's name (`conversation-activity-2026-10-13-to-2026-11-11.pdf`, 759,831 B, `%PDF-1.4`), announced in both languages. A **real refusal** was read from the API by asking for a fresh report's file before it was ready: 409 `reports.file.not_ready`. That exact body was answered to the button and worded "This report is still being prepared. Try the download again in a moment." / «ما زال هذا التقرير قيد الإعداد. حاول التنزيل مرة أخرى بعد قليل.», as an alert, with a screenshot in each language. The one console line, "Failed to load resource: 409", is the browser's own network log of the refused request (`chrome\results-after.json` → `downloads`) |
| Contrast of new pairs | **PASS**: 71 pairs (gold tile at both gradient ends, deep tile in both looks, ring, channel parts, attention rows, refusal sentence, day line), all at or above their need. The lowest: 4.03:1 for a meter fill on its track (needs 3:1) and 4.77:1 for the dark danger chip (needs 4.5:1). One failure found and fixed: the success chip in the dark look's deep tile, 4.22 → 7.98 (`scratch\contrast.py`, `chrome\contrast.json`; MASTER.md §13.7) |
| RTL mirroring | **PASS**: the bento flows right to left; the ring fills in the reading direction (mirrored drawing, text unmirrored); chevrons mirror through `MIRRORED_ICONS`; codes, masked numbers and the name in the greeting are isolated; Latin digits throughout (spec-checked) |
| Touch targets | **PASS**: no control under 44 px on touch widths, after the two fixes above |
| Fonts | **PASS**: preloaded as 39.2a set them (English: `geist-latin` 29,400 B; Arabic: plus `ibm-plex-sans-arabic-400` 42,848 B). The English overview adds only `instrument-serif-latin` (21,032 B) for the greeting, on that page alone. Arabic pages use Plex 500 and 600 (45,296 / 45,688 B) and no serif. Layout shift while settling, font swap included: 0 everywhere, 0.001 on the Arabic overview (`chrome\fonts.json`) |
| Provider canary | **PASS** at the start and at the end: every listed provider variable absent, SMTP host loopback, the AI stand-in listed as info (`gates\canary-start.txt`, `canary-end.txt`) |
| Secrets | **PASS**: `scratch\secret-scan.mjs` over the evidence folder, the changed sources and this report: 0 files with the demo password, 0 invitation links. The temporary cookie jars and downloaded files in `p39-7a\tmp\` were deleted |
| Stack at the end | API (`/health/ready` 200) and dev server (:4200) left running. No container, volume or database was created or removed |

## 6. See it live

- Sign in at http://localhost:4200/sign-in as the **Owner** `p36.owner@omniai.example`. The **Support agent** `p36.support@omniai.example` and the **Analyst** `p36.analyst@omniai.example` also work. Passwords are in `omniai-redesign-evidence\p36-01\demo-accounts.md`.
- **Overview**: http://localhost:4200/overview.
  - Check the greeting with Rana Haddad's name, then the gold tile (152 waiting in Northwind) with three two-line rows, and the ring (25 of 40 this week).
  - Switch to Arabic and dark from the account menu.
  - Narrow the window to a phone to see the two-line rows.
  - As the Analyst there is no gold tile; the ring and "Right now" widen.
  - As the Support agent there are no numbers, only the gold tile and what needs attention.
- **Analytics**: http://localhost:4200/analytics. Try the 7/30/90-day choice, "View as table", and the emerald conversations line.
- **Reports**: http://localhost:4200/reports.
  - Press **Download** on a ready report: the file saves under its own name.
  - Generate a report and look at its language chip.
  - Tab through the form: each radio group is one stop.
- **Schedules**: http://localhost:4200/reports/schedules shows the On/Off chips and language chips.
- Mailpit is at http://127.0.0.1:8026 (nothing new is sent by this step).

## 7. Backend asks

| Id | Ask | What ships meanwhile |
| --- | --- | --- |
| **BA39-7a-1** (new) | The previous period's days (`previous.dailyActivity`) beside its totals, for the board's dashed "the week before" line | The change is said in words with its glyph; no second line is drawn |
| BA39-2 (open) | First reply time per voice | The tile is left out |
| (no ask) | A "Today" range on the dashboard | The overview stays on the last 7 days, as before |

## 8. New Arabic for your review

| English | Arabic | Where |
| --- | --- | --- |
| Good morning | صباح الخير | overview greeting (no display name) |
| Good afternoon | مساء الخير | overview greeting |
| Good evening | مساء الخير | overview greeting |
| Good morning, {name} | صباح الخير، {name} | overview greeting |
| Good afternoon, {name} | مساء الخير، {name} | overview greeting |
| Good evening, {name} | مساء الخير، {name} | overview greeting |
| Conversations | المحادثات | conversations tile title |
| Last 7 days | آخر 7 أيام | conversations tile |
| Answered by the AI | أجاب عنها الذكاء الاصطناعي | ring tile title |
| Conversations the AI answered with nobody stepping in. | محادثات أجاب عنها الذكاء الاصطناعي دون أن يتدخّل أحد. | ring tile |
| {answered} of {started} | {answered} من {started} | ring tile |
| The 7 days before: {percent} | الأيام السبعة السابقة: {percent} | ring tile |
| No conversations started the 7 days before | لم تبدأ أي محادثة في الأيام السبعة السابقة | ring tile |
| No conversation started in the last 7 days. | لم تبدأ أي محادثة في آخر 7 أيام. | ring tile, empty |
| Needs a person now | بحاجة إلى الفريق الآن | gold tile title |
| Open the inbox | فتح صندوق المحادثات | gold tile action |
| The waiting conversations could not be read. | تعذّرت قراءة المحادثات المنتظرة. | gold tile, error |
| and 1 more in the inbox | ومحادثة أخرى في صندوق المحادثات | gold tile (one) |
| and 2 more in the inbox | ومحادثتان أخريان في صندوق المحادثات | gold tile (two) |
| and {n} more in the inbox (few) | و{n} محادثات أخرى في صندوق المحادثات | gold tile |
| and {n} more in the inbox (many, other) | و{n} محادثة أخرى في صندوق المحادثات | gold tile |
| {n} conversations (zero, many, other) | {n} محادثة | channels legend |
| 1 conversation | محادثة واحدة | channels legend |
| 2 conversations | محادثتان | channels legend |
| {n} conversations (few) | {n} محادثات | channels legend |
| AI usage this month | استخدام الذكاء الاصطناعي هذا الشهر | deep tile title |
| Plan limit: {limit} | حدّ الباقة: {limit} | deep tile |
| No monthly limit on this plan | لا حدّ شهريًا في هذه الباقة | deep tile |
| All clear | كل شيء على ما يرام | needs attention, nothing to do |
| Downloading… | جارٍ التنزيل… | report download button |
| {title} downloaded. | تمّ تنزيل تقرير «{title}». | report download, announced |

No existing Arabic string was reworded. The 14 removed ids belonged to the old overview ("Right now" link and note, the description sentences, the needs-a-person attention row and its action).

## 9. Risks and open issues

1. **Loading sizes are measured on Northwind's data.** A workspace with three attention items, long names or a capped plan has taller loaded tiles, and its skeleton is then a little shorter than the loaded tile. Nothing overlaps; the content below moves by the difference.
2. **"Answered by the AI" on old dev data** may overcount slightly for conversations from before Phase 37.4 (39.1 decision 6). It is exact on every deployment.
3. **The startup JavaScript grew 135 B** with no startup-path change in this step. Only types and lazy code changed, and builds of the same tree differ by tens of bytes as chunk names change. It is well under the ceiling and was not investigated further.
4. **The meter's warning and critical colours in the deep tile** exist live only in the Design Lab: the dev stack has no capped plan (unchanged from 39.6b). Their contrast is measured from the token values.
5. **Forced colours**: status chips show their word and outline but not their glyph. This is the 39.6b open item for 39.8b.
6. **Arabic analytics loads Plex 700** for the agents table headings (the browser's default bold for `th`). This is not new in this step, but 39.8b's pass could set table headings to 600.
7. **The blob download holds the file in memory** until it is saved. Report files are bounded by the server's size limits (the largest in dev is 760 KB).
8. **The phone order** is conversations, the ring, then the gold tile. It follows the desktop reading order, so keyboard and reading order match. A reader who wants the waiting conversations first has "Open the inbox" and the bottom bar's Inbox.

## 10. Proposed checkpoint commit message

```
feat: lay the overview out as the board's bento from real data only (a serif greeting by the reader's display name, conversations with the change against the 7 days before and the days as an emerald line, "Answered by the AI" as a ring from 39.1's figure with the share before, a gold "Needs a person now" tile with the first three waiting conversations on two lines each and one "Open the inbox", what else needs attention as two-line rows with status chips, channels as one split pill, the month's AI usage on a deep tile, and messages per day across the width), hold every tile's loaded size while it loads, draw analytics on calm panels with the period being read in emerald and tabular figures, download report files through the client so a ready file keeps the server's name and a refusal is worded by its code in English and Arabic, show file languages and schedule states as chips, name the report forms' radio groups, and give the report tabs 44 px targets (Phase 39.7a)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

## Revision 1 (2026-11-15)

The checkpoint's `test:ci` failed one test: `core/authorization/permission-service.spec.ts` "writes nothing to browser storage" read `localStorage.length` 1 (`p39-7a\x1\test-ci.txt`). Fixed at the cause and not retried. Evidence: `p39-7a\rev1\`.

**The key and the leaking spec.** A probe setup file compared both stores before and after every test of the whole suite (`rev1\leak-probe.ts.txt`, run with `--setup-files`, then deleted). Exactly one test left anything behind:

- `features/conversations/conversation-context/conversation-context.spec.ts`, "the customer read (39.4b) › reads the customer once, and shows since when, how many and the newest others", left **`omniai.workspace-tenant-id`** in `localStorage` (`rev1\leaks-before-fix.txt`). The later tests in that file write the same key again, so it was still there when the file ended.
- **App code writes it, correctly.** `WorkspaceContextService.adopt` → `select` remembers the chosen workspace through `LOCAL_STORAGE` (`WORKSPACE_STORAGE_KEY`). The spec, added in 39.4b (`0a125de`), calls `adopt` without giving the service its own store, so the write went to jsdom's real `localStorage`.
- The unit tests run without isolation, so spec files that share a worker share one window and its storage. The permission test failed only when a worker ran that file after `conversation-context.spec.ts`. The step's own run split the files differently. No 39.7a file was involved.
- **Reproduced:** with old code, one worker, conversation-context first (`rev1\repro-one-worker.config.mjs`), the same assertion failed at line 118 (`rev1\repro-before-fix.txt`).

**The fix.**

1. The leaking spec provides `LOCAL_STORAGE` as `memoryStorage()` (the existing test double), so `adopt` never touches the real store. It passes in the failing order on its own, without step 2 (`rev1\repro-spec-fix-only.txt`).
2. A new test setup file, `src/app/testing/browser-storage-setup.ts`, clears `localStorage` and `sessionStorage` before and after every test. It is registered in `angular.json` (`test` → `options.setupFiles`). No spec can see another spec's keys again, whatever the worker split or order. The permission test is unchanged and still proves its own service writes nothing: both stores are empty when it starts, so anything it reads at its end was written during it.

The app code is unchanged.

**Files** (added to section 3's list): `frontend/angular.json` (M), `frontend/src/app/features/conversations/conversation-context/conversation-context.spec.ts` (M), `frontend/src/app/testing/browser-storage-setup.ts` (new).

**Runs.**

| Run | Result |
| --- | --- |
| Failing order, old code | 1 failed / 16 (`rev1\repro-before-fix.txt`) |
| Failing order, both fixes | 16 / 16 (`rev1\repro-after-fix.txt`) |
| `npm run test:ci` #1 (22:50) | **PASS**: 145 files, 2,215 tests, 0 failures, 27.3 s. Free memory before: 8.26 GB (`rev1\test-ci-1.txt`) |
| `npm run test:ci` #2 (22:51) | **PASS**: 145 / 2,215, 0 failures, 30.0 s. Free memory before: 8.34 GB (`rev1\test-ci-2.txt`) |
| `npm run test:ci -- --runner-config rev1\shuffle.config.mjs` (`sequence.shuffle: true`; `ng test` has no `--sequence.shuffle` flag, so it goes through a runner config) | **PASS**: seed 1794772312731, 145 / 2,215, 0 failures, 30.2 s. Free memory before: 8.09 GB (`rev1\test-ci-shuffle.txt`) |

The checkpoint's gates follow this revision.
