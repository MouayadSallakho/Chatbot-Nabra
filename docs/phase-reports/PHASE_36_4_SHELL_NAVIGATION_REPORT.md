# Phase 36.4: Shell and navigation (report)

- Date: 2026-10-18
- Scope: the frame around every admin page. That covers the sidebar, top bar, account menu, notification bell, page header, route transitions and a new keyboard command palette. Frontend only.
- Evidence folder (outside the repository): `C:\Users\mouay\Desktop\omniai-redesign-evidence\p36-04\`. Open `index.html` there for before/after side by side.

## 1. Summary

1. **Command palette.** Press Ctrl+K (⌘K on a Mac) anywhere to jump to any page you may open, or to create an agent, upload knowledge, invite a member, open notifications, switch theme or language. It says plainly that it finds pages and actions, not records.
2. **Workspace in the top bar.** The workspace now has a mark and a one-click way to switch (when you belong to another), next to a quiet "Search or jump to…  Ctrl K" field.
3. **Focus after navigation.** After every page change, keyboard and screen-reader users land on the new page's heading, including from the phone drawer and the palette. The list pager no longer throws focus away while the next page loads.
4. **Notification panel.** It shows a real loading shape, an empty state that says what will appear, and an error with "Try again". All its links are now at least 24 px targets (six were too small).
5. **Calmer sidebar, crossfade only between pages.** Sidebar labels are sentence case and the current page has a clean ink marker. Moving between pages of a list, or browsing with reduced motion, no longer fades.

## 2. Starting point

- Branch `master`, HEAD `af3ea0b` ("feat: add the Arabic and RTL foundation", the Phase 36.3b checkpoint), worktree clean (`git status --short` empty). This matched "Phase 36.3b accepted".
- Local stack already running: `omniai-p36-api`, `omniai_pg25` and `omniai_redis18` were up, readiness 200, and the provider canary passed before any check. A dev server was already serving this worktree on :4200; it was kept and is still running.
- Startup JavaScript at the start (production build, loader + application chunk with their static imports): **31 files, 381.6 kB raw, 124.9 kB gzip, 110.7 kB brotli** (`startup-js-baseline.json`).

## 3. Changes by area

`git status --short` at the end:

```
 M docs/architecture/admin-design-system.md
 M docs/design/MASTER.md
 M frontend/src/app/app.config.ts
 M frontend/src/app/core/media/media.ts
 M frontend/src/app/core/notifications/notification-item.ts
 M frontend/src/app/layout/app-shell/app-shell.html
 M frontend/src/app/layout/app-shell/app-shell.spec.ts
 M frontend/src/app/layout/app-shell/app-shell.ts
 M frontend/src/app/layout/navigation.ts
 M frontend/src/app/layout/notification-bell/notification-bell.html
 M frontend/src/app/layout/notification-bell/notification-bell.scss
 M frontend/src/app/layout/notification-bell/notification-bell.spec.ts
 M frontend/src/app/layout/notification-bell/notification-bell.ts
 M frontend/src/app/layout/profile-menu/profile-menu.html
 M frontend/src/app/layout/profile-menu/profile-menu.scss
 M frontend/src/app/layout/sidebar/sidebar.html
 M frontend/src/app/layout/sidebar/sidebar.scss
 M frontend/src/app/layout/sidebar/sidebar.ts
 M frontend/src/app/layout/topbar/topbar.html
 M frontend/src/app/layout/topbar/topbar.scss
 M frontend/src/app/layout/topbar/topbar.ts
 M frontend/src/app/shared/page-header/page-header.scss
 M frontend/src/app/shared/pagination/pagination.ts
 M frontend/src/locale/messages.ar.json
 M frontend/src/locale/messages.json
 M frontend/src/styles/_base.scss
 M frontend/src/styles/_primitives.scss
