# Phase 36.11: Polish (Arabic completeness, motion, accessibility and performance)

Date: 2026-10-20. Branch `master`. Not committed (see section 9).
Evidence: `C:\Users\mouay\Desktop\omniai-redesign-evidence\p36-11\` (written `<evidence>` below).

## 1. Summary

- Arabic reads as Arabic: all 2,028 messages were read through and 113 corrected, among them a misspelt verb on every Knowledge screen («فهرِس»), a support role that read as an AI agent, a sentence that said the opposite of what it meant, and counted nouns that were wrong for most numbers. The extraction now has no duplicate messages.
- The agent editor's "Customer view" is now the same chat customers use in the widget and on the hosted page, in the workspace's own colour. The Overview names limits exactly as Plan & usage does.
- Frequent actions are quicker (menus, the bell, the palette, toasts and a new message now take 150 ms or less, where some took 220 ms), and the Overview no longer jumps while it loads (layout shift 0.13 down to 0.00).
- Accessibility holds everywhere: axe finds no WCAG violation on 164 pages (every route family, both languages, both themes). The product name and the workspace are reachable by landmark, the account button can be named by voice, and a customer page no longer scrolls sideways at 320 px.
- A deep-linked conversation receives its transcript 0.8 s sooner at 4× CPU (the reads no longer wait for each other). It is drawn at about the same time, because the remaining cost is rendering work, not requests.

## 2. Starting point

- Branch `master`, HEAD `fb2d416` ("feat: show what access means before it is given"), the accepted Phase 36.10 checkpoint. Worktree clean.
- Stack up (API `/health/ready` 200, dev server 200). The provider canary passed before any traffic (printed in the session, not saved to a file), again after the first connection drop (`<evidence>\canary-after-drop.log`), and at the end (`canary-end.log`).
- The connection dropped twice. Both times the machine had not restarted: the worktree, the dev server, the API and my measurement servers were intact, and every background run had finished and written its output, so nothing was re-run.
- Baseline production build: no warnings; initial 313.31 kB raw / 73.16 kB transfer. Startup JavaScript (loader + application chunk) 415,973 B raw, 136,853 B gzip, 121,094 B brotli (`startup-js-baseline.json`).
- Baseline lints (`<evidence>\lint\*-before.md`): token lint 16 warnings; RTL lint 1 warning; motion audit 1 medium, 2 low; extraction printed **four** duplicate-ID notices, not the three the prompt lists (`notifications.loading` was the fourth).
- axe-core and Lighthouse were not project dependencies. With your approval, axe-core 4.12.1 was installed in `<evidence>\tools\` (outside the repository). Lighthouse came from the Chrome DevTools MCP, so nothing was installed for it.

## 3. Changes by area

`git status --short` at the end (81 modified, 4 new; paths under `frontend/src/app/` shortened):

```
 M app.config.ts
 M core/api/tenant-api.ts
 M core/auth/auth-service.ts|.spec.ts
 M core/auth/session-expiry-interceptor.ts|.spec.ts
?? core/auth/session-probe.ts|.spec.ts
 M core/authorization/permission-api.ts, permission-service.ts
 M core/feedback/confirm-dialog.scss, toast-host.scss
?? core/navigation/route-warmup.ts|.spec.ts
 M core/workspace/workspace-context-service.ts|.spec.ts
 M features/ai-agents/agent-editor/agent-editor.html|.ts|.spec.ts, agent-preview.html|.scss|.ts|.spec.ts
 M features/analytics/analytics-dashboard/analytics-dashboard.html|.ts, provider-usage/provider-usage.scss
 M features/conversations/conversation-detail/conversation-detail.ts|.spec.ts
 M features/customers/customer-detail/customer-detail.scss
 M features/design-lab/design-lab.scss|.ts
 M features/embed/widget-loader.spec.ts
 M features/notifications/notifications-page/notifications-page.html|.ts
 M features/overview/overview.html|.scss|.ts|.spec.ts
 M layout/app-shell/app-shell.spec.ts
 M layout/profile-menu/profile-menu.ts|.spec.ts
 M layout/sidebar/sidebar.html|.scss, layout/topbar/topbar.html|.scss
 M layout/notification-bell/notification-bell.scss, command-palette/command-palette.scss
 M shared/chat/chat-panel.html|.scss|.ts|.spec.ts, chat-transcript.scss
 M shared/voice/voice-split.ts, voice-track.ts, shared/page-header/page-header.scss
 M 20 more component stylesheets (radius role names, hairline gap)
 M frontend/src/styles/_tokens.scss, _primitives.scss, _authoring.scss, _admin.scss, _base.scss, _charts.scss
 M frontend/src/locale/messages.json, messages.ar.json, GLOSSARY.md
 M frontend/scripts/i18n-check.mjs
 M docs/design/MASTER.md, docs/architecture/admin-design-system.md
