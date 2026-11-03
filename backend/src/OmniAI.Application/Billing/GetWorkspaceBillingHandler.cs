using OmniAI.Application.Authorization;
using OmniAI.Application.Billing.Persistence;
using OmniAI.Application.Plans;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Billing;
using OmniAI.Domain.Plans;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Billing;

/// <summary>Read one workspace's commercial state.</summary>
public sealed record GetWorkspaceBillingQuery(Guid TenantId);

/// <summary>One thing a workspace could buy.</summary>
/// <remarks>
/// Carries the internal offer id, which is what a purchase request sends back.
/// No provider price identifier appears here: if the browser held one it would
/// be choosing what it pays.
/// </remarks>
public sealed record BillingOfferView(
    Guid OfferId,
    string Code,
    string PlanName,
    BillingInterval Interval,
    long AmountMinorUnits,
    string Currency,
    int? TrialDays);

/// <summary>What a workspace's subscription currently is.</summary>
/// <remarks>
/// Deliberately free of provider identifiers. A customer has no use for a Stripe
/// customer or subscription id, and putting one on a screen invites somebody to
/// treat it as authority.
/// </remarks>
public sealed record WorkspaceSubscriptionView(
    SubscriptionStatus Status,
    string PlanName,
    BillingInterval Interval,
    DateTimeOffset? CurrentPeriodStart,
    DateTimeOffset? CurrentPeriodEnd,
    DateTimeOffset? TrialEndsAt,
    bool CancelAtPeriodEnd,
    DateTimeOffset? GraceEndsAt);

/// <summary>
/// A workspace's billing screen.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A workspace with no subscription is not a broken workspace.</strong>
/// Everything created before this phase has a plan and no commercial
/// relationship, and that is reported as what it is rather than as an error or a
/// prompt to pay.
/// </para>
/// <para>
/// Offers are only listed when this deployment can actually sell: a deployment
/// with no billing configuration shows none rather than an offer that cannot be
/// bought.
/// </para>
/// </remarks>
public sealed record WorkspaceBilling(
    /// <summary>Whether this deployment takes payments at all.</summary>
    /// <remarks>
    /// A property of the installation, not of the workspace. False means nothing
    /// is for sale here and nothing is owed, and a screen should say so rather
    /// than showing an empty catalogue that reads as an outage.
    /// </remarks>
    bool IsManaged,
    WorkspaceSubscriptionView? Subscription,
    string CurrentPlanName,
    IReadOnlyList<BillingOfferView> Offers,
    bool CanPurchase,

    /// <summary>
    /// The plan the workspace is on, with its limits (Phase 37.5b). The same plan
    /// as <c>CurrentPlanName</c>.
    /// </summary>
    BillingPlanView CurrentPlan,

    /// <summary>
    /// The plan a suspended or cancelled workspace would fall back to, or
    /// <see langword="null"/> when this deployment names none it would use.
    /// </summary>
    BillingPlanView? FallbackPlan);

/// <summary>
/// A plan as the billing screen shows it: its code, its name and the six limits it
/// sets (Phase 37.5b, row 27).
/// </summary>
/// <remarks>
/// <strong>A plan definition, not usage.</strong> How much of each limit a workspace
/// has used stays behind <c>analytics.view</c> on the plan-usage read; somebody who
/// may see what the workspace pays may see what its plan allows.
/// </remarks>
public sealed record BillingPlanView(string Code, string Name, BillingPlanLimits Limits)
{
    /// <summary>A plan's own definition.</summary>
    public static BillingPlanView From(Plan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return new BillingPlanView(
            plan.Code.Value,
            plan.DisplayName.Value,
            BillingPlanLimits.From(kind => plan.LimitFor(kind).Maximum));
    }
}

/// <summary>The six limits a plan sets; <see langword="null"/> means unlimited.</summary>
public sealed record BillingPlanLimits(
    int? Members,
    int? AiAgents,
    int? KnowledgeDocuments,
    int? WebChatEndpoints,
    int? WhatsAppConnections,
    int? MonthlyProviderOperations)
{
    /// <summary>The limits, read one entitlement at a time.</summary>
    public static BillingPlanLimits From(Func<EntitlementKind, int?> limit)
    {
        ArgumentNullException.ThrowIfNull(limit);

        return new BillingPlanLimits(
            limit(EntitlementKind.Members),
            limit(EntitlementKind.AiAgents),
            limit(EntitlementKind.KnowledgeDocuments),
            limit(EntitlementKind.WebChatEndpoints),
            limit(EntitlementKind.WhatsAppConnections),
            limit(EntitlementKind.MonthlyProviderOperations));
    }
}

