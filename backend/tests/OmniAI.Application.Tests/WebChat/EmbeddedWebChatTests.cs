using OmniAI.Application.WebChat;
using OmniAI.Application.WebChat.Embedded;
using OmniAI.Application.WebChat.Hosted;
using OmniAI.Application.WebChat.Protection;
using OmniAI.Domain.WebChat;

namespace OmniAI.Application.Tests.WebChat;

/// <summary>
/// The embedded widget's doorway, and the wall between the two transports.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Every refusal here is the same refusal.</strong> An unknown key, a
/// malformed key, a website nobody approved, and a website whose approval was
/// withdrawn all produce one nothing. Telling them apart would let anybody holding
/// a public key — which appears in the customer's own HTML — enumerate the
/// websites a workspace has authorised, which is exactly the list needed to attack
/// it.
/// </para>
/// <para>
/// <strong>The allowlist is browser embedding policy, not abuse
/// protection.</strong> An approved website still passes through the Phase 16
/// start limit, the send limit, the workspace budget and the conversation lock.
/// The tests below check both halves, because a reviewer reading "origin allowed"
/// must not conclude that anything else was skipped.
/// </para>
/// </remarks>
public sealed class EmbeddedWebChatTests
{
    private const string Website = "https://www.example.com";
    private const string OtherWebsite = "https://other.example.com";

    // -------------------------------------------------------------- the door --

    [Fact]
    public async Task An_approved_website_is_told_how_the_widget_should_look()
    {
        Harness harness = Harness.Build();

        harness.Allow(Website);

        harness.Endpoint.Configure(
            WebChatWidgetSettings.Create(
                WebChatGreeting.CreateOrNone("Hi — ask us anything."),
                WebChatAccentColor.Create("#123456"),
                WebChatLauncherPosition.Left,
                WebChatTheme.Dark),
            [WebChatOrigin.Create(Website)],
            Harness.Noon);

        EmbeddedWebChatView? view = await harness.Embed.GetAsync(
            new GetEmbeddedWebChatQuery(harness.Endpoint.PublicKey.Value, Website));

        Assert.NotNull(view);
        Assert.True(view.IsAvailable);
        Assert.Equal("Support", view.AgentName);
        Assert.Equal("Hi — ask us anything.", view.Greeting);
        Assert.Equal("#123456", view.AccentColor);
        Assert.Equal("Left", view.LauncherPosition);
        Assert.Equal("Dark", view.Theme);
    }

    [Fact]
    public async Task Reading_the_widget_creates_nothing_at_all()
    {
        Harness harness = Harness.Build();

        harness.Allow(Website);

        await harness.Embed.GetAsync(
            new GetEmbeddedWebChatQuery(harness.Endpoint.PublicKey.Value, Website));

        // A page view, a reload, a crawler and a visitor who opens the launcher
        // and changes their mind all end here. On a busy website, creating a
        // conversation for each would fill a workspace with empty ones.
        Assert.Empty(harness.World.Conversations);
        Assert.Empty(harness.World.Sessions);
    }

    [Theory]
    [InlineData(OtherWebsite)]
    [InlineData("https://evil.www.example.com")]
    [InlineData("https://www.example.com.evil.test")]
    [InlineData("http://www.example.com")]
    [InlineData("null")]
    [InlineData("not an origin")]
    [InlineData("")]
    [InlineData(null)]
    public async Task A_website_nobody_approved_gets_the_same_nothing(string? origin)
    {
        Harness harness = Harness.Build();

        harness.Allow(Website);

        Assert.Null(
            await harness.Embed.GetAsync(
                new GetEmbeddedWebChatQuery(harness.Endpoint.PublicKey.Value, origin)));
    }

    [Fact]
    public async Task An_unknown_or_malformed_key_gets_that_same_nothing()
    {
        Harness harness = Harness.Build();

        harness.Allow(Website);

        Assert.Null(await harness.Embed.GetAsync(new GetEmbeddedWebChatQuery("nope", Website)));
        Assert.Null(
            await harness.Embed.GetAsync(
                new GetEmbeddedWebChatQuery(new string('z', 22), Website)));
        Assert.Null(await harness.Embed.GetAsync(new GetEmbeddedWebChatQuery(null, Website)));
    }

