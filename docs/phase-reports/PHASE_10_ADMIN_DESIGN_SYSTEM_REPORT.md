# Phase 10 — Professional admin design system and application shell

## 1. Phase objective

Build the permanent visual and interaction foundation for the OmniAI admin
application: semantic design tokens, light / dark / system theming, a responsive
application shell with a professional sidebar and top bar, a Bootstrap-backed
responsive content grid, reusable layout and UI primitives, and an accessibility
baseline — without implementing any product feature, calling any backend, or
introducing authentication.

## 2. Final status

**PASS.**

Production build clean. **49 of 49 tests pass** (baseline was 3). Prettier
clean. `npm audit` reports 0 vulnerabilities. Backend untouched. No API call, no
OpenAI usage, no VPS access, no commit.

Two genuine defects were found during the phase and fixed at source — one of
them by looking at the running application in a browser rather than by reading
the code. Both are described in §52.

## 3. Baseline commit

`eaa54d8b4202369da41c096c715c97dbfcb75c81` — *feat: integrate openai responses
provider* (Phase 9 checkpoint).

## 4. Initial Git state

`git status --short` was **empty**. Working tree clean, `eaa54d8` at HEAD, no
remotes configured.

## 5. Angular version

| Component | Version |
| --- | --- |
| Angular framework | 22.1.5 |
| Angular CLI | 22.1.7 |
| `@angular/build` | 22.1.7 |
| TypeScript | 6.0.2 |

Existing architectural style was continued: standalone components, signals,
strict TypeScript (`strict`, `noUnusedLocals`, `noImplicitReturns`,
`noPropertyAccessFromIndexSignature`), `strictTemplates`, and the `@app/*` path
alias.

The application has **no zone.js polyfill** and is therefore zoneless. That is
not a stylistic detail: it means signal-driven state is how change detection
works here, and every component added in this phase is
`ChangeDetectionStrategy.OnPush`.

## 6. Node / npm versions

Node **v24.19.0**, npm **11.17.0** (pinned by `packageManager`).

## 7. Packages added

| Package | Version | Scope |
| --- | --- | --- |
| `bootstrap` | 5.3.8 (exact) | dependency — grid CSS only |
| `@popperjs/core` | 2.11.8 | auto-installed peer of `bootstrap`; never imported |

Installed package count **373 → 375**.

**No other package was added.** In particular: no UI component framework, no
state-management library, no icon package, no animation library, no chart
library, no test runner, no `@types/node`, no jQuery.

## 8. Bootstrap decision and version

**Bootstrap 5.3.8, CSS only, grid build only.**

```jsonc
"styles": [
  "node_modules/bootstrap/dist/css/bootstrap-grid.min.css",
  "src/styles.scss"
]
```

**Used for:** containers, `.row`, `.col-*`, breakpoints, and the layout
utilities that ship in the same file.

**Not used for:** component styling of any kind. Reboot is not loaded, so
Bootstrap does not touch typography, buttons, cards, tables or form controls.
The product does not look like a Bootstrap admin template because none of
Bootstrap's component CSS is present in the bundle.

**No Bootstrap JavaScript. No jQuery.** The sidebar, drawer and theme control are
Angular's. `@popperjs/core` is present in `node_modules` as a peer dependency and
never reaches the bundle.

**Why the prebuilt CSS rather than the SCSS entry point.** This was measured, not
assumed: compiling `bootstrap/scss/bootstrap-grid` with the project's Sass emits
**21 `@import` deprecation warnings**. They originate in Bootstrap's own source,
so they cannot be fixed here, and suppressing them was not acceptable. Nothing is
lost by using the prebuilt file — Bootstrap 5 drives gutters through custom
properties, so the grid is bound to this product's spacing scale without
recompiling anything:

```scss
.row {
  --bs-gutter-x: var(--space-6);
  --bs-gutter-y: var(--space-6);
}
```

**Bootstrap's spacing utilities are deliberately unused.** Its spacers form a
second spacing scale; using both would put two systems in one template. An early
draft of the overview page used `mt-3` / `h-100` and was rewritten to use tokens.
(`h-100` is not in the grid build at all — caught by checking rather than
assuming.)

## 9. Design-token architecture

`src/styles/_tokens.scss` is the single source of visual truth. Two rules:
components consume semantic tokens and never raw values; tokens are named for
their **role**, not their appearance.

