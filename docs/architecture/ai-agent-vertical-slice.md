# The AI agent vertical slice

Phase 7 made the accepted AI agent Domain persistable and usable: PostgreSQL
storage, a focused Application boundary, and six HTTP endpoints scoped to the
tenant that owns each agent.

Phase 6 built the aggregate. This document covers everything around it, and
nothing in it changed the Domain — the aggregate committed in `ce34392` is
byte-for-byte the one running here.

> **The tenant is not decoration in these routes.**
>
> Every operation is addressed as `/api/tenants/{tenantId}/ai-agents/{aiAgentId}`,
> and the tenant travels all the way into the SQL `WHERE` clause. An agent
> belonging to another tenant is never read, never materialised, never returned
> and never written. Knowing a valid agent id — or a valid entity tag for it — is
> not enough.

> **Every mutation is conditional.**
>
> The four mutating endpoints require `If-Match`. A database concurrency token on
> its own does not prevent a lost update: it closes a window measured in
> milliseconds, while the window that actually destroys people's work is measured
> in minutes and opens on the client. See B16.

---

## DECIDED NOW

### B1 — Storage: one relational table, scalar columns, no JSON

**Decision.** `AiAgent` maps to `ai_agents`: `id` and `tenant_id` as native
PostgreSQL `uuid`, `name` as `character varying(120)`, `instructions` as
`character varying(20000)`, `status` as `character varying(32)` holding the
member name, and `created_at` / `updated_at` as `timestamptz`. Every column is
`NOT NULL`.

**Why.** Each column length mirrors the Domain limit exactly, so the rule stays
true of the data even if something ever writes to this table without going
through the model. `status` is stored by name rather than ordinal: an integer
column would make the stored meaning depend on the declaration order of the enum,
so inserting a member later would silently reinterpret every existing row.

**Instructions are a scalar column, not JSON, and that is the load-bearing part.**
The Domain holds one piece of text whose interior formatting is meaningful.
A relational text column stores exactly that, byte for byte, line breaks
included. Serialising it as JSON would add an encoding layer that can only lose
or alter formatting, forfeit the length constraint, and encode a message shape no
provider has asked for.

**Forbids.** Serialising the aggregate as a JSON document; serialising any value
object as JSON; an integer status column; a nullable column.

### B2 — Reconstitution needed no Domain change

**Decision.** EF Core binds the aggregate's private constructor by parameter name
and writes the remaining properties through their private setters. No public
setter, parameterless constructor, EF attribute, persistence property or
`Version` field was added.

**Why.** This was checked before anything was written, not assumed afterwards:
`Conversation` already reconstitutes the same way, with get-only `Id`,
`TenantId` and `StartedAt` and private setters elsewhere. The accepted Domain
needed nothing.

A test asserts it rather than trusting it — it reloads an agent, then calls
`Disable` on the reconstituted instance and checks the lifecycle rule still
holds. If a future EF change ever forces a Domain concession, that test is where
it surfaces.

### B3 — Tenant foreign key: `RESTRICT`, never `CASCADE`

**Decision.** `fk_ai_agents_tenant` references `tenants(id)` with
`ON DELETE RESTRICT`, matching the accepted decision for conversations.

**Why.** Cascade would mean that whatever deletes a tenant row also destroys
every instruction set that tenant ever wrote — business content, not derived
data — as an invisible side effect. Tenant deletion semantics remain deferred, so
the database refuses until they are decided. This is enforcement, not
documentation: a test deletes a tenant that still owns an agent and asserts
PostgreSQL rejects it, and another asserts the applied `delete_rule` is
`RESTRICT` so a future migration cannot quietly change it.

**The foreign key is defence in depth, not the primary check.** `CreateAiAgentHandler`
verifies the tenant exists first, so an ordinary request naming a missing tenant
is answered `404` rather than surfacing as a constraint violation. The key
catches only what that check cannot: the tenant disappearing between the check
and the insert. Even then the caller is told "no tenant", never handed a
`500`.

### B4 — One index, on the tenant, justified by an access path

**Decision.** `ix_ai_agents_tenant` on `tenant_id` alone. Nothing else.

**Why.** The authorised lookup is by tenant **and** id, and that is already
optimal without a composite index: the primary key on `id` finds at most one row
and the tenant is then checked against it. A `(tenant_id, id)` index would add
write cost and win nothing.

