# Phase 36.3b: Arabic and RTL foundation

- Date: 2026-10-18
- Option implemented: **B**, `@angular/localize` with runtime `loadTranslations()` before
  bootstrap (approved in 36.3a; record: `docs/architecture/localisation-arabic-rtl.md`)
- Evidence folder: `C:\Users\mouay\Desktop\omniai-redesign-evidence\p36-03b\`
- Not committed. Proposed message in section 9.

## 1. Summary

- OmniAI now runs in Arabic with full right-to-left layout. Choose العربية under **Language** in
  the account menu, next to Theme. The choice is kept in this browser, and the page restarts in
  the new language. A member who never chose follows their browser.
- The shell is fully Arabic and mirrored: the sidebar and drawer open from the right, menus,
  notifications, page titles, back links, pagers (with correct Arabic plurals), dialogs, toasts,
  error messages and the unsaved-changes prompts. The Design Lab is fully Arabic.
- Every date and number in the product uses one formatter, with Western digits and the Gregorian
  calendar in both languages ("18 أكتوبر 2026").
- The hosted chat and the widget follow the visitor's own browser language. Their text, and every
  page body, is still English until each page's own phase (section 3).
- Language and direction are set before first paint, also behind the production edge with its
  CSP (checked: no violations).

## 2. Starting point

- Branch `master`, HEAD `1cb4036` ("docs: record the Arabic and RTL decision (Phase 36.3a)"),
  worktree clean. As the brief expected.
- Skills used: `frontend-pro-max:angular-enterprise`, `ui-ux-design-lead`,
  `design-system-tokens`, `frontend-testing-quality`, `frontend-security` (all present).

## 3. Changes by area

`git status --short`: 106 modified files, 4 new paths (`frontend/scripts/`,
`frontend/src/app/bootstrap.ts`, `frontend/src/app/core/i18n/`, `frontend/src/locale/`); 1,654
insertions and 635 deletions in tracked files. Nothing in `backend/`, `deploy/` or the database.

**Package (approved in 36.3a).** `@angular/localize` `^22.1.5`, resolved 22.1.5, in
`dependencies`, installed with `npm install`, not `ng add`. Its dependencies (`@babel/core`,
`yargs`, `tinyglobby`) were already in the tree; the lockfile only drops 60 `"dev": true` flags.
`npm` printed its `allow-scripts` notice for esbuild, lmdb and msgpackr-extract (already
installed); nothing was approved.

**Startup.** `main.ts` loads the Arabic catalogue and locale data only for `lang="ar"`, then
imports `app/bootstrap.ts` (which provides `APP_LANGUAGE` and `LOCALE_ID`). If the Arabic chunk
fails, the page falls back to English and resets `lang`/`dir`. `angular.json` adds the
`@angular/localize/init` polyfill and `sourceLocale: en`; both tsconfigs add the types.

**Pre-paint (`index.html`).** The existing theme script is extended, not duplicated (still one
inline script, one CSP hash). It sets `lang` and `dir` from the stored choice (`omniai.lang`),
otherwise from the browser's first Arabic or English preference, otherwise English. `/chat/` and
`/embed/` ignore the stored choice and follow the visitor. Each half has its own `try`/`catch`.
The boot status is written in both languages, and CSS shows the one matching `html[lang]`.

**`core/i18n/`.** `language.ts` (type, key, endonyms, `APP_LANGUAGE` read from `<html lang>`),
`language-service.ts` (store, then reload through a replaceable `PAGE_RELOAD`),
`locale-format.ts` (all formatting, `latn` + `gregory`), `locale-pipes.ts` (`localDate`,
`localNumber`), `plural.ts` (six-category `pluralMessage`).

**Translated shared parts (357 messages):**
- navigation labels and groups (group IDs kept apart from their words);
- the top bar, the account menu (new Language group) and the notification bell, panel, item,
  unread counts and relative times;
- page-header back links (the "Back to" prefix and all 32 back labels in the feature templates);
- the pager, with whole phrases per list kind and an LTR-isolated range ("21–40 من 57 عميلًا");
- the confirm-dialog defaults, the toast dismiss label;
- the voice chips (الذكاء الاصطناعي، الفريق), the voice track and split sentences;
- `core/api/api-error.ts`, the session-ended notice, both unsaved-changes prompts (the editors'
  duplicated reload prompt became one shared constant);
- every route title (the browser tab);
- the Design Lab.

**Formatting.** All 24 Angular `date`/`number` pipe uses and every `Intl`/`toLocale*` call in
features were replaced by the one formatter. English now uses the plain `en` tag everywhere;
some screens previously followed the browser's region.

**RTL fixes.**
- The sidebar rail's RTL clip and the back-arrow flip used `[dir='rtl'] .x`, which never matches
  under emulated encapsulation. The rail now uses `:host-context`.
- Directional icons (`arrow-*`, `chevron-start/end`, `sign-out`) mirror inside `app-icon`, through
  `MIRRORED_ICONS`.
- The toast padding became logical.
- The account-menu identity lines align with the menu, with their values isolated in `<bdi>`.

**Guard.**
- `src/locale/i18n-catalogue.spec.ts` runs in `npm run test:ci`. It fails on:
  - a used ID without Arabic, or a stale Arabic ID;
  - an anonymous message;
  - a lost or added placeholder;
  - an incomplete Arabic plural ICU;
  - markup in a translation.
- `npm run i18n:check` re-extracts. It fails on a stale `messages.json` or a missing translation,
  and warns on English left behind, ASCII punctuation and glossary terms (`src/locale/GLOSSARY.md`).
- `npm run i18n:extract` regenerates the source catalogue.

**Tests (+35, 801 → 836).**
- `theme-startup.spec.ts` now runs the real inline script against fake browsers: themes,
  languages, public paths, throwing storage, and exactly one inline script.
- New: `locale-format.spec.ts` (digits, calendar, plurals), `main-startup.spec.ts` (startup
  order), `arabic-render.spec.ts` (pager plurals, voice words, navigation, unread counts in
  Arabic) and the catalogue guard.
- Language tests in `profile-menu.spec.ts` cover endonyms with their own `lang`/`dir`, storing
  the choice and reloading, and no reload for the current language.

**Docs.**
- The decision record gained "As built" pointers and section 11.
- `MASTER.md`: digit row confirmed, voice names, the RTL commitments and two log rows.
- `admin-design-system.md`: D10 note, and the localisation row moved out of Deferred.
- The architecture index links the record.

**Page bodies still English (each translated in its own phase).** Every route body except the
Design Lab:
- overview; conversations (list and detail); customers (list and detail);
- AI agents (list, new, editor); knowledge (list, new, upload, document);
- channels, website chat (page and detail), WhatsApp (page and detail);
- analytics, reports and schedules;
- team (list, invite, member); roles (list, new, editor); plan and usage; billing;
- notifications and preferences; not found; workspace setup;
- sign in, create account, accept invitation;
- the hosted chat and the widget.

Inside those bodies, the shared parts are already Arabic: pager, back link, dates, numbers and
the tab title.

**Screenshots.**
- Before (English, 36.2): `p36-02\after\`.
- After: `p36-03b\gallery\` (232 captures, contact sheet `index.html`). Examples:
  `ar-overview-1440-light.png`, `ar-customers-390-dark.png`, `ar-design-lab-1440-light.png`,
  `visitor-ar-widget-open-1440-light.png`.
- Edge smoke: `p36-03b\csp\`.
- Interactive checks: `p36-03b\interactive\`.

## 4. Decisions

- **One inline script.** The brief overrode the record's plan for a second script; the record is
  updated (3.5, 11.1).
- **Pager owns its counted phrases** per list kind (`items="customers"`, six messages each), so
  pagers in untranslated pages are already Arabic. Rejected: a template ICU per list (one more
  edit per page, and the pager's own English sentence stayed noun-glued).
- **Arabic renders in the normal `test:ci` run** with `vi.resetModules()` on the way in and out.
  Rejected: a second `test:ci:ar` configuration, which wasn't needed.
- **Guard split** between a cheap spec in `test:ci` and an extracting `npm run i18n:check`.
  Extraction is a build, so it is kept out of every test run.
- **English date format fixed to `en`** for consistency with the existing `'MMM d, y'` screens.
  Rejected: following the browser's English region.
- **Hosted chat and widget text deferred** to their page phase, with a release blocker (section 8).
- Deferred: a budget for the startup chunk (11.1 of the record) and translating page bodies.

## 5. Verification

| Check | Result |
|---|---|
| `npm run format:check` | PASS |
| `npm run test:ci` | PASS: 69 files, 836 tests, 0 failures (final run). Two earlier runs lost 3 workers to "out of memory" while two dev servers and Chrome were open; the four runs after that all passed in full (one verbose, two plain, the final). |
| `npm run build` | PASS. Initial 274.21 kB raw / 64.65 kB transfer (the app is now a dynamic import, so this no longer includes it; see section 8). One warning, already present before this phase: `embed-chat.scss` 4.07 kB > 4 kB warning budget. No budget changed. |
| `npm run i18n:check` | PASS: 357 messages, 0 errors, 0 warnings |
| Chrome matrix, admin (CDP script, after the final catalogue) | PASS: 198 cells (33 routes × 1440/390 × light/dark in Arabic, plus English at 1440 light and 390 dark). Page overflow 0; console errors 0; exactly one `h1` everywhere; unnamed controls 0; `lang`/`dir` right at DOMContentLoaded; mirrored icons correct; no Latin left in the Arabic shell; Arabic tab title on every Arabic cell |
| Chrome matrix, public | PASS for behaviour: 32 cells. An Arabic visitor gets `ar`/`rtl` on `/chat/` and `/embed/` even with an English admin choice stored, and the reverse; `/sign-in` follows the stored choice; overflow 0. Console: only the signed-out `401` from `/api/auth/me`, as before. Widget on the host page renders mirrored. Text is still English (section 8). |
| CSP smoke through the edge image | PASS. `omniai-p36-edge:smoke`, built from `deploy/edge.Dockerfile`, with `deploy/` unchanged. The image's `$spa_script_hashes` holds one inline-script hash, equal to the hash of the local build's `index.html`. In Chrome through `https://localhost:8453`: 0 CSP violations for English and Arabic sign-in, Arabic and English overview after signing in through the edge, and an Arabic visitor on `/chat/` and `/embed/`. `lang`/`dir` were right at DOMContentLoaded in every case. Container, image and test certificate removed afterwards. |
| Startup cost (cold, through the edge) | English 171 kB JavaScript transferred (34 files); Arabic 180 kB (36 files; catalogue about 8 kB compressed) |
| Contrast | PASS: `contrast_check.py`, 79 pairs in light and in dark, 76 pass, 0 fail, 3 informational (unchanged tokens; no new colour pair) |
| Keyboard | PASS (Arabic, 1440): skip link first, then the sidebar; the rail opens as an overlay from the right on focus; ink focus ring visible. Menu arrow keys not re-run by hand (code unchanged; `menu.spec.ts` passes). |
| Reduced motion | PASS: `--duration-md` resolves to `0.01ms` under `prefers-reduced-motion: reduce` |
| RTL mirroring | PASS: sidebar rail on the right; drawer slides in from the right at 390 px (0 overflow); account menu opens on the inline-end side and fits; `rtl_logical_lint.py`: 0 warnings left in shared code (one fixed; 3 literal letter-spacings remain in page bodies) |
| Provider canary | PASS, twice: every provider and live-switch variable absent from `omniai-p36-api` (names only) |

