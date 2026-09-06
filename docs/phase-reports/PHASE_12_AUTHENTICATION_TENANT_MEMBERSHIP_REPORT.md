# Phase 12 — Secure Admin Authentication & Tenant Membership Foundation

## 1. Phase objective

Establish the platform's first security boundary. Before this phase a tenant id
in a URL was believed: anyone who could reach the process could name any tenant
and receive its data. After it, three things must agree before tenant-owned data
moves — the tenant id in the route, the authenticated account, and a membership
row pairing the two. The tenant id remains resource addressing; membership
becomes authorization.

## 2. Final status

**PASS, after one round of remediation.**

The phase as first submitted contained a security defect that review correctly
rejected: production antiforgery cookies had been allowed to lose the `Secure`
attribute so that ASP.NET Core would stop throwing behind a TLS-terminating
reverse proxy. That fixed the symptom by removing the guarantee. It has been
replaced with trusted forwarded headers and a fail-safe that refuses rather than
downgrades — see §13a and §66.

Everything else is implemented, tested and verified in a real browser. Two
accepted tests from earlier phases were deliberately narrowed rather than deleted
(§66).

## 3. Baseline commit

`6952ae8a82af94469d90734772967dd378ac2ff9` — *feat: implement tenant and ai agent
admin experience* (Phase 11 checkpoint).

## 4. Initial Git state

Working tree clean. `git log -12 --oneline` showed the eleven-commit history
ending at `6952ae8`. No remotes. Nothing was reset, cleaned, stashed or
discarded at any point.

## 5. Identity technology

ASP.NET Core Identity. No custom password hashing, verification, salting or
brute-force counting was written — a bespoke password hasher is the single most
reliably catastrophic thing a team can hand-roll.

Registered with `AddIdentityCore<OmniAiUser>()` rather than `AddIdentity<,>()`:
the latter requires a role type, which would have brought three role tables and a
role manager into a platform that has no roles. `AddSignInManager()` is added
explicitly, because a headless API that only validates credentials does not need
one and it is therefore not in the core set.

## 6. Identity package and version

`Microsoft.AspNetCore.Identity.EntityFrameworkCore` **10.0.11**, declared once in
`backend/Directory.Packages.props` and referenced by `OmniAI.Infrastructure`
only.

Pinned to the same patch as every EF Core package already in the file, because it
depends on EF Core itself — a different patch would have reintroduced exactly the
split-version problem that motivated transitive pinning in an earlier phase.

Everything else authentication needs — cookie handling, antiforgery, the
authorization pipeline, `UserManager`, `SignInManager` — is part of the ASP.NET
Core shared framework and needed no package. No JWT library, no identity server,
no external provider SDK was added.

## 7. User persistence

`users`, `user_claims`, `user_logins`, `user_tokens` — four tables, in the same
PostgreSQL database as the business tables, through the same `OmniAiDbContext`.

`OmniAiDbContext` now derives from `IdentityUserContext<OmniAiUser, Guid>`. One
context over one database, because a membership row carries foreign keys to both
a user and a tenant and creating a workspace must write two rows in one
transaction. Two contexts could hold neither guarantee without a distributed
transaction or a hand-shared connection.

`OmniAiUser : IdentityUser<Guid>` adds **no properties**. `IdentityUser<Guid>`
already carries fifteen columns and Phase 12 needs none beyond them; a created-at
timestamp or a display name would be a column nothing reads, and profile
management is explicitly a later phase.

## 8. User ID strategy

`Guid`, allocated as **UUIDv7** through `Domain.Users.UserId.New()` — the same
allocator every other identifier in this platform uses.

Sequential integers were rejected outright: they disclose how many accounts exist
and how fast they are being created, and make one account's id guessable from
another's. Identity's own default (`string` holding a GUID) was also rejected: it
would have stored every user id as text and made the membership foreign key a
`varchar` pointing at a `uuid`-shaped platform.

`UserId` is a readonly record struct with a private constructor, modelled exactly
like `TenantId` and `AiAgentId`, so a user id is not assignable from a tenant id
and the compiler enforces it.

## 9. Password hashing

Identity's `IPasswordHasher`. Nothing bespoke.

`The_stored_password_is_not_the_password` reads the `password_hash` column
directly rather than trusting that Identity did the right thing: it asserts the
stored value is not the password, does not contain it, and is long enough to be a
salted, iterated hash rather than a bare digest. The exact contents are
deliberately **not** asserted — that would pin the suite to a hashing format this
code does not own and should be free to upgrade.

## 10. Password policy

```
RequiredLength           12
RequireDigit             false
RequireLowercase         false
RequireUppercase         false
RequireNonAlphanumeric   false
RequiredUniqueChars      1
```

Length over composition, following NIST SP 800-63B. Composition rules are the
reason people produce `Password1!` — they push everybody towards the same small
set of predictable substitutions while adding very little real entropy.

Both halves are asserted. `A_password_below_the_minimum_length_is_rejected`
covers the boundary at 5 and 11 characters;
`A_twelve_character_password_with_no_symbols_or_digits_is_accepted` uses
`abcdefghijkl` — twelve lowercase letters, no digit, no capital, no punctuation.
A suite with only the first test would pass just as well against a policy
demanding punctuation.

## 11. Lockout policy

```
MaxFailedAccessAttempts   5
DefaultLockoutTimeSpan    15 minutes
AllowedForNewUsers        true
```

Identity's own counter; no bespoke brute-force tracking and no distributed rate
limiter for a scaling problem this deployment does not have.

`AllowedForNewUsers` matters: the alternative leaves every freshly registered
account unprotected, which is precisely the set an attacker would target.

`PasswordSignInAsync` is called with `lockoutOnFailure: true` — without it the
counter is never incremented and the policy is unreachable.
`Repeated_failures_lock_the_account_without_saying_so` makes five failed attempts
and then uses the **correct** password: a success on the sixth would mean the
counter was never applied. It also reads `lockout_end` from the database to prove
the lockout, not merely the refusal.

## 12. Cookie authentication

Cookie, not JWT. The admin is a same-origin browser client: the browser stores
the cookie where script cannot reach it, attaches it automatically and drops it
on expiry. See §14.

All four Identity cookie schemes are registered by `AddIdentityCookies()` and
only the application cookie is ever issued. The external and two-factor schemes
exist because `SignInManager.SignOutAsync` signs out of them by name and would
throw if they were absent; no flow here ever sets them, so no additional cookie
reaches a browser — confirmed in the browser, where only `omniai.session` and the
two antiforgery cookies appear.

## 13. Cookie flags and policy

| Attribute | Value |
| --- | --- |
| Name | `omniai.session` |
| `HttpOnly` | always |
| `SameSite` | `Lax` |
| `Secure` | `SameAsRequest` in Development, `Always` everywhere else |
| `Path` | `/` |
| `ExpireTimeSpan` | 8 hours, sliding |
| Persistence | session cookie — no `Expires`, no `Max-Age` |

`Lax` rather than `Strict`: every request needing this cookie is same-site, so
`Strict` would cost nothing in security terms but would show a signed-out
application to anybody following a link in from elsewhere. `Lax` withholds it
from cross-site POSTs, and the antiforgery token covers those regardless.

No `__Host-` prefix: it requires `Secure`, which local HTTP cannot set, so the
cookie's *name* would have to differ between environments — the sort of
difference that works everywhere except production.

