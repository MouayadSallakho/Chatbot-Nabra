# Design brief (MASTER.md): "Two Voices"

> The single source of truth for Nabra's visual and interaction decisions (OmniAI is the
> internal code name; see section 0).
> Read it before any UI work. Change it only by adding a row to the decisions log (section 12).
> Page-level exceptions live in `docs/design/pages/<page>.md` and must name the rule they override.

- Status: approved direction, implemented at the foundation (Phase 36.2)
- Last updated: 2026-10-18 (brand step: the public name is Nabra / نبرة)
- Started from: the "Two Voices" direction approved for the Phase 36 redesign (reference
  `OmniAI-UI-Pack/reference/omniai-directions.html`, "Two Voices" tab)
- Implementation: `frontend/src/styles/_tokens.scss` (values), `_fonts.scss`, `_base.scss`,
  `_primitives.scss`, `src/app/shared/voice/`, `src/app/shared/brand-mark/`. The development-only
  Design Lab (`/design-lab`) shows all of it on one page.

## 0. Name

- **Nabra** in English, **نبرة** in Arabic. The only name people who use the product ever see or
  hear: the wordmark, tab titles, sentences, accessible names, the loading state.
- Meaning: نبرة is "tone of voice". Every conversation has two voices, the AI and the team; Nabra is
  the one tone they speak in. The name says what the mark draws.
- Arabic form: نبرة, written in Arabic in Arabic copy and never transliterated there. Inside a
  sentence it takes the grammar of a feminine noun ("نبرة لا تستجيب", "الانتقال إلى نبرة",
  "تسجيل الدخول إلى نبرة"). In English copy it is "Nabra", never "NABRA" or "nabra".
- Tagline: "Two voices. One tone." / «صوتان… بنبرة واحدة».
- Wordmark: "Nabra" in Instrument Sans, narrowed like every title (`--heading-stretch`, tight
  tracking); "نبرة" in IBM Plex Sans Arabic at full width with no tracking (the same tokens under
  `:lang(ar)`). Always beside the mark, in the page's language: sidebar, sign-in, registration,
  invitation, workspace chooser, loading state. No other font files.
- Tab titles: "<page> · Nabra" and "<page> · نبرة" (`AppTitleStrategy`).
- **"OmniAI" is never shown to users.** It is the internal code name only: the repository,
  namespaces, folders, CSS classes, `data-testid`s, i18n message IDs, `localStorage` and cookie
  keys, protocol strings and headers, the database, Docker and environment variables. Copy
  always says Nabra or نبرة; the source of the name in code is `src/app/core/brand/brand.ts`
  (message `@@brand.name`).

## 1. Product and audience

- Product type: multi-tenant admin for AI customer conversations (website chat, WhatsApp,
  Instagram, Messenger), plus the customer-facing hosted chat and embeddable widget.
- Audience: workspace owners setting up AI agents; support staff and supervisors who watch the
  inbox and take conversations over during long shifts; analysts reading reports.
- Job to be done: "See who is answering my customers right now, the AI or one of us, and step
  in the moment a person is needed."
- Languages and directions: English (LTR) and Arabic (RTL, from Phase 36.3). Every screen is
  designed for both.
- Primary devices: desktop at 1280-1600 px for the admin; phones at 360-430 px for supervisors
  on the move and for every customer-facing surface.
- Brand attributes: calm, precise, trustworthy, quietly premium, legible for eight hours.
- Not this: playful, "magical AI", neon tech, decorative, editorial-retro.

## 2. Design direction

- Concept: every Nabra conversation is a duet between the AI and the team. The interface gives
  each voice one colour and shows the handoff between them everywhere. Colour is information,
  not decoration; everything else is calm ink on porcelain.
- Principles:
  1. The two voice colours mean only "the AI" (Lagoon) and "a person on the team" (Ember).
     Never decorative, never status.
  2. Ink is the action colour: primary buttons are ink on light and porcelain on dark. Links are
     ink and always underlined.
  3. Calm surfaces, crisp hairlines, depth only for overlays.
  4. Type with character: Instrument Sans narrowed for headings, IBM Plex Sans Arabic for
     Arabic, tabular figures for numbers.
  5. The handoff is the hero moment: chip, track, split bar and the "Take over" transition.
  6. Real data only. If the API cannot tell it, the design does not show it.
- Alternatives considered (Phase 36 direction study): the generic indigo admin it replaces
  (rejected: the AI-default look, and colour said nothing), and the other directions in the
  reference pack.

### Signature elements

