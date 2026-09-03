# Phase 2 — Tenant Core Domain

**Date:** 2026-09-03
**Scope:** Core tenant domain model and its invariants. No persistence, no
authentication, no HTTP tenant resolution, no isolation mechanisms.

---

## 1. Phase objective

Establish the minimum robust tenant domain model needed to make the tenant a
first-class boundary: a strongly typed identity, a focused `Tenant` aggregate
with name, slug and status, enforced invariants, behaviour instead of setters,
focused unit tests, and a documented multi-tenancy isolation strategy.

## 2. Final status

**PASS**

Every authorised objective was completed. The build is clean with warnings as
errors, all 90 backend tests pass, no vulnerable packages are present, no NuGet
or npm dependency was added, and nothing outside the authorised scope was
implemented.

## 3. Baseline commit used

```
b7ab2ea  chore: establish omnichannel ai platform foundation
```

The accepted Phase 1 commit. No Phase 1 file was recreated, reverted or
restructured; the only pre-existing file touched is
`docs/architecture/README.md`, which was extended (§6).

## 4. Initial git state

Working tree **clean** before any change — `git status --short` produced no
output, `git log -1 --oneline` showed `b7ab2ea`, 1 commit total, 0 remotes.
Proceeding was therefore safe; no stop condition applied.

Before modifying anything, the following were inspected: `OmniAI.Domain` (only
`AssemblyMarker`), `OmniAI.Application` (only `AssemblyMarker`), the four test
projects, `Directory.Build.props`, `Directory.Packages.props`,
`backend/tests/.editorconfig` and `docs/architecture/README.md`.

Two baseline facts shaped the implementation:

- `TreatWarningsAsErrors` with analyzers at `latest-recommended` — new code had
  to be analyzer-clean from the start.
- `InvariantGlobalization` is `true` — all casing and comparison had to be
  explicitly invariant/ordinal, never culture-dependent.

## 5. Files created

| File | Purpose |
| --- | --- |
| `backend/src/OmniAI.Domain/DomainValidationException.cs` | The single domain rule-violation exception |
| `backend/src/OmniAI.Domain/Tenants/TenantId.cs` | Strongly typed tenant identity |
| `backend/src/OmniAI.Domain/Tenants/TenantName.cs` | Validated display name |
| `backend/src/OmniAI.Domain/Tenants/TenantSlug.cs` | Validated, canonicalised slug |
| `backend/src/OmniAI.Domain/Tenants/TenantStatus.cs` | `Active` / `Suspended` |
| `backend/src/OmniAI.Domain/Tenants/Tenant.cs` | The aggregate |
| `backend/tests/OmniAI.Domain.Tests/Tenants/TenantIdTests.cs` | 8 tests |
| `backend/tests/OmniAI.Domain.Tests/Tenants/TenantNameTests.cs` | 16 tests |
| `backend/tests/OmniAI.Domain.Tests/Tenants/TenantSlugTests.cs` | 36 tests |
| `backend/tests/OmniAI.Domain.Tests/Tenants/TenantTests.cs` | 25 tests |
| `docs/architecture/multi-tenancy-isolation.md` | Tenant isolation ADR |
| `docs/phase-reports/PHASE_02_TENANT_CORE_DOMAIN_REPORT.md` | This report |

## 6. Files modified

One file only:

| File | Change |
| --- | --- |
| `docs/architecture/README.md` | +54 / −2. Intro updated to say Phase 2 added the tenant domain; decision 4 given a Status paragraph pointing at the new ADR; decisions 9 (error strategy) and 10 (value objects) appended. |

No source file, project file, build property, test infrastructure file or
frontend file from Phase 1 was modified.

## 7. Domain types introduced

All in `OmniAI.Domain.Tenants`, except the exception which is in `OmniAI.Domain`:

| Type | Kind | Notes |
| --- | --- | --- |
| `TenantId` | `readonly record struct` | Wraps `Guid`; UUIDv7 for new values |
| `TenantName` | `sealed record` | Wraps `string` |
| `TenantSlug` | `sealed record` | Wraps `string`, canonical lowercase |
| `TenantStatus` | `enum` | `Active = 1`, `Suspended = 2` |
| `Tenant` | `sealed class` | The aggregate |
| `DomainValidationException` | `sealed class : Exception` | Domain-wide |

