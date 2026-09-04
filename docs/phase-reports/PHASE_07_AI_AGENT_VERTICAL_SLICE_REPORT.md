# Phase 7 — AI agent persistence and Application/API vertical slice

## 1. Phase objective

Make the accepted provider-independent `AiAgent` Domain aggregate persistable and
usable through focused Application use cases and HTTP endpoints, for six
operations: create, read, rename, update instructions, activate and disable.

Every operation is scoped to a tenant. No delete, archive, list, search,
pagination, cloning, prompt versioning, OpenAI, RAG, tools, channel assignment or
frontend work was in scope, and none was done.

## 2. Final status

**PASS**, after the review-mandated concurrency remediation. Sections 63-75
document that work in full; every section before it describes the design as it
now stands, updated wherever the remediation changed it.

- Release build: **0 warnings, 0 errors** across all 8 projects.
- Tests: **688 defined, 688 passing, 0 failing, 0 skipped** (Debug configuration;
  see §40 for why the Release run cannot execute on this machine).
- Vulnerable packages: **none**, 8/8 projects.
- Packages added: **none**.
- Domain modified: **NO**.
- Migration changed: **NO** — the remediation needed no schema change.

One environmental limitation, not a code defect, is reported in §40 and §56.

## 3. Baseline commit

`ce3439289aeb1336f86c05c71b52464c074869c9` — `feat: establish ai agent core domain`.

## 4. Initial git state

`git status --short` was **empty** before any work began. `git log -6 --oneline`:

```
ce34392 feat: establish ai agent core domain
d2393e3 feat: implement tenant onboarding vertical slice
38f7e48 feat: establish postgresql persistence foundation
e714005 feat: establish conversation and message domain
478dd2b feat: establish tenant core domain
b7ab2ea chore: establish omnichannel ai platform foundation
```

## 5. Domain modified — **NO**

`git status --short backend/src/OmniAI.Domain` and
`git diff --stat HEAD -- backend/src/OmniAI.Domain` are both empty. The aggregate,
its value objects and its enum are byte-for-byte as accepted in `ce34392`.

Nothing was added for EF's benefit: no public setter, no parameterless
constructor, no EF attribute, no persistence metadata, no `xmin` property, no
`Version` field, no provider configuration. `grep -rn "xmin" backend/src/OmniAI.Domain`
returns nothing.

This was verified as feasible *before* writing the mapping rather than discovered
afterwards: `Conversation` already reconstitutes through a private constructor
with get-only `Id`, `TenantId` and `StartedAt`, so the pattern was known to work.
EF binds `AiAgent`'s private constructor by parameter name and writes `Status`
and `UpdatedAt` through their private setters.

## 6. Persistence implementation

Infrastructure only. Added:

- `Persistence/Configurations/AiAgentConfiguration.cs` — the `IEntityTypeConfiguration<AiAgent>`.
- `Persistence/Repositories/AiAgentRepository.cs` — `internal sealed`, implements the Application contract.
- `Persistence/PersistenceConverters.cs` — three new converters (`AiAgentId`, `AiAgentName`, `AgentInstructions`).
- `Persistence/OmniAiDbContext.cs` — one line: `public DbSet<AiAgent> AiAgents => Set<AiAgent>();`.
- `Time/MicrosecondTimeProvider.cs` — see §20.

`Message` remains deliberately unexposed as a `DbSet`; `AiAgent` gets one because
it is an aggregate root.

## 7. Migration

**`20260904170913_AddAiAgents`.**

The Phase 4 `InitialPersistence` migration was **not** rewritten, amended or
regenerated — `git status --short` on both its files is empty.

The scaffolder emitted a physical `xmin` column for `ai_agents`, exactly as it
did in Phase 4. It was removed by hand, with the same eight-line explanatory
comment. `xmin` is PostgreSQL's per-row transaction id system column: it exists
on every table already and `CREATE TABLE` rejects a user column of that name.
Npgsql 10 cannot know that, so it will keep emitting the line on every
regeneration.

`Down` drops only `ai_agents`, leaving the Phase 4 schema intact.

## 8. AiAgent table / schema

Verified by applying both migrations to a scratch database and reading the
applied schema:

```
                         Table "public.ai_agents"
    Column    |           Type           | Nullable
--------------+--------------------------+----------
 id           | uuid                     | not null
 tenant_id    | uuid                     | not null
 name         | character varying(120)   | not null
 instructions | character varying(20000) | not null
 status       | character varying(32)    | not null
 created_at   | timestamp with time zone | not null
 updated_at   | timestamp with time zone | not null
Indexes:
    "PK_ai_agents" PRIMARY KEY, btree (id)
    "ix_ai_agents_tenant" btree (tenant_id)
Foreign-key constraints:
    "fk_ai_agents_tenant" FOREIGN KEY (tenant_id) REFERENCES tenants(id) ON DELETE RESTRICT
```

No `xmin` user column (`information_schema` count = 0). Column lengths mirror the
Domain limits and a test asserts the correspondence rather than the literals
alone. Status is stored by **name**, never by ordinal.

## 9. Tenant FK / delete behaviour

`fk_ai_agents_tenant` → `tenants(id)`, **`ON DELETE RESTRICT`**, matching the
accepted decision for conversations.

Cascade would mean that whatever deletes a tenant row also destroys every
instruction set that tenant ever wrote — business content, not derived data — as
an invisible side effect. Tenant deletion semantics remain deferred, so the
database refuses until they are decided.

Two tests: one deletes a tenant that still owns an agent and asserts PostgreSQL
rejects it with SQLSTATE `23503` on that constraint; another asserts the applied
`referential_constraints.delete_rule` is `RESTRICT`, so a future migration cannot
silently change it to `CASCADE`.

**The FK is defence in depth, not the primary check** — see §14.

## 10. Index strategy

One index: **`ix_ai_agents_tenant` on `tenant_id`**.

The authorised lookup is by tenant **and** id, and that path is already optimal
without an extra index — the primary key on `id` finds at most one row and the
tenant is checked against it. A `(tenant_id, id)` composite would add write cost
and win nothing.

What does need an index is the other side of the foreign key: PostgreSQL does not
index a referencing column automatically, so without this, enforcing
`fk_ai_agents_tenant` on any tenant delete would sequentially scan `ai_agents`.
It is also the index a tenant-scoped listing will use when listing is authorised.

**No `UNIQUE (tenant_id, name)`.** No Domain or product rule says an agent name is
unique; the accepted Phase 6 documentation explicitly records that no uniqueness
rule was invented, and a unique index here would impose one through the back
door. Two tests guard it: one stores two identically named agents in one tenant
and expects success, another asserts no unique index mentioning `name` exists.

## 11. xmin concurrency strategy

`ai_agents` carries the same Infrastructure-only `xmin` shadow property as
`tenants` and `conversations`: `HasColumnType("xid")`,
`ValueGeneratedOnAddOrUpdate()`, `IsConcurrencyToken()`.

An agent is mutable in three independent ways and is edited by people, so a lost
update is realistic — and a lost update to `instructions` means a business
quietly running on rules somebody thought they had replaced.

The Domain is untouched: no `Version` property, and `xmin` appears in no view,
response or contract. `MigrationSchemaTests` now asserts the token exists in the
model for **all three** concurrency-tracked aggregates, and that on `AiAgent` it
is a concurrency token generated on add-or-update — so a future entity added
without one is noticed here rather than by a lost update in production.

**No automatic retry.** See §29.

## 12. IAiAgentRepository design

Three methods:

```csharp
Task<VersionedAiAgent?> FindAsync(TenantId tenantId, AiAgentId agentId, CancellationToken ct = default);
Task<ConcurrencyToken> AddAsync(AiAgent agent, CancellationToken ct = default);
Task<ConcurrencyToken> UpdateAsync(AiAgent agent, ConcurrencyToken expected, CancellationToken ct = default);
```

**Still three methods after the remediation**, now carrying versions: reads return
the version the agent was read at, writes return the version they produced, and a
mutation states which version it believed it was changing. See §65-§67.

**Tenant scoping is in the signature, not in a convention.** There is no overload,
no default and no code path that fetches an agent by identity alone, so the
isolation rule cannot be forgotten at a call site — the code would not compile.

No `IRepository<T>`, `GenericRepository`, `RepositoryBase<T>`, specification
framework or generic query abstraction. A second repository existing is not a
reason to introduce one. The Application project references no EF Core, Npgsql or
ASP.NET Core type, and three pre-existing reflection guard tests fail the build if
that changes.

No speculative methods: there is no `ListAsync`, `DeleteAsync`, `CountAsync` or
`ExistsAsync`, because no authorised use case needs them.

`CreateAiAgentHandler` reuses the accepted `ITenantRepository.FindByIdAsync` to
check the owning tenant rather than adding a method to either contract.

## 13. Infrastructure repository design

`internal sealed class AiAgentRepository(OmniAiDbContext context) : IAiAgentRepository`.

**`FindAsync` puts both predicates in the SQL**:

```csharp
.FirstOrDefaultAsync(agent => agent.Id == agentId && agent.TenantId == tenantId, ct)
```

Not fetch-then-compare. The distinction is the whole design: a fetch-then-compare
implementation would satisfy a behavioural test while pulling another tenant's
instruction text into this process's memory, one early return away from leaking
it. Here there is nothing to leak because nothing came back. A test asserts on
the recorded statement, not the result.

`AddAsync` translates a `fk_ai_agents_tenant` violation into
`TenantNotFoundException`, matched on **both** SQLSTATE `23503` and the constraint
name, so a future foreign key on this table cannot be silently misreported. The
failed insert is detached so a reused context does not retry it.

`UpdateAsync` translates `DbUpdateConcurrencyException` into
`AiAgentConcurrencyConflictException`. It is a no-op when the aggregate refused to
change — a test asserts no `UPDATE` statement is issued for an idempotent
activation.

Nothing leaves this class carrying a `DbUpdateException`, `PostgresException`,
SQLSTATE, constraint name or SQL text. A test asserts the escaping exception's
assembly is `OmniAI.Application`.

## 14. Create agent flow

1. `TenantId.From`, `AiAgentName.Create`, `AgentInstructions.Create` — Domain
   validation, before any database access.
2. `ITenantRepository.FindByIdAsync` — the owning tenant must exist.
3. `AiAgent.Create(tenantId, name, instructions, clock.GetUtcNow())`.
4. `IAiAgentRepository.AddAsync` — commits.
5. Return `AiAgentView`.

**Input is validated before existence is checked**, deliberately: a malformed name
is wrong however the rest of the world looks and can be answered without touching
the database, whereas whether a tenant exists is a question about stored state.
So a request that is both malformed and aimed at a missing tenant is reported as
malformed. This ordering is also what makes the cross-tenant probe in §22 fail.

**An unknown tenant is a `404`, not a `500`.** The FK still refuses the row — it is
the defence that survives the check being raced — but it is no longer what a
normal caller meets. A test drives the raced path explicitly.

The caller supplies only `name` and `instructions`. Identity, tenant, status and
both timestamps come from the route and the aggregate. A test posts `id`,
`tenantId`, `status`, `createdAt`, `model` and `temperature` in the body and
asserts every one is ignored.

## 15. Get agent flow

`TenantId.From` + `AiAgentId.From` → `FindAsync(tenantId, agentId)` → `AiAgentView?`.

`null` means not-found. Absence is a result, not a failure: no exception type, no
stack unwind, no error-level log.

**No tenant-existence check, deliberately.** A request naming a tenant that is not
there finds no agent and is answered exactly like one naming a real tenant that
does not own the agent — so this endpoint cannot be used to discover which tenant
ids are real. Adding a check would create that difference for no benefit.

Returns id, tenantId, name, instructions, status, createdAt, updatedAt. No
persistence metadata, no `xmin`.

## 16. Rename flow

`TenantId.From` + `AiAgentId.From` + `AiAgentName.Create`, then tenant-scoped
find, then `agent.Rename(name, clock.GetUtcNow())`, then `UpdateAsync`.

The Domain remains the only authority on what a name may be. Renaming to the
current name is a no-op inside the aggregate — including `UpdatedAt` — so
`UpdateAsync` has nothing to write and the response reports an unchanged
`updatedAt`.

Unknown or cross-tenant → `null` → `404`, and `UpdateAsync` is never called.

## 17. Instructions update flow

Same shape, with `AgentInstructions.Create`.

Permitted in every status including `Active`: correcting a live agent is the
normal way a business fixes its behaviour, and requiring it to be disabled first
would mean taking the agent off the air to fix a typo.

**Multiline content is preserved exactly** — through JSON escaping, model binding,
the value object, the value converter, `character varying`, and back. Tested at
Application level, at persistence level (byte-for-byte including CRLF), and over
HTTP with an assertion against the raw stored column.

A replacement, not a patch: no version, expected-current-value or diff, because
instruction history is not modelled and pretending otherwise would imply a
rollback this platform cannot perform.

## 18. Activate flow

Tenant-scoped find → `agent.Activate(clock.GetUtcNow())` → `UpdateAsync`.

`Draft → Active` and `Disabled → Active`. Activating an already active agent is a
no-op that writes nothing and leaves `updatedAt` alone.

**No readiness or configuration guard**, carrying forward the accepted Phase 6
decision: an agent cannot exist through public Domain APIs with an invalid name
or instructions, so such a guard could not fail. `Draft` is a publication state,
not an incomplete aggregate.

**No OpenAI call. No provider contacted. No channel publication. No credential
read.** Activation is Domain lifecycle activation only.

## 19. Disable flow

Tenant-scoped find → `agent.Disable(clock.GetUtcNow())` → `UpdateAsync`.

`Active → Disabled`; disabling an already disabled agent is a no-op.
**`Draft → Disabled` throws `DomainStateException`**, which the handler lets
propagate. The refusal happens before any save, so nothing partial is left
behind — asserted on the stored row.

Draft is never silently converted to Disabled.

## 20. Application time strategy

**BCL `TimeProvider`, injected into the five handlers that need an instant.** No
handler calls `DateTimeOffset.UtcNow`. No third-party clock package.
`TimeProvider` does not appear in the Domain.

Production registers `new MicrosecondTimeProvider(TimeProvider.System)` as a
singleton. Tests inject `FixedTimeProvider` — a nine-line `TimeProvider` subclass
in `backend/tests/Shared`, not a package.

**Why the wrapper — a real defect found by a test, not predicted.**
`DateTimeOffset` counts 100-nanosecond ticks; PostgreSQL `timestamptz` stores
microseconds. A create response is built from the aggregate in memory, so the
client was handed `...0.6905973Z` while the database held `...0.690597Z` — and a
`GET` of the same resource a moment later returned a *different* `createdAt`
despite nothing having been edited. `Post_then_follow_the_location_then_get_returns_the_same_agent`
caught it.

The fix truncates at the source, once, for every use case, in the only place that
knows the constraint exists. Truncation rather than rounding, so a recorded
instant is never later than the moment it describes. It lives in Infrastructure
because the limit is a storage property, not a business rule. `GetTimestamp()` is
delegated untouched — it measures durations and is never persisted.