## 6. See it live

- **http://localhost:4210/**, the dev server started in this phase with the new config.
  Sign in, then choose **Language → العربية** in the account menu (the avatar, top corner).
  - Design Lab: http://localhost:4210/design-lab
  - Hosted chat as a visitor: http://localhost:4210/chat/9veAaKzu0Ee-isA7jrQzYw, in a browser
    whose language is Arabic.
- **The old dev server on http://localhost:4200 is broken until restarted.** It was started
  before `angular.json` gained the `$localize` polyfill and now throws
  `$localize is not defined`. I was not permitted to stop it. Restart it in its terminal
  (Ctrl+C, then `npm start` in `frontend/`), or use 4210.
- Synthetic demo accounts:
  `C:\Users\mouay\Desktop\omniai-redesign-evidence\p36-01\demo-accounts.md`.

## 7. Backend asks

None blocks this phase. Each closes a gap the frontend can't:

| Ask | Why |
|---|---|
| Notifications rendered from `kind` plus parameters, or localised server-side | Titles and bodies are English sentences from `NotificationCatalog.cs`; they show isolated (`dir="auto"`) but in English |
| A stable `code` on every error whose `detail` is shown, and validation messages by code | `api-error.ts` shows some server `detail` text verbatim |
| Widget loader: translate `frame.title` ("Chat") and choose the launcher side from the host page's `dir` | Both live in `Widget/v1.js` on the customer's page |
| Member language preference endpoint | E-mails, scheduled reports and server text need a language the server can see (D22) |
| Localised report export headers | Server-written English |

