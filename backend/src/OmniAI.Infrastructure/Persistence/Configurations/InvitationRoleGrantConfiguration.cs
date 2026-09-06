using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Invitations;

namespace OmniAI.Infrastructure.Persistence.Configurations;

/// <summary>
/// How the roles an invitation intends to grant are stored.
/// </summary>
/// <remarks>
/// <strong>Two composite foreign keys reading the same tenant column.</strong>
/// One reaches the invitation by <c>(invitation_id, tenant_id)</c>, the other the
/// role by <c>(role_id, tenant_id)</c>. Because both consult the single
/// <c>tenant_id</c> on this row, a grant pairing an invitation in one workspace
/// with a role from another is not refused by a check -- it cannot be
/// represented. The same arrangement protects membership role assignments, and it
/// is the reason a transposed identifier is a write that fails rather than a
/// privilege that leaks.
/// <para>
/// The primary key is the pair, so the same role cannot be attached to an
/// invitation twice however many times a request names it.
/// </para>
/// <para>
/// Deleting a role is not implemented anywhere in this platform, so the reference
/// to it restricts rather than cascades: if deletion is added later, whoever adds
/// it has to decide what happens to invitations naming that role, and this makes
/// them decide rather than silently emptying invitations.
/// </para>
/// </remarks>
internal sealed class InvitationRoleGrantConfiguration
    : IEntityTypeConfiguration<InvitationRoleGrant>
{
    public void Configure(EntityTypeBuilder<InvitationRoleGrant> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("invitation_roles");

        builder.HasKey(role => new { role.InvitationId, role.RoleId });

        builder.Property(role => role.InvitationId)
            .HasColumnName("invitation_id")
            .HasConversion(PersistenceConverters.TenantInvitationId)
            .ValueGeneratedNever();

        builder.Property(role => role.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(PersistenceConverters.TenantId)
            .ValueGeneratedNever();

        builder.Property(role => role.RoleId)
            .HasColumnName("tenant_role_id")
            .HasConversion(PersistenceConverters.TenantRoleId)
            .ValueGeneratedNever();

        builder.HasOne<TenantInvitation>()
            .WithMany(invitation => invitation.Roles)
            .HasForeignKey(role => new { role.InvitationId, role.TenantId })
            .HasPrincipalKey(invitation => new { invitation.Id, invitation.TenantId })
            .HasConstraintName("fk_invitation_roles_invitation_tenant")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<TenantRole>()
            .WithMany()
            .HasForeignKey(role => new { role.RoleId, role.TenantId })
            .HasPrincipalKey(role => new { role.Id, role.TenantId })
            .HasConstraintName("fk_invitation_roles_role")
            .OnDelete(DeleteBehavior.Restrict);

        // The role side of the composite reference, so removing a role can find
        // the invitations that name it without scanning.
        builder.HasIndex(role => new { role.RoleId, role.TenantId })
            .HasDatabaseName("ix_invitation_roles_role");
    }
}
