# Phase 36.1: Baseline (live local stack, demo data, "before" gallery, scored audit)

Date: 2026-10-18. Baseline for the "Two Voices" redesign (Phase 36). No product code changed in this phase; this document and the evidence folder are the only outputs.

Evidence folder (outside the repository): `C:\Users\mouay\Desktop\omniai-redesign-evidence\p36-01\`.

## 1. Summary

- A fresh local OmniAI (database `omniai_p36`, API in Docker, Angular dev server) runs with rich synthetic data: 3 members, 2 workspaces (one Arabic), 5 AI agents, 16 knowledge documents, 46 website-chat conversations, human replies in English and Arabic, reports, schedules and notifications.
- A 168-capture "before" gallery covers every route family at 1440 px and 390 px in light and dark, the public pages, the embedded widget open and closed, and the analyst's permission-limited view.
- The scored audit sets the numbers the redesign must beat: rubric averages between 2.6 (Conversations) and 3.4 (Overview, Knowledge); no screen passes the admin ship bar today.
- The biggest "before" problems for people using OmniAI: conversations show no customer identity, Arabic customer content is laid out left-to-right, failed AI replies are invisible in transcripts, and the interface is interchangeable with any indigo admin template.
- Nothing left the machine: no provider secret or live switch exists inside the API container (proven by name), and every request from the application goes to localhost.

## 2. Starting point

- Branch `master`, HEAD `d2c907d` ("feat: finalize production readiness for v1"), worktree clean, no Phase 36 commits.
- Docker held stopped containers from earlier phases (`omniai_pg25`, `omniai_redis18`, `omniai_p335_*`, `omniai_p34_*`, others). Nothing was deleted.
- `frontend/node_modules` was absent; `npm ci` restored it from the lockfile (no package added or upgraded).

## 3. Changes by area

`git status --short` at the end:

```
?? docs/phase-reports/PHASE_36_1_BASELINE_REPORT.md
```

Only this report was added to the repository. Everything else lives in the evidence folder.

### 3.1 Local stack (`stack.md`, `p36-stack.sh`)

| Piece | What |
| --- | --- |
| PostgreSQL 17 + pgvector | Reused container `omniai_pg25` on network `omniai_p18`. New role and database `omniai_p36`, created empty, then `20260925225105_Initial` and `20260926215858_ProductionReadinessHardening` applied with `dotnet-ef` (49 public tables, 2 history rows). No older database was read, migrated or dropped. |
| Redis 7 | Reused container `omniai_redis18`. |
| API | New container `omniai-p36-api` (`mcr.microsoft.com/dotnet/aspnet:10.0`), Development environment, published on `127.0.0.1:5027`, running a Release publish stored in volume `omniai-p36-app`; report files in volume `omniai-p36-reports`. |
| Dev server | `npm start` in `frontend/` on `http://localhost:4200`; `proxy.conf.json` unchanged. |
| Secrets | A master key, a network-key secret and the database password were generated once into `stack.secrets.env` in the evidence folder. Never printed, never copied into the repository. |

No Stripe stub, Meta stub or mail sink was started. The API receives none of `OPENAI_API_KEY`, `STRIPE_*`, `OMNIAI_WHATSAPP_*`, `OMNIAI_LIVE_*` or `Email__Smtp__*`, so billing, WhatsApp, e-mail, AI generation and knowledge indexing refuse honestly.

Build note: a clean restore of the backend now fails under NuGet's vulnerability audit (transitive `System.Security.Cryptography.Pkcs` 10.0.0, GHSA-846f-f764-76mx high and GHSA-2fmv-rv2c-59x7 moderate) because the solution treats warnings as errors. The script passes `-p:NuGetAudit=false` on the local command line only; the repository was not touched. See §7.

### 3.2 AI replies without live traffic

`OpenAiClientFactory` builds the `ResponsesClient` with only a credential, retry and timeout options; there is no endpoint or base-address setting anywhere in the adapter or its options. A local deterministic stub is therefore impossible without a backend change, which this phase may not make. AI turns fail honestly in the demo: the hosted chat shows "Sorry — no answer came back just now", the overview shows "103 AI replies failed", and the usage ledger holds no provider calls. The Design Lab in 36.2 will carry labelled sample transcripts.

### 3.3 Synthetic demo data (`demo-accounts.md`, `scratch/seed-p36.mjs`)

All through the real API, reusing the loopback-guarded client from `tools/performance/lib/`. Public-chat limits were raised for seeding only through the existing `PublicChatProtection__*__Limit` settings, then the API was recreated with defaults.