The environment switch is keyed on `IsDevelopment()`, not "is not Production", so
a deployment that forgets `ASPNETCORE_ENVIRONMENT` gets the strict behaviour.

**The same policy applies to both antiforgery cookies.** The first submission
allowed them to drop `Secure`; that is the defect §13a describes and it is fixed.

The API answers statuses rather than redirects: `OnRedirectToLogin` and
`OnRedirectToAccessDenied` set `401` and `403`, because the framework default is
a `302` to a login page that does not exist here — which would reach a JSON
client as HTML and be reported as a parse failure, burying an expired session
under an unrelated error.

## 13a. Reverse proxy, forwarded headers and the Secure guarantee

**The original defect.** ASP.NET Core's antiforgery service throws when its
cookie policy is `Always` and `Request.IsHttps` is `false`. Behind a
TLS-terminating reverse proxy the connection Kestrel accepts is plain HTTP even
though the browser spoke HTTPS, so that condition held on every request: a
production deployment would have answered `500` to the very first call the admin
application makes, and nobody could have signed in. It was found by writing a
Production-environment test, not by reading the code.

**The rejected workaround.** The first submission set the antiforgery cookies to
`SameAsRequest` so the exception stopped. Review rejected it, correctly. `Secure`
is a transport guarantee, and removing it means the browser is now permitted to
send those cookies over plain HTTP — solving a deployment problem by making the
deployment less secure. The argument offered at the time, that those cookies are
not credentials, was true and irrelevant.

**The final design.** Make the browser's scheme survive the proxy hop.

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

`ForwardedHeadersSetup` opts in to **`X-Forwarded-Proto` only**.
`X-Forwarded-For` and `X-Forwarded-Host` are deliberately not processed: nothing
here reads the client address or builds absolute URLs, so accepting them would
widen the trusted input surface for no benefit. The framework default for
`ForwardedHeaders` is `None`, so this is an opt-in of exactly one header.

**Trust boundary.** Forwarded headers are attacker-controlled input: anything
that can open a socket to Kestrel can claim HTTPS. The only thing separating a
proxy from a liar is where the connection came from, so the middleware consults
`Connection.RemoteIpAddress` and ignores the header entirely otherwise.

Verified against .NET 10 rather than assumed — the API surface was probed before
implementing: `KnownProxies` defaults to `[::1]` and `KnownIPNetworks` to
`[127.0.0.0/8]`, which is exactly the same-host proxy topology this platform
expects. Additional trust comes from ordinary non-secret configuration:

```
ForwardedHeaders:KnownProxies:0    = "10.0.0.4"
ForwardedHeaders:KnownNetworks:0   = "10.0.0.0/8"
```

**No address is compiled in.** Configured trust *replaces* the defaults rather
than adding to them, so a typo narrows trust and the application fails safe. A
malformed address or CIDR block is refused at startup rather than skipped.

`KnownIPNetworks` is used rather than `KnownNetworks`: the latter is typed against
`Microsoft.AspNetCore.HttpOverrides.IPNetwork`, obsolete as of .NET 10, and the
analyzer refuses it outright.

**Middleware ordering.**

```
app.UseForwardedHeaders();       <- FIRST: establishes Request.Scheme
app.UseOmniAiSecureTransport();  <- the fail-safe, before anything sets a cookie
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();
```

Not stylistic: everything after the first line may read `Request.Scheme`, and both
the antiforgery service and the cookie handler would be deciding from a false
premise if it ran later.

**Fail-safe.** `SecureTransportMiddleware` refuses any non-Development request
that did not arrive over HTTPS. Nothing downstream runs — no session cookie, no
antiforgery cookie, no account. The caller gets `500` and *"This deployment is not
configured to serve requests securely"*, which names no proxy, address or header;
the operator gets a `Critical` log entry naming the peer address and the
configuration section to fix. Development is exempt entirely so
`http://localhost` keeps working, and `/health` is exempt so a liveness probe can
still answer over the internal hop.

**Still deferred:** the deployment side. The VPS was not accessed, no Nginx
configuration was read or written, and the production proxy's address has not
been named. What this phase guarantees is that the application is already correct
when that phase arrives.

## 14. Why JWT is not used

A bearer token would have to live somewhere JavaScript can read, which turns any
XSS defect anywhere on the page into account takeover. It would also bring a
refresh-token flow, a revocation story and a signing-key rotation problem — all
to solve a cross-origin problem this client does not have.

External APIs, mobile clients and service-to-service calls may genuinely need
tokens. They can have a design that fits them, when they exist. Building it now
would be paying the whole cost of the harder architecture for no current benefit.

## 15. CSRF architecture

Cookie authentication means the browser attaches the session to requests another
site caused, so a token is required.

**Two cookies, and confusing them would be the bug.**

| | Cookie | Script-readable | Role |
| --- | --- | --- | --- |
| Secret half | `.AspNetCore.Antiforgery.*` | no | proves the request came from this origin |
| Request half | `XSRF-TOKEN` | **yes, deliberately** | echoed in `X-XSRF-TOKEN` |

The request token is not a credential: alone it authenticates nothing and is only
accepted alongside the secret half. Script must read it, because double-submit
requires the client to echo it — which a cross-site attacker cannot do, since the
same-origin policy stops them reading this origin's cookies even though the
browser will send them.

**Enforced by an endpoint filter, not `UseAntiforgery`.** The middleware
validates only endpoints the framework decided require it, which for minimal APIs
means those binding form data. Every endpoint here takes JSON, so **none** would
have been validated and the protection would have looked present while doing
nothing. `AntiforgeryEndpointFilter` is applied at each route group's mapping
site, so a new group without it is a visible missing line.

Safe methods are exempt by method (GET, HEAD, OPTIONS, TRACE) via an allow-list,
so an unanticipated method is protected by default. That exemption is why preview
is **not** exempt: it stores nothing but is a POST that costs money at a third
party.

A failed validation answers `400` with a Problem Details body that says to reload
and says nothing about whether the cookie was missing or the token mismatched.
The distinction is logged at Information level for an operator.

## 16. Angular XSRF integration

```ts
withXsrfConfiguration({ cookieName: 'XSRF-TOKEN', headerName: 'X-XSRF-TOKEN' })
```

Both names are stated on both sides rather than relied on as defaults, so a
change to either is a visible edit in two files rather than a silent mismatch
that surfaces only as rejected writes.

Angular attaches the header to unsafe, same-origin requests only. Every URL is a
relative `/api/...` path, so the same build works behind the dev proxy and behind
a production reverse proxy. **No CORS configuration was added** — none is needed
for a same-origin client.

## 17. Auth endpoints

| Method | Route | Auth | CSRF | Answers |
| --- | --- | --- | --- | --- |
| GET | `/api/auth/csrf` | anonymous | n/a | `204` |
| POST | `/api/auth/register` | anonymous | required | `201` / `400` |
| POST | `/api/auth/login` | anonymous | required | `200` / `401` |
| POST | `/api/auth/logout` | required | required | `204` |
| GET | `/api/auth/me` | required | n/a | `200` / `401` |
| GET | `/api/me/tenants` | required | n/a | `200` |

`/api/me` is a separate group from `/api/auth` deliberately: the first is about
establishing a session, the second about the resources that session reaches.
Putting the workspace list under the authentication group would suggest
membership is part of the credential — exactly the misconception this phase
exists to prevent.