## 21. Tenant isolation strategy

Four layers, each independently verified:

1. **Route** — every path carries `{tenantId:guid}`. There is no tenant-less agent route.
2. **Contract** — `FindAsync` cannot be called without a tenant.
3. **SQL** — both predicates are in the `WHERE` clause; another tenant's row is never read.
4. **Tests** — the full matrix over HTTP, with before/after snapshots read straight from PostgreSQL.

Layer 3 is what makes the not-found honest rather than cosmetic: the API is not
hiding something it is holding.

## 22. Cross-tenant not-found behaviour

A request for an agent the named tenant does not own is answered `404`,
indistinguishable from an id that exists nowhere: same status, same media type,
same detail wording (which names only the id the caller already supplied).

Two subtleties are tested because either would leak existence:

- **A cross-tenant `disable` of a draft answers `404`, not `409`.** The owner's own
  draft answers `409`. Had the state rule been evaluated before ownership, the
  difference would confirm both that the id is real and that it is still a draft.
  The test asserts both halves.
- **An invalid payload aimed across the boundary answers `400` either way.**
  Validation runs before the lookup, so a probe with a deliberately bad body
  cannot use the status code to tell a real agent from an absent one.

Every cross-tenant **mutation** additionally leaves the target byte-for-byte
unchanged — the important half, since a write reported as not-found while still
landing would be silent cross-tenant corruption nobody would notice.

## 23. API routes

Under `/api/tenants/{tenantId:guid}/ai-agents`:

| Method | Path | Success |
| --- | --- | --- |
| `POST` | `/` | `201` + `Location` |
| `GET` | `/{aiAgentId:guid}` | `200` |
| `PUT` | `/{aiAgentId:guid}/name` | `200` |
| `PUT` | `/{aiAgentId:guid}/instructions` | `200` |
| `POST` | `/{aiAgentId:guid}/activate` | `200` |
| `POST` | `/{aiAgentId:guid}/disable` | `200` |

Minimal APIs in `AiAgentEndpoints`, matching the tenant endpoint style. Each
method translates the request, calls one handler, translates the outcome — no
business logic, no database call, no error mapping. That is what keeps six
endpoints from becoming a fat class.

`PATCH` and `DELETE` are not mapped, and tests assert both answer `405`. No list
or search endpoint exists.

## 24. Request DTOs

- `CreateAiAgentRequest(string? Name, string? Instructions)`
- `RenameAiAgentRequest(string? Name)`
- `UpdateAiAgentInstructionsRequest(string? Instructions)`

Nullable strings, no validation attributes: the Domain value objects own the
rules, and a `[Required]` here would be a second copy that can disagree. The
owning tenant comes from the route, never the body. No `id`, `status`,
`createdAt`, `model`, `provider`, `temperature` or `assistantId` field exists.

## 25. Response DTO

`AiAgentResponse(Guid Id, Guid TenantId, string Name, string Instructions, string Status, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)`.

Seven fields. No `xmin`, row version or other persistence metadata; no provider,
model, sampling parameter, token limit or external assistant id. A test
enumerates the JSON property names and fails if the set changes.

## 26. Status serialization

An explicit `switch` with a throwing default arm mapping `Draft`/`Active`/`Disabled`
to those exact strings — never `ToString()`, never a JSON enum converter, never an
integer ordinal. Both would make the published contract a side effect of a member's
spelling or a serialiser setting. A test asserts the JSON value kind is `String`.

## 27. Domain validation mapping

`DomainValidationException` → **`400`**, `application/problem+json`, title
"Invalid request details", detail = the Domain's own message.

Covers blank/over-long/control-character names and instructions, and an empty
route GUID — `Guid.Empty` satisfies the `:guid` constraint, reaches the handler
and is rejected by `TenantId.From` / `AiAgentId.From` as malformed input rather
than answered as not-found.

## 28. Domain state mapping

`DomainStateException` → **`409 Conflict`**.

`409` rather than `400` because nothing about the request is malformed — the same
request succeeds once the agent has been activated — so the conflict is with the
state of the resource rather than with what the caller wrote. `422` was considered
and rejected: the content is perfectly processable, it just contradicts the
resource as it stands.

## 29. Optimistic-concurrency mapping

**Superseded by the remediation — see §66 and §69.**

`DbUpdateConcurrencyException` → (Infrastructure)
`AiAgentVersionMismatchException` → (API) **`412 Precondition Failed`**.

It was `409` before the remediation. `412` is now correct because every mutation
carries an `If-Match` precondition and this outcome is that precondition failing.
`409` stays reserved for a Domain state conflict, which is a different thing: it
would still be refused after a re-read, whereas a `412` usually would not.

**Never a `500`**, and never a silent overwrite.

**No automatic retry, deliberately.** Renaming an agent or rewriting its
instructions is an expression of intent about a specific version of the text.
Replaying that intent against state the operator has not seen would overwrite a
colleague's edit — the exact loss the token exists to prevent. Retry policy must
be use-case specific once there is a product answer to "what should happen to the
other person's change". A test proves nothing retries; another proves the caller
can re-read and reapply successfully.

## 30. ProblemDetails behaviour

RFC 9457 throughout, via the Phase 1 `AddProblemDetails` + `UseExceptionHandler`
pipeline. Unrecognised exceptions are left to the default handler: `500` with no
message, type name or stack trace.

`TenantProblemDetailsHandler` was **renamed** to `DomainProblemDetailsHandler`. It
never was tenant-specific — `DomainValidationException` is raised by every value
object in the model — and now serves AI agents too. A second handler was
considered and rejected: it could not have worked, because nothing in a
`DomainValidationException` says which aggregate raised it.

**Nothing internal is disclosed.** A shared test helper asserts every problem body
is free of `Npgsql`, `SQLSTATE`, stack frames, `xmin`, `xid`, `DbUpdate`,
`EntityFramework` and the table name, and separately that no SQL keyword appears.
The malformed-body arm uses a fixed sentence rather than the parser message,
which can quote the payload back.

The handler gained three outcomes in the remediation — `428`, `412` and the
malformed-precondition `400`. See §69.

## 31. Persistence integration tests

**Real PostgreSQL 17.6. No in-memory provider.** 63 new tests across four classes:

**`AiAgentPersistenceTests` (27)** — full round trip; reconstitution without a
Domain change; native `uuid` for both ids; stored identity matches the Domain
identity read outside EF; tenant ownership on the row; name as a scalar column;
non-Latin name; multiline instructions byte-for-byte; CRLF preserved; both
lengths at the Domain maximum; status stored by name (theory over all three);
status change round trip; both instants as `timestamptz`; offset normalised to
UTC; **high-water mark survives a round trip**; FK rejects an orphan; tenant
delete restricted; `delete_rule` is `RESTRICT`; two same-named agents in one
tenant; two tenants with the same agent name; every column `NOT NULL`; column
lengths mirror Domain limits; tenant index exists; **no unique index on name**.

**`AiAgentRepositoryTests` (12)** — store and find; `AddAsync` commits (verified on
a separate connection); unknown id absent; cross-tenant not found while the row
demonstrably exists; **the tenant filter is in the SQL** (asserted on the recorded
statement); FK translated not leaked; rejected insert leaves no row and detaches;
update persists; **no `UPDATE` issued for an idempotent call**; stale write
translated; escaping exception is Application-owned; null arguments rejected.

**`AiAgentConcurrencyTests` (7)** — stale rename rejected; stale instructions
update rejected and the earlier text survives; stale activation rejected; stale
disable rejected; a rejected write leaves the row exactly as the winner left it;
sequential updates from a fresh load succeed; an idempotent lifecycle call cannot
produce a spurious conflict.