Groups: surfaces, text, borders, accent, status (success / warning / danger /
info), shadows, skeleton, scrim, spacing, radius, typography, motion, layout,
plus a direction multiplier for RTL.

Theming redefines the same custom properties under a different selector, so **no
component stylesheet is duplicated per theme**. The dark palette lives in a Sass
mixin because it is needed in two places — explicit selection, and the
no-JavaScript media-query fallback — and writing it once means the two cannot
drift.

## 10. Light theme

Not "white with black text". Hierarchy comes from a tinted page (`#f5f7fa`)
behind white cards, so a surface reads as raised without needing a shadow.
Borders are light but present; text runs primary → secondary → muted; the
current navigation item gets a tinted background, the accent colour and a solid
edge marker.

## 11. Dark theme

Designed, not inverted. Surfaces are desaturated blue-greys — deliberately not
pure black, which makes shadows invisible, causes halation against light text
and reads as "terminal" rather than "product". Text is `#e7ecf3`, not pure
white, for the same reason.

The accent is a **different value** (`#7b87ff` vs `#4a56e2`), not the same one
reused: the light indigo fails contrast on dark surfaces. Status colours,
borders, shadows, skeletons and the scrim are each separately specified for dark.

## 12. System theme

`system` is treated as a mode in its own right — "keep following the operating
system" — not the absence of a choice. The resolved theme is **derived**:

```ts
resolved = computed(() =>
  preference() === 'system' ? (systemPrefersDark() ? 'dark' : 'light') : preference()
);
```

Because it is derived, an OS change while `system` is selected flows through
automatically and an explicit choice ignores the OS just as automatically.
Neither has its own code path, so neither can be broken independently. The OS
preference is tracked through a live `matchMedia` listener released via
`DestroyRef`.

## 13. Theme persistence

`localStorage`, key `omniai.theme`, values `system` / `light` / `dark`.

Access goes through a `LOCAL_STORAGE` injection token that resolves to `null`
when storage is unusable — `localStorage` **throws** rather than returning null
in private modes and for opaque origins. An unrecognised stored value is treated
as absent rather than trusted, because the store is editable by hand.

A test proves theme switching still works when storage refuses every operation:
the choice applies for the session and simply does not survive a reload.

**Per-user server persistence is deferred** — there are no user accounts to hang
a preference on. `ThemeService` is the only place that would change.

## 14. Theme startup / no-flash strategy

A small inline script in `<head>` resolves the theme and stamps `data-theme`
before first paint. Without it the browser paints light, Angular boots, and a
dark-mode reader sees a white flash on **every** load.

Inline rather than an external file, because a separate request is one more round
trip during which the flash happens anyway. Wrapped in `try`/`catch`, degrading
to light.

The resolved theme — never the preference — is written to `data-theme`;
`data-theme="system"` would force every themed rule to also handle the media
query.

**The script duplicates three constants** from `core/theme/theme.ts`, because it
must run before the bundle exists. This is guarded by `theme-startup.spec.ts`,
which pins the literals so renaming a constant fails a test whose name says which
file to edit. (A stronger guard would read `index.html` directly; that needs
either a raw-file loader this test builder does not have, or `@types/node` — a
dependency this phase does not otherwise need. Recorded honestly as a weaker
guard.)

A `@media (prefers-color-scheme: dark)` fallback in the token layer covers the
JavaScript-disabled case, scoped so it can never override an explicit choice.

## 15. App shell architecture

```
App (root)                    router outlet only, deliberately empty
└── AppShell                  layout route: owns layout state
    ├── Sidebar               presentational
    ├── Topbar
    │   └── ThemeToggle
    └── <router-outlet>       lazily loaded pages
```

Every page is a child of the shell, so navigation, the top bar and the theme
control persist across navigations rather than being torn down each time.

The root component holds nothing — a deliberate guard against the root
accumulating global state, and it means a future sign-in screen can render
outside the shell without unpicking anything. A test asserts the root renders no
heading, header or nav.

The shell owns exactly two pieces of state (collapsed, drawer-open), both
genuinely shared between the sidebar and the top bar. It owns no feature logic
and loads no data.

## 16. Sidebar design

Full height beside the content, brand at top, collapse control at the bottom.
Presentational: it renders `NAV_ITEMS`, emits what the reader did, and owns no
state.