Six types, no interfaces, no base classes, no generic infrastructure. No
`Entity`/`AggregateRoot`/`ValueObject` base types were introduced: with one
aggregate there is nothing to factor out, and inventing a domain framework now
would be a guess about what later aggregates need.

## 8. TenantId design

A `readonly record struct` wrapping `Guid`, with a private constructor and two
factories:

- `TenantId.New()` — generates a value using **`Guid.CreateVersion7()`**. UUIDv7
  is time-ordered, so generated keys insert locally in a B-tree instead of
  scattering across it the way UUIDv4 does. Available in the BCL since .NET 9, so
  no package was added for ID generation.
- `TenantId.From(Guid)` — wraps an existing value, throwing
  `DomainValidationException` on `Guid.Empty`.

Properties: immutable, value equality (record), `IsEmpty`, and
`ToString()` returning the plain `"D"` GUID form with
`CultureInfo.InvariantCulture`. No static mutable state; no infrastructure
dependency; no third-party strongly-typed-ID package.

**Why a distinct type rather than `Guid`:** tenant identity *is* the isolation
boundary. As `Guid`, any other identifier — a user id, a conversation id, an id
off a request body — can be passed where a tenant id is expected and the compiler
will not object. A distinct type converts a class of isolation bugs into compile
errors.

**Known limitation, stated plainly.** The brief asked that `TenantId` "cannot
represent `Guid.Empty`" *and* that it be "a small immutable value type". These
conflict: C# always permits `default(T)` for a struct, and that bypasses every
constructor. No public construction path can produce an empty id, but
`default(TenantId)` holds `Guid.Empty` and no language feature prevents it.

The trade-off was resolved in favour of the value type, because it is the more
specific instruction and the standard choice for an identifier that will appear
on every tenant-owned record. The hole is mitigated and made visible rather than
hidden: `IsEmpty` exists so any boundary accepting an identity from outside the
Domain can reject it explicitly, and a test asserts the default instance reports
itself empty. Using a `sealed record` class would close the hole exactly but
allocate per identity and reintroduce null as the failure mode. This is recorded
in decision 10 of the architecture README and in §29.

## 9. Tenant aggregate design

```csharp
public sealed class Tenant
{
    public TenantId     Id     { get; }               // fixed for life
    public TenantName   Name   { get; private set; }
    public TenantSlug   Slug   { get; private set; }
    public TenantStatus Status { get; private set; }

    public static Tenant Create(TenantName name, TenantSlug slug);
    public void Rename(TenantName name);
    public void ChangeSlug(TenantSlug slug);
    public void Suspend();
    public void Activate();
}
```

- `sealed`, private constructor, no public setters. State changes only through
  the four named operations, so an invariant cannot be bypassed by assignment.
- `Create` generates the identity and starts the tenant `Active`. Onboarding
  gates belong to the use case that admits a tenant, not to the tenant's shape.
- `Id` has no setter at all — identity is immutable for the aggregate's lifetime.
- Operations take value objects, not raw strings, so an invalid value cannot
  reach the aggregate: it is rejected while constructing the value object.
- Four properties and four operations. No billing, subscription, plan, quota,
  branding, address, localisation, CRM, user list, AI or channel configuration —
  each of those is a separate concern that will own its own types.

Deliberately **not** added: `Reconstitute`/rehydration factory (speculative until
persistence exists), domain events (nothing consumes them yet), and any base
class.

## 10. Name invariants

`TenantName.Create(string?)`:

| Rule | Behaviour |
| --- | --- |
| Required | `null`, empty or whitespace-only → `DomainValidationException` |
| Trimmed | Surrounding whitespace removed |
| Bounded | Length > `MaxLength` (**200**) → rejected, **never truncated** |
| Printable | Any control character → rejected |
| Interior text preserved | Casing and interior spacing untouched |
| Unicode allowed | Non-Latin names accepted (e.g. Arabic) |

**Why 200:** measured in UTF-16 code units, matching `string.Length` and how a
database sizes an `nvarchar`. It comfortably fits a full legal or trading name —
including long non-Latin names and corporate suffixes — while staying well inside
the index key limits of every mainstream database. It bounds display, log and
storage size without being a limit a real business would reach.