What genuinely needs an index is the other side of the foreign key. PostgreSQL
does not index a referencing column automatically, so without this, enforcing
`fk_ai_agents_tenant` on any tenant delete would sequentially scan `ai_agents`.
It is also the index a tenant-scoped listing will use when listing is authorised.

**No uniqueness on `(tenant_id, name)`.** No Domain or product rule says an agent
name is unique. The accepted Phase 6 documentation records that no uniqueness
rule was invented, and a unique index here would impose one through the back
door — rejecting a second `Sales Assistant` for reasons nobody asked for. Two
tests guard this: one stores two identically named agents in one tenant, another
asserts no unique index mentioning `name` exists.

### B5 — Optimistic concurrency: `xmin`, unchanged from the accepted strategy

**Decision.** `ai_agents` carries the same `xmin` shadow concurrency token as
`tenants` and `conversations`. It is a shadow property in Infrastructure only.

**Why.** An agent is mutable in three independent ways — name, instructions and
status — and it is edited by people rather than by webhooks. Two operators in the
same business opening the same agent is a realistic scenario, not a theoretical
one, and a lost update to `instructions` means a business quietly running on
rules somebody thought they had replaced.

`xmin` is PostgreSQL's own per-row transaction id. It already exists on every
table, so nothing creates it; EF reads it and puts it in the `UPDATE` predicate,
and a stale writer matches no row.

**The Domain knows nothing about it.** There is no `Version` property on the
aggregate for EF's benefit, and no `xmin` in any view, response or contract.

**No automatic retry, deliberately.** Renaming an agent or rewriting its
instructions is an expression of intent about a specific version of the text.
Replaying that intent against state the operator has not seen would overwrite a
colleague's edit — exactly the loss the token exists to prevent. Whether to
retry, and with what merge rule, is a decision each use case must make once there
is a product answer to "what should happen to the other person's change". A test
asserts nothing retries.

**On its own, this was not enough — see B16.** `xmin` detects a competing write
that lands between one request's read and that same request's write. It cannot
detect an edit computed from a screen somebody opened an hour ago, because the
request carrying that edit reads current state and therefore conflicts with
nothing. B16 is what closes that.

### B6 — A new migration, beside the accepted one

**Decision.** `20260904170913_AddAiAgents`. The Phase 4 `InitialPersistence`
migration was **not** rewritten, amended or regenerated.

**Why.** It is an accepted committed baseline, and rewriting an applied migration
is how deployed environments diverge from source.

**The scaffolder emitted a physical `xmin` column again, and it was removed by
hand again** — the same edit, with the same explanatory comment, as Phase 4.
`xmin` is a system column that exists on every table and `CREATE TABLE` rejects a
user column of that name. Npgsql 10 cannot know that, so it will keep emitting
the line on every regeneration. `MigrationSchemaTests` fails if it ever survives:
it checks the generated script, the applied `public` schema, and now
`ai_agents` specifically.

### B7 — `IAiAgentRepository`: three methods, tenant in the signature

**Decision.** `FindAsync(TenantId, AiAgentId)`, `AddAsync`, `UpdateAsync`. Both
writes commit.

**Why the tenant is a required argument.** Reading an agent by identity alone is
not expressible — there is no overload, no default and no code path that offers
it. Agent ids appear in URLs, logs and client state, so a caller who knows one
must still not be able to reach it through another tenant's route. Making the
tenant a required parameter means the isolation rule cannot be forgotten at a
call site: the code would not compile.

**Still no generic repository, and a second repository existing is not a reason
to introduce one.** `IRepository<T>` would have to expose either `IQueryable` or
a specification language to stay useful, and both hand the caller the storage
model back under a different name. Nothing in the Application project mentions
`IQueryable`, `DbSet`, `DbContext`, EF Core or PostgreSQL, and a reflection test
fails the build if a reference to any of them appears.

### B8 — Still no unit of work

**Decision.** The Phase 5 repository-save transaction boundary continues.
`AddAsync` and `UpdateAsync` commit, and say so.

