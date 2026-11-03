# Embeddable website chat widget

Phase 18 put the Phase 15 conversation runtime onto other people's websites. A
customer pastes one `<script>` tag; a launcher appears in a corner; a visitor
talks to the same agent, through the same runtime, under the same Phase 16
protections.

Nothing about the runtime changed. What was added is a second **transport** —
a cross-origin iframe with a header credential — and the security boundary that
decides which websites may open it.

---

## 1. The shape of it

```
customer's page (https://shop.example.com)
└── <script async src="https://omniai.example/widget/v1.js"
            data-nabra-key="KEY"></script>
        │  creates one iframe, positions it, relays two messages
        ▼
    <iframe src="https://omniai.example/embed/KEY"
            sandbox="allow-scripts allow-same-origin"
            referrerpolicy="strict-origin">
        │  the whole chat: launcher, panel, transcript, composer
        │  same-origin with the API, so it calls it directly
        ▼
    POST /api/public/embed/... with
        X-OmniAI-Embed-Origin:  https://shop.example.com
        X-OmniAI-Embed-Session: <credential>
```

Three things follow from this picture, and the rest of the document is mostly
their consequences:

- The customer's page holds **no conversation state and no secret**. It holds a
  rectangle.
- The iframe is on the OmniAI origin, so its calls to the API are **same-origin**.
  No CORS grant was added, and none is needed.
- The only thing the two documents exchange is **how big the rectangle should
  be**.

---

## 2. What the loader is allowed to be

`backend/src/OmniAI.Api/Widget/v1.js`, served at `/widget/v1.js`.

It runs on somebody else's website, so it is small, dependency-free, ES5-syntax,
and does exactly four things: create the iframe, position it, relay `open`/`close`,
and re-measure on resize.

**It never uses `eval`, `document.write`, `innerHTML`, or `Function`.** A frontend
test evaluates the shipped file and asserts each of those strings is absent from
the code.

**It never runs anything a workspace configured.** A tenant picks a colour and a
corner. A tenant does not pick code that executes on another company's customers'
machines — which is also why there is no custom CSS, no custom HTML, no font URL,
no logo upload and no callback hook anywhere in Phase 18.

**It is versioned in its path, and that is a compatibility promise.** A customer
pastes the snippet once and may never touch it again. `/widget/v1.js` must keep
working for them forever; a breaking change becomes `/widget/v2.js` and leaves v1
alone.

**It is served from an embedded resource, not a static-file directory.** There is
no static-file middleware in this API and this phase did not add one: serving a
directory means serving whatever ends up in it. The response carries a
content-derived strong ETag and `public, max-age=3600`.

### The iframe attributes

| Attribute        | Value                             | Why                                                                                                                                                                                                      |
| ---------------- | --------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `sandbox`        | `allow-scripts allow-same-origin` | Scripts, so the widget runs. Same-origin, so it stays on the OmniAI origin and can use its own API and storage. It grants the iframe nothing over the host page — they are different origins regardless. |
| `allow`          | _(empty)_                         | Camera, microphone and geolocation are denied by default cross-origin; saying so explicitly means a future default cannot quietly grant one.                                                             |
| `referrerpolicy` | `strict-origin`                   | The host page's path can carry an order number, a search term or a customer identifier. None of that is OmniAI's business.                                                                               |
| `title`          | `Chat`, or «الدردشة» on an Arabic page | A frame announced as "iframe" is a frame a screen-reader user has no reason to enter. From Phase 37.1 it follows the host page's `<html lang>` (any `ar` tag → Arabic, anything else → English), falling back to the browser's language when the page declares none. |

Everything else is withheld: no forms, no popups, no top-level navigation, no
downloads, no pointer lock. A widget that could navigate the host page away would
be a widget usable to phish the site's own visitors.

**`allow-forms` is withheld on purpose, and the composer is built around its
absence.** The panel does contain a `<form>`, but nothing ever submits it: Send is
`type="button"` with a click handler, and Enter is handled as a keystroke that
calls `preventDefault()`. A composer that relied on native submission would be a
composer whose Send button silently did nothing inside this sandbox — so the
capability is not granted, and the code does not want it. Chrome's console during
verification carries no sandbox violation of any kind.

