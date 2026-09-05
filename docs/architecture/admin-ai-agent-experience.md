# Admin AI agent experience

The first real product workflow: choose a development workspace, list AI agents,
create one, configure it, control its lifecycle, and test it — all against the
real backend.

Phase 11 added one backend capability (a tenant-scoped agent list) and the
frontend that consumes the existing API.

---

## D1. The development workspace context — and why it is not authentication

**There is no authentication in this platform.** No sign-in, no session, no
token, no principal, no permissions. `WorkspaceContextService` remembers which
tenant id the operator wants the admin UI pointed at, and nothing more.

That id lives in `localStorage`, where the operator can read and edit it. The
backend believes whatever tenant id the URL carries, because there is no verified
principal to check it against. **Anyone who can open this application can reach
any tenant whose id they know.**

It is named for what it is. `AuthService`, `CurrentUserService` or
`IdentityService` would be a lie that outlived the phase and would eventually be
trusted by somebody who had not read this document. A unit test asserts the
service's surface contains no `user`, `token`, `auth`, `login`, `role` or
`permission`.

The route guard is the same story: it answers "is a tenant selected and does it
exist", never "may this person see it". It exists so screens that need a tenant
id are never rendered without one — a correctness concern, not a security one.

**Consequence: this application and the endpoints behind it remain
development-only and are not authorised for public deployment** until an
authentication phase binds a verified principal to the tenant ids it may use.

## D2. Workspace bootstrap

```
stored id?  ──no──▶  setup screen  ──create tenant──▶  adopt, store id, enter admin
    │
   yes
    │
    ▼
GET /api/tenants/{id}
    ├── 200 ──▶ ready
    ├── 404 ──▶ clear the stored id, back to setup
    └── anything else ──▶ "cannot reach the backend", id KEPT
```

**The 404-versus-unreachable distinction is the whole design.** A 404 is the
backend saying the tenant is genuinely gone, so keeping the id would strand the
operator on a workspace that can never load. A connection failure says nothing
about whether the tenant exists — clearing on that would discard a perfectly good
workspace every time the backend restarts, and would invite the operator to
create a duplicate to "fix" it. The unreachable state therefore offers a retry
and a deliberate "forget", and **no create form**.

The setup screen states on its face that it is not a sign-in, and asks for no
credential of any kind.

Resetting is labelled **"Change workspace"**, never "Sign out": nothing is signed
in, no session ends, and the tenant and its agents are untouched.

## D3. The agent list endpoint

`GET /api/tenants/{tenantId:guid}/ai-agents?page=1&pageSize=20`

```json
{
  "items": [
    { "id": "…", "tenantId": "…", "name": "…",
      "status": "Draft", "createdAt": "…", "updatedAt": "…" }
  ],
  "page": 1, "pageSize": 20, "totalCount": 4
}
```

**No instructions.** They can run to twenty thousand characters, a list shows
none of them, and the projection does not select the column — so the cost is not
paid anywhere in the stack and the tenant's configured business behaviour is not
published by a listing. An Infrastructure test captures the generated SQL and
asserts `instructions` never appears in it.

**No entity tag**, in the body or the header. A page is not a representation a
caller may write against; publishing a tag would invite a conditional write
against something only half read. And no `xmin`, provider, model or vendor
metadata, matching the single-agent contract.

## D4. Pagination

Explicit from the first day the endpoint exists. Defaults `page=1`,
`pageSize=20`; maximum `pageSize=100`.

**Out-of-range values are refused with 400, not clamped.** Clamping is the more
common choice and the wrong one: a caller that asked for five hundred and
silently received a hundred cannot tell a truncated page from the end of the
list, so it stops paginating and loses data. Being told is recoverable; guessing
is not.

`page` and `pageSize` bind as nullable integers, so "not supplied" stays
distinguishable from "supplied as zero" — `?page=0` is a mistake and is reported
as one rather than silently reinterpreted as page 1.

## D5. Ordering

`updated_at DESC, id ASC`.

The tie-break is not cosmetic. Two agents created in one transaction share an
`updated_at` exactly, and without a total order PostgreSQL may return them in a
different order each time — which under paging makes consecutive pages overlap or
skip rows. A test seeds rows with identical timestamps and asserts the order is
both deterministic and stable across calls.

