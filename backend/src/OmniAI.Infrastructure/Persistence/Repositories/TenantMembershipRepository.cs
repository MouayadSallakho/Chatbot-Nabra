using Microsoft.EntityFrameworkCore;
using OmniAI.Application.Tenants;
using OmniAI.Application.Tenants.Persistence;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of the membership queries.
/// </summary>
/// <remarks>
/// Both methods take the user as a required argument and put it in the SQL. There
/// is no overload that omits it and no query shape here that could read across
/// people, which is the property that matters: the predicate cannot be forgotten
/// at a call site because there is no call that compiles without it.
/// </remarks>
internal sealed class TenantMembershipRepository(OmniAiDbContext context)
    : ITenantMembershipRepository
{
    private readonly OmniAiDbContext _context =
        context ?? throw new ArgumentNullException(nameof(context));

    /// <summary>
    /// Whether a membership row pairs this person with this tenant.
    /// </summary>
    /// <remarks>
    /// <c>AnyAsync</c> rather than a fetch: the caller needs a yes or no, and this
    /// becomes an index probe on the composite primary key rather than a row
    /// materialisation. Nothing is tracked, so the check cannot leave state behind
    /// in a unit of work that is about to write something else.
    /// <para>
    /// Runs on every tenant-scoped request. That is deliberate and is the reason
    /// membership is not baked into the sign-in cookie as a claim: a cookie claim
    /// would keep granting access for as long as the cookie lived, so revoking
    /// somebody would not take effect until they signed out. One primary-key probe
    /// is a small price for a check that is correct at the moment it is asked.
    /// </para>
    /// </remarks>
    public async Task<bool> ExistsAsync(
        UserId userId,
        TenantId tenantId,
        CancellationToken cancellationToken = default) =>
        await _context.TenantMemberships
            .AsNoTracking()
            .AnyAsync(
                membership => membership.UserId == userId && membership.TenantId == tenantId,
                cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// The tenants this person belongs to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A join expressed as a query over both sets, projected straight into the
    /// Application's read model. It never materialises a <c>Tenant</c> aggregate:
    /// the caller is building a list of workspaces to choose from, and putting a
    /// page of aggregates into the change tracker for a read that will never write
    /// costs memory and invites an accidental save.
    /// </para>
    /// <para>
    /// <strong>Ordered by membership age, then by tenant id.</strong> The
    /// tie-break is not decoration: two workspaces created in the same transaction
    /// share a timestamp exactly, and without a total order the workspace switcher
    /// would reorder itself between page loads for no visible reason. Oldest first,
    /// so somebody's original workspace keeps its position as they add more.
    /// </para>
    /// <para>
    /// The ordering is applied <em>after</em> the join rather than before it. A
    /// sort placed on the membership set ahead of a join is not guaranteed to
    /// survive into the generated SQL, which would leave the contract's promised
    /// order true only by accident of the query plan.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<TenantView>> ListTenantsAsync(
        UserId userId,
        CancellationToken cancellationToken = default) =>
        await _context.TenantMemberships
            .AsNoTracking()
            .Where(membership => membership.UserId == userId)
            .Join(
                _context.Tenants,
                membership => membership.TenantId,
                tenant => tenant.Id,
                (membership, tenant) => new { membership.CreatedAt, Tenant = tenant })
            .OrderBy(row => row.CreatedAt)
            .ThenBy(row => row.Tenant.Id)
            .Select(row => new TenantView(
                row.Tenant.Id.Value,
                row.Tenant.Name.Value,
                row.Tenant.Slug.Value,
                row.Tenant.Status))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
}