Length is measured **after** trimming, so padding cannot push an otherwise valid
name over the limit. Invalid values are rejected, never silently repaired.
Control characters are excluded because they serve no purpose in a display name
and would corrupt logs, headers and rendered output.

## 11. Slug invariants

`TenantSlug.Create(string?)`:

| Rule | Behaviour |
| --- | --- |
| Required | `null`/empty/whitespace → rejected |
| Trimmed | Surrounding whitespace removed |
| Canonical lowercase | `ToLowerInvariant()` — never culture-dependent |
| Length | **3–63**, measured after trimming |
| Character set | `a–z`, `0–9`, `-` only |
| Leading hyphen | Rejected |
| Trailing hyphen | Rejected |
| Consecutive hyphens | Rejected |
| Spaces / underscores / dots / accents / non-ASCII | Rejected |

**Why 3–63:** the maximum is the 63-character limit of a single DNS label, so any
accepted slug can be used as a subdomain without further truncation — this keeps
subdomain-based tenant resolution open as an option without committing to it. The
minimum of 3 keeps slugs typeable and readable while reserving one- and
two-character values, which are the ones most likely to be wanted for
platform-level routes later.

**Normalisation vs repair.** Only two normalisations are applied — trim and
lowercase — because case is not meaningful in a slug and `Acme` and `acme` must
not become two different tenants. Nothing else is repaired: `"acme syria"` is
rejected rather than rewritten to `"acme-syria"`, so a mistyped slug cannot
silently become a different tenant's slug. A test asserts this explicitly.

**No regular expression.** The grammar is three character ranges plus two
positional rules, so a hand-written scan is clearer at a glance, allocates
nothing, and presents no backtracking surface.

**Uniqueness is explicitly not enforced here.** Two slugs with the same value are
equal, but the Domain cannot know whether a value is already taken — that
requires knowledge of every stored tenant. A static registry inside the Domain
would be process-local, wrong the moment a second instance runs, and actively
harmful because it would look authoritative. Uniqueness is a persistence
guarantee (unique index) surfaced through an Application use case that translates
the resulting conflict. Recorded as decision D3 in the isolation ADR.

Reserved-slug policy (`www`, `api`, `admin`, …) was **not** implemented: it is
routing policy, not a domain invariant, and depends on the routing decision
deferred as M4.

## 12. Status / lifecycle rules

`TenantStatus` has exactly two members, `Active = 1` and `Suspended = 2`.
`Deleted`, `Archived`, `Trial`, `PastDue` and `Cancelled` were not added — they
are billing and retention concerns whose rules do not exist yet.

There is **no zero member**, so an uninitialised value is detectable rather than
silently meaning "Active".

Transitions:

```
Create  ─────────────►  Active
Active  ── Suspend ──►  Suspended
Suspended ─ Activate ─► Active
```

**Repeated operations are idempotent** — `Suspend()` on an already suspended
tenant is a no-op, and likewise `Activate()`. This is the simpler behaviour the
brief asked for, and it is also the safer one: the caller's intent ("this tenant
must not be active") is already satisfied, and forcing callers to check current
state first would invite a check-then-act race without preventing anything. Both
behaviours are covered by tests.

No state machine abstraction was built for two states and two transitions.

## 13. Error / validation strategy

**Two tiers, deliberately separated:**

| Situation | Mechanism |
| --- | --- |
| Domain rule violated by an input value | `DomainValidationException` |
| Programming defect (null passed where a value object is required) | `ArgumentNullException` |

`DomainValidationException` is one sealed type with the three standard
constructors. It is used for *every* domain rule violation across the Domain
layer.

**Why this and not the alternatives:**

- **Not a third-party Result library** — forbidden by the brief, and it would put
  a package dependency in the Domain.
- **Not a hand-rolled `Result<T>` framework** — that is a substantial piece of
  infrastructure, and building it for a domain with one aggregate is exactly the
  speculative generality the project guardrails forbid.
- **Not a family of exception types** — a type per rule adds ceremony and carries
  no information the message does not.
- **Not bare BCL `ArgumentException`** — the BCL throws it for programmer errors
  too, so an outer layer could not reliably tell a client error from a bug. That
  distinction is the whole point.

The separation means a future API edge can map `DomainValidationException` → 400
and anything else → 500, with a single `catch` and no string matching.

