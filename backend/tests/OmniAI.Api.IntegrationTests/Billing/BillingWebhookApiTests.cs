using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OmniAI.Domain.Billing;
using OmniAI.Domain.Plans;
using OmniAI.Domain.Tenants;
using OmniAI.Infrastructure.Persistence;

namespace OmniAI.Api.IntegrationTests.Billing;

/// <summary>
/// The provider's webhook, over real HTTP.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is the route that decides who gets a plan.</strong> It is
/// anonymous, so the signature is the only thing standing between a stranger and
/// a free subscription. Every negative case here fails against Stripe's own
/// verifier rather than against a stub told to refuse.
/// </para>
/// <para>
/// <strong>No Stripe account, no key, no card.</strong> Signatures are generated
/// locally with the same library that verifies them, and the outbound provider is
/// a double.
/// </para>
/// </remarks>
public sealed class BillingWebhookApiTests(BillingApiFixture api) : IClassFixture<BillingApiFixture>
{
    private const string Route = "/api/billing/webhook";

    [Fact]
    public async Task A_request_with_no_signature_is_refused()
    {
        using StringContent body = new(
            """{"id":"evt_none","object":"event","type":"customer.subscription.updated"}""",
            Encoding.UTF8,
            "application/json");

        using HttpResponseMessage response = await api.CreateClient().PostAsync(Route, body);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        // And nothing was stored. An unsigned request must not even become a row
        // somebody later has to explain.
        await using OmniAiDbContext context = api.CreateDbContext();

        Assert.False(await context.BillingEvents.AnyAsync(
            stored => stored.ProviderEventId == ProviderReference.Create("evt_none")));
    }

    [Fact]
    public async Task A_signature_made_with_the_wrong_secret_is_refused()
    {
        using HttpContent body = api.SignedWebhook(
            "evt_wrong_secret",
            "customer.subscription.updated",
            BillingApiFixture.SubscriptionPayload(
                ProviderReference.Create("sub_test_wrong"),
                "active"),
            secret: "whsec_test_somebody_elses_secret");

        using HttpResponseMessage response = await api.CreateClient().PostAsync(Route, body);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertNotStoredAsync("evt_wrong_secret");
    }

    [Fact]
    public async Task A_body_altered_after_signing_is_refused()
    {
        using HttpContent body = api.TamperedWebhook(
            "evt_tampered",
            "customer.subscription.updated",
            BillingApiFixture.SubscriptionPayload(
                ProviderReference.Create("sub_test_tampered"),
                "active"));

        using HttpResponseMessage response = await api.CreateClient().PostAsync(Route, body);

        // The one thing a signature scheme exists to catch. It is caught because
        // the endpoint verifies the exact bytes it received rather than a
        // re-serialised copy of what it parsed.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertNotStoredAsync("evt_tampered");
    }

    [Fact]
    public async Task A_correctly_signed_request_from_yesterday_is_refused()
    {
        using HttpContent body = api.SignedWebhook(
            "evt_stale",
            "customer.subscription.updated",
            BillingApiFixture.SubscriptionPayload(
                ProviderReference.Create("sub_test_stale"),
                "active"),
            signedAt: DateTimeOffset.UtcNow.AddDays(-1));

        using HttpResponseMessage response = await api.CreateClient().PostAsync(Route, body);

        // Perfectly valid cryptography, and still refused. Without the timestamp
        // tolerance, anybody who captured one request could replay it forever.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertNotStoredAsync("evt_stale");
    }

    [Theory]
    [InlineData("")]
    [InlineData("nonsense")]
    [InlineData("t=1,v1=zz")]
    [InlineData("v1=abc")]
    [InlineData("t=1")]
    public async Task A_malformed_signature_header_is_refused(string header)
    {
        using StringContent body = new(
            """{"id":"evt_malformed","object":"event","type":"customer.subscription.updated"}""",
            Encoding.UTF8,
            "application/json");

        body.Headers.Add("Stripe-Signature", header);

        using HttpResponseMessage response = await api.CreateClient().PostAsync(Route, body);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertNotStoredAsync("evt_malformed");
    }