The current page is marked **three ways** — tinted background, accent colour, and
a solid inline-start edge marker — because colour alone fails anyone who cannot
separate the accent from secondary text, and the marker is what still reads in
greyscale. `aria-current="page"` is applied alongside via `routerLinkActive`, so
the state is announced as well as drawn.

## 17. Desktop collapsed behaviour

Implemented. 264px → 72px, preference persisted under
`omniai.sidebar-collapsed`.

Labels are hidden **visually only** and stay in the accessibility tree, with a
`title` tooltip for sighted readers — verified in the browser: all eight
accessible names survive collapse. The active-item edge marker is retained,
because it becomes the only remaining signal of position. The control carries
`aria-expanded` and `aria-controls`.

One label, whose text changes with state (`Collapse navigation` /
`Expand navigation`). An earlier draft added a second visually-hidden label,
which would have made a screen reader announce both; caught and fixed before
testing.

## 18. Mobile navigation

Below `lg` (992px) the sidebar leaves the grid and becomes an overlay drawer —
a different layout, not a narrower one, because a shrunken permanent sidebar
would eat a third of a phone screen forever.

- The drawer stays rendered so it can animate, so **`inert`** keeps it out of the
  tab order while off screen.
- While open, `inert` moves to the page content, making it a genuine modal
  surface: focus cannot escape behind it, and a screen reader cannot wander into
  hidden content.
- Opening moves focus into the drawer; closing returns it to the trigger.
- Closes on Escape, the close button, the scrim, and on navigation (otherwise the
  drawer covers the page the reader just asked for).
- The scrim uses `visibility` as well as `opacity`, so an invisible full-screen
  layer cannot swallow taps.

All of the above verified in a real browser, not only in tests.

## 19. Topbar

Drawer trigger, current page name, theme control. Nothing else, because nothing
else exists.

**No avatar, no notification count, no tenant switcher, no account menu.** Each
implies a system this product does not have. A control that looks functional and
is not is worse than an empty space.

The page name is a `<p>`, not a heading — the page's own `<h1>` is in the page
header, and two level-one headings would break the document outline. It truncates
with an ellipsis rather than pushing the theme control off a phone screen.

## 20. Page-layout convention

`.page` gives every routed view `max-inline-size: 1600px`, token gutters, and
auto margins. Wider than Bootstrap's 1320px container deliberately: an admin
product on a 1920px screen should feel like it is using the machine it was given.
Measured at 1920px: 1600px of content, no horizontal overflow.

Prose is separately capped at a readable measure (`68ch` descriptions, `42ch`
state text) — full-width prose on a 1600px page is genuinely harder to read.

`PageHeader` fixes the `<h1>`: exactly one per view, in a predictable place, with
an optional description and an optional actions slot that collapses when empty.

## 21. Bootstrap grid usage

Used on the overview page for the operations panels:

```html
<div class="row area-panels">
  <div class="col-12 col-md-6 col-xl-4">…</div>
</div>
```

Verified in the browser: **1 panel per row at 390px, 2 at 768px, 2 at 1024px, 3
at 1920px**. Gutters come from `--space-6` via the `--bs-gutter-*` custom
properties.

## 22. CSS Grid / Flexbox usage

| Layer | Technique |
| --- | --- |
| Application frame | CSS Grid (`grid-template-columns: <sidebar> 1fr`) |
| Page content | Bootstrap grid |
| Component internals | Flexbox |

The shell is two regions, not twelve — a fixed column beside a filling one, which
is a job for the layout engine rather than a proportional grid. `min-inline-size:
0` on the content column is what stops a wide child stretching the layout instead
of scrolling inside it.

Scrolling is confined to `.shell-main`, which is what holds the sidebar and top
bar still while the page moves.

## 23. Typography

A system font stack. **No webfont is fetched from a CDN** — a third-party
dependency on every page load, a privacy consideration, and a guaranteed flash of
unstyled text.

Steps named for their job: `--text-page-title`, `--text-section-title`,
`--text-card-title`, `--text-body`, `--text-body-strong`, `--text-small`,
`--text-label`, `--text-nav`. Body is 14px (denser and more readable in an admin
context); the page title uses `clamp()` to scale 22px → 28px.

Headings are reset to inherit and sized by class, keeping the heading **level** —
which screen readers use to build an outline — independent of visual size.

## 24. Spacing / radius / shadow system

