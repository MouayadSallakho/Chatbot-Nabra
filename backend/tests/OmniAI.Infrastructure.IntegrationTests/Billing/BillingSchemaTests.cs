using Microsoft.EntityFrameworkCore;
using Npgsql;
using OmniAI.Application.Billing;
using OmniAI.Domain.Billing;
using OmniAI.Domain.Plans;
using OmniAI.Domain.Tenants;
using OmniAI.Infrastructure.Billing;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Billing;

/// <summary>
/// What the database refuses, against a real PostgreSQL.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Every rule here is enforced by the database, not by a handler.</strong>
/// An application check is passed by two concurrent requests that both read the
/// old state; a constraint is not. These tests exist because that difference is
/// invisible until the day two requests actually collide, and by then somebody
/// has two subscriptions and one of them needs refunding.
/// </para>
/// <para>
/// The in-memory provider would pass every one of these while enforcing nothing,
/// which is precisely why it is not used.
/// </para>
/// </remarks>
public sealed class BillingSchemaTests(PostgresTestDatabase database)
    : IClassFixture<PostgresTestDatabase>
{
    [Fact]
    public async Task An_offer_code_is_unique()
    {
        await using OmniAiDbContext context = database.CreateContext();

        PlanId planId = await StarterAsync(context);

        context.BillingPlanOffers.Add(Offer(planId, "duplicate-code"));
        await context.SaveChangesAsync();

        context.BillingPlanOffers.Add(Offer(planId, "duplicate-code"));

        await AssertRefusedAsync(context);
    }

    [Fact]
    public async Task A_negative_price_is_impossible()
    {
        // Enforced in SQL rather than only in the value object, because a row can
        // arrive from a migration, a script or a future code path that never
        // touched the constructor.
        await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteScalarAsync(
            """
            INSERT INTO billing_plan_offers
                (id, plan_id, code, "interval", amount_minor_units, currency, is_active, created_at)
            SELECT gen_random_uuid(), id, 'negative', 'Monthly', -1, 'USD', TRUE, NOW()
              FROM plans WHERE code = 'starter'
            """));
    }

    [Fact]
    public async Task An_interval_this_platform_does_not_sell_is_impossible()
    {
        // Weekly, daily or lifetime would reach renewal and display code that has
        // no idea what to do with them.
        await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteScalarAsync(
            """
            INSERT INTO billing_plan_offers
                (id, plan_id, code, "interval", amount_minor_units, currency, is_active, created_at)
            SELECT gen_random_uuid(), id, 'weekly', 'Weekly', 100, 'USD', TRUE, NOW()
              FROM plans WHERE code = 'starter'
            """));
    }

    [Fact]
    public async Task One_provider_price_belongs_to_one_offer()
    {
        await using OmniAiDbContext context = database.CreateContext();

        PlanId planId = await StarterAsync(context);

        BillingPlanOffer first = Offer(planId, "price-owner-first");
        BillingPlanOffer second = Offer(planId, "price-owner-second");

        context.BillingPlanOffers.AddRange(first, second);
        await context.SaveChangesAsync();

        ProviderReference price = ProviderReference.Create("price_shared");

        context.BillingProviderPriceMappings.Add(
            BillingProviderPriceMapping.Create(
                first.Id,
                BillingProvider.Stripe,
                price,
                DateTimeOffset.UnixEpoch));

        await context.SaveChangesAsync();

        // Two offers claiming one price would make the webhook's reverse lookup
        // ambiguous, and the plan somebody is granted would depend on row order.
        context.BillingProviderPriceMappings.Add(
            BillingProviderPriceMapping.Create(
                second.Id,
                BillingProvider.Stripe,
                price,
                DateTimeOffset.UnixEpoch));

        await AssertRefusedAsync(context);
    }

    [Fact]
    public async Task A_workspace_has_at_most_one_billing_customer_per_provider()
    {
        await using OmniAiDbContext context = database.CreateContext();

        TenantId tenantId = await TenantAsync(context, "customer-once");

        context.WorkspaceBillingCustomers.Add(WorkspaceBillingCustomer.Create(
            tenantId,
            BillingProvider.Stripe,
            ProviderReference.Create("cus_first"),
            DateTimeOffset.UnixEpoch));

        await context.SaveChangesAsync();

        // Sent as SQL rather than through the model, so what refuses it is the
        // database. EF's change tracker would reject a second instance of the
        // same key first, and the test would then prove only that EF knows about
        // the key -- not that a second connection is stopped.
        await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteScalarAsync(
            $"""
             INSERT INTO workspace_billing_customers
                 (tenant_id, provider, provider_customer_id, created_at)
             VALUES ('{tenantId.Value}', 'Stripe', 'cus_second', NOW())
             """));
    }

    [Fact]
    public async Task One_provider_customer_belongs_to_one_workspace()
    {
        await using OmniAiDbContext context = database.CreateContext();

        TenantId first = await TenantAsync(context, "customer-share-a");
        TenantId second = await TenantAsync(context, "customer-share-b");

        ProviderReference customer = ProviderReference.Create("cus_shared");

        context.WorkspaceBillingCustomers.Add(
            WorkspaceBillingCustomer.Create(
                first,
                BillingProvider.Stripe,
                customer,
                DateTimeOffset.UnixEpoch));

        await context.SaveChangesAsync();

        // Otherwise one workspace would be paying for another's entitlements, and
        // the portal would show each of them the other's invoices.
        context.WorkspaceBillingCustomers.Add(
            WorkspaceBillingCustomer.Create(
                second,
                BillingProvider.Stripe,
                customer,
                DateTimeOffset.UnixEpoch));

        await AssertRefusedAsync(context);
    }

    /// <remarks>
    /// The structural guarantee this phase rests on. A handler that checked
    /// "does this workspace already have one?" would be passed by two concurrent
    /// checkouts, and the workspace would end up paying twice.
    /// </remarks>
    [Fact]
    public async Task A_workspace_has_at_most_one_live_subscription()
    {
        await using OmniAiDbContext context = database.CreateContext();

        PlanId planId = await StarterAsync(context);
        TenantId tenantId = await TenantAsync(context, "one-live");

        BillingPlanOffer offer = Offer(planId, "one-live-offer");

        context.BillingPlanOffers.Add(offer);
        await context.SaveChangesAsync();

        context.WorkspaceSubscriptions.Add(WorkspaceSubscription.Begin(
            tenantId,
            BillingProvider.Stripe,
            offer.Id,
            planId,
            BillingInterval.Monthly,
            DateTimeOffset.UnixEpoch));

        await context.SaveChangesAsync();

        context.WorkspaceSubscriptions.Add(WorkspaceSubscription.Begin(
            tenantId,
            BillingProvider.Stripe,
            offer.Id,
            planId,
            BillingInterval.Monthly,
            DateTimeOffset.UnixEpoch));

        await AssertRefusedAsync(context);
    }

    /// <remarks>
    /// The index is filtered, so cancelled subscriptions stay for history and a
    /// workspace that cancelled may subscribe again. A plain unique index would
    /// have made "cancel and come back" impossible.
    /// </remarks>
    [Fact]
    public async Task A_cancelled_subscription_does_not_block_a_new_one()
    {
        await using OmniAiDbContext context = database.CreateContext();

        PlanId planId = await StarterAsync(context);
        TenantId tenantId = await TenantAsync(context, "resubscribe");

        BillingPlanOffer offer = Offer(planId, "resubscribe-offer");

        context.BillingPlanOffers.Add(offer);
        await context.SaveChangesAsync();

        WorkspaceSubscription first = WorkspaceSubscription.Begin(
            tenantId,
            BillingProvider.Stripe,
            offer.Id,
            planId,
            BillingInterval.Monthly,
            DateTimeOffset.UnixEpoch);

        first.Apply(
            SubscriptionStatus.Canceled,
            "canceled",
            ProviderReference.Create("sub_gone"),
            currentPeriodStart: null,
            currentPeriodEnd: null,
            trialEndsAt: null,
            cancelAtPeriodEnd: false,
            canceledAt: DateTimeOffset.UnixEpoch,
            TimeSpan.FromDays(7),
            DateTimeOffset.UnixEpoch);

        context.WorkspaceSubscriptions.Add(first);
        await context.SaveChangesAsync();

        context.WorkspaceSubscriptions.Add(WorkspaceSubscription.Begin(
            tenantId,
            BillingProvider.Stripe,
            offer.Id,
            planId,
            BillingInterval.Monthly,
            DateTimeOffset.UnixEpoch));

        await context.SaveChangesAsync();

        Assert.Equal(
            2,
            await context.WorkspaceSubscriptions
                .CountAsync(candidate => candidate.TenantId == tenantId));
    }

    [Fact]
    public async Task A_provider_event_is_recorded_once()
    {
        await using OmniAiDbContext context = database.CreateContext();

        context.BillingEvents.Add(Event("evt_unique"));
        await context.SaveChangesAsync();

        // The whole of duplicate protection. A redelivery, whether a second later
        // or concurrently, loses this insert and causes no second transition.
        context.BillingEvents.Add(Event("evt_unique"));

        await AssertRefusedAsync(context);
    }

    [Fact]
    public async Task Two_checkout_attempts_with_one_key_cannot_both_exist()
    {
        await using OmniAiDbContext context = database.CreateContext();

        PlanId planId = await StarterAsync(context);
        TenantId tenantId = await TenantAsync(context, "checkout-key");

        BillingPlanOffer offer = Offer(planId, "checkout-key-offer");

        context.BillingPlanOffers.Add(offer);
        await context.SaveChangesAsync();

        DateTimeOffset at = DateTimeOffset.UnixEpoch;

        context.BillingCheckoutIntents.Add(
            BillingCheckoutIntent.Start(tenantId, BillingProvider.Stripe, offer.Id, at));

        await context.SaveChangesAsync();

        // The same workspace, offer and hour. This is what makes a double click
        // one checkout across every web server rather than one per process.
        context.BillingCheckoutIntents.Add(
            BillingCheckoutIntent.Start(tenantId, BillingProvider.Stripe, offer.Id, at));

        await AssertRefusedAsync(context);
    }

    [Fact]
    public async Task A_subscription_cannot_belong_to_a_workspace_that_does_not_exist()
    {
        await using OmniAiDbContext context = database.CreateContext();

        PlanId planId = await StarterAsync(context);

        BillingPlanOffer offer = Offer(planId, "orphan-offer");

        context.BillingPlanOffers.Add(offer);
        await context.SaveChangesAsync();

        context.WorkspaceSubscriptions.Add(WorkspaceSubscription.Begin(
            TenantId.From(Guid.CreateVersion7()),
            BillingProvider.Stripe,
            offer.Id,
            planId,
            BillingInterval.Monthly,
            DateTimeOffset.UnixEpoch));

        await AssertRefusedAsync(context);
    }

    /// <remarks>
    /// <strong>What is absent is as deliberate as what is present.</strong> This
    /// platform never receives card data, so there is nowhere for it to be stored
    /// even by accident — and a column added later would have to get past this.
    /// </remarks>
    [Fact]
    public async Task No_billing_table_has_anywhere_to_put_a_card()
    {
        object? found = await database.ExecuteScalarAsync(
            """
            SELECT string_agg(table_name || '.' || column_name, ', ')
              FROM information_schema.columns
             WHERE table_schema = 'public'
               AND (table_name LIKE 'billing%' OR table_name LIKE 'workspace_%')
               AND (column_name ILIKE '%card%'
                 OR column_name ILIKE '%cvv%'
                 OR column_name ILIKE '%cvc%'
                 OR column_name ILIKE '%iban%'
                 OR column_name ILIKE '%pan%'
                 OR column_name ILIKE '%payment_method%'
                 OR column_name ILIKE '%account_number%'
                 OR column_name ILIKE '%sort_code%'
                 OR column_name ILIKE '%routing%')
            """);

        Assert.True(found is null or DBNull, $"A billing column looks like payment data: {found}");
    }

    /// <remarks>
    /// The webhook payload is verified and then discarded. A column to keep it in
    /// would turn every redelivery into permanently stored payment data this
    /// product has no use for.
    /// </remarks>
    [Fact]
    public async Task The_webhook_inbox_has_nowhere_to_keep_a_raw_payload()
    {
        object? found = await database.ExecuteScalarAsync(
            """
            SELECT string_agg(column_name, ', ')
              FROM information_schema.columns
             WHERE table_schema = 'public'
               AND table_name = 'billing_events'
               AND (column_name ILIKE '%payload%'
                 OR column_name ILIKE '%body%'
                 OR column_name ILIKE '%raw%'
                 OR data_type IN ('json', 'jsonb'))
            """);

        Assert.True(found is null or DBNull, $"The billing inbox can store a payload: {found}");
    }

    /// <remarks>
    /// Phase 25 is a fixed recurring subscription to a plan. None of these is
    /// part of that, and each would be a whole second product's worth of
    /// obligations arriving through the back door.
    /// </remarks>
    [Theory]
    [InlineData("billing_wallets")]
    [InlineData("billing_credits")]
    [InlineData("billing_transactions")]
    [InlineData("billing_invoices")]
    [InlineData("billing_payments")]
    [InlineData("billing_usage_records")]
    [InlineData("billing_meters")]
    [InlineData("billing_tax_rates")]
    public async Task No_table_was_added_for_something_this_phase_does_not_do(string table)
    {
        object? found = await database.ExecuteScalarAsync(
            $"SELECT to_regclass('public.{table}')::text");

        Assert.True(found is null or DBNull, $"{table} exists and should not.");
    }

    /// <remarks>
    /// The migration seeds it, and a deployment naming it in
    /// <c>Billing:FallbackPlanCode</c> must land somewhere finite. A fallback
    /// that granted everything would make not paying an upgrade.
    /// </remarks>
    [Fact]
    public async Task The_seeded_fallback_plan_is_finite()
    {
        await using OmniAiDbContext context = database.CreateContext();

        Plan free = await context.Plans.SingleAsync(plan => plan.Code == PlanCode.Create("free"));

        Assert.False(free.MaxMembers.IsUnlimited);
        Assert.False(free.MaxAiAgents.IsUnlimited);
        Assert.False(free.MaxKnowledgeDocuments.IsUnlimited);
        Assert.False(free.MaxWebChatEndpoints.IsUnlimited);
        Assert.False(free.MaxWhatsAppConnections.IsUnlimited);
        Assert.False(free.MaxMonthlyProviderOperations.IsUnlimited);
    }

    /// <remarks>
    /// Two constraints on one table mean two different situations. Recovering
    /// from a workspace race is right; silently recovering from "this provider
    /// customer belongs to somebody else" would read back nothing and report a
    /// confusing failure, hiding the fact that a provider handed out a reference
    /// another workspace already holds.
    /// </remarks>
    [Fact]
    public async Task A_provider_customer_owned_by_another_workspace_is_not_mistaken_for_a_race()
    {
        await using OmniAiDbContext context = database.CreateContext();

        TenantId owner = await TenantAsync(context, "conflict-owner");
        TenantId newcomer = await TenantAsync(context, "conflict-newcomer");

        ProviderReference shared = ProviderReference.Create("cus_conflict");

        context.WorkspaceBillingCustomers.Add(
            WorkspaceBillingCustomer.Create(
                owner,
                BillingProvider.Stripe,
                shared,
                DateTimeOffset.UnixEpoch));

        await context.SaveChangesAsync();

        WorkspaceBillingCustomerStore store = new(context);

        BillingProviderUnavailableException failure =
            await Assert.ThrowsAsync<BillingProviderUnavailableException>(
                () => store.GetOrAddAsync(
                    WorkspaceBillingCustomer.Create(
                        newcomer,
                        BillingProvider.Stripe,
                        shared,
                        DateTimeOffset.UnixEpoch)));

        Assert.Contains("another workspace", failure.Message, StringComparison.Ordinal);

        // And nothing was written for the newcomer.
        await using OmniAiDbContext after = database.CreateContext();

        Assert.False(await after.WorkspaceBillingCustomers
            .AnyAsync(candidate => candidate.TenantId == newcomer));
    }

    /// <remarks>
    /// The race that <em>is</em> worth recovering from: two first purchases at
    /// once. The loser reads back what the winner wrote rather than leaving the
    /// workspace with two provider customers and two places a card could be
    /// charged.
    /// </remarks>
    [Fact]
    public async Task Two_simultaneous_first_purchases_share_one_customer()
    {
        await using OmniAiDbContext context = database.CreateContext();

        TenantId tenantId = await TenantAsync(context, "race-winner");

        context.WorkspaceBillingCustomers.Add(
            WorkspaceBillingCustomer.Create(
                tenantId,
                BillingProvider.Stripe,
                ProviderReference.Create("cus_race_winner"),
                DateTimeOffset.UnixEpoch));

        await context.SaveChangesAsync();

        await using OmniAiDbContext loser = database.CreateContext();

        WorkspaceBillingCustomer resolved = await new WorkspaceBillingCustomerStore(loser)
            .GetOrAddAsync(WorkspaceBillingCustomer.Create(
                tenantId,
                BillingProvider.Stripe,
                ProviderReference.Create("cus_race_loser"),
                DateTimeOffset.UnixEpoch));

        Assert.Equal("cus_race_winner", resolved.ProviderCustomerId.Value);
    }

    private static BillingPlanOffer Offer(PlanId planId, string code) =>
        BillingPlanOffer.Create(
            planId,
            OfferCode.Create(code),
            BillingInterval.Monthly,
            Money.Of(2900, BillingCurrency.Create("USD")),
            trialDays: null,
            DateTimeOffset.UnixEpoch);

    private static BillingEvent Event(string providerEventId) =>
        BillingEvent.Receive(
            BillingProvider.Stripe,
            ProviderReference.Create(providerEventId),
            "customer.subscription.updated",
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch);

    private static async Task<PlanId> StarterAsync(OmniAiDbContext context) =>
        (await context.Plans.FirstAsync(plan => plan.Code == PlanCode.Create("starter"))).Id;

    private static async Task<TenantId> TenantAsync(OmniAiDbContext context, string slug)
    {
        Tenant tenant = Tenant.Create(
            TenantName.Create($"Workspace {slug}"),
            TenantSlug.Create($"billing-{slug}"));

        context.Tenants.Add(tenant);

        await context.SaveChangesAsync();

        return tenant.Id;
    }

    /// <summary>Asserts that PostgreSQL rejected the pending change.</summary>
    /// <remarks>
    /// The point of every test above: the refusal comes from the database, so it
    /// holds for concurrent writers and for anything that reaches the table by a
    /// route nobody has written yet.
    /// </remarks>
    private static async Task AssertRefusedAsync(OmniAiDbContext context)
    {
        DbUpdateException failure =
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        Assert.IsType<PostgresException>(failure.InnerException);
    }
}
