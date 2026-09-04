# Phase 5 — Tenant Onboarding Application + API Vertical Slice

**Date:** 2026-09-04
**Scope:** Two use cases end to end — Create Tenant and Get Tenant By Id — through
API, Application, Domain, Infrastructure and PostgreSQL. No other tenant
operation, no authentication, no AI, no channels, no frontend.

---

## 1. Phase objective

Implement the first complete production-quality vertical slice through every
layer, and establish the shape later slices follow: explicit use-case handlers, a
minimum persistence contract, a race-safe duplicate-slug outcome, RFC 9457
Problem Details mapping, and integration tests over HTTP against real PostgreSQL.

## 2. Final status

**PASS**

Every authorised objective is implemented and verified end to end against real
PostgreSQL 17.6, and for the first time since Phase 1 **the entire suite executed
on this machine: 289 tests defined, 289 passing, 0 failing, 0 skipped.**

Two things worth stating plainly:

1. **The Smart App Control blocker cleared itself.** `OmniAI.Domain.Tests` --
   blocked from loading in Phases 1 through 4 — now runs and passes 172/172,
   repeatably across four consecutive runs. Smart App Control is **still
   enabled** and was **not** modified (§32). Its verdicts are per file hash, and
   this assembly's hash is now permitted where it previously was not. So the
   constraint is not fixed, only currently absent, and a future rebuild could be
   blocked again.
2. **A malformed request body returned `500` instead of `400`.** Found by
   `A_malformed_body_is_rejected_at_the_transport_layer`, diagnosed and fixed --
   `UseExceptionHandler` intercepts `BadHttpRequestException` and the default
   handler does not read the status the exception already carries (§19).

Nothing on the exclusion list was implemented. **Deployment is not authorised**
(§40).

## 3. Baseline commit

```
38f7e48  feat: establish postgresql persistence foundation
```

The accepted Phase 4 checkpoint, verified as `HEAD` before any file was touched.
Phases 1–4 were preserved; no accepted decision was reverted.

## 4. Initial Git state

```
$ git status --short          # (no output — clean working tree)
$ git log -4 --oneline
38f7e48 feat: establish postgresql persistence foundation
e714005 feat: establish conversation and message domain
478dd2b feat: establish tenant core domain
b7ab2ea chore: establish omnichannel ai platform foundation
```

Clean, as required. Before writing anything, the following were read: the
`Tenant` aggregate, `TenantId`, `TenantName`, `TenantSlug`, `TenantStatus`,
`DomainValidationException`, `DomainStateException`, `OmniAiDbContext`,
`TenantConfiguration` (including `ix_tenants_slug`), `Program.cs` and its
existing Problem Details pipeline, the current DI structure, the API integration
test architecture, and the Phase 4 Infrastructure test architecture.

Conventions found and followed: Minimal APIs (no controllers exist), `.slnx`,
Central Package Management, `Subject_scenario_expectation` test names, shared
test files via `<Compile Include="..\Shared\…" Link="…" />`, and
`AddProblemDetails` + `UseExceptionHandler` + `UseStatusCodePages` already wired.

## 5. Application types introduced

| Type | Kind | Purpose |
| --- | --- | --- |
| `CreateTenantCommand` | `record` | Raw `Name` and `Slug` as supplied. No id, no status. |
| `CreateTenantHandler` | class | The create use case. |
| `GetTenantByIdQuery` | `record` | Raw `Guid`, still untrusted at this point. |
| `GetTenantByIdHandler` | class | The read use case. |
| `TenantView` | `record` | Read model: `Id`, `Name`, `Slug`, `Status`. |
| `TenantSlugAlreadyExistsException` | exception | The one error type this phase adds. |

**No mediator, no command bus, no query bus, no pipeline behaviour, no
reflection-based discovery.** MediatR was not added; it is explicitly not
approved. With two use cases a dispatcher would replace a one-hop call graph with
an indirection, a convention and a runtime failure mode. Handlers are focused
(one public method each), constructor-injected, and cancellation-token aware —
`A_cancelled_token_is_honoured` asserts the token reaches the store rather than
being accepted and ignored.

## 6. Persistence contracts introduced

One interface, `ITenantRepository`, in
`OmniAI.Application/Tenants/Persistence/`, with three members:

```csharp
Task<Tenant?> FindByIdAsync(TenantId id, CancellationToken ct = default);
Task<bool>    SlugExistsAsync(TenantSlug slug, CancellationToken ct = default);
Task          AddAsync(Tenant tenant, CancellationToken ct = default);
```

**Not created:** `IRepository<T>`, `GenericRepository<T>`, `RepositoryBase<T>`, a
specification framework, or any member exposing `IQueryable`, `DbSet` or
`DbContext`.

**No `IUnitOfWork`, as a decision.** Both use cases touch one aggregate, and
`SaveChanges` is already a transaction, so a unit of work today would be a second
abstraction that forwards a call — and would still need reworking when real
multi-aggregate transactions arrive, because their boundaries are not yet known.
`AddAsync` therefore commits and the contract says so;
`Add_commits_so_a_separate_connection_sees_the_row` verifies the claim from
outside EF Core. The revisit trigger is one use case writing two aggregates
atomically.

`SlugExistsAsync` is documented in the contract as **advisory only**, with the
reason: its answer is stale the instant it is read.

## 7. Infrastructure implementations

| Type | Notes |
| --- | --- |
| `TenantRepository` | `internal`, over `OmniAiDbContext`. Where PostgreSQL stops. |
| `InfrastructureServiceCollectionExtensions.AddOmniAiPersistence` | Registers the context and the repository. |

`TenantRepository` is deliberately `internal` — nothing outside Infrastructure
should name it, only the contract it satisfies. Its own tests reach it through
`<InternalsVisibleTo Include="OmniAI.Infrastructure.IntegrationTests" />` rather
than by making the type public to the whole solution.

