using OmniAI.Application.Analytics;

namespace OmniAI.Api.Analytics;

/// <summary>
/// The workspace analytics dashboard.
/// </summary>
/// <remarks>
/// <para>
/// <strong>One route, and it only reads.</strong> A dashboard is one screen and
/// should be one request: eight endpoints would move the fan-out into the browser
/// and let the cards on one screen describe eight slightly different instants.
/// </para>
/// <para>
/// <strong>No antiforgery filter, because nothing here changes state.</strong>
/// The shared filter would wave a GET through anyway; leaving it off says plainly
/// that this group has no unsafe verb to protect. There is no POST, PUT, PATCH or
/// DELETE under this path and none is intended.
/// </para>
/// <para>
/// <strong>Reading it costs nothing.</strong> No AI provider is involved at any
/// point: every number comes from rows the platform already stores.
/// </para>
/// </remarks>
internal static class AnalyticsEndpoints
{
    public static IEndpointRouteBuilder MapAnalyticsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder analytics = endpoints
            .MapGroup("/api/tenants/{tenantId:guid}/analytics")
            .WithTags("Analytics")
            .RequireAuthorization();

        analytics.MapGet("/dashboard", DashboardAsync)
            .WithName("GetWorkspaceAnalyticsDashboard");

        return endpoints;
    }

    /// <summary>
    /// <c>GET /api/tenants/{tenantId}/analytics/dashboard?range=30d</c>.
    /// </summary>
    /// <remarks>
    /// An unrecognised range is a 400 rather than a silent fallback to the
    /// default. Somebody who asked for 45 days and was quietly given 30 would draw
    /// conclusions about a period they did not choose.
    /// </remarks>
    private static async Task<IResult> DashboardAsync(
        Guid tenantId,
        string? range,
        GetWorkspaceAnalyticsHandler handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);

        if (!AnalyticsRanges.TryParse(range, out AnalyticsRange parsed))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "That range is not one this platform reads",
                detail: $"Ask for one of: {AnalyticsRanges.Allowed}.");
        }

        WorkspaceAnalytics? analytics = await handler
            .HandleAsync(new GetWorkspaceAnalyticsQuery(tenantId, parsed), cancellationToken)
            .ConfigureAwait(false);

        // Absent rather than forbidden: somebody who is not a member of this
        // workspace learns nothing about whether it exists.
        return analytics is null
            ? Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "No such workspace",
                detail: "This workspace does not exist, or you are not a member of it.")
            : Results.Ok(AnalyticsDashboardResponse.From(analytics));
    }
}
