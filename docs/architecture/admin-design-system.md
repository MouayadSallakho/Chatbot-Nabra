# Admin design system and application shell

The permanent visual and interaction foundation for the OmniAI admin application.
Phase 10 built the shell, the token layer and the primitives. Phase 33.5 revised
them across the whole product -- the shell, the token scale, the shared list,
pagination, menu, dialog and notice patterns, and the error copy -- so that every
area is learned once. Sections below describe the current state; D21 lists the
product patterns and D22 what the backend contract does not yet support.

This document records what was decided and what was deliberately left for later.

---

## D1. UI philosophy

The product should read as calm, precise and unremarkable in the way good tools
are unremarkable. Three rules follow from that, and they explain most of the
decisions below.

**Hierarchy comes from surface and space, not from decoration.** A tinted page
behind white cards separates content without a shadow on every panel. Shadows
exist but are almost invisible; they mark genuine elevation — drawers and
overlays — rather than being sprinkled to suggest depth.

**One accent, used sparingly.** A single indigo family carries the primary
action, the current navigation item and links. Everything else is neutral. An
interface where several things compete for attention has, in practice, no
hierarchy at all.

**Nothing decorative that has to be maintained.** No gradients, no
glassmorphism, no illustrations, no animated backgrounds. These are the first
things to look dated and the first things to slow a page down, and none of them
help somebody configure an AI agent.

## D2. Angular shell architecture

```
App (root)                    router outlet only, deliberately empty
└── AppShell                  layout route: owns pinned + drawer state
    ├── Sidebar               navigation model, rail peek state (its own)
    ├── Topbar                drawer trigger, workspace context
    │   ├── NotificationBell
    │   └── ProfileMenu       identity, theme, preferences, switch, sign out
    ├── <router-outlet>       lazily loaded feature pages
    └── ToastHost             outside the grid: a fixed layer
```

Every page is a child of the shell, so navigation, the top bar and the account
menu persist across navigations instead of being rebuilt each time.

The root component holds nothing. That is a deliberate guard -- the root is the
easiest place in an Angular application to accumulate global state and
"temporary" markup.

The shell owns exactly two pieces of state: whether the sidebar is **pinned**
(which decides the grid) and whether the **drawer** is open (which the top bar's
trigger reports). Whether the rail is **peeking** changes no layout, so it is
the sidebar's own. The shell owns no feature logic and loads no data.

The toast host sits outside the grid. As a grid child it had claimed an implicit
second row, which left the sidebar and main region 52px short of the viewport.

**Standalone components, signals, `OnPush` everywhere.** The application is
zoneless, so signal-driven state is how change detection works here. No
state-management library.

## D3. Bootstrap's role, and its limits

Bootstrap **5.3.8**, CSS only, and only the grid build:

```jsonc
// angular.json
"styles": [
  "node_modules/bootstrap/dist/css/bootstrap-grid.min.css",
  "src/styles.scss"
]
```

**Used for:** containers, `.row`, `.col-*` and their breakpoints, plus the
layout utilities that ship in the same file.

**Not used for:** any component styling. Reboot is not loaded, so Bootstrap does
not touch typography, buttons, cards, tables or form controls — all of which are
defined by this design system. The product does not look like a Bootstrap admin
template because none of Bootstrap's component CSS is present.

**No Bootstrap JavaScript, and no jQuery.** The sidebar, drawer and theme
control are Angular's. `@popperjs/core` arrives as an auto-installed peer
dependency of the `bootstrap` package but is never imported and never reaches
the bundle.

**Why the prebuilt CSS rather than the SCSS entry point.** Compiling
`bootstrap/scss/bootstrap-grid` with the project's Sass emits **21 `@import`
deprecation warnings** — measured, not assumed. They come from Bootstrap's own
source, so they cannot be fixed here, and suppressing them is not acceptable.
The prebuilt file is the same grid with none of that noise. Nothing is lost:
Bootstrap 5 drives gutters through custom properties, so the grid is tuned to
this product's spacing scale without recompiling anything:

```scss
.row {
  --bs-gutter-x: var(--space-6);
  --bs-gutter-y: var(--space-6);
}
```

