using OmniAI.Application.Authorization;
using OmniAI.Application.Tenants.Persistence;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Analytics;

/// <summary>Read one workspace's dashboard.</summary>
/// <param name="ComparePrevious">
/// Whether to add the period just before, for comparison (<c>compare=previous</c>,
/// Phase 37.6). Off unless asked for: it costs three more statements.
/// </param>
public sealed record GetWorkspaceAnalyticsQuery(Guid TenantId, AnalyticsRange Range, bool ComparePrevious = false);

/// <summary>
/// The analytics dashboard.
/// </summary>
/// <remarks>
/// <para>
/// <strong>It spends nothing.</strong> No embedding, no completion, no provider
/// call of any kind: every number comes from rows this platform already holds.
/// Opening a dashboard must never cost money, or nobody will open it.
/// </para>
/// <para>
/// <strong>The window is computed here, from the clock, once.</strong> Every
/// query underneath is handed the same instants, so the summary, the daily
/// buckets and the channel breakdown describe the same period rather than three
/// periods a few milliseconds apart.
/// </para>
/// <para>
/// <strong>In the workspace's zone (Phase 37.6).</strong> The zone is read after
/// authorisation, so a non-member learns nothing about it, and the window's days
/// are local days of it. The previous period, when asked for, is derived from the
/// same window: adjacent, as many days, and in the same zone.
/// </para>
/// </remarks>
public sealed class GetWorkspaceAnalyticsHandler(
    IWorkspaceAnalyticsReader reader,
    IWorkspaceTimeZones timeZones,
    TenantAuthorizer authorizer,
    TimeProvider clock)
{
    private readonly IWorkspaceAnalyticsReader _reader =
        reader ?? throw new ArgumentNullException(nameof(reader));

    private readonly IWorkspaceTimeZones _timeZones =
        timeZones ?? throw new ArgumentNullException(nameof(timeZones));

    private readonly TenantAuthorizer _authorizer =
        authorizer ?? throw new ArgumentNullException(nameof(authorizer));

    private readonly TimeProvider _clock =
        clock ?? throw new ArgumentNullException(nameof(clock));

    /// <returns>
    /// The dashboard, or <see langword="null"/> when the caller is not entitled to
    /// it or does not belong to this workspace.
    /// </returns>
    public async Task<WorkspaceAnalytics?> HandleAsync(
        GetWorkspaceAnalyticsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        TenantId tenantId = TenantId.From(query.TenantId);

        if (!await _authorizer
            .RequireAsync(tenantId, PlatformPermissions.AnalyticsView, cancellationToken)
            .ConfigureAwait(false))
        {
            return null;
        }

        WorkspaceTimeZone zone = await _timeZones.OfAsync(tenantId, cancellationToken).ConfigureAwait(false);

        AnalyticsWindow window = query.Range.Window(_clock.GetUtcNow(), zone);

        WorkspaceAnalytics analytics = await _reader
            .ReadAsync(tenantId, window, AnalyticsBreakdown.Dashboard, cancellationToken)
            .ConfigureAwait(false);

        if (!query.ComparePrevious)
        {
            return analytics;
        }

        AnalyticsComparison previous = await _reader
            .ReadComparisonAsync(tenantId, window.Previous(), cancellationToken)
            .ConfigureAwait(false);

        return analytics with { Previous = previous };
    }
}
