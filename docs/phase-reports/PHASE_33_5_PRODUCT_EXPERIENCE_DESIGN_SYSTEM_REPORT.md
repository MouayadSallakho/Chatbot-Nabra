# Phase 33.5 — Product experience, design system and enterprise UX/UI hardening

Baseline: `eb2359e` (Phase 33.1, accepted and checkpointed). Working tree left
unstaged and uncommitted for review. Phases 26B–29 remain deferred pending the
authorised Meta business account. No VPS was accessed and nothing was deployed.

**Canonical design-system document:**
[`docs/architecture/admin-design-system.md`](../architecture/admin-design-system.md)
— the single source of truth, linked from the architecture index. No second
design document was created. Sections D2, D5, D9, D11–D19 revised; D21 product
patterns (including focus continuity and field errors) and D22 unsupported by the
backend contract added.

This report covers the implementation pass and the final acceptance pass that
followed it (section 10). Together they complete the phase specification.

## 1. Outcome

Frontend only. No backend file, migration, package, endpoint, permission, status
or business rule changed. The Admin application now shares one shell, one token
scale and one set of list, pagination, menu, dialog, notice, toast and error
patterns, verified in the real application in Chrome throughout the work.

## 2. Local stack

|                  |                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                               |
| ---------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Database         | New `omniai_p335` on the local PostgreSQL 17 + pgvector container, created empty and initialised only by `20260925225105_Initial` (`__EFMigrationsHistory` holds that one row; 49 tables). No old `omniai_*` database was read, migrated or dropped                                                                                                                                                                                                                                                                                                                                                                                                                                           |
| Services         | Redis, the .NET API (Release, Development environment), the Angular dev server, local Stripe and Meta stubs, a local mail sink — all on loopback                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                              |
| Data             | Synthetic only, created through the real API: one workspace (plus a second with an Arabic name), 3 members (owner, support lead, a permission-limited analyst), 3 custom roles (one with a 60-character name), 2 pending/revoked invitations (one with a long address), 4 AI agents (Draft / Active / Disabled; one 90-character name, one `<script>` name), 12 Knowledge documents (long, Arabic, Spanish, `<img onerror>` titles; Needs index / Disabled), 42 website-chat conversations and customers (José García, François Müller, أحمد محمد, شركة النور, a 110-character name, an HTML-looking name), a WhatsApp connection without credential, 5 reports, 2 schedules, 8 notifications |
| External traffic | None. `OMNIAI_LIVE_OPENAI`, `OMNIAI_LIVE_META`, `OMNIAI_LIVE_EMAIL` were never set. AI calls fail locally by design (no provider), which the UI shows honestly                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                |

To seed 40+ conversations the public-chat protection limits were raised for the
local API only, through the existing `PublicChatProtection__*__Limit`
configuration. The first attempt was refused with real 429s, which was itself a
useful check.

## 3. Audit findings (before)

Found by reading every route family and by Chrome at the start:

| Finding                                                                                                                                                                                             | Scope                                                                             |
| --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------- |
| The toast host was an auto-placed grid child, so the shell grid had an implicit second row and the sidebar/main stopped 52px short of the viewport                                                  | every page                                                                        |
| Bootstrap `.form-control` / `.form-select` / `.form-label` on ~30 controls, with only Bootstrap's grid loaded — browser-default controls                                                            | WhatsApp, website chat, roles, team, reports, schedules, preferences, hosted chat |
| 11 class names and 8 CSS variables used but defined nowhere (`.card-title` ×21, `.text-label` ×28, `.pager`, `.form-error`, `.alert-success`, `.badge-accent`, `--text-caption`, `--focus-ring`, …) | many                                                                              |
| Five pagination markups; Conversations loaded page 1 only while showing the total                                                                                                                   | lists                                                                             |
| No URL state anywhere; list → detail → back always returned to page 1                                                                                                                               | lists                                                                             |
| Three confirmation mechanisms (dialog, `window.confirm`, inline); disabling a live website chat, a WhatsApp number or a Knowledge document asked nothing                                            | destructive actions                                                               |
| No route-leave protection for unsaved edits                                                                                                                                                         | editors                                                                           |
| Five different stale-version banners                                                                                                                                                                | editors                                                                           |
| Three nav destinations shared a bar-chart glyph, two a sliders glyph, two a people glyph                                                                                                            | sidebar                                                                           |
| Overview was a scaffolding page: "Available" pills, "Ready to use" filler cards, a product-areas table                                                                                              | overview                                                                          |
| A visually hidden `<caption>` anchored to `<body>` made the document scroll beside `main` — a second scrollbar                                                                                      | pages with hidden captions                                                        |
| The off-screen skip link and closed drawer bled their shadows onto the page edge                                                                                                                    | every page                                                                        |
| `prefers-reduced-motion` lost to component styles (a component selector out-ranks `*`): the sidebar and menus still animated                                                                        | global                                                                            |
| Technical copy: "Could not reach the OmniAI backend", "Cannot reach the backend", a 503 on a list said "This needs an AI provider"                                                                  | errors                                                                            |

