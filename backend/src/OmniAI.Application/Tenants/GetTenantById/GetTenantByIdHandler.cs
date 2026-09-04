using OmniAI.Application.Tenants.Persistence;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Tenants.GetTenantById;

/// <summary>
/// Reads one tenant.
/// </summary>
public sealed class GetTenantByIdHandler(ITenantRepository tenants)
{
    private readonly ITenantRepository _tenants =
        tenants ?? throw new ArgumentNullException(nameof(tenants));

    /// <summary>Finds the tenant.</summary>
    /// <returns>
    /// The tenant's read model, or <see langword="null"/> when no tenant has that
    /// identity. Absence is a result, not a failure: it needs no exception type,
    /// produces no stack unwind and no error-level log, and the API maps it to
    /// its own not-found response.
    /// </returns>
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

        Tenant? tenant = await _tenants.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);

        return tenant is null ? null : TenantView.From(tenant);
    }
}
