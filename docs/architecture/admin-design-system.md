# Admin design system and application shell

The permanent visual and interaction foundation for the OmniAI admin application.
Phase 10 built the shell, the token layer and the primitives. Phase 33.5 revised
them across the whole product -- the shell, the token scale, the shared list,
pagination, menu, dialog and notice patterns, and the error copy -- so that every
area is learned once. Phase 36.2 replaced the visual foundation with the "Two
Voices" identity (D1, D5-D10, D16-D19); its brief, palette, type and decision
log are in `docs/design/MASTER.md`, which every UI change reads first. Phase
36.11 closed the redesign with a polish pass (D25). Sections below describe the
current state; D21 lists the product patterns and D22 what the backend contract
does not yet support.

This document records what was decided and what was deliberately left for later.

---

## D1. UI philosophy

**"Calm Glass" (Phase 39).** One tone, two voices: every Nabra conversation is a
duet between the AI and the team, with the customer as the third voice. The AI
speaks in **emerald**, the team in **gold**, the customer in plain **cream**, on
every screen, in the widget, in emails and in report files. Colour is
information, not decoration. The full brief, the contrast table and the
component catalogue are `docs/design/MASTER.md` (sections 3, 13 and 14).

**The voices mean one thing each.** Emerald and gold say who is answering -- in
a chip, a split bar, a 4px track, an event line in a transcript, the "Take over"
colour change -- and are never decoration. Status words that are not voices
(quotas, billing, indexing) reuse the hues only with their own glyph and word
(owner decision Q4). Nothing is shown in voice colours that the API cannot tell.

**Emerald is the action colour.** Primary buttons, links (always underlined) and
the focus ring are emerald; one primary action per area.

**Glass only where things float.** Menus, popovers, tooltips, dialogs, toasts and
the command palette (and the side bar from 39.3) are glass; conversations,
tables, forms and charts sit on solid panels with a 1px line and no shadow. At
most three blurred layers on screen; each becomes its solid twin under "Reduce
transparency", the OS preference and forced colours.

**Hierarchy comes from surface, space and type.** A cream ground (emerald-950 in
dark) behind solid panels; Geist and IBM Plex Sans Arabic, with Instrument Serif
for one display line; tabular figures keep numbers calm. Motion explains a
change and then stops.

**Nothing decorative that has to be maintained.** No gradients beyond the two
background glows and the orb, no illustrations, sparkle icons or animated
backgrounds.

## D2. Angular shell architecture

```
App (root)                    router outlet only, deliberately empty
└── AppShell                  layout route: wide | rail | bars, by the window's width (39.3)
    ├── Sidebar               from 992px: floating glass side bar (216px; a 72px rail below 1280px)
    │   ├── WorkspaceSwitcher a glass menu of the account's workspaces
    │   ├── NotificationBell  a glass popover beside the bar
    │   └── ProfileMenu       the member block: identity, theme, transparency, language, sounds, sign out
    ├── Topbar                below 992px: the glass top bar (area, workspace, palette, bell, account)
    ├── <router-outlet>       lazily loaded feature pages, on the ground with two glows
    ├── BottomBar             below 992px: four areas + "More" (a modal glass sheet)
    ├── CommandPalette        @defer: loaded on first Ctrl+K (or when idle), a modal dialog
    └── ToastHost             outside the grid: a fixed layer
```

Every page is a child of the shell, so navigation, the bars and the account
menu persist across navigations instead of being rebuilt each time.

The root component holds nothing. That is a deliberate guard -- the root is the
easiest place in an Angular application to accumulate global state and
"temporary" markup.

The shell owns two pieces of state: its **layout** (wide, rail or bars, from
two media queries; Phase 39.3 replaced the pin, the peeking rail and the drawer)
and whether the **command palette** is open. The "More" sheet's open state is the
bottom bar's own. The inbox's needs-a-person count comes from `NavCounts`, which
only tells the existing `InboxSignals` reader (38.2b) that the count is on screen:
no read of its own. The shell owns no feature logic and loads no data.

The toast host sits inside the frame but out of the grid (a fixed layer), so it
reads `--shell-nav-end`: bottom start beside the side bar on a desktop, under the
top bar on a phone. The frame is `overflow: clip`, so focus can never scroll it;
only the content scrolls.

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

`src/styles/_tokens.scss` is the single source of visual truth. Since Phase 39.2a
it holds the **Calm Glass** tokens (`calm-glass.md`, Phase 39 pack); the values,
the decisions and the contrast table are in `docs/design/MASTER.md` (sections 3
to 7 and 13). Two rules:

1. Components consume semantic or component tokens, never the raw scale and never
   raw values (`token_lint.py` from the `design-system-tokens` skill checks it).