**Spacing** — `--space-1` … `--space-10` = 4, 8, 12, 16, 20, 24, 32, 40, 48, 64px.
Ten steps, nothing between them.

**Radius** — `sm` 6px, `md` 8px, `lg` 12px, `xl` 16px, plus `pill` for status
badges where the shape itself carries meaning.

**Shadow** — `subtle`, `raised`, `overlay`; redefined for dark mode, where
shadows read poorly and borders do most of the separating. Deliberately almost
invisible on cards: stacked shadows on every panel are the fastest way to make an
interface look like a template.

## 25. Form foundation

Styling only — no Tenant or AiAgent form was built.

`.field`, `.field-label`, `.field-required`, `.field-hint`, `.field-error`,
`.input`, `.textarea`, `.select`, with hover, focus, disabled and invalid states.

The invalid state is driven by `aria-invalid`, so the visual state and the
announced state have one source of truth. Control borders use the dedicated
`--border-control` token, which meets the 3:1 non-text contrast requirement.

## 26. Button foundation

`.btn` with `primary` / `secondary` / `ghost` / `danger` variants, plus `sm` and
`icon` modifiers and a `loading`-ready state.

States: default, hover, active, `:focus-visible`, disabled (`not-allowed` cursor,
flattened colours), loading. Destructive styling is clearly distinct — "delete"
and "save" must never be one misread apart.

## 27. Card / surface foundation

`.card`, `.card-header`, `.card-body`. Separation comes from a border plus the
tinted page behind; the shadow is nearly invisible. Cards are for grouping that
aids comprehension, not a wrapper for every paragraph.

## 28. Table foundation

`.table-wrap` (contained horizontal scroll, so a wide table scrolls and the
layout does not) and `.table` with a sticky header, hover rows and an
`aria-selected` state that uses an accent edge as well as a tint.

**No sorting, filtering or pagination abstraction was built.** A generic table
framework written before a real feature is a framework written blind.

## 29. Empty / loading / error foundations

Part of the system from the start, because a state designed after the happy path
ships as a blank rectangle.

- `EmptyState` component with `neutral` and `error` tones and an actions slot
- `.alert` / `.alert-danger` / `.alert-info` for inline errors
- `.skeleton`, `.skeleton-text`, `.skeleton-title` with a slow sweep that stops
  under `prefers-reduced-motion`

No decorative illustrations: a large graphic makes an ordinary "nothing here"
feel like an error and is the first thing to look dated.

The skeleton convention is defined and documented but not used on any page —
rendering a skeleton with nothing loading would be a fake loading state. Phase 11
will use it against real requests.

## 30. Accessibility work

- Semantic landmarks: `nav[aria-label="Primary"]`, `main`, `header`
- Skip link, first in the tab order, that **actually moves focus** (see §52)
- `aria-current="page"` on the active navigation entry
- `aria-expanded` + `aria-controls` on the drawer trigger and the collapse control
- `inert` on the closed drawer and on content behind an open one
- One `:focus-visible` treatment defined once, drawn with `outline` so it follows
  border-radius and cannot be clipped
- Labels hidden visually, never removed from the accessibility tree
- Status conveyed by shape and text as well as colour (badges carry a dot and a
  word)
- `<caption>` and `scope="col"` / `scope="row"` on the table
- Icons `aria-hidden`, since each sits beside a real label

The theme control is built on **native radio inputs in a fieldset with a hidden
legend**. A `role="radio"` implementation would have to reimplement arrow-key
movement, roving tabindex, the checked state and form semantics — all things the
browser already does correctly.

### Contrast

Verified numerically against WCAG by a script reading the committed token file —
**40 pairs across both themes, all passing** (text ≥ 4.5:1, control boundaries
≥ 3:1). Three failures were found and fixed:

| Issue | Before | After |
| --- | --- | --- |
| light `--text-muted` on page | 4.46:1 | 4.80:1 |
| dark `--danger-solid` vs its foreground | 4.34:1 | 5.19:1 |
| interactive control boundary | no dedicated token | `--border-control`, ≥ 3.12:1 both themes |

## 31. Keyboard behaviour

- Skip link is the first focusable element and moves focus to `main`
- Full keyboard navigation of the sidebar; no mouse-only interaction anywhere
- Escape closes the drawer
- Focus moves into the drawer on open and returns to the trigger on close
- `inert` prevents tabbing to off-screen or behind-modal content
- Theme options are reachable by arrow keys (native radio behaviour)
- Focus rings visible in both themes — confirmed visually in the browser