    [Fact]
    public async Task A_widget_with_no_approved_website_runs_nowhere()
    {
        Harness harness = Harness.Build();

        Assert.Null(
            await harness.Embed.GetAsync(
                new GetEmbeddedWebChatQuery(harness.Endpoint.PublicKey.Value, Website)));
    }

    [Fact]
    public async Task A_withdrawn_endpoint_is_reached_but_says_it_is_not_serving()
    {
        // The website is approved, so this is not the "you may not embed here"
        // answer: the widget is told the chat is unavailable, and renders nothing
        // rather than a launcher that would refuse every click.
        Harness harness = Harness.Build(activateEndpoint: false);

        harness.Allow(Website);

        EmbeddedWebChatView? view = await harness.Embed.GetAsync(
            new GetEmbeddedWebChatQuery(harness.Endpoint.PublicKey.Value, Website));

        Assert.NotNull(view);
        Assert.False(view.IsAvailable);
    }

    // ------------------------------------------------------------ starting up --

    [Fact]
    public async Task A_conversation_exists_only_once_somebody_starts_one()
    {
        Harness harness = Harness.Build();

        harness.Allow(Website);

        StartedEmbeddedConversation started = await harness.StartEmbeddedAsync(Website);

        Assert.Single(harness.World.Conversations);
        Assert.Single(harness.World.Sessions);
        Assert.NotEqual(Guid.Empty, started.ConversationId);
    }

    [Fact]
    public async Task The_session_records_the_website_it_was_opened_on()
    {
        Harness harness = Harness.Build();

        harness.Allow(Website);

        await harness.StartEmbeddedAsync(Website);

        WebChatSession session = harness.World.Sessions.Single();

        Assert.Equal(WebChatClientMode.Embedded, session.Mode);
        Assert.Equal(WebChatOrigin.Create(Website), session.EmbeddedOrigin);
    }

    [Fact]
    public async Task The_raw_credential_is_returned_once_and_never_stored()
    {
        Harness harness = Harness.Build();

        harness.Allow(Website);

        StartedEmbeddedConversation started = await harness.StartEmbeddedAsync(Website);

        Assert.NotEmpty(started.Credential);

        // What is kept is a digest. Nothing the session holds equals the value
        // that was handed to the browser, so a database read cannot resume
        // anybody's conversation.
        WebChatSession session = harness.World.Sessions.Single();

        Assert.DoesNotContain(
            started.Credential,
            Convert.ToHexString(session.AccessTokenHash.ToArray()),
            StringComparison.OrdinalIgnoreCase);

        Assert.True(session.Admits(harness.Tokens.Recognise(started.Credential)));
        Assert.False(session.Admits(harness.Tokens.Recognise(new string('b', 43))));
    }

    [Fact]
    public async Task Starting_from_an_unapproved_website_starts_nothing()
    {
        Harness harness = Harness.Build();

        harness.Allow(Website);

        Assert.Null(
            await harness.Embed.StartAsync(
                new StartEmbeddedConversationCommand(
                    harness.Endpoint.PublicKey.Value,
                    OtherWebsite,
                    Harness.Visitor)));

        Assert.Empty(harness.World.Conversations);
    }

    [Fact]
    public async Task Starting_a_withdrawn_endpoint_is_refused_as_unavailable()
    {
        Harness harness = Harness.Build(activateEndpoint: false);

        harness.Allow(Website);

        await Assert.ThrowsAsync<WebChatUnavailableException>(
            () => harness.Embed.StartAsync(
                new StartEmbeddedConversationCommand(
                    harness.Endpoint.PublicKey.Value,
                    Website,
                    Harness.Visitor)));
    }