| Data | Northwind Operations (`p36-northwind`) | شركة النور للتجارة الإلكترونية (`p36-alnoor`) |
| --- | --- | --- |
| Members | owner, support agent (custom role), analyst (read-only insights role) | owner, support agent |
| AI agents | Northwind Concierge (Active), Returns & Refunds Specialist (Draft), Legacy Holiday Assistant 2025 (Disabled), a 90-character Active name | مساعد النور (Active) |
| Knowledge | 14: English, Arabic, Spanish, French; one 190-character title, one long Arabic title, `<img onerror>` and `<script>` titles, two failed index attempts, one disabled | 2 Arabic documents |
| Conversations | 42 (103 customer messages); 9 taken over with human replies in English and Arabic, 2 assigned to support, 2 returned to AI | 4 |
| Customers | 42; 12 named: Arabic, accented Latin, a 110-character name, `<b>Bold Customer</b>` | 3 named |
| Other | WhatsApp connection without credential; 5 completed reports (CSV, XLSX, PDF); 2 schedules; 7 notifications (5 ReportCompleted, 2 KnowledgeIndexingFailed); widget allowlist with two loopback origins | — |

### 3.4 "Before" gallery (`before\index.html`)

168 captures by a dependency-free DevTools-protocol script (`scratch/cdp-gallery.mjs`, headless Chrome, Node 24 WebSocket): 33 owner routes × 2 widths × 2 themes, 6 public routes × 2 × 2, the widget closed and open × 2 × 2, and 4 analyst captures. Each capture records page overflow, console errors, `h1` count and unnamed controls.

### 3.5 Audit outputs (`audit\`)

`token_lint.md/json`, `contrast_light.md`, `contrast_dark.md` (44 pairs each), `motion_audit.md`, `perf_scan.md`, `hero_check.md`, `test_inventory.md`.

## 4. Decisions

- **Reuse Postgres and Redis containers, add only `omniai-p36-*` things.** Rejected: a new Postgres container (needless duplicate of a 600 MB image) and reusing `omniai_p335` data (the brief asks for a new database initialised only by the repository migrations).
- **Run the API from a published volume on the `aspnet` image, not `dotnet run` inside the SDK image.** Restarts take seconds and later phases never rebuild. Rejected: bind-mounting and running from source as in 33.5, which was slower and compiled into the Windows tree on every start.
- **No stubs.** Rule 7 forbids `STRIPE_*`, `OMNIAI_WHATSAPP_*` and `Email__Smtp__*` inside the container, so the Stripe, Meta and mail stubs of 33.5 were not started. Billing, WhatsApp and e-mail therefore show their honest "not configured" states, which is the real first-run experience.
- **No AI stub** (§3.2). Deferred to the Design Lab's labelled samples.
- **`-p:NuGetAudit=false` on the local command line** instead of asking to edit `backend/`. The repository is untouched and the ask is recorded (§7). Alternative rejected: stopping the phase for a backend dependency bump.
- **Raised public-chat limits only while seeding**, then defaults restored, so the gallery shows real protection behaviour.
- **Gallery settling** waits for a rendered `h1`, no skeletons and 600 ms of stable DOM; the first pass captured skeletons on cold lazy chunks and was discarded.

## 5. Verification

