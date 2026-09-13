using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.WebChat;

namespace OmniAI.Domain.Tests.WebChat;

/// <summary>
/// The transport a session was opened through, and why it is part of the session.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A hosted credential must not authenticate an embedded request, and an
/// embedded one must not authenticate a hosted request.</strong> The two carry
/// their secrets differently — a cookie the browser attaches by itself, and a
/// header the widget sets — and a session that accepted either would let the
/// weaker of the two protections stand in for both.
/// </para>
/// <para>
/// <strong>An embedded session knows which website it was opened on.</strong>
/// Without that, removing a website from the allowlist could not stop the
/// conversations already running on it.
/// </para>
/// </remarks>
public sealed class WebChatEmbeddedSessionTests
{
    private static readonly DateTimeOffset Noon =
        new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Tomorrow = Noon.AddHours(24);

    private static readonly WebChatOrigin Website =
        WebChatOrigin.Create("https://www.example.com");

    private static WebChatAccessTokenHash Digest(byte seed) =>
        WebChatAccessTokenHash.From([.. Enumerable.Repeat(seed, 32)]);

    private static WebChatSession Hosted() =>
        WebChatSession.Open(
            ConversationId.New(),
            TenantId.New(),
            WebChatEndpointId.New(),
            AiAgentId.New(),
            Digest(1),
            Noon,
            Tomorrow);

    private static WebChatSession Embedded(WebChatOrigin? origin = null) =>
        WebChatSession.OpenEmbedded(
            ConversationId.New(),
            TenantId.New(),
            WebChatEndpointId.New(),
            AiAgentId.New(),
            Digest(1),
            origin ?? Website,
            Noon,
            Tomorrow);

    [Fact]
    public void A_hosted_session_is_hosted_and_names_no_website()
    {
        WebChatSession session = Hosted();

        Assert.Equal(WebChatClientMode.Hosted, session.Mode);
        Assert.False(session.IsEmbedded);
        Assert.Null(session.EmbeddedOrigin);
    }

    [Fact]
    public void An_embedded_session_records_the_website_it_was_opened_on()
    {
        WebChatSession session = Embedded();

        Assert.Equal(WebChatClientMode.Embedded, session.Mode);
        Assert.True(session.IsEmbedded);
        Assert.Equal(Website, session.EmbeddedOrigin);
    }

    [Fact]
    public void An_embedded_session_cannot_be_opened_without_a_website()
    {
        Assert.Throws<ArgumentNullException>(
            () => WebChatSession.OpenEmbedded(
                ConversationId.New(),
                TenantId.New(),
                WebChatEndpointId.New(),
                AiAgentId.New(),
                Digest(1),
                null!,
                Noon,
                Tomorrow));
    }

    [Fact]
    public void A_stored_row_that_disagrees_with_itself_is_refused()
    {
        // The database has a check constraint saying the same thing. This is the
        // aggregate refusing to be built from a row that could not have been
        // written by this code in the first place.
        Assert.Throws<DomainValidationException>(
            () => WebChatSession.Restore(
                ConversationId.New(),
                TenantId.New(),
                WebChatEndpointId.New(),
                AiAgentId.New(),
                Digest(1),
                WebChatClientMode.Embedded,
                embeddedOrigin: null,
                Noon,
                Tomorrow));

        Assert.Throws<DomainValidationException>(
            () => WebChatSession.Restore(
                ConversationId.New(),
                TenantId.New(),
                WebChatEndpointId.New(),
                AiAgentId.New(),
                Digest(1),
                WebChatClientMode.Hosted,
                Website,
                Noon,
                Tomorrow));
    }

    [Fact]
    public void A_session_admits_only_the_transport_it_was_opened_through()
    {
        Assert.True(Hosted().AdmitsMode(WebChatClientMode.Hosted));
        Assert.False(Hosted().AdmitsMode(WebChatClientMode.Embedded));

        Assert.True(Embedded().AdmitsMode(WebChatClientMode.Embedded));
        Assert.False(Embedded().AdmitsMode(WebChatClientMode.Hosted));
    }

    [Fact]
    public void The_transport_is_checked_independently_of_the_credential()
    {
        // Mode is not a second factor on the secret: a caller holding the right
        // credential on the wrong transport is refused, and a caller on the right
        // transport with the wrong credential is refused. Both must hold, which is
        // why the runtime checks them separately.
        WebChatSession embedded = Embedded();

        Assert.True(embedded.Admits(Digest(1)));
        Assert.False(embedded.AdmitsMode(WebChatClientMode.Hosted));

        Assert.False(embedded.Admits(Digest(2)));
        Assert.True(embedded.AdmitsMode(WebChatClientMode.Embedded));
    }

    [Fact]
    public void A_stored_embedded_session_is_rebuilt_exactly_as_it_was_written()
    {
        ConversationId conversation = ConversationId.New();

        WebChatSession restored = WebChatSession.Restore(
            conversation,
            TenantId.New(),
            WebChatEndpointId.New(),
            AiAgentId.New(),
            Digest(3),
            WebChatClientMode.Embedded,
            Website,
            Noon,
            Tomorrow);

        Assert.Equal(conversation, restored.ConversationId);
        Assert.Equal(WebChatClientMode.Embedded, restored.Mode);
        Assert.Equal(Website, restored.EmbeddedOrigin);
        Assert.True(restored.Admits(Digest(3)));
    }
}