## 4. What changed

**Tokens (`_tokens.scss`).** Radius 4 / 6 / 8 / 12 (was up to 16); control
heights 32 / 36 / 40, raised to touch targets under `pointer: coarse`; icon
sizes 16 / 18 / 20; one z-index scale; motion 150 / 200 / 280 ms; caption and
table type steps; a 16px gutter on phones; motion tokens collapse under reduced
motion.

**Primitives (`_primitives.scss`).** Buttons (sizes, danger-ghost, link, a
width-stable loading state); cards (header context wraps under the title, footer
actions); badges (4px chips, accent and plain tones, count badge); forms (the
Bootstrap aliases, switch, segmented control, native checks, capped single-line
fields); tables (caption headers, numeric alignment, actions column, contained
hidden captions); the shared **data list**; toolbar and pager; menus; the
**stale notice**; alert-success; section-title rhythm.

**Shell.** Rail sidebar with hover intent (150 ms open, 260 ms close, re-entry
cancels, sweeps and touch never open it), keyboard focus opens it, Escape
closes; drawn with `clip-path` so peeking reflows nothing; Pin / Unpin persisted
per browser; five labelled sections; a distinct glyph per destination. Top bar:
workspace context, bell, account menu (identity, theme, preferences, switch,
sign out) on a shared keyboard `Menu`. Mobile drawer unchanged in behaviour,
44px touch targets, no shadow bleed.

**Shared components and services.** `Menu`, `Pagination` + `?page=` helpers,
`NavigationHistory` (back to the exact list page), `PageHeader` back link and
status slot, `ConfirmDialog.ask()` with content rendered only while open and
focus on the safe answer, `unsavedChangesGuard` + `protectUnsavedChanges`,
toasts that pause under the pointer or focus and announce errors assertively.

**Screens.** Overview rebuilt on server data only (needs attention / right now /
last 7 days / shortcuts, each permission-gated). Agents, Knowledge,
Conversations, Customers, Roles and Team on the data list; every paged list on
one pager with URL state; Conversations paged (was page 1 only), with a live
indicator, a width-stable refresh and rows kept on a failed refresh; confirmations
on revoke / disable chat / take WhatsApp out of service / take Knowledge out of
use; unsaved-change protection on four editors; back links on 15 detail and
create pages; unified stale notices; unpadded cards and legends fixed on Roles,
Billing, Plan & usage and Customers; handling buttons sized to their labels;
area-specific empty-state icons; minute-precision dates everywhere.

**Copy.** "AI agent" and "Website chat" used consistently; load failures read
"Could not load …"; the offline, stale, precondition, invalid-response and
antiforgery messages rewritten in product language; a 503 names the AI provider
only on AI operations; session expiry arrives at sign-in with a sentence saying
why (`?reason=session-ended`); "No limit on this plan" instead of "does not
limit ai agents".

Files: see `git status --short` (section 12). The `ThemeToggle` component was
removed; its choice now lives in the account menu.

## 5. Chrome verification

Interactive work used the Chrome DevTools MCP against the real app; systematic
matrices used a dependency-free Node script driving local headless Chrome over
the DevTools protocol. Nothing was verified by template reading alone.