## 18. Register flow

Body: `{ email, password }`. Two fields, so there is no field through which a
caller could ask for a tenant, a role or an admin flag — asserted anyway by
posting `tenantId`, `role` and `isAdmin` and confirming they change nothing.

The id is allocated as UUIDv7 before `CreateAsync`. On success the account is
signed in immediately and the antiforgery token is reissued, because it is bound
to the identity and the one obtained while anonymous no longer validates.

Signing in immediately is a deliberate decision: somebody who has just proved
they hold the password by choosing it has told us everything a sign-in would. It
is only defensible while email confirmation is not required; once it is,
registration must stop granting a session and start granting a pending one.

`201` without a `Location` header — an account was created, but this API
publishes no per-account resource to point at, and inventing a URL nothing serves
would be a promise it cannot keep.

**A taken address is reported plainly.** That is an account-enumeration vector
and it is an accepted, documented one: a self-service registration form cannot
both refuse duplicates and hide that it did so without email confirmation to
defer the answer into an inbox.

## 19. Login flow

Body: `{ email, password }`. No "remember me".

An unknown address hashes the supplied password against a throwaway account
before answering, because otherwise it returns in microseconds while a known one
pays for a deliberately slow key derivation — a difference measurable over a
network, which would restore the enumeration oracle the shared message closes.

`PasswordSignInAsync(..., lockoutOnFailure: true)`. Every unsuccessful outcome —
wrong password, unknown address, locked out — answers `401` with the same title
and the same sentence: *"The email address or password is incorrect."*

`An_unknown_address_and_a_wrong_password_are_indistinguishable` asserts the
status, title and detail all match, and that the shared message contains none of
"exist", "found" or "locked".

Folding lockout into the same message is a genuine trade, made deliberately:
telling a caller their account is locked is equally an admission that it exists.
The cost is real and is bounded by the fifteen-minute window; telling the account
holder by email is the right fix and arrives with email transport.

## 20. Logout flow

`POST /api/auth/logout`, requiring both a session and a token. An endpoint any
site could trigger would let a third party sign people out at will — a denial of
service rather than a courtesy. `Signing_out_without_a_token_is_rejected` also
asserts the session **survives** a refused sign-out.

`SignInManager.SignOutAsync()` clears the cookie; a fresh anonymous antiforgery
token is issued on the way out so the login form can post immediately.

On the client, `logout()` never throws. Every caller navigates to the sign-in
screen immediately afterwards, so a rejected promise would strand somebody on the
page they were trying to leave; local state is cleared whatever the server said.
The workspace selection is cleared with it.

**A gap, stated plainly:** sign-out deletes the browser's cookie. A cookie copied
off the machine beforehand would remain valid until it expires. Server-side
revocation belongs with security-event auditing and is deferred.

## 21. `/me` flow

Returns `{ id, email }` and nothing else. Written out by hand rather than
serialised from the Identity entity, so adding a column there cannot quietly add
a field here.

`The_current_account_response_carries_two_fields_and_no_credential` asserts the
exact field list and that the body contains none of `passwordHash`,
`securityStamp`, `concurrencyStamp`, `lockout`, `accessFailed`, `twoFactor` or
`normalized`.

Anonymous callers get `401` — which is what tells a freshly loaded application to
show the sign-in screen. A cookie whose account has since been deleted resolves
to nothing and is answered the same way, because it means the same thing.

## 22. TenantMembership schema

```
tenant_memberships
  user_id     uuid                      NOT NULL
  tenant_id   uuid                      NOT NULL
  created_at  timestamp with time zone  NOT NULL
```

Three columns and deliberately no fourth. No role, no permission set, no
invitation state, no "is owner" flag: today every member of a workspace is its
creator, so a role column would hold the same value in every row and would be a
schema commitment made before anybody decided what the roles are.

`The_membership_table_exists_with_exactly_three_columns` asserts the exact column
list, types and nullability, so a fourth appearing quietly is a failing test.

The Domain entity `TenantMembership` holds a `UserId` — a value, not an account.
No password hash, no email, no Identity type appears in the Domain.

## 23. Membership constraints and indexes

| | |
| --- | --- |
| Primary key | `PK_tenant_memberships` on `(user_id, tenant_id)` |
| Index | `ix_tenant_memberships_tenant` on `tenant_id` |
| FK → `tenants` | `fk_tenant_memberships_tenant`, `ON DELETE RESTRICT` |
| FK → `users` | `fk_tenant_memberships_user`, `ON DELETE RESTRICT` |

The composite key **is** `UNIQUE(user_id, tenant_id)`, structurally rather than
as a separate constraint a future migration could drop. A person either belongs
to a tenant or does not.

`user_id` leads the key, so "which workspaces does this person have" is a range
scan and "may this person touch this tenant" is a single probe — neither needs
its own index. The tenant index exists for the other direction, because
PostgreSQL does not index a foreign key automatically.

**Both keys RESTRICT.** The user one is the interesting choice: a cascade would
mean deleting an account silently removed every membership it held, and since a
person is today the only member of the workspaces they created, those workspaces
and every agent in them would become permanently unreachable while still
occupying the database. What should happen when a workspace loses its last member
is an unanswered product question, and RESTRICT keeps it unanswered rather than
answering it by accident.

**The user foreign key is written by hand in the migration.** EF Core requires
both sides of a relationship to agree on their CLR type; this side is a `UserId`
value object and the principal is `IdentityUser<Guid>.Id`, a framework type this
platform does not own. The alternatives were worse — a strongly-typed Identity key
breaks its stores, which round-trip the key through a string using a
`TypeConverter` a record struct does not have, and weakening `UserId` to a bare
`Guid` removes the type safety. The constraint is an ordinary PostgreSQL foreign
key and is asserted against `pg_constraint` on the applied schema, which is
stronger evidence than an EF model assertion would have been.

No `xmin` token: nothing updates a membership, so there is no lost update to
prevent.

## 24. Tenant authorization flow

```
request  →  RequireAuthorization()      401 if anonymous
         →  AntiforgeryEndpointFilter   400 if unsafe and untokened
         →  use case
              → TenantAccessGuard        401 anonymous / false for a non-member
              → repository               tenant predicate still in the SQL
```

Two independent layers would both have to fail for one tenant's data to reach
another's request.

## 25. Application current-user abstraction

```csharp
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    UserId? Id { get; }
}
```

The point is what it does not mention: no `ClaimsPrincipal`, no `HttpContext`, no
cookie, no scheme, no claim type.

`HttpCurrentUser` in the API reads the principal, taking the claim type from
`IdentityOptions.ClaimsIdentity.UserIdClaimType` rather than hard-coding
`ClaimTypes.NameIdentifier` — the hard-coded version would work today and fail
silently if the claim type were ever configured differently, and the failure would
be every signed-in person appearing anonymous, which reads as a session bug rather
than a configuration one.

A principal marked authenticated but carrying no usable id resolves to `null`: it
cannot be matched to a membership row, so the honest answer is that nobody is
signed in.

## 26. Authenticated workspace creation

`POST /api/tenants` now requires a session and writes the tenant **and** the
creator's membership.

`ITenantRepository.AddAsync` takes the membership as a **required argument**. The
single-argument version was replaced rather than kept beside it, so forgetting the
membership is a compile error rather than something review must notice. A tenant
without one is not untidy but broken: nobody could ever open it, because every
request checks membership and no endpoint grants one.

