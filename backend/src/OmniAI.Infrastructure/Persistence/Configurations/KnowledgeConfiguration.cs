using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Knowledge;
using OmniAI.Domain.Tenants;

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

        builder.ToTable("knowledge_documents");

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
