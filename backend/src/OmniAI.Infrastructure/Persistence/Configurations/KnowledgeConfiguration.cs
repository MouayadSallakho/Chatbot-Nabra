using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Knowledge;
using OmniAI.Domain.Tenants;
using OmniAI.Infrastructure.Knowledge;

namespace OmniAI.Infrastructure.Persistence.Configurations;

/// <summary>
/// The knowledge document table.
/// </summary>
/// <remarks>
/// <para>
/// <strong>There is no vector here.</strong> Chunks and their embeddings are not
/// part of the EF model at all -- see <c>KnowledgeIndexStore</c> for why. Keeping
/// them out means no query anywhere can accidentally load a megabyte of floats,
/// and no projection can accidentally publish one.
/// </para>
/// <para>
/// <strong>The alternate key on (id, tenant_id) is load-bearing.</strong> It is
/// what the chunk table and the assignment table point at, and it is what makes a
/// row naming one workspace's agent and another workspace's document impossible
/// to write: the same tenant_id has to satisfy both composite foreign keys at
/// once.
/// </para>
/// </remarks>
internal sealed class KnowledgeDocumentConfiguration : IEntityTypeConfiguration<KnowledgeDocument>
{
    public void Configure(EntityTypeBuilder<KnowledgeDocument> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Index metadata is all three columns or none of them. A row holding a
        // fingerprint but no profile would be a document claiming to be indexed
        // by nothing in particular, and readiness would have to guess what that
        // meant.
        builder.ToTable("knowledge_documents", table =>
            table.HasCheckConstraint(
                "ck_knowledge_documents_index_complete",
                "(indexed_fingerprint IS NULL AND indexed_profile IS NULL AND indexed_at IS NULL) "
                + "OR (indexed_fingerprint IS NOT NULL AND indexed_profile IS NOT NULL AND indexed_at IS NOT NULL)"));

        builder.HasKey(document => document.Id);

        builder.Property(document => document.Id)
            .HasColumnName("id")
            .HasConversion(PersistenceConverters.KnowledgeDocumentId)
            .ValueGeneratedNever();

        builder.Property(document => document.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(PersistenceConverters.TenantId)
            .IsRequired();

        builder.Property(document => document.Title)
            .HasColumnName("title")
            .HasConversion(PersistenceConverters.KnowledgeTitle)
            .HasMaxLength(KnowledgeTitle.MaxLength)
            .IsRequired();

        builder.Property(document => document.Content)
            .HasColumnName("content")
            .HasConversion(PersistenceConverters.KnowledgeContent)
            .HasMaxLength(KnowledgeContent.MaxLength)
            .IsRequired();

        builder.Property(document => document.IsEnabled)
            .HasColumnName("is_enabled")
            .IsRequired();

        // Stored as well as derived. The retrieval query needs to compare it
        // against the indexed fingerprint inside the database -- that comparison
        // is what excludes a document whose text has moved on since it was
        // indexed, and it cannot be done in memory without loading every
        // document's content on every turn.
        builder.Property<byte[]>(PersistenceSchema.KnowledgeContentFingerprint)
            .HasColumnName("content_fingerprint")
            .HasColumnType("bytea")
            .IsRequired();

        builder.Property(document => document.IndexedFingerprint)
            .HasColumnName("indexed_fingerprint")
            .HasConversion(PersistenceConverters.NullableContentFingerprint)
            .HasColumnType("bytea");

        builder.Property(document => document.IndexedProfile)
            .HasColumnName("indexed_profile")
            .HasConversion(PersistenceConverters.NullableEmbeddingProfileId)
            .HasMaxLength(EmbeddingProfileId.MaxLength);

        builder.Property(document => document.IndexedAt)
            .HasColumnName("indexed_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(document => document.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(document => document.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(document => document.TenantId)
            .HasConstraintName("fk_knowledge_documents_tenant")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.HasAlternateKey(document => new { document.Id, document.TenantId })
            .HasName("ak_knowledge_documents_id_tenant");

        builder.HasIndex(document => document.TenantId)
            .HasDatabaseName("ix_knowledge_documents_tenant");

        builder.Property<uint>(PersistenceSchema.XminConcurrencyToken)
            .HasColumnName(PersistenceSchema.XminConcurrencyToken)
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();
    }
}

/// <summary>
/// Which agents may draw on which documents.
/// </summary>
/// <remarks>
/// Two composite foreign keys and a composite primary key, and each one is doing
/// real work. The agent key and the document key both carry the workspace, so a
/// cross-workspace pair cannot be written at all. The primary key on
/// (agent, document) makes the same assignment twice impossible rather than
/// merely unusual.
/// </remarks>
internal sealed class AgentKnowledgeAssignmentConfiguration
    : IEntityTypeConfiguration<AgentKnowledgeAssignment>
{
    public void Configure(EntityTypeBuilder<AgentKnowledgeAssignment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("ai_agent_knowledge_documents");

        builder.HasKey(assignment => new
        {
            assignment.AiAgentId,
            assignment.KnowledgeDocumentId,
        }).HasName("pk_ai_agent_knowledge_documents");

        builder.Property(assignment => assignment.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(PersistenceConverters.TenantId)
            .IsRequired();

        builder.Property(assignment => assignment.AiAgentId)
            .HasColumnName("ai_agent_id")
            .HasConversion(PersistenceConverters.AiAgentId)
            .IsRequired();

        builder.Property(assignment => assignment.KnowledgeDocumentId)
            .HasColumnName("knowledge_document_id")
            .HasConversion(PersistenceConverters.KnowledgeDocumentId)
            .IsRequired();

        builder.Property(assignment => assignment.AssignedAt)
            .HasColumnName("assigned_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne<AiAgent>()
            .WithMany()
            .HasForeignKey(assignment => new { assignment.AiAgentId, assignment.TenantId })
            .HasPrincipalKey(agent => new { agent.Id, agent.TenantId })
            .HasConstraintName("fk_ai_agent_knowledge_documents_agent")
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        builder.HasOne<KnowledgeDocument>()
            .WithMany()
            .HasForeignKey(assignment => new
            {
                assignment.KnowledgeDocumentId,
                assignment.TenantId,
            })
            .HasPrincipalKey(document => new { document.Id, document.TenantId })
            .HasConstraintName("fk_ai_agent_knowledge_documents_document")
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();
    }
}

/// <summary>
/// Real indexing attempts: the durable anchor that lets a crashed attempt be
/// settled and alerted on exactly once.
/// </summary>
/// <remarks>
/// A composite foreign key to the document, so an attempt can only ever belong to
/// a document of its own workspace, and it goes with the document. The status is
/// bounded by a check constraint, and a partial index serves the reconciler's only
/// query: the few rows still Started.
/// </remarks>
internal sealed class KnowledgeIndexAttemptConfiguration : IEntityTypeConfiguration<KnowledgeIndexAttempt>
{
    public void Configure(EntityTypeBuilder<KnowledgeIndexAttempt> builder)
    {
        builder.ToTable(PersistenceSchema.KnowledgeIndexAttemptsTable, table =>
        {
            table.HasCheckConstraint(
                "ck_knowledge_index_attempts_status",
                "status IN ('Started', 'Succeeded', 'Failed', 'Unknown')");
            table.HasCheckConstraint(
                "ck_knowledge_index_attempts_settled",
                "(status = 'Started') = (completed_at IS NULL)");
        });

        builder.HasKey(attempt => attempt.Id);

        builder.Property(attempt => attempt.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(attempt => attempt.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(PersistenceConverters.TenantId)
            .IsRequired();

        builder.Property(attempt => attempt.DocumentId)
            .HasColumnName("document_id")
            .HasConversion(PersistenceConverters.KnowledgeDocumentId)
            .IsRequired();

        builder.Property(attempt => attempt.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(attempt => attempt.FailureCode)
            .HasColumnName("failure_code")
            .HasMaxLength(KnowledgeIndexAttemptFailures.MaxLength);

        builder.Property(attempt => attempt.StartedAt)
            .HasColumnName("started_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(attempt => attempt.CompletedAt)
            .HasColumnName("completed_at")
            .HasColumnType("timestamp with time zone");

        builder.HasOne<KnowledgeDocument>()
            .WithMany()
            .HasForeignKey(attempt => new { attempt.DocumentId, attempt.TenantId })
            .HasPrincipalKey(document => new { document.Id, document.TenantId })
            .HasConstraintName("fk_knowledge_index_attempts_document")
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        builder.HasIndex(attempt => attempt.StartedAt)
            .HasDatabaseName("ix_knowledge_index_attempts_started")
            .HasFilter("status = 'Started'");

        // Leads with the document so it also serves the composite foreign key.
        builder.HasIndex(attempt => new { attempt.DocumentId, attempt.TenantId })
            .HasDatabaseName("ix_knowledge_index_attempts_document");
    }
}

/// <summary>
/// Maps <c>knowledge_chunks</c>, the vector index behind retrieval.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Schema only.</strong> The Phase 17 stores write and read this table in
/// SQL and never through EF; see <see cref="KnowledgeChunkRow"/>. Everything here
/// describes the table exactly as Phase 17 created it by hand, so a migration
/// generated from the model produces it with no hand-written SQL.
/// </para>
/// <para>
/// <strong>The vector is a real <c>vector</c> column</strong>, its width taken
/// from <see cref="EmbeddingOptions.Dimensions"/>: vectors of different widths
/// cannot be compared, so the column is what makes a mismatch a refused insert
/// rather than a silently meaningless distance. The CLR type of the shadow
/// property is irrelevant because EF never reads or writes it; only its store
/// type reaches the migration.
/// </para>
/// <para>
/// <strong>HNSW with cosine distance</strong>, matching the <c>&lt;=&gt;</c>
/// operator the retrieval query orders by. Cosine because embeddings encode
/// direction rather than magnitude; HNSW rather than IVFFlat because it needs no
/// training pass over existing data, which a workspace indexing its first
/// document does not have. Expressed with Npgsql's own index method and operator
/// metadata, so the migration generator writes the <c>USING hnsw</c> clause.
/// </para>
/// </remarks>
internal sealed class KnowledgeChunkRowConfiguration : IEntityTypeConfiguration<KnowledgeChunkRow>
{
    private const string Embedding = "Embedding";

    public void Configure(EntityTypeBuilder<KnowledgeChunkRow> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(PersistenceSchema.KnowledgeChunksTable, table =>
            table.HasCheckConstraint("ck_knowledge_chunks_ordinal", "ordinal >= 0"));

        builder.HasKey(chunk => chunk.Id).HasName("pk_knowledge_chunks");

        builder.Property(chunk => chunk.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(chunk => chunk.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(PersistenceConverters.TenantId)
            .IsRequired();

        builder.Property(chunk => chunk.KnowledgeDocumentId)
            .HasColumnName("knowledge_document_id")
            .HasConversion(PersistenceConverters.KnowledgeDocumentId)
            .IsRequired();

        builder.Property(chunk => chunk.Ordinal)
            .HasColumnName("ordinal")
            .IsRequired();

        builder.Property<string>("Content")
            .HasColumnName("content")
            .HasColumnType("text")
            .IsRequired();

        builder.Property<string>(Embedding)
            .HasColumnName("embedding")
            .HasColumnType($"vector({EmbeddingOptions.Dimensions})")
            .IsRequired();

        builder.Property<string>("EmbeddingProfile")
            .HasColumnName("embedding_profile")
            .HasMaxLength(EmbeddingProfileId.MaxLength)
            .IsRequired();

        builder.Property<DateTimeOffset>("CreatedAt")
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        // A document's passages are numbered once each. A unique constraint, as
        // Phase 17 declared it, and it also leads with the document -- which is
        // why the composite key below needs no index of its own.
        builder.HasAlternateKey(chunk => new { chunk.KnowledgeDocumentId, chunk.Ordinal })
            .HasName("ux_knowledge_chunks_document_ordinal");

        // The tenant boundary. Into (id, tenant_id) rather than id alone, so a
        // chunk filed under a workspace its document does not belong to cannot be
        // written. Cascade: a passage means nothing without its document.
        builder.HasOne<KnowledgeDocument>()
            .WithMany()
            .HasForeignKey(chunk => new { chunk.KnowledgeDocumentId, chunk.TenantId })
            .HasPrincipalKey(document => new { document.Id, document.TenantId })
            .HasConstraintName("fk_knowledge_chunks_document")
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired()
            .WithoutConventionIndex();

        // The workspace-scoped read and the per-document replacement both filter
        // on this pair.
        builder.HasIndex(chunk => new { chunk.TenantId, chunk.KnowledgeDocumentId })
            .HasDatabaseName("ix_knowledge_chunks_tenant_document");

        builder.HasIndex(Embedding)
            .HasDatabaseName("ix_knowledge_chunks_embedding")
            .HasMethod("hnsw")
            .HasOperators("vector_cosine_ops");
    }
}