`AddOmniAiPersistence` takes the connection string as a **parameter** instead of
reading `IConfiguration`, so choosing the configuration source stays the
composition root's job and this project gains no configuration dependency.

## 8. Create Tenant flow

```
POST /api/tenants  { name, slug }
  │
  ├─ Minimal API binds CreateTenantRequest        (unreadable body → 400, §19)
  ├─ CreateTenantHandler
  │    ├─ TenantName.Create(name)                 (Domain rule → 400 on failure)
  │    ├─ TenantSlug.Create(slug)                 (Domain rule → 400 on failure)
  │    ├─ SlugExistsAsync(slug)                   ADVISORY early failure → 409
  │    ├─ Tenant.Create(name, slug)               id generated, status Active
  │    └─ AddAsync(tenant)
  │         └─ INSERT … → ix_tenants_slug         AUTHORITATIVE → 409
  └─ 201 Created + Location + TenantResponse
```

Creation yields a `TenantId` generated by `TenantId.New()` (UUIDv7) inside the
aggregate, the Domain-canonical name and slug, and `TenantStatus.Active` — all
three chosen by the Domain, none of them by the caller. The command carries no
`id` and no `status` field, and `The_caller_cannot_choose_the_id_or_the_status`
posts both anyway and asserts they are ignored.

## 9. Get Tenant flow

```
GET /api/tenants/{tenantId:guid}
  │
  ├─ Route constraint rejects non-GUID segments   (no route match → 404)
  ├─ GetTenantByIdHandler
  │    ├─ TenantId.From(guid)                     (Guid.Empty → 400, §11)
  │    └─ FindByIdAsync(id)
  └─ 200 + TenantResponse    |    null → 404 Problem Details
```

The response exposes only `id`, `name`, `slug` and `status`. No field was
invented.

## 10. Domain validation mapping

The Domain remains authoritative for `name` and `slug`, and its rules are not
duplicated anywhere. `CreateTenantRequest` carries **no** validation attributes;
FluentValidation was **not** added.

| Rule | Owner |
| --- | --- |
| Name required, trimmed, ≤ 200 chars, no control characters | `TenantName` |
| Slug required, 3–63 chars, lowercase DNS-label grammar, no leading/trailing/double hyphen | `TenantSlug` |
| Identifier not empty | `TenantId.From` |
| Body is readable JSON of the expected shape | Transport (§19) |

`DomainValidationException` → **400** with `application/problem+json`. Verified
for a null, empty and whitespace name, an over-long name, and nine invalid slug
shapes.

**No silent repair.** An over-long name is rejected, not truncated —
`An_over_long_name_is_rejected_rather_than_truncated` also asserts no row was
written. A slug is never derived from the name; the caller supplies both.

## 11. Not-found strategy

`GetTenantByIdHandler` returns `TenantView?`, and `null` means not found. There is
deliberately **no `TenantNotFoundException`**: absence is an ordinary answer to a
lookup, and making it an exception would spend a stack unwind on a normal outcome
and invite an error-level log for a request that worked. The endpoint maps `null`
to `Results.Problem(status: 404)`, so the body is Problem Details rather than
empty.

An empty GUID is **400, not 404** — `TenantId.From(Guid.Empty)` throws, because
an all-zero identifier is malformed input rather than a tenant that happens not
to exist. Answering 404 would tell a caller their request was well-formed when it
was not.

## 12. Duplicate slug strategy

Two layers with an explicit hierarchy:

| Layer | Role | Authority |
| --- | --- | --- |
| `SlugExistsAsync` in the handler | Early, friendly failure for the common case | **Advisory. Never authoritative.** |
| `ix_tenants_slug` unique index | Rejects the insert | **Authoritative** |

The advisory check cannot be more than advisory because its answer is stale the
instant it is read; treating it as authoritative *is* the bug, and it is one that
only appears under load.

**No lock of any kind** — no table lock, no advisory lock, no serialisable
transaction, no application mutex. Each would tax every onboarding to tidy up a
rare race the database already resolves, and a mutex would additionally be wrong
the moment a second instance runs, which for a multi-tenant SaaS is the expected
deployment.

Case is not a loophole: the Domain lowercases the slug, so `ACME` and `acme`
cannot become two tenants — asserted at both the Application and API levels.

## 13. Concurrent duplicate slug behavior

| Test | Level | Result |
| --- | --- | --- |
| `Two_concurrent_duplicate_creations_produce_exactly_one_201_and_one_409` | API, over HTTP | exactly one `201`, exactly one `409`, no `500`, one row |
| `Eight_concurrent_duplicate_creations_still_produce_exactly_one_tenant` | API, over HTTP | one `201`, seven `409`, no `500`, one row |
| `Concurrent_inserts_of_one_slug_produce_exactly_one_tenant` | Infrastructure, real PostgreSQL | eight writers, eight connections, **advisory check bypassed**; one row |
| `The_store_is_the_authority_when_the_early_check_misses_the_conflict` | Application | handler surfaces the store's rejection rather than trusting its own check |

**How the claim is split honestly.** The API tests assert the *outcome* and do not
care which layer rejected a given request — with two requests, one may legitimately
complete before the other starts, and the assertion holds either way. The
Infrastructure test is the one that isolates the constraint path: it calls
`AddAsync` directly, so the advisory check plays no part and only the unique index
can be rejecting the losers. The eight-caller API test exists so that several
requests reliably pass the advisory check before any commits.

**No global application lock is used to make any of these pass**, and no test
serialises the requests it claims are concurrent.

## 14. Database unique constraint role