?? docs/phase-reports/PHASE_36_4_SHELL_NAVIGATION_REPORT.md
?? frontend/src/app/core/navigation/route-focus.ts
?? frontend/src/app/core/navigation/route-transition.spec.ts
?? frontend/src/app/core/navigation/route-transition.ts
?? frontend/src/app/layout/command-palette/   (command-palette.ts/.html/.scss/.spec.ts, commands.ts/.spec.ts, shortcut.ts/.spec.ts)
?? frontend/src/app/shared/pagination/pagination.spec.ts
```

Nothing changed in `backend/`, `deploy/`, `package.json` or the lockfile. No package was added. No budget was changed.

The before/after screenshots below are in the evidence folder. `before/` holds 28 shell states captured on the untouched HEAD; `after/` holds the same 28 plus 20 new ones (palette, phone palette, phone bell). Names follow `<lang>-<theme>-<width>-<state>.png`.

| Area | What changed, and why | Before → after (examples) |
|---|---|---|
| **1. Sidebar** | **Brand lockup:** mark + "OmniAI" wordmark in the narrowed heading face; the rail shows only the mark. **Group labels:** sentence case instead of uppercase, in line with Two Voices and because Arabic has no case. **Current item:** neutral fill + ink text + a short rounded 2 px ink bar (new `.edge-marker` primitive, logical inset, mirrors with no transform). It replaces an inset shadow that followed the rounded corner and read as a bracket. Items are now 36 px tall. **Rail behaviour unchanged:** peek after 150 ms, close after 260 ms, open on keyboard focus, Escape to content, pin. The visibility filter moved into `visibleNavItems()` so the sidebar and the palette share one rule. **RTL:** the clip and the marker mirror. | `before/en-light-1440-peek.png` → `after/en-light-1440-peek.png`; `before/ar-light-1440-peek.png` → `after/ar-light-1440-peek.png` |
| **2. Top bar** | **Workspace identity** placed here (decision in §4): the first letter on a neutral tile plus the name, in an isolate that truncates at its own end in either language. When the account has another workspace, the whole identity is a link to the chooser with a switch glyph. The tile is hidden below 576 px so the name gets the room. **Palette trigger:** a quiet field-shaped button ("Search or jump to…" + `kbd` "Ctrl K"/"⌘K") with `aria-haspopup="dialog"`, `aria-expanded` and `aria-keyshortcuts`; below 768 px it shows only its icon. Bell and account menu unchanged in place. | `before/en-light-1440-rail.png` → `after/en-light-1440-rail.png`; `before/en-light-390-topbar.png` → `after/en-light-390-topbar.png`; `after/ar-light-390-topbar.png` |
| **3. Command palette** (new) | `layout/command-palette/`. A native modal `<dialog>` (page behind inert) holding an APG editable combobox (`aria-activedescendant`, `aria-controls`, `aria-autocomplete="list"`, `aria-expanded`) and a grouped listbox: Go to / Actions / Preferences. **Destinations:** `NAV_ITEMS` filtered by permission, plus Notifications and Notification preferences. **Quick actions:** each gated on the permission its page uses (`ai-agents.create`, `knowledge.manage`, `members.invite`), plus open the notification panel, switch workspace (only when there is another), theme ×3, and both languages in their own names (`lang`/`dir`). Keys follow `Menu`: arrows wrap, Enter chooses, Escape closes and returns focus. Matching folds case, accents, tashkeel, tatweel and the interchangeable Arabic letters. Latin labels emphasise the typed part; Arabic labels do not, because a weight change inside a word broke letter joining (seen in Chrome, fixed). It never calls the server, and the line under the field says it does not search records. **Shortcut** matched on the physical K key (works on an Arabic layout); Ctrl+K on a Mac stays a text-editing key. **Loading:** `@defer (when requested; prefetch on idle)`, so it is not part of startup. | new: `after/en-light-1440-palette.png`, `after/en-dark-1440-palette-query.png`, `after/ar-light-1440-palette-query.png`, `after/en-light-1440-palette-empty.png`, `after/en-light-390-palette.png` |
| **4. Account menu** | Opens on the same face the trigger shows: initials beside the address and the workspace. Each value truncates inside its own isolate (an address loses its end, never its start). Theme, Language, Notification preferences, Switch workspace and Sign out are unchanged, as is the `Menu` behaviour. | `before/ar-light-1440-account-menu.png` → `after/ar-light-1440-account-menu.png` |
| **5. Notification bell** | **Header:** title + unread sentence + "Mark all as read" with a check. **Loading:** three skeleton rows in the item's geometry, `aria-busy`, with the words for screen readers. **Empty:** icon tile + "No notifications yet" + what will appear. **Error:** "could not be loaded" + reason + "Try again". **Targets:** action links and footer links ≥ 28 px; this fixes the six sub-24 px targets the "before" scan found. **Motion:** the panel enters like a menu. New `openPanel()` lets the palette open it with focus on its heading. SignalR, unread logic and "opening does not mark read" are unchanged. | `before/en-light-1440-bell.png` → `after/en-light-1440-bell.png`; `after/ar-light-390-bell.png`; `keyboard/en-08-bell-from-palette.png` |
| **6. Page header** | Two Voices rhythm: 4–8 px inside the block, 24 px after it. Back link becomes a 28 px target; the title balances; description capped at 72 ch; the actions row is one title line tall so buttons centre on the title. All 73 in-shell page headers already use `PageHeader`, so every page changes at once. | `before/en-light-1440-pinned-detail.png` → `after/en-light-1440-pinned-detail.png`; any `matrix/*.png` |
| **7. Route transitions** | Kept `withViewTransitions({ skipInitialTransition: true })` and the 150 ms content-only crossfade. Added `onViewTransitionCreated` (`core/navigation/route-transition.ts`): a query-only or fragment-only change, or reduced motion, makes the transition **instant** via a class on `<html>`. I first used `skipTransition()`, but Chrome showed it logs `AbortError: Transition was skipped` on every list page in dev mode, so I replaced it. Added `RouteFocus` (`core/navigation/route-focus.ts`): after each change of page, focus goes to the new `<h1>` (waits up to 3 s for pages that render it after loading, then the content region). It skips the first load and query-only changes, and never overrides focus the page already placed. | `keyboard/en-03-page-heading.png`, `keyboard/en-06-palette-result.png`, `keyboard/keyboard.json` (transitions recorded per step) |
| **8. Mobile (≤ 991 px)** | **Drawer:** rounded page-facing edge (logical corners), 44 px close button, 44 px items under coarse pointers. Navigating from it now puts focus on the new page's heading instead of the trigger (an improvement to D13; Escape, close and scrim still return to the trigger). **Top bar:** compact (menu, workspace, palette icon, bell, avatar), all 44 px on touch. Opening the palette closes the drawer first. | `before/ar-dark-390-drawer.png` → `after/ar-dark-390-drawer.png`; `keyboard/en-09-drawer.png`, `keyboard/en-10-drawer-result.png` |
| **Pager focus** (found while testing 7) | `shared/pagination`: while a page loads, the buttons were `disabled`, which dropped focus to `<body>` (verified in Chrome). Now, while busy, they are `aria-disabled` and ignore presses. At the end of the list, where a button really disables, focus moves to the other button. Boundary `disabled` is kept, as existing specs expect. | `keyboard/keyboard.json`, step "pager Next" (focus stays on "Next") |
| **i18n** | 44 new messages and 1 retired (`notifications.empty`, split into a title and a text), with natural Arabic for all. Arabic plurals use all six forms (نتيجة واحدة، نتيجتان، 3 نتائج …). `kbd` key names stay Latin LTR islands. | `src/locale/messages*.json` |
| **Global layers** | `_primitives.scss`: `.edge-marker` and `.kbd`. `_base.scss`: `.route-transition-instant` and the no-ring rule for a programmatically focused page heading. Moving the marker here kept `sidebar.scss` under its 4 kB warning budget. | — |
| **Docs** | `admin-design-system.md`: D2, D12–D15, D18, D19, D21 and D22 updated; new D23 "Command palette". `MASTER.md`: motion and rule 4 updated, plus six decision-log rows. | — |

## 4. Decisions

- **Workspace identity: top bar, not sidebar.** The top bar exists at every width and the rail does not. The rail stays a stable 64 px column of destinations, and the switch sits next to the name it changes.
  - Rejected: a workspace block at the sidebar's top or bottom. It would be invisible on the collapsed rail and duplicated in the drawer.
- **Workspace mark = first letter of the real name** on a neutral tile, the same family as the account initials.
  - Rejected: a coloured tile (voices are not decoration) and any invented logo.
- **Trigger copy kept as asked ("Search or jump to…"), with honesty carried by the palette itself.** The line under the field says it does not search conversations, customers or other records, and "no results" names only pages and actions.
  - Rejected: renaming the trigger. The brief fixed the copy.
- **Palette built by hand on `Menu` conventions.** `@angular/aria` and the CDK are not installed, and no package was added.
- **Lazy palette** (`@defer` + prefetch on idle) to keep startup flat; first use is usually already prefetched.
- **Instant, not skipped, transitions** for query-only changes and reduced motion. This avoids a dev-mode console error per list page that `skipTransition()` causes in Angular 22.
- **Focus to the heading after navigation, without a visible ring.** The heading is a reading position, consistent with the skip link's target.
  - Rejected: a ring on the `h1`. It looked like a control.
- **Drawer → page focus change.** D13 previously returned focus to the trigger after navigating; it now goes to the new heading. This is an intentional improvement, recorded in D13 and MASTER.
- **Palette Home/End keys left to the text field** (APG combobox), unlike `Menu`, where they move between items.
- **Language commands list both languages**, the current one hinted "In use", rather than only "the other one", so nobody wonders where theirs went.
- **Deferred:**
  - recent/frequent commands (needs server-held history, D22);
  - record search (needs a search endpoint);
  - a "Pin sidebar" command;
  - Home/End in the palette list.

## 5. Verification

| Check | Result |
|---|---|
| `npm run format:check` | **PASS**: "All matched files use Prettier code style!" (`final-format.log`) |
| `npm run test:ci` | **PASS**: 74 files, 890 tests, 0 failures, 0 stray `ERROR` lines (`final-test.log`). At the start: 69 files, 836 tests. New specs: `command-palette.spec.ts` (14), `commands.spec.ts` (12), `shortcut.spec.ts` (5), `route-transition.spec.ts` (5), `pagination.spec.ts` (3), plus 11 new in `app-shell.spec.ts` (palette, route focus, drawer focus, workspace mark) and 4 in `notification-bell.spec.ts` (loading, empty, retry, `openPanel`). |
| `npm run build` | **PASS** with 1 warning, pre-existing and unchanged from the baseline: `embed-chat.scss` 72 bytes over its 4 kB *warning* budget (not touched). Mid-phase, `sidebar.scss` exceeded the budget by 102 bytes; fixed by moving the marker to `_primitives`. No budget raised. Initial total 285.1 kB raw / 67.6 kB transfer. |
| `npm run i18n:check` | **PASS**: 400 messages, 0 errors, 0 warnings (`final-i18n.log`) |
| Startup JavaScript (rule 13) | **PASS** (+5.5%, under 10%): start 31 files / 381.6 kB raw / **124.9 kB gzip** / 110.7 kB brotli → end 32 files / 404.3 kB raw / **131.7 kB gzip** / 116.6 kB brotli. The growth is the `@defer` runtime in `main` (+2.9 kB gzip) plus the new top bar, bell states and route focus in the application chunk. The palette is a separate lazy chunk (17.4 kB raw / 5.8 kB gzip), verified absent from the startup set. (`startup-js-baseline.json`, `startup-js-final.json`, `scratch/startup-js.mjs`) |
| Chrome matrix: 33 routes × 1440/1024/768/390 × light/dark × en/ar | **PASS**: 528 captures, 0 flagged. Max page overflow 0 px; exactly one `h1` on every capture; 0 unnamed controls; 0 shell targets under 24 px; 0 console errors; `lang`/`dir` correct on all 528. Sign-in is excluded (it redirects a signed-in session). (`matrix/`, `matrix/matrix.json`, `matrix.log`) |
| Shell states (rail, peek, pinned, account menu, bell, palette ×3, phone top bar/drawer/palette/bell) × light/dark × en/ar | **PASS**: 48 captures, all clean (`after/states.json`). Before: 28 captures, the bell had 6 sub-24 px targets (`before/states.json`). |
| Contrast (`design-system-tokens` `contrast_check.py`, 32 new pairs × light, dark, light + `prefers-contrast: more`, dark + more) | **PASS**: 31/31 required pairs pass in all four modes; 1 informational (the decorative edge of the `aria-hidden` workspace-mark tile). Examples (light / dark): palette trigger words 5.50 / 5.34; trigger boundary 3.79 / 3.57; `kbd` 7.27 / 8.48; edge marker on selected fill 14.48 / 13.42; palette hint on selected fill 4.84 / 4.86; switch glyph 5.94 / 5.64. (`contrast/*.md`, `contrast/pairs.json`) |
| Keyboard: skip link → rail → page (en + ar) | **PASS**: Tab 1 skip link (visible), Enter focuses content; Tab reaches the rail, which opens on focus; Enter on Knowledge → focus on the "Knowledge" `h1` and the rail closes; Escape in the peeking rail closes it and focuses content. (`keyboard/keyboard.json`, `keyboard/*-01…03*.png`) |
| Keyboard: Ctrl K → command → page (en + ar) | **PASS**: Ctrl+K opens the modal with focus in the combobox; typing narrows ("analyt" / "التحليلات"); Enter → `/analytics` with focus on its `h1`; ArrowDown moves `aria-activedescendant`; Escape returns focus to where it was; "Theme: Dark" applies and returns focus; "Open the notification panel" opens it with focus on its heading, and Escape returns to the bell. (`keyboard/*-04…08*.png`) |
| Keyboard: drawer at 390 (en + ar) | **PASS**: trigger → drawer with focus on close and content inert; Escape → trigger; link → page heading, drawer inert again |
| Keyboard: pager | **PASS**: Next on `/customers` → `?page=2`, focus stays on "Next" (it dropped to `<body>` before the fix) |
| Reduced motion | **PASS**: palette animation 1e-05 s and rail transition 1e-05 s; route change recorded as instant. With full motion the same route change crossfades. |
| RTL mirroring | **PASS**: rail clip, edge marker, drawer side and rounded edge, top bar order, palette layout, `kbd` and address isolates all mirror correctly in all Arabic captures; the workspace name truncates at its own end (fixed during the phase). |
| Provider canary | **PASS** at start and end: `OPENAI_API_KEY`, `STRIPE_SECRET_KEY`, `STRIPE_WEBHOOK_SECRET`, `OMNIAI_WHATSAPP_APP_SECRET`, `OMNIAI_WHATSAPP_VERIFY_TOKEN`, `OMNIAI_LIVE_OPENAI/META/EMAIL` and `Email__Smtp__*` all absent inside `omniai-p36-api` (`canary.log`) |
| Heuristic scans | Motion audit and ERP lint: 3 findings, all confirmed false positives in context (global reduced-motion handling outside the scanned folder; a pre-existing 0 s delayed snap; the APG `role="option"` and the pre-existing pointer-only scrim). The palette exception is annotated at the site. (`motion-audit.md`, `erp-lint.md`) |
| Chrome DevTools **MCP** for interactive checks | **NOT RUN as MCP.** Both DevTools MCP servers refused to start: their shared profile is held by a Chrome owned by another session's still-running MCP server (PID 33272, started 11:28), which I did not kill. All interactive and bulk checks ran through a dependency-free CDP script driving my own isolated headless Chrome (real `Input.dispatchKeyEvent`/mouse events) — same browser engine, different driver. |
| Automated axe/AccessLint audit | **NOT RUN**: not part of this phase's toolchain; the structural checks above (names, `h1`, targets, contrast, focus) were run instead. |
| Screen-reader smoke test | **NOT RUN**: no screen reader available in this environment; accessible names and roles were checked from the DOM. |
| Real touch device | **NOT RUN**: touch and coarse pointer emulated in Chrome only. |

## 6. See it live

- Dev server (running): **http://localhost:4200/sign-in**, then for example http://localhost:4200/overview. Press **Ctrl K** anywhere, or click "Search or jump to…".
- The owner account belongs to two workspaces, so the top bar's workspace link and "Switch workspace" both appear. The support and analyst accounts show the permission-filtered palette.
- Synthetic demo accounts: `C:\Users\mouay\Desktop\omniai-redesign-evidence\p36-01\demo-accounts.md`.
- Before/after gallery: `C:\Users\mouay\Desktop\omniai-redesign-evidence\p36-04\index.html`.
- Arabic: account menu → Language → العربية.

## 7. Backend asks

1. **A search endpoint** across conversations, customers and knowledge documents (permission-aware, paged). It would let the palette offer record results; until then it searches commands only and says so (D22, D23).
2. **Server-held UI preferences** (theme, language, sidebar pin, palette recents) on the member's profile, so they follow the member across devices (already D22).
3. **A display name on the account** (`/me`). The account menu can show only the address and its initials today.
4. Unchanged from 36.1: the backend's clean restore still needs `-p:NuGetAudit=false` (transitive `System.Security.Cryptography.Pkcs` 10.0.0).

## 8. Risks and open issues

- **Arabic page bodies are still English**, as are uppercase "CUSTOMER/COLLEAGUE" speaker labels in the conversation list. These are page content from earlier phases, scheduled for the page phases. The shell, palette and bell are fully translated.
- **Pagination changed beyond the shell.** The fix is small and spec-covered, but it touches every paged list; review it with that in mind.
- **Route focus waits 3 s for a heading.** A page slower than that gets focus on the content region instead. A page that renders a different `h1` while loading (none today) would get the first one.
- **`prefetch on idle`** downloads the 5.8 kB gzip palette chunk for every session once the browser is idle. That's network cost, not startup cost.
- **The instant transition still lets Chrome take its snapshot** under reduced motion. That's cheap, but not literally "no transition".
- **jsdom has no modal dialogs.** Palette specs stub `showModal`/`close` at the prototype; real modality, inertness and focus return were checked only in Chrome.
- **Chrome DevTools MCP unavailable** (see §5). The earlier session's MCP-owned Chrome (PID 21028) is still running and holds the shared profile.
- **The design-system doc's D20 ("English-only")** predates 36.3 and was not part of this phase's sections; it should be refreshed when RTL docs are next touched.
- The **pre-existing `embed-chat.scss` budget warning** remains.

## 9. Proposed checkpoint commit message (not committed)

```
feat: redesign the shell and add a command palette

Phase 36.4. The frame around every admin page, in Two Voices:

- Sidebar: brand lockup, sentence-case groups, and a rounded 2px ink
  marker for the current page (.edge-marker); rail peek, pin and focus
  behaviour unchanged.
- Top bar: the workspace's mark and name, a link to the chooser when the
  account has another workspace, and a quiet "Search or jump to… Ctrl K"
  trigger.
- Command palette (Ctrl/Cmd+K): pages the member may open and quick
  actions behind their own permissions, theme and language. Commands, not
  data, and it says so. APG combobox + grouped listbox in a modal dialog,
  Arabic-aware matching, loaded on first use.
- Focus: the new page's heading takes focus after every page change,
  including from the drawer and the palette; the pager keeps focus while
  the next page loads.
- Notification panel: loading shape, empty and error states with retry,
  24px+ targets, and an entry point for the palette.
- Page header rhythm; crossfade made instant for query-only changes and
  reduced motion.

Docs: admin-design-system D2, D12-D15, D18, D19, D21-D23; MASTER.md log.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```
