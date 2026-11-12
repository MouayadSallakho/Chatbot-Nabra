using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OmniAI.Api.IntegrationTests.AiAgents;

namespace OmniAI.Api.IntegrationTests.WebChat;

/// <summary>
/// Phase 38.7a over HTTP: who handles a conversation (<c>takenOverBy</c>, BA-2) and
/// why it needs a person (<c>needsPerson</c>, <c>needsPersonReason</c>, BA-7), on
/// the list and the detail.
/// </summary>
/// <remarks>
/// Every conversation is produced by a visitor on the real public API and every
/// handling change by the real takeover and return routes, so the events the
/// reads name are the ones the runtime stored. Branches no route can produce (a
/// closed conversation) are proved against PostgreSQL in <c>InboxReadModelTests</c>.
/// </remarks>
public sealed class HandlingFactsApiTests(WebChatApiFixture fixture)
    : IClassFixture<WebChatApiFixture>
{
    private const string ChatRoot = "/api/public/webchat";

    // Each visitor gets its own network (documentation range), so this class's
    // conversations do not add up against one client's public-chat limits.
    private static int _visitors;

    // ---- Who handles it (BA-2) ----------------------------------------------

    [Fact]
    public async Task The_newest_takeover_names_who_handles_it_and_a_return_clears_it()
    {
        (HttpClient owner, Guid tenantId, string key) = await WorkspaceAsync();
        HttpClient colleague = await fixture.CreateRestrictedMemberAsync(
            tenantId, "conversations.view", "conversations.handoff");
        string ownerEmail = await AiAgentApiFixture.CurrentEmailAsync(owner);
        string colleagueEmail = await AiAgentApiFixture.CurrentEmailAsync(colleague);

        (Guid conversationId, _) = await StartAsync(key, "Hello");

        // The AI handles it: nobody.
        await AssertTakenOverByAsync(owner, tenantId, conversationId, null);

        await HandoffAsync(owner, tenantId, conversationId, "take-over");
        await AssertTakenOverByAsync(owner, tenantId, conversationId, ownerEmail);

        await HandoffAsync(owner, tenantId, conversationId, "return-to-ai");
        await AssertTakenOverByAsync(owner, tenantId, conversationId, null);

        // Taken over again, by somebody else: the newest event wins.
        await HandoffAsync(colleague, tenantId, conversationId, "take-over");
        await AssertTakenOverByAsync(owner, tenantId, conversationId, colleagueEmail);

        // The colleague reads the same thing about themselves.
        await AssertTakenOverByAsync(colleague, tenantId, conversationId, colleagueEmail);
    }

    [Fact]
    public async Task Another_workspaces_takeovers_never_show_and_its_reads_find_nothing()
    {
        (HttpClient owner, Guid tenantId, string key) = await WorkspaceAsync();
        (HttpClient rival, Guid rivalTenantId, _) = await fixture.CreateOwnerAsync("Rival Handling Trading");
        (string rivalKey, _) = await WebChatApiFixture.PublishChatAsync(rival, rivalTenantId, "Rival");
        string rivalEmail = await AiAgentApiFixture.CurrentEmailAsync(rival);

        (Guid ours, _) = await StartAsync(key, "Hello");
        (Guid theirs, _) = await StartAsync(rivalKey, "Hello");

        await HandoffAsync(owner, tenantId, ours, "take-over");
        await HandoffAsync(rival, rivalTenantId, theirs, "take-over");

        JsonElement page = await ReadJsonAsync(owner, List(tenantId, string.Empty));

        Assert.DoesNotContain(rivalEmail, page.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            page.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("id").GetGuid() == theirs);

        // The rival cannot read ours, with or without the new fields.
        HttpResponseMessage refused = await rival.GetAsync(Detail(tenantId, ours));

        Assert.True(
            refused.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden,
            $"Another workspace's read answered {(int)refused.StatusCode}.");
    }

    [Fact]
    public async Task A_member_without_conversations_view_gets_neither_the_list_nor_the_detail()
    {
        (HttpClient owner, Guid tenantId, string key) = await WorkspaceAsync();
        (Guid conversationId, _) = await StartAsync(key, "Hello");
        await HandoffAsync(owner, tenantId, conversationId, "take-over");

        HttpClient blind = await fixture.CreateRestrictedMemberAsync(tenantId, "customers.view");

        Assert.False((await blind.GetAsync(List(tenantId, string.Empty))).IsSuccessStatusCode);
        Assert.False((await blind.GetAsync(Detail(tenantId, conversationId))).IsSuccessStatusCode);
    }

    // ---- Why it needs a person (BA-7) ---------------------------------------

    [Fact]
    public async Task Each_reason_is_named_and_the_flag_agrees_with_the_filter()
    {
        (HttpClient owner, Guid tenantId, string key) = await WorkspaceAsync();

        // Answered by the AI: nobody needed.
        Guid answered = (await StartAsync(key, "Opening hours?")).ConversationId;

        // The AI's turn failed.
        (Guid failed, HttpClient failedVisitor) = await StartAsync(key, "Hello");
        fixture.Generator.PretendUnavailable = true;
        await SendAsync(failedVisitor, failed, "Refund please", expectFailure: true);
        fixture.Generator.PretendUnavailable = false;

        // A person handles it, and the customer wrote last.
        (Guid handled, HttpClient handledVisitor) = await StartAsync(key, "Hello");
        await HandoffAsync(owner, tenantId, handled, "take-over");
        await SendAsync(handledVisitor, handled, "Still there?");

        // A person handles it and answered last.
        (Guid replied, HttpClient repliedVisitor) = await StartAsync(key, "Hello");
        await HandoffAsync(owner, tenantId, replied, "take-over");
        await SendAsync(repliedVisitor, replied, "Question");
        await ReplyAsync(owner, tenantId, replied, "Answered.");

        // Handed back to the AI before anybody answered.
        (Guid returned, HttpClient returnedVisitor) = await StartAsync(key, "Hello");
        await HandoffAsync(owner, tenantId, returned, "take-over");
        await SendAsync(returnedVisitor, returned, "I need a person, please");
        await HandoffAsync(owner, tenantId, returned, "return-to-ai");

        // The AI's turn failed, and then a person took it over: the person wins.
        (Guid failedThenTaken, HttpClient failedThenTakenVisitor) = await StartAsync(key, "Hello");
        fixture.Generator.PretendUnavailable = true;
        await SendAsync(failedThenTakenVisitor, failedThenTaken, "Refund please", expectFailure: true);
        fixture.Generator.PretendUnavailable = false;
        await HandoffAsync(owner, tenantId, failedThenTaken, "take-over");

        Dictionary<Guid, string?> expected = new()
        {
            [answered] = null,
            [failed] = "AiTurnFailed",
            [handled] = "PersonHandling",
            [replied] = null,
            [returned] = "ReturnedUnanswered",
            [failedThenTaken] = "PersonHandling",
        };

        Dictionary<Guid, JsonElement> rows = (await ReadJsonAsync(owner, List(tenantId, "pageSize=50")))
            .GetProperty("items").EnumerateArray()
            .ToDictionary(item => item.GetProperty("id").GetGuid(), item => item.Clone());

        foreach ((Guid id, string? reason) in expected)
        {
            JsonElement row = rows[id];
            JsonElement detail = await ReadJsonAsync(owner, Detail(tenantId, id));

            foreach (JsonElement read in new[] { row, detail })
            {
                Assert.Equal(reason is not null, read.GetProperty("needsPerson").GetBoolean());
                Assert.Equal(reason, ReasonOf(read));
            }
        }

        // The flag and the filter, on the same data: the same set, and the same total.
        JsonElement filtered = await ReadJsonAsync(owner, List(tenantId, "filter=needs-person&pageSize=50"));
        HashSet<Guid> byFilter = [.. filtered.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid())];
        HashSet<Guid> byFlag = [.. rows.Where(row => row.Value.GetProperty("needsPerson").GetBoolean()).Select(row => row.Key)];

        Assert.True(byFilter.SetEquals(byFlag), "The flag and the filter disagree.");
        Assert.Equal(byFlag.Count, filtered.GetProperty("total").GetInt32());
        Assert.All(
            filtered.GetProperty("items").EnumerateArray(),
            item => Assert.Equal(expected[item.GetProperty("id").GetGuid()], ReasonOf(item)));
    }

    // ---- Helpers --------------------------------------------------------------

    private sealed record Started(Guid ConversationId);

    private async Task<(HttpClient Owner, Guid TenantId, string Key)> WorkspaceAsync()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Handling");

        fixture.ResetGenerator().Reply = "The answer.";

        return (owner, tenantId, key);
    }

    private async Task<(Guid ConversationId, HttpClient Visitor)> StartAsync(string key, string firstMessage)
    {
        HttpClient visitor = await fixture.CreateVisitorAsync(
            new IPAddress([198, 51, 100, (byte)(Interlocked.Increment(ref _visitors) % 250 + 1)]));

        HttpResponseMessage started = await visitor.PostAsync(
            WebChatApiFixture.Relative($"{ChatRoot}/{key}/conversations"),
            content: null);

        Assert.Equal(HttpStatusCode.OK, started.StatusCode);

        Guid conversationId = (await started.Content.ReadFromJsonAsync<Started>())!.ConversationId;

        await SendAsync(visitor, conversationId, firstMessage);

        return (conversationId, visitor);
    }

    private static async Task SendAsync(
        HttpClient visitor,
        Guid conversationId,
        string text,
        bool expectFailure = false)
    {
        HttpResponseMessage sent = await visitor.PostAsJsonAsync(
            WebChatApiFixture.Relative($"{ChatRoot}/conversations/{conversationId}/messages"),
            new { clientMessageId = Guid.NewGuid().ToString("N"), text });

        Assert.Equal(!expectFailure, sent.IsSuccessStatusCode);
    }

    private static async Task ReplyAsync(HttpClient owner, Guid tenantId, Guid conversationId, string text)
    {
        HttpResponseMessage sent = await owner.PostAsJsonAsync(
            WebChatApiFixture.Relative($"/api/tenants/{tenantId}/conversations/{conversationId}/messages"),
            new { clientMessageId = Guid.NewGuid().ToString("N"), text });

        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
    }

    private static async Task HandoffAsync(HttpClient client, Guid tenantId, Guid conversationId, string action)
    {
        HttpResponseMessage read = await client.GetAsync(Detail(tenantId, conversationId));

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        using HttpRequestMessage request = new(
            HttpMethod.Post,
            WebChatApiFixture.Relative($"/api/tenants/{tenantId}/conversations/{conversationId}/{action}"));

        request.Headers.TryAddWithoutValidation("If-Match", read.Headers.ETag!.ToString());

        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request)).StatusCode);
    }

    /// <summary>The list row and the detail both name the same member, or both nobody.</summary>
    private static async Task AssertTakenOverByAsync(
        HttpClient client,
        Guid tenantId,
        Guid conversationId,
        string? email)
    {
        JsonElement row = (await ReadJsonAsync(client, List(tenantId, "pageSize=50")))
            .GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("id").GetGuid() == conversationId);
        JsonElement detail = await ReadJsonAsync(client, Detail(tenantId, conversationId));

        foreach (JsonElement read in new[] { row, detail })
        {
            JsonElement takenOverBy = read.GetProperty("takenOverBy");

            if (email is null)
            {
                Assert.Equal(JsonValueKind.Null, takenOverBy.ValueKind);
            }
            else
            {
                Assert.Equal(email, takenOverBy.GetProperty("email").GetString(), ignoreCase: true);
                Assert.NotEqual(Guid.Empty, takenOverBy.GetProperty("userId").GetGuid());
            }
        }

        Assert.Equal(row.GetProperty("takenOverBy").GetRawText(), detail.GetProperty("takenOverBy").GetRawText());
    }

    private static string? ReasonOf(JsonElement read) =>
        read.GetProperty("needsPersonReason") is { ValueKind: JsonValueKind.String } reason
            ? reason.GetString()
            : null;

    private static async Task<JsonElement> ReadJsonAsync(HttpClient client, Uri address)
    {
        HttpResponseMessage response = await client.GetAsync(address);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static Uri List(Guid tenantId, string query) =>
        WebChatApiFixture.Relative($"/api/tenants/{tenantId}/conversations?{query}");

    private static Uri Detail(Guid tenantId, Guid conversationId) =>
        WebChatApiFixture.Relative($"/api/tenants/{tenantId}/conversations/{conversationId}");
}
