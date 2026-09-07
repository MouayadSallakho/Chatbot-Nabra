using System.Net;
using System.Net.Http.Json;

namespace OmniAI.Api.IntegrationTests.WebChat;

/// <summary>
/// Reading real conversations from the admin.
/// </summary>
/// <remarks>
/// Every conversation these tests read was produced by a visitor talking to the
/// hosted chat through the public API. Nothing is seeded straight into the
/// database, so what the admin sees is what the runtime actually stored.
/// </remarks>
public sealed class ConversationAdminApiTests(WebChatApiFixture fixture)
    : IClassFixture<WebChatApiFixture>
{
    private const string ChatRoot = "/api/public/webchat";

    private sealed record StartedConversation(Guid ConversationId, string AgentName);

    private sealed record ConversationSummary(
        Guid Id,
        string Channel,
        string Status,
        DateTimeOffset StartedAt,
        DateTimeOffset LastActivityAt,
        string? AgentName,
        int MessageCount,
        string? LastMessagePreview,
        string? LastMessageSender);

    private sealed record ConversationPage(
        IReadOnlyList<ConversationSummary> Items,
        int Page,
        int PageSize,
        int Total);

    private sealed record TranscriptMessage(
        Guid Id,
        string Sender,
        string Text,
        DateTimeOffset SentAt,
        long Sequence);

    private sealed record TranscriptPage(
        IReadOnlyList<TranscriptMessage> Items,
        long? NextCursor);

    private static Uri Conversations(Guid tenantId) =>
        WebChatApiFixture.Relative($"/api/tenants/{tenantId}/conversations");

    // ---- Authorization matrix -------------------------------------------

    [Fact]
    public async Task An_anonymous_caller_is_refused()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await fixture.CreateClient().GetAsync(Conversations(tenantId))).StatusCode);
    }

    [Fact]
    public async Task A_non_member_gets_not_found()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (HttpClient outsider, _, _) = await fixture.CreateOwnerAsync("Rival Trading");

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await outsider.GetAsync(Conversations(tenantId))).StatusCode);
    }

    [Fact]
    public async Task A_member_without_the_permission_is_forbidden()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        HttpClient member = await fixture.CreateRestrictedMemberAsync(tenantId, "workspace.view");

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await member.GetAsync(Conversations(tenantId))).StatusCode);
    }

    [Fact]
    public async Task A_member_with_conversations_view_may_read()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        HttpClient reader = await fixture.CreateRestrictedMemberAsync(
            tenantId,
            "conversations.view");

        Assert.Equal(
            HttpStatusCode.OK,
            (await reader.GetAsync(Conversations(tenantId))).StatusCode);
    }

    [Fact]
    public async Task Reading_conversations_does_not_imply_administering_the_channel()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        HttpClient reader = await fixture.CreateRestrictedMemberAsync(
            tenantId,
            "conversations.view");

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await reader.GetAsync(
                WebChatApiFixture.Relative($"/api/tenants/{tenantId}/website-chat"))).StatusCode);
    }

    [Fact]
    public async Task A_conversation_of_another_workspace_is_not_found()
    {
        // A foreign conversation id must not become an alternate read path.
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Cross tenant");

        fixture.ResetGenerator();

        HttpClient visitor = await fixture.CreateVisitorAsync();
        StartedConversation conversation = await StartAsync(visitor, key);

        await SendAsync(visitor, conversation, "m1", "Private to this workspace");

        (HttpClient rival, Guid rivalTenantId, _) = await fixture.CreateOwnerAsync("Rival Trading");

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await rival.GetAsync(WebChatApiFixture.Relative(
                $"/api/tenants/{rivalTenantId}/conversations/{conversation.ConversationId}")))
                .StatusCode);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await rival.GetAsync(WebChatApiFixture.Relative(
                $"/api/tenants/{rivalTenantId}/conversations/{conversation.ConversationId}/messages")))
                .StatusCode);
    }

    // ---- Reading real conversations --------------------------------------

    [Fact]
    public async Task A_real_website_chat_conversation_appears_with_its_channel_and_agent()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Night shift");

        fixture.ResetGenerator().Reply = "We close at six.";

        HttpClient visitor = await fixture.CreateVisitorAsync();
        StartedConversation conversation = await StartAsync(visitor, key);

        await SendAsync(visitor, conversation, "m1", "When do you close?");

        ConversationPage? page = await owner.GetFromJsonAsync<ConversationPage>(
            Conversations(tenantId));

        Assert.NotNull(page);

        ConversationSummary summary = Assert.Single(
            page.Items,
            item => item.Id == conversation.ConversationId);

        Assert.Equal("WebChat", summary.Channel);
        Assert.Equal("Open", summary.Status);
        Assert.Equal("Night shift", summary.AgentName);
        Assert.Equal(2, summary.MessageCount);
        Assert.Equal("We close at six.", summary.LastMessagePreview);
        Assert.Equal("AiAgent", summary.LastMessageSender);
        Assert.True(summary.LastActivityAt >= summary.StartedAt);
    }

    [Fact]
    public async Task The_transcript_matches_what_the_visitor_saw_in_ingestion_order()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Transcript");

        fixture.ResetGenerator().Reply = "Certainly.";

        HttpClient visitor = await fixture.CreateVisitorAsync();
        StartedConversation conversation = await StartAsync(visitor, key);

        await SendAsync(visitor, conversation, "m1", "First");
        await SendAsync(visitor, conversation, "m2", "Second");

        TranscriptPage? page = await owner.GetFromJsonAsync<TranscriptPage>(
            Transcript(tenantId, conversation));

        Assert.NotNull(page);
        Assert.Equal(
            [
                ("Customer", "First"),
                ("AiAgent", "Certainly."),
                ("Customer", "Second"),
                ("AiAgent", "Certainly."),
            ],
            page.Items.Select(item => (item.Sender, item.Text)));

        // The ordering is the stored ingestion sequence, strictly increasing.
        Assert.Equal(
            page.Items.Select(item => item.Sequence).Order(),
            page.Items.Select(item => item.Sequence));
    }

    [Fact]
    public async Task No_reply_is_shown_after_a_provider_failure()
    {
        // The admin sees exactly what happened: a question nobody answered.
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Outage view");

        fixture.ResetGenerator().PretendUnavailable = true;

        try
        {
            HttpClient visitor = await fixture.CreateVisitorAsync();
            StartedConversation conversation = await StartAsync(visitor, key);

            Assert.Equal(
                HttpStatusCode.ServiceUnavailable,
                (await SendAsync(visitor, conversation, "m1", "Hello?")).StatusCode);

            TranscriptPage? page = await owner.GetFromJsonAsync<TranscriptPage>(
                Transcript(tenantId, conversation));

            Assert.NotNull(page);

            TranscriptMessage only = Assert.Single(page.Items);

            Assert.Equal("Customer", only.Sender);
            Assert.Equal("Hello?", only.Text);
            Assert.DoesNotContain(page.Items, item => item.Sender == "AiAgent");
        }
        finally
        {
            fixture.ResetGenerator();
        }
    }

    [Fact]
    public async Task A_transcript_pages_by_ingestion_sequence_without_repeating_or_losing_a_message()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Paging");

        fixture.ResetGenerator().Reply = "Noted.";

        HttpClient visitor = await fixture.CreateVisitorAsync();
        StartedConversation conversation = await StartAsync(visitor, key);

        for (int index = 0; index < 4; index++)
        {
            await SendAsync(visitor, conversation, $"m{index}", $"Question {index}");
        }

        List<TranscriptMessage> collected = [];
        long? cursor = null;

        do
        {
            Uri page = WebChatApiFixture.Relative(
                $"/api/tenants/{tenantId}/conversations/{conversation.ConversationId}/messages"
                + $"?limit=3{(cursor is long value ? $"&after={value}" : string.Empty)}");

            TranscriptPage? read = await owner.GetFromJsonAsync<TranscriptPage>(page);

            Assert.NotNull(read);

            collected.AddRange(read.Items);
            cursor = read.NextCursor;
        }
        while (cursor is not null);

        // Eight messages -- four questions and four answers -- each exactly once,
        // in one strictly increasing run.
        Assert.Equal(8, collected.Count);
        Assert.Equal(8, collected.Select(item => item.Id).Distinct().Count());
        Assert.Equal(
            collected.Select(item => item.Sequence).Order(),
            collected.Select(item => item.Sequence));
    }

    [Fact]
    public async Task The_list_is_most_recently_active_first()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync("Ordering Trading");
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Ordering");

        fixture.ResetGenerator();

        HttpClient first = await fixture.CreateVisitorAsync();
        HttpClient second = await fixture.CreateVisitorAsync();

        StartedConversation older = await StartAsync(first, key);
        StartedConversation newer = await StartAsync(second, key);

        await SendAsync(first, older, "a", "Older");
        await SendAsync(second, newer, "b", "Newer");

        ConversationPage? page = await owner.GetFromJsonAsync<ConversationPage>(
            Conversations(tenantId));

        Assert.NotNull(page);
        Assert.Equal(2, page.Total);
        Assert.Equal(newer.ConversationId, page.Items[0].Id);
        Assert.Equal(older.ConversationId, page.Items[1].Id);
    }

    [Fact]
    public async Task A_workspace_with_no_conversations_reads_as_empty_rather_than_missing()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync("Quiet Trading");

        ConversationPage? page = await owner.GetFromJsonAsync<ConversationPage>(
            Conversations(tenantId));

        Assert.NotNull(page);
        Assert.Empty(page.Items);
        Assert.Equal(0, page.Total);
    }

    [Fact]
    public async Task The_admin_read_returns_no_credential_of_any_kind()
    {
        // A transcript is the visitor's conversation; the credential that opens it
        // is theirs alone and must not be reachable from the admin surface.
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Confidential desk");

        fixture.ResetGenerator();

        HttpClient visitor = await fixture.CreateVisitorAsync();
        StartedConversation conversation = await StartAsync(visitor, key);

        await SendAsync(visitor, conversation, "m1", "Hello");

        string detail = await (await owner.GetAsync(WebChatApiFixture.Relative(
            $"/api/tenants/{tenantId}/conversations/{conversation.ConversationId}")))
            .Content.ReadAsStringAsync();

        string transcript = await (await owner.GetAsync(Transcript(tenantId, conversation)))
            .Content.ReadAsStringAsync();

        foreach (string body in new[] { detail, transcript })
        {
            Assert.DoesNotContain("token", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("secret", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("accessToken", body, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ---- Helpers ---------------------------------------------------------

    private static Uri Transcript(Guid tenantId, StartedConversation conversation) =>
        WebChatApiFixture.Relative(
            $"/api/tenants/{tenantId}/conversations/{conversation.ConversationId}/messages");

    private static async Task<StartedConversation> StartAsync(HttpClient visitor, string key)
    {
        HttpResponseMessage started = await visitor.PostAsync(
            WebChatApiFixture.Relative($"{ChatRoot}/{key}/conversations"),
            content: null);

        Assert.Equal(HttpStatusCode.OK, started.StatusCode);

        return await started.Content.ReadFromJsonAsync<StartedConversation>()
            ?? throw new InvalidOperationException("No conversation was returned.");
    }

    private static Task<HttpResponseMessage> SendAsync(
        HttpClient visitor,
        StartedConversation conversation,
        string clientMessageId,
        string text) =>
        visitor.PostAsJsonAsync(
            WebChatApiFixture.Relative(
                $"{ChatRoot}/conversations/{conversation.ConversationId}/messages"),
            new { clientMessageId, text });
}
