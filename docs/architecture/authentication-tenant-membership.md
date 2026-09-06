# Authentication and tenant membership

Phase 12. The first security boundary in this platform.

Before it, a tenant id in a URL was believed: anyone who could reach the process
could name any tenant and receive its data. After it, three things must agree
before tenant-owned data moves — the tenant id in the route, the authenticated
account, and a membership row in the database pairing the two.

```
route tenant id        →  which resource is being addressed
authenticated account  →  who is asking
membership row         →  whether they may
```

The first two are supplied by the caller. Only the third is authority, and it is
read from the database on every request.

---

## Decided now

### D1 — ASP.NET Core Identity, for credentials only

`Microsoft.AspNetCore.Identity.EntityFrameworkCore` 10.0.11, pinned to the same
patch as every EF Core package so the two cannot resolve different versions.

Nothing about password hashing, verification, salting or lockout counting is
written by hand. That is not a stylistic preference: a bespoke password hasher is
the single most reliably catastrophic thing a team can write, and Identity's is
maintained, versioned and upgradeable in place.

**Where it lives.** The stores are in Infrastructure, because they are
persistence. The endpoints and the cookie configuration are in the API, because
they are HTTP. Neither the Domain nor the Application references Identity at all,
and executable guards assert it — `IdentityBoundaryTests` names the assemblies
explicitly rather than relying on the general "Domain references nothing"
rule, so a failure says what was violated rather than merely that something was.

### D2 — `IdentityUserContext`, not `IdentityDbContext`

The difference is four tables against seven. `IdentityUserContext<TUser, TKey>`
omits roles, user-roles and role-claims entirely.

Phase 12 has no roles. Creating three empty tables that imply a permission system
which does not exist would be schema invented from a template, and it would
invite somebody to start writing rows into it before anybody had decided what a
role means here. `TenantMembershipSchemaTests` asserts they are absent.

The three tables that do come along — `user_claims`, `user_logins`,
`user_tokens` — are required by `UserOnlyStore`, the store Identity resolves for
a role-free configuration. Dropping them would mean writing a bespoke store, and
re-implementing credential persistence to avoid three empty tables is a poor
trade in a security-critical component. They are also where password-reset,
email-confirmation and external-login state will live when those phases arrive.

### D3 — One database context

`OmniAiDbContext` derives from `IdentityUserContext<OmniAiUser, Guid>`.

A membership row carries foreign keys to both a user and a tenant, and creating a
workspace has to write the tenant and the membership in one transaction. Two
contexts could hold neither guarantee without a distributed transaction or a
hand-shared connection — considerably more machinery to protect a boundary that
is already enforced by which layer may name which type.

### D4 — Table naming

Identity's defaults (`AspNetUsers`, `Id`, `NormalizedEmail`) are renamed to this
schema's convention: `users`, `user_claims`, `user_logins`, `user_tokens`, all
`snake_case`, matching `tenants`, `ai_agents`, `conversations` and `messages`.

Renamed, not reshaped. Not one column is dropped, widened or retyped — Identity's
stores read these by name through their own model, and quietly removing one that
looks unused would break a store method reached only in a flow nobody has
exercised yet.

Constraint and index names follow the existing convention too: `PK_<table>` from
EF's default, `fk_…` and `ix_…` written out in lowercase.

### D5 — User identity is a UUIDv7 `Guid`

`IdentityUser`'s default key is a `string` holding a GUID, which would have stored
every user id as text and made the membership foreign key a `varchar` pointing at
a `uuid`-shaped platform.

Sequential integers were rejected outright: they disclose how many accounts exist
and how fast they are being created, and make one account's id guessable from
another's.

Ids are allocated through `Domain.Users.UserId.New()`, which is `Guid.CreateVersion7()` —
the same allocator every other identifier in this platform uses. Version 7 keeps
the time-ordered insert locality that makes a b-tree behave, without carrying a
count.

### D6 — Email is the login identifier

`RequireUniqueEmail` is on, and `UserName` is set to the address at registration
so the two never disagree.

Uniqueness is enforced twice. The Identity option is a read-then-write that two
concurrent registrations can both pass; the unique index on `normalized_email` is
what makes exactly one of them win. Identity normalises to upper-case invariant,
so an address differing only in case is the same address — which is how people
actually think about email, and avoids a second account nobody could reliably
sign in to.

