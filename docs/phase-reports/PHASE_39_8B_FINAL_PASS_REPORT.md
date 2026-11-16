# Phase 39.8b: the final pass and the Phase 39 summary

Step 39.8b of the approved plan (`PHASE_39_0_REDESIGN_PLAN_REPORT.md`, row 39.8b, sections 3.3
and 6), plus the owner's assistant's items for this part. Written 2026-11-16 on `960c874`.
Nothing is committed. Evidence is in `C:\Users\mouay\Desktop\nabra-frontend-evidence\p39-8b\`.

## 1. Summary

- Nothing looks different on purpose, except where the final pass found a defect. Every screen was
  checked in both languages, both themes, four widths, reduced transparency, reduced motion and
  forced colours, with screenshots beside 39.0's.
- In Windows high contrast, every chip keeps its dot or glyph beside its word.
- On phones and tablets: the skip link and the top bar's workspace line are 44 px targets; every
  bottom-bar name fits on one line from 320 px; the bell's action links are 44 px. In the bell
  panel, unread is the dot, the word and the heavier title.
- A keyboard user who reaches "Show earlier messages" now sees it and the messages it brings. It
  used to load a page and leave the focused button out of view. In Arabic, the inbox list no longer
  jumps when live updates connect.
- In Arabic, an on/off status says that something works («التحديثات المباشرة تعمل», «تعمل»,
  «يعمل»), not «مفعّلة», which reads like «مقفلة» at a small size.

## 2. Starting point

- Branch `master`, HEAD `960c874` (39.8a), worktree clean (`git status --short` empty), as the
  prompt's "Starting point" requires.
- The dev stack was running: API `omniai-p36-api` with the AI stand-in, Mailpit, and the dev server
  on :4200. The provider canary passed before any traffic and again before every e2e run
  (`CANARY PASS`).
- Skills loaded: `frontend-pro-max:first-impression-experience`, `frontend-pro-max:ui-ux-design-lead`,
  `frontend-pro-max:frontend-testing-quality` and `frontend-pro-max:web-performance`. The 21st.dev
  MCP ("magic") was not connected (not authenticated) and was not needed.
- Chrome checks used Playwright from outside the repository, driving the installed Google Chrome
  with temporary contexts, as 39.0–39.8a did. The owner's password was read into the process only.
  The cookie jar was kept in `p39-8b\tmp\`, deleted at the end.

## 3. Changes by area

`git status --short`: 84 modified, 4 new (88 files; the new ones are `PHASE_39_SUMMARY.md`, this report, `retired-names.spec.ts` and `channel-words.spec.ts`). Nothing under `backend/` or
`deploy/` changed.

### 3.1 The alias layer and the class twins (the plan's decision 1)

- **Moved every remaining use to the Calm Glass names.** There were 323 uses of 66 token names in
  34 files: 27 stylesheets (the four global layers and 23 components), the inline styles of 6
  components, and `index.html`. Each rewrite is
  exactly what the alias resolved to, read from the alias block itself
  (`p39-8b\scratch\e01_alias_rewrite.py`). One duplicated declaration this produced in
  `.bubble-ai` was removed.
- **Deleted the alias block** at the end of `_tokens.scss` and rewrote the file's header.
- **Retired the class twins too,** because their comments promised "39.8b removes them":
  - `.btn-ghost` became `.btn-quiet`;
  - `.badge` and `.badge-*` became `.chip` and `.chip-*`;
  - `--badge-glyph` became `--chip-glyph`.

  72 uses moved in templates and code, including the status-tone functions that return class names.
  The selectors were removed from `_primitives`, `_admin` and `_authoring`.
- **The gate:** a new `src/styles/retired-names.spec.ts` runs with `npm run test:ci`. It fails if:
  - any of the 66 retired token names appears anywhere under `src/`;
  - any retired class name appears;
  - any `var(--x)` without a fallback names a property nothing declares.

  A small test checks the guard itself. A comment may still mention an old name.
- **`.page-calm` stays a modifier.** Decision 2 explains why.
- Docs:
  - `MASTER.md` §3 (alias paragraph), §7 (motion names), rule 10 (the retired entry example),
    §14's intro, and five decisions-log rows;
  - `admin-design-system.md` (the token layer, buttons, chips).

### 3.2 Defects the final pass found, fixed (frontend only)

| # | Defect | Where | Fix |
| --- | --- | --- | --- |
| D1 | In forced colours a chip's dot or glyph took the pill's Canvas colour and vanished (39.6b risk 2; 39.7a risk 5) | every chip: voice, status, language, schedule state, limit reached | `.chip::before` opts out (`forced-color-adjust: none`) and is painted `CanvasText`; the draft ring keeps its inset ring. Verified on every screen: the modes pass counts chip markers that are blank or still forced (0) |
| D2 | Skip link 40 px (42 in Arabic) and the top bar's workspace line 24 px tall on touch (39.8a risk 3) | shell, 390 and 768 | Skip link `min-block-size: var(--target-min)`. The workspace trigger is 44 px and reaches 20 px up over the area's title, so the bar keeps its 56 px and the line does not move; its ring is drawn round the visible line. Every top-bar and bottom-bar control is now ≥ 44 px |
| D3 | The bell panel's action links were 28 px on touch, and the panel kept the Two Voices unread bar (39.6b dec. 8, risk 5) | notification bell | Unread is the dot, the word and the heavier title in the shared item. Its link is 44 px on coarse pointers and under 992 px. The history page's two overrides were deleted as redundant |
| D4 | The bottom bar broke "Knowledge", "Customers" and "Overview" mid-word at 320–390 px (39.5a risk 2; 39.5b, 39.6a) | phone bottom bar | No side padding on the items (each name gets the whole fifth). Under 360 px the columns share the bar by content, because "Inbox" and "More" have room to spare. `hyphens: auto` comes before `overflow-wrap: anywhere` as a fallback. Measured: one line for every name at 320, 360 and 390 in both languages; the narrowest column is 51 px |
| D5 | Arabic analytics loaded Plex Arabic 700 for the agents table's row headers (39.7a risk 6) | `.table` | `tbody th` is 600 |
| D6 | In the side bar's head the name ran under the search button's 44 px box (the head needed 196 px of 188), visible in forced colours as the button's edge touching "Nabra" | side bar, ≥ 1280 | The search and bell are the dense header's 40 px icon buttons (44 px on a coarse pointer, through `--control-height-sm`), as the specification allows for dense desktop headers. Clearance is now 9 px in English and 36 px in Arabic |
| D7 | On every width, Tab to "Show earlier messages" scrolled it into range and triggered the automatic read. The page that read brought in kept the old place, so the focused button sat 3,800–5,300 px above the view (WCAG 2.4.11) | conversation transcript | While focus is in the edge, the observer does not read (the button is the keyboard's way to ask). After a read the keyboard asked for, the button stays in view with the new messages under it. Live: focusing reads nothing, and Enter reads one page with the button still visible, in both languages at 390 and 1440 |
| D8 | The design lab's section links were 20–22 px tall, and one Arabic link 23 px wide (development only) | design lab | 24 px floor with a mouse and 44 px on touch, from a new token `--target-min-pointer` (24 px) |
| D9 | Lighthouse measured CLS 0.03 on the Arabic inbox (39.0: 0). While the connection starts, the long «التحديثات المباشرة متوقفة مؤقتًا» wrapped the title row; the short «… تعمل» unwrapped it, and the list moved up 24 px | inbox list header | Both wordings sit hidden (`visibility: hidden`, `aria-hidden`) in the same grid cell as the one shown, so the state is always as wide as its longer wording. The `role="status"` text still changes, so it is still announced. Arabic inbox CLS is now 0, and the header keeps one height (191 px) from first paint |

### 3.3 Arabic wording (owner's assistant)

On/off status labels now say «تعمل» / «يعمل»; settings switches and whole sentences keep «تفعيل» and
«مفعّلة». The six changes are in section 8. `GLOSSARY.md`'s "Active" row and its agreement note were
updated. The catalogue spec pins the new labels and fails if any status label (`*.status.active|on`,
`*.live.on`, `schedules.on`, `websiteChat.agentNotActive`) contains «مفع».

Left as they are, on purpose:
- `notificationPreferences.someOn` («الفئات المفعّلة»), the settings switches' own summary;
- the sentences `websiteChat.detail.agentNotActive`, `websiteChat.create.lede` and
  `notificationPreferences.inAppNote`.

### 3.4 Specs

| Spec | Covers |
| --- | --- |
| `src/styles/retired-names.spec.ts` (new) | the retired token and class names; undeclared `var()` without a fallback; the guard itself |
| `src/locale/i18n-catalogue.spec.ts` | the on/off status wording in Arabic |
| `shared/chip/chip.spec.ts` | the forced-colours marker rule, and that it is last of the chip rules |
| `layout/topbar/topbar.spec.ts` | 44 px workspace line (reaching over the title) and skip link |
| `layout/bottom-bar/bottom-bar.spec.ts` | names given the whole column, hyphenation before `anywhere`, columns by content under 360 px |
| `layout/notification-bell/notification-bell.spec.ts` | unread as the dot (not a bar) in the panel; the 44 px touch link |
| `features/conversations/conversation-transcript/conversation-transcript.spec.ts` | focus on the button reads nothing; the asked-for page keeps the focused button in view |
| `features/conversations/conversation-list/conversation-list.spec.ts` | the live state holds both wordings hidden and says only the one that holds |
| `features/reports/reports-words.spec.ts` | report status tones are chip classes |
| `features/channels/channel-words.spec.ts` (new) | channel status words and chip classes |

jsdom lays nothing out, so the size and forced-colours rules are read from the stylesheets.
The Chrome pass measures them live.

### 3.5 Screens

| Screen | What changed in 39.8b | Components and tokens it now uses | Tests |
| --- | --- | --- | --- |
| Every screen | Calm Glass names only (alias layer gone); chips' markers in forced colours | `--color-*`, `--status-*`, `--voice-*`, `--chip-*`, `.chip-*`, `.btn-quiet` | `retired-names.spec`, chip spec, matrix |
| Shell: top bar, skip link | 44 px workspace line and skip link | `--target-min` | topbar spec, `bars` probe |
| Shell: bottom bar | names on one line from 320 px | `grid-auto-columns: auto` < 360 px | bottom-bar spec, `barlabels` probe |
| Shell: side bar head | 40 px tools on a fine pointer | `--control-height-sm` | `sidehead` probe, matrix |
| Notification bell panel | unread without the bar; 44 px touch link | `--target-min` | bell spec |
| Inbox (live status) | «التحديثات المباشرة تعمل»; the state as wide as its longer wording (no jump when it connects) | `.live-words` grid with hidden sizers | catalogue spec, conversation-list spec, `shift-probe` |
| Conversation | the focused "Show earlier messages" stays in view; no read on focus | — | transcript spec, `older-probe2` |
| Analytics | row headers 600 | `.table tbody th` | matrix |
| AI agents, channels, website chat, schedules | Arabic «يعمل» / «تعمل» / «الوكيل لا يعمل» | `app-chip`, `.chip-*` | catalogue spec, channel-words spec |
| Notification history | its two overrides moved into the shared item | — | bell spec |
| Design lab | 24 px (44 on touch) section links | `--target-min-pointer` (new) | matrix |

### 3.6 Screenshots

- **After, named and sized like 39.0's before:** 168 PNG files in `p39-8b\shots\`,
  `<screen>-<1440|390>-<en-light|ar-dark>-after.png`. That is 41 screens and the widget (closed and
  open on its host pages), full page. The before files are in `p39-0\shots\`, `…-before.png`.
- **Contact sheets, one per area, before beside after:** `p39-8b\contact\<area>.png` (and `.html`).
  The areas are entry, inbox, customers, knowledge, agents, channels, visitor, insights, team,
  workspace and design-lab.
- **Modes:** `p39-8b\shots\modes\` holds every screen in reduced transparency, reduced motion and
  forced colours (`<screen>-<1440-en-light|390-ar-dark>-<mode>-after.png`, full page for forced
  colours). It also has the focus states (`focus-*.png`), the bars (`bars-skip-link-*`,
  `bottom-bar-*`), the side bar's head (`sidebar-head-*`) and the visitor page under the system's
  reduced transparency.

## 4. Decisions

1. **The class twins went with the token aliases.** `.btn-ghost` and `.badge-*` were exact twins
   whose comments said 39.8b removes them. Keeping them would have left two names for one rule, so
   the gate covers them too.
2. **`.page-calm` stays a modifier.** Its comment said 39.8b makes it the default. Every list, form
   and settings page already carries it. The screens without it are the inbox thread (`.card` in the
   context panel), the entry pages and the design lab, and each was redesigned on its own terms in
   39.4b, 39.8a and 39.2b. A global rule would restyle their cards, rows and icons, which is new
   design work rather than a final pass. The comment and the decisions log say so.
3. **Forced colours: opt the marker out, not the chip.** The pill keeps the browser's system colours
   (Canvas, CanvasText, the outline). Only `::before` is painted `CanvasText`, so a chip follows any
   high-contrast theme.
4. **The workspace line reaches up over the title** instead of growing the 56 px top bar. A taller
   bar would cost every phone screen 20 px of content. Tapping the area's title now opens the
   workspaces too; the title is not interactive on its own.
5. **The side bar's tools are 40 px on a fine pointer.** The specification allows 40 px in dense
   desktop headers, and the token makes them 44 on a coarse pointer. This frees the 8 px the name
   was missing. Rejected: a narrower mark or name, which the brand fixes.
6. **Bottom bar under 360 px: columns by content,** not new short strings. The plan said a fix
   needs "short labels or a type change". Sharing the free space fits every name with no new copy
   and no smaller type (the caption stays 12 px, 13 px in Arabic). `hyphens: auto` was tried first,
   but the headless Chrome here has no English hyphenation patterns, so it stays only as a fallback.
7. **"Show earlier messages" with keyboard focus** (D7) reads only when asked, and keeps the button
   in view. Scrolling still reads automatically, and a reader scrolling with a mouse keeps their
   place as before.
8. **Arabic wording.** Labels change; settings switches, the switches' summary and whole sentences
   do not (the owner's assistant's rule). «الوكيل لا يعمل» was chosen for "Agent not active" over
   «الوكيل متوقف», because a Draft agent was never started.
9. **A 24 px token.** `--target-min-pointer` was added rather than writing `1.5rem` in the design
   lab (rule 8).
10. **Measurement corrections, not product changes.** The first matrix flagged the choice family's
    20 px boxes; the tool now measures the label, which is the 44 px target. The first keyboard pass
    flagged three cases, and the tool now waits 250 ms and accepts a ring on a focused wrapper or on
    the visible line. Screenshots confirm each case: the skip link mid-slide, the workspace line,
    and the composer's field ring.
11. **Spec departure (plan section 6 vs this prompt):** none.

Deferred: nothing of 39.8b's scope. Open items for later phases are in `PHASE_39_SUMMARY.md` §5–6.

## 5. Verification

| Check | Result |
| --- | --- |
| `npm run format:check` | **PASS**: all files use Prettier style (`gates\final3\format-check.log`, the last run after the last change) |
| `npm run test:ci` | **PASS**: 147 files, 2,236 tests, 0 failures (39.8a: 145 / 2,220). The first run of the step failed 1 test, a filter in my own new catalogue test that also caught a sentence; the filter was fixed, not the product (`gates\test-ci-run1-failed.log`) |
| `npm run build` | **PASS**: 0 warnings, budgets pass. Initial total 337.62 kB raw / 76.30 kB transfer. **Startup JS 149,268 B gzip**, 31 files, 470,357 raw, 131,553 brotli: 39.8a 149,213 (+55 B); 38.0 baseline 138,907 (+7.5%); ceiling 152,797 (`gates\final3\`) |
| `npm run i18n:check` | **PASS**: 2,557 messages, 0 errors, 0 warnings |
| `npm run e2e` | **PASS**: 46 journeys (23 × en-ltr and ar-rtl). Seven full runs, every one 46/46, none retried; the code changed under the first four, so the counted pair is the last two, on the final code: **run C 06:00:06–06:02:37 (2.5 min) and run D 06:18:02–06:20:57 (2.9 min), 15 min 25 s apart**. Earlier, in order: run 0 03:37 (before the bottom-bar, bell and table fixes), run 1 03:47:31 (**6 min 53 s after run 0: a breach of the 15-minute rule**, reported; it passed, the throttling journey included), run 2 04:32:07, runs A 05:05:27 and B 05:34:20 (after the side bar, transcript and design lab fixes, before the live-state fix). No run was limited with `--grep-invert` (`e2e\run0…run6\`) |
| Backend gates | **NOT RUN**: nothing under `backend/` changed in 39.8b. Last run: 39.8a, 4,420 passed / 3 skipped |
| Chrome matrix | **PASS**, final build: 41 screens × en/ar × light/dark × 390/768/1280/1920 = **656 loads, axe (WCAG 2.2 AA tags) on 328** (390 and 1280): page overflow 0, one `h1` on every page, unnamed controls 0, axe violations 0, `dir` right, console clean, at most 3 blurred layers, no target under 24 px, every bar control ≥ 44 px on touch widths (`chrome\results-final.json`). After the last fix (the inbox's live state) the three screens with the inbox list were run again: 48 loads, 24 axe, 0 problems. The first full pass, on an earlier build, flagged 80 loads: 64 were the choice family's 20 px boxes measured without their 44 px labels (the tool was corrected) and 16 were the design lab's short links (D8, fixed) |
| Reduced transparency, reduced motion, forced colours | **PASS**: every screen in each mode, en-light 1440 and ar-dark 390 (246 loads), screenshots in `shots\modes\`. Forced colours: 0 chip markers blank or still forced. Four flags, all by design and checked: (1) the hosted chat keeps its 2 glass layers under the *in-app* setting, because the visitor's page follows the visitor's own system setting (`index.html`); with `prefers-reduced-transparency: reduce` emulated it has 0 blurred layers in both languages at 1440 and 390 (`visitorglass`); (2) the design lab's spinner and thinking orb keep moving under reduced motion, the documented exceptions (`_primitives.scss` spinner note; MASTER §2 item 5). Inbox screens re-run on the final build: 18 loads, 0 problems |
| Keyboard paths and focus | **PASS** after one fix: Tab through every screen, en 1440 and ar 390, up to 16 stops each (82 paths, 986 stops): every stop has a visible ring and is on screen. The pass found D7 ("Show earlier messages" focused out of view), fixed. Focus states seen in `shots\modes\focus-*.png` (the skip link, the workspace line's ring, the composer's field ring, the earlier-messages button) |
| Lighthouse 12.8.2 and axe | **PASS**: Lighthouse **12.8.2**, 39.0's exact protocol (desktop preset, the mobile preset for the public page, performance and accessibility, one temporary profile with the owner's session, storage not reset), served with the edge's cache rule. **Accessibility 100 on all 8 runs; performance 100 on 7, 99 on the English mobile public page (TBT 116 ms in a single run).** One weight-0 audit fails on the inbox and the overview: `label-content-name-mismatch` on the bell, whose badge shows "99+" while its name says "105 unread" (the count passed 99 in this data; the badge is `aria-hidden`; it is not in WCAG tags and did not fail in 39.0, when the count was under 100). Table in `PHASE_39_SUMMARY.md` §3. axe: 0 violations on 352 page loads (the matrix) |
| Lab vitals | **MEASURED** (5 cold contexts, medians, 1440): LCP inbox 356 / 472 ms (en / ar), overview 388 / 456, public page 248 / 240; CLS ≤ 0.001; INP 80 / 64, 48 / 56, 32 / 32. 39.0's numbers were dominated by its server's 0.8–0.9 s TTFB (now 3–7 ms), so LCP minus TTFB is the comparison: +38 to +135 ms, most on Arabic pages (summary §3) |
| Fonts | **PASS**: files per language: Geist Latin 29,400 B on every page; IBM Plex Sans Arabic 400 42,848, 500 45,296, 600 45,688; Instrument Serif Latin 21,227 (the display line). Preloaded from the pre-paint script: Geist Latin always; Arabic 400 on Arabic pages and on English pages under `/conversations` (low priority). Arabic font timing, 39.0's protocol (150 ms, 200 KB/s, cache off): requested at 190–195 ms in every case (39.0: 4,274 on an English page with Arabic text, 1,478 on the Arabic inbox), done at about 1.05 s, **fallback shown 0 ms** (39.0: 796 ms on the English page). First-paint font bytes: 117,936 (English page with Arabic), 163,232 (Arabic inbox, with 500 and 600 later), 72,248 (Arabic public page). Layout shift on swap: none measured (CLS 0 on the Arabic inbox after the live-state fix; ≤ 0.01 elsewhere) |
| Contrast | No new colour pair: every change uses existing roles. The forced-colours marker is `CanvasText` on `Canvas`, the system's own pair |
| RTL mirroring | The workspace line's reach, the bottom bar's columns and the side bar head measured in Arabic (clearance 36 px). The transcript fix is direction-free |
| Touch targets | Every top-bar and bottom-bar control ≥ 44 px at 390 and 768 in both languages (`bars`); no target under 24 px on any screen (matrix) |
| Provider canary | **PASS** before the first traffic and before each e2e run: every listed secret and `OMNIAI_LIVE_*` absent, `Email__Smtp__Host` loopback, `OMNIAI_DEV_AI_STANDIN` listed as info |

## 6. See it live

The dev stack is running:
- API `http://127.0.0.1:5027` (stand-in on);
- dev server `http://localhost:4200`;
- Mailpit `http://127.0.0.1:8026`.