## 8. Risks and open issues

1. **Release blocker: hosted chat and widget text.** An Arabic visitor gets a correctly mirrored
   layout with English copy, and the bidi algorithm moves its final punctuation (see
   `visitor-ar-widget-open-1440-light.png`). Nothing is deployed until 36.12, but these two
   surfaces must be translated before any deploy, or their page phase must come first.
2. **Mixed language in the admin until each page phase.** Members whose browser prefers Arabic now
   start in Arabic (the approved default) and see English page bodies inside an Arabic shell.
3. **Budget coverage.** The `initial` budget no longer covers the application's startup chunk
   (11.1 of the record). The cold start was measured instead (171 kB / 180 kB). Adding a budget
   for that chunk is your call; no budget was changed.
4. **Test memory.** `test:ci` lost workers to out-of-memory errors under heavy local load. Worth
   watching on smaller machines or CI.
5. **Arabic copy needs a native reader's review**, the glossary and the voice names especially.
6. **English dates** now use the `en` format everywhere ("Oct 18, 2026"), where some screens
   previously followed a non-US English browser's region.
7. The edge smoke image was built from the worktree before the back-link edits. `index.html` and
   `main.ts` were already final, so the CSP result holds; 36.12 re-checks the final build.
8. Observation: the edge readiness watcher expects IP upstreams; a hostname breaks its shell
   variables. The production compose file uses IPs, so it only affected this smoke run.

## 9. Proposed checkpoint commit message

```
feat: add the Arabic and RTL foundation

Run OmniAI in English or Arabic with full RTL, using @angular/localize at
runtime (Phase 36.3a, Option B). The language is set before first paint by
the existing inline script, kept per browser, and follows the visitor on
the hosted chat and widget. The shell, shared components, shared strings,
route titles and the Design Lab are translated; every date and number goes
through one Intl formatter with Western digits and the Gregorian calendar.
Directional icons mirror centrally, and component RTL rules use
:host-context. A spec in test:ci and npm run i18n:check guard the catalogue.
```
