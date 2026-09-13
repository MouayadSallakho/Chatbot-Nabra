# Phase 18 — Embeddable website chat widget, origin security and branding

## 1. Objective and status

Put the Phase 15 conversation runtime onto other people's websites: a versioned
loader script, a cross-origin iframe served from the OmniAI origin, a
per-endpoint allowlist of exact origins, a header-borne conversation credential
that needs no third-party cookie, appearance a workspace can configure, and an
admin screen to install and control all of it.

**Status: complete and verified in Chrome.** Nothing is staged and nothing is
committed. No VPS, no deployment.

The design and its reasoning are recorded in
[docs/architecture/embeddable-web-chat-widget.md](../architecture/embeddable-web-chat-widget.md).

## 2. Baseline

HEAD was `b8135a2` — the Phase 17 checkpoint. The only tracked change carried in
from before this phase is the authorised Phase 17 micro-fix to
`ConversationContextPolicy`'s XML documentation (the value was and remains
16,000; the contradictory "twenty-four thousand" sentence was removed).

## 3. What was built

**Domain** — `WebChatOrigin` (canonical scheme+host+port, wildcards and page
addresses refused), `WebChatClientMode`, `WebChatLauncherPosition`,
`WebChatTheme`, `WebChatAccentColor`, `WebChatGreeting`, `WebChatWidgetSettings`,
`WebChatAllowedOrigin`. `WebChatEndpoint` gained `Widget`, `AllowedOrigins`,
`MaxAllowedOrigins = 20`, `Configure`, `Allows`, `RestoreAllowedOrigins`.
`WebChatSession` gained `Mode`, `EmbeddedOrigin`, `OpenEmbedded`, `Restore` and
`AdmitsMode`, with mode and origin enforced as one fact.

**Application** — `GetWebChatWidgetHandler`, `UpdateWebChatWidgetHandler`,
`EmbeddedWebChatHandler`. `PublicWebChatAccess` now takes the client mode and the
endpoint repository: it checks the transport *before* comparing the credential,
and re-reads the allowlist on every embedded read and send.

**Infrastructure** — converters and mapping for the new value objects, the
`web_chat_allowed_origins` table, and one migration.

**API** — `/api/public/embed/*` (config, start, list, send), `/widget/v1.js`,
`GET`/`PUT /api/tenants/{t}/website-chat/{id}/widget`, and the server-composed
install snippet.

**Loader** — `backend/src/OmniAI.Api/Widget/v1.js`, compiled in as an embedded
resource. No dependencies, no build step, no `eval`, no `document.write`, no
`innerHTML`.

**Frontend** — `features/embed/` (the widget itself: bridge, API client, session
store, accent contrast, component) and
`features/channels/website-chat/website-chat-widget/` (the admin INSTALL /
ALLOWED ORIGINS / APPEARANCE / SECURITY sections).

## 4. The security boundary, in one paragraph

The customer's page gets a rectangle and nothing else. The chat lives in an
iframe on the OmniAI origin, so it is same-origin with the API and **no CORS
grant was added anywhere**. The iframe learns which website it is on from
`postMessage` `event.origin` — the parent speaks first, because every other
source of that value is controlled by the embedding page. The server checks that
origin against the workspace's allowlist with exact equality. The credential is a
256-bit value returned once, kept in the iframe's own `sessionStorage`, sent in a
custom header, and never given to the parent, a URL, `localStorage` or a log.
Removing a website stops conversations already running on it, immediately.

## 5. Two transports, no shared authority

`AdmitsMode` is checked before `Admits`, so:

- an embedded credential presented as a hosted cookie authenticates nothing;
- a hosted cookie value presented in the embed header authenticates nothing;
- a conversation id on its own authenticates nothing on either.

Asserted at the domain, application and HTTP levels.

## 6. Origin allowlisting is not abuse protection

Stated in the code, the architecture document, the admin screen's SECURITY
section and the tests. An approved website still passes through every Phase 16
protection: the per-network start limit, the per-conversation send limit, the
workspace generation budget, the conversation lock and the idempotency key. Tests
drive a refusal at the start limit and at the send limit from an approved
website, and assert the generator was never reached.