Properties required by the brief: **deterministic** (same input always produces
the same outcome; no culture, clock or environment dependence),
**testable** (`Assert.Throws<DomainValidationException>`, used throughout), and
**no infrastructure leakage** (`Exception` is BCL; the Domain still references
only `System.*`, proven by the architecture guard which continues to pass).

**Acknowledged limitation:** exceptions report the first failure only, so a form
with three bad fields yields one error. That is acceptable now and is recorded in
architecture decision 9 as the trigger for revisiting: when a use case must
report several validation failures at once, an explicit result type earns its
cost.

## 14. Tests added

**85 new tests**, all in `OmniAI.Domain.Tests`, no mocking framework (there is
nothing to mock — the domain has no dependencies):

| Class | Tests | Coverage |
| --- | --- | --- |
| `TenantIdTests` | 8 | `From` keeps value; empty GUID rejected; `New` non-empty and distinct; equality, inequality, `GetHashCode`, `==`/`!=`; default instance reports empty; `ToString` format |
| `TenantNameTests` | 16 | Valid name; trimming; interior spacing/casing preserved; non-Latin names; 5 blank/null cases; at-limit and over-limit; length measured after trim; control character rejected; equality incl. trim-insensitive and case-*sensitive*; `ToString` |
| `TenantSlugTests` | 36 | 6 well-formed shapes; lowercasing; trimming; 3 blank cases; 4 leading/trailing hyphen cases; 4 consecutive-hyphen cases; 8 out-of-grammar character cases; below-min, at-min, at-max, over-max; length after trim; no-silent-repair; equality incl. case/padding insensitivity; `ToString` |
| `TenantTests` | 25 | Creation with name/slug; non-empty and distinct identity; starts Active; normalisation on create; null name/slug rejected; rename incl. invariance of other fields, null and invalid; `ChangeSlug` incl. canonicalisation, invariance, null and 4 invalid values; suspend; activate; both idempotency cases; status change leaves other fields intact; two identical tenants remain distinct entities |

Tests assert observable behaviour — the value produced, the exception raised, the
state after an operation — not internal structure. Several assert what *does not*
change (identity and unrelated fields survive an operation), which is where
aggregate bugs usually hide. There are no coverage-only tests.

## 15. Number of backend tests before Phase 2

**5** — `OmniAI.Domain.Tests` 2, `OmniAI.Application.Tests` 1,
`OmniAI.Infrastructure.IntegrationTests` 1, `OmniAI.Api.IntegrationTests` 1.

## 16. Number of backend tests after Phase 2

**90** — `OmniAI.Domain.Tests` **87** (2 pre-existing architecture guards + 85
new), `OmniAI.Application.Tests` 1, `OmniAI.Infrastructure.IntegrationTests` 1,
`OmniAI.Api.IntegrationTests` 1. All pass; none skipped.

## 17. Commands executed

Baseline: `git status --short`, `git log -1 --oneline`, `git rev-list --all
--count`, `git remote`.

Inspection: reads of the Domain/Application trees, `Directory.Build.props`,
`Directory.Packages.props`, `backend/tests/.editorconfig`,
`DomainDependencyTests.cs`, `docs/architecture/README.md`.

Validation: `dotnet build src/OmniAI.Domain/OmniAI.Domain.csproj -c Release`
(early analyzer check), `dotnet restore OmniAI.slnx`,
`dotnet build OmniAI.slnx -c Release`, `dotnet test OmniAI.slnx -c Release`,
`dotnet test … --filter` (per-class counts),
`dotnet list OmniAI.slnx package --vulnerable --include-transitive`.

Diff review: `git status --short`, `git diff --check`, `git diff --stat`,
`git diff -- docs/architecture/README.md`, plus a path scan and a content secret
scan over every Phase 2 file.

No VPS command, no SSH, no deployment command, no `git commit`, no `git push`, no
remote change.

## 18. Restore result

`dotnet restore OmniAI.slnx` — **success**, all 8 projects, no warnings, no
package changes ("All projects are up-to-date for restore").

## 19. Release build result

`dotnet build OmniAI.slnx -c Release` — **success**:

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

All 8 projects built, with `TreatWarningsAsErrors` and analyzers at
`latest-recommended` in force.

