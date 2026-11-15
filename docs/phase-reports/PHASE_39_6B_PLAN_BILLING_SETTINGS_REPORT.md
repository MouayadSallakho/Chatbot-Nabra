# Phase 39.6b: plan and usage, billing, workspace settings and notifications (Calm Glass)

Step 39.6b of the approved plan (`PHASE_39_0_REDESIGN_PLAN_REPORT.md`, sections 3.4, 3.6 and 12). Customers, team, roles and invitations were 39.6a and were not touched.

## 1. Summary

- Plan & usage shows each limit as a calm tile. Each tile has the limit's name, its state as a chip with a word and a glyph (tick, triangle, octagon, "no entry" disc, or "i" for Unlimited), the count in large figures inside its own sentence, and a 10 px meter for a capped limit. Next to the plan's name, one chip says how the limits stand overall.
- Billing says plainly when this installation does not take payments: a "Subscription" panel with a "Payments not set up" chip, the plan in effect and the plan limits beside the fallback plan. Nothing is invented.
- Workspace settings name the time zone in the reader's language ("Arabian Standard Time" / «التوقيت العربي الرسمي»), with the IANA id always under it. Managers also see how many report schedules follow the zone. A zone you choose is named before you save it.
- Notifications: the two halves are now a segmented control. The history is one panel of row tiles under quiet day captions. Each email category is a soft tile with the new switch. "Email not set up" is said honestly when it applies. The sound test is unchanged.

## 2. Starting point

- Branch `master`, HEAD `83ebd79` (Phase 39.6a), worktree clean (`git status --short` empty).
- `git log -5 --oneline`: `83ebd79` 39.6a, `f0d6da7` 39.5b, `8fe4dd4` 39.5a, `0a125de` 39.4b, `cf7b109` 39.4a.
- The dev stack was up: `omniai-p36-api` (AI stand-in on), Mailpit, PostgreSQL, Redis. `/health/ready` returned 200, and the dev server on `localhost:4200` was serving. The provider canary passed before any traffic.
- **Interrupted once.** The session hit the 5-hour usage limit after the matrix, the modes and the flows had run. On resume, HEAD was still `83ebd79` with the 34 changed paths intact. Every matrix part and the modes had finished, and the stack and dev server were up (nothing to restart). The canary passed again before any traffic. The remaining checks (keyboard, Design Lab shots), every gate and two more e2e runs were run after the resume.

## 3. Changes by area

`git status --short` (35 files changed, 1 new: this report; +1,641/−346 in tracked files; nothing under `backend/` or `deploy/`, no package change):

```text
 M docs/architecture/admin-design-system.md
 M docs/design/MASTER.md
 M frontend/src/app/core/i18n/arabic-render.spec.ts
 M frontend/src/app/core/i18n/time-zone.spec.ts
 M frontend/src/app/core/i18n/time-zone.ts
 M frontend/src/app/core/notifications/notification-api.ts
 M frontend/src/app/features/billing/billing-page/billing-page.html
 M frontend/src/app/features/billing/billing-page/billing-page.scss
 M frontend/src/app/features/billing/billing-page/billing-page.spec.ts
 M frontend/src/app/features/billing/billing-words.ts
 M frontend/src/app/features/billing/subscription-summary/subscription-summary.ts
 M frontend/src/app/features/notifications/notification-preferences/notification-preferences.html
 M frontend/src/app/features/notifications/notification-preferences/notification-preferences.scss
 M frontend/src/app/features/notifications/notification-preferences/notification-preferences.spec.ts
 M frontend/src/app/features/notifications/notification-preferences/notification-preferences.ts
 M frontend/src/app/features/notifications/notifications-nav.ts
 M frontend/src/app/features/notifications/notifications-page/notifications-page.html
 M frontend/src/app/features/notifications/notifications-page/notifications-page.scss
 M frontend/src/app/features/notifications/notifications-page/notifications-page.spec.ts
 M frontend/src/app/features/plan-usage/plan-usage-page/plan-usage-page.html
 M frontend/src/app/features/plan-usage/plan-usage-page/plan-usage-page.scss
 M frontend/src/app/features/plan-usage/plan-usage-page/plan-usage-page.spec.ts
 M frontend/src/app/features/plan-usage/plan-usage-page/plan-usage-page.ts
 M frontend/src/app/features/plan-usage/plan-words.ts
 M frontend/src/app/features/plan-usage/quota-row/quota-row.ts
 M frontend/src/app/features/workspace-settings/workspace-settings-page/workspace-settings-page.html
 M frontend/src/app/features/workspace-settings/workspace-settings-page/workspace-settings-page.scss
 M frontend/src/app/features/workspace-settings/workspace-settings-page/workspace-settings-page.spec.ts
 M frontend/src/app/features/workspace-settings/workspace-settings-page/workspace-settings-page.ts
 M frontend/src/locale/messages.ar.json
 M frontend/src/locale/messages.json
 M frontend/src/styles/_admin.scss
 M frontend/src/styles/_charts.scss
 M frontend/src/styles/_primitives.scss
 M frontend/src/styles/_tokens.scss
?? docs/phase-reports/PHASE_39_6B_PLAN_BILLING_SETTINGS_REPORT.md
```