## 7. Lazy conversations

Verified in Chrome against the real admin list, not only in tests:

| Event | Conversations |
| --- | --- |
| Customer page load, loader runs, iframe loads | 0 |
| Widget opened | 0 |
| First real Send | 1 |

## 8. Migration

Exactly one: `20260913114224_AddWebChatWidgetEmbedding`. No historical migration
was edited.

The scaffolder emitted `defaultValue: ""` for four new non-nullable columns —
`client_mode`, `widget_accent_color`, `widget_launcher_position`, `widget_theme`.
An empty string is not a valid value for any of them: three are enum names and one
is a hex colour, so every pre-existing row would have failed to read back, not at
deployment but the first time somebody opened an endpoint. The migration was
rewritten by hand: add nullable, backfill with the value the row actually
represents, then `SET NOT NULL`. A redundant index the scaffolder added over the
allowlist's foreign key was also removed.

**No physical `xmin` column exists anywhere.** A schema test now asserts this
across the whole `public` schema rather than table by table.

## 9. Test totals (actual)

Backend, run on Linux in Docker against PostgreSQL 17.11 + pgvector 0.8.6 and
Redis:

| Project | Passed | Failed |
| --- | --- | --- |
| OmniAI.Domain.Tests | 580 | 0 |
| OmniAI.Application.Tests | 547 | 0 |
| OmniAI.Infrastructure.IntegrationTests | 470 | 0 |
| OmniAI.Api.IntegrationTests | 621 | 0 |
| **Total** | **2,218** | **0** |

Frontend: **531 tests across 43 files, 0 failing** (`npm run test:ci`).

Both totals include the remediation in §12: **+16 backend** tests
(`LiveProviderOptInTests`, the provider opt-in table) and **+5 frontend** tests
(the iframe-hardening assertions in `widget-loader.spec.ts`). Neither addition
reaches the network.

New Phase 18 test files: six backend (`WebChatOriginTests`,
`WebChatWidgetTests`, `WebChatEmbeddedSessionTests`, `EmbeddedWebChatTests`,
`EmbeddedChatApiTests`, `WebChatWidgetApiTests`, `WebChatWidgetSchemaTests`) and
five frontend (`accent-color.spec`, `allowed-origin.spec`, `embed-session.spec`,
`embed-bridge.spec`, `embed-chat.spec`, `website-chat-widget.spec`,
`widget-loader.spec`).

`widget-loader.spec.ts` evaluates the *shipped* `backend/src/OmniAI.Api/Widget/v1.js`
in a DOM rather than a copy, so a loader that diverged from the one customers run
would fail the suite.

Three existing tests were updated because Phase 18 changed what they assert:
the migration count (8 → 9), the `web_chat_sessions` column list (two columns
added), and the `PublicWebChatAccess` construction in the shared harness.

## 10. Builds and audits

| Gate | Result |
| --- | --- |
| `dotnet restore` / `dotnet build` | Succeeded, **0 warnings** (`TreatWarningsAsErrors`, no `NoWarn`) |
| `dotnet list package --vulnerable --include-transitive` | No vulnerable packages in any of the 8 projects |
| `npm ci` / `npm run build` | Succeeded, **0 warnings** — including the per-component CSS budget |
| `npm run test:ci` | 531 passed |
| `npm run format:check` | Clean |
| `npm audit` | 0 vulnerabilities |
| Lint | **There is no lint script in this frontend.** The quality gates are the build, the tests and the format check. |

No package was added to either side in this phase.

Every gate above was re-run from clean after the §12 remediation, and the results
in the table are that re-run.

**The backend gates run on Docker's Linux engine, not on Windows directly.**
Windows Smart App Control blocks loading the freshly built `OmniAI.*.dll`
assemblies (`0x800711C7`), so `dotnet test` and `dotnet run` both fail on the host
before any project code executes. The same `.slnx`, the same sources and the same
PostgreSQL 17 + pgvector and Redis containers are used; only the process running
`dotnet` moves. The browser in §11 is a real Windows Chrome throughout.

