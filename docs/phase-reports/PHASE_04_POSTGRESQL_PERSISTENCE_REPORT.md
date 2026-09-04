# Phase 4 — PostgreSQL & Persistence Foundation

**Date:** 2026-09-04
**Revision:** 2 — Phase 4 review required one remediation, and this report
records it. The message ingestion-order mechanism was replaced so that appending
a message no longer reads a conversation's message history (§0, §20). Every other
Phase 4 decision was accepted and is unchanged.
**Scope:** Relational persistence for `Tenant`, `Conversation` and `Message`. No
application CRUD, no endpoints, no product features.

---

## 0. Phase 4 remediation

The Phase 4 review accepted the implementation except for one scalability defect
and required it fixed before acceptance.

**What the review rejected.** The persisted ingestion order was the message's
index in its conversation's loaded collection, and `OmniAiDbContext` threw if the
history had not been loaded. That makes appending one message require reading the
entire transcript. A production conversation may hold tens of thousands of
messages, so the normal write path would get slower for the rest of that
conversation's life.

**What changed.** Five files, and nothing else:

| File | Change |
| --- | --- |
| `Persistence/OmniAiDbContext.cs` | `PendingMessagesInAppendOrder` + sequence allocation replace the index lookup and the "history not loaded" exception. `OnModelCreating` declares the sequence. |
| `Persistence/PersistenceSchema.cs` | Added `MessageIngestionSequenceName`; rewrote the rationale for the ordering value. |
| `Persistence/Configurations/MessageConfiguration.cs` | Comment rewritten; the unique index's justification changed from *policing the allocator* to *protecting against future direct writes*. Column shape unchanged. |
| `Persistence/ConversationLoading.cs` | Documentation only: it is now explicitly the **read** path, and the write path does not use it. |
| `Persistence/Migrations/*` | Regenerated cleanly (§24), adding `CREATE SEQUENCE`. |

**What did not change:** PostgreSQL, EF Core/Npgsql in Infrastructure, Domain
independence, Application's EF independence, typed ids as native `uuid`, value
objects as scalar columns, enums stored by name, `timestamptz`/UTC, slug
uniqueness, the FK and delete decisions, `xmin` optimistic concurrency, RLS
remaining disabled and under evaluation, real PostgreSQL integration tests,
`Message.SentAt` independence from ingestion order, and T1/T3/T2 surviving as
T1/T3/T2.

**Requirement by requirement:**

| # | Non-negotiable requirement | Where it is satisfied |
| --- | --- | --- |
| 1 | A new message persists without loading existing history | `A_message_is_appended_without_loading_the_existing_history`; the shared `AppendInOwnTransactionAsync` helper asserts `loaded.Messages` is empty before appending, so **seven** tests append without a history load — six in `MessagePersistenceTests` and one in `ConcurrencyTests` |
| 2 | Allocation depends on none of `Messages.Count`, a loaded-history index, unguarded `MAX(seq)+1`, `SentAt`, `MessageId`, UUIDv7 order, or EF batch order | `nextval` supplies every value; stored history contributes nothing, not even an offset (§20) |
| 3 | `Message.SentAt` stays the Domain-supplied instant | `Out_of_order_timestamps_are_not_rewritten_by_persistence`, and `Messages_reload_in_ingestion_order_not_in_timestamp_order` asserts the instants are `T1, T3, T2` and explicitly **not** `T1, T2, T3` |
| 4 | Persisted ingestion order is deterministic | Verified by three consecutive full runs, 45/45 each |
| 5 | Out-of-order event times remain valid | Same two tests, plus the cross-transaction variant |
| 6 | Safe for future multiple workers | `nextval` is atomic, lock-free and never repeats — `The_allocator_is_a_real_postgresql_sequence` |
| 7 | Ordering metadata stays Infrastructure state | Shadow property; **no Domain change** (§34) |
| 8 | `MessageId` remains Domain identity | `Message_identity_is_still_the_primary_key` |
| 9 | Gaps acceptable | `A_gap_left_by_a_rolled_back_append_does_not_disturb_later_order` |
| 10 | No complex distributed sequencing service | One `CREATE SEQUENCE` and one `nextval` per save |

**Domain:** unchanged, byte-for-byte, from the accepted Phase 3 checkpoint. No
`IngestionSequence` was added to `Message`; it is persistence metadata, not a
business concept, and nothing in the Domain reads it.

## 1. Phase objective

Introduce the first real persistence for the accepted Domain using EF Core,
PostgreSQL and Npgsql: a DbContext, focused Fluent mappings, the initial
migration, strongly typed id and value-object conversion, aggregate
reconstitution, database constraints, optimistic concurrency, a deterministic
persisted message ingestion order, and integration tests against a real
PostgreSQL server.

## 2. Final status

**PASS WITH WARNINGS — remediation applied**

Every authorised Phase 4 objective is implemented and verified against real
PostgreSQL 17.6. One environmental warning, and two ingestion-order designs that
were replaced — the second on review:

1. **`OmniAI.Domain.Tests` (172 tests) cannot execute on this machine.** Smart
   App Control blocks loading the test assembly. This is the risk recorded in the
   Phase 1 report §19.1 materialising, not a Phase 4 regression: the Domain layer
   and its tests are byte-identical to the accepted Phase 3 checkpoint (§34).
   All 47 executable tests pass.
2. **The first ingestion-order mechanism was wrong and was replaced.** A
   database identity column produced scrambled history because EF Core does not
   guarantee insert order within a batch. Found by a flaky test, diagnosed, and
   fixed (§20).
3. **The second was scalable-wrong and was replaced on review.** Reading the
   message's index out of the aggregate's collection was correct only if the whole
   history had been loaded, so the write path had to read every stored message
   before appending one. Replaced with a PostgreSQL sequence (§0, §20).

## 3. Baseline commit

```
e714005  feat: establish conversation and message domain
```

The accepted Phase 3 checkpoint, confirmed present at recovery. Phases 1–3 were
preserved intact; no accepted file was reverted or recreated.

## 4. Initial recovered repository state

This session began by recovering state rather than assuming it. `pwd`,
`git status`, `git status --short`, `git log --oneline --decorate -10`,
`git diff --check`, `git diff --stat` and `git diff` were all run before any
modification, and `docs/phase-reports/`, `docs/architecture/`, `backend/` and
`frontend/` were inspected.

Found:

- 3 commits, `HEAD -> master` at `e714005`, **0 remotes**.
- **The Phase 3 checkpoint had been created** before the interruption.
- Uncommitted Phase 4 work already present: modified
  `backend/Directory.Packages.props` and
  `backend/src/OmniAI.Infrastructure/OmniAI.Infrastructure.csproj`; untracked
  `backend/dotnet-tools.json` and
  `backend/src/OmniAI.Infrastructure/Persistence/`.
- No `PHASE_04_POSTGRESQL_PERSISTENCE_REPORT.md` and no
  `persistence-postgresql.md`.