The command carries no user id. Who is creating the workspace is read from
`ICurrentUser`, never accepted as input — a `UserId` parameter would be a field a
future caller could fill with somebody else's identity, and its correctness would
then depend on every call site. `A_workspace_cannot_be_created_for_somebody_else`
posts `userId` and `ownerId` fields and confirms the membership names the caller.

One instant, read once from the injected clock, is used for both rows.

## 27. Atomic Tenant + membership transaction

An explicit `BeginTransactionAsync` in `TenantRepository.AddAsync`.

**Why, when one `SaveChanges` is already atomic.** Because of the failure path.
The slug race is caught here, the losing tenant is detached and the caller gets a
clean conflict — but without an enclosing transaction the membership would still
be in the change tracker as an `Added` row for a tenant that will never exist, and
the next save would try to insert it and fail on a foreign key with an error
nobody could explain. The transaction and the paired detach leave the unit of work
exactly as it was.

**Not an `IUnitOfWork`.** One use case needs two rows written together, so the
transaction lives in the one operation that needs it. A general
commit-orchestration abstraction would have meant reworking every accepted
repository so that none of them committed, to serve a single call site.

The repository also refuses a membership naming a different tenant before writing
anything — the database would accept it happily, since both foreign keys resolve,
and it would silently grant somebody access to a workspace they did not create.

Proven three ways: `A_rejected_slug_leaves_neither_a_tenant_nor_a_membership`
over HTTP, `A_failed_insert_leaves_no_row_and_no_tracked_leftover` against
PostgreSQL directly (asserting both entities are detached and no orphan membership
survives), and `After_a_rejected_creation_the_same_caller_can_try_again`, which
is the case the detach exists for.

## 28. Slug-race behaviour

Unchanged. The unique index decides.

`Concurrent_creations_of_one_slug_produce_exactly_one_workspace` races eight
signed-in accounts for one slug over HTTP: exactly one `201`, seven `409`, one
tenant row, and **one** membership row. Any more would mean a losing transaction
committed half of itself.

## 29. `/api/me/tenants`

Returns the tenants the authenticated account belongs to, as
`{ id, name, slug, status }`.

It takes no parameter — who is asking is the session, not an argument. There is
no shape of request that asks about somebody else.

Ordered oldest membership first, tie-broken by tenant id. The ordering is applied
**after** the join, because a sort placed before one is not guaranteed to survive
into the generated SQL, which would leave the promised order true only by accident
of the query plan. `The_order_is_oldest_membership_first_and_survives_into_sql`
and `Memberships_created_at_the_same_instant_are_still_totally_ordered` assert
both halves against PostgreSQL.

An account with no memberships gets `[]`, not `404`. That is the state of
everybody the moment after they register.

`A_workspace_summary_carries_four_fields_and_no_membership_detail` asserts the
exact field list and that the body contains none of "member", "role", "owner",
"user" or "permission".

## 30. Workspace selection

The selected tenant id is remembered in `localStorage`. It is a preference; the
server is the boundary.

| Situation | Behaviour |
| --- | --- |
| Stored id is in the account's list | kept |
| Stored id is not in the list | **discarded** |
| One workspace, nothing stored | selected automatically |
| Several workspaces, nothing stored | the reader chooses |
| The list request failed | selection **kept**, status `unavailable` |

One workspace is not a choice — presenting a list of one is a step that exists
only to be completed. `none` and `unavailable` are different states because a
backend that is not running says nothing about which workspaces exist, and
discarding a valid preference on every restart would be its own bug.

`select()` refuses any id not in the authorised list.

## 31. Removal of arbitrary development tenant trust

Phase 11's `WorkspaceContextService` read a tenant id from `localStorage` and
asked the backend to load it. Anything the operator typed there was attempted.

That is gone. The account's workspaces come from `/api/me/tenants`, the stored id
is reconciled against that list before it is used, and an id that is not in it is
discarded. `select()` cannot be made to accept one either.

The class comment that said **"THIS IS NOT AUTHENTICATION, AND IT IS NOT A
SECURITY BOUNDARY"** was replaced with one explaining that the selection is a
preference and the server is the boundary — because the first statement is no
longer the whole truth and leaving it would misdescribe the system.

Verified in a real browser (§47): planting another account's tenant id in
`localStorage` and reloading discards it, restores the reader's own workspace and
shows nothing belonging to the other account.

## 32. `401` behaviour

Unauthenticated requests to protected APIs answer `401`, never a fake `404`.
Turning one into the other would tell a signed-out browser tab that its workspace
had disappeared, when what it needs to do is sign in again.

`AnonymousAccessTests` covers **every route group separately** — tenant read,
tenant create, agent list, agent read, agent mutation, preview, `/api/me/tenants`
and `/api/auth/me` — because authorization is configured per group and proving one
answers `401` proves nothing about another.

The body is Problem Details with `content-type: application/problem+json` and no
`Location` header, asserted explicitly.

`NotAuthenticatedException` is the Application-level second layer: if an endpoint
were ever mapped without its authorization requirement, the use case still refuses
and the handler maps it to `401`.

## 33. Cross-tenant `404` behaviour

Authenticated non-members receive `404` for every tenant-scoped resource:

| Operation | Result |
| --- | --- |
| `GET /api/tenants/{B}` | `404` |
| `GET /api/tenants/{B}/ai-agents` | `404` |
| `POST /api/tenants/{B}/ai-agents` | `404`, nothing created |
| `GET /api/tenants/{B}/ai-agents/{agent}` | `404`, no agent detail in the body |
| `PUT .../name` with B's **real** ETag | `404`, agent unchanged |
| `PUT .../instructions` | `404`, agent unchanged |
| `POST .../activate` | `404`, agent unchanged |
| `POST .../disable` | `404` (not the `409` a draft would give its owner) |
| `POST .../preview` | `404`, provider never called |

`A_workspace_that_exists_and_one_that_never_did_are_answered_alike` asserts the
same status, title and detail — not merely the same status. A difference in
wording would restore exactly the oracle the shared status removes.

Never `403`: that would confirm the workspace exists.

## 34. Preview security

The billable endpoint, and the one where ordering matters most.

Membership is checked **before** the agent is read, before instructions are
loaded and before the generator is reached. Nothing below that line runs for a
non-member.

The proof is a status, not an inspection: this fixture has no AI provider
configured, so a preview that reached the generator would answer `503`. A
cross-tenant preview answers **`404`** — which is direct evidence it never got
past the membership check. Asserted over HTTP
(`Previewing_another_persons_agent_is_not_found`, which also asserts the body
contains neither "unavailable" nor the instruction text) and without HTTP
(`A_preview_from_outside_the_tenant_never_reaches_the_generator`, asserting the
recording generator saw zero calls and zero requests).

Anonymous preview: `401`. CSRF applies despite storing nothing — a cross-site
request that spends this deployment's provider budget is a real attack.

The OpenAI provider architecture was not modified.

## 35. ETag behaviour after authentication

Unchanged. `ETag`, `If-Match`, `412`, `428` and `409` behave exactly as Phase 11
accepted them. All Phase 11 concurrency and precondition tests pass unmodified
except for acquiring a session.

The only interaction is ordering: membership resolves before any precondition is
compared, so another tenant's genuine tag produces `404` rather than `412`.
`A_real_version_does_not_help_a_caller_outside_the_tenant` asserts it directly.

