# Tenant onboarding: the first vertical slice

**Status:** Phase 5 implemented two use cases end to end — Create Tenant and Get
Tenant By Id — through API, Application, Domain, Infrastructure and PostgreSQL.
Nothing marked DEFERRED below exists in code.

> **Not authorised for production exposure.** These endpoints are
> unauthenticated, because authentication and authorisation are not implemented
> yet. Anyone who can reach the process can create a tenant. See
> [Security posture](#security-posture).

This is the shape every later slice should follow, so the reasoning matters more
than the code volume: two use cases, three repository methods, one new error
type, two endpoints, and no framework introduced to hold them together.

---

## DECIDED NOW

### V1 — Explicit handlers, no mediator

**Decision.** One class per use case — `CreateTenantHandler`,
`GetTenantByIdHandler` — each with a constructor dependency and a single
`HandleAsync`. Registered by name at the composition root.

**Why.** A mediator, command bus or pipeline earns its cost when there are many
handlers and genuinely cross-cutting behaviour to insert between them. With two
handlers it removes nothing and adds three things: an indirection that hides the
call graph, a registration convention that has to be learned, and reflection that
turns a missing registration into a runtime failure instead of a compile error.
Today `POST /api/tenants` reaches its handler in one readable hop.

**Forbids.** MediatR (explicitly not approved), a generic command or query bus,
reflection-based handler discovery, and pipeline behaviours added before there is
cross-cutting behaviour to put in them.

**Revisit when** handlers number in the dozens *and* there is real cross-cutting
work — transaction scoping, authorisation, idempotency, auditing — that would
otherwise be repeated in every one. That is a dispatcher's actual job; two
handlers is not.

**Handler shape.** Focused (one public method), dependency-injected, and
cancellation-token aware end to end: the token reaches the repository and the
database, and an Application test asserts it is honoured rather than accepted and
ignored.

### V2 — A focused `ITenantRepository`, and no `IUnitOfWork`

**Decision.** One Application-owned interface with exactly three members:

```csharp
Task<Tenant?> FindByIdAsync(TenantId id, CancellationToken ct = default);
Task<bool>    SlugExistsAsync(TenantSlug slug, CancellationToken ct = default);
Task          AddAsync(Tenant tenant, CancellationToken ct = default);
```

**Why these three.** They are what the two authorised use cases need. Nothing was
added for a use case that does not exist.

**Forbids.** `IRepository<T>`, `GenericRepository<T>`, `RepositoryBase<T>`, a
specification framework, and any member returning `IQueryable`, `DbSet` or
`DbContext`. A generic repository has to expose either `IQueryable` or a
specification language to stay useful, and both hand the caller the storage model
back under a different name — at which point the boundary is decorative.

**No unit of work, and that is a decision rather than an omission.**
`AddAsync` commits, and the interface says so. A unit of work earns its cost when
one use case must commit several aggregates together, which is exactly when the
commit point has to move out of the repository. Both use cases here touch one
aggregate, and `SaveChanges` is already a transaction, so an `IUnitOfWork` today
would be a second abstraction that forwards a call — and would still need
reworking when real multi-aggregate transactions arrive, because their boundaries
are not yet known. `TenantRepositoryTests.Add_commits_so_a_separate_connection_sees_the_row`
checks the commit claim from outside EF Core.

**Revisit when** a single use case must write two aggregates atomically. That is
the signal, not the number of repositories.

**Enforcement.** `ApplicationDependencyTests` asserts the compiled
`OmniAI.Application` assembly references no EF Core assembly, no Npgsql assembly
and no ASP.NET Core assembly. The boundary is a failing test, not a convention.

### V3 — Duplicate slugs: the database is the authority

**Decision.** Two layers, with an explicit hierarchy between them.

1. `CreateTenantHandler` calls `SlugExistsAsync` first. This is **advisory** — a
   courtesy that gives the common case (the slug was taken long before this
   request) a clear failure before any write is attempted.
2. The **unique index `ix_tenants_slug` is the authority.** `AddAsync` lets the
   insert reach PostgreSQL, and a rejection there is what settles the outcome.

**Why the check is advisory and can never be more.** Its answer is stale the
instant it is read. Two concurrent requests for `acme` can both be told the slug
is free. Treating that answer as authoritative *is* the bug — and it is a bug
that only appears under load, which is the worst time to find it.

**Why no lock.** No table lock, no advisory lock, no serialisable transaction and
no application-level mutex. All of them tax every onboarding to tidy up a rare
race that the database already resolves correctly, and a mutex would additionally
be wrong the moment a second instance runs — which for a multi-tenant SaaS is the
expected deployment.

**Result.** Exactly one of N concurrent requests for the same slug is created;
the rest receive a conflict. Proven at two levels:

| Test | Level | What it proves |
| --- | --- | --- |
| `Concurrent_inserts_of_one_slug_produce_exactly_one_tenant` | Infrastructure, real PostgreSQL | Eight writers on eight connections; the advisory check is bypassed entirely, so **only the unique index** can be rejecting the losers. Exactly one row exists. |
| `Two_concurrent_duplicate_creations_produce_exactly_one_201_and_one_409` | API, over HTTP | The required two-request race resolves to one `201` and one `409`, never a `500`. |
| `Eight_concurrent_duplicate_creations_still_produce_exactly_one_tenant` | API, over HTTP | Widened so several callers reliably pass the advisory check before any commits. |
| `The_store_is_the_authority_when_the_early_check_misses_the_conflict` | Application | The handler surfaces the store's rejection instead of assuming its own check was decisive. |

The API-level tests assert the *outcome* and do not care which layer rejected a
given request; the Infrastructure-level test is the one that isolates the
constraint path.

### V4 — Persistence exception translation stops at Infrastructure

**Decision.** `TenantRepository` catches `DbUpdateException`, and when the inner
exception is a `PostgresException` with SQLSTATE `23505` **and** constraint name
`ix_tenants_slug`, it throws the Application's `TenantSlugAlreadyExistsException`
with the provider exception attached as the inner exception.

**Why match the constraint name as well as the SQLSTATE.** A future unique index
on `tenants` must not be silently reported as a slug conflict. Anything that does
not match both propagates as the unexpected error it is — a `500` with no detail —
because guessing would turn a real defect into a tidy `409`.

**What never crosses the boundary.** `DbUpdateException`, `PostgresException`,
SQLSTATE values, constraint names, table names and SQL text. Two tests assert
this rather than trusting it: one checks the exception message names none of
them, and the API tests scan raw response bodies for `ix_tenants_slug`, `23505`,
`duplicate key`, `Npgsql`, `INSERT INTO`, `stackTrace` and a stack-frame prefix.

**The rejected aggregate is detached** after a failed insert, so the unit of work
is not left holding a row it would retry on the next save.

### V5 — Error strategy: one new type, and a nullable result

**Decision.** The smallest set that covers the four required outcomes:

| Outcome | Mechanism | Layer that owns it |
| --- | --- | --- |
| Success | The return value (`TenantView`) | Application |
| Domain validation failure | `DomainValidationException` | Domain (already existed) |
| Tenant slug conflict | `TenantSlugAlreadyExistsException` | Application (**the one new type**) |
| Tenant not found | `TenantView?` — `null` | Application |

**Why not a `Result<T>`.** No third-party result library, and no hand-rolled
generic result framework. Domain validation already arrives as an exception, so a
result type would mean the API needed both mechanisms anyway — strictly more
machinery for strictly no gain. This remains the position recorded in
[README.md](README.md) decision 9, and the trigger for revisiting it is unchanged:
a use case that must report several validation failures at once.

**Why not-found is `null` rather than an exception.** "No such tenant" is an
ordinary answer to a lookup. Making it an exception would spend a stack unwind on
a normal outcome and invite an error-level log for a request that worked. There is
deliberately no `TenantNotFoundException`: one focused exception type, not a
catalogue.

**Why an empty GUID is `400` and not `404`.** `TenantId.From(Guid.Empty)` throws
`DomainValidationException`. An all-zero identifier is malformed input, not a
tenant that happens not to exist, and answering `404` would tell a caller their
request was well-formed when it was not.

**Transport neutrality.** Neither the Domain nor the Application names a status
code, a header, `ProblemDetails` or a content type. `TenantSlugAlreadyExistsException`
carries the rejected slug so the API can name it without re-parsing a message.

### V6 — API: Minimal APIs, two endpoints

**Decision.** Focused endpoint mapping in `TenantEndpoints.MapTenantEndpoints`,
matching the style Phase 1 established for `/health`. A route group carries the
`/api/tenants` prefix.

| Method | Route | Success | Notes |
| --- | --- | --- | --- |
| `POST` | `/api/tenants` | `201 Created` | `Location` header, created representation in the body |
| `GET` | `/api/tenants/{tenantId:guid}` | `200 OK` | `404` as Problem Details when absent |

**Why no controller.** Controllers are not established here, and mixing two API
styles in one application means two places to look for routing, model binding and
filters. No fat controller exists because no controller exists.

**Each endpoint does three things** — translate the request, call one handler,
translate the outcome. No business logic, no database access, and no `try`/`catch`:
error mapping lives in one place (V8).

**`Location` is generated from the route name** (`CreatedAtRoute`), not built by
string concatenation, so the header and the route cannot drift apart. A test
follows the header and asserts it resolves to `200`.

### V7 — DTOs, and why there are two of them

**Decision.** Three explicit types, and the Domain aggregate is never serialised.

| Type | Layer | Shape |
| --- | --- | --- |
| `CreateTenantRequest` | API | `{ name, slug }` |
| `TenantView` | Application | `Id`, `Name`, `Slug`, `Status` (Domain enum) |
| `TenantResponse` | API | `{ id, name, slug, status }` with `status` as a name |

**Why not one DTO.** `Status` has to appear on the wire as a name, and choosing
that representation is a transport decision. Keeping it in the API type means the
Application layer never holds an opinion about JSON, and the published contract
cannot drift because someone edited a read model. Four fields each; the cost is
one mapping method.

**No invented fields.** No created or updated timestamp, no plan, no
subscription, no owner, no user, no channel and no AI configuration. The
aggregate owns none of those, and publishing a field with nothing behind it is a
promise the API cannot keep.

**The caller cannot choose `id` or `status`** — the request type has no such
properties, and a test posts both anyway and asserts they are ignored.

### V8 — Problem Details mapping lives in exactly one place

**Decision.** `TenantProblemDetailsHandler`, an `IExceptionHandler`, plugged into
the pipeline Phase 1 already established (`AddProblemDetails` +
`UseExceptionHandler` + `UseStatusCodePages`).

| Failure | Status | Title |
| --- | --- | --- |
| `DomainValidationException` | `400` | Invalid tenant details |
| `TenantSlugAlreadyExistsException` | `409` | Tenant slug already exists |
| `BadHttpRequestException` (unreadable body) | its own status, `400` in practice | Malformed request |
| Tenant not found | `404`, returned directly by the endpoint | Tenant not found |
| Anything else | `500` via the default handler | — |

All error responses carry `application/problem+json`, asserted on the response
header rather than assumed.

**Why a central handler rather than per-endpoint `catch` blocks.** The mapping is
a single decision about the whole API. Repeating it per endpoint is how two
endpoints end up disagreeing about what a conflict is.

**Unrecognised exceptions return `false`** and keep their existing behaviour: a
`500` whose body carries no message, no type name and no stack trace.

**`BadHttpRequestException` was a real defect found by a test.** Without that arm,
a malformed JSON body returned `500`, because `UseExceptionHandler` intercepts the
exception and the default handler does not read the status it already carries. The
detail is a fixed sentence rather than the framework's parser message, which can
quote the offending payload back at the caller.

**`Detail` is the exception's own message for the two business cases.** That is a
bounded decision, not an oversight: both types are raised only by the Domain value
objects and the tenant use case, their messages describe a business rule and echo
caller-supplied input, and neither can carry a database identifier, SQL or a
credential. No other exception's message reaches a client.

### V9 — Status serialisation is written out, not inferred

**Decision.** `TenantResponse.From` maps `TenantStatus` to a string with an
explicit `switch`, whose default arm throws.

**Why not `ToString()` or `JsonStringEnumConverter`.** Both make the published
contract a side effect — of an enum member's spelling, or of a global serialiser
setting. Renaming a member or changing an option would silently change what every
client receives. Here the mapping *is* the contract, and adding a status without
deciding its public name fails loudly instead of leaking an integer ordinal.

**Enforcement.** A test asserts the raw JSON contains `"status":"Active"` and
does *not* contain `"status":1`, because a deserialised value would hide exactly
the difference that matters.

### V10 — Dependency injection: explicit, and split by layer

**Decision.** Two registration extensions and one line in `Program.cs`.

- `InfrastructureServiceCollectionExtensions.AddOmniAiPersistence(connectionString)`
  registers `OmniAiDbContext` and `ITenantRepository → TenantRepository`. It
  lives beside the implementations, so the composition root need not know that
  persistence is EF Core or which type satisfies the contract. It takes the
  connection string as a **parameter** rather than reading `IConfiguration`,
  because deciding where configuration comes from is the composition root's job.
- `CompositionRoot.AddOmniAi(configuration)` reads the connection string,
  calls the above, registers the two handlers as their concrete types, and
  registers the exception handler.

**Why concrete handler registrations.** Nothing substitutes them at runtime; an
interface per handler would be ceremony with exactly one implementation each.
Scoped, so a handler shares the request's `DbContext` and therefore its unit of
work.

**No assembly scanning.** Two registrations do not justify a convention-based
scanner, and an explicit list is the one form of this code that cannot silently
register — or silently miss — a type.

**The connection string is blank in `appsettings.json`** and supplied by the
environment. A real connection string in source control is a committed
credential; a plausible-looking default is worse, because a misconfigured
deployment would quietly connect somewhere. A blank string configures the
provider without one, so the process still starts and `/health` still answers,
while the first database call fails with EF Core's own explicit error.

### V11 — Validation ownership

**Decision.** The Domain is authoritative for `name` and `slug`. The transport
layer validates only that the request body is readable JSON of the expected
shape.

**Forbids.** FluentValidation (not added), and `[Required]`/`[StringLength]`
attributes on `CreateTenantRequest`. Any of those would be a second copy of a rule
the value objects already own, and two copies of a rule drift.

**No silent repair.** An over-long name is rejected, never truncated — asserted
by a test that also confirms no row was written. A slug is never derived from the
name: guessing one would mint an identifier the caller did not ask for and may not
be able to change later. The caller supplies both values explicitly.

### V12 — Logging

**Decision.** Expected outcomes — validation failures, conflicts, not-found — are
logged at **Information**, via a source-generated `[LoggerMessage]`.

**Why.** They are the API working correctly. Logging them at error level would
fill an alert channel with other people's typing mistakes and bury real faults.
Source-generated rather than a `LogInformation` call so the template is compiled,
nothing is boxed into a `params` array, and arguments are not assembled when the
level is disabled — which is also what the `CA1848`/`CA1873` analyzers require,
and they were satisfied on their merits rather than suppressed.

**Never logged.** Connection strings, request secrets, PostgreSQL exception
detail as a message to the client, or credentials. Unexpected exceptions keep the
existing pipeline's behaviour. No observability platform was added.

---

## Security posture

**Authentication and authorisation are not implemented.** These endpoints are
therefore development-stage functionality:

- Anyone able to reach the process can create a tenant.
- There is no caller identity, so nothing binds a tenant to an owner.
- There is no tenant request context, so the isolation invariant in
  [multi-tenancy-isolation.md](multi-tenancy-isolation.md) is unchanged: an
  unscoped tenant-owned query remains *incorrect*, not *impossible*.

**No placeholder control was added** — no hard-coded API key, no fake admin
token, no temporary bearer secret. A fake control would hide the gap rather than
close it, and would very likely outlive the phase that was supposed to replace it.

**Production exposure is not authorised until the authentication/authorisation
phase is complete. Phase 5 does not authorise deployment.**

---

## DEFERRED

Not implemented, and not described anywhere as if it were:

| Area | Deferred |
| --- | --- |
| Identity | Authentication, authorisation, platform admin roles, tenant ownership by users, invitations, tenant selection |
| Tenancy | Current-tenant request context, tenant-host/subdomain routing, query scoping, row level security |
| Tenant operations | Update, delete, suspend and activate endpoints; list, search and pagination |
| Commerce | Billing, subscriptions, plans, quotas, usage metering |
| Frontend | Onboarding UI, forms, admin screens, API client |
| Platform | AI providers, RAG, embeddings, channels, webhooks, deployment |

`Tenant.Rename`, `Tenant.ChangeSlug`, `Tenant.Suspend` and `Tenant.Activate` exist
on the accepted aggregate and are **deliberately unreachable over HTTP**: this
phase authorised two use cases, and exposing more would be scope the review did
not approve.

---

## Things a reviewer can check independently

1. `OmniAI.Application` references no EF Core, Npgsql or ASP.NET Core assembly —
   asserted by two tests reading the compiled assembly's metadata.
2. No Domain file changed: `git diff` against the Phase 4 checkpoint is empty for
   `backend/src/OmniAI.Domain` and `backend/tests/OmniAI.Domain.Tests`.
3. `ITenantRepository` exposes no `IQueryable`, `DbSet` or `DbContext`, and has
   three members.
4. There is no `IUnitOfWork`, no `IRepository<T>` and no MediatR package.
5. Eight concurrent inserts of one slug produce exactly one row, with the advisory
   check bypassed.
6. Eight concurrent `POST`s of one slug produce exactly one `201`, no `500`, and
   one row.
7. No response body contains `ix_tenants_slug`, `23505`, `Npgsql`,
   `DbUpdateException`, SQL text or a stack trace.
8. `"status"` is `"Active"` in the raw JSON, never `1`.
9. `appsettings.json` contains no connection string value.
10. The API integration tests run against a database named `omniai_test_*`, which
    one test asserts by reading the resolved connection string out of the hosted
    application.

## Phase 37.5a: renaming the workspace (owner decision D-3)

Visitors now see the workspace's name on the public chat reads, so its owner must
control it.

- `GET /api/tenants/{id}` sends a strong `ETag` (the row's `xmin`, as for agents and
  roles). The body is unchanged.
- `PUT /api/tenants/{id}/name` `{ name }` with `If-Match`: `428` without it, `412`
  when stale, `200` with the workspace and a new `ETag` on success. The name follows
  the creation rules (required, at most 200 characters after trimming, no control
  characters); a refused one is `400 workspace.name.invalid`. Guarded by
  `workspace.manage` (`tenant-rbac.md`): `403` for a member without it, `404` for
  anybody else, checked before the workspace is read.
- Renaming to the current name changes nothing and records nothing.
- Each real rename writes one `workspace.renamed` security event (actor, workspace,
  time; never the names) in the same transaction as the rename.
- The hosted and embedded chat reads show the new name on their next read.

## Phase 37.6: the workspace time zone (row 34)

The days a workspace counts in, so the Gulf (UTC+3) no longer reads yesterday's
evening as today.

- `Tenant.TimeZone` is an IANA name (`WorkspaceTimeZone`), stored in
  `tenants.time_zone varchar(64) NOT NULL DEFAULT 'UTC'` (migration
  `AddWorkspaceTimeZone`, M7). The default is a model default, so every existing
  workspace reads `UTC`, exactly the zone it was already counted in; no data step.
- `GET /api/tenants/{id}` (and the create and rename responses) gain `timeZone`.
  `GET /api/me/tenants` is unchanged.
- `PUT /api/tenants/{id}/time-zone` `{ timeZone }` with `If-Match`, guarded by
  `workspace.manage`, in the rename's order: the name is checked against the
  runtime's zone database first (the same `400 workspace.time_zone.invalid` for
  anybody), then membership (`404`) and the permission (`403`), then PostgreSQL's
  own `pg_timezone_names` (same code), then the version (`428` / `412`). `200` with
  the workspace and a new `ETag`. Choosing the current zone writes nothing.