- No persistence integration tests: `OmniAI.Infrastructure.IntegrationTests`
  contained only the Phase 1 architecture guard.
- An ephemeral PostgreSQL cluster left over from the interrupted session,
  outside the repository, with a **stale `postmaster.pid`** (its recorded pid was
  dead) and nothing listening.

**Classification: STATE C** — Phase 3 checkpointed, Phase 4 partially started
with uncommitted work.

## 5. Whether this session resumed partial existing work

**Yes.** Nothing was recreated. The pre-existing DbContext, converters, three
entity configurations, `ConversationLoading`, the design-time factory and the
local tool manifest were all kept and built upon.

Work added in this session: the integration-test suite, the EF Core version pin,
the corrected ingestion-order mechanism, a regenerated migration, the
architecture document and this report.

One pre-existing file was materially reworked rather than accepted: the
ingestion-order design (§20), because testing showed it did not hold.

## 6. Packages added

| Package | Version | Project | Reason |
| --- | --- | --- | --- |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | **10.0.3** | `OmniAI.Infrastructure` | The PostgreSQL provider for EF Core. Latest 10.x, matching the .NET 10 target. |
| `Microsoft.EntityFrameworkCore.Design` | **10.0.11** | `OmniAI.Infrastructure` | Required by `dotnet ef migrations`. `PrivateAssets=all`, so it is design-time only and never ships. |
| `Microsoft.EntityFrameworkCore` | **10.0.11** | *(central pin)* | Not a new reference — pinned so every project resolves one EF Core version. Without it Infrastructure compiled against 10.0.11 (pulled by Design) while its own test project resolved 10.0.4 (pulled by the provider), which failed the build with CS1705. |
| `Microsoft.EntityFrameworkCore.Relational` | **10.0.11** | *(central pin)* | Same reason, one layer down. |

All four are declared in `backend/Directory.Packages.props` under Central Package
Management; project files reference packages without versions.

**No other package was added.** In particular: no Testcontainers (§26), no
strongly-typed-id library, no naming-convention package, no Result library.

**Tooling:** `dotnet-ef` **10.0.11** installed as a **local** tool via
`backend/dotnet-tools.json`, not globally. A manifest is reproducible and
version-pinned per repository, and it avoids mutating the machine's global tool
set — invoked as `dotnet dotnet-ef`.

## 7. EF Core version

**10.0.11** (runtime, Relational and Design all pinned to it).

## 8. Npgsql provider version

**Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3**, which brings `Npgsql` itself
transitively.

## 9. PostgreSQL test version

**PostgreSQL 17.6** (x86_64-windows), verified with `postgres --version` and
`psql --version`.

## 10. DbContext design

`OmniAI.Infrastructure/Persistence/OmniAiDbContext.cs` — `sealed`, primary
constructor taking `DbContextOptions<OmniAiDbContext>`.

- Two sets only: `Tenants` and `Conversations`. **`Message` has no `DbSet`** —
  it belongs to a conversation, and exposing it independently would route around
  the aggregate that enforces its rules. No speculative sets for entities that do
  not exist.
- `OnModelCreating` does one thing: `ApplyConfigurationsFromAssembly`. No god
  configuration method.
- `SaveChanges`/`SaveChangesAsync` are overridden for exactly one purpose:
  stamping message ingestion positions (§20).
- Lives in Infrastructure only. Not referenced by Domain, Application or the API.

## 11. Tenant mapping

`TenantConfiguration` → table `tenants`.

| Column | Type | Notes |
| --- | --- | --- |
| `id` | `uuid` | PK, `ValueGeneratedNever` — ids come from the Domain |
| `name` | `character varying(200)` | not null, length = `TenantName.MaxLength` |
| `slug` | `character varying(63)` | not null, length = `TenantSlug.MaxLength` |
| `status` | `character varying(32)` | not null, member name |
| `xmin` | `xid` | concurrency token, system column, not created |

Index: `ix_tenants_slug` **unique** on `slug`.

Column lengths mirror the Domain limits deliberately: the application already
rejects longer values, and the column makes the rule true of the data even if
something ever writes to the table without going through the model.

## 12. Conversation mapping

`ConversationConfiguration` → table `conversations`.

| Column | Type | Notes |
| --- | --- | --- |
| `id` | `uuid` | PK, `ValueGeneratedNever` |
| `tenant_id` | `uuid` | not null, FK → `tenants.id` |
| `channel` | `character varying(32)` | not null, member name |
| `status` | `character varying(32)` | not null, member name |
| `started_at` | `timestamp with time zone` | not null |
| `last_activity_at` | `timestamp with time zone` | not null |
| `xmin` | `xid` | concurrency token |

- FK `fk_conversations_tenant`, **`OnDelete(Restrict)`** — see §19.
- Index `ix_conversations_tenant_last_activity` on
  `(tenant_id, last_activity_at)`: every realistic query is tenant-scoped, and
  this is also the index that makes a tenant's conversation list cheap.
- The messages relationship uses `HasField("_messages")` with
  `PropertyAccessMode.Field`, so EF populates the private list rather than going
  through the read-only view.

Note the brief's field list says `CreatedAt`; the accepted Phase 3 aggregate
names it `StartedAt`. The Domain name was kept rather than renamed to match the
brief's wording — the column is `started_at`.

## 13. Message mapping

`MessageConfiguration` → table `messages`.

| Column | Type | Notes |
| --- | --- | --- |
| `id` | `uuid` | **PK** — `MessageId` remains the identity |
| `conversation_id` | `uuid` | not null, FK → `conversations.id`, cascade |
| `sender` | `character varying(32)` | not null, member name |
| `content_type` | `character varying(32)` | not null, member name |
| `text` | `character varying(32000)` | not null, length = `Message.MaxTextLength` |
| `sent_at` | `timestamp with time zone` | not null |
| `ingestion_sequence` | `bigint` | not null, shadow property (§20) |

- FK `fk_messages_conversation`, **`OnDelete(Cascade)`** — messages are inside
  the conversation aggregate and have no meaning without it.
- Index `ix_messages_conversation_ingestion` **unique** on
  `(conversation_id, ingestion_sequence)`.
- **No concurrency token**: a message is immutable once written, so there is no
  update to lose.

## 14. ID persistence

Three explicit `ValueConverter`s in `PersistenceConverters.cs`, each mapping the
typed id to `Guid`, stored as native PostgreSQL **`uuid`**. Never text — asserted
by a test that reads `information_schema.columns`.

Written out by hand rather than produced by a reflection-driven convention: the
point of the typed ids is that substituting one for another is a compile error,
and a generic "anything wrapping a Guid" mapper quietly gives that away. Five
converters, two lines each.

`ValueGeneratedNever()` on every key, because ids come from the Domain's UUIDv7
factories and the database must not invent them.

## 15. Value-object persistence