`ix_tenants_slug` — created by the Phase 4 `InitialPersistence` migration and
unchanged by this phase — is the final authority on slug uniqueness. It is the
only mechanism that is correct across concurrent requests, across multiple
application instances, and against any future writer that does not go through
`CreateTenantHandler`.

Its role was already recorded in Phase 2 (`TenantSlug` cannot know what is
stored; uniqueness is a persistence guarantee) and Phase 4 (`D3 is now
enforced`). Phase 5 is where an application path finally depends on it, and the
tests above are the first proof it behaves that way under contention.

## 15. PostgreSQL exception translation strategy

`TenantRepository.AddAsync` catches `DbUpdateException` and translates **only**
when the inner exception is a `PostgresException` whose `SqlState` is `23505`
(`PostgresErrorCodes.UniqueViolation`) **and** whose `ConstraintName` is
`ix_tenants_slug`.

**Both conditions, on purpose.** A future unique index on `tenants` must not be
silently reported as a slug conflict. Anything not matching both propagates as
the unexpected error it is — a `500` with no detail — because guessing would turn
a real defect into a tidy `409`.

The result is `TenantSlugAlreadyExistsException`, carrying the rejected slug and
the provider exception as `InnerException` (for logs only, never for a response
body). The rejected aggregate is then **detached**, so the unit of work is not
left holding a row it would retry on the next save.

**What never crosses the boundary:** `DbUpdateException`, `PostgresException`,
SQLSTATE values, constraint names, table names, SQL text. Two tests assert this
rather than trusting it —
`The_conflict_message_discloses_no_database_internals`, and an API-level scan of
raw response bodies for `ix_tenants_slug`, `23505`, `duplicate key`, `Npgsql`,
`PostgresException`, `DbUpdateException`, `INSERT INTO`, `SELECT `, `stackTrace`
and a stack-frame prefix.

## 16. API endpoints

| Method | Route | Success | Errors |
| --- | --- | --- | --- |
| `POST` | `/api/tenants` | `201 Created` + `Location` | `400`, `409` |
| `GET` | `/api/tenants/{tenantId:guid}` | `200 OK` | `400` (empty GUID), `404` |

Minimal APIs with a route group, matching the Phase 1 style for `/health`. **No
controller was introduced**, so there is no fat controller; mixing two API styles
would mean two places to look for routing, binding and filters. Each endpoint
translates the request, calls one handler, translates the outcome — no business
logic, no database access, no `try`/`catch`.

`Location` is generated with `CreatedAtRoute` from the read endpoint's route name,
not concatenated, so the header and the route cannot drift.
`Creating_a_tenant_sets_a_location_header_pointing_at_the_read_endpoint` follows
it and asserts it resolves to `200`.

No `PUT`, `PATCH`, `DELETE`, suspend, activate, list, search or pagination
endpoint exists.

## 17. Request DTOs

`CreateTenantRequest(string? Name, string? Slug)` — nullable strings, no
validation attributes (§10), no `id`, no `status`.

`GET` takes `Guid tenantId` from the route with a `:guid` constraint, so a
non-GUID segment never reaches the handler.

## 18. Response DTOs

`TenantResponse(Guid Id, string Name, string Slug, string Status)`.

**Why two DTOs** — `TenantView` in Application and `TenantResponse` in API:
`Status` must appear on the wire as a name, and choosing that representation is a
transport decision. Keeping it in the API type means the Application layer never
holds an opinion about JSON and the published contract cannot drift because
someone edited a read model. Four fields each; the cost is one mapping method.

The Domain aggregate is never serialised, and no persistence entity is exposed.
No timestamp, plan, subscription, owner, user, channel or AI field was added — the
aggregate owns none of them.

```jsonc
// 201 Created
{ "id": "01a06c92-…", "name": "Acme Corporation", "slug": "acme", "status": "Active" }
```

## 19. ProblemDetails mapping

`TenantProblemDetailsHandler`, an `IExceptionHandler`, is the single place a
business outcome becomes a status code. It plugs into the pipeline Phase 1
established; that behaviour is preserved, not replaced.

| Failure | Status | Title |
| --- | --- | --- |
| `DomainValidationException` | `400` | Invalid tenant details |
| `TenantSlugAlreadyExistsException` | `409` | Tenant slug already exists |
| `BadHttpRequestException` | its own status (`400` in practice) | Malformed request |
| Tenant not found | `404` (returned by the endpoint) | Tenant not found |
| Anything else | `500` via the default handler | — |

All error responses are `application/problem+json`, asserted on the response
header. Unrecognised exceptions return `false`, keeping the existing behaviour: a
`500` whose body carries no message, no type name and no stack trace.

**The `BadHttpRequestException` arm is a defect fix, not a nicety.** Without it a
malformed body returned `500`, because `UseExceptionHandler` intercepts the
exception and the default handler does not read the status it already carries. The
detail is a fixed sentence rather than the framework's parser message, which can
quote the offending payload back at the caller.

`Detail` is the exception's own message for the two business cases — a bounded
decision: both types are raised only by the Domain value objects and the tenant
use case, their messages describe a business rule and echo caller-supplied input,
and neither can carry a database identifier, SQL or a credential. No other
exception's message reaches a client.

No HTTP concept was placed in the Domain, and no Npgsql concept in the
Application (§32, enforced by tests).

## 20. Status serialization

`TenantResponse.From` maps `TenantStatus` with an explicit `switch` whose default
arm throws.

Neither `ToString()` nor `JsonStringEnumConverter` is used: both make the
published contract a side effect — of an enum member's spelling, or of a global
serialiser setting — so renaming a member or changing an option would silently
change what every client receives. Here the mapping *is* the contract, and adding
a status without deciding its public name fails loudly instead of leaking an
integer ordinal.

`The_status_is_a_name_on_the_wire_never_an_enum_ordinal` asserts the **raw JSON**
contains `"status":"Active"` and does not contain `"status":1`, because a
deserialised value would hide exactly the difference that matters.