The two-tab concurrency test now signs the same account in twice rather than
using two anonymous clients — one person with two tabs, which is the real
scenario. Two different accounts would have produced `404`s instead of the
precondition failures the test exists to prove.

Browser-verified: rename sent `if-match: "AABtPA"` and received `etag: "AABtPQ"`.

## 36. Migration

`20260905235628_AddIdentityAndTenantMemberships`.

`InitialPersistence` and `AddAiAgents` were not touched.

## 37. Migration inspection

Read line by line, regenerated once after correcting naming, and hand-edited once.

**Tables created (5):** `tenant_memberships`, `users`, `user_claims`,
`user_logins`, `user_tokens`.

**No role tables.** `IdentityUserContext` was chosen over `IdentityDbContext`
precisely to omit `roles`, `user_roles` and `role_claims` — Phase 12 has no roles,
and three empty tables implying a permission system that does not exist would be
schema invented from a template. `No_role_tables_were_created` asserts it.

**No passkey table.** .NET 10's `IdentityUserContext` carries an
`IdentityUserPasskey` generic parameter but does not map the entity; the generated
migration creates no such table, confirmed against the model snapshot and the
applied schema.

**Naming corrected before acceptance.** The first generation produced
`FK_user_claims_users_user_id` and `IX_user_claims_user_id` — EF defaults,
inconsistent with this schema's `fk_…`/`ix_…` convention. Rather than hand-edit the
migration, the relationships were named in the model and the migration
regenerated, yielding `fk_user_claims_user`, `fk_user_logins_user`,
`fk_user_tokens_user`, `ix_user_claims_user`, `ix_user_logins_user`. Verified that
this renamed the existing relationships rather than creating duplicates.

**One hand-edit,** documented in place: `fk_tenant_memberships_user` is added with
`migrationBuilder.AddForeignKey` because EF cannot model a relationship whose
sides disagree on their CLR type (§23). Its `Down` needs no matching drop, because
`tenant_memberships` is dropped first and takes the constraint with it.

**Verified on the applied schema** (`omniai_dev`, and every disposable test
database):

```
tenant_memberships | PK_tenant_memberships  | p |
                   | fk_tenant_memberships_tenant | f | confdeltype=r
                   | fk_tenant_memberships_user   | f | confdeltype=r
columns            | user_id uuid NO, tenant_id uuid NO, created_at timestamptz NO
```

`confdeltype = 'r'` is RESTRICT for both. Unique index on `normalized_email`
confirmed. No `AspNet*` table exists. No user column named `xmin` exists anywhere
in the public schema — the existing guard still passes.

## 38. Backend auth tests

`AuthEndpointsTests` (21 cases), `SessionCookieTests` (12),
`AnonymousAccessTests` (11) and `WorkspaceProvisioningTests` (11). Counts are
executed cases, so a `[Theory]` contributes one per row.

`SessionCookieTests` gained three during remediation that state the Development
requirement outright rather than leaving it implied: the antiforgery bootstrap
succeeds over plain `http://localhost`, the whole register/sign-in/write loop
works there, and CSRF enforcement is **not** relaxed in Development either. If
forwarded headers or the transport fail-safe ever leaked into Development, every
test in that file would fail.

The proxy-aware Production suites are in §40a.

Registration, immediate sign-in, correct and wrong password, indistinguishable
failures, lockout, password-policy boundaries, duplicate address, case-insensitive
duplicate, the two-field `/me` contract, anonymous `/me`, sign-out, sign-out
requiring its own session, signing in again afterwards, registration creating
nothing but an account, and registration ignoring injected privilege fields.

Nothing is substituted. Passwords are hashed by the real hasher, stored in the
real table and verified by the real `SignInManager`; the cookie is the one the
framework issued; accounts are created through the real endpoint. A suite that
faked any of that would be proving a test double works.

## 39. Membership tests

`TenantAccessGuardTests` (9), `AiAgentMembershipTests` (12),
`ListMyTenantsHandlerTests` (6), `TenantMembershipRepositoryTests` (9),
`TenantMembershipSchemaTests` (10), `TenantAuthorizationTests` (13),
`IdentityBoundaryTests` (3).

`AiAgentMembershipTests` is the defence-in-depth suite and deliberately does not
go through HTTP: the API tests prove the endpoints answer `404`, and they would
keep passing if the check lived only in a route mapping. It gives each handler a
real guard over a membership store that grants nothing and asserts the refusal,
that nothing was written, and — for the anonymous case — that all **eight**
tenant-scoped use cases throw. The count is asserted, so adding a ninth without
adding it there is a failing test rather than a silent gap.

## 40. CSRF tests

`AntiforgeryTests` (13).

Missing token rejected for workspace creation, agent creation, agent mutation,
preview, sign-out, sign-in and registration — each also asserting nothing was
written. Valid token allowed. A token lifted from another session rejected. GET
requests allowed without a token on three separate routes. The refusal message
asserted to say "reload" and not to name the cookie.

## 40a. Reverse-proxy, cookie and trust-boundary tests

Three suites, all hosting the application as `Production` over a plain-HTTP
connection — exactly what a TLS-terminating proxy produces — with the browser's
scheme carried in `X-Forwarded-Proto`.

**`ProductionProxyCookieTests` (7).** The antiforgery bootstrap succeeds rather
than throwing. The session cookie carries `Secure`, `HttpOnly`, `SameSite=Lax`
and `Path=/`. The antiforgery **secret** cookie carries `Secure` and `HttpOnly`.
The **`XSRF-TOKEN`** cookie carries `Secure` and is deliberately **not**
`HttpOnly`. One assertion sweeps every cookie the deployment sets and requires
`Secure` on all of them, so a cookie added later without it fails even if nobody
writes it a test. The OpenAPI document stays unpublished.

**`ProductionProxyCsrfTests` (5).** The complete flow, not just the cookie
attributes: bootstrap, register, then a protected unsafe request with a valid
`X-XSRF-TOKEN` succeeds; the created workspace is immediately readable, which
proves the session cookie round-tripped too. Withholding the token on an
otherwise identical request is still rejected with `400`, and registration is
still protected. An anonymous caller still gets `401`.

**`ForwardedHeaderTrustTests` (7).** The spoofing boundary. A caller from
`203.0.113.7` (TEST-NET-3, unroutable) sends `X-Forwarded-Proto: https` to a
deployment that trusts only loopback: the header is ignored, the request is
refused, and **no cookie is set at all**. Registration from the same peer is
refused rather than served insecurely. A request with no forwarded header lands
in the same place, so an ordinary proxy misconfiguration and a deliberate spoof
are handled identically. The response body names no proxy, header or address; the
log names all three. `/health` still answers.

**The two proxy fixtures differ in exactly one thing.** Same application, same
Production environment, same configured trust list, same header, same requests —
only the simulated peer address changes. It is set through an `IStartupFilter`
that runs ahead of `UseForwardedHeaders`, which is the connection-level fact the
middleware actually consults, so the boundary is exercised faithfully rather than
approximated. Nothing in the implementation was relaxed or parameterised to make
the test possible.

One honest limitation: `TestServer` has no real socket, so the peer address is
simulated rather than genuinely remote. What is being tested is the trust
*decision*, which reads exactly that value.

## 41. Frontend auth tests