`TenantName` and `TenantSlug` convert to `string` and map to
`character varying` columns — **not JSON**. A JSON column would forfeit the
length constraint, the unique index the slug needs, and plain readability. A test
asserts both column types are `character varying` and that the stored value is
the plain string.

Reads go back through the Domain factory, so a stored value violating a Domain
rule fails loudly on load instead of entering the model. The factories normalise,
but stored values are already canonical, so a round trip cannot alter persisted
data.

## 16. Enum / status persistence

All five enums — `TenantStatus`, `ConversationStatus`, `ChannelKind`,
`MessageSender`, `MessageContentType` — are stored **by member name** as
`character varying(32)`, via `HasConversion<string>()`.

Deliberately **not** integers. Storing ordinals would make the compiler's
numbering a permanent database contract: reordering members or inserting one
would silently re-interpret existing rows. Names also keep the tables legible to
someone reading them directly during an incident.

Tests assert the stored text for every value: all four channel kinds, all three
text senders, `Suspended`, `Closed`, and `System` content.

## 17. Timestamp strategy

`timestamp with time zone` (`timestamptz`) for all three instants, holding an
absolute point in time.

The Domain already normalises every instant to UTC (architecture decision 11), so
nothing depends on the server's or the client's time zone, and no local time is
ever stored. A test supplies a `+03:00` value and asserts it returns as the same
instant with a zero offset, and another asserts the column type from
`information_schema`.

## 18. Slug uniqueness

`ix_tenants_slug`, **unique**, on `tenants.slug` — the guarantee the Domain
deliberately could not make on its own (tenancy ADR D3).

A plain unique index is exact here because slugs are already canonical lowercase
before reaching persistence. No `citext` extension and no `lower()` expression
index: those would solve a problem the Domain has already removed.

Verified by a test that inserts a second tenant with the same slug and asserts
`DbUpdateException` naming `ix_tenants_slug`.

## 19. Relational tenant safeguards

Implemented:

- **`conversations.tenant_id` is non-null with a foreign key** to `tenants.id`.
  A conversation referencing a nonexistent tenant is rejected — tested, by
  constraint name.
- **`OnDelete(Restrict)`** on that FK. Deleting a tenant that still owns
  conversations is refused — tested. Restrict rather than Cascade because
  erasing a tenant's entire history is a deliberate operation with rules that do
  not exist yet, so the database refuses until they do. EF's default made Cascade
  the easy choice; it was rejected on purpose.
- **Tenant-scoped index** `(tenant_id, last_activity_at)`.
- **Cascade** from conversation to message, which is correct in the other
  direction: messages are aggregate-internal.

**Deliberately not implemented — no persistence-only `tenant_id` on `messages`.**
Evaluated as the brief asks, and rejected: a message reaches its tenant through
exactly one conversation, and `messages.conversation_id` is a non-null FK to a
conversation with exactly one non-null `tenant_id`. The tenant of a message is
therefore already single-valued and already enforced. A duplicated column would
add a second copy of a fact the schema cannot contradict, plus the obligation to
keep the two in step. A composite `(tenant_id, conversation_id)` constraint was
evaluated for the same purpose and rejected on the same grounds — it would guard
a mismatch the simpler single FK already makes unrepresentable. The brief's own
instruction applies: *"If a simpler constraint provides the same invariant, prefer
it."*

Recorded as a decision to **revisit, not reverse**, if tenant-scoped indexes or
RLS later require `tenant_id` on every tenant-owned table.

**What is still missing** and is stated plainly in the ADR: there is no request
tenant context, no query scoping and no RLS, so an unscoped tenant-owned query is
currently *incorrect* rather than *impossible*. Tenancy invariant 1 is not yet
satisfied.

## 20. Message ingestion-order design

The most subtle part of this phase. Two mechanisms were implemented and rejected
before the current one, and both are recorded because each was rejected for a
reason worth keeping.

Phase 3 established that `Message.SentAt` is not monotonic and that history is
kept in ingestion order. Reload therefore cannot `ORDER BY sent_at`, and the
ordering value has to be persisted separately.

### Rejected designs

1. **`ORDER BY sent_at`** — rejected by definition; it destroys the accepted
   guarantee.
2. **`ORDER BY id`** — rejected. `MessageId` is a UUIDv7, time-ordered only to
   the millisecond with random bits below. Messages appended in the same
   millisecond sort arbitrarily. **Observed, not assumed**: a failure showed
   three ids sharing the prefix `01a06c54-9344-7…`.
3. **A database identity column** — implemented, then **rejected on evidence**.
   EF Core gives no guarantee about the order in which it inserts rows of the
   same type within one batch, so the generated numbers did not follow append
   order. Reloaded history came back as `3rd, 1st, 2nd`. It passed when run alone
   and failed in the full suite, which is exactly how a latent ordering bug
   behaves.
4. **The message's index in the aggregate's collection** — implemented, passed
   every test written for it, and **rejected on review**. It was correct only
   while the collection held the whole history, so the context required a
   full-history load before an append and threw otherwise. That makes the cost of
   appending grow with the length of the conversation, which is unacceptable for a
   production chat platform. Two alternatives were considered and also rejected:
   `SELECT MAX(ingestion_sequence) + 1`, which is a lost-update race unless it
   takes a lock, and a per-conversation counter column advanced with
   `UPDATE … RETURNING`, which would write the conversation row on every append,
   bump that row's `xmin`, and make unrelated concurrent writers fail their own
   concurrency check.

### What is implemented

A PostgreSQL **sequence**, `message_ingestion_sequence`, declared on the model
(`modelBuilder.HasSequence<long>`) so the migration creates it.
`OmniAiDbContext` allocates one value per new message immediately before insert,
in a single round trip:

```sql
SELECT nextval('message_ingestion_sequence')::bigint AS "Value"
FROM generate_series(1, @count)
```

The values are sorted in memory and stamped onto the pending rows, lowest to
earliest append, so nothing depends on the order PostgreSQL emitted the rows in.
If the count returned ever failed to match the rows pending, the save is refused
rather than written in an undefined order.

The division of labour is the whole design: **the caller's append order** supplies
the relative order of the rows being written now, and **PostgreSQL** supplies the
absolute value. Stored history supplies nothing. `PendingMessagesInAppendOrder`
therefore filters each tracked conversation's collection down to the rows being
inserted rather than reading positions within it — which is why it behaves
identically whether the history is loaded or not, and why the write path can skip
loading it.

Against the brief's requirements:

| Requirement | Status |
| --- | --- |
| Append without loading history | **Yes** — proven by recorded SQL against a 500-message conversation |
| No dependency on `Count`, loaded index, unguarded `MAX+1`, `SentAt`, `MessageId`, UUIDv7 or EF batch order | Yes — the value comes from `nextval` |
| `MessageId` remains Domain identity | Yes — and remains the primary key, asserted by a test querying `pg_index` |
| Metadata stays Infrastructure-only | Yes — shadow property; **no Domain change** |
| Deterministic order | Yes — three consecutive full runs |
| Out-of-order `SentAt` preserved | Yes — T1/T3/T2 reloads as T1/T3/T2, instants unchanged |
| Safe for concurrent workers | Yes — `nextval` is atomic, lock-free, never repeats |
| No complex distributed sequencing service | Yes — one sequence, one `nextval` per save |
| Gaps acceptable | Yes — and now expected; a rolled-back transaction burns its value |