**Bootstrap's spacing utilities are deliberately not used.** Its spacers are a
second spacing scale, and using both would mean two systems in one template.
Spacing comes from the tokens; Bootstrap stays structural.

## D4. CSS Grid and Flexbox

The right tool per layer:

| Layer | Technique | Why |
| --- | --- | --- |
| Application frame | CSS Grid | A fixed column beside a filling one. Two regions, not twelve. |
| Page content | Bootstrap grid | Proportional columns that reflow at defined breakpoints. |
| Component internals | Flexbox | One-dimensional arrangement of a handful of children. |

Forcing the shell into Bootstrap columns would mean expressing "264px, then the
rest" as a twelfth of something. Forcing page content into hand-written media
queries would mean re-deriving what the grid already does.

## D5. Design tokens

`src/styles/_tokens.scss` is the single source of visual truth. Two rules:

1. Components consume semantic tokens, never raw values.
2. Tokens are named for their **role**, not their appearance. `--surface-raised`
   survives a palette change; `--grey-100` becomes a lie the first time the
   product is restyled.

Groups: surfaces, text, borders, accent, status (success / warning / danger /
info), shadows, skeleton, scrim, spacing, radius, typography, motion, layout.

Theming works by redefining the same custom properties under a different
selector, so no component stylesheet is duplicated per theme.

### Spacing — a 4px foundation

`--space-1` … `--space-10` = 4, 8, 12, 16, 20, 24, 32, 40, 48, 64 px.

Ten steps, nothing in between. If a gap seems to want 13px the answer is 12 or
16, not a new token.

### Radius

`--radius-sm` 4px (badges, checkboxes) · `--radius-md` 6px (buttons, inputs, nav
and menu items) · `--radius-lg` 8px (cards, tables, menus) · `--radius-xl` 12px
(dialogs and drawers) · `--radius-pill` (counts only). Small corners read as
precise; 16-24px rounding everywhere is what makes an admin look like a template.

### Control sizing

`--control-height-sm` 32 · `--control-height` 36 · `--control-height-lg` 40, shared
by buttons, inputs and selects so they align without measuring. Under
`pointer: coarse` all three rise to 40-44px touch targets. Icon sizes:
`--icon-size-sm` 16 · `--icon-size` 18 · `--icon-size-nav` 20.

### Layers

`--z-sticky` 10 · `--z-sidebar` 20 · `--z-dropdown` 30 · `--z-drawer` 40 ·
`--z-modal` 50 · `--z-toast` 60. The only z-index values in the product.

### Shadow

`--shadow-subtle` · `--shadow-raised` · `--shadow-overlay`. Redefined for dark
mode, where shadows read poorly and borders do most of the separating.

### Motion

`--motion-fast` 150ms (hover, press) · `--motion-normal` 200ms (menus, the rail
opening) · `--motion-slow` 280ms (drawers, dialogs, the rail closing), with
`--ease-standard` and `--ease-exit`. Every transition and animation reads these
tokens; under `prefers-reduced-motion` the tokens themselves collapse to ~0 (D19).

## D6. Typography

A system font stack. **No webfont is fetched from a CDN** — that is a
third-party dependency on every page load, a privacy consideration, and a
guaranteed flash of unstyled text. System UI fonts are also what the reader's OS
renders best.

Type steps are named for the job they do, so a stylesheet says what it means:
`--text-page-title`, `--text-section-title`, `--text-card-title`, `--text-body`,
`--text-body-strong`, `--text-small`, `--text-label`, `--text-nav`.

Body text is 14px, which reads better than 16px in a dense admin context. The
page title uses `clamp()` to scale between 22px and 28px with the viewport.

Headings are reset to inherit and given size by these classes, which keeps the
heading **level** — what a screen reader builds its outline from — independent
of the visual size.

## D7. Light mode

Not "white with black text". Hierarchy is built from a tinted page
(`#f5f7fa`) sitting behind white cards, so a surface reads as raised without a
shadow. Borders are light but present; text runs primary → secondary → muted;
the current navigation item gets a tinted background, the accent colour and a
solid edge marker.

## D8. Dark mode