?? docs/phase-reports/PHASE_36_11_POLISH_QA_REPORT.md
```

Nothing in `backend/`, `deploy/`, the database, endpoints, permissions or business rules changed. No package was added to the project. No record was created or changed: every check was a read.

**1. Arabic.**

- **Duplicate IDs (4 → 0).** All four were the same text with different surrounding whitespace, which Prettier controls. The second use of each now binds a `$localize` constant with identical text: the analytics table headers, the notifications page's loading line, and the agent preview heading (`PREVIEW_HEADING`, shared by the preview and the editor's no-permission card).
- **Untranslated IDs: 0** (`i18n:check`). A scan for user-facing text that skips i18n (template text outside `i18n`, literal `placeholder`/`title`/`aria-label`-style attributes, literal strings in TypeScript) found only key caps inside `<kbd>`, a URL path, the brand name, an example URL placeholder and Design Lab sample text (`<evidence>\lint\untranslated-scan.txt`).
- **Read-through.** Two reviewers read 1,014 messages each against the English and the glossary (`<evidence>\arabic\`). I checked every finding before applying it, and 113 messages changed (`arabic\fixes.json`, `apply.mjs`):
  - meaning errors: «وكيل دعم» for a human role (وكيل is only an AI agent), «واحد لكل مساحة عمل» which said "one each" for "the same in every workspace", «الآن» for "just now";
  - misspellings: the imperative «افهرس» corrected to «فهرِس» in three places, «ساري» to «سارٍ»;
  - grammar: «يحتاج إلى صلاحية» throughout;
  - counted nouns rewritten so they are right for any number;
  - one form for "access denied" headings and for "ask an administrator";
  - one word each for deployment («التثبيت»; «النشر» is Publish), UTC («بالتوقيت العالمي») and Suspended («معلّق»), now in `GLOSSARY.md`;
  - singular address of the reader everywhere.
- **Truncation.** The matrix now records every ellipsis cut. The only real one was the search field's label at 768–1199 px in both languages (192 px, where 221 px was needed). The field is now 14rem, and the label fits whole. The other cuts are deliberate: the collapsed rail's transparent labels, the inbox's one-line previews, and a long workspace name on a phone.

**2. RTL.** `rtl_logical_lint.py` warning 1 → 0: the bell panel header's padding shorthand, which gave the title 12 px and the button 16 px in Arabic (the reverse of English); it is now logical. Measured in the page (`flow\rtl-motion.json`):
- every directional icon flips in Arabic and none flips in English, and no other icon flips;
- all LTR islands (addresses, codes, URLs) compute LTR inside Arabic pages;
- the daily chart puts the latest day at the reading end and the value axis at the reading start;
- the switch thumb travels toward the reading end.

The 10 informational `letter-spacing` notes use `--letter-spacing-tight`, which is 0 under `:lang(ar)`.

**3. Consistency.**

- **One customer chat.** `AgentPreview` now renders `ChatPanel` (widget look) with its own single-turn exchanges as messages, the workspace's accent through `checkAccent` (an invalid value never reaches a style; new spec), the greeting, the honesty line and the panel's typing state. `ChatPanel` gained `infoKinds`: the preview shows an unconfigured provider as information, while visitors are still told a missing answer as a failure (new specs on both sides).
  - The whole `.customer-view` layer (about 150 lines) left `_authoring.scss`; the frame is 10 lines.
  - Before: `p36-08\matrix-editor-rerun\owner-en-light-1440-agent-editor.png`. After: `<evidence>\matrix\en\owner-en-light-1440-ai-agent-editor.png`, `keyboard\agent-preview-en.png`, `keyboard\agent-preview-ar.png`.
  - The preview's inner test IDs are now the panel's (`chat-input`, `chat-send`, `chat-greeting`, `chat-error`, inside `agent-preview`); `agent-preview`, `preview-clear`, `preview-unsaved` and `preview-look` are kept, and `preview-frame` is new.
- **One set of limit words.** Overview's attention cards take names, state words, figures, consequences and meter states from `plan-words.ts` ("Members: Limit reached", "AI agents: Approaching the limit"). The Overview's own names, which had drifted («الحد مستنفد» against «بلغ الحد»), are gone. New spec: a monthly limit that ran out says AI work stops until the month resets.
- **Tokens.** `token_lint` 16 warnings → 0:
  - eleven raw 2 px gaps became `--gap-hairline`, a named role beside the spacing scale, since the scale says "nothing in between";
  - the segmented control's 3 px inset is annotated with its reason, and `#4821` (an order number in Lab sample text) is annotated;
  - the radius size names (`--radius-sm` … `-xl`, 77 uses) were moved to the role names they already resolved to, so no pixel changed;
  - six unused tokens were removed: `--shadow-raised`, `--shadow-subtle` (resolved to nothing), `--icon-stroke`, `--letter-spacing-caps` (marked legacy), `--drawer-width`, `--z-modal`.
  - The 6 remaining infos are the pre-CSS boot frame's fallbacks in `index.html` and `boot-state.ts`, which must work before any stylesheet loads.