### Characteristics worth stating plainly

- **Values are global, not per-conversation.** Within one conversation they are
  sparse. Only the relation between them is meaningful, and nothing reads them as
  a count or a position.
- **Gaps are expected and harmless.** Sequences do not roll back. That is the
  price of not locking, and it is the right trade: a gap costs nothing, a lock
  costs throughput.
- **Concurrency-compatible with `xmin`.** Allocation reads a sequence and touches
  no aggregate row, so it neither reads nor advances any concurrency token. A
  stale append still fails with `DbUpdateConcurrencyException`, and still fails as
  one transaction — the rejected conversation update takes the message insert
  down with it. **No automatic retry was added**; retry policy is an Application
  concern for a later phase.
- **`ix_messages_conversation_ingestion` remains unique** on
  `(conversation_id, ingestion_sequence)`. The allocator cannot produce a
  duplicate, so this is no longer there to police it — it is there so that any
  future code path setting the column directly cannot make a conversation's order
  ambiguous without the write failing. Tested with a raw SQL insert that reuses a
  stored value and expects `PostgresException`.

### Reading is a separate path

`ConversationLoading.FindConversationWithMessagesAsync` loads a conversation and
its history with an explicit `ORDER BY ingestion_sequence`, because EF Core cannot
express an ordered `Include`. It exists for reconstitution, which genuinely needs
the whole transcript. **The write path does not use it.** Loading complete
history and loading the state required to append are two different operations,
and the review was right that conflating them was the defect. It remains a query
helper, not a repository: no interface, no injection, no abstraction over the
context (§33).

## 21. Concurrency strategy

**PostgreSQL `xmin`, as a read-only shadow concurrency token on `Tenant` and
`Conversation`.**

Each maps a `uint` shadow property named `xmin`, typed `xid`,
`ValueGeneratedOnAddOrUpdate`, `IsConcurrencyToken`. Every `UPDATE` carries the
row's transaction id in its `WHERE` clause, so a stale writer matches no row and
EF raises `DbUpdateConcurrencyException`. **The database performs the detection**
— this is not an in-memory check.

Chosen because it is PostgreSQL-native, needs no schema column, and stays
entirely in Infrastructure: no version property leaks into the Domain.

Two things the brief warned about, both verified rather than assumed:

- **`UseXminAsConcurrencyToken()` does not exist** in Npgsql provider 10.0.3. The
  installed assembly was inspected before writing any code; the old API is gone.
  The mapping uses the provider-agnostic EF API instead.
- **The scaffolded migration creates an `xmin` column**, which PostgreSQL rejects
  because `xmin` is a system column. The generated line is removed from the
  migration by hand with a comment explaining why. This must be redone whenever
  the migration is regenerated — noted as a known issue (§40).

`Message` has no token: immutable, so no update to lose.

**Proven by four tests** against real PostgreSQL: a stale tenant rename is
rejected and the winner's value survives; a stale conversation status change is
rejected and the winner's instant survives; a stale message append is rejected
**and leaves no partial write** (the message insert rolls back with the
conversation update, verified by row count); and ordinary sequential updates from
a fresh load still succeed, so the token does not block normal work.

One finding worth recording: a stale `Reopen` on an instance whose in-memory
status is already `Open` is **not** a valid concurrency probe, because Phase 3
idempotency makes it a no-op that produces no `UPDATE`. The probe has to be an
operation that actually changes state. My first version of that test was wrong for
this reason and was corrected.

## 22. RLS evaluation and current decision

**Decision: PostgreSQL Row Level Security remains UNDER EVALUATION and is
deliberately NOT enabled.**

**What it would buy.** Enforcement in the database rather than in application
code — the only mechanism considered that makes an unscoped tenant query
*impossible* rather than merely *incorrect*, which is what tenancy invariant 1
asks for.

**Why not now:**

- **There is no tenant context to feed it.** RLS policies read a session setting
  (`current_setting`). Nothing in this codebase establishes who the current
  tenant is, so a policy written today would be permanently open or permanently
  closed. This alone settles it.
- **Connection pooling.** The session variable must be set per connection and
  reset on return, or a pooled connection leaks one tenant's context into
  another's request — turning the safety feature into the exact breach it exists
  to prevent.
- **Migrations and admin operations** run as a role that must bypass RLS, so
  there are two access paths to keep correct and the privileged one is the easy
  one to reach for by accident.
- **Background workers** have no request to inherit context from; every job would
  need to establish it explicitly.
- **Test complexity.** Every integration test would need a tenant context, and
  the adversarial cross-tenant tests still to be written would need a deliberate
  bypass.

**Recommendation.** Revisit in the phase that introduces tenant context and
authentication, and adopt then only with the pooling question answered and the
bypass paths named. Full assessment in
`docs/architecture/persistence-postgresql.md`.

## 23. Reconstitution strategy

**No Domain change was required.** EF Core materialises all three aggregates
through their existing **private constructors**, whose parameter names already
match the property names, and writes the remaining properties through their
private setters and backing fields.

`Conversation.Messages` is configured with `HasField("_messages")` and
`PropertyAccessMode.Field`, so EF populates the private list and never touches
the read-only view.

Nothing was added: no public setter, no persistence DTO, no parameterless
constructor, no `internal` reconstitution factory, no mutable public collection.

Round-trip tests assert reconstitution does **not** mint new ids, rewrite
timestamps, reorder messages by `SentAt`, or lose tenant ownership.

## 24. Migration

**`20260904124546_InitialPersistence`**, in
`backend/src/OmniAI.Infrastructure/Persistence/Migrations/`.

Created with the local tool:
`dotnet dotnet-ef migrations add InitialPersistence --project src/OmniAI.Infrastructure --output-dir Persistence/Migrations`.

**One clean initial migration, not a remediation migration stacked on top.** Two
earlier `InitialPersistence` migrations were discarded and regenerated — the
first when the ingestion-order model changed during the phase, the second for the
remediation. This is still the greenfield initial schema: it has never been
applied to any long-lived or published database, only to disposable test
databases created and dropped per test class, so there was nothing to preserve
and nothing to revert. Stacking an `AddMessageIngestionSequence` migration onto
an unreleased initial schema would have left a meaningless step in the history
forever. Regenerating also keeps the migration and the model snapshot consistent,
which hand-patching would not.

`Up` creates one sequence, three tables and three indexes. `Down` drops the three
tables and the sequence. No destructive SQL beyond that.

## 25. Migration inspection

