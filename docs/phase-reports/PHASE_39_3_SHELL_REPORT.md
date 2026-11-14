# Phase 39.3: the shell (navigation, top bar, menus, notifications, toasts)

Step 39.3 of the Calm Glass redesign (plan: `PHASE_39_0_REDESIGN_PLAN_REPORT.md` §3.6, "39.3 shell", one session, not split). Evidence: `C:\Users\mouay\Desktop\nabra-frontend-evidence\p39-3\`.

## 1. Summary

- On a desktop, Nabra's navigation is now a floating glass side bar: the mark and the name, search and notifications, the workspace switcher, the areas under four short labels (Conversations · AI · Insights · Workspace) with the inbox's needs-a-person count, and the member (name and sounds state), which opens the account menu.
- On narrower desktops (992–1279 px) the side bar becomes a rail of icons, each named in a glass tooltip on hover and focus, with a thin divider between sections.
- On phones and tablets a glass top bar names the area and the workspace, and a bottom bar offers four areas plus "More", chosen by what the member may open; "More" opens a glass sheet with the rest.
- The workspace switcher is a menu of the account's workspaces; the notification panel takes focus and keeps Tab inside it; toasts sit beside the side bar on a desktop and under the top bar on a phone.
- On a phone, a conversation's header is one line (back, the customer, who is answering, Details); the facts, the take-over action and its help open with Details.

## 2. Starting point

- Branch `master`, HEAD `d2521aa` ("give every checkbox, radio and switch a 44 px target …" (Phase 39.2b)), worktree clean (`git status --short` empty), as the prompt's starting point says.
- Dev stack running (p36: `omniai-p36-api`, Mailpit, Postgres, Redis); provider canary PASS before any traffic (`gates/canary-start.txt`). Dev server on `http://localhost:4200`.

## 3. Changes by area

`git status --short` (39 modified, 8 new paths plus this report):

```text
 M docs/architecture/admin-design-system.md
 M docs/design/MASTER.md
 M frontend/src/app/core/auth/auth-api.ts
 M frontend/src/app/core/feedback/toast-host.scss
 M frontend/src/app/core/i18n/arabic-render.spec.ts
 M frontend/src/app/core/media/media.ts
 M frontend/src/app/features/conversations/conversation-detail/conversation-detail.html
 M frontend/src/app/features/conversations/conversation-detail/conversation-detail.scss
 M frontend/src/app/features/conversations/conversation-detail/conversation-detail.spec.ts
 M frontend/src/app/features/conversations/conversation-detail/conversation-detail.ts
 M frontend/src/app/layout/app-shell/app-shell.html
 M frontend/src/app/layout/app-shell/app-shell.scss
 M frontend/src/app/layout/app-shell/app-shell.spec.ts
 M frontend/src/app/layout/app-shell/app-shell.ts
 M frontend/src/app/layout/command-palette/command-palette.spec.ts
 M frontend/src/app/layout/navigation.ts
 M frontend/src/app/layout/notification-bell/notification-bell.html
 M frontend/src/app/layout/notification-bell/notification-bell.scss
 M frontend/src/app/layout/notification-bell/notification-bell.spec.ts
 M frontend/src/app/layout/notification-bell/notification-bell.ts
 M frontend/src/app/layout/profile-menu/profile-menu.html
 M frontend/src/app/layout/profile-menu/profile-menu.scss
 M frontend/src/app/layout/profile-menu/profile-menu.spec.ts
 M frontend/src/app/layout/profile-menu/profile-menu.ts
 M frontend/src/app/layout/sidebar/sidebar.html
 M frontend/src/app/layout/sidebar/sidebar.scss
 M frontend/src/app/layout/sidebar/sidebar.spec.ts
 M frontend/src/app/layout/sidebar/sidebar.ts
 M frontend/src/app/layout/topbar/topbar.html
 M frontend/src/app/layout/topbar/topbar.scss
 M frontend/src/app/layout/topbar/topbar.ts
 M frontend/src/app/shared/menu/menu.spec.ts
 M frontend/src/app/shared/menu/menu.ts
 M frontend/src/app/shared/tooltip/tooltip.spec.ts
 M frontend/src/app/shared/tooltip/tooltip.ts
 M frontend/src/locale/messages.ar.json
 M frontend/src/locale/messages.json
 M frontend/src/styles/_primitives.scss
 M frontend/src/styles/_tokens.scss
?? frontend/src/app/layout/bottom-bar/            (bottom-bar.ts/.html/.scss/.spec.ts)
?? frontend/src/app/layout/nav-counts.spec.ts
?? frontend/src/app/layout/nav-counts.ts
?? frontend/src/app/layout/navigation.spec.ts
?? frontend/src/app/layout/topbar/topbar.spec.ts
?? frontend/src/app/layout/trap-tab.spec.ts
?? frontend/src/app/layout/trap-tab.ts
?? frontend/src/app/layout/workspace-switcher/    (workspace-switcher.ts/.html/.scss/.spec.ts)
?? docs/phase-reports/PHASE_39_3_SHELL_REPORT.md
```

