using OmniAI.Application.Authorization;
using OmniAI.Application.Tenants.Persistence;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Tenants.GetTenantById;

/// <summary>
/// Reads one tenant the caller belongs to.
/// </summary>
public sealed class GetTenantByIdHandler(ITenantRepository tenants, TenantAuthorizer authorizer)
{
    private readonly ITenantRepository _tenants =
        tenants ?? throw new ArgumentNullException(nameof(tenants));

    private readonly TenantAuthorizer _authorizer =
        authorizer ?? throw new ArgumentNullException(nameof(authorizer));

    /// <summary>Finds the tenant.</summary>
    /// <returns>
    /// The tenant's read model, or <see langword="null"/> when no tenant has that
    /// identity <em>or</em> the caller does not belong to it. Absence is a
    /// result, not a failure: it needs no exception type, produces no stack
    /// unwind and no error-level log, and the API maps it to its own not-found
    /// response.
    /// </returns>
    /// <remarks>
    /// <strong>Membership is checked before the tenant is read, and the two
    /// outcomes are deliberately identical.</strong> A caller who is not a member
    /// gets exactly what they would get for a tenant id that was never issued, so
    /// this endpoint cannot be used to discover which workspaces exist. Reading
    /// the tenant first and then filtering would produce the same status but
    /// would put somebody else's row into memory to do it, which is a needless
    /// place for a mistake to become a disclosure.
    /// </remarks>
    /// <exception cref="Security.NotAuthenticatedException">Nobody is signed in.</exception>
    /// <exception cref="Domain.DomainValidationException">
    /// The supplied identifier is empty. Rejected by <see cref="TenantId.From"/>
    /// rather than treated as a lookup that happens to find nothing, because an
    /// empty GUID is malformed input rather than a tenant that does not exist.
    /// </exception>
    public async Task<TenantView?> HandleAsync(
        GetTenantByIdQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        TenantId id = TenantId.From(query.TenantId);

        // Authorisation before the tenant is read, so a non-member never puts
        // somebody else's row into memory and a member without workspace.view is
        // refused without one either.
        if (!await _authorizer
            .RequireAsync(id, PlatformPermissions.WorkspaceView, cancellationToken)
            .ConfigureAwait(false))
        {
            return null;
        }

        Tenant? tenant = await _tenants.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);

        return tenant is null ? null : TenantView.From(tenant);
    }
}
