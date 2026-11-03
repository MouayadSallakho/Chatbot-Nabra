using OmniAI.Domain.Authorization;
using OmniAI.Domain.Tenants;

namespace OmniAI.Domain.Invitations;

/// <summary>
/// One role an invitation intends its acceptor to receive.
/// </summary>
/// <remarks>
/// <strong>A row, not a serialised list.</strong> The roles an invitation carries
/// are stored relationally so the database can hold them to the same rules
/// membership assignments obey: the role must exist, it must belong to this
/// workspace, and it cannot appear twice. A JSON column would accept an id from
/// another tenant, a deleted role, or the same role five times, and would only
/// find out when somebody accepted.
/// <para>
/// The pair <c>(InvitationId, TenantRoleId)</c> is the identity of this row and
/// its primary key, which is what makes a duplicate unrepresentable rather than
/// merely refused.
/// </para>
/// <para>
/// <strong>It carries the tenant, which looks redundant and is not.</strong> Both
/// the invitation and the role already know which workspace they belong to, so a
/// third copy appears to be duplication -- but it is what lets the database hold
/// the two to the <em>same</em> answer. Two composite foreign keys read this one
/// column: one into the invitation, one into the role. A row pairing an
/// invitation in one workspace with a role from another cannot be written, rather
/// than being written and then caught by whichever code path remembered to look.
/// This is the arrangement membership role assignments already use.
/// </para>
/// </remarks>
public sealed class InvitationRoleGrant
{
    private InvitationRoleGrant(
        TenantInvitationId invitationId,
        TenantId tenantId,
        TenantRoleId roleId)
    {
        InvitationId = invitationId;
        TenantId = tenantId;
        RoleId = roleId;
    }

    /// <summary>The invitation that carries the role.</summary>
    public TenantInvitationId InvitationId { get; }

    /// <summary>The workspace both the invitation and the role belong to.</summary>
    public TenantId TenantId { get; }

    /// <summary>The role it intends to grant.</summary>
    public TenantRoleId RoleId { get; }

    /// <summary>Links a role to an invitation.</summary>
    /// <remarks>
    /// Internal, because an invitation composes its own grants. A caller who
    /// could build one independently could build a set that disagrees with the
    /// invitation holding it.
    /// </remarks>
    internal static InvitationRoleGrant For(
        TenantInvitationId invitationId,
        TenantId tenantId,
        TenantRoleId roleId)
    {
        if (invitationId.IsEmpty)
        {
            throw new DomainValidationException("An invitation role must name an invitation.")
            {
                Code = "invitations.role_grant.invalid",
            };
        }

        if (tenantId.IsEmpty)
        {
            throw new DomainValidationException("An invitation role must name a tenant.")
            {
                Code = "invitations.role_grant.invalid",
            };
        }

        if (roleId.IsEmpty)
        {
            throw new DomainValidationException("An invitation role must name a role.")
            {
                Code = "invitations.role_grant.invalid",
            };
        }

        return new InvitationRoleGrant(invitationId, tenantId, roleId);
    }
}
