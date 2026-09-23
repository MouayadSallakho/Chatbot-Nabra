using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OmniAI.Domain.Billing;
using OmniAI.Infrastructure.Persistence;

namespace OmniAI.Api.IntegrationTests.Billing;

/// <summary>
/// Starting a purchase, over real HTTP.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The browser never names a price.</strong> Several tests here exist
/// only to prove that: a request carries an internal offer id, and there is no
/// field, header or route that would let a client choose what the provider is
/// asked to bill.
/// </para>
/// <para>
/// <strong>Nothing in this suite takes a card.</strong> Checkout answers with a
/// URL to somebody else's page, and the assertions are about what this platform
/// stored before handing that URL over.
/// </para>
/// </remarks>
public sealed class BillingCheckoutApiTests(BillingApiFixture api) : IClassFixture<BillingApiFixture>
{
    [Fact]
    public async Task An_owner_is_sent_to_the_provider_and_the_attempt_is_recorded()
    {
        await api.SeedOffersAsync();

        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        using HttpResponseMessage response = await owner.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/billing/checkout",
            new { offerId = api.Offers[BillingApiFixture.MonthlyOfferCode] });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.StartsWith(
            "https://checkout.test.local/",
            body.RootElement.GetProperty("url").GetString(),
            StringComparison.Ordinal);

        // The durable record that makes the second click harmless. It exists
        // before the provider is called, which is the whole point of it.
        await using OmniAiDbContext context = api.CreateDbContext();

        BillingCheckoutIntent intent = await context.BillingCheckoutIntents
            .SingleAsync(candidate => candidate.TenantId == Tenant(tenantId));

        Assert.Equal(CheckoutIntentStatus.Pending, intent.Status);
        Assert.NotNull(intent.ProviderSessionId);

