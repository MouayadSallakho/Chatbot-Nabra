using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OmniAI.Application.Billing;

namespace OmniAI.Api.IntegrationTests.Billing;

/// <summary>
/// Phase 37.5b, row 27: the billing read names the current plan and the fallback
/// plan, with their limits. Plan definitions only; usage stays behind
/// <c>analytics.view</c>.
/// </summary>
public sealed class BillingPlansApiTests(BillingApiFixture api) : IClassFixture<BillingApiFixture>
{
    private static readonly string[] Limits =
        ["members", "aiAgents", "knowledgeDocuments", "webChatEndpoints", "whatsAppConnections", "monthlyProviderOperations"];

    [Fact]
    public async Task The_current_plan_is_the_one_the_usage_screen_judges_against()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        using JsonDocument billing = await ReadAsync(owner, $"/api/tenants/{tenantId}/billing");
        using JsonDocument usage = await ReadAsync(owner, $"/api/tenants/{tenantId}/plan-usage");

        JsonElement current = billing.RootElement.GetProperty("currentPlan");

        Assert.Equal(usage.RootElement.GetProperty("planCode").GetString(), current.GetProperty("code").GetString());
        Assert.Equal(usage.RootElement.GetProperty("planName").GetString(), current.GetProperty("name").GetString());
        Assert.Equal(billing.RootElement.GetProperty("currentPlanName").GetString(), current.GetProperty("name").GetString());

        // Every limit is written, a number or null for unlimited, and agrees with
        // the usage screen's.
        foreach (JsonElement entitlement in usage.RootElement.GetProperty("entitlements").EnumerateArray())
        {
            JsonElement limit = current.GetProperty("limits").GetProperty(entitlement.GetProperty("entitlement").GetString()!);
            JsonElement expected = entitlement.GetProperty("limit");

            Assert.Equal(expected.ValueKind, limit.ValueKind);

            if (expected.ValueKind == JsonValueKind.Number)
            {
                Assert.Equal(expected.GetInt32(), limit.GetInt32());
            }
        }

        Assert.Equal(Limits, current.GetProperty("limits").EnumerateObject().Select(property => property.Name));
    }

    [Fact]
    public async Task The_fallback_plan_is_the_configured_one_with_its_limits()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        using JsonDocument billing = await ReadAsync(owner, $"/api/tenants/{tenantId}/billing");
        JsonElement fallback = billing.RootElement.GetProperty("fallbackPlan");

        Assert.Equal(BillingApiFixture.FallbackPlanCode, fallback.GetProperty("code").GetString());
        Assert.Equal("Free", fallback.GetProperty("name").GetString());
        Assert.Equal(
            [2, 1, 10, 1, 0, 200],
            Limits.Select(name => fallback.GetProperty("limits").GetProperty(name).GetInt32()));
    }

    /// <remarks>
    /// The refusals as they were: 403 for a member without <c>billing.view</c>,
    /// 404 for somebody outside the workspace.
    /// </remarks>
    [Fact]
    public async Task Plans_need_billing_view_alone_and_the_refusals_are_unchanged()
    {
        (_, Guid tenantId, _) = await api.CreateOwnerAsync();
        HttpClient billingOnly = await api.CreateRestrictedMemberAsync(tenantId, "billing.view");
        HttpClient analyticsOnly = await api.CreateRestrictedMemberAsync(tenantId, "analytics.view");
        HttpClient stranger = await api.CreateOtherTenantUserAsync();

        using JsonDocument seen = await ReadAsync(billingOnly, $"/api/tenants/{tenantId}/billing");
        Assert.Equal(JsonValueKind.Object, seen.RootElement.GetProperty("currentPlan").ValueKind);

        using HttpResponseMessage noBilling = await analyticsOnly.GetAsync($"/api/tenants/{tenantId}/billing");
        using HttpResponseMessage elsewhere = await stranger.GetAsync($"/api/tenants/{tenantId}/billing");

        Assert.Equal(HttpStatusCode.Forbidden, noBilling.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, elsewhere.StatusCode);
    }

    [Fact]
    public async Task A_checkout_naming_no_offer_carries_its_code()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        using HttpResponseMessage refused = await owner.PostAsJsonAsync($"/api/tenants/{tenantId}/billing/checkout", new { });

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await refused.Content.ReadAsStringAsync());
        Assert.Equal("billing.offer_id.invalid", body.RootElement.GetProperty("code").GetString());
    }

    internal static async Task<JsonDocument> ReadAsync(HttpClient client, string path)
    {
        using HttpResponseMessage response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }
}

/// <summary>A deployment whose fallback names an unlimited plan, which it would refuse.</summary>
public sealed class UnlimitedFallbackBillingFixture : BillingApiFixture
{
    protected override IReadOnlyDictionary<string, string> HostSettings =>
        new Dictionary<string, string>(base.HostSettings, StringComparer.Ordinal)
        {
            [$"{BillingOptions.Section}:{nameof(BillingOptions.FallbackPlanCode)}"] = "development",
        };
}

/// <summary>
/// No usable fallback reads as none: never a failure, never an unlimited plan
/// presented as the fallback.
/// </summary>
public sealed class UnlimitedFallbackBillingApiTests(UnlimitedFallbackBillingFixture api)
    : IClassFixture<UnlimitedFallbackBillingFixture>
{
    [Fact]
    public async Task An_unlimited_fallback_is_shown_as_none()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        using JsonDocument billing = await BillingPlansApiTests.ReadAsync(owner, $"/api/tenants/{tenantId}/billing");

        Assert.Equal(JsonValueKind.Null, billing.RootElement.GetProperty("fallbackPlan").ValueKind);
        Assert.Equal(JsonValueKind.Object, billing.RootElement.GetProperty("currentPlan").ValueKind);
    }
}

/// <summary>An installation with no billing configuration at all.</summary>
public sealed class UnbilledPlansApiTests(UnbilledDeploymentApiFixture api)
    : IClassFixture<UnbilledDeploymentApiFixture>
{
    [Fact]
    public async Task No_fallback_configured_reads_as_null_beside_the_current_plan()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        using JsonDocument billing = await BillingPlansApiTests.ReadAsync(owner, $"/api/tenants/{tenantId}/billing");

        Assert.Equal(JsonValueKind.Null, billing.RootElement.GetProperty("fallbackPlan").ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(billing.RootElement.GetProperty("currentPlan").GetProperty("code").GetString()));
    }
}
