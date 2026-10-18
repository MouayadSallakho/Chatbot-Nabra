# Phase 36.7: Overview, Analytics, Provider usage and Reports ("numbers that explain themselves")

Date: 2026-10-18 to 2026-10-19. Branch `master`. Not committed (see section 9).
Evidence: `C:\Users\mouay\Desktop\omniai-redesign-evidence\p36-07\` (written `<evidence>` below).

## 1. Summary

- The Overview now answers three questions in order: what needs you (one card per item, each with one action), what is happening now (open conversations split between the AI and the team), and how the week went (four real figures with their seven-day shapes, plus a messages-per-day chart).
- Analytics is a set of clear charts in the two voice colours: a period choice that is kept in the link, messages and new conversations per day, channels, agents, how AI replies ended, and today marked as "so far".
- Provider usage shows calls, tokens and the estimated cost by kind of operation and by model. Unrated calls are said to be unrated, and a period with no calls says so in a sentence, never as `$0.00`.
- Reports is a calm catalogue of report cards beside a period-and-format panel, with clear statuses for generated files. The schedule editor now warns before unsaved edits are lost.
- Everything is in English and Arabic: 353 new messages, all six Arabic plural forms, and charts that run right to left in Arabic.

## 2. Starting point

- Branch `master`. HEAD `e43a821` ("feat: make the inbox a workspace where the handoff is the heart"), which is the accepted Phase 36.6 checkpoint. The tree was clean.
- The first attempt at this phase stopped before any change, because 36.6 was still uncommitted. After the 36.6 checkpoint commit, the phase began from a clean tree. Two connection drops during the phase were recovered without losing work: the tree, the dev server and the API were checked each time.
- Local stack: `omniai-p36-api` (Development), `omniai_pg25`, `omniai_redis18`, already running and ready (`/health/ready` 200). Dev server on 4200 already running. Provider canary PASS before any traffic (`<evidence>\canary-start.log`).
- Baseline: production build PASS with one existing warning (`embed-chat.scss` 4.07 kB). Startup JavaScript is 411,149 B raw, 135,674 B gzip and 120,053 B brotli (`<evidence>\startup-js-baseline.json`).
- What the API returns (`<evidence>\api-inventory.json`, owner, analyst and support, read before any chart was designed):
  - `GET /analytics/dashboard?range=7d|30d|90d` returns:
    - `period` (from, to = now, days, timeZone UTC);
    - `summary` (conversations started; customer, AI, human and system messages);
    - `current` (open conversations, by AI, by human);
    - `dailyActivity` (every UTC day of the period, with the four counts);
    - `channels` (used ones only) and `agents` (top 10);
    - `aiOperations` (attempts, completed, failed, in progress);
    - `aiUsage` (calls; generation and embedding calls; succeeded, failed and unresolved; five token sums; unrated calls; `ratedCost[]` per currency), which is null when there were no calls in the period;
    - `models[]` (top 10: calls, input and output tokens, cost or null, unrated) and `whatsappDelivery` (seven statuses, or null).
  - There is no comparison period and no purpose breakdown. Every synthetic record falls on 2026-10-18, so the daily charts honestly show one busy day. No provider was ever called, so `aiUsage` is null.
  - Reports:
    - the catalogue has 8 types, formats `Csv`/`Xlsx`/`Pdf`, max 366 days, storage configured;
    - 5 generated reports;
    - 2 schedules (one on, one off).
  - The analyst gets 403 on billing; support gets 403 on dashboard, catalogue, executions and schedules.

## 3. Changes by area

`git status --short` at the end (29 modified, 6 new paths):

```
 M docs/design/MASTER.md
 M frontend/src/app/features/analytics/analytics-dashboard/analytics-dashboard.html|.scss|.spec.ts|.ts
 M frontend/src/app/features/analytics/provider-usage/provider-usage.html|.scss|.spec.ts|.ts
 M frontend/src/app/features/design-lab/design-lab.html|.ts
 M frontend/src/app/features/overview/overview.html|.scss|.spec.ts|.ts
 M frontend/src/app/features/reports/report-schedules/report-schedules.html|.scss|.spec.ts|.ts
 M frontend/src/app/features/reports/reports-api.ts
 M frontend/src/app/features/reports/reports-nav/reports-nav.ts
 M frontend/src/app/features/reports/reports-page/reports-page.html|.scss|.ts
 M frontend/src/app/features/reports/reports.routes.ts
 M frontend/src/app/shared/icon/icon-paths.ts
 M frontend/src/locale/messages.ar.json
 M frontend/src/locale/messages.json
 M frontend/src/styles.scss