**Names in the loader (Phase 37.1).** The loader's text says Nabra, the public
name, and so does the install snippet: it carries the key as `data-nabra-key`.
The loader reads `data-nabra-key` first and falls back to `data-omniai-key`, so
every snippet pasted before 37.1 keeps working unedited; when one tag carries
both, the newer name wins, and a page with one tag of each still loads one
widget. The `__omniaiWidgetLoaded` global and the `omniai:v1:` bridge messages
keep their names: nobody installing the chat reads them.

These are the attributes on the frame the loader actually creates, not the ones it
intends to: `widget-loader.spec.ts` evaluates the shipped `Widget/v1.js` in a DOM
and asserts the granted sandbox tokens exactly, the empty `allow`, the referrer
policy and the title — so a loader that quietly gained a capability would fail the
suite rather than ship. The same four attributes were read back out of the live
DOM in Chrome.

---

## 3. How the widget learns where it is

This is the load-bearing decision of the phase.

**The iframe must know which website it is embedded on**, because that is what
the server checks against the workspace's allowlist. Every obvious source is
controlled by the embedding page:

- a query parameter in the iframe's own URL — chosen by whoever wrote the embed
  code;
- `document.referrer` — suppressible, and absent under some referrer policies;
- `window.parent.location` — unreadable across origins, by design.

The one value a page cannot forge is `event.origin` on a `postMessage` it
actually sent. So **the parent speaks first**: the loader knows the iframe's
origin from its own `script.src`, sends `omniai:v1:init` addressed to that exact
origin, and the widget reads the browser-supplied `event.origin` off the message.

```
loader  ──  omniai:v1:init  ─────────────▶  widget     (targetOrigin: OmniAI)
                                             learns event.origin
widget  ──  omniai:v1:ready {position} ──▶  loader     (targetOrigin: parent)
loader  ──  omniai:v1:viewport {compact} ▶  widget
widget  ──  omniai:v1:open / close ──────▶  loader
```

The handshake repeats every 250 ms for at most 10 s and stops the moment the
widget answers. The frame's `load` event and the widget's own start-up are not
ordered with respect to each other — a framework that boots asynchronously can
attach its listener after `load` has already fired, and a single `init` would
land on nobody.

### The rules on that channel

- **After origin discovery, no application message is ever sent with `'*'`.** The
  loader always names the iframe's origin; the widget always names the discovered
  parent origin, and stays silent before it has one.
- The loader validates **both** `event.origin === <OmniAI origin>` and
  `event.source === frame.contentWindow`. The origin check alone would accept a
  message from any other OmniAI frame on the page; the source check alone would
  accept a message from this frame after it had been navigated elsewhere.
- The widget validates `event.source === window.parent` and that the origin is a
  real `http(s)` origin. `"null"` — a sandboxed or `data:` document — is refused:
  it identifies nothing, so no allowlist entry could ever match it.
- Messages outside the `omniai:v1:` namespace are ignored, and so are messages
  inside it that this version does not know. A v1 loader must not guess at v2
  semantics.
- **Nothing of substance crosses.** No credential, no conversation id, no message
  text, no TenantId, no AgentId, no knowledge id or content. The parent learns a
  corner and a boolean.

---

## 4. The allowlist

`WebChatOrigin` (Domain) is a canonical `scheme + host + optional non-default
port`, and nothing else.

**Accepted:** `https://` anywhere; `http://` only on loopback (`localhost`,
`*.localhost`, `127.0.0.1`, `[::1]`), because a developer building their site
locally is the one honest case for an unencrypted origin.

**Refused, with a reason:** `*` and any wildcard; a path, query or fragment;
userinfo; the opaque origin `"null"`; any scheme but http/https; a bare hostname;
anything over 255 characters.

**Canonicalised on the way in:** scheme and host lower-cased, IDN converted to its
punycode form (which is what the browser reports), IPv6 bracketed, default port
dropped. `HTTPS://WWW.Example.COM:443/` and `https://www.example.com` are one
value, so approving one and "removing" the other is impossible.

Because canonicalisation happens on the way in, the runtime check is **exact
equality**. No prefix matching, no suffix matching, no subdomain rule:
`https://evil.www.example.com` and `https://www.example.com.evil.test` do not
match `https://www.example.com`, and there is no code path where they could.

