using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OmniAI.Domain.Invitations;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Infrastructure.Persistence.Configurations;

/// <summary>
/// How an invitation is stored.
/// </summary>
/// <remarks>
/// <strong>The three constraints that carry the security of this table.</strong>
/// The token digest is unique, so one secret can never unlock two invitations.
/// The digest column is the only representation of the secret anywhere -- there
/// is no raw token column, and no property that could become one. And at most one
/// invitation per workspace and address may be open at a time, enforced by a
/// partial unique index rather than by a read-then-write that two concurrent
/// requests would both pass.
/// <para>
/// <strong>The open-invitation index is predicated on the stored status, not on
/// the clock.</strong> PostgreSQL will not build an index over a volatile
/// expression, and a rule that changed as time passed would not be a constraint
/// at all. So expiry is stamped into the status when a replacement is issued, and
/// the index reads a stable column. Whether an invitation may actually be used is
/// still decided by <c>ExpiresAt</c> at the moment somebody tries.
/// </para>
/// </remarks>
internal sealed class TenantInvitationConfiguration : IEntityTypeConfiguration<TenantInvitation>
{
    public void Configure(EntityTypeBuilder<TenantInvitation> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("tenant_invitations");

        builder.HasKey(invitation => invitation.Id);

        builder.Property(invitation => invitation.Id)
            .HasColumnName("id")
            .HasConversion(PersistenceConverters.TenantInvitationId)
            .ValueGeneratedNever();

        builder.Property(invitation => invitation.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(PersistenceConverters.TenantId)
            .IsRequired();

        // The address as written, for display, and the canonical form the
        // uniqueness rule and the acceptance check both compare on. Two columns
        // because they answer two questions: one is shown to a person, the other
        // decides who somebody is. Deriving the second in SQL would tie this
        // platform's identity comparison to a database collation.
        builder.Property(invitation => invitation.Email)
            .HasColumnName("email")
            .HasConversion(PersistenceConverters.EmailAddress)
            .HasMaxLength(EmailAddress.MaxLength)
            .IsRequired();

        builder.Property<string>(PersistenceSchema.CanonicalEmail)
            .HasColumnName("canonical_email")
            .HasMaxLength(EmailAddress.MaxLength)
            .IsRequired();

        builder.Property(invitation => invitation.InvitedByUserId)
            .HasColumnName("invited_by_user_id")
            .HasConversion(PersistenceConverters.UserId)
            .IsRequired();

        // bytea, not text. A digest is binary; storing it hex-encoded would make
        // the column longer, invite somebody to render it, and let a mistaken
        // comparison succeed on a case difference.
        builder.Property(invitation => invitation.TokenHash)
            .HasColumnName("token_hash")
            .HasConversion(PersistenceConverters.InvitationTokenHash)
            .HasColumnType("bytea")
            .IsRequired();

        builder.Property(invitation => invitation.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(invitation => invitation.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(invitation => invitation.ExpiresAt)
            .HasColumnName("expires_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(invitation => invitation.AcceptedAt)
            .HasColumnName("accepted_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(invitation => invitation.AcceptedByUserId)
            .HasColumnName("accepted_by_user_id")
            .HasConversion(PersistenceConverters.NullableUserId);

        builder.Property(invitation => invitation.RevokedAt)
            .HasColumnName("revoked_at")
            .HasColumnType("timestamp with time zone");

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(invitation => invitation.TenantId)
            .HasConstraintName("fk_tenant_invitations_tenant")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        // One secret, one invitation. Without this a digest collision -- or a
        // defect that reused a token -- would make a single link ambiguous.
        builder.HasIndex(invitation => invitation.TokenHash)
            .HasDatabaseName("ux_tenant_invitations_token_hash")
            .IsUnique();

        // At most one open invitation per workspace and address. Predicated on the
        // stored status so the index is over immutable input.
        builder.HasIndex(nameof(TenantInvitation.TenantId), PersistenceSchema.CanonicalEmail)
            .HasDatabaseName("ux_tenant_invitations_open")
            .IsUnique()
            .HasFilter("status = 'Pending'");

        builder.HasIndex(invitation => invitation.TenantId)
            .HasDatabaseName("ix_tenant_invitations_tenant");

        // The composite key an invitation's roles point at, so a role row cannot
        // name an invitation in one workspace and a role in another.
        builder.HasAlternateKey(invitation => new { invitation.Id, invitation.TenantId })
            .HasName("ak_tenant_invitations_id_tenant");

        // The relationship itself is declared on the grant, where the composite
        // key that makes cross-tenant pairs unrepresentable lives. Declaring it
        // here as well would produce a second, weaker foreign key beside it.
        builder.Navigation(invitation => invitation.Roles)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