Read line by line, not accepted blind.

| Checked | Result |
| --- | --- |
| Primary keys | `PK_tenants`, `PK_conversations`, `PK_messages`, all on `id` |
| Foreign keys | `fk_conversations_tenant` (Restrict), `fk_messages_conversation` (Cascade) |
| Unique indexes | `ix_tenants_slug`; `ix_messages_conversation_ingestion` on `(conversation_id, ingestion_sequence)` |
| Non-unique index | `ix_conversations_tenant_last_activity` |
| Nullability | Every column `nullable: false` |
| Lengths | 200 / 63 / 32 / 32000, matching Domain limits |
| UUID storage | All three ids `type: "uuid"` |
| Timestamps | All three `timestamp with time zone` |
| Enum/status storage | `character varying(32)`, not integer |
| Sequence | `CreateSequence(name: "message_ingestion_sequence")` in `Up`, `DropSequence` in `Down` |
| Ingestion order | `ingestion_sequence` plain `bigint`, **no identity annotation** (deliberately — see §20.3), unique composite index |
| Concurrency | **`xmin` column creation removed by hand** — PostgreSQL rejects a user column of that name |

**One defect found and fixed by inspection, on every regeneration:** the
scaffolder emits `xmin = table.Column<uint>(type: "xid", rowVersion: true, …)` on
both `tenants` and `conversations`. `CREATE TABLE` would fail. Both lines were
removed with a comment explaining why, and the migration then applied cleanly to
PostgreSQL 17.6 — which is the real proof.

Because hand-editing generated code regresses silently, that edit is now
**guarded by tests** rather than by memory. `MigrationSchemaTests` generates the
migration script through `IMigrator.GenerateScript()` and asserts it contains no
`xmin` column, and separately asserts the applied `public` schema has no column
of that name. A future regeneration that reintroduces it fails a test instead of
a deployment.

## 26. Real PostgreSQL test strategy

**Real PostgreSQL 17.6. No EF Core in-memory provider anywhere.**

`PostgresTestDatabase` is a class fixture that, per test class, creates a
randomly named database, applies the **real migration** (`Database.MigrateAsync`,
not `EnsureCreated`, so the schema under test is the schema the migration
produces), and drops it afterwards with `WITH (FORCE)`. Each class therefore has
its own database and classes run in parallel without seeing each other's rows.
Parallelisation was **not** disabled.

The server is located via the **`OMNIAI_TEST_POSTGRES`** environment variable. If
it is absent the fixture **throws with an actionable message** rather than
substituting a fake — so a green run always means PostgreSQL was exercised. No
credential is compiled in or committed.

**Docker was unavailable, and this is a genuine environmental constraint, not a
shortcut.** The CLI is installed (29.4.1) but the daemon is not running: the
Docker Desktop service is `Stopped`/`Manual` and starting it failed with
*"Cannot open com.docker.service"* (needs elevation), while launching the desktop
app was blocked in this environment. Testcontainers would therefore have been an
unexercisable dependency, so it was **not added**.

Instead, per the brief's instruction to *"establish another isolated
real-PostgreSQL testing method without touching production infrastructure"*, an
ephemeral cluster was provisioned from the official PostgreSQL 17.6 Windows
**binaries zip** (no installer, no administrator rights): `initdb` into a
throwaway data directory outside the repository, listening on **127.0.0.1:55433**
only. It holds no production data, is reachable only from this machine, and is
disposable.

**For CI and other developers, Testcontainers or any disposable server remains
the intended route** — the env-var contract means nothing in the test code needs
to change. That is recorded as deferred rather than claimed as done.

**Recommendation for CI, explicitly.** CI should provide `OMNIAI_TEST_POSTGRES`
from a disposable PostgreSQL (Testcontainers, a service container, or an
equivalent ephemeral instance) and run the **complete** suite — all four test
projects — on a host without an application-control policy, which is the only
environment where `OmniAI.Domain.Tests` can currently be executed at all (§31).
Nothing about the local Windows cluster is portable or intended to be: its data
directory, password file and port live entirely outside the repository and none
of them are committed.

## 27. Integration tests

**44 new tests** in `OmniAI.Infrastructure.IntegrationTests`, all against real
PostgreSQL:

| Class | Tests | Coverage |
| --- | --- | --- |
| `TenantPersistenceTests` | 6 | Full round-trip; typed id round-trip **plus** `information_schema` proof the column is `uuid`; value objects stored as scalar `character varying` (not JSON) with the plain string readable; `Suspended` round-trip stored by name; **duplicate slug rejected by the database** by index name; two distinct slugs both persist |
| `ConversationPersistenceTests` | 10 | Full round-trip; tenant ownership preserved and distinct from another tenant; all four channel kinds round-trip and are stored by name (4 cases); closed conversation round-trips with its activity instant; `timestamptz` column type and `+03:00` → UTC normalisation; **nonexistent tenant FK rejected**; **deleting a tenant that owns conversations refused** |
| `MessagePersistenceTests` | 20 | See the breakdown below |
| `ConcurrencyTests` | 4 | Stale tenant update rejected, winner survives; stale conversation status change rejected, winner's instant survives; **stale message append rejected with no partial write**, and that append does not load history either; sequential updates from a fresh load still succeed |
| `MigrationSchemaTests` | 4 | Migration script contains no `xmin` column and the `xmin` token is still in the model; applied `public` schema has no `xmin` column; the migration creates `message_ingestion_sequence`; `ingestion_sequence` is `bigint` with `is_identity = NO` |

Plus the pre-existing `InfrastructureDependencyTests` (1) — the Phase 1
architecture guard, still passing. **45 tests in the project.**

**`MessagePersistenceTests` in detail**, since ingestion order is what the
remediation changed:

| Test | Proves |
| --- | --- |
| `A_message_round_trips_with_every_field_intact` | Every column survives |
| `Every_text_sender_round_trips_and_is_stored_by_name` (3 cases) | Senders stored by name |
| `A_system_message_round_trips_with_system_sender_and_content_type` | System messages |
| `Message_text_round_trips_including_interior_whitespace_and_non_latin_script` | Text fidelity |
| `Multiple_messages_round_trip_with_distinct_identities` | Identity per row |
| `Messages_reload_in_ingestion_order_not_in_timestamp_order` | **C** — T1/T3/T2 reloads as T1/T3/T2, and explicitly not T1/T2/T3 |
| `Out_of_order_timestamps_are_not_rewritten_by_persistence` | **C** — `SentAt` unchanged; high-water mark held |
| `Ingestion_order_holds_when_messages_arrive_in_separate_units_of_work` | **A + C** — three deliveries, three transactions, none loading history |
| `The_ingestion_sequence_follows_append_order_and_strictly_increases` | Order follows append order; values ascend, not contiguous |
| `The_ingestion_sequence_continues_across_separate_units_of_work` | **A** — values continue rather than restarting, with no history read |
| `A_message_is_appended_without_loading_the_existing_history` | **A** — the full ten-step scenario the review asked for |
| `Appending_to_a_large_history_never_queries_the_history` | **B** — 500 existing messages; recorded SQL contains no `FROM messages`, and does contain `nextval` |
| `The_allocator_is_a_real_postgresql_sequence` | **D** — the sequence exists in `pg_sequences`; two allocations never collide |
| `No_two_messages_ever_share_an_ingestion_sequence_value` | **D** — across conversations and transactions |
| `A_duplicate_ingestion_position_is_rejected_by_the_database` | **D** — raw insert reusing a stored value hits `ix_messages_conversation_ingestion` |
| `A_gap_left_by_a_rolled_back_append_does_not_disturb_later_order` | Gap semantics; order still correct afterwards |
| `Message_identity_is_still_the_primary_key` | `MessageId` identity |
| `Deleting_a_conversation_removes_its_messages` | Cascade |