## 32. Reduced-motion behaviour

A single global rule rather than per-component opt-in, so a component added later
cannot forget it:

```css
@media (prefers-reduced-motion: reduce) {
  *, *::before, *::after {
    animation-duration: 0.01ms;
    animation-iteration-count: 1;
    transition-duration: 0.01ms;
    scroll-behavior: auto;
  }
}
```

Durations collapse to ~0 rather than being removed, so `transitionend` still
fires and anything waiting on it keeps working. Confirmed present in the served
stylesheet at runtime. Navigation scroll reset uses `scrollTop` rather than
`scrollTo`, which avoids inviting a smooth behaviour reduced-motion readers do
not want.

## 33. RTL readiness

**No localisation was implemented.** Layout is written with logical properties
throughout — `margin-inline`, `padding-inline`, `inset-inline`, `border-inline`,
`text-align: start` — so adding Arabic later does not require a CSS rewrite.

`transform` cannot mirror on its own, so the drawer takes its direction from a
token that flips with `dir`:

```scss
:root { --direction-flip: 1; }
[dir='rtl'] { --direction-flip: -1; }
transform: translateX(calc(var(--direction-flip) * -100%));
```

## 34. Overview page

**Everything on it is true.** No metrics, no counts, no revenue, no resolution
rates, no customers — none of those exist, and this application has no API layer.

- Page header: "Platform overview", with a description stating plainly that
  nothing below reports live data yet
- An "Operations" section: three responsive-grid panels (AI Agents,
  Conversations, Channels), each a card with a "Not implemented" badge and an
  **empty state**, plus a real link to that area
- A "Product areas" table rendered from `NAV_ITEMS` — the one genuinely factual
  thing the frontend knows: which areas are built and which are not. Because it
  is rendered from the navigation model, it cannot claim an area exists when it
  does not.

The page exercises page header, responsive grid, cards, badges, empty states,
buttons and the table foundation in one view.

## 35. Future feature placeholders

Seven areas (AI Agents, Conversations, Knowledge, Channels, Integrations,
Analytics, Settings) route to a single reusable `Placeholder` component that
states plainly that the area is not built, and offers a route back.

Its routes are **generated from `NAV_ITEMS`**, which is what makes a dead
navigation link structurally impossible rather than merely unlikely. A test walks
every entry and asserts each resolves to a page with a heading and is not the
404.

The area name arrives via route `data` through `withComponentInputBinding()`, so
it is written in exactly one place.

Unknown addresses render a **404 inside the shell** — no redirect. Bouncing to
the overview would leave the reader unsure whether their link was wrong or the
product ignored it, and would hide broken links from whoever should fix them.

## 36. Responsive verification

Verified in Chrome via device emulation (window resizing was ineffective on this
machine, so CDP viewport emulation was used).

| Width | Layout | Panels/row | Horizontal overflow |
| --- | --- | --- | --- |
| 390 | drawer, sidebar `inert` | 1 | none (`scrollWidth` 390) |
| 768 | drawer, theme labels visible | 2 | none |
| 1024 | permanent sidebar | 2 | none |
| 1440 | permanent sidebar | 3 | none |
| 1920 | permanent sidebar, content capped 1600px | 3 | none |

An "elements extending past the viewport" sweep returned **zero offenders** at
every width. Mobile is a genuinely different layout, not a shrunken desktop.

## 37. Browser visual verification

**Performed.** Chrome DevTools was available and was used against the running dev
server (`localhost:4310`, stopped afterwards).

Verified: light and dark at 390 / 768 / 1024 / 1440 / 1920; no horizontal
overflow at any width; drawer open/close with `aria-expanded`, `inert` on both
sides, focus entry and focus return; Escape close; collapsed sidebar retaining
all accessible names, tooltips and the active marker; theme switching applying
`data-theme` and repainting; `System` correctly following the host OS (which
prefers dark); the placeholder and 404 pages; the accessibility tree
(landmarks, heading levels, radio group, `aria-current`); token resolution at
runtime; the reduced-motion and no-JS dark media rules present in the served CSS.

**Console: zero errors and zero warnings.**

No screenshots were committed to the repository.

## 38. Tests before

**3** tests, 1 file.

## 39. Tests after