Designed, not inverted. Surfaces are desaturated blue-greys, deliberately not
pure black: `#000` makes shadows invisible, causes halation against light text,
and reads as "terminal" rather than "product". Text is `#e7ecf3` rather than
pure white for the same reason.

The accent is a **different value**, not the same one reused — the light indigo
would fail contrast on dark surfaces. Status colours, borders, shadows,
skeletons and the scrim are all separately specified.

### Contrast

Every foreground/background pair in both palettes was checked against WCAG
programmatically. **40 pairs, all passing** — text at ≥ 4.5:1, control
boundaries at ≥ 3:1.

Three failures found and fixed during the phase:

- light `--text-muted` was 4.46:1 → darkened to pass at 4.80:1
- dark `--danger-solid` gave 4.34:1 against its foreground → lightened to 5.19:1
- a dedicated `--border-control` token was added at ≥ 3:1 for interactive
  control boundaries, which WCAG 1.4.11 requires and decorative borders do not

## D9. Light / Dark / System

Exactly three modes. `system` is a choice in its own right — "keep following the
operating system" — not the absence of one.

`ThemeService` holds the preference as a signal; the resolved theme is
**derived**:

```ts
resolved = computed(() =>
  preference() === 'system' ? (systemPrefersDark() ? 'dark' : 'light') : preference()
);
```

Because it is derived, an OS change while `system` is selected flows through
automatically and an explicit choice ignores the OS just as automatically.
Neither behaviour has its own code path, so neither can be broken independently.

The resolved theme — never the preference — is written to `data-theme` on the
document element. `data-theme="system"` would force every themed rule to also
handle the media query.

**Persistence is `localStorage`, per browser.** OmniAI has authenticated users,
but there is no server-side UI or profile preference contract to store a theme
(or the sidebar pin) against, so the choice stays with the browser rather than
following the person between devices. Adding that contract is backend work and
out of scope here. See D22 and *Deferred*.

## D10. Startup, and the flash of the wrong theme

A small inline script in `<head>` resolves the theme and stamps `data-theme`
before first paint. Without it, the browser paints light, Angular boots, and a
dark-mode reader sees a white flash on every single load.

It is inline because an external file is one more round trip during which the
flash happens anyway, and it is wrapped in `try`/`catch` because `localStorage`
**throws** rather than returning null in some privacy modes.

**This duplicates three constants** from `core/theme/theme.ts` — the storage
key, the attribute name and the preference values — because it has to run before
the bundle exists. The duplication is unavoidable and is guarded by
`theme-startup.spec.ts`, which pins the literals so that renaming a constant
fails a test whose name says which file to edit.

There is also a `@media (prefers-color-scheme: dark)` fallback in the token
layer, scoped so it can never override an explicit choice. It applies only when
the inline script did not run at all — that is, with JavaScript disabled.

## D11. Navigation model

`NAV_ITEMS` in `layout/navigation.ts` is the single source of truth. The sidebar
renders it **and the router generates routes from it**, which is what makes a
dead navigation link structurally impossible rather than merely unlikely. A test
walks every entry and asserts each resolves to a page.

Each entry carries `implemented`, so the product can be honest about which areas
exist. It is not a feature flag; it disappears as each area ships. Since Phase
33.5 every listed entry is implemented: Integrations, the last placeholder, was
taken out of the primary navigation rather than offered as a destination where
nothing can be done. An unbuilt area can still be listed with
`implemented: false` and gets the generated placeholder route, but the primary
navigation should not carry one.

## D12. Sidebar

Five shallow sections -- **Work** (Overview, Conversations, Customers), **AI**
(AI Agents, Knowledge), **Channels**, **Insights** (Analytics, Reports),
**Workspace** (Team, Roles & permissions, Plan & usage, Billing) -- with daily
work first and administration last. Every destination has its own glyph.
Notifications are reached from the bell and the account menu, not the sidebar.

**Rail (desktop default, unpinned).** A 64px column of icons. The panel is
always the full 240px wide and is clipped to the rail with `clip-path`; peeking
animates only the clip, so nothing behind it reflows, and the clip also clips
hit-testing. It opens after the pointer rests for 150ms (a sweep across the rail
does not open it), closes 260ms after the pointer leaves (closing slower than
opening is deliberate), and a pointer that comes back cancels a pending close.
Touch never peeks. Keyboard focus entering the rail opens it at once, so hover is
never required; it does not close while keyboard focus is inside; Escape closes
it and moves focus to the page. Labels fade but never leave the DOM, so every
link keeps its accessible name in every state.