**How B proves history-independence.** It does not time anything, which would be
a flaky benchmark. It attaches a `DbCommandInterceptor` that records the text of
every statement the context sends, clears it after loading the conversation row,
then appends one message to a conversation that already holds 500. It asserts
that **no recorded statement selects `FROM messages`** — so no history was read —
and that one **does** contain `nextval`, so the value provably came from the
sequence and the assertion is not passing because nothing was recorded at all.
Two supporting assertions confirm the aggregate held exactly one message and the
change tracker exactly one message entry, never 501.

**Requirement E** is covered by `ConcurrencyTests`. The appender loads only the
conversation row, so the ordering mechanism is exercised in exactly the way the
write path uses it, and the stale append is still rejected with
`DbUpdateConcurrencyException` and leaves no message row behind. No retry was
added.

Determinism was verified by running the suite **three times consecutively**
(45/45 each time) after the remediation.

## 28. Backend test totals before / after

| | Before Phase 4 | After Phase 4 |
| --- | --- | --- |
| `OmniAI.Domain.Tests` | 172 | 172 *(unchanged; blocked from executing — §31, §34)* |
| `OmniAI.Application.Tests` | 1 | 1 |
| `OmniAI.Infrastructure.IntegrationTests` | 1 | **45** |
| `OmniAI.Api.IntegrationTests` | 1 | 1 |
| **Total defined** | **175** | **219** |
| **Executed this session** | — | **47, all passing** |

## 29. Restore / build / test results

**Restore** — `dotnet restore OmniAI.slnx`: success, all 8 projects, no warnings.

**Build** — `dotnet build OmniAI.slnx -c Release`:

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

**Test** — `dotnet test OmniAI.slnx -c Release`:

| Project | Passed | Failed | Skipped |
| --- | --- | --- | --- |
| `OmniAI.Infrastructure.IntegrationTests` | 45 | 0 | 0 |
| `OmniAI.Application.Tests` | 1 | 0 | 0 |
| `OmniAI.Api.IntegrationTests` | 1 | 0 | 0 |
| `OmniAI.Domain.Tests` | — | — | — *(host could not load the assembly; §31)* |
| **Total executed** | **47** | **0** | **0** |

The Infrastructure suite was run **three consecutive times at 45/45** after the
remediation, and the solution was rebuilt in Release at `0 Warning(s), 0
Error(s)` beforehand.

## 30. Vulnerability result

`dotnet list OmniAI.slnx package --vulnerable --include-transitive` — **no
vulnerable packages in any of the 8 projects**, including the four EF
Core/Npgsql packages added this phase.

## 31. Warnings

**Compiler / analyzer: zero.** `0 Warning(s), 0 Error(s)` in Release across all
8 projects, with `TreatWarningsAsErrors` and analyzers at `latest-recommended`.

Build failures encountered and fixed on their merits during the phase:

1. **IDE0161 + CA1861 ×2 in the generated migration.** Resolved with a scoped
   `.editorconfig` in the `Persistence/Migrations/` folder **only**, disabling
   those two rules for generated code with a comment giving the reason. Every
   hand-written file in the solution keeps both rules enforced. Editing generated
   output to satisfy formatting conventions would mean re-editing it after every
   migration for no correctness benefit.
2. **CS1705 EF Core assembly version conflict.** Fixed by pinning one EF Core
   version centrally (§6), not by suppressing the error.

**Environmental warning — `OmniAI.Domain.Tests` cannot execute.** The test host
fails with:

```
System.IO.FileLoadException: Could not load file or assembly
'...\OmniAI.Domain.Tests.dll'. An Application Control policy has blocked
this file. (0x800711C7)
```

Confirmed as **Smart App Control** via the `CodeIntegrity/Operational` event log
(events 3077 and 3033 naming that assembly under `testhost.exe`). This is the
Phase 1 §19.1 risk materialising — previously it blocked `OmniAI.Domain.dll`, now
the test assembly itself, because verdicts are per file hash and a rebuild
produces a new hash.

Two remediations were attempted and both failed: a clean rebuild (new hash), and
building to a directory outside the Desktop tree and running via `dotnet vstest`.
The policy is machine-wide and path-independent. **Smart App Control was not
disabled** — that is irreversible without an OS reinstall, requires elevation,
and is the user's decision, not mine.

**No database or provider warning** was emitted during any test run.

## 32. Frontend modified

**NO.** `git status --short -- frontend/` returns nothing — zero files added,
changed or deleted. No npm command was run at all this phase.

## 33. Application layer modified

**NO.** `OmniAI.Application` and `OmniAI.Application.Tests` are unmodified.

No `IRepository<T>`, `GenericRepository<T>`, `UnitOfWork`, `TenantRepository`,
`ConversationRepository`, `TenantService`, `ConversationService`, command,
handler, DTO, controller or endpoint was created. EF Core already provides
repository and unit-of-work mechanics; the right abstraction is the one the first
real vertical slice asks for.

The one thing that could be mistaken for a repository is
`ConversationLoading.FindConversationWithMessagesAsync` — an extension method on
`OmniAiDbContext` in Infrastructure. It exists because EF Core cannot express an
ordered `Include`, and something must implement the brief's explicit requirement
that reconstitution preserve ingestion order. It has no interface, no injection,
no abstraction over the context, and nothing in Application references it.

## 34. Domain layer modified

**NO.** Verified two ways: `git diff --stat HEAD -- backend/src/OmniAI.Domain
backend/tests/OmniAI.Domain.Tests` is empty, and `git status --short` for those
paths lists nothing. The Domain layer and its tests are **byte-identical to the
accepted Phase 3 checkpoint**.

No public setter was added, no mutable collection exposed, no persistence
attribute applied, no parameterless constructor introduced, no invariant
weakened. EF Core materialises the aggregates through their existing private
constructors and backing fields (§23).

This also establishes that the blocked Domain tests (§31) are not a Phase 4
regression: the code they cover has not changed since it passed 172/172 at the
accepted checkpoint.