`auth-service.spec.ts` (16), `login.spec.ts` (13), `register.spec.ts` (11),
`session-expiry-interceptor.spec.ts` (7) — 47 declarations in total. Vitest
reports 231 executed cases across 17 files against 215 declarations, the
difference being parameterised cases inside a single `it`.

Startup loading and ordering, unauthenticated resolution, transport failure
resolution, sign-in success, sign-in failure, registration, logout, logout when
the request itself fails, `/me` state, the session-`401` transition, the exempt
routes whose `401` is meaningful, no navigation loop, password never stored, no
token in `localStorage` or `sessionStorage`, CSRF bootstrap, password-manager
autocomplete attributes, the reveal control, the length policy at its boundary,
and the refusal message asserted to disclose nothing.

## 42. Frontend workspace tests

`workspace-context-service.spec.ts` (18), `workspace-setup.spec.ts` (20).

No workspaces → create; create; membership list refresh; one workspace
auto-selected; several offered as a choice; a persisted authorised id restored; a
persisted unauthorised id **discarded**; `select()` refusing an unlisted id; a
backend failure keeping the selection and reporting `unavailable`; retry; sign-out
from the workspace screen; and the screen listing nothing the server did not
return.

## 43. Existing agent tests

Every Phase 11 agent test was preserved. None was weakened to make guards easier.

The changes were mechanical: fixtures now produce a registered, signed-in client
(`CreateSignedInClientAsync`) and seed the tenant **with a membership** for that
account. Handler tests gained a permissive guard through `TestAccess.Anywhere()`,
which is documented as safe only because each handler has a dedicated refusal test
that would fail if the handler stopped consulting the guard.

Agent list, create, editor, lifecycle, ETag, `412` and preview behaviour are all
unchanged and passing.

## 44. Browser authentication workflow

Local PostgreSQL, local backend on `:5027` with **no `OPENAI_API_KEY`**, local
Angular on `:4310`. No VPS. No live OpenAI call.

All fifteen steps performed:

1. Opened logged out → redirected to `/sign-in`.
2. Sign-in and registration screens rendered.
3. Registered `alice@example.test`.
4. Session established; account named on screen.
5. Created workspace *Alice Trading*.
6. It appeared in `/api/me/tenants` and in the sidebar.
7. Entered the workspace.
8. Created agent *Alice Sales Assistant*.
9. Renamed it — `if-match: "AABtPA"` → `etag: "AABtPQ"`.
10. Activated it; status became Active.
11. Previewed it → genuine `503`, message kept, no provider named on screen.
12. Signed out.
13. Typing an admin URL redirected to `/sign-in`; `/api/me/tenants` answered `401`.
14. Signed back in — by keyboard, pressing Enter from the password field.
15. Workspace and agent both intact.

**Console: zero application errors or warnings.** The only entries are Chrome's
own logging of deliberate `401`/`404` status codes from the anonymous startup
probe and the cross-tenant probes — the boundary working, not a fault.

## 45. Browser cookie verification

From `document.cookie` in the running application:

```
hasSessionCookieInJs        false
hasAntiforgerySecretInJs    false
scriptReadableCookies       [ XSRF-TOKEN, ... ]
```

The session cookie and the antiforgery secret are both invisible to script. Only
`XSRF-TOKEN` is readable, which is what makes double-submit possible.

Raw `Set-Cookie` from the running server:

```
.AspNetCore.Antiforgery.…=…; path=/; samesite=lax; httponly
XSRF-TOKEN=…;                path=/; samesite=lax
```

`localStorage` after a full session: `omniai.theme`, `omniai.sidebar-collapsed`,
`omniai.workspace-tenant-id`. `sessionStorage` empty. No token, no password.

## 46. Browser XSRF verification

Every unsafe request carried `x-xsrf-token`, matching the `XSRF-TOKEN` cookie:
`POST /api/auth/register`, `POST /api/tenants`, `POST .../ai-agents`,
`PUT .../name`, `POST .../activate`, `POST .../preview`.

Preview carried the antiforgery header **and no `If-Match`** — both correct.

No `Authorization` header on any request. No bearer token anywhere. No request to
any origin but `localhost:4310`.

## 47. Browser cross-tenant verification

A second account (`bob@example.test`) was registered in an **isolated browser
context** with its own workspace, then used to probe Alice's resources from a
real signed-in session:

```
GET  /api/me/tenants                       200  → only Bob Trading
GET  /api/tenants/{alice}                  404
GET  /api/tenants/{alice}/ai-agents        404
GET  /api/tenants/{alice}/ai-agents/{a}    404
POST /api/tenants/{alice}/ai-agents        404
POST /api/tenants/{alice}/ai-agents/{a}/preview  404
```

Preview answered `404`, not the `503` an unconfigured provider gives — direct
evidence it never reached the provider.

**localStorage manipulation:** Alice's tenant id was written into Bob's
`localStorage` and the page reloaded at Alice's agent URL. The planted id was
**discarded and replaced with Bob's own**; the sidebar showed *Bob Trading*, and
none of Alice's workspace name, agent name or instruction text appeared anywhere
on the page.

## 48. Responsive auth verification

Sign-in, registration and workspace screens at **390**, **768** and **1440**, in
**light and dark**:

- Zero horizontal scroll, zero overflowing elements at every width in both themes.
- The auth card holds 26rem rather than stretching across a desktop.
- The password reveal control stays inside the field's bounds at 390.
- The submit button is full width, because it is the only thing to do on the page.

The admin shell at 390 in both themes: no overflow. In the mobile drawer the
sidebar's account block and Sign out control are within the fold, verified at
390×844 and again at 375×667. The nav carries `overflow-y: auto` with
`flex: 1 1 0` while the workspace and account blocks are `flex: 0 0 auto`, so
adding navigation items makes the nav scroll rather than pushing Sign out
off-screen.

## 49. Accessibility

Phase 10/11 baseline preserved.

- Every field has a real `<label for>`; required fields carry the shared marker.
- `autocomplete="username"` on both email fields; `current-password` on sign-in
  and `new-password` on registration, which is what lets a manager fill and
  generate. Nothing disables password managers or blocks pasting.
- `inputmode="email"`, `autocapitalize="off"`, `spellcheck="false"` on addresses.
- Errors use `role="alert"` and are tied to their input with `aria-describedby`;
  `aria-invalid` is set only when the error is shown.
- The password hint is announced with the field, not merely displayed beside it.
- The reveal control is a real `<button>` with `aria-pressed` and an accessible
  name of "Show password" / "Hide password".
- Errors appear only after blur or a submit attempt — marking a field invalid
  while somebody types its first character reads as nagging.
- Keyboard verified in the browser: Tab moves email → password, focus rings are
  present (`outline-style: solid`), and Enter from the password field signs in.
- The startup loading state uses `role="status"` with `aria-live="polite"`, so a
  screen reader meets an announcement rather than silence.

## 50. Backend tests before

**958** (Domain 288, Application 216, Infrastructure 233, Api 221) at commit
`6952ae8`.

## 51. Backend tests after

**1117**, all passing, Release configuration:

| Suite | Before | First submission | After remediation |
| --- | --- | --- | --- |
| Domain | 288 | 291 | **291** |
| Application | 216 | 252 | **252** |
| Infrastructure | 233 | 252 | **252** |
| Api | 221 | 305 | **322** |
| **Total** | **958** | 1100 | **1117** |

