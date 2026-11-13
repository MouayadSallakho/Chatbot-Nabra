using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OmniAI.Api.IntegrationTests.WebChat;

namespace OmniAI.Api.IntegrationTests.ErrorHandling;

/// <summary>
/// Phase 38.7b, BA-4: the customer, conversation, notification and report
/// refusals that answered the generic <c>validation.failed</c> or
/// <c>state.conflict</c> now reach the client with their own codes.
/// </summary>
/// <remarks>
/// Through the real routes, with customers and conversations made by a real
/// visitor's chat. Only the code is new: each refusal keeps the status it had,
/// which these tests pin beside it.
/// </remarks>
public sealed class AreaProblemCodeApiTests(WebChatApiFixture fixture)
    : IClassFixture<WebChatApiFixture>
{
    private const string ChatRoot = "/api/public/webchat";

    // Documentation addresses (RFC 5737), apart from the other suites' visitors,
    // so the public chat's start limit is never shared with them.
    private static int _visitors;

    [Fact]
    public async Task Customer_refusals_name_their_rules()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (Guid first, Guid second) = await TwoCustomersAsync(owner, tenantId);

        await ExpectAsync(
            await owner.GetAsync(Url($"/api/tenants/{tenantId}/customers?pageSize=1000")),
            HttpStatusCode.BadRequest,
            "customers.page.invalid");

        await ExpectAsync(
            await owner.GetAsync(Url($"/api/tenants/{tenantId}/customers/{Guid.Empty}")),
            HttpStatusCode.BadRequest,
            "customers.id.invalid");

        await ExpectAsync(
            await owner.PostAsJsonAsync(Url($"/api/tenants/{tenantId}/customers/{first}/merge"), new { }),
            HttpStatusCode.BadRequest,
            "customers.merge.target_required");

        await ExpectAsync(
            await SendAsync(owner, HttpMethod.Post, $"/api/tenants/{tenantId}/customers/{first}/merge", new { targetCustomerId = first }, "\"any\""),
            HttpStatusCode.BadRequest,
            "customers.merge.self");

        string tag = await EtagAsync(owner, $"/api/tenants/{tenantId}/customers/{first}");

        // A blank name clears the name (unchanged), so the rules a rename can
        // break are the length and the characters.
        await ExpectAsync(
            await SendAsync(owner, HttpMethod.Put, $"/api/tenants/{tenantId}/customers/{first}/display-name", new { displayName = new string('n', 121) }, tag),
            HttpStatusCode.BadRequest,
            "customers.display_name.too_long");

        await ExpectAsync(
            await SendAsync(owner, HttpMethod.Put, $"/api/tenants/{tenantId}/customers/{first}/display-name", new { displayName = "Line\nbreak" }, tag),
            HttpStatusCode.BadRequest,
            "customers.display_name.control_characters");

        // Merge the first into the second, then address the first again: the
        // existing 409 now names its rule, and keeps both ids it always carried.
        HttpResponseMessage merged = await SendAsync(
            owner,
            HttpMethod.Post,
            $"/api/tenants/{tenantId}/customers/{first}/merge",
            new { targetCustomerId = second },
            tag);
        Assert.Equal(HttpStatusCode.OK, merged.StatusCode);

        HttpResponseMessage renameMerged = await SendAsync(
            owner,
            HttpMethod.Put,
            $"/api/tenants/{tenantId}/customers/{first}/display-name",
            new { displayName = "Late" },
            await EtagAsync(owner, $"/api/tenants/{tenantId}/customers/{first}"));

        JsonElement problem = await ExpectAsync(renameMerged, HttpStatusCode.Conflict, "customers.merged");
        Assert.Equal(first, problem.GetProperty("customerId").GetGuid());
        Assert.Equal(second, problem.GetProperty("canonicalCustomerId").GetGuid());
    }

    [Fact]
    public async Task Conversation_refusals_name_their_rules()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Codes");
        Guid conversationId = await StartAsync(key);
        string conversation = $"/api/tenants/{tenantId}/conversations/{conversationId}";

        await ExpectAsync(
            await owner.GetAsync(Url($"/api/tenants/{tenantId}/conversations/{Guid.Empty}")),
            HttpStatusCode.BadRequest,
            "conversations.id.invalid");

        // The AI is answering it: a reply is refused until somebody takes it over.
        await ExpectAsync(
            await owner.PostAsJsonAsync(Url($"{conversation}/messages"), new { clientMessageId = "c-1", text = "Hello" }),
            HttpStatusCode.Conflict,
            "conversations.reply.not_taken_over");

        await ExpectAsync(
            await owner.PostAsJsonAsync(Url($"{conversation}/messages"), new { clientMessageId = "not valid!", text = "Hello" }),
            HttpStatusCode.BadRequest,
            "conversations.client_message_id.invalid");

        await ExpectAsync(
            await SendAsync(owner, HttpMethod.Put, $"{conversation}/assignment", new { member = Guid.CreateVersion7() }, await EtagAsync(owner, conversation)),
            HttpStatusCode.UnprocessableEntity,
            "conversations.assignee.not_member");

        HttpResponseMessage taken = await SendAsync(owner, HttpMethod.Post, $"{conversation}/take-over", null, await EtagAsync(owner, conversation));
        Assert.Equal(HttpStatusCode.OK, taken.StatusCode);

        await ExpectAsync(
            await owner.PostAsJsonAsync(Url($"{conversation}/messages"), new { clientMessageId = "c-2", text = "   " }),
            HttpStatusCode.BadRequest,
            "conversations.message.required");
    }

    [Fact]
    public async Task Notification_refusals_name_their_rules()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        await ExpectAsync(
            await owner.GetAsync(Url($"/api/tenants/{tenantId}/notifications?pageSize=1000")),
            HttpStatusCode.BadRequest,
            "notifications.page.invalid");

        await ExpectAsync(
            await SendAsync(owner, HttpMethod.Put, $"/api/tenants/{tenantId}/notifications/preferences", new { email = new { reports = true } }, "\"any\""),
            HttpStatusCode.BadRequest,
            "notifications.preferences.incomplete");

        // An all-zero notification id is still "not yours" (404), as before.
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await owner.PostAsync(Url($"/api/tenants/{tenantId}/notifications/{Guid.Empty}/read"), content: null)).StatusCode);
    }

    [Fact]
    public async Task Report_and_schedule_refusals_name_their_rules()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        string reports = $"/api/tenants/{tenantId}/reports";

        (object Body, string Code)[] requests =
        [
            (new { reportType = "NoSuchReport", format = "Csv", range = "30d", clientRequestId = Guid.NewGuid() }, "reports.type.invalid"),
            (new { reportType = "ChannelUsage", format = "Docx", range = "30d", clientRequestId = Guid.NewGuid() }, "reports.format.invalid"),
            (new { reportType = "ChannelUsage", format = "Csv", range = "30d" }, "reports.client_request_id.required"),
            (new { reportType = "ChannelUsage", format = "Csv", range = "1y", clientRequestId = Guid.NewGuid() }, "reports.range.invalid"),
            (new { reportType = "ChannelUsage", format = "Csv", range = "custom", from = "2026-10-01", clientRequestId = Guid.NewGuid() }, "reports.range.dates_required"),
            (new { reportType = "ChannelUsage", format = "Csv", range = "30d", from = "2026-10-01", to = "2026-10-02", clientRequestId = Guid.NewGuid() }, "reports.range.conflict"),
            (new { reportType = "ChannelUsage", format = "Csv", range = "custom", from = "2026-10-05", to = "2026-10-01", clientRequestId = Guid.NewGuid() }, "reports.period.reversed"),
        ];

        foreach ((object body, string code) in requests)
        {
            await ExpectAsync(await owner.PostAsync(Url(reports), JsonContent.Create(body, body.GetType())), HttpStatusCode.BadRequest, code);
        }

        await ExpectAsync(
            await owner.GetAsync(Url($"{reports}/executions?pageSize=1000")),
            HttpStatusCode.BadRequest,
            "reports.page.invalid");

        await ExpectAsync(
            await owner.PostAsJsonAsync(Url($"{reports}/schedules"), new { name = " ", reportType = "ChannelUsage", format = "Csv", frequency = "Weekly", enabled = true }),
            HttpStatusCode.BadRequest,
            "reports.schedule.name.required");

        await ExpectAsync(
            await owner.PostAsJsonAsync(Url($"{reports}/schedules"), new { name = "Daily", reportType = "ChannelUsage", format = "Csv", frequency = "Daily", enabled = true }),
            HttpStatusCode.BadRequest,
            "reports.frequency.invalid");

        await ExpectAsync(
            await SendAsync(owner, HttpMethod.Put, $"{reports}/schedules/{Guid.CreateVersion7()}/enabled", new { }, "\"any\""),
            HttpStatusCode.BadRequest,
            "reports.schedule.enabled_required");
    }

    [Fact]
    public async Task A_refusal_still_on_a_generic_code_keeps_it()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        // The analytics range is outside these areas and keeps validation.failed
        // (problem-codes.md, Analytics), exactly as before.
        await ExpectAsync(
            await owner.GetAsync(Url($"/api/tenants/{tenantId}/analytics/dashboard?range=1y")),
            HttpStatusCode.BadRequest,
            "validation.failed");
    }

    private async Task<(Guid First, Guid Second)> TwoCustomersAsync(HttpClient owner, Guid tenantId)
    {
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Directory");
        await StartAsync(key);
        await StartAsync(key);

        HttpResponseMessage page = await owner.GetAsync(Url($"/api/tenants/{tenantId}/customers"));
        using JsonDocument body = JsonDocument.Parse(await page.Content.ReadAsStringAsync());
        Guid[] ids = [.. body.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid())];

        Assert.Equal(2, ids.Length);

        return (ids[0], ids[1]);
    }

    private async Task<Guid> StartAsync(string key)
    {
        HttpClient visitor = await fixture.CreateVisitorAsync(
            new IPAddress([192, 0, 2, (byte)(Interlocked.Increment(ref _visitors) % 250 + 1)]));

        HttpResponseMessage started = await visitor.PostAsync(Url($"{ChatRoot}/{key}/conversations"), content: null);

        Assert.Equal(HttpStatusCode.OK, started.StatusCode);

        using JsonDocument body = JsonDocument.Parse(await started.Content.ReadAsStringAsync());

        return body.RootElement.GetProperty("conversationId").GetGuid();
    }

    private static async Task<string> EtagAsync(HttpClient client, string path)
    {
        HttpResponseMessage read = await client.GetAsync(Url(path));

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        return read.Headers.ETag!.ToString();
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        object? body,
        string ifMatch)
    {
        using HttpRequestMessage request = new(method, Url(path));

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType());
        }

        request.Headers.TryAddWithoutValidation("If-Match", ifMatch);

        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> ExpectAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        string text = await response.Content.ReadAsStringAsync();

        Assert.True(status == response.StatusCode, $"Expected {(int)status} {code}, got {(int)response.StatusCode}: {text}");

        using JsonDocument body = JsonDocument.Parse(text);

        Assert.Equal(code, body.RootElement.GetProperty("code").GetString());

        return body.RootElement.Clone();
    }

    private static Uri Url(string path) => WebChatApiFixture.Relative(path);
}