**49** tests, 5 files, all passing.

| File | Tests | Covers |
| --- | --- | --- |
| `theme-service.spec.ts` | 14 | system default, light, dark, OS change while system, explicit choice ignoring later OS changes, returning to system, persistence, restore, invalid stored value, hostile storage, `data-theme` application |
| `app-shell.spec.ts` | 24 | landmarks, skip link + focus movement, nav rendering, root redirect, every nav route resolving, `aria-current`, page title, desktop vs drawer, collapse + label retention, drawer open/close/Escape/navigate, `inert` both sides, focus entry and return, breakpoint crossing |
| `theme-toggle.spec.ts` | 7 | three modes, fieldset/legend grouping, visible text labels, default, apply + persist, checked state |
| `theme-startup.spec.ts` | 3 | the `index.html` duplication contract |
| `app.spec.ts` | 2 | root stays empty |

Assertions are on semantics — landmarks, roles, `aria-*`, `inert`, focus — never
on pixel values or class names a restyle would legitimately change.

Two test doubles were required because jsdom cannot provide them: `matchMedia`
does not exist in jsdom **at all**, and `localStorage` can throw. Both are
injected through tokens, which is also what lets tests drive the OS theme and the
viewport width deterministically.

## 40. Production build

Clean. **Zero errors, zero warnings.** Lazy chunks emitted per feature route.

## 41. Bundle size before

| Chunk | Raw | Transfer |
| --- | --- | --- |
| main | 190.80 kB | 52.18 kB |
| styles | 0 bytes | 0 bytes |
| **Initial total** | **190.80 kB** | **52.18 kB** |

## 42. Bundle size after

| Chunk | Raw | Transfer |
| --- | --- | --- |
| main | 257.17 kB | 69.78 kB |
| styles | 65.63 kB | 5.45 kB |
| **Initial total** | **322.80 kB** | **75.23 kB** |
| overview (lazy) | 3.64 kB | 1.40 kB |
| shared (lazy) | 2.56 kB | 925 B |
| placeholder (lazy) | 1.19 kB | 592 B |
| not-found (lazy) | 942 B | 942 B |

**Delta: +131.99 kB raw, +23.05 kB transfer.** Well inside the 500 kB warning
budget.

The whole styles bundle — Bootstrap's grid plus the entire design system — is
**5.45 kB transferred**. The main-bundle growth is the router, the shell and the
theme service. Feature pages are lazily loaded and contribute nothing to the
initial download.

## 43. Lint

**Not run — no lint script exists.** `package.json` defines no `lint` script and
the project contains no ESLint configuration or dependency. None was added:
introducing a linter is a separate decision with its own configuration and
baseline, and adding it silently inside a design-system phase would be scope
creep.

Quality was enforced instead by TypeScript in strict mode with
`strictTemplates`, which the build runs and which passes with zero warnings. **No
diagnostic was suppressed anywhere in this phase.** Recommended as a candidate
for a future phase.

## 44. Formatting

`npm run format:check` — **clean**, all files match Prettier style. Six files
were reformatted by `npm run format` during the phase; the suite was re-run
afterwards and still passes.

## 45. npm audit

**0 vulnerabilities**, before and after. `npm audit fix` was not run.

## 46. Backend modified

**NO.** `git status --porcelain -- backend` is empty.
`backend/Directory.Packages.props` is untouched. No `.cs`, `.csproj`, `.slnx`,
migration or backend document was changed. No backend build or test was run.

## 47. OpenAI / API calls

**NO.**

- No HTTP client: `provideHttpClient` is not used and `HttpClient` is not
  imported anywhere
- No `fetch`, `XMLHttpRequest` or `axios` in `frontend/src`
- No `/api/` path or backend URL referenced anywhere in the frontend
- The string `openai` does not appear anywhere in `frontend/src` or
  `frontend/package.json`
- `OPENAI_API_KEY` was neither read nor required

Verified by grep across the whole frontend source tree.

## 48. Files created / modified

**38 created, 13 modified, 1 deleted — 52 total.**

