# Phase 36.12: Live in Docker (production-like stack, final proof and handover)

Date: 2026-10-20. Branch `master`, starting HEAD `440f1d8`. Not committed: the checkpoint prompt follows.

Evidence folder (outside the repository): `C:\Users\mouay\Desktop\omniai-redesign-evidence\p36-12\` (below: `<p36-12>`).

## 1. Summary

- The redesigned Nabra now runs locally exactly as production would serve it: the nginx edge with TLS, two Production APIs, the migrator, PostgreSQL and Redis. It is reachable on https://localhost:8443 and left running.
- In both languages and both themes, at 1440 and 390 px, every screen came through the edge clean: 312 Chrome cells, 0 issues, 0 CSP violations and 0 third-party requests.
- Arabic visitors get the hosted chat and the embedded widget fully in Arabic and right to left, including when the widget is embedded on another website.
- Two defects, both data-dependent and present since earlier phases, only showed with production-like data:
  - On a phone, the Overview still jumps while it loads when several items need attention (CLS 0.48).
  - A long, human-handled conversation's transcript is a scroll area a keyboard cannot reach.
- The backend images could only be built with the NuGet vulnerability audit switched off (approved, outside the repository). The audit failure blocks a real release until the backend packages are patched.

## 2. Starting point

| | |
| --- | --- |
| Branch | `master` |
| HEAD | `440f1d8 feat: finish the redesign in both languages, quietly and quickly` (the accepted 36.11 checkpoint) |
| Worktree | clean (`git status --short` printed nothing) |
| Already running | the p36 dev stack: `omniai-p36-api` (127.0.0.1:5027, healthy), dev server on :4200 |

## 3. Changes by area

`git status --short` at the end:

```
?? docs/phase-reports/PHASE_36_12_LIVE_DOCKER_REPORT.md
```

This report is the only repository change. No frontend, backend, `deploy/`, migration or package change was made.

Created outside the repository (everything is listed in `<p36-12>\HANDOVER.md`):

| Area | What | Where |
| --- | --- | --- |
| Images | `omniai-edge:p36` built from `deploy/edge.Dockerfile`, **unchanged**. `omniai-api:p36` and `omniai-migrator:p36` built from copies whose only change is `ENV NuGetAudit=false` in the SDK build stage (approved, §4 D1). | `<p36-12>\live\dockerfiles\` |
| Stack | compose project `omniai_p36_live`, from `deploy/compose.production.yml` unchanged. Containers: `postgres`, `redis`, `migrator`, `migrator-privileges`, `api-a`, `api-b`, `edge`. Volumes `omniai_p36_live_pgdata`, `omniai_p36_live_reports`. Networks `omniai_p36_live_edge`, `omniai_p36_live_data`. | Docker |
| Configuration | `live.env`: generated secrets, never printed. Loopback-only edge on 8080/8443, `OMNIAI_PUBLIC_HTTPS_PORT_SUFFIX=:8443`, `free` default plan. | `<p36-12>\live\live.env` |
| TLS | a 7-day drill CA plus a `localhost`/`127.0.0.1` leaf certificate; SPKI pin in `tls\spki-pin.txt`. The CA is trusted nowhere. | `<p36-12>\live\ca\`, `live\tls\` |
| Scripts | `live.sh` (up, stop, start, status, canary, logs, remove), `drill-seed.override.yml`, `widget-host.mjs` (the second origin), `gen-live.sh` | `<p36-12>\live\` |
| Evidence | the matrix, chat and widget captures, SignalR, perf, axe, keyboard, RTL/motion, contrast, logs and gallery | `<p36-12>\…` |

Before and after screenshots: §11, the gallery index.

## 4. Decisions

- **D1. NuGet audit (owner-approved).** `deploy/api.Dockerfile` and `deploy/migrator.Dockerfile` fail on a clean restore because warnings are errors:
  - NU1903 and NU1902 on transitive `System.Security.Cryptography.Pkcs` 10.0.0: GHSA-846f-f764-76mx (high) and GHSA-2fmv-rv2c-59x7 (moderate);
  - NU1902 on `MessagePack` 2.5.302: GHSA-qhrr-8q5h-9q3h (moderate). **This one is new since 36.1.**

  You chose out-of-repository copies with `ENV NuGetAudit=false`, so `deploy/` and `backend/` are untouched. Rejected alternatives: stopping the phase, or bumping the packages in `backend/` (out of scope). The runtime images are otherwise exactly what `deploy/` builds. **This is a release blocker until the backend pins patched versions** (§7 ask 1).
- **D2. Chrome that trusts only the drill certificate.** The Chrome DevTools MCP refused the edge (`net::ERR_CERT_AUTHORITY_INVALID`). The phase forbids adding the CA to any trust store. Every browser check through the edge therefore used the dependency-free DevTools-protocol scripts, in a Chrome with an isolated profile and `--ignore-certificate-errors-spki-list=<leaf pin>`, valid only for that process.
- **D3. Lighthouse not run.** The MCP's Lighthouse cannot reach the edge (D2), and installing Lighthouse would add a package. The prompt's alternative was used instead: DevTools-protocol lab measurement (§5.3).
- **D4. Canary before any traffic.** The first bring-up started everything **except the edge**, so no request could reach an API until the canary had proved both containers clean. The edge started afterwards.
- **D5. Drill-only settings, as in Phase 35, never for production.** They are documented in `<p36-12>\demo-accounts.md`:
  - **Northwind moved to `starter`** by operator SQL (`logs\drill-plan-sql.log`), because the seed needs 3 members. Al Noor stays on the default `free` plan.
  - **Public-chat limits raised during seeding only** (`drill-seed.override.yml`), because the edge correctly makes every seeded visitor one client. The APIs were then recreated with production defaults, proved by 0 `PublicChatProtection__*` variables. AI generation budgets were never raised.
- **D6. Two defects reported, not fixed in this phase.** The Overview layout shift and the transcript's keyboard reachability (§8) both need a change to the shipped build, followed by a rebuild of the edge image and a re-run of this whole drill. This phase is the final verification, so they are reported with their exact cause and fix for a remediation step you choose to run.
- **D7. The SignalR proof uses changes the backend announces.** A customer message whose AI turn fails is never broadcast (`CustomerTurnRunner.AnswerAsync`: `FailTurnAsync`, then rethrow, with no announcement). With no provider, the AI-handled path cannot update live; that is a backend ask (§7 ask 2), not an edge fault. Live delivery was proved with a take-over and team replies from another session.

## 5. Verification

### 5.1 Gates

| # | Check | Result |
| --- | --- | --- |
| 1 | `npm run format:check` | **PASS**: "All matched files use Prettier code style!", exit 0 |
| 2 | `npm run test:ci` | **PASS**: 93 files, 1,101 tests, 0 failures, exit 0 |
| 3 | `npm run build` | **PASS**: exit 0, **0 warnings**, no budget warning. Initial total 310.48 kB raw / 72.79 kB transfer. |
| 4 | Startup JS (loader + application chunk, rule 13) | **PASS**: start 38 files, 417,841 raw / **137,638 gzip** / 121,845 brotli; end identical (no frontend change). |
| 5 | Image builds | edge **PASS** with the unchanged Dockerfile. api and migrator **FAIL** with the unchanged Dockerfiles (NuGet audit, D1), then **PASS** with the approved copies. |
| 6 | Edge SPA = repository build | **PASS**: `index.html`, `main-*.js` and `styles-*.css` have identical md5. All 5 WOFF2 fonts are present. |
| 7 | Start order | **PASS**: postgres and redis healthy → migrator applied `Initial` and `ProductionReadinessHardening` (exit 0) → migrator-privileges (exit 0) → api-a and api-b healthy in 10 s. |
| 8 | Runtime role on `__EFMigrationsHistory` | **PASS**: `SELECT` only |
| 9 | Provider canary, inside both APIs, names only | **PASS**, 3 times (before any traffic, after recreating the APIs, at the end). In both: `OPENAI_API_KEY`, `STRIPE_SECRET_KEY`, `STRIPE_WEBHOOK_SECRET`, `OMNIAI_WHATSAPP_APP_SECRET`, `OMNIAI_WHATSAPP_VERIFY_TOKEN`, `OMNIAI_LIVE_OPENAI`, `OMNIAI_LIVE_META` and `OMNIAI_LIVE_EMAIL` absent; `Email__Smtp__*`: 0; `ASPNETCORE_ENVIRONMENT=Production`. `deploy/providers.env` absent. |
| 10 | No live provider traffic | **PASS**: `ai_provider_usages` = 0 at the end |
| 11 | Seed through the edge | **PASS**: 3 accounts, 2 workspaces, 46 conversations, 5 reports completed. Honest refusals observed: 4th agent 409 `entitlement.aiagents.exhausted`; indexing 503; AI turns 503; 73 messages 429 from the generation budget. |
| 12 | Chrome matrix: 39 routes × 1440/390 × light/dark × en/ar (312 cells) | **PASS**. See §5.2. |
| 13 | Hosted chat, en and Arabic browser | **PASS**: `lang=en dir=ltr` and `lang=ar dir=rtl` with Arabic copy ("إرسال"), at 1440 and 390, light and dark. Overflow 0, CSP 0, third-party 0. A sent message is stored and the honest failure line is shown. |
| 14 | Widget from a second origin (`https://127.0.0.1:8444`, allowed by its endpoint), en and Arabic | **PASS**: closed and open at 1440/390, light and dark. Read inside the cross-origin frame: `lang=ar dir=rtl` with an Arabic browser. Frame CSP 0, third-party 0, no frame console errors. A sent message is stored and the honest failure line is shown. |
| 15 | CSP | **PASS**: 0 violations across all 312 cells, the chat and widget runs, and SignalR. The 2 hashes match the served `index.html` exactly: the inline pre-paint script (`sha256-IxNR…`) and the stylesheet `onload` handler (`sha256-MhtP…`, under `'unsafe-hashes'`). Fonts load under `font-src 'self'`. |
| 16 | Security headers | **PASS**: §5.4 |
| 17 | No third-party requests | **PASS**: 0 in every cell, the chat and widget runs, and every Web Vitals run |
| 18 | SignalR through the edge | **PASS**: `wss://localhost:8443/hubs/workspace-inbox` upgraded 101, twice (bell and inbox; known 36.11 item). A take-over and 3 team replies from another session appeared without reload, each in about 261–266 ms, across both APIs. See D7 for the AI-failed path. |
| 19 | Fonts and assets cached | **PASS**: hashed JS, CSS and WOFF2 carry `Cache-Control: public, max-age=31536000, immutable`. Warm visit: 47/50 (sign-in) and 52/62 (Overview) requests from cache, 0 static files refetched, fonts from cache. |
| 20 | Contrast (`design-system-tokens` `contrast_check.py` on the **served** stylesheet, 36.11 pair list) | **PASS**: 123 rows in each of light, dark, light-more and dark-more. 113 pass, 0 fail; the other 10 are decorative "info" pairs with no requirement. |
| 21 | Keyboard | **PASS**: 20 pages swept (en and ar), 884 stops, 0 problems. Every journey step passes in both languages: skip link, Ctrl+K palette, inbox to conversation, agent preview, sign-in. |
| 22 | Reduced motion | **PASS**: 0 of 472 elements animate longer than 1 ms under `reduce` (en and ar). Frequent motion is 150 ms. |
| 23 | RTL mirroring | **PASS**: directional icons mirrored; emails, URLs, ranges and shortcuts kept as left-to-right islands (8–10 per page); charts put the latest day at the reading end; switches move toward the reading end. |
| 24 | axe-core 4.12 (WCAG 2.0–2.2 A/AA), 164 pages through the edge | **FAIL (1 root cause)**: 160 pages clean. `scrollable-region-focusable` (serious) on `app-conversation-transcript` for the human-handled conversation, en/ar × light/dark (§8 R2). Best practice: `landmark-one-main` and `region` on 4 pages each, the embed document (kept by the 36.11 decision). |
| 25 | Lab performance | **PARTIAL**: §5.3. Overview CLS on mobile **FAIL**; everything else as measured. |
| 26 | Lighthouse | **NOT RUN**: D3 |
| 27 | Chrome DevTools MCP | **NOT RUN** for edge pages: `ERR_CERT_AUTHORITY_INVALID`, and the CA must not be trusted (D2) |
| 28 | Log canary, 7 containers, 10,375 lines | **PASS**: 0 occurrences of the 6 generated secrets, the host's OpenAI key, the synthetic password, `omniai.session=`, `Set-Cookie`, `Authorization:`, `X-XSRF-TOKEN`, `Bearer `, `sk-`, `access_token=`, `X-OmniAI-Embed-Session`, `Password=` or `@nabra.example` |
| 29 | Static scans (skills' scripts) | **PASS**: `security_scan.py` (medium+): 0 leads in 257 files. `perf_scan.py`: 0 critical or high (3 medium). `test_inventory.py`: 0 flags. `bundle_budget_check.py`: report only, initial 310.5 kB raw / 85.7 kB gzip. |
| 30 | TLS | **PASS** (TLS 1.1 **NOT RUN**): TLS 1.3 and 1.2 negotiate, and the chain verifies against the drill CA. TLS 1.1 could not be offered by this machine's OpenSSL 3.5 client ("no protocols available"). |

Every result above is LIVE: the real stack, through the edge, with synthetic data created through the API.

### 5.2 Chrome matrix detail

The matrix ran through `https://localhost:8443` at 1440 and 390 px, light and dark, en and ar. It covers 33 signed-in routes (owner) plus sign-in, create-account, accept-invitation, both hosted chats and the embed document.

| Language | Cells | Overflow | `h1` ≠ 1 | Unnamed controls | Clipped | Small shell targets | Console errors | CSP | Third-party |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| en | 156 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| ar | 156 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |

- **Expected network logs:** 12 per language, the signed-out `GET /api/auth/me` 401 on sign-in, create-account and accept-invitation (36.11 decision; backend ask 1 in 36.11).
- **Titles:** every title is "… · Nabra" or "… · نبرة"; "OmniAI" appears in 0 titles.
- **Visitor pages:** in the Arabic run, the 3 visitor pages stayed `en/ltr` by design, because they follow the browser language. They are covered with an Arabic browser in rows 13 and 14.
- **Files:** `<p36-12>\matrix\{en,ar}\*.png` and `matrix-{en,ar}.json`.

### 5.3 Lab performance (DevTools protocol, through the edge, median of 5 cold runs)

- **Mobile profile:** 390×844, 4× CPU, 150 ms RTT, 1.6 Mbps down.
- **Desktop profile:** 1440×900, unthrottled.
- **Conditions:** cache disabled for cold runs. These are lab numbers, not field Core Web Vitals.

| Page | Profile | FCP | LCP (element) | CLS | Long tasks | Transfer (JS / fonts) |
| --- | --- | ---: | --- | ---: | ---: | --- |
| Sign-in | desktop | 900 ms | **1.18 s** (`p.entry-promise`) | 0.00 | 0 ms | 342.6 kB (192.3 / 103.2) |
| Sign-in | mobile | 1.18 s | **4.55 s** (`p.auth-lead`) | 0.00 | 709 ms | same |
| Overview | desktop | 48 ms | **0.40 s** (`p.now-note`) | **0.084** | 65 ms | 327.7 kB (217.5 / 57.6) |
| Overview | mobile | 296 ms | **3.61 s** (`p.page-header-description`) | **0.477** | 1,127 ms | same |

Against the web-performance thresholds:
- **LCP:** desktop is good; mobile cold is in "needs improvement" (Overview) and "poor" (sign-in) under these slow-4G, 4× CPU conditions.
- **CLS:** desktop Overview is good (0.084); mobile Overview is **poor** (0.477).

What the follow-up measurements showed:
- **Overview CLS is deterministic.** A warm repeat visit on mobile gives 0.4769 in 3 of 3 runs, always one shift of the "Right now" block when the attention cards arrive (§8 R1).
- **The cold TTFB is not the server.** Cold sign-in TTFB of about 0.8 s is entirely `fetchStart → DNS` in a brand-new browser context, i.e. browser start-up. Connect 4–9 ms, TLS 3–8 ms and server wait 1–5 ms; the next navigation's TTFB is 4–23 ms.
- **The English sign-in downloads an Arabic font.** It pulls `ibm-plex-sans-arabic-500` (45 kB) for one word, the "العربية" language switch, which is a known gotcha in the web-performance skill.

Files: `<p36-12>\perf\vitals.json`, `vitals-probe.json`, `bundle-budget.md`, `perf-scan.md`.

### 5.4 Headers through the edge (`<p36-12>\logs\edge-headers.log`)

- **SPA (`/`, `/sign-in`, `/overview`, `/chat/…`):**
  - CSP: `default-src 'self'`; `script-src 'self'` plus the 2 hashes; `style-src 'self' 'unsafe-inline'`; `img-src 'self' data:`; `font-src 'self'`; `connect-src 'self' wss://localhost:8443`; `object-src 'none'`; `base-uri 'self'`; `form-action 'self'`; `frame-ancestors 'none'`.
  - Also: `X-Frame-Options: DENY`, `nosniff`, `Referrer-Policy: no-referrer`, `Permissions-Policy`, HSTS for one year, `Cache-Control: no-cache`.
- **`/embed/…`:** the same CSP, but `frame-ancestors *`, no `X-Frame-Options`, `Referrer-Policy: strict-origin`.
- **API:**
  - Headers: `default-src 'none'; frame-ancestors 'none'`, DENY, `nosniff`, HSTS.
  - Responses are **not** compressed, which keeps strong ETags; `If-Match` writes worked through the edge (widget PUT 200, take-over 200).
  - Cookies: `omniai.session` and the antiforgery cookie are `Secure; HttpOnly; SameSite=Lax`; `XSRF-TOKEN` is `Secure; SameSite=Lax`. Names and flags only; values were redacted.
- **Hidden surfaces:** `/health`, `/health/ready` and `/openapi/v1.json` return 404; an unknown `/api/*` returns 404 ProblemDetails. `Server: nginx`, no version.
- **HTTP:** `http://localhost:8080/sign-in?x=1` → 308 to `https://localhost:8443/sign-in?x=1`.

## 6. See it live

| What | Where |
| --- | --- |
| **Nabra, production-like** | **https://localhost:8443** (sign-in: https://localhost:8443/sign-in) |
| Widget on a second origin | https://127.0.0.1:8444/?key=7UynZcljXN-0StfMzB-9RA (append `&lang=ar` for an Arabic host page) |
| Hosted chat | https://localhost:8443/chat/7UynZcljXN-0StfMzB-9RA (Northwind), https://localhost:8443/chat/WrQ1z6nmEc2kphL3mDi6VQ (Al Noor) |
| **Demo accounts** | `C:\Users\mouay\Desktop\omniai-redesign-evidence\p36-12\demo-accounts.md` (`live.owner@`, `live.support@` and `live.analyst@nabra.example`; the password is in that file, outside the repository) |
| Handover (certificate, commands) | `C:\Users\mouay\Desktop\omniai-redesign-evidence\p36-12\HANDOVER.md` |
| Dev stack (unchanged, still running) | http://localhost:4200, accounts in `p36-01\demo-accounts.md` |

### The certificate

The drill CA is trusted nowhere. The recommended way in is a separate Chrome that trusts only this certificate's key, with a throw-away profile. This pinning approach is the one every check in this phase used, headless:

```bash
"/c/Program Files/Google/Chrome/Application/chrome.exe" \
  --user-data-dir="C:/Users/mouay/Desktop/omniai-redesign-evidence/p36-12/live/chrome-profile" \
  --ignore-certificate-errors-spki-list=sdxcG1+w+SqUkn/UXaih3oGQC/iYri4Ufqt1o9xQUAQ= \
  https://localhost:8443/sign-in
```

- Add `--lang=ar` with a second profile folder to see the visitor pages as an Arabic visitor.
- In any other browser, expect "Your connection is not private" (`NET::ERR_CERT_AUTHORITY_INVALID`). Choose Advanced, then Continue, once for `localhost:8443` and once for `127.0.0.1:8444`.
- Do not add the CA to Windows. The edge's one-year HSTS could then stick to `localhost` and affect the dev server on http://localhost:4200.

### Commands (Git Bash)

```bash
L="C:/Users/mouay/Desktop/omniai-redesign-evidence/p36-12/live/live.sh"
bash "$L" stop      # stop everything, keep data
bash "$L" start     # start again, wait for both APIs healthy
bash "$L" status    # containers, health, migrations
bash "$L" canary    # names only: no provider secret inside either API
bash "$L" remove    # remove ONLY Phase 36.12's stack: omniai_p36_live containers, its 2 volumes
                    # and 2 networks, and the images omniai-{api,migrator,edge}:p36
node "C:/Users/mouay/Desktop/omniai-redesign-evidence/p36-12/live/widget-host.mjs"   # second origin, after a reboot
```

Production limits you will meet: **5 new conversations per client per 5 minutes**, and every tab on this machine is the same client behind the edge. No AI reply ever arrives, because there is no provider; take a conversation over to reply as a person.

## 7. Backend asks

New in 36.12:

1. **Patch the vulnerable packages so a clean restore passes the NuGet audit (release blocker).**
   - Transitive `System.Security.Cryptography.Pkcs` 10.0.0: GHSA-846f-f764-76mx (high), GHSA-2fmv-rv2c-59x7. Carried since 36.1.
   - `MessagePack` 2.5.302: GHSA-qhrr-8q5h-9q3h. **New.**
   - Pin patched versions centrally in `Directory.Packages.props`; then the unchanged `deploy/` Dockerfiles build again.
2. **Announce a conversation change when an AI turn fails.** `CustomerTurnRunner.AnswerAsync` calls `FailTurnAsync`, then rethrows without `Announce`. The customer's message is stored but never pushed, so an open inbox misses it until a refresh. This is the common case whenever the provider is down or not configured.

Still open from 36.1–36.11 (unchanged; consolidated in the 36.11 report §7):
- a session probe without a 401;
- customer identity on conversation rows;
- the latest transcript page first;
- failed AI turns marked in the transcript;
- who holds a role;
- an invitation status filter;
- the report type on notifications;
- a translated permission catalogue;
- the fallback and subscribed plan details on billing;
- email subjects in Nabra, translated;
- widget appearance on the hosted page;
- a translated loader frame title and the launcher side from the host page's `dir`;
- the workspace name on public reads;
- stable codes on origin refusals and file refusals;
- a persisted indexing outcome;
- the source file size in the knowledge list;
- channels per agent;
- provider usage by purpose;
- a comparison period;
- a localised report catalogue;
- the workspace's first activity date.

## 8. Risks and open issues

- **R1. Overview layout shift on phones (FAIL, measured).**
  - **Measurement:** mobile CLS 0.477 (poor), desktop 0.084, deterministic, also on warm visits.
  - **Cause:** [overview.html:16-25](../../frontend/src/app/features/overview/overview.html#L16-L25) holds one skeleton card while "Needs attention" loads. Northwind has 5 items (3 limits reached, failed AI replies, unread notifications), so the stacked cards push "Right now" down by several hundred pixels.
  - **Status:** this was the 36.11 open risk; 36.11 measured 0.00 with less attention data.
  - **Fix options (need a design call):**
    - keep "Needs attention" below "Right now" on narrow screens;
    - show a compact list there, one line per item, with a reserved height for the first few;
    - reserve the height of the last known count.
- **R2. Transcript scroll region not keyboard-reachable (axe serious, WCAG 2.1.1).**
  - **Cause:** [conversation-transcript.scss:18-22](../../frontend/src/app/features/conversations/conversation-transcript/conversation-transcript.scss#L18-L22) makes the host an `overflow-y: auto` region in the filled layout, with no `tabindex`, role or name. When the transcript overflows and has no focusable content (no "Show earlier messages" button, no links), keyboard users of browsers without focusable scrollers cannot scroll it.
  - **Fix:** while it is the scroll region, give the host `tabindex="0"`, `role="region"` and `aria-labelledby` pointing at its "Messages" heading. Add a spec for it.
  - **Why it was missed:** 36.11's test conversation always had the button.
- **R3. Images build only with the NuGet audit off** (ask 1). Do not release with these images.
- **R4. Mobile cold LCP on slow 4G:** 4.55 s for sign-in and 3.61 s for Overview (lab). Contributing:
  - 192 kB of startup JS;
  - the English sign-in downloads the Arabic 500 weight (45 kB) for the "العربية" label (fix: render that label with the system Arabic font or an SVG wordmark);
  - 0.7–1.1 s of long tasks at 4× CPU.

  There is no field data (no RUM, which needs the `web-vitals` package and approval).
- **R5. Two hub sockets per signed-in page** (bell and inbox), carried from 36.11.
- **R6. AI-handled conversations do not update live while the provider fails** (ask 2).
- **R7. The drill CA expires on 2026-10-27.**
  - **Effect:** after that the edge certificate is invalid.
  - **Fix:** regenerate with `gen-live.sh` into a fresh folder, or remove the stack.
- **R8. Synthetic data is thinner than on the dev stack.** The production generation budget refused 73 seeded messages (honest behaviour), so many AI-handled conversations show "No messages yet".
- **Not covered:**
  - field Core Web Vitals;
  - TLS 1.1 refusal (client limitation);
  - a rolling restart under load (Phase 35 covered it, and it is unchanged);
  - real providers (forbidden).

## 9. Proposed checkpoint commit message (not committed)

```
docs: prove the redesign live behind the production edge

Phase 36.12: Nabra served as production would serve it (nginx edge with
TLS, two Production APIs, migrator, PostgreSQL, Redis), from this worktree,
on loopback with a short-lived drill CA. Canary clean in both APIs before
any traffic; no provider traffic (0 usage rows); seeded through the edge.

312 Chrome cells (en/ar, light/dark, 1440/390) clean: no CSP violation,
no third-party request; hosted chat and the widget on a second origin in
Arabic and RTL; SignalR through the edge; immutable caching proven.

Found and reported, not fixed: Overview CLS 0.48 on phones with several
attention items; the transcript scroll region is not keyboard-reachable.
The API and migrator images needed NuGet audit off (approved, outside the
repository): Pkcs 10.0.0 and MessagePack 2.5.302 must be patched before
any release.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

## 10. Final rubric scores

Same ten screens and the same rubric (ui-ux-design-lead critique, 1–5) as 36.1 §6.1 and 36.11 §5. Every screen was looked at through the edge in this phase:
- Overview: en, 1440, light;
- Conversations: ar, 390, dark;
- Conversation detail: en, 1440, live;
- Agent editor: ar, 1440;
- Knowledge: en, 390;
- Channels: ar, 1440;
- Analytics: en, 1440, dark;
- Sign-in: ar, 1440;
- Hosted chat: ar, dark;
- Widget: en open, ar after sending.

Scores changed only where this phase measured something new.

| Screen | Hierarchy | Clarity | Consistency | Accessibility | Responsive/RTL | Craft | Distinctiveness | Performance | 36.12 | 36.11 | 36.1 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | ---: | ---: | ---: |
| Overview | 4 | 5 | 5 | 5 | 5 | 4 | 4 | **3** (CLS 0.48 on phones, R1) | **4.4** | 4.5 | 3.4 |
| Conversations list | 4 | 4 | 4 | 5 | 5 | 4 | 4 | 4 | **4.3** | 4.3 | 2.6 |
| Conversation detail | 5 | 4 | 5 | **4** (R2) | 5 | 4 | 5 | 3 | **4.4** | 4.5 | 2.8 |
| AI agent editor | 4 | 5 | 5 | 5 | 5 | 4 | 4 | 4 | **4.5** | 4.5 | 3.3 |
| Knowledge list | 4 | 4 | 5 | 5 | 5 | 4 | 4 | 4 | **4.4** | 4.4 | 3.4 |
| Channels | 4 | 5 | 5 | 5 | 5 | 4 | 4 | 4 | **4.5** | 4.5 | 3.1 |
| Analytics | 4 | 4 | 5 | 5 | 5 | 4 | 4 | 4 | **4.4** | 4.4 | 3.1 |
| Sign-in | 5 | 5 | 5 | 5 | 5 | 4 | 4 | 4 | **4.6** | 4.6 | 3.3 |
| Hosted chat | 4 | 4 | 5 | 5 | 5 | 4 | 4 | 4 | **4.4** | 4.4 | 2.9 |
| Widget (open and closed) | 4 | 4 | 5 | 5 | 5 | 4 | 4 | 4 | **4.4** | 4.4 | 3.0 |

Every screen still meets the admin ship bar on average, but two hold a measured defect: Overview (performance 3) and Conversation detail (accessibility 4, performance 3). Fixing R1 and R2 returns both to their 36.11 scores.

## 11. Before/after gallery index (36.1 versus now)

**`C:\Users\mouay\Desktop\omniai-redesign-evidence\p36-12\gallery\index.html`**

- 160 comparisons, 0 images missing.
- Each row puts three images side by side: **Before** (36.1, dev server, English), **Now, English** and **Now, Arabic**, both now-images through the edge.
- Coverage: 33 signed-in routes; sign-in, create-account, accept-invitation and the embed document; the hosted chat; and the widget closed and open. Each at 1440 and 390, light and dark.
- The visitor surfaces' Arabic column comes from an Arabic browser, and the widget is shown on the second origin.

| Family | Before (36.1) | Now: English / Arabic (36.12) |
| --- | --- | --- |
| Signed-in routes | `p36-01\before\owner-<route>-<width>-<theme>.png` | `p36-12\matrix\en\owner-en-<theme>-<width>-<route>.png` / `matrix\ar\owner-ar-…` |
| Entry pages and embed | `p36-01\before\public-<route>-<width>-<theme>.png` | `p36-12\matrix\{en,ar}\public-<lang>-<theme>-<width>-<route>.png` |
| Hosted chat | `p36-01\before\public-hosted-chat-<width>-<theme>.png` | `p36-12\chat-widget\hosted-{en,ar}-<theme>-<width>.png` |
| Widget | `p36-01\before\public-widget-{closed,open}-<width>-<theme>.png` | `p36-12\chat-widget\widget-{en,ar}-<theme>-<width>-{closed,open}.png` |
| Live proof | (none) | `p36-12\signalr\conversation-live-1440-light-en.png`, `chat-widget\{hosted,widget}-{en,ar}-light-390-after-send.png` |

The 36.1 route slugs map to the 36.12 slugs as follows:

| 36.1 | 36.12 |
| --- | --- |
| `conversation-detail-human` | `conversation-human` |
| `conversation-detail-ai` | `conversation-ai` |
| `channels-website-chat` | `website-chat` |
| `channels-website-chat-detail` | `website-chat-detail` |
| `channels-whatsapp` | `whatsapp` |
| `channels-whatsapp-detail` | `whatsapp-detail` |

The synthetic data differs between the two stacks.

## 12. Release notes: every Phase 36 change

Phase 36, "Two Voices", ran from `d2c907d` (Phase 35) to `440f1d8`. It is frontend only: no endpoint, schema or permission changed.

**Identity and foundation**
- **36.2 (`ce4ac51`)**
  - A new "Two Voices" identity: calm ink on a porcelain light theme and a separately designed dark theme. The indigo accent is gone, and colour is kept for meaning.
  - Lagoon marks the AI and Ember marks a person, always with a glyph and a word.
  - Self-hosted Instrument Sans and IBM Plex Sans Arabic: 5 WOFF2 files, split by `unicode-range`, metric-matched fallbacks, no third-party request.
  - A new mark, favicon and loading state, and a 150 ms page crossfade (none under reduced motion).
  - Engineering:
    - a new token system (voice, action, focus, chart, radius roles, motion, type roles);
    - 79 contrast pairs proven in four modes;
    - VoiceChip, VoiceSplit and VoiceTrack;
    - a development-only Design Lab;
    - `docs/design/MASTER.md`.
- **36.3a (`1cb4036`)**: the Arabic and RTL decision record: `@angular/localize` with runtime translations and one build; Western digits and the Gregorian calendar; mirrored charts.
- **36.3b (`af3ea0b`)**
  - Nabra in Arabic with full right-to-left layout; the language is chosen in the account menu.
  - The visitor pages follow the visitor's browser language.
  - One formatter for every date and number. Language and direction are set before first paint, CSP-safe.
  - Engineering: `@angular/localize` added; i18n check and extract scripts; the app loads through a dynamic import.
- **Brand (`6331671`)**
  - The public name is Nabra / نبرة everywhere, and tabs read "<page> · Nabra".
  - Versioned icon names, so the year-long cache cannot serve the old icon.
  - Storage keys and the widget protocol are unchanged, so saved preferences carry over.
  - A spec blocks the code name from any message or template.

**Navigation and first impression**
- **36.4 (`325bc34`)**
  - A Ctrl/⌘K command palette for pages and permitted actions (it searches commands, not records).
  - The workspace mark and name in the top bar, with one-click switching.
  - Focus moves to each new page's heading.
  - Notification panel states. All targets are at least 24 px. A calmer sidebar.
- **36.5 (`dfa55e6`)**
  - A new entry frame for sign-in, registration, invitation and workspace setup: the form first, with a story panel from 1024 px.
  - The entry pages in Arabic, with a language switch before sign-in.
  - One 750 ms signature moment, and no blank frame while loading.
  - Honest invitation and next-step messages; a calm not-found page; forced-colours fixes.

**Work surfaces**
- **36.6 (`e43a821`)**
  - A three-pane inbox from 1280 px, with a voice chip on every row.
  - The take-over and hand-back sweep.
  - Transcript: day separators and a bubble style per voice; handoff dividers drawn from real author changes; an honest note when the AI has not replied; an "N new messages" button.
  - Composer: Ctrl/Cmd+Enter sends, drafts are kept per conversation, and retries reuse the same idempotency key.
  - Fixed: live refresh past the first page, and a leaked socket.
  - Customers directory and profile redesigned.
- **36.7 (`c352f9e`)**
  - The Overview answers three questions: what needs you, what is happening now, how the week went.
  - Six dependency-free SVG charts, each with "View as table", a keyboard readout and Arabic mirroring.
  - Analytics keeps the period in the URL. Honest provider usage that never shows `$0.00` for no calls.
  - A reports catalogue, and the unsaved-changes guard on schedules.
- **36.8 (`b95267b`)**
  - The agents list shows status as a word and the channels each agent is used on.
  - Guided agent creation, and a four-section editor with a live customer view.
  - The knowledge library shows type, size, indexing state and last change; upload by drop zone; keyboard-friendly selection of an agent's knowledge.
- **36.9 (`783ad75`)**
  - One shared chat for the widget, the hosted page and the settings preview.
  - Arabic visitors get full Arabic and RTL.
  - The widget's own JS fell from 33.0 to 13.2 kB gzip, and it no longer shows a 401 on customer websites.
  - Accent rules keep labels at 4.5:1 or better on any colour.
  - The channels hub shows real states, and Instagram and Messenger as "Not available yet".
  - Widget settings get a live preview and a guard.
- **36.10 (`fb2d416`)**
  - Roles show 29 permissions in 11 areas, with impact hints.
  - Member detail shows what a member can do and what saving changes.
  - Team paging, invitation expiry and history.
  - Plan & usage meters in four worded states.
  - Billing shows the grace-period deadline.
  - Notifications translated by kind and grouped by day; email preferences guarded.

**Polish**
- **36.11 (`440f1d8`)**
  - Arabic: a read-through of 2,028 messages, 113 corrected; duplicate message IDs went to 0.
  - The agent preview uses the shared chat.
  - Token lint went to 0 warnings.
  - Every frequent action takes 150 ms or less.
  - The sidebar and top bar are landmarks; the account button's spoken name begins with its initials.
  - No sideways scroll at 320 px.
  - Overview CLS 0.13 → 0.00 on the 36.11 data. On data with several attention items it measures 0.48 on phones (this phase, R1).
  - Deep-linked conversations read in parallel.
- **36.12 (this phase)**: no product change; the production-like proof above.

**By the numbers since 36.1**
- Tests: 784 → 1,101.
- Messages: 0 → 2,028 in two languages.
- Startup JS: 113.6 → 137.6 kB gzip (+21%; every phase stayed under the 10% rule).
- New self-hosted font families: 2.
- One new dependency: `@angular/localize` ^22.1.5, the only `package.json` dependency change since `d2c907d`. Two scripts were added: `i18n:extract` and `i18n:check`.

## Revision 1 (2026-10-20): review notes fixed

### R1.1 The notes

1. **R1, Overview.** The page must not move while it loads, at any width and with any number of attention items.
   - Keep "Needs attention" first, as a compact list: one line per item, at most 3 visible, "+N more" for the rest.
   - Give that area a fixed height in every state: loading, none, a few, many.
   - Target: CLS 0.00 on the live data, never above 0.05, on mobile and desktop, over 5 cold and 3 warm runs.
2. **R2, conversation transcript.** While it is the scroll region:
   - give it `tabindex="0"`, `role="region"` and `aria-labelledby` pointing at its "Messages" heading, with a visible focus ring;
   - add a spec;
   - axe must be clean on all 164 pages.
3. **R4, English entry pages.** The English sign-in downloads the Arabic 500 font (45 kB) for «العربية». Draw that label without the web font. English entry pages must load no Arabic font file, and Arabic pages keep IBM Plex Sans Arabic.
4. **Section 6 password.** Remove the synthetic password from §6 and point to `demo-accounts.md`. No password may appear in any committed file.

### R1.2 What changed

Only `frontend/` and this report changed. `backend/` and `deploy/` are untouched (`git diff HEAD -- deploy/` is empty).

| Note | Change | Files |
| --- | --- | --- |
| R1 | "Needs attention" is one panel with a fixed height in every state: three 3rem rows plus a 2.75rem footer, `block-size: calc(11.75rem + 2px)`. See below for the row layout and states. | `features/overview/overview.{html,ts,scss,spec.ts}` |
| R1 | Two global utilities, `.visually-hidden-below-md` (below 768px) and `.visually-hidden-below-lg` (below 1024px), beside the existing `.visually-hidden-narrow`. They hide a row's secondary words visually while keeping them for screen readers. Moving this shared rule to the global layer, and reusing the global badge, brought `overview.scss` back under the 4 kB component budget (a first draft was 5.33 kB). **No budget was raised.** | `styles/_base.scss` |
| R1 | New messages: `overview.attention.more.{zero,one,two,few,many,other}` and `overview.attention.fewer`. Arabic forms: «+ بند آخر», «+ بندان آخران», «+{n} بنود أخرى», «+{n} بندًا آخر», «+{n} بند آخر» and «عرض أقل». | `locale/messages.json`, `locale/messages.ar.json` |
| R2 | New `scrollRegion` input on the transcript, bound to the conversation's `fill()`. While true, the host carries `tabindex="0"`, `role="region"` and `aria-labelledby` pointing at its own "Messages" heading (now with a unique id). The focus ring is the product's 2px ring, drawn inside (`outline-offset: calc(-1 * var(--focus-ring-width))`). When the page scrolls instead, nothing is added, so there is no extra tab stop. | `conversation-transcript.{ts,html,scss,spec.ts}`, `conversation-detail.html` |
| R4 | On a page that is not Arabic, the switch's «العربية» option uses the metric-matched local fallback (`'IBM Plex Sans Arabic Fallback', system-ui, sans-serif`, i.e. `local()` Segoe UI, Tahoma, Geeza Pro or Noto Sans Arabic). It downloads nothing. The rule is `:host-context(html:not([lang='ar']))`, so Arabic pages are unaffected. | `layout/entry-frame/entry-frame.scss` |
| §6 | The password was replaced by a pointer to `demo-accounts.md`. This report now contains no password value. | this report |

The R1 panel in detail:
- **Rows.** Each item is one row: a status badge, the title, the figure (e.g. "3 of 3"), the consequence sentence, the action's name, and a chevron. The badge is the product's own status badge (glyph and word), replacing the page-local icon chips. Items with a destination are a single link; "not checked" items are a "Try again" button.
- **Truncation.** The title and the sentence end in an ellipsis when long, so a row never wraps. The full text stays in the link's accessible name.
- **Narrow screens.**
  - Below 1024px the sentence is spoken, not shown.
  - Below 768px the badge word, the figure and the action's name are spoken, not shown. The badge's glyph stays visible, and so does "Try again".
- **States.**
  - Loading: three placeholder rows.
  - Nothing: "Nothing needs you right now." on the first row, with a success-badge glyph.
  - More than three items: "+N more" in the footer (`aria-expanded`, `aria-controls`). It reveals the rest, and "Show fewer" folds them again.
  - Only the reader's own "+N more" makes the panel taller.

Tests:
- 4 new tests in total.
- **Overview, 3 new:**
  - three rows, "+N more", expand and fold;
  - no "more" at three items or fewer;
  - the same panel with three placeholder rows while the sources answer.
- **Overview, rewritten:** "one way on per row", and the figure checks that replace the meter-bar checks.
- **Transcript, 1 new:** the region attributes appear only while it scrolls by itself, and the label names the "Messages" heading.

### R1.3 Results (all LIVE, through https://localhost:8443)

| Check | Result |
| --- | --- |
| `npm run format:check` | **PASS**, exit 0 |
| `npm run test:ci` | **PASS**: 93 files, **1,105** tests (4 new), 0 failures |
| `npm run build` | **PASS**: exit 0, **0 warnings** (`overview.scss` under 4 kB). Initial total 310.86 kB raw / 72.91 kB transfer. |
| `npm run i18n:check` | **PASS**: 2,035 messages, 0 errors, 0 warnings |
| Startup JS | **PASS**: 38 files, 417,830 raw / **137,623 gzip** / 121,766 brotli. Before revision: 137,638 / 121,845 (−15 B gzip). |
| Edge image | **PASS**: rebuilt from the unchanged `deploy/edge.Dockerfile`. Only the edge container was recreated (`live.sh recreate-edge`); api-a, api-b, postgres and redis kept their start times. The served `index.html`, `main-YEUBW3WP.js` and `styles-OUP563GT.css` match the local build by md5. |
| CSP hash check | **PASS**: the served CSP has 2 hashes, exactly the new `index.html`'s inline script and its stylesheet `onload` handler. The other headers are unchanged (`logs\r1-edge-headers.log`). |
| Matrix, changed routes (overview, conversation-human, conversation-ai, sign-in, create-account, accept-invitation, workspace-setup) × 1440/390 × light/dark × en/ar | **PASS**: 56 cells, 0 issues: no overflow, one `h1`, no unnamed controls, no clipped content, no small targets, no unexpected console errors, 0 CSP violations, 0 third-party requests (`matrix-r1\`, `logs\r1-matrix-*.log`). |
| axe-core, all 164 pages | **PASS**: **0 WCAG violations**, down from 4 pages. Best practice unchanged: `landmark-one-main` and `region` on the 4 embed-document pages (36.11 decision). See `axe-r1\axe.json`. |
| Keyboard: transcript, en and ar, 1440×900 | **PASS** in both. Details below the table. |
| Lab runs: sign-in and Overview, mobile (390, 4× CPU, slow 4G) and desktop | **PASS** for CLS. Details below the table. |
| "Never moves", measured directly | **PASS**. Details below the table. |
| Entry-page fonts, cache disabled, 1440 and 390 | **PASS**. Details below the table. |
| §6 and committed files | **PASS** for this phase's files: the report has no password value, and the frontend diff contains none. Pre-existing occurrences in older committed files are listed in R1.6. |

Keyboard detail (`keyboard-r1\transcript.json`):
- The region has `tabindex="0"` and `role="region"`, named "Messages" / «الرسائل».
- It is reached in 4 Tabs from the page heading, with `:focus-visible` and the 2px solid ring inside.
- End, Page Up, Arrow Down, Home and End all scroll it, and focus stays on it.
- 0 console errors.

Lab runs detail (`perf\vitals-r1.json`):

| Page | Profile | CLS, 5 cold runs | CLS, 3 warm runs | Median FCP | Median LCP |
| --- | --- | --- | --- | ---: | ---: |
| Overview | mobile | 0.0001 each (0.00 to two decimals) | 0, 0, 0 | 292 ms | 3.24 s |
| Overview | desktop | 0 each | 0, 0, 0 | 52 ms | 0.34 s |
| Sign-in | mobile | 0.0001 each | 0, 0, 0 | 1.26 s | 4.60 s |
| Sign-in | desktop | 0 each | 0, 0, 0 | 980 ms | 1.20 s |

- **Before the revision**, Overview CLS was 0.477 on mobile and 0.084 on desktop.
- **Every run is now under 0.05, and 0.00 to two decimals.** No layout shift of 0.005 or more was recorded.
- **Third-party requests: 0.** The script flagged one URL per Overview run, but it is the badge's inline `data:` SVG glyph from the global stylesheet, not a network request.
- **Caching:** warm runs come from cache, with 0 static files refetched.

"Never moves" detail (`overview-r1\states.json`):
- **Owner, loading versus loaded.** The Overview's three reads were held with DevTools Fetch interception, then released. Covered: Northwind (5 items) and Al Noor (5 items of its own: members 2/2, agents 1/1, links 1/1, WhatsApp 0/0, 5 failed AI replies), en and ar, 1440 and 390. Panel height is 190 px both while loading and once loaded in every case, and the "Right now" heading's position is identical. "+2 more" grows the panel to 286 px only on request, and "Show fewer" restores it.
- **Support account, the "nothing" state.** This account reads no Overview sources, so there is no loading phase to hold, and the script therefore marks those 8 cases "pass=false". All 8 measured the same 190 px panel, with "Nothing needs you right now." and an unchanged shortcuts position (`overview-r1\states-support.json`).
- **A few items (1–3).** No live account has this state. The spec covers it, and the height is fixed by CSS, so it is the same element at the same height.

Entry-page fonts detail (`perf\fonts-entry.json`):
- **English** sign-in, create-account and accept-invitation load only `instrument-sans-latin`: 57,604 B, where sign-in loaded 103,163 B before. No Arabic file; the switch uses the local fallback.
- **Arabic** pages load IBM Plex Sans Arabic 400/500/600, and `document.fonts` reports it loaded.

### R1.4 New screenshots (evidence folder `C:\Users\mouay\Desktop\omniai-redesign-evidence\p36-12\`)

| What | Path |
| --- | --- |
| Overview: loading, loaded and expanded, Northwind and Al Noor, en/ar, 1440/390 | `overview-r1\{northwind,alnoor}-{en,ar}-light-{1440,390}-{loading,loaded,expanded}.png` |
| Overview, nothing needs attention (support account) | `overview-r1\support-{northwind,alnoor}-{en,ar}-light-{1440,390}-loaded.png` |
| Overview, conversation detail and entry pages in the matrix | `matrix-r1\{en,ar}\{owner,public}-<lang>-<theme>-<width>-<route>.png` (56) |
| Transcript focused by keyboard, en and ar | `keyboard-r1\transcript-focused-{en,ar}-light-1440.png` |
| Entry pages, with the «العربية» label in the system font | `fonts-r1\{sign-in,create-account,accept-invitation}-{en,ar}-light-1440.png` |

### R1.5 Updated rubric rows

| Screen | Hierarchy | Clarity | Consistency | Accessibility | Responsive/RTL | Craft | Distinctiveness | Performance | Revision 1 | Before revision | 36.1 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | ---: | ---: | ---: |
| Overview | 4 | 5 | 5 | 5 | 5 | 4 | 4 | **4** (CLS 0.00; mobile cold LCP 3.24 s, lab) | **4.5** | 4.4 | 3.4 |
| Conversation detail | 5 | 4 | 5 | **5** (axe clean, keyboard-reachable region) | 5 | 4 | 5 | 3 | **4.5** | 4.4 | 2.8 |
| Sign-in | 5 | 5 | 5 | 5 | 5 | 4 | 4 | 4 (45 kB less font on English entry pages; LCP unchanged) | **4.6** | 4.6 | 3.3 |

The other seven rows are unchanged from §10. Every screen meets the ship bar again with no measured defect.

The §8 items are now settled as follows:
- **R1 (Overview layout shift):** closed.
- **R2 (transcript keyboard reachability):** closed.
- **R4 (font and LCP):** the font part is closed. Mobile cold LCP on slow 4G remains a lab observation, with no field data.
- **R3 (NuGet audit, ask 1) and the other items:** still open.

### R1.6 Open items from this revision

- **Older committed files still contain the earlier synthetic password** (the one used by the Phase 34 and 36.1 local stacks; its value is not repeated here):
  - `docs/phase-reports/PHASE_36_1_BASELINE_REPORT.md` §6 (line 192);
  - 11 Phase 34 tools under `tools/performance/`, as the `SEED_PASSWORD` default.

  The note asked for exactly these fixes and nothing else, so they were not changed. Removing them means editing a 36.1 report and Phase 34 tooling: say if you want that in the checkpoint.
- **The live drill password stays out of the repository**, in `<p36-12>\demo-accounts.md`, as intended.

### R1.7 Proposed checkpoint commit message (replaces §9; not committed)

```
feat: prove the redesign live, and keep the Overview still

Phase 36.12: Nabra served as production would serve it (nginx edge with
TLS, two Production APIs, migrator, PostgreSQL, Redis), from this worktree,
on loopback with a short-lived drill CA. Canary clean in both APIs before
any traffic; no provider traffic; seeded through the edge. 312 Chrome cells
clean (en/ar, light/dark, 1440/390), no CSP violation, no third-party
request; hosted chat and the widget on a second origin in Arabic and RTL;
SignalR through the edge; immutable caching proven.

Revision 1 fixes what the drill found:
- Overview: "Needs attention" is a compact list of three one-line rows
  with "+N more", in a panel of one height in every state, so nothing
  below it moves while it loads (CLS 0.48 -> 0.00 on phones, 0.08 -> 0.00
  on desktop, 5 cold + 3 warm runs). Rows use the product's status badge.
- Transcript: while it scrolls by itself it is a named, focusable region
  with the product's focus ring (axe: 0 violations on 164 pages).
- Entry pages: the Arabic language-switch label uses the system Arabic
  font on English pages, so they download no Arabic font file (-45 kB).
- Two global visually-hidden-below-md/lg utilities; component budgets kept.

The API and migrator images needed NuGet audit off (approved, outside the
repository): Pkcs 10.0.0 and MessagePack 2.5.302 must be patched before
any release.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```
