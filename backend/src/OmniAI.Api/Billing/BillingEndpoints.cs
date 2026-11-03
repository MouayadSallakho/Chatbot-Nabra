using OmniAI.Api.Security;
using OmniAI.Application.Billing;

namespace OmniAI.Api.Billing;

/// <summary>
/// A workspace's subscription, and the two ways to act on it.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The browser never names a price.</strong> A purchase carries an
/// internal offer id and nothing else. The provider's price comes from a
/// server-side mapping, the return addresses from configuration, and the
/// idempotency key from a durable intent — so nothing a client sends can change
/// what is bought, what it costs or where the customer is sent afterwards.
/// </para>
/// <para>
/// <strong>Both writes are antiforgery-checked.</strong> These are
/// cookie-authenticated routes that spend money and change what a workspace is
/// allowed to do; a cross-site form post that started a subscription would be a
/// real attack. The provider's webhook is the deliberate exception, and it lives
/// on its own anonymous route with a signature instead.
/// </para>
/// <para>
/// <strong>There is no plan or price editor here.</strong> No route creates an
/// offer, sets an amount or maps a provider price. Those are platform decisions,
/// made by people with access to the deployment rather than by anybody holding
/// <c>billing.manage</c> in one workspace.
/// </para>
/// </remarks>
internal static class BillingEndpoints
{
    public static IEndpointRouteBuilder MapBillingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder billing = endpoints
            .MapGroup("/api/tenants/{tenantId:guid}/billing")
            .WithTags("Billing")
            .RequireAuthorization();

        billing.MapGet("/", OverviewAsync)
            .WithName("GetWorkspaceBilling")
            .WithSummary("What the workspace pays for: subscription, offers, and its current and fallback plans with their limits (null = unlimited). 404 without billing.view.")
            .Produces<BillingResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        billing.MapPost("/checkout", CheckoutAsync)
            .WithName("StartWorkspaceBillingCheckout")
            .AddEndpointFilter<AntiforgeryEndpointFilter>();

        billing.MapPost("/portal", PortalAsync)
            .WithName("OpenWorkspaceBillingPortal")
            .AddEndpointFilter<AntiforgeryEndpointFilter>();

        return endpoints;
    }

    /// <summary><c>GET /api/tenants/{tenantId}/billing</c>. Requires <c>billing.view</c>.</summary>
    private static async Task<IResult> OverviewAsync(
        Guid tenantId,
        GetWorkspaceBillingHandler handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);

        WorkspaceBilling? billing = await handler
            .HandleAsync(new GetWorkspaceBillingQuery(tenantId), cancellationToken)
            .ConfigureAwait(false);

        // Absent rather than forbidden. Somebody who is not a member must not
        // learn that this workspace exists, and somebody without billing
        // authority must not learn what it pays.
        return billing is null
            ? Results.NotFound()
            : Results.Ok(BillingResponse.From(billing));
    }

    /// <summary>
    /// <c>POST /api/tenants/{tenantId}/billing/checkout</c>. Requires <c>billing.manage</c>.
    /// </summary>
    /// <remarks>
    /// Answers with a provider URL to follow. Reaching the URL's success page is
    /// not evidence of anything — the subscription becomes real when a verified
    /// provider event says so, which happens on a different route entirely.
    /// </remarks>
    private static async Task<IResult> CheckoutAsync(
        Guid tenantId,
        StartCheckoutRequest request,
        StartBillingCheckoutHandler handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);

        if (request is null)
        {
            return Results.BadRequest();
        }

        BillingCheckoutStarted started = await handler
            .HandleAsync(
                new StartBillingCheckoutCommand(tenantId, request.OfferId),
                cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(new StartCheckoutResponse(started.Url));
    }

    /// <summary>
    /// <c>POST /api/tenants/{tenantId}/billing/portal</c>. Requires <c>billing.manage</c>.
    /// </summary>
    /// <remarks>
    /// Payment methods, invoices, receipts and cancellation all live in the
    /// provider's hosted portal. Rebuilding them here would move their entire
    /// compliance surface onto this platform for no benefit to anybody.
    /// </remarks>
    private static async Task<IResult> PortalAsync(
        Guid tenantId,
        CreateBillingPortalSessionHandler handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);

        BillingPortalSession session = await handler
            .HandleAsync(new CreateBillingPortalSessionCommand(tenantId), cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(new BillingPortalResponse(session.Url));
    }
}

/// <summary>
/// What a browser sends to buy a subscription.
/// </summary>
/// <remarks>
/// <strong>One field, and it is internal.</strong> No price, no amount, no
/// currency, no provider identifier, no plan. A client that could send any of
/// those could buy something nobody put on sale, or buy it for a number it
/// chose.
/// </remarks>
public sealed record StartCheckoutRequest(Guid OfferId);

