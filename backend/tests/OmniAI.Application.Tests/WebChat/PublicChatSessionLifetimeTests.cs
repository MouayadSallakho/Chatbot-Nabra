using OmniAI.Application.WebChat.Hosted;
using OmniAI.Application.WebChat.Protection;
using OmniAI.Domain.WebChat;
using OmniAI.Testing;

namespace OmniAI.Application.Tests.WebChat;

/// <summary>
/// How long an anonymous visitor's credential opens their conversation.
/// </summary>
/// <remarks>
/// <strong>An absolute lifetime, decided once and stored.</strong> The two
/// properties worth proving are that the expiry is written from the configured
/// lifetime at the moment the session opens, and that using the session does not
/// extend it. The second is the security-relevant one: a sliding expiry means
/// whoever holds a stolen credential keeps it alive forever simply by using it,
/// which is precisely the case the expiry exists for.
/// </remarks>
public sealed class PublicChatSessionLifetimeTests
{
    [Fact]
    public async Task A_session_expires_one_configured_lifetime_after_it_opened()
    {
        FixedTimeProvider clock = new(Harness.Noon);

        Harness harness = Harness.Build(
            limits: new PublicChatProtectionOptions { SessionLifetime = TimeSpan.FromHours(6) },
            clock: clock);

        await harness.StartAsync();

        WebChatSession session = Assert.Single(harness.World.Sessions);

        Assert.Equal(Harness.Noon, session.CreatedAt);
        Assert.Equal(Harness.Noon.AddHours(6), session.ExpiresAt);
    }

    [Fact]
    public async Task Before_the_expiry_the_visitor_can_read_and_write()
    {
        FixedTimeProvider clock = new(Harness.Noon);

        Harness harness = Harness.Build(
            limits: new PublicChatProtectionOptions { SessionLifetime = TimeSpan.FromHours(6) },
            clock: clock);

        StartedWebChatSession session = await harness.StartAsync();

        clock.Advance(TimeSpan.FromHours(5));

        WebChatTurnResult? turn = await harness.Send.HandleAsync(Harness.Sending(
            session.Conversation.ConversationId,
            session.AccessToken,
            "m1",
            "Still here?"));

        Assert.NotNull(turn);

        PublicChatMessagePage? page = await harness.List.HandleAsync(
            new ListWebChatMessagesQuery(
                session.Conversation.ConversationId,
                session.AccessToken,
                null,
                null));

        Assert.NotNull(page);
        Assert.Equal(2, page.Items.Count);
    }

    [Fact]
    public async Task After_the_expiry_a_send_is_refused_and_no_provider_is_called()
    {
        FixedTimeProvider clock = new(Harness.Noon);
        FakePublicChatProtection guard = new();

        Harness harness = Harness.Build(
            protection: guard,
            limits: new PublicChatProtectionOptions { SessionLifetime = TimeSpan.FromHours(6) },
            clock: clock);

        StartedWebChatSession session = await harness.StartAsync();

        clock.Advance(TimeSpan.FromHours(6));

        WebChatTurnResult? refused = await harness.Send.HandleAsync(Harness.Sending(
            session.Conversation.ConversationId,
            session.AccessToken,
            "m1",
            "Still here?"));

        // Not-found, exactly as an unknown conversation gets. Telling somebody
        // their credential had expired would confirm the conversation is real.
        Assert.Null(refused);

        Assert.Equal(0, harness.Generator.Calls);
        Assert.Equal(0, guard.Generations);
        Assert.Empty(harness.World.Turns);
    }

    [Fact]
    public async Task After_the_expiry_the_transcript_is_refused_too()
    {
        FixedTimeProvider clock = new(Harness.Noon);

        Harness harness = Harness.Build(
            limits: new PublicChatProtectionOptions { SessionLifetime = TimeSpan.FromHours(6) },
            clock: clock);

        StartedWebChatSession session = await harness.StartAsync();

        await harness.Send.HandleAsync(Harness.Sending(
            session.Conversation.ConversationId,
            session.AccessToken,
            "m1",
            "Hello"));

        clock.Advance(TimeSpan.FromHours(6));

        PublicChatMessagePage? refused = await harness.List.HandleAsync(
            new ListWebChatMessagesQuery(
                session.Conversation.ConversationId,
                session.AccessToken,
                null,
                null));

        Assert.Null(refused);
    }

