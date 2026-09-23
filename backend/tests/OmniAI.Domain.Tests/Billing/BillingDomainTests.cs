using OmniAI.Domain.Billing;
using OmniAI.Domain.Plans;
using OmniAI.Domain.Tenants;

namespace OmniAI.Domain.Tests.Billing;

/// <summary>
/// The rules a subscription enforces for itself.
/// </summary>
/// <remarks>
/// No database, no provider, no HTTP. What is under test is the aggregate's own
/// arithmetic — which states grant a plan, when a grace period starts, and what
/// cancellation forecloses — because those are the decisions everything else in
/// this phase is built on top of.
/// </remarks>
public sealed class WorkspaceSubscriptionTests
{
    private static readonly TimeSpan Grace = TimeSpan.FromDays(7);

    private static readonly DateTimeOffset Now =
        new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(SubscriptionStatus.Trialing, true)]
    [InlineData(SubscriptionStatus.Active, true)]
    [InlineData(SubscriptionStatus.PastDue, true)]
    [InlineData(SubscriptionStatus.PendingActivation, false)]
    [InlineData(SubscriptionStatus.Suspended, false)]
    [InlineData(SubscriptionStatus.Canceled, false)]
    public void The_entitlement_policy_is_one_expression(SubscriptionStatus status, bool grants)
    {
        // Trialing grants, or a trial would be a trial of nothing. PastDue
        // grants, because during grace the workspace keeps what it paid for.
        // PendingActivation does not: nobody has paid yet.
        WorkspaceSubscription subscription = Begin();

        subscription.Apply(
            status,
            status.ToString(),
            ProviderReference.Create("sub_policy"),
            currentPeriodStart: null,
            currentPeriodEnd: null,
            trialEndsAt: null,
            cancelAtPeriodEnd: false,
            canceledAt: null,
            Grace,
            Now);

        Assert.Equal(grants, subscription.GrantsPlan);
    }

    [Fact]
    public void Entering_arrears_starts_the_clock_once()
    {
        WorkspaceSubscription subscription = Begin();

        Arrears(subscription, Now);

        DateTimeOffset first = subscription.GraceEndsAt!.Value;

        Assert.Equal(Now.Add(Grace), first);

        // A provider that retries a failed payment daily sends one of these every
        // day. Restarting the clock on each would extend grace indefinitely and a
        // workspace could sit unpaid forever.
        Arrears(subscription, Now.AddDays(3));
        Arrears(subscription, Now.AddDays(5));

        Assert.Equal(first, subscription.GraceEndsAt);
    }

    [Fact]
    public void Recovering_clears_the_deadline()
    {
        WorkspaceSubscription subscription = Begin();

        Arrears(subscription, Now);

        subscription.Apply(
            SubscriptionStatus.Active,
            "active",
            ProviderReference.Create("sub_recovered"),
            currentPeriodStart: null,
            currentPeriodEnd: null,
            trialEndsAt: null,
            cancelAtPeriodEnd: false,
            canceledAt: null,
            Grace,
            Now.AddDays(2));

        Assert.Null(subscription.GraceEndsAt);
    }

    [Fact]
    public void Suspension_waits_for_the_deadline()
    {
        WorkspaceSubscription subscription = Begin();

        Arrears(subscription, Now);

        Assert.False(subscription.Suspend(Now.AddDays(6)));
        Assert.Equal(SubscriptionStatus.PastDue, subscription.Status);

        Assert.True(subscription.Suspend(Now.AddDays(8)));
        Assert.Equal(SubscriptionStatus.Suspended, subscription.Status);
    }

    [Fact]
    public void An_active_subscription_is_never_suspended_for_arrears_it_does_not_have()
    {
        WorkspaceSubscription subscription = Begin();

        subscription.Apply(
            SubscriptionStatus.Active,
            "active",
            ProviderReference.Create("sub_active"),
            currentPeriodStart: null,
            currentPeriodEnd: null,
            trialEndsAt: null,
            cancelAtPeriodEnd: false,
            canceledAt: null,
            Grace,
            Now);

        Assert.False(subscription.Suspend(Now.AddYears(1)));
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
    }

    [Fact]
    public void Cancellation_is_final()
    {
        WorkspaceSubscription subscription = Begin();

        subscription.Apply(
            SubscriptionStatus.Canceled,
            "canceled",
            ProviderReference.Create("sub_gone"),
            currentPeriodStart: null,
            currentPeriodEnd: null,
            trialEndsAt: null,
            cancelAtPeriodEnd: false,
            canceledAt: Now,
            Grace,
            Now);

        // A late event, or a redelivery of an older one. Resurrecting a cancelled
        // subscription would hand back a paid plan nobody is paying for.
        Assert.False(subscription.Apply(
            SubscriptionStatus.Active,
            "active",
            ProviderReference.Create("sub_gone"),
            currentPeriodStart: null,
            currentPeriodEnd: null,
            trialEndsAt: null,
            cancelAtPeriodEnd: false,
            canceledAt: null,
            Grace,
            Now.AddDays(1)));

        Assert.Equal(SubscriptionStatus.Canceled, subscription.Status);
        Assert.False(subscription.GrantsPlan);
    }

    private static WorkspaceSubscription Begin() => WorkspaceSubscription.Begin(
        TenantId.New(),
        BillingProvider.Stripe,
        BillingOfferId.New(),
        PlanId.New(),
        BillingInterval.Monthly,
        Now);

    private static void Arrears(WorkspaceSubscription subscription, DateTimeOffset at) =>
        subscription.Apply(
            SubscriptionStatus.PastDue,
            "past_due",
            ProviderReference.Create("sub_arrears"),
            currentPeriodStart: null,
            currentPeriodEnd: null,
            trialEndsAt: null,
            cancelAtPeriodEnd: false,
            canceledAt: null,
            Grace,
            at);
}

