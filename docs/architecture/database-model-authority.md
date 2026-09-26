# Database model authority

Phase 33.1. Applies to every schema change from now on.

## 1. The rule

**The final database schema is derived from the EF Core / Npgsql model.**

The model (entity configurations, Infrastructure persistence rows, model-level
annotations and seed data) is the source. A migration is generated output: a
record of how one model version became the next. Anyone must be able to delete
the development migrations, scaffold a single Initial from the model, apply it
to an empty PostgreSQL database, and get the complete OmniAI schema. That means
tables, columns, types, nullability, keys, foreign keys and their delete rules,
checks, partial and HNSW indexes, the `vector` extension, sequences and shipped
reference data. None of it should depend on remembering a hand-written step.

`DatabaseModelAuthorityTests` enforces this. It builds one database from the
repository's migrations and one from the model alone, through EF's differ and
Npgsql's migrations SQL generator. It then requires their PostgreSQL catalog
descriptions to be identical.

## 2. Migration generation policy

1. Change the model. Run `dotnet ef migrations add <Name>` (from `backend/`, with
   `-p src/OmniAI.Infrastructure -s src/OmniAI.Infrastructure`).
2. **Do not edit the generated migration, its Designer or the snapshot.** Do not
   remove, add or reorder operations, and do not change nullability or names by
   hand.
3. If the output is wrong, the model is wrong. Revert the database if it was
   updated, run `dotnet ef migrations remove`, fix the model, and generate again.
4. Render the SQL (`dotnet ef migrations script`) and read it. Apply it to a
   disposable PostgreSQL 17 + pgvector database. Run the suite.
5. `migrations/.editorconfig` relaxes two style rules (block namespaces and
   inline arrays) for generated code only. It is not a migration and stays.

**Final-schema objects never go into `migrationBuilder.Sql`.** A future
upgrade may still need a data step, such as a backfill that turns existing rows
into the new shape. That step goes in its own migration: first generate the
schema migration, then create an empty migration with `migrations add` while the
model has no pending change. The empty one holds only data statements, and it
must not create or change schema objects. Generated schema migrations stay
untouched.

**If the pinned provider genuinely cannot express a final-schema object**, stop.
Record the evidence and take an explicit architecture decision before any
workaround.

## 3. Historical raw SQL: classification

The development chain (Phases 4–33, 20 migrations) contained 35 hand-written
`migrationBuilder.Sql` statements in 11 migrations. It also had hand-added or
hand-removed EF operations in several others. Each is classified below by what
it did, and by how the Initial now reproduces it.

| Migration | What | Class | Now |
| --- | --- | --- | --- |
| Phase 13 `AddTenantRolesAndPermissions` | Owner role for each existing tenant + assignment for each existing member | B – legacy backfill | Excluded: a fresh database has no tenants |
| Phase 14 `AddTenantInvitations…` | `tenant_memberships.updated_at` backfill then `SET NOT NULL` | B + C | Column is `NOT NULL` from the model |
| Phase 16 `AddWebChatSessionExpiry` | `expires_at` add nullable, backfill `created_at + 24h`, `SET NOT NULL` (3) | B + C | Column is `NOT NULL` from the model |
| Phase 17 `AddKnowledgeBaseAndVectorIndex` | `CREATE EXTENSION vector` | A – final schema | `HasPostgresExtension("vector")` |
| 〃 | `ck_knowledge_documents_index_complete` | A | `HasCheckConstraint` |
| 〃 | `knowledge_chunks` table (PK, check, unique, composite FK) | A | `KnowledgeChunkRow` + configuration |
| 〃 | `ix_knowledge_chunks_tenant_document` | A | `HasIndex` |
| 〃 | `ix_knowledge_chunks_embedding USING hnsw (vector_cosine_ops)` | A | `HasMethod("hnsw").HasOperators("vector_cosine_ops")` |
| Phase 18 `AddWebChatWidgetEmbedding` | `client_mode` add/backfill `'Hosted'`/`SET NOT NULL` (3) | B + C | `NOT NULL` from the model |
| 〃 | `ck_web_chat_sessions_embedded_origin` | A | `HasCheckConstraint` |
| 〃 | widget columns add/backfill defaults/`SET NOT NULL` (3) | B + C | `NOT NULL` from the model |
| 〃 | Down: drop check | D | Not applicable to a baseline |
| Phase 19 `AddHumanHandoffAndAssignment` | `handling` add/backfill `'Ai'`/`SET NOT NULL` (3) | B + C | `NOT NULL` from the model |
| 〃 | `fk_conversations_assigned_member` | A | Composite FK to the membership key |
| 〃 | `ix_conversations_assigned_to WHERE assigned_to IS NOT NULL` | A | `HasIndex(...).HasFilter(...)` |
| 〃 | `fk_human_replies_sender_member` | A | Composite FK to the membership key |
| 〃 | Down: drop FK and index (2) | D | Not applicable |
| Phase 20 `AddWhatsAppCloudApiChannel` | `ux_whatsapp_contacts_current WHERE is_current` | A | `HasIndex(...).IsUnique().HasFilter("is_current")` |
| Phase 21 `AddKnowledgeFileSources` | `fk_knowledge_sources_document` | A | Composite FK to the documents' alternate key |
| Phase 24 `AddWorkspacePlansAndEntitlements` | Existing tenants onto `legacy` | B | Excluded: no tenants |
| Phase 25 `AddSaasSubscriptionBilling` | Insert the `free` plan | A – reference data | `HasData` |
| 〃 | Down: delete it | D | Not applicable |
| Phase 30 `AddUnifiedCustomerIdentity` | Customer backfill (3: WhatsApp senders, link, orphans) | B | Excluded: no conversations |