## 20. Test result

`dotnet test OmniAI.slnx -c Release` — **all pass**:

| Project | Passed | Failed | Skipped |
| --- | --- | --- | --- |
| `OmniAI.Domain.Tests` | 87 | 0 | 0 |
| `OmniAI.Application.Tests` | 1 | 0 | 0 |
| `OmniAI.Infrastructure.IntegrationTests` | 1 | 0 | 0 |
| `OmniAI.Api.IntegrationTests` | 1 | 0 | 0 |
| **Total** | **90** | **0** | **0** |

The Phase 1 architecture guard asserting that Domain references nothing but
`System.*` still passes with the new types in place — the Domain remains
dependency-free.

## 21. Vulnerability scan result

`dotnet list OmniAI.slnx package --vulnerable --include-transitive` — **no
vulnerable packages** in any of the 8 projects. Unchanged from Phase 1, as
expected: no dependency was added or changed.

## 22. Compiler / analyzer warnings

**Zero.** No warning was produced, so none was suppressed. No
`#pragma warning disable`, no `[SuppressMessage]`, and no change to
`Directory.Build.props` or any `.editorconfig`. The Phase 1 scoped CA1707
exception for test projects was already in place and was not widened.

Three analyzer constraints shaped the code rather than being suppressed:

- `CA1305` — `ToString` on `Guid` uses an explicit `CultureInfo.InvariantCulture`.
- `CA1065` (do not throw from property getters) — ruled out making `TenantId.Value`
  throw on the default instance; `IsEmpty` is the non-throwing alternative.
- `CA1032` — `DomainValidationException` provides the three standard constructors.

## 23. NuGet packages added

**None.** `Directory.Packages.props` is byte-for-byte unchanged.

`Guid.CreateVersion7()` is in the BCL as of .NET 9, so UUIDv7 generation needed no
package — the brief's instruction not to add a dependency solely for ID
generation was satisfied without compromise.

## 24. Confirmation of whether frontend was modified

**The frontend was not modified.** `git status --short -- frontend/` returns
nothing: zero files added, changed or deleted under `frontend/`.

Phase 2 touched only the Domain layer and documentation, which the Angular
application does not consume — there is still no API surface beyond `/health`, and
no client contract changed. Frontend validation was therefore **not** re-run, and
Angular was not modified to manufacture Phase 2 activity.

## 25. Multi-tenancy security ADR summary

Created: `docs/architecture/multi-tenancy-isolation.md`, linked from
`docs/architecture/README.md`. It separates what is settled from what is not.

**The non-negotiable invariant:** no request, query, job or export may ever
return, modify, delete or reveal the existence of another tenant's data. Two
consequences are stated explicitly — a cross-tenant read is a data breach, not a
cosmetic bug; and absence of evidence is not isolation, so the design must make
the unsafe thing *awkward to write*, not merely undone. Enumeration and existence
are in scope: cross-tenant access must be indistinguishable from absence, or the
error code itself leaks which identifiers are real.

**Decisions already made:** tenant as a first-class domain concept (D1);
`TenantId` never a bare `Guid` (D2); slug uniqueness explicitly *not* a domain
guarantee (D3); an explicit `TenantId` column on every tenant-owned record, never
inferred from a join path (D4); defence in depth, with at least three independent
layers required to fail before a leak escapes (D5); tenant context explicit and
request-scoped, never ambient-mutable, because ambient context leaks across async
continuations and pooled threads (D6); uniqueness scoped `(TenantId, …)` for
tenant-owned data and global for the slug (D7).

**Eight binding security invariants** constrain whatever mechanism is eventually
chosen — including that an unscoped tenant query must be impossible or loud;
writes are checked at save time rather than assumed correct; `TenantId` is never
taken from a client-controlled field; background jobs, exports and retries carry
tenant context explicitly; and diagnostics carry the tenant id but never another
tenant's data.

**A testing obligation** is recorded: when persistence arrives, isolation gets
adversarial integration tests proving tenant A cannot read, update, delete,
enumerate or detect tenant B's records — through list endpoints, filters, sorts,
aggregates, search, exports and direct-id access. These are part of the
mechanism's definition of done, not a follow-up task.

## 26. Deferred tenant isolation mechanisms

None of these was implemented; all are open questions for the persistence phase:

