using OmniAI.Application.Authorization;
using OmniAI.Application.Authorization.Persistence;
using OmniAI.Application.Tenants.Persistence;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Application.Reports;

/// <summary>
/// What a named member may do, asked on behalf of a background worker.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Only ever an internal step.</strong> <see cref="TenantAuthorizer"/>
/// deliberately answers questions about the caller alone. A worker generating a
/// report has no caller: it has the member who requested it, and must ask whether
/// that member still holds what the report needs. This asks exactly that, from the
/// same stores and with the same union rule, and no endpoint exposes it.
/// </para>
/// <para>
/// <strong>Read fresh every time.</strong> Nothing is cached, so a role removed a
/// second before an occurrence is honoured by that occurrence.
/// </para>
/// </remarks>
public sealed class ReportAuthority(
    ITenantMembershipRepository memberships,
    ITenantRoleRepository roles)
{
    private readonly ITenantMembershipRepository _memberships =
        memberships ?? throw new ArgumentNullException(nameof(memberships));

    private readonly ITenantRoleRepository _roles =
        roles ?? throw new ArgumentNullException(nameof(roles));

    /// <returns>The member's permissions, or <see langword="null"/> if they are no longer a member.</returns>
    public async Task<IReadOnlyList<PermissionCode>?> PermissionsOfAsync(
        TenantId tenantId,
        UserId userId,
        CancellationToken cancellationToken = default)
    {
        if (!await _memberships.ExistsAsync(userId, tenantId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        IReadOnlyList<TenantRole> assigned = await _roles
            .ListAssignedRolesAsync(userId, tenantId, cancellationToken)
            .ConfigureAwait(false);

        return EffectivePermissions.Union(assigned);
    }

    /// <summary>Whether the member is still a member and holds every one of these.</summary>
    public async Task<bool> StillHoldsAsync(
        TenantId tenantId,
        UserId userId,
        IReadOnlyList<PermissionCode> required,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PermissionCode>? held = await PermissionsOfAsync(tenantId, userId, cancellationToken)
            .ConfigureAwait(false);

        return held is not null && ReportCatalog.Holds(held, required);
    }
}