## 11. Chrome verification

Done in a real Chrome against three origins: the API on `localhost:5027`, the
Angular app on `localhost:4200` (which also proxies `/widget`), and two customer
websites on `127.0.0.1:8081` (approved) and `127.0.0.1:8082` (never approved).
The customer pages were created **outside the repository**, in the session
scratchpad, and are not committed.

Verified:

1. **Install flow** — account, workspace, agent, chat link, widget settings, all
   through the UI. The snippet shown is the one the server composed.
2. **Approved origin** — launcher appears; panel opens; greeting renders as the
   workspace's own words, not an AI bubble.
3. **Unapproved origin** — the same snippet, verbatim: `404`, no launcher, and
   **not one postMessage sent to the page**.
4. **Cross-origin isolation** — the host page reports `iframe.contentDocument:
   null`, its own `localStorage` and `sessionStorage` empty, and no OmniAI cookie.
5. **postMessage inspection** — the only messages the host ever received were
   `{"type":"omniai:v1:ready","position":"Right"}` and `{"type":"omniai:v1:open"}`.
   No credential, no text, no identifiers.
6. **Lazy creation** — the workspace's conversation list stayed at 0 through page
   load, iframe load and opening the widget.
7. **Resume after reload** — the transcript came back from the iframe's own
   `sessionStorage`, with no cookie involved.
8. **Live revocation** — with the widget open and its credential unexpired,
   removing the website in the admin made the very next send return `404`, and the
   visitor's typed message was preserved.
9. **Third-party cookie independence** — the start response sets no cookie at all;
   a `curl` check confirms `no-store` and no `Set-Cookie`.
10. **No CORS grant** — an `OPTIONS` preflight carrying `Origin: https://evil.example`
    and `Access-Control-Request-Headers: x-omniai-embed-origin` is answered `405`
    with no `Access-Control-*` header of any kind.
11. **Loader caching** — strong content-derived ETag, `304` on revalidation,
    `public, max-age=3600`, `application/javascript; charset=utf-8`.
12. **Client-side origin validation** — wildcards, paths, plain http and bare
    hostnames each refused with their own sentence, before anything is sent.
13. **Responsive** — 390 (panel goes full-screen, square corners), 768, 1440 and
    1920 (floating card, rounded, host page visible around it), in light and dark.
14. **Admin 412, two tabs** — both read, one saved, the other's save was refused;
    the banner appeared, the second reader's own edits were left exactly as typed,
    and an explicit "Reload and discard my changes" replaced them only when clicked.
15. **Console** — no errors or warnings on any page.

### Re-verification after the remediation

A focused re-run in Chrome after the provider safeguard landed, on the same local
stack. Only the checks a remediation change could plausibly have affected were
repeated — the full matrix above was not redone, because nothing in the safeguard
touches origin policy, caching, responsiveness or the admin screens.

16. **The real iframe attributes, read out of the live DOM** on the approved
    website: `title="Chat"`, `sandbox="allow-scripts allow-same-origin"` (exactly
    two tokens — no forms, popups, top-navigation, downloads, modals or pointer
    lock), `allow=""` (no camera, microphone or geolocation), and
    `referrerpolicy="strict-origin"`. `iframe.contentDocument` is `null` and
    reading `contentWindow.location.href` throws `SecurityError`, so the
    cross-origin boundary still holds.
17. **No sandbox violation in the console.** The composer works inside a sandbox
    without `allow-forms` because Send is an ordinary button and Enter is a
    handled keystroke; Chrome logged no blocked-submission error.
18. **Approved origin still works, unapproved origin still refused** — the same
    snippet on the never-approved website gets `404` from the embed configuration
    route, renders no launcher, and sends **not one postMessage** to the host page.
19. **postMessage remains structural only** — `{"type":"omniai:v1:ready","position":"Right"}`
    and `{"type":"omniai:v1:open"}`, nothing else.
20. **No secret reached the browser**, and the host page's `localStorage` and
    `sessionStorage` were both empty with no OmniAI cookie set.