No sort parameter: a query surface with no requirement behind it is a surface
that must be supported before anybody knows whether it was right.

**No new index, and no migration.** The existing `tenant_id` index supports the
scoped read, agent counts are expected to be modest, and a composite
`(tenant_id, updated_at DESC, id)` index would be added on measurement rather
than on speculation. Recorded as deferred.

## D6. Tenant isolation

The tenant predicate is in the SQL, first, for both the page **and the count** —
a caller cannot learn how many agents somebody else has from a total that looks
too large. Proved at three levels: Application (with a fake that also filters, so
the test cannot pass against an unscoped handler), Infrastructure against real
PostgreSQL, and API over HTTP including a check that another tenant's agent name
never appears in the response bytes.

## D7. The frontend API boundary

Two services, `TenantApi` (root) and `AiAgentApi` (provided at the feature route,
so it lands in the lazy chunk). Typed request and response interfaces. **No page
component calls `HttpClient` directly**, and no client is generated.

**Same-origin relative paths.** Nothing names a host or a port. In development
the Angular dev server proxies `/api` to `http://localhost:5027`
(`proxy.conf.json`); in production the same path is served by a reverse proxy in
front of the backend. A hard-coded `http://localhost:5027` would work on one
machine and be a bug everywhere else. No CORS change was needed.

**No interceptors.** There is no token to attach, and error handling belongs to
the screens that know what a failure means to the reader in front of them.

## D8. The error model

One function, `toApiError`, and a small closed union of kinds:

| Status | Kind | What the reader is told |
| --- | --- | --- |
| 400 | `validation` | the backend's own `detail` |
| 404 | `notFound` | the backend's own `detail` |
| 409 | `conflict` | the backend's own `detail` |
| 412 | `stale` | "updated somewhere else" — the conflict panel |
| 428 | `preconditionRequired` | treated exactly as `stale` |
| 502 | `invalidResponse` | "the AI provider returned a response this platform could not use" |
| 503 | `providerUnavailable` | "AI preview is currently unavailable because no provider is configured" |
| 504 | `providerTimeout` | "the AI provider took too long to respond" |
| 0 | `offline` | "could not reach the OmniAI backend" |
| other | `unknown` | generic |

A component that switches on `412` has HTTP knowledge smeared through it; one
that switches on `'stale'` says what it means.

**Only `detail`, and only for 400/404/409** — where the backend authors genuine
guidance a reader can act on. Everything else gets our copy, so a server fault or
a careless future handler can never put internals on screen. Never shown: `type`,
`traceId`, status codes, stack traces, SQL, Npgsql, or any provider name. Tests
plant `Npgsql.PostgresException` and `OpenAI key rejected for org-1234` in error
bodies and assert neither reaches the DOM.

## D9. Entity tag state — the heart of the editor

- The tag is read from the `ETag` header when the agent loads, and kept in a
  signal.
- Every mutation sends it as `If-Match`.
- **Every successful response replaces it with the new tag.**
- It is never shown, parsed, compared, stripped of quotes, or constructed. It is
  a string the server produced and the server will interpret; the moment a client
  derives one, the guarantee is gone.

The tag reflects the version **the reader saw**, which is the point: somebody who
opened the page ten minutes ago is checked against what they were looking at, not
against whatever the server holds now.

`AiAgentApi` makes the precondition impossible to forget — the mutation methods
will not compile without a tag — and throws loudly if a response carries no
`ETag`, rather than returning an empty string that would later be rejected as a
confusing 412.

**A missing local tag blocks mutation.** The save controls disable rather than
sending an unconditional request. The backend's 428 remains defence in depth, and
is handled identically to 412 if it ever arrives.

## D10. What happens on 412

The behaviour this phase exists to get right:

1. The save is refused.
2. **The reader's unsaved text stays exactly where it is.**
3. A panel explains what happened in plain words, confirms nothing was
   overwritten, and offers one action: *Reload latest version*.
4. **Nothing is retried, merged or overwritten automatically.**
5. The local tag is dropped, so no further mutation can be attempted with a
   version already known to be stale.
6. Reloading warns first if it would discard work, then performs a fresh `GET`
   and adopts the new tag.

Silently replacing the editor with server state would destroy the thing the
reader was in the middle of writing — precisely the loss the precondition exists
to prevent. Merge and conflict editing are deferred.