    [Fact]
    public async Task A_redelivered_event_is_stored_once()
    {
        string payload = BillingApiFixture.SubscriptionPayload(
            ProviderReference.Create("sub_test_duplicate"),
            "active");

        for (int attempt = 0; attempt < 3; attempt++)
        {
            using HttpContent body =
                api.SignedWebhook("evt_duplicate", "customer.subscription.updated", payload);

            using HttpResponseMessage response = await api.CreateClient().PostAsync(Route, body);

            // Every delivery is a success. Answering a redelivery with an error
            // would only cause more of them.
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        await using OmniAiDbContext context = api.CreateDbContext();

        Assert.Equal(
            1,
            await context.BillingEvents.CountAsync(
                stored => stored.ProviderEventId == ProviderReference.Create("evt_duplicate")));
    }

    [Fact]
    public async Task Concurrent_redeliveries_are_stored_once()
    {
        string payload = BillingApiFixture.SubscriptionPayload(
            ProviderReference.Create("sub_test_concurrent"),
            "active");

        // The case an in-memory guard cannot cover: several copies arriving at
        // the same instant, potentially at different web servers. The unique
        // index is what decides.
        HttpResponseMessage[] responses = await Task.WhenAll(
            Enumerable.Range(0, 6).Select(_ =>
            {
                HttpContent body =
                    api.SignedWebhook("evt_concurrent", "customer.subscription.updated", payload);

                return api.CreateClient().PostAsync(Route, body);
            }));

        try
        {
            Assert.All(responses, response =>
                Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        }
        finally
        {
            foreach (HttpResponseMessage response in responses)
            {
                response.Dispose();
            }
        }

        await using OmniAiDbContext context = api.CreateDbContext();

        Assert.Equal(
            1,
            await context.BillingEvents.CountAsync(
                stored => stored.ProviderEventId == ProviderReference.Create("evt_concurrent")));
    }

    [Fact]
    public async Task An_event_type_this_platform_does_nothing_with_is_accepted_and_settled()
    {
        using HttpContent body = api.SignedWebhook(
            "evt_unknown_type",
            "radar.early_fraud_warning.created",
            """{"id":"issfr_test","object":"radar.early_fraud_warning"}""");

        using HttpResponseMessage response = await api.CreateClient().PostAsync(Route, body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Accepted, stored, and then settled by the worker rather than retried
        // forever. Providers send far more than this platform needs.
        await BillingApiFixture.WaitAsync(async () =>
        {
            await using OmniAiDbContext context = api.CreateDbContext();

            return await context.BillingEvents.AnyAsync(stored =>
                stored.ProviderEventId == ProviderReference.Create("evt_unknown_type")
                && stored.Status == BillingEventStatus.Processed);
        });
    }

    [Fact]
    public async Task An_event_for_a_subscription_nobody_owns_grants_nothing()
    {
        using HttpContent body = api.SignedWebhook(
            "evt_orphan",
            "customer.subscription.updated",
            BillingApiFixture.SubscriptionPayload(
                ProviderReference.Create("sub_test_orphan"),
                "active"));

        using HttpResponseMessage response = await api.CreateClient().PostAsync(Route, body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Signed, stored, and then refused: a provider reference is not evidence
        // of which workspace it belongs to, and there is no safe guess.
        await BillingApiFixture.WaitAsync(async () =>
        {
            await using OmniAiDbContext context = api.CreateDbContext();

            return await context.BillingEvents.AnyAsync(stored =>
                stored.ProviderEventId == ProviderReference.Create("evt_orphan")
                && stored.Status == BillingEventStatus.Unprocessable);
        });

        await using OmniAiDbContext after = api.CreateDbContext();

        Assert.False(await after.WorkspaceSubscriptions.AnyAsync());
    }

    /// <remarks>
    /// The case that matters most. A price this platform does not sell must grant
    /// nothing at all — not a guessed plan, not one named in provider metadata,
    /// and above all not an unlimited one.
    /// </remarks>
    [Fact]
    public async Task A_checkout_billing_an_unknown_price_creates_no_subscription()
    {
        await api.SeedOffersAsync();

        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        ProviderReference sessionId = await StartCheckoutAsync(owner, tenantId);

        api.Provider.CompleteCheckout(sessionId, api.UnknownPriceId);

        using HttpContent body = api.SignedWebhook(
            $"evt_unknown_price_{Guid.NewGuid():N}",
            "checkout.session.completed",
            BillingApiFixture.CheckoutPayload(sessionId));

        using HttpResponseMessage response = await api.CreateClient().PostAsync(Route, body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Give the worker a chance to do the wrong thing, then confirm it did
        // not: no subscription, and the workspace is still where it was.
        await Task.Delay(1500);

        await using OmniAiDbContext context = api.CreateDbContext();

        Assert.False(await context.WorkspaceSubscriptions
            .AnyAsync(candidate => candidate.TenantId == TenantId.From(tenantId)));

        WorkspacePlanAssignment assignment = await context.WorkspacePlanAssignments
            .SingleAsync(candidate => candidate.TenantId == TenantId.From(tenantId));

        Domain.Plans.Plan plan =
            await context.Plans.SingleAsync(candidate => candidate.Id == assignment.PlanId);

        // Still the plan onboarding gave it, and emphatically not an unlimited
        // one granted by an unrecognised price.
        Assert.Equal("development", plan.Code.Value);
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

    private async Task AssertNotStoredAsync(string eventId)
    {
        await using OmniAiDbContext context = api.CreateDbContext();

        Assert.False(await context.BillingEvents.AnyAsync(
            stored => stored.ProviderEventId == ProviderReference.Create(eventId)));
    }
}