## 21. Dependency injection changes

| Registration | Where | Lifetime |
| --- | --- | --- |
| `OmniAiDbContext` | `AddOmniAiPersistence` (Infrastructure) | Scoped (`AddDbContext`) |
| `ITenantRepository → TenantRepository` | `AddOmniAiPersistence` | Scoped |
| `CreateTenantHandler` | `CompositionRoot.AddOmniAi` (Api) | Scoped |
| `GetTenantByIdHandler` | `CompositionRoot.AddOmniAi` | Scoped |
| `TenantProblemDetailsHandler` | `CompositionRoot.AddOmniAi` | via `AddExceptionHandler` |

`Program.cs` grew by one call, `builder.Services.AddOmniAi(builder.Configuration)`,
plus `app.MapTenantEndpoints()` — so it stays a readable list of pipeline steps
rather than gaining a line per use case.

Handlers are registered as concrete types because nothing substitutes them at
runtime; an interface per handler would be ceremony with one implementation each.
Scoped, so a handler shares the request's `DbContext` and therefore its unit of
work.

**No assembly scanning.** Two registrations do not justify a convention-based
scanner, and an explicit list is the one form of this code that cannot silently
register — or silently miss — a type.

**The connection string is blank in `appsettings.json`** and supplied by the
environment. A real one in source control is a committed credential; a
plausible-looking default is worse, because a misconfigured deployment would
quietly connect somewhere. A blank value configures the provider without a
connection string, so the process still starts and `/health` still answers, while
the first database call fails with EF Core's own explicit error.

## 22. Application tests

**30 new tests** in `OmniAI.Application.Tests` (31 in the project, including the
Phase 1 guard):

| Class | Tests | Coverage |
| --- | --- | --- |
| `CreateTenantHandlerTests` | 21 | Valid creation; name normalised by the Domain; slug canonicalised by the Domain; initial status `Active`; generated non-empty and distinct `TenantId`; blank name rejected (3 cases); invalid slug rejected (6 cases); validation precedes any store access; taken slug is a conflict carrying the slug; conflict detected case-insensitively; **the store is the authority when the advisory check misses**; null command; missing repository; cancellation token honoured |
| `GetTenantByIdHandlerTests` | 7 | Existing tenant returned; suspended status reported; unknown tenant → `null`; another tenant's id does not match; empty GUID rejected as invalid rather than missing; null query; missing repository |
| `ApplicationDependencyTests` | 3 | 1 pre-existing (no Infrastructure/Api reference) + **2 new**: no EF Core or Npgsql reference, no ASP.NET Core reference |

**No mocking framework was added.** `InMemoryTenantRepository` is a hand-written
fake, shorter than the setup a framework would need, and able to express the one
thing that matters — that the conflict is raised by the *store* — without a
configuration DSL. Its `PretendSlugIsFree` switch is what lets a test drive the
race the advisory check cannot catch.

**The Domain is never faked.** `Tenant`, `TenantName` and `TenantSlug` are the
real types throughout, so these tests exercise the real invariants.

The two new dependency guards are the enforcement behind §6's boundary claim: the
layer purity is a failing test, not a convention.

## 23. API integration tests

**32 new tests** in `TenantEndpointsTests` (33 in the project, with the Phase 1
health test), all over HTTP against real PostgreSQL:

| Group | Tests | Coverage |
| --- | --- | --- |
| Create | 5 | `201` with correct body; `Location` header present, correct and resolvable; row present in PostgreSQL read directly; Domain-canonical name and slug in both response and row; caller cannot choose `id` or `status` |
| Validation | 15 | Invalid name → `400` + `application/problem+json` (3 cases); over-long name rejected and no row written; invalid slug → `400` + problem+json (9 cases); malformed body → `400`; validation failure leaks no internals |
| Conflict | 4 | Sequential duplicate → `201` then `409` + problem+json, no leaked detail, exactly one row; case-only duplicate → `409`; **two concurrent → exactly one `201`, one `409`**; **eight concurrent → one `201`, seven `409`** |
| Get | 5 | `200` with correct payload; status is a name not an ordinal; unknown → `404` + problem+json with no leaked detail; empty GUID → `400`; non-GUID does not match the route |
| Round trip | 2 | `POST` → read `Location` → `GET` → `200` → identical tenant, field for field; two tenants with different slugs both created and readable |
| Harness | 1 | The hosted application really resolved the disposable test database |

Assertions avoid implementation-specific exception text: they check status codes,
the `Content-Type`, the Problem Details `status` and `title` fields, the stored
row, and the absence of internals — not exact message strings.

The harness test exists because every persistence assertion above would be
meaningless if the connection string were not reaching the application; it reads
the resolved connection string out of the hosted app's own service provider and
asserts it names an `omniai_test_*` database.

## 24. PostgreSQL test strategy

**Real PostgreSQL 17.6 for both suites. No EF Core in-memory provider anywhere** —
it has no unique index, so a duplicate slug would simply succeed and the most
important test in this phase would pass while proving nothing.

The Phase 4 strategy is reused rather than reimplemented.
`PostgresTestDatabase` was **relocated** from
`OmniAI.Infrastructure.IntegrationTests/Persistence/` to `backend/tests/Shared/`
and is now compiled into both test projects, following the convention already
used for `AssemblyReferenceReader`. Behaviour is unchanged: a randomly named
database per test class, created by applying the **real migration**, dropped
afterwards with `WITH (FORCE)`. The only signature change is
`CreateContext(params IInterceptor[])` in place of a `RecordedSql`-typed
parameter, so `RecordedSql` could stay in the Infrastructure test project (now
its own file). A second copy of "create a database, migrate it, drop it" is
exactly the kind of duplicate that drifts until two suites test different
schemas.