### D7 — Cookie authentication, not JWT

The admin is a same-origin browser client. For that shape a session cookie is
both simpler and safer:

- The browser stores it where script cannot reach it, attaches it automatically,
  and drops it on expiry.
- A JWT would have to live somewhere JavaScript can read, which turns any XSS
  defect anywhere on the page into account takeover.
- A token design brings a refresh-token flow, a revocation story and a
  signing-key rotation problem — all to solve a cross-origin problem this client
  does not have.

External APIs, mobile clients and service-to-service calls may genuinely need
tokens. They can have a design that fits them, when they exist. Solving for them
now would be paying the whole cost of the harder architecture for no current
benefit.

### D8 — The session cookie

| Attribute | Value | Why |
| --- | --- | --- |
| Name | `omniai.session` | Not the framework default, which announces the stack for no benefit |
| `HttpOnly` | always | Script that can read it can impersonate its holder |
| `SameSite` | `Lax` | See below |
| `Secure` | required outside Development | See D9 |
| `Path` | `/` | The whole application |
| Lifetime | 8 hours, sliding | A working day without re-authentication; not indefinite |
| Persistence | session cookie | No "remember me" — see D10 |

**`Lax` rather than `Strict`.** Every request that needs this cookie is
same-site, so `Strict` would cost nothing in security terms — but it would mean
that following a link into the admin from anywhere else showed a signed-out
application until the reader navigated again, which reads as a broken session.
`Lax` withholds the cookie from cross-site POSTs, which is the case that matters,
and the antiforgery token covers those regardless.

**No `__Host-` prefix.** That prefix requires `Secure`, which local HTTP
development cannot set, so the cookie's *name* would have to differ between
environments — exactly the sort of difference that works everywhere except
production. Adopting it belongs with the deferred production-hardening work.

**The API answers statuses, not redirects.** `OnRedirectToLogin` and
`OnRedirectToAccessDenied` are overridden to set `401` and `403`. The framework
default is a `302` to a login page that does not exist here, which would reach a
JSON client as a successful navigation to HTML and be reported as a parse
failure — burying an expired session under an unrelated error.

### D9 — Environment-specific cookie security

```
Development           -> CookieSecurePolicy.SameAsRequest
everything else       -> CookieSecurePolicy.Always
```

Keyed on `IsDevelopment()`, not on "is not Production". A deployment that forgets
to set `ASPNETCORE_ENVIRONMENT` therefore gets the strict behaviour, not the
convenient one.

**This applies to all three cookies**: the session cookie, the antiforgery secret
cookie, and the readable `XSRF-TOKEN` cookie. Getting there took a correction, and
the correction is the most instructive part of this phase — see D9a.

### D9a — Why TLS termination broke this, and the fix that was rejected

**The symptom.** ASP.NET Core's antiforgery service *throws* when its cookie
policy is `Always` and `Request.IsHttps` is `false`. Behind a TLS-terminating
reverse proxy, the connection Kestrel accepts is plain HTTP even though the
browser spoke HTTPS — so that condition is met on **every** request. Every
production deployment would have answered `500` to the very first request the
admin application makes, and nobody could have signed in.

It was found by writing a Production-environment test, not by reading the code.

**The fix that was rejected.** The first attempt set the antiforgery cookies to
`SameAsRequest` so the exception stopped. That is treating the symptom by removing
the guarantee: `Secure` exists to stop the browser sending a cookie over plain
HTTP, and dropping it means the browser is now permitted to do exactly that. The
deployment problem would have been "solved" by making the deployment less secure.

It is worth being precise about why the reasoning was wrong. The argument at the
time was that the antiforgery cookies are not credentials — one authenticates
nobody on its own, the other is only meaningful paired with it. That is true, and
irrelevant. `Secure` is a transport guarantee, not a statement about how sensitive
a value is, and weakening it because a value seems unimportant is how a deployment
ends up with a rule nobody can state.

**The fix.** Make the browser's scheme survive the proxy hop, so `Request.IsHttps`
is true and `Always` both holds *and* works.

