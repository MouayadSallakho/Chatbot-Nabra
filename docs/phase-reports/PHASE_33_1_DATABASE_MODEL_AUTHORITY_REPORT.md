# Phase 33.1 — EF model authority and migration baseline convergence

> **Status: complete, not committed.** Baseline `b2ca442` (clean tree). Local
> Docker only: PostgreSQL 17.11 + pgvector 0.8.6 (`pgvector/pgvector:pg17`), Redis
> 7, .NET SDK 10.0.400. No VPS was accessed, nothing was deployed, and no live
> provider was contacted. Rules: [database-model-authority.md](../architecture/database-model-authority.md).

## 1. Baseline and safety

- HEAD `b2ca442`. The working tree was clean and `git diff --check` was clean.
- **Never deployed.** Every phase report through 33 states no deployment and no
  VPS. The repository has no Git remote. The shipped `appsettings.json` has an
  empty connection string. The only databases are local, synthetic, phase-named
  ones in a local container. No customer, VPS or shared database depends on the
  chain. Git history keeps the old migrations.

## 2. Audit method

The audit was empirical rather than a reading exercise. Two disposable databases
were built and their catalogs compared:

- **A:** the 20-migration historical chain, applied with `dotnet ef database update`.
- **B0:** an Initial scaffolded in a scratch copy from the *uncorrected* model.

A catalog describer covered every public-schema object: extensions, sequences,
tables, every column with type, nullability, identity and default, every
constraint with its full definition and name, and every non-constraint index
definition with its predicate and name. It excluded only `__EFMigrationsHistory`.
Views, functions, triggers, custom types, comments and non-default collations
were checked separately: there are none in either database.

## 3. Historical raw-SQL inventory

There were 35 `migrationBuilder.Sql` statements in 11 migrations. The full table,
with classification, is in the architecture document (§3). In summary:

| Class | Count | Examples |
| --- | --- | --- |
| A – final schema DDL | 13 | vector extension; `knowledge_chunks`; 2 chunk indexes incl. HNSW; 2 checks; 3 FKs; 2 partial indexes; `free` plan row |
| B/C – legacy backfill or temporary procedure | 17 | Owner-role backfill; nullable-add/backfill/`SET NOT NULL` for `updated_at`, `expires_at`, `client_mode`, widget columns, `handling`; legacy plan assignment; 3 customer-identity backfill statements |
| D – no longer relevant (Down only) | 5 | drop check; drop FK and index; drop chunk table; delete `free` |

Other hand edits that were not raw SQL:

- four hand-added `AddForeignKey` operations to `users`;
- a hand-written `InsertData` for three plans;
- a PK rename and a removed index in Phase 19;
- repeated hand removal of `xmin`.

## 4. Every gap found (B0 against A)

| # | Object | Kind |
| --- | --- | --- |
| 1 | `vector` extension | extension |
| 2 | `knowledge_chunks` (8 columns, PK, check, unique, composite FK) | table |
| 3 | `ix_knowledge_chunks_tenant_document` | index |
| 4 | `ix_knowledge_chunks_embedding` HNSW `vector_cosine_ops` | index |
| 5 | `ck_knowledge_documents_index_complete` | check |
| 6 | `ck_web_chat_sessions_embedded_origin` | check |
| 7 | `fk_knowledge_sources_document` | composite tenant FK |
| 8 | `fk_conversations_assigned_member` | composite tenant FK |
| 9 | `fk_human_replies_sender_member` | composite tenant FK |
| 10 | `fk_tenant_memberships_user` | FK to Identity `users` |
| 11–13 | `fk_notifications_recipient_user`, `fk_notification_emails_recipient_user`, `fk_notification_preferences_user` | FK to Identity `users` |
| 14 | `ix_conversations_assigned_to WHERE assigned_to IS NOT NULL` | partial index |
| 15 | `ux_whatsapp_contacts_current WHERE is_current` | partial unique index |
| 16 | `pk_conversation_human_replies` (model: `PK_…`) | constraint name |
| 17 | `IX_conversation_human_replies_tenant_id`: present in the model, deliberately absent from the chain | extra index (reverse drift) |
| 18 | The four shipped plans (`legacy`, `development`, `starter`, `free`) | reference data **not listed by Phase 33** |
| 19 | `conversations.customer_id` nullable in the chain, required by the model | **real integrity defect** |