**Why.** Every one of the six authorised use cases mutates at most one aggregate,
and `SaveChanges` is already atomic. An `IUnitOfWork` today would be a second
abstraction that does nothing but forward a call — and would still have to be
reworked when real multi-aggregate transactions arrive, because their boundaries
are not known yet.

**The trigger for revisiting is concrete:** the first use case that must write two
aggregates, or an aggregate and an outbox, atomically.

### B9 — Cross-tenant access is not-found, and the query is what makes it so

**Decision.** A request for an agent the named tenant does not own is answered
`404`, identically to an id that exists nowhere.

**Why it is honest rather than cosmetic.** The repository puts both predicates in
the SQL. The row is not fetched by id and then compared — it is never read. So
the `404` is not a decision to hide something the process is holding; it is the
truthful result of a query that found nothing.

That distinction is the whole design. A fetch-then-compare implementation would
satisfy a behavioural test while pulling another tenant's instruction text into
this process's memory, one early return away from leaking it. A test asserts on
the recorded SQL statement itself: `tenant_id` is in the `WHERE` clause.

**Nothing distinguishes the two absences.** Status, media type and detail wording
are identical, so these endpoints cannot be used to discover that an id is real
somewhere else. Two subtleties are tested explicitly:

- **A cross-tenant `disable` of a draft answers `404`, not `409`.** The owner's
  own draft answers `409`. If the state rule were evaluated before ownership, the
  difference between the two would confirm both that the id is real and that it
  is still a draft.
- **An invalid payload aimed across the boundary answers `400` either way.**
  Validation runs before the lookup, so a probe with a deliberately bad body
  cannot use the status code to tell a real agent from an absent one.

### B10 — Explicit handlers, one per use case

**Decision.** Six handlers, plain classes with constructor dependencies,
registered by name at the composition root. No mediator, command bus, pipeline or
assembly scanning.

**Why.** A dispatcher would add indirection, reflection and a registration
convention while removing nothing, and would hide a call graph that is currently
readable in one hop.

**Ordering inside a handler is a decision, not an accident:** identifiers and
payload are validated first, then the resource is looked up. A malformed name is
wrong however the rest of the world looks and can be answered without touching
the database; whether a tenant or agent exists is a question about stored state.
So a request that is both malformed and aimed at something missing is reported as
malformed. That ordering is also what makes B9's second subtlety hold.

**Reads do not check that the tenant exists.** A request naming a tenant that is
not there finds no agent and is answered exactly like one naming a real tenant
that does not own the agent — so the read endpoints cannot be used to enumerate
tenant ids either.

### B11 — Application time: BCL `TimeProvider`, injected

**Decision.** Handlers take a `TimeProvider` and supply the instant the Domain
requires. No handler calls `DateTimeOffset.UtcNow`. `TimeProvider.System` backs
production; tests inject a deterministic instance. No third-party clock package.

**Why.** The Domain deliberately never reads the clock, so somebody has to supply
the instant, and that somebody is the use case. `TimeProvider` is the BCL
abstraction, so nothing had to be invented and nothing had to be installed.

The payoff is that exact timestamp assertions are possible end to end — and,
more importantly, that the clock can be moved **backwards**, which is the only
way to exercise the accepted high-water-mark rule: an out-of-order instant still
applies the edit, but never drags `updatedAt` back with it. That is proven
through routing, the handler, the aggregate, EF Core and PostgreSQL.

### B12 — The clock reports instants the database can store

**Decision.** The registered `TimeProvider` is `TimeProvider.System` wrapped in
`MicrosecondTimeProvider`, which truncates to whole microseconds.

**Why.** This was found by a test, not predicted. `DateTimeOffset` counts
100-nanosecond ticks; PostgreSQL `timestamptz` stores microseconds. A create
response is built from the aggregate in memory — nothing re-reads the row it just
wrote — so without this the client is handed `...0.6905973Z` while the database
holds `...0.690597Z`. Read the same resource back a moment later and its
`createdAt` has apparently changed, having never been edited. Anything comparing
the two — a cache, a diff, a "has this changed since I last looked" check — sees
a difference that does not exist.

Truncating at the source fixes it once, for every use case, in the only place
that knows the constraint exists. Truncation rather than rounding, so a recorded
instant is never later than the moment it describes. The type lives in
Infrastructure because the limit is a storage property, not a business rule: the
Domain still takes whatever instant it is handed and neither it nor the
Application knows this type exists.

