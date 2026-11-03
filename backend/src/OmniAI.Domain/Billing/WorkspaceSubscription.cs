using OmniAI.Domain.Plans;
using OmniAI.Domain.Tenants;

namespace OmniAI.Domain.Billing;

/// <summary>
/// Where a workspace's commercial relationship stands.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Provider-independent, and smaller than any provider's own set.</strong>
/// Stripe has eight subscription statuses; several of them mean the same thing to
/// this product, and one of them (<c>unpaid</c>) means something Stripe decides
/// by configuration. Copying them in would scatter provider vocabulary through
/// the domain and make a second provider impossible to add without either
/// distorting its states or widening this enum for everyone.
/// </para>
/// <para>
/// <strong>But not so small that it lies.</strong> Trialing is not Active — one
/// has been paid for and the other has not. PastDue is not Suspended — one still
/// has its plan and the other does not. Collapsing either pair would lose a
/// distinction the product genuinely makes.
/// </para>
/// </remarks>
public enum SubscriptionStatus
{
    /// <summary>
    /// Checkout completed or a subscription exists, but nothing is confirmed yet.
    /// </summary>
    /// <remarks>
    /// Grants nothing. A customer who reached the success page has not
    /// necessarily paid, and this is the state that says so honestly while the
    /// provider's own answer is fetched.
    /// </remarks>
    PendingActivation = 1,

    /// <summary>In a trial the provider itself reports. Grants the plan.</summary>
    Trialing = 2,

    /// <summary>Paid and current. Grants the plan.</summary>
    Active = 3,

    /// <summary>
    /// A payment failed and the grace period has not expired. Still grants the plan.
    /// </summary>
    /// <remarks>
    /// One failed card must not cost somebody their workspace the same afternoon.
    /// The plan is retained until the platform's own grace period ends.
    /// </remarks>
    PastDue = 4,

    /// <summary>
    /// Grace expired without recovery. The plan is withdrawn; nothing is deleted.
    /// </summary>
    Suspended = 5,

    /// <summary>Terminally ended. The plan is withdrawn; nothing is deleted.</summary>
    Canceled = 6,
}