Items 17 and 18 were not in Phase 33's list. Without item 18, a model-built
database cannot create a workspace: `appsettings.Development.json` names
`development`, and fixtures name `legacy`.

## 5. Model corrections

| Gap | Correction |
| --- | --- |
| 1 | `builder.HasPostgresExtension("vector")` in `OmniAiDbContext` |
| 2–4 | New internal `KnowledgeChunkRow` + `KnowledgeChunkRowConfiguration`: shadow text/vector/profile/time columns, `vector(EmbeddingOptions.Dimensions)`, key, check, alternate key, composite FK, tenant/document index, `HasMethod("hnsw").HasOperators("vector_cosine_ops")`. No `DbSet`; the stores still use SQL |
| 5, 6 | `HasCheckConstraint` with the original predicates |
| 7–9 | `HasOne<…>().WithMany().HasForeignKey(composite).HasPrincipalKey(composite)`, original names and delete rules |
| 10–13 | New internal `UserReferenceRow` (the `users` table typed `UserId`, `ToTable("users", "public", t => t.ExcludeFromMigrations())`) as the principal |
| 14, 15 | `HasIndex(...).HasFilter(...)` |
| 16 | `HasKey(...).HasName("pk_conversation_human_replies")` |
| 17 + FK indexes | `ForeignKeyIndexPolicy`: a derived `ForeignKeyIndexConvention` (`CreateIndex` override, installed in `ConfigureConventions`) plus `.WithoutConventionIndex()` on 8 relationships, each with its reason |
| 18 | `PlanConfiguration.HasData`: same ids, codes, names, limits, dates |
| 19 | Already `IsRequired()`. The clean Initial creates it `NOT NULL` |

**Technical evidence for the two non-obvious mechanisms.** Both were verified in
scratch before use.

- `HasOne<OmniAiUser>().HasForeignKey(m => m.UserId)` fails with *"cannot target
  the primary key {'Id' : Guid} because it is not compatible"*.
- A `UserId` shadow alternate key on `OmniAiUser` mapped to `id` fails with
  *"'OmniAiUser.Id' and 'OmniAiUser.DomainKey' are both mapped to column 'id' …
  same hierarchy"*.

Changing the Identity key type or weakening the Domain `UserId` were rejected, as
Phase 12 recorded, and both would have been out of scope. `ExcludeFromMigrations`
is EF's documented way to reference another mapping's table.

For the FK-index convention, EF 10.0.11's `ForeignKeyIndexConvention` source was
read. It is public, `CreateIndex` is virtual, and it has no per-relationship
opt-out. Without the policy, the model re-adds indexes that accepted decisions
rejected. One of them, the reply tenant index, is asserted absent by
`HandoffSchemaTests`.

**No package was added.** No pgvector .NET package was needed. The embedding
property's CLR type is irrelevant because EF never reads or writes it.

## 6. Scratch Initial and semantic comparison

The corrected model was scaffolded in the scratch copy (`/scratch` inside the
SDK container, never in the repository), rendered by Npgsql, and applied to a
fresh database B1.

**Result: A and B1 describe 700 facts each and differ in exactly one line.**

```
< COL|conversations.customer_id uuid NULL
> COL|conversations.customer_id uuid NOT NULL
```

Everything else is identical: every table, column, type, default, key, FK,
delete rule, check, index definition, partial predicate, HNSW method and
operator class, extension, sequence, and **every constraint and index name**.
The plan rows are byte-identical (the same md5 over all four rows).

That single line is the **expected fix** (§17 of the phase brief), not a parity
failure.

**Harmless difference, observed separately:** physical column *order* in
`web_chat_sessions`. The chain appended `client_mode` and `embedded_origin` in
Phase 18, while the Initial creates columns in EF's order. The column set is
identical. One test that compared by `ordinal_position` now compares by name.

**Additional historical defects:** the only real defect is `customer_id`. The
reverse drift in item 17 and the name in item 16 were chain-side hand edits,
now expressed in the model.

## 7. Consolidation

1. Removed the 40 development migration files (20 migrations + Designers) and
   the old snapshot from the working tree with `rm`, nothing staged. The folder's
   `.editorconfig`, which relaxes two style rules for generated code, was kept.
