using OmniAI.Application.Tenants;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Authorization.GetPermissionCatalog;

/// <summary>
/// Publishes the capabilities a role in this workspace may be given.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The list is a constant; the authorisation is not.</strong> Every
/// workspace sees the same catalogue, because it describes the product rather
/// than the tenant. What varies is who may see it: the role editor needs it, and
/// nothing else does, so it is published to callers with <c>roles.view</c> and
/// nobody else. An unauthenticated or unauthorised caller learns nothing about
/// what this platform can do.
/// </para>
/// <para>
/// <strong>There is no way to add to it.</strong> This handler reads; there is no
/// create, no update and no delete, and no table behind it. Permissions are
/// platform capabilities, and a tenant that could invent one would be inventing
/// authority over something that does not exist.
/// </para>
/// <para>
/// It takes no repository, and needs none. That is worth stating rather than
/// leaving to be noticed: the catalogue is compiled in, so this use case exists
/// only to place the authorisation check in front of it.
/// </para>
/// </remarks>
public sealed class GetPermissionCatalogHandler(TenantAuthorizer authorizer)
{
    private readonly TenantAuthorizer _authorizer =
        authorizer ?? throw new ArgumentNullException(nameof(authorizer));

    /// <summary>Reads the catalogue.</summary>
    /// <returns>
    /// Every capability this platform recognises, with the label, description and
    /// group a person needs to decide whether to grant it -- or
    /// <see langword="null"/> when the caller does not belong to this workspace.
    /// </returns>
    /// <exception cref="Security.NotAuthenticatedException">Nobody is signed in.</exception>
    /// <exception cref="PermissionDeniedException">
    /// The caller is a member without <c>roles.view</c>.
    /// </exception>
    /// <exception cref="Domain.DomainValidationException">The tenant id is empty.</exception>
    public async Task<IReadOnlyList<PermissionDefinition>?> HandleAsync(
        GetPermissionCatalogQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        TenantId tenantId = TenantId.From(query.TenantId);

        return await _authorizer
            .RequireAsync(tenantId, PlatformPermissions.RolesView, cancellationToken)
            .ConfigureAwait(false)
            ? PlatformPermissions.All
            : null;
    }
}