The demo accounts are in `omniai-redesign-evidence\p36-01\demo-accounts.md` (the password is in
that file): Owner `p36.owner@omniai.example`, Support agent `p36.support@omniai.example`, Analyst
`p36.analyst@omniai.example`.

- **Forced colours:** on Windows, turn on a contrast theme (Settings > Accessibility > Contrast
  themes). Or in Chrome DevTools, open Rendering and set "Emulate CSS forced-colors: active". Then
  open `/plan-usage`, `/reports/schedules` and `/conversations`: every chip keeps its dot or glyph.
- **Phone (390 px, or 320):** open `/conversations`, press Tab once for the skip link, then tap the
  workspace line under "Inbox". Every bottom-bar name is on one line.
- **The keyboard in a long conversation:**
  1. Open `/conversations/01a15013-917d-75a7-870c-98ceb0b9e7f8`.
  2. Click into the reply field, then press Shift+Tab: "Show earlier messages" is in view and
     nothing loads.
  3. Press Enter: the earlier messages appear under it, and it stays focused and visible.
- **Arabic:** switch to العربية.
  - The inbox header says «التحديثات المباشرة تعمل», and the list does not jump when it connects.
  - `/reports/schedules`: a schedule that is on says «تعمل».
  - `/ai-agents` and `/channels/website-chat`: «يعمل» / «تعمل».
