# Phase 36.2: Design foundation ("Two Voices" identity, tokens, type, primitives and the Design Lab)

Evidence folder: `C:\Users\mouay\Desktop\omniai-redesign-evidence\p36-02\` (written `<evidence>` below).
Design brief: `docs/design/MASTER.md` (new).

## 1. Summary

- Every screen now speaks "Two Voices": calm ink on a porcelain page (and a separately designed "night shift" dark), with the indigo accent gone and colour kept for meaning only.
- Who is answering is visible at a glance: Lagoon means the AI and Ember means a person, always with a glyph and the word ("AI", "Person"). A conversation taken over by a person now shows an Ember "Person" chip and edge in the inbox.
- Type has character and stays comfortable for long shifts: self-hosted Instrument Sans (narrowed headings, tabular figures) and IBM Plex Sans Arabic, with no font service contacted and no layout shift on load.
- A new OmniAI mark (two speech shapes forming an "O") is in the sidebar, sign-in, favicon and loading state. Page changes crossfade in 150 ms, and the effect is off under reduced motion.
- Reviewers get a development-only Design Lab (`/design-lab`) showing the whole system in light, dark and RTL. It is absent from the production build.

## 2. Starting point

- Branch `master`, HEAD `26c680f docs: record the Phase 36.1 redesign baseline` (36.1 accepted, its report present).
- This phase ran in two sessions. The first stopped before its report: an API authentication error, then VS Code was closed. The recovery session began on 2026-10-18 and found:
  - 38 modified files and 4 untracked paths (`frontend/dark.md`, `shared/brand-mark/`, `src/assets/`, `styles/_fonts.scss`), all from this phase;
  - no commit since `26c680f`;
  - containers `omniai-p36-api`, `omniai_pg25` and `omniai_redis18` up, with `http://localhost:4200` → 200 and `http://localhost:5027/health` → 200, so nothing needed restarting.
- What the first session had finished:
  - fonts (Option B, approved at the gate);
  - tokens, the base and primitive layers;
  - the brand-mark component;
  - icon stroke and the voice and channel glyphs;
  - the first contrast run;
  - the `--accent` remap (`scratch\remap_accent.py`, which printed "done").
- What it had not done:
  - MASTER.md, favicon and loading state;
  - router View Transitions wiring;
  - voice components, the Design Lab and the docs;
  - verification and this report.
- `frontend/dark.md` was an empty (0-byte) file created by mistake in the first session's contrast step. It was removed; nothing else was discarded.

## 3. Changes by area

`git status --short` at the end of the phase: 45 tracked files modified (`+1212 / −585`) and 7 new paths:

```
 M docs/architecture/admin-design-system.md
 M frontend/public/favicon.ico
 M frontend/src/app/app.config.ts
 M frontend/src/app/app.routes.ts
 M frontend/src/app/core/feedback/confirm-dialog.scss
 M frontend/src/app/core/feedback/toast-host.scss
 M frontend/src/app/core/notifications/notification-item.ts
 M frontend/src/app/features/ai-agents/agent-editor/agent-preview.scss
 M frontend/src/app/features/analytics/analytics-dashboard/analytics-dashboard.scss
 M frontend/src/app/features/auth/auth-page.scss
 M frontend/src/app/features/auth/login/login.html
 M frontend/src/app/features/auth/login/login.ts
 M frontend/src/app/features/auth/register/register.html
 M frontend/src/app/features/auth/register/register.ts
 M frontend/src/app/features/billing/billing-page/billing-page.scss
 M frontend/src/app/features/chat/hosted-chat/hosted-chat.html
 M frontend/src/app/features/chat/hosted-chat/hosted-chat.scss
 M frontend/src/app/features/chat/hosted-chat/hosted-chat.ts
 M frontend/src/app/features/conversations/conversation-detail/conversation-detail.scss
 M frontend/src/app/features/conversations/conversation-list/conversation-list.html
 M frontend/src/app/features/conversations/conversation-list/conversation-list.scss
 M frontend/src/app/features/conversations/conversation-list/conversation-list.ts
 M frontend/src/app/features/invitations/accept-invitation/accept-invitation.html
 M frontend/src/app/features/invitations/accept-invitation/accept-invitation.ts
 M frontend/src/app/features/knowledge/agent-knowledge/agent-knowledge.scss
 M frontend/src/app/features/notifications/notification-preferences/notification-preferences.scss
 M frontend/src/app/features/notifications/notifications-nav.ts
 M frontend/src/app/features/plan-usage/plan-usage-page/plan-usage-page.scss
 M frontend/src/app/features/reports/reports-nav/reports-nav.ts
 M frontend/src/app/features/reports/reports-page/reports-page.scss
 M frontend/src/app/features/workspace-setup/workspace-setup.html
 M frontend/src/app/features/workspace-setup/workspace-setup.scss
 M frontend/src/app/features/workspace-setup/workspace-setup.ts
 M frontend/src/app/layout/notification-bell/notification-bell.scss
 M frontend/src/app/layout/profile-menu/profile-menu.scss
 M frontend/src/app/layout/sidebar/sidebar.html
 M frontend/src/app/layout/sidebar/sidebar.scss
 M frontend/src/app/layout/sidebar/sidebar.ts
 M frontend/src/app/shared/icon/icon-paths.ts
 M frontend/src/app/shared/icon/icon.ts
 M frontend/src/index.html
 M frontend/src/styles.scss
 M frontend/src/styles/_base.scss
 M frontend/src/styles/_primitives.scss
 M frontend/src/styles/_tokens.scss
?? docs/design/
?? frontend/public/favicon.svg
?? frontend/src/app/features/design-lab/
?? frontend/src/app/shared/brand-mark/
?? frontend/src/app/shared/voice/
?? frontend/src/assets/
?? frontend/src/styles/_fonts.scss
```

