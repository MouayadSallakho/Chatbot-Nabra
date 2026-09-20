using OmniAI.Application.Plans;
using OmniAI.Domain.Plans;

namespace OmniAI.Api.Plans;

/// <summary>
/// What a workspace's plan allows, and how much of it is used.
/// </summary>
/// <remarks>
/// <para>
/// <strong>One route, and it only reads.</strong> There is deliberately no
/// <c>POST /plans</c>, no <c>PUT /plans/{id}</c> and no upgrade route: plans are
/// platform configuration, and a workspace that could write here would be a
/// workspace that could raise its own limits. Commercial transitions between
/// plans belong to Phase 25, along with whatever authority is meant to approve
/// them.
/// </para>
/// <para>
/// <strong>Behind <c>analytics.view</c>, reusing what already exists.</strong>
/// No <c>plan.view</c>, <c>quota.view</c> or <c>billing.view</c> was added.
/// </para>
/// </remarks>
internal static class PlanUsageEndpoints
{
    public static IEndpointRouteBuilder MapPlanUsageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder plans = endpoints
            .MapGroup("/api/tenants/{tenantId:guid}")
            .WithTags("Plan and usage")
            .RequireAuthorization();

        plans.MapGet("/plan-usage", UsageAsync).WithName("GetWorkspacePlanUsage");

        return endpoints;
    }

    /// <summary><c>GET /api/tenants/{tenantId}/plan-usage</c>.</summary>
    private static async Task<IResult> UsageAsync(
        Guid tenantId,
        GetWorkspacePlanUsageHandler handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);

        WorkspaceEntitlements? usage = await handler
            .HandleAsync(new GetWorkspacePlanUsageQuery(tenantId), cancellationToken)
            .ConfigureAwait(false);

        // Absent rather than forbidden: somebody who is not a member of this
        // workspace must not learn that it exists, let alone how full it is.
        return usage is null
            ? Results.NotFound()
            : Results.Ok(PlanUsageResponse.From(usage));
    }
}

/// <summary>One entitlement on the wire.</summary>
/// <remarks>
/// <para>
/// <c>limit</c> is null exactly when <c>unlimited</c> is true. No sentinel is
/// ever sent — a client that saw <c>999999999</c> would draw a progress bar
/// against it and eventually print it.
/// </para>
/// <para>
/// <c>state</c> is the word, not a colour. A client that only had a percentage
/// would have to re-derive the thresholds and would drift from the server the
/// first time they changed.
/// </para>
/// </remarks>
public sealed record PlanEntitlementResponse(
    string Entitlement,
    long Used,
    int? Limit,
    bool Unlimited,
    int? PercentUsed,
    string State,
    long? Remaining,
    PlanPeriodResponse? Period);

/// <summary>The window a monthly entitlement is measured over. Half-open, UTC.</summary>
public sealed record PlanPeriodResponse(DateTimeOffset From, DateTimeOffset To);

/// <summary>
/// A workspace's plan and its usage.
/// </summary>
/// <remarks>
/// <strong>No price, no payment, no upgrade link.</strong> This platform has no
/// commercial machinery, and a field here implying otherwise would be read as a
/// promise. The plan's code and name travel because a screen has to say which
/// plan it is describing; nothing else about the plan does.
/// </remarks>
public sealed record PlanUsageResponse(
    string PlanCode,
    string PlanName,
    DateTimeOffset AssignedAt,
    IReadOnlyList<PlanEntitlementResponse> Entitlements)
{
    public static PlanUsageResponse From(WorkspaceEntitlements usage)
    {
        ArgumentNullException.ThrowIfNull(usage);

        return new PlanUsageResponse(
            usage.PlanCode,
            usage.PlanName,
            usage.AssignedAt,
            [
                .. usage.Entitlements.Select(entitlement => new PlanEntitlementResponse(
                    Name(entitlement.Kind),
                    entitlement.Used,
                    entitlement.Limit,
                    entitlement.IsUnlimited,
                    entitlement.PercentUsed,
                    entitlement.State.ToString(),
                    entitlement.Remaining,
                    entitlement.Period is null
                        ? null
                        : new PlanPeriodResponse(
                            entitlement.Period.From,
                            entitlement.Period.To))),
            ]);
    }

    /// <summary>
    /// The wire name of an entitlement.
    /// </summary>
    /// <remarks>
    /// Spelt out rather than taken from <c>ToString</c>, so renaming the enum
    /// member cannot silently change a published contract that clients branch on.
    /// </remarks>
    internal static string Name(EntitlementKind kind) => kind switch
    {
        EntitlementKind.Members => "members",
        EntitlementKind.AiAgents => "aiAgents",
        EntitlementKind.KnowledgeDocuments => "knowledgeDocuments",
        EntitlementKind.WebChatEndpoints => "webChatEndpoints",
        EntitlementKind.WhatsAppConnections => "whatsAppConnections",
        EntitlementKind.MonthlyProviderOperations => "monthlyProviderOperations",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown entitlement."),
    };
}
