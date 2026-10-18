# Localisation: English and Arabic, with full RTL (decision record)

- Status: **Accepted**, 2026-10-18, by the product owner: Option B, with the defaults in
  section 10.
- Date: 2026-10-18. Phase 36.3a ("Two Voices" redesign). No product code changed in this phase.
- Scope: the Angular admin (`frontend/`), the hosted chat page (`/chat/:publicKey`) and the
  embedded widget document (`/embed/:publicKey`). The backend, `deploy/` and the database are
  untouched; what they would need is listed under "Backend asks" (section 8).
- Reads with: `docs/design/MASTER.md` (type, numerals, Arabic rules, signature elements) and
  `docs/architecture/admin-design-system.md` (D10 pre-paint script, D22 server limits).

## 0. Context: what exists today

Measured in the worktree at `ce4ac51` (2026-10-18):

| Fact | Where |
|---|---|
| Angular 22.1.5 (`@angular/build` 22.1.7), zoneless, standalone, Vitest through `@angular/build:unit-test`; no `@angular/localize`, no translation library, no `LOCALE_ID`, no `registerLocaleData` | `frontend/package.json`, `app.config.ts` |
| `main.ts` imports `App` and `appConfig` statically; `appConfig` imports `app.routes.ts`, which imports `layout/navigation.ts` | `src/main.ts` |
| User-facing English sits in module-level constants (`NAV_ITEMS[].label`, `NAV_GROUPS`, `THEME_LABELS`, `UNSAVED_CHANGES_QUESTION`, `api-error.ts` `MESSAGES`), in route `title`s copied from those labels, in 48 templates and in TS-built sentences | `layout/navigation.ts`, `core/theme/theme.ts`, `core/api/api-error.ts`, `app.routes.ts` |
| Plurals are English ternaries (`failedReplies === 1 ? 'reply' : 'replies'`) | `features/overview/overview.ts:142-164` |
| Dates and numbers: 24 Angular pipe uses in templates; about 20 direct `Intl`/`toLocale*` calls, most with locale `undefined` (the browser's default, so formats already vary by machine), three hard-coded `'en'` | `notification-item.ts`, `analytics-dashboard.ts`, `billing-*.ts`, `plan-usage-page.ts`, `reports-*.ts`, `customer-*.ts`, `overview.ts` |
| `index.html` has one inline pre-paint script (theme, D10) and a static "Loading OmniAI…" status inside `<app-root>` | `src/index.html` |
| The edge hashes **every** inline `<script>` in the built `index.html` into `$spa_script_hashes`, for both `location /` and `location /embed/` | `deploy/edge/csp-hashes.mjs`, `deploy/edge/templates/omniai.conf.template` |
| The widget loader served by the API builds the iframe as `origin + '/embed/' + key` and sets `frame.title = 'Chat'` on the customer's page | `backend/src/OmniAI.Api/Widget/v1.js:89-96` |
| Notifications arrive as server-written English `title` and `body` (with a `kind`); some API error `detail` strings are shown verbatim | `NotificationCatalog.cs`, `core/api/api-error.ts:298-321` |
| RTL groundwork: no physical `left`/`right`/`margin-left` in styles or templates (scan found none), no Bootstrap directional utilities (only `bootstrap-grid.min.css` is loaded), `--direction-flip` token used in 6 places, `:lang(ar)` type tokens already in `_tokens.scss`, IBM Plex Sans Arabic self-hosted with `unicode-range` | `styles/`, `MASTER.md` section 4 |
| Directional icons exist as `arrow-start`, `arrow-end`, `chevron-start`, `chevron-end`, `sign-out`, but are drawn LTR and **nothing mirrors them yet** | `shared/icon/icon-paths.ts`, `icon.scss` |
| Production build: initial **454.05 kB raw / 111.07 kB transfer**, under the 500 kB warning by about 46 kB | `npm run build`, 2026-10-18 |

### How the facts were checked

- Angular documentation: the Angular CLI MCP (`search_documentation`) is **not connected** in this
  session, so the pages were read on angular.dev, whose footer reads "Built by Angular at
  v22.2.3": `guide/i18n/merge`, `guide/i18n/manage-marked-text`, `guide/i18n/translation-files`,
  `guide/i18n/add-package`, `guide/i18n/prepare`, `guide/i18n/deploy`,
  `guide/i18n/import-global-variants`, `api/localize/loadTranslations`, `api/localize/init/$localize`.
  Claims below that the docs do not state are marked **(not in the docs; prove in 36.3b)**.
- `Intl` behaviour: one probe, run in real Chrome 155 through the Chrome DevTools MCP, in headless
  Chrome 155 with `--lang=en-US` and `--lang=ar`, and in Node 24.19 (ICU 78.3, CLDR 48). Same
  results everywhere. Raw output: `omniai-redesign-evidence\p36-03a\intl-*.json`.

| Locale | `NumberFormat` 1234567.5 | Numbering | `dateStyle: 'medium'` | Calendar |
|---|---|---|---|---|
| `ar` | 1,234,567.5 | latn | 18‏/10‏/2026 | gregory |
| `ar-AE` | 1,234,567.5 | latn | 18‏/10‏/2026 | gregory |
| `ar-SA` | ١٬٢٣٤٬٥٦٧٫٥ | arab | ١٨‏/١٠‏/٢٠٢٦ | gregory |
| `ar-EG` | ١٬٢٣٤٬٥٦٧٫٥ | arab | ١٨‏/١٠‏/٢٠٢٦ | gregory |
| `ar` + `{ numberingSystem: 'latn', calendar: 'gregory' }`, long month | 18 أكتوبر 2026 في 2:05 م | latn | (explicit fields) | gregory |

`Intl.PluralRules('ar')`: 0 zero, 1 one, 2 two, 3-10 few, 11-99 many (21, 40 and 57 are all
`many`), 100-102 other. `formatRange(21, 40)` returns a bare `21–40` with **no** bidi marks;
`style: 'percent'` returns `42‎%‎` with left-to-right marks.

## 1. Requirements

| # | Requirement | Acceptance check |
|---|---|---|
| R1 | English is the source language. Arabic covers every admin screen, the hosted chat and the widget | the untranslated-ID check (3.9) passes with zero missing IDs |
| R2 | Each member's choice is kept in the browser (`localStorage`); no server preference endpoint exists (D22) | no new API call; the key is read only by the pre-paint script and the locale service |
| R3 | `lang` and `dir` are correct before first paint, with no flash of the other direction | Chrome check reads `document.documentElement` at `DOMContentLoaded`, before the bundle runs |
| R4 | Public surfaces follow the **visitor's** browser language, never the admin's stored choice | `/chat/` and `/embed/` ignore `omniai.lang`; checked with `--lang=ar` and `--lang=en-US` Chrome |
| R5 | Dates and numbers go through `Intl` with explicit Arabic settings: Gregorian calendar and an explicit `numberingSystem` (policy in 5.4) | unit tests assert digits and calendar for `en` and `ar` |
| R6 | Arabic plural rules (six categories) work in counts such as "21–40 of 57 customers" | specs for 0, 1, 2, 3, 11, 57, 100 in Arabic |
| R7 | The 65 existing spec files (801 tests) keep working | `npm run test:ci` stays green with no copy changes |
| R8 | No third-party request, no CDN, no `innerHTML` with dynamic content; CSP stays hash-based | network log in Chrome; `csp-hashes.mjs` output covers every inline script |
| R9 | The `angular.json` budgets are kept (initial 500 kB warning / 1 MB error; component styles 4 / 8 kB) | `npm run build` with no new budget warning |

## 2. Options

### A. `@angular/localize`, build-time inlining (one build per locale, `/en/` and `/ar/`)

How: mark templates with `i18n` and code with `$localize`; `angular.json` gets
`i18n.sourceLocale: "en"` and `locales.ar`; `localize: true` makes the CLI emit one complete
application per locale. The docs: the CLI "places each generated version in a locale-specific
directory" and "adjusts the HTML base HREF for each version"; `i18nMissingTranslation` can be
`error` ("the build fails"); the deploy guide serves "each language from a different
subdirectory" and redirects `/` by `Accept-Language`.

```jsonc
// angular.json (sketch)
"i18n": { "sourceLocale": "en", "locales": { "ar": "src/locale/messages.ar.json" } },
"build": { "options": { "localize": true, "i18nMissingTranslation": "error" } }
```

```nginx
# edge (sketch): every SPA URL gains a prefix
location ~ ^/(en|ar)/ { try_files $uri /$1/index.html; }
location = /            { return 302 /en/; }   # the edge cannot read localStorage
```

Impact on this codebase:
- **Edge (`deploy/`).** New locations per prefix, a root redirect, and a rewrite for every old
  URL. The member's choice lives in `localStorage`, which the server cannot see, so a
  server-side redirect would need a cookie (a new contract) or a client-side bounce through `/`.
- **CSP hashes.** `csp-hashes.mjs` reads one `index.html`; it would have to hash both
  `en/index.html` and `ar/index.html` and union the results (an edit to `edge.Dockerfile`).
  The inline scripts would be identical, so the hashes would match, but the pipeline changes.
- **Embed routes.** The widget loader (backend) builds `/embed/<key>` with no prefix, so
  following the visitor's language means either an edge `Accept-Language` rewrite of `/embed/`
  into `/ar/` or `/en/` (nginx parses q-values crudely) or a loader change (backend). The hosted
  chat URL `/chat/<key>` is already published by workspaces and must not change.
- **Backend links.** Notifications carry root-relative paths (`/reports`, `/billing`);
  invitation e-mails link to `/accept-invitation`. All of them would need a prefix or an edge
  redirect.
- Cost: two builds (twice the build time), the dev server serves one locale per configuration,
  and switching language is a navigation to the other prefix.
- Gains: zero runtime translation cost, and missing translations fail the build natively.
- Risk: **high**. It needs changes to `deploy/` and the backend, which this redesign keeps out
  of scope, and it changes every URL users and e-mails already hold.

### B. `@angular/localize`, runtime `loadTranslations()` before bootstrap (one build)

How: the same `i18n` and `$localize` markup as A, but no `localize` build option. `$localize` stays a
runtime function (`@angular/localize/init` as a polyfill). Before the application module graph is
evaluated, `main.ts` loads the Arabic catalogue with `loadTranslations()`; then it imports and
bootstraps the app. The docs: `loadTranslations` "Load[s] translations for use by `$localize`, if
doing runtime translation", messages "are only processed once, when the tagged string is first
encountered, and [it] does not provide dynamic language changing without refreshing the browser".
`ng add @angular/localize --use-at-runtime` exists for exactly this case: the package goes in
`dependencies`, not `devDependencies`.

```ts
// one stable custom ID per message; English stays the source text
readonly label = $localize`:@@nav.overview:Overview`;
```

```html
<button type="button" i18n="@@inbox.takeOver">Take over</button>
<input i18n-aria-label="@@customers.search.label" aria-label="Search customers" />
```

- One build and one `index.html`. Every URL, the edge, the CSP pipeline, the embed route and
  backend links stay as they are.
- Switching language writes the choice and reloads (accepted in R2; the docs say a reload is
  required).
- English is the source text, so templates and the 801 tests keep their wording: with no
  translations loaded, `$localize` passes the source through.
- Custom stable IDs (`@@inbox.takeOver`) keep translations attached when English copy changes,
  and give translators context.
- Cost: `@angular/localize` 22.1.5 as a runtime dependency (its three dependencies, `@babel/core`
  8.0.1, `yargs` 18.1.0 and `tinyglobby` 0.2.17, are **already in the tree** through
  `@angular/build`); the `$localize` runtime in the initial bundle (NOT MEASURED; measure in
  36.3b against the 46 kB headroom); an Arabic catalogue chunk downloaded only by Arabic
  sessions; a strict bootstrap order (3.2).
- Risk: **medium-low**. The known pitfalls are listed with their mitigations in 3.13.

### C. Runtime library (Transloco 8.4.0)

How: `provideTransloco()` with a loader for `en.json` and `ar.json`; templates use
`{{ 'inbox.takeOver' | transloco }}` or `*transloco`; ICU plurals need the separate
`@jsverse/transloco-messageformat` plugin, which pulls in `@messageformat/core`.

```html
<button type="button">{{ 'inbox.takeOver' | transloco }}</button>
```

- Gains: language switch without a reload; lazy per-scope files.
- Cost: three or more new runtime packages (`@jsverse/transloco`, `@jsverse/transloco-utils`, a
  pinned prerelease `@jsverse/utils@1.0.0-beta.5`, plus the messageformat plugin and
  `@messageformat/core`), and probably `transloco-keys-manager` for extraction. Templates switch
  from English text to keys, so every spec that asserts visible text needs a testing module that
  loads English. Module-level constants such as `NAV_ITEMS` turn into keys translated at render
  time.
- Risk: **medium**. More supply-chain surface (one prerelease), a larger test churn, and a
  runtime that a no-reload switch does not justify, since a reload is acceptable here.

### D. In-house signal service, JSON catalogues and an `Intl`-backed pipe

How: a `LocaleService` with `lang = signal<'en' | 'ar'>()`, `en.json` and `ar.json` keyed by
ID, a `t` pipe (`{{ 'inbox.takeOver' | t: 'Take over' : locale.lang() }}`), and `Intl` pipes.

```ts
@Pipe({ name: 't' })
export class TranslatePipe implements PipeTransform {
  private readonly catalogue = inject(CATALOGUE);
  transform(id: string, fallback: string, _lang?: string): string {
    return this.catalogue()[id] ?? fallback;
  }
}
```

- Gains: no dependency; a no-reload switch is possible.
- Cost: we would build and maintain extraction (nothing finds keys in templates for us), ICU
  plural and select parsing for six Arabic categories, attribute translation and placeholder
  validation. Every template string becomes an ID plus a duplicated English fallback.
- Risk: **medium-high**. It is the most code we would own, for a problem the framework already
  solves.

### Comparison

| | A build-time | B runtime `$localize` | C Transloco | D in-house |
|---|---|---|---|---|
| New packages | 1 (`@angular/localize`, dev) | 1 (`@angular/localize`, runtime) | 3-5 | 0 |
| URL or edge change | every URL; `deploy/` + backend | none | none | none |
| Language switch | navigate to other prefix | reload | live | live |
| Templates keep English text | yes | yes | no (keys) | no (keys + fallback) |
| Existing specs untouched | yes | yes (polyfill only) | no | no |
| ICU plurals (6 Arabic forms) | built in | built in (templates) | plugin | write it |
| Extraction | `ng extract-i18n` | `ng extract-i18n` | keys manager | write it |
| Missing-ID gate | build option | own check (3.9) | keys manager | own check |
| Runtime cost | none | small, initial (NOT MEASURED) | library | small |
| Risk | high | medium-low | medium | medium-high |

## 3. Recommendation: Option B

B is the only option that keeps every URL, the edge, the CSP pipeline, the embed route and the
backend untouched while using the framework's own extraction, ICU plurals and custom IDs. The
documentation does not argue against it: `loadTranslations` is documented for runtime
translation, `ng add --use-at-runtime` exists for it, and the one limitation stated (no live
switch without a refresh) is one R2 already accepts. The arguments against B are practical, not
documentary, and each has a mitigation in 3.13: the strict bootstrap order, no ICU in
`$localize` in code, and the size of the runtime.

### 3.1 Package

- `@angular/localize`, **22.1.5**, the same version as the installed `@angular/core`,
  `@angular/common` and `@angular/compiler-cli`. Its `peerDependencies` pin
  `@angular/compiler` and `@angular/compiler-cli` to exactly `22.1.5`. MIT license, published
  2026-09-03.
- In `dependencies` (runtime `$localize`), written `^22.1.5` like its siblings. The lockfile
  resolves it to 22.1.5, and it is upgraded together with the other `@angular/*` packages,
  never on its own.
- It is installed with `npm install @angular/localize@22.1.5`, not `ng add`: `ng add` would
  resolve the latest version (22.2.3, mismatched with core 22.1.5). The three configuration
  edits that `ng add` makes are done by hand in 36.3b: `polyfills: ["@angular/localize/init"]`
  in the build options, and `types: ["@angular/localize"]` in `tsconfig.app.json` and
  `tsconfig.spec.json`.

### 3.2 Bootstrap order

`$localize` resolves a message the first time its tagged string is evaluated. `NAV_ITEMS` and
the route `title`s are evaluated when their modules are imported, so the catalogue must be loaded
**before** anything imports `app.config.ts`, `app.routes.ts` or `layout/navigation.ts`. Today
`main.ts` imports them statically. It becomes a small loader that imports the application
dynamically:

```ts
// src/main.ts -- no static import from ./app may ever be added here (a spec enforces it)
import { registerLocaleData } from '@angular/common';
import { loadTranslations } from '@angular/localize';

async function start(): Promise<void> {
  let lang: 'en' | 'ar' = document.documentElement.lang === 'ar' ? 'ar' : 'en';

  if (lang === 'ar') {
    try {
      const [catalogue, localeAr] = await Promise.all([
        import('./locale/messages.ar.json'),
        import('@angular/common/locales/ar'),
      ]);
      loadTranslations(catalogue.default.translations);
      registerLocaleData(localeAr.default, 'ar');
    } catch {
      // A failed chunk (deploy race, offline) must not leave a blank page: fall back to
      // English, and say so in the document so CSS and assistive technology agree.
      lang = 'en';
      document.documentElement.lang = 'en';
      document.documentElement.dir = 'ltr';
    }
  }

  const { bootstrap } = await import('./app/bootstrap');
  await bootstrap(lang);
}

start().catch((err) => console.error(err));
```

```ts
// src/app/bootstrap.ts -- the old main.ts body, now evaluated after translations exist
export function bootstrap(lang: AppLanguage): Promise<ApplicationRef> {
  return bootstrapApplication(App, {
    providers: [
      ...appConfig.providers,
      { provide: LOCALE_ID, useValue: lang },
      { provide: APP_LANGUAGE, useValue: lang },
    ],
  });
}
```

- An `async` function, not top-level `await`, so the order does not depend on how the bundler
  treats top-level `await`.
- Both imports are lazy chunks with hashed names. An English session downloads neither.
- Modules that must translate at import time keep data and labels apart: `NAV_GROUPS` stays the
  typed ID list (`'Work' | 'AI' | …`) and a separate `NAV_GROUP_LABELS: Record<NavGroup, string>`
  holds the `$localize` text, so a translation can never change a type or a route.
- A unit test reads `src/main.ts` (as `theme-startup.spec.ts` reads `index.html`) and fails on
  any static import from `./app/`.

### 3.3 Locale data and formatting

- `registerLocaleData(localeAr, 'ar')` (above) is required, not optional: Angular's ICU
  `plural` in templates picks the category from the locale data's plural function, and the
  date, number and currency pipes read their symbols from it. Angular's `ar` data uses Latin
  separators, the month names يناير … ديسمبر, a Saturday week start and **EGP as its default
  currency**, so currency is always passed explicitly from the API's ISO code.
- One formatting service owns the digit and calendar policy:
  `core/i18n/locale-format.ts`, with `Intl` formatters cached per language and
  `numberingSystem: 'latn'`, `calendar: 'gregory'` always set. Pipes `localDate`,
  `localNumber` and `localCurrency` sit on top of it. The 24 template pipe uses and the
  `undefined`/`'en'` call sites move to it page by page as each page is rebuilt (rule 11), not in
  one sweep.
- `dateStyle: 'medium'` is numeric in Arabic (`18‏/10‏/2026`, measured), so the service uses
  explicit fields (`day: 'numeric', month: 'long', year: 'numeric'`), giving
  `18 أكتوبر 2026` and `Oct 18, 2026`.
- Time zone: unchanged (the browser's), as today.
- Locale tag: `ar` with explicit options, never `ar-SA` or `ar-EG`, whose defaults differ
  (measured above).

### 3.4 The language switch

- Lives in the account menu (`layout/profile-menu`), as a "Language" group of `menuitemradio`
  items next to the existing "Theme" group: "English" and "العربية". Each item carries its own
  `lang` and `dir`, so it is pronounced and shaped correctly in either interface.
- Choosing a language writes `omniai.lang` (`'en'` or `'ar'`) through the existing
  `core/storage/local-storage.ts` wrapper and calls `location.reload()`. The reload goes through
  the existing `protectUnsavedChanges` `beforeunload` protection, so a dirty form still asks
  before anything is discarded. The URL, and with it the page in the URL, is kept.
- With no stored choice, the admin follows the browser: the first entry of
  `navigator.languages` that is Arabic or English decides, with English as the fallback. An
  explicit choice always wins.
- The Arabic label "العربية" is in the menu, which is rendered only when opened, so an English
  page never downloads the Arabic font until somebody opens that menu.

### 3.5 The pre-paint `lang`/`dir` script and its CSP hash

> **As built (36.3b): one script, not two.** The phase brief required extending the existing
> theme script rather than adding a second; the language half below lives inside it, with its own
> `try`/`catch`. See 11.1.

A second inline script in `<head>`, after the theme script, with the same constraints as D10
(synchronous, ES5, `try`/`catch`, coupled to the TS constants and asserted by a spec that reads
`index.html`):

```html
<script>
  (function () {
    var lang = 'en';
    try {
      // Public surfaces follow the visitor, never a stored admin choice.
      var visitor = /^\/(chat|embed)\//.test(location.pathname);
      var stored = visitor ? null : localStorage.getItem('omniai.lang');
      if (stored === 'en' || stored === 'ar') {
        lang = stored;
      } else {
        var prefs = navigator.languages || [navigator.language];
        for (var i = 0; i < prefs.length; i++) {
          var p = String(prefs[i]).toLowerCase();
          if (p === 'ar' || p.indexOf('ar-') === 0) { lang = 'ar'; break; }
          if (p === 'en' || p.indexOf('en-') === 0) { break; }
        }
      }
    } catch (_) {}
    document.documentElement.lang = lang;
    document.documentElement.dir = lang === 'ar' ? 'rtl' : 'ltr';
  })();
</script>
```

- **CSP: no `deploy/` change.** `csp-hashes.mjs` already hashes every inline script in the built
  `index.html` (`matchAll` over `<script>` without `src`) and fails the image build if none is
  found. A second script adds a second hash to `$spa_script_hashes`, which both `location /` and
  `location /embed/` use. The edge image is rebuilt from the same `index.html`, so the hashes
  cannot go stale.
- A separate script rather than an edit to the theme script, so each one has its own spec
  and either can change without touching the other's coupling.
- The static boot status in `<app-root>` ("Loading OmniAI…") cannot be rewritten from `<head>`
  (the body does not exist yet), and `innerHTML` is out. Both strings are written in the markup,
  each with its own `lang`, and one inline `<style>` rule (allowed: `style-src 'unsafe-inline'`)
  hides the one that does not match `html[lang]`.
- `<title>` stays "OmniAI Admin" until `AppTitleStrategy` sets the translated title.
- In the widget iframe the document's `navigator.languages` is the visitor's own browser, so R4
  needs nothing from the loader.

### 3.6 Translation files: location and format

```
frontend/src/locale/
  messages.json      # extracted English source (committed, regenerated, never hand-edited)
  messages.ar.json   # Arabic: { "locale": "ar", "translations": { "<id>": "<text>" } }
  GLOSSARY.md        # the terminology in section 6, kept beside the files translators edit
```

- **JSON** ("simple JSON"), because `loadTranslations` takes exactly that `Record<id, text>`
  shape (the API page describes the same format, with `{$PLACEHOLDER}` placeholders), so the
  catalogue is consumed without a converter.
- Arabic is imported as a module (`import('./locale/messages.ar.json')`), so the build emits it as
  a content-hashed chunk: cached for a year by the edge's `.js` rule, allowed by
  `script-src 'self'`, no unhashed `.json` fetch (which `location /` would serve `no-cache`).
  **(JSON chunking: not in the docs; prove in 36.3b.)**
- One catalogue for admin and public surfaces. If its gzip size passes 30 kB (it would weigh on
  an Arabic visitor opening the widget), it is split by ID prefix (`chat.*`, `embed.*`,
  `common.*`) and `loadTranslations` is called with only what the surface needs.

### 3.7 Message IDs

- Every message has a custom ID: `@@<area>.<element>[.<detail>]`, lower camel case per segment
  (`@@nav.overview`, `@@inbox.takeOver`, `@@customers.list.empty`, `@@common.action.retry`).
  The docs warn that duplicate custom IDs keep only the first message, so the check rejects
  duplicates.
- A `meaning|description` is given whenever the English is ambiguous or one word means two
  things (`i18n="status of an agent|@@agents.status.active"`).
- **Arabic adjectives agree in gender**: an agent (وكيل) is مفعّل, a channel (قناة) is مفعّلة.
  So status words are never shared across entities: `@@agents.status.active` and
  `@@channels.status.active` are two messages even when the English is identical.
- An ICU expression is always the **whole** content of its `i18n` element. A nested ICU inside
  other text is extracted as a separate message with a generated ID, which the check would
  reject. **(Nested-ICU ID behaviour: not in the docs; prove in 36.3b.)**

### 3.8 Extraction command

```
ng extract-i18n --format json --output-path src/locale
```

That is `npm run i18n:extract` in `frontend/package.json` (36.3b), with
`"i18n": { "sourceLocale": "en" }` on the project so the extracted file says `en`.

### 3.9 The untranslated-ID check

> **As built (36.3b):** the errors below run as a spec inside `npm run test:ci`
> (`src/locale/i18n-catalogue.spec.ts`, file reads only); `npm run i18n:check` re-extracts, fails
> on a stale `messages.json` and prints the warnings. See 11.4.

`npm run i18n:check` runs a dependency-free Node script (`frontend/scripts/i18n-check.mjs`)
that compares `messages.json` with `messages.ar.json`. Errors (exit 1):
- an ID in the source and missing from Arabic, or an Arabic value that is empty;
- a generated (non-`@@`) ID, or a duplicate ID;
- a placeholder set that differs between source and translation (`{$INTERPOLATION}`,
  `{$START_TAG_SPAN}` …);
- an Arabic plural ICU without all six categories (`zero`, `one`, `two`, `few`, `many`,
  `other`; an `=n` case may replace one), or with broken ICU syntax;
- markup in a translation that the source does not have (translations are data, never HTML).

Warnings (reported, exit 0):
- an Arabic value identical to the English, unless the ID is in the allowlist
  (`OmniAI`, codes);
- a glossary term in the English whose glossary Arabic is missing from the translation;
- ASCII `?`, `,` or `;` after Arabic text (use ؟ ، ؛).

It runs in the verification ladder of every phase from 36.3b on, next to
`format:check`, `test:ci` and `build`. The repository has no CI workflow; when one exists, the same
script becomes a blocking step.

### 3.10 Plurals and counts

> **As built (36.3b):** the shared pager owns the counted phrase per item kind
> (`items="customers"`), chosen in TS with `pluralMessage` and six messages per kind; no template
> ICU was needed yet. The Arabic table below is what ships. See 11.3.

- Templates use ICU. A shared pager that shows no noun needs no plural
  (`21–40 of 57` and `21–40 من 57`). A list that names its noun passes its own message, because
  the Arabic noun changes with the count:

```html
<p class="pager-summary" aria-live="polite" i18n="@@customers.pager.summary">
  {total, plural,
    one {{{ first | localNumber }}–{{ last | localNumber }} of 1 customer}
    other {{{ first | localNumber }}–{{ last | localNumber }} of {{ total | localNumber }} customers}}
</p>
```

| Category | Example total | Arabic (range isolated LTR, see 4.6) |
|---|---|---|
| zero | 0 | لا يوجد عملاء |
| one | 1 | 1–1 من عميل واحد |
| two | 2 | 1–2 من عميلَين |
| few | 3-10 | 1–5 من 5 عملاء |
| many | 11-99 | 21–40 من 57 عميلًا |
| other | 100-102, 200 … | 1–20 من 100 عميل |

- `$localize` in TS has no ICU (the docs show ICU only in templates, and ternaries of separate
  `$localize` calls in code). Counts move into templates wherever possible. Where a sentence must
  be built in TS (document titles, toasts, live-region text), a helper `pluralMessage(count,
  forms)` picks one of six `$localize` messages with `Intl.PluralRules` for the current language.
  The English source declares all six IDs (`.zero` … `.other`, the English text repeating the
  `one`/`other` wording), so Arabic can translate each.

### 3.11 Unit tests in both languages

> **As built (36.3b): no second test configuration.** The Arabic renders live in the normal run
> (`core/i18n/arabic-render.spec.ts`), which loads the catalogue, resets the module registry, and
> imports what it renders dynamically; it resets the registry again on the way out. See 11.5.

- English: the 65 files and 801 tests keep their wording. The unit-test builder initialises "the
  application's polyfills and the Angular TestBed … before" any setup file (its schema), so adding
  `@angular/localize/init` to the build polyfills defines `$localize` in tests, and with no
  translations loaded it passes the English through.
- Arabic: a second test configuration, `npm run test:ci:ar`
  (`ng test --no-watch --configuration=ar`), with `setupFiles: ["src/testing/arabic.setup.ts"]`
  (which calls `loadTranslations` and `registerLocaleData` before any spec module is evaluated)
  and `include: ["src/**/*.ar.spec.ts"]`. A separate run, because Angular resolves a template's
  messages once per component definition and module constants once per import, so English and
  Arabic cannot share one module graph.
- Arabic specs assert: translated text on the page under test, `lang`/`dir`, plural categories
  (0, 1, 2, 3, 11, 57, 100), Western digits, and that no English fallback is visible.
- Pure units: `locale-format.spec.ts` (digits and calendar for `en` and `ar`, measured strings
  from section 0), `plural-message.spec.ts`, and `locale-startup.spec.ts`, which runs the
  pre-paint script from `index.html` against a fake `localStorage` and `navigator`: stored `ar`,
  stored `en`, a public path ignoring storage, `navigator.languages = ['ar-SA']`,
  `['fr', 'en']`, and a throwing `localStorage`.

### 3.12 Chrome checks in both languages

- Interactive checks through the Chrome DevTools MCP. Bulk screenshots through the Phase 33.5
  style dependency-free CDP script (Node 24 `WebSocket`), into `<evidence>\p36-<N>\`.
- Admin: set `localStorage['omniai.lang']` per run, reload. Matrix: en-LTR and ar-RTL x 360 and
  1280 px x light and dark, plus keyboard paths and 200% zoom on rebuilt pages.
- Public surfaces: launch Chrome with `--lang=ar` and `--lang=en-US` (proved in this phase:
  `navigator.languages` becomes `["ar","en-US","en"]`), with a stored admin choice of the other
  language to prove R4.
- Pre-paint: `Page.addScriptToEvaluateOnNewDocument` records `lang`/`dir` at
  `DOMContentLoaded`, before the bundle has run (R3).
- Untranslated text: on Arabic pages, a script lists visible text nodes with Latin letters that
  are not inside `dir="ltr"`/`<bdi>` islands or the allowlist (brand names). Heuristic: a
  native reader still reviews.
- CSP: the dev server sends no CSP, so 36.3b runs `deploy/edge/csp-hashes.mjs` on the built
  `index.html` and serves `dist/` from a scratch static server (in the evidence folder) that
  sends the same policy, then checks Chrome's console for CSP violations on `/`, `/chat/…`
  and `/embed/…`. No `deploy/` change.

### 3.13 Risks of B and their mitigations

| Risk | Mitigation |
|---|---|
| A static import added to `main.ts` makes module-level `$localize` resolve to English | spec on `main.ts` (3.2); labels split from data |
| Initial budget: the `$localize` runtime and Angular's i18n template runtime enter the initial bundle; headroom is about 46 kB | measure in 36.3b on the scaffold before any page is translated; `@angular/localize/init` only, no extra helpers in the initial chunk; if the warning is approached, report rather than raise the budget |
| Arabic catalogue fails to load | fall back to English and reset `lang`/`dir` (3.2) |
| Nested ICU gets a generated ID | ICU is the whole message (3.7); the check rejects generated IDs |
| JSON dynamic import not emitted as a hashed chunk | 36.3b spike; fallback: a generated `messages.ar.ts` (`export default {…}`) |
| Translator-supplied text | rendered through Angular's i18n (text nodes) and interpolation, never `innerHTML`; the check rejects markup the source lacks |

### 3.14 Performance budget for this decision

| Item | English session | Arabic session |
|---|---|---|
| `$localize` runtime (initial) | NOT MEASURED (36.3b) | same |
| Arabic catalogue chunk | 0 | target 30 kB gzip or less (measured in 36.3b, then per phase) |
| Angular `ar` locale data chunk | 0 | 2.7 kB raw source (`locales/ar.js`) |
| Fonts | Instrument Sans Latin 57 kB only (unchanged) | plus IBM Plex Sans Arabic 400/500/600, 43-46 kB each, only for glyphs on screen (`unicode-range`) |
| Extra requests before first paint | 0 | 2 lazy chunks, in parallel, before bootstrap; the boot status covers them |

## 4. RTL rules for this codebase

### 4.1 Logical properties (already the rule)

- `margin-inline-*`, `padding-inline-*`, `inset-inline-*`, `border-inline-*`,
  `border-start-start-radius` …, `text-align: start | end`. The scan in this phase found no
  physical property in `src/`; it is repeated in each phase report.
- Direction comes only from `html[dir]`. No `direction: rtl` in component styles, ever.
- `bootstrap-grid.min.css` stays the only Bootstrap file; its directional utilities
  (`ms-*`, `me-*`, `text-start` …) are not used and must not be, since they flip only with
  Bootstrap's RTL build.
- A physical value is allowed only as an explicit RTL override that names `[dir='rtl']`, like
  the select caret in `_primitives.scss:682`.

### 4.2 `--direction-flip` for transforms

`--direction-flip` is `1`, and `-1` under `[dir='rtl']` (`_tokens.scss:356-360`). Every transform
or shadow offset that moves along the inline axis multiplies by it
(`translateX(calc(var(--direction-flip) * -100%))`). Already used for the sidebar drawer, the
current-item edges, the switch thumbs and the row-chevron hover nudge; every new one follows.

### 4.3 Icons

- Mirror in RTL (one `MIRRORED_ICONS` set in `shared/icon/`; the host gets a class that applies
  `transform: scaleX(var(--direction-flip))`): `arrow-start`, `arrow-end`, `chevron-start`,
  `chevron-end`, `sign-out`, and any future `send`, `reply`, `undo`/`redo` or "open in"
  arrow. Their names already say start and end; the glyph has to follow.
- Never mirror: the mark (`app-brand-mark`), the voice glyphs `ai` and `person`, channel glyphs,
  `check`, `alert`, `critical`, `info`, `search`, `refresh`, `download`, `plus`, `close`,
  `menu`, `more`, `chevron-down`, `switch` (symmetric), `sun`, `moon`, `monitor`, `bell`, `pin`,
  and any media control or clock.
- An icon-only control keeps its accessible name on the control (existing rule), translated.

### 4.4 Charts

- The Analytics columns and channel bars are CSS flex and widths, so they follow the inline
  direction: in Arabic, time runs right to left (the oldest day at the right), horizontal bars
  grow from the right, and the legend order mirrors. This is intended, matching reading order.
  Numbers and axis values stay Western digits (5.4).
- Voice split bar: the AI part sits at inline-start (right in Arabic). Voice track: the real
  sequence of reply authors in inline order (the first reply at the right in Arabic).
- Tooltips (`title="label: total"`) become one translated message with placeholders, never
  concatenation.
- The screen-reader sentence each chart already carries is translated with the chart.

### 4.5 User content: `dir="auto"`

On every element whose only content is user or customer data: customer, member, agent and
workspace names, message bodies, knowledge titles and text, report and schedule names, role
names, and free-text inputs and textareas (`dir="auto"` on the field). Inside a sentence, user
data goes in `<bdi>` (`Assigned to <bdi>{{ name }}</bdi>`).

### 4.6 LTR islands: `<bdi dir="ltr">` / `dir="ltr"`

- Display: e-mail addresses, URLs and origins (the widget allowlist), phone numbers (WhatsApp,
  E.164 `+966 …`), public keys, IDs and GUIDs, permission codes, hex colours (the widget accent),
  version strings, file names that are Latin.
- Inputs whose value is inherently LTR (`type="email"`, `url`, `tel`, keys, origins, colours)
  get `dir="ltr"` in both languages.
- Numeric ranges: `formatRange` returns a bare `21–40` (measured), and inside an RTL paragraph
  the bidi algorithm can display it reversed. A range is always wrapped
  `<bdi dir="ltr">21–40</bdi>`. **Native review in 36.3b.**
- Arabic-Indic digits typed into a numeric field are normalised to ASCII before validation.

### 4.7 Numbers in tables

`font-variant-numeric: tabular-nums` (already the rule), `text-align: end` (so numeric columns
mirror to the left edge in Arabic). Values come from `localNumber`; units, percentages and
currency come from `Intl` output, never concatenated by hand (Arabic percent output carries its
own direction marks, measured).

## 5. Arabic typography

### 5.1 Roles and sizes (from `MASTER.md` section 4, already tokens under `:lang(ar)`)

| Role | Arabic (IBM Plex Sans Arabic) | Latin, for comparison (Instrument Sans) |
|---|---|---|
| Display | same size as Latin, width 100%, tabular | 600, `clamp(28px, …, 40px)` / 1.1, width 86% |
| Page title | 600, `clamp(24px, …, 30px)` / 1.4, tracking 0 | 620, / 1.2, width 86%, −0.015em |
| Section title | 600, 18px / 1.45 | 600, 18px / 1.35 |
| Card title | 600, 15px / 1.5 | 600, 15px / 1.4 |
| Body | 400, 15px / 1.7 | 400, 14px / 1.55 |
| Reading (transcripts) | 400, 15px / 1.75 | 400, 15px / 1.6 |
| Small | 400, 14px / 1.65 | 400, 13px / 1.5 |
| Caption | 500, 13px / 1.5 | 500, 12px / 1.35 |
| Numeric | 550, 14px / 1.3, tabular | same |

Arabic is a step larger with more leading because, at the same size, it reads smaller. Weights are
400, 500 and 600 only (the files shipped). A bold role asks for 600.

### 5.2 No letter-spacing

`--letter-spacing-tight` and `--letter-spacing-caps` are `0` under `:lang(ar)`. Any new tracking
uses those tokens, never a literal, because tracking breaks cursive joining. No uppercase,
small caps, italics or narrowed widths for Arabic; emphasis by weight or colour.

### 5.3 Line-height, truncation and wrapping

- Line-height: the role tokens above. Headings at least 1.35. Controls keep their height
  tokens; their label line box must not clip marks above and below the letters (checked with
  real Arabic labels in 36.3b, not with English stand-ins).
- Truncation: single-line `text-overflow: ellipsis` only on rows and chips whose full value is
  available elsewhere (detail page or `title`). The ellipsis falls at inline-end, so it follows
  the element's own direction (`dir="auto"` on user content puts it on the correct side). Page and
  section titles never truncate; they wrap. Multi-line clamps for previews only.
- Wrapping: `overflow-wrap: anywhere` only on LTR islands (URLs, e-mails, keys). No
  `hyphens` and no `text-align: justify` for Arabic (justification stretches with kashida and
  breaks the rhythm). `text-wrap: balance` on headings as an enhancement.
- Arabic copy is often 20-30% longer than English, sometimes shorter: layouts are checked with the
  real translation, never assumed to fit.

### 5.4 Numerals (digit policy, confirming the 36.2 log entry)

**Western digits (0-9) in both languages, everywhere, including the public surfaces**:
`numberingSystem: 'latn'` and `calendar: 'gregory'` on every Arabic formatter.
- Chromium's defaults differ by region (`ar`/`ar-AE` Western, `ar-SA`/`ar-EG` Arabic-Indic,
  measured), so leaving it to the runtime would make one workspace show two digit systems
  depending on who opened it.
- The admin is dense data in tabular figures. Western digits fall in the Latin `unicode-range`
  and render in Instrument Sans tabular, so columns line up identically in both languages.
- Machine values (IDs, phone numbers, codes, URLs) are Western anyway; one system avoids two
  digit systems on one screen.
- Gulf business software commonly uses Western digits (it is the `ar-AE` default).
- Cost: readers used to Arabic-Indic digits (Egypt, formal Saudi documents) see Western ones. If
  a workspace needs Arabic-Indic digits, that is a per-workspace setting, which is a backend ask
  (section 8), not a per-browser one.

### 5.5 Mixed Arabic and Latin names

- Each name is isolated (`dir="auto"` alone, `<bdi>` in a sentence), so "Sara Al-Harbi" inside an
  Arabic sentence, or "سارة الحربي" inside an English one, keeps its punctuation in place.
- Latin characters inside Arabic text keep Instrument Sans and Arabic inside English keeps Plex
  Sans Arabic (per-face `unicode-range`); no font is forced per element.
- Brand names: `OmniAI` stays Latin (isolated). Channel names use the Arabic forms Meta uses
  (section 6).
- `lang="en"` on an English fragment inside Arabic (and the reverse) when it is a phrase rather
  than a name, so screen readers switch voice.

## 6. Starter glossary (refined)

Native review is required before 36.3b ships. Rows marked * were changed or added in this
phase.

| English | Arabic | Note |
|---|---|---|
| Overview | نظرة عامة | |
| Conversations | المحادثات | singular محادثة |
| Inbox | صندوق المحادثات | not صندوق الوارد (reads as e-mail) |
| Customers | العملاء | singular عميل; counts per 3.10 |
| AI Agents | وكلاء الذكاء الاصطناعي | singular وكيل ذكاء اصطناعي; "agent" alone: وكيل |
| Knowledge | المعرفة | nav label; page title قاعدة المعرفة* |
| Channels | القنوات | singular قناة (feminine) |
| Website chat | دردشة الموقع | |
| Analytics | التحليلات | |
| Reports | التقارير | |
| Team | الفريق | |
| Roles & permissions | الأدوار والصلاحيات | |
| Plan & usage | الباقة والاستخدام | |
| Billing | الفوترة | |
| Notifications | الإشعارات | |
| Workspace | مساحة العمل | |
| Take over | استلام المحادثة | button, verbal noun |
| Return to AI | إعادتها إلى الذكاء الاصطناعي | button; ها = the conversation |
| Handled by AI | يردّ الذكاء الاصطناعي | |
| Handled by a person | يردّ أحد أعضاء الفريق | |
| Draft | مسودة | |
| Active | مفعّل | agrees with its noun: مفعّلة for a channel* |
| Disabled | معطّل | agrees with its noun: معطّلة for a channel* |
| Sign in | تسجيل الدخول | |
| Sign out | تسجيل الخروج | |
| AI (voice chip)* | الذكاء الاصطناعي | the Lagoon voice |
| Person (voice chip)* | الفريق | the Ember voice; gender-neutral, "a person on the team" |
| Member* | عضو | plural أعضاء |
| Invitation* | دعوة | |
| Role* | دور | |
| Permission* | صلاحية | |
| Handoff* | التحويل إلى الفريق | |
| Transcript* | نص المحادثة | |
| Document (knowledge)* | مستند | |
| Indexing* | الفهرسة | |
| Widget* | نافذة الدردشة | the embedded chat |
| Hosted chat page* | صفحة الدردشة المستضافة | |
| Allowed websites* | المواقع المسموح بها | |
| Usage limit* | حد الاستخدام | |
| Subscription* | الاشتراك | |
| Invoice* | فاتورة | |
| Export* | تصدير | |
| Schedule* | جدولة | |
| Unread* | غير مقروءة | for notifications (feminine plural) |
| Save changes* | حفظ التغييرات | |
| Cancel* | إلغاء | |
| Delete* | حذف | |
| Retry* | إعادة المحاولة | |
| Previous / Next* | السابق / التالي | |
| Loading…* | جارٍ التحميل… | |
| Page not found* | الصفحة غير موجودة | |
| Nothing was lost* | لم يُفقد شيء | |
| Theme* / Light / Dark / System | المظهر / فاتح / داكن / حسب النظام | |
| Language* | اللغة | |
| WhatsApp / Instagram / Messenger* | واتساب / إنستجرام / ماسنجر | Meta's Arabic forms |
| OmniAI* | OmniAI | never translated; isolated |

Punctuation in Arabic copy: ، ؛ ؟ and «» for quoted names.

## 7. What changes, in order (for 36.3b onwards, after approval)

1. **36.3b scaffold** (one gated phase): the package; polyfill and tsconfig types; `main.ts` and
   `bootstrap.ts`; the pre-paint script and boot status; `core/i18n/` (language token, locale
   format service, pipes, `pluralMessage`, mirrored icons); the language switch; `src/locale/`
   with extraction, `GLOSSARY.md` and the check; `test:ci:ar`; the shell (navigation,
   sidebar, topbar, account menu, titles) translated as the first real slice; the spikes named
   in 3.6, 3.7 and 3.13; the bundle measured against the 46 kB headroom.
2. **Each redesign phase after it** translates the pages it rebuilds, once, with Arabic in the
   same change (standing rule 11), and moves those pages' formatting to the locale service.
3. Untouched pages stay English inside an Arabic shell until rebuilt. The check reports that as a
   count, not a failure, until the page's phase; that exception is recorded in each phase report
   and ends with the last redesign phase.

## 8. Backend asks (none is required by option B; each closes a gap B cannot)

| Ask | Why |
|---|---|
| Notifications: render from `kind` plus structured parameters, or localise server-side by a member language | `title`/`body` are English sentences built in `NotificationCatalog.cs`; an Arabic member sees English notifications until then |
| Problem Details: a stable `code` on every error whose `detail` is shown | `api-error.ts` shows some `detail` text verbatim; with a code the frontend shows its own translated message and keeps `detail` only as a fallback |
| Validation messages keyed by code | field errors from the server are English |
| Widget loader: translate `frame.title` ("Chat") from `navigator.language`, and choose the launcher side from the host page's `dir` | both live in `Widget/v1.js` on the customer's page, outside the iframe |
| Member language preference endpoint | e-mails (invitations, scheduled reports) and server-built text need a language the server can see; D22 has no preference endpoint |
| Report exports: localised column headers | CSV/file headers are server-written English |
| Per-workspace digit preference (only if a tenant asks) | 5.4 |

## 9. Verification in this phase

| Check | Result |
|---|---|
| `npm run format:check` | PASS (no product file changed; this document is outside `frontend/`) |
| `npm run test:ci` | PASS: 65 files, 801 tests |
| `npm run build` | PASS: initial 454.05 kB raw / 111.07 kB transfer; one budget warning that predates this phase (`embed-chat.scss` 4.07 kB > 4 kB warning) |
| `Intl` probe | PASS: Chrome 155 (DevTools MCP and headless, `--lang` en-US and ar), Node 24.19 / ICU 78.3; output in the evidence folder |
| Angular docs | read on angular.dev (v22.2.3); Angular CLI MCP not connected |
| Spikes named in 3.6, 3.7, 3.13 | NOT RUN: they need `@angular/localize`, which is not installed until approved |

## 10. Decisions (accepted 2026-10-18, product owner)

| # | Decision | Where it applies |
|---|---|---|
| 1 | **Option B**: `@angular/localize` with runtime `loadTranslations()` before bootstrap, one build; switching language reloads | section 3 |
| 2 | **`@angular/localize@22.1.5`** approved, in `dependencies` as `^22.1.5` (lockfile 22.1.5), installed with `npm install @angular/localize@22.1.5`, **not** `ng add`. Not installed in 36.3a; Phase 36.3b installs it | 3.1 |
| 3 | **Western digits everywhere**, admin and public surfaces: every Arabic formatter sets `numberingSystem: 'latn'` and `calendar: 'gregory'`. This confirms the "Western digits in Arabic" entry in the `MASTER.md` decisions log (marked "confirm in 36.3"); the log row is updated in 36.3b | 3.3, 5.4 |
| 4 | **A member with no saved choice follows the browser's language** (first Arabic or English entry of `navigator.languages`, English as the fallback); an explicit choice always wins. Public surfaces always follow the visitor | 3.4, 3.5 |
| 5 | **Charts mirror in Arabic**: time runs right to left, bars grow from inline-start, legends follow the inline order; digits stay Western | 4.4 |
| 6 | **Voice chips**: AI is **الذكاء الاصطناعي**, Person is **الفريق** | section 6 |

## 11. As built in Phase 36.3b (where it differs from, or proves, the plan)

Status: **implemented** for the shell, the shared components and every shared string. Page bodies
are translated in their own phases (rule 11 of the phase brief).

### 11.1 Startup

- `main.ts` loads `src/locale/messages.ar.json` and `@angular/common/locales/ar` only when
  `<html lang="ar">`, then imports `app/bootstrap.ts`, which provides `APP_LANGUAGE` and
  `LOCALE_ID`. `core/i18n/main-startup.spec.ts` fails on any static import from the app.
- **One inline script** (the brief overrode 3.5): the theme script in `index.html` now also sets
  `lang` and `dir`, in a second guarded half, so the edge still allows exactly one inline-script
  hash. `theme-startup.spec.ts` reads the real `index.html`, asserts there is one inline script,
  and runs it against fake browsers for every theme and language branch.
- The boot status carries both languages; one inline `<style>` rule shows the matching one.
- **JSON chunking proved:** the Arabic catalogue is emitted as its own content-hashed `.js` chunk
  (3.6 spike). Measured cold start through the production edge, `/sign-in`, cache disabled:
  English 171 kB of JavaScript transferred (34 files), Arabic 180 kB (36 files); the catalogue is
  about 8 kB compressed, well under the 30 kB split threshold.
- **Budget caveat:** because the application is imported dynamically, Angular's `initial` budget
  now measures only `main` and the polyfills (274 kB raw against 454 kB before); the application
  itself loads one step later as a lazy chunk. The startup set is unchanged in substance but is no
  longer what the 500 kB budget guards. Budgets were not changed; adding a budget for the
  startup chunk is a follow-up for the owner to decide.

### 11.2 Formatting

`core/i18n/locale-format.ts` (functions and the `LocaleFormat` service) and the pure pipes
`localDate` / `localNumber` replaced every `DatePipe`, `DecimalPipe`, `Intl.*` and `toLocale*`
call in the application (24 template pipe uses in 10 templates, and about 20 TS calls in 12 files). Named styles: `date`, `longDate`,
`dateTime`, `dayMonth`, `dayMonthTime`, `monthYear`, `time`. English uses the plain `en` tag
(it previously followed the browser's region on some screens); Arabic uses `ar` with
`numberingSystem: 'latn'` and `calendar: 'gregory'`. Currency always comes from the data;
`currencyExponent` replaces the browser lookup in billing.

### 11.3 Plurals

The shared pager takes `items="customers" | "conversations" | …` and owns six whole phrases per
kind (`pager.<kind>.<category>`), picked with `pluralMessage`; the range is a `<bdi dir="ltr">`.
The Arabic `zero`, `one` and `two` forms drop the numeral (عميل واحد، عميلَين), which the guard
allows for those three categories only. The voice split bar takes `subject` instead of a free
English noun. English zero is handled explicitly where a "none" sentence exists (English has no
`zero` plural category).

### 11.4 The guard

- `src/locale/i18n-catalogue.spec.ts` (inside `npm run test:ci`): every `@@id` used in
  `src/app` has non-empty Arabic; no stale Arabic IDs; every `i18n` attribute and `$localize` has
  a custom ID; placeholders kept; six categories in any plural ICU; no markup added.
- `npm run i18n:check` (`frontend/scripts/i18n-check.mjs`): re-extracts into
  `node_modules/.cache/omniai-i18n`, fails on a stale `messages.json` or a missing translation,
  and warns on English left behind, ASCII punctuation after Arabic, and checked glossary terms
  (`src/locale/GLOSSARY.md`, with a Match column of stems).
- `npm run i18n:extract` regenerates and formats `messages.json`. Format first, then extract:
  Prettier's line breaks change the whitespace inside a message.

### 11.5 Tests

The unit-test runner shares one module graph across spec files, so the Arabic renders
(`core/i18n/arabic-render.spec.ts`) call `vi.resetModules()` before and after, and import what
they render dynamically; without the second reset, later English specs received modules
evaluated in Arabic. No `test:ci:ar` configuration was needed. The suite ran out of memory in
three workers on two runs while two dev servers and Chrome were open (V8 "Zone Allocation
failed"); with one dev server stopped it passed twice in a row.

### 11.6 RTL fixes the pass found

- Component styles that wrote `[dir='rtl'] .x` never matched under emulated encapsulation (the
  attribute selector is scoped too). The sidebar rail's clip and the page header's back arrow
  were silently LTR in Arabic. The sidebar now uses `:host-context([dir='rtl'])`; icon mirroring
  moved into `app-icon` (`MIRRORED_ICONS`, `scaleX(var(--direction-flip))`).
- The toast's four-value physical padding became `padding-block` / `padding-inline`.
- Identity lines in the account menu follow the page direction with the values isolated in
  `<bdi>`, so they align with the menu instead of the left edge.

### 11.7 Spikes from 3.13

| Spike | Result |
|---|---|
| Dotted custom IDs (`@@nav.overview`) | Work; extracted verbatim |
| JSON catalogue as a hashed chunk | Works (11.1) |
| Nested ICU IDs | Not exercised: no template ICU was needed in this phase |
| `$localize` in tests through the build polyfills | Works; the English suite needed no copy changes |
| Duplicate custom IDs | Extraction warns when the same ID has different whitespace; give each wrapped use its own ID |