### 3.1 What changed and why

- **Meter tokens and the meter.**
  - `_tokens.scss` gains `--meter-height` (10 px), `--meter-track`, `--meter-fill`, `--meter-warning` and `--meter-critical`, in light and dark.
  - The shared `.meter` (`_charts.scss`) becomes the board's 10 px pill: primary fill, then gold-800 / gold-300, then coral-600 / coral-300.
  - At the limit, the meter gets a coral edge. The forced-colours rules are unchanged.
  - The `Meter` component's TS is unchanged. It is used by Plan & usage and the Design Lab only (the Overview draws its own attention items with the same state words).
- **Plan & usage (owner decision Q4).**
  - `plan-words.ts`: each server state maps to a chip class instead of a badge class: `chip-success` (tick), `chip-warning` (triangle), `chip-danger` (octagon), `chip-exhausted` (the solid coral "no entry" chip, `.chip-exhausted` added beside `.badge-exhausted`). Unlimited is `chip-info`.
  - `QuotaRow` raises the count. The figure sentence is cut around its first number, so "23 of 25", «23 من 25» and «قيد الاستخدام: 11» keep their own order and no message changed.
  - The page is `.page-calm` with a summary panel: the usage glyph tile, "Current plan", the name in title-1 and a **glance chip**. That chip shows the worst state of any capped limit, or "Unlimited". The panel also has "In effect since" as a fact tile and where the plan comes from.
  - The thresholds legend is four chips. The limits are soft tiles (2 columns from 48rem, 3 from 90rem), and the monthly allowance is one wide tile. The skeleton draws the summary and six tiles.
- **Billing.**
  - `billing-words.ts` maps each subscription status to a chip: awaiting payment hollow ring, trial "i", active tick, payment failed triangle, suspended octagon, ended plain.
  - `SubscriptionSummary` uses those chips. The plan name is title-1, the grace and cancellation callouts are soft tiles (gold tint for warnings), and the fallback and the dates are fact tiles.
  - With payments not set up (`isManaged: false`, the dev stack's case), the old large empty state becomes a panel: glyph tile, "Subscription", the **"Payments not set up"** chip, the two existing sentences and "In effect now" as a fact tile.
  - Offers are soft tiles with the price in title-2.
  - The limits comparison stays a table, without a frame of its own: quiet captions, the panel's line between rows, tabular figures.
  - The skeleton draws the subscription panel and six rows. Nothing else changed: checkout, portal, permissions and refusal words.
- **Workspace settings (Phase 38 note 8).**
  - New `zoneName(zone, language)` in `core/i18n/time-zone.ts`. It reads `Intl.DateTimeFormat`'s `longGeneric` name, then `long` where the generic one is only an offset (UTC). It returns null for an offset ("GMT+03:00"), the id itself or an unknown zone.
  - The time-zone panel now opens with fact tiles: "Time zone in use", with the localised name over the IANA id (left to right). For a manager whose read carries `scheduleCount` (BA-9), it adds "Report schedules in this zone" with the count, zero included.
  - While a choice is unsaved, "Selected zone: <name>" appears under the list.
  - A reader who cannot manage sees the same tiles, and `time-zone-text` keeps its test hook.
  - The confirmation, the count sentence, `If-Match`, the stale notices and the guards are unchanged. The toast still names the id, which the e2e journey checks.
  - The zone list (`dir="ltr"`) no longer has its arrow over "UTC" in Arabic. This was a pre-existing overlap, fixed in `_primitives.scss` for a select that declares `dir="ltr"`; only this select does.
- **Notifications.**
  - `NotificationsNav` is the segmented control's two links (`aria-current="page"`, new hooks `nav-all` / `nav-preferences`).
  - The history is one `.page-calm` panel: the summary ("43 unread of 44." in 600 plus the retention sentence), quiet day captions, and each notification a row tile with hover.
  - On the page only, unread is the dot, the word and the heavier title. The bell's 3 px bar curls round a rounded tile, so it is dropped here (a global rule; the shared item's TS is untouched).
  - On touch, the row's link ("Open Reports") is a 44 px target on this page. It is 28 px in the shared item.
  - The skeleton draws rows of an icon and two lines.
