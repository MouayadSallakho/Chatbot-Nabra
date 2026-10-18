# Admin design system and application shell

The permanent visual and interaction foundation for the OmniAI admin application.
Phase 10 built the shell, the token layer and the primitives. Phase 33.5 revised
them across the whole product -- the shell, the token scale, the shared list,
pagination, menu, dialog and notice patterns, and the error copy -- so that every
area is learned once. Phase 36.2 replaced the visual foundation with the "Two
Voices" identity (D1, D5-D10, D16-D19); its brief, palette, type and decision
log are in `docs/design/MASTER.md`, which every UI change reads first. Sections
below describe the current state; D21 lists the product patterns and D22 what
the backend contract does not yet support.

This document records what was decided and what was deliberately left for later.

---

## D1. UI philosophy

**"Two Voices."** Every OmniAI conversation is a duet between the AI and the
team, and the interface gives each voice one colour: **Lagoon** for the AI,
**Ember** for a person on the team. Colour is information, not decoration;
everything else is calm ink on porcelain, comfortable for an eight-hour shift
and premium without effects. The full brief is `docs/design/MASTER.md`.

**The voices mean one thing each.** Lagoon and Ember say who is answering --
in a chip, a split bar, a 4px track, a handoff divider in a transcript, the
"Take over" sweep -- and are never used for decoration or for status. Status
keeps its own colours (green, yellow amber, crimson, blue), each with its own
icon and word; a voice always carries its glyph and word. Nothing is shown in
voice colours that the API cannot tell.

**Ink is the action colour.** Primary buttons are ink on light and porcelain on
dark, links are ink and always underlined, the focus ring is ink, and selection
is neutral. There is no brand blue or indigo anywhere.

**Hierarchy comes from surface, space and type, not decoration.** A porcelain
page behind white panels and hairline borders; a shadow only on overlays.
Instrument Sans, narrowed for headings, gives the type its character; tabular
figures keep numbers calm.

**Nothing decorative that has to be maintained.** No gradients, glow, glass,
illustrations, sparkle icons or animated backgrounds. These are the first things
to look dated, the first to slow a page down, and none of them help somebody
answer a customer.

## D2. Angular shell architecture

```
App (root)                    router outlet only, deliberately empty
└── AppShell                  layout route: owns pinned + drawer state
    ├── Sidebar               navigation model, rail peek state (its own)
    ├── Topbar                drawer trigger, workspace identity + switch, palette trigger
    │   ├── NotificationBell
    │   └── ProfileMenu       identity, theme, language, preferences, switch, sign out
    ├── <router-outlet>       lazily loaded feature pages
    ├── CommandPalette        @defer: loaded on first Ctrl+K (or when idle), a modal dialog
    └── ToastHost             outside the grid: a fixed layer
```

Every page is a child of the shell, so navigation, the top bar and the account
menu persist across navigations instead of being rebuilt each time.

The root component holds nothing. That is a deliberate guard -- the root is the
easiest place in an Angular application to accumulate global state and
"temporary" markup.