?? frontend/src/app/features/analytics/analytics-words.ts (+ spec)
?? frontend/src/app/features/reports/reports-words.ts (+ spec)
?? frontend/src/app/shared/charts/   (chart-model, chart-measure, chart-frame, time-series-chart,
                                       column-chart, area-chart, bar-chart, stacked-bar, sparkline,
                                       meter, charts.spec)
?? frontend/src/styles/_charts.scss
```

Screenshots:
- Before: `<evidence>\before\` — `{owner,analyst}-{en,ar}-light-{1440,390}-{overview,analytics,reports,schedules}.png`, 32 files.
- After: `<evidence>\matrix\` — `{owner,analyst}-{en,ar}-{light,dark}-{1920,1440,1280,1024,768,390}-{overview,analytics,analytics-7d,reports,schedules}.png`, 240 files, with `matrix.json`.
- Design Lab charts with sample data: `<evidence>\lab\lab-charts-{en,ar}-{light,dark}.png`.
- Keyboard: `<evidence>\keyboard\*.png`.

### Charts without a library (`shared/charts/`, `styles/_charts.scss`)

There are six components on one model:
- `app-column-chart`: stacked columns over time;
- `app-area-chart`: a line over a 10% wash;
- `app-bar-chart`: horizontal bars with the value at the tip;
- `app-stacked-bar`: the parts of one whole, such as AI against the team;
- `app-sparkline`: decorative, with its numbers printed beside it;
- `app-meter`: a real `role="meter"` with a value text.

Each plotted chart sits in `app-chart-frame`. The frame gives it a title at the right heading level, a visible one-sentence summary (its text alternative), and a **View as table / View as chart** switch that keeps its place, so focus stays on it. The table holds every number the marks draw.

How they are drawn:
- They draw in measured pixels (`ResizeObserver`), so columns never exceed 24 px and text is never stretched.
- Marks follow the dataviz spec: 4 px rounded data end, square foot, 2 px surface gap between segments, 2 px lines, markers with a surface ring, hairline gridlines. Tabular figures are used on axes and in tables.
- Arabic mirrors in code: time runs from the right, the value axis sits on the right, and bars grow from the right.

How they are read:
- The plot is one tab stop. The arrow key that points along time moves forward (so they swap in Arabic), and Home, End and Escape work. Each day is spoken once in a polite live region.
- The pointer gets the same readout as a hover card that never gates a value.
- A running day ("today, so far") is drawn lighter and said in a sentence, the readout and the table.

Colour comes from roles only:
- Lagoon is the AI, Ember the team, slate the customers, and neutral ink a single series.
- Danger appears only for failures, and a lighter neutral step only for "still running". There is no green (see Decisions).
- Forced colours fall back to system colours.

Shared rules live in the new global `_charts.scss` layer, so no component budget grows. The figures strip moved there too, because Overview and Analytics share it. Before: `before\owner-ar-light-1440-analytics.png` (a CSS bar chart with no axis, all English). After: `matrix\owner-en-light-1440-analytics.png`, `matrix\owner-ar-dark-1440-analytics.png`.

### Overview

- **Needs attention:** one card per item, most serious first, each with exactly one action.
  - Every card carries its severity as a word (Critical, Warning, For your information), a glyph and an edge.
  - Limits running out show a meter with "N of M used".
  - Failed AI replies say "Out of 135 attempts in the last 7 days".
  - New: a source that fails to load (plan limits, subscription) becomes a "Not checked" card with **Try again**, instead of silently disappearing and suggesting "all clear".
- **Right now:** the open-conversation count and the voice split bar. This is the page's one signature.
- **Last 7 days:** four figures (conversations started, customer messages, AI replies, replies from the team), each with its seven-day sparkline, and a messages-per-day column chart whose table also carries conversations per day. There are no trends or comparisons.
- **Shortcuts:** unchanged in behaviour, now translated.
- Each source still loads with its own permission and fails on its own.
- Before: `before\owner-en-light-1440-overview.png`. After: `matrix\owner-en-light-1440-overview.png`, `matrix\analyst-en-light-1440-overview.png` (no billing, one shortcut), `matrix\owner-ar-dark-390-overview.png`.

### Analytics

- **Period:** a radio segmented control for 7, 30 or 90 days, kept in the address (`?period=7d`; the 30-day default is left out). A sentence bounds the period: "Sep 19, 2026 – Oct 18, 2026, counted in UTC days. Today is counted until 10:46 PM UTC."
- **Layout:**
  - figures strip;
  - Right now (voice split), beside "Who replied to customers" (AI against the team as a stacked bar, with its share sentence);
  - Day by day: messages per day by sender, and conversations started per day;
  - Channels: a bar chart, or a sentence when only one channel was used, since a one-bar chart is an anti-pattern;
  - AI agents table;
  - "How AI reply attempts ended";
  - WhatsApp delivery, with the failure flagged by glyph and word.
- **States:**
  - first load shows skeletons in the final geometry;
  - a refresh dims the figures instead of blanking them;
  - a failed refresh keeps the figures and says "These figures were read at …" with Try again;
  - a failed first load shows the error state with Try again;
  - no activity in the period shows the honest empty state;
  - no permission shows its state;
  - partial data is marked "today, so far", plus "Still running" turns and "Outcome unknown" calls;
  - system messages are noted apart when present.
- After: `matrix\owner-en-light-1440-analytics.png`, `matrix\owner-en-dark-1920-analytics-7d.png`, `matrix\analyst-ar-light-390-analytics.png`.

### Provider usage

- Calls are shown by kind of operation (generation and embedding; a bar chart with table), how the calls ended (answered, failed, outcome unknown), and tokens by kind (generation input with its cached part, output with its reasoning part, embedding input).
- The estimated provider cost is shown per currency and never summed, to six places when needed ("€0.004812").
- "Not priced" appears for a model billed in two currencies. "Unrated does not mean free" appears with an exact plural count.
- A period with no calls is one sentence, never a zero or a currency. This replaces the old "render nothing", which left the reader guessing.
- The section stays outside the activity guard.
- Purpose (answering, retrieval, indexing, preview) is recorded by the backend but not returned, so it is a backend ask.
- The populated design is shown only in the Design Lab with labelled sample data (`lab\lab-charts-en-light.png`), because the stack never called a provider.

### Reports

- **Generate and download:**
  - The catalogue is radio cards (title, description, formats, "Can be scheduled"), beside a period-and-format panel that stays in view from 64rem.
  - The format is a segmented choice. The generate behaviour, the idempotency key, the bounded polling and the custom-range validation are unchanged.
  - The generated list is now five columns: report with format and size, period, requested (who and when), status, download. Statuses are word + the badge's own glyph + colour ("Waiting", "Generating", "Ready", "Failed", "Expired").
  - Failure sentences are translated by the server's stable code.
  - Sizes use `Intl` byte units ("4.9 kB" / «4.9 كيلوبايت»).
- **Schedules:** the list and editor are in the new system. The frequency choice has a hint for each option, and On/Off is the badge's word and glyph.
  - **New: the editor has the unsaved-changes guard** (route `canDeactivate` + `beforeunload`), the one editor that lacked it.
  - The stale-version notice, focus return, `If-Match` and "Turn off" all work exactly as before. There is no destructive action on these screens, and none was added.
- Before: `before\owner-en-light-1440-reports.png`. After: `matrix\owner-en-light-1440-reports.png`, `matrix\owner-ar-dark-1440-reports.png`, `matrix\analyst-ar-light-768-schedules.png`.

### Arabic

- 353 new messages. The catalogue went from 651 to 1,004 messages, all translated, `i18n:check` 0 errors and 0 warnings. The glossary is respected (التحليلات، المحادثات، القنوات، وكلاء الذكاء الاصطناعي، مساحة العمل، الفوترة، الإشعارات، نبرة), with Arabic punctuation.
- Every counted sentence has all six plural forms. Elsewhere counts are written as "label: number" to avoid agreement errors.
- Dates are Gregorian with Western digits through the existing policy. UTC days are read as UTC.
- Codes and amounts are isolated as LTR where needed: model names, currency amounts and format codes.
- Report titles, descriptions and failure sentences are named on the client by type and code; an unknown type or code keeps the server's words.

### Shared

- Three glyphs added to the icon set: `table`, `clock`, `calendar`.
- `reports-api.ts` no longer holds the English-only format and size labels; they moved to `reports-words.ts`.
- MASTER.md has nine new decision rows.
- The Design Lab has a "Charts (sample data)" section, labelled as sample. It reuses existing lab classes, so its stylesheet stays within budget.

## 4. Decisions

1. **No chart package.** The six components cover every series the API returns. A package would add weight, and its own RTL and accessibility gaps. None was proposed.
2. **Colour-vision separation was computed, not judged** (dataviz `validate_palette.js`).
   - Slate customers beside Lagoon is ΔE 2.9 for deuteranopes, so stacks run customers → team → AI from the baseline. As stacked, every adjacent pair is ΔE ≥ 12.4 in light and dark.
   - The Lagoon/Ember pair is ΔE 13.1 / 12.8.
   - The validator also flags Lagoon's chroma (0.09 < 0.1) and the dark voice lightness. These are locked brand tokens, always shown with words, a legend and a surface gap. They were not changed.
3. **No green in charts.** Green beside red is ΔE 3.2 (deutan). Outcome bars use neutral ink for the expected outcome, danger for the failure, and a lighter neutral for unsettled, with words and counts.
4. **"By purpose" is honest by kind of operation and by model.** A real purpose breakdown is a backend ask.
5. **One-channel case:** a sentence instead of a one-bar chart.
6. **The period lives in the URL**, as the only list-like state on these pages. Nothing is stored on the server (D22).
7. **The failed-source card** on the Overview replaces silent hiding. It says "Not checked" rather than letting an empty list read as all clear.
8. **Badges keep their primitive glyph.** My first version added icons inside badges, which doubled the glyph. Removed after looking at the screenshots.
9. **Rejected:**
   - KPI deltas and trend arrows (no comparison period, D22);
   - a donut for AI against the team (close values, part-to-whole is clearer as a bar);
   - per-chart period filters (one filter row scopes the page);
   - count-ups and fade-ups (MASTER section 7).
10. **Deferred:**
    - Charts mirror with the page language, not with the Design Lab's preview-direction toggle; the lab says so.
    - The schedule "Turn off" stays unconfirmed, as before: it is reversible.

Skills used: `frontend-pro-max:ui-ux-design-lead`, `enterprise-erp-ux`, `design-system-tokens`, `motion-interaction`, `web-performance`, `angular-enterprise` and `frontend-testing-quality`, plus `dataviz` for the chart method and palette validation. None was missing.

## 5. Verification

| Check | Result |
|---|---|
| `npm run format:check` | **PASS**: all matched files use Prettier style (`<evidence>\final-format.log`). |
| `npm run test:ci` | **PASS (final run)**: 86 files, 990 tests, 0 failures (`<evidence>\final-test-2.log`; was 83 files / 961 tests). **FLAKY, not from this phase:** `core/realtime/conversation-realtime.spec.ts` (36.6) failed 2 tests in 2 of the 5 full runs made after this phase's code was complete (`test-2.log`, `final-test.log`), passed in the other 3, and passed 3 of 3 times on its own. In the failing runs the mocked SignalR builder is never called, which points at the `vi.mock('@microsoft/signalr')` stand-in not applying in some parallel runs. An attempted fix rested on a wrong diagnosis and was reverted; the spec is as 36.6 left it. |
| `npm run build` | **PASS**: one warning, the existing `embed-chat.scss` 4.07 kB (also in the baseline). No new budget warning: a `design-lab.scss` overrun from this phase was found and removed by reusing existing classes. Initial 295.86 kB raw / 71.02 kB transfer (baseline 290.12 / 70.30; the growth is the global chart layer). |
| Startup JavaScript (loader + application chunk, production) | **PASS**: baseline 135,674 B gzip / 120,053 B brotli; final 135,644 B gzip / 119,938 B brotli (−0.02% / −0.10%). Charts live in the lazy feature chunks (`<evidence>\startup-js-*.json`). |
| `npm run i18n:check` | **PASS**: 1,004 messages, 0 errors, 0 warnings. |
| Chrome matrix | **PASS**: 240 cells. Routes: overview, analytics, analytics?period=7d, reports, schedules. Widths 1920/1440/1280/1024/768/390. Light and dark. English and Arabic. Owner and analyst. Per cell: page overflow 0, clipped content none, console errors 0, exactly one `h1`, unnamed controls 0 (`<evidence>\matrix\matrix.json`, `matrix.log`). A layout bug found earlier at 390 px (cards clipped by the shell, invisible to the overflow check) was fixed (`contain: inline-size` on charts, `minmax(0, 1fr)` tracks), and a clipped-content check was added to the matrix. |
| Contrast (`contrast_check.py`, built CSS) | **PASS**: 42 pairs × light, dark, light + more, dark + more. Per mode: 40 pass, 0 fail, 2 informational (decorative gridline; the pale "still running" step, always with its word). Tightest required: 3.57:1 (radio boundary, dark, UI 3:1); tightest text 4.86:1 (`<evidence>\contrast\*.md`). |
| Keyboard | **PASS** (`<evidence>\interactive.json`, `keyboard\*.png`): Tab reaches each chart's switch and plot; arrows read days aloud ("October 18, 2026 (so far): Customers 135, Team 217, AI 0") with visible focus; the table switch keeps focus; period radios change the URL by arrow key; catalogue radios select by arrow key; the schedule editor opens with focus in the name and returns it on Cancel. Targets ≥ 24 px on every new control. |
| Reduced motion | **PASS**: the refresh dimming, segmented and catalogue transitions compute to 1e-05 s; nothing moves on load, with no count-ups or fade-ups. |
| RTL mirroring | **PASS**: in Arabic the first day (12 أكتوبر) is drawn at x 1145 and the last (18 أكتوبر) at x 92 on a 1271 px plot; the value axis is on the right; ArrowLeft moves forward; bars and the meter grow from the right; amounts and model names stay LTR islands. |
| Unsaved-changes guard (schedules) | **PASS**: in Chrome, leaving with a typed name asks "You have unsaved changes on this page. Leave and discard them?"; dismissing stays on `/reports/schedules`. Also covered by a spec. |
| Lints | **PASS**: `erp_screen_lint.py` 0 findings; `motion_audit.py` 0 HIGH / 0 MEDIUM. `rtl_logical_lint.py` and `token_lint.py`: five warnings, each a deliberate value, annotated with its reason (`<evidence>\lint\`). |
| Provider canary | **PASS** at start and end: `OPENAI_API_KEY`, `STRIPE_SECRET_KEY`, `STRIPE_WEBHOOK_SECRET`, `OMNIAI_WHATSAPP_APP_SECRET`, `OMNIAI_WHATSAPP_VERIFY_TOKEN`, `OMNIAI_LIVE_*` and `Email__Smtp__*` are absent (names only; `<evidence>\canary-start.log`, `canary-end.log`). No provider was called, and no report or schedule was created or changed by the checks. |

## 6. See it live

- http://localhost:4200/overview
- http://localhost:4200/analytics (and http://localhost:4200/analytics?period=7d)
- http://localhost:4200/reports and http://localhost:4200/reports/schedules
- http://localhost:4200/design-lab#lab-charts (sample data, development only; shows the populated Provider usage)
- Accounts: `C:\Users\mouay\Desktop\omniai-redesign-evidence\p36-07\demo-accounts-p36-07.md`. It points to `p36-01\demo-accounts.md`, which holds the password. Use the owner and the analyst; support shows the permission states.

The dev server (`npm start`, port 4200) and the API (`omniai-p36-api`, 127.0.0.1:5027) are running.

## 7. Backend asks

1. **Provider usage by purpose.** The usage table records a purpose for every call (conversation generation, retrieval embedding, knowledge indexing, agent preview), but the dashboard returns only per-operation and per-model figures. A `byPurpose[]` aggregate (calls, tokens, rated cost per currency, unrated) would let the screen answer "what did indexing cost us?".
2. **A comparison period** (the previous equal-length window) for summary and current figures, if trends are wanted. Today the design shows none (D22).
3. **Localised report catalogue.** Titles, descriptions and failure sentences come only in English. The client now translates the eight known types and four codes; a new type or code will appear in English until both are updated.
4. **The workspace's first activity date** in the dashboard, so a period that starts before the workspace existed could say "data from 18 Oct" instead of drawing empty days.
5. Still open from earlier phases: the transitive `System.Security.Cryptography.Pkcs` NuGet-audit issue (36.1).

## 8. Risks and open issues

- The flaky 36.6 realtime spec can fail a CI run (2 of 5 full runs here). It needs a deterministic mock, for example injecting the hub builder through DI. That is outside this phase's screens.
- The demo data covers one day only, so the daily charts are mostly empty bands. This is honest, but less telling than real traffic; spreading data over days would need backdated writes, which the API cannot do.
- Provider usage with real data was verified only through specs and the labelled Design Lab sample. The local stack has never called a provider, by rule.
- Arabic copy has not had a native review (as in earlier phases). The plural forms and glossary are checked by script, not by a native reader.
- The pale "still running" column and step are below 3:1 by design. They are always paired with words; a reader who relies on colour alone still has the sentence and the table.
- The global stylesheet grew by about 5.7 kB raw (0.7 kB transfer) for the chart layer. It stays within budget.

## 9. Proposed checkpoint commit message (not committed)

```
feat: give the numbers words, and the charts both voices

