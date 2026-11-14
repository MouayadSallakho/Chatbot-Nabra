# Phase 39.2a: foundations, tokens and type (Calm Glass)

Step 39.2a of the approved plan (`PHASE_39_0_REDESIGN_PLAN_REPORT.md`, sections 3.2, 3.6 and 12): the Calm Glass token layer with the alias layer, light and dark, reduced transparency, forced colours, the motion tokens, the glass mixin, the fonts with their per-language preload and metric fallbacks, and the contrast table. The components are 39.2b.

Evidence: `C:\Users\mouay\Desktop\nabra-frontend-evidence\p39-2a\` (`gates\`, `chrome\`, `shots\`, `e2e\`, `scratch\`, `downloads\`).

## 1. Summary

- Every screen now wears Calm Glass: a cream ground with white panels (emerald-950 and emerald-900 in dark), emerald for the AI and every primary action, gold for the team, cream for the customer. Only colours and type changed; every layout is as it was.
- Type is Geist for Latin and IBM Plex Sans Arabic for Arabic, with Instrument Serif ready for the display line. A page now loads less font: 117,936 B instead of 145,868 B on an English page, and 163,232 B instead of 191,164 B on an Arabic one.
- On an English page that shows Arabic customer text, the Arabic font no longer appears late. The fallback that used to show for 0.8 s now shows for 0 s.
- The account menu has a new "Reduce transparency" item («تقليل الشفافية») beside the theme. With it, or with the device's own setting, every glass surface becomes solid. In Windows high contrast, glass uses the system colours.
- Contrast was measured on all 436 required text and boundary pairs, in both themes and with "more contrast". All of them pass WCAG 2.2 AA. The table is in `MASTER.md`.

## 2. Starting point

- Branch `master`, HEAD `9bbd4a3` (Phase 39.1, "feat: name each member, …"), worktree clean (`git status --short` empty).
- Dev stack up: `omniai-p36-api` (with the AI stand-in), `omniai-p37-mailpit`, `omniai-p37-netns`, `omniai_pg25`, `omniai_redis18`; 15 migrations; `/health/ready` 200; dev server on :4200 answering. Provider canary PASS before any traffic.
- No frontend file changed between 39.0's `f42a5c1` and `9bbd4a3`.

## 3. Changes by area

`git status --short` at the end of the step:

```text
 M docs/architecture/admin-design-system.md
 M docs/design/MASTER.md
 M frontend/src/app/core/boot/boot-state.ts
 M frontend/src/app/core/i18n/arabic-render.spec.ts
 M frontend/src/app/core/theme/theme-startup.spec.ts
 M frontend/src/app/core/theme/theme.ts
 M frontend/src/app/features/analytics/analytics-dashboard/analytics-dashboard.scss
 M frontend/src/app/features/design-lab/design-lab.html
 M frontend/src/app/features/design-lab/design-lab.scss
 M frontend/src/app/features/design-lab/design-lab.ts
 M frontend/src/app/features/overview/overview.scss
 M frontend/src/app/layout/command-palette/command-palette.scss
 M frontend/src/app/layout/entry-frame/entry-frame.scss
 M frontend/src/app/layout/profile-menu/profile-menu.html
 M frontend/src/app/layout/profile-menu/profile-menu.spec.ts
 M frontend/src/app/layout/profile-menu/profile-menu.ts
 M frontend/src/app/layout/sidebar/sidebar.scss
 M frontend/src/app/layout/topbar/topbar.scss
 M frontend/src/app/shared/icon/icon-paths.ts
 M frontend/src/app/shared/icon/icon.ts
D  frontend/src/assets/fonts/ibm-plex-sans-arabic/OFL.txt
D  frontend/src/assets/fonts/ibm-plex-sans-arabic/ibm-plex-sans-arabic-arabic-400-normal.woff2
D  frontend/src/assets/fonts/ibm-plex-sans-arabic/ibm-plex-sans-arabic-arabic-500-normal.woff2
D  frontend/src/assets/fonts/ibm-plex-sans-arabic/ibm-plex-sans-arabic-arabic-600-normal.woff2
D  frontend/src/assets/fonts/instrument-sans/OFL.txt
D  frontend/src/assets/fonts/instrument-sans/instrument-sans-latin-ext-wdth-normal.woff2
D  frontend/src/assets/fonts/instrument-sans/instrument-sans-latin-wdth-normal.woff2
 M frontend/src/index.html
 M frontend/src/locale/messages.ar.json
 M frontend/src/locale/messages.json
 M frontend/src/styles.scss
 M frontend/src/styles/_base.scss
 M frontend/src/styles/_fonts.scss
 M frontend/src/styles/_primitives.scss
 M frontend/src/styles/_tokens.scss