/// <summary>
/// Money, as this platform refuses to get it wrong.
/// </summary>
public sealed class BillingMoneyTests
{
    [Fact]
    public void An_amount_is_a_whole_number_of_minor_units()
    {
        // 19.99 has no exact binary floating-point representation. Storing 1999
        // is exact, and happens to be what every payment provider expects.
        Money money = Money.Of(1999, BillingCurrency.Create("usd"));

        Assert.Equal(1999, money.MinorUnits);
        Assert.Equal("USD", money.Currency.Value);
    }

    [Fact]
    public void A_negative_amount_is_refused()
    {
        Assert.Throws<DomainValidationException>(
            () => Money.Of(-1, BillingCurrency.Create("USD")));
    }

    [Theory]
    [InlineData("US")]
    [InlineData("USDD")]
    [InlineData("US1")]
    [InlineData("")]
    [InlineData(null)]
    public void A_currency_that_is_not_an_iso_code_is_refused(string? value)
    {
        Assert.Throws<DomainValidationException>(() => BillingCurrency.Create(value));
    }
}

/// <summary>
/// The checkout attempt that makes a second click harmless.
/// </summary>
public sealed class BillingCheckoutIntentTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 12, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Two_attempts_in_one_hour_share_a_key()
    {
        TenantId tenantId = TenantId.New();
        BillingOfferId offerId = BillingOfferId.New();

        BillingCheckoutIntent first =
            BillingCheckoutIntent.Start(tenantId, BillingProvider.Stripe, offerId, Now);

        BillingCheckoutIntent second = BillingCheckoutIntent.Start(
            tenantId,
            BillingProvider.Stripe,
            offerId,
            Now.AddMinutes(20));

        // The key is what the unique index collides on. Derived from the
        // workspace, the offer and the hour -- never from anything the browser
        // sent, which a client could vary to get a second checkout.
        Assert.Equal(first.IdempotencyKey, second.IdempotencyKey);
    }

    [Fact]
    public void A_different_workspace_or_offer_gets_its_own_key()
    {
        BillingOfferId offerId = BillingOfferId.New();

        BillingCheckoutIntent mine =
            BillingCheckoutIntent.Start(TenantId.New(), BillingProvider.Stripe, offerId, Now);

        BillingCheckoutIntent theirs =
            BillingCheckoutIntent.Start(TenantId.New(), BillingProvider.Stripe, offerId, Now);

        Assert.NotEqual(mine.IdempotencyKey, theirs.IdempotencyKey);
    }

    [Fact]
    public void A_completed_attempt_cannot_be_reused()
    {
        BillingCheckoutIntent intent = BillingCheckoutIntent.Start(
            TenantId.New(),
            BillingProvider.Stripe,
            BillingOfferId.New(),
            Now);

        intent.Started(ProviderReference.Create("cs_test_1"));

        Assert.True(intent.IsUsableAt(Now));
        Assert.True(intent.Complete(Now.AddMinutes(5)));

        // A stale browser tab holding the old URL must not be able to start a
        // second subscription against a purchase that already happened.
        Assert.False(intent.IsUsableAt(Now.AddMinutes(6)));
        Assert.False(intent.Complete(Now.AddMinutes(7)));
    }

    [Fact]
    public void An_attempt_expires()
    {
        BillingCheckoutIntent intent = BillingCheckoutIntent.Start(
            TenantId.New(),
            BillingProvider.Stripe,
            BillingOfferId.New(),
            Now);

        Assert.False(intent.IsUsableAt(Now.Add(BillingCheckoutIntent.Lifetime).AddSeconds(1)));
    }
}