```
Browser --HTTPS--> reverse proxy --HTTP--> Kestrel
                                  + X-Forwarded-Proto: https
                                       |
                                       v
                             UseForwardedHeaders
                                       |
                                       v
                           Request.Scheme == "https"
                                       |
                                       v
                      Secure cookies issue correctly
```

### D9b — Trusted forwarded headers

`UseForwardedHeaders`, configured in `ForwardedHeadersSetup`.

**Only `X-Forwarded-Proto` is processed.** `X-Forwarded-For` and
`X-Forwarded-Host` are deliberately left alone: nothing here reads the client
address or reconstructs absolute URLs, so accepting them would widen the trusted
input surface for no benefit. The framework default for `ForwardedHeaders` is
`None`, so this is an opt-in of exactly one header.

**Forwarded headers are attacker-controlled input.** Anything that can open a TCP
connection to Kestrel can send `X-Forwarded-Proto: https`. The only thing
separating a real proxy from a liar is *where the connection came from*, which no
header can express — so the middleware consults
`HttpContext.Connection.RemoteIpAddress` and ignores the header entirely when it
is not on the trusted list.

**The default trust boundary is loopback, and it comes from the framework.**
Verified against .NET 10 rather than assumed: `KnownProxies` starts as `[::1]` and
`KnownIPNetworks` as `[127.0.0.0/8]`. That is precisely the same-host
reverse-proxy topology this platform expects, and nothing off the machine is
trusted unless somebody says so.

**Configuration, not source.** Additional trust is supplied through ordinary
non-secret deployment configuration:

```
ForwardedHeaders:KnownProxies:0    = "10.0.0.4"
ForwardedHeaders:KnownNetworks:0   = "10.0.0.0/8"
```

No address is compiled in. Hard-coding a production host into source would put a
deployment fact in the repository and make every environment need its own build.

**Configured trust replaces the defaults rather than adding to them.** An operator
who names their proxies has made a deliberate statement about the boundary, and
silently keeping loopback trusted alongside it would mean the configuration did
not say what it appeared to say. A typo therefore *narrows* trust and the
application fails safe rather than widening it. A malformed address or CIDR block
is refused at startup rather than skipped, so a mistake is a failure to start
rather than a silently smaller trust list.

`ForwardLimit` stays at the framework default of 1 — one proxy hop, which is the
topology this platform has. A second proxy is a deployment change that must be
made deliberately.

**`KnownIPNetworks`, not `KnownNetworks`.** The latter is typed against
`Microsoft.AspNetCore.HttpOverrides.IPNetwork`, which is obsolete as of .NET 10 in
favour of `System.Net.IPNetwork`; the analyzer refuses it outright, which is how
the deprecation was noticed.

### D9c — Middleware ordering

```
app.UseForwardedHeaders();       <- FIRST: establishes Request.Scheme
app.UseOmniAiSecureTransport();  <- the fail-safe, before anything sets a cookie
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();
```

The order is not stylistic. Everything after the first line may read
`Request.Scheme`: the antiforgery service decides whether it may issue a token, and
the cookie handler decides whether to mark cookies `Secure`. Both would be
deciding from a false premise if forwarded headers ran later.

### D9d — Fail-safe when no trusted proxy established HTTPS

`SecureTransportMiddleware`. Outside Development, a request that did not arrive
over HTTPS is **refused**, not served with weaker cookies.

The situation it covers is real: the proxy is misconfigured, or its address is not
on the trusted list, and this process cannot tell whether the browser used TLS.
There are only two honest responses to that, and issuing cookies as though
everything were fine is not one of them.

What happens: nothing downstream runs, so no session cookie, no antiforgery cookie
and no account is created. The caller gets `500` with *"This deployment is not
configured to serve requests securely"* — which says nothing about proxies,
trusted addresses or which check failed, because a caller cannot fix it and an
attacker should not be handed a description of the topology. The operator gets a
`Critical` log entry naming the peer address and the configuration section to
change.

**Development is exempt entirely**, so `http://localhost` keeps working. The
exemption is keyed on `IsDevelopment()`, so Staging, Production and an
unconfigured environment all get the strict behaviour.

