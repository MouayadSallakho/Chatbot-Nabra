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

    [Fact]
    public async Task Quick_replies_are_shared_per_workspace_and_language_and_saving_is_idempotent()
    {
        var (owner, tenant, _) = await ConversationAsync();
        var (other, otherTenant, _) = await ConversationAsync();
        Uri Arabic(Guid id) => WebChatApiFixture.Relative($"/api/tenants/{id}/conversations/quick-replies/ar");
        var route = Arabic(tenant);
        var payload = new { text = "مرحبا" };
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsJsonAsync(route, payload)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsJsonAsync(route, payload)).StatusCode);
        var item = Assert.Single((await owner.GetFromJsonAsync<QuickReplyItem[]>(route))!);
        Assert.Equal(payload.text, item.Text);
        Assert.Empty((await owner.GetFromJsonAsync<QuickReplyItem[]>(WebChatApiFixture.Relative($"/api/tenants/{tenant}/conversations/quick-replies/en")))!);
        Assert.Empty((await other.GetFromJsonAsync<QuickReplyItem[]>(Arabic(otherTenant)))!);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(route, new {text = "  "})).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync(WebChatApiFixture.Relative($"/api/tenants/{tenant}/conversations/quick-replies/ar/{item.Id}"))).StatusCode);
        Assert.Empty((await owner.GetFromJsonAsync<QuickReplyItem[]>(route))!);
    }

    private sealed record QuickReplyItem(Guid Id, string Text);

    [Fact]
    public async Task Internal_notes_stay_out_of_both_transcripts_and_retries_store_one_copy()
    {
        var (owner, tenant, conversation, visitor) = await ConversationWithVisitorAsync();
        var (other, otherTenant, _) = await ConversationAsync();
        var notes = Action(tenant, conversation, "notes");
        var publicMessages = WebChatApiFixture.Relative($"{ChatRoot}/conversations/{conversation.ConversationId}/messages");
        var publicBefore = await visitor.GetStringAsync(publicMessages);
        var messagesBefore = await owner.GetStringAsync(Messages(tenant, conversation));
        var detailBefore = await ReadAsync(owner, tenant, conversation);
        var payload = new { requestId = Guid.NewGuid(), text = "Private shift handover" };

        var responses = await Task.WhenAll(owner.PostAsJsonAsync(notes, payload), owner.PostAsJsonAsync(notes, payload));
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        var page = await owner.GetFromJsonAsync<OmniAI.Application.Conversations.ConversationNotePage>(notes);
        var note = Assert.Single(page!.Items);
        Assert.Equal(payload.text, note.Text);
        Assert.False(string.IsNullOrWhiteSpace(note.Author));
        Assert.Null(page.NextBefore);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(notes, new { payload.requestId, text = "Different" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(notes, new { requestId = Guid.NewGuid(), text = "   " })).StatusCode);

        Assert.Equal(publicBefore, await visitor.GetStringAsync(publicMessages));
        Assert.Equal(messagesBefore, await owner.GetStringAsync(Messages(tenant, conversation)));
        Assert.Equal(detailBefore, await ReadAsync(owner, tenant, conversation));
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.GetAsync(notes)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(notes)).StatusCode);
        var foreignConversation = Action(otherTenant, conversation, "notes");
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(foreignConversation)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync(foreignConversation, payload)).StatusCode);
    }

    [Fact]
    public async Task Older_internal_notes_resume_before_the_last_note_on_the_previous_page()
    {
        var (owner, tenant, conversation) = await ConversationAsync();
        var notes = Action(tenant, conversation, "notes");
        for (int i = 0; i < 21; i++)
        {
            var response = await owner.PostAsJsonAsync(notes, new { requestId = Guid.NewGuid(), text = $"Note {i}" });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var newest = (await owner.GetFromJsonAsync<OmniAI.Application.Conversations.ConversationNotePage>(notes))!;
        Assert.Equal(20, newest.Items.Count);
        Assert.Equal("Note 20", newest.Items[0].Text);
        Assert.Equal(newest.Items[^1].Id, newest.NextBefore);
        var earlier = Action(tenant, conversation, $"notes?before={newest.NextBefore}");
        var oldest = (await owner.GetFromJsonAsync<OmniAI.Application.Conversations.ConversationNotePage>(earlier))!;
        Assert.Equal("Note 0", Assert.Single(oldest.Items).Text);
        Assert.Null(oldest.NextBefore);
    }

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
        Assert.Equal(reply.SentBy, (await sent.Content.ReadFromJsonAsync<TranscriptMessage>())!.SentBy);
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

        var first = await owner.PostAsJsonAsync(
            Messages(tenantId, conversation), new { clientMessageId = "same", text = "Only once" });
        var retried = await owner.PostAsJsonAsync(
            Messages(tenantId, conversation), new { clientMessageId = "same", text = "Only once" });
        var stored = await first.Content.ReadFromJsonAsync<TranscriptMessage>();
        var replay = await retried.Content.ReadFromJsonAsync<TranscriptMessage>();
        Assert.NotNull(stored!.SentBy);
        Assert.Equal(stored.SentBy, replay!.SentBy);
        Assert.Equal(stored.Id, replay.Id);

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