| # | Element | Where | Rule |
|---|---|---|---|
| 1 | Voice chip (`app-voice-chip`) | conversation rows, headers, legends | "AI" in Lagoon or "Person" in Ember, always glyph + word |
| 2 | Voice split bar (`app-voice-split`) | Overview "Right now", Analytics | AI vs people from API counts only; legend + one screen-reader sentence |
| 3 | Voice track (`app-voice-track`) | conversation detail header | 4 px; the real sequence of reply authors; renders nothing it cannot derive |
| 4 | Handoff divider (`.handoff-divider`) | transcripts | only where the replying voice changes, from real message authors or real handoff events |
| 5 | The "Take over" moment | conversation header chip | the chip sweeps Lagoon → Ember in 320 ms (emphasized); "Return to AI" reverses; instant under reduced motion |
| 6 | The mark (`app-brand-mark`) | sidebar, sign-in, favicon, loading state | two speech shapes, two voices in one tone (no longer the letter "O" of the code name; geometry unchanged); works at 16 px and in one colour; never mirrored |

One signature per page: a page leads with at most one of 2, 3 or 5; chips are vocabulary, not
a signature.

## 3. Colour roles

All values are tokens in `_tokens.scss`; components never use raw hex. Dark ("night shift") is
designed separately, not inverted.

| Role | Light ("porcelain") | Dark ("night shift") | Use |
|---|---|---|---|
| `--surface-page` | `#f3f2ee` | `#0f1012` | the page behind panels |
| `--surface-default` / `--surface-raised` | `#ffffff` / `#ffffff` | `#17191c` / `#1e2024` | panels, cards; overlays |
| `--surface-muted` / `--surface-inset` | `#f7f6f3` / `#eeede8` | `#1c1e22` / `#0b0c0e` | quiet areas; wells, tracks |
| `--surface-hover` / `--surface-selected` | `#f0efea` / `#e9e8e2` | `#202328` / `#23262b` | hover; selection (neutral) |
| `--text-primary` / `-secondary` / `-muted` | `#16181b` / `#4d525a` / `#5f646d` | `#f2f1ed` / `#b6b9bf` / `#8e929a` | text |
| `--border-subtle` / `-default` / `-strong` | `#e7e5df` / `#dedbd4` / `#cbc8bf` | `#24272c` / `#2c3036` / `#3a3f46` | decorative hairlines |
| `--border-control` | `#80838a` | `#6b717a` | boundaries of controls (3:1) |
| `--action` / `-hover` / `-pressed` / `--on-action` | `#16181b` / `#2b2e33` / `#000000` / `#ffffff` | `#f2f1ed` / `#ffffff` / `#d9d8d3` / `#0f1012` | primary action (ink) |
| `--link`, `--focus-ring` | `#16181b` | `#f2f1ed` | links (underlined), focus ring |
| `--voice-ai-fg` / `-strong` / `-tint` / `-border` | `#0e7c72` / `#0a5e57` / `#e2f2ef` / `#bfe3dd` | `#41c2b4` / `#7ad9ce` / `#0f2a27` / `#1c4a44` | Lagoon: the AI, only |
| `--voice-human-fg` / `-strong` / `-tint` / `-border` | `#c2410c` / `#9a330a` / `#fbe9df` / `#f4cdb8` | `#ff8d57` / `#ffb088` / `#33190c` / `#5a2c16` | Ember: a person, only |
| `--success-*` fg / bg / border | `#2b7a30` / `#e8f3e8` / `#c3e1c4` | `#6bd68a` / `#10251a` / `#1f4a2e` | status |
| `--warning-*` fg / bg / border | `#8a5a00` / `#fdf3d7` / `#f0d99a` | `#f2c94c` / `#2a220b` / `#4d3e12` | status: yellow amber, never orange |
| `--danger-*` fg / bg / border / solid | `#b42318` / `#fdecea` / `#f5c2bd` / `#c0392c` | `#ff8a80` / `#2d1513` / `#57261f` / `#f07a6e` | status: crimson |
| `--info-*` fg / bg / border | `#1f5fad` / `#e7f0fb` / `#c3d8f3` | `#7db7ff` / `#0d1f33` / `#1c3a5c` | status |
| `--chart-ai` / `--chart-human` | the voices' `fg` | the voices' `fg` | AI and people series |
| `--chart-customer` / `--chart-neutral` | `#5b6472` / `#4d525a` | `#a3aab5` / `#b6b9bf` | customers; single-series charts |
| `--chart-ramp-1…5` | `#e2f2ef` → `#0a5e57` | `#0f2a27` → `#7ad9ce` | sequential: Lagoon tint to Lagoon strong |