**Its purpose stated plainly: application- and API-generated timestamps must be
representable at the `timestamptz` precision this platform persists them at.** It
is not an AI concern, has nothing to do with agents, and would be equally
necessary for any aggregate that records an instant. It was found here only
because this was the first slice whose create response and subsequent read were
compared for equality.

### B13 — Routes and the response contract

**Decision.** Six endpoints under `/api/tenants/{tenantId:guid}/ai-agents`:

| Method | Path | Success |
| --- | --- | --- |
| `POST` | `/` | `201` + `Location` |
| `GET` | `/{aiAgentId:guid}` | `200` |
| `PUT` | `/{aiAgentId:guid}/name` | `200` |
| `PUT` | `/{aiAgentId:guid}/instructions` | `200` |
| `POST` | `/{aiAgentId:guid}/activate` | `200` |
| `POST` | `/{aiAgentId:guid}/disable` | `200` |

**`PUT` on a named sub-resource, not `PATCH` on the agent.** Each `PUT` replaces
one addressable thing whose whole value is in the body, which is what `PUT`
means. A `PATCH` would need partial-update semantics this contract deliberately
does not define — where a missing field might mean "leave alone" or "set to
null". `PATCH` and `DELETE` are not mapped, and a test asserts both answer
`405`.

**`POST` for the lifecycle operations.** Activation and disabling are operations
rather than representations: there is nothing to send and nothing at those paths
to read back, so modelling them as state resources would invent a document that
does not exist.

**All four mutations return `200` with the full representation** rather than
`204`. Each changes `updatedAt` and two change `status`, so a client would
otherwise have to issue a second `GET` to learn the result of its own request,
and would meanwhile be showing state it inferred rather than state the server
confirmed. It is also what makes an idempotent call informative: activating an
already active agent answers `200` with an unchanged `updatedAt`, which is the
truthful account of a request that changed nothing.

**The response carries seven fields and no persistence metadata.** No `xmin`, no
row version, no model, provider, temperature, token limit or external assistant
id. `status` is a stable name produced by an explicit `switch` with a throwing
default arm — never `ToString()`, never a serialiser setting, never an integer
ordinal. A test enumerates the JSON properties and fails if the set changes.

### B14 — Lifecycle endpoint semantics

`Draft → Active`, `Active → Disabled`, `Disabled → Active`. Repeat calls are
idempotent `200`s that write nothing.

**`Draft → Disabled` answers `409 Conflict`**, and `409` rather than `400` is the
decision. Nothing about the request is malformed — the same request succeeds once
the agent has been activated — so the conflict is with the state of the resource
rather than with what the caller wrote. `422` was considered and rejected for the
same reason: the content is perfectly processable, it just contradicts the
resource as it stands. The refusal happens before any save, so nothing partial is
left behind.

**Activation is Domain lifecycle activation only.** No provider is contacted, no
model selected, no channel published to, no credential read. There is no
readiness guard, and that is the accepted Phase 6 decision carried forward: an
agent cannot exist through public Domain APIs with an invalid name or
instructions, so such a guard could not fail. Provider, channel and knowledge
readiness belong to publishing orchestration, and will be written when those
concepts exist rather than stubbed now.

### B15 — Error mapping, in one place

| Outcome | Status |
| --- | --- |
| `DomainValidationException` (incl. empty route GUID) | `400` |
| Malformed request body | `400` |
| Tenant not found | `404` |
| Agent not found within tenant | `404` |
| Cross-tenant agent access | `404` |
| `DomainStateException` | `409` |
| Tenant slug already exists | `409` |
| `AiAgentConcurrencyConflictException` | `409` |
| Anything unrecognised | `500`, empty body |

All RFC 9457 `application/problem+json`.

**The handler was renamed from `TenantProblemDetailsHandler` to
`DomainProblemDetailsHandler`.** It never was tenant-specific —
`DomainValidationException` is raised by every value object in the model — and
now it serves AI agents too. The alternative was a second handler duplicating the
shared arms, which could not have worked: nothing in a `DomainValidationException`
says which aggregate raised it.