- **Email preferences (39.1 schema).**
  - `notification-api.ts` now has `EmailPreferencesResponse` (five non-null booleans, read) and `EmailPreferencesRequest` (five booleans, sent).
  - The page builds the request explicitly from the five controls (`request()`).
  - Each category is a soft tile with the family's switch (its 44 px row). The "In this browser" volume and test sound share one tile, and desktop alerts are another.
  - When the deployment cannot send email, an **"Email not set up"** "i" chip sits above the existing sentence.
  - The skeleton draws a tile per category.
  - The sound test, its focus landing (`#browser-heading`) and the desktop-alert permission flow (asked only inside the click) are unchanged.
- **Docs.** MASTER.md: eight decision-log rows, §13.6 (this step's contrast pairs), §14.6 (the components). `admin-design-system.md`: the meter tokens.

### 3.2 Screens

| Screen | What changed | Components and tokens it now uses | Tests |
| --- | --- | --- | --- |
| Plan & usage | Summary panel with glance chip; limits as soft tiles with chips, raised counts and pill meters; legend as chips; skeleton in shape | `.page-calm`, `.record-glyph`, `.facts`, `.chip-*` (+ `.chip-exhausted`), `app-meter`, `--meter-*`, `--radius-tile`, `--color-panel-soft`, `--text-title-1` | plan-usage-page.spec (+8: calm, glance worst/within/unlimited, raised count, meter state and value text, skeleton, legend chips; chip classes updated) |
| Billing | "Payments not set up" panel; status chips; soft callouts; offer tiles; calm limits table; skeleton | `.billing-state`, `.chip-info`, `.subscription-callout`, `.facts`, `--text-title-2` | billing-page.spec (+5: not-set-up panel, the six status chips, offer tile, skeleton, grace tile) |
| Workspace settings | Zone tiles (localised name + IANA id, schedule count); "Selected zone"; LTR select arrow; skeleton | `.facts`, `zoneName`, `--gap-hairline` | workspace-settings-page.spec (+7: name and id, count 3, count 0, no count, selected name, offset-only zone, skeleton); time-zone.spec (+4 `zoneName`) |
| Notifications | Segmented nav; one panel of row tiles; quiet captions; 44 px link on touch; skeleton | `app-segmented` links, `--radius-tile`, `--color-hover` | notifications-page.spec (+2: skeleton rows, segmented nav) |
| Email preferences | Category tiles with the family's switch; "Email not set up" chip; browser tiles; skeleton; split schema | `label[app-switch]`, `.prefs-state`, `.sound-tile`, `EmailPreferencesRequest/Response` | notification-preferences.spec (+5: chip, no chip, tiles, request of five booleans, skeleton) |
| Arabic | All of the above in Arabic | the catalogue | arabic-render.spec (+4: quota chip and raised count, unlimited sentence order, zone name in Arabic beside the id and the count, status chips and new strings) |

### 3.3 Screenshots

All under `C:\Users\mouay\Desktop\nabra-frontend-evidence\p39-6b\shots\`, named `<screen>-<width>-<lang>-<theme>-<before|after>.png`:

- **Before** (HEAD `83ebd79`, 52 files): `plan-usage`, `billing`, `workspace-settings`, `workspace-settings-zone` (Riyadh chosen, unsaved), `notifications`, `notification-preferences`, `notification-browser` (scrolled to "In this browser") × {1440, 390} × {en-light, ar-dark}. Also the restricted views `plan-usage-analyst`, `billing-analyst`, `workspace-settings-analyst`, `notification-preferences-analyst`, `plan-usage-support` and `workspace-settings-support`.
- **After** (90 files): the same 13 screens × {1440, 768, 390} × {en-light, ar-dark}.
- **After, states and flows:**
  - `workspace-settings-confirm-1440-{en-light,ar-dark}`: the confirmation with "1 scheduled report moves…".
  - `workspace-settings-stale-1440-en-light`, `notification-preferences-stale-1440-en-light`.
  - `lab-quotas-{1440,390}-{en-light,ar-dark}`: every meter state, from the Design Lab's labelled sample limits.
  - `{plan-usage,billing,workspace-settings,notification-preferences}-1280-en-light-{transparency-media,transparency-app,reduced-motion,forced-colors}-after.png`.

To compare: `plan-usage-1440-en-light-before/after`, `billing-1440-en-light-before/after`, `workspace-settings-zone-1440-ar-dark-*`, `notification-preferences-1440-ar-dark-*`.

## 4. Decisions

1. **The count is raised by cutting the sentence around its number, not by new messages.** "23 of 25", «23 من 25» and «قيد الاستخدام: 11» keep their translated order. A sentence without the number is shown whole. Rejected: separate "of {limit}" / "in use" messages, which would freeze an English word order into Arabic.
2. **Status chips (Q4):** Normal becomes the emerald tick ("Within the limit"); before, it was a neutral badge. Unlimited is the "i" chip. Limit reached keeps its stronger solid coral chip, because it is the one state that refuses something.
3. **A glance chip beside the plan's name.** It shows the worst state of any capped limit, in that state's word and glyph, or "Unlimited". It comes from the server's own states; nothing is re-derived and no word is new.
4. **Billing without payments is a state panel, not an empty state.** The reader learns the state ("Payments not set up"), why, and the plan in effect, which is the honest content of the page. The fallback plan appears only in the limits comparison, as before (the server sends it as a deployment setting).
5. **The limits comparison stays a table.** A plan beside a plan is a comparison of figures. It loses its own frame inside the panel instead of turning into tiles.
6. **The time zone's name never replaces its id.** The id is what is stored, what the report files print and what the confirmation, the toast and the e2e journey name. The localised name sits above it on the tile and in "Selected zone". The list itself stays ids: one LTR list that the "Find" box filters by city.
7. **The schedule count is shown on the page, zero included.** It is shown only when the read carries it (a manager). A member who cannot manage simply sees no count tile. Absent is never shown as zero.
8. **The unread bar is dropped on the history page only.** The page's rows are rounded tiles, and the spec's unread signal is the dot plus heavier weight (plus the word). The bell's panel keeps its bar until its own step. The shared item's TS was not touched, so the startup JavaScript did not move.
9. **Chips on a soft tile composite over the panel colour.** Measured: the dark "Within the limit" chip was 4.48:1 straight on the tile, and 4.95:1 with the change.
10. **Departures from the specification or the boards:** none. No board covers these screens except the Overview's "AI usage this month" tile, which the meter and the monthly tile follow (10 px pill, raised number). The 21st.dev MCP was not connected (authentication error), so everything was built from our own primitives.
11. **Not changed, seen in passing:** the notifications page's «تعليم الكل كمقروء» / «تعليم كمقروء» (the inbox chose «تحديد» in 38.2a). These are existing strings outside this step's redesign; see risk 4.

## 5. Verification

| Check | Result |
| --- | --- |
| `npm run format:check` | PASS: all files Prettier-clean (`gates\format-check.log`, final state) |
| `npm run test:ci` | PASS: 142 files, 2,170 tests, 0 failures (39.6a: 142 / 2,136; +34 tests). Run twice; the final run is on the final code. Free memory before: 9.2 GB (`gates\test-ci.log`) |
| `npm run build` | PASS: 0 warnings, every budget met (none changed). Initial total 338.17 kB raw / 76.79 kB transfer (39.6a: 336.92 / 76.51; the stylesheet grew 0.3 kB). **Startup JS 148,686 B gzip** (39.6a: 148,815; −129 B). Against the 38.0 baseline of 138,907 B that is +7.0%, under the 152,797 B ceiling. Free memory before: 9.3 GB (`gates\build.log`, `gates\startup-js.json`) |
| `npm run i18n:check` | PASS: 2,543 messages (+5), 0 errors, 0 warnings |
| `npm run e2e` | PASS: 46 journeys (23 × en-ltr and ar-rtl), 46 passed, 0 failed, in all three runs. Run 1 at 16:32 (3.0 min) and run 2 at 19:54 (3.1 min) are the two full runs, more than 15 minutes apart. **Run 3 at 20:05 broke the 15-minute rule:** I started it 11 minutes after run 2, as a full run, to cover the last CSS change (the LTR select arrow). The throttling journey passed in it too; it is reported, not counted as compliant. The canary passed before each run, and `NABRA_E2E_OUT` / `NABRA_E2E_STATE_DIR` were outside the repository (`p39-6b\e2e\run{1,2,3}.log`) |
| Backend gates | NOT RUN: nothing under `backend/` changed in this step |
| Chrome matrix | PASS. 208 loads: 13 screens (plan & usage, billing, settings, settings with a zone chosen, notifications, preferences, "In this browser", and the analyst's and support member's views) × 390/768/1280/1920 × en/ar × light/dark. The settings screens were re-run (48 loads) after the select fix. Page overflow 0, one `h1` everywhere, unnamed controls 0, axe (WCAG 2.2 AA, at 1280) 0 violations, at most 3 blurred layers, no blur on rows, console clean. After the fix, no target under 44 px on touch widths. The first pass flagged the 28 px "Open Reports" link at 390/768, fixed before the reported run (`chrome\matrix-after{1,1b,2,3}.json`) |
| Modes | PASS, 28 loads. Reduced transparency (media query and the in-app setting): 0 blurred layers. Reduced motion: nothing running at rest. Forced colours: system colours, tiles outlined. The chips' glyph masks do not draw in forced colours; their words do. This is the shared 39.2b chip, risk 2 (`chrome\modes-after.json`) |
| Keyboard | PASS, en and ar, at 1280. Every stop has a visible ring. Plan & usage: Refresh → Open billing. Settings: name → Save name → Find → zone list → Use this browser's zone → Save time zone. Notifications: Mark all as read → the two segments → each row's link and Mark as read. Preferences: the two segments → five switches (Space toggles) → volume → Play a test sound → desktop alerts (`chrome\flows.json` `keyboard`) |
| Flows (live) | PASS, en and ar. **Zone:** the tile reads "Coordinated Universal Time / UTC" and "Report schedules in this zone: 1". Choosing Riyadh shows "Selected zone: Arabian Standard Time" («التوقيت العربي الرسمي») and "Unsaved changes". Save asks with "1 scheduled report moves…" («ينتقل تقرير مجدول واحد…»), and Escape keeps UTC (re-read: `UTC`). Leaving asks the unsaved-changes question and stays. **Stale rename:** 412 → "This workspace was changed somewhere else", the typed name kept; the original name was put back (200). **Preferences:** "Unsaved changes", then save; the request body is the five booleans; a second tab's save gets 412 → "changed somewhere else… Nothing was saved" with Reload; leaving asks; every category back off (200). The only console lines are Chrome's own reports of the intended 412 responses |
| Owner and restricted members | PASS. Analyst: plan & usage (6 tiles), billing refused in words, settings read-only (the zone tile, no form), preferences without "In this browser". Support: plan & usage, billing and settings each explain the missing access; preferences with "In this browser". No request was refused, and no status code or permission name appears on any screen |
| Refusals by code | PASS (specs): billing and workspace refusals in the existing catalogues (unchanged), Arabic included; a stale preferences save is worded, not shown as a code. No new refusal code exists on these screens |
| Contrast | PASS: 74 measurements (37 pairs × light/dark). Text ≥ 4.5:1, graphics and edges ≥ 3:1. Lowest text: the dark "Within the limit" chip at 4.95 after decision 9 (it was 4.48 first). Lowest graphics: the light critical meter fill on its track at 4.01, and the switch edge on a soft tile at 3.41 / 3.27. MASTER §13.6, `chrome\contrast.json` |
| RTL | PASS: tiles, meters (fill from the inline start), chips and the segmented control mirror. Plan names keep `dir="auto"` and line up with the page (`text-align: match-parent`). The IANA id is `dir="ltr"` under the name, at the tile's start; the LTR zone list keeps its arrow at its own end |
| Touch targets | PASS: switch rows are the family's 44 px label targets, buttons are 44 px, the history row link is 44 px on touch; nothing under 44 px on touch widths in the matrix |
| Fonts | No font file added or changed. English pages fetch `geist-latin.v1.woff2` (29,400 B, preloaded). Arabic pages preload it with `ibm-plex-sans-arabic-arabic-400.v1.woff2` (42,848 B) and fetch 500/600 (45,296 / 45,688 B) for headings. CLS 0 on six of the eight checks and 0.002 on the preferences page in both languages (`chrome\fonts.json`) |
| Provider canary | PASS at the start, before each e2e run, and on resume (`gates\canary-*.txt`) |
| Secret scan | PASS: 755 evidence and source files, 0 with the demo password, 0 with an invitation link. `tmp\` (cookie jars, e2e state) deleted at the end |

- **Tools.** The Chrome checks ran through Playwright from outside the repository, driving the installed Google Chrome with temporary contexts. The scripts are in `p39-6b\scratch\`:
  - `ta.mjs`, sections signin/ids/shots/matrix/modes;
  - `flows.mjs`, sections flows/keyboard/lab;
  - `contrast.mjs`, which now also composites a flat gradient tint;
  - `fonts.mjs`, `startup-js.mjs`, `secret-scan.mjs`.

  The Chrome DevTools MCP was not used because it cannot write screenshots outside its workspace roots (the 39.5a trap). The 21st.dev MCP did not connect (authentication error).
- **Skills loaded:** `frontend-pro-max:enterprise-erp-ux`, `frontend-pro-max:ui-ux-design-lead`, `frontend-pro-max:angular-enterprise`.
- **Fixed during the checks:**
  - Billing's row headers broke the row lines (a flex `th`).
  - The dark success chip on a soft tile was 4.48:1.
  - The history link was 28 px on touch.
  - The IANA id sat at the far side of its tile in Arabic.
  - An English plan name started at the left of an Arabic page.
  - The zone list's arrow covered "UTC" in Arabic (this one predates the step).

## 6. See it live

The dev stack is running: the API with the AI stand-in on 127.0.0.1:5027, Mailpit on http://127.0.0.1:8026, and the dev server on http://localhost:4200. The accounts are in `omniai-redesign-evidence\p36-01\demo-accounts.md` (the passwords are only there):

- **Owner** `p36.owner@omniai.example` ("Rana Haddad");
- **Support agent** `p36.support@omniai.example`;
- **Analyst** `p36.analyst@omniai.example`.

What to try:

- **Plan & usage:** http://localhost:4200/plan-usage. The dev plan caps nothing, so every tile says "Unlimited" with the "i" chip, and the glance chip says the same. To see the meters in every state, open http://localhost:4200/design-lab and scroll to the sample limits (labelled sample figures).
- **Billing:** http://localhost:4200/billing. The "Payments not set up" panel, then the Development plan beside the Free fallback plan.
- **Workspace settings:** http://localhost:4200/workspace-settings.
  - The zone tile shows "Coordinated Universal Time" over "UTC", and "Report schedules in this zone: 1".
  - Type "riyadh" and pick Asia/Riyadh to see "Selected zone: Arabian Standard Time", then Save to see the question with the count. "Keep the current zone" leaves everything as it was.
  - Switch to Arabic for «التوقيت العالمي المنسق».
- **Notifications:** http://localhost:4200/notifications (the segmented control and the row tiles). Under **Email preferences**, turn a category on and leave the page to see the guard; open the page in two tabs and save both to see the stale notice. "In this browser" → **Play a test sound**.
- **As the Analyst:** plan & usage reads; billing explains the missing access; workspace settings are read-only with the zone tile. **As the Support agent:** plan & usage, billing and settings explain the missing access.

## 7. Backend asks

None. Every field these screens use exists and was checked live:

- `PlanUsageResponse`;
- `BillingResponse` with `currentPlan` / `fallbackPlan`;
- `TenantResponse.scheduleCount`;
- `EmailPreferencesRequest` / `EmailPreferencesResponse` / `NotificationPreferencesResponse`.

One honest limit, which is not an API gap: the dev stack has no capped plan in effect (every workspace is on "Development (unlimited)", 38.7b). The live pages therefore show no meter, and the meters are shown with the Design Lab's labelled samples and covered by specs.

## 8. New Arabic for my review

| Where | English | Arabic |
| --- | --- | --- |
| Billing, the subscription's chip when payments are not set up | Payments not set up | المدفوعات غير مُعدّة |
| Email preferences, the chip when the deployment cannot send email | Email not set up | البريد الإلكتروني غير مُعدّ |
| Workspace settings, zone tile term | Time zone in use | المنطقة الزمنية المستخدمة |
| Workspace settings, schedule count term (managers) | Report schedules in this zone | التقارير المجدولة وفق هذه المنطقة |
| Workspace settings, under the zone list while a choice is unsaved | Selected zone: | المنطقة المختارة: |

No existing Arabic string changed. The localised zone names («التوقيت العربي الرسمي», «التوقيت العالمي المنسق», …) come from the browser's own data (`Intl`), not from the catalogue.

## 9. Risks and open issues

1. **Run 3 of the e2e suite broke the 15-minute rule** (11 minutes after run 2; see §5). All 46 journeys passed, including the throttling one. Runs 1 and 2 are the compliant pair, and the only change after run 2 was the LTR select arrow (CSS).
2. **Chip glyphs in forced colours.** The shared chip's glyph masks (tick, triangle, octagon, "i") do not draw under Windows high contrast; the word and the outlined pill do. This is the 39.2b primitive on every screen, so it is left for 39.8b (a `forced-color-adjust` on the glyph).
3. **No live capped plan.** The meters are verified on Design Lab samples and in specs. A real "Approaching" state needs a finite current plan, which only a provider subscription or a different default plan gives (38.7b §4).
4. **Notifications' Arabic "mark read" wording** («تعليم كمقروء» / «تعليم الكل كمقروء») differs from the inbox's «تحديد» (38.2a). These are existing strings, unchanged here; one word for both is the owner's call.
5. **The bell's panel** still draws its unread bar and 28 px links; it belongs to the shell's next pass (39.8b), and the history page no longer does either.
6. **Live dev data:** the Northwind name was renamed for the stale-version check and restored to "Northwind Operations" (200). The owner's email preferences were turned on and back to all off. Nothing else was written.

## 10. Proposed checkpoint commit message

```text
feat: give plan and usage, billing, workspace settings and notifications Calm Glass (each limit a soft tile with its state as a chip with a word and a glyph, the count raised inside its own sentence and a 10 px pill meter in the three hues from new meter tokens, a chip beside the plan's name for how the limits stand, a "Payments not set up" panel with the plan in effect where the installation takes no payments, subscription states as chips and its callouts as soft tiles, the time zone named in the reader's language with its IANA id always under it and the count of report schedules that follow it, the chosen zone named before saving, the notification halves as a segmented control and the history as row tiles, each email category a soft tile with the family's switch and an honest "Email not set up", the email preferences read and saved with the 39.1 request and response types), keep a left-to-right zone list's arrow off its text in Arabic, and draw every skeleton in its loaded shape (Phase 39.6b)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```