2. Tokens are named for their **role**, not their appearance.

Three tiers:

- **Raw scale**: `--emerald-*`, `--cream-*`, `--sand-*`, `--stone-*`, `--ink-*`,
  `--gold-*`, `--coral-*`, `--mist-*`, `--sage-*`, `--white`. Builds the roles;
  read by nothing else.
- **Semantic roles**: `--color-*` (ground, panel, panel-soft, panel-raised,
  hover, selected, selection, line, line-strong, control-border, text,
  text-secondary, text-quiet, primary, primary-hover, primary-pressed,
  on-primary, link, link-hover, focus-ring, focus-halo), the three voices
  (`--voice-ai-*`, `--voice-team-*`, `--voice-customer-*`), chips (`--chip-need-*`,
  `--chip-ai-*`, `--chip-failed-*`), status (`--status-success|warning|danger|info-*`)
  and charts (`--chart-*`). Light on `:root`, dark in the `dark-palette` mixin.
- **Component tokens**: the glass material (`--glass-bg`, `-border`, `-shadow`,
  `-highlight`, `-blur`, `-solid-bg`), `--scrim`, `--glow-a`/`-b`,
  `--orb-gradient`, `--skeleton-*`, `--channel-whatsapp`/`-web`.

**The alias layer.** At the end of the file every Two Voices name (`--surface-*`,
`--text-primary|secondary|muted`, `--border-*`, `--action*`, `--link*`,
`--focus-ring`, `--selection`, `--voice-ai|human-*`, `--success|warning|danger|info-*`,
`--chart-human`, `--shadow-overlay`, `--radius-control|card|overlay`,
`--text-page-title|section-title|card-title|reading`, `--duration-xs…xl`,
`--ease-*`, `--motion-*`) is a deprecated alias of its Calm Glass role. Every
screen took the new palette and type at once; each Phase 39 screen step moves its
components to the new names, and 39.8b deletes the block. The aliases are
declared on `:root` (and the type aliases also on `:lang(ar)`), where every axis
switch lives, so they follow the theme, transparency and contrast.

The embedded widget, the hosted chat and the agent preview still take the
workspace's own colour as `--chat-accent` / `--chat-on-accent` (checked by
`accent-color.ts`), never the product's.

### Spacing -- a 4px foundation

`--space-1` … `--space-10` = 4, 8, 12, 16, 20, 24, 32, 40, 56, 64 px (Calm Glass
moved the ninth step from 48 to 56). Beside the scale: `--gap-hairline` (2px),
and the shell's `--shell-padding` 14px, `--column-gap` 12px, `--panel-padding`
24px.

### Radius

By role: `--radius-pill` 999 · `--radius-input` 12 · `--radius-nav-item` 12 ·
`--radius-tile` 16 · `--radius-panel` 24 · `--radius-dialog` 24 ·
`--radius-toast` 18 · `--radius-bubble` 18 with a `--radius-bubble-tail` 6px
corner on the speaker's side (logical corners) · `--choice-radius` 6 (the
checkbox box) · `--radius-badge` 6 (keys and small marks).

### Control sizing

`--control-height` 44 (buttons and inputs) · `--control-height-sm` 40 (the dense
size: `.btn-sm`, menu items, desktop headers) · `--control-height-lg` 44; every
one is 44px under `pointer: coarse`. `--target-min` 44px: each choice control's
label is a 44px target. `--chip-height` 24px (`--chip-height-sm` 22). The choice
controls draw a `--choice-size` 20px box or a `--switch-inline` ×
`--switch-block` 40×24 track with an 18px thumb. Avatars `--avatar-size` 40 /
`-sm` 34 / `-lg` 64; the orb `--orb-size` 26 / `-lg` 64. Icon sizes:
`--icon-size-sm` 16 · `--icon-size` 18 · `--icon-size-nav` 20.

The usage meter (Phase 39.6b): `--meter-height` 10px, a pill;
`--meter-track` (emerald-700 at 14%, dark sage at 18%) and the fills
`--meter-fill` (the primary), `--meter-warning` (gold-800 / gold-300) and
`--meter-critical` (coral-600 / coral-300), each 3:1 or more on its track and
on the soft tile. The state's chip and the figures beside it carry the
meaning; the fill never does alone.

### Layers

