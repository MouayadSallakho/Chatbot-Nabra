using Microsoft.EntityFrameworkCore;
using OmniAI.Application.Billing;
using OmniAI.Domain.Billing;
using OmniAI.Domain.Plans;
using OmniAI.Domain.Tenants;
using OmniAI.Infrastructure.Billing;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Infrastructure.Plans;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Billing;

/// <summary>
/// Subscription state becoming entitlement, against a real PostgreSQL.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Time is supplied, never observed.</strong> Grace is measured in days,
/// and a suite that waited for one would be untestable. The clock is a value
/// these tests move, which is the only way to assert on a seven-day window in a
/// second.
/// </para>
/// <para>
/// <strong>Every assertion ends at the plan assignment.</strong> A subscription
/// row is bookkeeping; the assignment is what every entitlement check in the
/// platform actually reads.
/// </para>
/// </remarks>
public sealed class BillingSubscriptionServiceTests(PostgresTestDatabase database)
    : IClassFixture<PostgresTestDatabase>
{
    private static readonly TimeSpan Grace = TimeSpan.FromDays(7);

    private readonly FixedTimeProvider _clock = new(new DateTimeOffset(2026, 9, 22, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task A_completed_checkout_starts_a_subscription_and_grants_its_plan()
    {
        Fixture fixture = await ArrangeAsync("activate");

        BillingApplyOutcome outcome = await ApplyAsync(fixture, SubscriptionStatus.Active, "active", mayBegin: true);

        Assert.Equal(BillingApplyOutcome.Applied, outcome);
        Assert.Equal("starter", await PlanCodeAsync(fixture.TenantId));
    }

    /// <remarks>
    /// Without this, anybody who could name a provider subscription could grant a
    /// workspace a plan by sending one signed event.
    /// </remarks>
    [Fact]
    public async Task An_event_for_a_workspace_that_bought_nothing_creates_nothing()
    {
        Fixture fixture = await ArrangeAsync("no-subscription");

        BillingApplyOutcome outcome =
            await ApplyAsync(fixture, SubscriptionStatus.Active, "active", mayBegin: false);

        Assert.Equal(BillingApplyOutcome.NoSubscription, outcome);

        await using OmniAiDbContext context = database.CreateContext();

        Assert.False(await context.WorkspaceSubscriptions
            .AnyAsync(candidate => candidate.TenantId == fixture.TenantId));

        Assert.Equal("development", await PlanCodeAsync(fixture.TenantId));
    }

    /// <remarks>
    /// The rule that must never bend: an unrecognised price grants nothing. Not
    /// the nearest plan, not one named in provider metadata, and never an
    /// unlimited one.
    /// </remarks>
    [Fact]
    public async Task A_price_this_platform_does_not_sell_grants_nothing()
    {
        Fixture fixture = await ArrangeAsync("unknown-price");

        BillingApplyOutcome outcome = await ApplyAsync(
            fixture,
            SubscriptionStatus.Active,
            "active",
            mayBegin: true,
            priceId: ProviderReference.Create("price_never_seen"));

        Assert.Equal(BillingApplyOutcome.UnknownPrice, outcome);

        await using OmniAiDbContext context = database.CreateContext();

        Assert.False(await context.WorkspaceSubscriptions
            .AnyAsync(candidate => candidate.TenantId == fixture.TenantId));

        Assert.Equal("development", await PlanCodeAsync(fixture.TenantId));
    }

    [Fact]
    public async Task A_trialing_subscription_grants_its_plan()
    {
        Fixture fixture = await ArrangeAsync("trialing");

        await ApplyAsync(fixture, SubscriptionStatus.Trialing, "trialing", mayBegin: true);

        // A trial that granted nothing would be a trial of nothing.
        Assert.Equal("starter", await PlanCodeAsync(fixture.TenantId));
    }

    [Fact]
    public async Task A_workspace_in_arrears_keeps_its_plan_until_grace_runs_out()
    {
        Fixture fixture = await ArrangeAsync("grace");

        await ApplyAsync(fixture, SubscriptionStatus.Active, "active", mayBegin: true);
        await ApplyAsync(fixture, SubscriptionStatus.PastDue, "past_due");

        // Still theirs. A card that expired on a Friday must not take a business
        // offline before anybody has had the chance to notice.
        Assert.Equal("starter", await PlanCodeAsync(fixture.TenantId));

        _clock.Advance(TimeSpan.FromDays(3));

        Assert.False(await SuspendAsync(fixture.TenantId));
        Assert.Equal("starter", await PlanCodeAsync(fixture.TenantId));

        _clock.Advance(TimeSpan.FromDays(5));

        Assert.True(await SuspendAsync(fixture.TenantId));
        Assert.Equal("free", await PlanCodeAsync(fixture.TenantId));
    }

    /// <remarks>
    /// A provider that retries a failed payment daily would otherwise extend the
    /// grace period indefinitely, and a workspace could sit unpaid forever.
    /// </remarks>
    [Fact]
    public async Task Repeated_arrears_events_do_not_extend_the_grace_period()
    {
        Fixture fixture = await ArrangeAsync("grace-extension");

        await ApplyAsync(fixture, SubscriptionStatus.Active, "active", mayBegin: true);
        await ApplyAsync(fixture, SubscriptionStatus.PastDue, "past_due");

        DateTimeOffset firstDeadline = await GraceEndsAtAsync(fixture.TenantId);

        for (int day = 0; day < 6; day++)
        {
            _clock.Advance(TimeSpan.FromDays(1));

            await ApplyAsync(fixture, SubscriptionStatus.PastDue, "past_due");
        }

        Assert.Equal(firstDeadline, await GraceEndsAtAsync(fixture.TenantId));

        _clock.Advance(TimeSpan.FromDays(2));

        Assert.True(await SuspendAsync(fixture.TenantId));
    }

    [Fact]
    public async Task A_payment_during_grace_restores_the_subscription_and_clears_the_deadline()
    {
        Fixture fixture = await ArrangeAsync("recovered");

        await ApplyAsync(fixture, SubscriptionStatus.Active, "active", mayBegin: true);
        await ApplyAsync(fixture, SubscriptionStatus.PastDue, "past_due");

        _clock.Advance(TimeSpan.FromDays(2));

        await ApplyAsync(fixture, SubscriptionStatus.Active, "active");

        await using OmniAiDbContext context = database.CreateContext();

        WorkspaceSubscription subscription = await context.WorkspaceSubscriptions
            .SingleAsync(candidate => candidate.TenantId == fixture.TenantId);

        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.Null(subscription.GraceEndsAt);

        // And the sweeper, re-checking under the lock, declines to suspend them.
        _clock.Advance(TimeSpan.FromDays(30));

        Assert.False(await SuspendAsync(fixture.TenantId));
        Assert.Equal("starter", await PlanCodeAsync(fixture.TenantId));
    }

    [Fact]
    public async Task A_cancelled_subscription_never_comes_back()
    {
        Fixture fixture = await ArrangeAsync("terminal");

        await ApplyAsync(fixture, SubscriptionStatus.Active, "active", mayBegin: true);
        await ApplyAsync(fixture, SubscriptionStatus.Canceled, "canceled");

        Assert.Equal("free", await PlanCodeAsync(fixture.TenantId));

        // A late event, or a redelivery of an older one. Terminal is terminal:
        // resurrecting a cancelled subscription would hand back a paid plan
        // nobody is paying for.
        await ApplyAsync(fixture, SubscriptionStatus.Active, "active");

        Assert.Equal("free", await PlanCodeAsync(fixture.TenantId));
    }

    [Fact]
    public async Task A_plan_change_in_the_provider_portal_moves_the_workspace()
    {
        Fixture fixture = await ArrangeAsync("moved", withSecondOffer: true);

        await ApplyAsync(fixture, SubscriptionStatus.Active, "active", mayBegin: true);

        Assert.Equal("starter", await PlanCodeAsync(fixture.TenantId));

        // The customer switched to the annual offer, which this fixture mapped to
        // the free plan so the move is visible.
        await ApplyAsync(
            fixture,
            SubscriptionStatus.Active,
            "active",
            priceId: fixture.SecondPriceId);

        await using OmniAiDbContext context = database.CreateContext();

        WorkspaceSubscription subscription = await context.WorkspaceSubscriptions
            .SingleAsync(candidate => candidate.TenantId == fixture.TenantId);

        Assert.Equal(BillingInterval.Annual, subscription.Interval);
        Assert.Equal("free", await PlanCodeAsync(fixture.TenantId));
    }

    [Fact]
    public async Task One_workspace_s_subscription_never_reaches_another()
    {
        Fixture first = await ArrangeAsync("isolation-a");
        Fixture second = await ArrangeAsync("isolation-b");

        await ApplyAsync(first, SubscriptionStatus.Active, "active", mayBegin: true);

        Assert.Equal("starter", await PlanCodeAsync(first.TenantId));
        Assert.Equal("development", await PlanCodeAsync(second.TenantId));

        await using OmniAiDbContext context = database.CreateContext();

        Assert.False(await context.WorkspaceSubscriptions
            .AnyAsync(candidate => candidate.TenantId == second.TenantId));
    }

    /// <remarks>
    /// Phase 24's monthly provider-operation quota is counted from Phase 23's
    /// usage rows, and a plan change moves the limit rather than the counter. A
    /// downgrade must not reset what a workspace has already consumed this month.
    /// </remarks>
    [Fact]
    public async Task A_downgrade_moves_the_limit_and_leaves_the_month_s_usage_alone()
    {
        Fixture fixture = await ArrangeAsync("quota");

        await ApplyAsync(fixture, SubscriptionStatus.Active, "active", mayBegin: true);

        await using (OmniAiDbContext context = database.CreateContext())
        {
            Assert.Equal("starter", await PlanCodeAsync(fixture.TenantId));
        }

        await ApplyAsync(fixture, SubscriptionStatus.Canceled, "canceled");

        await using OmniAiDbContext after = database.CreateContext();

        Plan free = await after.Plans.SingleAsync(plan => plan.Code == PlanCode.Create("free"));

        Assert.Equal("free", await PlanCodeAsync(fixture.TenantId));

        // The limit is finite and lower, and nothing in the usage tables was
        // touched by the downgrade.
        Assert.False(free.MaxMonthlyProviderOperations.IsUnlimited);
        Assert.Equal(0, await after.AiProviderUsages.CountAsync());
    }

    private async Task<BillingApplyOutcome> ApplyAsync(
        Fixture fixture,
        SubscriptionStatus status,
        string rawStatus,
        bool mayBegin = false,
        ProviderReference? priceId = null)
    {
        await using OmniAiDbContext context = database.CreateContext();

        return await ServiceFor(context).ApplyAsync(
            fixture.TenantId,
            BillingProvider.Stripe,
            new ProviderSubscriptionSnapshot(
                fixture.SubscriptionId,
                fixture.CustomerId,
                priceId ?? fixture.PriceId,
                status,
                rawStatus,
                _clock.GetUtcNow().AddDays(-1),
                _clock.GetUtcNow().AddDays(29),
                TrialEndsAt: null,
                CancelAtPeriodEnd: false,
                CanceledAt: status == SubscriptionStatus.Canceled ? _clock.GetUtcNow() : null),
            mayBegin);
    }

    private async Task<bool> SuspendAsync(TenantId tenantId)
    {
        await using OmniAiDbContext context = database.CreateContext();

        return await ServiceFor(context).SuspendAsync(tenantId);
    }

    private BillingSubscriptionService ServiceFor(OmniAiDbContext context)
    {
        BillingOptions options = new()
        {
            FallbackPlanCode = "free",
            ReturnBaseUrl = "https://app.test.local",
            PastDueGracePeriod = Grace,
        };

        PlanCatalog catalog = new(context);

        return new BillingSubscriptionService(
            context,
            new BillingCatalog(context),
            new FallbackPlanResolver(catalog, options),
            options,
            _clock);
    }

    private async Task<string> PlanCodeAsync(TenantId tenantId)
    {
        await using OmniAiDbContext context = database.CreateContext();

        WorkspacePlanAssignment assignment = await context.WorkspacePlanAssignments
            .SingleAsync(candidate => candidate.TenantId == tenantId);

        Plan plan = await context.Plans.SingleAsync(candidate => candidate.Id == assignment.PlanId);

        return plan.Code.Value;
    }

    private async Task<DateTimeOffset> GraceEndsAtAsync(TenantId tenantId)
    {
        await using OmniAiDbContext context = database.CreateContext();

        WorkspaceSubscription subscription = await context.WorkspaceSubscriptions
            .SingleAsync(candidate => candidate.TenantId == tenantId);

        return subscription.GraceEndsAt
            ?? throw new InvalidOperationException("The subscription has no grace deadline.");
    }

    /// <summary>A workspace on the development plan, with offers it could buy.</summary>
    private async Task<Fixture> ArrangeAsync(string slug, bool withSecondOffer = false)
    {
        await using OmniAiDbContext context = database.CreateContext();

        Plan starter = await context.Plans.SingleAsync(plan => plan.Code == PlanCode.Create("starter"));
        Plan free = await context.Plans.SingleAsync(plan => plan.Code == PlanCode.Create("free"));
        Plan development =
            await context.Plans.SingleAsync(plan => plan.Code == PlanCode.Create("development"));

        Tenant tenant = Tenant.Create(
            TenantName.Create($"Workspace {slug}"),
            TenantSlug.Create($"svc-{slug}"));

        context.Tenants.Add(tenant);

        context.WorkspacePlanAssignments.Add(
            WorkspacePlanAssignment.Create(tenant.Id, development.Id, _clock.GetUtcNow()));

        BillingPlanOffer offer = BillingPlanOffer.Create(
            starter.Id,
            OfferCode.Create($"svc-{slug}-monthly"),
            BillingInterval.Monthly,
            Money.Of(2900, BillingCurrency.Create("USD")),
            trialDays: null,
            _clock.GetUtcNow());

        ProviderReference priceId = ProviderReference.Create($"price_svc_{slug}");

        context.BillingPlanOffers.Add(offer);
        context.BillingProviderPriceMappings.Add(
            BillingProviderPriceMapping.Create(
                offer.Id,
                BillingProvider.Stripe,
                priceId,
                _clock.GetUtcNow()));

        ProviderReference secondPriceId = ProviderReference.Create($"price_svc_{slug}_annual");

        if (withSecondOffer)
        {
            // Mapped to the free plan on purpose, so that moving between offers
            // is visible in the assignment rather than only in the interval.
            BillingPlanOffer annual = BillingPlanOffer.Create(
                free.Id,
                OfferCode.Create($"svc-{slug}-annual"),
                BillingInterval.Annual,
                Money.Of(29000, BillingCurrency.Create("USD")),
                trialDays: null,
                _clock.GetUtcNow());

            context.BillingPlanOffers.Add(annual);
            context.BillingProviderPriceMappings.Add(
                BillingProviderPriceMapping.Create(
                    annual.Id,
                    BillingProvider.Stripe,
                    secondPriceId,
                    _clock.GetUtcNow()));
        }

        await context.SaveChangesAsync();

        return new Fixture(
            tenant.Id,
            priceId,
            secondPriceId,
            ProviderReference.Create($"sub_svc_{slug}"),
            ProviderReference.Create($"cus_svc_{slug}"));
    }

    private sealed record Fixture(
        TenantId TenantId,
        ProviderReference PriceId,
        ProviderReference SecondPriceId,
        ProviderReference SubscriptionId,
        ProviderReference CustomerId);
}