**Pinned.** "Pin sidebar" keeps it expanded and the layout makes room for it. The
preference is this browser's (`omniai.sidebar-pinned` in `localStorage`); it is
not sent anywhere.

The current page is marked three ways: a tinted fill, the accent colour and an
inline-start edge marker -- the marker is what still reads in greyscale and on
the rail. `aria-current="page"` announces it.

## D13. Mobile navigation

Below `lg` (992px) the sidebar leaves the grid and becomes an overlay drawer.
This is a different layout, not a narrower one: a shrunken permanent sidebar
would eat a third of a phone screen forever.

The drawer stays rendered so it can animate, which means an off-screen menu
would otherwise still be in the tab order — one of the most common ways drawers
fail for keyboard users. `inert` is what prevents it. While the drawer is open,
`inert` moves to the page content instead, which makes it a genuine modal
surface: focus cannot escape behind it and a screen reader cannot wander into
hidden content.

Opening moves focus into the drawer. Closing — by Escape, the close button, the
scrim, or navigating — returns focus to the trigger.

No hover logic runs in drawer mode, and navigation items grow to 44px targets
under `pointer: coarse`. The closed drawer casts no shadow, so nothing of it
bleeds onto the page edge.

## D14. Top bar

Global context and global actions only: the drawer trigger (narrow screens), the
workspace every screen is about, the notification bell and the account menu.
Page actions belong to the page header.

The **account menu** shows the signed-in address and the workspace, the theme
(System / Light / Dark as `menuitemradio`), notification preferences, switch
workspace (only when there is another) and sign out. Initials come from the
address; there is no invented display name, role or avatar image. It is built on
the shared `Menu` (D21).

The workspace name is a `<p>`, not a heading; the page's own `<h1>` lives in the
page header. While the workspace loads, a skeleton holds the bar's height.

## D15. Page layout

`.page` gives every routed view a max width of **1600px** with token gutters
(24px, 16px on a phone), centred. `.page-narrow` caps forms at 60rem.

`PageHeader` fixes the `<h1>` -- exactly one per view -- and the positions the
reader learns once: the way **up** (a quiet "← Parent" link above the title, on
every detail and create page), a **status** slot beside the title, the
description, and the **actions** at the end of the title row with the primary
one last. Back links return to the parent exactly as it was left -- the same page
of a list -- through `NavigationHistory` (D21).

## D16. Primitives

Buttons, cards, badges, form controls, tables, lists and the empty / loading /
error states are **CSS conventions**, not Angular components. Components are
reserved for primitives that carry structure or behaviour: `Icon`, `PageHeader`,
`EmptyState`, `Pagination`, `Menu`, `ConfirmDialog`.

- **Buttons** -- primary / secondary / ghost / danger / danger-ghost / link, in
  sm / default / lg, plus icon-only. Loading overlays a spinner on a transparent
  label, so a busy button keeps its width.
- **Cards** -- border plus tinted page; `.card-header` (a sentence in it wraps
  under the title), `.card-body`, and `.card-actions` as a card's last child is
  its footer. Content never sits directly in a `.card` without `.card-body`.
- **Badges** -- a dot and a word, on a 4px-radius chip. Tones: neutral, success,
  warning, danger, info, accent; `.badge-plain` (no dot) for metadata such as a
  channel; `.count-badge` for numbers.
- **Forms** -- `.field` > label + control + hint + error; `.field-row` for two
  compact fields; single-line fields capped at 40rem. The Bootstrap names some
  screens use (`.form-control`, `.form-select`, `.form-label`, `.form-hint`,
  `.form-error`) are aliases of the same rules -- only Bootstrap's grid is
  loaded, so they had been rendering as browser defaults. `.switch`
  (`role="switch"`), `.segmented` (radios), native checkboxes in the accent.