/// <summary>
/// Reads what a workspace is paying for.
/// </summary>
/// <remarks>
/// Behind <c>billing.view</c>, which is its own permission rather than borrowed
/// from analytics: what a workspace spends is a different kind of fact from how
/// many conversations it handled, and the people trusted with one are not
/// automatically the people trusted with the other.
/// </remarks>
public sealed class GetWorkspaceBillingHandler(
    IWorkspaceSubscriptionStore subscriptions,
    IBillingCatalog catalog,
    IPlanCatalog plans,
    IWorkspaceEntitlementService entitlements,
    BillingOptions options,
    TenantAuthorizer authorizer,
    FallbackPlanResolver fallback)
{
    private readonly IWorkspaceSubscriptionStore _subscriptions =
        subscriptions ?? throw new ArgumentNullException(nameof(subscriptions));

    private readonly IBillingCatalog _catalog =
        catalog ?? throw new ArgumentNullException(nameof(catalog));

    private readonly IPlanCatalog _plans =
        plans ?? throw new ArgumentNullException(nameof(plans));

    private readonly IWorkspaceEntitlementService _entitlements =
        entitlements ?? throw new ArgumentNullException(nameof(entitlements));

    private readonly BillingOptions _options =
        options ?? throw new ArgumentNullException(nameof(options));

    private readonly TenantAuthorizer _authorizer =
        authorizer ?? throw new ArgumentNullException(nameof(authorizer));

    private readonly FallbackPlanResolver _fallback =
        fallback ?? throw new ArgumentNullException(nameof(fallback));

    /// <returns>
    /// The workspace's billing state, or <see langword="null"/> when the caller
    /// does not belong to it.
    /// </returns>
    public async Task<WorkspaceBilling?> HandleAsync(
        GetWorkspaceBillingQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        TenantId tenantId = TenantId.From(query.TenantId);

        // Membership first, and a non-member is told the workspace is not there:
        // what somebody else pays is emphatically not their business.
        if (!await _authorizer
            .RequireAsync(tenantId, PlatformPermissions.BillingView, cancellationToken)
            .ConfigureAwait(false))
        {
            return null;
        }

        WorkspaceSubscription? subscription = await _subscriptions
            .FindCurrentAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);

        // The entitlement plan, which is the authority on what the workspace may
        // do — with or without a subscription behind it.
        WorkspaceEntitlements current = await _entitlements
            .DescribeAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);

        List<BillingOfferView> offers = [];

        // Nothing is offered on a deployment that cannot complete a purchase.
        // Showing a price that leads to a failure is worse than showing none.
        if (_options.IsConfigured)
        {
            foreach (BillingPlanOffer offer in await _catalog
                .ActiveOffersAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                Plan? plan = await _plans
                    .FindByIdAsync(offer.PlanId, cancellationToken)
                    .ConfigureAwait(false);

                if (plan is null)
                {
                    // An offer whose plan has gone is not sellable. Skipped
                    // rather than shown with a blank name.
                    continue;
                }

                offers.Add(new BillingOfferView(
                    offer.Id.Value,
                    offer.Code.Value,
                    plan.DisplayName.Value,
                    offer.Interval,
                    offer.Amount.MinorUnits,
                    offer.Currency.Value,
                    offer.TrialDays));
            }
        }

        WorkspaceSubscriptionView? view = null;

        if (subscription is not null)
        {
            Plan? plan = await _plans
                .FindByIdAsync(subscription.PlanId, cancellationToken)
                .ConfigureAwait(false);

            view = new WorkspaceSubscriptionView(
                subscription.Status,
                plan?.DisplayName.Value ?? current.PlanName,
                subscription.Interval,
                subscription.CurrentPeriodStart,
                subscription.CurrentPeriodEnd,
                subscription.TrialEndsAt,
                subscription.CancelAtPeriodEnd,
                subscription.GraceEndsAt);
        }

        // Phase 37.5b: the current plan's limits are the ones the entitlements were
        // just judged against, so they cost no further read and cannot disagree
        // with the usage screen. The fallback is what this deployment would
        // suspend to; none configured, or one it would refuse, shows as null.
        BillingPlanView currentPlan = new(
            current.PlanCode,
            current.PlanName,
            BillingPlanLimits.From(kind => current.Find(kind)?.Limit));

        Plan? fallbackPlan = await _fallback.FindAsync(cancellationToken).ConfigureAwait(false);

        return new WorkspaceBilling(
            // Whether this deployment takes payments at all -- not whether this
            // workspace happens to have bought something. A screen reading it the
            // second way would hide the whole purchase path on exactly the
            // deployments that can sell.
            _options.IsConfigured,
            view,
            current.PlanName,
            offers,

            // A second subscription is never started through checkout. A
            // workspace that already has one manages it in the provider's portal.
            _options.IsConfigured && subscription is null && offers.Count > 0,
            currentPlan,
            fallbackPlan is null ? null : BillingPlanView.From(fallbackPlan));
    }
}