| Check | Result |
| --- | --- |
| `npm run format:check` | PASS ("All matched files use Prettier code style!") |
| `npm run test:ci` | PASS: 63 files, 784 tests, 0 failures (Vitest 4.1.11, 21.8 s) |
| `npm run build` | PASS: initial 439.46 kB raw / 108.18 kB transfer (budget 500 kB warn); one known warning, `embed-chat.scss` 4.07 kB exceeds the 4 kB style budget by 72 bytes; 0 errors |
| Chrome matrix (gallery) | PASS on the automated checks: 168 captures, 33 admin + 6 public + widget routes × 1440/390 × light/dark, English only (no Arabic UI exists before 36.3). Page overflow 0 everywhere; exactly one `h1` everywhere; 0 unnamed controls; the only console error is the session probe `GET /api/auth/me` → 401 on signed-out pages (24 captures), see finding C-6 |
| Chrome interactive (DevTools MCP) | PASS for: sign-in keyboard path (email → password → Show password → Sign in, visible 1.6 px accent focus ring on each); wrong-password error announced; admin focus continuity (first Tab after route change lands in `main`); hosted chat send and honest failure message; no third-party request (61 requests on the hosted chat page, all `localhost:4200`) |
| Contrast (`contrast_check.py`, 44 pairs) | Light: 38 pass, 5 fail (all non-text borders: default, strong and subtle borders on surfaces, success and danger borders on their badges; control border passes at 3.43:1). Dark: 35 pass, 8 fail: the same 5 borders plus **muted text on muted (4.37:1), hover (4.12:1) and selected (4.36:1) surfaces**, which are text failures. All primary, secondary, accent, status and button-label pairs pass AA in both themes |
| Keyboard | PASS on the paths above; full keyboard matrix of every screen NOT RUN (Phase 36.2 onwards does it per changed screen) |
| Reduced motion | PASS: with `prefers-reduced-motion: reduce` emulated, `--motion-*` collapse to 0.01 ms and every transition on the overview is 1e-5 s |
| RTL mirroring | NOT RUN as a UI language: the product has no Arabic locale yet (36.3). Arabic **content** was checked: see findings R-1 to R-3 |
| Provider canary | PASS, twice (seeding API and final API): `OPENAI_API_KEY`, `STRIPE_SECRET_KEY`, `STRIPE_WEBHOOK_SECRET`, `OMNIAI_WHATSAPP_APP_SECRET`, `OMNIAI_WHATSAPP_VERIFY_TOKEN`, `OMNIAI_LIVE_OPENAI`, `OMNIAI_LIVE_META`, `OMNIAI_LIVE_EMAIL` and every `Email__Smtp__*` absent (names only, never values) |
| `token_lint.py --root frontend` | 15 warnings, 2 info (see §6.2) |
| `motion_audit.py --root frontend` | 0 high, 2 medium, 2 low (see §6.4) |
| `perf_scan.py --root frontend` | 0 critical/high/medium, 1 low (no web-vitals RUM), 2 info; built output 88 files, about 302 kB gzip JS across all routes |
| `hero_check.py --root frontend` | 0 findings |
| `test_inventory.py --root frontend` | 54 TestBed files (~667 tests) + 9 unit files (~93); no flags |
| Lighthouse / performance trace | NOT RUN (dev server measurements are not representative; the production build sizes above are the baseline) |

## 6. Findings

### 6.1 Rubric scores (ui-ux-design-lead critique rubric, 1–5; the numbers to beat)

Evidence: gallery captures at 1440 and 390 px, light and dark, plus the interactive checks in §5. Context: admin screens use the ERP/admin bar (nothing below 3, accessibility and responsive/RTL ≥ 4, clarity and consistency ≥ 4). Sign-in, hosted chat and the widget use the general bar.

| Screen | Hierarchy | Clarity | Consistency | Accessibility | Responsive/RTL | Craft | Distinctiveness | Performance | Average | Verdict |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Overview | 3 | 4 | 4 | 4 | 3 | 3 | 2 | 4 | 3.4 | FAILS (distinctiveness 2, responsive/RTL 3) |
| Conversations list | 2 | 2 | 3 | 3 | 2 | 3 | 2 | 4 | 2.6 | FAILS (clarity 2, responsive/RTL 2) |
| Conversation detail | 2 | 3 | 3 | 3 | 2 | 3 | 2 | 4 | 2.8 | FAILS (hierarchy 2, responsive/RTL 2) |
| AI agent editor | 3 | 4 | 4 | 3 | 3 | 3 | 2 | 4 | 3.3 | FAILS (accessibility 3, distinctiveness 2) |
| Knowledge list | 3 | 4 | 4 | 4 | 3 | 3 | 2 | 4 | 3.4 | FAILS (responsive/RTL 3, distinctiveness 2) |
| Channels | 3 | 2 | 4 | 4 | 3 | 3 | 2 | 4 | 3.1 | FAILS (clarity 2) |
| Analytics | 3 | 3 | 4 | 3 | 3 | 3 | 2 | 4 | 3.1 | FAILS (clarity 3, accessibility 3) |
| Sign-in | 4 | 4 | 4 | 3 | 3 | 3 | 1 | 4 | 3.3 | FAILS (distinctiveness 1, accessibility 3) |
| Hosted chat | 3 | 3 | 4 | 3 | 2 | 3 | 1 | 4 | 2.9 | FAILS (distinctiveness 1, responsive/RTL 2) |
| Widget (open and closed) | 3 | 3 | 3 | 3 | 3 | 3 | 2 | 4 | 3.0 | FAILS (accessibility 3, distinctiveness 2) |

Responsive/RTL never scores above 3 because the product has no Arabic interface yet; 2 where Arabic content is laid out wrongly today.

### 6.2 AI-default tells present (dated 2026-10)

