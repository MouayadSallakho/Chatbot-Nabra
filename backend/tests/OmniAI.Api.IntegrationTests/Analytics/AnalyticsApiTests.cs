using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OmniAI.Api.IntegrationTests.Authorization;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.WebChat;
using OmniAI.Infrastructure.Persistence;

namespace OmniAI.Api.IntegrationTests.Analytics;

/// <summary>
/// The analytics dashboard, over HTTP.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Authorisation first, because analytics aggregates everything.</strong>
/// A single leak here would publish one workspace's whole operating picture to
/// another, and unlike a list endpoint there is no per-row check further down to
/// catch it.
/// </para>
/// <para>
/// The numbers themselves are proved against real rows in the Infrastructure
/// suite. What is proved here is the route, the statuses, the shape on the wire,
/// and that nothing private travels in it.
/// </para>
/// </remarks>
public sealed class AnalyticsApiTests(AnalyticsApiFixture fixture)
    : IClassFixture<AnalyticsApiFixture>
{
    // ------------------------------------------------------------- who may ask

    [Fact]
    public async Task An_anonymous_request_is_refused()
    {
        (_, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        using HttpClient anonymous = fixture.CreateClient();

        using HttpResponseMessage response = await anonymous.GetAsync(Url(tenantId));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Somebody_who_is_not_a_member_is_told_nothing_exists()
    {
        (_, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        using HttpClient outsider = await fixture.CreateOtherTenantUserAsync();

        using HttpResponseMessage response = await outsider.GetAsync(Url(tenantId));

        // Not 403. Somebody outside the workspace learns nothing about whether it
        // exists, which a refusal would tell them.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_member_without_analytics_view_is_refused()
    {
        (_, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        using HttpClient member =
            await fixture.CreateRestrictedMemberAsync(tenantId, "conversations.view");

        using HttpResponseMessage response = await member.GetAsync(Url(tenantId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_member_holding_analytics_view_reads_it()
    {
        (_, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        using HttpClient member =
            await fixture.CreateRestrictedMemberAsync(tenantId, "analytics.view");

        using HttpResponseMessage response = await member.GetAsync(Url(tenantId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task An_owner_reads_it_without_being_granted_anything()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        using HttpResponseMessage response = await owner.GetAsync(Url(tenantId));

        // Owner authority is the whole catalogue, read at the moment the question
        // is asked -- so a permission added this phase reaches every existing
        // Owner with no migration and no backfill.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ------------------------------------------------------------------- shape

    [Fact]
    public async Task An_empty_workspace_answers_with_zeroes_and_a_full_set_of_days()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        JsonElement body = await ReadAsync(owner, tenantId);

        Assert.Equal(0, body.GetProperty("summary").GetProperty("conversationsStarted").GetInt64());
        Assert.Equal(0, body.GetProperty("current").GetProperty("openConversations").GetInt64());
        Assert.Empty(body.GetProperty("channels").EnumerateArray());
        Assert.Empty(body.GetProperty("agents").EnumerateArray());

        // Thirty buckets, all zero. A chart of a quiet month is a flat line, not
        // a missing axis -- and certainly not a sample chart.
        Assert.Equal(30, body.GetProperty("dailyActivity").GetArrayLength());

        // No WhatsApp history means no WhatsApp panel at all.
        Assert.Equal(JsonValueKind.Null, body.GetProperty("whatsappDelivery").ValueKind);
    }

    [Fact]
    public async Task The_period_says_which_range_and_which_zone_it_is_in()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        JsonElement period = (await ReadAsync(owner, tenantId, "7d")).GetProperty("period");

        Assert.Equal("7d", period.GetProperty("range").GetString());
        Assert.Equal(7, period.GetProperty("days").GetInt32());

        // Published rather than assumed: a reader in Singapore looking at a day
        // bucket needs to know which day it is.
        Assert.Equal("UTC", period.GetProperty("timeZone").GetString());
    }

    [Theory]
    [InlineData("7d", 7)]
    [InlineData("30d", 30)]
    [InlineData("90d", 90)]
    public async Task Each_allowed_range_is_accepted(string range, int days)
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        JsonElement body = await ReadAsync(owner, tenantId, range);

        Assert.Equal(days, body.GetProperty("dailyActivity").GetArrayLength());
    }

    [Fact]
    public async Task No_range_means_thirty_days()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        JsonElement body = await ReadAsync(owner, tenantId);

        Assert.Equal("30d", body.GetProperty("period").GetProperty("range").GetString());
    }

    [Theory]
    [InlineData("45d")]
    [InlineData("365d")]
    [InlineData("all")]
    [InlineData("1000000d")]
    public async Task An_unbounded_or_unknown_range_is_refused(string range)
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        using HttpResponseMessage response = await owner.GetAsync(Url(tenantId, range));

        // Refused rather than clamped. A caller who asked for a year and silently
        // received a month would draw conclusions about a period they did not
        // choose -- and an unbounded range is a denial of service in a query
        // string.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ------------------------------------------------------------ real numbers

    [Fact]
    public async Task The_numbers_describe_the_rows_this_workspace_actually_has()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        await fixture.SeedAsync(tenantId, "Support", customerMessages: 3, aiMessages: 2);

        JsonElement body = await ReadAsync(owner, tenantId);
        JsonElement summary = body.GetProperty("summary");

        Assert.Equal(1, summary.GetProperty("conversationsStarted").GetInt64());
        Assert.Equal(3, summary.GetProperty("customerMessages").GetInt64());
        Assert.Equal(2, summary.GetProperty("aiMessages").GetInt64());

        JsonElement channel = body.GetProperty("channels").EnumerateArray().Single();

        Assert.Equal("WebChat", channel.GetProperty("channel").GetString());
        Assert.Equal(1, channel.GetProperty("conversationsStarted").GetInt64());

        JsonElement agent = body.GetProperty("agents").EnumerateArray().Single();

        Assert.Equal("Support", agent.GetProperty("name").GetString());
        Assert.Equal(3, agent.GetProperty("customerMessages").GetInt64());
    }

    /// <remarks>
    /// The isolation case that matters most on this endpoint: two workspaces
    /// built identically, and neither may see a single row of the other's.
    /// </remarks>
    [Fact]
    public async Task One_workspace_never_sees_another_workspaces_numbers()
    {
        (HttpClient mine, Guid myTenant, _) = await fixture.CreateOwnerAsync();
        (HttpClient theirs, Guid theirTenant, _) = await fixture.CreateOwnerAsync();

        await fixture.SeedAsync(myTenant, "Support", customerMessages: 2, aiMessages: 1);
        await fixture.SeedAsync(theirTenant, "Support", customerMessages: 7, aiMessages: 5);

        JsonElement myBody = await ReadAsync(mine, myTenant);
        JsonElement theirBody = await ReadAsync(theirs, theirTenant);

        Assert.Equal(2, myBody.GetProperty("summary").GetProperty("customerMessages").GetInt64());
        Assert.Equal(7, theirBody.GetProperty("summary").GetProperty("customerMessages").GetInt64());

        // And asking for the other workspace by id is simply absent.
        using HttpResponseMessage crossed = await mine.GetAsync(Url(theirTenant));

        Assert.Equal(HttpStatusCode.NotFound, crossed.StatusCode);
    }

    // ----------------------------------------------------------------- privacy

    /// <remarks>
    /// Analytics aggregates conversations, so the things it must never carry are
    /// exactly the things conversations are full of.
    /// </remarks>
    [Fact]
    public async Task The_response_carries_counts_and_never_content()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        await fixture.SeedAsync(
            tenantId,
            "Support",
            customerMessages: 2,
            aiMessages: 1,
            text: "My card number is 4111 1111 1111 1111");

        using HttpResponseMessage response = await owner.GetAsync(Url(tenantId));

        string raw = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("4111", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("card number", raw, StringComparison.OrdinalIgnoreCase);

        // Nor any of the other things a conversation drags along with it.
        foreach (string forbidden in
            new[] { "text", "message\"", "email", "phone", "token", "ticket", "recipient" })
        {
            Assert.DoesNotContain(forbidden, raw, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ------------------------------------------------------------------ safety

    [Fact]
    public async Task A_safe_read_needs_no_antiforgery_token()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        owner.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");

        using HttpResponseMessage response = await owner.GetAsync(Url(tenantId));

        // A GET changes nothing, so demanding a token would be ceremony that
        // teaches callers to send one everywhere.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task Analytics_has_nothing_to_write(string method)
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        using HttpRequestMessage request = new(new HttpMethod(method), Url(tenantId));

        using HttpResponseMessage response = await owner.SendAsync(request);

        // There is no mutation endpoint under this path and none is intended.
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task Reading_analytics_makes_no_ai_provider_call()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        await fixture.SeedAsync(tenantId, "Support", customerMessages: 2, aiMessages: 1);

        fixture.Embeddings.Reset();

        using HttpResponseMessage response = await owner.GetAsync(Url(tenantId));

        response.EnsureSuccessStatusCode();

        // The provider double throws if it is ever reached, so this is something
        // the suite states rather than assumes.
        Assert.Equal(0, fixture.Embeddings.Calls);
    }

    // ----------------------------------------------------------------- helpers

    private static string Url(Guid tenantId, string? range = null) =>
        range is null
            ? $"/api/tenants/{tenantId}/analytics/dashboard"
            : $"/api/tenants/{tenantId}/analytics/dashboard?range={range}";

    private static async Task<JsonElement> ReadAsync(
        HttpClient client,
        Guid tenantId,
        string? range = null)
    {
        using HttpResponseMessage response = await client.GetAsync(Url(tenantId, range));

        response.EnsureSuccessStatusCode();

        using JsonDocument document =
            JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.Clone();
    }
}