Hand-edited EF operations that were not raw SQL, and are now model-driven:

- `fk_tenant_memberships_user` (Phase 12) and `fk_notifications_recipient_user`,
  `fk_notification_emails_recipient_user`, `fk_notification_preferences_user`
  (Phase 32) were added by hand as `AddForeignKey` operations.
- Phase 24 inserted `legacy`, `development` and `starter` through a hand-written
  `InsertData`. These are now `HasData`.
- Phase 19 renamed the reply table's key to `pk_conversation_human_replies` and
  removed the scaffolded tenant index. Both are now in the model.
- Every migration before Phase 30 removed the scaffolded `xmin` column by hand
  (see §9).

**Class B/C steps are intentionally absent from the Initial.** They existed only
to move development databases from one shape to the next. A fresh database has
no rows to transform. The final shape they produced is in the model.

## 4. PostgreSQL extensions

`OmniAiDbContext.OnModelCreating` declares
`builder.HasPostgresExtension(PersistenceSchema.VectorExtension)`. The generated
migration carries it as a database annotation. Npgsql renders it as
`CREATE EXTENSION IF NOT EXISTS vector;` before any table, so the `vector(1536)`
column's type exists by the time it is used. Infrastructure has to provide a
PostgreSQL build with pgvector available (for example `pgvector/pgvector:pg17`).
The application declares that it needs the extension; nobody creates it by hand.

## 5. `knowledge_chunks`

Phase 17 kept chunks out of the EF model so no query could load a vector by
accident. That intent stands: chunks are still written only by
`KnowledgeIndexStore` and read only by `KnowledgeSearchStore`, in parameterised
SQL, and the context has no set for them.

What changed is that the **table is now described by the model**, through the
internal Infrastructure row `KnowledgeChunkRow`:

- Its CLR properties are only `Id`, `TenantId`, `KnowledgeDocumentId` and
  `Ordinal`. `content`, `embedding`, `embedding_profile` and `created_at` are
  shadow properties, so the class has no field that could hold a vector.
- The embedding column is `vector({EmbeddingOptions.Dimensions})`, which is
  `vector(1536)`. EF never reads or writes the property, so its CLR type is
  irrelevant. Only the store type reaches the migration. No pgvector .NET
  package was needed.
- Key `pk_knowledge_chunks`. Check `ck_knowledge_chunks_ordinal` (`ordinal >= 0`).
  Unique constraint `ux_knowledge_chunks_document_ordinal`.
- Composite FK `fk_knowledge_chunks_document`: `(knowledge_document_id,
  tenant_id) → knowledge_documents(id, tenant_id) ON DELETE CASCADE`. This is the
  tenant boundary Phase 17 established.
- Index `ix_knowledge_chunks_tenant_document (tenant_id, knowledge_document_id)`.

Nothing extra is stored and the RAG semantics are unchanged.

## 6. HNSW

```csharp
builder.HasIndex("Embedding")
    .HasDatabaseName("ix_knowledge_chunks_embedding")
    .HasMethod("hnsw")
    .HasOperators("vector_cosine_ops");
```

These are Npgsql's own index annotations. The generator writes
`USING hnsw (embedding vector_cosine_ops)`: cosine distance, matching the `<=>`
the retrieval query orders by. The index method and operator class are asserted
from `pg_am` / `pg_opclass`, not by parsing DDL.

## 7. Foreign keys, checks and partial indexes

**Composite tenant keys.** EF expresses a key into a non-primary pair with
`HasPrincipalKey` on an alternate key or composite primary key, with no
navigation on the Domain type. Earlier comments claiming otherwise were wrong.
`fk_conversations_assigned_member` and `fk_human_replies_sender_member` target
`tenant_memberships (user_id, tenant_id)`, the membership primary key.
`fk_knowledge_sources_document` and `fk_knowledge_chunks_document` target
`ak_knowledge_documents_id_tenant`. Delete rules are unchanged: Restrict for the
membership keys, Cascade for the document keys.