Elevation: hairlines everywhere; one shadow, on overlays only:
`0 1px 2px rgb(22 24 27 / 5%), 0 12px 32px -8px rgb(22 24 27 / 18%)`. In dark mode, borders do
the separating.

Contrast evidence (WCAG 2 ratios, `contrast_check.py`, 2026-10-18; 79 pairs in each of light,
dark, light + `prefers-contrast: more`, dark + more; 76 pass, 3 informational, 0 fail; full
tables in the Phase 36.2 report):

| Pair | Light | Dark | Needs |
|---|---|---|---|
| muted text on page | 5.31 | 6.09 | 4.5 |
| AI chip text (strong on tint) | 6.60 | 9.15 | 4.5 |
| Person chip text (strong on tint) | 6.24 | 9.19 | 4.5 |
| Lagoon as text on page | 4.52 | 8.70 | 4.5 |
| Ember as text on page | 4.62 | 8.33 | 4.5 |
| Lagoon / Ember bar on inset | 4.32 / 4.41 | 8.94 / 8.57 | 3 |
| warning text on its background | 5.35 | 9.93 | 4.5 |
| danger button label | 5.43 | 6.99 | 4.5 |
| control border on page | 3.38 | 3.86 | 3 |
| focus ring on page | 15.87 | 16.84 | 3 |

Rules: about 85/10/5 (neutral surfaces, ink, voices and status). Status colours only for status,
always with their own icon and word. Voices always with their glyph and word. Lagoon and Ember
sit at 1.02-1.04:1 to each other, so adjacent voice areas are separated by a hairline of the
surface, never by colour alone. Charts never use a rainbow.

## 4. Typography

Self-hosted from `src/assets/fonts/` (OFL texts beside the files); no font service is ever
contacted. Each family has a metric-matched local fallback (`size-adjust`, `ascent-override`)
so the swap shifts nothing.

| Role | Token / class | Latin (Instrument Sans) | Arabic (IBM Plex Sans Arabic) |
|---|---|---|---|
| Display (overview numbers) | `--text-display` / `.text-display` | 600, `clamp(28px, …, 40px)` / 1.1, width 86%, tabular | same size, width 100% |
| Page title | `--text-page-title` | 620, `clamp(24px, …, 30px)` / 1.2, width 86%, −0.015em | 600, / 1.4, tracking 0 |
| Section title | `--text-section-title` | 600, 18px / 1.35, width 86% | 600, 18px / 1.45 |
| Card title | `--text-card-title` | 600, 15px / 1.4 | 600, 15px / 1.5 |
| Body (dense admin) | `--text-body` | 400, 14px / 1.55 | 400, 15px / 1.7 |
| Reading (transcripts) | `--text-reading` | 400, 15px / 1.6 | 400, 15px / 1.75 |
| Small | `--text-small` | 400, 13px / 1.5 | 400, 14px / 1.65 |
| Caption | `--text-caption` | 500, 12px / 1.35 | 500, 13px / 1.5 |
| Numeric | `--text-numeric` + `tabular-nums` | 550, 14px / 1.3 | same |

- Files: Instrument Sans variable (`wdth` 75-100, `wght` 400-700), Latin 57 KB + Latin Extended
  19 KB; IBM Plex Sans Arabic 400/500/600, Arabic subset only, 43-46 KB each. Every face has a
  `unicode-range`: an English page downloads only the 57 KB Latin file, and Latin characters
  inside Arabic text keep Instrument Sans.
- Numerals policy: Western digits (0-9) in both languages, tabular in tables, metrics and
  meters. From Phase 36.3, Arabic dates and numbers are formatted with an explicit
  `numberingSystem: 'latn'` (and a Gregorian calendar), never left to runtime locale defaults.
- Arabic rules (`:lang(ar)`): letter-spacing 0, no narrowed width, no uppercase, no italics;
  heading line-height at least 1.35; emphasis by weight or colour. Mixed-direction strings use
  `dir="auto"` or `<bdi>`; emails, URLs, codes and IDs are isolated `dir="ltr"`.
- Loading: `font-display: swap`; no preload (Angular cannot write a hashed media name into
  `index.html`, and an unhashed preload would download the font twice). Font files are emitted
  with content-hashed names and cached by the edge for a year.

## 5. Layout, spacing and density

- Spacing: 4 px scale, `--space-1…10` = 4, 8, 12, 16, 20, 24, 32, 40, 48, 64. Nothing in
  between.
