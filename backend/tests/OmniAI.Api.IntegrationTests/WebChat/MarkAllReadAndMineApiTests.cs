using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OmniAI.Api.IntegrationTests.AiAgents;
using OmniAI.Api.IntegrationTests.Realtime;

namespace OmniAI.Api.IntegrationTests.WebChat;

/// <summary>
/// Phase 39.1: "mark all as read" with its count (note 6) and the inbox's "Mine"
/// filter (BA39-5), over HTTP and the real hub.
/// </summary>
public sealed class MarkAllReadAndMineApiTests(WebChatApiFixture fixture)
    : IClassFixture<WebChatApiFixture>
{
    private const string ChatRoot = "/api/public/webchat";

    private static readonly TimeSpan Arrival = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Silence = TimeSpan.FromSeconds(1);

    private static int _visitors;

    // ---- mark all read --------------------------------------------------------

    [Fact]
    public async Task Read_all_clears_unread_for_the_caller_only_and_counts_what_it_marked()
    {
        (HttpClient owner, Guid tenantId, string key) = await WorkspaceAsync();
        HttpClient colleague = await fixture.CreateRestrictedMemberAsync(tenantId, "conversations.view");

        List<Guid> conversations = [];

        for (int index = 0; index < 3; index++)
        {
            conversations.Add((await StartAsync(key, $"Hello {index}")).ConversationId);
        }

        // One the owner already read: not counted, not moved.
        long newest = await NewestAsync(owner, tenantId, conversations[0]);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsJsonAsync(Inbox(tenantId, $"/{conversations[0]}/read"), new { sequence = newest })).StatusCode);

        Assert.Equal(2, await UnreadCountAsync(owner, tenantId));
        Assert.Equal(3, await UnreadCountAsync(colleague, tenantId));
        Assert.Equal(await UnreadRowsAsync(owner, tenantId), await UnreadCountAsync(owner, tenantId));

        Assert.Equal(2, await ReadAllAsync(owner, tenantId));

        Assert.Equal(0, await UnreadCountAsync(owner, tenantId));
        Assert.Equal(0, await UnreadRowsAsync(owner, tenantId));

        // Each marker sits on its conversation's newest message.
        foreach (Guid conversationId in conversations)
        {
            JsonElement detail = await ReadAsync(owner, Inbox(tenantId, $"/{conversationId}"));
            Assert.False(detail.GetProperty("unread").GetBoolean());
            Assert.Equal(await NewestAsync(owner, tenantId, conversationId), detail.GetProperty("lastReadSequence").GetInt64());
        }

        // The colleague's own unread is untouched.
        Assert.Equal(3, await UnreadCountAsync(colleague, tenantId));
        Assert.Equal(3, await UnreadRowsAsync(colleague, tenantId));
    }

    [Fact]
    public async Task Read_all_is_idempotent_and_a_new_message_makes_one_unread_again()
    {
        (HttpClient owner, Guid tenantId, string key) = await WorkspaceAsync();
        (Guid conversationId, HttpClient visitor) = await StartAsync(key, "Hello");
        await StartAsync(key, "Hello again");

        Assert.Equal(2, await ReadAllAsync(owner, tenantId));
        Assert.Equal(0, await ReadAllAsync(owner, tenantId));

        await SendAsync(visitor, conversationId, "One more thing");

        Assert.Equal(1, await UnreadCountAsync(owner, tenantId));
        Assert.Equal(1, await ReadAllAsync(owner, tenantId));
        Assert.Equal(0, await UnreadCountAsync(owner, tenantId));
    }

    [Fact]
    public async Task An_empty_workspace_marks_nothing()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        Assert.Equal(0, await UnreadCountAsync(owner, tenantId));
        Assert.Equal(0, await ReadAllAsync(owner, tenantId));
    }

    [Fact]
    public async Task Another_workspace_is_told_nothing_exists_and_its_conversations_are_never_touched()
    {
        (HttpClient owner, Guid tenantId, string key) = await WorkspaceAsync();
        await StartAsync(key, "Hello");

        (HttpClient rival, Guid rivalTenantId, _) = await fixture.CreateOwnerAsync("Rival Unread Trading");

        Assert.Equal(HttpStatusCode.NotFound, (await rival.PostAsync(Inbox(tenantId, "/read-all"), content: null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await rival.GetAsync(Inbox(tenantId, "/unread-count"))).StatusCode);

        // Their own read-all marks nothing of ours.
        Assert.Equal(0, await ReadAllAsync(rival, rivalTenantId));
        Assert.Equal(1, await UnreadCountAsync(owner, tenantId));
    }

    [Fact]
    public async Task A_member_without_conversations_view_is_refused_and_an_anonymous_caller_too()
    {
        (_, Guid tenantId, string key) = await WorkspaceAsync();
        await StartAsync(key, "Hello");
        HttpClient blind = await fixture.CreateRestrictedMemberAsync(tenantId, "customers.view");

        Assert.Equal(HttpStatusCode.Forbidden, (await blind.PostAsync(Inbox(tenantId, "/read-all"), content: null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await blind.GetAsync(Inbox(tenantId, "/unread-count"))).StatusCode);

        HttpClient anonymous = fixture.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Inbox(tenantId, "/unread-count"))).StatusCode);
    }

    [Fact]
    public async Task Read_all_needs_the_antiforgery_token()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        owner.DefaultRequestHeaders.Remove(ApiAuthentication.AntiforgeryHeader);

        HttpResponseMessage refused = await owner.PostAsync(Inbox(tenantId, "/read-all"), content: null);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    [Fact]
    public async Task The_caller_s_own_sessions_hear_conversations_read_and_nobody_else_does()
    {
        (HttpClient owner, Guid tenantId, string key) = await WorkspaceAsync();
        string ownerEmail = await AiAgentApiFixture.CurrentEmailAsync(owner);

        string colleagueEmail = ApiAuthentication.UniqueEmail();
        await fixture.CreateMemberWithEmailAsync(tenantId, colleagueEmail, "conversations.view");

        await StartAsync(key, "Hello");

        await using InboxHubClient ownerTab = await InboxHubClient.SignInAndConnectAsync(fixture.Application, ownerEmail);
        await using InboxHubClient colleagueTab = await InboxHubClient.SignInAndConnectAsync(fixture.Application, colleagueEmail);

        Assert.Null(await ownerTab.InvokeAsync("SubscribeNotifications", tenantId));
        Assert.Null(await colleagueTab.InvokeAsync("SubscribeNotifications", tenantId));
        Assert.Null(await colleagueTab.InvokeAsync("Subscribe", tenantId));

        Assert.Equal(1, await ReadAllAsync(owner, tenantId));

        // The payload is the workspace id and nothing else.
        Assert.True(await ownerTab.WaitForConversationsReadAsync(tenantId, Arrival));
        Assert.False(await colleagueTab.WaitForConversationsReadAsync(tenantId, Silence));

        // Nothing moved the second time: nothing is announced.
        Assert.Equal(0, await ReadAllAsync(owner, tenantId));
        Assert.False(await ownerTab.WaitForConversationsReadAsync(tenantId, Silence));
    }

    // ---- filter=mine ---------------------------------------------------------------

    [Fact]
    public async Task Mine_lists_what_is_assigned_to_or_taken_over_by_the_reader_and_nothing_else()
    {
        (HttpClient owner, Guid tenantId, string key) = await WorkspaceAsync();
        Guid ownerId = await IdAsync(owner);

        string colleagueEmail = ApiAuthentication.UniqueEmail();
        (HttpClient colleague, Guid colleagueId) = await fixture.CreateMemberWithEmailAsync(
            tenantId, colleagueEmail, "conversations.view", "conversations.reply");

        Guid assignedOnly = (await StartAsync(key, "Assigned only")).ConversationId;
        Guid takenOverOnly = (await StartAsync(key, "Taken over only")).ConversationId;
        Guid both = (await StartAsync(key, "Both")).ConversationId;
        Guid neither = (await StartAsync(key, "Neither")).ConversationId;
        Guid colleagues = (await StartAsync(key, "The colleague's")).ConversationId;

        await AssignAsync(owner, tenantId, assignedOnly, ownerId);
        await HandoffAsync(owner, tenantId, takenOverOnly, "take-over");
        await HandoffAsync(owner, tenantId, both, "take-over");
        await AssignAsync(owner, tenantId, both, ownerId);
        await AssignAsync(owner, tenantId, colleagues, colleagueId);

        // Taken over and handed back: no longer the reader's by takeover.
        Guid returned = (await StartAsync(key, "Returned")).ConversationId;
        await HandoffAsync(owner, tenantId, returned, "take-over");
        await HandoffAsync(owner, tenantId, returned, "return-to-ai");

        JsonElement mine = await ReadAsync(owner, Inbox(tenantId, "?filter=mine&pageSize=50"));
        HashSet<Guid> ids = Ids(mine);

        Assert.Equal(new HashSet<Guid> { assignedOnly, takenOverOnly, both }, ids);
        Assert.Equal(3, mine.GetProperty("total").GetInt32());

        // Each row agrees with its chips.
        foreach (JsonElement row in mine.GetProperty("items").EnumerateArray())
        {
            bool assignedToMe = row.GetProperty("assignedTo").ValueKind != JsonValueKind.Null
                && row.GetProperty("assignedTo").GetProperty("userId").GetGuid() == ownerId;
            bool takenOverByMe = row.GetProperty("takenOverBy").ValueKind != JsonValueKind.Null
                && row.GetProperty("takenOverBy").GetProperty("userId").GetGuid() == ownerId;

            Assert.True(assignedToMe || takenOverByMe);
        }

        Assert.DoesNotContain(neither, ids);
        Assert.DoesNotContain(colleagues, ids);
        Assert.DoesNotContain(returned, ids);

        // The colleague's own Mine is theirs: only the one assigned to them.
        Assert.Equal(new HashSet<Guid> { colleagues }, Ids(await ReadAsync(colleague, Inbox(tenantId, "?filter=mine&pageSize=50"))));

        // In any letter case.
        Assert.Equal(ids, Ids(await ReadAsync(owner, Inbox(tenantId, "?filter=MINE&pageSize=50"))));
    }

    [Fact]
    public async Task Mine_pages_with_total_counting_the_filtered_set()
    {
        (HttpClient owner, Guid tenantId, string key) = await WorkspaceAsync();
        Guid ownerId = await IdAsync(owner);

        List<Guid> assigned = [];

        for (int index = 0; index < 3; index++)
        {
            Guid conversationId = (await StartAsync(key, $"Mine {index}")).ConversationId;
            await AssignAsync(owner, tenantId, conversationId, ownerId);
            assigned.Add(conversationId);
        }

        await StartAsync(key, "Not mine");

        JsonElement first = await ReadAsync(owner, Inbox(tenantId, "?filter=mine&page=1&pageSize=2"));
        JsonElement second = await ReadAsync(owner, Inbox(tenantId, "?filter=mine&page=2&pageSize=2"));

        Assert.Equal(3, first.GetProperty("total").GetInt32());
        Assert.Equal(3, second.GetProperty("total").GetInt32());
        Assert.Equal(2, first.GetProperty("items").GetArrayLength());
        Assert.Equal(1, second.GetProperty("items").GetArrayLength());
        Assert.Equal(assigned.ToHashSet(), Ids(first).Union(Ids(second)).ToHashSet());
    }

    [Fact]
    public async Task Needs_person_is_unchanged_and_any_other_filter_is_still_refused()
    {
        (HttpClient owner, Guid tenantId, _) = await WorkspaceAsync();

        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync(Inbox(tenantId, "?filter=needs-person"))).StatusCode);

        foreach (string filter in new[] { "everything", "mine,needs-person", "my" })
        {
            HttpResponseMessage refused = await owner.GetAsync(Inbox(tenantId, $"?filter={Uri.EscapeDataString(filter)}"));

            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Equal("conversations.filter.invalid", (await BodyAsync(refused)).GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task Mine_is_refused_without_conversations_view_and_another_workspace_gets_nothing()
    {
        (HttpClient owner, Guid tenantId, string key) = await WorkspaceAsync();
        Guid conversationId = (await StartAsync(key, "Hello")).ConversationId;
        await AssignAsync(owner, tenantId, conversationId, await IdAsync(owner));

        HttpClient blind = await fixture.CreateRestrictedMemberAsync(tenantId, "customers.view");
        Assert.Equal(HttpStatusCode.Forbidden, (await blind.GetAsync(Inbox(tenantId, "?filter=mine"))).StatusCode);

        (HttpClient rival, Guid rivalTenantId, _) = await fixture.CreateOwnerAsync("Rival Mine Trading");
        Assert.Equal(HttpStatusCode.NotFound, (await rival.GetAsync(Inbox(tenantId, "?filter=mine"))).StatusCode);
        Assert.Equal(0, (await ReadAsync(rival, Inbox(rivalTenantId, "?filter=mine"))).GetProperty("total").GetInt32());
    }

    // ---- helpers ------------------------------------------------------------------

    private sealed record Started(Guid ConversationId);

    private async Task<(HttpClient Owner, Guid TenantId, string Key)> WorkspaceAsync()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Inbox");

        fixture.ResetGenerator().Reply = "The answer.";

        return (owner, tenantId, key);
    }

    private async Task<(Guid ConversationId, HttpClient Visitor)> StartAsync(string key, string firstMessage)
    {
        HttpClient visitor = await fixture.CreateVisitorAsync(
            new IPAddress([203, 0, 113, (byte)(Interlocked.Increment(ref _visitors) % 250 + 1)]));

        HttpResponseMessage started = await visitor.PostAsync(WebChatApiFixture.Relative($"{ChatRoot}/{key}/conversations"), content: null);
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);

        Guid conversationId = (await started.Content.ReadFromJsonAsync<Started>())!.ConversationId;

        await SendAsync(visitor, conversationId, firstMessage);

        return (conversationId, visitor);
    }

    private static async Task SendAsync(HttpClient visitor, Guid conversationId, string text)
    {
        HttpResponseMessage sent = await visitor.PostAsJsonAsync(
            WebChatApiFixture.Relative($"{ChatRoot}/conversations/{conversationId}/messages"),
            new { clientMessageId = Guid.NewGuid().ToString("N"), text });

        Assert.True(sent.IsSuccessStatusCode, $"A visitor message answered {(int)sent.StatusCode}.");
    }

    private static async Task<Guid> IdAsync(HttpClient client) =>
        (await ReadAsync(client, WebChatApiFixture.Relative("/api/auth/me"))).GetProperty("id").GetGuid();

    private static async Task<int> ReadAllAsync(HttpClient client, Guid tenantId)
    {
        HttpResponseMessage response = await client.PostAsync(Inbox(tenantId, "/read-all"), content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await BodyAsync(response)).GetProperty("marked").GetInt32();
    }

    private static async Task<int> UnreadCountAsync(HttpClient client, Guid tenantId) =>
        (await ReadAsync(client, Inbox(tenantId, "/unread-count"))).GetProperty("count").GetInt32();

    private static async Task<int> UnreadRowsAsync(HttpClient client, Guid tenantId) =>
        (await ReadAsync(client, Inbox(tenantId, "?pageSize=50"))).GetProperty("items").EnumerateArray()
            .Count(item => item.GetProperty("unread").GetBoolean());

    private static async Task<long> NewestAsync(HttpClient client, Guid tenantId, Guid conversationId) =>
        (await ReadAsync(client, Inbox(tenantId, $"/{conversationId}/messages?tail=true")))
            .GetProperty("items").EnumerateArray().Last().GetProperty("sequence").GetInt64();

    private static async Task AssignAsync(HttpClient owner, Guid tenantId, Guid conversationId, Guid member)
    {
        using HttpRequestMessage request = new(HttpMethod.Put, Inbox(tenantId, $"/{conversationId}/assignment"))
        {
            Content = JsonContent.Create(new { member }),
        };
        request.Headers.TryAddWithoutValidation("If-Match", await VersionAsync(owner, tenantId, conversationId));

        Assert.Equal(HttpStatusCode.OK, (await owner.SendAsync(request)).StatusCode);
    }

    private static async Task HandoffAsync(HttpClient owner, Guid tenantId, Guid conversationId, string action)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, Inbox(tenantId, $"/{conversationId}/{action}"));
        request.Headers.TryAddWithoutValidation("If-Match", await VersionAsync(owner, tenantId, conversationId));

        Assert.Equal(HttpStatusCode.OK, (await owner.SendAsync(request)).StatusCode);
    }

    private static async Task<string> VersionAsync(HttpClient owner, Guid tenantId, Guid conversationId)
    {
        HttpResponseMessage read = await owner.GetAsync(Inbox(tenantId, $"/{conversationId}"));
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        return read.Headers.ETag!.ToString();
    }

    private static HashSet<Guid> Ids(JsonElement page) =>
        [.. page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid())];

    private static Uri Inbox(Guid tenantId, string suffix) =>
        WebChatApiFixture.Relative($"/api/tenants/{tenantId}/conversations{suffix}");

    private static async Task<JsonElement> ReadAsync(HttpClient client, Uri address)
    {
        HttpResponseMessage response = await client.GetAsync(address);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await BodyAsync(response);
    }

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
}
