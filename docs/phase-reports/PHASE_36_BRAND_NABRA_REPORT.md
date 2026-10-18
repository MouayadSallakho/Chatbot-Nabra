# Phase 36 brand step: the public name becomes Nabra (نبرة) (report)

- Date: 2026-10-18
- Scope: what people see and hear. "OmniAI" becomes **Nabra** in English and **نبرة** in Arabic everywhere in the frontend. OmniAI stays the internal code name. Frontend only; nothing in `backend/` or `deploy/` changed.
- Evidence folder (outside the repository): `C:\Users\mouay\Desktop\omniai-redesign-evidence\p36-brand\`

## 1. Summary

1. People now see one name everywhere: **Nabra** in English and **نبرة** in Arabic. That covers the sidebar, sign-in, registration, invitation and workspace screens, the loading state, and the "could not be reached" error.
2. Every browser tab reads "<page> · Nabra" or "<page> · نبرة", including the hosted chat page customers open.
3. The wordmark is set in Instrument Sans in English and IBM Plex Sans Arabic in Arabic, with no new font files. The mark keeps its shape; it now stands for two voices in one tone.
4. People who visited before get the icon from new file names, so the year-long cache on the old names can't hold them back.
5. Saved theme, language, workspace and sidebar choices carry over: every internal key is unchanged.

## 2. Starting point

- Branch `master`, HEAD `325bc34` ("feat: redesign the shell and add a command palette", the accepted Phase 36.4 checkpoint), worktree clean.
- Local stack up (API, Postgres, Redis); provider canary passed before any check. The dev server on :4200 was already running.
- Startup JavaScript at the start (production build of HEAD): **32 files, 404.3 kB raw, 131.7 kB gzip, 116.6 kB brotli** (`startup-js-baseline.json`).
- In the HEAD build, 12 files contained "OmniAI" (`dist-omniai-before.txt`).

## 3. Changes by area

`git status --short`:

```
 M docs/architecture/admin-design-system.md
 M docs/design/MASTER.md
R  frontend/public/favicon.ico -> frontend/public/nabra-icon.v1.ico
RM frontend/public/favicon.svg -> frontend/public/nabra-icon.v1.svg
 M frontend/src/app/core/api/api-error.ts
 M frontend/src/app/core/i18n/arabic-render.spec.ts
 M frontend/src/app/core/title/app-title-strategy.ts
 M frontend/src/app/features/auth/login/login.html
 M frontend/src/app/features/auth/register/register.html
 M frontend/src/app/features/channels/website-chat/website-chat-widget/website-chat-widget.html
 M frontend/src/app/features/design-lab/design-lab.html
 M frontend/src/app/features/design-lab/design-lab.scss
 M frontend/src/app/features/invitations/accept-invitation/accept-invitation.html
 M frontend/src/app/features/notifications/notification-preferences/notification-preferences.html
 M frontend/src/app/features/workspace-setup/workspace-setup.html
 M frontend/src/app/features/workspace-setup/workspace-setup.spec.ts
 M frontend/src/app/layout/sidebar/sidebar.html
 M frontend/src/app/layout/sidebar/sidebar.scss
 M frontend/src/app/shared/brand-mark/brand-mark.spec.ts
 M frontend/src/app/shared/brand-mark/brand-mark.ts
 M frontend/src/index.html
 M frontend/src/locale/GLOSSARY.md
 M frontend/src/locale/messages.ar.json
 M frontend/src/locale/messages.json