**`MicrosecondTimeProviderTests` (14)** — truncation to whole microseconds,
truncation not rounding, never moving forwards, offset preserved, the clock still
advances, delegation of `GetTimestamp` and `LocalTimeZone`, null rejected.

`MigrationSchemaTests` grew from 4 to 7: the token is asserted for all three
concurrency-tracked aggregates, `ai_agents` specifically has no `xmin` user
column, the token is read-only, and both migrations are applied in order.

## 32. Application tests

92 new tests, hand-written fakes, **no mocking framework**. The Domain is never
faked — the real aggregate and value objects are used, so the real invariants and
lifecycle rules are exercised.

| Class | Tests | Covers |
| --- | --- | --- |
| `CreateAiAgentHandlerTests` | 21 | valid create, stored, `Draft` status, tenant ownership, unknown tenant, tenant vanishing after the check, name and instruction validation, not-truncated messages, empty tenant id, validation-before-lookup ordering, deterministic time, canonical values |
| `GetAiAgentByIdHandlerTests` | 9 | found, every field projected, unknown absent, cross-tenant hidden, **hidden is indistinguishable from non-existent**, empty ids, nulls |
| `RenameAiAgentHandlerTests` | 18 | success, mark advances, no-op rename, **out-of-order instant applies the edit but does not move the mark**, validation, unknown, cross-tenant leaves the target untouched, concurrency reaches the caller |
| `UpdateAiAgentInstructionsHandlerTests` | 18 | success, **multiline preserved**, trimming only outside, allowed while active, no-op, out-of-order instant, validation, unknown, cross-tenant, concurrency |
| `ActivateAiAgentHandlerTests` | 12 | `Draft→Active`, `Disabled→Active`, idempotent no-op, **activation needs nothing external**, unknown, cross-tenant, concurrency, empty ids |
| `DisableAiAgentHandlerTests` | 14 | `Active→Disabled`, idempotent, **`Draft` refused**, refusal writes nothing, re-disable cycle, unknown, cross-tenant, **cross-tenant draft is 404 not a state error**, concurrency |

`InMemoryAiAgentRepository` matches on tenant **and** id, exactly as the real
repository does — a fake matching on id alone would let the cross-tenant tests
pass against a handler with no scoping at all.

## 33. API integration tests

**Real HTTP over the real in-memory-hosted API against real PostgreSQL.** 66 new
tests. No service is replaced except where noted.

**`AiAgentEndpointsTests` (40)** — `201` with the representation; `Location`
pointing at the *tenant-scoped* read endpoint and resolving; database state;
canonical values; **multiline through the whole round trip and into the raw
column**; matching UTC instants; caller cannot choose id/status/timestamps;
`200` reads; POST → Location → GET identity; `404`; **no persistence metadata in
the payload**; status is a string not an ordinal; name and instruction validation
(theories); over-long name not truncated; malformed body; empty tenant GUID
`400`; empty agent GUID `400`; non-GUID segment does not match; unknown tenant
`404`; full `Draft→Active→Disabled→Active` cycle with the stored row checked;
**`Draft` disable `409` changing nothing**; both idempotent lifecycle calls;
lifecycle on unknown agent; rename success/validation/unknown; instructions
success/while-active/validation/unknown; **`DELETE` and list `405`**; **`PATCH`
`405`**; the API is talking to the test database.

**`AiAgentTenantIsolationTests` (11)** — the control (own agent readable), then
read, rename, instructions, activate and disable across the boundary, each
`404` **and** leaving a byte-for-byte identical row; cross-tenant read
indistinguishable from non-existent; cross-tenant draft disable is `404` not
`409`; invalid payload reveals nothing; an agent unreachable through a
non-existent tenant; and a final test that runs the entire crossing matrix in
both directions and asserts both tenants are exactly as they started.

**`AiAgentConcurrencyApiTests` (7)** — see §35.

**`AiAgentTimeApiTests` (8)** — see §35.

## 34. Cross-tenant test results

**All 11 isolation tests pass.** Every authorised operation attempted across the
boundary returns `404`, and every mutation leaves the target unchanged —
verified by snapshotting `id`, `tenant_id`, `name`, `instructions`, `status`,
`created_at` and `updated_at` straight from PostgreSQL before and after, so the
assertion cannot be satisfied by the same filtering it is testing.

No cross-tenant read, write or existence disclosure was possible in any test.

## 35. Concurrency test results

**All 7 HTTP concurrency tests pass, plus 7 at the Infrastructure level.**

Making the race deterministic mattered: two parallel HTTP requests may or may not
overlap, so a test built on them either passes for the wrong reason or fails
intermittently. `CompetingWriterInterceptor` runs one out-of-band `UPDATE` on its
own connection at the moment `SaveChanges` begins — after the request has read
the row and computed its change, before its `UPDATE` reaches PostgreSQL. The
conflict is real: the row genuinely was modified by another transaction on
another connection. **Each test asserts the competing write actually fired**, so a
passing conflict test cannot be a false positive.

It is added via `IDbContextOptionsConfiguration<OmniAiDbContext>` — EF Core's
supported way to append to an existing `AddDbContext` registration — so no
production service is replaced.

Results: stale rename → `409`, other writer's value survives; stale instructions
update → `409`, other text survives; stale lifecycle change → `409`, activation
did not land; the conflict body discloses nothing about the database; **nothing
retries**; the caller can re-read and reapply successfully. A further test fires
eight genuinely parallel renames and asserts every response is `200` or `409`,
none is `500`, and the row holds exactly one of the requested values.

**Time (`AiAgentTimeApiTests`)** — the one fixture that replaces a service, and it
replaces exactly one: `TimeProvider`, wrapped in `MicrosecondTimeProvider` the
same way the composition root wraps the system clock. It proves exact
`createdAt`/`updatedAt` values over HTTP, that the response and the stored row
agree, that idempotent calls do not advance the mark, and — by moving the clock
**backwards** — that an out-of-order request still applies the edit while
`updatedAt` never regresses. A handler reading `DateTimeOffset.UtcNow` would fail
these.

## 36. Backend test count before

**405** at the Phase 6 baseline — Domain 288, Application 31, Infrastructure 53,
Api 33.

**616** when Phase 7 was first submitted for review — Domain 288,
Application 113, Infrastructure 116, Api 99.

## 37. Backend test count after

**688** — Domain **288** (unchanged), Application **123**, Infrastructure **144**,
Api **133**.

**+283 over the Phase 6 baseline**: Application +92, Infrastructure +91, Api +100.
Domain +0, as required.

**+72 added by the remediation alone**: Application +10, Infrastructure +28,
Api +34. See §74.

## 38. Restore result

`dotnet restore OmniAI.slnx` → "All projects are up-to-date for restore." No new
package was resolved.

## 39. Release build result

`dotnet build OmniAI.slnx -c Release` → **`0 Warning(s), 0 Error(s)`**, all 8
projects. Nothing suppressed; `TreatWarningsAsErrors` and the analyzers remain on.

## 40. Test result

**688 defined, 688 passing, 0 failing, 0 skipped — Debug configuration.**

```
OmniAI.Domain.Tests                     288 passed
OmniAI.Application.Tests                123 passed
OmniAI.Infrastructure.IntegrationTests  144 passed
OmniAI.Api.IntegrationTests             133 passed
```