| Check                                                                                                                         | Result                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                              |
| ----------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Route × viewport × theme matrix: 32 route families × 390 / 768 (light, dark), 1366, 1440 (light, dark), 1920 = 256 page loads | 0 page-level horizontal overflow, 0 content overflowing outside a scroll region, exactly one `h1` everywhere, 0 unnamed buttons or links, 0 console errors or warnings; the only non-2xx call is the expected pre-sign-in `GET /api/auth/me 401`. Every route rendered its own page (titles checked)                                                                                                                                                                                                                |
| Sidebar                                                                                                                       | Timing measured: closed at 80 ms, open by 200 ms, still open 150 ms after leave, re-entry cancels the close, closed by 320 ms, pointer jitter stable, sweep never opens, touch never peeks. Main content left edge and width unchanged while peeking. Keyboard Tab opens with a visible ring; Escape collapses and moves focus to `main`. Pin → grid 240px, persisted across reload; Unpin → 64px                                                                                                                   |
| Mobile drawer (390)                                                                                                           | Trigger → focus on Close, content inert, no overflow; Escape closes and focus returns to the trigger; navigating closes it; items 44px                                                                                                                                                                                                                                                                                                                                                                              |
| Account menu                                                                                                                  | Opens on the first item; End / ArrowDown wrap; Escape closes with focus on the trigger; outside press closes; choosing Light applies it and returns focus                                                                                                                                                                                                                                                                                                                                                           |
| Confirmation dialog                                                                                                           | Keyboard Enter opens on "Keep invitation"; Tab stays inside; Escape closes with focus back on Revoke; a 50-character address wraps (was overflowing)                                                                                                                                                                                                                                                                                                                                                                |
| List context                                                                                                                  | Customers page 2 → a customer → back link → `/customers?page=2`, "21–40 of 42 customers"                                                                                                                                                                                                                                                                                                                                                                                                                            |
| Unsaved changes                                                                                                               | Edited agent name, navigate away: declined stays, accepted leaves                                                                                                                                                                                                                                                                                                                                                                                                                                                   |
| Inbox                                                                                                                         | Take over → handling "A person", composer appears; reply sent; Return to AI restores AI handling                                                                                                                                                                                                                                                                                                                                                                                                                    |
| Permission-limited user (analyst)                                                                                             | Sidebar shows only permitted areas; Overview hides links it cannot follow (one found and fixed); `/team` typed directly explains the refusal without a code                                                                                                                                                                                                                                                                                                                                                         |
| Error states                                                                                                                  | 503 real (preview, no provider): honest AI message. 502 / 429 / 403 / 428 / 409 intercepted: product copy, the server's own sentence for 409, stale notice for 428. 412 real (renamed underneath an open editor): stale notice, edits kept. Offline real: human message (the list-clearing it exposed was fixed). 503 on a list read: now "temporarily unavailable" (was the AI message). 401 real (cookies cleared): sign-in with the new explanation. No raw status codes, exception names or ETag language shown |
| 200% zoom (720 CSS px)                                                                                                        | Overview, Conversations, Customers, Reports, Invite member, AI agents: drawer layout, no overflow, no clipped control                                                                                                                                                                                                                                                                                                                                                                                               |
| Reduced motion                                                                                                                | Emulated: sidebar and menu durations ~0 after the token fix (were 280 / 200 ms); rail still opens and closes                                                                                                                                                                                                                                                                                                                                                                                                        |
| Grayscale                                                                                                                     | Knowledge, Overview, Reports: states distinguished by word, edge marker and weight                                                                                                                                                                                                                                                                                                                                                                                                                                  |
| Unicode / long / hostile content                                                                                              | Arabic, accented Latin, 110-character names, `<script>` / `<img onerror>` labels render as text and wrap; no layout break                                                                                                                                                                                                                                                                                                                                                                                           |

## 6. Tests and gates

Final values, after the acceptance pass (section 10).