- **The bell:** open it on a phone width. Unread rows show the dot and the word, with no bar, and
  "Open …" links are 44 px.
- **Analytics in Arabic:** agent names in the table are 600.
- **Before and after:** `nabra-frontend-evidence\p39-8b\contact\*.png`.

## 7. Backend asks

None new. The open asks from Phase 39 are listed in `PHASE_39_SUMMARY.md` §5.1.

## 8. New Arabic for my review

| Arabic now | Was | English | Where |
| --- | --- | --- | --- |
| «التحديثات المباشرة تعمل» | «التحديثات المباشرة مفعّلة» | Live updates on | the inbox's live status (`inbox.live.on`); «التحديثات المباشرة متوقفة مؤقتًا» unchanged |
| «تعمل» | «مفعّلة» | On | a report schedule's state chip (`schedules.on`) |
| «يعمل» | «مفعّل» | Active | an AI agent's status chip (`agents.status.active`) |
| «تعمل» | «مفعّلة» | Active | a website chat link's or WhatsApp number's status chip (`channels.status.active`) |
| «مراحل القناة: مسودة، ثم تعمل، ويمكن تعطيلها ثم تشغيلها من جديد» | «… ثم مفعّلة، ويمكن تعطيلها ثم تفعيلها من جديد» | Lifecycle: Draft, then Active, which can be Disabled and made Active again | the accessible name of a channel's lifecycle steps (`channels.lifecycle.label`) |
| «الوكيل لا يعمل» | «الوكيل غير مفعّل» | Agent not active | the warning chip on a website chat link whose agent is not active (`websiteChat.agentNotActive`) |

