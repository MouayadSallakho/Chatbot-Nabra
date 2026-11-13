# Phase 39.0: the redesign plan (Calm Glass)

- Step: 39.0, plan only. This report is the only file this step adds to the repository; no code changed.
- Written: 2026-11-13, on top of `f42a5c1` (Phase 38.8, accepted and committed).
- Inputs: `Nabra-Phase39-Pack\reference\calm-glass.md` (the specification), `brand.md`, `boards\*.dc.html` (read for their inline values, not rendered), `phase-38-notes.md`; today's truth in `docs/design/MASTER.md`, `docs/architecture/*.md`, `frontend/src/locale/GLOSSARY.md`, `docs/architecture/problem-codes.md`, the live OpenAPI document and `PHASE_38_SUMMARY.md`.
- Evidence (outside the repository): `C:\Users\mouay\Desktop\nabra-frontend-evidence\p39-0\` (`gates\`, `shots\`, `chrome\`, `lighthouse\`, `scratch\`).
- Skills loaded and used: `frontend-pro-max:ui-ux-design-lead`, `frontend-pro-max:design-system-tokens`, `frontend-pro-max:angular-enterprise`, `frontend-pro-max:enterprise-erp-ux`, `frontend-pro-max:web-performance`, `frontend-pro-max:frontend-testing-quality`. All six were present.
- 21st.dev MCP: **not connected** in this session (the server answered "Not authenticated: API key missing or reset"). This step needed no reference component. From 39.2 on, a step that wants one says so, and builds from our own primitives if the MCP is still down.

## 1. Summary

Nothing changed yet for the people who use Nabra. This step is the plan:

- Phase 39 gives every screen the Calm Glass look in fourteen sessions (39.1 to 39.8, with six steps split in two). It starts with the small backend changes the redesign needs.
- People will be shown by a name they choose instead of their email. The public chat will greet each visitor in their own language. The inbox gets "Mark all as read". The customers screens show the same masked WhatsApp number as the inbox.
- The baselines, the before screenshots (172 PNG files covering 41 screens plus the widget) and the gates were measured today. Section 11 lists eight decisions only the owner can make.

## 2. Starting point

| Item | State when this step began |
| --- | --- |
| Branch | `master` |
| HEAD | `f42a5c1` feat: patch Angular for the router and zod advisories, … (Phase 38.8) |
| `git status --short` | empty (clean worktree) |
| Last commits | `f42a5c1`, `4b762c4`, `c757f27`, `413615a`, `311c807`, as "Starting point" says |
| Dev stack | `omniai-p36-api` (AI stand-in on), `omniai-p37-mailpit`, `omniai-p37-netns`, `omniai_pg25`, `omniai_redis18` up; `/health/ready` 200; 13 migrations applied; dev server on :4200 running |

## 3. Changes by area

```text
$ git status --short
?? docs/phase-reports/PHASE_39_0_REDESIGN_PLAN_REPORT.md
```

That file is the only change. Outside the repository, the step wrote the evidence folder `p39-0\` and a production build into `frontend/dist/` (ignored by git, as in every phase).

The plan follows: inventory (3.1), the specification mapped onto the code (3.2), the baselines (3.3), Phase 38's notes (3.4), the backend step (3.5) and the steps (3.6). The screens table and the screenshot paths are in 3.7.

### 3.1 Inventory: what exists

#### 3.1.1 Screens

The app has one Angular project, `omniai-admin`, with lazy standalone routes. No route has a permission guard: each page reads the member's permissions and explains a refusal itself, and the sidebar hides what the member may not open.

**Outside the shell** (`app.routes.ts`):

| Screen | Route | Guard | Page component | testids | E2E journeys |
| --- | --- | --- | --- | --- | --- |
| Sign in | `/sign-in` | anonymous | `features/auth/login/login.ts` + `EntryFrame` | 3 + 3 (`sign-in-failure`, `sign-in-wait-*`, `entry-language-*`, `entry-story`) | sign-in, sign-in-throttled, signed-out-no-401 |
| Create account | `/create-account` | anonymous | `features/auth/register/register.ts` | 1 + 3 (`password-problems`) | signed-out-no-401 |
| Forgot password | `/forgot-password` | anonymous | `features/auth/forgot-password/` | 1 + 3 (`forgot-password-sent`) | signed-out-no-401 |
| Reset password | `/reset-password` | anonymous | `features/auth/reset-password/` | 4 + 3 (`reset-password-done/-expired/-invalid`) | signed-out-no-401 |
| Accept invitation | `/accept-invitation` | none | `features/invitations/accept-invitation/` | 0 + 3 | signed-out-no-401, invitation-language (Mailpit) |
| Workspace chooser | `/workspace` | auth | `features/workspace-setup/` | 0 + 3 | (global setup creates a workspace) |
| Hosted chat page | `/chat/:publicKey` | none | `features/chat/hosted-chat/` + `shared/chat` (`ChatPanel`, `ChatTranscript`) | 1 + 11 + 9 (`chat-input`, `chat-send`, `chat-message`, `chat-typing`, `chat-handoff-divider`) | hosted-page-ar, rename-workspace, slow-reply, take-over, needs-a-person |
| Embedded widget (iframe document) | `/embed/:publicKey` | none | `features/embed/embed-chat/` + `ChatLauncher` + `ChatPanel` | 1 + 11 + 9 (`chat-launcher`) | none (checked in Chrome in 38.7a/38.8) |
| Widget loader | `/widget/v1.js` (backend, `OmniAI.Api/Widget/v1.js`) | none | creates the iframe; frame title "Chat" / «الدردشة» | n/a | `widget-loader.spec.ts` (unit) |
| Design lab | `/design-lab` (dev builds only, `ngDevMode`) | none | `features/design-lab/` (636/863/274 lines) | 7 | none |

**Inside the shell** (`AppShell`, `authGuard` + `workspaceGuard`; `''` redirects to `/overview`):

| Screen | Route | Page component (`features/…`) | testids (own + children) | E2E journeys |
| --- | --- | --- | --- | --- |
| Overview | `/overview` | `overview/overview.ts` | 10 + 4 (`attention-panel`, `now`, `week`) | sign-in, sound-test |
| Inbox list | `/conversations` (`?view=needs-person`) | `conversations/conversation-inbox`, `conversation-list`, `inbox-placeholder` | 1 + 11 (`inbox`, `view-all`, `view-needs-person`, `needs-person-count`, `conversation-{id}`, `row-unread`, `row-reason`) | needs-a-person, one-connection, slow-reply |
| Conversation | `/conversations/:id` | `conversations/conversation-detail` + transcript, composer, context | 9 + 7 + 2 + 2 (`take-over`, `return-to-ai`, `handled-by`, `transcript`, `awaiting-ai`, `send-reply`) | deep-link-newest-first, slow-reply, take-over, needs-a-person |
| Customers | `/customers` | `customers/customer-list` | 5 (`customers`, `customer-{id}`, `row-label`) | none |
| Customer | `/customers/:id` | `customers/customer-detail` | 22 (`label`, `identities`, `conversations`, `open-merge`) | none |
| Knowledge | `/knowledge` | `knowledge/knowledge-list` | 10 (`documents`, `document-state`, `document-size`) | knowledge-index |
| New document | `/knowledge/new` | `knowledge/knowledge-create` | 3 | none |
| Upload | `/knowledge/upload` | `knowledge/knowledge-upload` | 10 (`drop-zone`, `choose-file`, `upload-button`) | knowledge-index |
| Document | `/knowledge/:id` | `knowledge/knowledge-editor` | 23 (`readiness`, `index-button`, `state-sentence`) | knowledge-index |
| AI agents | `/ai-agents` | `ai-agents/agent-list` | 5 | agent-preview-reply |
| New agent | `/ai-agents/new` | `ai-agents/agent-create` | 3 | none |
| Agent | `/ai-agents/:id` | `ai-agents/agent-editor` + preview + knowledge section | 15 + 6 + 12 | agent-preview-reply |
| Channels | `/channels` | `channels/channels-page` | 6 | none |
| Website chat list | `/channels/website-chat` | `channels/website-chat/website-chat-page` | 2 | widget-corners |
| Website chat | `/channels/website-chat/:id` | `…/website-chat-detail` + `website-chat-widget` | 7 + 8 (`widget-preview`, `accent-hex`, `widget-save`) | widget-corners |
| WhatsApp list | `/channels/whatsapp` | `channels/whatsapp/whatsapp-page` | 2 | none |
| WhatsApp number | `/channels/whatsapp/:id` | `…/whatsapp-detail` | 3 | none |
| Analytics | `/analytics` | `analytics/analytics-dashboard` + `provider-usage` + charts | 19 + 8 (`range-{value}`, `period`, `comparison`) | time-zone |
| Reports | `/reports` | `reports/reports-page` | 20 (`catalogue`, `report-{type}`, `generate`, `download`) | arabic-report, time-zone |
| Schedules | `/reports/schedules` | `reports/report-schedules` | 17 | none |
| Team | `/team` | `team/team-page` | 13 (`members`, `invitations`, `revoke`) | invitation-language |
| Invite | `/team/invite` | `team/invite-member` | 8 (`invitation-link`, `invite-language`, `role-option-{id}`) | invitation-language |
| Member | `/team/:userId` | `team/member-detail` | 9 | none |
| Roles | `/roles` | `roles/role-list` | 5 | none |
| New role | `/roles/new` | `roles/role-create` + `permission-picker` | 3 + 4 | none |
| Role | `/roles/:id` | `roles/role-editor` + `permission-picker` | 11 + 4 | none |
| Workspace settings | `/workspace-settings` | `workspace-settings/workspace-settings-page` | 18 (`time-zone-select`, `save-zone`) | rename-workspace, time-zone |
| Plan & usage | `/plan-usage` | `plan-usage/plan-usage-page` + `quota-row` | 10 + 5 | none |
| Billing | `/billing` | `billing/billing-page` + `subscription-summary` | 21 + 8 | none |
| Notifications | `/notifications` | `notifications/notifications-page` | 4 + 3 (`page-mark-all-read`) | none |
| Notification preferences | `/notifications/preferences` | `notifications/notification-preferences` | 17 (`sound-test`, `email-{key}`) | sound-test |
| Not found | `/**` (inside the shell) | `features/not-found/` | 1 | none |
| Shell chrome | (every page) | `layout/`: sidebar, topbar (3), command palette (6), notification bell (10), profile menu (5), toast host | 24 | one-connection, sound-test |

**The end-to-end suite** (`frontend/e2e`, Playwright 1.64.1, installed Chrome): 17 spec files and 23 journeys, run in the `en-ltr` and `ar-rtl` projects (46 runs), on one worker with no retries. Timeouts: `actionTimeout` 15 s, `navigationTimeout` 20 s, test 60 s, `globalTimeout` 15 min.

The suite finds elements by `data-testid` first. It also uses these **structural hooks**, which the redesign must keep or replace in the same step that changes them:

- `#email`, `#password`, `button[type=submit]`, `#invite-email`, `#widget-position`, `#conversation-reply`, `#browser-heading`
- `.bubble`, `.message-text`, `.live-state.is-live`, `.toast-region[role="status"]`, `.chat-honesty`, `a.conversation-row`
- `[data-sender=…]`, `dialog[open]`, `app-profile-menu [aria-haspopup="menu"]`, `app-chat-launcher`, `h1`, `bdi`, `time`, `option`

The expected wording sits in a per-spec `WORDS` object for each language. No helper reads `messages*.json`, so a step that rewords a sentence a journey checks must update that spec in the same step.

Routes no journey visits: the customers screens, roles, billing, plan & usage, the notifications page, `/ai-agents/new`, `/knowledge/new`, `/reports/schedules`, `/channels`, the WhatsApp screens, `/team/:userId`, `/workspace`, `/embed/:key`, the design lab and the not-found page. The Chrome pass of each step covers them.

#### 3.1.2 Styling

- **Tokens:** `src/styles/_tokens.scss`, 428 lines and about 127 custom properties. They cover surfaces, text, borders, action, link, focus, voice AI/human, status, charts, skeleton, scrim and one overlay shadow. Beside them are `--space-1…10` (4 px base), radius by role, control heights, 13 `font` shorthands (`--text-*`), motion (`--duration-xs…xl`, four easings), layout and z-index. **Component styles hold no colour literals at all** (0 hex, 0 `rgb()` outside `_tokens.scss`). The few literals in TypeScript are the widget accent default `#2563eb` (`accent-color.ts`, `channel-problems.ts`, `website-chat-widget.html`) and the launcher's shadows (`chat-launcher.ts`).
- **Themes:** `data-theme="light|dark"` on `<html>`, set before first paint by the inline script in `index.html`, which also sets `lang` and `dir`. The choice (`system|light|dark`) is stored per browser in `localStorage['omniai.theme']`; there is no server preference. The visitor pages (`/chat/`, `/embed/`) ignore the stored admin theme and follow the OS. `prefers-contrast: more` is handled; `forced-colors` is handled in the global layers and five components. **`prefers-reduced-transparency` is not handled** (there is no glass today).
- **Global layers** (`styles.scss` → `_fonts`, `_tokens`, `_base`, `_primitives` (1,559 lines), `_charts`, `_authoring`, `_admin`), plus `bootstrap-grid.min.css` (grid only).
- **The global checkbox rule** (Phase 38 note 4) is at `_primitives.scss:701–715`: `input[type='checkbox']:not(.switch), input[type='radio'], .form-check-input { inline-size: 1rem; block-size: 1rem; … accent-color: var(--action) }`. The `.switch` is defined at `_primitives.scss:726–773` (36×20 px) and used in `notification-preferences.html:77, :223` (enlarged to 40×24 there) and the design lab.
- **Fonts:** self-hosted in `src/assets/fonts/` with their OFL texts:
  - Instrument Sans variable: Latin 57,332 B, Latin Extended 18,856 B.
  - IBM Plex Sans Arabic 400 / 500 / 600, Arabic subset: 42,848 / 45,296 / 45,688 B.
  - Every face has a `unicode-range` and `font-display: swap`, with metric-matched fallbacks (`size-adjust` 101.61% and 90.8%).
  - **No preload.** File names are hashed by the build, so `index.html` cannot name them. On English pages, `ArabicFonts.notice()` calls `document.fonts.load()` just before Arabic text renders (inbox list and conversation only).
- **CSP** (edge, `deploy/edge/templates/omniai.conf.template`): `font-src 'self'`, `style-src 'self' 'unsafe-inline'`. `script-src` uses hashes computed from the built `index.html` at image build (`deploy/edge/csp-hashes.mjs`), so a change to the pre-paint script needs no deploy edit. Every `.woff2`, `.svg`, `.png` and `.ico` is served `immutable` for a year, so unhashed assets must carry a version in their name (as `nabra-icon.v1.svg` does).
- **Icons:** `app-icon`, one authored 24 px outline set of 51 glyphs (`shared/icon/icon-paths.ts`). Five glyphs are mirrored in RTL through `MIRRORED_ICONS`. Used in 58 files.
- **Motion:** CSS only. There is no `@angular/animations` and no `animate.enter`. Route crossfades use View Transitions (`withViewTransitions`). Keyframes cover menus, bubbles, skeletons, dialogs, toasts, the takeover sweep, the widget opening, the palette, the entry signature and the typing dots. Under reduced motion, the duration tokens collapse to 0.01 ms.
- **Design lab:** colour, type, shape and motion, icons, the mark, buttons, form controls, content, feedback, the two voices, transcript, charts and admin patterns, with theme and direction toggles. In the baseline it has **5 unnamed buttons** (axe `button-name`) on both widths and both themes. It is development-only, but 39.2 rewrites it and fixes them.

#### 3.1.3 Shared components and where they are used

| Today | Kind | Where it is used | Phase 39 owner |
| --- | --- | --- | --- |
| `.btn`, `.btn-primary/-secondary/-ghost/-danger/-danger-ghost/-link/-icon/-sm/-lg/-loading` | global CSS (about 199 uses) | every feature, layout | 39.2b restyles in place |
| `.badge*`, `.count-badge` | global CSS (about 53 uses) | status words across admin | 39.2b → `app-chip` for status; counts stay a class |
| `app-voice-chip`, `app-voice-split`, `app-voice-track`, `.handoff-divider` | components / CSS | conversations, customers, overview, analytics | 39.2b (chip, event line), 39.4 / 39.7 (split, track) |
| `app-channel-badge` | component | agents, conversations, customers | 39.2b (avatar badge) |
| `.field`, `.input`, `.select`, `.textarea`, `.form-*` | global CSS | every form | 39.2b restyles in place |
| checkbox, radio, `.switch`, `.segmented*` | global CSS (the 38 note 3/4 rule) | invite, report form, analytics range, notification preferences, permission picker, role options, reports catalogue | 39.2b → the choice-control family |
| `.table`, `.data-list` / `.data-row`, `.pager`, `app-pagination` | CSS + component | lists everywhere | 39.2b restyles; rows per screen step |
| `app-menu`, `.menu*` | component + CSS | profile menu, design lab | 39.2b (glass) |
| `app-confirm-dialog` (native `<dialog>`) | component | agents, channels, knowledge, team, workspace settings | 39.2b (glass + scrim) |
| `app-toast-host` + `ToastService` | component + service (13 users) | shell | 39.2b look, 39.3 placement |
| `.skeleton*` | global CSS | every list and the overview | 39.2b look; geometry per screen step |
| `app-empty-state` | component (71 uses) | almost every list | 39.2b |
| `app-page-header` | component (53 uses) | every admin page | 39.2b |
| `app-brand-mark` | component | sidebar, entry frame, hosted chat, boot state, design lab | 39.2b (new mark) |
| `shared/chat/` (`ChatPanel`, `ChatTranscript`, `ChatLauncher`) | components | hosted page, widget, agent preview, widget preview | 39.2b bubbles, 39.5b layout |
| `shared/charts/` (column, area, bar, stacked bar, sparkline, meter, frame) | components | analytics, overview, plan & usage | 39.7 |
| `app-command-palette`, `app-notification-bell`, `app-profile-menu`, `app-topbar`, `app-sidebar`, `app-entry-frame` | layout | shell, entry pages | 39.3, 39.8a |
| **Not present:** tabs or tablist, popover, avatar component (only an `.avatar` class), an orb, input/checkbox components | | | 39.2b adds what the spec names |

#### 3.1.4 The public name

`brand.md` keeps **Nabra / نبرة**, so Phase 39 renames nothing. The audit found **no user-visible "OmniAI"** anywhere:

- **Frontend:** `brand.name` comes from `core/brand/brand.ts`, with 31 uses of "Nabra" in English and 34 of «نبرة» in Arabic. Tab titles read "<page> · Nabra". `index.html` holds the title, the boot lockup and `nabra-icon.v1.svg` / `.v1.ico`. **There is no web manifest and no apple-touch icon.**
- **Emails:** `EmailLayout.cs` and `EmailWords.Product` give "Nabra" or «نبرة». Subjects are "Nabra: …", and the sender name is "Nabra".
- **Report files:** PDF metadata is `Creator "Nabra reports"` and `Producer "Nabra"`. Fonts are DejaVu Sans and Bold, embedded with their licence. XLSX and CSV carry no product name.
- **Widget loader:** the frame title is "Chat" / «الدردشة». It keeps the internal `omniai:v1:` protocol and `data-omniai-key` as a fallback.
- **OpenAPI:** the title is "Nabra API".
- **Tests that pin the name:**
  - `PublicNameTests` (backend) scans every string literal and the `.js`, `.json`, `.html`, `.txt`, `.resx` and `.md` sources for `omni[\s-]?ai`, minus the allowed internal shapes.
  - `brand.spec.ts` (frontend) checks the catalogues, the templates, `index.html` and the icon links.
  - `OpenApiDocumentContractTests` checks the API title.

The internal "omniai" names (storage keys, cookies, headers, the postMessage protocol, Redis keys) stay, as `brand.md` says.

### 3.2 The specification mapped onto the code

#### 3.2.1 The token layer

**Where tokens live.** `src/styles/_tokens.scss` is rewritten in 39.2a as the Calm Glass token file, still the single source of values:

- raw scale (primitive tier);
- semantic roles on `:root` (light), the dark block (`:root[data-theme='dark']` plus the no-JS `prefers-color-scheme` fallback), `prefers-contrast: more`, reduced transparency and forced colours;
- component tokens for glass, bubbles and chips.

Components read semantic or component tokens only, never the raw scale (rule 8; `token_lint.py` gates it).

**Migration without a big bang.** Every component already reads semantic tokens and holds no literals. So 39.2a can:

1. add the new roles;
2. **re-point the old role names at them as deprecated aliases** (`--surface-page: var(--color-ground)`, `--action: var(--color-primary)`, `--voice-human-fg: var(--voice-team-label)`, …), so every screen takes the Calm Glass palette at once without a layout change;
3. let each screen step move its own components to the new names.

39.8b deletes the alias block, and a grep gate proves no old name is left.

**Token names** (light / dark values from `calm-glass.md` §2–3; † marks a departure, see section 4):

| Token | Light | Dark | Use |
| --- | --- | --- | --- |
| `--emerald-950 … --emerald-100`, `--cream-50/100/150/200`, `--sand-300/400`, `--stone-600`, `--ink-700/900`, `--gold-100/500/800/850`, `--coral-50/600/700` | the raw scale of §2 | (same) | building roles only |
| `--color-ground` | cream-100 `#F4F1EA` (visitor pages: cream-50) | emerald-950 `#051F20` | page behind panels |
| `--color-panel` | `#FFFFFF` | emerald-900 `#0B2B26` | solid content |
| `--color-panel-soft` | cream-150 `#F6F3EC` | `#12332D` | fills inside panels, segmented track |
| `--color-panel-raised` | `#FFFFFF` | emerald-800 `#163832` | selected segment, raised tile |
| `--color-line` | sand-300 `#E3DCCC` | `rgba(142,182,155,.16)` | decorative lines (not a control boundary) |
| `--color-line-strong` | sand-400 `#CFC7B5` | `rgba(142,182,155,.28)` | card edges that need more presence |
| `--color-control-border` † | `#8A8370` | `#668276` | boundaries of inputs, checkboxes, radios, switch off, selected segment edge: 3:1 |
| `--color-text` / `-secondary` / `-quiet` | ink-900 / ink-700 / stone-600 | `#E9F2EC` / `#C9D8CF` / `#9AA79F` | text |
| `--color-primary` / `-hover` / `--color-on-primary` | emerald-700 / emerald-600 / `#FFF` | emerald-300 / emerald-100 / emerald-950 | buttons, links, focus |
| `--color-focus-ring` / `--color-focus-halo` | emerald-700 / emerald-700 at 18% | emerald-300 / emerald-300 at 24% | 2 px border + 3 px ring (inputs); 2 px outline elsewhere |
| `--color-selected` | `#EEF5EF` (board) | emerald-300 at 12% | selected row tint |
| `--voice-ai-bubble` / `-text` / `-label` / `-dot` | emerald-100 / emerald-900 / emerald-700 / emerald-700 | `#1C4A40` / `#E9F2EC` / emerald-300 / emerald-300 | the AI's voice |
| `--voice-team-bubble` / `-text` / `-edge` / `-label` / `-dot` | gold-100 / ink-900 / gold-500 at 55% / gold-800 / gold-500 | `#3A2F18` / `#F5ECD8` / gold-500 at 45% / `#E3C98F` / gold-500 | the team's voice |
| `--voice-customer-bubble` / `-text` | cream-100 / ink-900 (widget: visitor's own cream-200) | `#12332D` / `#E9F2EC` (widget own `#2A3B35`) | the customer |
| `--chip-need-bg` / `-fg` / `-dot` | gold-100 / gold-850 / gold-500 | gold-500 at 18% / `#E3C98F` / gold-500 | needs a person, handled by, waiting |
| `--chip-ai-bg` / `-fg` / `-dot` | emerald-100 / emerald-900 / emerald-700 | emerald-300 at 16% / emerald-100 / emerald-300 | AI is answering |
| `--chip-failed-bg` / `-fg` / `-dot` | coral-50 / coral-700 / coral-600 | `rgba(240,160,143,.14)` / `#F0A08F` / `#F0A08F` | the AI could not reply, errors |
| `--status-success-*`, `--status-warning-*`, `--status-danger-*`, `--status-info-*` † | see section 4 | | status words that are not voices (quotas, billing, indexing) |
| `--glass-bg` / `-border` / `-shadow` / `-highlight` / `-blur` | `rgba(255,255,255,.62)` / `rgba(255,255,255,.78)` / `0 14px 44px rgba(11,43,38,.10)` / `inset 0 1px 0 rgba(255,255,255,.9)` / `blur(24px) saturate(140%)` | `rgba(11,43,38,.62)` / `rgba(142,182,155,.22)` / `0 14px 44px rgba(0,0,0,.38)` / `inset 0 1px 0 rgba(218,241,222,.10)` / same | the material |
| `--glass-solid-bg` | `--color-panel` | `--color-panel` | the solid twin |
| `--scrim` | `rgba(11,43,38,.28)` | `rgba(0,0,0,.5)` | behind dialogs |
| `--glow-a` / `--glow-b` | emerald-700 at 18% / gold-500 at 24% | emerald-300 at 16% / gold-500 at 12% | the two background glows |
| `--orb-gradient` | `radial-gradient(circle at 35% 30%, #DAF1DE, #2F6B58 55%, #0B2B26)` | same | the AI's presence |
| `--chart-current` / `--chart-previous` † / `--chart-ai` / `--chart-team` / `--chart-channel-2` | emerald-700 / gold-800 dashed / emerald-700 / gold-800 lines (gold-500 fills with ink labels) / emerald-300 | emerald-300 / `#E3C98F` / emerald-300 / `#E3C98F` / emerald-100 | charts |
| `--space-1…10` | 4, 8, 12, 16, 20, 24, 32, 40, 56, 64 | | spec §5 (today's 48 becomes 56) |
| `--shell-padding` 14 px, `--column-gap` 12 px, `--panel-padding` 20–24 px | | | spec §5 |
| `--radius-pill` 999, `--radius-input` 12, `--radius-tile` 16, `--radius-panel` 24, `--radius-dialog` 24, `--radius-bubble` 18, `--radius-bubble-tail` 6, `--radius-nav-item` 12 | | | spec §5 and the boards |
| `--control-height` 44, `--control-height-dense` 40, `--chip-height` 22–28, `--target-min` 44 | | | spec §6 |
| `--font-ui-latin` Geist, `--font-ui-arabic` IBM Plex Sans Arabic, `--font-display-latin` Instrument Serif | | | spec §4 |
| `--text-display` 44/48 serif; `--text-title-1` 30/36 600; `--text-title-2` 20/28 600; `--text-title-3` 16/24 600; `--text-body` 15/24 (ar 15/26); `--text-small` 13/20; `--text-caption` 12/16 500 | | | spec §4; Arabic one line-height step up, never under 13 px |
| `--duration-press` 120, `--duration-menu` 200, `--duration-dialog` 280; `--ease-calm` `cubic-bezier(.2,0,0,1)` | | | spec §5; collapse to ~0 under reduced motion, fades kept |

**Axes, one attribute each:**

| Axis | Switch |
| --- | --- |
| Theme | `data-theme` (unchanged) |
| Transparency | `data-transparency="reduced"` (new; the in-app setting) **or** `@media (prefers-reduced-transparency: reduce)` → every `--glass-*` resolves to its solid twin, same border, no blur |
| Forced colours | `@media (forced-colors: active)` → system colours, no shadows, no blur, outline focus |
| Contrast | `@media (prefers-contrast: more)` → quiet text becomes secondary, lines become control borders |
| Motion | `@media (prefers-reduced-motion)` → durations to ~0, movement dropped, fades kept |
| Script | `:lang(ar)` → Plex Arabic stack, line heights one step up, letter-spacing 0 |

**Glass in code.** One mixin (`@include glass`) and one class (`.glass`) are the only places that declare `backdrop-filter`, with `@supports not (backdrop-filter: blur(1px))` giving the solid twin. Five surfaces use them: the side bar, the phone top and bottom bars, menus and popovers, dialogs and the palette, and toasts; on the widget, the header and composer. The budget is enforced by review and a design-lab check: at most three blurred layers on screen, never on a scrolling row, and `will-change` only while animating.

#### 3.2.2 Fonts (answers Phase 38 note 1)

| Family | File (39.2a) | Weights | Subset | Size | Where it is used |
| --- | --- | --- | --- | --- | --- |
| Geist (OFL) | `public/fonts/geist/geist-latin.v1.woff2` | variable 100–900 | Latin `U+0000-00FF, U+0131, U+0152-0153, U+02BB-02BC, U+02C6, U+02DA, U+02DC, U+0304, U+0308, U+0329, U+2000-206F, U+20AC, U+2122, U+2191, U+2193, U+2212, U+2215, U+FEFF, U+FFFD` | 28,356 B (measured on the Google Fonts subset of Geist; re-measured on the release files in 39.2a) | all Latin UI text and the digits (both languages) |
| Geist | `geist-latin-ext.v1.woff2` | variable | Latin Extended | 15,292 B (same source) | accented names, rare |
| IBM Plex Sans Arabic (OFL) | `public/fonts/ibm-plex-sans-arabic/…-arabic-{400,500,600,700}.v1.woff2` | 400, 500, 600, 700 | Arabic `U+0600-06FF, U+0750-077F, U+0870-088E, U+0890-0891, U+0897-08E1, U+08E3-08FF, U+200C-200E, U+2010-2011, U+204F, U+2E41, U+FB50-FDFF, U+FE70-FE74, U+FE76-FEFC` | 42,848 / 45,296 / 45,688 B (today's files); 700 about 46 KB (added) | Arabic UI; 700 only for the Arabic name in lockups and display |
| Instrument Serif (OFL) | `public/fonts/instrument-serif/instrument-serif-latin.v1.woff2` | 400 | Latin | about 20–30 KB (measured in 39.2a) | the overview greeting, empty-state lines, entry pages; never tables or forms |
| Instrument Sans | removed | | | −76,188 B | retired with the Two Voices wordmark |

- **Preload, per language.** The inline pre-paint script in `index.html` already knows the language before first paint. It adds `<link rel="preload" as="font" type="font/woff2" crossorigin>` for exactly what the first paint of that language needs:
  - **English:** `geist-latin` (28 KB).
  - **Arabic:** `ibm-plex-sans-arabic-arabic-400` (43 KB) plus `geist-latin` (28 KB, the digits and the Latin inside Arabic pages).
  - Whether Arabic 600 (titles) is preloaded too is decided by measurement in 39.2a, against the 39.0 numbers in 3.3.3.
  - Nothing else is preloaded: Latin Extended, 500, 700 and the serif load when used.

  On English pages under `/conversations`, where customer text is often Arabic, the script also preloads Arabic 400 (about 43 KB); 39.2a keeps this only if it removes the measured 0.8 s fallback (3.3.3). `ArabicFonts.notice()` stays as the hint elsewhere.
- **Why `public/fonts/` with versioned names.** Angular hashes `url()`s it processes, so `index.html` could never name the file, which is why 36.2 shipped without a preload. Root-relative URLs (`url('/fonts/…v1.woff2')`) are expected to pass through the application builder as written. This is not yet verified on this code base: 39.2a proves it in the build output before relying on it. If it fails, the fallback is a build-time manifest read by the pre-paint script, and 39.2a reports which one shipped. Both the stylesheet and the preload then name one URL, so nothing downloads twice. The edge's year-long `immutable` rule for `.woff2` makes the version in the name mandatory: the next change is `v2`, as with `nabra-icon.v1`.
- **No layout shift on swap.** Metric-matched fallbacks: `Geist Fallback` (local Arial / Arimo / Liberation Sans / Helvetica with `size-adjust`, `ascent-override`, `descent-override`, `line-gap-override` computed from Geist's metrics), `IBM Plex Sans Arabic Fallback` (today's, re-checked), and `Instrument Serif Fallback` (local Georgia / Times New Roman / Tinos). 39.2a measures CLS from the swap on the inbox and the overview in both languages, with the font delayed.
- **The lockup on English pages.** The lockup sets «نبرة» in Plex Arabic 700, which would make every English page download an Arabic file. The lockup's two names are therefore drawn as **outlined SVG paths** inside the brand-mark component. OFL allows this for artwork; it needs no font at runtime and keeps the right shapes in forced colours with `currentColor`. Running text never uses outlined glyphs.
- **Size cost against today**, first paint:
  - English: 57 KB (Instrument Sans Latin) → about 28 KB.
  - Arabic: 57 + 46 + 43 KB measured (Instrument Sans, Plex 600 and 400; 3.3.3) → 28 + 43 KB preloaded, plus 46 KB (600) when titles render.
  - The fonts total about 76 KB less on an English first paint. Nothing is added to the startup JavaScript.
- **CSP:** unchanged (`font-src 'self'`). No new third-party origin; the files are copied into the repository with their `OFL.txt`.

#### 3.2.3 Components: the family to build first (39.2b)

| Component | Spec (calm-glass §6 + boards) | Replaces | Notes |
| --- | --- | --- | --- |
| **Choice controls**: `app-check` (checkbox), `app-radio`, `app-switch`, `app-segmented` (radio group or link set) | visible 20 px box / 40×24 track; **44 px target** that includes the label; native input inside, so keyboard and screen readers get native behaviour | the global rule at `_primitives.scss:701–773`, `.switch`, `.segmented*`, the hidden-radio segmented pattern | Wrappers project the consumer's own `<input>`, so `formControlName`, ids, `data-testid`s and the e2e locators (`#widget-position`, `option`) stay as they are. No ControlValueAccessor is needed. The global rule and the `.switch` escape are deleted in the same step, and every use moves (invite form, report form, analytics range, notification preferences, permission picker, role options, reports catalogue, design lab). |
| Buttons | pill, 44 px (40 in dense desktop headers), 600; primary, secondary (panel or glass + line), quiet; icon-only with a name | `.btn*` restyled in place; `.btn-ghost` → `.btn-quiet` alias | No component, to avoid 199 template edits. One primary per area. |
| Chips | 22–28 px pill, 6–7 px dot + a word; tones need / ai / failed / team / neutral | `.badge-*` where they show status; `app-voice-chip` restyled | `app-chip` with `tone` and projected text |
| Inputs, selects, text areas | 44 px, radius 12, control border, focus 2 px primary + 3 px halo; labels above | `.input`, `.select`, `.textarea`, `.field` restyled | The widget's pill input is its own variant. |
| Segmented / tabs | soft pill track, selected segment raised on panel + control-border edge | `.segmented*`, inbox view links | Links when the state is in the URL (inbox views, analytics period); radios otherwise; never `role=tab` for URL state |
| Rows | 16 px radius, 40 px avatar with channel badge, name, time, one-line preview, chips; selected = primary tint; unread = dot + heavier name + the word in the accessible name | `.data-row`, `.conversation-row` styling | `app-avatar` (initials or code, channel badge `--channel-whatsapp` `#25A36B`, web `--color-primary`) |
| Bubbles | AI bubble + 26 px orb; team bubble + gold edge + name under; customer cream; 18 px radius with a 6 px corner on the speaker's side | `.bubble*`, `shared/chat` bubbles | Logical corners mirror in RTL. |
| Event lines | centred line with words; failed in coral | `.handoff-divider`, transcript events | `role="note"` |
| Toasts | glass; bottom-start on desktop, top on phones; orb or icon; 4–6 s, paused on hover and focus; polite | `toast-host` look | 39.3 places them |
| Dialogs, menus, popovers, palette | glass + scrim; focus trapped; Escape closes; focus returns | `confirm-dialog`, `.menu*`, `app-menu`, command palette, bell panel | native `<dialog>` kept (top layer) |
| Tooltips | glass, small; on hover and focus | new (collapsed side bar names) | |
| Skeletons | panel colour, soft shimmer, stops under reduced motion; each layout's own geometry | `.skeleton*` | geometry redrawn per screen step (note 11) |
| Empty states | one Instrument Serif line (Latin) or Plex Arabic 600, one sentence, one action | `app-empty-state` | |
| Orb | radial emerald, 26 px in threads, 64 px in widget greeting; breathes 1→1.04 while the AI answers, still under reduced motion | new `app-orb` | `aria-hidden`; the state is said in words beside it |
| Glass surface | `.glass` / `@include glass` | new | the only `backdrop-filter` in the code base |
| Brand mark and lockup | two circles (140×120), overlap emerald-900; dark: emerald-100, gold-500, emerald-300; mono `currentColor` | `app-brand-mark` (`BRAND_MARK_PATHS`) | lockup with outlined names; Arabic name first on Arabic pages |
| Page header, panel, tile | title-1 / title-2, panels radius 24, tiles 16, 1 px line, no shadow | `app-page-header`, `.card*` | |

#### 3.2.4 Layouts against the boards

- **Shell, desktop (boards Inbox, Overview):**
  - 14 px padding on the ground, with the two glows (900 px emerald at the start-top, 760 px gold at the end-bottom).
  - A **floating glass side bar** of 216 px with radius 24 and 18/14 px padding. From top to bottom it holds: the mark and "Nabra"; the workspace switcher (a 44 px panel button); the areas, with 40 px items, radius 12, 18 px icons and the active item in primary fill (dark: a 16% tint); the inbox count as a gold pill; and the member at the bottom (34 px avatar, name and sounds state).
  - Content columns are solid panels, radius 24, with a 12 px gap.
  - **Collapsed** below 1280 px: icons only, with names in glass tooltips on hover and focus.
  - Today's top bar (workspace identity, palette trigger, bell, account) moves: the workspace into the side bar, the palette trigger and bell into the side bar's head, the account into its foot.
- **Shell, phone:**
  - A glass top bar holds the area title, the workspace and the account.
  - A glass bottom bar holds four areas plus "More". The four are chosen per permission in this order: Inbox, Overview, Customers, Knowledge, with Analytics and Reports as fallbacks. "More" opens a glass sheet with the rest.
  - That is three blurred layers at most: top bar, bottom bar and one sheet or menu.
  - Panels are full width. The conversation header shrinks to one line (note 5); the facts open on demand.
- **Inbox:** list (about 350 px) · thread · customer panel (about 264 px), from 1280 px; below that, today's two-page pattern stays.
  - Tabs: All · Needs a person (count) · Mine, where Mine depends on owner question Q1.
  - The search field on the board is not offered by the API, so it is not shipped (backend ask BA39-3).
  - The thread header carries the customer, channel and window facts, the handling chip, "Handled by …" and the actions.
  - The composer is a panel-soft field with radius 20.
  - The customer panel holds: the avatar, the label, "Customer since" (from the customer read), conversations and channel tiles, and earlier conversations. Only what `customers.view` returns is shown, and the panel is hidden without that permission.
- **Overview (bento, board Overview):** a 4-column grid with 14 px gaps, panels radius 24 and 22 px padding. Each tile against the API:

  | Tile | Data | Status |
  | --- | --- | --- |
  | Conversations, 2×2 | `summary.conversationsStarted` + `previous` + `dailyActivity`, as 37.6 serves them | ✓ |
  | Answered by the AI (ring) | no per-conversation "no person needed" figure; ships honestly as "Replies written by the AI": `aiMessages / (aiMessages + humanMessages)`, with its sentence | backend ask BA39-1 |
  | Needs a person now (gold tile, 1×2) | the needs-a-person read: total + first three with their reasons + "Open the inbox" | ✓ |
  | First reply (AI and team) | **not offered by the API: the tile is left out** | backend ask BA39-2 |
  | Channels | `channels[]` | ✓ |
  | AI usage this month (dark tile) | the plan-usage monthly provider operations against the limit, shown only with `billing.view` or `analytics.view` as today | ✓ |

  The greeting uses Instrument Serif 44/48, or Plex Arabic 600. The board's "Sample figures" line is design-only and does not ship.
- **Widget and hosted page (boards Widget, WidgetAr):**
  - A glass header (radius 22, 12 px from the edges) holds the workspace tile (its first letter on the accent), the name and a status line.
  - Below it: the 64 px orb with the greeting (22/30, 600), quick-reply chips and the bubbles (visitor on the end side in cream-200, AI on the start side with a 26 px orb).
  - The typing state shows three dots plus "Typing…", and the composer is a glass pill input with a 44 px round Send.
  - The board's status "Replies in seconds" and its quick-reply chips are not offered by the API, so neither ships. The status line says only what is true today (the agent's name, or "Unavailable"). Backend ask BA39-4 covers quick replies.
  - "Powered by Nabra" is new copy: see Q6.
  - The workspace's accent may replace emerald only where `accent-color.ts` proves every contrast pair; otherwise emerald is used and the settings preview says so.

#### 3.2.5 The brand

No rename (`brand.md`). The mark changes to direction A, the two circles:

| When | Change |
| --- | --- |
| 39.2b | `app-brand-mark` gets the new geometry and the outlined lockup; the boot lockup in `index.html` follows; `brand.spec.ts` is updated |
| 39.3 | the side bar shows it |
| 39.8a | the app icon (rounded square, radius 22%, emerald-600 → emerald-900 gradient, mark at 62% in its dark-ground colours) |
| 39.8a | favicons: `nabra-icon.v2.svg` plus PNG 16/32/48 and `.ico`; `apple-touch-icon` 180; a new `manifest.webmanifest` with 192/512 and a maskable 512 (no manifest exists today; same origin, so no CSP change) |
| 39.8a | emails' header mark (backend templates) |
| 39.7b | report files (backend renderer) |
| 39.5b | the widget's "Powered by" (if Q6 is yes) |

The mark is never rotated or mirrored and never given a gradient on its circles.

### 3.3 Baselines (before any change)

Measured on 2026-11-13 against the dev stack (Northwind, the owner account, installed Chrome), using the production build of `f42a5c1` served by a loopback static server with gzip and the dev proxy's forwards (`p39-0\scratch\prod-serve.mjs`, port 4300).

#### 3.3.1 Startup JavaScript

| Measure | 38.0 baseline | Today (`f42a5c1`) | Ceiling (+10%) |
| --- | --- | --- | --- |
| Loader + application chunk, gzip | 138,907 B | **141,423 B** (+1.8%), 30 files, 438,167 raw, 125,096 brotli | 152,797 B |
| Initial total (`ng build`) | 313.56 kB | 313.77 kB raw / 73.71 kB transfer | budget 500 kB warn / 1 MB error |

Phase 39 may add at most 11,374 B gzip of startup JavaScript before it needs the owner. The plan adds none on purpose: the choice controls and chips are small, `app-orb` is CSS, glass is CSS, and the bento and widget pieces are lazy.

#### 3.3.2 Lab vitals and Lighthouse

**Lab vitals.** Installed Chrome, production build at :4300, 1440×900, no throttling, signed in as the owner, 5 cold contexts per page and language, medians. INP is the longest event duration of one scripted interaction per load:

- inbox: switch to "Needs a person";
- overview: open the notification bell;
- hosted page: click the input and type two letters.

Source: `p39-0\scratch\baseline.mjs vitals`, results in `chrome\results.json` → `vitals`.

| Page | Language | LCP (ms) | LCP element | CLS | INP (ms) |
| --- | --- | --- | --- | --- | --- |
| Inbox `/conversations` | en | 1,176 | the placeholder's sentence (`p.state-description`) | 0 | 64 |
| Inbox | ar | 1,140 | same | 0 | 64 |
| Overview | en | 1,216 | `p.now-note` | 0.005 | 48 |
| Overview | ar | 1,200 | the page description | 0.005 | 56 |
| Hosted chat page | en | 1,084 | the greeting (`li[chat-greeting]`) | 0 | 32 |
| Hosted chat page | ar | 1,116 | the greeting | 0 | 32 |

TTFB on the loopback static server was 0.8–0.9 s in most runs, and it dominates these LCPs (one run reached 464 ms). The numbers compare like with like across Phase 39, not with production. The first inbox run's INP was 240 ms; the median is 64.

**Lighthouse 12.8.2** (the version in the local npx cache; not 13). Desktop preset, plus the default mobile preset (simulated slow 4G, moto g power) for the public chat page. Performance and accessibility only. The owner's session cookie sat in a temporary profile.

The profile's storage was **not reset** between runs, because a reset would have signed it out. Only the first page of each language loaded with a cold HTTP cache; the others are warm-cache and therefore optimistic. 39.8b repeats this exact protocol for comparison. JSON reports: `p39-0\lighthouse\*.json`, summary in `summary.json`.

| Page | Lang | Perf | A11y | LCP | FCP | CLS | TBT | Transfer |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Inbox (desktop) | en | 100 | 100 | 644 ms | 229 ms | 0 | 0 ms | 139 KB (cold) |
| Overview (desktop) | en | 100 | 100 | 495 ms | 190 ms | 0 | 0 ms | 36 KB |
| Hosted chat (desktop) | en | 100 | 100 | 394 ms | 181 ms | 0 | 0 ms | 25 KB |
| Hosted chat (mobile) | en | 100 | 100 | 1,104 ms | 636 ms | 0 | 50 ms | 13 KB |
| Inbox (desktop) | ar | 100 | 100 | 614 ms | 244 ms | 0 | 0 ms | 132 KB (cold catalogue) |
| Overview (desktop) | ar | 100 | 100 | 469 ms | 183 ms | 0 | 1 ms | 38 KB |
| Hosted chat (desktop) | ar | 100 | 100 | 367 ms | 194 ms | 0 | 0 ms | 15 KB |
| Hosted chat (mobile) | ar | 100 | 100 | 1,097 ms | 739 ms | 0 | 59 ms | 15 KB |

No accessibility audit failed on any of the eight runs.

#### 3.3.3 When the Arabic font renders

**Method.** Production build, cache disabled, 150 ms latency, 200 KB/s down, 5 cold loads per case, medians. Times are milliseconds from navigation start. Source: `p39-0\scratch\baseline.mjs arabicfont`; results in `chrome\results.json` → `arabicfont`, with screenshots `chrome\arabicfont-*.png`.

| Case | Arabic text first in the DOM | Arabic 400 requested | Arabic 400 arrived | Fallback shown | LCP | Fonts fetched (bytes) |
| --- | --- | --- | --- | --- | --- | --- |
| English page with Arabic text (deep link to 38.6's Arabic visitor) | 4,309 | 4,274 (35 ms before the text) | 5,111 | **796 ms** | 4,340 | Instrument Sans Latin 57,332; Plex Arabic 400 42,848 and 600 45,688 |
| Arabic inbox (`/conversations`, `lang=ar`) | 5,016 (the `h1`) | 1,478 | 2,435 | **0** (the font was there 2.6 s before the page's text) | 5,036 | Instrument Sans Latin 57,332; Plex Arabic 600, 400 and later 500 (45,296 at 4,854) |
| Arabic hosted chat page (`?lang=ar`) | not detected (its heading is the workspace's Latin name) | 1,481 | 2,433 | 0 in practice: the font arrived 2.3 s before LCP (4,708) | 4,708 | same as above |

**What this says:**

- On **Arabic** pages today, the boot screen's «نبرة» in `index.html` already makes the browser fetch the Arabic files as soon as the stylesheet is parsed, so no fallback is seen. They still cost 146 KB on the first paint (Instrument Sans, then Arabic 600 and 400), and the Arabic 500 file arrives late.
- The 38 note 1 problem is the **English** page that shows Arabic customer text. `ArabicFonts.notice()` starts the font only about 35 ms before the text, and the fallback shows for about 0.8 s at this throttling (38.6 measured 1.1 s under its own conditions).

**What 39.2a does with it:**

- Arabic pages preload Arabic 400 and `geist-latin` from the pre-paint script. They are requested at HTML parse instead of at about 1.5 s, and drop to about 71 KB first-paint fonts, with 600 decided by measurement.
- English pages under `/conversations` get a preload of Arabic 400 too. That costs about 43 KB, but removes the 0.8 s fallback where customers write in Arabic.
- 39.2a repeats this exact table, and keeps or drops the English-inbox preload by the numbers.

#### 3.3.4 Accessibility (axe) and the page checks

axe-core (the p36-11 tool copy) ran on every screen of the before pass, with tags `wcag2a`, `wcag2aa`, `wcag21a`, `wcag21aa` and `wcag22aa`. Each screen was also checked for page overflow, a single `h1`, unnamed controls, `dir`, and console errors and warnings. That was 41 screens × 2 widths × (en-light, ar-dark) = **164 page loads**, plus the widget on two host pages.

| Pages | axe violations | Overflow | One `h1` | Unnamed controls | Console | `dir` |
| --- | --- | --- | --- | --- | --- | --- |
| Inbox (`/conversations`), en and ar, 1440 and 390 | 0 | 0 | yes | 0 | clean | correct |
| Overview, en and ar, 1440 and 390 | 0 | 0 | yes | 0 | clean | correct |
| Hosted chat page, en and ar, 1440 and 390 | 0 | 0 | yes | 0 | clean | correct |
| All other screens except the design lab | 0 | 0 | yes | 0 | clean | correct |
| Design lab (dev only), all four cells | `button-name` (5 nodes) | 0 | yes | 0 | clean | correct |

Lighthouse's accessibility audits are in 3.3.2. Results: `p39-0\chrome\shots.json`.

#### 3.3.5 Before screenshots

There are 172 PNG files in `p39-0\shots\`, all full page. The list is in 3.7.

They were taken on the production build at :4300, signed in as the owner in Northwind, with live updates on. The first pass ran before the loopback server forwarded WebSockets, so the inbox read "Live updates paused"; that pass was discarded and the whole set retaken.

At 390 px in Arabic, full-page captures can look shifted by the off-canvas drawer (a 38.8 trap). The measured `overflowX` is 0 everywhere.

### 3.4 Phase 38's twenty notes

| # | Note | What Phase 39 does | Step |
| --- | --- | --- | --- |
| 1 | Arabic font loading | Per-language preload from the pre-paint script, versioned unhashed font URLs, metric fallbacks; measured against 3.3.3 | 39.2a |
| 2 | Report files: DejaVu, truncated headings | Brand fonts (Geist + Plex Arabic, OFL beside them) in PDF; headings wrap; columns sized by content; numeric heading aligned with its numbers (Arabic: both at the end) | 39.7b (backend) |
| 3 | Small form controls on touch | The choice-control family: 20 px box, 44 px target with the label | 39.2b, then every form |
| 4 | The global checkbox rule and `.switch` | Deleted with the family; no escape class | 39.2b |
| 5 | Phone conversation header | One line (customer + handling chip + a facts button); facts, handling bar and help open on demand | 39.3 (shell part), 39.4b |
| 6 | Unread on day one | Quieter unread (dot + heavier name, word in the accessible name, no fill) and "Mark all as read" with the count in the confirmation | 39.1 (route), 39.4a |
| 7 | People shown by email | Member display name; bubbles, takeover lines, assignees, team page show it, email where none | 39.1, then 39.4b and 39.6a |
| 8 | Time zones by IANA id | Localised name from `Intl.DisplayNames`/`Intl.DateTimeFormat` `timeZoneName: 'longGeneric'` where the browser offers one, the id stays visible beside it | 39.6b; report files keep the id (backend, unchanged; noted in 39.7b) |
| 9 | Greeting in one language | Per-language greetings with today's as fallback | 39.1, 39.5b |
| 10 | Overview "Needs attention" rows on phones | The new needs-a-person tile rows: name on its own line, reason chip under it, no truncated title | 39.7a |
| 11 | Skeletons | Each screen step redraws its skeletons to its new geometry; a spec per skeleton checks the loaded and loading boxes match | every screen step |
| 12 | E2E finds by testid and words | Every testid kept; the structural hooks of 3.1.1 kept or moved to testids in the same step; specs' `WORDS` updated where copy changes | every step |
| 13 | Busy inbox row | One "who has it" chip: who handles it when taken over, else who it is assigned to; reason chip; no third line | 39.4a |
| 14 | "Handled by" placement and its two Arabic forms | In the thread header's meta line beside the handling chip. The row's chip says «تتولاها <name>» and the heading keeps «تمّ استلام المحادثة من قِبل …» (see Q8) | 39.4b |
| 15 | Two codes on a merge candidate | One code: the label's short code; "ref." dropped | 39.6a |
| 16 | Report downloads are plain links | Download through the client (`HttpClient` blob, `responseType: 'blob'`), refusals worded by code, a ready file saved under its server name via an object URL; no `innerHTML` | 39.7a |
| 17 | Knowledge list misses an event during first load | Count events that arrive while the first read is in flight; re-read once after it if any arrived | 39.5a |
| 18 | Customer directory has no masked number | `reference` on the customer reads | 39.1, 39.6a |
| 19 | Email preferences schema | Request and response split; response non-null | 39.1 |
| 20 | E2E time limits | Kept. A heavier first paint is measured against them in each step; raised only with a measured reason (none expected: startup JS unchanged, fonts lighter) | every step |

### 3.5 The backend step (39.1)

All five changes are additive and backward compatible. Unknown request members are ignored as today, every new response member is optional for older clients, and no route or code changes meaning. Two migrations are needed. Tests follow the project's patterns: integration tests in `OmniAI.Api.IntegrationTests` (HTTP and permission), store tests in `OmniAI.Infrastructure.IntegrationTests`, domain rules in `OmniAI.Domain.Tests`, the problem codes in `ProblemCodeTests` and `problem-codes.md`, and the document in `OpenApiDocumentContractTests`.

#### 3.5.1 A display name for each member (note 7)

- **Where it lives:** on the account (`OmniAiUser.DisplayName`, table `users`, column `display_name varchar(80) null`), not on the membership. A person's name is the same in every workspace they belong to, and they edit it once.
- **Rules:**
  - Trimmed, with inner whitespace runs collapsed to one space.
  - Empty after trimming means "no name" (null).
  - 1–80 characters after trimming, counted in UTF-16 code units as `WebChatGreeting` does.
  - No control or format-control characters (`Cc`, and `Cf` except ZWJ and ZWNJ, which Arabic and emoji need).
  - Domain value object `MemberDisplayName`.
- **Migration:** `AddUserDisplayName` (generated, nullable column, no backfill).
- **Edit:** `PUT /api/me/display-name`.

  | Item | Value |
  | --- | --- |
  | Body | `{ "displayName": string \| null }` |
  | Response | **204** |
  | Caller | signed in, own account only; no workspace and no permission involved, like `PUT /api/me/language` |
  | Security | antiforgery as on every write |
  | Concurrency | no `If-Match` (last write wins on one's own name, as with language) |
  | Refusal codes (400) | `account.display_name.too_long` (with `limit: 80`, `limitUnit: "characters"`), `account.display_name.control_characters` |
  | Not possible | editing anybody else's name: there is no route with a user id |

- **Reads:**
  - `GET /api/auth/me` and `GET /api/auth/session` → `CurrentUserResponse` gains `displayName` (nullable).
  - **`WorkspaceMemberResponse(UserId, Email, DisplayName?)`** gains it. That single record carries `assignedTo`, `takenOverBy`, transcript `sentBy`, event `actor` and `GET …/conversations/assignees`. `ConversationMessageAuthor` and `NameMembersAsync` read it in the same query that reads the email, so there is no extra round trip.
  - `MemberSummaryResponse` and `MemberResponse` (team) gain `displayName`.
  - `InvitationResponse` gains `invitedByDisplayName` beside `invitedByEmail`.
  - The email stays everywhere it is today.
- **Public pages:** the visitor's transcript keeps saying "Support" / «الدعم» and never a name (decision of 2026-10-19). The display name is staff-facing only; see Q5.
- **Tests:**
  - The edit, with each refusal code, and clearing it.
  - Whitespace collapsing, the 80-character boundary (80 passes, 81 is refused) and ZWJ/ZWNJ kept.
  - Each read carries it: list, detail, transcript `sentBy`, event `actor`, assignees, team list and member, invitation inviter, `me`, `session`.
  - The email is still present.
  - A member of another workspace cannot read it through any workspace route.
  - A null name serialises as `null`.
  - `DatabaseModelAuthorityTests` stays green.

#### 3.5.2 A greeting per language for the public chat (note 9)

- **Where it lives:** two nullable columns beside today's on `web_chat_endpoints`: `widget_greeting_en` and `widget_greeting_ar`, the same `WebChatGreeting` value object (300 characters, no control characters). `WebChatWidgetSettings` gains `GreetingEn` and `GreetingAr`.
- **Migration:** `AddWebChatGreetingPerLanguage` (generated, nullable, no backfill: today's greeting stays the fallback).
- **Edit:** `PUT /api/tenants/{tenantId}/website-chat/{webChatEndpointId}/widget`.
  - Permission `website-chat.manage`, `If-Match` required, and the ETag answered, all as today.
  - The request gains `greetings: { en: string|null, ar: string|null } | null`. When `greetings` is **absent or null**, the stored per-language greetings are unchanged, so a client that does not know the field never clears them. When it is present, both are set (null or empty clears one).
  - `greeting` keeps its meaning as the fallback.
  - Refusals reuse `website-chat.greeting.too_long` and `website-chat.greeting.control_characters` with `field: "greeting" | "greetings.en" | "greetings.ar"` in the problem's extension members. The client also checks the 300 limit before sending.
  - `GET …/widget` and the PUT response gain `widget.greetings { en, ar }`.
- **Public reads:**
  - `GET /api/public/webchat/{publicKey}` and `GET /api/public/embed/{publicKey}` accept an optional `lang` (`en` or `ar`, case-insensitive).
  - `greeting` in the response is the greeting for that language when set, else the fallback. An absent or unknown `lang` gives the fallback, exactly today's answer.
  - **No new member** is added to either public response, so the public read leaks nothing new. Rate limits, the origin check and `no-store` are unchanged.
  - The frontend sends the page's `lang` (`/chat/…?lang=`, and the embed URL the loader already builds).
- **Tests:**
  - Each language, with the fallback when one is missing.
  - An unknown `lang` and an absent `lang`.
  - The edit's `If-Match` (412 on a stale version).
  - Absent `greetings` leaves the stored values, and an explicit null clears.
  - The refusals with their `field`.
  - Both public responses: the same member set as today (a snapshot of the property names), `no-store` kept, another workspace's key unaffected.

#### 3.5.3 Mark all read (note 6)

- **Routes**, on the group `/api/tenants/{tenantId:guid}/conversations` (authorised, antiforgery):

  | Route | Response | What it does |
  | --- | --- | --- |
  | `POST …/read-all` | **200** `{ "marked": n }` | marks every conversation of the workspace read **for the caller**, up to its newest message |
  | `GET …/unread-count` | **200** `{ "count": n }` | so the confirmation can say how many before anything changes |

  This follows the notifications precedent (`POST …/notifications/read-all` → `MarkAllReadResponse(count)`, `GET …/notifications/unread-count`). The static segments cannot collide with `{conversationId:guid}`.
- **Scope:** all the workspace's conversations, not the current filter (Q2).
- **Permission:** `conversations.view`. Without it the answer is 403, as on the single read route. Another workspace answers 404, with nothing touched.
- **Implementation:**
  - **One statement:** `INSERT INTO conversation_reads (conversation_id, user_id, tenant_id, last_read_sequence, read_at) SELECT … newest sequence per conversation of this tenant … ON CONFLICT (conversation_id, user_id) DO UPDATE SET last_read_sequence = GREATEST(conversation_reads.last_read_sequence, EXCLUDED.last_read_sequence), read_at = EXCLUDED.read_at WHERE EXCLUDED.last_read_sequence > conversation_reads.last_read_sequence`.
  - `marked` = the rows inserted or moved forward.
  - Idempotent: a second call answers `marked: 0`.
  - The marker never moves back.
  - No `If-Match`, as for the single marker.
- **Realtime:**
  - Today the single read marker **announces nothing** (`MarkConversationReadHandler`: "no lock and no announcement"). The prompt's "as the read marker does today" therefore has no precedent in the code.
  - The plan follows the notifications one instead. A new structural event **`conversationsRead`** (payload: the workspace id) goes to the caller's own member group (`RealtimeGroups.ForMember(tenant, user)`, the group notifications already use), only when `marked > 0`.
  - The member's other tabs re-read their list. Nobody else is told anything.
- **Problem codes:** none new (403 and 404 as today).
- **Tests:**
  - Unread drops to zero for the caller only: another member's unread is unchanged.
  - Idempotency, and `marked` counts.
  - Another workspace: 404, untouched.
  - A member without `conversations.view`: 403.
  - The event goes to the caller's group only.
  - `unread-count` matches the list's unread rows.
  - **p95 of `read-all` on the dev data** (961 conversations in Northwind), measured on a copy of the dev database, 20 runs, reported.

#### 3.5.4 The masked number on the customer directory (note 18)

- **Reads:** `CustomerRowResponse` (`GET …/customers`) and `CustomerResponse` (`GET …/customers/{id}`) gain **`reference`** (nullable).
  - It is the customer's WhatsApp identity masked by `WhatsAppCustomerId.Masked`, exactly as the conversation reads mask it (`••••` plus the last four).
  - For a customer with several WhatsApp identities, it is the most recently created one.
  - It is null for web visitors.
  - The full number is never sent.
- **Permission:** `customers.view`, unchanged. No migration: it is computed in the existing query (one join, no N+1).
- **Merge candidates** come from the customer list read, so they carry it too.
- **Tests:** a WhatsApp customer (masked, and equal to the conversation read's `reference` for the same customer), a web visitor (`null`), several identities, the full number absent from the JSON, and another workspace.

#### 3.5.5 The email preferences schema (note 19)

- **Split** `EmailPreferencesContract` into:
  - `EmailPreferencesRequest(bool? Reports, bool? Usage, bool? Billing, bool? Knowledge, bool? ChannelHealth)` for `PUT …/notifications/preferences`. `null` is still refused with `notifications.preferences.incomplete`.
  - `EmailPreferencesResponse(bool Reports, bool Usage, bool Billing, bool Knowledge, bool ChannelHealth)` for `NotificationPreferencesResponse.Email`.
- The wire JSON is unchanged and there is no behaviour change. `OpenApiDocumentContractTests` gains an assertion: the response schema's five members are required and not nullable, and the request schema's members stay nullable.

#### 3.5.6 One more item the inbox board needs (owner question Q1)

- **`filter=mine`** on `GET …/conversations`, for the board's "Mine" tab: conversations assigned to the caller **or** taken over by the caller.
- It is additive: `filter=needs-person` is unchanged, any other value is still `400 conversations.filter.invalid`, and `total` counts the filtered set.
- A client-side filter of a server page would break the page-in-URL pagination (rule 7), so without this route the Mine tab is not shipped.
- **Tests:** assigned only, taken over only, both, neither, another member's conversations, paging.

#### 3.5.7 What 39.1 also runs

- Migrations generated (never hand-edited), applied to a fresh database and to a copy of the dev database.
- `pg_dump` of the dev database into `nabra-backend-evidence\p39-1\sql\` before migrating it forward.
- The whole suite in Docker (`-m:1`, 0 warnings, counts per project, vulnerable packages).
- The dev API rebuilt with the `stack.md` commands and the canary run.
- Each new field, route and code checked live, with the refusals.
- The current frontend's e2e suite unchanged against the rebuilt API.
- **Size:** one session.

### 3.6 The steps

Each step keeps rules 1–20. Each runs the full gate set of rule 12 and the Chrome matrix for the screens it touches, with before and after screenshots, and keeps the e2e suite green.

| Step | Contents | Screens and components | Main risks | Tests and journeys that must stay green | Size |
| --- | --- | --- | --- | --- | --- |
| **39.1** backend | 3.5: display name, per-language greeting, mark all read + unread count, `reference` on customers, email preferences schema, `filter=mine` (if Q1) | backend only; problem-codes.md; OpenAPI | migrations on dev data; realtime event naming; p95 of `read-all` | whole backend suite; e2e 46/46 against the rebuilt API, unchanged frontend | 1 session |
| **39.2a** foundations: tokens and type | Calm Glass tokens (raw, roles, component tier) with the alias layer; light/dark; `data-transparency` + "Reduce transparency" in the account menu (stored per browser like the theme, `localStorage['omniai.transparency']`); forced colours; motion tokens and the reduced-motion rules; the glass mixin; fonts (Geist, Plex Arabic 400–700, Instrument Serif) in `public/fonts/` with OFL, per-language preload, metric fallbacks; contrast table in MASTER.md | every screen re-themed through the aliases, layouts unchanged; theme-startup and brand specs | the alias layer hiding a wrong mapping; the root-relative font URL; a font swap shift; CSP hash of the pre-paint script | `theme-startup.spec`, `brand.spec`, i18n catalogue spec; e2e (the whole suite: every screen changes colour); Arabic font timing vs 3.3.3; startup JS | 1 session |
| **39.2b** foundations: components | the choice-control family and removal of the global rule and `.switch`; buttons, chips, inputs, segmented, rows + avatar, bubbles, event lines, toasts, dialogs, menus, popovers, tooltips, skeleton look, empty states, orb, glass surface, brand mark + outlined lockup; design lab rewritten (every component × en/ar × light/dark × desktop/phone × reduced transparency); MASTER.md rewritten for Calm Glass; `admin-design-system.md` token sections | every form that uses a checkbox, radio, switch or segmented control (invite, reports, analytics range, notification preferences, permission picker, role options); design lab | the 38 note 4 specificity coming back; e2e locators (`#widget-position`, `option`, `.bubble`, `.toast-region`); `anyComponentStyle` 4 kB budget per component | a Vitest spec per component (states, en/ar, RTL, keyboard); e2e whole suite; Chrome matrix of the design lab and of the forms touched | 1 session |
| **39.3** shell | floating glass side bar (216 px, collapsed below 1280 px), workspace switcher, areas with counts, member + account menu (theme, reduce transparency, language, sounds, sign out), notifications popover, command palette glass, phone top bar + bottom bar (4 + More), toasts placement, the phone conversation header's one-line frame | shell, profile menu, bell, palette, topbar removal | focus order and landmarks (side bar = navigation, top bar = banner on phones); the 38.1b one-connection rule; three-layer glass budget on phones | one-connection, sound-test, sign-in; keyboard (skip link, menus, Escape, focus return); owner and restricted member (analyst) navigation | 1 session |
| **39.4a** inbox: list | three-pane layout from 1280 px; tabs All · Needs a person (count) · Mine; rows (avatar + channel badge, label, time, preview, one "who has it" chip, reason chip, quieter unread); "Mark all as read" with the count; selected row; keyboard through rows; list skeleton | `conversation-inbox`, `conversation-list`, `inbox-placeholder` | `a.conversation-row`, `row-unread`, `row-reason`, `needs-person-count`, `view-*` hooks; p95 of a 50-row render; sounds | needs-a-person, one-connection, slow-reply; specs for rows (unread/selected/chips), mark-all-read (confirm, count, refusal), tabs | 1 session |
| **39.4b** inbox: thread | three voices (AI + orb, team gold with display name or email, customer cream), event lines, the waiting line with the breathing orb, header (customer, channel, window, handling chip, "Handled by", take over / return / assign), composer, customer panel, one-line phone header with facts on demand, thread skeleton | `conversation-detail`, transcript, composer, context | `.bubble`, `.message-text`, `[data-sender]`, `#conversation-reply`, `.live-state.is-live`, `awaiting-ai`, `handled-by`, `take-over`, `return-to-ai`; no layout shift when messages arrive | deep-link-newest-first, slow-reply, take-over, needs-a-person; the stand-in's `[[slow]]` and `[[fail]]` live; a second member sees takeover and return | 1 session |
| **39.5a** knowledge and agents | knowledge list (states as chips with words, sizes, failure reasons), create, upload, editor (preview), the first-load event gap (note 17); agent list, create, editor with channels and the preview chat in the three voices | `knowledge/*`, `ai-agents/*`, `agent-preview` | `document-state`, `readiness`, `index-button`, `drop-zone`, `agent-preview` hooks; the shared `ChatPanel` used by the preview | knowledge-index, agent-preview-reply | 1 session |
| **39.5b** channels, widget, hosted page | channels hub, website chat (calm form, corners, accent picker with the contrast fallback, snippet with copy), per-language greetings (39.1), WhatsApp list and number; the widget and the hosted page per the boards (glass header, orb + greeting in the visitor's language, three voices, typing, glass composer), 320 px up, light/dark, en/ar | `channels/*`, `shared/chat/*`, `features/chat`, `features/embed` | widget size and the "no forms module, realtime only once a conversation exists" rule; three blurred layers inside a host page; the accent fallback; the 5 starts per 5 minutes per link limit in checks | hosted-page-ar, widget-corners, rename-workspace, slow-reply, take-over (visitor side); embed checked on English and Arabic host pages; transfer size vs 39.0 | 1 session |
| **39.6a** customers, team, roles, invitations | customer directory and page (masked `reference`, one code on merge candidates, rename and merge confirmations); team list with display names and emails, member page, invite (language), invitation lists; roles list and editor with the new choice controls (44 px); **the member's own display name**, edited in a small profile dialog opened from the account menu | `customers/*`, `team/*`, `roles/*`, `permission-picker`, profile menu | unsaved-changes guards, `If-Match` and stale notices on role and member edits; `#invite-email`, `role-option-{id}` | invitation-language; specs for each changed component; owner and restricted member | 1 session |
| **39.6b** billing, plan, settings, notifications | plan & usage meters (numbers and words), billing (fallback plan, honest "not configured"), workspace settings (name, time zone with the schedule count and the localised zone name, note 8), notifications page and preferences with the new controls | `plan-usage/*`, `billing/*`, `workspace-settings/*`, `notifications/*` | the time-zone confirmation; `sound-test` and the switch rewrite; email preferences against the split schema | time-zone, rename-workspace, sound-test | 1 session |
| **39.7a** overview, analytics, reports (frontend) | the bento overview from real data (3.2.4), the serif greeting, the needs-a-person tile rows (note 10); analytics charts in the spec's colours with words and glyphs; report and schedule forms with the new controls; downloads through the client (note 16) | `overview/*`, `analytics/*`, `reports/*`, `shared/charts/*` | chart colour roles (gold lines at 3:1); the client download keeping the file name and the language tag; skeleton geometry of the bento | time-zone, arabic-report; specs per tile and chart (loading, empty, error, no permission) | 1 session |
| **39.7b** report files (backend) | PDF: Geist + IBM Plex Sans Arabic embedded with OFL (replacing DejaVu Sans), headings wrap, columns by content, numeric headings aligned (Arabic at the end); XLSX/CSV unchanged | `PdfReportRenderer`, `PdfText`, fonts in `Reports/Rendering/Fonts/` | Arabic shaping in the PDF library; golden files (37.7) need regeneration and review; PublicNameTests | backend suite in Docker; golden files; one file of each kind in each language opened and checked | 1 session |
| **39.8a** entry pages and brand | sign in, register, forgot/reset password, accept invitation, workspace chooser, not found: calm split layout with the mark, the name in both scripts, one serif line; favicons, app icons, manifest; email templates (header mark, voice colours where a conversation is shown, both languages; backend) checked in Mailpit; widget loader titles and the hosted page title | `features/auth/*`, `invitations`, `workspace-setup`, `layout/entry-frame`, `index.html`, `public/`, backend email renderers | throttling and `Retry-After` messages, the reset flow, invitation language, the signed-out session rule (no 401s); PublicNameTests; email rendering in clients | sign-in, sign-in-throttled, signed-out-no-401, invitation-language; backend suite in Docker for the templates | 1 session |
| **39.8b** final pass and summary | every screen in the full matrix (2 languages × 2 themes × 4 widths + reduced transparency, reduced motion, forced colours), after screenshots next to 39.0's; Lighthouse and axe vs 3.3; startup JS vs 38.0; Arabic font timing; alias layer deleted; e2e twice 15 minutes apart; `PHASE_39_SUMMARY.md` | all | regressions found late; time | the whole suite twice | 1 session |

**Order and why:**

- 39.1 comes first, so every screen step can use the new contracts.
- 39.2a re-themes everything through aliases, so no screen is ever half Two Voices and half Calm Glass in colour.
- 39.2b gives the screen steps their parts. 39.3 (the frame) comes before the screens.
- The inbox comes next, because it is the product's main screen and the heaviest.
- 39.8b closes the phase.

Each step's "after" screenshots go next to 39.0's "before" ones under its own `p39-<N>\shots\`.

### 3.7 The screens table and the screenshots

| Screen | What changes (step) | Components and tokens it will use | Tests |
| --- | --- | --- | --- |
| Shell (side bar, phone bars, menus, toasts) | floating glass side bar, phone bars, glass popovers (39.3) | `.glass`, `--glass-*`, `--color-primary`, `app-brand-mark`, `app-menu`, toasts | one-connection, sound-test, keyboard pass |
| Inbox list, needs a person | three panes, tabs, rows, quiet unread, mark all read (39.4a) | `app-segmented` (links), rows, `app-avatar`, `app-chip`, `--color-selected` | needs-a-person, one-connection, slow-reply |
| Conversation | three voices, events, waiting orb, one-line phone header, customer panel (39.4b) | bubbles, `app-orb`, event lines, `--voice-*`, `--chip-*` | deep-link, slow-reply, take-over |
| Customers, customer | rows, masked reference, one code (39.6a) | rows, `app-avatar`, `app-chip`, dialogs | specs + Chrome |
| Knowledge (list, new, upload, editor) | chips with words, calm forms (39.5a) | `app-chip`, inputs, `app-empty-state` | knowledge-index |
| AI agents (list, new, editor) | calm forms, preview in three voices (39.5a) | `ChatPanel`, bubbles, `app-switch` | agent-preview-reply |
| Channels, website chat, WhatsApp | calm forms, greetings per language, snippet (39.5b) | inputs, `app-radio` (corners), code block | widget-corners |
| Widget, hosted chat page | boards Widget/WidgetAr (39.5b) | glass header/composer, `app-orb`, bubbles | hosted-page-ar, slow-reply, take-over |
| Team, member, invite, roles | display names, choice controls (39.6a) | `app-check`, `app-radio`, rows, dialogs | invitation-language |
| Plan & usage, billing, workspace settings, notifications | meters, localised zones, new controls (39.6b) | meters, `app-switch`, `app-chip` | time-zone, rename-workspace, sound-test |
| Overview | bento (39.7a) | tiles, charts, `--text-display` serif | sign-in, specs per tile |
| Analytics, reports, schedules | charts, forms, client downloads (39.7a) | charts, `app-segmented`, `app-radio` | time-zone, arabic-report |
| Entry pages, workspace chooser, not found | calm split layout (39.8a) | lockup, inputs, buttons, serif line | sign-in, sign-in-throttled, signed-out-no-401 |
| Design lab | rewritten (39.2b) | every component | Chrome matrix |

**Before screenshots** (this step): `C:\Users\mouay\Desktop\nabra-frontend-evidence\p39-0\shots\`, 172 PNG files, `<screen>-<width>-<lang>-<theme>-before.png`:

- 41 screens × {1440, 390} × {en-light, ar-dark};
- the widget closed and open on English and Arabic host pages (`widget-closed-*`, `widget-open-*`).

Screen names: sign-in, create-account, forgot-password, reset-password, accept-invitation, workspace-chooser, overview, inbox, inbox-needs-person, conversation, customers, customer, knowledge, knowledge-new, knowledge-upload, knowledge-editor, agents, agent-new, agent-editor, channels, website-chat, website-chat-detail, whatsapp, whatsapp-detail, analytics, reports, report-schedules, team, team-invite, member, roles, role-new, role-editor, workspace-settings, plan-usage, billing, notifications, notification-preferences, not-found, hosted-chat, design-lab.

**After screenshots:** none in this step; each later step writes its own under `p39-<N>\shots\`.

## 4. Decisions

Choices made in this plan (the owner may overrule any in section 11):

1. **Additive tokens with a deprecated alias layer**, removed in 39.8b. Rejected: renaming every component in 39.2 (one huge, risky step), and keeping the old names forever (names like `--voice-human-*` and `--action` would lie about Calm Glass).
2. **Choice controls wrap the consumer's native `<input>`**, so forms, ids and testids are untouched. Rejected: ControlValueAccessor components (rewiring every form, and the e2e locators on native inputs), and a new global rule (the 38 note 4 problem again).
3. **Buttons stay a CSS class family**, restyled in place. Rejected: a component (199 template edits for no behaviour gain).
4. **Fonts in `public/fonts/` with versioned names and a per-language preload from the pre-paint script.** Rejected: hashed assets without a preload (note 1 stays), and an unhashed preload of a hashed file (double download).
5. **The lockup's names as outlined SVG**, so English pages never download an Arabic font for the logo.
6. **Display name on the account, not per workspace.** Staff-facing only; visitors still read "Support".
7. **Mark all read covers the whole workspace,** with the count asked first, and a member-only realtime event.
8. **Per-language greetings in a nested `greetings` object**, absent means unchanged, so the write stays backward compatible under today's replace-all semantics.
9. **Six of the eight steps split in two (39.2, 39.4, 39.5, 39.6, 39.7, 39.8 → a and b)**: fourteen sessions after this one, as 38.1 and 38.2 were split. 39.1 and 39.3 stay whole.

**Departures from the specification or the boards** (each measured in `p39-0\chrome\contrast.txt`):

| Where | Specification / board | Plan | Why |
| --- | --- | --- | --- |
| Input borders (§6 "sand-400 border") | sand-400 `#CFC7B5` | `--color-control-border` `#8A8370` light (3.78:1 on panel, 3.35 on ground, 3.41 on panel-soft), `#668276` dark (3.62 / 4.10 / 3.27) | sand-400 is **1.68:1** on white: it fails WCAG 1.4.11 (3:1 for a control's boundary). Sand-400 stays for decorative card edges. Q3 |
| Segmented control (§6 "selected raised on panel colour") | panel on panel-soft, a soft shadow | the same, plus a 1 px `--color-control-border` edge and 600 weight on the selected segment | panel on panel-soft is **1.10:1**; the selected state needs a 3:1 cue that is not a shadow (shadows vanish in forced colours). Q3 |
| Status colours (spec silent; today's UI has success/warning/info) | not defined | success = emerald with a tick; warning = gold-850 on gold-100 with a triangle; danger = coral; info = ink-700 on panel-soft with an "i"; always glyph + word | the spec gives gold to "needs a person" and emerald to the AI; reusing them for quota and billing states keeps the palette to three hues, with the glyph and word carrying the meaning. Q4 |
| Chart lines for "previous" and the team (board Overview: gold-500 dashed) | gold-500 | gold-800 lines (6.2:1); gold-500 only for fills, with labels in ink | gold-500 is about **2.2:1** on white: below 3:1 for a graphical object someone must read |
| Board Inbox: search field | shown | not shipped | no search in the API (BA39-3) |
| Board Overview: "First reply" tile | shown | left out | not in the API (BA39-2) |
| Board Overview: "Answered by the AI" | conversations with no person needed | "Replies written by the AI" (messages) | not in the API (BA39-1) |
| Board Widget: "Replies in seconds", quick-reply chips | shown | not shipped | not in the API (BA39-4) |
| Board Inbox: the AI's bubbles on the end side in the inbox | as drawn | kept (inbox: AI and team on the end side, customer on the start side; widget: the visitor's own on the end side) | the boards agree; noted because the perspective flips between inbox and widget |
| §6 AI bubble "with the small orb" in the inbox | the Inbox board shows no orb, only "AI · 10:40" | orb shown (spec wins) | the specification wins over the board |

Deferred: the board's toast "Visitor 7KQ2MX wrote. The AI is answering." becomes possible since 38.7a's stored-message announcement (BA-13 closed). 39.3 builds the toast and 39.4a decides whether that sentence ships, alongside the sounds rule. A real search and the first-reply metric wait for the backend asks.

## 5. Verification

| Check | Result |
| --- | --- |
| `npm run format:check` | **PASS**: all matched files use Prettier style (19.6 s) |
| `npm run test:ci` | **PASS**: 124 files, 1,874 tests, 0 failures (25.8 s; free memory 8.05 GB before) |
| `npm run build` | **PASS**: 0 warnings, budgets met; initial 313.77 kB raw / 73.71 kB transfer. Startup JS **141,423 B** gzip (38.0 baseline 138,907 B, +1.8%; ceiling 152,797 B); unchanged from 38.8. Free memory 13.04 GB before |
| `npm run i18n:check` | **PASS**: 2,448 messages, 0 errors, 0 warnings |
| `npm run e2e` | **PASS**: 46 of 46 (23 journeys × en-ltr and ar-rtl), 0 failures, no retries, 2.2 min; the full suite, including the throttling journey (no full run in the 15 minutes before it). `NABRA_E2E_OUT` and `NABRA_E2E_STATE_DIR` in `p39-0\e2e\`. Free memory 13.03 GB before |
| Backend gates | **NOT RUN**: nothing under `backend/` changed in this step |
| Chrome matrix (baseline, not a redesign check) | **Baseline recorded**: 41 screens × {1440, 390} × {en-light, ar-dark} = 164 loads, plus the widget on English and Arabic host pages (4 cells). 0 overflow, one `h1` everywhere, 0 unnamed controls, 0 console errors, `dir` correct; axe 0 violations except the dev-only design lab (`button-name`, 5). The 768 and 1920 widths, reduced transparency, reduced motion and forced colours: **NOT RUN** (nothing changed in this step; 38.6's 448-load pass covers today's design at all four widths, and each 39.x step runs the full matrix on the screens it touches) |
| Lighthouse and lab vitals | **Baseline measured** (3.3.2): Lighthouse 12.8.2, 8 runs, performance 100 and accessibility 100 on all (warm-cache caveat stated there). Lab medians: LCP 1.08–1.22 s, CLS 0–0.005, INP 32–64 ms |
| Contrast of every new colour pair | **Measured**: 51 pairs from §2–3 (light and dark, glass composited over its ground). All 45 text and voice pairs pass (lowest: dark quiet text on panel-soft 5.46, light quiet text on cream-100 5.07). The failures are the two control-state pairs fixed in section 4 (sand-400 input border 1.68, selected segment 1.10), the gold dot on gold-100 (1.91; decorative, always beside a word) and the decorative lines (1.36 / 1.35; not control boundaries). Replacement control borders: 3.27–4.10. `p39-0\chrome\contrast.txt` |
| RTL mirroring, touch targets | **NOT RUN** as a redesign check (nothing changed); the baseline matrix records `dir` per page. Targets: today's checkboxes are 16 px (note 3); the plan's family is 44 px |
| Fonts | **Baseline measured** (3.3.3). An English first paint fetches Instrument Sans Latin (57,332 B). An Arabic first paint fetches 57,332 + 45,688 + 42,848 B, plus 45,296 B later. Fallback on an English page with Arabic text: 796 ms (throttled); on Arabic pages: 0. Today there is no preload; the plan's per-language preload is specified in 3.2.2 |
| Provider canary | **PASS** before any traffic and again at the end: every listed provider variable absent, `Email__Smtp__Host` loopback, `OMNIAI_DEV_AI_STANDIN` present (info). No visitor conversation was started and no record was written by the checks; the e2e run made its usual synthetic workspace |
| Secrets | **PASS**: `p39-0\scratch\secret-scan.mjs` (725 files across the evidence folder and this report): 0 with the demo password, 0 with an invitation link. The temporary cookie jar (`p39-0\tmp\`) and the Lighthouse profile were deleted; the e2e state folder was removed by the suite's teardown |
| Stack at the end | The API (`/health/ready` 200) and the dev server (:4200) left running. The loopback production-build server (:4300) this step started was stopped. Nothing else was created or removed |
| Memory before heavy gates | 4.16 GB after one Docker page-cache drop at the start, so nothing heavy was started then. 8.05 GB before `test:ci`; 13.0 GB before the build and the e2e run |

## 6. See it live

Nothing new to see in this step: the product is exactly `f42a5c1`. To compare later steps with today:

- The admin app: http://localhost:4200/sign-in, with the **Owner** account `p36.owner@omniai.example`, the **Support agent** `p36.support@omniai.example` and the **Analyst** `p36.analyst@omniai.example` (passwords in `omniai-redesign-evidence\p36-01\demo-accounts.md`, never copied here).
- The inbox: http://localhost:4200/conversations. The 232-message conversation: `/conversations/01a15013-917d-75a7-870c-98ceb0b9e7f8`.
- The hosted chat page: http://localhost:4200/chat/9veAaKzu0Ee-isA7jrQzYw (`?lang=ar` for Arabic).
- Mailpit: http://127.0.0.1:8026.
- The before screenshots: `C:\Users\mouay\Desktop\nabra-frontend-evidence\p39-0\shots\`. Open `inbox-1440-en-light-before.png` beside the Inbox board, and `overview-1440-en-light-before.png` beside the Overview board.

## 7. Backend asks

Things a board shows that the API does not offer. Each ships honestly, as described, until the backend has it.

| Id | Ask | What ships meanwhile | When |
| --- | --- | --- | --- |
| BA39-1 | "Answered by the AI": conversations in a period that ended with no person involved, plus the previous period | The ring shows "Replies written by the AI" from message counts, with its own sentence | After Phase 39 (an analytics change), or in 39.1 if the owner wants it (Q7) |
| BA39-2 | First reply time, median per voice (AI and team), per period | The tile is left out | After Phase 39 |
| BA39-3 | Conversation search (by customer label, short code, masked reference, message text) | No search field in the inbox | After Phase 39 (needs a decision on what is searchable and an index) |
| BA39-4 | Quick replies for the widget (workspace-defined, per language) | No chips under the greeting | After Phase 39 |
| BA39-5 | `filter=mine` | (Q1) If not approved for 39.1, no Mine tab | 39.1 if approved |
| BA-6 (open since 38.0) | Session-check budget | Unchanged | With the tuning before launch |
| BA-8 (open since 38.0) | Web-vitals endpoint | Unchanged | Only if RUM is wanted |

## 8. New Arabic for the owner's review

This step adds **no string to the product** (no file under `frontend/` changed). Some new sentences can already be seen in the plan, and early wording helps the steps. They are listed here as **proposals**, with GLOSSARY.md terms. They are not in the catalogue yet, and each step lists its final strings again.

| English | Proposed Arabic | Where (step) |
| --- | --- | --- |
| Mark all as read | تعليم الكل كمقروء (as the notifications page already says) | inbox (39.4a) |
| Mark {n} conversations as read? | تعليم {n} محادثة كمقروءة؟ (ICU plural: محادثة واحدة، محادثتان، محادثات، محادثة) | inbox confirmation (39.4a) |
| Mine | محادثاتي | inbox tab (39.4a) |
| Reduce transparency | تقليل الشفافية | account menu (39.2a) |
| Display name | الاسم الظاهر | account (39.6a) |
| Your name as your team sees it. | اسمك كما يراه فريقك. | account (39.6a) |
| Greeting in English / Greeting in Arabic | الترحيب بالإنجليزية / الترحيب بالعربية | website chat (39.5b) |
| Visitors whose language has no greeting of its own see this one. | يرى هذا الترحيبَ الزوارُ الذين لا يوجد ترحيب بلغتهم. | website chat (39.5b) |
| Replies written by the AI | الردود التي كتبها الذكاء الاصطناعي | overview (39.7a) |
| Powered by Nabra (if Q6) | بدعم من نبرة | widget (39.5b) |
| Good morning / Good afternoon / Good evening, {name} | صباح الخير / مساء الخير، {name} | overview greeting (39.7a) |

## 9. Risks and open issues

1. **Size of 39.2a.** Re-theming every screen through aliases can surface low-contrast corners the token table did not foresee: the Chrome matrix of 39.2a covers all 41 screens, not only the three named in its prompt.
2. **Root-relative font URLs** must pass through the build untouched. 39.2a verifies this first, and the fallback is described in 3.2.2.
3. **Glass cost.** `backdrop-filter` on a 216 px side bar over a scrolling content column repaints on scroll. 39.3 measures scroll frame times at 1440 px with the side bar's blur on and off, and drops to the solid twin if frames go over budget.
4. **The PDF library's Arabic shaping with Plex Arabic** (39.7b) is unproven. If it shapes worse than DejaVu does today, the step reports it and keeps DejaVu for Arabic.
5. **E2E wording.** Specs hard-code their `WORDS`. Any reworded sentence must change the spec in the same step, never the other way round.
6. **"Answered by the AI"**: the honest substitute measures replies, not conversations. The owner may prefer to leave the tile out (Q7).
7. **The baseline Lighthouse is 12.8.2** (the version in the local npx cache), not 13. 39.8b uses the same version for comparability.
8. The **design lab has 5 unnamed buttons** today (dev-only). 39.2b fixes them.

## 10. Proposed checkpoint commit message

```text
docs: plan the Calm Glass redesign (Phase 39.0)

Inventory every screen, its testids and journeys; map calm-glass.md to
tokens, fonts, components and layouts; measure the baselines (startup JS,
lab vitals, Lighthouse, axe, Arabic font timing) and take before
screenshots; answer Phase 38's twenty notes; specify the 39.1 backend
contracts (display name, per-language greeting, mark all read, masked
reference on customers, email preferences schema); and order the steps.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

## 11. Questions for the owner (the plan's section 7)

Each has options and a recommendation. Steps that depend on an answer say so above.

- **Q1. The inbox's "Mine" tab** needs `filter=mine` (3.5.6), a sixth backend item.
  - (a) Add it to 39.1. **Recommended**: small, additive, and the board shows the tab.
  - (b) Ship All · Needs a person only.
- **Q2. "Mark all as read" scope.**
  - (a) The whole workspace, with the count in the confirmation. **Recommended**: one clear meaning, and it solves day one.
  - (b) Only the current tab's conversations: needs the filter in the route too.
- **Q3. Control borders and the selected segment** (section 4).
  - (a) `--color-control-border` (`#8A8370` / `#668276`) on inputs, checkboxes, radios and the selected segment. **Recommended**: it meets WCAG 1.4.11.
  - (b) Keep sand-400 as the specification says. It fails 3:1 for control boundaries.
- **Q4. Status colours** (quotas, billing, indexing), which the specification does not define.
  - (a) Reuse the three hues with distinct glyphs and words: emerald tick, gold triangle, coral octagon, neutral "i". **Recommended**.
  - (b) Add a separate amber and blue for warning and info.
- **Q5. The display name on public pages.**
  - (a) Staff-facing only; visitors keep reading "Support". **Recommended**: privacy by default, and today's decision.
  - (b) Show the person's display name to visitors.
- **Q6. "Powered by Nabra"** in the widget (on the board, not in today's product; no link while there is no domain).
  - (a) Add it, quiet, in both languages, without a link. **Recommended**: the board and `brand.md` show it.
  - (b) Leave it out.
- **Q7. The overview's "Answered by the AI" ring.**
  - (a) Ship "Replies written by the AI" from message counts now, and ask the backend for the conversation measure later. **Recommended**.
  - (b) Leave the tile out until BA39-1.
  - (c) Add BA39-1 to 39.1.
- **Q8. "Handled by" in Arabic** (note 14).
  - (a) Keep both forms: the heading's «تمّ استلام المحادثة من قِبل …» and the row chip «تتولاها …», as on the Main board. **Recommended**: each fits its place.
  - (b) One form everywhere.

## 12. Owner's decisions (2026-11-13)

The plan is accepted as written, with these answers and one correction.

- **Q1 (a).** Add `filter=mine` to 39.1 (BA39-5), and ship the Mine tab in 39.4a.
- **Q2 (a).** "Mark all as read" covers the whole workspace, with the count in the confirmation.
- **Q3 (a).** `--color-control-border` (`#8A8370` light, `#668276` dark) for every control boundary and for the selected segment's edge. Sand-400 stays for decorative edges.
- **Q4 (a).** Status words reuse the three hues, each with its own glyph and word. In the inbox and the transcript only the voice chips appear, so a status chip never sits beside a voice chip of the same hue.
- **Q5 (a).** Display names are staff-facing only. Visitors keep reading "Support" / «الدعم».
- **Q6 (a).** "Powered by Nabra" / «بدعم من نبرة» in the widget: quiet, in both languages, with no link while there is no domain.
- **Q7 (c), with a fallback.** Add BA39-1 to 39.1 as its seventh item, if it is one aggregate over data the API already stores, with no migration and no new event: the conversations started in the period in which the AI replied and no person replied or took over, with the same figure for the previous period, beside today's summary. The overview tile then reads "Answered by the AI", with a sentence that says what it counts. If 39.1 finds it cannot be computed exactly from today's data, it says so in its report, and the tile ships as (a), "Replies written by the AI".
- **Q8 (b), a correction.** «تتولاها …» names the member's gender (a man would need «يتولاها …»), and the app does not know a member's gender. The Main board's «تتولاها سارة» is type-specimen text, not copy. Keep 38.8's accepted, gender-neutral forms: the heading «تمّ استلام المحادثة من قِبل …», and «استلام المحادثة: …» in rows and chips. Never use a gendered verb for a member.
- **Section 8 wording.** "Visitors whose language has no greeting of its own see this one." becomes «يظهر هذا الترحيب للزوار الذين لا يوجد ترحيب بلغتهم.»
- **The 21st.dev MCP** stays optional. When it is not connected, a step builds from our own primitives (section 1), and the owner's assistant reviews each step's screens against current references.