Nothing in `backend/`, `deploy/`, the database, endpoints, permissions or `package.json` changed. No npm package was added.

### 3.1 Design brief (`docs/design/MASTER.md`)

It contains:
- product and audience;
- the direction and concept, and the six signature elements with their rules;
- palette roles (light and dark) and key contrast evidence;
- Latin and Arabic type roles, and the numerals policy;
- spacing and density, shape and elevation;
- the motion stance and content voice;
- accessibility and RTL commitments, and ten enforced rules;
- the AI-default list to avoid;
- a decision log of nine rows.

### 3.2 Fonts (Option B, approved)

- Files: five WOFF2 files and two OFL texts in `frontend/src/assets/fonts/`:
  - Instrument Sans 5.3.0 variable (`wdth` 75–100, `wght` 400–700): Latin 57,332 B and Latin Extended 18,856 B;
  - IBM Plex Sans Arabic 5.3.0, 400/500/600, Arabic subset: 42,848 / 45,296 / 45,688 B.
- `src/styles/_fonts.scss` declares them:
  - each face has a `unicode-range`, and the Arabic range is Arabic only;
  - `font-display: swap`;
  - metric-matched fallbacks (`size-adjust`, `ascent-override`, `descent-override`, `line-gap-override`, measured in Chrome against the shipped files).
- The production build emits them with content-hashed names (`browser/media/instrument-sans-latin-wdth-normal-NDB4KJ7D.woff2`, …).
- The edge (`deploy/edge/templates/omniai.conf.template`, `location ~* "\.(?:js|css|woff2?|svg|png|ico|webp)$"`) serves them with `Cache-Control: public, max-age=31536000, immutable`.
- No preload: Angular cannot write the hashed media name into `index.html`, and an unhashed preload would download the font twice.
- Measured in Chrome:
  - sign-in fetched only the Latin file;
  - the Design Lab (Latin + Arabic) fetched the Latin file plus exactly the three Arabic weights it renders;
  - every request went to `localhost:4200`.

### 3.3 Tokens (`src/styles/_tokens.scss`)

- Kept: the architecture and role names (`--surface-*`, `--text-*`, `--border-*`, status), `--direction-flip`, the reduced-motion collapse at the source, and the pre-paint theme contract (`theme-startup.spec.ts` unchanged, passing).
- Added:
  - `--voice-ai-*` and `--voice-human-*` (fg, strong, tint, border);
  - `--action`, `-hover`, `-pressed`, `--on-action`, `--link`, `--link-hover`, `--focus-ring`, `--selection`;
  - `--border-control`;
  - chart tokens (`--chart-ai`, `-human`, `-customer`, `-neutral`, `-grid`, `-ramp-1…5`);
  - radius by role, the overlay-only shadow, `--duration-*` and the four easings;
  - type roles (display, page, section and card titles, body, reading, small, caption, numeric), `--heading-stretch` and the `:lang(ar)` overrides;
  - `--z-skip-link`, `--voice-track-height`.
- Dark ("night shift") is a separate palette. It is one Sass mixin used by both the explicit `data-theme="dark"` and the no-JavaScript media query.
- `--accent-*` remap. I reviewed the diff of `remap_accent.py` and every change is deliberate:
  - **To ink:** the primary action, the notification count and unread dot, tab markers, the switch, the plan meter.
  - **To `--link`:** links.
  - **To neutral:** selection (billing plan pill, profile avatar, report option).
  - **To voices:** "the AI" (agent preview bubble) and "a person" (hosted-chat handover line, transcript human edge, conversation-list edge).
  - **To chart tokens:** analytics bars and swatches. Before, they misused success and warning for AI and people.
  - **Focus rings:** the local ones moved to `--focus-ring`.
  - The edges now mirror with `--direction-flip` instead of separate `[dir='rtl']` rules.
- One use the script missed: `.badge-accent` had been deleted from the primitives, but `conversation-list.html` still rendered `<span class="badge badge-accent badge-sm">Human</span>`, which showed as an unstyled badge. It is now `<app-voice-chip voice="human" size="sm" />`.
- Final grep: no `var(--accent…)`, `--accent-subtle`, `--accent-contrast` or `badge-accent` remains. The only matches are the widget's own `--embed-accent-*`, which is the workspace's colour and is kept on purpose.

### 3.4 Primitives (`_base.scss`, `_primitives.scss`, dialog, toast)

- All the listed primitives are restyled with the same class names and DOM contracts. All 788 pre-existing tests pass unchanged.
- Hover, pressed (1 px settle), focus (2 px ink ring with 2 px offset, drawn with `outline`), disabled and busy states use the motion tokens.
- Links are ink and underlined. `::selection` is neutral.
- New shared rules:
  - transcript `.bubble-*` with logical tail corners;
  - `.handoff-divider`;
  - `.text-display` and `.text-reading`;
  - the route crossfade.
- A wrong comment was corrected. It said the spinner "stops as a still ring" under reduced motion; the spinner deliberately keeps its slow 0.9 s turn (the Phase 33.5 decision), and the docs now say so.

### 3.5 Brand mark

- `shared/brand-mark/brand-mark.ts` (with spec): inline SVG, two pre-cut paths (no mask, no `url(#id)`), `tone="voices" | "mono"`, decorative unless given a `label`, forced-colours fallback, never mirrored.
- Used in the sidebar, sign-in, register, accept-invitation, workspace setup and the hosted chat.
- `public/favicon.svg` is new and follows the browser's colour scheme.
- `public/favicon.ico` was regenerated at 16, 32 and 48 px, rasterised in headless Chrome. The old file is kept at `<evidence>\favicon\favicon-before.ico` and the preview at `<evidence>\favicon\preview.png`.
- `index.html` loading state:
  - the static mark plus "Loading OmniAI…", using inline styles and token colours with literal fallbacks;
  - no inline script was added, so `deploy/edge/csp-hashes.mjs` keeps hashing only the theme script.