**4. Motion.** `motion_audit` 1 medium + 2 low → 0.
- Every frequent action is within 150 ms: menus and the bell panel (were 220), the command palette (220), toasts (220), a new message settling (220), the switch thumb (220), and the rail peeking open (220 in / 320 back, now 100 / 150).
- Rarer moments keep their length: the widget opening, the dialog scrim, dialogs and the phone drawer.
- Three patterns are annotated with their reasons: the rail's 0 s width snap (never an animated size), the spinner (removed with the work), the skeleton sweep (none under reduced motion).
- Under reduced motion, 0 of 423 elements keep any transition or animation over 1 ms.

**5. Accessibility.**
- **Landmarks:** the whole sidebar is the navigation landmark (it held only the links; the product name sat outside every landmark on 132 pages), and the top bar is the banner (`role="banner"`, because the bell panel has its own `<header>`).
- **Account button:** its name now begins with the initials it shows, "AO: Account menu for …" (WCAG 2.5.3, found by Lighthouse; axe has the rule only as experimental).
- **Reflow:** the customer page scrolled sideways at 320 px (an implicit grid track grew to a text field's intrinsic width, plus an unwrappable button label). Both are fixed: `minmax(0, 1fr)` and a wrapping label.

**6. Performance.**
- **Loading geometry:** Overview's loading state now holds its loaded size. Its 30-day chart card used to be absent until data came (416 px of shift). Measured at six widths, every section is within 3 px except the number of attention cards on a phone, which cannot be known ahead (`perf\overview-skeleton-geometry.txt`).
- **The deep-link chain:** the antiforgery token and the session are asked for together. A browser that holds a workspace selection reads the workspace list and its permissions alongside the session check, as probes (`session-probe.ts`: the interceptor ignores a probe's 401, and entry pages never probe), and warms the deep link's lazy chunks (`route-warmup.ts`). A conversation asks for its facts and its first transcript page together.

**Test reliability.** No whole-module `vi.mock` remains (36.7 removed the last one). One leak of the same family was fixed: `widget-loader.spec.ts` defined `currentScript` on the shared `document` and "restored" the prototype instead, so the last script element outlived the file in its worker.

**Docs.** `admin-design-system.md` gained D25 (this phase) and corrected tokens, shadows, layers, motion and the widget accent; `MASTER.md` has the final status, the refreshed contrast evidence, the 150 ms rule, the hairline gap and seven decision rows.

## 4. Decisions

1. **A named hairline gap, not a half step of the scale.** The scale says "nothing in between"; a 2 px role token for things that read as one keeps that true. Rejected: rounding eleven gaps to 0 or 4 px (a visible change in eleven places) or annotating each one.
2. **The preview's failure tone is the host's choice (`infoKinds`), not a rule in the panel.** To a visitor a missing answer is a failure; to an administrator an unconfigured provider is a fact. Rejected: a second alert outside the panel, which would bring back two chats.
3. **Probes only where a session is likely and useful.** Read-ahead runs only with a stored selection and never on entry pages. The first matrix caught the entry-page case (two extra 401s), which is why the rule exists. Rejected: reading ahead always, which would log 401s for every signed-out visitor.
4. **The deep link is reported as it is.** The reads now overlap, and the transcript arrives about 0.8 s sooner, but it is drawn at the same time within noise. Measured, the main thread is the limit: about 1.2–1.9 s of layout (two full relayouts of about 2,181 objects, one of them when the Arabic font arrives for Arabic customer text on an English page), about 1.1 s of framework script, and 0.7–0.9 s of change detection. Fixes that would move it are listed in section 8, not attempted under time pressure: a smaller first transcript window, preloading the Arabic font, and one shared hub connection.
5. **Before/after timing interleaves the two builds** (A, B, A, B … in one Chrome). The machine was at 90–98% CPU from the desktop browser during the first attempt, and a sequential before-then-after run showed a 2.6 s "regression" that was pure load.
6. **The 36.1 code was rebuilt with today's toolchain for the size comparison** (in the evidence folder, from `git archive d2c907d`). It reproduces 36.1's recorded 439.46 kB initial total and its known `embed-chat.scss` budget warning.
7. **Kept as is:** the 401 session probe on signed-out pages (needs a server answer; backend ask 1), the embed document without a `main` landmark (a frame holding one launcher button), the widget loader's postMessage warning during a viewport change (untouched code; section 8).
8. **Deferred:** a web-vitals RUM module, which needs a package (`perf_scan` LOW).

## 5. Verification

| Check | Result |
| --- | --- |
| `npm run format:check` | **PASS** (`final-format.log`). |
| `npm run test:ci` | **PASS, three runs in a row**: 93 files, 1,101 tests, 0 failures each (`final-test-1/2/3.log`; 91 / 1,081 at 36.10). New: preview on the shared panel (invalid accent never painted, greeting, info tone, error tone), `ChatPanel` tones, Overview limit words and monthly consequence, parallel conversation reads, token and session together, prefetch probes (used, re-read on failure, discarded on a changed selection), probe 401 ignored, `readsAhead`, route warm-up matching, landmarks, the account label. |
| `npm run build` | **PASS**: no warnings, no budget raised. Initial 313.31 → 310.48 kB raw (73.16 → 72.79 kB transfer). One component (`design-lab.scss`) went 28 B over 4 kB mid-phase; two identical rules were merged instead. |
| Startup JavaScript | **PASS**: 136,853 → 137,638 B gzip (+0.57%), brotli 121,094 → 121,845 (+0.62%), raw 415,973 → 417,841 (`startup-js-*.json`). |
| Sizes vs 36.1 (`perf\sizes.json`) | Initial total 439.46 kB / 108.20 kB transfer → 310.48 / 72.79 (since 36.3b "initial" is the loader only). Startup JavaScript 113.6 → 137.6 kB gzip (+21% over the whole redesign). CSS 11.2 → 17.2 kB gzip. Fonts 0 → 5 self-hosted WOFF2 (Instrument Sans Latin 57 kB on every page; IBM Plex Sans Arabic 43–46 kB per weight, only when Arabic text is drawn). All JavaScript 309 → 468 kB gzip across 138 lazy files. |
| `i18n:check` / extraction | **PASS**: 2,028 messages, 0 errors, 0 warnings; 0 duplicate-ID notices (4 at the start). |
| Chrome matrix (`<evidence>\matrix\`, `matrix-rerun\`) | **PASS**, 780 cells: 39 routes (33 signed-in families, 6 public) × 1920/1440/1366/768/390 × light/dark × en/ar, all widths in Arabic. Checks per cell: page overflow (document **and** `main`, new), clipped content, console errors, exactly one `h1`, unnamed controls, small shell targets. <br>• **First full run:** 720 signed-in cells clean. 60 entry-page cells failed on console errors (two probe 401s each, a regression of this phase); fixed. <br>• **After the last two code changes** (entry-page probes, search field width) the affected slice was re-run: 252 cells (every signed-in route at 768 px, every public cell), 0 issues. <br>• The `/api/auth/me` 401 session probe on the 60 entry-page cells is counted apart as an expected network log, as in 36.1/36.5, and never hidden. |
| axe-core 4.12.1 (`axe\axe.json`) | **PASS**: 164 pages (33 route families + 2 overlays + 6 public pages, en/ar × light/dark at 1440), **0 WCAG violations**. Best practice: `region` 136 → 0 after the landmark fix; 4 cells left, the embed document's launcher-only frame (decision 7). |
| Lighthouse (desktop, production build) | Sign-in: Accessibility 100, Best practices 96 (the `/me` 401), SEO 82. Overview before fixes: Accessibility 100 with label-in-name failed, CLS 0.177. **Overview final: Accessibility 100, Best practices 100**, label-in-name and CLS pass. SEO 82 on both: no meta description (an authenticated app), plus robots.txt/llms.txt, which my static measurement server answered with the app's HTML (`perf\lighthouse\`). Performance comes from traces, below. |
| Traces | Overview CLS 0.13 → **0.00** (trace). Long transcript and lists at 4× CPU (`perf\scroll.json`): the 232-message transcript and the customers, knowledge and team lists scroll at 17 ms frames (p50, p95 and max), CLS 0, en and ar. |
| Deep-linked conversation (`perf\deeplink-ab1.json`, 9 interleaved runs each, 4× CPU, cold) | First transcript page requested 2,069 → 1,437 ms; last page received 2,879 → 2,086 ms; session + workspace + permissions 772 → 726 ms; route code started 940 → 735 ms. **Newest message drawn 3,779 → 3,894 ms: unchanged within noise** (decision 4). Main-thread profile: `perf\main-thread-*.json`. |
| Contrast (`contrast\`) | **PASS**: all 123 pairs from every phase × light, dark, light + more, dark + more: 113 pass, 10 informational (decorative edges), 0 fail, 0 resolution errors in each. Tightest text 4.52:1 (AI voice as text on the page, light); tightest UI 3.08:1 (chosen candidate edge, dark). |
| Keyboard (`keyboard\keyboard.json`) | **PASS** in en and ar. <br>• Focus sweep: 884 stops across 20 key pages, every one visible, `:focus-visible` and on screen. <br>• Skip link → main. <br>• Ctrl+K, type, Enter → Knowledge, focus on its heading. <br>• Inbox: Tab to a row, Enter opens it, focus kept. <br>• Agent preview: Tab into the box, type, Enter sends; "not configured" shown as information, message kept. <br>• Sign-in by keyboard. |
| 200% zoom, 320 px reflow (`reflow\`) | **PASS** after the customer fix: 12 key pages + 3 public pages × en/ar × 720 px (200%) and 320 px, page overflow 0 (now including `main`), clipped 0. The install snippet scrolls inside its own `pre`; the phone's visually hidden handoff help is intentional. |
| Reduced motion, RTL mirroring (`flow\rtl-motion.json`) | **PASS**: 0 of 423 elements over 1 ms under reduce; frequent actions at 150 ms; icons, islands, chart and switch as in section 3.2. |
| Lints (`lint\*-final.md`, `*-after.md`) | **PASS**: token lint 0 warnings (6 justified infos); RTL lint 0 warnings; motion audit 0; `perf_scan` 0 critical/high (3 medium font findings and the `@defer` placeholder confirmed false positives; RUM deferred); `hero_check` 0; bundle budget initial 310.5 kB. |
| Security (`security_scan.py`, `lint\security-*.md`) | **PASS**: 0 critical or high. Against the 36.1 code, the only new product findings are the website-chat install sentence (it mentions CSP) inside the two message catalogues; no new sink. Product code has no `innerHTML`, `outerHTML`, `insertAdjacentHTML`, `bypassSecurityTrust*`, `eval` or `new Function`. |
| Test reliability | **PASS**: no whole-module `vi.mock`; the `currentScript` leak fixed; 3 consecutive full runs green. |
| Widget on a host page (`widget\`) | **PASS** for overflow and layout, closed and open, en/ar, light/dark, 1440/390. 4 captures logged a Chrome security warning (a postMessage dropped while the frame was mid-load during the script's viewport change); not an error and not from this phase's code. |
| Provider canary | **PASS** at start, after the drop and at the end: `OPENAI_API_KEY`, `STRIPE_SECRET_KEY`, `STRIPE_WEBHOOK_SECRET`, `OMNIAI_WHATSAPP_APP_SECRET`, `OMNIAI_WHATSAPP_VERIFY_TOKEN`, `OMNIAI_LIVE_*`, `Email__Smtp__*` absent (names only). No provider was called. |
| Skills | `frontend-testing-quality`, `ui-ux-design-lead`, `motion-interaction`, `web-performance` loaded and followed. `design-system-tokens`, `frontend-security` and `first-impression-experience` were used through their scripts (`token_lint`, `contrast_check`, `rtl_logical_lint`, `security_scan`, `hero_check`) without loading the skill text. |

**Final critique** (ui-ux-design-lead rubric, 1–5; the same ten screens as 36.1; evidence: matrix captures, widget captures, the checks above):

| Screen | Hierarchy | Clarity | Consistency | Accessibility | Responsive/RTL | Craft | Distinctiveness | Performance | Average (36.1) |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Overview | 4 | 5 | 5 | 5 | 5 | 4 | 4 | 4 | **4.5** (3.4) |
| Conversations list | 4 | 4 | 4 | 5 | 5 | 4 | 4 | 4 | **4.3** (2.6) |
| Conversation detail | 5 | 4 | 5 | 5 | 5 | 4 | 5 | 3 | **4.5** (2.8) |
| AI agent editor | 4 | 5 | 5 | 5 | 5 | 4 | 4 | 4 | **4.5** (3.3) |
| Knowledge list | 4 | 4 | 5 | 5 | 5 | 4 | 4 | 4 | **4.4** (3.4) |
| Channels | 4 | 5 | 5 | 5 | 5 | 4 | 4 | 4 | **4.5** (3.1) |
| Analytics | 4 | 4 | 5 | 5 | 5 | 4 | 4 | 4 | **4.4** (3.1) |
| Sign-in | 5 | 5 | 5 | 5 | 5 | 4 | 4 | 4 | **4.6** (3.3) |
| Hosted chat | 4 | 4 | 5 | 5 | 5 | 4 | 4 | 4 | **4.4** (2.9) |
| Widget (open and closed) | 4 | 4 | 5 | 5 | 5 | 4 | 4 | 4 | **4.4** (3.0) |

Every screen meets the admin ship bar (nothing below 3; accessibility, responsive/RTL, clarity and consistency 4+), and every axis improved or held on every screen. **Still below target:**
- Conversation detail performance 3: a deep link at 4× CPU takes about 3.8 s to draw, and it is CPU-bound.
- Conversations list clarity 4: rows still cannot show who the customer is (backend ask 2).
- Distinctiveness 4, not 5, on most screens: the identity is the voices, and admin screens rightly stay quiet.
- Performance 4 everywhere: startup JavaScript +21% and two font families since 36.1.

## 6. See it live

- http://localhost:4200/ai-agents, then "Northwind Concierge": the Customer view is the shared chat. Press Enter on a message: information, message kept.
- http://localhost:4200/overview: limit words come from Plan & usage (this stack is unlimited; the Design Lab's `#lab-admin` shows the states as labelled sample). Reload with the network throttled: the loading state keeps its shape.
- http://localhost:4200/conversations/01a15013-917d-75a7-870c-98ceb0b9e7f8: the 232-message conversation.
- http://localhost:4200/customers: open one at 320 px wide; no sideways scroll.
- Switch to Arabic from the account menu (the "PO" button) to read the corrected copy: Knowledge, Roles, Plan & usage, Reports.
- http://localhost:4200/design-lab: the hairline gap, radius roles and motion durations.
- Accounts: `C:\Users\mouay\Desktop\omniai-redesign-evidence\p36-11\demo-accounts-p36-11.md`, which points to `p36-01\demo-accounts.md` for the password. The dev server (`npm start`, :4200) and the API (`omniai-p36-api`, 127.0.0.1:5027) are running.

## 7. Backend asks

1. **A session probe that answers without a 401** (for example `200 { authenticated: false }` on `/api/auth/me`), so signed-out pages log no console error (36.1 C-6, still open).
2. **Customer identity on conversation list items** (36.1): the list's clarity is capped at 4 without it.
3. **The latest transcript page first** (a `before` cursor or a descending read), so a long conversation can draw its newest messages without paging forward from the first.
4. **Failed AI turns in the transcript** (36.1): a marker where no answer was sent.
5. Still open from earlier phases:
   - who holds a role, an invitation status filter, report type on notifications, translated permission catalogue, fallback plan name, "OmniAI:" in e-mail subjects (36.10);
   - widget appearance on the hosted read, a translated loader frame title, workspace display name on public reads, coded origin refusals (36.9);
   - persisted indexing outcome, source file size, channels per agent, coded file refusals (36.8);
   - purpose breakdown, comparison period, localised report catalogue, first-activity date (36.7);
   - the NuGet-audit transitive package (36.1).

## 8. Risks and open issues

- **The deep link is still about 3.8 s at 4× CPU.** It is CPU-bound, and three concrete fixes are measured but not built:
  - draw a smaller first window of the transcript (100 → about 40 messages; the transcript's first layout costs about 320 ms at 4×);
  - load the Arabic font early on inbox pages when the workspace has Arabic conversations (its late arrival forces a full relayout costing about 470 ms at 4×);
  - share one SignalR connection between the bell and the inbox, which today negotiate the same hub twice.
- **Read-ahead probes:** a browser whose session ended while it kept a workspace selection, and which opens a deep link, now logs two extra 401s beside `/me`'s before the sign-in page. Nothing is used from them, and entry pages never probe.
- **One attention card is held while Overview loads.** With two or more items on a phone, the page below still moves when they arrive.
- **The Arabic read-through was done by model reviewers and checked by me, not by a native speaker.** The terms that matter are now in `GLOSSARY.md`; a native review remains worthwhile.
- **Sizes since 36.1:** startup JavaScript +21% gzip and two self-hosted font families. Each phase stayed under the 10% rule; the total is reported here as asked.
- **No RUM:** field data for Core Web Vitals needs a `web-vitals` package (ask before adding).
- **Widget loader warning:** a postMessage to a frame that is still loading is dropped with a Chrome security warning. It is harmless, but the loader could wait for the frame's load event.
- Scratch tooling and builds are in `<evidence>\scratch\` (including the rebuilt 36.1 code; its `node_modules` link was removed).

## 9. Proposed checkpoint commit message (not committed)

```
feat: finish the redesign in both languages, quietly and quickly

Arabic is read through: all 2,028 messages checked against the English and
the glossary, 113 corrected (the imperative فهرِس, a human role no longer
called an agent, one word each for deployment, UTC and Suspended, counted
nouns right for every number), and no message is extracted twice.

The agent editor's Customer view is the shared customer chat, in the
workspace's colour, saying an unconfigured provider as information. The
Overview names limits with Plan & usage's own words. Tokens lose their old
names and dead entries and gain a hairline gap; token, RTL and motion lints
are clean, and every frequent action takes 150 ms or less.

The sidebar is the navigation landmark and the top bar the banner, the
account button can be named by what it shows, a customer page reflows at
320px, and the Overview's loading state keeps its final shape (layout shift
0.13 to 0). Startup asks for the token, the session and, when a workspace is
already chosen, its list and permissions together, warms the deep link's
code, and reads a conversation and its first page in parallel.

axe: 0 WCAG violations on 164 pages; Chrome matrix 780 cells in both
languages and themes. Frontend only.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```
