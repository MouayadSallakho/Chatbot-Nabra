using OmniAI.Application.Tenants;
using OmniAI.Application.Tenants.Persistence;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Tests.Tenants;

/// <summary>
/// A hand-written stand-in for the tenant repository.
/// </summary>
/// <remarks>
/// Written out rather than produced by a mocking framework. The contract has
/// three methods, this fake is shorter than the setup code a framework would
/// need, and it can express the one thing that actually matters here -- that the
/// slug conflict is raised by the store rather than decided by the handler --
/// without any configuration DSL. No mocking package is added to the solution.
/// <para>
/// The Domain is never faked. <c>Tenant</c>, <c>TenantName</c> and
/// <c>TenantSlug</c> are the real types, so these tests exercise the real
/// invariants.
/// </para>
/// </remarks>
internal sealed class InMemoryTenantRepository : ITenantRepository
{
    private readonly List<Tenant> _stored = [];

    /// <summary>Tenants currently stored, in insertion order.</summary>
    public IReadOnlyList<Tenant> Stored => _stored;

    /// <summary>How many times <see cref="AddAsync"/> was called.</summary>
    public int AddAttempts { get; private set; }

    /// <summary>
    /// When true, <see cref="SlugExistsAsync"/> always reports the slug as free.
    /// </summary>
    /// <remarks>
    /// Lets a test drive the case the advisory check cannot catch: two callers
    /// both pass it and the store has to be the one that rejects the loser. That
    /// is the behaviour the real unique index provides, and it is the reason the
    /// check is documented as advisory.
    /// </remarks>
    public bool PretendSlugIsFree { get; init; }

    public Task<Tenant?> FindByIdAsync(TenantId id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_stored.Find(tenant => tenant.Id == id));

    public Task<bool> SlugExistsAsync(TenantSlug slug, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(slug);

        return Task.FromResult(!PretendSlugIsFree && ContainsSlug(slug));
    }

    public Task AddAsync(Tenant tenant, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        AddAttempts++;

        // Stands in for the unique index: the store, not the caller, is the
        // authority on whether a slug is taken.
        if (ContainsSlug(tenant.Slug))
        {
            throw TenantSlugAlreadyExistsException.ForSlug(tenant.Slug.Value);
        }

        _stored.Add(tenant);

        return Task.CompletedTask;
    }

    /// <summary>Seeds a tenant without going through the use case.</summary>
    public InMemoryTenantRepository Seed(Tenant tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        _stored.Add(tenant);

        return this;
    }

    private bool ContainsSlug(TenantSlug slug) =>
        _stored.Exists(stored => stored.Slug == slug);
}