**Identity `users` keys.** Domain rows hold a `UserId` value object, while
`IdentityUser<Guid>.Id` is a `Guid`. EF refuses to relate properties of different
CLR types. It also refuses a second property mapped to `users.id` on the Identity
entity. Both refusals were verified. The keys therefore target `UserReferenceRow`:
the same table, typed `UserId`, mapped as `public.users` with
`ExcludeFromMigrations()`. That is EF's supported way to reference a table another
mapping owns. Migrations emit the four FKs and never create, alter or drop `users`.
The schema qualification only stops EF treating it as a second entity sharing
Identity's table. PostgreSQL resolves both names to the same table, and the catalog
shows `REFERENCES users(id)`. If someone ever sets a default schema of `public`,
model validation fails loudly rather than silently.

**Checks.** `ck_knowledge_documents_index_complete` and
`ck_web_chat_sessions_embedded_origin` join the model's other
`HasCheckConstraint` declarations, with their Phase 17/18 predicates unchanged.

**Partial indexes.** `ix_conversations_assigned_to WHERE assigned_to IS NOT NULL`
and `ux_whatsapp_contacts_current WHERE is_current` use `HasFilter`.

**Foreign-key index policy.** EF indexes every foreign key not already led by an
index or key. Where an accepted decision said otherwise, earlier phases deleted
the generated index by hand, so it came back on every regeneration. A relationship
can now opt out in the model with `.WithoutConventionIndex()`
(`ForeignKeyIndexPolicy`). That is a derived `ForeignKeyIndexConvention`
overriding its virtual `CreateIndex`, installed in `ConfigureConventions`. It is
used on eight relationships, each with its reason beside it:

- the reply table's tenant key: no index, as `HandoffSchemaTests` requires;
- the reply sender key;
- the assignee key, served by the partial index;
- the chunk and source document keys, led by their unique constraint and index;
- the three notification recipient keys, which Phase 32 left unindexed.

Every other relationship keeps EF's default. Phase 34 owns any wider index review.

## 8. `Conversation.CustomerId`

The Domain requires it (`Conversation.CustomerId` is a non-nullable
`CustomerId`), and the model maps it `IsRequired()` with the composite
`fk_conversations_customer (tenant_id, customer_id) → customers(tenant_id, id)
ON DELETE RESTRICT`. The Phase 30 migration changed the column with an
`AlterColumn` that omitted the old nullability, so Npgsql emitted no
`SET NOT NULL`. **Every chain-built database had `conversations.customer_id`
nullable.** That was a real integrity defect. The Initial creates it
`NOT NULL`, which is the only intended difference between a chain-built and a
model-built schema. Phase 30's semantics (one canonical customer for each
conversation, in the same workspace) are unchanged.

## 9. xmin

`xmin` is PostgreSQL's per-row transaction-id system column. Mutable aggregates
map it as a row-version concurrency token (`uint`, `xid`). EF's differ is
provider-agnostic, so the generated C# does contain an `xmin` column for each
such table. Npgsql's migrations SQL generator recognises the system column and
emits **no DDL** for it. Rendered SQL contains no `xmin`, and no table has a
physical column of that name. The evidence is the model metadata, the rendered
SQL, the catalog, and the concurrency tests. Searching migration C# for the
word proves nothing. The historical hand-removal of those lines was never
necessary.

## 10. Seed data

The four shipped plans (`legacy`, `development`, `starter`, `free`, with their
original ids, limits and dates) are `HasData` in `PlanConfiguration`.
Development configuration names `development`, and fixtures name `legacy`, so
a model-built database has to contain them.

## 11. The Initial baseline

OmniAI has never been deployed, and no customer, VPS or shared database depends on
the old chain. Phase 33.1 therefore replaced the 20 development migrations with
one generated, unedited `…_Initial`. The old files remain in Git history.
**Existing development databases are not migrated across this reset.** Recreate
them from the Initial. The next schema change adds one generated migration beside
the Initial, and from the first real deployment onward the chain is never reset
again.

## 12. Rules for future migrations

- Model first. If a migration needs a hand edit, stop and fix the model.
- Put new final-schema objects in configuration: keys, FKs (composite and
  tenant-aware), checks, filtered indexes, provider index metadata, extensions,
  and reference data through `HasData`.
- Use `.WithoutConventionIndex()` only with a stated reason. Speculative indexes
  are not added.
- Keep upgrade-only data steps out of schema migrations. After the first
  deployment, a data step lives in its own empty-generated data migration (§2).
  Keep it minimal, test it against the previous schema, and never use it to
  create an object the model lacks.
- `DatabaseModelAuthorityTests` must stay green. A failure there means the
  migrations and the model disagree.