2. Ran `dotnet ef migrations add Initial` once:
   `backend/src/OmniAI.Infrastructure/Persistence/Migrations/20260925225105_Initial.cs`
   (+ Designer + snapshot).
3. **Zero hand edits.** SHA-256 values were recorded right after generation and
   re-verified at the end (all `OK`). `Initial.cs` and the snapshot are
   byte-identical to the scratch generation apart from the timestamp.
4. `migrationBuilder.Sql` occurrences: **0**. The migration C# names `xmin` for
   each row-version table (provider-recognised operations). The rendered SQL
   contains **0** `xmin`.
5. The snapshot contains the extension annotation, `vector(1536)`, the HNSW method
   and operator annotations, all checks and filters, the excluded `users`
   reference, eight `OmniAI:SuppressForeignKeyIndex` annotations, a required
   `CustomerId`, xmin row versions and the plan `HasData`.

Final migration count: **1**.

## 8. Fresh database from zero

`dotnet ef database update` ran against an empty database that had no tables
and no extension, applying only the Initial. Afterwards:

- the `vector` extension exists;
- the catalog equals A apart from `customer_id NOT NULL`;
- `__EFMigrationsHistory` holds only `20260925225105_Initial`;
- all four plans are present.

A direct insert of a conversation with `customer_id NULL` was refused:
`null value in column "customer_id" … violates not-null constraint`.

## 9. Application startup

I ran the Release API (Development environment) against that database with no
master key and no provider credentials.

| Check | Result |
| --- | --- |
| `/health/live` | Healthy |
| `/health/ready` | Healthy |
| `GET /api/auth/csrf` | 204 |
| `POST /api/auth/register` (synthetic `@example.invalid`) | 201 |
| `POST /api/tenants` | 201 |
| `plan-usage` | resolved the model-seeded `development` plan (unlimited, members used 1) |
| Data Protection key ring | persisted to its table |

Two warnings appeared, both known: an unencrypted key ring without a master key
(Phase 33 debt), and the container's port override. There was no schema bootstrap
SQL of any kind.

## 10. RAG / pgvector regression

All deterministic (no live OpenAI). Each item passed against real PostgreSQL + pgvector.

| Requirement | Test |
| --- | --- |
| Document persistence, chunk creation, ordering | `KnowledgeIndexStoreTests.Indexing_writes_every_chunk_in_order` |
| Chunk replacement | `Re_indexing_replaces_the_whole_set_rather_than_adding_to_it`, `A_refused_re_index_leaves_the_previous_chunks_exactly_as_they_were` |
| Vector persistence | `KnowledgeSearchStoreTests.A_vector_survives_a_round_trip_through_the_database` |
| Dimensions | `KnowledgeSchemaTests.The_embedding_column_is_a_vector_of_the_configured_width`, `A_vector_of_the_wrong_width_is_refused_by_the_column` |
| Similarity retrieval, cosine | `Results_come_back_in_real_cosine_order`, `Only_the_requested_number_of_results_comes_back` |
| HNSW existence + operator | `The_embedding_has_an_hnsw_index_for_cosine_distance`, `DatabaseModelAuthorityTests.The_chunk_embedding_has_an_hnsw_cosine_index` |
| Agent-to-Knowledge assignment | `Another_agent_in_the_same_workspace_gets_nothing_it_was_not_given`, `An_unassigned_document_is_never_returned` |
| Tenant isolation | `Another_workspace_identical_vector_is_never_returned`, `A_chunk_must_belong_to_the_same_workspace_as_its_document`, `A_chunk_carries_the_workspace_of_its_own_document` |
| Enabled/ready eligibility | `A_disabled_document_stops_answering_immediately`, `An_edited_document_stops_answering_until_it_is_indexed_again`, `An_index_made_under_another_profile_is_never_compared_against` |
| Unchanged-document zero cost | `IndexKnowledgeDocumentTests.Re_indexing_unchanged_content_makes_no_provider_call_at_all`, `OpenAiEmbeddingAccountingTests.Re_indexing_unchanged_text_makes_no_provider_request` |
| No provider call without eligible Knowledge | `WebChatKnowledgeTests.An_agent_with_no_eligible_knowledge_never_pays_to_embed_a_question`, `Eligibility_answers_no_before_anything_is_embedded` |

## 11. Database constraint regression