**`/health` is exempt, and only it.** An orchestrator calls it over the internal
hop, before and independently of any proxy; a probe that could not answer would
report a healthy process as dead. It sets no cookie and reads no session.

### D9e — What is still deferred

This is application configuration, not deployment. The VPS was not accessed, no
Nginx configuration was read or written, and the actual proxy topology has not
been discovered. Naming the production proxy's address, adopting the `__Host-`
cookie prefix and verifying HTTPS termination end to end all belong to the
deployment and hardening phase.

What this phase guarantees is that when that phase arrives the application is
already correct: it will accept the forwarded scheme from an address the operator
names, and refuse to serve anything insecurely if they get it wrong.

### D10 — Password policy

```
RequiredLength           12
RequireDigit             false
RequireLowercase         false
RequireUppercase         false
RequireNonAlphanumeric   false
```

Length over composition. Composition rules are the reason people produce
`Password1!` — they push everybody towards the same small set of predictable
substitutions while adding very little real entropy, and NIST SP 800-63B is
explicit that arbitrary composition rules should not be imposed. A twelve
character passphrase is both stronger and easier to remember than an eight
character one wearing punctuation.

Both halves are asserted: a shorter password is rejected, and twelve lowercase
letters with no digit or symbol is accepted. A test that only checked the
rejection would pass just as well against a policy demanding punctuation.

### D11 — Lockout

```
MaxFailedAccessAttempts   5
DefaultLockoutTimeSpan    15 minutes
AllowedForNewUsers        true
```

Identity's own counter. No bespoke brute-force tracking, and no distributed rate
limiter for a scaling problem this deployment does not have.

`AllowedForNewUsers` matters: the alternative leaves every freshly registered
account unprotected, which is precisely the set an attacker would target.

The sign-in endpoint passes `lockoutOnFailure: true`. Without it the counter is
never incremented and the whole policy is unreachable — so the test that proves
lockout uses the *correct* password on the sixth attempt, because a success there
would mean the counter was never applied.

### D12 — Antiforgery

Cookie authentication means the browser attaches the session to requests another
site caused, so a token is required.

**Two values, and confusing them would be the bug.**

| | Cookie | Readable by script | Role |
| --- | --- | --- | --- |
| Secret half | `.AspNetCore.Antiforgery.*` | no | proves the request came from this origin |
| Request half | `XSRF-TOKEN` | **yes, deliberately** | copied into `X-XSRF-TOKEN` by the client |

The request token is not a credential: on its own it authenticates nothing, and
it is only accepted alongside the secret half. Script must be able to read it,
because the double-submit pattern requires the client to echo it in a header —
which a cross-site attacker cannot do, since the same-origin policy stops them
reading this origin's cookies even though the browser will send them.

**Enforced by an endpoint filter, not by `UseAntiforgery`.** The middleware
validates only endpoints the framework has decided require it, which for minimal
APIs means those binding form data. Every endpoint here takes JSON, so none would
have been validated and the protection would have looked present while doing
nothing. `AntiforgeryEndpointFilter` is applied at each route group's mapping
site, so a new group without it is a visible missing line rather than an absent
metadata attribute.

**Safe methods are exempt by method, not by route** — GET, HEAD, OPTIONS, TRACE.
That exemption is why preview is *not* exempt: it stores nothing, but it is a
POST that costs money at a third party, and a cross-site request that spends this
deployment's provider budget is a real attack even though no row changes.

**SameSite is not the design.** It is one control, implemented by the browser,
varying by version. The token is the control this server enforces itself.

### D13 — Angular integration

```ts
withXsrfConfiguration({ cookieName: 'XSRF-TOKEN', headerName: 'X-XSRF-TOKEN' })
```

Both names are stated on both sides rather than relied on as defaults, so a
change to either is a visible edit in two files instead of a silent mismatch that
surfaces only as rejected writes.

Angular attaches the header to unsafe, same-origin requests only. Every URL in
this application is a relative `/api/...` path, so the same build works behind
the dev proxy and behind a production reverse proxy, and no CORS configuration
was added — none is needed for a same-origin client.

### D14 — `TenantMembership`

A Domain entity, holding exactly three things:

```
UserId  ·  TenantId  ·  CreatedAt
```

