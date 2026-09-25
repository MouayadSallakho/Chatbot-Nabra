using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OmniAI.Domain.Billing;
using OmniAI.Domain.Plans;
using OmniAI.Domain.Tenants;
using OmniAI.Infrastructure.Persistence;

namespace OmniAI.Api.IntegrationTests.Billing;

/// <summary>
/// A subscription's whole life, through the real pipeline.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Everything moves because a verified event said to go and look.</strong>
/// No test here calls an internal method to change a status: each drives the
/// provider double, sends a signed webhook and waits for the platform to catch
/// up, which is what a deployment does.
/// </para>
/// <para>
/// <strong>Each assertion checks the plan assignment, not just the
/// subscription.</strong> Phase 24's assignment is what every entitlement check
/// reads, so a subscription that says Active while the assignment says otherwise
/// is the failure that matters.
/// </para>
/// </remarks>
public sealed class BillingLifecycleApiTests(BillingApiFixture api) : IClassFixture<BillingApiFixture>
{
    private const string Route = "/api/billing/webhook";

    [Fact]
    public async Task A_completed_checkout_activates_the_subscription_and_assigns_its_plan()
    {
        await api.SeedOffersAsync();

        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        ProviderReference sessionId = await StartCheckoutAsync(owner, tenantId);

        ProviderReference subscriptionId =
            api.Provider.CompleteCheckout(sessionId, api.MonthlyPriceId);

        await DeliverAsync(
            "checkout.session.completed",
            BillingApiFixture.CheckoutPayload(sessionId));

        await WaitForStatusAsync(tenantId, SubscriptionStatus.Active);

        // The intent is settled by the save after the subscription's, so the
        // subscription turning Active is not yet proof the intent has. Waited for
        // rather than read at once: reading between the two saves is a race in
        // the test (seen once in Phase 33), not a defect in the application.
        await BillingApiFixture.WaitAsync(async () =>
        {
            await using OmniAiDbContext settling = api.CreateDbContext();

            return await settling.BillingCheckoutIntents.AnyAsync(candidate =>
                candidate.TenantId == TenantId.From(tenantId)
                && candidate.Status == CheckoutIntentStatus.Completed);
        });

        await using OmniAiDbContext context = api.CreateDbContext();

        WorkspaceSubscription subscription = await context.WorkspaceSubscriptions
            .SingleAsync(candidate => candidate.TenantId == TenantId.From(tenantId));

        Assert.Equal(subscriptionId, subscription.ProviderSubscriptionId);
        Assert.Equal(BillingInterval.Monthly, subscription.Interval);

        Assert.Equal("starter", await PlanCodeAsync(tenantId));

        // The checkout attempt is settled, so a stale browser tab cannot start a
        // second purchase against it.
        BillingCheckoutIntent intent = await context.BillingCheckoutIntents
            .SingleAsync(candidate => candidate.TenantId == TenantId.From(tenantId));

        Assert.Equal(CheckoutIntentStatus.Completed, intent.Status);

        // And the screen now says what is being paid for, still without a single
        // provider identifier on it.
        using HttpResponseMessage screen =
            await owner.GetAsync($"/api/tenants/{tenantId}/billing");

        string body = await screen.Content.ReadAsStringAsync();

        Assert.DoesNotContain("sub_test", body, StringComparison.Ordinal);

        using JsonDocument parsed = JsonDocument.Parse(body);

        Assert.Equal(
            "Active",
            parsed.RootElement.GetProperty("subscription").GetProperty("status").GetString());

        Assert.False(parsed.RootElement.GetProperty("canPurchase").GetBoolean());
    }

