using System.Net;
using System.Net.Http.Json;

namespace OmniAI.Api.IntegrationTests.WebChat;

/// <summary>
/// The hosted chat surface: what a stranger can do with a public link.
/// </summary>
/// <remarks>
/// <strong>These are the tests that decide whether this feature is safe to
/// publish.</strong> A public key is meant to be known, so every one of them
/// starts from the assumption that an attacker has one — and asserts what that
/// does not buy them.
/// </remarks>
public sealed class HostedChatApiTests(WebChatApiFixture fixture)
    : IClassFixture<WebChatApiFixture>
{
    private const string ChatRoot = "/api/public/webchat";
    private const string SessionCookie = "omniai.webchat";

    private sealed record PublicConfig(string AgentName, bool Available);

    private sealed record StartedConversation(Guid ConversationId, string AgentName);

    private sealed record ChatMessage(Guid Id, string Sender, string Text, DateTimeOffset SentAt);

    private sealed record ChatTurn(ChatMessage CustomerMessage, ChatMessage Reply);

    private sealed record ChatPage(IReadOnlyList<ChatMessage> Items, long? NextCursor);

    // ---- The public face -------------------------------------------------

    [Fact]
    public async Task A_public_key_reveals_an_agent_name_and_nothing_else()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Concierge");

        HttpClient visitor = await fixture.CreateVisitorAsync();

        HttpResponseMessage response = await visitor.GetAsync(
            WebChatApiFixture.Relative($"{ChatRoot}/{key}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();

        PublicConfig? config = await response.Content.ReadFromJsonAsync<PublicConfig>();

        Assert.NotNull(config);
        Assert.Equal("Concierge", config.AgentName);
        Assert.True(config.Available);

        // Nothing about the workspace or the agent's configuration leaks.
        Assert.DoesNotContain(tenantId.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("instruction", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tenant", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_unknown_or_malformed_public_key_is_not_found()
    {
        HttpClient visitor = await fixture.CreateVisitorAsync();

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await visitor.GetAsync(WebChatApiFixture.Relative($"{ChatRoot}/xxxxxxxxxxxxxxxxxxxxxx")))
                .StatusCode);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await visitor.GetAsync(WebChatApiFixture.Relative($"{ChatRoot}/short"))).StatusCode);
    }

    [Fact]
    public async Task A_public_key_grants_no_admin_data_at_all()
    {
        // The key is published in every hosted URL. Holding it must reach the
        // chat product surface and nothing else.
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (string key, Guid endpointId) =
            await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Key probe");

        HttpClient visitor = await fixture.CreateVisitorAsync();

        foreach (string path in new[]
        {
            $"/api/tenants/{tenantId}/conversations",
            $"/api/tenants/{tenantId}/website-chat",
            $"/api/tenants/{tenantId}/website-chat/{endpointId}",
            $"/api/tenants/{tenantId}/ai-agents",
            $"/api/tenants/{tenantId}/members",
            $"/api/tenants/{tenantId}/roles",
        })
        {
            HttpResponseMessage refused =
                await visitor.GetAsync(WebChatApiFixture.Relative(path));

            Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        }
    }

    // ---- Starting a conversation ----------------------------------------

    [Fact]
    public async Task Starting_a_conversation_issues_an_http_only_cookie_and_no_body_secret()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Cookie probe");

        HttpClient visitor = await fixture.CreateVisitorAsync();

        HttpResponseMessage started = await visitor.PostAsync(
            WebChatApiFixture.Relative($"{ChatRoot}/{key}/conversations"),
            content: null);

        Assert.Equal(HttpStatusCode.OK, started.StatusCode);

        string cookie = WebChatApiFixture.ReadSetCookie(started, SessionCookie)
            ?? throw new InvalidOperationException("No conversation cookie was issued.");

        // Script must never be able to read it, and it must never travel anywhere
        // but the hosted chat's own API.
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/public/webchat", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);

        // Session-scoped: no expiry that would leave a stranger's conversation
        // reachable on a shared computer.
        Assert.DoesNotContain("expires=", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("max-age=", cookie, StringComparison.OrdinalIgnoreCase);

        // The credential is never in the body, and nothing may cache the response.
        string body = await started.Content.ReadAsStringAsync();
        string secret = WebChatApiFixture.ReadCookie(started, SessionCookie)!;

        Assert.DoesNotContain(secret, body, StringComparison.Ordinal);
        Assert.Equal("no-store", started.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task A_conversation_cannot_be_started_without_the_antiforgery_token()
    {
        // Anonymous is not a reason to skip CSRF: there is a session here, and it
        // is exactly what a cross-site request would ride on.
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "CSRF probe");

        HttpClient raw = fixture.CreateClient();

        HttpResponseMessage refused = await raw.PostAsync(
            WebChatApiFixture.Relative($"{ChatRoot}/{key}/conversations"),
            content: null);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    [Fact]
    public async Task A_draft_endpoint_refuses_to_start_a_conversation()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        Guid agentId = await WebChatApiFixture.CreateActiveAgentAsync(owner, tenantId, "Unpublished");

        HttpResponseMessage created = await owner.PostAsJsonAsync(
            WebChatApiFixture.Relative($"/api/tenants/{tenantId}/website-chat"),
            new { aiAgentId = agentId });

        string key = (await created.Content.ReadFromJsonAsync<EndpointKey>())!.PublicKey;

        HttpClient visitor = await fixture.CreateVisitorAsync();

        // The page renders, and says it is unavailable -- without saying why.
        PublicConfig? config = await visitor.GetFromJsonAsync<PublicConfig>(
            WebChatApiFixture.Relative($"{ChatRoot}/{key}"));

        Assert.NotNull(config);
        Assert.False(config.Available);

        HttpResponseMessage refused = await visitor.PostAsync(
            WebChatApiFixture.Relative($"{ChatRoot}/{key}/conversations"),
            content: null);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
    }

    private sealed record EndpointKey(string PublicKey);

    // ---- Sending a message -----------------------------------------------

    [Fact]
    public async Task A_visitor_message_is_stored_answered_and_readable_back()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Turn probe");

        fixture.ResetGenerator().Reply = "We are open until six.";

        HttpClient visitor = await fixture.CreateVisitorAsync();
        StartedConversation conversation = await StartAsync(visitor, key);

        HttpResponseMessage sent = await SendAsync(visitor, conversation, "m1", "When do you close?");

        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);

        ChatTurn? turn = await sent.Content.ReadFromJsonAsync<ChatTurn>();

        Assert.NotNull(turn);
        Assert.Equal("When do you close?", turn.CustomerMessage.Text);
        Assert.Equal("Customer", turn.CustomerMessage.Sender);
        Assert.Equal("We are open until six.", turn.Reply.Text);
        Assert.Equal("AiAgent", turn.Reply.Sender);

        ChatPage? page = await visitor.GetFromJsonAsync<ChatPage>(Messages(conversation));

        Assert.NotNull(page);
        Assert.Equal(
            [("Customer", "When do you close?"), ("AiAgent", "We are open until six.")],
            page.Items.Select(item => (item.Sender, item.Text)));
    }

    [Fact]
    public async Task Text_that_looks_like_markup_is_stored_and_returned_as_text()
    {
        // The API stores and returns exactly what was typed. Rendering it safely
        // is the client's job, and the client binds it as text.
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "XSS probe");

        fixture.ResetGenerator().Reply = "<b>Not markup either</b>";

        HttpClient visitor = await fixture.CreateVisitorAsync();
        StartedConversation conversation = await StartAsync(visitor, key);

        const string Payload = "<script>alert(1)</script>";

        HttpResponseMessage sent = await SendAsync(visitor, conversation, "m1", Payload);
        ChatTurn? turn = await sent.Content.ReadFromJsonAsync<ChatTurn>();

        Assert.NotNull(turn);
        Assert.Equal(Payload, turn.CustomerMessage.Text);
        Assert.Equal("<b>Not markup either</b>", turn.Reply.Text);
    }

    [Fact]
    public async Task Repeating_a_message_returns_the_first_answer_and_calls_the_provider_once()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Idempotency");

        fixture.ResetGenerator();

        HttpClient visitor = await fixture.CreateVisitorAsync();
        StartedConversation conversation = await StartAsync(visitor, key);

        ChatTurn? first = await (await SendAsync(visitor, conversation, "same", "Hello"))
            .Content.ReadFromJsonAsync<ChatTurn>();

        ChatTurn? repeat = await (await SendAsync(visitor, conversation, "same", "Hello"))
            .Content.ReadFromJsonAsync<ChatTurn>();

        Assert.NotNull(first);
        Assert.NotNull(repeat);

        // The same stored rows, not a second pair that happens to read alike.
        Assert.Equal(first.CustomerMessage.Id, repeat.CustomerMessage.Id);
        Assert.Equal(first.Reply.Id, repeat.Reply.Id);

        Assert.Equal(1, fixture.Generator.Calls);

        ChatPage? page = await visitor.GetFromJsonAsync<ChatPage>(Messages(conversation));

        Assert.NotNull(page);
        Assert.Equal(2, page.Items.Count);
    }

    [Fact]
    public async Task An_unconfigured_provider_keeps_the_customer_message_and_answers_503()
    {
        // What the browser verification sees, and it is genuine behaviour rather
        // than a fixture: the customer's words survive, no reply is invented.
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Outage");

        fixture.ResetGenerator().PretendUnavailable = true;

        try
        {
            HttpClient visitor = await fixture.CreateVisitorAsync();
            StartedConversation conversation = await StartAsync(visitor, key);

            HttpResponseMessage failed =
                await SendAsync(visitor, conversation, "m1", "Anyone there?");

            Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);

            ChatPage? page = await visitor.GetFromJsonAsync<ChatPage>(Messages(conversation));

            Assert.NotNull(page);

            ChatMessage only = Assert.Single(page.Items);
            Assert.Equal("Customer", only.Sender);
            Assert.Equal("Anyone there?", only.Text);
        }
        finally
        {
            fixture.ResetGenerator();
        }
    }

    [Fact]
    public async Task An_invalid_provider_answer_keeps_the_customer_message_and_answers_502()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Bad gateway");

        fixture.ResetGenerator().Reply = "   ";

        try
        {
            HttpClient visitor = await fixture.CreateVisitorAsync();
            StartedConversation conversation = await StartAsync(visitor, key);

            HttpResponseMessage failed = await SendAsync(visitor, conversation, "m1", "Hello");

            Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode);

            ChatPage? page = await visitor.GetFromJsonAsync<ChatPage>(Messages(conversation));

            Assert.NotNull(page);
            Assert.Single(page.Items);
        }
        finally
        {
            fixture.ResetGenerator();
        }
    }

    [Fact]
    public async Task An_over_long_message_is_refused_and_stored_nowhere()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Too long");

        fixture.ResetGenerator();

        HttpClient visitor = await fixture.CreateVisitorAsync();
        StartedConversation conversation = await StartAsync(visitor, key);

        HttpResponseMessage refused =
            await SendAsync(visitor, conversation, "m1", new string('x', 4_001));

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        ChatPage? page = await visitor.GetFromJsonAsync<ChatPage>(Messages(conversation));

        Assert.NotNull(page);
        Assert.Empty(page.Items);
        Assert.Equal(0, fixture.Generator.Calls);
    }

    // ---- Isolation -------------------------------------------------------

    [Fact]
    public async Task One_visitor_cannot_read_or_write_anothers_conversation()
    {
        // The property the whole design exists for. Both visitors hold real
        // credentials; neither is worth anything against the other's conversation.
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Isolation");

        fixture.ResetGenerator();

        HttpClient visitorA = await fixture.CreateVisitorAsync();
        HttpClient visitorB = await fixture.CreateVisitorAsync();

        StartedConversation a = await StartAsync(visitorA, key);
        StartedConversation b = await StartAsync(visitorB, key);

        await SendAsync(visitorB, b, "b1", "Something private");

        // A knows B's conversation id -- and that is all knowing it is worth.
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await visitorA.GetAsync(Messages(b))).StatusCode);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await SendAsync(visitorA, b, "a1", "Reading your mail")).StatusCode);

        // B's own conversation is untouched by the attempt.
        ChatPage? page = await visitorB.GetFromJsonAsync<ChatPage>(Messages(b));

        Assert.NotNull(page);
        Assert.Equal(2, page.Items.Count);
        Assert.DoesNotContain(page.Items, item => item.Text == "Reading your mail");

        // And A's own conversation still works, so the refusal was about B.
        Assert.Equal(HttpStatusCode.OK, (await visitorA.GetAsync(Messages(a))).StatusCode);
    }

    [Fact]
    public async Task A_conversation_id_alone_is_not_a_credential()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "No cookie");

        fixture.ResetGenerator();

        HttpClient visitor = await fixture.CreateVisitorAsync();
        StartedConversation conversation = await StartAsync(visitor, key);

        // A browser that has the id but never received the cookie.
        HttpClient stranger = await fixture.CreateVisitorAsync();

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await stranger.GetAsync(Messages(conversation))).StatusCode);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await SendAsync(stranger, conversation, "s1", "Hello")).StatusCode);
    }

    [Fact]
    public async Task An_admin_session_is_not_a_visitor_credential()
    {
        // The two authorisation systems never substitute for each other. An
        // owner's session says nothing about an anonymous conversation.
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Admin probe");

        fixture.ResetGenerator();

        HttpClient visitor = await fixture.CreateVisitorAsync();
        StartedConversation conversation = await StartAsync(visitor, key);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await owner.GetAsync(Messages(conversation))).StatusCode);
    }

    // ---- Withdrawal ------------------------------------------------------

    [Fact]
    public async Task Disabling_the_endpoint_stops_new_turns_but_leaves_the_transcript_readable()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (string key, Guid endpointId) =
            await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Withdrawal");

        fixture.ResetGenerator();

        HttpClient visitor = await fixture.CreateVisitorAsync();
        StartedConversation conversation = await StartAsync(visitor, key);

        Assert.Equal(
            HttpStatusCode.OK,
            (await SendAsync(visitor, conversation, "m1", "Hello")).StatusCode);

        // The owner withdraws the chat.
        HttpResponseMessage read = await owner.GetAsync(
            WebChatApiFixture.Relative($"/api/tenants/{tenantId}/website-chat/{endpointId}"));

        HttpRequestMessage disable = new(
            HttpMethod.Post,
            WebChatApiFixture.Relative(
                $"/api/tenants/{tenantId}/website-chat/{endpointId}/disable"));

        disable.Headers.TryAddWithoutValidation("If-Match", read.Headers.ETag!.ToString());

        Assert.Equal(HttpStatusCode.OK, (await owner.SendAsync(disable)).StatusCode);

        // No new conversation, and no new turn in the existing one.
        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            (await visitor.PostAsync(
                WebChatApiFixture.Relative($"{ChatRoot}/{key}/conversations"),
                content: null)).StatusCode);

        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            (await SendAsync(visitor, conversation, "m2", "Still there?")).StatusCode);

        // What was already said is still theirs to read.
        ChatPage? page = await visitor.GetFromJsonAsync<ChatPage>(Messages(conversation));

        Assert.NotNull(page);
        Assert.Equal(2, page.Items.Count);
    }

    // ---- Helpers ---------------------------------------------------------

    private static Uri Messages(StartedConversation conversation) =>
        WebChatApiFixture.Relative(
            $"{ChatRoot}/conversations/{conversation.ConversationId}/messages");

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
            Messages(conversation),
            new { clientMessageId, text });
}