    [Fact]
    public async Task Sending_does_not_extend_the_lifetime()
    {
        // The property that makes a stolen credential worthless by tomorrow. A
        // sliding expiry would let whoever holds it keep it alive indefinitely by
        // sending traffic, which is what the expiry is for.
        FixedTimeProvider clock = new(Harness.Noon);

        Harness harness = Harness.Build(
            limits: new PublicChatProtectionOptions { SessionLifetime = TimeSpan.FromHours(4) },
            clock: clock);

        StartedWebChatSession session = await harness.StartAsync();

        for (int hour = 1; hour <= 3; hour++)
        {
            clock.Advance(TimeSpan.FromHours(1));

            Assert.NotNull(await harness.Send.HandleAsync(Harness.Sending(
                session.Conversation.ConversationId,
                session.AccessToken,
                $"m{hour}",
                "Still here?")));
        }

        // Three hours of activity, and the stored expiry has not moved.
        Assert.Equal(
            Harness.Noon.AddHours(4),
            Assert.Single(harness.World.Sessions).ExpiresAt);

        clock.Advance(TimeSpan.FromHours(1));

        Assert.Null(await harness.Send.HandleAsync(Harness.Sending(
            session.Conversation.ConversationId,
            session.AccessToken,
            "m4",
            "And now?")));
    }

    [Fact]
    public async Task Lengthening_the_configured_lifetime_does_not_revive_an_expired_session()
    {
        // The reason the expiry is stored rather than recomputed. A deployment that
        // doubles its session lifetime tomorrow must not thereby resurrect every
        // credential that expired today.
        FixedTimeProvider clock = new(Harness.Noon);

        Harness harness = Harness.Build(
            limits: new PublicChatProtectionOptions { SessionLifetime = TimeSpan.FromHours(2) },
            clock: clock);

        StartedWebChatSession session = await harness.StartAsync();

        clock.Advance(TimeSpan.FromHours(3));

        // A session opened under the old policy, read by handlers configured with
        // the new one. The stored instant is what decides.
        Harness relaxed = Harness.Build(
            limits: new PublicChatProtectionOptions { SessionLifetime = TimeSpan.FromDays(7) },
            clock: clock);

        Assert.Null(await harness.Send.HandleAsync(Harness.Sending(
            session.Conversation.ConversationId,
            session.AccessToken,
            "m1",
            "Hello")));

        Assert.Empty(relaxed.World.Sessions);
    }

    [Fact]
    public async Task Each_conversation_gets_its_own_credential_and_its_own_expiry()
    {
        // Two conversations in one browser is the Phase 15 limitation this phase
        // fixes at the transport level. At this level the property is simpler and
        // just as important: the two claims are separate rows with separate secrets,
        // so one expiring says nothing about the other.
        FixedTimeProvider clock = new(Harness.Noon);

        Harness harness = Harness.Build(
            limits: new PublicChatProtectionOptions { SessionLifetime = TimeSpan.FromHours(6) },
            clock: clock);

        StartedWebChatSession first = await harness.StartAsync();

        clock.Advance(TimeSpan.FromHours(2));

        StartedWebChatSession second = await harness.StartAsync();

        Assert.NotEqual(first.Conversation.ConversationId, second.Conversation.ConversationId);
        Assert.NotEqual(first.AccessToken, second.AccessToken);

        Assert.Equal(2, harness.World.Sessions.Count);
        Assert.Equal(
            [Harness.Noon.AddHours(6), Harness.Noon.AddHours(8)],
            harness.World.Sessions.Select(session => session.ExpiresAt));

        // The first expires; the second is untouched.
        clock.Advance(TimeSpan.FromHours(4));

        Assert.Null(await harness.Send.HandleAsync(Harness.Sending(
            first.Conversation.ConversationId,
            first.AccessToken,
            "m1",
            "Hello")));

        Assert.NotNull(await harness.Send.HandleAsync(Harness.Sending(
            second.Conversation.ConversationId,
            second.AccessToken,
            "m2",
            "Hello")));
    }

    [Fact]
    public async Task One_conversations_credential_never_opens_another()
    {
        // Phase 15's isolation, re-asserted because the cookie transport changed
        // underneath it. Paths separate conversations inside one browser; they are
        // not authorisation, and the digest comparison is still what decides.
        Harness harness = Harness.Build();

        StartedWebChatSession first = await harness.StartAsync();
        StartedWebChatSession second = await harness.StartAsync();

        Assert.Null(await harness.Send.HandleAsync(Harness.Sending(
            first.Conversation.ConversationId,
            second.AccessToken,
            "m1",
            "Hello")));

        Assert.Null(await harness.List.HandleAsync(new ListWebChatMessagesQuery(
            second.Conversation.ConversationId,
            first.AccessToken,
            null,
            null)));

        Assert.Equal(0, harness.Generator.Calls);
    }
}