    [Fact]
    public async Task A_second_purchase_is_refused_while_one_subscription_is_live()
    {
        await api.SeedOffersAsync();

        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        await ActivateAsync(owner, tenantId);

        using HttpResponseMessage response = await owner.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/billing/checkout",
            new { offerId = api.Offers[BillingApiFixture.AnnualOfferCode] });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(
            "billing.checkout.alreadysubscribed",
            body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Arrears_keep_the_plan_during_grace_and_cancellation_falls_back()
    {
        await api.SeedOffersAsync();

        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        ProviderReference subscriptionId = await ActivateAsync(owner, tenantId);

        // The payment failed. During grace the workspace keeps what it paid for:
        // cutting somebody off the instant a card expires would punish the
        // commonest and most innocent failure there is.
        api.Provider.SetSubscription(
            subscriptionId,
            ProviderReference.Create("cus_test_grace"),
            api.MonthlyPriceId,
            SubscriptionStatus.PastDue,
            "past_due");

        await DeliverAsync(
            "invoice.payment_failed",
            BillingApiFixture.SubscriptionPayload(subscriptionId, "past_due"));

        await WaitForStatusAsync(tenantId, SubscriptionStatus.PastDue);

        Assert.Equal("starter", await PlanCodeAsync(tenantId));

        await using (OmniAiDbContext during = api.CreateDbContext())
        {
            WorkspaceSubscription subscription = await during.WorkspaceSubscriptions
                .SingleAsync(candidate => candidate.TenantId == TenantId.From(tenantId));

            Assert.NotNull(subscription.GraceEndsAt);
        }

        // Cancelled outright. Now the workspace falls back -- to a finite plan,
        // never to an unlimited one.
        api.Provider.SetSubscription(
            subscriptionId,
            ProviderReference.Create("cus_test_grace"),
            api.MonthlyPriceId,
            SubscriptionStatus.Canceled,
            "canceled",
            canceledAt: DateTimeOffset.UtcNow);

        await DeliverAsync(
            "customer.subscription.deleted",
            BillingApiFixture.SubscriptionPayload(subscriptionId, "canceled"));

        await WaitForStatusAsync(tenantId, SubscriptionStatus.Canceled);

        Assert.Equal(BillingApiFixture.FallbackPlanCode, await PlanCodeAsync(tenantId));

        await using OmniAiDbContext after = api.CreateDbContext();

        Plan fallback = await after.Plans.SingleAsync(
            plan => plan.Code == PlanCode.Create(BillingApiFixture.FallbackPlanCode));

        // The single most important property of the fallback: it is finite.
        Assert.False(fallback.MaxMembers.IsUnlimited);
        Assert.False(fallback.MaxAiAgents.IsUnlimited);
    }

    /// <remarks>
    /// A downgrade lowers what may be created. It does not remove anything, which
    /// is why a workspace that falls back still has every member and agent it
    /// had — and is simply refused the next one.
    /// </remarks>
    [Fact]
    public async Task A_downgrade_destroys_nothing_and_only_refuses_new_creation()
    {
        await api.SeedOffersAsync();

        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        ProviderReference subscriptionId = await ActivateAsync(owner, tenantId);

        // Two agents, which the starter plan allows and the free plan does not.
        for (int index = 0; index < 2; index++)
        {
            using HttpResponseMessage created = await owner.PostAsJsonAsync(
                $"/api/tenants/{tenantId}/ai-agents",
                new { name = $"Agent {index}", instructions = "Be helpful." });

            created.EnsureSuccessStatusCode();
        }

        api.Provider.SetSubscription(
            subscriptionId,
            ProviderReference.Create("cus_test_downgrade"),
            api.MonthlyPriceId,
            SubscriptionStatus.Canceled,
            "canceled",
            canceledAt: DateTimeOffset.UtcNow);

        await DeliverAsync(
            "customer.subscription.deleted",
            BillingApiFixture.SubscriptionPayload(subscriptionId, "canceled"));

        await WaitForStatusAsync(tenantId, SubscriptionStatus.Canceled);

        // Both agents are still there. Deleting somebody's work because their
        // card expired would be indefensible.
        using HttpResponseMessage list = await owner.GetAsync($"/api/tenants/{tenantId}/ai-agents");

        using JsonDocument agents = JsonDocument.Parse(await list.Content.ReadAsStringAsync());

        Assert.Equal(2, agents.RootElement.GetProperty("items").GetArrayLength());

        // And the next one is refused, by Phase 24 rather than by anything added
        // here.
        using HttpResponseMessage refused = await owner.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/ai-agents",
            new { name = "One too many", instructions = "Be helpful." });

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);