### 3.6 Signature components (`src/app/shared/voice/`)

- `voice.ts`: `Voice`, `voiceOf(sender)`, `replyRuns(messages)` and the voice words.
- `VoiceChip`: glyph and word, md or sm, an optional specific label; the 320 ms emphasized colour sweep when the voice changes.
- `VoiceSplit`: a bar with a 2 px surface seam, a legend of chips and counts, and one visually hidden sentence. The bar and legend are `aria-hidden`.
- `VoiceTrack`: 4 px runs derived only from the given messages, a `partial` flag for paged transcripts, and nothing rendered until somebody has replied.
- `voice.spec.ts`: 13 tests.
- One type-only import from `features/conversations` (`MessageSender`), so the sender values cannot drift from the API contract.

### 3.7 Design Lab

- `features/design-lab/` (ts, html, scss).
- Route `design-lab` in `app.routes.ts`. Its condition is `typeof ngDevMode === 'undefined' || ngDevMode`, which is `isDevMode()`'s own body written out. `isDevMode()` itself would still have emitted the lazy chunk.
- It shows:
  - live-resolved colour tokens, radius, spacing, elevation and motion tokens;
  - every icon;
  - the mark at 16/28/48 px and in mono;
  - every button variant × size × icon / disabled / loading;
  - form controls with hint, invalid, disabled, switch, checks, radios and segmented;
  - card, badges, menu, table, data list, toolbar and pager;
  - alerts, stale notice, dialogs (normal and destructive), toasts, skeleton, empty and error states;
  - voice chips, the live "Take over" demo, the voice split (with counts and empty) and the voice track;
  - a sample transcript with handoff divider, marked "Sample, not a real conversation".
- Toggles for theme (System / Light / Dark) and direction (LTR / RTL).
- It reads no API.

### 3.8 Motion wiring

- `app.config.ts`: `withViewTransitions({ skipInitialTransition: true })`.
- `_base.scss`: `.shell-main { view-transition-name: route-content }`, a 150 ms crossfade on both halves, and the root snapshot not animated.
- View Transitions are off under reduced motion.

### 3.9 Documentation

- `docs/architecture/admin-design-system.md`:
  - rewrote the intro, D1, D5–D10 and D16–D19 for the new system;
  - corrected the D12 sentence about the current navigation item;
  - kept D21 and D22 unchanged.

### 3.10 Formatting

- Prettier reformatted 22 files whose only problem was CRLF line endings written by the first session (`--write` on exactly the flagged files). The repository uses LF.

### Before / after screenshots

- Contact sheet, before (36.1) on the left and after (36.2) on the right, all 168 captures: `<evidence>\compare.html`
- After gallery and its checks: `<evidence>\after\index.html`, `<evidence>\after\results.json`. The before gallery is `…\p36-01\before\`.
- First-screen composites: `<evidence>\pairs\` (`owner-overview-1440-light.png`, `owner-conversations-1440-light.png`, `owner-conversation-detail-human-1440-light.png`, `owner-analytics-1440-dark.png`, `owner-ai-agent-editor-1440-light.png`, `owner-overview-390-dark.png`, `public-sign-in-1440-light.png`, `mobile-4up.png`).
- Design Lab: `<evidence>\lab\lab-{1440|390}-{light|dark}-{ltr|rtl}-NN.png` (64 slices), `<evidence>\lab\lab-checks.json`.
- Colour-vision checks: `<evidence>\cvd\sheet-status.png`, `sheet-split-light.png`, `sheet-split-dark.png`.
- Contrast: `<evidence>\contrast\final-{light,dark,light-more,dark-more}.md`, `pairs.json`, `tokens-final.css`, `contrast-table.md`.
- Lints: `<evidence>\lint\`.

## 4. Decisions

- **Fonts, Option B:** the approved approach, as in 3.2. The OFL texts sit beside the files in the repository and are not served.
- **Values adjusted, never the rule:**
  - light `--border-control` `#8b8e95` → `#80838a`: it was 2.79:1 on inset.
  - light `--success-fg` `#2e7d32` → `#2b7a30`: it was 4.49:1 on its background.
- **Unspecified dark values chosen:**
  - `--surface-raised` `#1e2024`;
  - `--action-pressed` `#d9d8d3`;
  - `--danger-solid` `#f07a6e` with a dark label (6.99:1);
  - status backgrounds and borders for dark.
- **Voice seams:** Lagoon and Ember are 1.02–1.04:1 against each other, so adjacent voice areas are separated by a surface hairline (split bar, stacked chart, track).
- **Numerals:** Western digits in both languages, recorded in MASTER as a decision for 36.3 to confirm.
- **Design Lab exclusion:** by the `ngDevMode` condition rather than file replacements. This needs no `angular.json` change and is proven by the chunk list (§5).
- **Route crossfade:** 150 ms on both halves, inside the 150–200 ms window, because navigation is frequent.
- **Kept, not changed:** the spinner keeps turning under reduced motion (33.5 decision; small, essential, travels nowhere).
- **Rejected:**
  - a preload of the Latin font (it would double-download);
  - `isDevMode()` in the route (the chunk would still ship);
  - an `.ico` re-render by ImageMagick's SVG renderer (Chrome renders the mark faithfully; ImageMagick only packs the ICO).
- **Deferred to page phases, as the prompt orders:**
  - real use of `VoiceSplit` (Overview "Right now", Analytics) and `VoiceTrack` (conversation header);
  - the conversation header's "Take over" chip and transcript bubbles;
  - the hosted chat and widget keeping the workspace accent: the hosted chat's Send button is ink for now, and the widget's own accent is untouched;
  - the uppercase "COLLEAGUE / CUSTOMER" sender labels in the conversation list;
  - all Arabic copy (36.3).

## 5. Verification