## 35. Files created / modified

**Created — Infrastructure persistence (11):**
`Persistence/OmniAiDbContext.cs`, `OmniAiDbContextFactory.cs`,
`PersistenceConverters.cs`, `PersistenceSchema.cs`, `ConversationLoading.cs`,
`Configurations/{Tenant,Conversation,Message}Configuration.cs`,
`Migrations/20260904124546_InitialPersistence.cs`, its `.Designer.cs`,
`Migrations/OmniAiDbContextModelSnapshot.cs`.

**Created — generated-code analyzer scope (1):**
`Persistence/Migrations/.editorconfig`.

**Created — integration tests (6):**
`Persistence/{PostgresTestDatabase, TenantPersistenceTests,
ConversationPersistenceTests, MessagePersistenceTests, ConcurrencyTests,
MigrationSchemaTests}.cs`. `PostgresTestDatabase.cs` also carries `RecordedSql`,
the command interceptor that makes the history-independence claim structural.

**Created — tooling (1):** `backend/dotnet-tools.json`.

**Created — documentation (2):** `docs/architecture/persistence-postgresql.md`,
this report.

**Modified (4):** `backend/Directory.Packages.props` (four package versions),
`backend/src/OmniAI.Infrastructure/OmniAI.Infrastructure.csproj` (two package
references), `docs/architecture/README.md` (document index; decision 4 status),
`docs/architecture/multi-tenancy-isolation.md` (status line; a new section on
what Phase 4 settled and what it did not).

Of these, the DbContext, `PersistenceSchema`, `MessageConfiguration` and the
migration were reworked once mid-phase to correct the first ingestion-order
defect, and reworked again — together with `ConversationLoading` — for the
review remediation (§0). `MigrationSchemaTests.cs` is new in the remediation.

**21 new files, 4 modified.**

## 36. Commands executed

**Recovery:** `pwd`, `git status`, `git status --short`,
`git log --oneline --decorate -10`, `git diff --check`, `git diff --stat`,
`git diff`, `git rev-list --all --count`, `git remote`, plus inspection of
`docs/`, `backend/` and `frontend/`.

**Environment probing:** `docker --version`, `docker info`, `docker context ls`,
service and WSL state checks, `dotnet ef --version`, PostgreSQL binary
availability probes, `postgres --version`, `psql --version`.

**Tooling:** `dotnet new tool-manifest`, `dotnet tool install dotnet-ef`.

**Database:** `initdb` (pre-existing cluster), `pg_ctl start` on port 55433 after
clearing a stale pid file. `CREATE DATABASE` / `DROP DATABASE` are performed by
the test fixture.

**Migrations:** `dotnet dotnet-ef migrations remove --force`, and
`dotnet dotnet-ef migrations add InitialPersistence` — the final one after
deleting the superseded migration, designer and model-snapshot files, so exactly
one clean initial migration exists (§24).

**Validation:** `dotnet restore OmniAI.slnx`,
`dotnet build OmniAI.slnx -c Release`, `dotnet test OmniAI.slnx -c Release`
(twice), per-project and per-class `dotnet test --filter` runs, the integration
suite three consecutive times, `dotnet list package --vulnerable
--include-transitive`, `dotnet vstest` (Smart App Control remediation attempt).

**Diff review:** `git status --short`, `git diff --check`, `git diff --stat`,
`git diff`, path scan, and a content secret scan over every Phase 4 file.

No VPS command, no SSH, no deployment, no `git commit`, no `git push`, no remote
change, no npm command.

## 37. Architectural decisions

Recorded in full in `docs/architecture/persistence-postgresql.md` (P1–P14).
Summary:

1. **PostgreSQL via Npgsql/EF Core**, verified against a real server because the
   schema depends on PostgreSQL-specific behaviour.
2. **Persistence confined to Infrastructure**; Domain and Application reference
   no EF Core or Npgsql assembly.
3. **Fluent configuration, one file per aggregate**; explicit snake_case names
   rather than another dependency for naming conventions.
4. **Typed ids as native `uuid`** with hand-written converters, no generic id
   framework.
5. **Value objects as scalar columns**, never JSON.
6. **Enums stored by name**, so ordinals never become a database contract.
7. **`timestamptz`, always UTC.**
8. **Unique index for slug uniqueness** — the guarantee the Domain could not make.
9. **Restrict on tenant delete, Cascade on conversation delete** — deliberately
   different, for opposite reasons.
10. **No `tenant_id` on `messages`** — the simpler FK already makes cross-tenant
    mismatch unrepresentable.
11. **Ingestion order as an aggregate-supplied position column**, after a
    database identity column was tried and disproved.
12. **`xmin` optimistic concurrency**, Infrastructure-only, database-enforced.
13. **No Domain change for reconstitution.**
14. **Real, disposable PostgreSQL per test class**, with a loud failure when no
    server is configured.

## 38. Deferred concerns

Nothing on the brief's exclusion list was implemented. Specifically deferred, and
documented as such:

RLS adoption; request tenant context; global query filters; admin/system tenant
bypass; provider message idempotency (no WhatsApp/Instagram/Messenger message id,
no webhook deduplication — `MessageId` uniqueness is enforced as the primary key,
provider replay protection is a separate concern); application persistence
contracts and repositories; production database topology and pooling; backups and
point-in-time recovery; read replicas; partitioning; API pagination of history;
Testcontainers as the CI database strategy.

Also absent: authentication, authorisation, JWT, OpenAI, Anthropic, AI
abstractions, RAG, vector storage, channel SDKs, any webhook, Angular changes,
deployment changes.

## 39. Deviations from prompt

1. **Testcontainers was not used, and the real-PostgreSQL requirement was met
   another way.** Docker's daemon could not be started without elevation (§26).
   The brief allows exactly this: report the blocker or establish another
   isolated real-PostgreSQL method. An ephemeral official-binaries cluster on
   `127.0.0.1:55433` was used, and the tests read their connection string from an
   environment variable so Docker/Testcontainers remains a drop-in for CI. No
   fake persistence test was substituted.
2. **The migration from the interrupted session was regenerated rather than
   kept.** The recovery rules say not to recreate an existing migration; the
   ingestion-order model changed materially, and hand-patching would have left
   the migration and model snapshot inconsistent. It had only ever been applied
   to disposable test databases. Documented in §24.
3. **`Conversation.CreatedAt` is named `StartedAt`.** The brief's field list says
   `CreatedAt`; the accepted Phase 3 aggregate names it `StartedAt`. The accepted
   Domain name was kept rather than renamed to match the brief's wording; the
   column is `started_at`.
4. **Two EF Core packages were pinned that the brief did not name.**
   `Microsoft.EntityFrameworkCore` and `.Relational` at 10.0.11 are not new
   references but version pins, required to resolve a genuine CS1705 build
   failure (§6).

No other deviation. No application CRUD, no endpoints, no Domain change, no
frontend change, no commit.

## 40. Known issues