        using JsonDocument problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync());

        Assert.Equal(
            "entitlement.aiagents.exhausted",
            problem.RootElement.GetProperty("code").GetString());
    }

    /// <remarks>
    /// Events arrive out of order in practice. A stale one must not regress a
    /// subscription that has since recovered, which it cannot do here because
    /// nothing acts on an event's contents — the provider is asked what is true
    /// now.
    /// </remarks>
    [Fact]
    public async Task A_late_arrears_event_does_not_regress_a_recovered_subscription()
    {
        await api.SeedOffersAsync();

        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        ProviderReference subscriptionId = await ActivateAsync(owner, tenantId);

        // The customer has already paid; the provider says Active. Only now does
        // the earlier failure event arrive.
        api.Provider.SetSubscription(
            subscriptionId,
            ProviderReference.Create("cus_test_late"),
            api.MonthlyPriceId,
            SubscriptionStatus.Active,
            "active");

        await DeliverAsync(
            "invoice.payment_failed",
            BillingApiFixture.SubscriptionPayload(subscriptionId, "past_due"),
            eventId: $"evt_late_{Guid.NewGuid():N}");

        await BillingApiFixture.WaitAsync(async () =>
        {
            await using OmniAiDbContext context = api.CreateDbContext();

            return await context.BillingEvents.AnyAsync(stored =>
                stored.Status != BillingEventStatus.Pending
                && stored.Status != BillingEventStatus.Claimed);
        });

        await using OmniAiDbContext after = api.CreateDbContext();

        WorkspaceSubscription subscription = await after.WorkspaceSubscriptions
            .SingleAsync(candidate => candidate.TenantId == TenantId.From(tenantId));

        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.Null(subscription.GraceEndsAt);
        Assert.Equal("starter", await PlanCodeAsync(tenantId));
    }

    [Fact]
    public async Task One_workspace_s_subscription_does_not_reach_another()
    {
        await api.SeedOffersAsync();

        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();
        (_, Guid otherTenantId, _) = await api.CreateOwnerAsync("Rival Trading");

        await ActivateAsync(owner, tenantId);

        await using OmniAiDbContext context = api.CreateDbContext();

        // The second workspace bought nothing and is exactly where onboarding
        // left it.
        Assert.False(await context.WorkspaceSubscriptions
            .AnyAsync(candidate => candidate.TenantId == TenantId.From(otherTenantId)));

        Assert.Equal("development", await PlanCodeAsync(otherTenantId));
    }

    /// <remarks>
    /// Workspaces created before this phase have no subscription and must be
    /// entirely unaffected by it. Nothing fabricates one from their existence.
    /// </remarks>
    [Fact]
    public async Task A_workspace_that_never_bought_anything_has_no_subscription()
    {
        await api.SeedOffersAsync();

        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        await using OmniAiDbContext context = api.CreateDbContext();

        Assert.False(await context.WorkspaceSubscriptions
            .AnyAsync(candidate => candidate.TenantId == TenantId.From(tenantId)));

        using HttpResponseMessage screen =
            await owner.GetAsync($"/api/tenants/{tenantId}/billing");

        using JsonDocument body = JsonDocument.Parse(await screen.Content.ReadAsStringAsync());

        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("subscription").ValueKind);

        // And its plan is untouched, so everything it could do before it still
        // can.
        Assert.Equal("development", await PlanCodeAsync(tenantId));
    }

    private async Task<ProviderReference> ActivateAsync(HttpClient owner, Guid tenantId)
    {
        ProviderReference sessionId = await StartCheckoutAsync(owner, tenantId);

        ProviderReference subscriptionId =
            api.Provider.CompleteCheckout(sessionId, api.MonthlyPriceId);

        await DeliverAsync(
            "checkout.session.completed",
            BillingApiFixture.CheckoutPayload(sessionId));

        await WaitForStatusAsync(tenantId, SubscriptionStatus.Active);

        return subscriptionId;
    }

    private async Task<ProviderReference> StartCheckoutAsync(HttpClient owner, Guid tenantId)
    {
        using HttpResponseMessage response = await owner.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/billing/checkout",
            new { offerId = api.Offers[BillingApiFixture.MonthlyOfferCode] });

        response.EnsureSuccessStatusCode();

        await using OmniAiDbContext context = api.CreateDbContext();

        BillingCheckoutIntent intent = await context.BillingCheckoutIntents
            .SingleAsync(candidate => candidate.TenantId == TenantId.From(tenantId));

        return intent.ProviderSessionId!.Value;
    }

    private async Task DeliverAsync(string eventType, string payload, string? eventId = null)
    {
        using HttpContent body = api.SignedWebhook(
            eventId ?? $"evt_{Guid.NewGuid():N}",
            eventType,
            payload);

        using HttpResponseMessage response =
            await api.CreateClient().PostAsync(Route, body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task WaitForStatusAsync(Guid tenantId, SubscriptionStatus expected) =>
        await BillingApiFixture.WaitAsync(async () =>
        {
            await using OmniAiDbContext context = api.CreateDbContext();

            return await context.WorkspaceSubscriptions.AnyAsync(candidate =>
                candidate.TenantId == TenantId.From(tenantId) && candidate.Status == expected);
        });

    private async Task<string> PlanCodeAsync(Guid tenantId)
    {
        await using OmniAiDbContext context = api.CreateDbContext();

        WorkspacePlanAssignment assignment = await context.WorkspacePlanAssignments
            .SingleAsync(candidate => candidate.TenantId == TenantId.From(tenantId));

        Plan plan = await context.Plans.SingleAsync(candidate => candidate.Id == assignment.PlanId);

        return plan.Code.Value;
    }
}