The API tests host the real application with `WebApplicationFactory<Program>` and
supply the connection string as **configuration**
(`ConnectionStrings:OmniAiDatabase`), the same way a deployment would — so the
composition root under test is the real one and **no service registration is
replaced**.

The server is located through `OMNIAI_TEST_POSTGRES`; if it is absent the fixture
throws with an actionable message rather than falling back to a fake. No
credential is compiled in or committed. Nothing depends on the VPS, on production
credentials or on production data.

**Docker is still unavailable** (the daemon needs elevation), so **Testcontainers
was again not added** — an untested dependency would be worse than none. The
disposable local cluster from Phase 4 was reused: official PostgreSQL 17.6
binaries, a data directory outside the repository, loopback-only, holding no real
data. The env-var contract keeps Testcontainers a drop-in change for CI.

## 25. Backend test totals before Phase 5

| Project | Tests |
| --- | --- |
| `OmniAI.Domain.Tests` | 172 |
| `OmniAI.Application.Tests` | 1 |
| `OmniAI.Infrastructure.IntegrationTests` | 45 |
| `OmniAI.Api.IntegrationTests` | 1 |
| **Total defined** | **219** |

## 26. Backend tests defined after Phase 5

| Project | Before | After | New |
| --- | --- | --- | --- |
| `OmniAI.Domain.Tests` | 172 | 172 | 0 *(unchanged, and now executing — §32)* |
| `OmniAI.Application.Tests` | 1 | **31** | +30 |
| `OmniAI.Infrastructure.IntegrationTests` | 45 | **53** | +8 |
| `OmniAI.Api.IntegrationTests` | 1 | **33** | +32 |
| **Total defined** | **219** | **289** | **+70** |

## 27. Tests executable in current environment

**All 289 are executable, and all 289 pass. 0 failing, 0 skipped.**

| Project | Passed | Failed | Skipped |
| --- | --- | --- | --- |
| `OmniAI.Domain.Tests` | **172** | 0 | 0 |
| `OmniAI.Application.Tests` | 31 | 0 | 0 |
| `OmniAI.Infrastructure.IntegrationTests` | 53 | 0 | 0 |
| `OmniAI.Api.IntegrationTests` | 33 | 0 | 0 |
| **Total** | **289** | **0** | **0** |

This is the first phase in which nothing was unexecutable. The 172 Domain tests
had been blocked by Smart App Control since Phase 1 and now load and pass; see
§32 for what changed and why it should not be relied on.

The three Phase 5-relevant suites were each run **three times consecutively**
with identical results, and the Domain suite four times — repetition matters here
because two of the new tests are genuinely concurrent.

## 28. Restore result

`dotnet restore OmniAI.slnx` — success, all 8 projects, no warnings, no new
package to restore (§33).

## 29. Release build result

`dotnet build OmniAI.slnx -c Release`:

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

With `TreatWarningsAsErrors`, analyzers at `latest-recommended` and
`EnforceCodeStyleInBuild`, across all 8 projects.

## 30. Test result

289 passed, 0 failed, 0 skipped, across all four test projects; stable over
three consecutive runs (four for the Domain suite). Details in §27.

## 31. Vulnerability scan

`dotnet list package --vulnerable --include-transitive` — **no vulnerable
packages in any of the 8 projects.**

## 32. Compiler/analyzer warnings

**Zero.** Two analyzer diagnostics were raised during the phase and both were
fixed on their merits, not suppressed:

1. **CA1848 + CA1873** on a `LogInformation` call — replaced with a
   source-generated `[LoggerMessage]` method, so the template is compiled,
   nothing is boxed into a `params` array, and arguments are not assembled when
   the level is disabled. The analyzers were right.
2. Two compile errors from missing `using` directives, fixed by adding them.

**The Smart App Control blocker is gone — and it was not fixed by anything I
did.** In Phases 1 through 4 the test host failed with:

```
System.IO.FileLoadException: Could not load file or assembly
'...\OmniAI.Domain.Tests.dll'. An Application Control policy has blocked
this file. (0x800711C7)
```

In this phase the same assembly loads and passes 172/172, four runs in a row.
What is true about that:

- **Smart App Control is still on.** `VerifiedAndReputablePolicyState` is `1`,
  i.e. enforcing. It was **not** disabled, and no Windows security policy,
  registry value or code-integrity setting was changed by me — that was
  prohibited and I did not attempt it.
- **The verdict is per file hash.** Smart App Control evaluates each file against
  a reputation service and caches the result, so a hash that was previously
  refused can later be permitted without the policy changing. That is the
  behaviour consistent with what happened here.
- **Therefore this is not a fix.** The constraint is currently absent, not
  removed. A rebuild produces a new hash and could be blocked again, and nothing
  in this repository influences the outcome.

**A Linux CI environment therefore remains mandatory before any production
release.** A suite that runs because a reputation service currently says yes is
not a suite you can gate a release on.

## 33. Packages added, exact versions/reasons

**None.** No NuGet package was added in this phase — no MediatR, no
FluentValidation, no mocking framework, no Testcontainers, no result library, no
observability package. `Directory.Packages.props` is unmodified.

Everything used was already present: `Microsoft.AspNetCore.Mvc.Testing` (Phase 1)
hosts the API in the tests, and `Microsoft.EntityFrameworkCore` /
`Npgsql.EntityFrameworkCore.PostgreSQL` (Phase 4) back the repository.
`IExceptionHandler`, `IProblemDetailsService`, `[LoggerMessage]` and
`AddDbContext` all ship in the framework or in packages already referenced.

## 34. Domain modified

