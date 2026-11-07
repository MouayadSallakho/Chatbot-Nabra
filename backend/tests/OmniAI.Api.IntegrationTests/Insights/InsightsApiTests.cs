using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OmniAI.Api.IntegrationTests.Notifications;
using OmniAI.Domain.Ai;

namespace OmniAI.Api.IntegrationTests.Insights;

/// <summary>
/// Phase 37.6 over HTTP: the workspace time zone (row 34), the comparison period
/// (31), the first-activity date (33), usage by purpose (30), the report catalogue
/// codes (32) and the report type on report notifications (25).
/// </summary>
/// <remarks>
/// Every contract here is additive and opt-in: a workspace nobody configured is in
/// UTC and answers exactly as before, and the comparison is there only when asked
/// for. Each test also proves the refusals: no permission, another workspace,
/// stale or missing versions, and values that are not accepted.
/// </remarks>
public sealed class InsightsApiTests(NotificationApiFixture fixture) : IClassFixture<NotificationApiFixture>
{
    // ------------------------------------------------------------- time zone

    [Fact]
    public async Task A_workspace_is_in_utc_until_somebody_changes_it()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        JsonElement workspace = await ReadAsync(await owner.GetAsync(Workspace(tenantId)));

        Assert.Equal("UTC", workspace.GetProperty("timeZone").GetString());
        Assert.Equal("UTC", (await DashboardAsync(owner, tenantId)).GetProperty("period").GetProperty("timeZone").GetString());
        Assert.Equal("UTC", (await ReadAsync(await owner.GetAsync(Catalog(tenantId)))).GetProperty("timeZone").GetString());
    }

    [Fact]
    public async Task The_owner_sets_the_gulf_zone_with_if_match_and_everything_counts_in_it()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        string tag = await TagAsync(owner, tenantId);

        using HttpResponseMessage changed = await ChangeAsync(owner, tenantId, "Asia/Riyadh", tag);

        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal("Asia/Riyadh", (await ReadAsync(changed)).GetProperty("timeZone").GetString());
        Assert.NotEqual(tag, changed.Headers.ETag!.ToString());
        Assert.Equal(changed.Headers.ETag!.ToString(), await TagAsync(owner, tenantId));

        // The dashboard's days are Gulf days: the window starts at 21:00 UTC.
        JsonElement period = (await DashboardAsync(owner, tenantId)).GetProperty("period");
        Assert.Equal("Asia/Riyadh", period.GetProperty("timeZone").GetString());
        Assert.Equal(21, period.GetProperty("from").GetDateTimeOffset().UtcDateTime.Hour);

        Assert.Equal("Asia/Riyadh", (await ReadAsync(await owner.GetAsync(Catalog(tenantId)))).GetProperty("timeZone").GetString());

        // A report asked for now is computed, and kept, in that zone.
        using HttpResponseMessage accepted = await owner.PostAsJsonAsync(
            Reports(tenantId),
            new { reportType = "ChannelUsage", format = "Csv", range = "7d", clientRequestId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        JsonElement execution = await ReadAsync(accepted);
        Assert.Equal("Asia/Riyadh", execution.GetProperty("timeZone").GetString());
        Assert.Equal(7, execution.GetProperty("periodTo").GetDateTime().Subtract(execution.GetProperty("periodFrom").GetDateTime()).Days);
    }

    [Fact]
    public async Task Without_if_match_the_change_is_428_and_with_a_stale_one_412()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        string stale = await TagAsync(owner, tenantId);

        using HttpResponseMessage missing = await ChangeAsync(owner, tenantId, "Asia/Riyadh", tag: null);
        Assert.Equal(HttpStatusCode.PreconditionRequired, missing.StatusCode);

        using HttpResponseMessage first = await ChangeAsync(owner, tenantId, "Europe/Berlin", stale);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using HttpResponseMessage second = await ChangeAsync(owner, tenantId, "Asia/Riyadh", stale);
        Assert.Equal(HttpStatusCode.PreconditionFailed, second.StatusCode);
        Assert.Equal("precondition.failed", await CodeAsync(second));

        Assert.Equal("Europe/Berlin", (await ReadAsync(await owner.GetAsync(Workspace(tenantId)))).GetProperty("timeZone").GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("asia/riyadh")]
    [InlineData("+03:00")]
    [InlineData("Arab Standard Time")]
    [InlineData("Mars/Olympus_Mons")]
    [InlineData("posix/Asia/Riyadh")]
    public async Task A_name_that_is_not_an_accepted_zone_is_400_with_its_code(string name)
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        using HttpResponseMessage refused = await ChangeAsync(owner, tenantId, name, await TagAsync(owner, tenantId));

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("workspace.time_zone.invalid", await CodeAsync(refused));
        Assert.Equal("UTC", (await ReadAsync(await owner.GetAsync(Workspace(tenantId)))).GetProperty("timeZone").GetString());
    }

    [Fact]
    public async Task A_member_without_workspace_manage_is_403_and_another_workspace_is_404()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        string tag = await TagAsync(owner, tenantId);

        HttpClient viewer = await fixture.CreateRestrictedMemberAsync(tenantId, "workspace.view", "analytics.view");
        using HttpResponseMessage forbidden = await ChangeAsync(viewer, tenantId, "Asia/Riyadh", tag);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        (HttpClient stranger, _, _) = await fixture.CreateOwnerAsync("Rival Trading");
        using HttpResponseMessage absent = await ChangeAsync(stranger, tenantId, "Asia/Riyadh", tag);
        Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);

        Assert.Equal("UTC", (await ReadAsync(await owner.GetAsync(Workspace(tenantId)))).GetProperty("timeZone").GetString());

        HttpClient manager = await fixture.CreateRestrictedMemberAsync(tenantId, "workspace.view", "workspace.manage");
        using HttpResponseMessage allowed = await ChangeAsync(manager, tenantId, "Asia/Dubai", tag);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    // ---------------------------------------------------- comparison and first

    [Fact]
    public async Task Without_compare_the_dashboard_has_no_previous_member_and_with_it_the_adjacent_period()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        await fixture.SeedAsync(tenantId, "Support", customerMessages: 2, aiMessages: 1);

        JsonElement plain = await DashboardAsync(owner, tenantId, "range=7d");
        Assert.False(plain.TryGetProperty("previous", out _));

        JsonElement compared = await DashboardAsync(owner, tenantId, "range=7d&compare=previous");
        JsonElement previous = compared.GetProperty("previous");
        JsonElement current = compared.GetProperty("period");

        Assert.Equal(
            current.GetProperty("from").GetDateTimeOffset(),
            previous.GetProperty("period").GetProperty("to").GetDateTimeOffset());
        Assert.Equal(7, previous.GetProperty("period").GetProperty("days").GetInt32());
        Assert.Equal("7d", previous.GetProperty("period").GetProperty("range").GetString());
        Assert.Equal("UTC", previous.GetProperty("period").GetProperty("timeZone").GetString());

        // Today's activity is in the current period and not in the previous one.
        Assert.Equal(1, compared.GetProperty("summary").GetProperty("conversationsStarted").GetInt64());
        Assert.Equal(0, previous.GetProperty("summary").GetProperty("conversationsStarted").GetInt64());
        Assert.Equal(0, previous.GetProperty("aiOperations").GetProperty("attempts").GetInt64());

        // Only the period's totals compare; the "open right now" figures do not.
        Assert.False(previous.TryGetProperty("current", out _));

        // And the rest of the response is what it was without the parameter.
        Assert.Equal(plain.GetProperty("summary").GetRawText(), compared.GetProperty("summary").GetRawText());
        Assert.Equal(plain.GetProperty("dailyActivity").GetRawText(), compared.GetProperty("dailyActivity").GetRawText());
    }

    [Theory]
    [InlineData("next")]
    [InlineData("previous,next")]
    [InlineData("1")]
    public async Task An_unknown_comparison_is_400_with_its_code(string compare)
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        using HttpResponseMessage refused = await owner.GetAsync($"{Dashboard(tenantId)}?compare={Uri.EscapeDataString(compare)}");

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("analytics.compare.invalid", await CodeAsync(refused));
    }

    [Fact]
    public async Task The_comparison_keeps_the_dashboard_s_permission_and_isolation()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        HttpClient withoutAnalytics = await fixture.CreateRestrictedMemberAsync(tenantId, "workspace.view");
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await withoutAnalytics.GetAsync($"{Dashboard(tenantId)}?compare=previous")).StatusCode);

        (HttpClient stranger, _, _) = await fixture.CreateOwnerAsync("Rival Trading");
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await stranger.GetAsync($"{Dashboard(tenantId)}?compare=previous")).StatusCode);

        _ = owner;
    }

    [Fact]
    public async Task The_first_activity_is_null_for_a_new_workspace_and_a_date_once_there_is_a_conversation()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        JsonElement before = await DashboardAsync(owner, tenantId);
        Assert.Equal(JsonValueKind.Null, before.GetProperty("firstActivityOn").ValueKind);

        await fixture.SeedAsync(tenantId, "Support", customerMessages: 1, aiMessages: 0);

        string? first = (await DashboardAsync(owner, tenantId)).GetProperty("firstActivityOn").GetString();
        Assert.Equal(DateTimeOffset.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), first);
    }

    // -------------------------------------------------------------- by purpose

    [Fact]
    public async Task Usage_by_purpose_lists_every_purpose_and_adds_up_to_the_totals()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        await fixture.SeedUsageAsync(tenantId);
        await fixture.SeedUsageAsync(tenantId, currency: "EUR");
        await fixture.SeedUsageAsync(
            tenantId,
            model: "test-embed-a",
            operationType: AiOperationType.Embedding,
            purpose: AiUsagePurpose.AgentRetrievalTest,
            outputTokens: null,
            inputPerMillion: null);
        await fixture.SeedUsageAsync(
            tenantId,
            model: "test-embed-a",
            operationType: AiOperationType.Embedding,
            purpose: AiUsagePurpose.KnowledgeIndexEmbedding,
            status: AiOperationStatus.Failed);

        JsonElement usage = (await DashboardAsync(owner, tenantId)).GetProperty("aiUsage");
        JsonElement[] rows = [.. usage.GetProperty("byPurpose").EnumerateArray()];

        Assert.Equal(
            ["ConversationGeneration", "ConversationRetrievalEmbedding", "KnowledgeIndexEmbedding", "AgentPreviewGeneration", "AgentRetrievalTest"],
            rows.Select(row => row.GetProperty("purpose").GetString()));

        Assert.Equal(usage.GetProperty("calls").GetInt64(), rows.Sum(row => row.GetProperty("calls").GetInt64()));
        Assert.Equal(usage.GetProperty("succeeded").GetInt64(), rows.Sum(row => row.GetProperty("succeeded").GetInt64()));
        Assert.Equal(usage.GetProperty("failed").GetInt64(), rows.Sum(row => row.GetProperty("failed").GetInt64()));
        Assert.Equal(usage.GetProperty("unratedCalls").GetInt64(), rows.Sum(row => row.GetProperty("unratedCalls").GetInt64()));

        JsonElement replies = rows[0];
        Assert.Equal(2, replies.GetProperty("calls").GetInt64());
        Assert.Equal(
            ["EUR", "USD"],
            replies.GetProperty("ratedCost").EnumerateArray().Select(cost => cost.GetProperty("currency").GetString()));

        Assert.Equal(1, rows[4].GetProperty("calls").GetInt64());
        Assert.Equal(1, rows[4].GetProperty("unratedCalls").GetInt64());
        Assert.Equal(1, rows[2].GetProperty("failed").GetInt64());
        Assert.Equal(0, rows[3].GetProperty("calls").GetInt64());
    }

    [Fact]
    public async Task With_no_provider_calls_there_is_still_no_usage_section()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        Assert.Equal(JsonValueKind.Null, (await DashboardAsync(owner, tenantId)).GetProperty("aiUsage").ValueKind);
    }

    // ----------------------------------------------------------- report codes

    [Fact]
    public async Task The_catalogue_publishes_stable_type_codes_and_its_parameters_by_code()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        JsonElement catalog = await ReadAsync(await owner.GetAsync(Catalog(tenantId)));
        JsonElement[] reports = [.. catalog.GetProperty("reports").EnumerateArray()];

        // The translation keys. They never change; a client words each report by
        // its code, so renaming one would orphan every translation of it.
        Assert.Equal(
            [
                "ConversationActivity", "ChannelUsage", "AiHumanHandling", "CustomerActivity",
                "AgentActivity", "ProviderUsage", "ProviderCost", "WhatsAppDelivery",
            ],
            reports.Select(report => report.GetProperty("type").GetString()));

        foreach (JsonElement report in reports)
        {
            JsonElement[] parameters = [.. report.GetProperty("parameters").EnumerateArray()];

            Assert.Equal(["range", "format"], parameters.Select(parameter => parameter.GetProperty("code").GetString()));
            Assert.Equal(
                ["7d", "30d", "90d", "custom"],
                parameters[0].GetProperty("values").EnumerateArray().Select(value => value.GetString()));
            Assert.Equal(
                report.GetProperty("formats").EnumerateArray().Select(value => value.GetString()),
                parameters[1].GetProperty("values").EnumerateArray().Select(value => value.GetString()));
        }
    }

    [Fact]
    public async Task A_report_notification_carries_its_report_type_and_other_kinds_carry_none()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        await fixture.SeedAsync(tenantId, "Support", customerMessages: 1, aiMessages: 1);

        using HttpResponseMessage accepted = await owner.PostAsJsonAsync(
            Reports(tenantId),
            new { reportType = "AgentActivity", format = "Csv", range = "7d", clientRequestId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);

        JsonElement item = await WaitForNotificationAsync(owner, tenantId, "ReportCompleted");

        Assert.Equal("AgentActivity", item.GetProperty("parameters").GetProperty("reportType").GetString());

        // A colleague who may read reports was not the requester and sees nothing.
        HttpClient colleague = await fixture.CreateRestrictedMemberAsync(tenantId, "reports.view", "analytics.view");
        JsonElement theirs = await ReadAsync(await colleague.GetAsync(Notifications(tenantId)));
        Assert.Empty(theirs.GetProperty("items").EnumerateArray());

        // Every notification that is not about a report has no parameters.
        JsonElement mine = await ReadAsync(await owner.GetAsync(Notifications(tenantId)));
        Assert.All(
            mine.GetProperty("items").EnumerateArray()
                .Where(notification => notification.GetProperty("category").GetString() != "Reports"),
            notification => Assert.Equal(JsonValueKind.Null, notification.GetProperty("parameters").ValueKind));
    }

    // ----------------------------------------------------------------- helpers

    private static string Workspace(Guid tenantId) => $"/api/tenants/{tenantId}";

    private static string Dashboard(Guid tenantId) => $"/api/tenants/{tenantId}/analytics/dashboard";

    private static string Reports(Guid tenantId) => $"/api/tenants/{tenantId}/reports";

    private static string Catalog(Guid tenantId) => $"{Reports(tenantId)}/catalog";

    private static string Notifications(Guid tenantId) => $"/api/tenants/{tenantId}/notifications/";

    private static async Task<JsonElement> DashboardAsync(HttpClient client, Guid tenantId, string query = "range=30d") =>
        await ReadAsync(await client.GetAsync($"{Dashboard(tenantId)}?{query}"));

    private static async Task<string> TagAsync(HttpClient client, Guid tenantId)
    {
        using HttpResponseMessage read = await client.GetAsync(Workspace(tenantId));

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        return read.Headers.ETag!.ToString();
    }

    private static async Task<HttpResponseMessage> ChangeAsync(HttpClient client, Guid tenantId, string name, string? tag)
    {
        HttpRequestMessage request = new(HttpMethod.Put, $"{Workspace(tenantId)}/time-zone")
        {
            Content = JsonContent.Create(new { timeZone = name }),
        };

        if (tag is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", tag);
        }

        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> WaitForNotificationAsync(HttpClient client, Guid tenantId, string kind)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(30);

        while (true)
        {
            JsonElement page = await ReadAsync(await client.GetAsync(Notifications(tenantId)));

            foreach (JsonElement item in page.GetProperty("items").EnumerateArray())
            {
                if (item.GetProperty("kind").GetString() == kind)
                {
                    return item;
                }
            }

            Assert.True(DateTimeOffset.UtcNow < deadline, $"No {kind} notification within 30 seconds.");

            await Task.Delay(100);
        }
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        JsonElement body = await ReadAsync(response, ensureSuccess: false);

        return body.TryGetProperty("code", out JsonElement code) ? code.GetString() : null;
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, bool ensureSuccess = true)
    {
        if (ensureSuccess)
        {
            response.EnsureSuccessStatusCode();
        }

        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.Clone();
    }
}