| Gate                   | Result                                                                                                                                                                                                                                                                                                                                                                                 |
| ---------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `npm ci`               | Clean install from the lockfile: 391 packages added, exit 0. npm printed its informational `allow-scripts` notice for four install scripts (`@parcel/watcher`, `esbuild`, `lmdb`, `msgpackr-extract`); nothing was approved or changed                                                                                                                                                 |
| `npm run test:ci`      | **63 files, 782 tests, all passing** (baseline `eb2359e`: 59 files, 741). New and extended specs: sidebar hover intent and pin, Menu keyboard contract, profile menu, Overview, toast holding, and the acceptance-pass focus and field-error specs for schedules, invitation, team revoke, WhatsApp create and detail, notification preferences, customer merge and the inbox composer |
| `npm run build`        | Succeeds. One pre-existing warning: `embed-chat.scss` 4.07 kB vs its 4.00 kB budget, unchanged since before Phase 33                                                                                                                                                                                                                                                                   |
| Initial bundle         | **439.46 kB raw / 108.22 kB estimated transfer** (baseline `eb2359e`: 408.39 kB / 100.10 kB; implementation pass: 439.56 kB / 108.25 kB). No further growth from the acceptance fixes (removing the Integrations entry offsets them). Feature code stays lazy; under the 500 kB warning budget                                                                                         |
| `npm run format:check` | All files formatted                                                                                                                                                                                                                                                                                                                                                                    |
| `npm audit`            | **0 vulnerabilities** (info 0, low 0, moderate 0, high 0, critical 0) across 519 dependencies; the advisory service answered with full metadata. `npm audit fix` was not run                                                                                                                                                                                                           |
| lint                   | No `lint` script exists in `frontend/package.json`; none was added                                                                                                                                                                                                                                                                                                                     |
| `git diff --check`     | Clean (only Git's LF→CRLF notices)                                                                                                                                                                                                                                                                                                                                                     |
| Backend                | See section 11                                                                                                                                                                                                                                                                                                                                                                         |

## 7. Unsupported by the current backend contract

Deferred rather than faked (architecture doc D22): list search, filters and
sorting (no list endpoint accepts them); column configuration and bulk actions;
KPI trends and period comparison; an Overview count of documents needing
indexing; server-held UI preferences.

## 8. Remaining risks and limitations

- A permission refused mid-session (403) was produced by interception, not by
  revoking a live role.
- Chart styling was checked in both themes on the existing analytics chart; no
  chart was redesigned.
- Route changes return focus to the start of the document, as a full page load
  does. Chrome keeps the sequential-focus starting point, so after the Reports
  sub-navigation the next Tab lands on the page's first control, not the skip
  link. No route-level focus management was added; that would be a shell-wide
  behaviour change.
- A native modal `<dialog>` lets Tab leave to the browser's own toolbar between
  the last and first buttons; page content is never reached. This is the
  platform's behaviour and was kept.
- Two duplicate reads observed in the network check are bounded and predate this
  phase: the Notifications page re-reads once when the bell's unread count first
  arrives, and Conversations opens its own listener on the workspace hub next to
  the shell's. One extra request each, never repeated.
- A merged customer's tombstone reads "Last activity: No activity yet" beneath
  the merged notice. That is the server's value for a record with no remaining
  conversations; the notice above it says where the history went. Customer
  identity semantics were not changed.

## 9. Assumptions

- Admin UI stays English; Unicode data must render, but no RTL switching.
- A native `confirm` is acceptable for the route-leave question, to match the
  browser's own tab-close prompt.
- "Could not load …" was kept as the failure-heading form because 14 screens
  already used it.

## 10. Final acceptance pass

Run on the same fresh stack (section 2): `omniai_p335`, initialised only by
`20260925225105_Initial`, synthetic data only, local stubs and mail sink.
Chrome was driven through the Chrome DevTools MCP against the real application.
Every workflow below was hand-driven, mostly by keyboard; each defect found was
fixed, covered by a spec, and re-driven in Chrome.

### Defects found and fixed

All are focus or field-feedback defects that the matrix could not see. Most
predate Phase 33.5 (the `eb2359e` screens behave the same way).

| Where                         | Defect                                                                                                                                                                                      | Fix                                                                                                                                                                                                                                         |
| ----------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Report schedules              | "New schedule" replaces itself with the form, so focus fell to the body; the same after Save / Cancel                                                                                       | Focus moves to Name on open and on a refused submit, back to "New schedule" or to that row's Edit on close                                                                                                                                  |
| Invite member                 | Empty and malformed addresses got the same sentence; the field was not `aria-invalid` or linked to the error; after Create the form is replaced and focus was lost                          | Separate messages, `aria-invalid` + `aria-describedby` (hint and error); focus to the field on a refused submit, to "Invitation created" on success, to the field on "Invite somebody else"                                                 |
| Team                          | Every pending invitation's button was named just "Revoke"; after a confirmed revoke the row vanished with focus and nothing was announced                                                   | Named "Revoke the invitation for …"; a success toast; focus to "Pending invitations"                                                                                                                                                        |
| WhatsApp (connect and detail) | A non-digit ID only disabled the button, with no explanation; after Connect the button disabled itself and dropped focus, and nothing was announced                                         | Inline errors linked to the field (the detail form also says when a required ID is empty); a success toast and focus to "Connected numbers"                                                                                                 |
| Notification preferences      | Save is disabled while saving and again once clean, so focus fell to the body after every save                                                                                              | Focus returns to the first category switch                                                                                                                                                                                                  |
| Customer merge                | Candidates were not distinguishable: nine "Anonymous visitor" rows read "Website chat · first seen Sep 26, 2026"; opening the picker, cancelling, succeeding and failing all lost focus     | "First seen" to the minute plus a short reference from the record's own identifier (the one in its address); focus to the first candidate, back to the opener on Cancel, to the merged notice on success, to the failure message on refusal |
| Inbox composer                | Take over / Return to AI replace each other, and Send disables itself, so focus was lost after each; a template comment claimed Enter was "handled as a keystroke" when it only adds a line | Focus into the composer after Take over and after a sent reply, to "Take over" after Return to AI; comment corrected (Enter adds a new line and never sends)                                                                                |

### Workflows

| Workflow                        | Result                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   |
| ------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **A. Report schedule**          | Reports → Schedules → New schedule by keyboard. Empty submit: "Give the schedule a name…", `aria-invalid`, `role="alert"`, focus on Name. Created "Synthetic monthly AI handling" (AI and human handling, monthly, CSV); announced; the row shows On with the next run on Oct 1. Edited it to PDF (Space on Save); saved; focus back on that row's Edit. At 390 px (device emulation) the form has no horizontal overflow and 44 px controls. No email: delivery stays in Reports, and the mail sink stayed empty                                                                                                                                                                        |
| **B. Invitation**               | `synthetic.invitee` refused as malformed; `synthetic.invitee@example.test` with one role created (link shown once, token in the fragment, never a query string); a second invitation `second.synthetic@example.invalid` created. Revoke: the dialog opens on "Keep invitation"; Escape cancels with focus back on the named Revoke; Tab cycles only through the dialog's buttons (and the browser toolbar); confirming revokes, shows the toast and focuses the section. Mail sink: 0 messages — invitations are delivered by copied link                                                                                                                                                |
| **C. WhatsApp**                 | Connect form: "12ab" and "+1 555 010 0142" as IDs show the digits-only error; a synthetic number (digit IDs, display "+1 555 010 0142") connected as Draft, "No access token". Detail: the token field is `type="password"`, empty, `autocomplete="off"`, "never shown again"; Put into service stays disabled without a token. **No token was entered** — none was invented. Settings: an invalid ID shows its error; a display-number change saved ("Saved."). The Meta stub logged no request and `OMNIAI_LIVE_META` was never set                                                                                                                                                    |
| **D. Notification preferences** | Channel health switched on by Space, saved by keyboard ("Email preferences saved." announced); reload showed it still on (server state); switched back off and saved, so the synthetic account is as it was. Focus stays in the form. No email sent                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| **E. Customer merge**           | Source "Anonymous visitor" (ref. c7db5d) combined into another (ref. 0bdc11); both are distinguishable in the list and in the confirmation, which says it cannot be undone. Explicit confirmation required. Afterwards the source is a tombstone: notice, 0 conversations, no channel, link to the surviving record, which now lists 2 conversations. The moved conversation's page text was identical before and after (same length and hash) — no transcript change. **Stale:** with the picker open in one tab, the same record was merged elsewhere in a second tab; confirming in the first was refused (412) with "Somebody else changed this customer…", and nothing was combined |

### Keyboard walkthroughs

**Reports tab strip.** It is a `nav` of two links with `aria-current="page"`, not
an ARIA tablist, so arrow keys are correctly not used: each half is its own
address. Tab reaches "Generate and download" (current, visible focus ring), then
"Schedules"; Enter navigates and `aria-current` moves. After the route change the
next Tab reaches "New schedule". The strip wraps at 390 px.

**Inbox composer.** Back link → Refresh → Take over by Tab; Enter takes over
("A person"), and focus lands in the labelled composer ("Your reply"). Typing,
then Enter, adds a newline — nothing is sent. Tab reaches Send reply; Enter sends
exactly one message, clears the composer and returns focus to it. Shift+Tab
walks back through assignment, Return to AI, Refresh and the back link — no trap.
Return to AI by Space restored AI handling, with focus on Take over.

### Integrations placeholder decision

Removed from the primary navigation. `/integrations` rendered only the generic
"Not implemented yet" placeholder — no screen, no backend. The entry was deleted
from `NAV_ITEMS`; `/integrations` now answers "Page not found" inside the shell.
The placeholder mechanism (`implemented: false` → generated route) is kept for
future use but is currently unused. No Integrations feature, route or endpoint
was added. The shell spec now asserts every navigation entry is implemented and
that `/integrations` is unknown.

### Documentation

- `admin-design-system.md` D9: the incorrect "there are no user accounts to hang
  a preference on" is corrected — OmniAI has authenticated users; what is missing
  is a server-side UI/profile preference contract. The same sentence in
  `theme-service.ts` was corrected. No endpoint or schema was added.
- D11 records that the navigation carries no unbuilt area; _Deferred_ records the
  Integrations removal; D21 adds focus continuity and field errors.
- One canonical design document, already linked from `docs/architecture/README.md`.

### Console and network

- **Console** across the workflows and 21 representative routes (every primary
  destination, detail pages, `/integrations`): only Vite's connection messages
  and Angular's development-mode notice. No runtime error, unhandled rejection,
  accessibility warning or retry loop. The API logged no exception after its
  restart.
- **Network** on the same 21 routes: every request went to `localhost:4200`
  (proxied to the loopback API); no external host, font, tracking pixel or
  third-party request. One SignalR negotiate per page (two on Conversations,
  which predates this phase), no reconnect storm, no retry storm. The only
  duplicate API read is the bounded Notifications re-read described in section 8.
- **Live providers:** none. Stripe and Meta stubs logged no request; the mail
  sink held 0 messages; `OMNIAI_LIVE_OPENAI`, `OMNIAI_LIVE_META` and
  `OMNIAI_LIVE_EMAIL` were never set.

### Accessibility sanity

Visible focus on every control walked; focus return verified for the dialog,
Cancel, Save and every replaced control above; accessible names are specific
(invitation revoke, schedule edit and toggle); the confirmation dialog traps
focus in its buttons; menus unchanged from the implementation pass; the Reports
strip uses link semantics; the composer is fully keyboard-usable; errors are
worded, linked and marked invalid; status stays word plus marker, not colour
alone ("On" / "Off", "Draft", "Access token: not configured").

## 11. Backend regression

Run although no backend file changed, as the phase requires. Docker Desktop's
Linux engine (`mcr.microsoft.com/dotnet/sdk:10.0`, repository root mounted) —
this machine's Smart App Control policy blocks locally built .NET assemblies on
Windows. The integration suites used a throwaway PostgreSQL 17 + pgvector
container and a throwaway Redis on a private network, via `OMNIAI_TEST_POSTGRES`
and `OMNIAI_TEST_REDIS`; the schema comes from the model-driven Initial. The
suites left no `omniai_test_*` database behind, and the throwaway containers
were removed afterwards. `omniai_p335` and the old `omniai_*` databases were not
touched by the run.

| Gate                                                    | Result                                                                                                                            |
| ------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------- |
| `dotnet restore OmniAI.slnx`                            | Succeeded                                                                                                                         |
| `dotnet build OmniAI.slnx -c Release`                   | **0 warnings, 0 errors**                                                                                                          |
| `dotnet test OmniAI.slnx -c Release`                    | **3,361 passed, 0 failed, 0 skipped**: Domain 797, Application 714, Api.IntegrationTests 860, Infrastructure.IntegrationTests 990 |
| `dotnet list package --vulnerable --include-transitive` | No vulnerable packages in any of the eight projects                                                                               |

The first invocation was issued from the repository root and failed immediately
with "Project file does not exist" (the solution lives in `backend/`); it was
rerun from `backend/` with the repository still mounted at the root. That was an
invocation mistake, not a test failure.

## 12. Repository scan

- **Scope:** only `frontend/src/**` and `docs/**` changed. No backend file,
  database model, migration (the only migration is `20260925225105_Initial`),
  package manifest or lockfile, `.csproj`, settings file, permission, endpoint
  or business rule changed. No package was added.
- **No fake capability:** no client-side search, filter or sort; no test-only
  production endpoint; no authorisation bypass; no live-provider opt-in.
- **Content:** the complete diff (tracked changes and untracked files) was
  scanned. Email addresses are only on `example.test`, `example.invalid`,
  `example.com` and `omniai.example`; the only phone-like value is
  `+44 7700 900000` (Ofcom's reserved fictional range) in an existing spec; no
  key, token, bearer value, antiforgery value, session cookie or invitation token.
  No transcript, Knowledge content, database or mail dump, generated report,
  screenshot, browser profile, `node_modules`, `bin`/`obj`/`dist` or IDE file
  is in the change set.
- **Temporary artefacts:** headless-Chrome profiles, page dumps and comparison
  builds from this phase were deleted from the session scratch area; none was
  ever inside the repository.
- Nothing was staged, committed or pushed. No VPS was accessed; nothing was
  deployed.
