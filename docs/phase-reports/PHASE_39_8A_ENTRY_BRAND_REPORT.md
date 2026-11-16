# Phase 39.8a: the entry pages and the brand everywhere

Step 39.8a of the approved plan (`PHASE_39_0_REDESIGN_PLAN_REPORT.md`, section 3.6 row 39.8a and section 3.2.5), plus the owner's two added backend items (the mark in report files, provider names in report files). Sections 3 and 4 of the prompt (the final pass and the Phase 39 summary) are 39.8b and were not started. Run unattended (headless); nothing here waits on an answer.

Evidence: `C:\Users\mouay\Desktop\nabra-frontend-evidence\p39-8a\` (`shots\`, `shots\modes\`, `chrome\`, `gates\`, `e2e\`, `icons\`, `mailpit\`, `pdf-images\`, `files\`, `live\`, `logs\`, `scratch\`).

## 1. Summary

- Every page a person meets before the app (sign in and its "session ended" state, create an account, forgot and reset password, accept an invitation, choose or create a workspace) is now a calm split: the form on one solid panel beside a deep emerald panel with the two-circle mark, one Instrument Serif line and one sentence; on a phone the form comes first and the brand band follows. Every Phase 37 and 38 behaviour is unchanged.
- The browser tab, home-screen icons and a new web manifest show the two-circle mark (SVG favicon that follows light and dark, PNG and ICO fallbacks, app icons with a maskable one).
- Every email (16 templates, English and Arabic) carries the mark in its header, inline in the message so Gmail, Outlook and Apple Mail show it without fetching anything, in Calm Glass colours with an emerald action and fonts that fall back well.
- Report PDFs start their footer with the mark and "Nabra" / «نبرة»; the provider files say "OpenAI" and "Local test stand-in" / «البديل التجريبي المحلي» instead of the code names, in PDF, Excel and CSV.
- "Page not found" (inside the app) now shows the mark above its serif line.

## 2. Starting point

- Branch `master`, HEAD `090c70a` (Phase 39.7b), worktree clean (`git status --short` empty), checked before any change.
- Dev stack: `omniai-p36-api` running, Mailpit (`omniai-p37-mailpit`) and the namespace up, dev server on `localhost:4200` running; free memory 9.5 GB.

## 3. Changes by area

`git status --short` at the end:

```
 M backend/src/OmniAI.Application/Notifications/EmailLayout.cs
 M backend/src/OmniAI.Application/Reports/ReportDocumentBuilder.cs
 M backend/src/OmniAI.Application/Reports/ReportWording.cs
 M backend/src/OmniAI.Infrastructure/Notifications/Email/SmtpOperationalEmailSender.cs
 M backend/src/OmniAI.Infrastructure/OmniAI.Infrastructure.csproj
 M backend/src/OmniAI.Infrastructure/Reports/Rendering/PdfReportRenderer.cs
 M backend/tests/OmniAI.Api.IntegrationTests/Accounts/PasswordResetApiTests.cs
 M backend/tests/OmniAI.Application.Tests/Notifications/BilingualEmailTests.cs
 M backend/tests/OmniAI.Application.Tests/Notifications/NotificationTemplateTests.cs
 M backend/tests/OmniAI.Application.Tests/Reports/ReportDocumentBuilderTests.cs
 M backend/tests/OmniAI.Infrastructure.IntegrationTests/Notifications/OperationalEmailTests.cs
 M backend/tests/OmniAI.Infrastructure.IntegrationTests/Reports/ArabicReportFileTests.cs
 M backend/tests/OmniAI.Infrastructure.IntegrationTests/Reports/Golden/en/  (12 files: 8 *.pdf.canonical, provider-cost and provider-usage .csv and .xlsx.parts.txt)
 M docs/architecture/admin-design-system.md
 M docs/architecture/operational-notifications.md
 M docs/architecture/reports-exports-scheduling.md
 M docs/design/MASTER.md
 M docs/design/pages/entry.md
 D frontend/public/nabra-icon.v1.ico
 D frontend/public/nabra-icon.v1.svg
 M frontend/src/app/core/brand/brand.spec.ts
 M frontend/src/app/core/i18n/arabic-render.spec.ts
 M frontend/src/app/features/auth/auth-page.scss
 M frontend/src/app/features/not-found/not-found.spec.ts
 M frontend/src/app/features/not-found/not-found.ts
 M frontend/src/app/features/workspace-setup/workspace-setup.scss
 M frontend/src/app/layout/entry-frame/entry-frame.{html,scss,spec.ts,ts}
 M frontend/src/index.html
 M frontend/src/locale/messages.ar.json
 M frontend/src/locale/messages.json
 M frontend/src/styles/_tokens.scss