**Two new Application exception types, both transport- and provider-neutral.**
`TenantNotFoundException` and `AiAgentConcurrencyConflictException`. Phase 5
deliberately had no not-found type, and that decision was about *queries* — a
lookup that finds nothing returns `null`. A command whose precondition failed has
no "no result" to return in place of the thing it was asked to make, which is a
genuinely different outcome.

**Nothing internal is ever disclosed.** Infrastructure translates provider
failures before they cross the boundary, so no `Npgsql` type, SQLSTATE,
constraint name, table name, SQL text or stack frame can reach a body. Tests
assert the absence of each, including `xmin`, `xid`, `ai_agents` and
`DbUpdate` on the conflict path.

### B16 — Two kinds of concurrency, and why the second one needs `If-Match`

**The distinction this whole design turns on.**

**Server-request concurrency** is the window inside one request: the server reads
a row, computes a change, and writes it back. Another transaction can commit in
between. That window is milliseconds wide, and `xmin` alone closes it (B5).

**Client think-time concurrency** is the window that belongs to a person. Someone
opens an agent, reads its instructions, goes to a meeting, comes back and saves.
That window is minutes or hours wide, and **`xmin` alone does nothing about it**:

1. Client A reads the agent — version V1.
2. Client B reads the same agent — version V1.
3. Client B saves. The database is now at V2.
4. Minutes later, Client A submits an edit computed from its V1 screen.
5. The server handling Client A **reads the current row, V2**.
6. It applies A's stale input to V2 and saves. The write carries V2, so it
   succeeds.
7. **Client B's change is gone, and nobody is told.**

Nothing in step 6 is a race. The request is orderly, the token is current, and
the loss is total. Closing this needs the *client* to state which version it was
looking at, which is what `If-Match` is for.

**Decision.** Every AI agent mutation is a conditional request. The client sends
`If-Match` carrying the `ETag` it last saw; the server refuses the write unless
the resource is still at that version.

### B17 — The concurrency token: opaque above Infrastructure

```
PostgreSQL xmin  ->  Infrastructure  ->  ConcurrencyToken  ->  HTTP ETag
   (uint)           (encode/decode)        (opaque)          ("AAAwOQ")
```

**Decision.** `ConcurrencyToken` is a single-field readonly record struct in
`OmniAI.Application.Concurrency`. The Application passes it around and compares
it for equality. It never parses one, never does arithmetic on one, and has no
idea what is inside.

`RowVersionTokens` in Infrastructure is the entire boundary: it encodes the four
bytes of `xmin`, big-endian, as unpadded base64url, and decodes them back.
Big-endian so the value does not depend on the machine that produced it;
base64url so it is header-safe without escaping and contains no quote, backslash,
comma or whitespace — asserted for 4096 consecutive values.

**One type, not a framework.** No `IVersioned<T>`, no generic entity-version
abstraction, no reflection, no middleware, no third-party package. AI agents are
the only resource with conditional requests today. The type is written generically
because a version marker genuinely has no aggregate-specific content, not because
a second user is planned.

**Not in the Domain.** An agent's identity, name, instructions and lifecycle are
business facts; which stored revision somebody happened to read is not.
`AiAgent` gained nothing — no `Version`, no `ETag`, no `xmin`, no
`ConcurrencyToken`.

**Opaque, not secret.** It is handed to clients and comes straight back. Opacity
means clients cannot build logic on the internals and then break when the encoding
changes, and the header does not advertise the storage engine. Deliberately **no
HMAC and no encryption**: signing a value that is not a secret would add key
management and rotation in exchange for an assurance nothing needs. A forged
token cannot do anything a real one could not — the worst outcome is a write
refused.

### B18 — The expected version reaches the database, and is also checked in front of it

Both, and each catches something the other cannot.

**In the repository, the caller's version becomes the SQL predicate.**

```csharp
version.OriginalValue = expectedRowVersion;   // not the version this request loaded
await _context.SaveChangesAsync(ct);          // UPDATE ... WHERE xmin = @expected
```

Without this substitution, EF would use the version *this request* loaded — which
is current by construction — and the update could never conflict. That is exactly
the step-6 bug above. A test proves it directly: it loads the current row, still
presents a stale version, and asserts the write is refused and the other writer's
value survives.

