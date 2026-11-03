using OmniAI.Application.Concurrency;
using OmniAI.Domain.Security;
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
/// <strong>There is still no separate unit of work</strong>, and Phase 13 was the
/// point at which that was re-examined rather than assumed. Provisioning now
/// writes four rows instead of two -- the tenant, the creator's membership, the
/// Owner role and the assignment -- which is the shape that usually argues for an
/// <c>IUnitOfWork</c>. It does not here, because the count is not what matters:
/// an <c>IUnitOfWork</c> earns its cost when <em>many</em> use cases must compose
/// commits that the repositories cannot see from where they sit, and there is
/// still exactly one. The four rows are a workspace and the three things that
/// make a workspace usable, which is one indivisible act rather than four
/// operations somebody chose to combine. Introducing a general
/// commit-orchestration abstraction for it would still mean reworking every
/// accepted repository so that none of them committed, in order to serve one call
/// site. <see cref="AddAsync"/> therefore still commits, and commits all four
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
    /// Stores a new workspace and everything that makes it usable, atomically.
    /// </summary>
    /// <param name="workspace">
    /// The tenant, its creator's membership, its protected Owner role and the
    /// assignment that gives the creator that role. Built together by
    /// <see cref="NewWorkspace.Create"/>, which is the only way to obtain one, so
    /// the four cannot disagree about who or which tenant they belong to.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="TenantSlugAlreadyExistsException">
    /// The slug is already taken. Raised from the database's unique constraint,
    /// which is what makes this outcome race-safe: two concurrent callers that
    /// both passed <see cref="SlugExistsAsync"/> still produce exactly one
    /// stored tenant and exactly one conflict. The loser leaves behind none of
    /// the four rows.
    /// </exception>
    /// <remarks>
    /// <strong>One argument describing a whole workspace, and that is the safety
    /// property.</strong> Phase 12 made the membership a required argument
    /// because a tenant nobody belongs to is unreachable. Phase 13 added two more
    /// rows with the same character: a workspace with no Owner role has no full
    /// authority in it and no way to acquire one, since no caller can mint a
    /// system role; a creator with no assignment is a member who may do nothing,
    /// in a workspace where nobody can grant them anything. None of those states
    /// is recoverable through any endpoint this platform publishes.
    /// <para>
    /// Four loose parameters would have compiled just as happily with a
    /// membership for one person and an assignment for another. Taking
    /// <see cref="NewWorkspace"/> instead means a mismatched set is not refused
    /// at run time -- it cannot be constructed.
    /// </para>
    /// <para>
    /// <strong>All four rows or none.</strong> The implementation must commit them
    /// in one transaction. A partial workspace is not a state any caller asked
    /// for, and none of the halves can be repaired afterwards.
    /// </para>
    /// </remarks>
    Task AddAsync(
        NewWorkspace workspace,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a tenant with the version a conditional write must name
    /// (Phase 37.5a).
    /// </summary>
    /// <returns>The tenant and its version, or <see langword="null"/>.</returns>
    Task<Versioned<Tenant>?> FindVersionedAsync(TenantId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a renamed tenant if it is still at <paramref name="expected"/>, and
    /// its audit record in the same transaction (Phase 37.5a, row 20a).
    /// </summary>
    /// <returns>The tenant's new version.</returns>
    /// <exception cref="TenantVersionMismatchException">
    /// The tenant changed since <paramref name="expected"/> was read. Neither the
    /// name nor the audit record was written.
    /// </exception>
    /// <remarks>
    /// <strong>Both or neither.</strong> A rename is what visitors see on the
    /// public chat, so a rename without its record, or a record of a rename that
    /// did not happen, is not a state this method can leave behind.
    /// </remarks>
    Task<ConcurrencyToken> RenameAsync(
        Tenant tenant,
        ConcurrencyToken expected,
        SecurityEvent audit,
        CancellationToken cancellationToken = default);
}