**Why the Domain.** Membership is the authorization rule, and authorization rules
are business rules. It carries a `UserId`, which is a *value* — an identifier for
the person acting — not an account. No password hash, no email, no Identity type
appears in the Domain, and a guard test asserts it.

**No fourth field.** No role, no permission set, no invitation state, no "is
owner" flag. Today every member of a workspace is its creator, so a role column
would hold the same value in every row and would be a schema commitment made
before anybody decided what the roles are.

**Storage.**

| | |
| --- | --- |
| Table | `tenant_memberships` |
| Primary key | `(user_id, tenant_id)` composite |
| Index | `ix_tenant_memberships_tenant` on `tenant_id` |
| FK → `tenants` | `fk_tenant_memberships_tenant`, `ON DELETE RESTRICT` |
| FK → `users` | `fk_tenant_memberships_user`, `ON DELETE RESTRICT` |

The composite key *is* `UNIQUE(user_id, tenant_id)`, structurally rather than as a
separate constraint a future migration could drop. A person either belongs to a
tenant or does not; a second row for the same pair has no meaning, and a
surrogate key would let one exist.

`user_id` leads the key, so "which workspaces does this person have" is a range
scan on it and "may this person touch this tenant" is a single probe. Neither
needs its own index. The tenant index exists for the other direction: PostgreSQL
does not index a foreign key automatically.

**Both keys RESTRICT, and the user one is the interesting choice.** A cascade
there would mean deleting an account silently removed every membership it held —
and since a person is today the only member of the workspaces they created, those
workspaces and every agent in them would become permanently unreachable while
still occupying the database. What should happen when a workspace loses its last
member is a product decision nobody has taken, so the constraint refuses the
deletion and forces that decision to be made deliberately. No endpoint deletes an
account, so nothing meets this refusal by accident.

**The user foreign key is written by hand in the migration.** EF Core requires
both sides of a relationship to agree on their CLR type; this side is a `UserId`
value object and the principal side is `IdentityUser<Guid>.Id`, a framework type
this platform does not own. The alternatives were worse — a strongly-typed
Identity key breaks its stores, which round-trip the key through a string using a
`TypeConverter` a record struct does not have, and weakening `UserId` to a bare
`Guid` removes the type that makes a user id unassignable from a tenant id. The
constraint is an ordinary PostgreSQL foreign key with real referential integrity,
and it is asserted against `pg_constraint` on the applied schema, which is
stronger evidence than an assertion about an EF model would have been.

No `xmin` concurrency token: nothing updates a membership, so there is no lost
update to prevent, and adding one would imply an edit operation that does not
exist.

### D15 — `ICurrentUser`

```csharp
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    UserId? Id { get; }
}
```

The point is what it does not mention: no `ClaimsPrincipal`, no `HttpContext`, no
cookie, no scheme, no claim type. A use case needs to know whether somebody is
authenticated and who they are; neither answer depends on how it arrived.

`HttpCurrentUser` in the API reads the principal, taking the claim type from
`IdentityOptions.ClaimsIdentity.UserIdClaimType` rather than hard-coding
`ClaimTypes.NameIdentifier` — spelling it out would work today and stop working
silently if the claim type were ever configured differently, and "silently" is the
problem: every signed-in person would appear anonymous, which reads as a session
bug rather than a configuration one.

A principal that claims to be authenticated but carries no usable id resolves to
`null`. It cannot be matched to a membership row, so the honest answer is that
nobody is signed in — and the request is then refused with `401`, which is exactly
what somebody holding a broken cookie needs to be told.

### D16 — `TenantAccessGuard`

```csharp
UserId RequireCurrentUser();                       // throws NotAuthenticatedException
Task<bool> IsCurrentUserMemberAsync(TenantId);     // false for a signed-in stranger
```

**Two failures, deliberately not merged.** Anonymous throws, because `401` is a
fact about the request the caller can fix by signing in. A signed-in non-member
gets `false`, which each use case turns into its own ordinary not-found answer.

**Not a general authorization framework.** No policy engine, no requirement
handler, no permission catalogue. One question is asked because one question is
what this phase authorises.

A plain class rather than an interface: nothing substitutes it, and an interface
would make it possible to register a permissive implementation by accident.