The list is relational — `web_chat_allowed_origins`, primary key
`(web_chat_endpoint_id, origin)`, composite foreign key
`(web_chat_endpoint_id, tenant_id) → web_chat_endpoints (id, tenant_id)` with
`ON DELETE CASCADE` — so duplicates are impossible and one workspace cannot
approve a website for another workspace's endpoint. It is bounded at 20 per
endpoint.

### It is embedding policy, not abuse protection

> Origin allowlisting decides **where the widget may appear in a browser**. It is
> not, and must never be treated as, a replacement for Phase 16.

An approved website still goes through the per-network start limit, the per-
conversation send limit, the workspace generation budget, the conversation lock
and the idempotency key. A caller who is not a browser ignores the allowlist
entirely — they can send whatever header they like — which is precisely why the
allowlist is not where abuse is stopped.

---

## 5. Two transports, one runtime, no shared authority

`WebChatSession` now records a `WebChatClientMode` (`Hosted` or `Embedded`) and,
for an embedded session, the `WebChatOrigin` it was opened on. The aggregate
refuses to exist in any other combination, and a check constraint says the same
thing in the database.

|                       | Hosted (`/chat/:key`)                    | Embedded (`/embed/:key` in an iframe)                                            |
| --------------------- | ---------------------------------------- | -------------------------------------------------------------------------------- |
| Credential carried in | `HttpOnly` cookie, per-conversation path | `X-OmniAI-Embed-Session` header                                                  |
| Readable by script    | No                                       | Yes, inside the OmniAI iframe only                                               |
| CSRF defence          | Antiforgery token                        | Non-ambient credential + a header that forces a preflight this API never answers |
| Third-party cookies   | Not applicable (first-party)             | **Never used**                                                                   |

`PublicWebChatAccess.AuthoriseAsync` takes the mode and checks it **before** the
credential is compared:

```csharp
if (session is null || !session.AdmitsMode(mode) || !session.Admits(presented))
    return null;
```

So an embedded credential presented as a hosted cookie authenticates nothing, and
a hosted cookie value presented in the embed header authenticates nothing. Both
are covered by tests at the domain, application and HTTP levels. A conversation id
on its own remains insufficient for either.

### Revocation is live

For an embedded session the authoriser then re-reads the endpoint and asks
`endpoint.Allows(session.EmbeddedOrigin)` on **every** read and send. Removing a
website therefore stops conversations that are already open on it, immediately,
without anybody hunting down sessions. The conversations themselves survive and
stay visible in the admin — the customer really did say those things.

### Why no CORS was added

The widget never calls the API from the customer's origin. It calls from the
OmniAI iframe, where it is same-origin. So:

- **No `Access-Control-Allow-Origin` header is emitted by any route**, and `*`
  least of all. An integration test asserts this on the embed routes even when an
  `Origin` header is present.
- The embed routes carry no antiforgery filter, and do not need one: the
  credential is not ambient, and every route requires a custom header, which
  forces a CORS preflight that the closed API never answers.

---

## 6. Lazy conversations

Unchanged from Phase 16 and re-asserted for the new transport:

| Event                                          | Conversations created |
| ---------------------------------------------- | --------------------- |
| Customer's page loads, loader runs             | 0                     |
| Iframe loads, handshake completes, config read | 0                     |
| Visitor opens the widget                       | 0                     |
| Visitor sends their first real message         | 1                     |

`GET /api/public/embed/{publicKey}` creates nothing at all. On a busy website the
alternative would leave one empty conversation per page view in somebody's
workspace.

The first send costs two requests — a start followed by a message. That is the
honest trade for lazy creation, and it is recorded rather than designed around.

---

## 7. The credential, and where it lives

A 256-bit CSPRNG value, returned **once** in the start response under
`Cache-Control: no-store`. PostgreSQL stores a SHA-256 digest and nothing else;
comparison is constant-time.

Inside the iframe it is kept in `sessionStorage` on the OmniAI origin, so a reload
of the customer's page resumes the conversation. That storage belongs to the
OmniAI origin, is partitioned per top-level site by the browser, and dies with the
browser session.

It is never:

- sent to the parent through `postMessage`;
- placed in a URL path, query or fragment;
- written to `localStorage`, or to any storage the embedding page can read;
- written to a log.

Storage access is wrapped in `try`/`catch` with an in-memory fallback, because a
browser configured to block site data throws outright — and a widget that crashed
over a convenience value would be a worse failure than one that simply cannot
resume after a reload.