The glossary note now reads: an on/off status label says that something works («تعمل» / «يعمل»,
off «متوقفة» or «معطّل»), never «مفعّل».

`PHASE_39_SUMMARY.md` appendix A lists every Arabic string Phase 39 added (182) or changed (12),
for the native review together with 38.8's 61.

## 9. Risks and open issues

1. **The e2e spacing rule broke once.** Counted run 1 of the first pair started 6 min 53 s after
   the preliminary run ended. Every journey passed, the throttling one included. Both pairs are
   reported in section 5, and the counted pair on the final code is runs C and D, 15 min 25 s apart, both 46/46.
2. **Tapping the top bar's area title now opens the workspace menu** (decision 4). This is a small
   change in behaviour on phones; the title was not interactive before.
3. **Hyphenation depends on the browser.** At 320 px no name needs it now. It stays as a fallback for
   enlarged text, and where the browser has no patterns, `overflow-wrap: anywhere` still breaks
   rather than cutting a name.
4. **Lab numbers come from a loopback HTTP/1.1 server,** as in 39.0, so they compare like with like
   but are not production numbers. Re-measure on the deployed edge.
5. **Dev data:** 39.8b wrote nothing to the API. Reading an earlier page in the long conversation
   changes no record. The synthetic data from earlier steps is listed in `PHASE_39_SUMMARY.md` §6.