/// <summary>
/// The value types that keep provider data opaque.
/// </summary>
public sealed class BillingIdentityTests
{
    [Fact]
    public void An_offer_code_is_a_stable_lowercase_handle()
    {
        Assert.Equal("starter-monthly", OfferCode.Create("  Starter-Monthly ").Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void A_blank_offer_code_is_refused(string? value)
    {
        Assert.Throws<DomainValidationException>(() => OfferCode.Create(value));
    }

    [Fact]
    public void A_provider_reference_is_kept_exactly_as_given()
    {
        // Opaque on purpose: whatever a provider calls its own records is its
        // business, and parsing meaning out of one would break the day they
        // change the format.
        Assert.Equal("cs_test_a1B2c3", ProviderReference.Create(" cs_test_a1B2c3 ").Value);
    }

    [Fact]
    public void A_blank_provider_reference_reads_as_absent_rather_than_empty()
    {
        // "The provider has not told us yet" is a different fact from "the
        // provider told us nothing", and only one of them is a valid reference.
        Assert.Null(ProviderReference.CreateOrNull("   "));
        Assert.Throws<DomainValidationException>(() => ProviderReference.Create("   "));
    }
}

/// <summary>
/// The durable record of a verified provider event.
/// </summary>
public sealed class BillingEventTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 13, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_refusal_is_recorded_without_the_provider_s_own_words()
    {
        BillingEvent stored = Receive();

        stored.Unprocessable(new string('x', 900), Now);

        // Bounded, so a long provider message cannot become an unbounded column
        // write, and so a log line built from it stays a log line.
        Assert.Equal(400, stored.Failure!.Length);
        Assert.Equal(BillingEventStatus.Unprocessable, stored.Status);
    }

    [Fact]
    public void Being_processed_clears_an_earlier_failure()
    {
        BillingEvent stored = Receive();

        stored.Retry("the provider did not answer");

        Assert.Equal(BillingEventStatus.Pending, stored.Status);
        Assert.Equal(1, stored.Attempts);

        stored.Processed(Now);

        Assert.Null(stored.Failure);
        Assert.Equal(Now, stored.ProcessedAt);
    }

    [Fact]
    public void An_event_with_no_type_is_refused()
    {
        Assert.Throws<DomainValidationException>(() => BillingEvent.Receive(
            BillingProvider.Stripe,
            ProviderReference.Create("evt_1"),
            "   ",
            Now,
            Now));
    }

    private static BillingEvent Receive() => BillingEvent.Receive(
        BillingProvider.Stripe,
        ProviderReference.Create("evt_1"),
        "customer.subscription.updated",
        Now,
        Now);
}
