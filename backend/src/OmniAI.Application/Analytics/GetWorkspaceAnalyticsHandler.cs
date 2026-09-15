using OmniAI.Application.Authorization;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Analytics;

/// <summary>Read one workspace's dashboard.</summary>
public sealed record GetWorkspaceAnalyticsQuery(Guid TenantId, AnalyticsRange Range);

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
/// </remarks>
public sealed class GetWorkspaceAnalyticsHandler(
    IWorkspaceAnalyticsReader reader,
    TenantAuthorizer authorizer,
    TimeProvider clock)
{
    private readonly IWorkspaceAnalyticsReader _reader =
        reader ?? throw new ArgumentNullException(nameof(reader));

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

        AnalyticsWindow window = query.Range.Window(_clock.GetUtcNow());

        return await _reader.ReadAsync(tenantId, window, cancellationToken).ConfigureAwait(false);
    }
}