6. **A process note:** a read-only research helper I started wrote one temporary file, `/tmp/x`,
   which holds only testid strings from a git diff. It is outside the repository and the evidence
   folder, and was not deleted (deleting is outside this step's rules).

## 10. Proposed checkpoint commit message

```
feat: finish the Calm Glass redesign (delete the deprecated alias layer and the .btn-ghost and .badge-* twins, moving the last 323 token uses and 72 class uses to their Calm Glass names, with a spec that fails on any retired name or an undeclared var() without a fallback; keep every chip's dot or glyph in forced colours; give the skip link and the phone and tablet top bar's workspace line 44 px targets, the bell panel's links 44 px on touch and its unread the dot, the word and the heavier title; fit every bottom-bar name on one line from 320 px; keep the side bar's name clear of its tools with dense 40 px buttons on a fine pointer; keep a keyboard reader's "Show earlier messages" in view and read only when asked; keep the inbox's live state as wide as its longer wording so the Arabic list no longer jumps when it connects; set table row headers at 600; say «تعمل» / «يعمل» for an on/off status in Arabic; run every screen in both languages, both themes, four widths, reduced transparency, reduced motion and forced colours with after screenshots and contact sheets beside 39.0's, Lighthouse 12.8.2 and axe against 39.0's baselines, the startup JavaScript and the Arabic font timing; and write the Phase 39 summary) (Phase 39.8b)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```