    [Fact]
    public async Task Starting_without_a_network_identity_is_refused()
    {
        // Phase 16's protection counts per network. A start that could arrive
        // without one would be a start nothing could limit -- so it is refused as
        // an outage rather than reported as a caller error: the visitor learns
        // that the chat is unavailable, and nothing about why.
        Harness harness = Harness.Build();

        harness.Allow(Website);

        await Assert.ThrowsAsync<PublicChatProtectionUnavailableException>(
            () => harness.Embed.StartAsync(
                new StartEmbeddedConversationCommand(
                    harness.Endpoint.PublicKey.Value,
                    Website,
                    null)));
    }

    [Fact]
    public async Task An_approved_website_is_still_subject_to_the_start_limit()
    {
        Harness harness = Harness.Build();

        harness.Allow(Website);
        harness.Protection.Start = PublicChatDecision.Reject(TimeSpan.FromSeconds(30));

        PublicChatRateLimitedException refused =
            await Assert.ThrowsAsync<PublicChatRateLimitedException>(
                () => harness.Embed.StartAsync(
                    new StartEmbeddedConversationCommand(
                        harness.Endpoint.PublicKey.Value,
                        Website,
                        Harness.Visitor)));

        Assert.Equal(TimeSpan.FromSeconds(30), refused.RetryAfter);
        Assert.Empty(harness.World.Conversations);
    }

    // ----------------------------------------------- the wall between the two --

    [Fact]
    public async Task An_embedded_credential_does_not_open_the_hosted_routes()
    {
        Harness harness = Harness.Build();

        harness.Allow(Website);

        StartedEmbeddedConversation started = await harness.StartEmbeddedAsync(Website);

        // The right secret, presented on the wrong transport. The hosted routes
        // authenticate with a cookie; accepting a header credential there would
        // make the cookie's protections optional.
        Assert.Null(
            await harness.List.HandleAsync(
                new ListWebChatMessagesQuery(started.ConversationId, started.Credential, null, null)));

        Assert.Null(
            await harness.Send.HandleAsync(
                Harness.Sending(started.ConversationId, started.Credential, "c1", "Hello")));
    }

    [Fact]
    public async Task A_hosted_credential_does_not_open_the_embedded_routes()
    {
        Harness harness = Harness.Build();

        StartedWebChatSession hosted = await harness.StartAsync();

        Assert.Null(
            await harness.List.HandleAsync(
                Harness.ListingEmbedded(hosted.Conversation.ConversationId, hosted.AccessToken)));

        Assert.Null(
            await harness.Send.HandleAsync(
                Harness.SendingEmbedded(
                    hosted.Conversation.ConversationId,
                    hosted.AccessToken,
                    "c1",
                    "Hello")));
    }

    [Fact]
    public async Task A_conversation_id_on_its_own_opens_neither()
    {
        Harness harness = Harness.Build();

        harness.Allow(Website);

        StartedEmbeddedConversation started = await harness.StartEmbeddedAsync(Website);

        Assert.Null(
            await harness.List.HandleAsync(
                Harness.ListingEmbedded(started.ConversationId, null)));

        Assert.Null(
            await harness.List.HandleAsync(
                Harness.ListingEmbedded(started.ConversationId, "not-the-credential")));
    }

    // ---------------------------------------------------------- live revocation --

    [Fact]
    public async Task Removing_a_website_stops_the_conversations_already_open_on_it()
    {
        Harness harness = Harness.Build();

        harness.Allow(Website);

        StartedEmbeddedConversation started = await harness.StartEmbeddedAsync(Website);

        Assert.NotNull(
            await harness.Send.HandleAsync(
                Harness.SendingEmbedded(started.ConversationId, started.Credential, "c1", "Hi")));

        // The allowlist is re-read on every turn rather than trusted from the
        // session. An administrator who removes a website expects the widget to
        // stop, not to keep answering until somebody's session happens to expire.
        harness.Allow(OtherWebsite);

        Assert.Null(
            await harness.Send.HandleAsync(
                Harness.SendingEmbedded(started.ConversationId, started.Credential, "c2", "Again")));

        Assert.Null(
            await harness.List.HandleAsync(
                Harness.ListingEmbedded(started.ConversationId, started.Credential)));
    }

