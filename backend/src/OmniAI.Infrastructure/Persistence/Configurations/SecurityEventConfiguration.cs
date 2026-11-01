using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OmniAI.Domain.Security;
using OmniAI.Domain.Tenants;

namespace OmniAI.Infrastructure.Persistence.Configurations;

/// <summary>
/// The audit table (Phase 37.3, M3). No address, token, password or free text.
/// </summary>
internal sealed class SecurityEventConfiguration : IEntityTypeConfiguration<SecurityEvent>
{
    public void Configure(EntityTypeBuilder<SecurityEvent> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(PersistenceSchema.SecurityEventsTable);

        builder.HasKey(security => security.Id);

        builder.Property(security => security.Id)
            .HasColumnName("id")
            .HasConversion(PersistenceConverters.SecurityEventId)
            .ValueGeneratedNever();

        builder.Property(security => security.OccurredAt)
            .HasColumnName("occurred_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(security => security.Kind)
            .HasColumnName("kind")
            .HasMaxLength(SecurityEvent.MaxKindLength)
            .IsRequired();

        builder.Property(security => security.UserId)
            .HasColumnName("user_id")
            .HasConversion(PersistenceConverters.NullableUserId);

        builder.Property(security => security.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(PersistenceConverters.NullableTenantId);

        builder.Property(security => security.SubjectKey)
            .HasColumnName("subject_key")
            .HasMaxLength(SecurityEvent.MaxKeyLength);

        builder.Property(security => security.ClientNetworkKey)
            .HasColumnName("client_network_key")
            .HasMaxLength(SecurityEvent.MaxKeyLength);

        // The record outlives the account: deleting it clears the reference. The
        // partial index below leads with user_id, so it also serves this key.
        builder.HasOne<UserReferenceRow>()
            .WithMany()
            .HasForeignKey(security => security.UserId)
            .HasConstraintName("fk_security_events_user")
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        // And the workspace, the same way (M3 as amended 2026-10-21). It keeps
        // EF's convention index, ix_security_events_tenant_id.
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(security => security.TenantId)
            .HasConstraintName("fk_security_events_tenant")
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        // One account's history, newest first.
        builder.HasIndex(security => new { security.UserId, security.OccurredAt })
            .HasDatabaseName("ix_security_events_user_occurred")
            .HasFilter("user_id IS NOT NULL");

        // Everything in a period, and the retention sweep a later phase may add.
        builder.HasIndex(security => security.OccurredAt)
            .HasDatabaseName("ix_security_events_occurred");
    }
}
