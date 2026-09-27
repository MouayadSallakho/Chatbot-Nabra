using OmniAI.Application.Billing.Persistence;
using OmniAI.Application.Plans;
using OmniAI.Domain;
using OmniAI.Domain.Billing;
using OmniAI.Domain.Plans;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Billing;

/// <summary>
/// How this deployment behaves commercially.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Secrets are not here.</strong> The provider's secret key and webhook
/// secret are read from the environment by the adapter and never travel through a
/// shape the rest of the application can see, log or serialise.
/// </para>
/// <para>
/// Bound from <c>Billing</c>. A deployment that configures none of it can still
/// run every non-commercial part of the platform; it simply cannot sell.
/// </para>
/// </remarks>
public sealed class BillingOptions
{
    public const string Section = "Billing";

    /// <summary>
    /// The plan a workspace falls back to when it stops paying.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Deliberately configured, and never guessed.</strong> The legacy and
    /// development plans are unlimited; falling back to either would mean a
    /// workspace that cancelled got everything for free, which is the one
    /// downgrade nobody would report. A deployment that has not named a real
    /// commercial fallback cannot suspend anybody, and says so rather than
    /// choosing.
    /// </para>
    /// </remarks>
    public string FallbackPlanCode { get; set; } = string.Empty;

    /// <summary>
    /// How long a workspace keeps its plan after a payment fails.
    /// </summary>
    /// <remarks>
    /// The platform's decision, not the provider's. Stripe will keep retrying on
    /// its own schedule; this is how long this product is willing to keep serving
    /// an unpaid workspace, and it is what the grace worker acts on.
    /// </remarks>
    public TimeSpan PastDueGracePeriod { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// Where the provider sends somebody back to after checkout.
    /// </summary>
    /// <remarks>
    /// <strong>From configuration, never from the request.</strong> A
    /// browser-supplied return URL is an open redirect, and one that a payment
    /// provider will dutifully send the customer to.
    /// </remarks>
    public string ReturnBaseUrl { get; set; } = string.Empty;

    /// <summary>Whether this deployment has enough to sell anything.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(FallbackPlanCode)
        && !string.IsNullOrWhiteSpace(ReturnBaseUrl);
}

/// <summary>
/// The commercial catalogue: what is for sale and what it maps to.
/// </summary>
/// <remarks>
/// Read-only, like the plan catalogue it sits beside. Offers arrive by
/// deployment; no running code path creates or edits one, because the first
/// caller of such a method would be something letting a workspace set its own
/// price.
/// </remarks>
public interface IBillingCatalog
{
    /// <summary>Every offer currently for sale, in display order.</summary>
    Task<IReadOnlyList<BillingPlanOffer>> ActiveOffersAsync(
        CancellationToken cancellationToken = default);

    /// <summary>One offer by id, whether or not it is still for sale.</summary>
    Task<BillingPlanOffer?> FindOfferAsync(
        BillingOfferId offerId,
        CancellationToken cancellationToken = default);