        // And no subscription yet. Being sent to a payment page is not paying.
        Assert.False(await context.WorkspaceSubscriptions
            .AnyAsync(candidate => candidate.TenantId == Tenant(tenantId)));
    }

    [Fact]
    public async Task Two_rapid_clicks_produce_one_checkout()
    {
        await api.SeedOffersAsync();

        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        int before = api.Provider.CheckoutsCreated;

        object request = new { offerId = api.Offers[BillingApiFixture.MonthlyOfferCode] };

        using HttpResponseMessage first = await owner.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/billing/checkout",
            request);

        using HttpResponseMessage second = await owner.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/billing/checkout",
            request);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        // One provider session, one durable intent. A disabled button in the
        // browser would not have achieved this: these requests could have
        // reached two different web servers.
        Assert.Equal(before + 1, api.Provider.CheckoutsCreated);

        await using OmniAiDbContext context = api.CreateDbContext();

        Assert.Equal(
            1,
            await context.BillingCheckoutIntents
                .CountAsync(candidate => candidate.TenantId == Tenant(tenantId)));
    }

    [Fact]
    public async Task Simultaneous_clicks_produce_one_checkout()
    {
        await api.SeedOffersAsync();

        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        int before = api.Provider.CheckoutsCreated;

        // Genuinely concurrent, which is what the unique index is for. An
        // application-level "have we started one?" check would let both requests
        // read "no" and both proceed.
        HttpResponseMessage[] responses = await Task.WhenAll(
            Enumerable.Range(0, 4).Select(_ => owner.PostAsJsonAsync(
                $"/api/tenants/{tenantId}/billing/checkout",
                new { offerId = api.Offers[BillingApiFixture.MonthlyOfferCode] })));

        try
        {
            Assert.All(responses, response =>
                Assert.Equal(HttpStatusCode.OK, response.StatusCode));

            Assert.Equal(before + 1, api.Provider.CheckoutsCreated);
        }
        finally
        {
            foreach (HttpResponseMessage response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task A_withdrawn_offer_cannot_be_bought()
    {
        await api.SeedOffersAsync();

        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        using HttpResponseMessage response = await owner.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/billing/checkout",
            new { offerId = api.Offers[BillingApiFixture.WithdrawnOfferCode] });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("billing.checkout.offernotavailable", await CodeAsync(response));
    }

    [Fact]
    public async Task An_offer_that_does_not_exist_is_refused()
    {
        await api.SeedOffersAsync();

        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        using HttpResponseMessage response = await owner.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/billing/checkout",
            new { offerId = Guid.CreateVersion7() });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("billing.checkout.unknownoffer", await CodeAsync(response));
    }

    /// <remarks>
    /// The request body is the whole client-supplied surface, and it has exactly
    /// one field. A price, an amount or a currency sent alongside is ignored
    /// rather than honoured — there is nothing to honour it with.
    /// </remarks>
    [Fact]
    public async Task A_price_sent_by_the_browser_is_ignored()
    {
        await api.SeedOffersAsync();

        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        using StringContent smuggled = new(
            """
            {"offerId":"OFFER","priceId":"price_attacker","amountMinorUnits":1,"currency":"USD"}
            """.Replace(
                "OFFER",
                api.Offers[BillingApiFixture.MonthlyOfferCode].ToString(),
                StringComparison.Ordinal),
            Encoding.UTF8,
            "application/json");

        using HttpResponseMessage response = await owner.PostAsync(
            $"/api/tenants/{tenantId}/billing/checkout",
            smuggled);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // What was actually bought is the offer's own mapped price.
        await using OmniAiDbContext context = api.CreateDbContext();

        BillingCheckoutIntent intent = await context.BillingCheckoutIntents
            .SingleAsync(candidate => candidate.TenantId == Tenant(tenantId));

        Assert.Equal(
            api.Offers[BillingApiFixture.MonthlyOfferCode],
            intent.OfferId.Value);
    }

    [Fact]
    public async Task A_member_without_billing_manage_cannot_buy_anything()
    {
        await api.SeedOffersAsync();

        (_, Guid tenantId, _) = await api.CreateOwnerAsync();

        // Being allowed to read what a workspace spends is not being allowed to
        // spend it. Nor is analytics: Phase 25 deliberately did not reuse
        // analytics.view for either half of billing.
        HttpClient viewer =
            await api.CreateRestrictedMemberAsync(tenantId, "billing.view", "analytics.view");

        using HttpResponseMessage refused = await viewer.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/billing/checkout",
            new { offerId = api.Offers[BillingApiFixture.MonthlyOfferCode] });

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        // And reading still works, so this is a real distinction rather than a
        // member with no access at all.
        using HttpResponseMessage read = await viewer.GetAsync($"/api/tenants/{tenantId}/billing");

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
    }

    [Fact]
    public async Task A_member_without_billing_view_cannot_read_what_the_workspace_pays()
    {
        await api.SeedOffersAsync();

        (_, Guid tenantId, _) = await api.CreateOwnerAsync();

        HttpClient other = await api.CreateRestrictedMemberAsync(tenantId, "analytics.view");

        using HttpResponseMessage response = await other.GetAsync($"/api/tenants/{tenantId}/billing");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_non_member_is_told_the_workspace_does_not_exist()
    {
        await api.SeedOffersAsync();

        (_, Guid tenantId, _) = await api.CreateOwnerAsync();

        HttpClient stranger = await api.CreateSignedInClientAsync();

        using HttpResponseMessage read = await stranger.GetAsync($"/api/tenants/{tenantId}/billing");

        // Not 403. Somebody outside a workspace must not learn that it exists,
        // let alone what it pays.
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);

        using HttpResponseMessage bought = await stranger.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/billing/checkout",
            new { offerId = api.Offers[BillingApiFixture.MonthlyOfferCode] });

        Assert.Equal(HttpStatusCode.NotFound, bought.StatusCode);
    }

    [Fact]
    public async Task A_purchase_without_an_antiforgery_token_is_refused()
    {
        await api.SeedOffersAsync();

        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        int before = api.Provider.CheckoutsCreated;

        // The same signed-in session, minus the header a browser form post from
        // another site could not supply.
        owner.DefaultRequestHeaders.Remove(ApiAuthentication.AntiforgeryHeader);

        using HttpResponseMessage response = await owner.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/billing/checkout",
            new { offerId = api.Offers[BillingApiFixture.MonthlyOfferCode] });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // Nothing was started. A cross-site post that spent somebody's money and
        // then failed the check afterwards would be no defence at all.
        Assert.Equal(before, api.Provider.CheckoutsCreated);
    }

    [Fact]
    public async Task A_provider_outage_is_reported_and_nothing_is_left_half_done()
    {
        await api.SeedOffersAsync();

        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        api.Provider.IsUnavailable = true;

        try
        {
            using HttpResponseMessage response = await owner.PostAsJsonAsync(
                $"/api/tenants/{tenantId}/billing/checkout",
                new { offerId = api.Offers[BillingApiFixture.MonthlyOfferCode] });

            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);

            // Told plainly, and without the provider's own words -- those can
            // name internal identifiers and quote the request back.
            string body = await response.Content.ReadAsStringAsync();

            Assert.DoesNotContain("test provider", body, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            api.Provider.IsUnavailable = false;
        }

        await using OmniAiDbContext context = api.CreateDbContext();

        // No subscription, and no session recorded against the attempt.
        Assert.False(await context.WorkspaceSubscriptions
            .AnyAsync(candidate => candidate.TenantId == Tenant(tenantId)));

        Assert.All(
            await context.BillingCheckoutIntents
                .Where(candidate => candidate.TenantId == Tenant(tenantId))
                .ToListAsync(),
            intent => Assert.Null(intent.ProviderSessionId));
    }

    [Fact]
    public async Task A_workspace_with_nothing_bought_has_no_portal_to_open()
    {
        await api.SeedOffersAsync();

        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        using HttpResponseMessage response = await owner.PostAsync(
            $"/api/tenants/{tenantId}/billing/portal",
            content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("billing.portal.nocustomer", await CodeAsync(response));
    }

    [Fact]
    public async Task The_billing_screen_never_carries_a_provider_identifier()
    {
        await api.SeedOffersAsync();

        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        using HttpResponseMessage response = await owner.GetAsync($"/api/tenants/{tenantId}/billing");

        string body = await response.Content.ReadAsStringAsync();

        // No Stripe price, customer, subscription or session id. A browser has no
        // use for any of them and each is a direct handle on a payment record.
        Assert.DoesNotContain("price_", body, StringComparison.Ordinal);
        Assert.DoesNotContain("cus_", body, StringComparison.Ordinal);
        Assert.DoesNotContain("sub_", body, StringComparison.Ordinal);
        Assert.DoesNotContain("cs_test", body, StringComparison.Ordinal);

        // What it does carry is what a screen needs.
        using JsonDocument parsed = JsonDocument.Parse(body);

        Assert.True(parsed.RootElement.GetProperty("canPurchase").GetBoolean());
        Assert.Equal(2, parsed.RootElement.GetProperty("offers").GetArrayLength());
    }

    private static Domain.Tenants.TenantId Tenant(Guid id) => Domain.Tenants.TenantId.From(id);

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return body.RootElement.TryGetProperty("code", out JsonElement code)
            ? code.GetString()
            : null;
    }
}