1. **`OmniAI.Domain.Tests` cannot execute on this machine** (§31). Smart App
   Control, machine-wide, path-independent, needs elevation to change. The 172
   tests are unchanged from the accepted checkpoint where they passed. Options
   for the user: allow the assembly in Smart App Control, sign the test
   assemblies, or run the suite in CI where the policy does not apply.
2. **Regenerating the migration reintroduces the `xmin` column** and it must be
   removed again by hand (§25). This is now caught by `MigrationSchemaTests`
   rather than by remembering, but the hand-edit itself still has to be redone
   after every regeneration.
3. **Ingestion order requires appending through the aggregate.** The relative
   order of several messages saved in one unit of work comes from the order they
   were appended to the conversation; a message forced into the change tracker
   with no tracked parent is ordered last rather than rejected. Nothing in this
   codebase can produce that — `Message` has no `DbSet` and is only reachable
   through its aggregate — and the unique index still prevents an ambiguous
   order, but it is a contract callers must honour.
4. **`ConversationLoading` is the only supported read path** for a conversation's
   history. Loading `Conversations` with a plain `Include` returns messages in
   whatever order PostgreSQL chooses. This should become harder to get wrong when
   Application persistence contracts arrive.
5. **A rolled-back save consumes sequence values it never stores**, so
   `ingestion_sequence` has gaps. This is intended and documented (§20); it is
   listed here only so nobody later reads the column as a count.
6. **Tenancy invariant 1 is still unsatisfied**: an unscoped tenant-owned query
   is incorrect, not impossible. Nothing in Phase 4 claims otherwise.
7. **No adversarial cross-tenant isolation tests yet** — they need tenant context
   and a query layer to attack. Still the recorded obligation in the tenancy ADR.
8. Earlier phases' open items are unchanged: no frontend linter, no CI pipeline,
   no toolchain pins, `InvariantGlobalization` revisit.

## 41. Blockers

**One, environmental and outside my authority:** Smart App Control prevents the
172 Domain tests from running on this machine (§31, §40.1). It does not block
Phase 4's own work — all 47 executable tests pass, including 45 against real
PostgreSQL — but the full suite cannot be demonstrated green here until you
decide how to handle the policy. It was **not** disabled and no Windows security
policy was changed.

Nothing else blocks progress to Phase 5.

## 42. `git status --short`

```
 M backend/Directory.Packages.props
 M backend/src/OmniAI.Infrastructure/OmniAI.Infrastructure.csproj
 M docs/architecture/README.md
 M docs/architecture/multi-tenancy-isolation.md
?? backend/dotnet-tools.json
?? backend/src/OmniAI.Infrastructure/Persistence/
?? backend/tests/OmniAI.Infrastructure.IntegrationTests/Persistence/
?? docs/architecture/persistence-postgresql.md
```

Plus this report and `docs/architecture/persistence-postgresql.md`. Untracked
directories are collapsed by Git; expanded, the new files are the 21 listed in
§35. **No commit was created, nothing was staged, and no remote exists.**

`git diff --check` reports no whitespace errors.

## 43. `git diff --stat`

```
 backend/Directory.Packages.props                   | 15 +++++++
 .../OmniAI.Infrastructure.csproj                   | 10 +++++
 docs/architecture/README.md                        | 16 ++++---
 docs/architecture/multi-tenancy-isolation.md       | 50 +++++++++++++++++++++-
 4 files changed, 84 insertions(+), 7 deletions(-)
```

`git diff` covers tracked files only; every other Phase 4 file is new and
therefore untracked (§42). The full diff was read: four package versions, two
package references, and additive documentation.

Diff review found no accidental changes, no generated build output, no container
or database data, no secrets, no package scope creep, no infrastructure leakage
into Domain, no Application change and no frontend change.

## 44. Secret review

**Confirmed — no secrets.** Checks over every Phase 4 file:

- **Path scan** — nothing under `node_modules`, `bin/`, `obj/`, `dist/`,
  `.angular/`, `.vs/`, and no `.env`, `pgdata` or `pgtest` path would be tracked.
  The ephemeral cluster lives entirely outside the repository.
- **Content scan** for passwords, `pwd=`, API keys, secrets, credentials, bearer
  values, `sk-`-style keys and the VPS address: the only matches were prose in doc
  comments and the architecture document stating that no credential belongs in
  source.
- **Targeted scan for connection strings carrying credentials**
  (`Host=…Password=…`) across `backend/src` and `backend/tests`: **none**.

The test connection string was supplied only through the `OMNIAI_TEST_POSTGRES`
environment variable in the shell and was never written to any file. The
design-time factory's fallback is a host and database name with no user and no
password, used only for model building. The ephemeral cluster's password file is
outside the repository and uses `trust` local authentication for a
loopback-only server holding no real data.

## 45. Confirmation production VPS was NOT accessed

**Confirmed.** No SSH connection was attempted or made to `186.240.145.196` or
any other host. No PostgreSQL was installed, configured or inspected on the VPS.
Nothing was deployed. No Nginx configuration, systemd service, server directory
or production application was touched.

All work was local: the repository at
`c:\Users\mouay\Desktop\omnichannel-ai-platform`, and a disposable PostgreSQL
cluster bound to `127.0.0.1:55433`. Network access was limited to
`api.nuget.org` for packages and `get.enterprisedb.com` for the PostgreSQL
binaries.

## 46. Readiness recommendation for Phase 5

**Ready to proceed.**

The three accepted aggregates now persist to PostgreSQL with real constraints,
working optimistic concurrency, and an ingestion order that survives round-trip
and does not make the write path read the transcript. That last mechanism was
wrong twice — once caught by a flaky test, once caught by review — so it is now
backed by evidence rather than assumption, including a recorded-SQL assertion
that appending to a long conversation reads none of it. The Domain is untouched.

Recommended for Phase 5, in this order:

1. **Decide how to handle Smart App Control** (§41) so the full 211-test suite
   can be demonstrated green. This is the one thing needing your decision.
2. **Introduce request tenant context** — the accepted tenancy ADR's D6. It is
   now the single largest gap: it blocks query scoping, RLS, the admin bypass
   design and the adversarial isolation tests all at once.
3. **Then the first Application vertical slice** — most usefully "start a
   conversation and post a message", end to end. That is where the Application
   persistence contracts should be introduced, shaped by a real use case rather
   than invented in advance, and where the two-exception error strategy first
   meets an HTTP edge.
4. **Write the adversarial cross-tenant isolation tests with that slice**, not
   after it. Conversations and messages are exactly the data a leak would expose,
   and they are now persisted.
5. Alongside: a CI pipeline running the same validation with a
   Testcontainers-backed PostgreSQL, plus a migration guard for the `xmin`
   column (§40.2).

Authentication should follow tenant resolution, since the resolution decision
(tenancy ADR M4) determines what the token must carry. RLS should be revisited
only once tenant context exists.
