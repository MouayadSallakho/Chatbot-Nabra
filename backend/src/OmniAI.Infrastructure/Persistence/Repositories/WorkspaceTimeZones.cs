using Microsoft.EntityFrameworkCore;
using OmniAI.Application.Tenants.Persistence;
using OmniAI.Domain.Tenants;

namespace OmniAI.Infrastructure.Persistence.Repositories;

/// <summary>
/// The workspace zone, and PostgreSQL's opinion of a new one (Phase 37.6).
/// </summary>
internal sealed class WorkspaceTimeZones(OmniAiDbContext context) : IWorkspaceTimeZones
{
    private readonly OmniAiDbContext _context =
        context ?? throw new ArgumentNullException(nameof(context));

    public async Task<WorkspaceTimeZone> OfAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        await _context.Tenants
            .AsNoTracking()
            .Where(tenant => tenant.Id == tenantId)
            .Select(tenant => tenant.TimeZone)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
        ?? WorkspaceTimeZone.Utc;

    /// <remarks>
    /// An exact, case-sensitive match on the catalogue's own spelling. PostgreSQL
    /// would accept <c>asia/riyadh</c> in <c>AT TIME ZONE</c>, but the stored name is
    /// the one every reader, renderer and client is given, so only one spelling is
    /// let in.
    /// </remarks>
    public async Task<bool> DatabaseKnowsAsync(
        WorkspaceTimeZone timeZone,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        return await _context.Database
            .SqlQuery<bool>($"SELECT EXISTS (SELECT 1 FROM pg_timezone_names WHERE name = {timeZone.Name}) AS \"Value\"")
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