- **Tables** -- `.table-wrap` frame with contained scroll (and the containing
  block for a hidden caption), caption-style headers, `.numeric` end-aligned
  tabular figures, `.actions` column.
- **Data list** -- `.data-list` / `.data-list-head` / `.data-row`: the pattern for
  lists whose rows are links. A table's look on a wide screen, stacked rows on a
  phone; a feature supplies only `--data-list-columns`.
- **Toolbar, pager** -- `.toolbar` / `.toolbar-end`; `.pager` drawn by the
  `Pagination` component.
- **Menus** -- `.menu`, `.menu-item`, `.menu-separator`, `.menu-label`.
- **Notices** -- `.alert-*` (danger, warning, info, success) and `.stale-notice`,
  the one treatment for "this changed somewhere else".
- **Empty / error / skeleton** -- skeletons match the final geometry; the sweep
  stops under reduced motion.

## D17. Icons

**No icon package.** The set is authored in `shared/icon/icon-paths.ts` on one
24×24 grid, outline-only, one stroke width, round caps and joins -- so an
inconsistent icon is impossible by construction. Phase 33.5 gave every
navigation destination its own glyph (Customers, Reports, Plan & usage, Billing,
Roles previously reused Team, Analytics and Settings) and added the interaction
glyphs the shared patterns need (pin, more, search, sign out, switch, chevron
down, arrow start, refresh, download, plus).

Glyphs are stored as **path data** and rendered via `<path [attr.d]>`; no SVG
source passes through `innerHTML`. Icons are `aria-hidden`; the accessible name
belongs to the control.

## D18. Accessibility baseline

Treated as a product requirement, not a pass at the end.

- Semantic landmarks: `nav[aria-label="Primary"]`, `main`, `header`
- A skip link, first in the tab order, that **actually moves focus**
- `aria-current="page"` on the active navigation entry
- `aria-expanded` + `aria-controls` on the drawer trigger, the notification bell
  and every menu trigger; `aria-haspopup="menu"` on menu triggers
- `inert` for the closed drawer and for content behind an open one
- Visible `:focus-visible` rings everywhere, one treatment defined once
- Escape closes the drawer; focus returns to the trigger
- Labels never removed for assistive technology, only hidden visually
- Status conveyed by shape and text as well as colour
- Contrast verified numerically across both themes
- `<caption>` and `scope` on the table

The theme choice lives in the account menu as `menuitemradio` items with
`aria-checked`, inside the shared `Menu`, which supplies the arrow keys, Home,
End, Escape and focus return. Segmented choices elsewhere (periods, formats) are
native radios, which keep arrow-key movement and the checked state the browser's.

## D19. Motion

Motion explains a change -- the rail opening, a menu appearing, a drawer sliding
in -- and nothing is animated for decoration. Transitions use transforms,
opacity, colour and `clip-path`; no `transition: all`, no animated layout.

`prefers-reduced-motion: reduce` is honoured **at the token level**: the three
duration tokens collapse to ~0, which reaches every component style. The earlier
universal-selector rule stays as a fallback, but on its own it lost to any
component rule (a component selector out-ranks `*`), so the rail and menus still
animated. Near zero rather than zero, so `transitionend` still fires. The loading
spinner keeps a slow fixed turn -- it is feedback, not decoration -- and the
skeleton sweep stops.

## D20. RTL readiness

The product is English-only and **no localisation was implemented**. Layout,
however, is written with logical properties throughout — `margin-inline`,
`padding-inline`, `inset-inline`, `border-inline`, `text-align: start` — so
adding Arabic later does not require a CSS rewrite.

The one thing logical properties cannot solve is `transform`, which does not
mirror. The drawer takes its direction from a token instead:

```scss
:root { --direction-flip: 1; }
[dir='rtl'] { --direction-flip: -1; }

transform: translateX(calc(var(--direction-flip) * -100%));
```

---

## D21. Product patterns

The patterns every area now shares, so a reader who has learned one screen has
learned them all.

