using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Tenants.Persistence;

/// <summary>
/// The only way the tenant use cases reach storage.
/// </summary>
/// <remarks>
/// Three methods, because the authorised use cases need three things and nothing
/// more. It is deliberately <em>not</em> an <c>IRepository&lt;T&gt;</c>: a
/// generic repository would have to expose either <c>IQueryable</c> or a
/// specification language to stay useful, and both hand the caller the storage
/// model back under a different name. Nothing here mentions <c>IQueryable</c>,
/// <c>DbSet</c>, <c>DbContext</c>, EF Core or PostgreSQL, and the Application
/// project references none of them.
/// <para>
/// <strong>There is still no separate unit of work</strong>, and that stayed a
/// decision rather than an omission when Phase 12 introduced the platform's first
/// genuine two-aggregate write. An <c>IUnitOfWork</c> earns its cost when
/// <em>many</em> use cases must compose commits that the repositories cannot see
/// from where they sit. Exactly one use case needs it here -- create a tenant and
/// the creator's membership together -- and the two rows it writes are the tenant
/// and the thing that makes the tenant reachable, which is a single indivisible
/// act rather than two operations somebody chose to combine. Introducing a
/// general commit-orchestration abstraction for it would have meant reworking
/// every accepted repository so that none of them committed, in order to serve one
/// call site. <see cref="AddAsync"/> therefore still commits, and now commits both
/// rows in one transaction.
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
    /// Stores a new tenant together with its first membership, atomically.
    /// </summary>
    /// <param name="tenant">The tenant to create.</param>
    /// <param name="membership">
    /// The membership that makes the new tenant reachable. It must name this
    /// tenant; an implementation that is handed a mismatched pair must refuse
    /// rather than write it.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="TenantSlugAlreadyExistsException">
    /// The slug is already taken. Raised from the database's unique constraint,
    /// which is what makes this outcome race-safe: two concurrent callers that
    /// both passed <see cref="SlugExistsAsync"/> still produce exactly one
    /// stored tenant and exactly one conflict. The loser leaves behind neither a
    /// tenant row nor a membership row.
    /// </exception>
    /// <remarks>
    /// <strong>The membership is a required argument, and that is the safety
    /// property.</strong> Phase 12 replaced a single-argument <c>AddAsync</c>
    /// with this one rather than adding a second method beside it, because a
    /// tenant nobody is a member of is unreachable: its creator could not open
    /// it, no endpoint would answer for it, and it would sit in the database as a
    /// permanent orphan. With the membership in the signature, a call site cannot
    /// forget it -- the mistake is a compile error rather than something review
    /// has to notice.
    /// <para>
    /// <strong>Both rows or neither.</strong> The implementation must commit them
    /// in one transaction. A tenant written without its membership is the orphan
    /// above; a membership written without its tenant cannot exist at all,
    /// because the foreign key refuses it.
    /// </para>
    /// </remarks>
    Task AddAsync(
        Tenant tenant,
        TenantMembership membership,
        CancellationToken cancellationToken = default);
}