| Check | Result |
|---|---|
| `npm run format:check` | **PASS**: "All matched files use Prettier code style!" (`<evidence>\format-check.log`) |
| `npm run test:ci` | **PASS**: 65 files, 801 tests, 0 failures. At recovery the baseline was 64 / 788; the new tests are 13 in `voice.spec.ts`, plus the brand-mark spec counted from the first session (`<evidence>\test-ci.log`) |
| `npm run build` | **PASS**: 0 errors. Initial 454.05 kB raw / 111.07 kB transfer (budget 500 kB warning, 1 MB error); 36.1 was 439.46 kB. One warning, **inherited and unchanged**: `embed-chat.scss` 4.07 kB, 72 B over the 4 kB component-style budget. The file is untouched in this phase and was reported in 36.1. No budget raised (`<evidence>\build.log`) |
| Design Lab absent from production | **PASS**: 0 files in `dist/omniai-admin` mention `design-lab`, `DesignLab` or `lab-transcript` (87 JS files). Positive control: a development build lists `chunk-FDFJVLYX.js \| design-lab \| 101.53 kB` |
| Chrome matrix, product routes | **PASS** on the automated checks: 168 captures, the same script, routes and settle rule as 36.1. 33 owner routes × 1440/390 × light/dark, 6 public routes × 2 × 2, widget closed and open × 2 × 2, 4 analyst captures. Page overflow 0 everywhere; exactly one `h1` everywhere; 0 unnamed controls. The only console errors are the 24 signed-out `GET /api/auth/me` → 401 probes, identical to 36.1 (finding C-6). English only: no Arabic UI exists before 36.3, so "languages" is NOT RUN for product routes |
| Chrome matrix, Design Lab | **PASS**: 1440/390 × light/dark × LTR/RTL (8 runs). Overflow 0, one `h1`, 0 unnamed controls, only the signed-out 401 probe in the console |
| Contrast (`contrast_check.py`) | **PASS**: 79 pairs in each of light, dark, light + `prefers-contrast: more`, dark + more. 76 pass and 3 informational (two decorative hairlines and the Lagoon/Ember seam), 0 fail. Full table below |
| Colour vision (Chrome emulation) | **PASS by glyph and word, not by hue.** Under protanopia and deuteranopia, Ember ("Person") and warning amber become nearly the same yellow-olive; under tritanopia Ember and danger both read pink-red. Each still carries a distinct glyph and word (person, triangle, exclamation), and the split bar's seam separates its parts in full greyscale (`<evidence>\cvd\`) |
| Keyboard | **PASS (Design Lab, spot check):** Tab reaches the segmented radios with a visible ink ring; a focused ink primary button shows the 2 px ring with the page colour between ring and button. The ring computes to 2px; Chrome draws it as 1.6 CSS px at this machine's 1.25 device pixel ratio. A full keyboard walk of product pages is NOT RUN this phase (no keyboard behaviour changed; the page phases own it) |
| Reduced motion | **PASS:** all `--duration-*` compute to 0.01ms; the "Take over" chip transition is 1e-05s and the colour has fully changed at 30 ms; bubble settle 1e-05s; skeleton sweep `none`; View Transitions disabled. The spinner keeps 0.9s by design. Without reduce, the chip runs 0.32s `cubic-bezier(0.05, 0.7, 0.1, 1)` with an interpolated colour mid-sweep |
| RTL mirroring | **PASS (Design Lab, `dir="rtl"`):** chips, split and track start from the inline-start (right); customer bubbles move to the right with their tail corner; button icons follow reading order; the nav-edge and conversation-row markers use `--direction-flip`; the mark does not mirror; code tokens are isolated `dir="ltr"`. English sample sentences show ordinary bidi punctuation inside an RTL block until 36.3 brings Arabic copy |
| Fonts and third parties | **PASS:** English page loads only `instrument-sans-latin-wdth-normal.woff2`; the Arabic weights load only where Arabic renders; every request goes to `localhost:4200` (plus `data:` URIs) |
| Token / RTL lints (changed files vs `HEAD`) | **PASS with justified exceptions.** New: 2 px seams and a 3 px segmented inset below the 4 px scale (`voice-split`, `voice-track`, `_primitives.scss` 767/769/1177, Design Lab); `var(--token, #hex)` fallbacks in the `index.html` loading state (deliberate); a false positive (`#4821` in a sample order number); title `letter-spacing` via `--letter-spacing-tight`, which is 0 under `:lang(ar)`. Removed: a literal colour and font stack in `index.html` and a numeric z-index |
| Provider canary | **PASS:** inside `omniai-p36-api`, names only: `OPENAI_API_KEY`, `STRIPE_SECRET_KEY`, `STRIPE_WEBHOOK_SECRET`, `OMNIAI_WHATSAPP_APP_SECRET`, `OMNIAI_WHATSAPP_VERIFY_TOKEN`, `OMNIAI_LIVE_OPENAI`, `OMNIAI_LIVE_META`, `OMNIAI_LIVE_EMAIL`, `Email__Smtp__*` all **absent** |
| Skills | `ui-ux-design-lead`, `design-system-tokens`, `motion-interaction` and `angular-enterprise` loaded from the `frontend-pro-max` plugin. `web-performance` and `frontend-testing-quality` were not loaded in the recovery session; their rules for this phase (hashed self-hosted fonts, no double preload, unit tests for each new component, real build and browser checks) were followed as summarised in the prompt |

### Contrast table (WCAG 2, exact ratios, never rounded)

"More contrast" is `@media (prefers-contrast: more)` applied on top of each theme.