    /// <summary>What a provider calls this offer's price.</summary>
    Task<ProviderReference?> PriceForAsync(
        BillingOfferId offerId,
        BillingProvider provider,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Which offer a provider price belongs to.
    /// </summary>
    /// <remarks>
    /// The reverse lookup a webhook needs. A price with no mapping grants
    /// nothing — guessing a plan from an unknown price, or trusting provider
    /// metadata to name one, would let anybody who can create a Stripe price
    /// grant themselves entitlements.
    /// </remarks>
    Task<BillingPlanOffer?> OfferForPriceAsync(
        ProviderReference priceId,
        BillingProvider provider,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Applies a subscription's state and the plan that follows from it, together.
/// </summary>
/// <remarks>
/// <para>
/// <strong>One step, under the workspace's billing lock.</strong> A window in
/// which the subscription says Active and the plan assignment says otherwise is a
/// window in which a workspace is refused something it is paying for — or allowed
/// something it is not. Both writes happen in one transaction.
/// </para>
/// <para>
/// <strong>It never deletes anything.</strong> A downgrade lowers what may be
/// created; it does not remove members, agents, documents or channels. Phase 24
/// then refuses new creation while usage is above the new limit, which is the
/// whole of the intended behaviour.
/// </para>
/// </remarks>
public enum BillingApplyOutcome
{
    /// <summary>The subscription and the plan assignment now match the provider.</summary>
    Applied = 1,

    /// <summary>
    /// The workspace has no subscription and this event may not start one.
    /// </summary>
    /// <remarks>
    /// A subscription is only ever created from a completed checkout this
    /// platform itself started. Inventing one from a provider reference would let
    /// anybody who can name a subscription grant a workspace a plan.
    /// </remarks>
    NoSubscription = 2,

    /// <summary>
    /// The provider is billing a price this platform does not sell.
    /// </summary>
    /// <remarks>
    /// <strong>Nothing is guessed and nothing is granted.</strong> Not the
    /// nearest plan, not a plan named in provider metadata, and above all not an
    /// unlimited one. A price created outside this platform's catalogue must not
    /// be a way to obtain entitlements.
    /// </remarks>
    UnknownPrice = 3,
}

public interface IBillingSubscriptionService
{
    /// <summary>
    /// Brings local state in line with what the provider currently says.
    /// </summary>
    /// <remarks>
    /// Driven by a snapshot rather than an event body, so a late event cannot
    /// regress a subscription that has since recovered.
    /// </remarks>
    /// <param name="mayBegin">
    /// Whether a subscription may be created when the workspace has none. True
    /// only for a checkout this platform started and can trace to the workspace.
    /// </param>
    /// <param name="claimedEvent">
    /// The claimed event this change is made on behalf of, if any (Phase 35). Its
    /// claim is re-checked, and the row locked, inside the same transaction that
    /// changes the subscription — so a worker whose lease has lapsed changes nothing.
    /// </param>
    /// <exception cref="BillingEventOwnershipLostException">
    /// <paramref name="claimedEvent"/>'s claim is no longer current. Nothing was written.
    /// </exception>
    Task<BillingApplyOutcome> ApplyAsync(
        TenantId tenantId,
        BillingProvider provider,
        ProviderSubscriptionSnapshot snapshot,
        bool mayBegin = false,
        BillingEvent? claimedEvent = null,
        CancellationToken cancellationToken = default);

    /// <summary>Ends grace for one workspace and assigns the fallback plan.</summary>
    Task<bool> SuspendAsync(
        TenantId tenantId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The plan a suspended or cancelled workspace falls back to.
/// </summary>
/// <remarks>
/// Resolved from configuration and refused when it names a plan with no limits:
/// a fallback that grants everything would make cancelling free rather than
/// limiting, and nobody would notice until the provider bill arrived.
/// </remarks>
public sealed class FallbackPlanResolver(IPlanCatalog catalog, BillingOptions options)
{
    private readonly IPlanCatalog _catalog =
        catalog ?? throw new ArgumentNullException(nameof(catalog));

    private readonly BillingOptions _options =
        options ?? throw new ArgumentNullException(nameof(options));

    /// <exception cref="BillingNotConfiguredException">
    /// Nothing is configured, the code is unusable, no such plan exists, or the
    /// plan named grants everything.
    /// </exception>
    public async Task<Plan> ResolveAsync(CancellationToken cancellationToken = default)
    {
        string configured = _options.FallbackPlanCode?.Trim() ?? string.Empty;

        if (configured.Length == 0)
        {
            throw new BillingNotConfiguredException(
                "no fallback plan is configured, so a subscription cannot be ended safely");
        }

        PlanCode code;

        try
        {
            code = PlanCode.Create(configured);
        }
        catch (DomainValidationException invalid)
        {
            throw new BillingNotConfiguredException(
                $"the configured fallback plan code is not usable ({invalid.Message})");
        }

        Plan plan = await _catalog.FindByCodeAsync(code, cancellationToken).ConfigureAwait(false)
            ?? throw new BillingNotConfiguredException(
                $"the configured fallback plan '{code}' does not exist on this deployment");

        // A fallback that caps nothing is not a fallback. Cancelling would then
        // be an upgrade, and the mistake would be invisible.
        return Unlimited(plan)
            ? throw new BillingNotConfiguredException(
                $"the configured fallback plan '{code}' has no limits, which would make "
                + "cancelling a subscription grant more than paying for one")
            : plan;
    }

    private static bool Unlimited(Plan plan) =>
        plan.MaxMembers.IsUnlimited
        && plan.MaxAiAgents.IsUnlimited
        && plan.MaxKnowledgeDocuments.IsUnlimited
        && plan.MaxWebChatEndpoints.IsUnlimited
        && plan.MaxWhatsAppConnections.IsUnlimited
        && plan.MaxMonthlyProviderOperations.IsUnlimited;
}