**In the use case, the version is compared before the aggregate is touched.**
The database check cannot cover a mutation that writes *nothing*: activating an
already active agent changes no property, so no statement is issued and there is
no predicate to fail. Without an explicit check, a caller holding a hopelessly
stale version could send that request and get a cheerful `200` — a precondition
silently skipped because the operation happened to be a no-op. HTTP preconditions
are evaluated against the resource, not against whether the request would have
changed it.

It runs **before** the Domain operation, so a stale caller is told it is stale
rather than told about a rule describing a state it has never seen.

### B19 — The HTTP contract: `ETag`, `If-Match`, and 428 / 412 / 409

**`ETag` is returned by** `POST` (create), `GET`, and all four mutations — always
strong, never `W/`. A weak tag asserts semantic equivalence rather than byte
equality, which cannot carry a lost-update precondition. Mutations return the new
version so consecutive edits chain without re-reading.

**It is a header, never the body.** The JSON representation remains the accepted
seven fields; no `xmin`, `version`, `rowVersion` or `transactionId` appears in it.
That is why the version travels *beside* the view through the Application rather
than inside it — `AiAgentView` is what becomes the body, so a version folded into
it would be one serialisation away from the wire.

**`If-Match` is required by** the four mutations. Creation does not require it —
there is no previous version of a resource that does not exist. `GET` does not
require it, and `If-None-Match` caching is out of scope.

| Situation | Status |
| --- | --- |
| `If-Match` absent or empty | **428 Precondition Required** |
| `*`, `W/"…"`, a list, or not a quoted tag | **400 Bad Request** |
| Well-formed tag that is not the current version | **412 Precondition Failed** |
| Well-formed tag this platform never issued | **412 Precondition Failed** |
| Version current when read, superseded before the write | **412 Precondition Failed** |
| Operation contradicts the agent's state (`Draft` → disable) | **409 Conflict** |

**Why these three are kept apart:**

- **428** — the request is well-formed; what is missing is the caller's statement
  of what it believed it was editing. Not `400`, and never a silent unconditional
  write.
- **412** — the caller's precondition was not satisfied. The operation may be
  perfectly fine; the caller simply has not seen the agent as it is. **Fixed by
  re-reading.**
- **409** — the operation contradicts the resource and would still be refused
  after a re-read. **Not fixed by re-reading.**

Conflating `412` and `409` would tell a client to give up when it should retry, or
to retry when it should not.

**`*` is refused rather than honoured**, because "if the resource exists at all"
is precisely the guarantee these endpoints must not accept — it would permit a
write computed from any earlier version, which is the loss this exists to prevent.

**A well-formed tag that does not decode is `412`, not `400`.** It is a valid
precondition that does not match. `400` says the client builds broken requests;
`412` says it is out of date — and only the second describes an action that
recovers. Parsing is deliberately small: one strong tag, no RFC 9110 engine.

### B20 — Ordering, and why a token is not an access path

Each mutation resolves in this order, and the order is a security decision:

1. Route identifier validation (`Guid.Empty` → `400`)
2. `If-Match` present (`428` if not)
3. `If-Match` syntax (`400` if malformed)
4. Payload Domain validation (`400`)
5. **Tenant-scoped lookup (`404` if absent)**
6. Version comparison (`412` if stale)
7. Domain operation (`409` if refused)

Steps 2–4 are transport and input concerns answerable without touching the
database, and they answer identically whether or not the resource exists — so
they cannot be used to probe.

**Step 5 before step 6 is the important one.** A request naming Tenant A, Agent
B's id and **Agent B's genuine current `ETag`** receives `404` — the same answer
as an invented tag, with the same body. A version marker is a statement about
what the caller last saw, never a capability, so it must not become an
alternative route to somebody else's agent; and answering `412` there would
confirm the agent exists. A cross-tenant read returns no `ETag` at all, because
no row was read.

### B21 — What the frontend must do

Not yet built, but the contract binds it:

- **Keep the `ETag`** from the `GET` (or from the create/mutation response that
  produced the state on screen) alongside the data it is showing.
- **Send it as `If-Match`** on every save. A request without it is refused with
  `428`.
- **Use the `ETag` from a successful mutation** as the basis for the next edit —
  no re-read needed.
- **On `412`**, tell the user somebody else changed the agent, re-read, and let
  them decide. Do not retry silently: that is the lost update wearing a different
  hat.
