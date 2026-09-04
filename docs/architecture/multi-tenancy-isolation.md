# Multi-tenancy and tenant isolation

**Status:** design; partially decided. Phase 2 introduced the tenant domain
model. Phase 4 added the first relational safeguards -- a tenant foreign key on
conversations and a unique slug index. The mechanisms that would satisfy the
non-negotiable invariant below -- request tenant context, query scoping and
row level security -- are **still absent**. See
[What Phase 4 settled, and what it did not](#what-phase-4-settled-and-what-it-did-not).

One deployment serves many independent businesses. A tenant is a business account
on the platform, and tenant-owned data will eventually include AI agents, channel
connections, conversations, customers, messages, knowledge sources,
integrations, reports, usage and billing configuration.

---

## The non-negotiable invariant

> **No request, query, job or export may ever return, modify, delete or reveal
> the existence of another tenant's data.**

Everything below exists to serve that one sentence. It is a security boundary,
not a filtering convenience. Two consequences follow, and they are the reason the
rest of this document is structured the way it is:

- **A cross-tenant read is a data breach**, not a bug with cosmetic impact.
- **Absence of evidence is not isolation.** "No test showed a leak" is not the
  same as "a leak is structurally hard to write." The design must make the unsafe
  thing awkward, not merely undone.

Corollary: enumeration and existence are in scope. Returning `404` for another
tenant's record but `403` for a record that does not exist leaks which
identifiers are real. Cross-tenant access should be indistinguishable from
absence.

---

## Decisions already made

### D1 — Tenant is a first-class domain concept (Phase 2, implemented)

`Tenant`, `TenantId`, `TenantName`, `TenantSlug` and `TenantStatus` live in
`OmniAI.Domain.Tenants`. Tenancy was modelled before any tenant-owned entity
exists, precisely so that no entity is ever designed without it.

### D2 — `TenantId` is a distinct type, never a bare `Guid` (Phase 2, implemented)

Tenant identity is the isolation boundary. If it travels through the codebase as
`Guid`, then any other `Guid` — a user id, a conversation id, an id from a
request body — can be passed where a tenant id is expected, and the compiler will
not object. A distinct type turns a whole class of isolation mistakes into
compile errors.

### D3 — Slug uniqueness is *not* a domain guarantee (Phase 2, implemented)

`TenantSlug` guarantees *shape*, not *uniqueness*. Uniqueness requires knowledge
of every stored tenant, which the Domain does not have. A static registry inside
the Domain would be process-local, wrong the moment a second instance runs, and
actively harmful because it would look authoritative. Uniqueness is therefore a
persistence guarantee (unique index) surfaced through an Application use case
that translates the resulting conflict into a domain-meaningful error.

### D4 — Every tenant-owned record carries an explicit `TenantId`

Not inferred from a join path, not derived from a parent, not implied by a schema
name. An explicit column on every tenant-owned table is what makes both a
database-level constraint and an automated audit possible: "which tables lack a
tenant id" must be answerable mechanically.

### D5 — Defence in depth; no single control is trusted

At least three independent layers must fail before a leak reaches a client:
Application-layer scoping, a persistence-level safeguard, and tenant-aware
constraints. Any design where one forgotten `.Where()` is the only thing standing
between two tenants is rejected on that basis alone.

### D6 — Tenant context is explicit and request-scoped, never ambient-mutable

The current tenant is resolved once per request/job and flows as an explicit,
immutable, request-scoped dependency. No mutable static or thread-global tenant
holder: those leak across async continuations and pooled threads, and produce
exactly the failure this document exists to prevent — silently serving one
tenant's data under another tenant's context.

### D7 — Uniqueness is scoped to the tenant, except where it is global

Tenant-owned uniqueness (say, a customer's external reference) is unique *within*
a tenant, so keys are `(TenantId, …)`. Tenant-level identifiers — the slug — are
globally unique. Every unique index must state which of the two it is; getting
this backwards either breaks tenants or leaks their existence.

---

## Security invariants that constrain any future design

These bind whatever mechanisms are eventually chosen:

1. A tenant-owned query without a tenant predicate must be **impossible or loud**
   — never silently global.
2. Writes must be checked, not assumed. Loading a record under tenant A and
   saving it under tenant B must be rejected at save time, because "the record
   was fetched correctly" is an assumption an attacker gets to break.
3. `TenantId` is never accepted from a client-controlled field (body, query
   string, or an unvalidated header) as the authority for scoping. It is derived
   from the authenticated principal.
4. Reassigning an existing record's `TenantId` is not an update; it is either
   forbidden or a deliberate, audited migration operation.
5. Background jobs, scheduled work, exports, webhooks and retries carry tenant
   context explicitly. Work that outlives a request is where ambient context
   quietly disappears.
6. Cross-tenant access is indistinguishable from absence (see the corollary
   above).
7. Logs, traces, metrics and error payloads carry the tenant id but never another
   tenant's data. Diagnostics are a real exfiltration path.
8. Administrative or support access that legitimately spans tenants is a separate,
   explicitly named, audited capability — never the ordinary path with a check
   omitted.

---

## Deferred for evaluation

Open, and deliberately not settled before the persistence phase:

| # | Question | Why it is not decided yet |
| --- | --- | --- |
| M1 | Isolation model: shared schema with a discriminator, schema-per-tenant, or database-per-tenant | Depends on expected tenant count, noisy-neighbour tolerance, per-tenant restore requirements and operational cost. Choosing now would be a guess. |
| M2 | **PostgreSQL Row Level Security** | A strong candidate for the "impossible or loud" property of invariant 1, because it is enforced by the database rather than by application code. Not selected: it must be weighed against connection/pooling implications, migration and admin paths, and its interaction with the chosen ORM. **RLS is under evaluation, not adopted.** |
| M3 | ORM-level global query filters | Cheap and effective, but bypassable via raw SQL and easy to disable per-query. Viable as *a* layer, not as *the* layer. |
| M4 | Where tenant resolution happens for HTTP (subdomain from the slug, path segment, or token claim) | Belongs with authentication; the slug grammar was chosen to keep the subdomain option open (see `TenantSlug`). |
| M5 | Save-time cross-tenant write interception | The mechanism depends on M1 and M3; the *requirement* (invariant 2) is already fixed. |
| M6 | Whether a suspended tenant is blocked at authentication, at authorisation, or per-operation | Needs the real rules for what a suspended tenant may still do — read-only access, billing pages, data export. |

## What Phase 4 settled, and what it did not

Phase 4 introduced PostgreSQL persistence for `Tenant`, `Conversation` and
`Message`. Its effect on the questions above:

**Now decided:**

- **M1 is partly answered.** The isolation model is **shared schema with an
  explicit discriminator**: `conversations.tenant_id` is a non-null column with a
  foreign key to `tenants.id`, satisfying D4 for the aggregates that exist.
  Schema-per-tenant and database-per-tenant are no longer under consideration for
  these tables. What remains open is whether any future high-volume table needs
  physical separation or partitioning.
- **D3 is now enforced.** Slug uniqueness is a unique index,
  `ix_tenants_slug`, proven by an integration test that expects the database to
  reject the duplicate.
- **Delete behaviour is decided.** `conversations.tenant_id` is `RESTRICT`, so a
  tenant that still owns conversations cannot be deleted casually. Messages
  cascade from their conversation, because they are inside that aggregate.
- **D7 is applied where it applies.** The slug is globally unique; the
  ingestion-order index is scoped to its conversation.

**Still open, and important:**

- **M2 (Row Level Security) remains under evaluation and is deliberately not
  enabled.** The blocking reason is concrete: RLS policies read a per-session
  tenant setting, and D6's request-scoped tenant context does not exist yet, so
  any policy written today would be permanently open or permanently closed. The
  full cost assessment -- connection pooling, migrations, admin bypass,
  background workers, test complexity -- is in
  [persistence-postgresql.md](persistence-postgresql.md).
- **M3, M4, M5 and M6 are untouched.** No query filter, no HTTP tenant
  resolution, no save-time cross-tenant interception, no suspended-tenant gate.
- **Invariant 1 is not yet satisfied.** An unscoped tenant-owned query is
  currently only *incorrect*, not *impossible*. Nothing in Phase 4 changed that,
  and nothing in Phase 4 pretends otherwise: the only tenant-scoping in place is
  the foreign key.
- **`messages` carries no `tenant_id`.** Evaluated and rejected as redundant,
  since a message's tenant is already single-valued through its conversation's
  non-null foreign key. Recorded as a decision to revisit if tenant-scoped
  indexes or RLS later require the column on every tenant-owned table.

## Testing obligation

When persistence arrives, isolation gets **adversarial integration tests**, not
just unit tests: with tenant A and tenant B both populated, prove that acting as
A cannot read, update, delete, enumerate or detect the existence of B's records —
including through list endpoints, filters, sorts, aggregates, search, exports and
direct-id access. These tests are part of the isolation mechanism's definition of
done, not a follow-up task.

---

## What Phase 2 deliberately did not do

No `DbContext`, migration, repository, query filter, RLS policy, HTTP middleware,
tenant claim, authentication or authorisation. The tenant model exists so those
mechanisms have something correct to build on; building them before the model was
agreed is what produces retrofitted, leaky tenancy.
