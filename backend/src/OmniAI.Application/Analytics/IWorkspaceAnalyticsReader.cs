using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Analytics;

/// <summary>
/// Reads one workspace's numbers out of the operational records.
/// </summary>
/// <remarks>
/// <para>
/// <strong>There is no analytics store.</strong> Conversations, messages,
/// conversation turns and deliveries are already the authoritative record of what
/// happened; copying them into a second system to draw a bar chart would create
/// two versions of the truth and a reconciliation job to argue between them. This
/// is a read over the tables that already exist.
/// </para>
/// <para>
/// <strong>Aggregation happens in PostgreSQL.</strong> The implementation is
/// required to count with <c>COUNT</c> and <c>GROUP BY</c> and to return bounded
/// result sets. Loading a quarter of conversations into memory to count them in
/// C# would work on a demo workspace and fall over on a real one.
/// </para>
/// <para>
/// One method, deliberately. A dashboard is one screen and should be one request;
/// splitting it into eight endpoints would move the fan-out into the browser and
/// make the numbers on one screen come from eight different instants.
/// </para>
/// </remarks>
public interface IWorkspaceAnalyticsReader
{
    /// <summary>
    /// Everything one dashboard shows, for one workspace and one window.
    /// </summary>
    /// <remarks>
    /// The workspace is a parameter rather than an ambient value: every query
    /// underneath filters on it, and a reader that could be called without one
    /// would be a cross-tenant leak waiting for a careless call site.
    /// </remarks>
    /// <param name="breakdown">
    /// How many agents and models the breakdowns may name. The dashboard passes
    /// <see cref="AnalyticsBreakdown.Dashboard"/>; a report passes its export
    /// bound. Nothing else about the read changes.
    /// </param>
    Task<WorkspaceAnalytics> ReadAsync(
        TenantId tenantId,
        AnalyticsWindow window,
        AnalyticsBreakdown breakdown,
        CancellationToken cancellationToken = default);
}
