# Phase 36.5: Sign-in, registration, invitation and workspace setup (report)

- Date: 2026-10-18
- Scope: the first screens a new customer sees. Covers sign-in, registration, accepting an invitation, choosing or creating a workspace, not found, the unbuilt-area placeholder and the `index.html` loading state. Frontend only.
- Evidence folder (outside the repository): `C:\Users\mouay\Desktop\omniai-redesign-evidence\p36-05\`. Open `index.html` there for before and after side by side.

## 1. Summary

1. **Nabra explains itself at the door.** From 1024 px, sign-in and registration sit beside a calm panel. It holds the tagline "Two voices. One tone.", one sentence of promise, and a labelled example conversation in which the AI answers and a person takes over. On a phone, the form comes first under a compact header.
2. **The door speaks Arabic.** Every word on these pages is now translated, with natural Arabic and correct right-to-left layout. Before, they were English with broken punctuation. There is an English / العربية switch before sign-in.
3. **One quiet signature, never in the way.** The person's shape settles into the mark in 0.75 s, once per visit, and not at all with reduced motion. The heading and form are still and readable from the first frame.
4. **No more blank frame.** The loading lockup now stays on screen until the page is ready. It used to give way to a blank page for up to a third of a second on slow phones.
5. **Welcoming next steps.** New accounts are told what comes after their first workspace. Invitees see the workspace they joined, by its initial and name. A mistyped address shows the path and one way back, and nothing alarming.

## 2. Starting point

- Branch `master`, HEAD `6331671` ("feat: rename the product to Nabra (نبرة) for the people who use it"). That is the brand step on top of the accepted 36.4 checkpoint `325bc34`. Worktree clean (`git status --short` empty).
- Local stack already up (`omniai-p36-api`, `omniai_pg25`, `omniai_redis18`, readiness 200). The dev server was already serving this worktree on :4200, and it is still running. Provider canary PASS before any check (`canary-start.log`).
- Startup JavaScript at the start (production build of HEAD, loader plus application chunk with their static imports): **32 files, 404,408 B raw, 131,685 B gzip, 116,508 B brotli** (`startup-js-baseline.json`).
- Tests at the start: 75 files, 899 tests.
- Before captures: 88 (`before/`, `before/entry.json`).

## 3. Changes by area

`git status --short` at the end:

```
 M docs/architecture/admin-design-system.md
 M docs/design/MASTER.md
 M frontend/src/app/app.html
 M frontend/src/app/app.routes.ts
 M frontend/src/app/app.spec.ts
 M frontend/src/app/app.ts
 M frontend/src/app/core/i18n/arabic-render.spec.ts
 M frontend/src/app/core/navigation/route-focus.ts
 M frontend/src/app/features/auth/auth-page.scss
 M frontend/src/app/features/auth/login/login.html
 M frontend/src/app/features/auth/login/login.spec.ts
 M frontend/src/app/features/auth/login/login.ts
 M frontend/src/app/features/auth/register/register.html
 M frontend/src/app/features/auth/register/register.ts
 M frontend/src/app/features/invitations/accept-invitation/accept-invitation.html
 M frontend/src/app/features/invitations/accept-invitation/accept-invitation.spec.ts
 M frontend/src/app/features/invitations/accept-invitation/accept-invitation.ts
 M frontend/src/app/features/not-found/not-found.ts
 M frontend/src/app/features/placeholder/placeholder.ts
 M frontend/src/app/features/workspace-setup/workspace-setup.html
 M frontend/src/app/features/workspace-setup/workspace-setup.scss
 M frontend/src/app/features/workspace-setup/workspace-setup.spec.ts
 M frontend/src/app/features/workspace-setup/workspace-setup.ts
 M frontend/src/app/shared/brand-mark/brand-mark.ts
 M frontend/src/index.html
 M frontend/src/locale/GLOSSARY.md
 M frontend/src/locale/messages.ar.json
 M frontend/src/locale/messages.json
 M frontend/src/styles/_primitives.scss