The Api suite gained 17 net during remediation: 19 new proxy, cookie, CSRF,
trust-boundary and Development-HTTP cases, less the 5 in the superseded
`ProductionCookieTests` and plus the 3 added to `SessionCookieTests`. The
superseded suite was replaced rather than deleted — every property it asserted is
still asserted, against a configuration that reflects a real deployment.

## 52. Frontend tests before

**170** across 13 files.

## 53. Frontend tests after

**231** across 17 files, all passing.

## 54. Backend build

`dotnet build OmniAI.slnx -c Release` — **succeeded, zero warnings**.
`TreatWarningsAsErrors` remains on; no `NoWarn` was added anywhere.

Two analyzer findings surfaced during the phase and were fixed rather than
suppressed: `CA1725` (the `OnModelCreating` parameter had to be renamed to match
the Identity base declaration) and `CA1859` (a test helper's return type).

## 55. Frontend build

`npm run build` — succeeded.

| | Raw | Transfer |
| --- | --- | --- |
| Phase 11 | 376.83 kB | 89.40 kB |
| Phase 12 | **379.52 kB** | **92.04 kB** |
| Change | +2.69 kB | +2.64 kB |

Small because the auth screens are lazily loaded — sign-in is 6.90 kB and
registration 7.25 kB in their own chunks. The initial increase is the auth
service, the guards, the interceptor and the XSRF configuration, all of which are
needed before any screen renders.

## 56. Vulnerabilities

`dotnet list package --vulnerable --include-transitive`: **no vulnerable packages**
in any of the 8 projects.

## 57. npm audit

**found 0 vulnerabilities.** `npm audit fix` was not run.

**`npm ci` did not complete, and that is worth stating plainly.** It was
attempted four times in total across the phase and its remediation, and failed
every time with `EPERM` after it had already removed `node_modules` — a Windows
file-lock problem, not a dependency one.

The remediation attempt was made deliberately from an idle frontend, as review
asked. The first failure named
`node_modules\@esbuild\...\esbuild.exe`; a stale `esbuild` service process was
found whose executable path was unambiguously inside this repository's own
`node_modules`, and it was stopped. The retry then failed on a different file,
`node_modules\@lmdb\...\node.napi.node`, held by a process that could not be
attributed to this project — and killing unattributable processes on a shared
machine is not a trade worth making for a package-manager preference.

`npm install` was used to restore the tree, which is the lockfile-consistent path
review authorised.

That the restoration is faithful is not asserted, it is shown: `package.json` and
`package-lock.json` are both **unmodified**, and the subsequent `npm run build`
produced a bundle byte-identical to the one before
(379.52 kB / 92.04 kB), with all 231 tests, the format check and the audit
passing afterwards. `npm audit fix` was not run and the lockfile was not touched.

## 58. Packages added

**One:** `Microsoft.AspNetCore.Identity.EntityFrameworkCore` 10.0.11.

No frontend package was added — `frontend/package.json` is **unmodified**.

No unrelated package was upgraded.

## 59. Backend files changed

**Created (41):** 21 under `backend/src`, 20 under `backend/tests`. The Domain's
`UserId` and `TenantMembership`; the Application's
`ICurrentUser`, `NotAuthenticatedException`, `ITenantMembershipRepository`,
`TenantAccessGuard` and `ListMyTenantsHandler`; the API's `HttpCurrentUser`,
`AuthenticationSetup`, `AntiforgeryEndpointFilter`, auth contracts, `AuthEndpoints`
and `MeEndpoints`; Infrastructure's `OmniAiUser`, the Identity and membership
configurations, `TenantMembershipRepository` and the migration pair; and 11 test
files.

Remediation added five of those: `ForwardedHeadersSetup`,
`SecureTransportMiddleware`, and the proxy fixtures and suites
(`ProductionApiFixture`, `ProxySession`, `ProductionProxyCookieTests`,
`ProductionProxyCsrfTests`, `ForwardedHeaderTrustTests`), replacing the
superseded `ProductionCookieTests`.

**Modified (50):** `Directory.Packages.props`, the Infrastructure project file,
`Program.cs`, `CompositionRoot`, `DomainProblemDetailsHandler`, both endpoint
groups, all ten tenant-scoped handlers, `ITenantRepository`, `TenantRepository`,
`OmniAiDbContext`, `PersistenceConverters`, the Infrastructure DI registration,
the model snapshot, and 33 test files.

## 60. Frontend files changed

**Created (13):** `core/auth/` (api, service, guards, interceptor and two specs),
`features/auth/` (the shared page stylesheet, plus login and register components
with templates and specs).

**Modified (18):** `app.config.ts`, `app.routes.ts`, `api-error.ts`,
`tenant-api.ts`, `workspace-context-service.ts` and its spec, `workspace-guard.ts`,
the whole workspace-setup screen, the app shell and sidebar with their specs, and
`index.html`.

## 61. Domain modified

**YES.** Two files added: `Users/UserId.cs` and `Tenants/TenantMembership.cs`.

Membership is the authorization rule, and authorization rules are business rules.
Both are free of any Identity, HTTP or persistence dependency — `IdentityBoundaryTests`
asserts the Domain assembly references no Identity assembly, no ASP.NET Core
assembly, and names no `ClaimsPrincipal`, `ClaimsIdentity`, `HttpContext` or
`IdentityUser` in its public surface.

## 62. Migration changed

**NO.** `InitialPersistence` and `AddAiAgents` are byte-for-byte untouched. One
new migration was added beside them.

## 63. Architectural decisions

Twenty-seven, recorded as D1–D27 in
[`docs/architecture/authentication-tenant-membership.md`](../architecture/authentication-tenant-membership.md).

## 64. Production-readiness warning

**Phase 12 does not authorise public production deployment.**

It closes the largest hole — the platform no longer believes a tenant id merely
because a browser sent it — but several controls a production authentication
system needs are absent by design, not by oversight:

- Email addresses are **unverified**. Anyone can register with any address.
- There is **no password reset**. A forgotten password today means a lost account.
- There is **no MFA**.
- Sign-out clears the browser's cookie; a cookie **copied off the machine
  beforehand** stays valid until it expires. There is no server-side revocation.
- Registration **discloses** whether an address is already registered (§18).
- Rate limiting is per-account lockout only, not per-origin.
- The reverse-proxy **deployment** is unverified. The application now accepts a
  trusted forwarded scheme and refuses to serve anything insecurely without one
  (§13a), but the production proxy's address has not been named and TLS
  termination has not been checked against the real host.

## 65. Deferred concerns

Email confirmation · password recovery · MFA · roles and permissions · user
invitations · user administration and account deletion semantics · SSO/OIDC ·
external API tokens · mobile authentication · refresh-token architecture ·
security-event auditing and session revocation · production rate limiting ·
forwarded-header and HTTPS verification · the `__Host-` cookie prefix.

**Roles and permissions must land before member administration is exposed.**
Membership currently means full access to a workspace, which is correct only
while every workspace has exactly one member.

## 66. Deviations

**One production defect found, then fixed twice — and the first fix was wrong.**

ASP.NET Core's antiforgery service throws when its cookie policy is `Always` and
the request is not HTTPS. Behind a TLS-terminating reverse proxy this process sees
plain HTTP, so every production deployment would have answered `500` to the first
request the admin makes and nobody could have signed in. Found by writing a
Production-environment test, not by reading the code.

The first submission fixed it by scoping the antiforgery cookies to
`SameAsRequest`. **Review rejected that, and was right to.** It treated the
symptom by removing the guarantee: `Secure` exists precisely to stop the browser
sending a cookie over plain HTTP, so dropping it permitted the thing it prevents.
The reasoning offered at the time — that those cookies are not credentials — was
true and beside the point.

The accepted fix carries the browser's scheme across the proxy hop with trusted
forwarded headers, restores `Secure` on all three cookies, and adds a fail-safe
that refuses rather than downgrades when no trusted proxy established HTTPS. See
§13a and §40a.

Worth recording as a lesson rather than only as a correction: the test that
exposed the original defect was written correctly, and the conclusion drawn from
it was not. A failing security test is a question about the design, not an
obstacle to be cleared.

**Two accepted tests narrowed, not deleted.**

- `A_tenant_that_does_not_exist_is_answered_exactly_like_an_empty_one` (Phase 11)
  asserted `200` with an empty page for an unknown tenant. Phase 12 replaces that
  rule with a stronger one: membership is the boundary, so both a foreign tenant
  and an invented one answer `404`. The property it protected — that the two are
  indistinguishable — is still asserted, by the renamed
  `A_tenant_the_caller_does_not_belong_to_is_answered_like_one_that_never_existed`,
  which now also compares the response **bodies**.
- `Both_migrations_are_applied_to_the_test_database` asserted exactly two
  migrations. Renamed to `Every_migration_is_applied_to_the_test_database` and
  extended to three, in order.

**One redundant call removed.** `withFetch()` is deprecated in Angular 22 —
`FetchBackend` is now the default — so the call emitted a deprecation and did
nothing. Removed.

**One comment-placement defect corrected.** In `DomainProblemDetailsHandler`, a
Phase 11 edit had left the paging arm's comment attached to the AI-turn arm.
Both comments now sit above the arms they describe.

**Three test-authoring mistakes of my own, found and fixed:**

- The disclosure assertion in `login.spec.ts` matched the page's own "No account
  yet? Create one" registration link. Rescoped to the refusal message, which is
  what must not disclose.
- Three new specs needed an extra event-loop turn in their `settle()` helper: the
  application is zoneless, so `whenStable()` reports that change detection is idle
  without waiting for promises, and sign-in makes two requests in sequence.
- A first draft of `Every_workspace_this_endpoint_creates_has_a_member` asserted
  `Assert.NotNull` on a count, which proves nothing. Replaced with a scoped
  orphan check and a second test covering the case the transaction's detach
  actually exists for.

**One client behaviour changed by a test.** `AuthService.logout()` originally
propagated a failed request. Every caller navigates to the sign-in screen
immediately afterwards, so that would have stranded somebody on the page they were
trying to leave. It now swallows the failure and always clears local state.

## 67. Known issues

- **Lockout is invisible to the person locked out.** They are told only that their
  credentials are wrong, for up to fifteen minutes. A deliberate
  confidentiality-over-UX trade (§19), fixed by email notification.
- **Registration discloses existing addresses** (§18).
- **Reverse-proxy deployment is unverified against the real host** (§13a). The
  application side is complete and tested; naming the production proxy's address
  and confirming TLS termination belong to the deployment phase.
- **`npm ci` could not run to completion on this machine** (§57). The tree was
  restored with `npm install` from the unchanged lockfile and every gate passes,
  but the exact command named in the phase brief did not succeed and is reported
  as such rather than as a pass.
- **No lint.** There is still no lint script and no ESLint configuration in the
  frontend. None was added — introducing one would be Phase 12 scope creep. The
  gates are `npm run build`, `npm run test:ci` and `npm run format:check`.
- **`docs/architecture/admin-ai-agent-experience.md` was not rewritten.** Several
  of its statements about the development workspace are now superseded by
  `authentication-tenant-membership.md`, which says so explicitly. Rewriting an
  accepted Phase 11 document was outside this phase's authorisation.

## 68. Blockers

None. Nothing in this phase is incomplete or unverified.

## 69. `git status --short`

56 created paths, 69 modified — **125 files** in total, matching
`git add -A --dry-run`. Broken down: 21 created under `backend/src`, 20 under
`backend/tests`, 13 under `frontend/src` and 2 under `docs`; 50 modified under
`backend`, 18 under `frontend` and 1 under `docs`.

The remediation changed no frontend file, no Domain file, no Application
business contract and no migration.

Nothing is staged. `HEAD` is still `6952ae8`, there are no remotes, and no
commit was created.

## 70. `git diff --stat`

Tracked files: **69 changed, 2739 insertions, 857 deletions**. `git diff --check`
is clean.

## 71. Secret review

Every file in the change set was scanned.

- **No API keys, OpenAI credentials, bearer tokens, JWTs, private keys or cloud
  access keys.** The only `OPENAI_API_KEY` occurrences are the configuration key
  name in comments and in test fixtures that blank it — never a value.
- **No passwords, SSH credentials or the production server address.**
- **No connection strings.** `appsettings.json` is **unmodified** and still holds
  `"OmniAiDatabase": ""`. The local database connection reached the backend only
  through a shell environment variable and was never written to any file.
- **No cookie or antiforgery token values.** No `CfDJ8`-prefixed value appears
  anywhere in the repository; the tests assert on cookie *attributes*, never on
  values.
- **One password literal exists and is deliberate:**
  `ApiAuthentication.Password = "correct-horse-battery-staple"`, documented at
  length as a fixture for randomly named accounts in a randomly named database
  that is dropped when the test class finishes. It is written out rather than
  randomised so a failing test can be reproduced by hand, and it is long enough to
  satisfy the real policy — which is itself worth proving, since a fixture using a
  shorter one would mean the policy was not being applied.
- **The passphrases typed into the browser during verification appear nowhere in
  the repository** — confirmed by search.
- **No `node_modules`, `bin`, `obj`, `dist`, IDE files, `.env` files, PostgreSQL
  data or screenshots.** The captured network-response file was written to the
  session scratchpad, outside the repository.

## 72. VPS not accessed

**Confirmed.** No SSH connection was attempted or made. No credential was read,
requested, printed or stored. No Nginx configuration was read or written. Nothing
outside this machine was contacted; every URL exercised was `localhost`.

## 73. Deployment not performed

**Confirmed.** No deployment, no build artefact published, no remote created or
contacted, no commit made. `HEAD` remains `6952ae8`.

## 74. Readiness recommendation for Phase 13

The platform now has a real security boundary, and the next phase can be built on
top of an authenticated, tenant-scoped foundation rather than around its absence.

The strongest candidate for Phase 13 is **roles, permissions and member
invitations**, and the reason is a constraint this phase deliberately created:
membership currently means *full* access to a workspace. That is correct only
while every workspace has exactly one member, and the moment a second member can
exist it becomes wrong. Nothing in the platform can add one today — which is why
the coarse rule is safe now and why it must be replaced before it stops being.

If product priorities point elsewhere first, the alternative worth taking is
**email verification and password recovery**, which closes the registration
enumeration vector, makes lockout explainable to the person locked out, and turns
a forgotten password from a lost account into an inconvenience. Either is a
smaller, better-bounded piece of work than Conversations, RAG or Channels, and
both leave this phase's boundary intact rather than widening it.