- **Exact spelling only.** The runtime and PostgreSQL both accept a name case-
  insensitively in places; this platform stores one spelling, the database's own,
  so every reader, renderer and client is given the same string.
- **Not a security event.** Nothing about access or identity changes, so no
  `security_events` row is written.
- **Reading never fails because of a zone.** A stored name is read without looking
  its rules up; the rules are resolved when a day is first computed. A name the
  runtime no longer knows then fails loudly rather than being counted in UTC.
- What follows the zone and what does not is described in
  `analytics-usage-foundation.md` §4 and `reports-exports-scheduling.md` §9 and §11.
  The plan quota's months stay in UTC (`workspace-plans-entitlements-quotas.md`).

## Phase 38.7b: how many schedules a zone change moves (BA-9)

The settings page asks before a zone change that moves report schedules. Until
now it learnt whether there were any by reading the schedule list, which needs
`reports.view`, so a manager without it was always asked.

- **Where:** `GET /api/tenants/{id}` carries `scheduleCount` (an integer) for a
  caller who holds `workspace.manage`, the one permission that may change the
  zone. Chosen over a dry run of `PUT …/time-zone` because the page already reads
  the workspace for its `ETag` before it saves: no extra round trip, and no write
  route that sometimes writes nothing.
- **Absent, not zero, for everybody else**, and on every other response that
  carries the workspace (create, rename, the zone change itself). A member
  without `workspace.manage` learns nothing about schedules from it, even one who
  holds `reports.view`. A non-member gets the read's ordinary `404`.
- **What it counts** is exactly what `ChangeTimeZoneAsync` locks and re-times:
  this workspace's schedules, enabled or not, whose next run is still to come.
  One already due keeps its time and is not counted.
  (`ITenantRepository.CountSchedulesToRezoneAsync`, the same predicate; a test
  counts, changes the zone, and checks that exactly the counted ones moved.)
- **One read of the caller's permissions** answers both questions the read asks
  (may they see the workspace; may they change its zone). The count is one
  statement, by workspace (`ix_report_schedules_tenant_name` leads with it), only
  for a manager.
- **The `ETag` is unchanged in meaning:** it still names the workspace row a
  rename or a zone change must match. Adding or editing a schedule changes the
  count and not the tag, so a client re-reads the workspace before it asks.