Overview answers three questions in order: what needs you (one card per
item, one action each, and "Not checked" when a source fails), what is
happening now (open conversations split between the AI and the team),
and how the week went (four real figures with their seven-day shapes and
a messages-per-day chart). No trend or comparison the API does not return.

Six small SVG charts in shared/charts (column, area, bar, stacked bar,
sparkline, meter), no package: a title, a summary sentence and a "View
as table" switch on each, one tab stop that reads each day aloud, Lagoon
for the AI and Ember for the team, today marked "so far", mirrored in
Arabic. Colour-vision separation computed: customers never touch Lagoon.

Analytics keeps the period in the link and keeps figures on screen
through a failed refresh. Provider usage shows calls, tokens and the
estimated cost by kind of operation and model; unrated is not free and no
calls is a sentence, never $0.00. Reports is a calm catalogue of report
cards; the schedule editor now asks before unsaved edits are lost.

Everything in English and Arabic (353 new messages, six plural forms).
Frontend only; no new dependency or endpoint.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

## Revision 1

### The note

`conversation-realtime.spec.ts` (from 36.6) is flaky: it failed 2 tests in 2 of 5 full `test:ci` runs. Fix it before the checkpoint by finding the root cause, and make it deterministic, with no retries, no longer timeouts and no skipped tests. Prove it with 5 consecutive full runs with 0 failures, plus the spec on its own.

