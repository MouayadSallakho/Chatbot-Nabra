using OmniAI.Application.Authorization.Persistence;
using OmniAI.Application.Concurrency;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Authorization.GetTenantRoleById;

/// <summary>
/// Reads one role and the permissions it grants.
/// </summary>
public sealed class GetTenantRoleByIdHandler(
    ITenantRoleRepository roles,
    TenantAuthorizer authorizer)
{
    private readonly ITenantRoleRepository _roles =
        roles ?? throw new ArgumentNullException(nameof(roles));

    private readonly TenantAuthorizer _authorizer =
        authorizer ?? throw new ArgumentNullException(nameof(authorizer));

    /// <summary>Finds the role.</summary>
    /// <returns>
    /// The role's read model and the version it was read at, or
    /// <see langword="null"/> when this workspace has no role with that id, or
    /// when the caller does not belong to the workspace at all. The two are
    /// deliberately indistinguishable.
    /// </returns>
    /// <remarks>
    /// <strong>Authorisation runs before the role is read, and that is the
    /// disclosure rule.</strong> A member without <c>roles.view</c> is refused
    /// identically whether the role id they named is real, belongs to another
    /// workspace, or was invented -- because nothing is looked up before the
    /// refusal. Checking the permission after the lookup would answer <c>403</c>
    /// for a role that exists and <c>404</c> for one that does not, which turns a
    /// permission they lack into a way to enumerate the ids they cannot see.
    /// </remarks>
    /// <exception cref="Security.NotAuthenticatedException">Nobody is signed in.</exception>
    /// <exception cref="PermissionDeniedException">
    /// The caller is a member without <c>roles.view</c>.
    /// </exception>
    /// <exception cref="Domain.DomainValidationException">An identifier is empty.</exception>
    public async Task<Versioned<TenantRoleDetailView>?> HandleAsync(
        GetTenantRoleByIdQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        TenantId tenantId = TenantId.From(query.TenantId);
        TenantRoleId roleId = TenantRoleId.From(query.TenantRoleId);

        if (!await _authorizer
            .RequireAsync(tenantId, PlatformPermissions.RolesView, cancellationToken)
            .ConfigureAwait(false))
        {
            return null;
        }

        Versioned<TenantRole>? stored = await _roles
            .FindAsync(tenantId, roleId, cancellationToken)
            .ConfigureAwait(false);

        if (stored is null)
        {
            return null;
        }

        // Phase 37.5b: one indexed count, after the role was found in this
        // workspace, so a missing role costs nothing more.
        int members = await _roles
            .CountMembersAsync(tenantId, roleId, cancellationToken)
            .ConfigureAwait(false);

        return new Versioned<TenantRoleDetailView>(
            new TenantRoleDetailView(TenantRoleView.From(stored.Value), members),
            stored.Version);
    }
}