    [Fact]
    public async Task Removing_every_website_stops_every_embedded_conversation()
    {
        Harness harness = Harness.Build();

        harness.Allow(Website);

        StartedEmbeddedConversation started = await harness.StartEmbeddedAsync(Website);

        harness.Allow();

        Assert.Null(
            await harness.List.HandleAsync(
                Harness.ListingEmbedded(started.ConversationId, started.Credential)));
    }

    [Fact]
    public async Task A_hosted_conversation_is_untouched_by_the_allowlist()
    {
        // The hosted page is not embedded anywhere, so an empty allowlist says
        // nothing about it. Coupling the two would mean configuring a widget could
        // silently take the hosted chat link offline.
        Harness harness = Harness.Build();

        StartedWebChatSession hosted = await harness.StartAsync();

        harness.Allow();

        Assert.NotNull(
            await harness.List.HandleAsync(
                new ListWebChatMessagesQuery(
                    hosted.Conversation.ConversationId,
                    hosted.AccessToken,
                    null,
                    null)));
    }

    [Fact]
    public async Task An_expired_embedded_session_stops_being_admitted()
    {
        Harness harness = Harness.Build();

        harness.Allow(Website);

        StartedEmbeddedConversation started = await harness.StartEmbeddedAsync(Website);

        harness.Clock.Advance(harness.Limits.SessionLifetime + TimeSpan.FromMinutes(1));

        Assert.Null(
            await harness.List.HandleAsync(
                Harness.ListingEmbedded(started.ConversationId, started.Credential)));

        Assert.Null(
            await harness.Send.HandleAsync(
                Harness.SendingEmbedded(started.ConversationId, started.Credential, "c1", "Hi")));
    }

    // ------------------------------------------------------- the same runtime --

    [Fact]
    public async Task An_embedded_turn_goes_through_the_same_runtime_as_a_hosted_one()
    {
        Harness harness = Harness.Build();

        harness.Allow(Website);

        StartedEmbeddedConversation started = await harness.StartEmbeddedAsync(Website);

        WebChatTurnResult? turn = await harness.Send.HandleAsync(
            Harness.SendingEmbedded(started.ConversationId, started.Credential, "c1", "Hello"));

        Assert.NotNull(turn);
        Assert.Equal("Hello", turn.CustomerMessage.Text);
        Assert.NotEmpty(turn.Reply.Text);

        // The conversation lock, the idempotency key and the generator are the
        // hosted ones, because this is the hosted runtime.
        Assert.Equal(1, harness.Generator.Calls);

        // The conversation lock was taken, and never by two turns at once.
        Assert.Equal(1, harness.Lock.MaxConcurrentHolders);
    }

    [Fact]
    public async Task An_embedded_send_is_idempotent_on_the_client_message_id()
    {
        Harness harness = Harness.Build();

        harness.Allow(Website);

        StartedEmbeddedConversation started = await harness.StartEmbeddedAsync(Website);

        WebChatTurnResult? first = await harness.Send.HandleAsync(
            Harness.SendingEmbedded(started.ConversationId, started.Credential, "c1", "Hello"));

        WebChatTurnResult? retry = await harness.Send.HandleAsync(
            Harness.SendingEmbedded(started.ConversationId, started.Credential, "c1", "Hello"));

        Assert.NotNull(first);
        Assert.NotNull(retry);
        Assert.Equal(first.Reply.Text, retry.Reply.Text);

        // The workspace paid for one answer, not two.
        Assert.Equal(1, harness.Generator.Calls);
    }

    [Fact]
    public async Task An_approved_website_is_still_subject_to_the_send_limit()
    {
        Harness harness = Harness.Build();

        harness.Allow(Website);

        StartedEmbeddedConversation started = await harness.StartEmbeddedAsync(Website);

        harness.Protection.Request = PublicChatDecision.Reject(TimeSpan.FromSeconds(15));

        await Assert.ThrowsAsync<PublicChatRateLimitedException>(
            () => harness.Send.HandleAsync(
                Harness.SendingEmbedded(started.ConversationId, started.Credential, "c1", "Hi")));

        Assert.Equal(0, harness.Generator.Calls);
    }
}
