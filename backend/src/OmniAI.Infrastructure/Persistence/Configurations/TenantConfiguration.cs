using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OmniAI.Domain.Tenants;

namespace OmniAI.Infrastructure.Persistence.Configurations;

internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants", t => t.HasCheckConstraint("ck_tenants_first_reply_target", "first_reply_target_minutes BETWEEN 1 AND 1440"));
        builder.Property<int>("FirstReplyTargetMinutes").HasColumnName("first_reply_target_minutes").HasDefaultValue(15);

        builder.HasKey(tenant => tenant.Id);

        builder.Property(tenant => tenant.Id)
            .HasColumnName("id")
            .HasConversion(PersistenceConverters.TenantId)
            .ValueGeneratedNever();

        // Column lengths mirror the Domain limits. The application already
        // rejects longer values; the column makes the rule true of the data even
        // if something ever writes to this table without going through the model.
        builder.Property(tenant => tenant.Name)
            .HasColumnName("name")
            .HasConversion(PersistenceConverters.TenantName)
            .HasMaxLength(TenantName.MaxLength)
            .IsRequired();

        builder.Property(tenant => tenant.Slug)
            .HasColumnName("slug")
            .HasConversion(PersistenceConverters.TenantSlug)
            .HasMaxLength(TenantSlug.MaxLength)
            .IsRequired();

        builder.Property(tenant => tenant.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        // Phase 37.6 (M7). A model default, so every existing workspace reads UTC —
        // exactly the zone it was already counted in — without a data step.
        builder.Property(tenant => tenant.TimeZone)
            .HasColumnName("time_zone")
            .HasConversion(PersistenceConverters.WorkspaceTimeZone)
            .HasMaxLength(WorkspaceTimeZone.MaxLength)
            .HasDefaultValue(WorkspaceTimeZone.Utc)
            .IsRequired();

        // The uniqueness the Domain deliberately could not guarantee. Slugs are
        // already canonical lowercase before they reach persistence, so a plain
        // unique index is exact -- no citext extension or lower() expression
        // index is needed to solve a problem the Domain has already removed.
        builder.HasIndex(tenant => tenant.Slug)
            .IsUnique()
            .HasDatabaseName("ix_tenants_slug");

        builder.Property<uint>(PersistenceSchema.XminConcurrencyToken)
            .HasColumnName(PersistenceSchema.XminConcurrencyToken)
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();
    }
}