- Density: admin compact-comfortable. Controls 32 / 36 / 40 px (`--control-height-sm`, default,
  `-lg`), raised to 40-44 px under `pointer: coarse`. Targets never under 24 px.
- Page frame: content capped at 1600 px; gutter 24 px, 16 px under 576 px.
- Patterns: page header + sections (not card soup); `.data-list` for record lists whose rows
  are links; `.table` for real tabular data; one pager pattern with the page in the URL.

## 6. Shape, depth, imagery and icons

- Radius by role: 6 px badges, 10 px controls, 14 px cards and panels, 20 px dialogs and
  drawers, 18 px bubbles with a 6 px "tail" corner on the speaker's side (logical corners, so it
  mirrors in RTL). Pills only for counts and chips.
- Nested radius: inner = outer − padding.
- Icons: one authored 24 px outline set (`app-icon`), stroke 1.6 with round caps and joins.
  Voice glyphs `ai` (a calm bot face, never a sparkle) and `person`. Channel glyphs are generic
  (`website`, `whatsapp` as a phone in a bubble, `instagram` as a camera, `messenger` as two
  bubbles) and always shown with the channel's name; no recoloured brand logos. Only directional
  icons mirror in RTL; the mark never does.
- Imagery: none in the admin. No illustrations in empty states.

## 7. Motion

- Stance: functional and quick. Response, not animation.
- Durations: xs 100, sm 150, md 220, lg 320, xl 450 ms (`--duration-*`).
- Easing: standard `cubic-bezier(.2,0,0,1)`, enter `(.22,1,.36,1)`, exit `(.4,0,1,1)`,
  emphasized `(.05,.7,.1,1)`.
