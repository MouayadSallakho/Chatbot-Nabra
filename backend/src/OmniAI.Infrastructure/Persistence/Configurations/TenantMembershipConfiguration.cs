using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OmniAI.Domain.Tenants;

namespace OmniAI.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the table that decides who may reach which workspace.
/// </summary>
internal sealed class TenantMembershipConfiguration : IEntityTypeConfiguration<TenantMembership>
{
    public void Configure(EntityTypeBuilder<TenantMembership> builder)
    {
        builder.ToTable("tenant_memberships");

        // The pair is the key. That is a modelling statement before it is a
        // storage one: a person either belongs to a tenant or does not, so a
        // second row for the same pair would be meaningless -- and with a
        // surrogate key it would also be possible. This makes
        // UNIQUE(user_id, tenant_id) structural rather than an extra constraint
        // that a future migration could drop without anything obviously breaking.
        // Left to EF's default name, PK_tenant_memberships, because that is the
        // convention every other table in this schema already follows.
        builder.HasKey(membership => new { membership.UserId, membership.TenantId });

        builder.Property(membership => membership.UserId)
            .HasColumnName("user_id")
            .HasConversion(PersistenceConverters.UserId)
            .ValueGeneratedNever();

        builder.Property(membership => membership.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(PersistenceConverters.TenantId)
            .ValueGeneratedNever();

        builder.Property(membership => membership.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(membership => membership.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        // The row version behind a member's ETag, so two administrators editing
        // one person's access cannot silently overwrite each other.
        //
        // xmin is PostgreSQL's own transaction stamp on every row: it is not a
        // column this platform declares, stores or migrates, and it advances
        // whenever the row is written. Mapped as a shadow property so nothing in
        // the Domain knows it exists, exactly as roles and agents already do.
        builder.Property<uint>(PersistenceSchema.XminConcurrencyToken)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        // The tenant key, modelled the same way ai_agents models its own:
        // Restrict, so business data is never removed as a side effect of
        // removing something it points at.
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(membership => membership.TenantId)
            .HasConstraintName("fk_tenant_memberships_tenant")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        // The user key. EF cannot relate this UserId to IdentityUser<Guid>.Id
        // directly -- the CLR types differ, and neither side should change: giving
        // Identity a strongly-typed key breaks its stores, and weakening this to a
        // bare Guid would remove the type that makes a user id unassignable from a
        // tenant id. So it targets UserReferenceRow, the same users table typed
        // the Domain's way and excluded from migrations. The primary key already
        // leads with user_id, so the convention adds no index.
        builder.HasOne<UserReferenceRow>()
            .WithMany()
            .HasForeignKey(membership => membership.UserId)
            .HasConstraintName("fk_tenant_memberships_user")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        // Restrict rather than Cascade, and that is the part worth pausing on. A
        // cascade would mean deleting an account silently removed every one of
        // that person's memberships, and since they are today the only member of
        // the workspaces they created, those workspaces and all their agents
        // would become permanently unreachable while still occupying the
        // database. What should happen to a workspace when its last member leaves
        // is a product decision nobody has taken, so the constraint refuses the
        // deletion and forces that decision to be made deliberately. No endpoint
        // deletes an account, so nothing meets this refusal by accident.

        // "Which workspaces does this person have" and "may this person touch
        // this tenant" are both served by the primary key: user_id leads it, so
        // the first is a range scan on the key and the second is a single probe.
        // No second index is added for either -- a covering index that duplicates
        // the key would cost writes and buy nothing.
        //
        // This one exists for the other direction. PostgreSQL does not index a
        // foreign key automatically, so without it every check of the tenant
        // reference -- and any future "who belongs to this workspace" -- would
        // scan the table.
        builder.HasIndex(membership => membership.TenantId)
            .HasDatabaseName("ix_tenant_memberships_tenant");

        // No xmin concurrency token, unlike every mutable aggregate. Nothing
        // updates a membership: it is inserted when a workspace is created and
        // would be deleted when access is revoked. A row that is never modified
        // has no lost update to prevent, and adding a token would imply an edit
        // operation that does not exist.
    }
}