**NO.** Verified two ways: `git diff --stat HEAD -- backend/src/OmniAI.Domain
backend/tests/OmniAI.Domain.Tests` is empty, and `git status --short` for those
paths lists nothing. Both are **byte-identical to the accepted Phase 4
checkpoint**.

The accepted `Tenant` model already supported both use cases, so nothing needed
justifying. No public setter was added, no invariant weakened, no persistence or
HTTP attribute applied, and `Tenant.Create` still owns identity generation and
the initial `Active` status. `Rename`, `ChangeSlug`, `Suspend` and `Activate`
exist on the aggregate and are deliberately unreachable over HTTP — this phase
authorised two use cases.

This also establishes that the blocked Domain tests are not a Phase 5 regression:
the code they cover has not changed since it passed 172/172.

## 35. Infrastructure modified

**YES**, additively.

Added: `Persistence/Repositories/TenantRepository.cs`,
`DependencyInjection/InfrastructureServiceCollectionExtensions.cs`.
Modified: `OmniAI.Infrastructure.csproj` — one `InternalsVisibleTo` entry, with a
comment explaining why the repository stays internal.

**No Phase 4 persistence decision was changed.** `OmniAiDbContext`, all three
entity configurations, `PersistenceSchema`, `PersistenceConverters`,
`ConversationLoading`, the design-time factory and the migration are all
untouched. The message ingestion-order mechanism, the `xmin` concurrency tokens
and `ix_tenants_slug` are exactly as accepted.

## 36. Application modified

**YES.** Previously it contained only `AssemblyMarker`. It now holds the two use
cases, the read model, the repository contract and one exception (§5, §6).

`OmniAI.Application.csproj` is **unmodified** — no package reference was added, so
the project still depends on nothing but Domain. That is asserted by the two new
guard tests rather than left to inspection.

## 37. API modified

**YES.** Added `Tenants/CreateTenantRequest.cs`, `Tenants/TenantResponse.cs`,
`Tenants/TenantEndpoints.cs`, `ErrorHandling/TenantProblemDetailsHandler.cs`,
`Composition/CompositionRoot.cs`. Modified `Program.cs` (two calls plus a comment
recording that the endpoints are not authorised for production) and
`appsettings.json` (a blank `ConnectionStrings:OmniAiDatabase` key).
`OmniAI.Api.csproj` is unmodified.

The `/health` endpoint and the existing Problem Details pipeline are unchanged,
and the Phase 1 health test still passes untouched.

## 38. Frontend modified

**NO.** `git status --short -- frontend/` returns nothing — zero files added,
changed or deleted. No npm command was run. No tenant UI, form, admin screen or
API client was created.

## 39. Architectural decisions

Recorded in full in
[tenant-onboarding-vertical-slice.md](../architecture/tenant-onboarding-vertical-slice.md)
as V1–V12. In brief:

| # | Decision |
| --- | --- |
| V1 | Explicit handlers; no mediator, bus, pipeline or reflection discovery |
| V2 | A three-member `ITenantRepository`; **no** `IUnitOfWork`, and why |
| V3 | Advisory pre-check, authoritative unique index, no locks |
| V4 | Provider exception translation stops at Infrastructure |
| V5 | One new Application exception; not-found as a nullable result; no result framework |
| V6 | Minimal APIs, two endpoints, no controller |
| V7 | Three explicit DTOs; the aggregate is never serialised |
| V8 | One central Problem Details mapping |
| V9 | Status serialisation written out, not inferred |
| V10 | Explicit DI split by layer; no assembly scanning |
| V11 | The Domain owns field validation; transport owns body syntax |
| V12 | Expected outcomes logged at Information, source-generated |

The architecture index and its multi-tenancy status were updated so that neither
overstates what Phase 5 delivered.

## 40. Security/deployment warning

> **These endpoints are NOT authorised for public production exposure until
> authentication and authorisation are implemented. Phase 5 does not authorise
> deployment.**

- **Unauthenticated.** Anyone able to reach the process can create a tenant.
- **No caller identity**, so nothing binds a tenant to an owner.
- **No tenant request context**, so the isolation invariant is unchanged: an
  unscoped tenant-owned query is still *incorrect*, not *impossible*.
- **No placeholder control was added** — no hard-coded API key, no fake admin
  token, no temporary bearer secret. A fake control would hide the gap rather
  than close it, and would likely outlive the phase meant to replace it.

The warning is recorded in three places that a developer actually reads: the
`Program.cs` comment beside `MapTenantEndpoints`, the `TenantEndpoints` class
documentation, and the architecture document.

No authentication, authorisation, JWT, API key or session handling was
implemented. Nothing was deployed.

## 41. Deferred concerns

Nothing on the exclusion list was implemented. Specifically deferred and
documented as such: authentication; authorisation; platform admin roles; tenant
ownership by users; tenant invitations; tenant selection; current-tenant request
context; tenant-host/subdomain routing; update, delete, suspend and activate
endpoints; list, search and pagination; billing and subscriptions; frontend
onboarding; RLS adoption; query scoping; adversarial cross-tenant isolation
tests; automatic concurrency retry; provider idempotency; Testcontainers as the
CI database strategy.

Also absent: OpenAI, Anthropic, `IAiProvider`, AI agents, RAG, embeddings, vector
search, tool calling, WhatsApp, Meta APIs, Instagram, Messenger, website chat,
Angular changes, deployment changes.

## 42. Deviations

1. **`PostgresTestDatabase` was relocated** from the Infrastructure test project
   to `backend/tests/Shared/` so both test suites compile one copy (§24). The
   brief said to reuse the Phase 4 strategy where appropriate; sharing the file
   is how that is done without duplicating provisioning logic. Behaviour is
   unchanged and all 45 Phase 4 tests still pass.
2. **`InternalsVisibleTo` was added to `OmniAI.Infrastructure.csproj`** so the
   repository could stay `internal` while its own tests reach it. The
   alternative — making the type public — would have widened its visibility to
   the whole solution for a test's benefit.