Created (frontend): `styles/_tokens.scss`, `_base.scss`, `_primitives.scss`;
`core/media/media.ts`, `core/storage/local-storage.ts`, `core/theme/theme.ts`,
`theme-service.ts`, `core/title/app-title-strategy.ts`;
`layout/navigation.ts`, `layout/app-shell/*`, `layout/sidebar/*`,
`layout/topbar/*`, `layout/theme-toggle/*`; `shared/icon/*`,
`shared/page-header/*`, `shared/empty-state/empty-state.ts`;
`features/overview/*`, `features/placeholder/placeholder.ts`,
`features/not-found/not-found.ts`; `testing/test-doubles.ts`; 4 spec files.

Created (docs): `docs/architecture/admin-design-system.md`, and this report.

Modified: `angular.json`, `package.json`, `package-lock.json`,
`tsconfig.app.json`, `tsconfig.spec.json`, `src/index.html`, `src/styles.scss`,
`src/app/app.ts`, `app.html`, `app.config.ts`, `app.routes.ts`, `app.spec.ts`,
`docs/architecture/README.md`.

Deleted: `src/app/app.scss` — the root component no longer renders anything, so
its stylesheet had no content left to hold.

## 49. Architectural decisions

Recorded in full in
[docs/architecture/admin-design-system.md](../architecture/admin-design-system.md)
as D1–D20. In brief:

1. Hierarchy from surface and space, not decoration; one accent, used sparingly
2. Thin root; shell as a layout route owning only shared layout state
3. Bootstrap for grid structure only, prebuilt CSS, no JS, no Reboot, no spacers
4. CSS Grid for the frame, Bootstrap for page content, Flexbox inside components
5. Role-named semantic tokens; theming by redefinition, never duplication
6. System font stack; no CDN webfont
7. Light and dark both designed; dark gets its own accent and its own status set
8. Resolved theme derived, not stored; `data-theme` carries light/dark only
9. Inline pre-paint theme script, with its duplication guarded by a test
10. Navigation model as the single source for both sidebar and routes
11. `inert` for drawer and modal semantics, rather than a hand-rolled focus trap
12. Native radios for the theme control instead of `role="radio"`
13. Primitives as CSS conventions; components only where structure or behaviour
    justifies one
14. No icon package for nineteen glyphs; path-data-only rendering, no `innerHTML`
15. Logical properties throughout, with a direction token for the one transform
16. Honesty over polish: no fake metrics, no fake identity, no dead links

## 50. Deferred concerns

Authenticated user profile · permission-based navigation · tenant switcher · real
notifications · real dashboard metrics · Tenant management UI · AiAgent
management UI · Agent Preview UI · Conversations inbox · Knowledge/RAG UI ·
Channels UI · billing · localisation and RTL translations · server-persisted user
preferences · API client layer · ESLint · scroll-position restoration.

Each is listed with its reasoning in the architecture document.

## 51. Deviations

1. **`npm run lint` was not run** — no lint script and no ESLint config exist in
   this project. Reported rather than papered over by installing a linter (§43).
2. **Prebuilt Bootstrap CSS instead of the SCSS entry point** — driven by the
   measured 21 deprecation warnings the SCSS emits (§8). The authorised surface
   (grid, containers, layout utilities) is unchanged.
3. **Bootstrap spacing utilities not adopted** — they would introduce a second
   spacing scale alongside the tokens.
4. **`index.html` coupling guard is weaker than intended** — pins literals rather
   than reading the HTML, because the stronger form needs a dependency this phase
   does not otherwise justify (§14).
5. **A `TitleStrategy` was added** — not requested, but Angular's default would
   have set the browser tab to just "Overview", losing the product name that
   `index.html` previously provided. Fifteen lines to avoid a regression this
   phase would otherwise have introduced.
6. **Window resizing was unavailable during visual verification** — CDP device
   emulation was used instead, which exercises the same media queries.

## 52. Known issues

None outstanding. Two defects were found **and fixed** during the phase, both
worth recording:

**The skip link did not move focus.** Found by clicking it in a real browser and
inspecting `document.activeElement`, which was `BODY`. With `<base href="/">` an
`href="#main-content"` anchor resolves against the base rather than the current
page, turning it into a router navigation, and focus was never moved. The link
scrolled and did nothing else — useless to exactly the keyboard users it exists
for. Fixed by handling the click and focusing the main region explicitly; covered
by a regression test and re-verified in the browser (URL unchanged, focus on
`main`, first in tab order). **Tests alone would not have caught this** — the
original test only asserted the `href`.

**Navigation scroll reset crashed change detection under jsdom.** `Element.scrollTo`
is not implemented there, and the effect threw on every shell test. Fixed at
source with `scrollTop = 0`, which is universally supported, instant, and avoids
inviting a smooth behaviour reduced-motion readers do not want.

