# Admin design system and application shell

The permanent visual and interaction foundation for the OmniAI admin application.
Phase 10 built the shell, the token layer and the primitives; no product feature
is implemented on top of them yet.

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
└── AppShell                  layout route: owns layout state
    ├── Sidebar               presentational, renders the navigation model
    ├── Topbar                drawer trigger, page context, theme control
    │   └── ThemeToggle
    └── <router-outlet>       lazily loaded feature pages
```

Every page is a child of the shell, so navigation, the top bar and the theme
control persist across navigations instead of being rebuilt each time.

The root component holds nothing. That is a deliberate guard — the root is the
easiest place in an Angular application to accumulate global state and
"temporary" markup — and it means a future sign-in screen can render outside the
shell without unpicking anything.

The shell owns exactly two pieces of state: whether the sidebar is collapsed and
whether the drawer is open. Both are genuinely shared, because the sidebar and
the top bar each need them; anything narrower would leave two components
disagreeing about one layout. It owns no feature logic and loads no data.

**Standalone components, signals, `OnPush` everywhere.** The application has no
zone.js polyfill and is therefore zoneless, so signal-driven state is not a
stylistic preference but how change detection works here. No state-management
library was added: there is one service with one value in it.

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

`--radius-sm` 6px (inputs, badges) · `--radius-md` 8px (buttons, nav items) ·
`--radius-lg` 12px (cards) · `--radius-xl` 16px (large layout surfaces) ·
`--radius-pill` (status pills, where the shape itself carries meaning).

### Shadow

`--shadow-subtle` · `--shadow-raised` · `--shadow-overlay`. Redefined for dark
mode, where shadows read poorly and borders do most of the separating.

### Motion

`--motion-fast` 120ms · `--motion-normal` 200ms · `--motion-slow` 320ms, with
`--ease-standard` and `--ease-exit`. Short enough to read as response rather
than as animation.

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

**Persistence is `localStorage`, per browser.** That is the right store today
because there are no user accounts to hang a preference on. See *Deferred*.

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
exist. It is not a feature flag; it disappears as each area ships.

## D12. Sidebar

Full height, beside the content, with the brand at the top and a collapse
control at the bottom.

The current page is marked **three ways**: a tinted background, the accent
colour, and a solid marker on the inline-start edge. Colour alone would fail
anyone who cannot separate the accent from the secondary text, and the marker is
what still reads in greyscale. `aria-current="page"` is applied alongside, so the
state is announced as well as drawn.

**Collapsed mode** (desktop, 264px → 72px) hides labels *visually* while keeping
them in the accessibility tree, and adds a `title` tooltip for sighted readers.
The preference is persisted. The active-item edge marker stays, because it is
the only remaining signal of where you are.

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

## D14. Top bar

Drawer trigger, current page name, theme control. Nothing else, because nothing
else exists.

**No avatar, no notification bell, no tenant switcher, no account menu.** Each
implies a system this product does not have. A control that looks functional and
is not is worse than an empty space: it invites a click, and it makes the
product look finished in a review while teaching the reader that its controls do
not work.

The page name is a `<p>`, not a heading — the page's own `<h1>` lives in the page
header, and two level-one headings would break the document outline.

## D15. Page layout

`.page` gives every routed view a max width of **1600px** with token gutters,
centred. Wider than Bootstrap's 1320px container on purpose: an admin product on
a 1920px screen should feel like it is using the machine it was given. Prose
blocks are separately capped at a readable measure (`68ch`, `42ch`).

`PageHeader` fixes the `<h1>` — exactly one per view, in a predictable place —
with an optional description and an optional actions slot.

## D16. Primitives

Buttons, cards, badges, form controls, tables and the empty / loading / error
states are **CSS conventions**, not Angular components. `<button class="btn
btn-primary">` needs no wrapper to be correct, and wrapping it would cost a
component instance and a re-implementation of `type`, `disabled` and `form`.

Components are reserved for primitives that carry structure or behaviour worth
naming: `Icon`, `PageHeader`, `EmptyState`.

- **Buttons** — primary / secondary / ghost / danger, with default, hover,
  active, `focus-visible`, disabled and loading-ready states. Destructive
  styling is distinct, because "delete" and "save" must never be one misread
  apart.