?? docs/design/pages/                          (entry.md)
?? docs/phase-reports/PHASE_36_5_FIRST_IMPRESSION_REPORT.md
?? frontend/src/app/core/boot/                 (boot-state.ts)
?? frontend/src/app/features/not-found/not-found.spec.ts
?? frontend/src/app/features/placeholder/placeholder.spec.ts
?? frontend/src/app/layout/entry-frame/        (entry-frame.ts/.html/.scss/.spec.ts)
```

Nothing changed in `backend/`, `deploy/`, `package.json` or the lockfile. No package was added. No budget was changed.

Captures are named `<lang>-<theme>-<width>-<state>.png`: `before/` (88, at 1440 and 390) and `after/` (155, at 1440, 1024 and 390, plus forced colours).

| Area | What changed, and why | Before → after (examples) |
|---|---|---|
| **Five-second brief** | Filled from the skill's template, at the **Calm** level, in `five-second-brief.md` (evidence) and `docs/design/pages/entry.md` (repo). It covers who arrives (owners, agents, invitees), the promise, one action, the LCP element and the signature. | — |
| **`EntryFrame`** (new, `layout/entry-frame/`) | One frame for the four pages before the shell: `header` (lockup and language switch), one `main`, and from 1024 px an `aside` story panel. The form is first in the document at every width. The panel holds the mark, the tagline (an `h2`), the promise, and an example conversation in a card. The example uses the product's own transcript primitives (`.bubble`, voice words, `.handoff-divider`), is labelled "Example conversation" / «محادثة توضيحية», is `aria-hidden`, and has no animation. It is HTML and CSS only, in a 3.0 kB-gzip lazy chunk shared by the four pages. The panel's vertical rhythm follows the viewport height, so the whole story stays in view at 1366×657 and 1024×768. | `before/en-light-1440-sign-in.png` → `after/en-light-1440-sign-in.png`; `after/ar-light-1440-sign-in.png`; `after/en-light-1024-register.png` |
| **Signature moment** | The mark's Ember shape (a person) settles into the room the Lagoon shape (the AI) keeps for it. Translate only, 600 ms on the emphasized curve after 150 ms, so it ends at 750 ms. Both shapes are fully drawn throughout. It plays once per page load (`EntrySignature`), never on the heading or the form, and not at all under reduced motion. The mark never mirrors. | `filmstrip/*-1440-motion-2000.png` (mid-settle) vs `*-5000.png` |
| **Sign-in and registration** | Fields, validation, error copy, password behaviour and `autocomplete` (`username`, `current-password`, `new-password`) are unchanged; nothing blocks paste or password managers. The email fields are `dir="ltr"`. Inputs take the 40 px form-action height, and the ink primary button is `btn-lg`, full width. The server's refusal is still shown verbatim, now with `dir="auto"` (it arrives in English on an Arabic page). The length hint and error use `$localize` with a named placeholder, so the minimum stays the code's constant. | `before/en-light-390-sign-in-errors.png` → `after/en-light-390-sign-in-errors.png`; `before/ar-dark-1440-register.png` → `after/ar-dark-1440-register.png` |
| **Language before sign-in** | English / العربية as a `.segmented` group of buttons: own name, `lang` and `dir`; `aria-pressed`; 44 px on touch. It reloads through the 36.3 `LanguageService`. It is **withheld while an invitation token waits in memory**, because the reload would lose the invitation. | `after/ar-dark-390-sign-in.png`; `keyboard/keyboard.json` |
| **Invitation** | All five states translated. "You have been invited" now says a team invited you to their workspace on Nabra. The accepted state shows the joined workspace as the top bar will: its first letter on a tile, then the name in its own direction. The two client-written refusals (missing code, other account) moved into i18n; the server's refusals still pass through verbatim. | `before/en-light-1440-invitation-needs-account.png` → `after/…`; `after/en-light-1440-invitation-accepted.png`, `after/ar-dark-1440-invitation-accepted.png` (a real invitation, created and accepted through the API) |
| **Workspace setup** | Inside `EntryFrame` with the language switch. Each workspace shows a first-letter tile, its name with `dir="auto"` and its slug in an LTR `bdi`. A new account reads one honest next-step line: "Next, you will set up an AI agent, give it knowledge to answer from, and connect your website chat". The unreachable state still offers no create form. The slug field is `dir="ltr"`; "Signed in as" isolates the address. | `before/ar-dark-390-workspace-create.png` → `after/ar-dark-390-workspace-create.png`; `before/en-light-1440-workspace-choose.png` → `after/…`; `after/*-workspace-unreachable.png` |
| **Not found** | A calm state, no longer the error tone: what happened, "Nothing in your workspace has changed", the path that matched nothing as an LTR island (query and fragment are never echoed), and **one** primary way back. Translated. | `before/en-light-1440-not-found.png` → `after/en-light-1440-not-found.png`; `after/ar-light-1440-not-found.png` |
| **Placeholder** | Translated. It is "Not built yet" with one primary way back, and its description is a `computed` rather than a template method. No navigation entry reaches it today, so a **development-only** route, `/design-lab-unbuilt-area` (compiled out of production like the Design Lab), shows it with a sample name. | `after/*-placeholder.png` (no before: unreachable) |
| **Loading state** | `index.html` is unchanged except one comment, so the CSP-hashed script is untouched. New `core/boot/boot-state.ts`: the root keeps the same lockup on screen until the first navigation lands. Redirects keep it up; every other ending removes it. Measured on the production build, the gap between the boot state leaving and the page's `h1` appearing was **40-60 ms at 1x CPU and 150-340 ms at 4x**, a blank frame. It is now **≤ 0 ms**: the two overlap. | `filmstrip/en-1440-reduce-1000.png` (before the fix it was blank; now the lockup); `scratch/gap.mjs` |
| **Focus** | `EntryFrame` starts 36.4's `RouteFocus`. Following "Create one" with Enter now lands on the registration `h1`, and a keyboard sign-in lands on the overview's `h1`. Both previously left focus on `<body>`. The first load still keeps the browser's start. | `keyboard/keyboard.json` |
| **Forced colours** (found in Chrome) | The pressed language option gets a system outline (its edge is a `box-shadow`, which forced colours remove). `BrandMark`'s forced-colours rule now matches the specificity of its voice rules. Before, the mark kept Lagoon and Ember in forced colours, in the sidebar too, despite MASTER section 9. | `after/en-forced-colors-1440-sign-in-before-fix.png` → `after/ar-forced-colors-1440-sign-in.png` |
| **i18n** | 82 new messages with natural Arabic: 81 for these pages and `boot.loading`. Glossary: Workspace now also matches the indefinite «مساحة عمل» («مساحة عمل أخرى»), so the check stays at 0 warnings without bending grammar. | `src/locale/*` |
| **Global layer** | `_primitives.scss`: `button.segmented-option` with `aria-pressed`, a 3:1 edge, a focus ring and the forced-colours outline. | — |
| **Docs** | `docs/design/pages/entry.md` (page brief, layout, the one exception, signature, switch); MASTER: motion line, rule 10 exception pointer, five decision rows; `admin-design-system.md`: D19 bullet, new **D24 Entry pages**. | — |
| **Tests** | +3 files, +26 tests. `entry-frame.spec.ts` (8): landmarks, switch semantics, reload and no-reload, withheld switch, story naming and the example hidden and labelled, signature once per load. Login +4 (switch shown; withheld during an invitation; password managers and paste untouched; one `h1`). Invitation +2, workspace +3, `not-found.spec.ts` (3, including that a query is never echoed), `placeholder.spec.ts` (3), root +2 (loading state until landing; the redirect rule), Arabic render +1 (registration, story, example and digits with the real catalogue). Existing specs needed no change, because the English copy they assert was kept. | — |

## 4. Decisions

- **Concepts (first-impression step 3):**
  - **Expected:** the centred card with a bigger logo. Rejected: it says nothing about the product.
  - **Recommended, chosen:** a split frame with a story panel made of the product's real parts and one signature on the mark.
  - **Bold:** an animated handoff, with the chip sweeping Lagoon to Ember inside the panel. Rejected for Calm: it is a second moment, it repeats the 36.4 "Take over" signature, and MASTER allows one per page.
- **Form first, panel at the inline end.** DOM order equals visual order, so keyboard and screen-reader users meet the task before the story. Rejected: putting the panel first, which would need visual reordering.
- **The example conversation is an exception to MASTER rules 7 and 10, written down** (`pages/entry.md`, MASTER log). The brief asks for real UI fragments, and standing rule 5 forbids fake data. They are reconciled because:
  - it is labelled "Example conversation";
  - it has no names, numbers or dates a reader could take for data, except the policy line "30 days", which is part of the example;
  - it shows only what Nabra does today (answer from knowledge, a person takes over);
  - it never sits next to workspace data.

  If you read rule 5 more strictly, deleting the `<figure>` leaves the tagline and promise, and nothing else depends on it.
- **The `h1` stays the task ("Sign in").** The panel carries the promise on desktop; the lead sentence carries it on a phone.
- **LCP element.** In the Chromium traces the largest paint is the lead sentence (390) or the promise (1440), not the `h1`. All three are static and readable in their first frame, so nothing was changed to force the `h1`.
- **Language switch withheld during an invitation**, rather than storing the token to survive the reload. The token stays memory-only, as 36.x designed it.
- **Not `withEnabledBlockingInitialNavigation()`** for the blank frame. Its initializer would start the first navigation concurrently with the session initializer, so guards could run before the session is known. The root's loading copy closes the gap without touching guard order.
- **Index loading state not redesigned.** It already met the brief (lockup, honest "Loading…", nothing moves). The change was keeping it up until the page lands.
- **Deferred:**
  - the workspace name on "You have been invited" (needs a backend ask, §7);
  - password reset and email verification (honest notes kept);
  - Arabic for the overview and other page bodies (page phases).

## 5. Verification

| Check | Result |
|---|---|
| `npm run format:check` | **PASS**: "All matched files use Prettier code style!" (`final-format.log`) |
| `npm run test:ci` | **PASS**: **78 files, 925 tests, 0 failures**, 0 stray `ERROR` lines (start: 75 files, 899 tests) (`final-test.log`) |
| `npm run build` | **PASS** with 1 warning, the pre-existing `embed-chat.scss` 72 bytes over its 4 kB warning budget (untouched). No new budget warning: `entry-frame.scss`, `auth-page.scss` and `workspace-setup.scss` are under 4 kB. Initial total 285.50 kB raw / 67.64 kB transfer. No budget raised. (`final-build.log`) |
| `npm run i18n:check` | **PASS**: 487 messages, 0 errors, 0 warnings (`final-i18n.log`) |
| Startup JavaScript (rule 13) | **PASS** (+0.68%): 32 files / 404,408 raw / **131,685 gzip** / 116,508 brotli → 33 files / 406,296 raw / **132,581 gzip** / 117,283 brotli. The growth is the root's loading copy and one shared chunk split in two. `EntryFrame` is not in the startup set. The sign-in route's lazy JS is login 3.1 kB + `EntryFrame` 3.0 kB + forms 11.3 kB (pre-existing) gzip. (`startup-js-baseline.json`, `startup-js-final.json`) |
| `hero_check.py` | **PASS**: 254 files, 0 findings at any severity (`hero-check.md`) |
| Five-second filmstrip (the skill's asset, ported to CDP because Playwright must not be added): `/sign-in`, en and ar, 390 and 1440, motion and reduced, CPU 4x, **production build** | **PASS**, 8 runs (`filmstrip/contact-sheet.html`, `summary.json`):<br>• the `h1` is readable in its first frame and in every later frame;<br>• layout-shift sum 0.0000 (1440) and ≤ 0.0001 (Arabic 390);<br>• every frame is pre-paint, the loading lockup or the readable page, with **no blank frame** (there was one before the boot fix);<br>• LCP 1.4-2.9 s at 4x CPU on a local dev proxy, element the lead or promise paragraph;<br>• the mark is mid-settle at ~2.0 s and at rest at 5 s in motion runs, `none` in all reduced runs;<br>• console: only the expected `401 /api/auth/me` network log. |
| Entry pages in Chrome: sign-in, errors, registration, invitation (needs account, missing code, refused, accepted), workspace (choose, create, unreachable), not found, placeholder, loading × en/ar × light/dark × 1440/1024/390 | **PASS**:<br>• 144 final captures plus 8 accepted-state captures, 0 flagged;<br>• 0 px overflow, exactly one `h1` (the loading state has none by design), 0 unnamed controls, the `h1` visible on every page;<br>• no Arabic page has a Latin `h1`;<br>• console: only network logs, the `401 /api/auth/me` session probe (72) and the server's `404` for the made-up invitation token (12);<br>• no JavaScript errors. (`after/entry.json`, `after-accepted/entry-first-run.json`) |
| Full route matrix: 34 routes × 1440/1024/768/390 × light/dark × en/ar | **PASS**: **544 captures, 0 flagged**. Overflow 0, one `h1`, 0 unnamed controls, 0 shell targets under 24 px, 0 console errors. Covers the global primitive, root and `BrandMark` changes. (`matrix/`, `matrix.log`) |
| Contrast (`design-system-tokens` `contrast_check.py`, 34 pairs × light, dark, light + more, dark + more) | **PASS**: 29/29 required pairs pass in all four modes; 5 informational, all decorative. Light / dark examples:<br>• pressed-language edge on the well: 3.23 / 3.97;<br>• tagline: 15.87 / 16.84;<br>• example caption: 5.94 / 5.64;<br>• AI speaker on Lagoon: 6.60 / 9.15;<br>• person speaker on Ember: 6.24 / 9.19;<br>• slug: 5.94 / 5.64;<br>• not-found address: 6.70 / 9.95.<br>(`contrast/*.md`) |
| Keyboard (en + ar, 1440 + 390) | **PASS**:<br>• sign-in order: English → العربية → email → password → Show password → Sign in → Create one; every stop named, `:focus-visible` with a ≥ 2 px ring, inside the viewport;<br>• registration: same order; the invitation page skips the switch while a token waits;<br>• "Create one" + Enter: focus on «أنشئ حسابك» / "Create your account";<br>• a full sign-in typed and submitted from the keyboard reaches `/overview` with focus on its `h1`;<br>• workspace chooser: one stop per workspace.<br>(`keyboard/keyboard.json`, `keyboard/*.png`) |
| Reduced motion | **PASS** (Chrome DevTools MCP, `getAnimations()` on the production build): the motion run has exactly 1 animation (`entry-settle`, delay 150 ms, duration 600 ms, end 750 ms) and 0 on the `h1` and form; the reduced run has 0 animations and the Ember shape's transform is `none`. The example bubbles never animate. |
| RTL mirroring | **PASS**: the frame, panel side, bubbles' sides and tails, handoff divider, switch order, password toggle, not-found arrow and joined-workspace tile all mirror. The mark does not. Emails, slugs and the not-found path are LTR islands, and Western digits are kept ("12 حرفًا"). |
| Forced colours | **PASS after the fix**: the pressed option is outlined, and the mark and story mark use system colours. |
| Static scans | `motion_audit.py`: 1 LOW (horizontal travel without RTL handling). It is a confirmed, deliberate exception, annotated at the site: the mark never mirrors. `security_scan.py` on `src/app`: 0 findings at medium or higher. |
| Provider canary | **PASS** at start and end: `OPENAI_API_KEY`, `STRIPE_SECRET_KEY`, `STRIPE_WEBHOOK_SECRET`, `OMNIAI_WHATSAPP_APP_SECRET`, `OMNIAI_WHATSAPP_VERIFY_TOKEN`, `OMNIAI_LIVE_OPENAI/META/EMAIL` and `Email__Smtp__*` all absent in `omniai-p36-api` (`canary-start.log`, `canary-end.log`) |
| Chrome DevTools MCP | **Used** this phase, in an isolated context: layouts at 1440, 1024, 1366×657 and 390; the language switch; animation timing; reduced motion; forced colours. Bulk captures, filmstrip and keyboard ran through the dependency-free CDP scripts in `scratch/`. |
| Skills | All five named skills were present as `frontend-pro-max:*` and used. |
| Five-second test with people | **NOT RUN**: no participants in this environment. The filmstrip and the brief stand in as a labelled proxy. |
| Screen reader | **NOT RUN**: none available. Names, roles, `aria-hidden` and `aria-pressed` were checked from the DOM and in specs. |
| Real touch device | **NOT RUN**: touch emulated in Chrome. |
| Lighthouse / field data | **NOT RUN**: traces are local and headless at 4x CPU; there is no field data before deployment. |

## 6. See it live

- **http://localhost:4200/sign-in** (dev server running). Use English / العربية at the top. At 1024 px and wider you'll see the story panel; narrower, the form under a compact header.
- Registration: http://localhost:4200/create-account
- Invitation (signed out): http://localhost:4200/accept-invitation#token=anything
- Workspace chooser: sign in as the owner, then http://localhost:4200/workspace. For "Create your workspace", sign in as `p36.newcomer@omniai.example`.
- Not found: http://localhost:4200/no-such-page (signed in). Unbuilt area (development only): http://localhost:4200/design-lab-unbuilt-area
- Synthetic demo accounts: `C:\Users\mouay\Desktop\omniai-redesign-evidence\p36-01\demo-accounts.md`, plus the ones this phase added: `C:\Users\mouay\Desktop\omniai-redesign-evidence\p36-05\demo-accounts-p36-05.md`.
- Before/after gallery: `C:\Users\mouay\Desktop\omniai-redesign-evidence\p36-05\index.html`. Filmstrip: `…\p36-05\filmstrip\contact-sheet.html`.

## 7. Backend asks

1. **Localised refusals.** The server's sentences arrive in English on Arabic pages: a refused sign-in, an address already in use, the password policy, a taken slug, an invitation used, withdrawn or expired. Either honour `Accept-Language`, or publish a stable `code` per refusal so the client can translate. Note that sign-in must keep one deliberately uninformative message.
2. **An invitation preview by token** (workspace name, perhaps the inviter's name), readable before sign-in. "You have been invited" could then name the workspace. Until then it says "a team… their workspace on Nabra".
3. **Password reset and email verification** (already acknowledged on the pages; nothing faked).
4. **Server-held language preference** (D22), so a language chosen before sign-in follows the member to other devices.
5. Unchanged from 36.1: the clean backend restore still needs `-p:NuGetAudit=false`.

## 8. Risks and open issues

- **The example conversation is a deliberate exception** to "sample content only in the Design Lab". It is labelled and documented, and removable by deleting one `<figure>`. Please confirm you're happy with it.
- **The language switch reloads the page.** An address typed before switching is lost (password managers refill). The switch sits above the form, so it is normally used first.
- **The loading copy must stay in step with `index.html`.** Both files say so; a mismatch would show as a small jump, not a failure.
- **`RouteFocus` now also runs on the entry pages.** Moving between them, or signing in, puts focus on the new `h1`. That is intended (36.4's rule), but it is new behaviour there.
- **The `BrandMark` forced-colours fix also changes the sidebar's mark**, to the system colour as MASTER requires.
- **Page bodies beyond this phase are still English in Arabic** (the overview's `h1` "Overview" after sign-in, for example), as accepted at 36.4.
- **Eight synthetic invitees** (Analyst role) now appear in Northwind's team, plus `p36.newcomer@`; all were created through the real API and are listed in `demo-accounts-p36-05.md`.
- **The filmstrip is a port.** The skill's Playwright asset could not run (Playwright must not be added), so its checks were re-implemented on CDP. Its timings are approximate, and a local dev proxy is not production.
- **The new Arabic copy** (the story, the example, the entry pages) should get a native reviewer's pass with the glossary.
- **The development-only route** `/design-lab-unbuilt-area` sits among the shell's children, compiled out of production by the same `ngDevMode` condition as the Design Lab.

## 9. Proposed checkpoint commit message (not committed)

```
feat: give the first screens Nabra's voice, in English and Arabic

Phase 36.5. Sign-in, registration, invitation and workspace setup, plus
not found, the unbuilt-area placeholder and the loading state.

- EntryFrame: the form first, under the lockup and an English / العربية
  switch; from 1024px a story panel with the tagline, one sentence of
  promise and a labelled example conversation built from the transcript
  primitives (aria-hidden, no data). The switch is withheld while an
  invitation token waits in memory.
- One signature: the mark's Ember shape settles into place (translate
  only, 750ms, once per load, none under reduced motion); the heading and
  form never move.
- Every string on these pages is translated, with Arabic; emails, slugs
  and paths are LTR islands; server refusals render with dir="auto".
- Invitation shows the workspace joined; workspace setup gives a new
  account its next step; not found is a calm state with the path and one
  way back; the placeholder is translated and reviewable in development.
- The loading lockup stays until the first page lands (no blank frame);
  RouteFocus also runs on the entry pages; forced colours fixed for the
  segmented pressed state and the mark.

Docs: docs/design/pages/entry.md; MASTER motion, rule 10 exception and
log; admin-design-system D19, D24; glossary.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```