## D11. 409 is a different thing, and looks different

A lifecycle refusal — disabling a draft — is not a stale version. It is not fixed
by reloading and re-saving the same request. It is reported in the backend's own
words, beside the status card, and never as an entity-tag conflict. The editor
then re-reads quietly to reconcile the displayed status, **without touching
fields the reader is editing**.

The status card only ever offers transitions the agent's current state allows: a
draft is offered *Activate* and no *Disable*, because showing an action
guaranteed to be refused invites a click and teaches the reader that this
product's buttons do not work.

Disabling asks for confirmation through a native `<dialog>` — real focus trap,
Escape, inert backdrop, focus return, all from the platform. Activating does not:
confirming every action is how confirmation stops meaning anything.

## D12. Dirty state

Derived, not tracked: a field is dirty when it differs from what the server
holds. An unchanged field cannot be saved — the button is disabled and no request
is sent, because a "save" that rewrites the same value would still move
`updatedAt` and advance the version, making somebody else's open editor stale for
nothing.

Unsaved edits are shown and announced, and offered a discard. Reloading after a
conflict warns before replacing them. No form-state framework was added.

## D13. Preview

`POST …/preview` with one message. **No `If-Match`, no entity tag consumed** — it
changes nothing, so there is no write to protect and nothing that can advance the
version the editor is holding.

**Single-turn, because the endpoint is stateless.** The transcript below the box
is this browser tab's own record; it is never sent back as context, never
persisted, and disappears on reload. The panel says so, because presenting it as
a conversation the agent remembers would be a lie about what the platform does.

Works for Draft, Active and Disabled: status governs whether an agent serves
customers, not whether its owner may test it.

States: idle, generating, success, and one panel per failure kind (400 / 502 /
503 / 504 / offline). **Nothing is retried automatically** — a timeout in
particular may mean the provider is still working and still being billed, so a
second request doubles the cost of a situation already going badly.

If the instructions on screen are unsaved, the panel says so: preview uses the
saved instructions, and somebody testing an unsaved edit would otherwise conclude
their change had no effect.

## D14. No provider secret ever reaches the browser

The browser calls this platform's backend and nothing else. There is no OpenAI
SDK in the frontend, no direct provider request, and no API key in any Angular
source file, environment file, bundle, storage or request. Phase 11 works
correctly with **no provider configured at all** — the 503 state is a designed,
polished outcome rather than a failure mode.

## D15. Responsive strategy

The list is a list of links, styled as an aligned grid from Bootstrap's `md` and
as stacked cards below it — one markup, two layouts, and no horizontally
scrolling table on a phone. These rows are navigational, not tabular data
somebody compares column by column.

The editor is a single column until `1200px`, then splits into configuration and
tools, with the editor given the larger share. Verified at 390, 768, 1024, 1440
and 1920 with zero horizontal overflow and zero overflowing elements.

## D16. Browser validation strategy

Automated tests prove contracts; the browser proves the product. Both were run.
The 412 scenario in particular was exercised with **two real tabs against the real
backend**, because "the request carried `If-Match`" says nothing about whether a
reader's half-written instructions survive a rejected save.

---

## Deferred

| Deferred | Note |
| --- | --- |
| Authentication | The next major gate before any deployment. |
| Authorization / permissions | Nothing to authorise against yet. |
| Real current-tenant principal | The workspace context is a development stand-in and must be replaced, not promoted. |
| Tenant membership and switching | Needs a membership model. |
| User profile | No users exist. |
| Server-stored preferences | Theme, sidebar and workspace all live in `localStorage`. |
| Agent search, filtering, sorting | No requirement yet; the endpoint deliberately has no sort parameter. |
| Bulk operations | Same. |
| Instruction version history | Would change the concurrency design; not needed yet. |
| Collaborative editing / merge | 412 deliberately refuses rather than merging. |
| Agent deletion or archive | `DELETE` still answers 405; what happens to conversations is undecided. |
| Composite list index | Add on measurement, not speculation. |
| Conversations, RAG, tools, channels | Later phases. |
| Dashboard aggregation | No endpoint authorised; the overview still shows no metrics. |
| ESLint | Still no lint script or config in the project; none added here. |
| Production deployment | Not authorised while the API is unauthenticated. |