---

## 8. Appearance: four values

`WebChatWidgetSettings` is a greeting, an accent colour, a launcher position and a
theme. That is the entire surface a tenant controls.

| Setting           | Type                              | Notes                                                                                                                                                           |
| ----------------- | --------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Greeting          | plain text, ≤ 300 chars, optional | Line feeds allowed, other control characters refused. Rendered as the workspace's own words — **not** as an AI message, not in the transcript the server holds. |
| Accent            | `#rrggbb`                         | Expanded and lower-cased. Reaches the browser as a CSS custom property value; six hex digits cannot introduce a declaration.                                    |
| Launcher position | `Left` \| `Right` \| `Start` \| `End` | `Start` and `End` (Phase 37.5a) follow the host page's reading direction; see below. |
| Theme             | `System` \| `Light` \| `Dark`     | `System` means the _visitor's_ OS setting, read inside the iframe.                                                                                              |

The readable foreground on the accent is **computed**, not configured: WCAG
relative luminance, best of near-black and white. Asking a workspace to also pick
a legible foreground would be asking them to do the contrast arithmetic, and the
answer would be wrong often enough to matter.

**Phase 36.9 (frontend only).** The rules live in `frontend/src/app/features/embed/accent-color.ts`
and run before anything is painted: a value that is not `#rrggbb` is replaced by the default;
where neither near-black nor white reaches 4.5:1 (a band of mid-tones such as `#2e8b57`, which
had a 4.25:1 white label before), the label is black; a launcher under 3:1 on a white page gets
a 1px edge in its label colour. The settings screen shows these checks with their ratios and a
live preview drawn by the widget's own components. Nothing is refused: the server still stores
any `#rrggbb`.

The frame's geometry, the launcher footprint (a 56px circle 16px inside the 84px closed frame)
and the bridge messages are unchanged. The widget document no longer asks `/api/auth/me` (it
only fetches the anti-forgery token), loads no forms module, and loads the realtime client
only once a conversation exists.

There is deliberately no custom CSS, no custom HTML, no custom JavaScript, no font
URL and no logo upload.

**Phase 37.5a: corners that follow the page.** `Start` is the bottom corner where the
host page's text starts and `End` the corner where it ends: left and right on a
left-to-right page, the other way round on a right-to-left one. Only the host page
knows which way it reads, so the **loader** resolves them, from `<html dir>` when the
page declares it and otherwise from the direction the browser computed, on every
layout (a page that switches language in place moves the launcher with it). The
widget document passes the configured word to the loader unchanged in its `ready`
message; `Left` and `Right` behave exactly as before, and an existing widget keeps
its corner. The column stores the name, with no check constraint, so no migration
was needed. A loader older than 37.5a would treat `Start`/`End` as `Right`; the
loader is served by the API, so it is upgraded in the same deployment.

**Phase 37.5a: the workspace's name (owner decision D-3).** The embedded read
(`GET /api/public/embed/{key}`) also carries `workspaceName`, the workspace's display
name, so a visitor knows whose chat it is. Nothing else about the workspace is read
or published. The owner controls it with `PUT /api/tenants/{id}/name`
(`tenant-onboarding-vertical-slice.md`), and visitors see a rename on the next read.

---

## 9. Configuring it

`GET` / `PUT /api/tenants/{tenantId}/website-chat/{id}/widget`, guarded by
`website-chat.view` and `website-chat.manage` respectively.

- **Strong ETag on every read.** A `PUT` with no `If-Match` is `428`; with a stale
  one, `412`; a success returns the new tag.
- **The whole intended list of websites is sent, not a difference.** Computing a
  change on the client would mean computing it from a view that may already be out
  of date, and an origin somebody thought they had removed still being allowed is
  exactly the failure the list exists to prevent.
- **A `412` preserves what the reader typed.** Removing a website is a security
  action; replaying one against state nobody has seen could reinstate a website
  that had just been removed. The screen says so and offers an explicit reload
  that discards the local edits.

**Coded refusals (Phase 37.5a, row 21).** The admin `PUT` names each refused rule
with a stable `code` (`problem-codes.md`): `website-chat.origin.invalid` (one line is
not an origin; the detail quotes it), `website-chat.origin.too_many`,
`website-chat.launcher_position.invalid`, `website-chat.theme.invalid`,
`website-chat.accent_color.invalid`, `website-chat.greeting.too_long`. A website
listed twice is still merged and never refused, so there is no duplicate code.