?? docs/phase-reports/PHASE_39_2A_TOKENS_TYPE_REPORT.md
?? frontend/public/fonts/
?? frontend/src/app/core/theme/transparency-service.spec.ts
?? frontend/src/app/core/theme/transparency-service.ts
?? frontend/src/styles/_glass.scss
```

The seven `D ` lines are staged: `git rm` removed the old font folder. Nothing was committed.

### 3.1 Tokens and themes (`_tokens.scss`, rewritten)

- **Three tiers.**
  - The raw scale: emerald, cream, sand, stone, ink, gold and coral from `calm-glass.md` §2, plus the few dark-only values (`--mist-*`, `--sage-*`, `--emerald-850/750`, `--gold-950/300`, `--coral-300/200`). Each is named, so no role holds a bare hex.
  - The semantic roles `--color-*`, `--voice-ai|team|customer-*`, `--chip-need|ai|failed-*`, `--status-*` and `--chart-*`: light on `:root`, dark in the `dark-palette` mixin (an explicit `data-theme="dark"`, plus the no-JavaScript `prefers-color-scheme` path).
  - The component tier: `--glass-*` with `--glass-solid-bg`, `--scrim`, `--glow-a/-b`, `--orb-gradient`, `--skeleton-*`, `--channel-whatsapp/-web`.
- **The alias layer**, at the end of the file.
  - Every Two Voices name is a deprecated alias of its Calm Glass role. That covers surfaces, text, borders, action, links, focus, selection, the voices, status, `--chart-human`, `--shadow-overlay`, the radius names, the type names and the motion names.
  - The aliases are declared on `:root`, where the theme, transparency and contrast switches also live. The type aliases are declared on `:lang(ar)` as well, so an Arabic fragment inside an English page keeps its Arabic leading.
  - Every component therefore took Calm Glass without being edited. 39.8b deletes the block.
- **The axes.**
  - Reduced transparency: `:root[data-transparency='reduced']`, or `@media (prefers-reduced-transparency: reduce)`, maps every glass token to its solid twin (panel colour, the same border, no blur).
  - `prefers-contrast: more`: quiet text becomes secondary, and lines become control borders.
  - `forced-colors: active`: glass becomes `Canvas`/`CanvasText`, with no shadow and no blur, and the focus ring is `Highlight`.
- **Motion tokens**: `--duration-press` 120, `--duration-menu` 200, `--duration-dialog` 280, `--duration-fade` 200 (kept under reduced motion), `--ease-calm`, `--motion-travel` (0 under reduced motion), `--motion-rise` and `--orb-breath`.
- **Space, shape and type.**
  - `--space-9` is 56 px (it was 48), and `--shell-padding`, `--column-gap` and `--panel-padding` are new.
  - The radius roles follow §5.
  - The type scale follows §4, with Arabic one step of leading up.
  - The control heights stay as they were until 39.2b (see section 4).
- **The base layer** (`_base.scss`) reads the new names directly: the body, selection, links, focus, the type utilities, the skip link and the route crossfade. `.text-display` is now the serif. A new `.text-figure` keeps the large sans numbers.
- **Two pair fixes found by measurement** (section 4):
  - `--voice-ai-meta`: the time inside an AI bubble (one line in `_primitives.scss`).
  - `--chart-channel-2` is sage in light.

### 3.2 The theme service and "Reduce transparency"

- **The service.**
  - `core/theme/theme.ts` gains `TRANSPARENCY_STORAGE_KEY` (`omniai.transparency`), `TRANSPARENCY_ATTRIBUTE` and `REDUCED_TRANSPARENCY`.
  - The new `TransparencyService` holds the choice as a signal and writes or removes `data-transparency` on `<html>`. It keeps the choice per browser, like the theme, and degrades to glass when storage throws.
- **The account menu.** A `menuitemcheckbox` "Reduce transparency" sits in the theme group, with a new `layers` glyph and `data-testid="reduce-transparency"`. Choosing it closes the menu and returns focus to the trigger, as every item does.
- **The pre-paint script** (`index.html`) stamps `data-transparency` before first paint, on admin pages only. The visitor pages follow the device, as they do for the theme.
- Theme stays light, dark or "System", in the account menu and per browser, unchanged.

### 3.3 The glass mixin (`_glass.scss`, new)

`@mixin glass` and `.glass` are the only places that declare `backdrop-filter`. They take the background, border, inner highlight, shadow and blur from the tokens. `@supports not (backdrop-filter: …)` gives the solid twin, and forced colours give system colours with no shadow. The budget is written in the file. Nothing in the product uses glass yet (39.2b and 39.3 do), so the design lab shows one sample over the two glows.

### 3.4 Fonts (`_fonts.scss`, `public/fonts/`, `index.html`)

- **The files** are Fontsource 5.3.0 builds, downloaded into `p39-2a\downloads\` and checked as woff2. Each family has its OFL text beside it:
  - `public/fonts/geist/geist-latin.v1.woff2` (29,400 B) and `geist-latin-ext.v1.woff2` (16,512 B);
  - `public/fonts/ibm-plex-sans-arabic/ibm-plex-sans-arabic-arabic-{400,500,600,700}.v1.woff2` (42,848 / 45,296 / 45,688 / 44,280 B). 400 to 600 are byte-identical to the old ones; 700 is new;
  - `public/fonts/instrument-serif/instrument-serif-latin.v1.woff2` (21,032 B) and `-latin-ext.v1.woff2` (11,604 B).
- Instrument Sans is removed (−76,188 B of files).
- **Root-relative URLs survive the build.** The production build keeps `url(/fonts/…v1.woff2)` exactly as written in the CSS. The build emits no `media/` font copies, and the files are served from `/fonts/`. The plan's open risk 2 is closed.
- **Preload** from the pre-paint script, at HTML parse:
  - Geist Latin on every page;
  - Arabic 400 on Arabic pages;
  - Arabic 400 on English pages under `/conversations`, at `fetchpriority="low"`.
  - Arabic 600 is not preloaded. The CSS asks for it about 620 ms after the HTML arrives, and it is there long before any Arabic heading (measured).
- **Metric fallbacks**, measured in Chrome (`chrome\font-metrics.json`). Each fallback was checked by rendering the same paragraphs in both fonts: they wrap to the same number of lines at the same height.

  | Fallback | Font | size-adjust | ascent | descent |
  | --- | --- | --- | --- | --- |
  | `Geist Fallback` | Arial | 101.72% | 98.8% | 29% |
  | `Instrument Serif Fallback` | Georgia | 76.14% | 130.03% | 40.72% |
  | `IBM Plex Sans Arabic Fallback` | Segoe UI | 89.52% | 121.2% | 46.36% |

- **Weights.** Running text no longer uses a weight above 600. The 650s in the boot lockup, the sidebar wordmark, the top bar's workspace tile and the palette's match emphasis became 600, so an Arabic page never downloads the 700 file for them.
- **Large figures** (the overview and analytics numbers, and the entry tagline) moved from `--text-display` to `--text-figure` and look as they did. `--text-display` is now the serif display line.

### 3.5 Design lab and documentation

- **Design lab** (development only; the full rewrite is 39.2b):
  - the colour section lists the Calm Glass roles, read live;
  - the type section adds the serif display line and the "Figure" row;
  - the radius and motion lists use the new tokens;
  - a glass sample (`data-testid="glass-sample"`) floats over the two glows, with a "Reduce transparency" toggle;
  - the intro and three notes are reworded, and the stylesheet stays inside its 4 kB budget.
- **`docs/design/MASTER.md`**:
  - header and status;
  - §3 colour roles and the alias layer, and §3.1 glass, transparency and the axes;
  - §4 typography, fonts and preload;
  - §5 spacing and density, §6 radius, §7 motion;
  - §10 and §11 rules;
  - 11 decision rows;
  - a new §13 with the full contrast table: 115 pairs × 4 modes.
- **`docs/architecture/admin-design-system.md`**: D5 (tokens, tiers, alias layer, glass), D6 (typography), D7 and D8 (themes and contrast) rewritten, plus a pointer at the top of D1. D1 itself is rewritten with the components in 39.2b.

### 3.6 Screens

| Screen | What changed | Components and tokens it now uses | Tests |
| --- | --- | --- | --- |
| All 41 screens and the widget | Calm Glass colours and type through the alias layer; layouts unchanged | every component, through the aliases (`--surface-*` → `--color-*`, `--action` → `--color-primary`, voices → `--voice-*`/`--chip-*`, type → `--text-title-*`/`--text-body`) | the whole Vitest suite; e2e 46/46; the Chrome matrix (section 5) |
| Account menu | "Reduce transparency" item | `TransparencyService`, `.menu-item`, `layers` glyph | `profile-menu.spec.ts` (+2), `transparency-service.spec.ts` (5), `arabic-render.spec.ts` (+1, Arabic), `theme-startup.spec.ts` (+11) |
| Boot / loading state | wordmark weight 600; Calm Glass fallback colours | `--voice-*` aliases, `--text-primary` | `theme-startup.spec.ts`, `brand.spec.ts` |
| Overview, analytics, entry frame | large numbers and the tagline on `--text-figure` (same look) | `--text-figure` | existing specs |
| Sidebar, top bar, command palette | 650 → 600 | `--font-sans` | existing specs |
| Design lab | foundations sections (section 3.5) | `--color-*`, `--voice-*`, `--chip-*`, `--status-*`, `--chart-*`, `--glass-*`, `.glass`, `.text-display`, `.text-figure` | Chrome matrix |

**Screenshots** (`p39-2a\shots\`, 434 PNG):

- **Before** (the committed HEAD build), 168 files: `<screen>-{1440,390}-{en-light,ar-dark}-before.png` for all 41 screens, plus `widget-closed-*` and `widget-open-*`.
- **After**, 168 files: the same set, `…-after.png`.
- **Account menu**, 10 files: `account-menu-{1440,390}-{en-light,ar-dark}-{before,after}.png`, plus `account-menu-1440-*-reduced-after.png` with the item checked.
- **Modes**, 80 files: `{inbox,overview,conversation,hosted-chat,design-lab}-{1440,390}-{en-light,ar-dark}-{rt-media,rt-setting,reduced-motion,forced-colors}-after.png`. Only the first 1,440 or 390 px of the page height, not full page.
- **Pairs to open first:**
  - `inbox-1440-en-light-before/after`;
  - `overview-1440-ar-dark-before/after`;
  - `hosted-chat-390-ar-dark-before/after`;
  - `design-lab-1440-en-light-after`;
  - `account-menu-390-ar-dark-after`.

## 4. Decisions

**Choices made:**

1. **The alias layer as the plan describes it (§3.2.1).** Two Voices names point at Calm Glass roles, and only the base layer and the new code read the new names. Rejected: renaming in every component now, because that is each screen step's own work.
2. **Colour and type only; geometry kept.**
   - The control heights stay 32 / 36 / 40 px (44 on touch) until 39.2b restyles buttons and inputs in place.
   - `--radius-badge` 6 px stays until the choice controls.
   - The plan lists `--control-height` 44 among the tokens. It is deferred to 39.2b because a height is layout. `--control-height-dense` 40, `--target-min` 44 and `--chip-height` 24 are added now.
3. **`--space-9` 48 → 56**, as the plan says. It has six uses, all paddings; the matrix shows no overflow.
4. **Body type 14 → 15 px** (`--text-body` 15/24, as the spec says). Dense rows grow by a few pixels (an inbox row is 102 px instead of 97). Skeleton geometry is redrawn per screen step (note 11).
5. **"Reduce transparency" is a checkbox item in the theme group**, kept per browser. Choosing it closes the menu like every other item. The theme options keep their current label "System" («حسب النظام»): the prompt describes the option as "same as my device", and rewording it would change an existing, reviewed string for no behaviour gain.
6. **Reduced motion.** Under reduced motion the base layer's universal rule still stops everything, including literal infinite animations such as the spinner, the skeleton sweep and the typing dots. A fade that must stay declares `--duration-fade` on its own rule, which out-ranks `*`.
7. **One easing.** `--ease-enter`, `--ease-exit` and `--ease-emphasized` alias `--ease-calm`, as §5 asks.
8. **The solid twin keeps the glass shadow.** §3 says "panel colour, the same border" and is silent on the shadow. A menu still floats, so it keeps its shadow. Forced colours drop it.
9. **Arabic 600 is not preloaded on Arabic pages** (the plan left this to measurement). It is requested about 620 ms after the HTML and lands about 1.3 s before the first Arabic heading, with fallback 0.
10. **The English-inbox Arabic preload is kept**, by the plan's own test. It removes the measured fallback (791 → 0 ms).
    - Cost, measured in the same session: the English inbox's LCP is +300 ms at 150 ms latency and 200 KB/s. About 220 ms of that is the 43 KB file sharing the link with the bundle.
    - `fetchpriority="low"` changed nothing measurable on the loopback HTTP/1.1 server, but stays: on the HTTP/2 edge it lets the bundle go first.
    - The owner may prefer to drop it (section 9).
11. **Instrument Serif Latin Extended** (11,604 B) ships beside Latin. It is not in the plan's table, but accented names in the greeting would otherwise fall into Georgia. It costs nothing unless used.
12. **The design lab** got a minimal foundations update, so the step's tokens can be seen and checked; its full rewrite stays in 39.2b. The five unnamed loading buttons (axe `button-name`, present before) are left for 39.2b, as the plan says.
13. **Chrome checks used Playwright**, driving the installed Google Chrome with temporary contexts, as 39.0 did. The Chrome DevTools MCP was not used: the matrix is about 1,100 scripted page loads with axe, overflow and console checks. Rule 12 names the MCP first; this is a departure in tool, not in what was checked.

**Departures from the specification or the boards, each measured:**

| Where | Specification / plan | Shipped | Why |
| --- | --- | --- | --- |
| `--chart-channel-2`, light | emerald-300 (plan §3.2.1) | sage `#668276` | emerald-300 is 2.25:1 on white, below 3:1 for a series a reader follows |
| The time inside an AI bubble, dark | quiet text `#9AA79F` | `--voice-ai-meta` `#C9D8CF` (stone-600 in light) | quiet text on the dark AI bubble is 3.99:1 |
| `--status-danger-fg` | coral-600 for errors (§2) | coral-700 as text; coral-600 for the solid fill and the failed dot | coral-700 holds 4.5:1 on coral-50 as well as on white; coral-600 stays where the spec draws it as a fill or a mark |
| Title 1 | 30/36 | `clamp(24px, …, 30px)` / 1.2 (30/36 from 1,108 px) | keeps today's phone title size; layouts are unchanged in this step |
| Control border, selected segment, status hues, team chart lines | as §2 | as the owner decided (Q3, Q4; plan §4) | recorded in MASTER.md |