Three contrast failures and one duplicate-accessible-name bug were also caught
and fixed before completion (§30, §17).

## 53. Blockers

**None.**

## 54. `git status --short`

```
 M docs/architecture/README.md
 M frontend/angular.json
 M frontend/package-lock.json
 M frontend/package.json
 M frontend/src/app/app.config.ts
 M frontend/src/app/app.html
 M frontend/src/app/app.routes.ts
 D frontend/src/app/app.scss
 M frontend/src/app/app.spec.ts
 M frontend/src/app/app.ts
 M frontend/src/index.html
 M frontend/src/styles.scss
 M frontend/tsconfig.app.json
 M frontend/tsconfig.spec.json
?? docs/architecture/admin-design-system.md
?? docs/phase-reports/PHASE_10_ADMIN_DESIGN_SYSTEM_REPORT.md
?? frontend/src/app/core/
?? frontend/src/app/features/
?? frontend/src/app/layout/
?? frontend/src/app/shared/
?? frontend/src/app/testing/
?? frontend/src/styles/
```

Nothing under `backend/` appears. `git diff --check` reports no whitespace
errors.

## 55. `git diff --stat`

```
 docs/architecture/README.md    | 10 +++++---
 frontend/angular.json          |  2 +-
 frontend/package-lock.json     | 31 ++++++++++++++++++++++++
 frontend/package.json          |  1 +
 frontend/src/app/app.config.ts | 20 ++++++++++++++--
 frontend/src/app/app.html      | 16 +++++--------
 frontend/src/app/app.routes.ts | 54 +++++++++++++++++++++++++++++++++++++++++-
 frontend/src/app/app.scss      | 21 ----------------
 frontend/src/app/app.spec.ts   | 37 ++++++++++++++++-------------
 frontend/src/app/app.ts        | 21 +++++++++-------
 frontend/src/index.html        | 36 ++++++++++++++++++++++++++++
 frontend/src/styles.scss       | 38 ++++++++++++++++++++++++++++-
 frontend/tsconfig.app.json     |  2 +-
 frontend/tsconfig.spec.json    |  2 +-
 14 files changed, 225 insertions(+), 66 deletions(-)
```

(Tracked files only; the 38 new files are untracked and not counted above.)

## 56. Secret review

**Clean.** Every created and modified file was scanned for `sk-`-shaped strings,
`OPENAI_API_KEY`, API keys, passwords, bearer tokens, SSH credentials, the
production server address and database credentials.

The only matches were the npm package name `@inquirer/password` inside
`package-lock.json` — a pre-existing transitive dependency, not a credential.

No environment file, no `.env`, no credential, no connection string, no API URL.
No generated build output (`dist/`, `.angular/`, `node_modules/` are all
gitignored and none appear in the status). No screenshots or image files were
added.

## 57. VPS not accessed

**Confirmed.** No SSH connection was attempted or made to the production server.
No deployment, no Nginx configuration, no remote command of any kind. The only
network activity in this phase was `npm` package installation from the public
registry and a local dev server on `localhost:4310`, which was stopped after
visual verification.

## 58. Readiness recommendation for Phase 11

**Ready.**

Phase 11 can build Tenant and AiAgent management on this foundation and should
find the following already in place:

- A shell that hosts any routed page without modification
- `PageHeader`, `EmptyState`, and CSS conventions for buttons, cards, badges,
  form controls, tables and error states
- Skeleton conventions ready for their first real loading state
- A responsive grid, verified from 390px to 1920px
- Light, dark and system theming that needs no per-component work
- An accessibility baseline — landmarks, focus management, contrast — that new
  pages inherit rather than re-establish

Two things Phase 11 will need to add, neither of which exists yet:

1. **An API client layer.** Deliberately out of scope here; no endpoint is
   referenced anywhere in the frontend. It needs its own decisions on error
   mapping, cancellation and the `ETag` / `If-Match` concurrency contract the
   backend already enforces (Phase 7).
2. **A route that is a real feature.** Adding one means setting `implemented:
   true` in `NAV_ITEMS` and pointing the route at a real component; the
   placeholder disappears for that area automatically.

Worth considering separately: adding ESLint, and moving theme and sidebar
preferences to the server once authentication exists.