| # | Deferred | Why not decided now |
| --- | --- | --- |
| M1 | Isolation model — shared schema + discriminator, schema-per-tenant, or database-per-tenant | Depends on tenant count, noisy-neighbour tolerance, per-tenant restore needs and operating cost |
| M2 | **PostgreSQL Row Level Security** | Strong candidate, because it is enforced by the database rather than by application code — but must be weighed against pooling implications, migration/admin paths and ORM interaction. **Under evaluation, explicitly not adopted.** |
| M3 | ORM global query filters | Effective but bypassable via raw SQL and disable-able per query; viable as *a* layer, not *the* layer |
| M4 | HTTP tenant resolution — subdomain, path segment or token claim | Belongs with authentication; the slug grammar keeps the subdomain option open |
| M5 | Save-time cross-tenant write interception | Mechanism depends on M1 and M3; the requirement is already fixed as invariant 2 |
| M6 | Whether a suspended tenant is blocked at authentication, authorisation or per operation | Needs the real rules for what a suspended tenant may still do |

Also not implemented, per the brief: EF Core, `DbContext`, migrations,
repositories, PostgreSQL, Redis, RabbitMQ, MassTransit, Hangfire, OpenAI,
Anthropic, AI abstractions, RAG, vector databases, conversations, messages, AI
agents, customers, contacts, any channel, authentication, authorisation, JWT,
tenant middleware, tenant claims, billing, subscriptions, quotas, reporting, CRM,
ERP, deployment changes.

## 27. Architectural decisions made

Recorded as decisions 9 and 10 in `docs/architecture/README.md`, plus D1–D7 in
the isolation ADR. In summary:

1. **One exception type for domain rule violations**, BCL argument exceptions
   reserved for programming defects (§13).
2. **Value objects over primitives** for identity and constrained strings — a
   security property for `TenantId`, not a stylistic one (§8).
3. **UUIDv7 for generated identities**, from the BCL, for index locality.
4. **Idempotent status operations**, chosen as both the simpler and the safer
   behaviour (§12).
5. **Slug grammar = DNS label grammar**, keeping subdomain routing open at no
   cost.
6. **Normalise, never repair** — trim and lowercase only, so mistakes surface.
7. **Uniqueness is a persistence guarantee**, never faked in the Domain (§11).
8. **No domain framework** — no `Entity`, `AggregateRoot`, `ValueObject` base
   types, and no domain events, until a second aggregate shows what is actually
   common.
9. **No Application-layer types were added.** The brief authorised them only if
   genuinely required, and nothing in this phase required one. No
   `TenantService`, `TenantManager`, `ITenantRepository`, `CreateTenantCommand`
   or `UpdateTenantCommand` exists. `OmniAI.Application` is unchanged.

## 28. Deviations from the prompt

**One**, and it is a resolved conflict between two instructions rather than a
departure from either:

- The brief required `TenantId` to be "a small immutable value type" *and* that
  it "cannot represent `Guid.Empty`". A C# struct can always be produced via
  `default`, which bypasses every constructor and yields `Guid.Empty`; the two
  requirements cannot both hold literally. I implemented the value type — the
  more specific instruction, and the right choice for an identifier that will sit
  on every tenant-owned record — and made the residual hole visible rather than
  silent: no public factory can produce an empty id, `IsEmpty` exposes the
  uninitialised state, a test asserts it, and the trade-off is documented in
  architecture decision 10 and §8 above. The alternative (a sealed record class)
  would satisfy the letter of the second requirement while allocating per
  identity and replacing the hole with `null`.

No other deviation. In particular: no Application types, no third-party
packages, no infrastructure, no frontend change, no commit.

## 29. Known issues

1. **`default(TenantId)` is representable** and holds `Guid.Empty` (§8, §28).
   Mitigated by `IsEmpty` plus explicit rejection at future boundaries; not
   closable in C#.
2. **Exceptions report one failure at a time**, so multi-field validation yields
   the first error only (§13). Acceptable now; architecture decision 9 records
   the trigger for revisiting.
3. **Slug uniqueness is unenforced** until persistence exists (§11). This is
   correct and deliberate, but it does mean nothing today prevents two `Tenant`
   instances with the same slug in memory.