| # | Pair | Kind | Needs | Light fg / bg | Light | Dark fg / bg | Dark | More contrast (light / dark) |
|---|---|---|---|---|---|---|---|---|
| 1 | primary text on page | text | 4.5:1 | `#16181b` / `#f3f2ee` | 15.87:1 pass | `#f2f1ed` / `#0f1012` | 16.84:1 pass | 15.87:1 pass / 16.84:1 pass |
| 2 | primary text on default | text | 4.5:1 | `#16181b` / `#ffffff` | 17.78:1 pass | `#f2f1ed` / `#17191c` | 15.58:1 pass | 17.78:1 pass / 15.58:1 pass |
| 3 | primary text on muted | text | 4.5:1 | `#16181b` / `#f7f6f3` | 16.45:1 pass | `#f2f1ed` / `#1c1e22` | 14.76:1 pass | 16.45:1 pass / 14.76:1 pass |
| 4 | primary text on raised | text | 4.5:1 | `#16181b` / `#ffffff` | 17.78:1 pass | `#f2f1ed` / `#1e2024` | 14.43:1 pass | 17.78:1 pass / 14.43:1 pass |
| 5 | primary text on inset | text | 4.5:1 | `#16181b` / `#eeede8` | 15.17:1 pass | `#f2f1ed` / `#0b0c0e` | 17.31:1 pass | 15.17:1 pass / 17.31:1 pass |
| 6 | primary text on hover | text | 4.5:1 | `#16181b` / `#f0efea` | 15.44:1 pass | `#f2f1ed` / `#202328` | 13.94:1 pass | 15.44:1 pass / 13.94:1 pass |
| 7 | primary text on selected | text | 4.5:1 | `#16181b` / `#e9e8e2` | 14.48:1 pass | `#f2f1ed` / `#23262b` | 13.42:1 pass | 14.48:1 pass / 13.42:1 pass |
| 8 | secondary text on page | text | 4.5:1 | `#4d525a` / `#f3f2ee` | 7.02:1 pass | `#b6b9bf` / `#0f1012` | 9.68:1 pass | 7.02:1 pass / 9.68:1 pass |
| 9 | secondary text on default | text | 4.5:1 | `#4d525a` / `#ffffff` | 7.86:1 pass | `#b6b9bf` / `#17191c` | 8.95:1 pass | 7.86:1 pass / 8.95:1 pass |
| 10 | secondary text on muted | text | 4.5:1 | `#4d525a` / `#f7f6f3` | 7.27:1 pass | `#b6b9bf` / `#1c1e22` | 8.48:1 pass | 7.27:1 pass / 8.48:1 pass |
| 11 | secondary text on raised | text | 4.5:1 | `#4d525a` / `#ffffff` | 7.86:1 pass | `#b6b9bf` / `#1e2024` | 8.29:1 pass | 7.86:1 pass / 8.29:1 pass |
| 12 | secondary text on inset | text | 4.5:1 | `#4d525a` / `#eeede8` | 6.70:1 pass | `#b6b9bf` / `#0b0c0e` | 9.95:1 pass | 6.70:1 pass / 9.95:1 pass |
| 13 | secondary text on hover | text | 4.5:1 | `#4d525a` / `#f0efea` | 6.83:1 pass | `#b6b9bf` / `#202328` | 8.01:1 pass | 6.83:1 pass / 8.01:1 pass |
| 14 | secondary text on selected | text | 4.5:1 | `#4d525a` / `#e9e8e2` | 6.40:1 pass | `#b6b9bf` / `#23262b` | 7.71:1 pass | 6.40:1 pass / 7.71:1 pass |
| 15 | muted text on page | text | 4.5:1 | `#5f646d` / `#f3f2ee` | 5.31:1 pass | `#8e929a` / `#0f1012` | 6.09:1 pass | 7.02:1 pass / 9.68:1 pass |
| 16 | muted text on default | text | 4.5:1 | `#5f646d` / `#ffffff` | 5.94:1 pass | `#8e929a` / `#17191c` | 5.64:1 pass | 7.86:1 pass / 8.95:1 pass |
| 17 | muted text on muted | text | 4.5:1 | `#5f646d` / `#f7f6f3` | 5.50:1 pass | `#8e929a` / `#1c1e22` | 5.34:1 pass | 7.27:1 pass / 8.48:1 pass |
| 18 | muted text on raised | text | 4.5:1 | `#5f646d` / `#ffffff` | 5.94:1 pass | `#8e929a` / `#1e2024` | 5.22:1 pass | 7.86:1 pass / 8.29:1 pass |
| 19 | muted text on inset | text | 4.5:1 | `#5f646d` / `#eeede8` | 5.07:1 pass | `#8e929a` / `#0b0c0e` | 6.26:1 pass | 6.70:1 pass / 9.95:1 pass |
| 20 | muted text on hover | text | 4.5:1 | `#5f646d` / `#f0efea` | 5.16:1 pass | `#8e929a` / `#202328` | 5.04:1 pass | 6.83:1 pass / 8.01:1 pass |
| 21 | muted text on selected | text | 4.5:1 | `#5f646d` / `#e9e8e2` | 4.84:1 pass | `#8e929a` / `#23262b` | 4.86:1 pass | 6.40:1 pass / 7.71:1 pass |
| 22 | on-action label on action | text | 4.5:1 | `#ffffff` / `#16181b` | 17.78:1 pass | `#0f1012` / `#f2f1ed` | 16.84:1 pass | 17.78:1 pass / 16.84:1 pass |
| 23 | on-action label on action-hover | text | 4.5:1 | `#ffffff` / `#2b2e33` | 13.62:1 pass | `#0f1012` / `#ffffff` | 19.03:1 pass | 13.62:1 pass / 19.03:1 pass |
| 24 | on-action label on action-pressed | text | 4.5:1 | `#ffffff` / `#000000` | 21.00:1 pass | `#0f1012` / `#d9d8d3` | 13.33:1 pass | 21.00:1 pass / 13.33:1 pass |
| 25 | link on page | text | 4.5:1 | `#16181b` / `#f3f2ee` | 15.87:1 pass | `#f2f1ed` / `#0f1012` | 16.84:1 pass | 15.87:1 pass / 16.84:1 pass |
| 26 | link on default | text | 4.5:1 | `#16181b` / `#ffffff` | 17.78:1 pass | `#f2f1ed` / `#17191c` | 15.58:1 pass | 17.78:1 pass / 15.58:1 pass |
| 27 | link on muted | text | 4.5:1 | `#16181b` / `#f7f6f3` | 16.45:1 pass | `#f2f1ed` / `#1c1e22` | 14.76:1 pass | 16.45:1 pass / 14.76:1 pass |
| 28 | link on hover | text | 4.5:1 | `#16181b` / `#f0efea` | 15.44:1 pass | `#f2f1ed` / `#202328` | 13.94:1 pass | 15.44:1 pass / 13.94:1 pass |
| 29 | selected text on ::selection | text | 4.5:1 | `#16181b` / `#dedbd4` | 12.86:1 pass | `#f2f1ed` / `#3a3f46` | 9.38:1 pass | 12.86:1 pass / 9.38:1 pass |
| 30 | voice-ai chip text (strong) on tint | text | 4.5:1 | `#0a5e57` / `#e2f2ef` | 6.60:1 pass | `#7ad9ce` / `#0f2a27` | 9.15:1 pass | 6.60:1 pass / 9.15:1 pass |
| 31 | primary text in ai bubble (tint) | text | 4.5:1 | `#16181b` / `#e2f2ef` | 15.39:1 pass | `#f2f1ed` / `#0f2a27` | 13.45:1 pass | 15.39:1 pass / 13.45:1 pass |
| 32 | secondary text in ai bubble (tint) | text | 4.5:1 | `#4d525a` / `#e2f2ef` | 6.80:1 pass | `#b6b9bf` / `#0f2a27` | 7.73:1 pass | 6.80:1 pass / 7.73:1 pass |
| 33 | voice-ai fg as text on default | text | 4.5:1 | `#0e7c72` / `#ffffff` | 5.06:1 pass | `#41c2b4` / `#17191c` | 8.05:1 pass | 5.06:1 pass / 8.05:1 pass |
| 34 | voice-ai fg as text on page | text | 4.5:1 | `#0e7c72` / `#f3f2ee` | 4.52:1 pass | `#41c2b4` / `#0f1012` | 8.70:1 pass | 4.52:1 pass / 8.70:1 pass |
| 35 | voice-ai glyph on its tint | ui | 3.0:1 | `#0e7c72` / `#e2f2ef` | 4.38:1 pass | `#41c2b4` / `#0f2a27` | 6.95:1 pass | 4.38:1 pass / 6.95:1 pass |
| 36 | voice-ai track/bar on default | ui | 3.0:1 | `#0e7c72` / `#ffffff` | 5.06:1 pass | `#41c2b4` / `#17191c` | 8.05:1 pass | 5.06:1 pass / 8.05:1 pass |
| 37 | voice-ai track/bar on inset | ui | 3.0:1 | `#0e7c72` / `#eeede8` | 4.32:1 pass | `#41c2b4` / `#0b0c0e` | 8.94:1 pass | 4.32:1 pass / 8.94:1 pass |
| 38 | voice-human chip text (strong) on tint | text | 4.5:1 | `#9a330a` / `#fbe9df` | 6.24:1 pass | `#ffb088` / `#33190c` | 9.19:1 pass | 6.24:1 pass / 9.19:1 pass |
| 39 | primary text in human bubble (tint) | text | 4.5:1 | `#16181b` / `#fbe9df` | 15.09:1 pass | `#f2f1ed` / `#33190c` | 14.45:1 pass | 15.09:1 pass / 14.45:1 pass |
| 40 | secondary text in human bubble (tint) | text | 4.5:1 | `#4d525a` / `#fbe9df` | 6.67:1 pass | `#b6b9bf` / `#33190c` | 8.31:1 pass | 6.67:1 pass / 8.31:1 pass |
| 41 | voice-human fg as text on default | text | 4.5:1 | `#c2410c` / `#ffffff` | 5.17:1 pass | `#ff8d57` / `#17191c` | 7.71:1 pass | 5.17:1 pass / 7.71:1 pass |
| 42 | voice-human fg as text on page | text | 4.5:1 | `#c2410c` / `#f3f2ee` | 4.62:1 pass | `#ff8d57` / `#0f1012` | 8.33:1 pass | 4.62:1 pass / 8.33:1 pass |
| 43 | voice-human glyph on its tint | ui | 3.0:1 | `#c2410c` / `#fbe9df` | 4.39:1 pass | `#ff8d57` / `#33190c` | 7.15:1 pass | 4.39:1 pass / 7.15:1 pass |
| 44 | voice-human track/bar on default | ui | 3.0:1 | `#c2410c` / `#ffffff` | 5.17:1 pass | `#ff8d57` / `#17191c` | 7.71:1 pass | 5.17:1 pass / 7.71:1 pass |
| 45 | voice-human track/bar on inset | ui | 3.0:1 | `#c2410c` / `#eeede8` | 4.41:1 pass | `#ff8d57` / `#0b0c0e` | 8.57:1 pass | 4.41:1 pass / 8.57:1 pass |
| 46 | success text on its bg | text | 4.5:1 | `#2b7a30` / `#e8f3e8` | 4.68:1 pass | `#6bd68a` / `#10251a` | 8.92:1 pass | 4.68:1 pass / 8.92:1 pass |
| 47 | success text on default | text | 4.5:1 | `#2b7a30` / `#ffffff` | 5.34:1 pass | `#6bd68a` / `#17191c` | 9.73:1 pass | 5.34:1 pass / 9.73:1 pass |
| 48 | success text on page | text | 4.5:1 | `#2b7a30` / `#f3f2ee` | 4.77:1 pass | `#6bd68a` / `#0f1012` | 10.52:1 pass | 4.77:1 pass / 10.52:1 pass |
| 49 | warning text on its bg | text | 4.5:1 | `#8a5a00` / `#fdf3d7` | 5.35:1 pass | `#f2c94c` / `#2a220b` | 9.93:1 pass | 5.35:1 pass / 9.93:1 pass |
| 50 | warning text on default | text | 4.5:1 | `#8a5a00` / `#ffffff` | 5.92:1 pass | `#f2c94c` / `#17191c` | 11.10:1 pass | 5.92:1 pass / 11.10:1 pass |
| 51 | warning text on page | text | 4.5:1 | `#8a5a00` / `#f3f2ee` | 5.29:1 pass | `#f2c94c` / `#0f1012` | 11.99:1 pass | 5.29:1 pass / 11.99:1 pass |
| 52 | danger text on its bg | text | 4.5:1 | `#b42318` / `#fdecea` | 5.74:1 pass | `#ff8a80` / `#2d1513` | 7.48:1 pass | 5.74:1 pass / 7.48:1 pass |
| 53 | danger text on default | text | 4.5:1 | `#b42318` / `#ffffff` | 6.57:1 pass | `#ff8a80` / `#17191c` | 7.71:1 pass | 6.57:1 pass / 7.71:1 pass |
| 54 | danger text on page | text | 4.5:1 | `#b42318` / `#f3f2ee` | 5.86:1 pass | `#ff8a80` / `#0f1012` | 8.33:1 pass | 5.86:1 pass / 8.33:1 pass |
| 55 | info text on its bg | text | 4.5:1 | `#1f5fad` / `#e7f0fb` | 5.53:1 pass | `#7db7ff` / `#0d1f33` | 8.00:1 pass | 5.53:1 pass / 8.00:1 pass |
| 56 | info text on default | text | 4.5:1 | `#1f5fad` / `#ffffff` | 6.36:1 pass | `#7db7ff` / `#17191c` | 8.46:1 pass | 6.36:1 pass / 8.46:1 pass |
| 57 | info text on page | text | 4.5:1 | `#1f5fad` / `#f3f2ee` | 5.68:1 pass | `#7db7ff` / `#0f1012` | 9.14:1 pass | 5.68:1 pass / 9.14:1 pass |
| 58 | primary text on warning bg (stale notice) | text | 4.5:1 | `#16181b` / `#fdf3d7` | 16.07:1 pass | `#f2f1ed` / `#2a220b` | 13.94:1 pass | 16.07:1 pass / 13.94:1 pass |
| 59 | danger button label | text | 4.5:1 | `#ffffff` / `#c0392c` | 5.43:1 pass | `#0f1012` / `#f07a6e` | 6.99:1 pass | 5.43:1 pass / 6.99:1 pass |
| 60 | danger button label (hover) | text | 4.5:1 | `#ffffff` / `#a83023` | 6.74:1 pass | `#0f1012` / `#f5928a` | 8.48:1 pass | 6.74:1 pass / 8.48:1 pass |
| 61 | control border on page | ui | 3.0:1 | `#80838a` / `#f3f2ee` | 3.38:1 pass | `#6b717a` / `#0f1012` | 3.86:1 pass | 3.38:1 pass / 3.86:1 pass |
| 62 | control border on default | ui | 3.0:1 | `#80838a` / `#ffffff` | 3.79:1 pass | `#6b717a` / `#17191c` | 3.57:1 pass | 3.79:1 pass / 3.57:1 pass |
| 63 | control border on muted | ui | 3.0:1 | `#80838a` / `#f7f6f3` | 3.51:1 pass | `#6b717a` / `#1c1e22` | 3.39:1 pass | 3.51:1 pass / 3.39:1 pass |
| 64 | control border on inset | ui | 3.0:1 | `#80838a` / `#eeede8` | 3.23:1 pass | `#6b717a` / `#0b0c0e` | 3.97:1 pass | 3.23:1 pass / 3.97:1 pass |
| 65 | focus ring on page | ui | 3.0:1 | `#16181b` / `#f3f2ee` | 15.87:1 pass | `#f2f1ed` / `#0f1012` | 16.84:1 pass | 15.87:1 pass / 16.84:1 pass |
| 66 | focus ring on default | ui | 3.0:1 | `#16181b` / `#ffffff` | 17.78:1 pass | `#f2f1ed` / `#17191c` | 15.58:1 pass | 17.78:1 pass / 15.58:1 pass |
| 67 | focus ring on muted | ui | 3.0:1 | `#16181b` / `#f7f6f3` | 16.45:1 pass | `#f2f1ed` / `#1c1e22` | 14.76:1 pass | 16.45:1 pass / 14.76:1 pass |
| 68 | focus ring on raised | ui | 3.0:1 | `#16181b` / `#ffffff` | 17.78:1 pass | `#f2f1ed` / `#1e2024` | 14.43:1 pass | 17.78:1 pass / 14.43:1 pass |
| 69 | primary button fill on page | ui | 3.0:1 | `#16181b` / `#f3f2ee` | 15.87:1 pass | `#f2f1ed` / `#0f1012` | 16.84:1 pass | 15.87:1 pass / 16.84:1 pass |
| 70 | primary button fill on default | ui | 3.0:1 | `#16181b` / `#ffffff` | 17.78:1 pass | `#f2f1ed` / `#17191c` | 15.58:1 pass | 17.78:1 pass / 15.58:1 pass |
| 71 | danger button fill on default | ui | 3.0:1 | `#c0392c` / `#ffffff` | 5.43:1 pass | `#f07a6e` / `#17191c` | 6.47:1 pass | 5.43:1 pass / 6.47:1 pass |
| 72 | switch on (ink) on default | ui | 3.0:1 | `#16181b` / `#ffffff` | 17.78:1 pass | `#f2f1ed` / `#17191c` | 15.58:1 pass | 17.78:1 pass / 15.58:1 pass |
| 73 | chart customers on default | ui | 3.0:1 | `#5b6472` / `#ffffff` | 5.98:1 pass | `#a3aab5` / `#17191c` | 7.52:1 pass | 5.98:1 pass / 7.52:1 pass |
| 74 | chart neutral on default | ui | 3.0:1 | `#4d525a` / `#ffffff` | 7.86:1 pass | `#b6b9bf` / `#17191c` | 8.95:1 pass | 7.86:1 pass / 8.95:1 pass |
| 75 | chart ramp-4 on default | ui | 3.0:1 | `#0e7c72` / `#ffffff` | 5.06:1 pass | `#41c2b4` / `#17191c` | 8.05:1 pass | 5.06:1 pass / 8.05:1 pass |
| 76 | count badge text (on-action) on action | text | 4.5:1 | `#ffffff` / `#16181b` | 17.78:1 pass | `#0f1012` / `#f2f1ed` | 16.84:1 pass | 17.78:1 pass / 16.84:1 pass |
| 77 | decorative hairline (default) on default | info | - | `#dedbd4` / `#ffffff` | 1.38:1 info | `#2c3036` / `#17191c` | 1.32:1 info | 3.79:1 info / 3.57:1 info |
| 78 | decorative hairline (strong) on default | info | - | `#cbc8bf` / `#ffffff` | 1.67:1 info | `#3a3f46` / `#17191c` | 1.66:1 info | 1.67:1 info / 1.66:1 info |
| 79 | Lagoon next to Ember (split bar seam) | info | - | `#0e7c72` / `#c2410c` | 1.02:1 info | `#41c2b4` / `#ff8d57` | 1.04:1 info | 1.02:1 info / 1.04:1 info |