**Deferred, per the plan:** the components and the choice-control family (39.2b), glass on the shell (39.3), the brand mark's new geometry and the outlined lockup (39.2b). Until then the old mark draws in the new colours. Also the visitor-page ground cream-50 (39.5b), and the deletion of the alias block (39.8b).

## 5. Verification

| Check | Result |
| --- | --- |
| `npm run format:check` | **PASS**: all matched files use Prettier style |
| `npm run test:ci` | **PASS**: 125 files, 1,893 tests, 0 failures (39.0: 124 / 1,874). Free memory before: 9.47 GB |
| `npm run build` | **PASS**: 0 warnings, budgets met. The design lab's stylesheet was first 151 B over its 4 kB warning budget and was trimmed, not raised. Initial total 325.47 kB raw / 74.85 kB transfer (HEAD: 313.77 / 73.69; the growth is the stylesheet, 112.89 → 124.59 kB raw, 12.47 → 13.62 kB transfer). **Startup JS 141,733 B gzip**, 30 files, 439,460 raw, 125,321 brotli. HEAD measured in the same session: 141,400 (+333 B). Against the 38.0 baseline of 138,907: +2.0%, ceiling 152,797. Free memory before: 9.46 GB |
| `npm run i18n:check` | **PASS**: 2,454 messages, 0 errors, 0 warnings (39.0: 2,448) |
| `npm run e2e` | **PASS**: 46 of 46 (23 journeys × en-ltr and ar-rtl), 0 failures, no retries, 2.9 min, the full suite including the throttling journey. `NABRA_E2E_OUT` and `NABRA_E2E_STATE_DIR` were in `p39-2a\e2e\`. One inert edit followed (the boot fallback colours), so the suite ran again on the final state with `--grep-invert "too many failed"`: **44 of 44, 3.0 min**. That second run was limited by the 15-minute rule (it started 9 minutes after the full run). Free memory before: 9.81 / 9.46 GB |
| Backend gates | **NOT RUN**: nothing under `backend/` changed in this step |
| Chrome: screenshots and checks | **PASS**: 41 screens × {1440, 390} × {en-light, ar-dark} plus the widget on English and Arabic host pages. 168 before (HEAD build) and 168 after (this build), each with axe (WCAG 2.2 AA tags), overflow, one `h1`, unnamed controls and console. After: 0 overflow, one `h1` everywhere, 0 unnamed controls, 0 console errors. axe 0 violations except the dev-only design lab's 5 `button-name` (unchanged from before; 39.2b) |
| Chrome: full matrix | **PASS**: 41 screens × {390, 768, 1280, 1920} × {en, ar} × {light, dark} = 656 loads: 0 overflow, one `h1`, 0 unnamed controls, 0 console errors. axe at 1280: 0 violations except the lab's 5 `button-name` |
| Reduced transparency | **PASS**, 40 loads: the media query and the in-app setting, on the inbox, overview, conversation, hosted page and design lab, 1440 and 390, en-light and ar-dark. `--glass-bg` resolves to the panel (`#fff` / `#0b2b26`), the blur is `none`, no element on the page has a `backdrop-filter`, and the sample keeps its border and shadow. Without either switch the sample blurs (`blur(24px) saturate(1.4)`) |
| Reduced motion | **PASS**, 20 loads: `--duration-menu` 0.01 ms, `--motion-travel` 0, glass unchanged; 0 overflow, 0 console errors |
| Forced colours | **PASS (rendering)**, 20 loads: glass is `Canvas`, with no shadow and no blur. axe reports `color-contrast` (1.22:1) on the bell's count badge in dark on 3 pages, and on the lab's count badge. The screenshot shows the badge in white on black: axe reads the author colour, not the forced one. Recorded as an axe limitation (section 9) |
| Keyboard and focus (the new item) | **PASS**, en and ar, 1440 and 390, on the production build: Escape returns focus to the trigger; Enter opens the menu; three ArrowDowns reach "Reduce transparency"; Enter sets `data-transparency="reduced"`, stores it, closes the menu and returns visible focus to the trigger; after a reload the attribute is already there before the bundle runs and the item reads checked; turning it off removes the attribute and stores `full` |
| Contrast of every new pair | **PASS**: 436 of 436 required pairs, measured from the live resolved tokens in light, dark, and both with `prefers-contrast: more`, alpha composited over the real ground. Tightest text 4.77:1 (quiet text on hover, light); tightest boundary 3.04:1 (control border on hover, dark). 28 decorative pairs reported (lines, gold dots beside a word, skeletons, gold fills). Table: MASTER.md §13; data `chrome\contrast.json` |
| RTL mirroring | **PASS**: `dir` correct on every load. `rtl_logical_lint.py` on the changed styles: 0 findings, plus 4 "letter-spacing" notes that `:lang(ar)` already zeroes. The new `layers` glyph is not directional and is not mirrored |
| Touch targets | **PASS at 24 px**: the new item is 32 px tall with a fine pointer (34 px in Arabic). With a coarse pointer every menu item is `--control-height-sm`, 40 px: below the 44 px touch target (Two Voices geometry, unchanged; menus are redrawn in 39.2b/39.3) |
| Token, motion and perf lints | `token_lint.py` 0 warnings (the 5 info items, Two Voices fallbacks in `index.html`, fixed); `motion_audit.py` 0 findings |
| Fonts: files and preload | See the table below |
| Fonts: layout shift on swap | Every font held back 1.5 s, 3 loads each. Inbox: en 0.0014 (before 0.0016), ar 0.0003 (0.0006). Hosted page: en 0 (0), ar 0.0001 (0.0001). Overview: ar 0.0032 (0.0079), en 0.0227 (0.0208), the same chart-and-section shift in both builds when the font lands, far below 0.1. Lab CLS without throttling: 0 on every Arabic-font case |
| Provider canary | **PASS** before any traffic and again at the end: every listed variable absent, `Email__Smtp__Host` loopback, `OMNIAI_DEV_AI_STANDIN` present (info) |
| Secrets | **PASS**: `scratch\secret-scan.mjs` scanned 1,041 files (this step's evidence, `frontend/src`, `frontend/e2e`, this report, MASTER.md and the design-system doc): 0 with the demo password, 0 with an invitation link (`gates\secret-scan.log`). The cookie jar (`p39-2a\tmp\`) was deleted; the e2e state folders were removed by the suite's teardown |

**Fonts per language** (39.0 protocol: production build, cache disabled, 150 ms latency, 200 KB/s, 5 cold loads, medians). Both builds were measured in the same session; times are from the HTML's first byte (this session's connection took about 1.04 s before it on both builds).

