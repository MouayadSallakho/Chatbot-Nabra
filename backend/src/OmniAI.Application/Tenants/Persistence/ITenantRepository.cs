using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Tenants.Persistence;

/// <summary>
/// The only way the tenant use cases reach storage.
/// </summary>
/// <remarks>
/// Three methods, because the two authorised use cases need three things and
/// nothing more. It is deliberately <em>not</em> an <c>IRepository&lt;T&gt;</c>: a
/// generic repository would have to expose either <c>IQueryable</c> or a
/// specification language to stay useful, and both hand the caller the storage
/// model back under a different name. Nothing here mentions <c>IQueryable</c>,
/// <c>DbSet</c>, <c>DbContext</c>, EF Core or PostgreSQL, and the Application
/// project references none of them.
/// <para>
/// <strong>There is no separate unit of work</strong>, and that was a decision
/// rather than an omission. A unit of work earns its cost when one use case must
/// commit several aggregates together, which is precisely when the commit point
/// has to move out of the repository. Both use cases here touch exactly one
/// aggregate, and <c>SaveChanges</c> is already atomic, so an
/// <c>IUnitOfWork</c> today would be a second abstraction that does nothing but
/// forward a call -- and would still have to be reworked when real multi-aggregate
/// transactions arrive, because their boundaries are not known yet.
/// <see cref="AddAsync"/> therefore commits, and says so.
/// </para>
/// </remarks>
public interface ITenantRepository
{
    /// <summary>Finds a tenant by identity.</summary>
    /// <returns>The tenant, or <see langword="null"/> if no tenant has that id.</returns>
    /// <remarks>
    /// Absence is returned, not thrown. "No such tenant" is an ordinary answer to
    /// a lookup, and making it an exception would turn a normal outcome into a
    /// stack unwind and an error-level log.
    /// </remarks>
    Task<Tenant?> FindByIdAsync(TenantId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether any tenant already uses this slug.
    /// </summary>
    /// <remarks>
    /// <strong>Advisory only.</strong> The answer is already stale by the time it
    /// is returned, so it exists to give a caller a clear failure before doing
    /// work -- never as the authority on uniqueness. The unique index enforced by
    /// <see cref="AddAsync"/> is the authority. Treating this check as
    /// authoritative is exactly the bug that lets two concurrent requests both
    /// pass it.
    /// </remarks>
    Task<bool> SlugExistsAsync(TenantSlug slug, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a new tenant and commits.
    /// </summary>
    /// <exception cref="TenantSlugAlreadyExistsException">
    /// The slug is already taken. Raised from the database's unique constraint,
    /// which is what makes this outcome race-safe: two concurrent callers that
    /// both passed <see cref="SlugExistsAsync"/> still produce exactly one
    /// stored tenant and exactly one conflict.
    /// </exception>
    Task AddAsync(Tenant tenant, CancellationToken cancellationToken = default);
}
