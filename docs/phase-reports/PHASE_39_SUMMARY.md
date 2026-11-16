# Phase 39 summary: Calm Glass

Phase 39 changed how Nabra looks and is laid out, not what it does. It replaced the "Two Voices"
design with **Calm Glass**: one emerald tone, the AI in emerald, the team in gold, the customer in
plain cream, and glass only where things float. It shipped as fifteen steps, 39.0 to 39.8b, each
reported and accepted before the next one started. Every route, contract, permission rule and
`data-testid` the end-to-end suite uses was kept. The approved plan is
`PHASE_39_0_REDESIGN_PLAN_REPORT.md` (committed `e79b5e6`, with the owner's decisions in its
section 12). This summary is part of 39.8b.

## 1. What changed for the people who use Nabra

- **Everywhere:** new colours and type, self-hosted with a per-language preload: Geist for Latin,
  IBM Plex Sans Arabic for Arabic, and Instrument Serif for one display line. Dark is designed on
  its own. "Reduce transparency" is in the account menu. The glass falls back to a solid twin under
  that setting, under the system's reduced transparency and in forced colours. 436 colour pairs are
  measured against WCAG 2.2 AA.
- **Controls:** every checkbox, radio, switch and segmented control is one family, with a 44 px
  target and a visible box. Buttons are emerald pills. Chips always carry a word and a dot or glyph,
  so colour never carries meaning alone.
- **The shell:** a floating glass side bar with four sections, the inbox's count and the member.
  On narrow desktops it becomes a named rail. Phones get a glass top bar and bottom bar with "More".
  There is a workspace switcher. Toasts are placed for each width.
- **The inbox:** three panes; All, Needs a person and Mine; a quiet unread treatment (a dot and a
  heavier name); "Mark all as read", which names the count first. The open conversation speaks in
  three voices, with a breathing orb while the AI answers. The header shows who has the
  conversation, by display name, and the phone header is one line.
- **Members have display names,** shown on bubbles, takeovers, assignees, the team page and the
  overview greeting. Members can change their own name.
- **The visitor's chat** (the widget and the hosted page) is Calm Glass, with a greeting in each
  language.
- **Knowledge, agents, channels, customers, the team, roles, plan, billing, settings and
  notifications** are calm panels of rounded rows. States are chips with words. Time zones are
  named in the reader's language, with the IANA id beside the name.
- **The overview** is a bento of real data only. **Analytics** draws the period being read in
  emerald. **Report files** download through the app, so a refusal is worded in the reader's
  language. Report PDFs use the brand's type, with tables sized by their content.
- **The entry pages** (sign in, create an account, password reset, invitations, workspace chooser)
  use a calm split layout with the two-circle mark. The favicon, app icons, web manifest, every
  email and the report footer carry the mark.

## 2. The steps and their commits

| Step | Commit | Date | What it did | Report |
| --- | --- | --- | --- | --- |
| 39.0 plan | `e79b5e6` | 2026-11-13 | The Calm Glass plan: inventory, tokens, the 14-session split, baselines (startup JS, Lighthouse, Arabic font timing, axe, 172 before screenshots); owner decisions Q1–Q8 | `PHASE_39_0_REDESIGN_PLAN_REPORT.md` |
| 39.1 backend | `9bbd4a3` | 2026-11-14 | Member display name (`PUT /api/me/display-name`), greetings per language and `?lang`, mark all read and unread count with `conversationsRead`, `filter=mine`, masked `reference` on customers, the email-preferences request/response split, "Answered by the AI" (BA39-1); 2 migrations | `PHASE_39_1_REDESIGN_BACKEND_REPORT.md` (Rev 1) |
| 39.2a tokens and type | `71f8e18` | 2026-11-14 | Calm Glass tokens with the deprecated alias layer, light and dark, Reduce transparency, forced colours, motion; self-hosted fonts with per-language preload; 436 contrast pairs | `PHASE_39_2A_TOKENS_TYPE_REPORT.md` |
| 39.2b components | `d2521aa` | 2026-11-14 | Choice-control family (44 px), buttons, inputs, chips, bubbles, menus, dialogs, toasts; the two-circle mark and outlined lockup; the design lab rewritten | `PHASE_39_2B_COMPONENTS_REPORT.md` |
| 39.3 shell | `99e0392` | 2026-11-14 | Glass side bar, rail, phone top and bottom bars, workspace switcher, notification-panel focus trap, toasts per width, one-line phone conversation header | `PHASE_39_3_SHELL_REPORT.md` |
| 39.4a inbox list | `cf7b109` | 2026-11-14 | Solid panels, All · Needs a person · Mine, avatars and two chips at most, quiet unread, Mark all as read, short «المحادثات» on the bottom bar | `PHASE_39_4A_INBOX_LIST_REPORT.md` |
| 39.4b inbox thread | `0a125de` | 2026-11-14 | Thread and customer panel, three voices, takeover/return/failed lines, waiting orb, "Handled by" and the Assign menu, one-field composer, the customer's history | `PHASE_39_4B_INBOX_THREAD_REPORT.md` |
| 39.5a knowledge and agents | `8fe4dd4` | 2026-11-15 | Calm panels of rounded rows, document kinds, agents' orbs, chips with words, the transcript's three voices, note 17 re-read | `PHASE_39_5A_KNOWLEDGE_AGENTS_REPORT.md` |
| 39.5b channels and widget | `f0d6da7` | 2026-11-15 | Widget and hosted page in Calm Glass (glass header and composer), greetings per language, accent rules, corner radio cards | `PHASE_39_5B_CHANNELS_WIDGET_REPORT.md` |
| 39.6a customers and team | `83ebd79` | 2026-11-15 | Customer rows and summary, one merge code, members by display name, the display-name dialog, «الأصوات تعمل» / «الأصوات متوقفة» | `PHASE_39_6A_CUSTOMERS_TEAM_REPORT.md` |
| 39.6b plan, billing, settings | `4a6085f` | 2026-11-15 | Quota tiles with chips and meters, "Payments not set up", localised time-zone names, notification tiles and the email-preferences types | `PHASE_39_6B_PLAN_BILLING_SETTINGS_REPORT.md` |
| 39.7a overview, analytics, reports | `3e5f5a5` | 2026-11-15 | The bento overview from real data, calm analytics, client report downloads with refusals worded by code, chips for languages and schedule states | `PHASE_39_7A_OVERVIEW_ANALYTICS_REPORTS_REPORT.md` (Rev 1) |
| 39.7b report files | `090c70a` | 2026-11-16 | PDFs in Geist and Plex Arabic (OFL beside them), tabular figures, tables sized by content, wrapping headings, aligned numeric headings | `PHASE_39_7B_REPORT_FILES_REPORT.md` (Review) |
| 39.8a entry and brand | `960c874` | 2026-11-16 | Entry pages as a calm split, not-found mark, v2 favicon, app icons and manifest, emails with the mark by Content-ID, the PDF footer mark, provider names in report files | `PHASE_39_8A_ENTRY_BRAND_REPORT.md` |
| 39.8b final pass | not committed yet | 2026-11-16 | Deleted the alias layer and the class twins, with a gate; chip markers in forced colours; 44 px skip link and top-bar workspace line; the bell panel's unread and touch link; the bottom bar's names on one line from 320 px; the side bar's name clear of its tools; a keyboard reader's "Show earlier messages" kept in view; the inbox's live state no longer moves the Arabic list; row headers at 600; Arabic on/off status wording; the full matrix, Lighthouse, axe, fonts and e2e twice; this summary | `PHASE_39_8B_FINAL_PASS_REPORT.md` |

## 3. Before and after

"Before" is the 39.0 baseline on `f42a5c1` (38.8), measured on 2026-11-13. "After" is 39.8b's
working tree on `960c874`, measured on 2026-11-16 with the same tools and protocol. Both were
measured on the production build, served by the same loopback static server on :4300.

