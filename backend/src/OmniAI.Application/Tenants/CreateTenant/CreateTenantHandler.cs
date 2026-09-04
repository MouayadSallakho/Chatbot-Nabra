using OmniAI.Application.Tenants.Persistence;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Tenants.CreateTenant;

/// <summary>
/// Onboards a new tenant.
/// </summary>
/// <remarks>
/// A plain class with a constructor dependency, resolved by name at the
/// composition root. There is no mediator, no command bus and no pipeline: with
/// two use cases, a dispatcher would add indirection, reflection and a
/// registration convention while removing nothing -- and it would hide the call
/// graph that is currently readable in one hop.
/// </remarks>
public sealed class CreateTenantHandler(ITenantRepository tenants)
{
    private readonly ITenantRepository _tenants =
        tenants ?? throw new ArgumentNullException(nameof(tenants));

    /// <summary>Creates the tenant and returns its read model.</summary>
    /// <exception cref="Domain.DomainValidationException">
    /// The name or slug breaks a Domain rule. Raised by the value objects, not
    /// re-implemented here.
    /// </exception>
    /// <exception cref="TenantSlugAlreadyExistsException">The slug is taken.</exception>
    public async Task<TenantView> HandleAsync(
        CreateTenantCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // The Domain validates and canonicalises. If either value is rejected,
        // this throws before anything touches the database.
        TenantName name = TenantName.Create(command.Name);
        TenantSlug slug = TenantSlug.Create(command.Slug);

        // An early, friendly failure for the overwhelmingly common case: the slug
        // was already taken well before this request arrived. This is a courtesy,
        // not a guard -- the answer can be stale the instant it is read, so the
        // authority is the unique index inside AddAsync. Deliberately no lock, no
        // serialisable transaction and no advisory lock: paying that cost on every
        // onboarding to tidy up a rare race would be the wrong trade, and the
        // database already resolves the race correctly.
        if (await _tenants.SlugExistsAsync(slug, cancellationToken).ConfigureAwait(false))
        {
            throw TenantSlugAlreadyExistsException.ForSlug(slug.Value);
        }

        // Identity and initial status come from the aggregate. The use case does
        // not get to choose either.
        Tenant tenant = Tenant.Create(name, slug);

        await _tenants.AddAsync(tenant, cancellationToken).ConfigureAwait(false);

        return TenantView.From(tenant);
    }
}