| Pattern | Where it lives | What it guarantees |
| --- | --- | --- |
| Page header | `shared/page-header` | One `<h1>`; "← Parent" back link; status beside the title; actions at the end, primary last |
| Back to where you were | `core/navigation/navigation-history.ts` | A back link reopens the exact URL left (same list page) when that was its parent; otherwise the plain parent. The `href` stays the real address |
| Pagination | `shared/pagination` | "21–40 of 57 customers", Previous / Page n of m / Next; server-paged only; busy while loading |
| Page in the URL | `shared/pagination/page-query.ts` | `?page=` read on open and replaced (not pushed) on change; bad values mean page one; a remembered page past the end goes to the last page |
| Data list | `.data-list` primitive | One look for Agents, Knowledge, Conversations, Customers, Roles and Team |
| Menu | `shared/menu` | Arrow keys, Home / End, Escape and focus return, Tab closes, outside press closes, flips to the side with room |
| Confirmation | `core/feedback/confirm-dialog` | Native `<dialog>`; content rendered only while open; focus on the safe answer; backdrop and Escape cancel; title names action and object, body the consequence; `ask()` resolves the answer |
| Unsaved changes | `core/navigation/unsaved-changes.ts` | Route guard plus tab-close prompt on the agent, knowledge, role and member editors -- only when something is actually unsaved |
| Stale version | `.stale-notice` | Warning tone; the reader's edits stay on screen; one "Reload latest version" action |
| Toasts | `core/feedback/toast-*` | Successes polite, errors `role="alert"` and longer-lived; hovering or focusing holds them |
| Keep content on refresh | lists | A failed refresh keeps the rows and says so inline, instead of blanking the list |
| Focus continuity | each screen, via `afterNextRender` | When the control that held focus is removed or disabled by its own action, focus moves to the next meaningful place instead of the body: into a form that just opened (schedule, merge picker), to the first invalid field on a refused submit, back to the opener on Cancel or Save, to the result that replaced a form (invitation created, merged notice), to the section heading when a row is removed (revoked invitation, connected number), and into the composer after Take over or a sent reply |
| Field errors | forms | An error is shown beside its field, linked by `aria-describedby` and marked `aria-invalid`; a disabled submit button is never the only explanation. An empty field and a malformed value get different sentences |
| Error copy | `core/api/api-error.ts` | No status codes or internals; a 503 blames an AI provider only on AI operations; session expiry arrives at sign-in with a sentence saying why |

**Destructive and high-impact actions confirm first:** revoking an invitation,
disabling a website chat, taking a WhatsApp number out of service, taking a
Knowledge document out of use, disabling an agent. Switching any of them back on
does not ask.

**Overview.** Answers what needs attention (limits in Warning / Critical /
Exhausted, a suspended or overdue subscription, failed AI replies, undelivered
WhatsApp messages, unread notifications), what is happening now (open
conversations and who handles them), what changed (the last seven days), and
where to go next (shortcuts the reader may use). Every figure is the server's;
nothing is trended, because the dashboard returns no comparison period. Each
source is read only with the permission that reveals it, and a failed source
hides only its own section.

## D22. Unsupported by the current backend contract

Deferred rather than faked. Each needs a server capability first.

| Idea | Why not now |
| --- | --- |
| Search, filters and sorting on lists | No list endpoint accepts a query, filter or sort parameter -- only `page` and `pageSize`. Filtering one page client-side would lie about the rest |
| Column configuration, bulk selection and bulk actions | No bulk endpoints |
| KPI trends and period-over-period comparison | The dashboard returns one period, no comparison |
| Overview count of documents needing indexing | No aggregate; counting would mean reading every page |
| Server-held UI preferences (theme, pin) | No profile preferences endpoint; both stay in `localStorage` |

## Deferred

Not built, and deliberately so.

| Deferred | Note |
| --- | --- |
| Localisation and RTL | Layout is RTL-ready (logical properties, `--direction-flip`); no translations, no `dir` switching. Unicode content (Arabic names, accented Latin) renders and wraps correctly |
| Server-persisted preferences | Theme and pin live in `localStorage`; see D22 |
| Scroll position restoration | The shell scrolls an inner element and resets it on navigation; the page, not the scroll offset, is what Back restores |
| ESLint | The project has no lint script or ESLint config, and none was added |
| Integrations | Not built. Removed from the primary navigation in Phase 33.5 rather than shown as a dead placeholder; `/integrations` answers "Page not found" |