| Tell | Where | Evidence |
| --- | --- | --- |
| **Indigo accent as the brand** | every screen: `--accent: #4a56e2` light, `#7b87ff` dark; primary buttons, nav selection, links, "Human" pill, chart bars | `_tokens.scss`, all gallery captures |
| **Default type stack by habit** | `--font-sans: system-ui, -apple-system, 'Segoe UI', roboto, …`; no Arabic family, no recorded reason | `_tokens.scss:208`; `index.html:71` repeats it as a literal |
| **Tracked uppercase eyebrows and labels** | `COLLEAGUE` / `CUSTOMER` on every conversation row, `PERIOD` on Analytics, uppercase table headers, sidebar group labels (`WORK`, `AI`, `CHANNELS`…) | `text-transform: uppercase` in 11 files |
| **Same-size stat tiles in rows** | Overview (3 + 4 tiles), Analytics (4 tiles) | overview and analytics captures |
| **Card soup** | shortcut cards, channel cards, every form section as a card inside a card page | overview, channels, agent editor |
| **Coloured start-edge stripe as the row differentiator** | human-handled conversation rows, knowledge editor, toasts | `border-inline-start: 3px` in 4 files |
| **Centered auth card with logo** | Sign-in, Create account | sign-in captures |
| **Second blue in the widget** | `--embed-accent: #2563eb` (loader, widget header, Send) differs from the app accent | `token_lint` color literals ×3 |

Absent, and to keep absent: gradients (the only two are functional: select chevron and skeleton sweep), glass, sparkle icons, count-ups, splash screens, fade-up-on-scroll, cream/serif/terracotta, emoji icons.

### 6.3 Clarity and hierarchy (by area)

- **C-1 Conversations list shows no customer.** Rows carry a sender eyebrow, a preview, channel, agent, handling and assignee e-mail, but never the customer's name, even for the 12 named customers. The primary fact of an inbox is missing (list capture, 1440 and 390).
- **C-2 Conversation detail has no identity either.** `h1` is "Conversation"; the customer is nowhere; members are shown by e-mail. On 390 px the facts card and the handling card push the transcript below the fold (detail-human-390).
- **C-3 Failed AI replies are invisible in the transcript.** 103 turns failed, the overview says so, but a transcript shows only the customer messages with no "no answer" marker, so a person reading it cannot tell the customer was left waiting.
- **C-4 Channels says "Set up" for configured channels.** Website chat has an active endpoint and WhatsApp has a connection, yet both cards show the same "Set up" button as an unconfigured channel.
- **C-5 Analytics "Daily activity" chart** has no axis labels, a single bar at the edge for the busiest day, and no series for failed AI replies, the one thing the overview flags.
- **C-6 Signed-out pages log a 401.** Every public page, including the customer-facing hosted chat and the embed document, probes `GET /api/auth/me` and prints a console error. Harmless but noisy, and a wasted request on a customer page.
- **C-7 Sign-in failure announces twice.** A wrong password raises an assertive alert ("The email address or password is incorrect.") and simultaneously clears the password field and marks it invalid with a second assertive alert ("Enter your password."), which reads as a different error.
- **C-8 Hosted chat keeps the sent text in the box after a failed AI turn** and invites "try sending it again", although the customer message was stored. A retry would send it twice. Needs a design decision (retry the answer, not the message).
- **C-9 The notification bell's live region** repeats "7 unread notifications" on every page as a polite announcement.

### 6.4 Accessibility, motion and targets