Nothing under `backend/` or `deploy/` changed. No package was added, removed or upgraded.

### 3.1 What changed, and why

**The shell (`layout/app-shell`).** Three layouts from two media queries (`desktop` 992 px, new `sidebarFull` 1280 px): **wide** (the full side bar), **rail** (the side bar collapsed), **bars** (top bar + bottom bar). The pin, the peeking rail, the drawer, its scrim and the desktop top bar are gone (the plan's "topbar removal"). The content sits on the ground with the two glows (900 px emerald at the start-top, 760 px gold at the end-bottom, `aria-hidden`, hidden in forced colours). The frame is `overflow: clip`, so focus can never scroll it; only `main` scrolls. One bell, one account menu and one palette trigger exist at any width (the e2e locators stay unambiguous). The palette's "Open the notification panel" reaches whichever bell is on screen.

**The side bar (`layout/sidebar`).** 216 px, radius 24, 18/14 px padding, 14 px from the window and 12 px from the content (board Inbox). From the top: the mark and "Nabra" (`brand.name`) with the palette trigger and the bell (icon buttons named by their words, with glass tooltips); the workspace switcher (a 44 px panel button); the areas (40 px, radius 12, 18 px icons) under four labels; the member at the foot. The glass is a sibling layer behind the content, not the bar's own background, so a menu or panel opening from the bar blurs the page instead of nothing (an element with `backdrop-filter` is a backdrop root for its descendants). The inbox's needs-a-person count is a gold pill (999+ cap; the exact number in the words a screen reader hears). On the rail the names are visually hidden but kept as the links' names, a glass tooltip shows them on hover and keyboard focus (placed against the window, so the scrolling list never clips it), and a 1 px control-border divider stands where each later section's label was.

**Navigation model (`layout/navigation.ts`).** The owner's assistant's note, implemented: sections `Conversations` (Overview, Inbox, Customers) · `AI` (Knowledge, AI agents, Channels) · `Insights` (Analytics, Reports) · `Workspace` (Team, Roles & permissions, Workspace settings, Plan & usage, Billing). A section with no area the member may open is not shown (the analyst sees Conversations → Overview, Insights, Workspace). The `conversations` area is labelled "Inbox" (`nav.inbox`, «صندوق المحادثات», the glossary's term). Routes and paths are unchanged. New: `PHONE_BAR_PRIORITY`, `phoneBarSplit()`, `navItemForUrl()`, `INBOX_PATH`.

**The phone's bars (`layout/topbar`, new `layout/bottom-bar`).** The top bar (56 px, glass, the banner landmark) shows the area's name (context, not a heading), the workspace switcher as a quiet line under it, then search, the bell and the account. The bottom bar (64 px, floating glass, 8 px from the edges and above the safe area) holds the first four of Inbox, Overview, Customers, Knowledge, Analytics, Reports the member may open, then "More". "More" is a native modal `<dialog>` sheet (glass over the scrim) with the remaining areas under the side bar's labels; it opens on its first area, keeps Tab inside, closes on Escape, a tap on the scrim or a navigation, and returns focus to "More". "More" is marked current when the page belongs to one of its areas. The content runs under both bars; `main`'s padding and `scroll-padding` keep the page's ends and any focused control clear of them (WCAG 2.4.11).

**Workspace switcher (new `layout/workspace-switcher`).** With one workspace it is the name only (`data-testid="workspace-name"`). With more, an `app-menu` (glass) lists the account's workspaces as `menuitemradio`s (the current one checked, names isolated with `dir="auto"`), then "Choose or create a workspace" (the chooser). Choosing another workspace navigates to the chooser first (so a page's unsaved-changes guard asks, and staying cancels the switch), then selects the workspace exactly as the chooser does, then opens its overview. Variants: panel (side bar), rail (letter only, named in full), bar (top bar).

**Account menu (`layout/profile-menu`).** New `member` variant for the side bar's foot: the 34 px gold avatar, the display name (39.1's `displayName` from `GET /api/auth/me`, now typed on `AuthenticatedUser`; the address when none was set) and "Sounds on/off" for a member who may hear the inbox. Its words are its name (plus a hidden "Account menu"). Initials come from the display name's first two words (Arabic letters for an Arabic name), else the address. The menu's content (identity, theme, reduce transparency, language, notification preferences, sounds, sound settings, switch workspace, sign out) is unchanged; the identity now shows the display name above the address when there is one.