### D17 — Defence in depth

`RequireAuthorization()` on a route group proves somebody is signed in. It says
nothing about *which* tenant they may address — and the tenant id arrives in the
route, where the caller put it.

So the membership check lives in the execution path, in every tenant-scoped use
case, below HTTP:

```
GetTenantById · ListMyTenants · CreateTenant
ListAiAgents · CreateAiAgent · GetAiAgentById
RenameAiAgent · UpdateAiAgentInstructions
ActivateAiAgent · DisableAiAgent · PreviewAiAgent
```

`AiAgentMembershipTests` exercises every one of them without HTTP, with a real
guard over a membership store that grants nothing. Those tests would fail if the
check lived only in a route mapping — which is the mistake they exist to catch,
because publishing an endpoint without its authorization requirement is a
one-line error no compiler notices.

The database predicates that scope every query to its tenant remain in place
beneath that check. Two independent layers would both have to fail for one
tenant's data to reach another's request.

**Ordering matters, twice over.**

- Membership is checked *before* the tenant existence check in `CreateAiAgent`.
  Reversed, a non-member would learn that somebody else's workspace exists by the
  order in which they were refused.
- Membership is checked *before* the entity tag is compared in every mutation. A
  genuine `ETag` for another tenant's agent therefore produces `404`, never `412`
  and never a successful write.
- Membership is checked *before* the AI provider is reached in preview. Nothing
  below that line runs for a non-member: the agent is never read, its
  instructions are never loaded, and the generator is never called.

### D18 — `401` against tenant-hiding `404`

| Situation | Status |
| --- | --- |
| Not authenticated | `401` |
| Authenticated, not a member | `404` |
| Authenticated, member, resource absent | `404` |

The last two are indistinguishable by design — same status, same title, same
detail once the caller's own tenant id is substituted out. A `403` would confirm
that the workspace exists, which is exactly what must not be learnable by trying
ids. `TenantAuthorizationTests` asserts the bodies match, not merely the statuses.

`403` remains defined in the client error model for a future explicit permission
check, but nothing produces one today.

**Phase 11's list rule was replaced, and the replacement is stronger.** That phase
answered `200` with an empty page for an unknown tenant so a caller could not tell
a real id from an invented one; with no authentication that was the best available
answer. It is now the weaker one, because it would hand a signed-in stranger a
successful response for somebody else's workspace. The Phase 11 test was narrowed
rather than deleted: the property it protected — that the two situations are
indistinguishable — is still asserted, and only the status they share changed.

### D19 — Authenticated workspace creation, atomically

`POST /api/tenants` requires a session. It writes the tenant **and** the creator's
membership, in one transaction.

A tenant without a membership is not untidy, it is broken: nobody could ever open
it, because every request checks membership and no endpoint grants one. It would
sit in the database as a permanent orphan.

`ITenantRepository.AddAsync` therefore takes the membership as a **required
argument**. Phase 12 replaced the single-argument version rather than adding a
second method beside it, so forgetting the membership is a compile error rather
than something review has to notice.

**Why an explicit transaction, when one `SaveChanges` is already atomic.** Because
of the failure path. The slug race is caught in the repository, the losing tenant
is detached, and the caller gets a clean conflict — but without an enclosing
transaction the membership would still be sitting in the change tracker as an
`Added` row for a tenant that will never exist, and the next save would try to
insert it and fail on a foreign key with an error nobody could explain. The
transaction and the paired detach leave the unit of work exactly as it was.

**Not an `IUnitOfWork`.** One use case needs two rows written together, so the
transaction lives in the one operation that needs it. A general
commit-orchestration abstraction would have meant reworking every accepted
repository so that none of them committed, to serve a single call site.

**The slug race is unchanged.** The unique index still decides; exactly one of
eight concurrent creators wins, and the seven losers leave no orphan tenant and no
orphan membership. Asserted over HTTP with eight signed-in accounts.

### D20 — `GET /api/me/tenants`

The authoritative list of tenant ids the admin application may use.

It takes no parameter. Who is asking is the session, not an argument — a user id
parameter would create a method whose security depended on every call site passing
the right value. There is no shape of request that asks about somebody else.

