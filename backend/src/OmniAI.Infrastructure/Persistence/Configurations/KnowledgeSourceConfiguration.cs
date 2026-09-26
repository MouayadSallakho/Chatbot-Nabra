using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OmniAI.Domain.Knowledge;
using OmniAI.Domain.Tenants;

namespace OmniAI.Infrastructure.Persistence.Configurations;

/// <summary>
/// Where a knowledge document's text came from.
/// </summary>
/// <remarks>
/// <para>
/// <strong>There is no file column, and that is the whole storage design.</strong>
/// No path, no bytes, no blob, no key into an object store. The upload is read
/// during the request and discarded; what persists is this row of metadata and
/// the document's text. Anybody looking for the original file will not find one,
/// which is the intended answer.
/// </para>
/// <para>
/// <strong>The foreign key is composite, and that is what makes a cross-workspace
/// source unrepresentable.</strong> A single-column key into the documents table
/// would let a row name a document belonging to somebody else, leaving an
/// application check as the only thing between that and one workspace's file
/// metadata attached to another's document. Carrying the workspace on both sides
/// and joining on the pair makes it impossible rather than merely checked.
/// </para>
/// </remarks>
internal sealed class KnowledgeSourceConfiguration : IEntityTypeConfiguration<KnowledgeSource>
{
    public void Configure(EntityTypeBuilder<KnowledgeSource> builder)
    {
        builder.ToTable(PersistenceSchema.KnowledgeSourcesTable);

        builder.HasKey(source => source.Id);

        builder.Property(source => source.Id)
            .HasColumnName("id")
            .HasConversion(PersistenceConverters.KnowledgeSourceId)
            .ValueGeneratedNever();

        builder.Property(source => source.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(PersistenceConverters.TenantId)
            .IsRequired();

        builder.Property(source => source.KnowledgeDocumentId)
            .HasColumnName("knowledge_document_id")
            .HasConversion(PersistenceConverters.KnowledgeDocumentId)
            .IsRequired();

        // Display metadata. Nothing joins this to a path, and nothing opens it.
        builder.Property(source => source.FileName)
            .HasColumnName("file_name")
            .HasConversion(PersistenceConverters.SourceFileName)
            .HasMaxLength(SourceFileName.MaxLength)
            .IsRequired();

        builder.Property(source => source.FileType)
            .HasColumnName("file_type")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(source => source.ByteLength)
            .HasColumnName("byte_length")
            .IsRequired();

        // A digest of the uploaded bytes, compared and never interpreted. It is
        // how an identical re-upload is recognised without keeping the file.
        builder.Property(source => source.Fingerprint)
            .HasColumnName("fingerprint")
            .HasConversion(PersistenceConverters.ContentFingerprint)
            .IsRequired();

        builder.Property(source => source.ExtractedAt)
            .HasColumnName("extracted_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(source => source.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(source => source.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(source => source.TenantId)
            .HasConstraintName("fk_knowledge_sources_tenant")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        // <strong>One file per document.</strong> "Which file is this document
        // from" has one answer, and a second row would make it two. Replacing the
        // file rewrites this row rather than adding another.
        builder.HasIndex(source => source.KnowledgeDocumentId)
            .IsUnique()
            .HasDatabaseName("ux_knowledge_sources_document");

        builder.HasIndex(source => source.TenantId)
            .HasDatabaseName("ix_knowledge_sources_tenant");

        // Into (id, tenant_id) on the documents table, so a source cannot describe
        // another workspace's document. Cascade, because a source is metadata
        // about its document and means nothing once the document is gone. No
        // navigation is needed: the key targets the documents' alternate key.
        // ux_knowledge_sources_document already leads with the document, so the
        // key needs no index of its own.
        builder.HasOne<KnowledgeDocument>()
            .WithMany()
            .HasForeignKey(source => new { source.KnowledgeDocumentId, source.TenantId })
            .HasPrincipalKey(document => new { document.Id, document.TenantId })
            .HasConstraintName("fk_knowledge_sources_document")
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired()
            .WithoutConventionIndex();

        builder.Property<uint>(PersistenceSchema.XminConcurrencyToken)
            .HasColumnName(PersistenceSchema.XminConcurrencyToken)
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();
    }
}