**The Release-configuration run cannot execute on this machine.** Smart App
Control blocks `OmniAI.Domain.dll` and `OmniAI.Application.dll` at load with
`0x800711C7`; because every suite references Domain, all four fail. Confirmed
from the CodeIntegrity event log — event 3118, "Smart App Control Block", policy
`{0283ac0f-fff1-49ae-ada1-8a933130cad6}`, blocking `testhost.exe` from loading
`OmniAI.Domain.dll`.

This is an unsigned-binary reputation verdict, not a code fault:

- The policy state was read and is **`VerifiedAndReputablePolicyState = 1`**, unchanged. **It was not disabled, and no machine security policy was modified.**
- The identical test source executes fully in Debug, where the assemblies have different file hashes and therefore different verdicts.
- The Phase 6 baseline (405/405) was re-verified in Debug before any Phase 7 work began, and reproduces exactly.

Nothing was faked, skipped or suppressed to work around it. **A Linux CI
environment must execute the complete suite in Release before any production
release** — this remains mandatory and is now load-bearing for two phases.

## 41. Vulnerability result

`dotnet list package --vulnerable --include-transitive` → **"no vulnerable packages"
for all 8 projects.**

## 42. Migration/schema validation

Both migrations were applied to a throwaway database and the result inspected
directly (§8). `__EFMigrationsHistory` contained exactly
`20260904124546_InitialPersistence` then `20260904170913_AddAiAgents`.
`information_schema` reported **0** columns named `xmin` in the `public` schema.
The scratch database was dropped.

Four schema guards now run in CI-able form as tests: no `xmin` in the generated
script, none in the applied schema, none on `ai_agents` specifically, and both
migrations applied in order.

## 43. Compiler / analyzer warnings

**Zero**, in both Debug and Release. Nothing was suppressed, and no
`#pragma warning disable`, `<NoWarn>` or `.editorconfig` severity change was
added.

One analyzer finding was fixed on merits during development: xUnit's
`xUnit2031` correctly rejected `Assert.Single(collection.Where(...))`, replaced
with the filtering overload.

## 44. Packages added

**NONE.**

`Directory.Packages.props` is unmodified. No `.csproj` gained a
`PackageReference` — the three test project edits add only `<Compile Include>`
links for the shared `FixedTimeProvider`, verified by
`git diff -- "*.csproj" | grep PackageReference` returning nothing.

No OpenAI package, no Anthropic package, no AI SDK, no mocking framework, no
clock package, no Result library, no MediatR, no FluentValidation, no
Testcontainers. `TimeProvider` is BCL.

## 45. Application modified — **YES**

15 new files under `AiAgents/` (contract, view, exception, six command/query +
handler pairs) and one new file under `Tenants/` (`TenantNotFoundException`). No
existing Application file was modified.

## 46. Infrastructure modified — **YES**

New: `AiAgentConfiguration`, `AiAgentRepository`, `MicrosecondTimeProvider`, the
`AddAiAgents` migration and its designer file.
Modified: `OmniAiDbContext` (one `DbSet`), `PersistenceConverters` (three
converters), `InfrastructureServiceCollectionExtensions` (one registration), and
the regenerated model snapshot.

## 47. API modified — **YES**

New: `AiAgentEndpoints`, `AiAgentResponse`, three request DTOs, and
`DomainProblemDetailsHandler`.
Modified: `CompositionRoot` (clock + six handlers + renamed exception handler),
`Program.cs` (one endpoint mapping plus its security note).
Deleted: `TenantProblemDetailsHandler.cs`, superseded by the rename.

## 48. Frontend modified — **NO**

`git status --short frontend` is empty.

## 49. Files created / modified

**68 total: 56 created, 11 modified, 1 deleted** (including this report).

| Area | Created | Modified |
| --- | --- | --- |
| Application | 20 | 0 |
| Infrastructure | 6 | 4 |
| API | 7 | 2 (+1 deleted) |
| Application tests | 7 | 1 (csproj) |
| Infrastructure tests | 7 | 2 (csproj, `MigrationSchemaTests`) |
| API tests | 7 | 1 (csproj) |
| Shared tests | 1 | 0 |
| Docs | 2 | 1 |

Files added by the remediation: `ConcurrencyToken`, `VersionedAiAgent`,
`VersionedAiAgentView`, `AiAgentVersionMismatchException`, `AiAgentConcurrency`,
`RowVersionTokens`, `EntityTags`, `AiAgentPreconditionApiTests`,
`RowVersionTokensTests`, `XminReadBackProbeTests`.
`AiAgentConcurrencyConflictException` was removed by the rename in §75.

## 50. Commands executed

`git status --short`, `git log -6 --oneline`, `git diff --check`,
`git diff --stat`, `git diff`; `dotnet restore`, `dotnet build -c Debug|Release`,
`dotnet test -c Debug|Release`, `dotnet list package --vulnerable --include-transitive`;
`dotnet tool restore`, `dotnet ef migrations add AddAiAgents`,
`dotnet ef database update` (against a throwaway database, then dropped);
`psql` schema inspection against the local disposable test cluster; read-only
Windows policy and CodeIntegrity event-log queries.

**No `git add`, `git commit`, `git push` or remote command was run.**

## 51. Architectural decisions

Recorded in [`docs/architecture/ai-agent-vertical-slice.md`](../architecture/ai-agent-vertical-slice.md)
as **B1–B21**, with a DEFERRED table and fifteen independently checkable claims.
B16–B21 were added by the remediation.

Summary: scalar relational storage and never JSON; reconstitution with no Domain
change; `RESTRICT` foreign key; one tenant index and no name uniqueness; `xmin`
concurrency with no Domain leak and no retry; a new migration beside the accepted
one; a three-method tenant-scoped repository; still no unit of work; cross-tenant
access as honest not-found enforced in SQL; explicit handlers with
validation-before-lookup ordering; injected `TimeProvider`; a storage-aligned
clock; `PUT` on named sub-resources with `POST` for operations and `200` for all
mutations; one central error-mapping handler.

From the remediation: server-request and client-think-time concurrency treated as
different problems; an opaque `ConcurrencyToken` between PostgreSQL's `xmin` and
the HTTP `ETag`; `If-Match` required on every mutation; the caller's expected
version participating in the SQL predicate **and** checked in front of it;
`428`/`412`/`409` kept strictly distinct; ownership resolved before any version
comparison so a token can never become an access path.

## 52. Security warning

**These endpoints are NOT authorised for public production exposure.**

Authentication and authorisation do not exist. The tenant id in the route is
supplied by the caller and **believed**, because there is no authenticated
principal to check it against.

Tenant scoping is therefore a **correctness** boundary, not yet a **security**
boundary. It is fully enforced and fully tested — no request can read or change an
agent through another tenant's route — but nothing yet stops a caller from naming
whichever tenant it likes. A future authentication phase must bind an
authenticated principal to the tenant ids it may use; the scoping built here is
what that phase will attach to.

**No fake API key, hard-coded admin token, placeholder JWT or fake authorisation
was created.** A fake control would hide the gap rather than close it. The Phase 5
tenant endpoints remain unauthenticated and equally unauthorised for exposure.

**No deployment was performed. The VPS was not accessed.**

## 53. Deferred concerns

Authentication; authorisation; current-tenant context; platform admin
permissions; list, search and pagination; agent deletion and archival; prompt
versioning and instruction history; OpenAI and every other provider; provider and
model selection; RAG, embeddings and vector storage; tools and function calling;
channels and channel assignment; human handoff; evaluations; cost limits and
quotas; frontend agent management. Tenant physical-deletion semantics also remain
deferred, which is why the foreign key is `RESTRICT`.

All are recorded as deferred in the architecture document. None is described
anywhere as implemented.