| Case | Fonts on first paint (bytes) | Preloaded | Arabic 400 requested | Latin font ready | Arabic fallback shown | LCP |
| --- | --- | --- | --- | --- | --- | --- |
| English page with Arabic text, before | Instrument Sans 57,332 + Plex 400 42,848 + Plex 600 45,688 = 145,868 | nothing | +3,241 ms | +980 ms | **791 ms** | +3,297 ms |
| same, after | Geist 29,400 + Plex 400 42,848 (+ 600 later) = 117,936 | Geist, Arabic 400 (low) | +32 ms | +752 ms | **0 ms** | +3,569 ms |
| Arabic inbox, before | 57,332 + 45,688 + 42,848 (+ 500 later) = 191,164 | nothing | +413 ms | +1,517 ms | 0 | +3,873 ms |
| Arabic inbox, after | 29,400 + 42,848 + 45,688 (+ 500 later) = 163,232 | Geist, Arabic 400 | +33 ms | +750 ms | 0 | +3,947 ms |
| Arabic hosted page, before / after | 191,164 / 163,232 | — / Geist, Arabic 400 | +413 / +32 ms | +1,534 / +748 ms | 0 / 0 | +3,630 / +3,661 ms |
| English inbox, before / after | 145,868 / 117,936 | — / Geist, Arabic 400 (low) | +3,357 / +34 ms | +975 / +750 ms | — | +3,066 / +3,327 ms |
| English overview, before / after (control: no Arabic preload) | 57,332 / 29,400 | — / Geist | — | +969 / +608 ms | — | +2,940 / +3,015 ms |