/// <summary>
/// One workspace's commercial subscription.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is not the entitlement source.</strong> Phase 24's plan
/// assignment remains the only thing entitlement checks read. A subscription
/// decides <em>which</em> plan a workspace should be assigned and when that
/// changes; it never answers "may this workspace create another agent".
/// </para>
/// <para>
/// <strong>Provider identifiers are carried, never obeyed.</strong> The Stripe
/// customer and subscription references live here so the platform can ask the
/// provider what it currently thinks; no branch in this class depends on their
/// contents, and the raw provider status is kept only so an operator can see what
/// was mapped.
/// </para>
/// <para>
/// <strong>At most one non-terminal subscription governs a workspace</strong>,
/// which the database enforces with a partial unique index rather than an
/// application check that two requests could both pass.
/// </para>
/// </remarks>
public sealed class WorkspaceSubscription
{
    private WorkspaceSubscription(
        WorkspaceSubscriptionId id,
        TenantId tenantId,
        BillingProvider provider,
        BillingOfferId offerId,
        PlanId planId,
        SubscriptionStatus status,
        BillingInterval interval,
        DateTimeOffset createdAt)
    {
        Id = id;
        TenantId = tenantId;
        Provider = provider;
        OfferId = offerId;
        PlanId = planId;
        Status = status;
        Interval = interval;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public WorkspaceSubscriptionId Id { get; }

    public TenantId TenantId { get; }

    public BillingProvider Provider { get; }

    /// <summary>The offer bought. Determines the plan and the interval.</summary>
    public BillingOfferId OfferId { get; private set; }

    /// <summary>The plan this subscription entitles the workspace to.</summary>
    public PlanId PlanId { get; private set; }

    public SubscriptionStatus Status { get; private set; }

    public BillingInterval Interval { get; private set; }

    /// <summary>The provider's own subscription, once one exists.</summary>
    public ProviderReference? ProviderSubscriptionId { get; private set; }

    /// <summary>
    /// What the provider called this state, kept for diagnostics only.
    /// </summary>
    /// <remarks>
    /// Never branched on. It exists so somebody reading the database can see
    /// which provider state produced a normalised one, which is the first thing
    /// anybody asks when a mapping looks wrong.
    /// </remarks>
    public string? ProviderStatus { get; private set; }

    public DateTimeOffset? CurrentPeriodStart { get; private set; }

    public DateTimeOffset? CurrentPeriodEnd { get; private set; }

    public DateTimeOffset? TrialEndsAt { get; private set; }

    /// <summary>Whether the provider says this ends when the paid period does.</summary>
    public bool CancelAtPeriodEnd { get; private set; }

    public DateTimeOffset? CanceledAt { get; private set; }

    /// <summary>
    /// When the platform's grace period for a failed payment runs out.
    /// </summary>
    /// <remarks>
    /// The platform's, not the provider's. Stripe may sit in <c>past_due</c> for
    /// as long as its retry schedule lasts; how long this product is willing to
    /// keep serving an unpaid workspace is a decision this platform makes and
    /// must be able to act on without waiting for anybody.
    /// </remarks>
    public DateTimeOffset? GraceEndsAt { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Whether this subscription still governs the workspace.</summary>
    public bool IsTerminal => Status is SubscriptionStatus.Canceled;

    /// <summary>Whether this state should provision the subscribed plan.</summary>
    /// <remarks>
    /// The whole entitlement policy, in one place and one expression. PastDue is
    /// included deliberately: during grace the workspace keeps what it paid for.
    /// </remarks>
    public bool GrantsPlan => Status
        is SubscriptionStatus.Trialing
        or SubscriptionStatus.Active
        or SubscriptionStatus.PastDue;

    /// <summary>Starts a subscription that nothing has confirmed yet.</summary>
    public static WorkspaceSubscription Begin(
        TenantId tenantId,
        BillingProvider provider,
        BillingOfferId offerId,
        PlanId planId,
        BillingInterval interval,
        DateTimeOffset createdAt)
    {
        if (tenantId.IsEmpty)
        {
            throw new DomainValidationException("A subscription must belong to a workspace.")
            {
                Code = "billing.subscription.invalid",
            };
        }

        if (offerId.IsEmpty)
        {
            throw new DomainValidationException("A subscription must record what was bought.")
            {
                Code = "billing.subscription.invalid",
            };
        }

        if (planId.IsEmpty)
        {
            throw new DomainValidationException("A subscription must record its plan.")
            {
                Code = "billing.subscription.invalid",
            };
        }

        return new WorkspaceSubscription(
            WorkspaceSubscriptionId.New(),
            tenantId,
            provider,
            offerId,
            planId,
            SubscriptionStatus.PendingActivation,
            interval,
            createdAt == default
                ? throw new DomainValidationException(
                    "A subscription must record when it was created.")
                    {
                        Code = "billing.subscription.invalid",
                    }
                : createdAt.ToUniversalTime());
    }

    /// <summary>Rehydrates a stored subscription. Persistence only.</summary>
    public static WorkspaceSubscription Restore(
        WorkspaceSubscriptionId id,
        TenantId tenantId,
        BillingProvider provider,
        BillingOfferId offerId,
        PlanId planId,
        SubscriptionStatus status,
        BillingInterval interval,
        ProviderReference? providerSubscriptionId,
        string? providerStatus,
        DateTimeOffset? currentPeriodStart,
        DateTimeOffset? currentPeriodEnd,
        DateTimeOffset? trialEndsAt,
        bool cancelAtPeriodEnd,
        DateTimeOffset? canceledAt,
        DateTimeOffset? graceEndsAt,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt) =>
        new(id, tenantId, provider, offerId, planId, status, interval, createdAt)
        {
            ProviderSubscriptionId = providerSubscriptionId,
            ProviderStatus = providerStatus,
            CurrentPeriodStart = currentPeriodStart,
            CurrentPeriodEnd = currentPeriodEnd,
            TrialEndsAt = trialEndsAt,
            CancelAtPeriodEnd = cancelAtPeriodEnd,
            CanceledAt = canceledAt,
            GraceEndsAt = graceEndsAt,
            UpdatedAt = updatedAt,
        };

    /// <summary>
    /// Applies what the provider currently says, and returns whether anything moved.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Driven by a snapshot, never by an event's contents.</strong>
    /// Provider events arrive out of order; a payment failure delivered late must
    /// not drag a subscription that has since recovered back into PastDue. So an
    /// event is only a signal to go and look, and this applies what the look
    /// found.
    /// </para>
    /// <para>
    /// <strong>Grace is this platform's to set and to keep.</strong> Entering
    /// PastDue starts the clock once; staying in PastDue does not restart it, or
    /// a provider that retries daily would extend the grace period indefinitely.
    /// </para>
    /// </remarks>
    public bool Apply(
        SubscriptionStatus status,
        string providerStatus,
        ProviderReference? providerSubscriptionId,
        DateTimeOffset? currentPeriodStart,
        DateTimeOffset? currentPeriodEnd,
        DateTimeOffset? trialEndsAt,
        bool cancelAtPeriodEnd,
        DateTimeOffset? canceledAt,
        TimeSpan graceFor,
        DateTimeOffset now)
    {
        if (IsTerminal)
        {
            // Cancellation is the end. A late event about a dead subscription
            // changes nothing, and letting one revive it would resurrect access
            // somebody has already stopped paying for.
            return false;
        }

        SubscriptionStatus previous = Status;

        Status = status;
        ProviderStatus = providerStatus;
        ProviderSubscriptionId = providerSubscriptionId ?? ProviderSubscriptionId;
        CurrentPeriodStart = currentPeriodStart;
        CurrentPeriodEnd = currentPeriodEnd;
        TrialEndsAt = trialEndsAt;
        CancelAtPeriodEnd = cancelAtPeriodEnd;
        CanceledAt = canceledAt;

        GraceEndsAt = status switch
        {
            // Entering PastDue starts the clock. Already being in it keeps the
            // original deadline, so repeated failure notices cannot extend it.
            SubscriptionStatus.PastDue when previous != SubscriptionStatus.PastDue =>
                now.ToUniversalTime().Add(graceFor),
            SubscriptionStatus.PastDue => GraceEndsAt,

            // Recovery clears it. Nothing is owed, so nothing is counting down.
            _ => null,
        };

        UpdatedAt = now.ToUniversalTime();

        return Status != previous;
    }

    /// <summary>
    /// Ends the grace period without recovery.
    /// </summary>
    /// <returns><see langword="false"/> when this was not eligible, changing nothing.</returns>
    public bool Suspend(DateTimeOffset now)
    {
        if (Status != SubscriptionStatus.PastDue || GraceEndsAt is null || now < GraceEndsAt)
        {
            return false;
        }

        Status = SubscriptionStatus.Suspended;
        GraceEndsAt = null;
        UpdatedAt = now.ToUniversalTime();

        return true;
    }

    /// <summary>Moves the subscription onto a different offer and plan.</summary>
    /// <remarks>
    /// A change of what is being paid for, made in the provider's portal and
    /// discovered when the platform next reads the provider's state.
    /// </remarks>
    public void MoveTo(BillingOfferId offerId, PlanId planId, BillingInterval interval, DateTimeOffset now)
    {
        if (offerId.IsEmpty || planId.IsEmpty)
        {
            throw new DomainValidationException("A subscription must always name an offer and a plan.")
            {
                Code = "billing.subscription.invalid",
            };
        }

        OfferId = offerId;
        PlanId = planId;
        Interval = interval;
        UpdatedAt = now.ToUniversalTime();
    }
}