`--z-sticky` 10 · `--z-sidebar` 20 (the side bar and the phone's bars) · `--z-dropdown` 30 ·
`--z-toast` 60 · `--z-skip-link` 70. The only z-index values in the product.
Dialogs need none: they open in the browser's top layer.

### Glass and shadow

Solid panels have a 1px line and no shadow; only glass casts one. The `glass`
mixin and `.glass` class in `src/styles/_glass.scss` are the only places that
declare `backdrop-filter`: background, border, the inner highlight and the
shadow from the `--glass-*` tokens, the solid twin where `backdrop-filter` is not
supported, and system colours with no shadow in forced colours. Under
`data-transparency="reduced"` (the account menu's "Reduce transparency") and
`prefers-reduced-transparency`, the tokens themselves resolve to the solid twin.
Budget: three blurred layers at most, never on a scrolling row; the Design Lab
counts them (`countBlurredLayers` in `shared/glass/glass-budget.ts`). Glass is
used by `.menu`, `.popover`, `.tooltip`, the confirm dialog, the command
palette, the notifications panel and toasts (39.2b).

### Motion

`--duration-press` 120 · `--duration-menu` 200 · `--duration-dialog` 280 ms, one
easing `--ease-calm` `cubic-bezier(0.2, 0, 0, 1)`, `--duration-fade` 200 ms for a
fade kept under reduced motion, and `--motion-travel` (0 under reduced motion)
multiplying every distance. Under `prefers-reduced-motion` the three durations
collapse to ~0 at the source (D19).

## D6. Typography

Three self-hosted families, all under the OFL, shipped as WOFF2 files with their
licence texts in `public/fonts/` and declared in `src/styles/_fonts.scss`:

- **Geist** (variable `wght` 100-900; Latin 29,400 B, Latin Extended 16,512 B):
  all Latin text and the digits, in both languages.
- **IBM Plex Sans Arabic** (400, 500, 600, 700; Arabic subset only, 42-46 KB
  each): Arabic. 700 only for Arabic display.
- **Instrument Serif** (400; Latin 21,032 B, Latin Extended 11,604 B): the
  display line only (the overview greeting, empty-state lines, entry pages),
  never tables or forms.

**No font service is contacted, ever.** The files keep versioned, unhashed names
(`geist-latin.v1.woff2`) and the stylesheet names them root-relative, which the
build leaves as written. That lets the pre-paint script in `index.html` preload
the same URLs per language: Geist Latin always; Arabic 400 on Arabic pages, and
at low priority on English pages under `/conversations`. Every face carries a
`unicode-range`; `font-display: swap` with metric-matched local fallbacks
(`Geist Fallback`, `IBM Plex Sans Arabic Fallback`, `Instrument Serif Fallback`,
measured in 39.2a) means the swap moves nothing. The edge caches `.woff2` for a
year as immutable, so a changed file is `v2`.

Type steps (MASTER.md section 4): `--text-display` (Instrument Serif 44/48),
`--text-figure` (large numbers), `--text-title-1` (30/36), `--text-title-2`
(20/28), `--text-title-3` (16/24), `--text-body` (15/24), `--text-body-strong`,
`--text-small` (13/20), `--text-label`, `--text-caption` (12/16),
`--text-table`, `--text-nav`, `--text-numeric` (with `tabular-nums`). Titles
track at −0.01em.

Under `:lang(ar)`: the same sizes with one more step of line height, never under
13px, tracking 0, nothing uppercase, display in Plex Arabic 600.

Headings are reset to inherit and given size by the type classes, which keeps the
heading **level** -- what a screen reader builds its outline from -- independent
of the visual size.

## D7. Light mode

A cream ground (cream-100 `#F4F1EA`) behind white panels; text ink-900 →
ink-700 → stone-600; decorative lines sand-300; control boundaries
`--color-control-border` `#8A8370` (3:1). Emerald-700 is the primary action,
with white text. Values: MASTER.md section 3.

## D8. Dark mode

Designed separately, not inverted: an emerald-950 ground (`#051F20`) behind
emerald-900 panels (`#0B2B26`), text `#E9F2EC` → `#C9D8CF` → `#9AA79F`, lines in
emerald-300 at 16-28%, control boundaries `#668276`. The primary is emerald-300
with emerald-950 text; the team's gold and the failed coral are lifted for the
dark ground.

### Contrast

Every pair the tokens can produce is measured from the live, resolved tokens in
Chrome, in light, dark, and both under `prefers-contrast: more` (Phase 39.2a:
436 required pairs, 0 fail, 28 decorative reported). The table is MASTER.md
section 13; the script is in the 39.2a evidence. Text needs 4.5:1, control
boundaries and marks a reader must see 3:1. Under `prefers-contrast: more`, quiet
text becomes secondary and the lines become control boundaries.

## D9. Light / Dark / System

Exactly three modes. `system` is a choice in its own right -- "keep following the
operating system" -- not the absence of one.

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

The resolved theme -- never the preference -- is written to `data-theme` on the
document element. `data-theme="system"` would force every themed rule to also
handle the media query. The dark palette is one Sass mixin applied in two places
(an explicit `data-theme="dark"`, and the no-JavaScript media-query path), so the
two can never drift.

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
**throws** rather than returning null in some privacy modes. The edge allows it
by hash: `deploy/edge/csp-hashes.mjs` hashes the inline scripts of the built
`index.html` at image build time, so any change to the script changes its hash
automatically.

**This duplicates three constants** from `core/theme/theme.ts` -- the storage
key, the attribute name and the preference values -- because it has to run before
the bundle exists. The duplication is unavoidable and is guarded by
`theme-startup.spec.ts`, which pins the literals so that renaming a constant
fails a test whose name says which file to edit.

The loading state inside `<app-root>` shows the lockup -- the mark (the same
geometry as `BrandMark` and `public/nabra-icon.v1.svg`) beside the wordmark,
Nabra or نبرة by `lang` -- and "Loading…" / "جارٍ التحميل…". Its styles are
inline but its colours are tokens: the global stylesheet is a blocking `<link>`,
so they resolve before it paints, already in the right theme. It does not move:
a loading state, not a splash screen.

There is also a `@media (prefers-color-scheme: dark)` fallback in the token
layer, scoped so it can never override an explicit choice. It applies only when
the inline script did not run at all -- that is, with JavaScript disabled.

From Phase 36.3b the same script (still one script, so still one hash) also sets
`lang` and `dir` on `<html>`: the stored language, else the browser's, with the
hosted chat and widget always following the visitor. The loading status is
written in both languages and CSS shows the one matching `html[lang]`. See
[localisation-arabic-rtl.md](localisation-arabic-rtl.md), 3.5 and 11.1.

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

Five shallow sections, labelled in sentence case (36.4: no uppercase labels;
Arabic has no case) -- **Work** (Overview, Conversations, Customers), **AI**
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

The current page is marked three ways: the neutral selected fill, ink text at a
heavier weight and a 2px ink marker on the inline-start edge -- the marker is
what still reads in greyscale and on the rail. Since Phase 36.4 the marker is
the shared `.edge-marker` primitive: a short rounded bar placed with a logical
inset (it mirrors in RTL with no transform), replacing an inset shadow that
followed the item's rounded corner and read as a bracket. The command palette's
highlighted option uses the same marker. `aria-current="page"` announces it.
The brand row carries the lockup: the mark (D17) and the "OmniAI" wordmark in
the narrowed heading face; on the rail only the mark shows.

**Workspace identity lives in the top bar, not the sidebar** (decided 36.4).
The top bar exists at every width and the rail does not; the rail stays a
stable 64px column of destinations; and the switch belongs next to the name it
changes. The sidebar's footer keeps only the pin control.

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

Opening moves focus into the drawer (its 44px close button). Closing by Escape,
the close button or the scrim returns focus to the trigger. **Closing by
navigating puts focus on the new page's heading instead** (changed in 36.4,
with `RouteFocus`, D18): the reader chose a page, and returning them to the
trigger left them a whole Tab sequence away from it.

No hover logic runs in drawer mode, and navigation items grow to 44px targets
under `pointer: coarse`. The open drawer rounds the edge that faces the page
(logical corners, so it mirrors) and casts the overlay shadow; the closed drawer
casts none, so nothing of it bleeds onto the page edge. Opening the command
palette closes the drawer first: two modal surfaces never stack.

## D14. Top bar

Global context and global actions only: the drawer trigger (narrow screens), the
workspace every screen is about, the command palette's trigger, the
notification bell and the account menu. Page actions belong to the page header.

**Workspace identity (36.4).** The workspace's first letter on a neutral tile
(the account initials' family; no colour, no invented logo; an Arabic name gives
an Arabic letter), then the name in an isolate that truncates at its own end in
either language ("Northwind Opera…", never "…Operations"). When the account has
another workspace, the whole identity is a link to the chooser with a switch
glyph -- the same destination as "Switch workspace" in the account menu. Below
576px the tile gives way to the name.

**Palette trigger (36.4).** A quiet, field-shaped button -- "Search or jump
to…" with the shortcut in a `kbd` -- that opens the command palette (D23). It
is a button with `aria-haspopup="dialog"`, `aria-expanded` and
`aria-keyshortcuts`, not a field: it searches nothing itself. Below 768px only
its icon shows; the words stay its accessible name.

The **account menu** opens on the same face the trigger shows: the initials
beside the signed-in address and the workspace, each truncating inside its own
isolate. It holds the theme (System / Light / Dark as `menuitemradio`), the
language (each in its own name, `lang` and `dir`), notification preferences,
switch workspace (only when there is another) and sign out. Initials come from the
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

Two Voices rhythm (36.4): tight inside the block (4-8px), 24px after it. The
back link is a 28px target, the title balances across two lines rather than
leaving an orphan, the description is capped at 72ch, and the actions row is
one title line tall so the buttons centre on the title's first line. All 73
in-shell page headers render through `PageHeader`; pages outside the shell
(sign-in, registration, the workspace chooser, invitation acceptance, the
hosted chat and widget, the Design Lab) keep their own `<h1>`.

## D16. Primitives

Buttons, inputs, tables, lists, menus, bubbles and the loading / empty / error
states are **CSS conventions**; components are reserved for what carries
structure or behaviour. Phase 39.2b restyled every convention to Calm Glass in
place, without changing a class name or DOM contract, and added the components
the screen steps build from. The catalogue, with tokens, is MASTER.md section 14.

- **Buttons** -- `.btn-primary` (emerald) / `-secondary` (a panel with a line) /
  `-quiet` (text only; `.btn-ghost` is the same rule) / `-danger` /
  `-danger-ghost` / `.btn-link`, pills of 44px (`.btn-sm` 40px), weight 600,
  plus round icon-only buttons with an accessible name. Press is a 1px settle in
  120ms. Busy overlays a spinner and keeps the button's size and its visually
  hidden label.
- **Choice controls** (`shared/choice`, `shared/segmented`) -- `label[app-check]`,
  `label[app-radio]`, `label[app-switch]` and `[app-segmented]`: attribute
  components on the consumer's own label or container, around its native
  `<input>`, so ids, `formControlName`, testids and e2e locators are unchanged
  and keyboard and screen-reader behaviour is native. A 20px box or a 40×24
  track in a 44px target that includes the label; segmented options are radios
  (forms), links with `aria-current` (an address; never `role="tab"`) or buttons
  with `aria-pressed`. The global checkbox rule and `.switch` are gone (Phase 38
  notes 3 and 4).
- **Inputs** -- `.field` > label + control + hint + error; `.input`, `.select`,
  `.textarea` (and the `.form-*` aliases) are 44px, radius 12, with the 3:1
  control border; focus is a 2px emerald border and a 3px halo (an outline in
  forced colours); invalid by `aria-invalid`.
- **Chips** -- `app-chip` with a tone (need, team, waiting, ai, failed, neutral;
  success, warning, danger, info with their glyphs): a 24px pill, a dot and a
  word. The `.badge-*` classes are the same rules until each screen moves to
  `app-chip`; `.count-badge` stays for numbers. `app-voice-chip` is a chip that
  names the voice.
- **Panels and tiles** -- `.panel-surface` (radius 24, a line, no shadow) and
  `.tile-surface` (radius 16, the soft fill); `.card` keeps radius 16 until each
  screen's step.
- **Rows and avatars** -- `.list-row` (radius 16; avatar, name and time, one line
  of preview, chips; selected tint; unread dot + heavier name + the word) and
  `app-avatar` (initials or a code, the channel's badge with its glyph).
- **Tables, data list, toolbar, pager** -- unchanged in structure; restyled
  through the tokens.
- **Menus, popovers, tooltips** -- `.menu` and `.popover` are glass (radius 16);
  `[appTooltip]` (`shared/tooltip`) shows small glass on hover and keyboard
  focus, is hoverable and dismissed by Escape.
- **Notices** -- `.alert-*` and `.stale-notice`, unchanged in structure.
- **Dialogs and toasts** -- glass; the dialog on the scrim in 280ms; toasts with a
  glyph and a sentence (39.3 places them).
- **Transcript** -- `.bubble` + `-ai` / `-team` (`-human`) / `-customer`, the
  three voices, the tail on the speaker's corner, rising 8px; `app-orb` beside
  the AI; `app-event-line` (`role="note"`; `.handoff-divider` is the same rule)
  for failed turns, takeovers and returns.
- **Empty / error / skeleton** -- `app-empty-state` draws one serif line (Plex
  Arabic 600 in Arabic), one sentence and one action; skeletons sweep softly and
  stop under reduced motion, and each screen step draws its own geometry.

**Voice components** (`shared/voice/`): `VoiceChip`; `VoiceSplit` (AI vs people
counts as one bar, a legend and one sentence for screen readers); `VoiceTrack`
(the real sequence of reply authors, derived from the messages given and nothing
else).

**Design Lab.** `/design-lab` shows the tokens, the glass (with a live count of
blurred layers), the mark and the lockup, and every component in its states,
with theme, direction and "Reduce transparency" toggles. It is registered only in
development builds and compiled out of production (the route's condition is
`ngDevMode`).

## D17. Icons

**No icon package.** The set is authored in `shared/icon/icon-paths.ts` on one
24×24 grid, outline-only, one stroke width (1.6 since Phase 36.2, to match
Instrument Sans), round caps and joins -- so an inconsistent icon is impossible
by construction. Phase 33.5 gave every navigation destination its own glyph;
Phase 36.2 added the voice glyphs `ai` (a calm bot face -- never a sparkle) and
`person`, and generic channel glyphs: `website`, `whatsapp` (a phone in a speech
bubble), `instagram` (a camera), `messenger` (two bubbles). Channel glyphs are
always shown with the channel's name; no brand logo is recoloured.

Glyphs are stored as **path data** and rendered via `<path [attr.d]>`; no SVG
source passes through `innerHTML`. Icons are `aria-hidden`; the accessible name
belongs to the control.

**The mark** (`shared/brand-mark/`) is two rounded speech shapes overlapping:
Lagoon for the AI, Ember for a person -- two voices in one tone, which is what the
name Nabra (نبرة, "tone of voice") says. Until the brand step the shapes also read
as the "O" of the code name; the geometry is unchanged. The Lagoon shape is pre-cut around
the Ember one, so it needs no mask or `url(#id)`, reads at 16px, works in a
single colour (`tone="mono"`, and in forced colours) and is never mirrored. The
same geometry is in `public/nabra-icon.v1.svg` (which follows the browser's
colour scheme), `public/nabra-icon.v1.ico` (16, 32, 48) and the `index.html`
loading state. The icon files carry a version in their names because the edge
caches them for a year: a changed mark ships as v2, never under the old name.

## D18. Accessibility baseline

Treated as a product requirement, not a pass at the end.

- Semantic landmarks: `nav[aria-label="Primary"]`, `main`, `header`
- A skip link, first in the tab order, that **actually moves focus**
- `aria-current="page"` on the active navigation entry
- `aria-expanded` + `aria-controls` on the drawer trigger, the notification bell
  and every menu trigger; `aria-haspopup="menu"` on menu triggers
- `inert` for the closed drawer and for content behind an open one
- Visible `:focus-visible` rings everywhere, one treatment defined once: 2px of
  ink with a 2px offset, drawn with `outline` so it survives forced colours
- Escape closes the drawer; focus returns to the trigger
- **Focus after navigation (36.4, `core/navigation/route-focus.ts`):** after
  every change of page, focus moves to the new page's `<h1>` (given
  `tabindex="-1"`, no ring: a reading position, like the skip link's target).
  It waits up to 3s for a page that renders its heading after loading, then
  settles on the content region. Not on the first load, not for a change of
  query string or fragment only (the pager keeps focus), and never over a
  choice the new page already made (focus already inside the content)
- **Pager focus (36.4):** while a page loads, the pager's buttons are
  `aria-disabled` and ignore presses rather than `disabled`, which had dropped
  focus to the body; at either end of the list focus moves to the other button
- **Command palette (D23):** a modal `<dialog>` (the page behind is inert)
  holding an editable combobox (`aria-activedescendant`, `aria-controls`,
  `aria-autocomplete="list"`, `aria-expanded`) and a grouped listbox (groups
  labelled by their heading, after the APG grouped-listbox example). Focus
  stays in the field; Escape returns it to where it was
- Labels never removed for assistive technology, only hidden visually
- Status conveyed by shape and text as well as colour; each voice by its glyph
  and word as well as Lagoon or Ember
- Voice displays hide their bars from assistive technology and say one plain
  sentence instead
- Contrast verified numerically across both themes and under
  `prefers-contrast: more` (D8)
- Targets at least 24px, 40-44px under a coarse pointer
- `<caption>` and `scope` on the table

The theme choice lives in the account menu as `menuitemradio` items with
`aria-checked`, inside the shared `Menu`, which supplies the arrow keys, Home,
End, Escape and focus return. Segmented choices elsewhere (periods, formats) are
native radios, which keep arrow-key movement and the checked state the browser's.

## D19. Motion

Functional and quick: motion explains a change and nothing is animated for
decoration. Transitions use transforms, opacity and colour; no
`transition: all`, no animated layout.

- **Frequent actions stay within 150ms** (Phase 36.11): anything a reader does
  many times an hour -- hover, press, a switch, a menu or the bell's panel
  opening, the command palette, a toast after a save, a new transcript message
  settling, the rail peeking open (100ms in, 150ms back) and its labels.
- Press: a 1px settle in 100ms. Hover and colour: 150ms.
- Rarer moments keep their length: the widget opening and the dialog scrim
  (220ms), dialogs and the phone navigation drawer (320ms).
- Dialogs and drawers: 320ms; the dialog's scrim fades in.
- **The "Take over" moment**: a voice chip that changes voice sweeps from Lagoon
  to Ember (and back for "Return to AI") in 320ms on the emphasized curve.
- **Route changes**: a 150ms crossfade of the content area through the View
  Transitions API (`withViewTransitions({ skipInitialTransition: true })`). Only
  `.shell-main` carries a `view-transition-name`, and the root snapshot does not
  animate, so the sidebar and top bar never flicker; a no-op in browsers without
  View Transitions. Since 36.4 `onViewTransitionCreated`
  (`core/navigation/route-transition.ts`) makes the transition **instant** when
  only the query string or fragment changes (a list's next page) and under
  reduced motion, by marking `<html>` with `route-transition-instant` until it
  finishes. Instant rather than `skipTransition()`: skipping rejects the
  transition's `ready` promise, which the router logs as a console error in
  development on every page of a list.
- **Command palette:** the dialog enters in 150ms on the enter curve (fade, 8px
  of travel, scale from 0.98) over a fading scrim, and leaves at once. The
  bell's panel enters like a menu (150ms).
- **The entry pages' signature** (Phase 36.5, D24): the mark's Ember shape
  settles into the room the Lagoon shape keeps for it, translate only, 600ms on
  the emphasized curve after 150ms, once per page load, none under reduced
  motion. Never on the heading or the form.
- Not in the admin: count-ups, stagger on every visit, parallax, fade-up on
  sections, bounce.

`prefers-reduced-motion: reduce` is honoured **at the token level**: every
duration token collapses to ~0, which reaches every component style (a component
selector out-ranks the universal fallback rule, so the token is what works).
Near zero rather than zero, so `transitionend` still fires. View Transitions are
switched off, the "Take over" sweep becomes instant and the skeleton sweep
stops. The loading spinner deliberately keeps its slow 0.9s turn (Phase 33.5):
it is essential feedback, small, and travels nowhere.

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
| Command palette | `layout/command-palette` | Ctrl+K / Command+K anywhere; destinations from `NAV_ITEMS` and permissions (`visibleNavItems`, shared with the sidebar); quick actions behind the permission their page uses; commands, never data, and it says so (D23) |
| Focus after navigation | `core/navigation/route-focus.ts` | The new page's heading takes focus; query-only changes and a page's own focus choice are left alone |
| Key | `.kbd` primitive | A key as printed on a keyboard; always an LTR island, never translated |
| Current-item marker | `.edge-marker` primitive | The 2px ink bar on the start edge, for the current page and the highlighted palette option |
| Confirmation | `core/feedback/confirm-dialog` | Native `<dialog>`; content rendered only while open; focus on the safe answer; backdrop and Escape cancel; title names action and object, body the consequence; `ask()` resolves the answer |
| Unsaved changes | `core/navigation/unsaved-changes.ts` | Route guard plus tab-close prompt on the agent, knowledge, role and member editors -- only when something is actually unsaved |
| Stale version | `.stale-notice` | Warning tone; the reader's edits stay on screen; one "Reload latest version" action |
| Toasts | `core/feedback/toast-*` | Successes polite, errors `role="alert"` and longer-lived; hovering or focusing holds them |
| Keep content on refresh | lists | A failed refresh keeps the rows and says so inline, instead of blanking the list |
| Focus continuity | each screen, via `afterNextRender`; between pages, the shell's `RouteFocus` | When the control that held focus is removed or disabled by its own action, focus moves to the next meaningful place instead of the body: into a form that just opened (schedule, merge picker), to the first invalid field on a refused submit, back to the opener on Cancel or Save, to the result that replaced a form (invitation created, merged notice), to the section heading when a row is removed (revoked invitation, connected number), and into the composer after Take over or a sent reply |
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

**D22 still stands for display (Phase 37.3).** The interface's language, theme and
sidebar pin are still the browser's (`localStorage`); the server does not drive what
the admin shows. What changed is that the **language is now also a stored
communication preference**: `users.language` decides what the server writes to the
person (emails). The client sends the language in use with sign-up
(`POST /api/auth/register`) and invitation acceptance (`POST /api/invitations/accept`),
and when a signed-in person switches language it also saves it with
`PUT /api/me/language`, without waiting on it for more than two seconds and without
failing the switch if the call fails. See `authentication-tenant-membership.md` D28.
| Searching records from the command palette, and "recent" items in it | No search endpoint across conversations, customers or documents, and no server-held history; the palette searches commands only (D23) |

## D23. Command palette (Phase 36.4)

**What it is.** Ctrl+K (Command+K on Apple devices; matched on the physical K
key, so it works with an Arabic layout active) or the top bar's trigger opens a
palette that jumps to any page the member may open and runs a few quick
actions: create an AI agent (`ai-agents.create`), upload a knowledge file
(`knowledge.manage`), invite a member (`members.invite`), open the notification
panel, switch workspace (only with another to go to), choose a theme, choose a
language. Pressing the shortcut again closes it; it does not open over a
confirmation that is waiting for an answer.

**What it is not.** It searches commands on the device, never data, and never
calls the server. The line under the field says so ("It does not search
conversations, customers or other records"), so nobody types a customer's name
and concludes the customer does not exist. Searching records needs a server
search endpoint (D22).

**Matching.** Every word of the query must match; a word that starts a word of
the label ranks first, one inside the label next, one among the keywords last.
Case, Latin accents, tashkeel and tatweel are ignored, and the Arabic letters
people type interchangeably (hamza forms of alef and plain alef, taa marbuta and
haa, alef maqsura and yaa, hamza on waw and yaa) count as one. The typed part is
emphasised in Latin labels only: a change of weight inside an Arabic word breaks
its letter joining.

**Cost.** Loaded through `@defer (when requested; prefetch on idle)`: 17.3 kB
raw, 5.8 kB gzip, in a lazy chunk; nothing of it is in the startup JavaScript.

## D24. Entry pages (Phase 36.5)

**What they are.** Sign-in, registration, an invitation, and choosing or
creating a workspace: the pages before the shell, sharing `EntryFrame`
(`layout/entry-frame/`). The full brief, the layout per width and the one
exception to MASTER rules 7 and 10 are in `docs/design/pages/entry.md`.

**Frame.** A `header` (the lockup, then English / العربية), one `main` holding
the page's content, and from 1024px an `aside` story panel named by its tagline:
the mark, the promise, and an example conversation built from the transcript
primitives, labelled "Example conversation" and `aria-hidden`. Below 1024px the
panel is not drawn and the form follows a compact header. The `h1` is the task
("Sign in"), never animated.

**Language before sign-in.** A `.segmented` group of buttons (`aria-pressed`)
that reloads into the chosen language through `LanguageService`. Withheld while
an invitation token waits in memory, because the reload would lose it.

**Copy.** Every string on these pages, and on not-found and the placeholder, is
now a translated message; server sentences (a refused sign-in, a taken slug)
still arrive in English and render with `dir="auto"` (D22).

**Not found** is a calm state: the path that matched nothing as an LTR island,
without its query or fragment, and one primary way back.

## D25. Polish and the final state (Phase 36.11)

- **One customer chat.** The website widget, the hosted chat page, the widget
  preview in the website chat settings and the agent editor's "Customer view"
  all draw `shared/chat/` (`ChatPanel`, `ChatTranscript`). The editor's preview
  feeds the panel its own single-turn exchanges; its failures go through the
  panel too, and `infoKinds` lets it say an unconfigured provider as
  information, which a visitor is still told as a failure. The `.customer-view`
  layer is gone.
- **One set of limit words.** Overview's attention cards name limits, states,
  figures and consequences with Plan & usage's own words (`plan-words.ts`), and
  its meter takes the same four states.
- **Landmarks.** The whole sidebar is the `navigation` landmark (the product name
  and the pin toggle with the links); the top bar is the `banner`
  (`role="banner"`, because the bell's panel has a `<header>` of its own). No
  shell text sits outside a landmark.
- **The account button's name begins with the initials it shows** ("AO: Account
  menu for …"), so speech input can reach it by what it reads (WCAG 2.5.3).
- **Loading states hold the loaded geometry.** Overview's attention, "now",
  figures and the 30-day chart keep their loaded size while their reads are on
  their way (measured from 390 to 1920px; the chart card used to be absent).
  Only the number of attention items cannot be known ahead: one card is held.
- **Startup reads in parallel.** The antiforgery token and the session are asked
  for together; a browser that holds a workspace selection also reads the
  workspace list and its permissions alongside the session check, as probes a
  401 cannot act on (`core/auth/session-probe.ts`), and starts the deep-linked
  page's lazy code (`core/navigation/route-warmup.ts`). A conversation reads its
  facts and its first transcript page together.
- **Reflow.** Grids that hold cards declare their column (`minmax(0, 1fr)`): an
  implicit one grows to a text field's intrinsic width and pushes a 320px screen
  sideways.

## Deferred

Not built, and deliberately so.

| Deferred | Note |
| --- | --- |
| Localisation and RTL | No longer deferred: English and Arabic with full RTL from Phase 36.3b, every screen translated by Phase 36.10 and read through in 36.11. See [localisation-arabic-rtl.md](localisation-arabic-rtl.md) |
| Server-persisted preferences | Theme and pin live in `localStorage`; see D22 |
| Scroll position restoration | The shell scrolls an inner element and resets it on navigation; the page, not the scroll offset, is what Back restores |
| ESLint | The project has no lint script or ESLint config, and none was added |
| Integrations | Not built. Removed from the primary navigation in Phase 33.5 rather than shown as a dead placeholder; `/integrations` answers "Page not found" |