- Where: press 100 ms (1 px settle); hover and colour 150 ms; menus and a new message settling
  220 ms; dialogs and drawers 320 ms; the "Take over" sweep 320 ms emphasized; route changes a
  150 ms crossfade of the content area only, through View Transitions, skipped on first load and
  instant for a query-only change (a list's next page); the command palette 220 ms on the enter
  curve.
- Not in the admin: count-ups, stagger on every visit, parallax, fade-up on sections, bounce.
- Reduced motion: the duration tokens collapse to ~0 at the source, so every component follows;
  View Transitions and keyframe animations stop; state changes stay visible.

## 8. Content and voice

- Plain, specific, calm. Say what happened and what is safe ("Nothing was lost").
- Buttons are verb + object ("Save changes", "Return to AI"); Arabic buttons prefer verbal
  nouns (حفظ, إرسال).
- The voices are named "AI" and "Person"; in Arabic الذكاء الاصطناعي and الفريق (approved
  36.3a). Arabic terms follow `frontend/src/locale/GLOSSARY.md`.
- Empty states say what belongs here and offer one next action.
- The customer-facing honesty line stays: answers are generated by AI.

## 9. Accessibility and RTL commitments

- WCAG 2.2 AA: text 4.5:1 (large 3:1), UI parts 3:1, proven per pair in both themes; targets at
  least 24 px (44 px on touch); visible focus (2 px ink ring, 2 px offset) never hidden; no
  colour-only meaning; reduced motion honoured; forced colours supported (outline focus, the
  mark and voice bars fall back to system colours).
- `lang` and `dir` on `html` per locale, set before first paint; logical CSS only; transforms
  that must mirror use `--direction-flip`; directional icons mirror through `MIRRORED_ICONS`
  (never at the use site); user content `dir="auto"` or `<bdi>`; emails, URLs, codes, IDs and
  numeric ranges are `dir="ltr"` islands. Component styles reach `[dir]` only through
  `:host-context([dir='rtl'])` (a plain `[dir='rtl']` selector never matches under emulated
  encapsulation). Full rules: `docs/architecture/localisation-arabic-rtl.md`.

## 10. Rules this project enforces

1. Lagoon means the AI and Ember means a person. Nothing else uses either colour.
2. Ink is the action colour. One primary (ink) action per view.
3. Links are ink and underlined.
4. Selection is neutral (`--surface-selected`), plus a 2 px ink edge (`.edge-marker`) for the
   current nav item and the command palette's highlighted option.
5. Shadows only on overlays (menus, dialogs, toasts, the peeking sidebar).
6. Every status carries its own icon and word; every voice its glyph and word.
7. Voice displays (chip, split, track, divider) are drawn only from real API data.
8. No CDN, font service or third-party request from the application.
9. Components use semantic tokens only; shared rules go to the global layers, not budgets.
10. Sample content exists only in the development-only Design Lab, labelled as sample.

## 11. Avoid in this project (as of 2026-10)

Purple or indigo; decorative gradients; glow; glass; sparkle icons for "AI"; emoji icons;
Inter by default; same-size card mosaics with no purpose; cream + serif + terracotta;
near-black + acid accent; fade-up on every section; count-ups; stagger on every visit; cursor
effects; splash screens; parallax; recoloured brand logos; status shown by colour alone; Ember
used for warnings, or amber that reads orange.

## 12. Decisions log

| Date | Decision | Reason | Owner |
|---|---|---|---|
| 2026-10-18 | Adopt the "Two Voices" direction for the whole product | Approved Phase 36 direction: the duet of AI and team is the product's core fact | Product owner |
| 2026-10-18 | Fonts self-hosted as WOFF2 + OFL in `src/assets/fonts/` (Option B), no npm package, no preload | Approved at the 36.2 font gate; build hashes the files; no new dependency | Product owner |
| 2026-10-18 | `--border-control` light `#8b8e95` → `#80838a` | Starting value was 2.79:1 on `--surface-inset`; the 3:1 rule stays | 36.2 |
| 2026-10-18 | `--success-fg` light `#2e7d32` → `#2b7a30` | Starting value was 4.49:1 on `--success-bg` | 36.2 |
| 2026-10-18 | Dark `--surface-raised` `#1e2024`, `--action-pressed` `#d9d8d3`, `--danger-solid` `#f07a6e` with a dark label | Not specified in the direction; overlays sit a step above the page; a light danger fill needs a dark label to pass | 36.2 |
| 2026-10-18 | Voice areas next to each other are separated by a 1-2 px surface hairline | Lagoon and Ember are 1.02-1.04:1 to each other | 36.2 |
| 2026-10-18 | Western digits in Arabic | One digit system across both languages, tabular everywhere | 36.2; confirmed by the product owner in 36.3a (`numberingSystem: 'latn'`, `calendar: 'gregory'`) |
| 2026-10-18 | The Design Lab route is compiled out of production builds (`ngDevMode` condition) | `isDevMode()` alone would still emit the lazy chunk | 36.2 |
| 2026-10-18 | Route crossfade is 150 ms on both halves | Inside the direction's 150-200 ms window; navigation is frequent | 36.2 |
| 2026-10-18 | English and Arabic through `@angular/localize` at runtime; language kept per browser, browser language by default; public pages follow the visitor | Decision record `localisation-arabic-rtl.md` (Option B) | Product owner (36.3a) |
| 2026-10-18 | Voice chips in Arabic: الذكاء الاصطناعي (AI), الفريق (Person); charts mirror in Arabic | Approved with Option B | Product owner (36.3a) |
| 2026-10-18 | Workspace identity in the top bar (first-letter tile + name, a link to the chooser when there is another workspace); the sidebar keeps the brand lockup and the pin | The top bar exists at every width, the rail does not; the switch sits next to the name it changes | 36.4 |
| 2026-10-18 | Sidebar group labels in sentence case | Uppercase labels shout, and Arabic has no case; matches every other label in Two Voices | 36.4 |
| 2026-10-18 | The current-item marker is a short rounded 2 px bar (`.edge-marker`) instead of an inset shadow | The shadow followed the item's rounded corner and read as a bracket; a logical inset mirrors with no transform | 36.4 |
| 2026-10-18 | Command palette (Ctrl/Cmd+K): pages and quick actions only, never data, and it says so; loaded on first use | Keyboard-first navigation without implying a search the API cannot do | 36.4 |
| 2026-10-18 | Focus moves to the new page's heading after every page change; a drawer closed by navigating no longer returns focus to its trigger | A keyboard or screen-reader user arrives at the page they chose | 36.4 |
| 2026-10-18 | No match emphasis inside Arabic words in the palette | A change of weight inside a word breaks Arabic letter joining | 36.4 |
| 2026-10-18 | The public name is Nabra / نبرة ("tone of voice"); OmniAI stays the internal code name and is never shown | Two voices, the AI and the team, in one tone: the direction's own idea, in a word that works in both languages | Product owner (brand step) |
| 2026-10-18 | The mark keeps its geometry; it now reads as two voices in one tone, not as the letter "O" | The shapes already draw the duet; only the letter reading belonged to the old name | Brand step |
| 2026-10-18 | Icon files versioned (`nabra-icon.v1.svg`, `nabra-icon.v1.ico`); the next change is v2 | The edge caches .svg and .ico for a year (`immutable`) while index.html is `no-cache`: only a new name reaches people who visited before | Brand step |