?? docs/phase-reports/PHASE_36_BRAND_NABRA_REPORT.md
?? frontend/src/app/core/brand/   (brand.ts, brand.spec.ts)
```

The two icon renames were made with `git mv`, so they show as staged (`R`). Nothing else is staged, and nothing is committed.

### 3.1 Inventory: where a user could see or hear "OmniAI" (before)

| Where | What the user met | Now |
|---|---|---|
| Tab titles (`AppTitleStrategy`) | "<page> · OmniAI" on every route, including the hosted chat and the widget's own document; "OmniAI Admin" without a route title | "<page> · Nabra" / "<page> · نبرة"; "Nabra Admin" / "إدارة نبرة" |
| `index.html` | `<title>OmniAI Admin</title>`; loading state "Loading OmniAI…" / "جارٍ تحميل OmniAI…" | `<title>Nabra</title>`; lockup (mark + "Nabra" / "نبرة") and "Loading…" / "جارٍ التحميل…" |
| Wordmarks | sidebar, sign-in, registration, invitation, workspace chooser: "OmniAI" | `i18n="@@brand.name"`: "Nabra" / "نبرة" |
| Messages | `apiError.offline` "OmniAI could not be reached…" (both languages) | "Nabra could not be reached…" / "تعذّر الوصول إلى نبرة…" |
| Page sentences (English-only until now) | workspace chooser "OmniAI is not responding"; invitation "Go to OmniAI"; notification preferences "…they link to OmniAI…"; website chat security "a frame served by OmniAI…" | all four moved into i18n (new IDs), with Arabic, naming Nabra / نبرة |
| Design Lab | the mark "overlapping into an "O"" | "two voices in one tone"; both lockups shown |
| `aria-label`s, alt text, manifest | none named the product; no web manifest exists | — |
| Hosted chat page and widget | only through the tab title above; the widget loader names its frame "Chat" | via the title strategy |
| Tests asserting the old name | `brand-mark.spec.ts` (label), `workspace-setup.spec.ts` (heading) | updated; new guards in `brand.spec.ts` |

"OmniAI" remains in the frontend source only as internal names, as the brief requires:
- `localStorage`/`sessionStorage` keys `omniai.*`, so saved preferences survive;
- the widget protocol `omniai:v1:` and the headers `X-OmniAI-Embed-*`;
- the project name `omniai-admin`, `angular.json`, the build folder and `README.md`;
- code comments.

Outside `frontend/` (read only, not changed) the name reaches people in emails and PDF reports, and in the widget snippet the API generates (§7).

### 3.2 What changed, and why

| Area | Change | Before → after (evidence) |
|---|---|---|
| **One source of the name** | New `core/brand/brand.ts` exports `PRODUCT_NAME` from the message `@@brand.name` ("Nabra"; Arabic "نبرة"). Templates mark the wordmark with the same ID, so the name lives once in each catalogue. | — |
| **Tab titles** | `AppTitleStrategy` uses `PRODUCT_NAME`. Arabic gives "نظرة عامة · نبرة". | `before/brand.json` → `after/brand.json` (`tabTitle` per capture) |
| **Wordmark** | Five templates. Sidebar style: tracking now `--letter-spacing-tight` (0 under `:lang(ar)`) instead of a hard Latin value. Line height 1.4, so the dots of ب and ة aren't cut by the `.fades` clip. Chrome confirms "Nabra" renders in Instrument Sans and "نبرة" in IBM Plex Sans Arabic SemiBold in the sidebar, sign-in and loading state (`fonts.json`). | `before/en-light-1440-sign-in.png` → `after/en-light-1440-sign-in.png`; `after/ar-light-1440-sign-in.png`; `before/ar-dark-1440-overview-pinned.png` → `after/ar-dark-1440-overview-pinned.png` |
| **Loading state** | Mark + wordmark lockup in the page's language, chosen by the existing CSS rule. Width and tracking come from tokens, so "نبرة" is untracked. Status "Loading…" / "جارٍ التحميل…" (glossary). The inline script is untouched, so its CSP hash is unchanged. | `before/en-light-1440-loading.png` → `after/en-light-1440-loading.png`; `before/ar-light-1440-loading.png` → `after/ar-light-1440-loading.png`, `after/ar-dark-390-loading.png` |
| **Sentences** | Four page sentences now go through i18n with natural Arabic: "نبرة لا تستجيب", "الانتقال إلى نبرة", the email note, and the frame security note. The offline error is updated. | `after/ar-light-390-notification-preferences.png`, `after/en-dark-1440-accept-invitation.png` |
| **The mark** | Geometry unchanged. Its description in `brand-mark.ts`, the icon file, MASTER.md (signature element 6), `admin-design-system.md` (D10, D17) and the Design Lab now reads "two voices in one tone". The Design Lab also shows both lockups, each in its own `lang`/`dir`. | `after/en-light-1440-design-lab-mark.png`, `after/ar-dark-1440-design-lab-mark.png` |
| **Favicon caching** | `favicon.svg`/`favicon.ico` → `nabra-icon.v1.svg`/`nabra-icon.v1.ico` (same icons; only the SVG's comment changed). `index.html` points at the new names, with a comment on why. | `curl`: new names 200 with `image/svg+xml` / `image/x-icon`; `favicon.svg` now 404 |
| **Design brief** | MASTER.md section 0 "Name" (name, meaning, Arabic form and grammar, tagline, wordmark, tab titles, never "OmniAI"); intro and concept lines; three decision-log rows. GLOSSARY row OmniAI → Nabra / نبرة (checked), so `i18n:check` flags any English "Nabra" without "نبرة". | — |
| **Tests** | `brand.spec.ts` (8). No catalogue message and no template text contains "OmniAI"; `index.html` shows Nabra / نبرة; the icon links point at existing versioned files and the old names are gone; tab titles in English. `arabic-render.spec.ts` (+1): "نبرة" and "نظرة عامة · نبرة" with the real catalogue. | — |

**What the edge does with the new icon names** (from `deploy/edge/templates/omniai.conf.template`, read only):
- `location ~* "\.(?:js|css|woff2?|svg|png|ico|webp)$"` sends `Cache-Control: public, max-age=31536000, immutable`, so `nabra-icon.v1.svg` and `.ico` are cached for a year, like the old names were.
- `location /` (the SPA, `index.html`) sends `Cache-Control: no-cache`, so the page with the new links is revalidated on every visit.
- A returning visitor's next load therefore asks for the new names, which their cache has never seen.
- The old names are no longer referenced; a direct request for `/favicon.svg` now gets the SPA fallback or a 404 (§8).

## 4. Decisions

- **One message for the name** (`@@brand.name`), used in TypeScript and templates.
  - Rejected: a hard-coded constant per language. It would bypass the catalogue and the glossary check.
- **Arabic grammar of the name:** a feminine noun ("نبرة لا تستجيب"), never transliterated in Arabic copy. MASTER section 0 records it.
- **The mark stays as it is.** Its two overlapping speech shapes already draw two voices in one tone, and dropping the "O" reading costs nothing. **No change proposed.** It could be reconsidered in a later identity pass, for example a shared "tone" stroke between the shapes, but it doesn't need to change.
- **Static `<title>Nabra</title>` in both languages** until the application sets the translated title, a moment later.
  - Rejected: setting the title from the pre-paint script. That would change the CSP-hashed inline script for a few milliseconds of tab text.
- **Status line "Loading…" beside a visible wordmark**, instead of "Loading Nabra…" under it, so the name isn't repeated on screen.
- **Icon names `nabra-icon.v1.*`.** The version is in the name; the next change ships as v2.
  - Rejected: keeping `favicon.ico` as an extra copy. It would duplicate the file under the year-cached old name for clients that ignore `<link rel="icon">`.
- **Sentences on English-only pages moved into i18n only where the name changed** (rule 11). The rest of those pages stays English, as accepted at the 36.4 checkpoint.
- **Deferred:**
  - a web manifest (none exists; none was added);
  - renaming internal keys, headers or the protocol (breaks saved preferences and embedded sites);
  - every backend surface (§7).

## 5. Verification

| Check | Result |
|---|---|
| `npm run format:check` | **PASS** (`final-format.log`) |
| `npm run test:ci` | **PASS**: 75 files, 899 tests, 0 failures, 0 stray `ERROR` lines (start: 74 files, 890 tests; +9) (`final-test.log`) |
| `npm run build` | **PASS** with 1 warning, the pre-existing `embed-chat.scss` 72 bytes over its 4 kB warning budget. No new budget warning (the Design Lab stylesheet stays under its budget); no budget raised. (`final-build.log`) |
| `npm run i18n:check` | **PASS**: 405 messages, 0 errors, 0 warnings. The new glossary row is checked. (`final-i18n.log`) |
| Startup JavaScript (rule 13) | **PASS**: 131,669 → 131,685 bytes gzip (+16 bytes, +0.01%); brotli 116,550 → 116,508 (`startup-js-*.json`) |
| "OmniAI" in `dist/` | **PASS**. Case-sensitive: only `X-OmniAI-Embed-Origin` and `X-OmniAI-Embed-Session`, request headers the backend reads and no user sees. Case-insensitive, also: the `omniai.theme`, `omniai.lang`, `omniai.workspace-tenant-id`, `omniai.sidebar-pinned`, `omniai.createdAgent`, `omniai.webchat.conversation.` and `omniai.embed.session.` storage keys (kept so preferences survive), and the `omniai:v1:` `postMessage` prefix. None is rendered or announced. Before: 12 files; after: 7, all internal. (`dist-omniai-before.txt`, `dist-omniai-after.txt`) |
| Brand surfaces in Chrome: sign-in, registration, invitation, hosted chat, widget document, widget on its host page, overview (rail and pinned), notification preferences, website chat detail, workspace chooser, Design Lab, loading state × en/ar × light/dark × 1440/390 | **PASS**: 100 captures. 0 overflow, exactly one `h1` (the loading state has none by design), 0 unnamed controls. Tab titles all "· Nabra" / "· نبرة". Wordmarks "Nabra" / "نبرة". No visible "OmniAI" apart from the synthetic `@omniai.example` addresses (demo data), the API-generated widget snippet's `data-omniai-key` (§7), and the local host page's own text (not the product). Console: only the pre-existing 401 from `/api/auth/me` on signed-out pages, the expected session probe, also present before. (`before/`, `after/`, `*/brand.json`) |
| Full route matrix (33 routes × 1440/1024/768/390 × light/dark × en/ar) | **PASS**: 528 captures, 0 flagged. Max overflow 0 px; exactly one `h1` everywhere; 0 unnamed controls; 0 shell targets under 24 px; 0 console errors; `lang`/`dir` right on all 528; every tab title ends in "· Nabra" or "· نبرة". (`matrix/`, `matrix/matrix.json`) |
| Contrast | **PASS**. No colour changed (`_tokens.scss` untouched). The wordmark and loading-state pairs, re-proved with the `design-system-tokens` script: wordmark on page 15.87 / 16.84, on panels 17.78 / 15.58, loading status 5.31 / 6.09 (light / dark) (`contrast/`) |
| Keyboard | **PASS**: the wordmark adds no tab stop. Sign-in: email → password → "Show password" → "Sign in" → "Create one". Invitation: "Go to Nabra" / "الانتقال إلى نبرة". Same order in Arabic, focus visible at every stop (`keyboard.json`). |
| Reduced motion | **PASS (no change)**: nothing animated was added or changed; the loading state stays still. |
| RTL mirroring | **PASS**: the Arabic lockup puts the mark at the start (right) in the sidebar, sign-in and loading state; the Design Lab shows each lockup in its own direction. |
| Provider canary | **PASS** at start and end: `OPENAI_API_KEY`, `STRIPE_SECRET_KEY`, `STRIPE_WEBHOOK_SECRET`, `OMNIAI_WHATSAPP_APP_SECRET`, `OMNIAI_WHATSAPP_VERIFY_TOKEN`, `OMNIAI_LIVE_*` and `Email__Smtp__*` all absent (`canary-start.log`, `canary-end.log`) |
| Chrome DevTools **MCP** | **NOT RUN as MCP**: its profile is still held by another session's MCP server (as in 36.4). All checks ran through the dependency-free CDP scripts in `scratch/` against my own headless Chrome. |
| The favicon as shown in a browser tab | **NOT RUN**: a headless browser shows no tab strip. The files are served (200, correct types) and linked; the rendered icon is unchanged geometry. |
| Skill `web-performance` | Used for the cache analysis and startup measurement. All four named skills were present (`frontend-pro-max:*`). |

## 6. See it live

- **http://localhost:4200/sign-in** (dev server running). Switch to Arabic from the account menu → Language → العربية, or open with a browser set to Arabic.
- Hosted chat: http://localhost:4200/chat/9veAaKzu0Ee-isA7jrQzYw
- Design Lab (both lockups under "The mark"): http://localhost:4200/design-lab
- Widget on its host page: http://127.0.0.1:8099/?key=9veAaKzu0Ee-isA7jrQzYw (served from `p36-01\widget-host`, running now)
- Synthetic demo accounts: `C:\Users\mouay\Desktop\omniai-redesign-evidence\p36-01\demo-accounts.md`

## 7. Backend asks

Not changed (frontend-only step); these still show "OmniAI" to people:

1. **Operational emails**:
   - subject prefix "OmniAI: …" (`NotificationCatalog.Subject`);
   - the "OmniAI" header line and "Open OmniAI" button, plus the footer "your OmniAI notification preferences" (`OperationalEmailRenderer`);
   - default sender name `FromName = "OmniAI"` (`OperationalEmailOptions`).

   These should say Nabra, in the recipient's language when the backend knows it.
2. **PDF reports**: document properties `Creator = "OmniAI reports"`, `Producer = "OmniAI"` (`PdfReportRenderer`), visible in any PDF viewer's properties.
3. **Widget snippet**: the embed code the API returns to workspace admins contains `data-omniai-key`. A `data-nabra-key` alias would need the loader (`Widget/v1.js`) to accept both, keeping the old one for sites already embedding it.
4. Not user-facing; no change needed:
   - the loader's comments, the `omniai:v1:` protocol, the `X-OmniAI-*` headers;
   - data-protection application name, meter name, assembly names.
5. `deploy/`: no user-facing page names the product; the nginx file names are internal. The cache headers are in §3.2.

## 8. Risks and open issues

- **Old icon URLs.** `/favicon.svg` and `/favicon.ico` are no longer served; a direct request gets the SPA fallback (dev) or a 404 (edge, the static-asset location uses `try_files $uri =404`). Browsers use the `<link rel="icon">` names, so only clients that ignore the page (some crawlers, old bookmarks) are affected.
- **For a moment before the application starts**, an Arabic visitor's tab reads "Nabra" (the static `<title>`), then "… · نبرة".
- **Mixed pages.** The four sentences moved into i18n now appear in Arabic on pages whose bodies are otherwise still English. This follows rule 11 and is consistent with the accepted state of page bodies.
- **Design Lab inheritance.** On an Arabic page, the English lockup sample inherits the Arabic width and tracking tokens (normal width, no tracking). This is a token-inheritance limit of showing a Latin sample inside an Arabic page; it is lab-only.
- **Emails, PDFs and the snippet keep "OmniAI"** until the backend asks in §7 are done.
- **Native review.** The new Arabic copy (نبرة لا تستجيب، الانتقال إلى نبرة، the two notes, «صوتان… بنبرة واحدة») should get a native reviewer's pass with the rest of the glossary.

## 9. Proposed checkpoint commit message (not committed)

```
feat: rename the product to Nabra (نبرة) for the people who use it

The public name is Nabra in English and نبرة in Arabic -- "tone of voice":
two voices, the AI and the team, in one tone. OmniAI stays the internal
code name and is never shown.

- One message, @@brand.name, for the wordmark and the tab titles
  ("<page> · Nabra" / "<page> · نبرة").
- Wordmarks in the sidebar, sign-in, registration, invitation, workspace
  chooser and the loading state; Instrument Sans in English, IBM Plex Sans
  Arabic in Arabic, no new fonts.
- The sentences that named the product move into i18n with Arabic.
- The mark keeps its geometry and now reads as two voices in one tone.
- Icons get versioned names (nabra-icon.v1.*): the edge caches icons for a
  year and index.html for no time, so returning visitors get the new ones.
- Storage keys, protocol strings, headers and all internal names unchanged.
- Guards: no message, template or boot text may show "OmniAI".

Docs: MASTER.md section 0 "Name" and decisions; admin-design-system D10,
D17; glossary.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```
