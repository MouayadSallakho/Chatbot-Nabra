using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace OmniAI.Api.IntegrationTests.Billing;

/// <summary>
/// A deployment with no billing configuration at all.
/// </summary>
/// <remarks>
/// It runs perfectly well and simply cannot sell. Everything else this platform
/// does — conversations, agents, knowledge, channels — is unaffected, which is
/// the whole reason the provider is registered as something that refuses rather
/// than left unregistered.
/// </remarks>
public sealed class UnbilledDeploymentApiFixture : BillingApiFixture
{
    // No fallback plan, no return address, no webhook secret. Exactly what an
    // installation that has never configured billing looks like.
    protected override IReadOnlyDictionary<string, string> HostSettings =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Billing:FallbackPlanCode"] = string.Empty,
            ["Billing:ReturnBaseUrl"] = string.Empty,
            ["STRIPE_WEBHOOK_SECRET"] = string.Empty,
        };
}

public sealed class UnbilledDeploymentApiTests(UnbilledDeploymentApiFixture api)
    : IClassFixture<UnbilledDeploymentApiFixture>
{
    [Fact]
    public async Task The_screen_says_nothing_is_for_sale_rather_than_failing()
    {
        await api.SeedOffersAsync();

        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        using HttpResponseMessage response =
            await owner.GetAsync($"/api/tenants/{tenantId}/billing");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        // isManaged describes the installation, not the workspace. A screen that
        // read it as "has a subscription" would hide the entire purchase path on
        // exactly the deployments that can sell.
        Assert.False(body.RootElement.GetProperty("isManaged").GetBoolean());
        Assert.False(body.RootElement.GetProperty("canPurchase").GetBoolean());

        // Offers exist in the database and are deliberately not listed: showing a
        // price that leads to a failure is worse than showing none.
        Assert.Equal(0, body.RootElement.GetProperty("offers").GetArrayLength());
    }

    [Fact]
    public async Task Buying_is_refused_as_unavailable_rather_than_as_a_payment_problem()
    {
        await api.SeedOffersAsync();

        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        using HttpResponseMessage response = await owner.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/billing/checkout",
            new { offerId = api.Offers[BillingApiFixture.MonthlyOfferCode] });

        // 503, and deliberately not 402: nothing about this is "payment
        // required", and a client showing a payment prompt would be prompting for
        // something no payment could satisfy.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task A_webhook_is_refused_because_nothing_can_verify_it()
    {
        using HttpContent body = api.SignedWebhook(
            "evt_unverifiable",
            "customer.subscription.updated",
            BillingApiFixture.SubscriptionPayload(
                Domain.Billing.ProviderReference.Create("sub_test_unverifiable"),
                "active"));

        using HttpResponseMessage response =
            await api.CreateClient().PostAsync("/api/billing/webhook", body);

        // "No secret configured" must never mean "skip the check". A deployment
        // that waved these through would be a way for anybody to grant a plan.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