**Notification panel (`layout/notification-bell`).** A glass popover: opening it (by click too, not only from the palette) moves focus to its heading, Tab and Shift+Tab stay inside (`layout/trap-tab.ts`), Escape closes and returns focus to the bell. In the side bar it opens beside the bar at the top of the content (`--shell-nav-end`); on a phone it hangs under the top bar within the gutters; on a tablet it is 26 rem at the bar's end. It still never opens itself and opening still marks nothing read.

**Menus (`shared/menu`).** A menu that opens upward is sized to the room above it, and the class is applied before focus moves in (the account menu at the foot of a short window had scrolled the frame). Optional `triggerTestId`.

**Tooltips (`shared/tooltip`).** No words, no tooltip (the full side bar passes '' because its names are on screen); `below` placement for the top bar; `fixed` (placed against the window, measured from the trigger when it opens) and `edge` (clear an ancestor’s edge: the rail’s, not its item’s) for the rail.

**Toasts (`core/feedback/toast-host.scss`).** Bottom start beside the navigation on a desktop (reads `--shell-nav-end`), top under the top bar on a phone (dropping in rather than rising). Behaviour unchanged: 5 s (errors 8 s), held on hover and focus, polite status region for confirmations and an alert region for failures.

**Conversation, phone header (`features/conversations/conversation-detail`).** Under 768 px: back (an icon button, its words kept as its name), the customer (one truncating line), the handling chip, Details. Details now also opens the facts line, the handling bar (handled by, Take over / Return to AI with `aria-describedby` help, help shown) and Refresh, plus the context panel as before. The chip appears once (on the title's line). Wider screens are unchanged. The layout uses the grid build's layout utilities (`d-flex`, `flex-wrap`, `align-items-center`, `p-2`) bound in the template, which kept the component's stylesheet under its 4 kB budget.

**Tokens (`styles/_tokens.scss`).** `--sidebar-width` 216 px (was 240), new `--sidebar-rail-width` 72, `--sidebar-padding-block/-inline` 18/14, `--nav-item-height` 40, `--bottombar-height` 64 (78 under `:lang(ar)`); `--sidebar-width-collapsed` and the unused `--z-drawer` removed. Component tier: `--nav-active-bg/-fg/-edge`, `--nav-hover-bg`, `--nav-count-bg/-fg`, `--member-avatar-bg/-fg` (light and dark).

**Docs.** `MASTER.md`: §5 shell sizes, six decisions-log rows, §13.2 the shell's contrast pairs, §14.1 the shell. `admin-design-system.md` D2: the shell tree and its state.

### 3.2 Screens

| Screen | What changed | Components and tokens it now uses | Tests |
| --- | --- | --- | --- |
| Shell, desktop ≥ 1280 px (every admin page) | floating glass side bar, four section labels, inbox count, member block, palette + bell in the head, glows | `app-sidebar`, `.glass`, `app-brand-mark`, `app-workspace-switcher`, `app-notification-bell`, `app-profile-menu` (member), `--sidebar-*`, `--nav-*`, `--member-avatar-*`, `--glow-a/-b` | `sidebar.spec` (16), `app-shell.spec`, `navigation.spec`, `nav-counts.spec` |
| Shell, rail 992–1279 px | icons, tooltips (window-placed), dividers, avatar-only member | `[appTooltip][fixed]`, `--sidebar-rail-width` | `sidebar.spec` (rail), `tooltip.spec`, `app-shell.spec` (rail) |
| Shell, phone and tablet < 992 px | glass top bar (area + workspace + actions), bottom bar 4 + More, "More" sheet | `app-topbar`, `app-bottom-bar`, native `<dialog>`, `--topbar-height`, `--bottombar-height` | `topbar.spec`, `bottom-bar.spec` (10, incl. the Tab trap), `app-shell.spec` (bars) |
| Workspace switcher | glass menu of workspaces + chooser | `app-menu`, `.menu`, `--color-control-border` | `workspace-switcher.spec`, `sidebar.spec` |
| Account menu | member block trigger, display name | `app-menu`, `--member-avatar-*` | `profile-menu.spec` (+4) |
| Notifications panel | focus in, Tab trapped, placement per layout | `.glass`, `trapTab` | `notification-bell.spec` (+2), `trap-tab.spec` |
| Toasts | placement per width | `.glass`, `--shell-nav-end` | e2e `rename-workspace`, `time-zone` (toast region) |
| Conversation (phone) | one-line header, facts on demand | layout utilities, `app-voice-chip` | `conversation-detail.spec` (+3) |
| Command palette | unchanged look; the trigger moved into the side bar / top bar | — | `app-shell.spec` (palette) |

### 3.3 Screenshots

`<evidence>\p39-3\shots\`, named `<screen>-<width>-<lang>-<theme>-<before|after>.png`, at 1440, 1024 (the rail) and 390 px, English light and Arabic dark. "Before" was captured from HEAD `d2521aa` on this session's dev server before any change.

- `shell-overview-*`, `shell-inbox-*`, `conversation-*`, `shell-account-menu-*`, `shell-notifications-*`, `shell-palette-*`, `shell-analyst-*` (the restricted member, on Analytics): before and after.
- `shell-workspace-switcher-*` and `shell-more-390-*`: after only (there was no switcher menu or "More" before; before, `shell-drawer-390-*` shows the drawer that "More" replaces).
- `shell-reduced-transparency-media-*`, `shell-reduced-transparency-setting-*`, `shell-reduced-motion-*`, `shell-forced-colours-*`: after, 1440 (account menu open) and 390 ("More" open).

44 before and 66 after PNGs (50 shell captures with 0 overflow, `h1`, naming, console or blur-budget issues, and 16 mode captures).

## 4. Decisions

1. **The design note's four sections**, as written, and recorded in MASTER.md: Conversations (Overview, Inbox, Customers) · AI (Knowledge, AI agents, Channels) · Insights (Analytics, Reports) · Workspace (Team, Roles & permissions, Workspace settings, Plan & usage, Billing). The labels are the caption style in the quiet tone, sentence case, no tracking, no capitals in either language (the house rule since 36.3; uppercase tracked Latin labels were considered and rejected, so both scripts read the same). On the rail a 1 px divider replaces each later label. Empty sections are hidden.
2. **"Conversations" became "Inbox" in the navigation** (`nav.inbox`, «صندوق المحادثات»): the board and the glossary name it so, and a section called Conversations holding an item called Conversations would read twice. The page's own heading and route are unchanged; the palette lists it as "Inbox".
3. **Pin, peek and drawer removed** (the plan: "216 px, collapsed below 1280 px"; "topbar removal"). The pin preference key `omniai.sidebar-pinned` is no longer read; an old value is harmless.
4. **The workspace switch goes through the chooser's own path** (navigate to the chooser, select, open the overview) rather than switching in place, so unsaved-changes guards ask and no page keeps the old workspace's state. Verified live with a dirty form (§5). Rejected: switching in place (pages that read the tenant once would show stale data).
5. **Popovers vs menus.** The notification panel and the "More" sheet move focus in and keep Tab inside (calm-glass.md §6 "focus trapped"). The workspace switcher and the account menu keep the menu pattern: arrow keys, Home/End, Escape returns focus, and Tab leaves and closes, as the APG menu button pattern and the product's `Menu` have done since 36.4. Trapping Tab in a menu would break that pattern; recorded as the reading of "focus trapped" for menus.
6. **The glass is a sibling layer** (`.sidebar-glass`, `.topbar-glass`, `.bottom-bar-glass`) rather than the bar's background, so popovers opening from a bar are not inside a backdrop root and blur the page behind them. The budget counter still sees each as one layer.
7. **The inbox count reuses `InboxSignals`** (38.2b) through `NavCounts`, which only says "the count is on screen": no new request, one reader per browser and workspace, lazy chunk. Consequence: with the side bar showing the inbox, the reader is active on every page (it was active only when sounds, desktop alerts or the inbox were on). Its reads stay event-driven and coalesced (at most one per 5 s, one tab).
8. **Toasts keep their two regions and lifetimes**: confirmations polite at 5 s; failures in an alert region at 8 s. The specification says "4–6 s … announced politely"; failures keep the existing guarantee (rule 7) because they usually say what to do next. Departure recorded.
9. **Departures from the boards**, each measured (§5): the switcher's border is the 3:1 control border (owner decision Q3), not the board's line; the dark current area has a 1 px control-border edge, because the board's 16% tint alone is 1.35:1 against the glass; the board's member line "Sounds on" is shown only to members who may hear the inbox (others have no sounds).
10. **Phone conversation header (Phase 38 note 5, the shell's part)**: the take-over action moved behind Details on phones, as the plan says ("facts, handling bar and help open on demand"). 39.4b redraws the thread and may revisit this.
11. **Component style budgets kept at 4 kB**: the glass fallbacks come from the shared `.glass` class (not the mixin per component), forced-colour rules are merged, and the conversation's phone header uses the grid build's layout utilities. One dead declaration (`font-stretch` at 100% on a font with no width axis) was removed. No budget was raised.
12. **Long names wrap rather than truncate.** «صندوق المحادثات» does not fit beside its count in a 216 px bar, nor in a fifth of a 390 px bottom bar. A shorter Arabic name («المحادثات») was tried and rejected: the glossary's Inbox is «صندوق المحادثات» and `i18n:check` would warn. Instead the side bar's names wrap onto a second line, the bottom bar uses the caption token (13 px in Arabic, as MASTER.md requires; it had been a fixed 12 px) and is 78 px tall in Arabic (64 px in English). Counts carry `dir="ltr"` so «99+» never reads «+99».
13. **Startup JavaScript +4,045 B** (gzip) for the side bar, both bars, the switcher, the count bridge and the Tab trap. The bars are not deferred: on a phone they are the navigation, and a late bar would shift the page.

Deferred: the board's toast "Visitor 7KQ2MX wrote. The AI is answering." (39.4a decides it with the sounds rule, as planned); the inbox's own redraw (39.4a/b).

## 5. Verification

All gates ran one at a time, never beside a browser check or a Docker run; free memory was 8.4–13.1 GB before each (`gates/memory.log`), so no page-cache drop was needed. Logs in `<evidence>\p39-3\gates\` and `chrome\`.

| Check | Result |
| --- | --- |
| `npm run format:check` | **PASS**: all files use Prettier style (`gates/format-check.log`). |
| `npm run test:ci` | **PASS**: 138 files, 1,979 tests, 0 failures (39.2b: 132 / 1,930). New specs: `bottom-bar`, `topbar`, `workspace-switcher`, `navigation`, `nav-counts`, `trap-tab`; rewritten: `sidebar`, `app-shell` layout sections; extended: `profile-menu` (member), `notification-bell` (focus + Tab trap, side placement), `tooltip` (no words, below, fixed, edge), `menu` (test id), `conversation-detail` (phone header, 3), `arabic-render` (the four section labels and «صندوق المحادثات»), `command-palette` ("Inbox"). |
| `npm run build` | **PASS**: 0 warnings, 0 errors; every budget met without raising one (four component stylesheets went over 4 kB during the step and were trimmed: decision 11). Initial total 330.04 kB raw / 75.67 kB transfer. |
| Startup JavaScript (loader + application chunk, gzip) | **148,413 B** (brotli 130,893). Before (HEAD `d2521aa`, 39.2b X1): 144,368 B. 38.0 baseline 138,907 B → **+6.8%**, under the 10% ceiling of 152,797 B (`gates/startup-js.txt`). |
| `npm run i18n:check` | **PASS**: 2,472 messages, 0 errors, 0 warnings. |
| `npm run e2e` | **PASS twice, full suite**: 46 journeys (23 × en-LTR and ar-RTL), 46 passed, 0 failed, 0 retries; 2.9 min (started 14:40) and 3.0 min (started 15:03), 23 minutes apart, so no run was limited by the 15-minute rule. `NABRA_E2E_OUT` and `NABRA_E2E_STATE_DIR` under `<evidence>\p39-3\e2e\`. Canary PASS before each run. |
| Backend gates | **NOT RUN**: nothing under `backend/` changed in this step. |
| Chrome matrix (Playwright library driving the installed Google Chrome, temporary profiles; the Chrome DevTools MCP servers were not used) | **PASS**: 208 loads = owner (Overview, Inbox, a 232-message conversation, Knowledge, Analytics, Team, Workspace settings, Notifications) and analyst (Overview, Analytics, Reports, Workspace settings, Notifications) × 390 / 768 / 1280 / 1920 × en / ar × light / dark. 0 problems: no page overflow, exactly one `h1`, no unnamed control, no console error or warning, no shell target under 24 px, never more than 3 blurred layers (max 2 at rest); axe (WCAG 2.2 AA tags) on the 26 loads at 1280 light: 0 violations. Areas shown: owner 13 in the side bar / Inbox, Overview, Customers, Knowledge + More on a phone; analyst Overview, Analytics, Reports, Workspace settings, Plan & usage / Overview, Analytics, Reports + More. |
| Screenshots | 44 before, 66 after (`shots\`): 50 shell captures at 1440, 1024 (rail) and 390 × en-light, ar-dark with menus open (0 issues), plus 16 mode captures. |
| Reduced transparency (OS media query and the in-app setting) | **PASS**: 0 blurred layers with the account menu (1440) or "More" (390) open; solid twins drawn. |
| Reduced motion | **PASS**: menu and sheet animations ~0 s (`1e-05s`); state changes still shown. |
| Forced colours | **PASS**: 0 blurred layers, no shadows, the current area and the counts in `Highlight`/`HighlightText`, glows hidden. |
| Keyboard | **PASS** at 1440 en/ar, 1100 en/ar (rail), 390 en/ar: Tab 1 is the skip link and Enter moves focus to `main`; order is skip link → search → notifications → workspace → areas → member → content (phones: top bar → content → bottom bar); account menu opens on its first item, arrows move, Escape returns focus to the trigger; notification panel opens on its heading, 14 Tabs stay inside, Escape returns to the bell; switcher opens on the current workspace, Escape returns; "More" opens on its first area, Tab stays inside, Escape returns to "More". Rail tooltips: shown on keyboard focus, beside the rail's edge, centred on their item, on top, in both directions. Icon-only controls are named ("Search or jump to…", "Notifications, 43 unread notifications", "Workspace: … . Switch workspace", "PO: Account menu for …", "Inbox, Needs a person: 148"). |
| Workspace switch, live (`chrome/switch-check.json`) | **PASS** at 1440 en and 390 ar: choosing Al Noor opens its overview; with an unsaved name in Workspace settings the browser's confirm appears, staying keeps the page and the workspace, leaving opens Northwind's overview. Nothing was saved. |
| Glass cost (plan risk 3): scroll frames at 1440, 4× CPU throttling, the long conversation | blur on: median 16.7 ms, p95 16.9 ms, 1 frame over 33 ms of 100; blur off (reduced transparency): 16.7 / 16.8 ms, 0 of 89. No reason to drop to the solid twin. |
| Contrast of every new pair (`chrome/contrast.txt`, `contrast.json`) | **PASS 72 / 72**: light and dark, on the glass over the ground, over each glow at its peak, and as the solid twin. Worst text pair 4.94:1 (quiet 12 px section labels on light glass over the emerald glow); current area words 8.75:1 light / 9.2:1 dark; inbox count and member avatar ink on gold 7.08:1; control-border edges and the switcher border 3.39:1 or more. Recorded in MASTER.md §13.2. |
| RTL mirroring | **PASS**: side bar on the right, glows from the start-top, tooltips and the notification panel open towards the inline end, the switcher's chevron and the back arrow mirror, counts and «99+» are LTR islands, Arabic names in `dir="auto"` isolates. |
| Touch targets | **PASS**: bottom-bar items a full column (≥ 64 px tall, 70 px wide at 390), More-sheet items 44 px, top-bar buttons 44 px, the phone header's back and Details 44 px; desktop areas 40 px (≥ 24 px). |
| Fonts | **NOT RE-MEASURED**: this step adds no font file and changes no preload; the shell uses the faces 39.2a self-hosts (Geist Latin; IBM Plex Sans Arabic 400/500/600, preloaded on Arabic pages). 39.2a's numbers stand: first-paint fonts 117,936 B English, 163,232 B Arabic; swap with metric fallbacks. |
| Provider canary | **PASS** at the start and before each e2e run: every provider variable absent, SMTP host loopback (Mailpit), `OMNIAI_DEV_AI_STANDIN` present as information (`gates/canary-*.txt`). |

## 6. See it live

- Admin app: http://localhost:4200 (dev server, running). API with the AI stand-in: http://127.0.0.1:5027. Mailpit: http://127.0.0.1:8026.
- Accounts (from `omniai-redesign-evidence\p36-01\demo-accounts.md`; passwords in that file only): **Owner** `p36.owner@omniai.example` (two workspaces), **Analyst** `p36.analyst@omniai.example` (the restricted member), **Support agent** `p36.support@omniai.example`.

What to try:

1. As the owner at 1440 px: the side bar's four sections and the gold count on Inbox; the workspace button opens "Your workspaces"; choose «شركة النور للتجارة الإلكترونية» and you land on its overview; the member block at the foot opens the account menu (theme, Reduce transparency, language, sounds, sign out).
2. Press Tab from the top: the skip link, search, notifications, the workspace, the areas, the member. Open notifications with Enter: focus is on its heading, Tab cycles inside, Escape returns to the bell.
3. Narrow the window to about 1100 px: the rail; hover or Tab through the icons to see their names.
4. At 390 px (DevTools device mode): the top bar names the area; the bottom bar shows Inbox, Overview, Customers, Knowledge, More; "More" opens the other areas; open a conversation from the inbox and press Details to see its facts and the take-over button.
5. Sign in as the analyst: the side bar shows Overview, Analytics, Reports, Workspace settings, Plan & usage (no AI section); on a phone the bar is Overview, Analytics, Reports, More.
6. In Settings → Workspace, type a new name without saving, then pick the other workspace from the switcher: the browser asks before leaving.

## 7. Backend asks

None. Everything shown comes from existing contracts: the workspaces list (`GET /api/me/tenants`), permissions, the display name (`displayName` on `CurrentUserResponse`, 39.1), the needs-a-person count already read by `InboxSignals`, and notifications.

## 8. New Arabic for my review

| Message id | English | Arabic | Where |
| --- | --- | --- | --- |
| `nav.group.conversations` | Conversations | المحادثات | side bar and "More" sheet: section label |
| `nav.inbox` | Inbox | صندوق المحادثات | side bar, bottom bar, top bar title, command palette (replaces `nav.conversations` «المحادثات») |
| `nav.more` | More | المزيد | bottom bar |
| `nav.moreTitle` | More areas | أقسام أخرى | "More" sheet heading |
| `account.soundsOn` | Sounds on | الأصوات مفعّلة | member block, second line |
| `account.soundsOff` | Sounds off | الأصوات معطّلة | member block, second line |
| `workspaceSwitcher.heading` | Your workspaces | مساحات العمل لديك | workspace switcher menu label |
| `workspaceSwitcher.chooser` | Choose or create a workspace | اختيار مساحة عمل أو إنشاء واحدة | workspace switcher, last item |

Reused in new places (unchanged): `inbox.view.needsPerson` «بحاجة إلى الفريق» (the count's words for a screen reader: «صندوق المحادثات، بحاجة إلى الفريق: 148»), `shell.workspacePrefix` «مساحة العمل: » and `shell.switchWorkspace` «تبديل مساحة العمل» (the switcher's name), `palette.close` «إغلاق» (the sheet's close button), `account.menuLabel` «قائمة الحساب» (the member block's hidden suffix).

Removed (no longer used): `nav.group.work` «العمل اليومي», `nav.group.channels` «القنوات» (as a section), `nav.conversations`, `nav.closeNavigation`, `nav.pinSidebar`, `nav.unpinSidebar`, `shell.navigationButton`.

## 9. Risks and open issues

1. **The count reader is now always on** where the side bar shows the inbox (decision 7): more coalesced reads on a busy workspace than before, still at most one per 5 s per browser and workspace. If that load matters, the side bar could show the count only while sounds or the inbox are on.
2. **The workspace switch shows the chooser for a moment** (it is the chooser's own path) while the new workspace's permissions are read.
3. **A fourth blurred layer is possible on a phone** for a few seconds: a toast arriving while a menu or the "More" sheet is open (top bar, bottom bar, sheet, toast). The budget counter reads 3 in every captured state.
4. **The rail's tooltip is measured when it opens**; scrolling the rail's list with a tooltip open leaves it in place until the pointer or focus moves.
5. **39.4a/b** redraw the inbox and the thread; this step only framed the phone header.

## 10. Proposed checkpoint commit message

```text
feat: float the navigation in a glass side bar with four sections, the inbox's count and the member, collapse it to a named rail on narrow desktops, give phones a glass top bar and a bottom bar with "More", turn the workspace into a switcher, trap focus in the notification panel, place toasts per width, and shrink a phone's conversation header to one line (Phase 39.3)

The side bar (216 px) holds the mark, search and notifications, the
workspace switcher, the areas under Conversations, AI, Insights and
Workspace (the owner's assistant's grouping), and the member's name and
sounds state, which opens the account menu. Below 1280 px it is a rail
whose names show in glass tooltips; below 992 px a glass top bar and a
bottom bar of four areas chosen by permission plus a "More" sheet replace
it. The pin, the peeking rail, the drawer and the desktop top bar are gone.
The inbox's needs-a-person count reuses the existing reader. Menus size to
the room they open into; the notification panel and the sheet keep Tab
inside. New tokens for the shell; MASTER.md records the decisions and 72
measured colour pairs.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```