3. **A `BadHttpRequestException` arm was added to the Problem Details handler**
   beyond the four mappings the brief listed. Without it a malformed body
   returned `500`, which contradicts the brief's own requirement that a malformed
   request body be handled at the transport level (§19).
4. **Two guard tests were added to `ApplicationDependencyTests`**, which the
   brief did not ask for. They make the "no EF leak into Application" and "no
   HTTP leak into Application" checks executable rather than one-time
   inspections.
5. **One stale sentence was corrected in `docs/architecture/persistence-postgresql.md`.**
   Its "Running the tests" section stated that this machine cannot execute
   `OmniAI.Domain.Tests`. That is no longer true (§32), and leaving a false claim
   in an accepted document would mislead the next reader. The correction is
   confined to that paragraph; no Phase 4 decision was altered.

No other deviation. No Domain change, no frontend change, no new package, no
commit.

## 43. Known issues

1. **The Domain suite's executability is not under our control.** It passes here
   now, but Smart App Control is still enforcing and its verdict is per file
   hash (§32), so a rebuild could be refused again at any point. The durable
   answers are unchanged: sign the test assemblies, or run the suite in CI on a
   host without an application-control policy. Nothing in the repository can
   guarantee it locally.
2. **The advisory `SlugExistsAsync` costs one extra query per creation.** A
   deliberate trade for a clear failure in the common case; the correctness does
   not depend on it. It could be dropped without changing any outcome.
3. **A conflicting creation consumes nothing but a rejected insert** — there is
   no automatic retry, by instruction. A caller receiving `409` must choose
   another slug; a retry policy is an Application concern for a later phase.
4. **`Detail` echoes caller-supplied input** for the two business cases (a
   rejected name or slug appears in the message). Bounded and reviewed (§19), but
   worth revisiting if these endpoints ever become publicly reachable, since it
   confirms which slugs are taken.
5. **The connection string is blank by default**, so a misconfigured deployment
   fails at the first database call rather than at startup. Chosen over a
   fabricated default; a startup health check would be the better answer once
   readiness checks exist.
6. **Tenancy invariant 1 is still unsatisfied**, and Phase 5 does not claim
   otherwise.
7. Earlier phases' open items are unchanged: no frontend linter, no CI pipeline,
   no toolchain pins, `InvariantGlobalization` revisit, and the migration `xmin`
   hand-edit.

## 44. Blockers

**None.**

The Phase 1–4 blocker did not apply this time: the full **289-test suite is green
on this machine**, including 85 tests that exercise real PostgreSQL (53
Infrastructure plus the 32 tenant API tests; the Phase 1 health test needs no
database). Smart App Control is nonetheless still enforcing and I did not change
it, so that outcome is not something to depend on (§32, §43.1) — which is a
standing risk, not a blocker on Phase 5.

Nothing blocks progress to Phase 6. Authentication is the natural next phase, and
until it exists these endpoints must not be exposed.

## 45. `git status --short`

```
 M backend/src/OmniAI.Api/Program.cs
 M backend/src/OmniAI.Api/appsettings.json
 M backend/src/OmniAI.Infrastructure/OmniAI.Infrastructure.csproj
 M backend/tests/OmniAI.Api.IntegrationTests/OmniAI.Api.IntegrationTests.csproj
 M backend/tests/OmniAI.Application.Tests/ApplicationDependencyTests.cs
 M backend/tests/OmniAI.Infrastructure.IntegrationTests/OmniAI.Infrastructure.IntegrationTests.csproj
 M backend/tests/OmniAI.Infrastructure.IntegrationTests/Persistence/ConcurrencyTests.cs
 M backend/tests/OmniAI.Infrastructure.IntegrationTests/Persistence/ConversationPersistenceTests.cs
 M backend/tests/OmniAI.Infrastructure.IntegrationTests/Persistence/MessagePersistenceTests.cs
 M backend/tests/OmniAI.Infrastructure.IntegrationTests/Persistence/MigrationSchemaTests.cs
 D backend/tests/OmniAI.Infrastructure.IntegrationTests/Persistence/PostgresTestDatabase.cs
 M backend/tests/OmniAI.Infrastructure.IntegrationTests/Persistence/TenantPersistenceTests.cs
 M docs/architecture/README.md
 M docs/architecture/persistence-postgresql.md
?? backend/src/OmniAI.Api/Composition/
?? backend/src/OmniAI.Api/ErrorHandling/
?? backend/src/OmniAI.Api/Tenants/
?? backend/src/OmniAI.Application/Tenants/
?? backend/src/OmniAI.Infrastructure/DependencyInjection/
?? backend/src/OmniAI.Infrastructure/Persistence/Repositories/
?? backend/tests/OmniAI.Api.IntegrationTests/Tenants/
?? backend/tests/OmniAI.Application.Tests/Tenants/
?? backend/tests/OmniAI.Infrastructure.IntegrationTests/Persistence/RecordedSql.cs
?? backend/tests/OmniAI.Infrastructure.IntegrationTests/Persistence/TenantRepositoryTests.cs
?? backend/tests/Shared/PostgresTestDatabase.cs
?? docs/architecture/tenant-onboarding-vertical-slice.md
?? docs/phase-reports/PHASE_05_TENANT_ONBOARDING_VERTICAL_SLICE_REPORT.md
```

Git collapses untracked directories. Expanded with `--untracked-files=all`, the
change set is **24 new files, 13 modified, 1 deleted** -- the deletion being
`PostgresTestDatabase.cs` moving to `Shared/`, so it is a relocation rather than
a removal.

Where the 24 new files are:

| Location | Files |
| --- | --- |
| `OmniAI.Application/Tenants/` | 6 -- command, its handler, query, its handler, `TenantView`, the exception |
| `OmniAI.Application/Tenants/Persistence/` | 1 -- `ITenantRepository` |
| `OmniAI.Infrastructure/` | 2 -- `TenantRepository`, the DI extension |
| `OmniAI.Api/` | 5 -- two DTOs, endpoints, the Problem Details handler, `CompositionRoot` |
| `OmniAI.Application.Tests/Tenants/` | 3 -- two test classes and the fake repository |
| `OmniAI.Infrastructure.IntegrationTests/Persistence/` | 2 -- `TenantRepositoryTests`, `RecordedSql` |
| `OmniAI.Api.IntegrationTests/Tenants/` | 2 -- the fixture and the endpoint tests |
| `tests/Shared/` | 1 -- the relocated `PostgresTestDatabase` |
| `docs/` | 2 -- the architecture document and this report |

**No commit was created, nothing was staged, and no remote exists.**
`git diff --check` reports no whitespace errors.

## 46. `git diff --stat`

```
 backend/src/OmniAI.Api/Program.cs                  |  16 ++
 backend/src/OmniAI.Api/appsettings.json            |   3 +
 .../OmniAI.Infrastructure.csproj                   |  10 ++
 .../OmniAI.Api.IntegrationTests.csproj             |   9 +
 .../ApplicationDependencyTests.cs                  |  43 +++++
 .../OmniAI.Infrastructure.IntegrationTests.csproj  |   1 +
 .../Persistence/ConcurrencyTests.cs                |   1 +
 .../Persistence/ConversationPersistenceTests.cs    |   1 +
 .../Persistence/MessagePersistenceTests.cs         |   1 +
 .../Persistence/MigrationSchemaTests.cs            |   1 +
 .../Persistence/PostgresTestDatabase.cs            | 190 ---------------------
 .../Persistence/TenantPersistenceTests.cs          |   1 +
 docs/architecture/README.md                        |  26 ++-
 docs/architecture/persistence-postgresql.md        |  15 +-
 14 files changed, 115 insertions(+), 203 deletions(-)
```

`git diff` covers tracked files only; every other Phase 5 file is new and
therefore untracked. The full diff was read:

- The five one-line changes to Phase 4 test files are each a single
  `using OmniAI.Testing;` added for the relocated fixture.
- The 190-line deletion is that fixture moving to `Shared/`, with one signature
  change (§24).
- `Program.cs` gained two calls and the comment recording that the endpoints are
  not authorised for production.
- `appsettings.json` gained a blank connection-string key.
- The two `.csproj` changes are one `InternalsVisibleTo` entry and two
  `<Compile Include>` entries for the shared fixture. **No package reference was
  added** (§33).
- The two documentation changes update the architecture index and correct one
  stale sentence (§42.5).

Diff review found no accidental change, no generated build output, no database
data, no secret, no package scope creep, no EF Core or Npgsql leak into
Application, no HTTP concept in Domain, no frontend change, and no generic
repository or mediator framework.

## 47. Secret review confirmation

**Confirmed — no secrets.**

- **Content scan** of every Phase 5 file for passwords, `pwd=`, API keys,
  secrets, credentials, bearer values, `sk-`-style keys and the VPS address:
  matches only in this report's own prose and in doc comments stating that no
  credential belongs in source.
- **`appsettings.json` carries an empty connection string value** and
  `appsettings.Development.json` carries none. No connection string with
  credentials exists anywhere in `backend/src` or `backend/tests`.
- **No hard-coded API key, admin token or bearer secret** was introduced — see
  §40.
- **The test connection string was supplied only through the
  `OMNIAI_TEST_POSTGRES` environment variable** in the shell and was never
  written to any file. The local cluster's data directory and password file live
  outside the repository; neither is tracked, and no `postmaster.pid`, data file
  or machine-specific cluster artefact is in the working tree.
- **Path scan:** nothing under `node_modules`, `bin/`, `obj/`, `dist/`,
  `.angular/` or `.vs/`; no `.env` file.

## 48. Confirmation VPS was NOT accessed

**Confirmed.** No SSH connection was attempted or made to `186.240.145.196` or
any other host. Nothing was deployed. No Nginx configuration, systemd service,
server directory, production database or production application was touched or
inspected. No SSH configuration or credential was read, written, printed, logged
or changed.

All work was local: the repository, and the disposable loopback-only PostgreSQL
cluster from Phase 4. Network access was limited to the local database.

## 49. Readiness recommendation for Phase 6

**Ready to proceed — with one thing that should come first.**

The slice is complete and the boundaries it establishes are enforced by tests
rather than by convention: Application holds no persistence or transport
dependency, the database is provably the authority on slug uniqueness under
concurrency, and no provider or framework detail reaches a client. The Domain was
untouched, which is the strongest evidence that the Phase 2 model was right — and
those 172 Domain tests executed for the first time since Phase 1, confirming it
directly rather than by inference from an unchanged diff.

Recommended for Phase 6, in this order:

1. **Authentication and authorisation.** It is the blocking dependency for
   everything else: these endpoints cannot be exposed without it, the tenant
   request context that tenancy invariant 1 needs depends on caller identity, and
   every later use case will be built on whatever shape it takes. Building
   another unauthenticated slice first would mean retrofitting identity into two
   slices instead of one.
2. **A Linux CI pipeline.** All 289 tests happened to run locally this time, but
   only because Smart App Control's per-hash verdict currently permits the
   Domain assembly (§32) — that could reverse on any rebuild, and it is not a
   property of this repository. CI on a host without an application-control
   policy is what makes a full-suite gate dependable, and it remains mandatory
   before any production release.
3. Then the tenant request context and query scoping, which together are what
   turn an unscoped tenant query from incorrect into impossible.