- **A-1 Dark muted text fails AA** on muted (4.37:1), hover (4.12:1) and selected (4.36:1) surfaces (`--text-muted` #7b8698). Light passes everywhere, the lowest being 4.54:1.
- **A-2 Decorative borders are below 3:1** in both themes (default 1.25:1, strong 1.58:1, subtle 1.17:1 on white). Acceptable only where the border is not the sole boundary of a control; the control border passes (3.43:1 light, 3.12:1 dark). The redesign must decide which borders carry meaning.
- **A-3 Small-target leads** (heuristic, 24 px floor): agent editor 14, role editor 29, notification preferences 5, analytics 3, reports 3. These are native checkboxes and small icon buttons; confirm the label hit area per control in 36.2+.
- **A-4 Motion**: sidebar and plan-usage transition `inline-size` (layout property, 2 medium); two infinite loops (spinner, skeleton sweep) need a reduced variant (2 low). Reduced motion itself is honoured globally.
- **A-5 Conversation row links** have very long accessible names (the whole row text), which makes screen-reader navigation slow.

### 6.5 Arabic and RTL content (before any Arabic UI exists)

- **R-1 Arabic previews and transcript messages render LTR.** In the conversation list and the transcript the Arabic text sits in a `direction: ltr` element with no `dir="auto"` or `<bdi>`; punctuation lands on the wrong side. The Knowledge list, by contrast, isolates Arabic titles correctly.
- **R-2 Arabic body text uses the Latin line-height** (14 px / 1.55) and the system Latin stack; Arabic wants 1.7 and a chosen family.
- **R-3 The hosted chat for an Arabic agent** ("مساعد النور") shows an Arabic title inside an entirely English, LTR page with English helper copy, and the select chevron in `_primitives.scss` is positioned with physical `right`, which will not mirror.

### 6.6 Tokens and consistency

- 15 hard-coded values outside the token file: 8 raw spacings (2–3 px tweaks), 5 colour literals (3 × `#2563eb` widget blue, `#111827` / `#ffffff` in `accent-color.ts`, `#646e7f` in `index.html`), one font-family literal in `index.html`, one numeric `z-index: 100` in `_base.scss` beside a `--z-*` scale.
- The `embed-chat.scss` budget warning (72 bytes over 4 kB) is the known style-budget warning; shared rules should move to the global layers, not the budget.

### 6.7 Performance baseline

- Production build: initial 439.46 kB raw / 108.18 kB transfer; largest initial chunk 178.69 kB raw; about 302 kB gzip JS across all 88 output files. Fonts: none shipped (system stack), so any self-hosted family added by the redesign is new weight to budget.
- Dev-server hosted chat page: 61 requests, all same-origin.

## 7. Backend asks

- **NuGet audit failure on a clean restore**: `System.Security.Cryptography.Pkcs` 10.0.0 (transitive) is flagged by GHSA-846f-f764-76mx and GHSA-2fmv-rv2c-59x7 and `TreatWarningsAsErrors` turns it into a restore error. The backend needs a dependency bump or an explicit, reviewed audit decision; the local stack disables the audit on the command line only.
- **Customer identity on conversation list items**: the conversation summary carries no customer name or id, so the redesign cannot show who a conversation is with without a second request per row. The detail endpoint likewise exposes no customer link.
- **Failed-turn markers in transcripts**: the transcript endpoint returns stored messages only; a failed AI turn leaves no item, so the UI cannot show "no answer was sent" at the right place in the thread.
- **Member display names**: assignees and human senders are exposed by e-mail only.
- **Channel status on the channel catalogue**: the Channels overview has no per-channel configured/active fact; it needs the website-chat and WhatsApp lists to compute it.
- **Local AI endpoint for demos**: the OpenAI adapter has no endpoint override, so deterministic local AI replies are impossible without a (guarded) configuration option.

## 8. Risks and open issues

- The Arabic locale does not exist yet; RTL findings above are about user content only. The full RTL matrix starts in 36.3.
- The small-target counts are heuristic (element box only, label hit areas not included) and must be confirmed per control.
- Public-chat defaults are in force, so more than 5 conversation starts from one browser within 5 minutes return 429 by design; the seed used raised limits that are no longer set.
- Reports in `omniai-p36-reports` and the Data Protection ring depend on `stack.secrets.env`; losing that file means re-seeding.
- The first gallery pass captured skeletons on cold lazy routes; the settle rule now waits for a rendered `h1`, no skeletons and a stable DOM. Later phases must keep that rule or captures will lie.

## 9. See it live

- Admin: http://localhost:4200/sign-in → `p36.owner@omniai.example` / `Synthetic-Local-Only-2026!` (support and analyst accounts in `demo-accounts.md`).
- Hosted chat: http://localhost:4200/chat/9veAaKzu0Ee-isA7jrQzYw (Arabic agent: http://localhost:4200/chat/o-M5OubU2TJkyTCA-CRvYg).
- Widget host page: http://127.0.0.1:8099/?key=9veAaKzu0Ee-isA7jrQzYw (serve `widget-host/` as in `stack.md`).
- Gallery: `C:\Users\mouay\Desktop\omniai-redesign-evidence\p36-01\before\index.html`.
- Accounts: `C:\Users\mouay\Desktop\omniai-redesign-evidence\p36-01\demo-accounts.md`. Stack commands: `…\p36-01\stack.md`.

## 10. Proposed checkpoint commit message

```
docs: record the Phase 36.1 redesign baseline

Adds the baseline report for the "Two Voices" redesign: local p36 stack,
synthetic demo data, the 168-capture "before" gallery and the scored audit
(rubric, tokens, contrast, motion, performance, first impression) that every
later Phase 36 phase is measured against. No product code changes.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
```