The LCP element on these pages is text that waits for API reads, so LCP follows the bundle, not the fonts. The English overview, which preloads no Arabic, moves +75 ms (the larger stylesheet). The English inbox and the deep link move about +270 ms, most of it the Arabic preload sharing a 200 KB/s link. Without throttling (the 39.0 vitals protocol) this is not measurable. It is the trade-off in section 9, item 1.

## 6. See it live

- The admin app: http://localhost:4200/sign-in.
  - Accounts: the **Owner** `p36.owner@omniai.example`, the **Support agent** `p36.support@omniai.example` and the **Analyst** `p36.analyst@omniai.example` (passwords in `omniai-redesign-evidence\p36-01\demo-accounts.md`, not copied here).
  - The dev server and the API (with the AI stand-in) are left running.
- **Things to try:**
  - **The inbox:** http://localhost:4200/conversations. Cream ground, emerald chips, Geist. Switch to Arabic and Dark from the account menu (top end): emerald-950 ground and Plex Arabic. The 232-message conversation `/conversations/01a15013-917d-75a7-870c-98ceb0b9e7f8` shows gold team bubbles.
  - **Reduce transparency:** in the account menu, under the theme. Nothing visibly changes on today's screens, because no glass ships before 39.2b and 39.3. Watch it in the design lab instead.
  - **The design lab:** http://localhost:4200/design-lab (development only). Live token values, the type scale with the serif display line, and the glass sample over the glows with a "Reduce transparency" toggle beside it.
  - **The overview:** http://localhost:4200/overview.
  - **The hosted chat page:** http://localhost:4200/chat/9veAaKzu0Ee-isA7jrQzYw (`?lang=ar` for Arabic).