Each entry carries the four fields the tenant itself owns. No member list, no
member count, no role, no owner. Ordered oldest membership first, tie-broken by
tenant id, so the workspace switcher does not reshuffle between page loads — and
the ordering is applied *after* the join, because a sort placed before one is not
guaranteed to survive into the generated SQL.

An account with no memberships gets an empty array, not a `404`. That is the state
of everybody the moment after they register.

### D21 — Frontend authentication state

`AuthService` holds three things: a status, the account's two public fields, and
nothing else. No token, no cookie value, no credential. A reload re-asks the
server, which is the only source that can answer.

`'unknown'` is a real state, not a placeholder. Before the first `/me` answers
this application genuinely does not know, and must not guess: assuming anonymous
would flash the sign-in screen at somebody who is signed in, and assuming
authenticated would flash the admin at somebody who is not.

**Startup order**, in an app initializer that runs before the first route renders:

```
GET /api/auth/csrf   →  establish the antiforgery pair
GET /api/auth/me     →  authenticated or anonymous
GET /api/me/tenants  →  only if authenticated
```

That is what removes the flicker: the router's guards run against a resolved
state. The two requests before first paint are covered by markup inside
`<app-root>` in `index.html` — a real loading state that the browser paints
immediately and Angular replaces on bootstrap, themed by the same pre-paint
script, rather than a blank page.

A backend that cannot be reached settles the application on `anonymous` rather
than leaving `unknown` on screen forever. The sign-in screen is where the next
failure can be reported in terms somebody can act on.

### D22 — Workspace selection

The selected tenant id is remembered in `localStorage`. It is a **preference**,
and the server is the boundary.

| Situation | Behaviour |
| --- | --- |
| Stored id is in the account's list | kept |
| Stored id is not in the list | **discarded** |
| One workspace, nothing stored | selected automatically |
| Several workspaces, nothing stored | the reader chooses |
| The list request failed | selection **kept**, status `unavailable` |

The third and fourth rows are the reason one workspace is not presented as a
choice: a list of one is a step that exists only to be completed. The fifth is
the reason `none` and `unavailable` are different states — a backend that is not
running says nothing about which workspaces exist, and discarding a valid
preference on every restart would be its own bug.

`select()` refuses any id not in the authorised list. That is not the security
boundary, but it means no screen can be built on the assumption that a
hand-edited id might become the selection. Verified in a real browser: planting
another account's tenant id in `localStorage` and reloading discards it, restores
the reader's own workspace, and shows nothing belonging to the other account.

### D23 — Sign-out and session expiry

`POST /api/auth/logout` requires both a session and a token — an endpoint any site
could trigger would let a third party sign people out at will.

It never throws on the client. Every caller navigates to the sign-in screen
immediately afterwards, so a rejected promise would strand somebody on the page
they were trying to leave; local state is cleared whatever the server said, and
the next request meets a `401`.

The workspace selection is cleared alongside it. It belonged to the account that
has just stopped being signed in, and the next person to use the browser must not
be shown the name of a workspace that is not theirs.

**One interceptor**, for the one cross-cutting concern no screen can own. A dead
session is not about the request that discovered it: it invalidates every screen
at once, and the right response is the same everywhere. It cannot loop — it reacts
only to `401`, the sign-in screen it navigates to makes no authenticated requests,
and `/api/auth/login`, `/api/auth/me` and `/api/auth/csrf` are excluded because
their own `401` is a legitimate answer rather than an expiry. Without that
exclusion a mistyped password would look like a broken application.

It rethrows, so the calling screen still stops its own spinner.

### D24 — Anonymous surface

Exactly five endpoints answer without a session:

```
GET  /health              liveness; a probe that needed a session could not be called
GET  /openapi/v1.json     Development only
GET  /api/auth/csrf       a client that cannot get a token can never sign in
POST /api/auth/register
POST /api/auth/login
```

Everything else requires authentication, asserted per route group rather than
once — authorization is configured per group, so proving one answers `401` proves
nothing about another.

### D25 — Generic sign-in failure

Wrong password, unknown address and locked-out account all produce the same `401`
with the same title and the same sentence.

