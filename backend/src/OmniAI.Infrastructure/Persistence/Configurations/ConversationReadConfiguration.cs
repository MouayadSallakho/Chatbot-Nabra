using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;

namespace OmniAI.Infrastructure.Persistence.Configurations;

/// <summary>
/// Read markers (Phase 37.4, migration M5).
/// </summary>
/// <remarks>
/// Keyed by conversation and member: one marker per person per conversation. The
/// membership key cascades, unlike a reply's sender: a marker is private state of
/// a member and means nothing once they have left the workspace.
/// </remarks>
internal sealed class ConversationReadConfiguration : IEntityTypeConfiguration<ConversationReadRow>
{
    public void Configure(EntityTypeBuilder<ConversationReadRow> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            PersistenceSchema.ConversationReadsTable,
            table => table.HasCheckConstraint(
                "ck_conversation_reads_last_read_sequence",
                "last_read_sequence >= 0"));

        builder.HasKey(read => new { read.ConversationId, read.UserId })
            .HasName("pk_conversation_reads");

        builder.Property(read => read.ConversationId)
            .HasColumnName("conversation_id")
            .HasConversion(PersistenceConverters.ConversationId)
            .IsRequired();

        builder.Property(read => read.UserId)
            .HasColumnName("user_id")
            .HasConversion(PersistenceConverters.UserId)
            .IsRequired();

        builder.Property(read => read.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(PersistenceConverters.TenantId)
            .IsRequired();

        builder.Property(read => read.LastReadSequence)
            .HasColumnName("last_read_sequence")
            .IsRequired();

        builder.Property(read => read.ReadAt)
            .HasColumnName("read_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne<Conversation>()
            .WithMany()
            .HasForeignKey(read => read.ConversationId)
            .HasConstraintName("fk_conversation_reads_conversation")
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        // The reader must be a member of the workspace the marker is in, and the
        // marker goes when the membership does.
        builder.HasOne<TenantMembership>()
            .WithMany()
            .HasForeignKey(read => new { read.UserId, read.TenantId })
            .HasPrincipalKey(membership => new { membership.UserId, membership.TenantId })
            .HasConstraintName("fk_conversation_reads_member")
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired()
            .WithoutConventionIndex();

        // Serves the membership key's cascade.
        builder.HasIndex(read => new { read.UserId, read.TenantId })
            .HasDatabaseName("ix_conversation_reads_member");
    }
}
