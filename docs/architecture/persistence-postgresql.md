# Persistence: PostgreSQL and EF Core

**Status:** Phase 4 implemented relational persistence for `Tenant`,
`Conversation` and `Message`. Nothing marked DEFERRED exists in code.

Phase 4 review required one remediation, and it is applied here: the message
ingestion-order mechanism no longer derives its value from the loaded aggregate
history, so **appending a message does not read a conversation's transcript**.
[P11](#p11--message-ingestion-order-comes-from-a-postgresql-sequence) records
the replaced designs and why each was rejected. Every other Phase 4 decision on
this page is unchanged.

Persistence is entirely an Infrastructure concern. `OmniAI.Domain` and
`OmniAI.Application` do not reference EF Core or Npgsql, and no Domain type
carries a persistence attribute.

---

## DECIDED NOW

### P1 — PostgreSQL, via Npgsql and EF Core

PostgreSQL is the relational database. EF Core is the mapper, with the Npgsql
provider.

The features this schema leans on are PostgreSQL's, not generic SQL: native
`uuid` columns, `timestamptz`, the `xmin` per-row transaction id used as a
concurrency token, and partial/composite unique indexes. That is also why the
integration tests run against a real PostgreSQL server and never the EF Core
in-memory provider — a green run against in-memory would prove nothing about any
of it.

### P2 — Persistence lives in Infrastructure only

`OmniAiDbContext` and every mapping sit in
`OmniAI.Infrastructure/Persistence/`. Domain has no idea it is persisted;
Application has no EF Core reference. The Phase 1 architecture guard still
proves Domain references nothing but `System.*`.

Only aggregate roots get a `DbSet`: `Tenants` and `Conversations`. `Message` is
mapped but has no set, because reaching a message independently would route
around the aggregate that enforces its rules.

### P3 — Fluent configuration, one file per aggregate

`IEntityTypeConfiguration<T>` per entity — `TenantConfiguration`,
`ConversationConfiguration`, `MessageConfiguration` — discovered by
`ApplyConfigurationsFromAssembly`. No single god configuration method, and no
attributes on Domain types.

Column and table names are written out explicitly in snake_case rather than
pulled in through a naming-convention package: fifteen columns is not enough to
justify another dependency, and being able to read the exact column name next to
the property is worth more than the brevity.

### P4 — Strongly typed ids are native `uuid`

`TenantId`, `ConversationId` and `MessageId` each get an explicit
`ValueConverter` to `Guid`, stored as PostgreSQL `uuid`. Never text.

Five hand-written converters in one file, not a reflection-driven convention:
the point of the typed ids is that substitution is a compile error, and a
generic framework that maps "anything wrapping a Guid" quietly gives that away.

`ValueGeneratedNever()` on every key: ids come from the Domain's UUIDv7
factories, so the database must not invent them.

### P5 — Value objects are scalar columns

`TenantName` and `TenantSlug` convert to `string` and map to
`character varying` columns with the Domain's own length limits (200 and 63).
Not JSON: a JSON column would forfeit the length constraint, the unique index the
slug needs, and plain readability.

Reading goes back through the Domain factory, so a stored value that violates a
Domain rule fails loudly on load rather than entering the model. The factories
normalise, but stored values are already canonical, so a round trip cannot alter
persisted data.

### P6 — Statuses and enums are stored by name

`TenantStatus`, `ConversationStatus`, `ChannelKind`, `MessageSender` and
`MessageContentType` are all stored as `character varying(32)` holding the member
name.

Deliberately not integers. Storing ordinals would make the compiler's numbering a
permanent database contract: reordering members or inserting one would silently
re-interpret existing rows. Names also make the tables legible to anyone reading
them directly during an incident. Tests assert the stored text for each value.

### P7 — Timestamps are `timestamptz`, always UTC

`StartedAt`, `LastActivityAt` and `SentAt` map to
`timestamp with time zone`. The Domain has already normalised every instant to
UTC (architecture decision 11), so nothing depends on the server's or the
client's time zone. A test writes a `+03:00` value and asserts it returns as the
same instant with a zero offset.

### P8 — Slug uniqueness is a unique index

`ix_tenants_slug`, unique, on `tenants.slug`. This is the guarantee the Domain
deliberately could not make on its own (see
[multi-tenancy-isolation.md](multi-tenancy-isolation.md) D3).

A plain unique index is exact here because slugs are already canonical lowercase
before they reach persistence. No `citext` extension and no `lower()` expression
index — those would solve a problem the Domain has already removed.

### P9 — Foreign keys, and deliberately different delete behaviour

| Relationship | Delete behaviour | Why |
| --- | --- | --- |
| `conversations.tenant_id` → `tenants.id` | **Restrict** | A tenant that still owns conversations must not be deletable by accident. Erasing a tenant's history is a deliberate operation with rules that do not exist yet, so the database refuses until they do. |
| `messages.conversation_id` → `conversations.id` | **Cascade** | Messages are inside the conversation aggregate and have no meaning without it. |

Both are covered by tests, including one that asserts deleting a tenant with
conversations is refused by name (`fk_conversations_tenant`).

### P10 — No persistence-only tenant column on `messages`

Considered and rejected. A message reaches its tenant through exactly one
conversation, and `messages.conversation_id` is a non-null foreign key to a
conversation that has exactly one non-null `tenant_id`. The tenant of a message
is therefore already single-valued and already enforced; a duplicated
`tenant_id` on `messages` would add a second copy of a fact the schema cannot
contradict, plus the obligation to keep the two in step.

A composite `(tenant_id, conversation_id)` constraint was evaluated for the same
reason and rejected on the same grounds: it would protect against a cross-tenant
mismatch that the simpler single foreign key already makes unrepresentable.

This is a decision to revisit — not reverse — if tenant-scoped indexes or row
level security later need `tenant_id` present on every tenant-owned table. At
that point the column becomes a means to an end that exists, rather than
denormalisation in advance of one.

### P11 — Message ingestion order comes from a PostgreSQL sequence

This is the subtlest thing in the schema, and it took three attempts. The first
two are recorded here in full, because each was rejected for a reason worth
keeping.

Phase 3 established that `Message.SentAt` is **not** monotonic: a message can be
ingested after one that was sent later, and history is kept in ingestion order.
So reload cannot `ORDER BY sent_at` without reordering history, and the ordering
value has to be persisted separately.

**Rejected: `ORDER BY sent_at`.** Rejected by definition; it destroys the
accepted ingestion-order guarantee.

**Rejected: `ORDER BY id`.** `MessageId` is a UUIDv7, time-ordered only to the
millisecond and random below that. Messages appended within the same millisecond
sort arbitrarily. This was observed in practice, not merely feared.

**Rejected on evidence: a self-filling identity column.** EF Core gives no
guarantee about the order in which it inserts rows of the same type within one
batch, so the generated numbers did not follow append order and reloaded
transcripts came back scrambled (`3rd, 1st, 2nd`), intermittently.

**Rejected on review: the message's index in the aggregate's collection.** This
produced correct order and passed every test written for it, and it was still
wrong. Reading an index out of the collection is only safe if the collection
holds the whole history, so the write path had to load every stored message
before it could append one, and the context threw when it had not. A production
conversation may hold tens of thousands of messages. Paying a full-history read
on every inbound webhook is not a scalability detail to fix later; it is a write
path that gets slower for the rest of the conversation's life. The mechanism was
replaced.

**What is implemented.** A PostgreSQL sequence, `message_ingestion_sequence`,
declared on the model (`modelBuilder.HasSequence<long>`) so the migration creates
it. `OmniAiDbContext` allocates one value per new message immediately before
insert, in a single round trip:

```sql
SELECT nextval('message_ingestion_sequence')::bigint AS "Value"
FROM generate_series(1, @count)
```

and stamps the values onto the pending rows, lowest value to earliest append.
The returned values are sorted in memory first, so nothing depends on the order
PostgreSQL happened to emit the rows in.

The division of labour is the whole point: the **caller's append order** supplies
the relative order of the rows being written right now, and **PostgreSQL**
supplies the absolute value. Stored history supplies nothing — not even an
offset.

Why a sequence rather than a per-conversation counter column advanced with
`UPDATE … RETURNING`: a counter on the conversation row would have to be written
on every append, which bumps that row's `xmin` and would make unrelated
concurrent writers fail their own optimistic-concurrency check. `nextval` takes
no row lock, touches no aggregate, and never returns a value twice — including
across sessions and after a rollback.

Properties of the design:

- **Appending never reads the message history.** The write path loads the
  conversation row, appends through the aggregate, and saves. Proven by
  `Appending_to_a_large_history_never_queries_the_history`, which records the SQL
  actually sent while appending to a 500-message conversation and asserts no
  statement selects `FROM messages`.
- `MessageId` remains the identity and the primary key. The sequence orders rows;
  it never identifies them.
- The ordering value is **not** a Domain property. It is a shadow property, and
  nothing in Domain reads or writes it. The Domain is unchanged from the Phase 3
  checkpoint.
- `Message.SentAt` is stored exactly as the Domain supplied it. Ingestion order
  and event time stay independent: T1, T3, T2 reloads as T1, T3, T2.
- **Values are global, not per-conversation**, so within one conversation they
  are sparse. Only the relation between them is meaningful.
- **Gaps are expected and harmless.** Sequences do not roll back, so a failed
  transaction consumes a value it never stores. That is the price of not
  locking, and it is the right trade: a gap costs nothing, a lock costs
  throughput.
- **Safe for concurrent writers by construction.** Two workers appending to the
  same conversation at the same time each get a distinct, increasing value with
  no coordination between them, and neither blocks the other.
- `ix_messages_conversation_ingestion` is **unique** on
  `(conversation_id, ingestion_sequence)`. The allocator cannot produce a
  duplicate, so this index is not there to police it; it is there so that any
  future code path which sets the column itself cannot make a conversation's
  order ambiguous without the write failing.
- **Ordering coexists with `xmin`.** Allocation reads a sequence and nothing
  else, so it neither reads nor advances any aggregate's token. A stale append
  still fails, and still fails as one transaction — the rejected conversation
  update takes the message insert down with it. No automatic retry exists, and
  none was added; retry policy belongs to the Application layer and to a later
  phase.

**Reading** is unchanged and remains a separate path.
`ConversationLoading.FindConversationWithMessagesAsync` loads a conversation and
its history with an explicit `ORDER BY ingestion_sequence`, because EF Core
cannot express an ordered `Include`. It exists for reconstitution, which
genuinely needs the whole transcript. Loading complete history and loading the
state required to append are two different operations, and only the first needs
that method.

### P12 — Optimistic concurrency via PostgreSQL `xmin`

`Tenant` and `Conversation` each map a `uint` shadow property named `xmin`, typed
`xid`, `ValueGeneratedOnAddOrUpdate`, marked as a concurrency token. Every
`UPDATE` therefore carries the row's transaction id in its `WHERE` clause: a
stale writer matches no row, and EF raises `DbUpdateConcurrencyException`.

Chosen because it is PostgreSQL-native, needs no schema column, and stays
entirely in Infrastructure — no version property leaks into the Domain.

Two things had to be handled, both verified rather than assumed:

- **`UseXminAsConcurrencyToken()` no longer exists** in the Npgsql provider
  version in use. The mapping is done with the provider-agnostic EF API instead.
- **The scaffolded migration tried to create an `xmin` column.** PostgreSQL
  rejects that: `xmin` is a system column present on every table. The generated
  column line is removed from the migration by hand, with a comment saying why.
  Regenerating the migration reintroduces it, and it must be removed again.
  *Superseded (Phases 33 and 33.1):* the removal was never necessary. The C#
  operation names `xmin`, but Npgsql's SQL generator emits no DDL for PostgreSQL's
  system column. Migrations are now generated and never edited; see
  [database-model-authority.md](database-model-authority.md).

`Message` gets no concurrency token: it is immutable once written, so there is no
update to lose.

### P13 — Reconstitution needs no Domain change

The Domain layer was **not modified** in Phase 4. EF Core materialises all three
aggregates through their existing private constructors, whose parameter names
already match the property names, and writes the remaining properties through
their private setters and backing fields.

`Conversation.Messages` is configured with `HasField("_messages")` and
`PropertyAccessMode.Field`, so EF populates the private list directly and never
goes through the read-only view.

No public setter, no persistence DTO, no parameterless constructor and no
`internal` reconstitution factory was added. Round-trip tests assert that
reloading does not mint new ids, rewrite timestamps, reorder messages or lose
tenant ownership.

### P14 — Integration tests use a real, disposable PostgreSQL

Each test class creates its own randomly named database, applies the **real
migration** (not `EnsureCreated`, so the schema under test is the schema the
migration produces), and drops the database afterwards. Classes therefore run in
parallel without seeing each other's rows.

The server is located through the `OMNIAI_TEST_POSTGRES` environment variable. If
it is absent the fixture **fails loudly** rather than substituting a fake
provider, so a green run always means PostgreSQL was exercised. No credential is
compiled in or committed.

---

## DEFERRED / UNDER EVALUATION

| Concern | Status |
| --- | --- |
| **PostgreSQL Row Level Security** | **Still under evaluation; deliberately not enabled.** See the assessment below. |
| **Request tenant context** | Does not exist. Nothing yet resolves "the current tenant" for a request or a job, which is why RLS cannot be adopted. |
| **Global query filters** | Not added. A filter needs an ambient current-tenant value, which does not exist. |
| **Admin / system tenant bypass** | Undesigned. Needs the tenant context first, and its own audit rules. |
| **Application persistence contracts** | None. No repository, unit-of-work or service was added — EF Core already provides those mechanics, and the right abstraction is the one the first real use case asks for. |
| **Provider message idempotency** | Deferred to the channel phases. No WhatsApp/Instagram/Messenger message id, and no webhook deduplication. `MessageId` uniqueness is enforced as the primary key; provider-level replay protection is a separate concern with provider-specific keys. |
| **Production database topology** | Undecided: instance sizing, connection pooling, PgBouncer. |
| **Backups and point-in-time recovery** | Undecided. Belongs with the deployment phase. |
| **Read replicas** | Undecided. No read model exists to route to one. |
| **Partitioning** | Undecided. `messages` is the eventual candidate, by tenant or by time; there is no data to justify a scheme yet. |
| **API pagination of history** | Still a transport concern. The schema supports it — `(conversation_id, ingestion_sequence)` is a keyset-pagination-friendly index. |

### RLS assessment (for the record)

**What it would buy.** Enforcement in the database rather than in application
code. It is the only mechanism on the table that makes an unscoped tenant query
*impossible* rather than merely *incorrect*, which is the property
[multi-tenancy-isolation.md](multi-tenancy-isolation.md) invariant 1 asks for.

**What it would cost, and why it is not adopted now:**

- **There is no tenant context to feed it.** RLS policies read a session variable
  (`current_setting`). Nothing in this codebase yet establishes who the current
  tenant is, so any policy written today would either be permanently open or
  permanently closed. This alone settles it.
- **Connection pooling.** The session variable must be set per connection and
  reset on return, or a pooled connection leaks one tenant's context into
  another's request — turning a safety feature into the exact breach it exists to
  prevent.
- **Migrations and admin work** run as a role that must bypass RLS
  (`BYPASSRLS` or table owner), so there are now two access paths to keep
  correct, and the privileged one is the easy one to reach for by accident.
- **Background workers** have no request to inherit context from, so every job
  would need to establish it explicitly.
- **Test complexity.** Every integration test would need a tenant context, and
  tests that legitimately span tenants — including the adversarial isolation
  tests still to be written — would need a deliberate bypass.

**Recommendation.** Revisit RLS in the phase that introduces tenant context and
authentication, and adopt it then only with the pooling question answered and
the bypass paths named. Until then the honest position is that isolation rests on
explicit foreign keys and, once it exists, application-layer scoping — with RLS
as the intended defence-in-depth layer, not a decided one.

---

## Running the tests

The Infrastructure integration tests read a maintenance connection string from
the `OMNIAI_TEST_POSTGRES` environment variable and create a randomly named
database per test class, migrate it, and drop it afterwards. If the variable is
absent the fixture throws with an explanation; it never falls back to a fake
provider, so a green run always means real PostgreSQL was exercised. No
credential is compiled in or committed.

**CI should supply that variable from a disposable PostgreSQL** — Testcontainers,
a service container, or an equivalent ephemeral instance — and run the whole
suite, including the Domain tests. Testcontainers is deliberately *not* a
dependency of this repository yet: Docker is unavailable on the current
development machine, so adding it would mean shipping a package no one here can
exercise. The environment-variable contract is what keeps it a drop-in change
when CI exists.

Two things this machine cannot be relied on for, and CI must cover:

- Executing `OmniAI.Domain.Tests`. Through Phases 1-4 the host's Windows
  Application Control policy refused to load locally built unsigned test
  assemblies (`0x800711C7`). As of Phase 5 that assembly loads and passes, with
  the policy still enforcing and unmodified -- its verdicts are per file hash, so
  a hash that was refused can later be permitted and a rebuild can be refused
  again. The suite currently runs; that is not something to gate a release on.
- Providing Docker, and therefore container-based database isolation.

---

## Things a reviewer can check independently

1. Domain and Application reference no EF Core or Npgsql assembly.
2. No Domain type carries a persistence attribute, and Domain is unchanged from
   the Phase 3 checkpoint.
3. Ids are `uuid`, statuses are `character varying`, instants are `timestamptz` —
   asserted by tests that query `information_schema`.
4. `messages.id` is the primary key; `ingestion_sequence` is not.
5. Persisting messages in ingestion order T1, T3, T2 reloads as T1, T3, T2.
6. Two stale competing updates cannot both succeed.
7. A duplicate slug and an orphaned conversation are both rejected by the
   database, by constraint name.
8. The migration creates no `xmin` column, and the applied `public` schema has
   no column of that name. Both are asserted by `MigrationSchemaTests`, which
   also generates the migration script and greps it, so a future regeneration
   that reintroduces the column fails a test rather than a deployment.
9. Appending one message to a 500-message conversation sends no `SELECT … FROM
   messages`. The assertion reads the intercepted command text, so it does not
   depend on inferring behaviour from entity counts.
10. `message_ingestion_sequence` exists as a real sequence in `pg_sequences`, and
    `ingestion_sequence` is a plain `bigint` with `is_identity = NO`.
11. No two message rows share an `ingestion_sequence` value.
12. A rolled-back append leaves a gap and the next append still orders
    correctly after the surviving rows.