4. **`TenantName.MaxLength` counts UTF-16 code units**, not grapheme clusters, so
   a name using surrogate pairs or combining marks counts higher than its
   perceived length. This matches `string.Length` and database `nvarchar` sizing;
   at a 200 limit it is a non-issue in practice, but it is the definition in use.
5. **No reserved-slug policy** (`www`, `api`, `admin`). Deferred with the routing
   decision (M4); worth adding before slugs become host names.
6. Phase 1 items still open and unchanged: no frontend linter, no CI pipeline, no
   toolchain pins, `InvariantGlobalization` revisit, and the Smart App Control
   constraint on reflective assembly loading.

## 30. Blockers

**None.** Every authorised Phase 2 objective was completed and validated. Nothing
is waiting on a decision to proceed to Phase 3.

## 31. `git status --short`

```
 M docs/architecture/README.md
?? backend/src/OmniAI.Domain/DomainValidationException.cs
?? backend/src/OmniAI.Domain/Tenants/
?? backend/tests/OmniAI.Domain.Tests/Tenants/
?? docs/architecture/multi-tenancy-isolation.md
```

Plus this report, added afterwards under `docs/phase-reports/`. Untracked
directories are collapsed by Git; expanded, the new files are the 12 listed in
§5. No commit was created.

`git diff --check` reports **no whitespace errors**.

## 32. `git diff --stat`

```
 docs/architecture/README.md | 56 +++++++++++++++++++++++++++++++++++++++++++--
 1 file changed, 54 insertions(+), 2 deletions(-)
```

`git diff` covers tracked files only, so it shows the single modified file; every
other Phase 2 file is new and therefore untracked (§31). The full diff was read
line by line: the change is additive documentation, with the only deletion being
two intro lines rewritten to say Phase 2 added the tenant domain model.

Diff review found no accidental changes, no generated or build files, no secrets,
no unrelated modifications, no unnecessary abstractions, no infrastructure
leakage into the Domain, and no scope creep.

## 33. Secret scan / manual secret review confirmation

**Confirmed — no secrets.** Two checks over every Phase 2 file:

- **Path scan** — no `node_modules`, `bin/`, `obj/`, `dist/`, `.angular/`,
  `.env`, `.pem` or `.key` path would be tracked. `.gitignore` held.
- **Content scan** for API keys, passwords, tokens, credentials, bearer values,
  connection strings, private-key headers and `sk-`-style keys — the only match
  was the phrase "token claim" in the ADR's deferred-decisions table, which is
  design prose about where tenant resolution might read from, not a credential.

Every new file was also read back after writing. Nothing in this phase touches
configuration, connection strings or credentials of any kind; the Domain layer
has no I/O.

## 34. Confirmation — VPS not accessed

**Confirmed.** No SSH connection was attempted or made to `186.240.145.196` or
any other host. Nothing was deployed. No Nginx configuration, systemd service,
server directory, package or production application was inspected or modified.
All work was local to `c:\Users\mouay\Desktop\omnichannel-ai-platform`.

Phase 2 required **no network access at all** — no package was added, and restore
resolved entirely from the existing local cache.

## 35. Readiness recommendation for Phase 3

**Ready to proceed.**

The tenant model is small, fully invariant-enforced, exhaustively tested and free
of infrastructure. The isolation strategy is written down with its invariants
fixed and its mechanisms honestly marked as open.

Recommended for Phase 3, in this order:

1. **Decide the isolation model (M1) before writing any persistence code.** It
   determines schema, indexing, connection management and migration strategy.
   Evaluate PostgreSQL RLS (M2) as part of that decision rather than after it —
   retrofitting RLS onto a schema designed without it is materially harder.
2. **Then the first tenant vertical slice** — register a tenant, end to end
   through Application, Infrastructure and Api. This is where slug uniqueness
   (D3), the conflict path, and tenant context resolution (D6) become real, and
   where the error strategy meets an HTTP edge and can be validated in practice.
3. **Write the adversarial isolation tests with that first slice**, not after it,
   so the pattern is established while there is only one thing to secure.
4. Alongside: forwarded-headers configuration, and the still-open Phase 1
   maintenance items (CI, toolchain pins, frontend linter).

Authentication should follow tenant resolution rather than precede it, since the
resolution decision (M4) determines what the token must carry.