21. **Zero outbound OpenAI traffic with `OPENAI_API_KEY` present** — see §12.

### Two real defects the browser found

**The closed widget painted an opaque box on the customer's website.** Dropping
the document background was not enough: a document that declares
`color-scheme: dark` gets a dark canvas painted by the browser wherever its own
background is transparent, so the launcher sat in a black rectangle over the
host's design. Fixed by also setting `color-scheme: normal` on the embed
document, inline, because the token layer sets that property under a more
specific selector. The cost — native scrollbars inside the widget stop following
the visitor's dark mode — is recorded in §13.

**Saving the widget settings tore the section down underneath the reader.** The
save re-read the endpoint, the page flipped to its loading skeleton, and the
whole widget section was destroyed and re-created — taking its "Saved."
confirmation with it, and any part-typed allowlist along with it. Fixed: a
refresh of something already on screen is not a load. A regression test asserts
the section's DOM node survives a publish.

## 12. Live OpenAI traffic — disclosed, and remediated

### What happened

**One live OpenAI generation call was made, and it should not have been.** The
brief said no `OPENAI_API_KEY` and no live provider traffic. I did not configure a
key, but `OPENAI_API_KEY` is already set in this machine's environment, and the
API I started with `dotnet run` inherited it. The first message sent through the
widget during Chrome verification therefore reached OpenAI and came back with a
real answer.

As soon as I saw the reply I stopped the API and restarted it with that variable
explicitly removed (`env -u OPENAI_API_KEY`); every subsequent check ran with no
provider configured. No key was written to any file, any configuration, or any
commit. The whole automated suite uses a recording double and reaches no network.

**Root cause: an inherited environment variable, treated as a decision.** The
composition asked "is a key present" when the question that mattered was "did
anybody decide to spend money here". On a developer machine those are different
questions, and the code could not tell them apart. The original mitigation —
"remember to strip the variable next time" — was not a safeguard: the next session
starts from the same shell, and the failure mode is silent success.

### What was changed

Development now requires a deliberate opt-in **alongside** the key. `AddOmniAiRuntime`
takes `requireLiveProviderOptIn`, and the composition root passes
`environment.IsDevelopment()` and nothing else. When the guard is on, a configured
key composes nothing unless `OMNIAI_LIVE_OPENAI` is exactly `1`:

| Environment | `OPENAI_API_KEY` | `OMNIAI_LIVE_OPENAI` | Composed |
| --- | --- | --- | --- |
| Development | absent | any | unconfigured, both providers |
| Development | present | absent | **unconfigured, both providers** |
| Development | present | `0`, `true`, `yes`, `01`, `1 `, empty | **unconfigured, both providers** |
| Development | present | `1` | live adapters |
| Production | present | *not consulted* | live adapters |
| Production | absent | *not consulted* | unconfigured, both providers |

- **The match is exact and ordinal.** A guard that also accepted `true` or any
  non-empty value would be a guard somebody defeats by accident.
- **It covers `IAiTextGenerator` and `ITextEmbeddingGenerator`.** Both are composed
  from the same key in the same place. Guarding only the visible one would leave
  knowledge indexing — the silent one — still billing.
- **Production semantics are unchanged.** The flag is not consulted outside
  Development. This is a cost guard for developer machines, not a security control
  for deployments, and a live deployment must not be takeable offline by somebody
  forgetting a second flag.
- **Behaviour when the guard holds is the pre-existing refusal**, not a new path:
  `503` from the text generator, `503` from knowledge indexing, no outbound
  request, nothing spent.

### How it is proven

`LiveProviderOptInTests` — 16 tests, no network — asserts every row of that table
for both providers, including that the opt-in alone composes nothing and that
Production ignores it entirely. The opt-in is read through `IConfiguration`, so
the tests never mutate the process environment.

### Re-verification in Chrome, after remediation

Re-run in a real Chrome against the same stack, deliberately with
**`OPENAI_API_KEY` present in the API process** (confirmed present without its
value ever being printed) and **`OMNIAI_LIVE_OPENAI` absent**:

- A real customer message was sent through the embedded widget on the approved
  website. It was accepted and persisted; the panel showed the honest refusal
  ("Sorry — no answer came back just now. Your message was received…"), and the
  message `POST` returned `503`. **No AI answer was generated.**
- The embedding path was exercised too, so that it was proven rather than merely
  untriggered: a knowledge document was created and **Index knowledge** pressed.
  It refused — "This needs an AI provider, and none is configured on this
  deployment. Nothing was sent and nothing was changed." — and
  `knowledge_chunks` remained at **0 rows**.
- **Zero outbound requests to OpenAI.** Chrome's network log contains only
  `localhost` and `127.0.0.1`; the API log contains **no OpenAI logger activity at
  all** (the live adapter logs a categorised warning on every failure path, and
  there is none), and the `503` text is the one the unconfigured generator throws.
  The container demonstrably had an egress path, so the absence is the guard
  working, not a network that could not have carried the call.
- **No secret anywhere.** The loader response, the embed configuration response,
  the host page DOM and the API log were scanned for API-key-shaped strings: none.

**The presence of `OPENAI_API_KEY` alone no longer activates live traffic.**

## 13. Known issues and deferred work

- **Native scrollbars and form controls inside the widget do not follow dark
  mode**, because the embed document declares `color-scheme: normal` so its canvas
  can be transparent. Every surface the widget itself paints is token-driven and
  correct in both themes.
- **The embed document loads the whole admin application bundle** and runs its
  auth bootstrap (`/api/auth/csrf`, `/api/auth/me` → 401) inside the iframe. It is
  harmless and lazily code-split, but a widget that shipped only its own chunk
  would start faster on a customer's page. Deferred deliberately: splitting the
  Angular application into two entry points is its own piece of work.
- **The first send is two requests** (start, then message). That is the price of
  lazy conversation creation, and it is recorded rather than designed around.
- **The allowlist is bounded at 20 websites per endpoint.** A customer with more
  subdomains than that will need a different mechanism, and a wildcard is not it.
- No `v2` loader exists and none is needed yet; `v1` is a compatibility promise.
- **`OMNIAI_LIVE_OPENAI` is a Development cost guard, not a spend limit.** Once a
  developer opts in, nothing caps what the opted-in session spends. A budget or a
  request ceiling is a separate piece of work and is not in this phase.

## 14. Security and privacy

- No third-party cookie is used, set or relied upon.
- No `Access-Control-Allow-Origin` was added to the API, and none is emitted.
- The raw credential is never persisted: PostgreSQL stores a SHA-256 digest and
  comparison is constant-time. A test asserts the stored digest does not contain
  the issued value.
- The embed routes carry no antiforgery filter and need none: the credential is
  not ambient, and every route requires a custom header, which forces a preflight
  this API refuses.
- A tenant may choose a greeting, a colour, a corner and a theme. A tenant may not
  choose CSS, HTML, JavaScript, a font or an image — nothing a settings page sets
  can execute on a visitor's machine.
- Every failure on the embed routes is one indistinguishable `404`, so a public
  key cannot be used to enumerate a workspace's approved websites.
- The widget renders every message through ordinary interpolation. A customer who
  types markup sees their markup; verified in the browser with `<b>` tags.
- The session table gained two columns and no new personal data: a transport name,
  and the customer's own website address.

## 15. Git status

Nothing staged, nothing committed, no branch created, no remote touched.

- 28 tracked files modified
- 47 new files
- 1 new migration

The remediation in §12 added one backend test file
(`LiveProviderOptInTests.cs`) and touched three sources — `OpenAiOptions.cs`,
`InfrastructureServiceCollectionExtensions.cs` and `CompositionRoot.cs` — plus
`widget-loader.spec.ts` and these two documents. No key, token, connection string,
raw client IP, cookie, screenshot, database content or build artifact is in the
diff; the customer test websites were served from the session scratchpad, outside
the repository, and are not part of it.

## 16. VPS and deployment

None. No VPS was accessed, no deployment was performed, and no deployment
configuration was changed.

---

**PHASE 18 REMEDIATION COMPLETE — WAITING FOR REVIEW**