The response also carries the install snippet and loader URL, **composed
server-side** from the address this deployment is actually answering on. A
hardcoded production domain would produce a snippet that silently does nothing on
staging, and a snippet nobody can test is a snippet nobody should trust.

---

## 10. One refusal for every reason

Every way an embed request can fail returns **404 with no body**:

- an unknown public key;
- a malformed public key;
- a missing, malformed or opaque origin;
- a website nobody approved;
- a website whose approval was withdrawn;
- a credential that does not match;
- a session that has expired.

The public key appears in the customer's own HTML. Telling these cases apart would
let anybody holding one enumerate the websites a workspace has authorised — which
is precisely the list needed to attack it.

**Phase 37.5a keeps this exactly.** The admin side now carries stable codes; these
public refusals do not, by design. Strictly, the 404 is not empty: since
`UseStatusCodePages` the pipeline writes its generic problem body (`type`, `title`,
`status` and a per-request `traceId`), with no `detail` and no `code`. That body is
the same for every reason above apart from the trace id, which is the property that
matters; a test compares an unknown key with an unapproved website, and the 37.5a
report compares the responses before and after this step.

The one thing that is _not_ flattened into 404 is an endpoint that exists and has
approved this website but is not currently serving: that returns a configuration
with `isAvailable: false`, so the widget renders nothing rather than a launcher
that would refuse every click.

---

## 11. The provider a development machine may not reach

A widget message ends at an AI provider, and that provider costs money. Which
means the interesting question on a developer's laptop is not "is a provider
configured" but "did anybody decide it should be".

**They are not the same question, and assuming they were cost real money once.**
During this phase's first browser verification the API was started from a shell
that already had `OPENAI_API_KEY` in its environment for an unrelated project.
The process inherited it, the run that was meant to exercise the
provider-unconfigured path composed the live adapters instead, and the first
message typed into the widget reached OpenAI and came back with a genuine answer.
The only symptom was that it worked.

So in **Development** the key is necessary but not sufficient. `OMNIAI_LIVE_OPENAI`
must also be exactly `1`:

| Environment | `OPENAI_API_KEY` | `OMNIAI_LIVE_OPENAI`                  | Composed                         |
| ----------- | ---------------- | ------------------------------------- | -------------------------------- |
| Development | absent           | any                                   | unconfigured, both providers     |
| Development | present          | absent                                | **unconfigured, both providers** |
| Development | present          | `0`, `true`, `yes`, `01`, `1 `, empty | **unconfigured, both providers** |
| Development | present          | `1`                                   | live adapters                    |
| Production  | present          | _not consulted_                       | live adapters                    |
| Production  | absent           | _not consulted_                       | unconfigured, both providers     |

Three things about the shape of that table are deliberate.

**The match on `1` is exact and ordinal.** A guard whose vocabulary is generous —
`true`, `yes`, any non-empty value — is a guard somebody defeats by accident,
which is the failure it exists to prevent.

**It covers both providers, not one.** `IAiTextGenerator` and
`ITextEmbeddingGenerator` are composed from the same key in the same place, so
guarding only the visible one would leave the silent one — indexing a knowledge
document — still billing.

**Production does not consult it.** This is a cost guard for developer machines,
not a security control for deployments. A deployment that has deliberately
configured a provider must not be taken offline because somebody forgot a second
flag.

The opt-in is read through `IConfiguration`, so the composition tests prove every
row of that table without mutating the process environment — global state they
would otherwise be sharing with every test running in parallel. When the guard
holds, the application behaves exactly as it does on a machine with no key at
all: the existing explicit refusal, no outbound request, nothing spent.

---

## 12. What this phase does not do

- No third-party cookie is used, set, or relied upon anywhere.
- No `Access-Control-Allow-Origin` was added to the API.
- No custom CSS, HTML, JavaScript, font or image may be supplied by a tenant.
- No wildcard, subdomain or regex origin matching exists, and none is planned.
- The allowlist does not replace, weaken or bypass any Phase 16 protection.
- The hosted chat page is untouched and unaffected by a widget's allowlist.
- No historical migration was edited; Phase 18 added exactly one.
- No physical `xmin` column exists anywhere; schema tests assert its absence.