The shell owns three pieces of state: whether the sidebar is **pinned**
(which decides the grid), whether the **drawer** is open (which the top bar's
trigger reports) and whether the **command palette** is open (Phase 36.4; the
top bar's trigger reports it too). Whether the rail is **peeking** changes no layout, so it is
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

Groups: surfaces, text, borders (decorative hairlines and `--border-control`),
action (`--action`, `-hover`, `-pressed`, `--on-action`, `--link`,
`--focus-ring`, `--selection`), the voices (`--voice-ai-*`, `--voice-human-*`:
`fg`, `strong`, `tint`, `border`), status (success / warning / danger / info),
charts (`--chart-ai`, `--chart-human`, `--chart-customer`, `--chart-neutral`,
`--chart-grid`, `--chart-ramp-1…5`), shadows, skeleton, scrim, spacing, radius,
type, motion, layout, layers.

The Phase 33.5 `--accent-*` family is gone. Every use was remapped by meaning in
Phase 36.2: the primary action, counts and tab markers to ink; links to
`--link`; selection to the neutral `--surface-selected`; anything that meant
"the AI" or "a person" to the matching voice; charts to the chart tokens. The
embedded widget keeps its own `--embed-accent-*`, which is the workspace's own
colour, not the product's.

Theming works by redefining the same custom properties under a different
selector, so no component stylesheet is duplicated per theme.

### Spacing -- a 4px foundation

`--space-1` … `--space-10` = 4, 8, 12, 16, 20, 24, 32, 40, 48, 64 px.

Ten steps, nothing in between. If a gap seems to want 13px the answer is 12 or
16, not a new token.

### Radius

By role: `--radius-badge` 6px · `--radius-control` 10px · `--radius-card` 14px ·
`--radius-overlay` 20px (dialogs, drawers) · `--radius-bubble` 18px with a
`--radius-bubble-tail` 6px corner on the speaker's side, written with logical
corners so it mirrors in RTL · `--radius-pill` for counts and chips only. The
older names still resolve: `--radius-sm` → badge, `-md` → control, `-lg` → card,
`-xl` → overlay, so no rule had to be touched to follow the new shape.

### Control sizing

`--control-height-sm` 32 · `--control-height` 36 · `--control-height-lg` 40, shared
by buttons, inputs and selects so they align without measuring. Under
`pointer: coarse` they rise to 40-44px touch targets. Icon sizes:
`--icon-size-sm` 16 · `--icon-size` 18 · `--icon-size-nav` 20; `--icon-stroke`
1.6.

### Layers

`--z-sticky` 10 · `--z-sidebar` 20 · `--z-dropdown` 30 · `--z-drawer` 40 ·
`--z-modal` 50 · `--z-toast` 60 · `--z-skip-link` 70. The only z-index values in
the product.

### Shadow

Hairlines everywhere; one shadow, for overlays only (menus, dialogs, toasts, the
peeking sidebar): `--shadow-overlay` and `--shadow-raised`, both
`0 1px 2px rgb(22 24 27 / 5%), 0 12px 32px -8px rgb(22 24 27 / 18%)` in light.
`--shadow-subtle` resolves to `none`. In dark mode borders do the separating and
the overlay shadow only grounds.

### Motion

Durations `--duration-xs` 100 · `-sm` 150 · `-md` 220 · `-lg` 320 · `-xl` 450 ms;
easings `--ease-standard` `cubic-bezier(.2,0,0,1)`, `--ease-enter`
`(.22,1,.36,1)`, `--ease-exit` `(.4,0,1,1)`, `--ease-emphasized`
`(.05,.7,.1,1)`. The older `--motion-fast` / `-normal` / `-slow` names alias
sm / md / lg. Under `prefers-reduced-motion` the duration tokens themselves
collapse to ~0 (D19).

## D6. Typography

Two self-hosted families, both under the OFL, shipped as WOFF2 files with their
licence texts in `src/assets/fonts/` and declared in `src/styles/_fonts.scss`:

- **Instrument Sans** (variable `wdth` 75-100, `wght` 400-700; Latin 57 KB,
  Latin Extended 19 KB) for Latin text. Headings use the narrowed cut
  (`--heading-stretch: 86%`), weight 600-650 and −0.015em tracking.
- **IBM Plex Sans Arabic** (400, 500, 600; Arabic subset only, 43-46 KB each)
  for Arabic.

**No font service is contacted, ever.** Because the stylesheet references the
files, the build emits them as content-hashed media files, which the edge caches
for a year as immutable. Every face carries a `unicode-range`, so an English page
downloads only the Latin file, and Latin characters inside Arabic text keep
Instrument Sans. `font-display: swap` with metric-matched local fallbacks
(`size-adjust`, `ascent-override`, `descent-override`, measured against the
shipped files) means the swap moves nothing. There is no preload: Angular cannot
write a hashed media name into `index.html`, and an unhashed copy would download
the font twice.

Type steps are named for the job they do: `--text-display` (28-40px, overview
numbers), `--text-page-title` (24-30px), `--text-section-title` (18px),
`--text-card-title` (15px), `--text-body` (14px / 1.55, dense admin),
`--text-reading` (15px / 1.6, transcripts), `--text-small` (13px),
`--text-caption` (12px), `--text-numeric` (with `tabular-nums`), plus
`--text-body-strong`, `--text-label`, `--text-nav`, `--text-table`. The title
utilities set `font-stretch` after the `font` shorthand, which would reset it.

Under `:lang(ar)` the width returns to 100%, tracking to 0, nothing is uppercase,
body text is 15px / 1.7 and heading line-height at least 1.35.

Headings are reset to inherit and given size by these classes, which keeps the
heading **level** -- what a screen reader builds its outline from -- independent
of the visual size.

## D7. Light mode

"Porcelain." A warm page (`#f3f2ee`) behind white panels, so a surface reads as
raised from the tint behind it and a hairline, never from a shadow. Text runs
ink → secondary → muted (`#16181b`, `#4d525a`, `#5f646d`). Decorative hairlines
are light; the boundary of an interactive control is `--border-control`, held to
3:1. The primary action is ink with white text. The current navigation item gets
the neutral selected fill, ink text at a heavier weight and a 2px ink edge on the
start side, which mirrors in RTL.

## D8. Dark mode

"Night shift." Designed separately, not inverted. Surfaces are warm near-blacks
(`#0f1012` page, `#17191c` panels, `#1e2024` overlays), deliberately not pure
black: `#000` causes halation against light text and makes overlays float
nowhere. Text is `#f2f1ed` rather than pure white. Ink becomes porcelain: the
primary action is the lightest thing on screen, with dark text. The voices are
lifted for the dark canvas (Lagoon `#41c2b4`, Ember `#ff8d57`), and status,
borders, chart colours, skeletons and the scrim are each specified for dark.
Borders do the separating; the one overlay shadow only grounds.

### Contrast

Every foreground/background pair is checked with `contrast_check.py` (the
`design-system-tokens` skill) against WCAG 2 ratios -- **79 pairs, in light,
dark, and both again under `prefers-contrast: more`: 76 pass, 3 are
informational (decorative hairlines and the Lagoon/Ember seam), 0 fail.** Text
is at least 4.5:1, large text and the parts of controls at least 3:1. The full
tables are in the Phase 36.2 report.

Two starting values of the direction failed and were adjusted (values, never the
rule):

- light `--border-control` `#8b8e95` was 2.79:1 on `--surface-inset` → `#80838a`
- light `--success-fg` `#2e7d32` was 4.49:1 on `--success-bg` → `#2b7a30`

Lagoon and Ember are about 1.02-1.04:1 against each other, so wherever they
touch (split bar, stacked chart, voice track) a hairline of the surface keeps
them apart, and every voice is also a word.

Under `prefers-contrast: more`, muted text becomes secondary and the decorative
hairlines become real boundaries.

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

Buttons, cards, badges, form controls, tables, lists and the empty / loading /
error states are **CSS conventions**, not Angular components. Components are
reserved for primitives that carry structure or behaviour: `Icon`, `BrandMark`,
`PageHeader`, `EmptyState`, `Pagination`, `Menu`, `ConfirmDialog`, and the voice
components below. Phase 36.2 restyled every one to "Two Voices" without changing
a class name or DOM contract. Every state -- hover, pressed, focus, disabled,
busy -- is drawn, with the motion tokens.

- **Buttons** -- primary (ink) / secondary / ghost / danger / danger-ghost / link,
  in sm / default / lg, plus icon-only. Press is a 1px settle in 100ms; colour
  changes take 150ms. Loading overlays a spinner on a transparent label, so a
  busy button keeps its width.
- **Cards** -- white on porcelain with a hairline, 14px radius, no shadow;
  `.card-header` (a sentence in it wraps under the title), `.card-body`, and
  `.card-actions` as a card's last child is its footer.
- **Badges** -- status badges draw their own glyph (tick, triangle, exclamation,
  "i") beside the word on a 6px-radius chip: neutral, success, warning, danger,
  info. `.badge-plain` (no glyph) for metadata such as a channel; `.count-badge`
  (ink) for numbers. `.badge-accent` is gone: what it meant ("Human") is now the
  voice chip.
- **Voice chips** (`app-voice-chip`) -- "AI" in Lagoon or "Person" in Ember, glyph
  and word, md or sm; a chip that changes voice sweeps across in 320ms on the
  emphasized curve (the "Take over" moment). Not a status badge.
- **Forms** -- `.field` > label + control + hint + error; `.field-row`; 10px
  controls with `--border-control` boundaries; invalid by `aria-invalid`. The
  Bootstrap aliases (`.form-control`, `.form-select`, `.form-label`, `.form-hint`,
  `.form-error`) are the same rules. `.switch` (`role="switch"`, ink when on, the
  thumb mirrors in RTL), `.segmented` (radios), native checkboxes and radios in
  ink.
- **Tables** -- `.table-wrap` frame with contained scroll, caption-style headers,
  `.num` end-aligned tabular figures, `.actions` column.
- **Data list** -- `.data-list` / `.data-list-head` / `.data-row`: the pattern for
  lists whose rows are links. A table's look on a wide screen, stacked rows on a
  phone; a feature supplies only `--data-list-columns`.
- **Toolbar, pager** -- `.toolbar` / `.toolbar-end`; `.pager` drawn by the
  `Pagination` component.
- **Menus** -- `.menu`, `.menu-item`, `.menu-separator`, `.menu-label`; an
  overlay, so it casts the shadow.
- **Notices** -- `.alert-*` (danger, warning, info, success; the glyph comes from
  the template) and `.stale-notice`, the one treatment for "this changed
  somewhere else" (warning, because nothing failed).
- **Dialog and toast** -- the dialog enters in 320ms on the enter curve with its
  scrim fading in; toasts carry a glyph and a sentence, no coloured edge stripe.
- **Transcript** -- `.bubble` (customer on the start side; AI and person on the
  end side in their voice tints, the tail on the speaker's corner) and
  `.handoff-divider`, drawn only where the replying voice really changes.
- **Empty / error / skeleton** -- skeletons match the final geometry; the sweep
  stops under reduced motion.

**Voice components** (`shared/voice/`): `VoiceChip`; `VoiceSplit` (AI vs people
counts as one bar, a legend and one sentence for screen readers); `VoiceTrack`
(the real sequence of reply authors in one conversation, derived from the
messages given and nothing else; it renders nothing until somebody has replied).

**Design Lab.** `/design-lab` shows the tokens, type in Latin and Arabic, every
primitive in every state, the voice components and a sample transcript, with
light / dark and LTR / RTL toggles. It is registered only in development builds
and compiled out of production (the route's condition is `ngDevMode`, which the
production build defines as `false`).

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

- Press: a 1px settle in 100ms. Hover and colour: 150ms.
- Menus, popovers and a new transcript message settling: 220ms on the enter
  curve.
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
- **Command palette:** the dialog enters in 220ms on the enter curve (fade, 8px
  of travel, scale from 0.98) over a fading scrim, and leaves at once. The
  bell's panel enters like a menu (220ms).
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

## Deferred

Not built, and deliberately so.

| Deferred | Note |
| --- | --- |
| Localisation and RTL | No longer deferred: English and Arabic with full RTL from Phase 36.3b. The shell, shared components and shared strings are translated; page bodies follow in their own phases. See [localisation-arabic-rtl.md](localisation-arabic-rtl.md) |
| Server-persisted preferences | Theme and pin live in `localStorage`; see D22 |
| Scroll position restoration | The shell scrolls an inner element and resets it on navigation; the page, not the scroll offset, is what Back restores |
| ESLint | The project has no lint script or ESLint config, and none was added |
| Integrations | Not built. Removed from the primary navigation in Phase 33.5 rather than shown as a dead placeholder; `/integrations` answers "Page not found" |