## 54. Deviations

Three, all judgement calls made within the brief and flagged here for review:

1. **`TenantProblemDetailsHandler` was renamed to `DomainProblemDetailsHandler`**
   (§30). The brief said to preserve the Phase 5 error strategy; the strategy is
   preserved exactly and the class is unchanged in behaviour for tenants, but the
   file, class and one title changed. The alternative — a second handler — could
   not work, because nothing in a `DomainValidationException` identifies the
   aggregate that raised it.

2. **`MicrosecondTimeProvider` was added** (§20), which was not asked for. It fixes
   a real contract inconsistency a test exposed. The alternative was to loosen the
   round-trip assertion and leave `createdAt` differing between a create response
   and the next read.

3. **AI agent concurrency tests were put in a new `AiAgentConcurrencyTests` class**
   rather than added to the accepted `ConcurrencyTests`, which is therefore
   untouched. Same mechanism, different realistic races.

4. **`AiAgentConcurrencyConflictException` was renamed to
   `AiAgentVersionMismatchException`** during the remediation, and its mapping
   moved from `409` to `412`. Both were consequences of the mandated design
   rather than independent choices, and both are recorded in §75 and §70.

## 55. Known issues

- **No instruction history.** Updating replaces the text; the previous version is
  gone. Recorded as deferred, not silently absent.
- **The four mutation handlers share a shape** — validate, tenant-scoped find,
  Domain call, update. Explicit and separate for now, as the brief asked. A fifth
  would justify extracting the shared part.
- **`AiAgentConcurrencyConflictException` is agent-specific.** When a second
  aggregate needs the same outcome, that is the point to generalise it.
- **`CreateAiAgentHandler` loads the whole tenant row** to answer an existence
  question. One indexed primary-key lookup of a four-column row; adding an
  `ExistsAsync` to the accepted contract was judged not worth it yet.
- **Tenant-scoped listing has no covering index** beyond `tenant_id`, because
  listing is not authorised and its ordering is undecided.
- **The Phase 5 tenant endpoints remain unauthenticated**, unchanged, and are
  also **unconditional**: they have no `ETag` or `If-Match`. Tenants have one
  mutating operation today (create, which needs no precondition), so nothing was
  added speculatively -- but a tenant update endpoint will need the same
  treatment, and `ConcurrencyToken` is ready for it.
- **`ConcurrencyToken` has exactly one user.** That is deliberate, and the
  trigger for reconsidering is a second aggregate needing conditional requests --
  at which point it should be reused as it stands, not generalised further.
- **A client that never re-reads can loop on `412`.** That is correct behaviour
  rather than a defect: the server refuses to guess what should happen to the
  other person's change. A merge or force-overwrite affordance is deferred until
  there is a product answer.

## 56. Blockers

**None for the phase.** One environmental limitation, unchanged in nature from
Phases 4 and 6 but now broader in effect:

Smart App Control blocks the Release-configuration `OmniAI.Domain.dll` and
`OmniAI.Application.dll` at load, so the Release test run cannot execute here
(§40). It was **not** disabled and no machine security policy was changed. The
identical suite executes fully in Debug, and the Release **build** is clean with
zero warnings. A Linux CI environment executing the full suite in Release remains
**mandatory** before any production release.

## 57. `git status --short`

```
 M backend/src/OmniAI.Api/Composition/CompositionRoot.cs
 D backend/src/OmniAI.Api/ErrorHandling/TenantProblemDetailsHandler.cs
 M backend/src/OmniAI.Api/Program.cs
 M backend/src/OmniAI.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs
 M backend/src/OmniAI.Infrastructure/Persistence/Migrations/OmniAiDbContextModelSnapshot.cs
 M backend/src/OmniAI.Infrastructure/Persistence/OmniAiDbContext.cs
 M backend/src/OmniAI.Infrastructure/Persistence/PersistenceConverters.cs
 M backend/tests/OmniAI.Api.IntegrationTests/OmniAI.Api.IntegrationTests.csproj
 M backend/tests/OmniAI.Application.Tests/OmniAI.Application.Tests.csproj
 M backend/tests/OmniAI.Infrastructure.IntegrationTests/OmniAI.Infrastructure.IntegrationTests.csproj
 M backend/tests/OmniAI.Infrastructure.IntegrationTests/Persistence/MigrationSchemaTests.cs
 M docs/architecture/README.md
?? backend/src/OmniAI.Api/AiAgents/
?? backend/src/OmniAI.Api/ErrorHandling/DomainProblemDetailsHandler.cs
?? backend/src/OmniAI.Application/AiAgents/
?? backend/src/OmniAI.Application/Concurrency/
?? backend/src/OmniAI.Application/Tenants/TenantNotFoundException.cs
?? backend/src/OmniAI.Infrastructure/Persistence/Configurations/AiAgentConfiguration.cs
?? backend/src/OmniAI.Infrastructure/Persistence/Migrations/20260904170913_AddAiAgents.Designer.cs
?? backend/src/OmniAI.Infrastructure/Persistence/Migrations/20260904170913_AddAiAgents.cs
?? backend/src/OmniAI.Infrastructure/Persistence/Repositories/AiAgentRepository.cs
?? backend/src/OmniAI.Infrastructure/Persistence/RowVersionTokens.cs
?? backend/src/OmniAI.Infrastructure/Time/
?? backend/tests/OmniAI.Api.IntegrationTests/AiAgents/
?? backend/tests/OmniAI.Application.Tests/AiAgents/
?? backend/tests/OmniAI.Infrastructure.IntegrationTests/Persistence/AiAgentConcurrencyTests.cs
?? backend/tests/OmniAI.Infrastructure.IntegrationTests/Persistence/AiAgentPersistenceTests.cs
?? backend/tests/OmniAI.Infrastructure.IntegrationTests/Persistence/AiAgentRepositoryTests.cs
?? backend/tests/OmniAI.Infrastructure.IntegrationTests/Persistence/RowVersionTokensTests.cs
?? backend/tests/OmniAI.Infrastructure.IntegrationTests/Persistence/XminReadBackProbeTests.cs
?? backend/tests/OmniAI.Infrastructure.IntegrationTests/Time/
?? backend/tests/Shared/FixedTimeProvider.cs
?? docs/architecture/ai-agent-vertical-slice.md
?? docs/phase-reports/PHASE_07_AI_AGENT_VERTICAL_SLICE_REPORT.md
```

Nothing is staged.

## 58. `git diff --stat`

```
 .../src/OmniAI.Api/Composition/CompositionRoot.cs  |  34 +++++-
 .../ErrorHandling/TenantProblemDetailsHandler.cs   | 126 ---------------------
 backend/src/OmniAI.Api/Program.cs                  |  11 ++
 .../InfrastructureServiceCollectionExtensions.cs   |  11 +-
 .../Migrations/OmniAiDbContextModelSnapshot.cs     |  60 ++++++++++
 .../Persistence/OmniAiDbContext.cs                 |   3 +
 .../Persistence/PersistenceConverters.cs           |  23 +++-
 .../OmniAI.Api.IntegrationTests.csproj             |   6 +
 .../OmniAI.Application.Tests.csproj                |   2 +
 .../OmniAI.Infrastructure.IntegrationTests.csproj  |   2 +
 .../Persistence/MigrationSchemaTests.cs            |  64 ++++++++++-
 docs/architecture/README.md                        |  10 +-
 12 files changed, 209 insertions(+), 143 deletions(-)
```

`git diff --check` reports no whitespace errors. The 126 deletions are the whole
of `TenantProblemDetailsHandler.cs`, superseded by the rename described in §30;
its replacement is one of the untracked files in §57.