| Measure | Before (39.0) | After (39.8b) |
| --- | --- | --- |
| Startup JavaScript (loader + application chunk, gzip) | 141,423 B (38.0 baseline 138,907; ceiling 152,797) | **149,268 B** (+7.5% over 38.0, inside the 10% ceiling) |
| Initial total (`ng build`) | 313.77 kB raw / 73.71 kB transfer | 337.62 kB raw / 76.30 kB transfer |
| Unit and component tests | 124 files / 1,874 tests | **147 files / 2,236 tests** |
| Messages (`i18n:check`) | 2,448 | **2,557**, 0 errors, 0 warnings |
| Backend tests | 4,257 (38.7a) → 4,288 (38.7b) | 4,420 passed / 3 skipped (39.8a; 39.8b changed nothing under `backend/`) |
| End-to-end | 46/46 in 2.2 min | 46/46 in every one of seven full runs; the counted pair on the final code, runs C and D, 15 min 25 s apart (2.5 and 2.9 min) |
| Contrast pairs measured (MASTER §13) | n/a (Two Voices table) | 436 (39.2a) plus every pair added by later steps; every pair passes |
| Screens in the matrix | 41 × 2 widths × 2 cells (164 loads) | 41 × 4 widths × 2 languages × 2 themes = 656 loads (axe on 328), 0 problems; plus reduced transparency, reduced motion and forced colours (246 loads) and keyboard paths (82) |

**Lighthouse 12.8.2** (desktop preset, plus mobile for the public chat page, performance and
accessibility, the same profile protocol as 39.0):

| Page | Lang | Perf | A11y | LCP | FCP | CLS | TBT | Transfer |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Inbox (desktop) | en | 100 → 100 | 100 → 100 | 644 → 606 ms | 229 → 194 ms | 0 → 0 | 0 → 0 ms | 139 → 135 KB (cold) |
| Overview (desktop) | en | 100 → 100 | 100 → 100 | 495 → 357 ms | 190 → 192 ms | 0 → 0.01 | 0 → 0 ms | 36 → 23 KB |
| Public chat (desktop) | en | 100 → 100 | 100 → 100 | 394 → 429 ms | 181 → 200 ms | 0 → 0 | 0 → 0 ms | 25 → 24 KB |
| Public chat (mobile) | en | 100 → **99** | 100 → 100 | 1,104 → 1,181 ms | 636 → 651 ms | 0 → 0 | 50 → 116 ms | 13 → 12 KB |
| Inbox (desktop) | ar | 100 → 100 | 100 → 100 | 614 → 524 ms | 244 → 253 ms | 0 → 0 | 0 → 0 ms | 132 → 123 KB (cold) |
| Overview (desktop) | ar | 100 → 100 | 100 → 100 | 469 → 473 ms | 183 → 196 ms | 0 → 0.001 | 1 → 55 ms | 38 → 23 KB |
| Public chat (desktop) | ar | 100 → 100 | 100 → 100 | 367 → 357 ms | 194 → 192 ms | 0 → 0 | 0 → 0 ms | 15 → 12 KB |
| Public chat (mobile) | ar | 100 → 100 | 100 → 100 | 1,097 → 1,099 ms | 739 → 644 ms | 0 → 0 | 59 → 38 ms | 15 → 12 KB |