Distinguishing the first two turns the login form into an account-existence
oracle: somebody can learn which of a list of addresses are registered here simply
by trying them. An unknown address also pays for a hash of the supplied password
against a throwaway account before answering, because otherwise it returns in
microseconds while a known one pays for a deliberately slow key derivation — a
difference measurable over a network, which would restore the oracle the shared
message exists to close.

**Folding lockout in is a genuine trade, made deliberately.** Telling a caller
"this account is locked" is equally an admission that the account exists. The
cost is real — somebody who has locked themselves out is told only that their
credentials are wrong — and it is bounded by the fifteen-minute window. Telling
the account holder by email is the right fix and arrives with email transport.

**Registration is the documented exception.** A taken address is reported plainly,
because a self-service registration form cannot both refuse duplicates and hide
that it did so without email confirmation to defer the answer into an inbox.
Closing it is part of the deferred email-verification work.

### D26 — Registration creates an account and nothing else

No workspace, no role, no permission. The request body has two fields, so there is
no field through which a caller could ask for a tenant or a privilege — asserted
anyway, by posting extra fields and confirming they change nothing.

Signing the new account in immediately is deliberate: somebody who has just proved
they hold the password by choosing it has told us everything a sign-in would. It
is only defensible while email confirmation is not required; once it is,
registration must stop granting a session and start granting a pending one.

### D27 — `ETag` behaviour is unchanged

Authentication and client concurrency solve different problems, and Phase 12
changed nothing about the second. `ETag`, `If-Match`, `412`, `428` and `409` behave
exactly as Phase 11 accepted them. Verified in the browser: a rename sent
`if-match: "AABtPA"` and received `etag: "AABtPQ"`.

The only interaction is ordering — membership is resolved before any precondition
is compared, so another tenant's real tag produces `404` rather than a
precondition result.

---

## Verified in a real browser

Local PostgreSQL, local backend, local Angular. No VPS, no OpenAI key.

- Registered, signed in, created a workspace, created an agent, renamed it with a
  real conditional request, activated it, previewed it (a genuine `503`, no
  provider named), signed out, was refused the admin, signed back in and found
  the workspace and agent intact.
- The session cookie and the antiforgery secret cookie are both invisible to
  `document.cookie`; only `XSRF-TOKEN` is readable.
- `localStorage` holds a theme, a sidebar preference and a tenant id. No token, no
  password.
- Every mutation carried `x-xsrf-token`; preview carried it too and carried no
  `If-Match`.
- A second account was refused every one of Alice's resources with `404` —
  including preview, which returned `404` rather than the `503` an unconfigured
  provider gives, proving it never reached the provider.
- Planting Alice's tenant id in Bob's `localStorage` and reloading discarded it.

Development is served over plain `http://localhost` throughout, which is the
exemption D9d describes. The production guarantees it does not exercise — every
cookie `Secure`, the forwarded scheme accepted only from a trusted proxy, and an
untrusted peer's spoofed header ignored — are proven instead by
`ProductionProxyCookieTests`, `ProductionProxyCsrfTests` and
`ForwardedHeaderTrustTests`, which host the application as `Production` behind a
simulated proxy.

---

## Deferred

Named so that nobody mistakes absence for completeness. **This phase does not
authorise production deployment.**

| | Why it matters |
| --- | --- |
| Email confirmation | Addresses are unverified; registration enumeration stays open until it lands |
| Password recovery | There is no reset. A forgotten password today means a lost account |
| Multi-factor authentication | Password alone is the whole factor |
| Roles and permissions | Membership means full access. Must land **before** member administration is exposed |
| User invitations | No way to add a second member, deliberately |
| User administration | No list, search, or account deletion. Deletion semantics are unanswered — see D14 |
| SSO / OIDC | No external provider |
| External API tokens, mobile auth, refresh tokens | See D7 |
| Security-event auditing | Sign-ins and failures are logged, not audited. Server-side session revocation belongs here |
| Production rate limiting | Identity's lockout is per-account, not per-origin |
| Reverse-proxy deployment | The application accepts a trusted forwarded scheme (D9b) and refuses insecure requests (D9d). What is deferred is the deployment side: naming the production proxy's address, adopting the `__Host-` prefix, and verifying TLS termination end to end against the real host |