/// <summary>Where to send the customer to pay.</summary>
public sealed record StartCheckoutResponse(string Url);

/// <summary>Where to send the customer to manage their billing.</summary>
public sealed record BillingPortalResponse(string Url);

/// <summary>An offer, as a screen needs it.</summary>
/// <remarks>
/// The amount travels in minor units with its currency beside it, exactly as it
/// is stored. Formatting is the browser's job, and sending a pre-formatted string
/// would bake this server's locale into every client.
/// </remarks>
public sealed record BillingOfferResponse(
    Guid Id,
    string Code,
    string PlanName,
    string Interval,
    long AmountMinorUnits,
    string Currency,
    int? TrialDays);

/// <summary>A workspace's subscription, as a screen needs it.</summary>
/// <remarks>
/// <strong>No provider identifiers.</strong> Not the subscription id, not the
/// customer id, not the session id. A browser has no use for them and they are a
/// direct handle on somebody's payment record.
/// </remarks>
public sealed record WorkspaceSubscriptionResponse(
    string Status,
    string Interval,
    string PlanName,
    DateTimeOffset? CurrentPeriodEnd,
    DateTimeOffset? TrialEndsAt,
    bool CancelAtPeriodEnd,
    DateTimeOffset? GraceEndsAt);

/// <summary>
/// The billing screen's whole payload.
/// </summary>
/// <remarks>
/// <c>isManaged</c> is false on a deployment that sells nothing, and then the
/// offer list is empty and <c>canPurchase</c> is false. A screen that showed a
/// buy button anyway would send somebody to a checkout that cannot be created.
/// </remarks>
public sealed record BillingResponse(
    bool IsManaged,
    bool CanPurchase,
    string CurrentPlanName,
    WorkspaceSubscriptionResponse? Subscription,
    IReadOnlyList<BillingOfferResponse> Offers,
    BillingPlanResponse CurrentPlan,
    BillingPlanResponse? FallbackPlan)
{
    public static BillingResponse From(WorkspaceBilling billing)
    {
        ArgumentNullException.ThrowIfNull(billing);

        return new BillingResponse(
            billing.IsManaged,
            billing.CanPurchase,
            billing.CurrentPlanName,
            billing.Subscription is null
                ? null
                : new WorkspaceSubscriptionResponse(
                    billing.Subscription.Status.ToString(),
                    billing.Subscription.Interval.ToString(),
                    billing.Subscription.PlanName,
                    billing.Subscription.CurrentPeriodEnd,
                    billing.Subscription.TrialEndsAt,
                    billing.Subscription.CancelAtPeriodEnd,
                    billing.Subscription.GraceEndsAt),
            [
                .. billing.Offers.Select(offer => new BillingOfferResponse(
                    offer.OfferId,
                    offer.Code,
                    offer.PlanName,
                    offer.Interval.ToString(),
                    offer.AmountMinorUnits,
                    offer.Currency,
                    offer.TrialDays)),
            ],
            BillingPlanResponse.From(billing.CurrentPlan),
            billing.FallbackPlan is null ? null : BillingPlanResponse.From(billing.FallbackPlan));
    }
}

/// <summary>
/// A plan on the billing read: its code, name and limits (Phase 37.5b, row 27).
/// </summary>
/// <remarks>
/// Plan definitions only. Usage stays on <c>GET …/plan-usage</c>, behind
/// <c>analytics.view</c>.
/// </remarks>
public sealed record BillingPlanResponse(string Code, string Name, BillingPlanLimitsResponse Limits)
{
    /// <summary>Projects a plan.</summary>
    public static BillingPlanResponse From(BillingPlanView plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return new BillingPlanResponse(
            plan.Code,
            plan.Name,
            new BillingPlanLimitsResponse(
                plan.Limits.Members,
                plan.Limits.AiAgents,
                plan.Limits.KnowledgeDocuments,
                plan.Limits.WebChatEndpoints,
                plan.Limits.WhatsAppConnections,
                plan.Limits.MonthlyProviderOperations));
    }
}

/// <summary>
/// The six limits a plan sets, named as the plan-usage read names its
/// entitlements. <see langword="null"/> means unlimited, and is always written.
/// </summary>
public sealed record BillingPlanLimitsResponse(
    int? Members,
    int? AiAgents,
    int? KnowledgeDocuments,
    int? WebChatEndpoints,
    int? WhatsAppConnections,
    int? MonthlyProviderOperations);
