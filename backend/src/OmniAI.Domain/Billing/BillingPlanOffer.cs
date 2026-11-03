using OmniAI.Domain.Plans;

namespace OmniAI.Domain.Billing;

/// <summary>
/// How a plan is sold.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Separate from the plan on purpose.</strong> A plan says what a
/// workspace may consume; an offer says what somebody pays to get it, how often,
/// and in what currency. The same plan can be sold monthly and annually, in two
/// currencies, or withdrawn from sale entirely while every workspace already on
/// it keeps working — none of which is expressible if price is a column on the
/// plan.
/// </para>
/// <para>
/// <strong>Platform-owned, like the plan itself.</strong> No workspace creates an
/// offer, edits an amount or picks a currency. There is no tenant-facing write
/// path and no customer-facing price editor.
/// </para>
/// <para>
/// <strong>It holds no provider identifier.</strong> Which Stripe price
/// corresponds to this offer is an integration detail, kept in its own mapping so
/// that a second provider is another row rather than another column here.
/// </para>
/// </remarks>
public sealed class BillingPlanOffer
{
    private BillingPlanOffer(
        BillingOfferId id,
        PlanId planId,
        OfferCode code,
        BillingInterval interval,
        long amountMinorUnits,
        BillingCurrency currency,
        int? trialDays,
        bool isActive,
        DateTimeOffset createdAt)
    {
        Id = id;
        PlanId = planId;
        Code = code;
        Interval = interval;
        AmountMinorUnits = amountMinorUnits;
        Currency = currency;
        TrialDays = trialDays;
        IsActive = isActive;
        CreatedAt = createdAt;
    }

    public BillingOfferId Id { get; }

    /// <summary>The entitlement plan this offer sells.</summary>
    public PlanId PlanId { get; }

    public OfferCode Code { get; }

    public BillingInterval Interval { get; }

    /// <summary>The price, in the currency's smallest unit. Stored, not computed.</summary>
    private long AmountMinorUnits { get; }

    public BillingCurrency Currency { get; }

    public Money Amount => Money.Of(AmountMinorUnits, Currency);

    /// <summary>
    /// Free days before the first charge, or null for no trial.
    /// </summary>
    /// <remarks>
    /// Null rather than zero, and there is no default: a trial is something
    /// somebody decided to offer, never something the code supplies because a
    /// field was left blank.
    /// </remarks>
    public int? TrialDays { get; }

    /// <summary>
    /// Whether this offer may still be bought.
    /// </summary>
    /// <remarks>
    /// Withdrawing an offer stops new purchases and changes nothing for the
    /// workspaces already on it. Deleting it instead would orphan their
    /// subscriptions.
    /// </remarks>
    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public static BillingPlanOffer Create(
        PlanId planId,
        OfferCode code,
        BillingInterval interval,
        Money amount,
        int? trialDays,
        DateTimeOffset createdAt)
    {
        if (planId.IsEmpty)
        {
            throw new DomainValidationException("An offer must sell a plan.")
            {
                Code = "billing.offer.invalid",
            };
        }

        if (code.IsEmpty)
        {
            throw new DomainValidationException("An offer needs a code.")
            {
                Code = "billing.offer.invalid",
            };
        }

        if (trialDays is int days and (< 0 or > 365))
        {
            throw new DomainValidationException(
                $"A trial of {days} days is not something this platform offers.")
            {
                Code = "billing.offer.invalid",
            };
        }

        return new BillingPlanOffer(
            BillingOfferId.New(),
            planId,
            code,
            interval,
            amount.MinorUnits,
            amount.Currency,
            trialDays,
            isActive: true,
            createdAt == default
                ? throw new DomainValidationException("An offer must record when it was created.")
                    {
                        Code = "billing.offer.invalid",
                    }
                : createdAt.ToUniversalTime());
    }

    /// <summary>Rehydrates a stored offer. Persistence only.</summary>
    public static BillingPlanOffer Restore(
        BillingOfferId id,
        PlanId planId,
        OfferCode code,
        BillingInterval interval,
        long amountMinorUnits,
        BillingCurrency currency,
        int? trialDays,
        bool isActive,
        DateTimeOffset createdAt) =>
        new(id, planId, code, interval, amountMinorUnits, currency, trialDays, isActive, createdAt);

    /// <summary>Takes the offer off sale. Existing subscriptions are untouched.</summary>
    public void Withdraw() => IsActive = false;
}

/// <summary>
/// Which provider price corresponds to one offer.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The browser never sends one of these.</strong> A customer chooses an
/// internal offer; the server looks up what that means to the provider. If the
/// browser could name a price directly it would be choosing what it pays, and a
/// mapping row is what makes that impossible.
/// </para>
/// <para>
/// One row per (offer, provider), so the same offer can be sold through a second
/// provider later without changing anything the customer sees.
/// </para>
/// </remarks>
public sealed class BillingProviderPriceMapping
{
    private BillingProviderPriceMapping(
        BillingOfferId offerId,
        BillingProvider provider,
        ProviderReference externalPriceId,
        DateTimeOffset createdAt)
    {
        OfferId = offerId;
        Provider = provider;
        ExternalPriceId = externalPriceId;
        CreatedAt = createdAt;
    }

    public BillingOfferId OfferId { get; }

    public BillingProvider Provider { get; }

    /// <summary>The provider's own identifier for this price. Opaque here.</summary>
    public ProviderReference ExternalPriceId { get; }

    public DateTimeOffset CreatedAt { get; }

    public static BillingProviderPriceMapping Create(
        BillingOfferId offerId,
        BillingProvider provider,
        ProviderReference externalPriceId,
        DateTimeOffset createdAt)
    {
        if (offerId.IsEmpty)
        {
            throw new DomainValidationException("A price mapping needs an offer.")
            {
                Code = "billing.offer.invalid",
            };
        }

        return externalPriceId.IsEmpty
            ? throw new DomainValidationException("A price mapping needs a provider price.")
                {
                    Code = "billing.offer.invalid",
                }
            : new BillingProviderPriceMapping(
                offerId,
                provider,
                externalPriceId,
                createdAt.ToUniversalTime());
    }

    /// <summary>Rehydrates a stored mapping. Persistence only.</summary>
    public static BillingProviderPriceMapping Restore(
        BillingOfferId offerId,
        BillingProvider provider,
        ProviderReference externalPriceId,
        DateTimeOffset createdAt) =>
        new(offerId, provider, externalPriceId, createdAt);
}
