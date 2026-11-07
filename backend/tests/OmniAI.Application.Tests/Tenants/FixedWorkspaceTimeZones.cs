using OmniAI.Application.Tenants.Persistence;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Tests.Tenants;

/// <summary>
/// The workspace zone port, answering with one zone for every workspace and a
/// fixed opinion of PostgreSQL's zone list (Phase 37.6).
/// </summary>
internal sealed class FixedWorkspaceTimeZones(WorkspaceTimeZone? zone = null, bool databaseKnows = true)
    : IWorkspaceTimeZones
{
    public WorkspaceTimeZone Zone { get; set; } = zone ?? WorkspaceTimeZone.Utc;

    /// <summary>How many times PostgreSQL was asked.</summary>
    public int DatabaseQuestions { get; private set; }

    public Task<WorkspaceTimeZone> OfAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Zone);

    public Task<bool> DatabaseKnowsAsync(WorkspaceTimeZone timeZone, CancellationToken cancellationToken = default)
    {
        DatabaseQuestions++;

        return Task.FromResult(databaseKnows);
    }
}