### Root cause (reproduced, not guessed)

- **The unit-test builder runs Vitest without isolation.** `@angular/build:unit-test` 22.1.7 defaults `isolate` to `false` (`runners/vitest/plugins.js:184`; the schema says "Defaults to false"), so spec files that land in the same worker share one module registry. Code splitting is on by default, so code shared by several specs is emitted once, as a shared chunk.
- **The spec mocked a module.** It was the only `vi.mock` in the codebase: `vi.mock('@microsoft/signalr')`, replacing SignalR's `HubConnectionBuilder` with a fake that records each connection in `built`.
- **Another spec loads the real service first.** `conversation-detail.spec.ts` (also from 36.6) imports `conversation-detail.ts`, which imports `ConversationRealtime`. Whenever it ran before the realtime spec in the same worker, the shared chunk holding `ConversationRealtime` was already evaluated and bound to the real `@microsoft/signalr`. The realtime spec then received the cached service, the mocked builder was never called, `built` stayed empty, and both tests failed with exactly the errors in the flaky runs: "expected [] to have a length of 1" and "Cannot read properties of undefined (reading 'state')". Which files share a worker, and in which order, depends on scheduling and cached durations; that is why it failed in 2 of 5 runs.
- **Reproduction.** A throwaway Vitest config, one worker, files in sequence, outside the repository (`<evidence>\revision-1\repro.vitest.mjs`, passed with `--runner-config`), running `conversation-detail.spec.ts` with `conversation-realtime.spec.ts`:
  - It failed on the first run.
  - It then failed in 2 of 3 repeats. Those were the runs where the detail spec went first.
  - With `reconnect-policy.spec.ts`, which imports only SignalR itself and not the service, it passed.