The `AddAiAgents` migration and `InitialPersistence` are both absent from this
diff for the right reasons: the first is untracked and unchanged since it was
generated, and the second was never touched.

## 59. Secret review confirmation

**Confirmed — no secrets.**

- No password, SSH credential, database credential, JWT signing secret, SMTP
  credential or OAuth secret in any changed file.
- **No connection string.** The test database is located through the existing
  `OMNIAI_TEST_POSTGRES` environment variable and was supplied only on the
  command line for test runs; it appears in no file. `appsettings.json` was not
  touched and its connection string remains blank.
- Pattern scans for `sk-`, `ghp_`, `xox[baprs]-`, `AKIA…` and
  `-----BEGIN … PRIVATE KEY`: no matches.
- No `bin`, `obj`, `node_modules`, `.vs`, `.idea`, `.vscode`, build output,
  IDE-generated file, PostgreSQL data directory, `postmaster.pid` or
  machine-specific cluster file is in the change set.
- The scratch database used for migration verification was dropped.
- The VPS address appears once, in §61 below, in the sentence stating it was not
  contacted — the same convention as every accepted phase report.

## 60. Confirmation OpenAI was NOT called or configured

**Confirmed.**

No OpenAI or Anthropic package was installed. No `OPENAI_API_KEY` or any provider
credential was read, referenced or added. No AI client was created. **No external
AI API was called.** No `IAiProvider` abstraction, model name, provider id,
temperature, top-p, token limit or provider configuration field exists anywhere
in the slice.

Every occurrence of a provider name in the source is documentation prose stating
what is absent. The Phase 6 reflection guards — which fail if a provider-shaped
type or property appears in the agent namespace — still pass, unchanged.

## 61. Confirmation VPS was NOT accessed

**Confirmed.** No SSH connection was attempted or made to `186.240.145.196` or any
other host. No deployment, no remote command, no file transfer. No SSH
configuration or credential was read, written, printed, logged or changed. No
Nginx configuration, systemd unit, port or unrelated application was touched.

Smart App Control was not disabled and no Windows security policy was modified;
the policy state was read only, and remains `1`.

## 62. Readiness recommendation for Phase 8

The slice is complete and tested, but it is **not deployable**, and the reason is
the same one recorded in Phase 5: the tenant boundary is enforced but not
authenticated.

**The recommended Phase 8 is authentication and authorisation**, ahead of any AI
provider integration. Two concrete reasons:

1. Every endpoint added in Phases 5 and 7 is unauthenticated, and there are now
   twelve of them addressing tenant-owned business configuration. The scoping
   machinery an authentication phase needs to attach to already exists and is
   proven — binding an authenticated principal to permitted tenant ids is a
   well-defined next step rather than a redesign.
2. Integrating an AI provider first would mean adding provider credentials and
   outbound calls to a surface anyone who can reach the process can drive. That
   is the wrong order.

If provider integration is preferred first, it should be built behind an
Application-owned abstraction with the Domain untouched, and the deployment
prohibition must remain in force until authentication lands.

Before **any** production release, a Linux CI environment must execute the
complete suite in Release configuration (§40, §56).

---

# PHASE 7 REMEDIATION — end-to-end optimistic concurrency

Sections 63-75 record the review-mandated remediation. Every section above has
been updated to describe the design as it now stands.

## 63. The original limitation

The `xmin` mechanism accepted in the first submission detects a competing write
that lands **between one request reading a row and that same request writing
it**. That window is milliseconds wide.

It does nothing about the window that belongs to a person, and that is the one
that destroys work: somebody opens an agent, goes to a meeting, and saves. The
request carrying that edit reads the *current* row, stamps the *current* version,
and therefore conflicts with nothing.

I built the first version around the narrow window and did not distinguish the
two. The review was right that this is insufficient for a multi-user admin
application.

## 64. The lost-update scenario

```
 Client A                       Database                    Client B
    |                              |                            |
    |----- GET agent ------------->| V1                         |
    |<---- V1 ---------------------|                            |
    |                              |<---- GET agent ------------|
    |                              |----- V1 ------------------>|
    |                              |                            |
    |                              |<---- save -----------------|
    |                              | V2  (accepted)             |
    |   ...minutes pass...         |                            |
    |----- save (from V1 screen) ->|                            |
    |                              | server loads CURRENT V2    |
    |                              | applies A's stale input    |
    |                              | writes: xmin matches V2    |
    |<---- 200 OK -----------------|                            |
    |                              | B's change is GONE         |
```

Nothing here is a race. Every step is orderly, the token is current at the moment
it is used, and the loss is total and silent. The only thing that reveals the
problem is what Client A was *looking at*, which the server never learns unless
the client tells it.

## 65. Chosen token representation

`ConcurrencyToken` — a single-field `readonly record struct` in
`OmniAI.Application.Concurrency`. Opaque above Infrastructure: the Application
compares tokens for equality and never parses one, does arithmetic on one, or
knows what is inside.

**Not in the Domain.** `AiAgent` gained nothing: no `Version`, `ETag`, `xmin` or
`ConcurrencyToken` property. Searching the Domain for any of those returns
nothing.

**One type, not a framework.** No `IVersioned<T>`, no generic entity-version
abstraction, no reflection, no middleware, no third-party package. It is written
generically because a version marker genuinely has no aggregate-specific content,
not because a second user is planned; when one arrives it can reuse this as it
stands.

## 66. Infrastructure mapping

```
PostgreSQL xmin  ->  RowVersionTokens  ->  ConcurrencyToken  ->  HTTP ETag
   (uint)             (encode/decode)         (opaque)          ("AAAwOQ")
```

`RowVersionTokens` is the entire boundary: four bytes of `xmin`, big-endian,
unpadded base64url. Big-endian so the value never depends on the machine that
produced it; base64url so it is header-safe without escaping — asserted to stay
within that alphabet for 4096 consecutive values.

**No HMAC and no encryption.** The value is not a secret; a transaction id
reveals nothing about anyone's data. Signing it would add key management and
rotation in exchange for an assurance nothing needs, and a forged token cannot do
anything a real one could not — the worst outcome is a write refused.

**A defect this found.** `Base64Url.TryDecodeFromChars` **throws**
`FormatException` on a non-base64url character rather than returning `false`.
Since the value arrives straight from a client header, that would have turned
"your header contains rubbish" into an unhandled exception and a `500`. It is now
validated with `Base64Url.IsValid` first, with a `catch` behind it, and a test
covers each malformed shape.

## 67. The expected version reaches the database

The repository substitutes the **caller's** version for the one this request
loaded:

```csharp
version.OriginalValue = expectedRowVersion;   // not the version we just read
await _context.SaveChangesAsync(ct);          // UPDATE ... WHERE xmin = @expected
```

Without this the update could never conflict, because the version EF would
otherwise use is current by construction. That is exactly the step-6 failure in
§64.

**Proven directly.** `The_callers_expected_version_is_what_the_database_checks`
loads the current row — as any real request does — presents a stale version
anyway, and asserts the write is refused and the other writer's value survives.
Two probe tests also pin the EF behaviours the design rests on: that `xmin` is
read back after insert and after update, and that overriding `OriginalValue`
really does change the `UPDATE` predicate.

## 68. The precondition is also checked in front of the database

Because the database check cannot cover a mutation that writes **nothing**.
Activating an already active agent changes no property, so EF issues no statement
and there is no predicate to fail. Without an explicit check, a caller holding a
hopelessly stale version would get a cheerful `200` — a precondition silently
skipped because the operation happened to be a no-op.