- **Cards** — a border plus the tinted page does the separating; the shadow is
  nearly invisible. Cards are for grouping that aids comprehension, not a
  default wrapper for every paragraph.
- **Badges** — carry a dot *and* a word. Colour alone fails colour-vision
  deficiency and greyscale.
- **Forms** — label, input, textarea, select, hint, error, disabled, focus.
  Invalid state is driven by `aria-invalid`, so the visual and the announced
  state have one source.
- **Tables** — wrapper with contained horizontal scroll, sticky header, hover
  and `aria-selected` rows. No sorting, filtering or pagination abstraction: a
  generic table framework written before a real feature is a framework written
  blind.
- **Empty / error / skeleton** — part of the system from the start, because a
  state designed after the happy path ships as a blank rectangle. No decorative
  illustrations.

## D17. Icons

**No icon package.** The application needs nineteen glyphs; every library worth
using ships hundreds. The set is authored in `shared/icon/icon-paths.ts` on a
shared 24×24 grid, outline-only, with one stroke width and round caps and joins
— so an inconsistent icon is impossible by construction rather than something
review has to police.

Glyphs are stored as **path data**, rendered via `<path [attr.d]>`. No SVG
source passes through `innerHTML` and there is no sanitiser bypass anywhere in
the icon layer.

Icons are `aria-hidden` by default: each sits beside a real label, and
announcing both makes a screen reader say everything twice.

## D18. Accessibility baseline

Treated as a product requirement, not a pass at the end.

- Semantic landmarks: `nav[aria-label="Primary"]`, `main`, `header`
- A skip link, first in the tab order, that **actually moves focus**
- `aria-current="page"` on the active navigation entry
- `aria-expanded` + `aria-controls` on both the drawer trigger and the collapse
  control
- `inert` for the closed drawer and for content behind an open one
- Visible `:focus-visible` rings everywhere, one treatment defined once
- Escape closes the drawer; focus returns to the trigger
- Labels never removed for assistive technology, only hidden visually
- Status conveyed by shape and text as well as colour
- Contrast verified numerically across both themes
- `<caption>` and `scope` on the table

The theme control is built on **native radio inputs in a fieldset**. A
`role="radio"` implementation would have to reimplement arrow-key movement,
roving tabindex and the checked state — four things browsers already do
correctly and hand-rolled versions habitually get wrong.

## D19. Motion

Small transitions on the drawer, hover states, theme surfaces and the scrim. No
entrance animations, no animated backgrounds.

`prefers-reduced-motion: reduce` is honoured by a single global rule rather than
per-component opt-in, so a component added later cannot forget it. Durations
collapse to ~0 instead of being removed, so `transitionend` still fires and
anything waiting on it keeps working.

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

## Deferred

Not built, and deliberately so.

| Deferred | Note |
| --- | --- |
| Authenticated user profile | No auth exists. No avatar or account menu was faked. |
| Permission-based navigation | `NAV_ITEMS` is static; filtering it needs a permission model. |
| Tenant switcher | Tenant membership is a backend concept with no UI contract yet. |
| Real notifications | No notification system exists. |
| Real dashboard metrics | No API layer. The overview shows empty states and one factual table. |
| Tenant management UI | Phase 11+. |
| AI agent management UI | Phase 11+. |
| Agent preview UI | Phase 11+. Backend exists; no client. |
| Conversations inbox | Later phase. |
| Knowledge / RAG UI | Later phase. |
| Channels UI | No channel provider architecture designed yet. |
| Billing | No billing system. |
| Localisation and RTL | Layout is RTL-ready; no translations, no `dir` switching. |
| Server-persisted preferences | Theme and collapse live in `localStorage`. Once users exist they belong on the profile so they follow a person between devices. `ThemeService` is the only place that changes. |
| Scroll position restoration | Angular's acts on the window; this application scrolls an inner element. The shell resets scroll on navigation instead. |
| ESLint | The project has no lint script or ESLint config, and none was added. |
| API client layer | Explicitly out of scope. No endpoint is referenced anywhere in the frontend. |
