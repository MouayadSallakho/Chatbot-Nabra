using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace OmniAI.Api.IntegrationTests.WebChat;

/// <summary>
/// Working a conversation over HTTP: taking it over, routing it, replying.
/// </summary>
/// <remarks>
/// Every conversation here was produced by a visitor talking to the real public
/// API, so what the admin acts on is what the runtime actually stored.
/// </remarks>
public sealed class ConversationInboxApiTests(WebChatApiFixture fixture)
    : IClassFixture<WebChatApiFixture>
{
    private const string ChatRoot = "/api/public/webchat";
    private const string EmbedRoot = "/api/public/embed";

    private sealed record StartedConversation(Guid ConversationId, string AgentName);

    private sealed record Member(Guid UserId, string Email);

    private sealed record Detail(
        Guid Id,
        string Channel,
        string Status,
        string Handling,
        DateTimeOffset StartedAt,
        DateTimeOffset LastActivityAt,
        string? AgentName,
        int MessageCount,
        Member? AssignedTo);

    private sealed record TranscriptMessage(
        Guid Id,
        string Sender,
        string Text,
        DateTimeOffset SentAt,
        long Sequence,
        Member? SentBy);

    private sealed record TranscriptPage(
        IReadOnlyList<TranscriptMessage> Items,
        long? NextCursor);

    private sealed record Ticket(string TicketValue, int ExpiresInSeconds);

    // ---- Handoff ---------------------------------------------------------

    [Fact]
    public async Task An_owner_takes_a_conversation_over_and_hands_it_back()
    {
        (HttpClient owner, Guid tenantId, StartedConversation conversation) =
            await ConversationAsync();

        (Detail detail, string version) = await ReadAsync(owner, tenantId, conversation);

        Assert.Equal("Ai", detail.Handling);

        (Detail taken, string next) = await HandoffAsync(owner, tenantId, conversation, "take-over", version);

        Assert.Equal("Human", taken.Handling);

        (Detail returned, _) = await HandoffAsync(owner, tenantId, conversation, "return-to-ai", next);

        Assert.Equal("Ai", returned.Handling);
    }

    [Fact]
    public async Task A_handoff_without_a_precondition_is_refused_with_428()
    {
        // The platform's standing rule for conditional writes: an unconditional
        // one never reaches a use case that would have performed it.
        (HttpClient owner, Guid tenantId, StartedConversation conversation) =
            await ConversationAsync();

        HttpResponseMessage response = await owner.PostAsync(
            Action(tenantId, conversation, "take-over"),
            content: null);

        Assert.Equal(HttpStatusCode.PreconditionRequired, response.StatusCode);
    }

    [Fact]
    public async Task A_handoff_with_a_stale_precondition_is_refused_with_412()
    {
        (HttpClient owner, Guid tenantId, StartedConversation conversation) =
            await ConversationAsync();

        (_, string version) = await ReadAsync(owner, tenantId, conversation);

        // Somebody else acts first, so the held version is no longer current.
        await HandoffAsync(owner, tenantId, conversation, "take-over", version);

        HttpResponseMessage stale = await SendConditionalAsync(
            owner,
            Action(tenantId, conversation, "return-to-ai"),
            version);

        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);

        // And nothing was applied: it is still being handled by a person.
        (Detail after, _) = await ReadAsync(owner, tenantId, conversation);

        Assert.Equal("Human", after.Handling);
    }

    [Fact]
    public async Task A_conversation_of_another_workspace_is_not_found()
    {
        (HttpClient owner, Guid tenantId, StartedConversation conversation) =
            await ConversationAsync();

        (_, string version) = await ReadAsync(owner, tenantId, conversation);

        (HttpClient rival, Guid rivalTenantId, _) = await fixture.CreateOwnerAsync("Rival Trading");

        HttpResponseMessage response = await SendConditionalAsync(
            rival,
            WebChatApiFixture.Relative(
                $"/api/tenants/{rivalTenantId}/conversations/{conversation.ConversationId}/take-over"),
            version);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_anonymous_caller_cannot_take_a_conversation_over()
    {
        (HttpClient owner, Guid tenantId, StartedConversation conversation) =
            await ConversationAsync();

        (_, string version) = await ReadAsync(owner, tenantId, conversation);

        HttpClient stranger = fixture.CreateClient();

        HttpResponseMessage response = await SendConditionalAsync(
            stranger,
            Action(tenantId, conversation, "take-over"),
            version);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---- Human reply -----------------------------------------------------

    [Fact]
    public async Task A_reply_reaches_the_visitor_and_names_nobody_to_them()
    {
        // The disclosure boundary, end to end. The workspace's own transcript
        // names the colleague; the visitor's does not, and has no field for one.
        (HttpClient owner, Guid tenantId, StartedConversation conversation, HttpClient visitor) =
            await ConversationWithVisitorAsync();

        (_, string version) = await ReadAsync(owner, tenantId, conversation);

        await HandoffAsync(owner, tenantId, conversation, "take-over", version);

        HttpResponseMessage sent = await owner.PostAsJsonAsync(
            Messages(tenantId, conversation),
            new { clientMessageId = "r1", text = "We refunded it this morning." });

        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);

        TranscriptPage? admin = await owner.GetFromJsonAsync<TranscriptPage>(
            Messages(tenantId, conversation));

        TranscriptMessage reply = Assert.Single(
            admin!.Items,
            message => message.Sender == "HumanAgent");

        Assert.NotNull(reply.SentBy);
        Assert.Contains("@", reply.SentBy.Email, StringComparison.Ordinal);

        // What the visitor reads back.
        string visitorTranscript = await visitor.GetStringAsync(
            WebChatApiFixture.Relative(
                $"{ChatRoot}/conversations/{conversation.ConversationId}/messages"));

        Assert.Contains("We refunded it this morning.", visitorTranscript, StringComparison.Ordinal);
        Assert.Contains("HumanAgent", visitorTranscript, StringComparison.Ordinal);
        Assert.DoesNotContain(reply.SentBy.Email, visitorTranscript, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Replying_to_an_ai_conversation_is_refused_with_409()
    {
        (HttpClient owner, Guid tenantId, StartedConversation conversation) =
            await ConversationAsync();

        HttpResponseMessage response = await owner.PostAsJsonAsync(
            Messages(tenantId, conversation),
            new { clientMessageId = "r1", text = "Hello" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task The_same_reply_key_twice_stores_one_message()
    {
        (HttpClient owner, Guid tenantId, StartedConversation conversation) =
            await ConversationAsync();

        (_, string version) = await ReadAsync(owner, tenantId, conversation);

        await HandoffAsync(owner, tenantId, conversation, "take-over", version);

        await owner.PostAsJsonAsync(
            Messages(tenantId, conversation),
            new { clientMessageId = "same", text = "Only once" });

        await owner.PostAsJsonAsync(
            Messages(tenantId, conversation),
            new { clientMessageId = "same", text = "Only once" });

        TranscriptPage? page = await owner.GetFromJsonAsync<TranscriptPage>(
            Messages(tenantId, conversation));

        Assert.Single(page!.Items, message => message.Sender == "HumanAgent");
    }

    // ---- Assignment ------------------------------------------------------

    [Fact]
    public async Task An_owner_can_be_assigned_and_unassigned()
    {
        (HttpClient owner, Guid tenantId, StartedConversation conversation) =
            await ConversationAsync();

        Member[]? eligible = await owner.GetFromJsonAsync<Member[]>(
            WebChatApiFixture.Relative($"/api/tenants/{tenantId}/conversations/assignees"));

        // The Owner holds every permission, including the one that makes
        // somebody eligible.
        Member target = Assert.Single(eligible!);

        (_, string version) = await ReadAsync(owner, tenantId, conversation);

        HttpResponseMessage assigned = await SendConditionalAsync(
            owner,
            Assignment(tenantId, conversation),
            version,
            new { member = target.UserId },
            HttpMethod.Put);

        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);

        (Detail routed, string next) = await ReadAsync(owner, tenantId, conversation);

        Assert.Equal(target.UserId, routed.AssignedTo!.UserId);

        // Routing does not change who is answering.
        Assert.Equal("Ai", routed.Handling);

        HttpResponseMessage cleared = await SendConditionalAsync(
            owner,
            Assignment(tenantId, conversation),
            next,
            new { member = (Guid?)null },
            HttpMethod.Put);

        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);

        (Detail unassigned, _) = await ReadAsync(owner, tenantId, conversation);

        Assert.Null(unassigned.AssignedTo);
    }

    [Fact]
    public async Task Somebody_from_another_workspace_cannot_be_assigned()
    {
        (HttpClient owner, Guid tenantId, StartedConversation conversation) =
            await ConversationAsync();

        (HttpClient rival, Guid rivalTenantId, _) = await fixture.CreateOwnerAsync("Rival Trading");

        Member[]? theirs = await rival.GetFromJsonAsync<Member[]>(
            WebChatApiFixture.Relative($"/api/tenants/{rivalTenantId}/conversations/assignees"));

        (_, string version) = await ReadAsync(owner, tenantId, conversation);

        HttpResponseMessage response = await SendConditionalAsync(
            owner,
            Assignment(tenantId, conversation),
            version,
            new { member = theirs![0].UserId },
            HttpMethod.Put);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    // ---- Realtime tickets ------------------------------------------------

    [Fact]
    public async Task A_hosted_visitor_mints_a_ticket_with_their_cookie()
    {
        (_, _, StartedConversation conversation, HttpClient visitor) =
            await ConversationWithVisitorAsync();

        HttpResponseMessage minted = await visitor.PostAsync(
            WebChatApiFixture.Relative(
                $"{ChatRoot}/conversations/{conversation.ConversationId}/realtime-ticket"),
            content: null);

        Assert.Equal(HttpStatusCode.OK, minted.StatusCode);

        string body = await minted.Content.ReadAsStringAsync();

        Assert.Contains("ticket", body, StringComparison.OrdinalIgnoreCase);

        // The response hands back a listening permission and nothing about the
        // conversation: no transcript, no workspace, no agent.
        Assert.DoesNotContain("conversationId", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_conversation_id_alone_mints_nothing()
    {
        // The id is not a secret -- it appears in an administrator's list -- so
        // holding it must buy nothing at all.
        (_, _, StartedConversation conversation, _) = await ConversationWithVisitorAsync();

        // A fully-formed visitor of their own -- antiforgery token and all --
        // so what is being tested is the conversation credential and not the
        // CSRF filter in front of it. They simply do not hold this
        // conversation's cookie.
        HttpClient other = await fixture.CreateVisitorAsync();

        HttpResponseMessage response = await other.PostAsync(
            WebChatApiFixture.Relative(
                $"{ChatRoot}/conversations/{conversation.ConversationId}/realtime-ticket"),
            content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_hosted_credential_cannot_mint_an_embedded_ticket()
    {
        // Phase 18's separation, carried into the realtime channel. The hosted
        // credential is a cookie the embed route never reads, so presenting it
        // there authenticates nothing.
        (_, _, StartedConversation conversation, HttpClient visitor) =
            await ConversationWithVisitorAsync();

        HttpResponseMessage response = await visitor.PostAsync(
            WebChatApiFixture.Relative(
                $"{EmbedRoot}/conversations/{conversation.ConversationId}/realtime-ticket"),
            content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- Helpers ---------------------------------------------------------

    private static Uri DetailOf(Guid tenantId, StartedConversation conversation) =>
        WebChatApiFixture.Relative(
            $"/api/tenants/{tenantId}/conversations/{conversation.ConversationId}");

    private static Uri Action(Guid tenantId, StartedConversation conversation, string action) =>
        WebChatApiFixture.Relative(
            $"/api/tenants/{tenantId}/conversations/{conversation.ConversationId}/{action}");

    private static Uri Assignment(Guid tenantId, StartedConversation conversation) =>
        Action(tenantId, conversation, "assignment");

    private static Uri Messages(Guid tenantId, StartedConversation conversation) =>
        Action(tenantId, conversation, "messages");

    private static async Task<(Detail Detail, string Version)> ReadAsync(
        HttpClient owner,
        Guid tenantId,
        StartedConversation conversation)
    {
        HttpResponseMessage response = await owner.GetAsync(DetailOf(tenantId, conversation));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string? tag = response.Headers.ETag?.ToString();

        Assert.False(string.IsNullOrWhiteSpace(tag), "A conversation read carried no ETag.");

        Detail? detail = await response.Content.ReadFromJsonAsync<Detail>();

        return (detail!, tag!);
    }

    private static async Task<(Detail Detail, string Version)> HandoffAsync(
        HttpClient owner,
        Guid tenantId,
        StartedConversation conversation,
        string action,
        string version)
    {
        HttpResponseMessage response = await SendConditionalAsync(
            owner,
            Action(tenantId, conversation, action),
            version);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Detail? detail = await response.Content.ReadFromJsonAsync<Detail>();

        return (detail!, response.Headers.ETag!.ToString());
    }

    private static async Task<HttpResponseMessage> SendConditionalAsync(
        HttpClient client,
        Uri uri,
        string version,
        object? body = null,
        HttpMethod? method = null)
    {
        HttpRequestMessage request = new(method ?? HttpMethod.Post, uri);

        request.Headers.TryAddWithoutValidation("If-Match", version);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await client.SendAsync(request);
    }

    private async Task<(HttpClient Owner, Guid TenantId, StartedConversation Conversation)>
        ConversationAsync()
    {
        (HttpClient owner, Guid tenantId, StartedConversation conversation, _) =
            await ConversationWithVisitorAsync();

        return (owner, tenantId, conversation);
    }

    private async Task<(HttpClient Owner, Guid TenantId, StartedConversation Conversation, HttpClient Visitor)>
        ConversationWithVisitorAsync()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Inbox");

        fixture.ResetGenerator().Reply = "The answer.";

        HttpClient visitor = await fixture.CreateVisitorAsync();

        HttpResponseMessage started = await visitor.PostAsync(
            WebChatApiFixture.Relative($"{ChatRoot}/{key}/conversations"),
            content: null);

        StartedConversation conversation =
            (await started.Content.ReadFromJsonAsync<StartedConversation>())!;

        await visitor.PostAsJsonAsync(
            WebChatApiFixture.Relative(
                $"{ChatRoot}/conversations/{conversation.ConversationId}/messages"),
            new { clientMessageId = "m1", text = "Where is my order?" });

        return (owner, tenantId, conversation, visitor);
    }
}