`AiAgentConcurrency.RequireExpectedVersion` is one method, called by four
handlers. No interface, no pipeline, no registration, no reflection.

It runs **before** the Domain operation, so a stale caller is told it is stale
rather than told about a rule describing a state it has never seen.

## 69. API ETag / If-Match contract

**`ETag` returned by** `POST` (create), `GET`, and all four mutations. Always
**strong**, never `W/`. A weak tag asserts semantic equivalence rather than byte
equality and cannot carry a lost-update precondition.

**A header, never the body.** The JSON representation is unchanged — the same
seven fields, with no `version`, `rowVersion`, `xmin` or `transactionId`. A test
enumerates the property names and separately searches the body for each of those
words.

**`If-Match` required by** all four mutations. Not by create (no previous version
exists) and not by `GET`. `If-None-Match` and caching are out of scope.

| Situation | Status |
| --- | --- |
| `If-Match` absent or empty | **428 Precondition Required** |
| `*` | **400** |
| `W/"…"` | **400** |
| A list of tags | **400** |
| Not a quoted entity tag | **400** |
| Well-formed tag, not the current version | **412 Precondition Failed** |
| Well-formed tag this platform never issued | **412** |
| Current when read, superseded before the write | **412** |
| Operation contradicts the agent's state | **409 Conflict** |

**`*` is refused rather than honoured**: "if the resource exists at all" is
precisely the guarantee these endpoints must not accept, since it permits a write
computed from any earlier version.

**A well-formed tag that does not decode is `412`, not `400`.** It is a valid
precondition that does not match. `400` says the client builds broken requests;
`412` says it is out of date — and only the second describes an action that
recovers. Documented as the selected contract.

## 70. 428 / 412 / 409 distinction

- **428** — the request is well-formed; what is missing is the caller's statement
  of what it believed it was editing. Never a silent unconditional write.
- **412** — the caller's precondition was not satisfied. The operation may be
  perfectly fine; the caller has simply not seen the agent as it is. **Fixed by
  re-reading.**
- **409** — the operation contradicts the resource and would still be refused
  after a re-read. `Draft` → disable is the only case. **Not fixed by
  re-reading.**

Conflating `412` and `409` would tell a client to give up when it should retry,
or to retry when it should not. A test asserts both answers on the same agent: a
stale caller gets `412`, a current-version caller gets `409`.

## 71. Ordering, and why a token is not an access path

1. Route identifier validation (`400`)
2. `If-Match` present (`428`)
3. `If-Match` syntax (`400`)
4. Payload Domain validation (`400`)
5. **Tenant-scoped lookup (`404`)**
6. Version comparison (`412`)
7. Domain operation (`409`)

Steps 2-4 are answerable without touching the database and answer identically
whether or not the resource exists, so they cannot be used to probe. **Step 5
before step 6** is the security-relevant one: a request naming Tenant A, Agent
B's id and Agent B's *genuine current* `ETag` receives `404`, with a body
identical to an invented tag's. Answering `412` there would confirm the agent
exists.

## 72. Stale-client test results

**`AiAgentPreconditionApiTests` — 25 tests, all passing.** No race, nothing
timing-dependent.

- **`A_stale_client_cannot_overwrite_a_newer_change`** — the mandatory scenario,
  two separate clients, exactly as §64. Client A gets `412`; Client B's text is
  still in the database afterwards.
- The same scenario across **every** mutating route, including the two with no
  body.
- **`An_idempotent_operation_with_a_stale_version_is_also_refused`** — the case
  the database alone can never catch.
- A stale caller is told it is stale rather than told about the draft rule, with
  the `409` counterpart asserted alongside.
- After a `412` the caller can re-read and reapply successfully.
- Missing `If-Match` → `428` on all four routes, with the row unchanged.
- Malformed, weak, wildcard, list, unquoted and empty → `400`, row unchanged.
- Well-formed but unissued tags → `412`, row unchanged.

## 73. Database-race and cross-tenant ETag test results

**`AiAgentConcurrencyApiTests` — 6 tests, all passing.** The deterministic
competing-writer interceptor is retained; each test asserts it actually fired, so
a pass cannot be a false positive. Stale rename, instructions and lifecycle each
surface as **`412`** with the competitor's value intact. Nothing retries. Eight
parallel conditional renames yield **exactly one** `200`, the rest `412`, no
`500`, and the row holds that winner's value.

**`AiAgentTenantIsolationTests` — 14 tests, all passing.** Every crossing attempt
now presents **Agent B's genuine current `ETag`** on a Tenant A route:

- read, rename, instructions, activate, disable → **`404`**, target byte-for-byte
  unchanged (snapshot read straight from PostgreSQL).
- A cross-tenant read returns **no `ETag` at all** — no row was read, so no
  version exists to leak.
- A real tag and an invented one produce the **same status and the same body**.
- A cross-tenant mutation with no `If-Match` is still just `428`, identical to a
  non-existent agent's.
- The full matrix in both directions leaves both tenants exactly as they started.

**`AiAgentConcurrencyTests` (Infrastructure) — 7 tests**, unchanged in intent.

## 74. Final test totals

**688 defined, 688 passing, 0 failing, 0 skipped.**

| Suite | Phase 6 | First submission | Now |
| --- | --- | --- | --- |
| `OmniAI.Domain.Tests` | 288 | 288 | **288** |
| `OmniAI.Application.Tests` | 31 | 113 | **123** |
| `OmniAI.Infrastructure.IntegrationTests` | 53 | 116 | **144** |
| `OmniAI.Api.IntegrationTests` | 33 | 99 | **133** |
| **Total** | **405** | **616** | **688** |

Remediation added **72**: Application +10, Infrastructure +28, Api +34. Domain
+0.

New test classes: `AiAgentPreconditionApiTests` (25), `RowVersionTokensTests`
(19), `XminReadBackProbeTests` (2). `AiAgentRepositoryTests` grew 12 → 19,
`AiAgentTenantIsolationTests` 11 → 14, `AiAgentEndpointsTests` 40 → 47.

## 75. Remediation scope confirmations

- **Domain modified: NO.** Byte-for-byte as committed in `ce34392`.
- **Migration changed: NO.** The remediation needed no schema change — `xmin`
  already carries the required persistence state. `AddAiAgents` is untouched and
  `InitialPersistence` was never touched.
- **Packages added: NONE.** `Base64Url` is BCL.
- **No EF, Npgsql or `xmin` in Application.** Verified by the existing reflection
  guards and by inspecting the compiled assembly's references.
- **No HTTP in Domain or Application.** `428`, `412` and `409` exist only in the
  API project; the Application throws `AiAgentVersionMismatchException`, which
  names no status code.
- **No automatic retry** anywhere. Two tests assert it.
- **No generic framework**: no `IVersioned<T>`, no entity-version framework, no
  reflection-based ETag system, no conditional-request middleware, no third-party
  concurrency package.
- **No `If-None-Match`**, no caching, no RFC 9110 conditional-request engine.
- **`MicrosecondTimeProvider` retained**, as accepted. Its purpose, restated in
  the architecture document: application- and API-generated timestamps must be
  representable at the `timestamptz` precision this platform persists them at. It
  is not an AI concern.
- **No OpenAI, no provider abstraction, no RAG, no tools, no channels, no
  authentication, no frontend change, no VPS access, no commit, no push.**
- **Smart App Control unchanged** — read only, still `1`.

**One rename:** `AiAgentConcurrencyConflictException` →
`AiAgentVersionMismatchException`. The old name described a race; the type now
covers both a race and a client that was simply out of date, and "version
mismatch" is what both actually are. Transport-neutral either way.
