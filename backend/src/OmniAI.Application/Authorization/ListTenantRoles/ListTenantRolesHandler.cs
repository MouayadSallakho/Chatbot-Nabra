using OmniAI.Application.Authorization.Persistence;
using OmniAI.Application.Tenants;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Authorization.ListTenantRoles;

/// <summary>
/// Reads a workspace's roles.
/// </summary>
/// <remarks>
/// No paging, no filtering, no search and no sort parameter. A workspace has one
/// Owner and however many custom roles somebody wrote, which is a number a person
/// keeps in their head -- a query surface invented before its first real
/// requirement is a surface that has to be supported before anybody knows whether
/// it was right. If a workspace ever accumulates enough roles to need paging, the
/// AI agent listing already shows the shape that would take.
/// </remarks>
public sealed class ListTenantRolesHandler(
    ITenantRoleRepository roles,
    TenantAuthorizer authorizer)
{
    private readonly ITenantRoleRepository _roles =
        roles ?? throw new ArgumentNullException(nameof(roles));

    private readonly TenantAuthorizer _authorizer =
        authorizer ?? throw new ArgumentNullException(nameof(authorizer));

    /// <summary>Reads the roles.</summary>
    /// <returns>
    /// The workspace's roles, Owner first and then custom roles by name. The
    /// order is part of the contract, not an accident of the query plan: a list
    /// that reshuffled itself between page loads would make a reader hunt for the
    /// row they were just looking at.
    /// </returns>
    /// <remarks>
    /// Authorisation runs before anything is read, so a member without
    /// <c>roles.view</c> is refused without a single role being loaded, and a
    /// non-member is answered as though the workspace did not exist.
    /// </remarks>
    /// <exception cref="Security.NotAuthenticatedException">Nobody is signed in.</exception>
    /// <exception cref="TenantNotFoundException">
    /// The caller does not belong to this workspace -- whether it exists or not.
    /// </exception>
    /// <exception cref="PermissionDeniedException">
    /// The caller is a member without <c>roles.view</c>.
    /// </exception>
    /// <exception cref="Domain.DomainValidationException">The tenant id is empty.</exception>
    public async Task<IReadOnlyList<TenantRoleSummary>> HandleAsync(
        ListTenantRolesQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        TenantId tenantId = TenantId.From(query.TenantId);

        // A list is not a nullable thing, so a non-member is refused the only
        // other way a not-found can travel out of the Application -- the same
        // exception, and therefore the same 404, that the agent listing raises.
        if (!await _authorizer
            .RequireAsync(tenantId, PlatformPermissions.RolesView, cancellationToken)
            .ConfigureAwait(false))
        {
            throw TenantNotFoundException.ForId(tenantId.Value);
        }

        return await _roles.ListAsync(tenantId, cancellationToken).ConfigureAwait(false);
    }
}