- **What it was not.** It was not timers, async ordering, or a dynamic import: the service imports SignalR statically. No spec added or changed in 36.7 (overview, analytics, reports, charts) loads the realtime service. My first attempt in the phase (waiting longer with `vi.waitFor`) rested on that wrong timing diagnosis, which is why it did not help; it was reverted before the report.

### What changed

Two files, and nothing else:

- **`frontend/src/app/core/realtime/conversation-realtime.ts`.** The service now gets its SignalR builder through a new injection token, `HUB_CONNECTION_BUILDER`. The token is `providedIn: 'root'`, and its default factory is `() => new HubConnectionBuilder()`, so the product builds exactly the same connection as before. There is no behaviour change and no new dependency.
- **`frontend/src/app/core/realtime/conversation-realtime.spec.ts`.**
  - The `vi.mock('@microsoft/signalr')` is gone. The same fake builder, which records each connection it builds, is now provided through `TestBed` for that token.
  - That way no other spec's module state can decide which builder the service uses. This is also what Angular's own unit-test patch recommends ("Please use Angular TestBed for mocking dependencies").
  - The assertions and the one-task `settle()` are unchanged: no retries, no longer timeouts, no skipped tests.

### Results

| Check | Result |
|---|---|
| Reproduction pair, after the fix (detail spec + realtime spec, one worker, in sequence) | **PASS** 5 of 5 runs, 20 tests each (was 2 of 3 failing) (`<evidence>\revision-1\repro-after-1..5.log`) |
| Realtime spec on its own | **PASS**: 2 tests (`alone.log`) |
| `npm run test:ci`, 5 consecutive full runs | **PASS** 5 of 5: each 86 files, 990 tests, 0 failures (`full-1.log` to `full-5.log`) |
| `npm run format:check` | **PASS** |
| `npm run build` | **PASS**: one warning, the existing `embed-chat.scss` 4.07 kB; initial 295.86 kB raw / 71.04 kB transfer |
| `npm run i18n:check` | **PASS**: 1,004 messages, 0 errors, 0 warnings |
| Startup JavaScript | 135,652 B gzip / 119,947 B brotli (phase baseline 135,674 / 120,053) |

The `test:ci` row in section 5 should now be read as PASS, without the FLAKY note, and risk 1 in section 8 is resolved.

### Commit message (section 9), one paragraph added

```
Fixes a flaky 36.6 spec: the realtime service now takes its SignalR
builder from an injection token, so its test supplies a stand-in through
TestBed instead of mocking the module, which depended on which spec in a
non-isolated worker loaded the service first.
```