?? backend/src/OmniAI.Infrastructure/Notifications/Email/Brand/nabra-mark.v1.png
?? backend/tests/OmniAI.Infrastructure.IntegrationTests/Notifications/EmailTemplateGalleryTests.cs
?? backend/tests/OmniAI.Infrastructure.IntegrationTests/Reports/PdfFooterMarkTests.cs
?? frontend/public/manifest.webmanifest
?? frontend/public/nabra-app-icon.v2-{180,192,512,maskable-512}.png
?? frontend/public/nabra-icon.v2-{16,32,48}.png, nabra-icon.v2.ico, nabra-icon.v2.svg
```

### 3.1 The entry pages (frontend)

- **`EntryFrame`** (`layout/entry-frame/`), shared by all six pages, rewritten: the ground with the shell's two glows, 14 px from the window; the form column (the lockup `app-brand-lockup` with the mark and both names, the page's language first, plus the language switch), then the page on one solid panel (white / emerald-900, a line, radius 24, no shadow, 26 rem measure). The brand side is a deep panel (`--entry-brand-*`, the overview's deep-tile ground: emerald-900, emerald-800 with a line in the dark look) with the dark look's two glows inside it, the mark at 120 px in its dark-ground colours with the existing once-per-load settle, one serif line (the tagline at `--text-display`: Instrument Serif 44/48, Plex Arabic 600 in Arabic) and the promise. Below 1024 px: one column, the form first, the brand band after it (mark 56 px, tagline at `--text-empty`), with any spare height given to the form column so the band keeps its size at the bottom.
- The brand side is `aria-hidden` and holds no heading, link or control; every word and action a reader needs is in the form. CSS and inline SVG only: no image, no script, no font preload. No glass anywhere on these pages (nothing floats).
- Phase 36's example conversation is removed (one serif line and one sentence, as the plan says), which also retires the entry brief's exception to MASTER rules 7 and 10. Six English and six Arabic strings go with it.
- **`auth-page.scss`** and **`workspace-setup.scss`** moved from the alias layer to Calm Glass names; the joined-workspace card and each workspace in the chooser are soft tiles (radius 16) with the neutral avatar tile, the chooser rows 44 px tall. No template, field, id, `data-testid`, route, guard or message changed on any page.
- **Not found** stays inside the shell (Phase 36's decision, so a mistyped address never loses the navigation) and gets the mark at 56 px above its serif line; the search glyph is gone.
- **Tokens** (`_tokens.scss`): `--entry-brand-bg/-edge/-text/-text-secondary/-glow-a/-glow-b`, `--brand-on-deep-ai/-team/-lens`, `--entry-form-width`, `--entry-mark-size`, `--entry-mark-size-sm`. Every pair measured (section 5).

### 3.2 Icons and the manifest

- `public/nabra-icon.v2.svg`: the mark cropped to its circles, light-ground colours with a `prefers-color-scheme: dark` block for the dark-ground ones. `nabra-icon.v2-16/32/48.png` and `nabra-icon.v2.ico` (the three PNGs packed as PNG entries). The 16 px file keeps the overlap's colour: it reads at 16 px (`icons\favicon-tab-light.png`, `candidate-16-no-lens.png` for comparison).
- App icon: rounded square (radius 22% of the side), emerald-600 → emerald-900 top to bottom with a soft top sheen, the mark at 62% in its dark-ground colours. `nabra-app-icon.v2-180.png` (Apple, full-bleed, iOS rounds it), `-192.png` and `-512.png` (rounded), `-maskable-512.png` (full-bleed). The maskable mark reaches 131.5 px from the centre against a 204.8 px safe zone (`icons\maskable-safe-zone.png`, the zone drawn in red, the mark's reach in yellow).
- `public/manifest.webmanifest`: name and short name "Nabra", `start_url` `/`, `display` `browser`, colours from the tokens, the three icons. Same origin, so no CSP change (`manifest-src` falls back to `default-src 'self'`); served `no-cache` by the edge's `location /`; the PNG and ICO names are versioned because the edge caches them for a year.
- `index.html` links the ICO, the three PNGs, the SVG (last, `sizes="any"`), the Apple touch icon and the manifest. The v1 files (the Phase 36 shapes) are deleted.
- Rendered with the installed Chrome through Playwright (`scratch\icons.mjs`, `scratch\email-mark.mjs`); no package added anywhere.

### 3.3 Emails (backend)

- `EmailLayout` (one layout for all 16 templates): cream-100 ground, a white card with a sand-300 line and radius 24, the header as a two-cell table (the mark image, then the product's name in the reader's language in emerald-900, 600), ink-900 heading, ink-700 paragraphs, stone-600 workspace line and footer (with a line above it), an emerald-700 pill for the one action (was blue `#2f5bd3`). Font stacks: Latin `'Geist',-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,'Helvetica Neue',Helvetica,Arial,sans-serif`; Arabic `'IBM Plex Sans Arabic','Segoe UI',Tahoma,'Geeza Pro','Noto Sans Arabic','Arabic Typesetting',Arial,sans-serif` with taller Arabic lines. `lang`/`dir` on the document, body and tables as before; the plain-text part is byte-for-byte the same as before.
- **The mark:** a 56 × 48 PNG (1,181 B, rendered from brand.md's SVG) embedded in the Infrastructure assembly; `SmtpOperationalEmailSender.Build` attaches it as an inline part of a `multipart/related` HTML body with Content-ID `nabra-mark@nabra` whenever the HTML refers to it. Shown at 28 × 24 with `alt=""` (the name beside it is text, so an image-blocking client still shows "Nabra"). See decision 4 for why this and not table cells.
- **Voice colours:** no email shows a conversation (13 operational kinds: reports, usage, billing, knowledge, WhatsApp delivery; 3 account emails), so the three voices' colours appear in none, and a test pins that.

### 3.4 Report files (backend)

- **The mark in the PDF footer:** the two circles and the overlap drawn as vector paths (`PdfReportRenderer.DrawMark`, light-ground colours, box 1.2 × the footer type), then the name ("Nabra", or «نبرة» in an Arabic file, from the new `ReportWording.Product`) in the footer's grey, then the title; at the left margin in English and the right margin in Arabic. The page number stays at the other margin. The mark is never mirrored (emerald on the left in both). No image object: the PDF still has none.
- **Provider names:** `ReportWording.Provider(code)`: `OpenAi` → "OpenAI" (the company's name, Latin in both languages), `DevelopmentStandIn` → "Local test stand-in" / «البديل التجريبي المحلي»; an unknown code prints as stored. Used by the provider usage and provider cost documents, so PDF, XLSX and CSV all change. The API still returns the codes (contract unchanged).
- Golden files: the old and new pages were compared as images first (`pdf-images\compare\`, 20 pages side by side; `pdf-images\zoom\` the footers at 400 dpi), then recaptured only through the test's write mode (`OMNIAI_WRITE_REPORT_GOLDEN=1`): the 8 English `.pdf.canonical` files and the provider files' `.csv` and `.xlsx.parts.txt` (12 files). The only XLSX change besides the names is the provider column's width (20 → 21, sized by content).

### 3.5 Widget loader and hosted page titles

Checked live, no change needed: the hosted page is "Chat · Nabra" / «الدردشة · نبرة», and the loader's frame is "Chat" / «الدردشة» on English, Arabic, French and language-less host pages, with the embed document titled "Chat · Nabra" / «الدردشة · نبرة» (`chrome\titles.json`). The plan has no social preview, so none was added.

### 3.6 Screens

| Screen | What changed | Components and tokens | Tests |
| --- | --- | --- | --- |
| Sign in (and "session ended") | calm split, lockup, panel, brand side | `EntryFrame`, `app-brand-lockup`, `--color-panel`, `--entry-brand-*`, `--text-display` | `login.spec`, `entry-frame.spec`, e2e sign-in, sign-in-throttled, signed-out-no-401 |
| Create an account | same frame | as above | `register.spec`, `arabic-render.spec` |
| Forgot password, reset password | same frame | as above | `forgot-password.spec`, `reset-password.spec` |
| Accept an invitation | same frame; joined-workspace soft tile | as above, `--avatar-*`, `--radius-tile` | `accept-invitation.spec`, e2e invitation-language |
| Choose or create a workspace | same frame; workspaces as 44 px soft row tiles | as above, `--color-panel-soft`, `--color-hover`, `--avatar-*` | `workspace-setup.spec` |
| Not found (in the shell) | the mark above the serif line | `app-brand-mark`, `--entry-mark-size-sm` | `not-found.spec` (new case) |
| Favicon, app icons, manifest | v2 files, manifest | `public/` | `brand.spec` (two new cases) |
| Emails (16 × 2) | Calm Glass layout, inline mark | `EmailLayout`, `SmtpOperationalEmailSender` | `EmailTemplateGalleryTests` (33 new), email tests updated |
| Report files | footer mark and name; provider names | `PdfReportRenderer`, `ReportWording` | `PdfFooterMarkTests` (4 new), builder theory (6 new), goldens |

### 3.7 Screenshots

`p39-8a\shots\<screen>-<width>-<lang>-<theme>-<before|after>.png` for `sign-in`, `session-ended`, `register`, `forgot-password`, `reset-password`, `accept-invitation`, `workspace-chooser`, `not-found`, each at 1440 and 390 px, English light and Arabic dark, before (HEAD, taken before any change) and after: 64 files. Modes: `shots\modes\sign-in-{transparency,motion,forced}-{en,ar}.png`. Emails: `mailpit\<Template>-<lang>-{html,html-390,text}.png` (96 files) and `mailpit\live-invitation-{en,ar}-html.png`. Icons: `icons\`. Report pages: `pdf-images\compare\`, `pdf-images\zoom\`, `live\pages\`.

## 4. Decisions

1. **The brand side is decoration and hidden from assistive technology.** Phase 36 read its tagline and promise aloud as an `aside` named by an `h2`. The owner's note says the decorative side carries nothing a screen reader needs, so it is `aria-hidden` with no heading; the `h1` stays the only heading. Alternative rejected: keeping a second landmark with a heading that repeats marketing on every entry page.
2. **The example conversation is removed.** The plan's 39.8a is "the mark, the name in both scripts, one serif line"; a transcript is a second signature and needed a MASTER exception. Its 12 strings are pruned.
3. **"Not found" keeps the shell.** The plan lists it with the entry pages; moving it out of the shell would change behaviour (a mistyped link would lose the navigation, Phase 36's decision). It gets the mark and keeps its serif line; recorded in `docs/design/pages/entry.md`.
4. **The email mark is a PNG attached inline by Content-ID.** MailKit (already referenced) supports linked resources, nothing new is needed, and Gmail, Outlook and Apple Mail all show `cid:` images in place without fetching anything, so "opening an email tells nobody" still holds. Rejected: drawing the mark with table cells (two overlapping circles cannot be drawn with tables; Outlook's Word engine ignores `border-radius`, so it degrades to squares), inline SVG and `data:` URIs (blocked or stripped by Gmail and Outlook), and a hosted image (a remote fetch and a tracking signal).
5. **The email header names the product in the reader's language only** ("Nabra" or «نبرة»), as Phase 37 did, rather than the two-script lockup; mail clients would set the second script in an unknown fallback font.
6. **Emails declare `color-scheme: light`**; clients that force dark mode may still invert, and the mark's PNG is transparent, so its circles then sit on the client's dark ground.
7. **Manifest `display: browser`.** The plan asks for a manifest with icons, not an installable app; `browser` gives Android and desktop their icons without offering an install prompt, which would be new behaviour. `standalone` is a one-word change if the owner wants it.
8. **App icon gradient top to bottom** (brand.md) rather than the Brand board's 160°; Apple and maskable icons full-bleed (the platform cuts the corners), the 192/512 icons with the 22% radius.
9. **The 16 px favicon keeps the overlap's colour**; brand.md allows dropping it "if it blurs", and it does not.
10. **The report footer mark is 1.2 × the footer's 8 pt type** (brand.md's lockup rule), in full colour; "Nabra"/«نبرة» in the footer's existing grey.
11. **Provider names** were not shown anywhere in the app (the analytics table shows only models), so both names are proposals (section 8). OpenAI keeps its Latin name in Arabic files.
12. **Two existing tests were changed on purpose:** the email "no `<img`" guards now require exactly one image, the `cid:` mark, and no `data:`/SVG/remote source; `ArabicReportFileTests` now expects the Arabic footer's text to end where the mark begins rather than at the margin.
13. **Departures from the spec or boards:** none on colours or type. No board draws the entry pages; the layout follows calm-glass.md §1, §3 and §5 (solid panels for forms, no glass where nothing floats) and the overview's deep tile.

## 5. Verification

| Check | Result |
| --- | --- |
| `npm run format:check` | **PASS** (final tree, `gates\format-check-final.log`) |
| `npm run test:ci` | **PASS**: 145 files, 2,220 tests, 0 failures (39.7a: 145 / 2,215). Free memory before: 9.8 GB (`gates\test-ci-final.log`) |
| `npm run build` | **PASS**: 0 warnings, every budget met (none changed). Free memory before: 9.9 GB. **Startup JavaScript 149,213 B gzip** (loader + application chunk, 31 files, `gates\startup-js.json`); 39.7a/HEAD 148,821 (+392 B: `BrandLockup` now sits in its own shared chunk because the lazy entry frame uses it too; no decorative code is eager); 38.0 baseline 138,907 (+7.4%); ceiling 152,797. The entry frame is a lazy chunk of 8,371 B raw / 2,795 B gzip, outside startup |
| `npm run i18n:check` | **PASS**: 2,557 messages, 0 errors, 0 warnings (2,563 before; −6 example strings) |
| `npm run e2e` | **PASS**: 46 journeys (23 × en-ltr and ar-rtl), 46 passed, 0 failed, 3.7 min, one full run with no retries (`e2e\e2e-run1.txt`), against the rebuilt API after the canary. Includes sign-in, sign-in-throttled, signed-out-no-401, invitation-language and arabic-report. The second run 15 minutes later is 39.8b's |
| Backend suite (Docker) | **PASS**: `mcr.microsoft.com/dotnet/sdk:10.0`, throwaway PostgreSQL 17 + pgvector and Redis, `-m:1`; restore with the NuGet audit on; Release build **0 warnings, 0 errors**; **4,420 passed, 3 skipped**: Api 1,192 (2 skipped), Application 927, Domain 939, Infrastructure 1,362 (1 skipped) (39.7b: 4,377; +6 provider-name cases, +33 email gallery, +4 footer); no vulnerable packages in any project; 0 leftover test databases; containers removed (`gates\backend-gates-run2.log`). A first run (`run1`) stopped at a nullable-dereference error in my test edit; fixed and re-run in full, never retried as-is |
| `PublicNameTests` | **PASS**, run by name (`logs\publicname.log`), including `No_user_facing_backend_text_says_OmniAI` |
| Golden files | **PASS**: 48/48 after recapture through the write mode; 12 files changed, after an image comparison of every page |
| Dev API rebuilt, canary | **PASS**: `p36-stack.sh build` then `api` (stand-in on), canary before any traffic and again at the end: every provider secret and `OMNIAI_LIVE_*` absent, SMTP host loopback, `OMNIAI_DEV_AI_STANDIN` info (`gates\canary-start.txt`, `canary-end.txt`) |
| Report files, live | **PASS** 24/24 (`live\live-reports.json`): one PDF of each of the 8 kinds in English and Arabic from the rebuilt API, opened as page images (`live\pages\`), Geist (+ Plex Arabic) embedded, mark and name in the footer; provider usage and cost as CSV and XLSX in both languages name "Local test stand-in" / «البديل التجريبي المحلي» and never the codes |
| Emails in Mailpit | **PASS**: all 16 templates × 2 languages built by the real sender and delivered to the dev Mailpit (`mailpit\gallery.json`): the inline mark loads (56 × 48) in every one, 0 attachments, `lang`/`dir` right, no overflow at 680 or 390 px, the action emerald (rgb 35 83 71), the text part starts with the name. Sizes 5,239 to 6,062 B each (English 5,239 to 5,645; Arabic 5,486 to 6,062). Live invitation emails sent by the e2e journey through the rebuilt API carry the mark inline too: 6,003 B (en) and 7,355 B (ar), against 3,116 / 4,217 B before |
| Chrome matrix (entry pages) | **PASS**: Playwright library driving the installed Chrome with fresh temporary profiles (the Chrome DevTools MCP was not used). 8 screens × en/ar × light/dark × 390/768/1280/1920 = 128 loads, 0 problems: no page overflow, no console error or warning, exactly one `h1`, no unnamed control, no target under 24 px and none under 44 px on touch widths, axe (WCAG 2.0–2.2 A/AA) clean at 390 and 1280, no blurred layer, the form before the brand side in the document, `dir` right, no 401 on any signed-out page (`chrome\results-after.json`) |
| Reduced transparency, reduced motion, forced colours | **PASS**: 8 screens × 3 modes × en/ar × 390/1440 = 96 loads, 0 problems; nothing animates under reduced motion, nothing blurred, forced colours draw system colours and the one-colour mark (`shots\modes\`) |
| Keyboard paths | **PASS**: 8 screens × en/ar × 390/1440, every stop with a visible ring and in view; sign-in: English, العربية, email, password, Show password, Forgot your password?, Sign in, Create one |
| Contrast of new pairs | **PASS** 30/30 (`chrome-contrast.txt`, MASTER.md §13.8); the gold circle on white (2.25:1) is part of the logo and reported |
| RTL mirroring | **PASS**: the frame mirrors through logical properties; the mark never mirrors (entry pages, PDF footer verified by pixel test and image) |
| Fonts | **PASS** (`chrome\fonts.json`): English entry pages load Geist Latin 29,400 B (preloaded, high) and Instrument Serif 21,032 B (not preloaded; it swaps), no Arabic file; Arabic pages load Plex Arabic 400 42,848 B (preloaded) plus 500 45,296 B and 600 45,688 B and Geist; no new preload; layout shift from the swap 0 in English and at most 0.00005 in Arabic with every font delayed 600 ms |
| Favicon | **PASS**: Chrome (headed, fresh profile) fetches the SVG for the tab in light and dark (`icons\favicon-fetched.json`); the SVG drawn on light and dark tab strips (`icons\favicon-tab-*.png`); every link answers 200 with its type; the manifest is `application/manifest+json` and each icon's real size equals its declared size |
| Secret scan | **PASS**: 4,459 files (evidence, changed sources, report): no demo password, no reset or invitation link. The temporary cookie folder `p39-8a\tmp\` is deleted |

## 6. See it live

The dev stack is running: API `http://127.0.0.1:5027` (stand-in on), dev server `http://localhost:4200`, Mailpit `http://127.0.0.1:8026`. Demo accounts from `omniai-redesign-evidence\p36-01\demo-accounts.md`: Owner `p36.owner@omniai.example`, Support agent `p36.support@omniai.example`, Analyst `p36.analyst@omniai.example` (password in that file).

- `http://localhost:4200/sign-in`: the split at full width; narrow the window under 1024 px to see the form first and the brand band after it; switch to العربية and back; reload with the system in dark mode. The mark's team circle settles once per load.
- `/sign-in?reason=session-ended`, `/create-account`, `/forgot-password`, `/reset-password` (no link: "This link is not valid"), `/accept-invitation`.
- Sign in as the Owner: the workspace chooser (Northwind and شركة النور) as soft row tiles; then any mistyped address, such as `/no-such-page`, for "not found" with the mark.
- The tab's icon; `http://localhost:4200/manifest.webmanifest`.
- Mailpit: 32 gallery messages addressed to `member@example.test` (every template in both languages; their links are placeholders) and the e2e invitations.
- Reports (Owner, Northwind): generate "AI provider usage" as PDF, Excel or CSV in English and Arabic; the footer's mark and name; the provider column says "Local test stand-in" / «البديل التجريبي المحلي».

## 7. Backend asks

None. Nothing on these pages wanted data the API does not offer.

## 8. New Arabic for my review

| Arabic | English | Where |
| --- | --- | --- |
| «البديل التجريبي المحلي» | Local test stand-in | the provider column of the provider usage and provider cost report files (PDF, Excel, CSV), Arabic files only; a proposed name for the Development-only AI stand-in (code `DevelopmentStandIn`), which no screen names today |
| "OpenAI" (unchanged Latin) | OpenAI | the same column; a company name, kept in Latin script in Arabic files (proposal) |

Removed (not replaced): the six strings of the Phase 36 example conversation, «محادثة توضيحية» (caption), «العميل», and the question, answer, follow-up and reply sentences. No other Arabic was added or changed: the tagline «صوتان… بنبرة واحدة» and the promise are the existing strings, and every email's Arabic text is unchanged.

## 9. Risks and open issues

1. **Provider names are proposals** (section 8): the app shows no provider name anywhere, so the owner may prefer other wording, especially for the stand-in.
2. **Dark-mode email clients** may invert the white card; the transparent mark then shows its emerald circle on a dark ground (about 2:1). A plate behind the mark would fix it at the cost of a visible chip in light mode; left for the owner.
3. **Shell phone top bar targets (seen, not this step's screens):** on not-found at 390 and 768 px the shell's skip link measures 40 px and the top bar's workspace button 24 px tall, under the 44 px touch rule. Not touched here (39.3's shell); listed for 39.8b's full matrix.
4. **Email rendering was checked in Mailpit only.** Gmail, Outlook and Apple Mail were not opened (no accounts and no live email allowed); the `cid:` approach is the one they all support, but a real-client check belongs to the real-provider phase.
5. **Startup JavaScript +392 B** from the lockup's shared chunk; 3,584 B remain under the ceiling.
6. The alias layer still exists (39.8b removes it); the entry pages, auth styles, chooser and not-found no longer use it.

## 10. Proposed checkpoint commit message

```
feat: give the entry pages and the brand Calm Glass (sign in and its session-ended state, create an account, forgot and reset password, accept an invitation and the workspace chooser as a calm split: the form on one solid panel beside a deep emerald panel with the two-circle mark, one Instrument Serif line and the promise, the decorative side hidden from assistive technology and drawn with CSS and inline SVG only, the form first on a phone with the brand band after it, every Phase 37 and 38 behaviour kept; the example conversation retired), show the mark above "not found"'s serif line, ship the v2 favicon (an SVG that follows light and dark, PNG 16/32/48 and an .ico), the app icon at 180/192/512 with a maskable 512 and a web manifest, lay every email out in Calm Glass with the mark attached inline by Content-ID and fonts that fall back well in English and Arabic, start each report PDF's footer with the mark drawn as paths and the name in the reader's language, name the AI providers in the usage and cost files ("OpenAI", "Local test stand-in") in PDF, Excel and CSV, recapture the twelve changed English golden files through the write mode, and record the new contrast pairs (Phase 39.8a)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

## Skills

Loaded and used: `frontend-pro-max:first-impression-experience` (the entry pages' five-second brief, Calm level, LCP-safe `h1`, signature moment), `frontend-pro-max:ui-ux-design-lead` (layout, colour roles, contrast), `frontend-pro-max:frontend-testing-quality` (specs, e2e, the Chrome matrix), `frontend-pro-max:web-performance` (startup JavaScript, fonts and layout shift). The 21st.dev MCP ("magic") was not connected this session (unauthenticated), so nothing was sourced from it; the layout was built from our own primitives as the owner's decision allows.
