using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Tenants.Persistence;

/// <summary>
/// The workspace time zone, for the readers that count days in it (Phase 37.6).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Read, never decided, here.</strong> Analytics, reports and schedules
/// ask which zone a workspace counts in. Nobody but the time-zone use case changes
/// it, and that goes through <see cref="ITenantRepository"/> with its version.
/// </para>
/// <para>
/// <strong>The database is asked too.</strong> PostgreSQL turns every stored
/// instant into a local day, so a zone it does not know cannot be accepted, even
/// if the runtime knows it.
/// </para>
/// </remarks>
public interface IWorkspaceTimeZones
{
    /// <summary>The workspace's zone, or UTC when there is no such workspace.</summary>
    /// <remarks>
    /// Not an authorisation check. Callers have already proved membership and
    /// permission; this is one primary-key read of one column.
    /// </remarks>
    Task<WorkspaceTimeZone> OfAsync(TenantId tenantId, CancellationToken cancellationToken = default);

    /// <summary>Whether PostgreSQL's own zone list (<c>pg_timezone_names</c>) has this exact name.</summary>
    Task<bool> DatabaseKnowsAsync(WorkspaceTimeZone timeZone, CancellationToken cancellationToken = default);
}