- **Treat the value as opaque.** Do not parse, compare for ordering, or persist
  it as anything but a string.

---

## DEFERRED

Recorded as absent. None of this is implemented, and nothing below is described
anywhere as if it were.

| Area | Deferred |
| --- | --- |
| Authentication | Every endpoint is unauthenticated |
| Authorisation | No permissions, roles or policies |
| Current tenant context | The route tenant is supplied by the caller and believed |
| Platform admin | No cross-tenant operator role |
| Listing | No list, search, filter, sort or pagination |
| Deletion | No delete, archive, soft delete or retention rules |
| Prompt versioning | Updating replaces the text; the previous version is gone |
| AI providers | OpenAI, Anthropic, any SDK, client, API key, model selection or routing |
| Model configuration | Model name, temperature, top-p, max tokens, response format |
| RAG | Embeddings, vector storage, knowledge sources, retrieval |
| Tools | Tool definitions, function calling, execution |
| Channels | WhatsApp, Instagram, Messenger, web chat, assignment or publication |
| Human handoff | Escalation rules, routing, availability |
| Evaluations | Quality scoring, regression testing, A/B comparison |
| Cost limits | Budgets, quotas, rate limits, usage accounting |
| Frontend | No agent management screens (the ETag obligations in B21 apply when there are) |
| Conditional reads | No `If-None-Match`, no `304`, no caching |
| Conditional requests elsewhere | Tenant endpoints stay unconditional; only AI agents use `If-Match` |
| Retry policy | Nothing retries a failed precondition; merge rules are undecided |

---

## Security status

**These endpoints are not authorised for public production exposure.**

Authentication and authorisation do not exist. The tenant id in the route is
supplied by the caller and believed, because there is no authenticated principal
to check it against.

Tenant scoping is therefore a **correctness** boundary, not yet a **security**
boundary. It is fully enforced and fully tested — no request can read or change
an agent through another tenant's route — but nothing yet stops a caller from
naming whichever tenant it likes. A future authentication phase will bind an
authenticated principal to the tenant ids it may use; the scoping built here is
what that phase will attach to.

No placeholder API key, bearer token, fake JWT or admin secret exists anywhere in
this slice. A fake control would hide the gap rather than close it.

---

## Things a reviewer can check independently

1. `git diff HEAD --stat -- backend/src/OmniAI.Domain` is empty. The Domain did
   not change.
2. `git status --short backend/src/OmniAI.Infrastructure/Persistence/Migrations/20260904124546_InitialPersistence.cs`
   is empty. The accepted migration was not touched.
3. `grep -rn "xmin" backend/src/OmniAI.Domain backend/src/OmniAI.Application` returns nothing.
4. `grep -rni "openai\|anthropic\|temperature\|top_p\|model_id" backend/src` returns
   only prose stating what is absent.
5. `Directory.Packages.props` is unmodified and no `.csproj` gained a
   `PackageReference`.
6. `AiAgentRepositoryTests.The_tenant_filter_is_in_the_sql_not_applied_after_loading`
   asserts on the recorded statement, not on the result.
7. `AiAgentTenantIsolationTests` snapshots every stored column straight from
   PostgreSQL before and after each crossing attempt.
8. `MigrationSchemaTests` fails if `xmin` ever appears as a user column.
9. `AiAgentConcurrencyApiTests` asserts the competing write actually fired, so a
   passing conflict test cannot be a false positive.
10. `AiAgentTimeApiTests` moves the clock backwards; a handler reading
    `DateTimeOffset.UtcNow` would fail it.
11. `AiAgentPreconditionApiTests.A_stale_client_cannot_overwrite_a_newer_change`
    reproduces the lost update with two clients and no race at all.
12. `AiAgentRepositoryTests.The_callers_expected_version_is_what_the_database_checks`
    loads the current row and still fails -- the proof that the caller's version,
    not the loaded one, is in the SQL predicate.
13. Searching the Domain for `ConcurrencyToken`, `ETag` or `xmin` returns nothing.
14. `AiAgentTenantIsolationTests` presents Agent B's genuine current `ETag` on a
    Tenant A route and asserts `404`, never `412`.
15. `RowVersionTokensTests` asserts the token stays within the base64url alphabet
    for 4096 consecutive values, so it can never break a header.