Each cell is 39.0 → 39.8b, one run each as in 39.0. The 39.8b server used the production edge's cache rule
(every `.woff2`, `.svg`, `.png`, `.js` and `.css` immutable, `deploy/edge/templates/omniai.conf.template`),
because since 39.2a the fonts are versioned, unhashed `/fonts/*.v1.woff2` files that 39.0's loopback server
would serve `no-cache`. A run with that loopback rule (fonts re-downloaded on every page) is kept in
`p39-8b\lighthouse-loopback\`: accessibility 100 everywhere, performance 97–100, the mobile public page's LCP
1.8 s (en) and 2.4 s (ar), with 76–169 KB transferred. The only failing audit is weight-0
`label-content-name-mismatch` on the bell (see the 39.8b report, section 5); axe found 0 violations on all 352
matrix loads.

**Lab vitals** (installed Chrome, 1440×900, five cold contexts, medians):

| Page | Lang | LCP 39.0 → 39.8b | LCP minus TTFB | CLS | INP | LCP element (39.8b) |
| --- | --- | --- | --- | --- | --- | --- |
| Inbox | en | 1,176 → 356 ms | 290 → 352 ms | 0 → 0 | 64 → 80 ms | the placeholder's sentence |
| Inbox | ar | 1,140 → 472 ms | 331 → 466 ms | 0 → 0 | 64 → 64 ms | the placeholder's sentence |
| Overview | en | 1,216 → 388 ms | 342 → 385 ms | 0.005 → 0 | 48 → 48 ms | the greeting (`h1`) |
| Overview | ar | 1,200 → 456 ms | 319 → 453 ms | 0.005 → 0.001 | 56 → 56 ms | the greeting (`h1`) |
| Public chat | en | 1,084 → 248 ms | 206 → 244 ms | 0 → 0 | 32 → 32 ms | the greeting |
| Public chat | ar | 1,116 → 240 ms | 221 → 236 ms | 0 → 0 | 32 → 32 ms | the greeting |

39.0's TTFB on its loopback server was 0.8–0.9 s; 39.8b's was 3–7 ms. So the LCP column mostly measures the
server, and "LCP minus TTFB" is the like-for-like comparison: Calm Glass paints its largest element 38–135 ms
later, the most on Arabic pages (the Arabic faces and the heavier first view), still far inside 2.5 s.

**When the Arabic font renders** (cache disabled, 150 ms latency, 200 KB/s, five cold loads):

| Case | Arabic 400 requested | Arabic 400 arrived | Fallback shown | LCP | Fonts in the first 2.5 s |
| --- | --- | --- | --- | --- | --- |
| English page with Arabic text | 4,274 → **195** ms | 5,111 → 1,067 ms | **796 → 0 ms** | 4,340 → 3,920 ms | Geist Latin 29,400; Plex Arabic 400 42,848 and 600 45,688 (117,936 B) |
| Arabic inbox | 1,478 → **190** ms | 2,435 → 1,053 ms | 0 → 0 ms | 5,036 → 4,216 ms | Geist Latin; Plex Arabic 400, then 500 and 600 for the list (163,232 B) |
| Arabic public chat page | 1,481 → **190** ms | 2,433 → 1,061 ms | 0 in practice → 0 | 4,708 → 3,732 ms | Geist Latin and Plex Arabic 400 (72,248 B) |

39.0 → 39.8b, five cold loads each, medians, times from navigation start. The Arabic files now come from the
pre-paint script's preload at HTML parse instead of from the stylesheet.

## 4. Phase 38's twenty notes

Notes from `Nabra-Phase39-Pack/reference/phase-38-notes.md`; the plan's mapping is 39.0 §3.4.

| # | Note | What happened | Step and commit | Left open or different |
| --- | --- | --- | --- | --- |
| 1 | Arabic font loading | Self-hosted subsets with versioned URLs. The pre-paint script preloads Geist Latin everywhere, Arabic 400 on Arabic pages, and Arabic 400 at low priority on English pages under `/conversations`; metric fallbacks. On an English page with Arabic text, Arabic 400 was requested at +32 ms instead of +3,241 ms, and **the fallback showed for 0 ms instead of 791 ms** (39.2a §5). | 39.2a `71f8e18` | The English-inbox preload was kept by the owner; it costs about 0.27–0.3 s of LCP at 200 KB/s and is a one-line change to drop (39.2a §4 dec. 10). First-paint fonts on Arabic pages are 163 KB, not the 71 KB the plan forecast. 39.8b re-measured: see section 3. |
| 2 | Report files: DejaVu, cut headings | Geist and Plex Arabic in PDFs (OFL beside them), tabular figures, tables sized by content, wrapping headings, numeric headings aligned with their numbers; an English PDF fell from about 755 KB to 125 KB | 39.7b `090c70a` | DejaVu stays as the last resort for scripts neither font draws; values can still end in "…" when a table cannot fit; Arabic ligatures reach the text layer as compatibility characters (section 5) |
| 3 | Small form controls on touch | One choice-control family with a 20 px box and a 44 px target that includes the label, on every pointer; adopted by every form | 39.2b `d2521aa`, then 39.5b–39.7a | Menu items are 44 px on coarse pointers (`--control-height-sm`); checked again in 39.8b |
| 4 | Global checkbox rule and `.switch` | Deleted; a checkbox outside the family keeps the browser's drawing (MASTER rule 11) | 39.2b `d2521aa` | None |
| 5 | Phone conversation header | One line under 768 px: back, customer, handling chip, Details; the facts, the handling bar and Refresh open on demand | 39.3 `99e0392`, kept in 39.4b | The thread is narrow at 1280 px (369 px), an owner option (39.4b §4 dec. 11) |
| 6 | Unread on day one | `read-all`, `unread-count` and `conversationsRead` (39.1); the unread mark is a dot plus a heavier name with "Unread" in words; "Mark all as read" names the count first and clears other tabs live (39.4a) | 39.1 `9bbd4a3`, 39.4a `cf7b109` | Notifications say «تعليم … كمقروء» while the inbox says «تحديد»: the owner picks one (section 5) |
| 7 | People shown by email | `users.display_name`, `PUT /api/me/display-name`, `displayName` on members and `invitedByDisplayName`; shown in the shell, rows, bubbles, events, Handled by, Assign, team, member, the greeting; a self-service dialog | 39.1, 39.3, 39.4a, 39.4b, 39.6a, 39.7a | The reply POST still answers `sentBy: null` (the client uses the member's own name); dialogs and toasts name the email on purpose |
| 8 | Time zones by IANA id | `zoneName()` from `Intl` (`longGeneric`, then `long`); the id always under the name; the schedule count; "Selected zone" before saving | 39.6b `4a6085f` | The zone list, the toast and the confirmation still show the id (the e2e journey checks it); report files keep the id (39.7b) |
| 9 | Greeting in one language | `widget_greeting_en/_ar`, `greetings{en,ar}`, `?lang` (39.1); three greeting boxes and per-language display (39.5b) | 39.1 `9bbd4a3`, 39.5b `f0d6da7` | «التحية» became «الترحيب» (39.5b dec. 7) |
| 10 | Overview "Needs attention" rows on phones | Two-line rows on the gold "Needs a person now" tile and the attention tile; nothing truncated | 39.7a `3e5f5a5` | "Needs attention" stays its own tile (not on the board) |
| 11 | Skeletons | Redrawn by each screen step to the loaded geometry; measured in Chrome (39.7a: 0–4 px from 1280, ≤10 px at 768, ≤20 px at 390) | 39.4a–39.7a | Sizes measured on Northwind's data |
| 12 | E2E by testid and words | Every journey passed in every step. Only `widget-corners` (select → radio locators) and `arabic-report` (client download) changed | all steps | One testid was removed: `entry-story` → `entry-brand` in 39.8a. No journey used it. `workspace-switch`, `attention-panel` and `assignee` moved but still exist |
| 13 | Busy inbox row | One "who has it" chip (Handled by, else Assigned to, by display name) and a reason chip, at most two chip lines | 39.4a `cf7b109` | "AI is answering" shows when nothing waits (a departure from the board) |
| 14 | "Handled by" placement and its two Arabic forms | In the header's handling row beside the voice chip; both accepted Arabic forms kept (owner Q8: no gendered verb) | 39.4b `0a125de` | A spec checks «تتولا» never appears |
| 15 | Two codes on a merge candidate | One short code, isolated; "ref." gone | 39.6a `83ebd79` | A WhatsApp candidate shows its masked number and the code once |
| 16 | Report downloads are plain links | `ReportsApi.download()` as a blob, the server's file name, refusals worded by code, `saveFile()` via an object URL | 39.7a `3e5f5a5` | The file is held in memory until saved (accepted under the server's size limits) |
| 17 | Knowledge list misses an event during first load | Events during any full read are counted, and the list re-reads once if any arrived | 39.5a `8fe4dd4` | Events during a failed read are forgotten |
| 18 | No masked number on customers | `reference` on the customer reads (39.1); labels use it (39.6a) | 39.1 `9bbd4a3`, 39.6a `83ebd79` | Never seen live: the dev stack has no WhatsApp customer (section 5) |
| 19 | Email preferences schema | `EmailPreferencesRequest` / `EmailPreferencesResponse`; the frontend uses both | 39.1 `9bbd4a3`, 39.6b `4a6085f` | None |
| 20 | E2E time limits | No limit was raised; `playwright.config.ts` unchanged through Phase 39; full runs took 2.4–5.2 min | all steps | 39.6b's third run and 39.8b's first counted run broke the 15-minute spacing rule (both reported) |

## 5. Open issues

Items still open at the end of Phase 39, with where they came from. Items 39.8b closed are listed
in its report (section 4).

### 5.1 Backend asks

| Ask | From | What is needed |
| --- | --- | --- |
| BA39-2 first reply time per voice and period | 39.0 §7 | A backend metric (median, AI and team); then the overview tile and the widget's "Replies in seconds" |
| BA39-3 conversation search | 39.0 §7; 39.4a/4b §7 | What is searchable, an index and a route; then the inbox's search field |
| BA39-4 widget quick replies per workspace and language | 39.0 §7; 39.5a/5b §7 | A model and route; then chips under the greeting |
| BA39-6 WhatsApp service window on the conversation read | 39.4b §7 | The window (open, closes at); then the thread header shows it |
| BA39-7 (optional) a subject or first line per conversation on the customer read | 39.4b §7 | Titles for "Earlier conversations" |
| BA39-6a-1 (optional) `language` on `InvitationResponse` | 39.6a §7 | The invitation lists show the language |
| BA39-7a-1 `previous.dailyActivity` | 39.7a §7 | The dashed "week before" line on the overview |
| No id: "Today" range on the dashboard | 39.7a §7 | Optional; the overview period selector |
| No id: the reply POST answers `sentBy: null` | 39.1 §4 dec. 8 | Fill `sentBy` in the POST answer (the client works around it) |
| BA-6 session-check budget (since 38.0) | 39.0 §7 | Tune before launch |
| BA-8 web-vitals endpoint (only if RUM is wanted) | 39.0 §7 | Decide on RUM first |

### 5.2 Owner decisions still pending

- The report files' provider names, proposed in 39.8a: "OpenAI", and "Local test stand-in" /
  «البديل التجريبي المحلي» (39.8a §8, §9 risk 1).
- «تعليم كمقروء» (notifications) against the inbox's «تحديد» (39.4a §9; 39.6b §9 risk 4).
- The English-inbox Arabic preload: kept, or dropped to save about 0.3 s of LCP on slow links
  (39.2a §4 dec. 10).
- The thread at 1280 px: an option to hide the help sentence there (39.4b §4 dec. 11).
- One chip tone for three meanings: the draft ring marks Draft, Expired and Custom/You (39.6a §4
  dec. 6).
- The loading screen shows both names in every language: an option to show one per language
  (39.2b §4 dec. 6).
- Dark-mode email clients may invert the email card, which leaves the mark at about 2:1: a plate
  behind it, or accept it (39.8a §9 risk 2; owner: waits for Phase 42).

### 5.3 Known and accepted, to watch

- A fourth blurred layer is possible on a phone: a toast over an open sheet (39.3 §9 risk 3).
- The rail's tooltip stays put while the rail list scrolls (39.3 §9 risk 4).
- Switching workspace shows the chooser for a moment (39.3 §4 dec. 4).
- A 0.003 layout shift in the list when the sender word changes width (39.4b §9).
- The inbox count reader is always on wherever the side bar shows the inbox. Watch the load on busy
  workspaces (39.3 §9 risk 1).
- The inbox's 50-row p95 was measured on the dev server only (39.4a §9).
- The default blue accent falls back to emerald in the dark look, by design (39.5b §9 risk 2).
- "Answered by the AI" may overcount on dev data from before 37.4. It is exact on deployments
  (39.1 §8 risk 2).
- `conversationsRead` reaches only connections that called `SubscribeNotifications`, so a future
  client must subscribe (39.1 §8 risk 6).
- Live-only gaps on the dev stack:
  - no capped plan, so the meters' warning states were seen only in the design lab (39.6b);
  - no WhatsApp customer, so the masked `reference` was never seen on screen (39.1, 39.6a);
  - emails were checked in Mailpit only (39.8a §9 risk 4).
- `docs/architecture/localisation-arabic-rtl.md` still quotes Instrument Sans in its history
  sections (39.2a §9 risk 7).
- From 39.7b, for the next report-renderer work:
  - Arabic ligatures reach the PDF text layer as compatibility characters, so searching for «شركة»
    can fail in readers that do not normalise;
  - `SkiaSharp.HarfBuzz` is used only for `ToHarfBuzzBlob`;
  - the widest English table's id column is still shortened with "…";
  - DejaVu Sans (about 1.4 MB) stays bundled as the last resort;
  - Arabic lines are taller, so a report can run a page longer.
- The bell's accessible name says the exact unread count ("105 unread") while its badge shows
  "99+": Lighthouse's weight-0 audit `label-content-name-mismatch` flags it (39.8b).

## 6. What the next phases need from Phase 39

### Before any demo or launch: dev data to clean (owner's assistant's list)

The dev database `omniai_p36` holds synthetic data from Phase 38 and 39 checks. Remove it, or keep
it knowingly, before a demo or a launch; never ship it.

| Data | Where it came from |
| --- | --- |
| The role "Zone manager (38.7b check)" | 38.7b's time-zone checks |
| Four visitor conversations "Phase 39.1 live check: is anyone there?" | 39.1 §8 risk 4 |
| Four accounts `p39-1.rival.<time>@omniai.example`, each with a workspace "P39.1 Rival (live check)" | 39.1 §8 risk 4 |
| The display names "Rana Haddad" (the owner) and «عمر صالح» (the support member) | 39.6a §9 risks 1–2 |
| Visitor X683JW merged into BG746R | 39.6a §9 risk 2 |
| Also: three "p39-5a note17 …" documents and four visitor conversations on the Northwind hosted link | 39.5a §9 risk 4; 39.4a/4b/5b live checks |

### The public website

- Owner's decision (16 Nov 2026): the owner designs the public website himself; the Phase 40 plan
  the owner's assistant prepared will not run.
- The brand is ready to use: the mark and lockup (`shared/brand-mark`, the outlined paths), the
  favicon and app-icon set, the manifest, the fonts with their licences, and the tokens.
- "Powered by Nabra" / «بدعم من نبرة» in the widget has no link until a public domain exists
  (owner Q6).
- The manifest says `display: browser`. Choose `standalone` only if an installable app is wanted
  (39.8a §4 dec. 7).
- The first-impression skills apply there. The admin's entry pages are a calm split, not a
  marketing hero.

### Launch hardening

- **Deploy images:** run the .NET runtime 10.0.15 or later, align the other ASP.NET Core packages
  with it, and keep the MessagePack comment (38.7a, 38.8).
- **npm advisories in dev-only packages:** undici (high); vitest, @vitest/mocker and fast-uri
  (moderate). These are from the owner's assistant's list; 39.8b did not re-run the audit.
- **Angular:** move to the latest 22.x patch that is at least 7 days old.
- BA-6 (session-check budget) and BA-8 (RUM), as in section 5.1.
- Re-measure the inbox's 50-row p95 on the production build.
- Re-measure fonts and LCP on the HTTP/2 edge. The local numbers in section 3 come from a loopback
  HTTP/1.1 server.
- **A native Arabic review** of every string Phase 39 added or changed (appendix A), together with
  38.8's 61 strings.

### Real providers

- Check emails in real clients (Gmail, Outlook, Apple Mail), including the `cid:` mark and dark mode
  (39.8a §9 risks 2 and 4).
- With a real WhatsApp number:
  - see the masked `reference` on the customers screens;
  - expose the service window (BA39-6).
- With a real subscription or a finite default plan, see the meters' warning and exhausted states
  live.
- Replace the stand-in's provider name with the real provider names in the report files (owner
  decision, section 5.2).

### Deployment

- Re-run 39.0 §3.3's protocol on the deployed edge: Lighthouse, lab vitals, the Arabic font table.
  Use the result to decide the English-inbox Arabic preload.
- The Content Security Policy needs nothing new. Fonts, icons and the email mark are all
  self-hosted or attached; Phase 39 added no third-party origin.

### Messenger and Instagram

No Phase 39 item depends on them. What a new channel takes from Phase 39:

- a channel badge glyph in `shared/channel` (the avatar's badge and the customer rows);
- its words in `channel-words.ts`, in both languages;
- the lifecycle chips (Draft, then «تعمل», then «معطّلة»);
- a corner of the channels page;
- a quick look in the matrix.

The three voices and the inbox already work for any channel.

## Appendix A. Every Arabic string Phase 39 added or changed, for the owner's native review

The catalogue (`frontend/src/locale/messages.ar.json`) at the plan commit `e79b5e6` compared with
39.8b's working tree. "Step" is the step that last set the text. Strings the backend renders:

- 39.8a added one: «البديل التجريبي المحلي» ("Local test stand-in"), in the provider column of the
  provider usage and cost report files (Arabic files only; a proposal, section 5.2).
- Emails and the other report wording are unchanged (39.7b §8, 39.8a §8).

Review these together with 38.8's 61 strings (`PHASE_38_8_ADOPT_AND_PATCH_REPORT.md`, section 8).

#### Changed (12)

| Last set in | Key | English | Arabic now | Arabic before Phase 39 |
| --- | --- | --- | --- | --- |
| 39.5b | `problem.websiteChat.greeting.control_characters` | This greeting has characters that cannot be saved. Paste it as plain text and try again. | يحوي هذا الترحيب رموزًا لا يمكن حفظها. الصقه نصًا عاديًا وحاول مرة أخرى. | تحوي هذه التحية رموزًا لا يمكن حفظها. الصقها نصًا عاديًا وحاول مرة أخرى. |
| 39.5b | `problem.websiteChat.greeting.too_long` | This greeting is too long. Shorten it and save again. | هذا الترحيب طويل جدًا. اختصره ثم احفظ مرة أخرى. | هذه التحية طويلة جدًا. اختصرها ثم احفظ مرة أخرى. |
| 39.5b | `widget.accent.hint` | The launcher wears it on your website. Inside the chat, the workspace tile and Send wear it wherever it keeps the contrast rules, and Nabra's emerald otherwise; the AI's replies always stay emerald. The text on top is chosen so it can always be read. | يظهر على زر الفتح في موقعك. وداخل الدردشة يظهر على مربّع مساحة العمل وزر الإرسال حيثما حافظ على قواعد التباين، وإلا فيظهر عليهما أخضر نبرة الزمردي؛ وتبقى ردود الذكاء الاصطناعي بالأخضر الزمردي دائمًا. يُختار لون النص فوقه بحيث يبقى مقروءًا دائمًا. | يُستخدم في زر الفتح والترويسة وزر الإرسال، وتأخذ ردود الذكاء الاصطناعي درجة فاتحة منه. يُختار لون النص فوقه بحيث يبقى مقروءًا دائمًا. |
| 39.5b | `widget.greeting` | Greeting | الترحيب | التحية |
| 39.5b | `widget.greeting.hint` | Shown at the top of the chat before anybody types, as your words rather than as something the AI said. Up to {$INTERPOLATION} characters; it may be left empty. | يظهر في أعلى الدردشة قبل أن يكتب أحد، على أنه كلماتك لا كلام الذكاء الاصطناعي. حتى {$INTERPOLATION} من الأحرف، ويمكن تركه فارغًا. | تظهر في أعلى الدردشة قبل أن يكتب أحد، على أنها كلماتك لا كلام الذكاء الاصطناعي. حتى {$INTERPOLATION} من الأحرف، ويمكن تركها فارغة. |
| 39.6a | `customer.merge.summary` | {$START_TAG_STRONG}{$INTERPOLATION}{$CLOSE_TAG_STRONG} ({$INTERPOLATION_1}) will be combined into {$START_TAG_STRONG}{$INTERPOLATION_2}{$CLOSE_TAG_STRONG} ({$INTERPOLATION_3}). | سيُدمج {$START_TAG_STRONG}{$INTERPOLATION}{$CLOSE_TAG_STRONG} ({$INTERPOLATION_1}) في {$START_TAG_STRONG}{$INTERPOLATION_2}{$CLOSE_TAG_STRONG} ({$INTERPOLATION_3}). | سيُدمج {$START_TAG_STRONG}{$INTERPOLATION}{$CLOSE_TAG_STRONG} ({$INTERPOLATION_1} · {$INTERPOLATION_2} · المرجع {$START_TAG_BDI}{$INTERPOLATION_3}{$CLOSE_TAG_BDI}) في {$START_TAG_STRONG}{$INTERPOLATION_4}{$CLOSE_TAG_STRONG} ({$INTERPOLATION_5} · {$INTERPOLATION_6} · المرجع {$START_TAG_BDI}{$INTERPOLATION_7}{$CLOSE_TAG_BDI}). |
| 39.8b | `agents.status.active` | Active | يعمل | مفعّل |
| 39.8b | `channels.lifecycle.label` | Lifecycle: Draft, then Active, which can be Disabled and made Active again | مراحل القناة: مسودة، ثم تعمل، ويمكن تعطيلها ثم تشغيلها من جديد | مراحل القناة: مسودة، ثم مفعّلة، ويمكن تعطيلها ثم تفعيلها من جديد |
| 39.8b | `channels.status.active` | Active | تعمل | مفعّلة |
| 39.8b | `inbox.live.on` | Live updates on | التحديثات المباشرة تعمل | التحديثات المباشرة مفعّلة |
| 39.8b | `schedules.on` | On | تعمل | مفعّلة |
| 39.8b | `websiteChat.agentNotActive` | Agent not active | الوكيل لا يعمل | الوكيل غير مفعّل |

#### Added (182)

| Last set in | Key | English | Arabic |
| --- | --- | --- | --- |
| 39.2a | `account.reduceTransparency` | Reduce transparency | تقليل الشفافية |
| 39.2a | `lab.colour.chips` | Chips | الشارات |
| 39.2a | `lab.colour.glass` | Glass and depth | الزجاج والعمق |
| 39.2a | `lab.colour.grounds` | Ground and panels | الأرضية واللوحات |
| 39.2a | `lab.colour.lines` | Lines and control boundaries | الخطوط وحدود عناصر التحكم |
| 39.2a | `lab.colour.note.calmGlass` | Values are read live from the page, so they are the ones this theme actually resolved. Emerald is the AI, gold is the team and cream is the customer; a voice is never decoration. | تُقرأ القيم مباشرة من الصفحة، فهي ما طبّقه هذا المظهر فعلًا. الزمرّدي للذكاء الاصطناعي، والذهبي للفريق، والكريمي للعميل؛ ولا يُستخدم لون الصوت للزينة أبدًا. |
| 39.2a | `lab.colour.primary` | Primary, links and focus (emerald) | اللون الأساسي والروابط والتركيز (زمرّدي) |
| 39.2a | `lab.colour.sideBySide.note.calmGlass` | Status words reuse the three hues, each with its own glyph and word, so none depends on its hue. In the inbox and the transcript only the voice chips appear. | تستخدم كلمات الحالة الألوان الثلاثة نفسها، ولكلٍّ منها رمزها وكلمتها، فلا يعتمد معناها على اللون. وفي صندوق المحادثات والمحادثة لا تظهر إلا شارات الأصوات. |
| 39.2a | `lab.colour.voice.ai` | Voice: the AI (emerald) | الصوت: الذكاء الاصطناعي (زمرّدي) |
| 39.2a | `lab.colour.voice.customer` | Voice: the customer (cream) | الصوت: العميل (كريمي) |
| 39.2a | `lab.colour.voice.team` | Voice: the team (gold) | الصوت: الفريق (ذهبي) |
| 39.2a | `lab.shape.glass` | Glass | الزجاج |
| 39.2a | `lab.shape.glassSample` | Glass: only where things float, and the only surface that casts a shadow. With "Reduce transparency" it becomes its solid twin. | الزجاج: فقط حيث تطفو العناصر، وهو السطح الوحيد الذي يُلقي ظلًا. ومع «تقليل الشفافية» يصبح نظيره الصلب. |
| 39.2a | `lab.shape.panel` | A panel: solid, a 1px line, no shadow. | لوحة: صلبة، بخط رفيع، بلا ظل. |
| 39.2a | `lab.type.figure` | Figure | رقم كبير |
| 39.2a | `lab.type.note.calmGlass` | Geist and IBM Plex Sans Arabic for the interface, Instrument Serif for the display line, all self-hosted. Figures are tabular and stay Western in both languages. | Geist وIBM Plex Sans Arabic للواجهة، وInstrument Serif لسطر العرض، وكلها مستضافة ذاتيًا. الأرقام جدولية وتبقى غربية في اللغتين. |
| 39.2b | `lab.buttons.dense` | Dense | كثيف |
| 39.2b | `lab.buttons.note.calmGlass` | Pills, 44px tall (40px in dense headers), weight 600. One primary per area. Hover, press and focus are live: point at them, or press Tab. | أزرار بيضوية بارتفاع 44 بكسل (40 في الرؤوس الكثيفة)، بوزن 600. زر أساسي واحد في كل منطقة. التمرير والضغط والتركيز تعمل فعلًا: أشِر إليها أو اضغط Tab. |
| 39.2b | `lab.chips.ai` | AI is answering | الذكاء الاصطناعي يجيب |
| 39.2b | `lab.chips.failed` | The AI could not reply | تعذّر على الذكاء الاصطناعي الرد |
| 39.2b | `lab.chips.need` | Needs a person | بحاجة إلى الفريق |
| 39.2b | `lab.chips.team` | Handled by Sara | استلام المحادثة: سارة |
| 39.2b | `lab.chips.waiting` | Waiting for the team | بانتظار رد الفريق |
| 39.2b | `lab.choices` | Choice controls | عناصر الاختيار |
| 39.2b | `lab.choices.checkboxes` | Checkboxes | مربعات الاختيار |
| 39.2b | `lab.choices.longHint` | Open the inbox and read every message, without replying. | فتح صندوق المحادثات وقراءة كل رسالة، دون الرد. |
| 39.2b | `lab.choices.longLabel` | Read conversations | قراءة المحادثات |
| 39.2b | `lab.choices.mine` | Mine | محادثاتي |
| 39.2b | `lab.choices.mixed` | Some of them | بعضها |
| 39.2b | `lab.choices.needsPerson` | Needs a person | بحاجة إلى الفريق |
| 39.2b | `lab.choices.note` | One family: a visible 20px box or a 40×24 track inside a 44px target, and the label is part of the target. Each wraps the native control, so keyboard and screen readers behave as they always do. | عائلة واحدة: مربع ظاهر بحجم 20 بكسل أو مسار بحجم 40×24 داخل هدف بحجم 44 بكسل، والتسمية جزء من الهدف. كلٌّ منها يغلّف عنصر التحكم الأصلي، فتعمل لوحة المفاتيح وقارئات الشاشة كما اعتادت. |
| 39.2b | `lab.choices.off` | Off | متوقف |
| 39.2b | `lab.choices.radios` | Radios | أزرار الاختيار الفردي |
| 39.2b | `lab.choices.segmented` | Segmented controls | أدوات التحكم المقسّمة |
| 39.2b | `lab.choices.segmented.note` | Radios for a choice in a form, links when the choice is an address (never tabs for an address), buttons for a choice that acts at once. | أزرار اختيار فردي لاختيار داخل نموذج، وروابط حين يكون الاختيار عنوانًا (ولا تبويبات للعناوين أبدًا)، وأزرار لاختيار يُنفَّذ فورًا. |
| 39.2b | `lab.choices.short` | Choices | الاختيارات |
| 39.2b | `lab.choices.switches` | Switches | المفاتيح |
| 39.2b | `lab.choices.views` | Sample views | طرق عرض تجريبية |
| 39.2b | `lab.empty.action` | Connect a channel | ربط قناة |
| 39.2b | `lab.event.failed` | The AI could not reply · 10:41 | تعذّر على الذكاء الاصطناعي الرد · 10:41 |
| 39.2b | `lab.event.today` | Today | اليوم |
| 39.2b | `lab.forms.inputs` | Inputs, selects and text areas | حقول الإدخال والقوائم ومربعات النص |
| 39.2b | `lab.glass.count` | Count the blurred layers | عُدّ الطبقات الضبابية |
| 39.2b | `lab.glass.note` | Glass only where things float: menus, popovers, tooltips, dialogs, toasts and, from the next step, the side bar. At most three blurred layers on screen, and never on a list that scrolls. With "Reduce transparency" each one becomes its solid twin. | الزجاج فقط حيث تطفو العناصر: القوائم والنوافذ المنبثقة والتلميحات ومربعات الحوار والإشعارات، ومن الخطوة التالية الشريط الجانبي. ثلاث طبقات ضبابية على الشاشة على الأكثر، ولا ضبابية أبدًا على قائمة تُمرَّر. ومع «تقليل الشفافية» تصبح كلٌّ منها نظيرها الصلب. |
| 39.2b | `lab.glass.result` | Blurred layers on screen now: {$INTERPOLATION} (the budget is 3). | الطبقات الضبابية على الشاشة الآن: {$INTERPOLATION} (والحد 3). |
| 39.2b | `lab.intro.components` | "Calm Glass": the tokens and every component screens are built from, on one page. Development builds only. Every name, number and message here is sample content, written for this page. | «زجاج هادئ»: الرموز وكل المكوّنات التي تُبنى منها الشاشات، في صفحة واحدة. لنسخ التطوير فقط. كل اسم ورقم ورسالة هنا محتوى تجريبي كُتب لهذه الصفحة. |
| 39.2b | `lab.mark.note.circles` | Two circles, the AI and the team, and the one tone where they meet. Never mirrored, rotated or given a gradient. One colour: the outlines and the filled overlap. | دائرتان، الذكاء الاصطناعي والفريق، والنبرة الواحدة حيث تلتقيان. لا تُعكس ولا تُدار ولا تُعطى تدرّجًا لونيًا. وبلون واحد: الحدود الخارجية والتقاطع المملوء. |
| 39.2b | `lab.orb.answering` | The AI is answering… | الذكاء الاصطناعي يجيب… |
| 39.2b | `lab.overlays` | Menus, popovers, tooltips, dialogs and toasts | القوائم والنوافذ المنبثقة والتلميحات ومربعات الحوار والإشعارات |
| 39.2b | `lab.overlays.note` | All glass. Focus moves in and returns to the control that opened it; Escape closes; a tooltip shows on hover and on keyboard focus. | كلها زجاج. ينتقل التركيز إليها ثم يعود إلى عنصر التحكم الذي فتحها؛ ومفتاح Escape يغلقها؛ ويظهر التلميح عند التمرير وعند التركيز بلوحة المفاتيح. |
| 39.2b | `lab.overlays.short` | Overlays | العناصر الطافية |
| 39.2b | `lab.popover.body` | Sample: a popover holds a little content, not a list of actions. | مثال: تحمل النافذة المنبثقة محتوى قليلًا، لا قائمة إجراءات. |
| 39.2b | `lab.popover.open` | Open a popover | فتح نافذة منبثقة |
| 39.2b | `lab.rows` | Rows and avatars | الصفوف والصور الرمزية |
| 39.2b | `lab.rows.list` | Sample rows | صفوف تجريبية |
| 39.2b | `lab.rows.note` | Radius 16, a 40px avatar with its channel's badge and glyph, the name and the time, one line of preview, then chips. Selected is a faint emerald tint; unread is a dot, a heavier name and the word "Unread" for screen readers. | زوايا بنصف قطر 16، وصورة رمزية بحجم 40 بكسل عليها شارة القناة ورمزها، ثم الاسم والوقت، وسطر واحد من المعاينة، ثم الشارات. المحدد بلون زمرّدي خفيف؛ وغير المقروء بنقطة واسم أثقل وكلمة «غير مقروءة» لقارئات الشاشة. |
| 39.2b | `lab.rows.short` | Rows | الصفوف |
| 39.2b | `lab.rows.unread` | Unread | غير مقروءة |
| 39.2b | `lab.shape.surfaces` | Panels and tiles | اللوحات والبلاطات |
| 39.2b | `lab.shape.tile` | A tile inside it: the soft fill. | بلاطة داخلها: التعبئة الناعمة. |
| 39.2b | `lab.states` | Notices, skeletons, empty and error states | التنبيهات والهياكل المؤقتة وحالات الفراغ والخطأ |
| 39.2b | `lab.states.short` | States | الحالات |
| 39.2b | `lab.tooltip.label` | Sample: information | مثال: معلومات |
| 39.2b | `lab.tooltip.text` | Sample: a tooltip names or explains. | مثال: التلميح يسمّي أو يشرح. |
| 39.2b | `lab.transcript.voices` | The three voices: bubbles, the orb and event lines | الأصوات الثلاثة: الفقاعات والكرة وخطوط الأحداث |
| 39.3 | `nav.group.conversations` | Conversations | المحادثات |
| 39.3 | `nav.inbox` | Inbox | صندوق المحادثات |
| 39.3 | `nav.more` | More | المزيد |
| 39.3 | `nav.moreTitle` | More areas | أقسام أخرى |
| 39.3 | `workspaceSwitcher.chooser` | Choose or create a workspace | اختيار مساحة عمل أو إنشاء واحدة |
| 39.3 | `workspaceSwitcher.heading` | Your workspaces | مساحات العمل لديك |
| 39.4a | `inbox.chip.aiAnswering` | AI is answering | يردّ الذكاء الاصطناعي |
| 39.4a | `inbox.markAll.action` | Mark all as read | تحديد الكل كمقروء |
| 39.4a | `inbox.markAll.body` | Only your own unread conversations change; everybody else on the team keeps theirs. | يقتصر ذلك على المحادثات غير المقروءة لديك، ولا يتغيّر شيء لدى بقية أعضاء الفريق. |
| 39.4a | `inbox.markAll.done.few` | {$count} conversations marked as read. | حُدِّدت {$count} محادثات كمقروءة. |
| 39.4a | `inbox.markAll.done.many` | {$count} conversations marked as read. | حُدِّدت {$count} محادثة كمقروءة. |
| 39.4a | `inbox.markAll.done.one` | {$count} conversation marked as read. | حُدِّدت محادثة واحدة كمقروءة. |
| 39.4a | `inbox.markAll.done.other` | {$count} conversations marked as read. | حُدِّدت {$count} محادثة كمقروءة. |
| 39.4a | `inbox.markAll.done.two` | {$count} conversations marked as read. | حُدِّدت محادثتان كمقروءتين. |
| 39.4a | `inbox.markAll.done.zero` | Every conversation was already read. | كانت كل المحادثات مقروءة بالفعل. |
| 39.4a | `inbox.markAll.heading` | Mark every conversation as read? | تحديد كل المحادثات كمقروءة؟ |
| 39.4a | `inbox.markAll.none` | Every conversation is already read. | كل المحادثات مقروءة بالفعل. |
| 39.4a | `inbox.markAll.unread.few` | {$count} conversations in this workspace are unread. | في مساحة العمل هذه {$count} محادثات غير مقروءة. |
| 39.4a | `inbox.markAll.unread.many` | {$count} conversations in this workspace are unread. | في مساحة العمل هذه {$count} محادثة غير مقروءة. |
| 39.4a | `inbox.markAll.unread.one` | {$count} conversation in this workspace is unread. | في مساحة العمل هذه محادثة واحدة غير مقروءة. |
| 39.4a | `inbox.markAll.unread.other` | {$count} conversations in this workspace are unread. | في مساحة العمل هذه {$count} محادثة غير مقروءة. |
| 39.4a | `inbox.markAll.unread.two` | {$count} conversations in this workspace are unread. | في مساحة العمل هذه محادثتان غيرُ مقروءتين. |
| 39.4a | `inbox.markAll.unread.zero` | No conversations are unread. | لا توجد محادثات غير مقروءة. |
| 39.4a | `inbox.mine.empty.body` | Conversations assigned to you, or that you took over, appear here. | تظهر هنا المحادثات المسندة إليك، والمحادثات المستلَمة من قِبلك. |
| 39.4a | `inbox.mine.empty.heading` | Nothing is yours right now | لا توجد محادثات لك الآن |
| 39.4a | `inbox.title` | Inbox | صندوق المحادثات |
| 39.4a | `inbox.view.mine` | Mine | محادثاتي |
| 39.4a | `nav.inboxShort` | Inbox | المحادثات |
| 39.4a | `title.inbox` | Inbox | صندوق المحادثات |
| 39.4b | `conversation.assign` | Assign | إسناد |
| 39.4b | `customerPanel.allConversations` | See all their conversations | عرض كل محادثات العميل |
| 39.4b | `customerPanel.channel` | Channel | القناة |
| 39.4b | `customerPanel.conversations` | Conversations | المحادثات |
| 39.4b | `customerPanel.earlier` | Earlier conversations | المحادثات السابقة |
| 39.4b | `customerPanel.failed` | Could not load this customer's other conversations. | تعذّر تحميل محادثات العميل الأخرى. |
| 39.4b | `customerPanel.loading` | Loading the customer… | جارٍ تحميل بيانات العميل… |
| 39.4b | `customerPanel.noEarlier` | No earlier conversations. | لا توجد محادثات سابقة. |
| 39.4b | `customerPanel.openCustomer` | Open the customer's page | فتح صفحة العميل |
| 39.4b | `customerPanel.since` | Customer since {$date} | عميل منذ {$date} |
| 39.5b | `problem.websiteChat.greeting.control_characters.ar` | The Arabic greeting has characters that cannot be saved. Paste it as plain text and try again. | يحوي الترحيب بالعربية رموزًا لا يمكن حفظها. الصقه نصًا عاديًا وحاول مرة أخرى. |
| 39.5b | `problem.websiteChat.greeting.control_characters.default` | The default greeting has characters that cannot be saved. Paste it as plain text and try again. | يحوي الترحيب الافتراضي رموزًا لا يمكن حفظها. الصقه نصًا عاديًا وحاول مرة أخرى. |
| 39.5b | `problem.websiteChat.greeting.control_characters.en` | The English greeting has characters that cannot be saved. Paste it as plain text and try again. | يحوي الترحيب بالإنجليزية رموزًا لا يمكن حفظها. الصقه نصًا عاديًا وحاول مرة أخرى. |
| 39.5b | `problem.websiteChat.greeting.too_long.ar` | The Arabic greeting is too long. Shorten it and save again. | الترحيب بالعربية طويل جدًا. اختصره ثم احفظ مرة أخرى. |
| 39.5b | `problem.websiteChat.greeting.too_long.default` | The default greeting is too long. Shorten it and save again. | الترحيب الافتراضي طويل جدًا. اختصره ثم احفظ مرة أخرى. |
| 39.5b | `problem.websiteChat.greeting.too_long.en` | The English greeting is too long. Shorten it and save again. | الترحيب بالإنجليزية طويل جدًا. اختصره ثم احفظ مرة أخرى. |
| 39.5b | `visitor.poweredBy` | Powered by Nabra | بدعم من نبرة |
| 39.5b | `visitor.status.unavailable` | Unavailable | غير متاحة |
| 39.5b | `widget.check.chatDarkEmerald` | Inside the chat, dark: your colour does not keep the contrast rules there, so the tile and Send use Nabra's emerald | داخل الدردشة، المظهر الداكن: لا يحافظ لونك على قواعد التباين هناك، لذلك يستخدم مربّع مساحة العمل وزر الإرسال أخضر نبرة الزمردي |
| 39.5b | `widget.check.chatDarkUsed` | Inside the chat, dark: {$START_TAG_BDI}{$INTERPOLATION}{$CLOSE_TAG_BDI} against its background, so the tile and Send use your colour | داخل الدردشة، المظهر الداكن: {$START_TAG_BDI}{$INTERPOLATION}{$CLOSE_TAG_BDI} مقابل خلفيتها، لذلك يستخدم مربّع مساحة العمل وزر الإرسال لونك |
| 39.5b | `widget.check.chatLightEmerald` | Inside the chat, light: your colour does not keep the contrast rules there, so the tile and Send use Nabra's emerald | داخل الدردشة، المظهر الفاتح: لا يحافظ لونك على قواعد التباين هناك، لذلك يستخدم مربّع مساحة العمل وزر الإرسال أخضر نبرة الزمردي |
| 39.5b | `widget.check.chatLightUsed` | Inside the chat, light: {$START_TAG_BDI}{$INTERPOLATION}{$CLOSE_TAG_BDI} against its background, so the tile and Send use your colour | داخل الدردشة، المظهر الفاتح: {$START_TAG_BDI}{$INTERPOLATION}{$CLOSE_TAG_BDI} مقابل خلفيتها، لذلك يستخدم مربّع مساحة العمل وزر الإرسال لونك |
| 39.5b | `widget.greeting.ar` | Greeting in Arabic | الترحيب بالعربية |
| 39.5b | `widget.greeting.ar.hint` | For visitors reading the chat in Arabic. | للزوار الذين يقرؤون الدردشة بالعربية. |
| 39.5b | `widget.greeting.default` | Default greeting | الترحيب الافتراضي |
| 39.5b | `widget.greeting.default.hint` | Visitors whose language has no greeting of its own see this one. | يظهر هذا الترحيب للزوار الذين لا يوجد ترحيب بلغتهم. |
| 39.5b | `widget.greeting.en` | Greeting in English | الترحيب بالإنجليزية |
| 39.5b | `widget.greeting.en.hint` | For visitors reading the chat in English. | للزوار الذين يقرؤون الدردشة بالإنجليزية. |
| 39.5b | `widget.preview.emeraldBoth` | Your colour does not keep the contrast rules inside the chat, so visitors see Nabra's emerald on the tile and Send. | لا يحافظ لونك على قواعد التباين داخل الدردشة، لذلك يرى الزوار أخضر نبرة الزمردي على مربّع مساحة العمل وزر الإرسال. |
| 39.5b | `widget.preview.emeraldDark` | In the dark look your colour does not keep the contrast rules, so visitors see Nabra's emerald on the tile and Send there. | في المظهر الداكن لا يحافظ لونك على قواعد التباين، لذلك يرى الزوار أخضر نبرة الزمردي على مربّع مساحة العمل وزر الإرسال هناك. |
| 39.5b | `widget.preview.emeraldLight` | In the light look your colour does not keep the contrast rules, so visitors see Nabra's emerald on the tile and Send there. | في المظهر الفاتح لا يحافظ لونك على قواعد التباين، لذلك يرى الزوار أخضر نبرة الزمردي على مربّع مساحة العمل وزر الإرسال هناك. |
| 39.6a | `account.displayName.hint` | Leave it empty to show your email address instead. | اتركه فارغًا ليظهر عنوان بريدك الإلكتروني بدلًا منه. |
| 39.6a | `account.displayName.item` | Change display name | تغيير الاسم الظاهر |
| 39.6a | `account.displayName.label` | Display name | الاسم الظاهر |
| 39.6a | `account.displayName.lede` | How your team sees you. Customers never see it: they read “Support”. | اسمك كما يراه فريقك. لا يراه العملاء، فهم يرون «فريق الدعم» بدلًا منه. |
| 39.6a | `account.displayName.removed` | Your display name is removed. Your team sees your email address. | أُزيل اسمك الظاهر، وسيرى فريقك عنوان بريدك الإلكتروني. |
| 39.6a | `account.displayName.save` | Save name | حفظ الاسم |
| 39.6a | `account.displayName.saved` | Your display name is saved. | حُفظ اسمك الظاهر. |
| 39.6a | `account.displayName.title` | Your display name | اسمك الظاهر |
| 39.6a | `account.soundsOff` | Sounds off | الأصوات متوقفة |
| 39.6a | `account.soundsOn` | Sounds on | الأصوات تعمل |
| 39.6a | `problem.account.display_name.control_characters` | Your display name contains a hidden character, such as a direction mark. Remove it and save again. | يحوي اسمك الظاهر رمزًا مخفيًا، مثل علامة اتجاه. احذفه ثم احفظ مرة أخرى. |
| 39.6a | `problem.account.display_name.too_long.few` | Your display name can be at most {$limit} characters. Shorten it and save again. | يمكن أن يتكوّن اسمك الظاهر من {$limit} أحرف على الأكثر. اختصره ثم احفظ مرة أخرى. |
| 39.6a | `problem.account.display_name.too_long.many` | Your display name can be at most {$limit} characters. Shorten it and save again. | يمكن أن يتكوّن اسمك الظاهر من {$limit} حرفًا على الأكثر. اختصره ثم احفظ مرة أخرى. |
| 39.6a | `problem.account.display_name.too_long.one` | Your display name can be at most {$limit} character. Shorten it and save again. | يمكن أن يتكوّن اسمك الظاهر من حرف واحد على الأكثر. اختصره ثم احفظ مرة أخرى. |
| 39.6a | `problem.account.display_name.too_long.other` | Your display name can be at most {$limit} characters. Shorten it and save again. | يمكن أن يتكوّن اسمك الظاهر من {$limit} حرف على الأكثر. اختصره ثم احفظ مرة أخرى. |
| 39.6a | `problem.account.display_name.too_long.two` | Your display name can be at most {$limit} characters. Shorten it and save again. | يمكن أن يتكوّن اسمك الظاهر من حرفين على الأكثر. اختصره ثم احفظ مرة أخرى. |
| 39.6a | `problem.account.display_name.too_long.zero` | Your display name can be at most {$limit} characters. Shorten it and save again. | يمكن أن يتكوّن اسمك الظاهر من {$limit} حرف على الأكثر. اختصره ثم احفظ مرة أخرى. |
| 39.6a | `team.member.emailLabel` | Email address: | عنوان البريد الإلكتروني: |
| 39.6b | `billing.state.notSetUp` | Payments not set up | المدفوعات غير مُعدّة |
| 39.6b | `notificationPreferences.emailNotSetUp` | Email not set up | البريد الإلكتروني غير مُعدّ |
| 39.6b | `workspaceSettings.zone.inUse` | Time zone in use | المنطقة الزمنية المستخدمة |
| 39.6b | `workspaceSettings.zone.schedules` | Report schedules in this zone | التقارير المجدولة وفق هذه المنطقة |
| 39.6b | `workspaceSettings.zone.selected` | Selected zone: | المنطقة المختارة: |
| 39.7a | `overview.answered.before` | The 7 days before: {$percent} | الأيام السبعة السابقة: {$percent} |
| 39.7a | `overview.answered.before.none` | No conversations started the 7 days before | لم تبدأ أي محادثة في الأيام السبعة السابقة |
| 39.7a | `overview.answered.counts` | {$answered} of {$started} | {$answered} من {$started} |
| 39.7a | `overview.answered.heading` | Answered by the AI | أجاب عنها الذكاء الاصطناعي |
| 39.7a | `overview.answered.none` | No conversation started in the last 7 days. | لم تبدأ أي محادثة في آخر 7 أيام. |
| 39.7a | `overview.answered.note` | Conversations the AI answered with nobody stepping in. | محادثات أجاب عنها الذكاء الاصطناعي دون أن يتدخّل أحد. |
| 39.7a | `overview.attention.clear.word` | All clear | كل شيء على ما يرام |
| 39.7a | `overview.channels.count.few` | {$count} conversations | {$count} محادثات |
| 39.7a | `overview.channels.count.many` | {$count} conversations | {$count} محادثة |
| 39.7a | `overview.channels.count.one` | {$count} conversation | محادثة واحدة |
| 39.7a | `overview.channels.count.other` | {$count} conversations | {$count} محادثة |
| 39.7a | `overview.channels.count.two` | {$count} conversations | محادثتان |
| 39.7a | `overview.channels.count.zero` | {$count} conversations | {$count} محادثة |
| 39.7a | `overview.conversations.heading` | Conversations | المحادثات |
| 39.7a | `overview.conversations.period` | Last 7 days | آخر 7 أيام |
| 39.7a | `overview.greeting.afternoon` | Good afternoon | مساء الخير |
| 39.7a | `overview.greeting.afternoon.named` | Good afternoon, {$name} | مساء الخير، {$name} |
| 39.7a | `overview.greeting.evening` | Good evening | مساء الخير |
| 39.7a | `overview.greeting.evening.named` | Good evening, {$name} | مساء الخير، {$name} |
| 39.7a | `overview.greeting.morning` | Good morning | صباح الخير |
| 39.7a | `overview.greeting.morning.named` | Good morning, {$name} | صباح الخير، {$name} |
| 39.7a | `overview.needsPerson.failed` | The waiting conversations could not be read. | تعذّرت قراءة المحادثات المنتظرة. |
| 39.7a | `overview.needsPerson.heading` | Needs a person now | بحاجة إلى الفريق الآن |
| 39.7a | `overview.needsPerson.more.few` | and {$count} more in the inbox | و{$count} محادثات أخرى في صندوق المحادثات |
| 39.7a | `overview.needsPerson.more.many` | and {$count} more in the inbox | و{$count} محادثة أخرى في صندوق المحادثات |
| 39.7a | `overview.needsPerson.more.one` | and {$count} more in the inbox | ومحادثة أخرى في صندوق المحادثات |
| 39.7a | `overview.needsPerson.more.other` | and {$count} more in the inbox | و{$count} محادثة أخرى في صندوق المحادثات |
| 39.7a | `overview.needsPerson.more.two` | and {$count} more in the inbox | ومحادثتان أخريان في صندوق المحادثات |
| 39.7a | `overview.needsPerson.open` | Open the inbox | فتح صندوق المحادثات |
| 39.7a | `overview.usage.heading` | AI usage this month | استخدام الذكاء الاصطناعي هذا الشهر |
| 39.7a | `overview.usage.limit` | Plan limit: {$limit} | حدّ الباقة: {$limit} |
| 39.7a | `overview.usage.unlimited` | No monthly limit on this plan | لا حدّ شهريًا في هذه الباقة |
| 39.7a | `reports.announce.downloaded` | {$title} downloaded. | تمّ تنزيل تقرير «{$title}». |
| 39.7a | `reports.downloading` | Downloading… | جارٍ التنزيل… |

#### Removed (73 keys, no longer shown)

`common.saveChanges`, `customer.candidate.facts`, `entry.example.answer`, `entry.example.caption`, `entry.example.customer`, `entry.example.followUp`, `entry.example.question`, `entry.example.reply`, `inbox.heading`, `lab.badge.active`, `lab.badge.paused`, `lab.buttons.large`, `lab.buttons.note`, `lab.buttons.small`, `lab.colour.action`, `lab.colour.borders`, `lab.colour.note`, `lab.colour.sideBySide`, `lab.colour.sideBySide.note`, `lab.colour.surfaces`, `lab.colour.voiceAi`, `lab.colour.voiceHuman`, `lab.content`, `lab.content.badges`, `lab.content.card`, `lab.content.card.body`, `lab.content.card.subtitle`, `lab.content.dataList`, `lab.content.dataList.conversation`, `lab.content.dataList.handledBy`, `lab.content.dataList.lastActivity`, `lab.content.menu`, `lab.content.toolbar`, `lab.feedback`, `lab.feedback.short`, `lab.forms`, `lab.forms.radio`, `lab.intro`, `lab.mark.note`, `lab.shape.card`, `lab.shape.elevation`, `lab.shape.overlay`, `lab.transcript`, `lab.type.note`, `lab.voices`, `lab.voices.chips`, `lab.voices.sara`, `lab.voices.short`, `lab.voices.takeOver.note`, `lab.voices.track`, `nav.closeNavigation`, `nav.conversations`, `nav.group.channels`, `nav.group.work`, `nav.pinSidebar`, `nav.unpinSidebar`, `overview.action.needsPerson`, `overview.attention.needsPerson.detail`, `overview.attention.needsPerson.few`, `overview.attention.needsPerson.many`, `overview.attention.needsPerson.one`, `overview.attention.needsPerson.other`, `overview.attention.needsPerson.two`, `overview.description`, `overview.description.named`, `overview.heading`, `overview.now.link`, `overview.now.note`, `overview.week.heading`, `overview.week.link`, `shell.navigationButton`, `title.conversations`, `widget.greeting.placeholder`