Real PostgreSQL throughout. The Phase 17–33 schema suites pass on the
Initial-built schema: `KnowledgeSchemaTests`, `HandoffSchemaTests`,
`TenantMembershipSchemaTests`, `WebChatWidgetSchemaTests`,
`KnowledgeSourcePersistenceTests`, `MigrationSchemaTests`, the Phase 33
reconciliation and composite-FK tests, and the customer merge and concurrency
tests. They cover the composite tenant FKs, the uniqueness constraints, checks,
partial indexes, and vector/index schema.

## 12. New and changed tests

**New: `DatabaseModelAuthorityTests` (20 cases).**

- The central test builds a second database from the model alone (EF differ +
  Npgsql generator) and requires its catalog description to equal the migrated
  database's.
- The other cases pin these invariants:
  - the vector extension and `vector(1536) NOT NULL`;
  - HNSW with `vector_cosine_ops`, read from `pg_am` / `pg_opclass`;
  - `customer_id NOT NULL`;
  - nine FK definitions with delete rules;
  - three checks;
  - two partial predicates;
  - no physical `xmin`;
  - the seeded plan codes.
- Migration source text is never read.

**Changed:**

- `MigrationSchemaTests`: 20 migrations → the single `_Initial`; stale
  "removed by hand" comments corrected; every xmin assertion kept.
- `WebChatSchemaTests`: the column set is compared by name, not physical order.
- `HandoffSchemaTests`, `FakeEntitlements`: comments only.

**Removed:** `CustomerBackfillTests` (1 test). It migrated to Phase 25, seeded
legacy rows and exercised the Phase 30 upgrade backfill. That migration no longer
exists, and upgrade-only backfills are intentionally excluded from a fresh-schema
baseline (§32 of the brief). The final-schema facts it touched (`customer_id`
NOT NULL, the composite customer FK) are asserted elsewhere.

Backfills intentionally excluded from the baseline, all of which only upgraded
development data:

- the Phase 13 Owner roles and assignments;
- the Phase 14, 16, 18 and 19 column backfills;
- the Phase 24 legacy plan assignments;
- the Phase 30 customer identities.

## 13. Quality gates

| Gate | Result |
| --- | --- |
| `dotnet restore OmniAI.slnx` | up to date |
| `dotnet build OmniAI.slnx -c Release` | **0 warnings, 0 errors** (warnings are errors) |
| `dotnet test OmniAI.slnx -c Release` | **3,361 passed, 0 failed, 0 skipped**: Domain 797, Application 714, Api.IntegrationTests 860, Infrastructure.IntegrationTests 990 |
| `dotnet list package --vulnerable --include-transitive` | no vulnerable packages in any project |

The full run passed first time. One earlier *targeted* run had one failure: the
column-order test in §12, a test-coupling issue rather than a product defect. It
was fixed before the full run.

## 14. Local databases

The pre-existing local synthetic databases (`omniai`, `omniai_dev`,
`omniai_p25`, `omniai_p26a`, `omniai_p30`, `omniai_p31`, `omniai_p32`) were
**not touched**. They were not migrated and not dropped. They carry the old chain
and must be recreated from the Initial before they are next used; do not run
`database update` against them. Every temporary Phase 33.1 database (`hist`,
`model0`, `model1`, `fresh`) was dropped, and the test suites left no
`omniai_test_*` databases behind. The scratch copy and the SDK container were
removed.

## 15. External traffic

| | |
| --- | --- |
| Live email | NO |
| Live Meta | NO |
| Live OpenAI | NO (no `OPENAI_API_KEY` in the environment) |
| Live Stripe | NO |

`OMNIAI_LIVE_OPENAI`, `OMNIAI_LIVE_META` and `OMNIAI_LIVE_EMAIL` were never set.
The frontend was not changed. No package was added.

## 16. Remaining warnings

- Existing local development databases must be recreated (§14).
- `UserReferenceRow` relies on `public.users` being distinct from Identity's
  unqualified `users` in EF's eyes. If a default schema is ever configured, model
  validation fails loudly. It never fails silently.
- The FK index policy is a derived EF convention. An EF major upgrade should
  re-run `DatabaseModelAuthorityTests`, which would catch any behaviour change.
- The Phase 33 items (master key, Angular budget warning, and others) are unchanged.