## 6. See it live

- Design Lab (development only): **http://localhost:4200/design-lab**. Use the Theme and Direction toggles at the top right.
- The product: **http://localhost:4200/sign-in**, then for example `/overview`, `/conversations`, `/analytics`.
- Synthetic demo accounts: `C:\Users\mouay\Desktop\omniai-redesign-evidence\p36-01\demo-accounts.md`
- Before / after contact sheet: `C:\Users\mouay\Desktop\omniai-redesign-evidence\p36-02\compare.html`
- Stack: the API container `omniai-p36-api` (127.0.0.1:5027) and the dev server (`npm start`, localhost:4200) are left running; restart commands are in `…\p36-01\stack.md`. The widget host page (127.0.0.1:8099) was started for the gallery and is still running.

## 7. Backend asks

No new ones from this phase. The foundation needs nothing the API lacks: the voice components take counts and senders the API already returns (`AnalyticsSnapshot.openHandledByAi/Human`, `TranscriptMessage.sender`). Carried forward from 36.1:
- the NuGet audit failure on a clean restore;
- no server-held UI preferences (D22).

The page phases may add asks, for example a handoff-event history for transcripts if the divider should mark the exact moment of a take-over rather than the change of reply author.

## 8. Risks and open issues

- **Ember vs warning and danger under colour-vision deficiency:** hue does not separate them for protan, deutan or tritan readers. The glyph-and-word rule is therefore load-bearing: any later screen that shows a voice or status by colour alone breaks it.
- **Unhashed `public/` files are cached for a year:** the edge's extension-based location matches `favicon.ico` and `favicon.svg` with `max-age=31536000, immutable`, not `no-cache` as assumed at kickoff. Browsers that cached an old `favicon.ico` (none outside local development, since nothing is deployed) would keep it. `index.html` now points first at the new `favicon.svg` URL. A future mark change needs a new file name; changing `deploy/` was out of scope.
- **Hosted chat and widget accent:** the hosted chat currently uses OmniAI's mark and an ink Send button. The direction says it should carry the workspace's own accent; this is planned for the widget/chat phase.
- **`embed-chat.scss` budget warning (72 B):** inherited; to be fixed in the widget phase by moving shared rules into the global layers.
- **Initial bundle +14.6 kB raw** (439 → 454 kB), mostly the larger global stylesheet (tokens, primitives, fonts). It is still under the 500 kB warning.
- **OFL texts** live beside the fonts in the repository but are not served with them. That is common practice for web fonts; confirm it meets your licence policy.
- **Data drift:** one more conversation exists than at the 36.1 capture ("do you ship to Canada?"), so before and after numbers differ by one. Both galleries show real API data.
- **Not run:**
  - a full keyboard walk of the product pages;
  - an Arabic UI check of product routes (36.3);
  - a forced-colours visual pass (the rules exist: outline focus, system colours for the mark and voice bars).

## 9. Proposed checkpoint commit message

Do not commit; for your checkpoint:

```
feat: lay the "Two Voices" design foundation

Replace the indigo system with the approved "Two Voices" identity at the
foundation, so every screen already reads in the new direction:

- docs/design/MASTER.md: the design brief every later phase reads first
- tokens: porcelain light and a separately designed night-shift dark, ink as
  the action colour, Lagoon (AI) and Ember (person) voice roles, chart roles,
  radius by role, overlay-only shadow, motion scale, type roles; every
  --accent use remapped by meaning; 79 contrast pairs pass in both themes and
  under prefers-contrast: more
- fonts: self-hosted Instrument Sans and IBM Plex Sans Arabic (OFL, hashed by
  the build, unicode-range subsets, metric-matched fallbacks, no preload)
- primitives restyled with unchanged class names and DOM contracts; route
  crossfade through View Transitions, off under reduced motion
- brand mark component, favicon.svg, regenerated favicon.ico, loading state
- shared/voice: VoiceChip (with the "Take over" sweep), VoiceSplit,
  VoiceTrack, with unit tests; the inbox shows the Person chip
- development-only Design Lab at /design-lab, compiled out of production
- admin-design-system.md updated (D1, D5-D10, D16-D19)
```