- **To see the font preload:** open DevTools → Network → Font on `/conversations` in English. `geist-latin.v1.woff2` and `ibm-plex-sans-arabic-arabic-400.v1.woff2` start at HTML parse (initiator "link").
- **Mailpit:** http://127.0.0.1:8026 (no email work in this step).

## 7. Backend asks

None new. Nothing in this step needs the API. Open asks are unchanged: BA39-2, BA39-3, BA39-4, BA-6 and BA-8 from the plan.

## 8. New Arabic for my review

| English | Arabic | Where |
| --- | --- | --- |
| Reduce transparency | تقليل الشفافية | account menu, beside the theme (and the design lab's toggle) |
| "Calm Glass": the tokens, type, glass and motion on one page; the components below take the new colours through the tokens and are redrawn in the next step. Development builds only. Every name, number and message here is sample content, written for this page. | «زجاج هادئ»: الرموز والخطوط والزجاج والحركة في صفحة واحدة؛ والمكوّنات أدناه تأخذ الألوان الجديدة عبر الرموز، وتُعاد رسمها في الخطوة التالية. لنسخ التطوير فقط. كل اسم ورقم ورسالة هنا محتوى تجريبي كُتب لهذه الصفحة. | design lab intro |
| Values are read live from the page, so they are the ones this theme actually resolved. Emerald is the AI, gold is the team and cream is the customer; a voice is never decoration. | تُقرأ القيم مباشرة من الصفحة، فهي ما طبّقه هذا المظهر فعلًا. الزمرّدي للذكاء الاصطناعي، والذهبي للفريق، والكريمي للعميل؛ ولا يُستخدم لون الصوت للزينة أبدًا. | design lab, colour |
| Status words reuse the three hues, each with its own glyph and word, so none depends on its hue. In the inbox and the transcript only the voice chips appear. | تستخدم كلمات الحالة الألوان الثلاثة نفسها، ولكلٍّ منها رمزها وكلمتها، فلا يعتمد معناها على اللون. وفي صندوق المحادثات والمحادثة لا تظهر إلا شارات الأصوات. | design lab, colour |
| Geist and IBM Plex Sans Arabic for the interface, Instrument Serif for the display line, all self-hosted. Figures are tabular and stay Western in both languages. | Geist وIBM Plex Sans Arabic للواجهة، وInstrument Serif لسطر العرض، وكلها مستضافة ذاتيًا. الأرقام جدولية وتبقى غربية في اللغتين. | design lab, type |
| Ground and panels | الأرضية واللوحات | design lab |
| Lines and control boundaries | الخطوط وحدود عناصر التحكم | design lab |
| Primary, links and focus (emerald) | اللون الأساسي والروابط والتركيز (زمرّدي) | design lab |
| Voice: the AI (emerald) | الصوت: الذكاء الاصطناعي (زمرّدي) | design lab |
| Voice: the team (gold) | الصوت: الفريق (ذهبي) | design lab |
| Voice: the customer (cream) | الصوت: العميل (كريمي) | design lab |
| Chips | الشارات | design lab |
| Glass and depth | الزجاج والعمق | design lab |
| Figure | رقم كبير | design lab, type |
| A panel: solid, a 1px line, no shadow. | لوحة: صلبة، بخط رفيع، بلا ظل. | design lab |
| Glass | الزجاج | design lab |
| Glass: only where things float, and the only surface that casts a shadow. With "Reduce transparency" it becomes its solid twin. | الزجاج: فقط حيث تطفو العناصر، وهو السطح الوحيد الذي يُلقي ظلًا. ومع «تقليل الشفافية» يصبح نظيره الصلب. | design lab |
| (sample text) Good morning, Layla | صباح الخير يا ليلى | design lab, display sample (sample content, not a catalogue message) |

The design lab strings are development-only. Eleven retired lab IDs were removed from the catalogue (`lab.intro`, `lab.colour.note`, `lab.colour.surfaces`, `lab.colour.borders`, `lab.colour.action`, `lab.colour.voiceAi`, `lab.colour.voiceHuman`, `lab.colour.sideBySide.note`, `lab.type.note`, `lab.shape.card`, `lab.shape.overlay`).

## 9. Risks and open issues

1. **The English inbox's Arabic preload costs about 0.27 s of LCP on a 200 KB/s link** while it removes a 0.8 s fallback (section 5). It is kept by the plan's rule. Dropping it is a one-line change in `index.html` plus its spec, if you prefer the faster list over the font being ready.
2. **Rows and skeletons:** body text grew from 14 to 15 px, so dense rows are a few pixels taller and some loading skeletons no longer match the loaded geometry exactly. The lab and swap CLS stayed at 0–0.02. Each screen step redraws its skeletons (note 11).
3. **The old mark in the new colours.** Until 39.2b draws the two-circle mark, the Two Voices speech shapes are emerald and gold-800 (sidebar, entry pages, boot state).
4. **axe and forced colours:** axe flags the count badges in dark forced colours (it reads the author colour); the rendering is correct. A later step could add an explicit `forced-colors` rule (`Highlight` / `HighlightText`) to make the check pass outright.
5. **Menu items on touch are 40 px**, not 44 (pre-existing geometry). Menus are redrawn in 39.2b and 39.3.
6. **The design lab's five unnamed loading buttons** (pre-existing, dev-only) are for 39.2b.
7. **`docs/architecture/localisation-arabic-rtl.md`** still quotes Instrument Sans in its Phase 36 measurement sections. Those are history; the current facts are in MASTER.md and D6.
8. **Staged deletions:** `git rm` staged the removal of `src/assets/fonts/`. Everything else is unstaged.
9. **Environment:** the loopback production-build server was stopped by the harness's background limit during the first modes run. The affected loads failed with "connection refused" and were re-run in full, with results above. This session's connections were about 0.45 s slower before the first byte than 39.0's, so the font table compares both builds in the same session.

## 10. Proposed checkpoint commit message

```text
feat: give every screen the Calm Glass colours and type, self-host Geist,
IBM Plex Sans Arabic and Instrument Serif with a per-language preload, add
"Reduce transparency", and measure every colour pair (Phase 39.2a)

Rewrite the token layer in three tiers (raw scale, semantic roles for light
and dark, glass and other component tokens) with a deprecated alias layer, so
every screen changes colour and type at once and each screen step moves its
own names; add reduced transparency (the account menu's new item and the OS
preference), forced colours and the motion tokens; add the glass mixin, the
only place that declares backdrop-filter; move the fonts to public/fonts with
versioned names, OFL texts, metric-matched fallbacks and a preload from the
pre-paint script (Geist always, Arabic 400 on Arabic pages and the English
inbox), which removes the 0.8 s Arabic fallback and lightens first-paint
fonts; and keep the measured contrast table (436 pairs, 0 failures) in
MASTER.md.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```
