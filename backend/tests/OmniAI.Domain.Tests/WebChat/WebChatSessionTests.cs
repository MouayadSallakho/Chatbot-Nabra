using System.Security.Cryptography;
using OmniAI.Domain;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.WebChat;

namespace OmniAI.Domain.Tests.WebChat;

/// <summary>
/// The anonymous claim that makes a public conversation private.
/// </summary>
public sealed class WebChatSessionTests
{
    private static readonly DateTimeOffset Noon =
        new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    private static WebChatAccessTokenHash Digest(byte seed) =>
        WebChatAccessTokenHash.From(Enumerable.Repeat(seed, 32).ToArray());

    private static WebChatSession Open(WebChatAccessTokenHash hash) =>
        WebChatSession.Open(
            ConversationId.New(),
            TenantId.New(),
            WebChatEndpointId.New(),
            AiAgentId.New(),
            hash,
            Noon);

    [Fact]
    public void A_session_admits_the_secret_that_opened_it()
    {
        WebChatSession session = Open(Digest(7));

        Assert.True(session.Admits(Digest(7)));
    }

    [Fact]
    public void A_session_refuses_any_other_secret()
    {
        // This is the whole public authorisation model: knowing a conversation id
        // is not enough, and a random credential is not enough either.
        WebChatSession session = Open(Digest(7));

        Assert.False(session.Admits(Digest(8)));
        Assert.False(session.Admits(null));
    }

    [Fact]
    public void A_session_records_the_agent_it_started_with()
    {
        // The stable binding. A conversation answers from the agent it began with,
        // read from here rather than followed through the endpoint -- so
        // re-pointing an endpoint could never move a conversation already running.
        AiAgentId agent = AiAgentId.New();

        WebChatSession session = WebChatSession.Open(
            ConversationId.New(),
            TenantId.New(),
            WebChatEndpointId.New(),
            agent,
            Digest(1),
            Noon);

        Assert.Equal(agent, session.AiAgentId);
    }

    [Fact]
    public void A_session_must_name_everything_it_binds()
    {
        ConversationId conversation = ConversationId.New();
        TenantId workspace = TenantId.New();
        WebChatEndpointId endpoint = WebChatEndpointId.New();
        AiAgentId agent = AiAgentId.New();

        Assert.Throws<DomainValidationException>(() => WebChatSession.Open(
            default, workspace, endpoint, agent, Digest(1), Noon));

        Assert.Throws<DomainValidationException>(() => WebChatSession.Open(
            conversation, default, endpoint, agent, Digest(1), Noon));

        Assert.Throws<DomainValidationException>(() => WebChatSession.Open(
            conversation, workspace, default, agent, Digest(1), Noon));

        Assert.Throws<DomainValidationException>(() => WebChatSession.Open(
            conversation, workspace, endpoint, default, Digest(1), Noon));

        Assert.Throws<ArgumentNullException>(() => WebChatSession.Open(
            conversation, workspace, endpoint, agent, null!, Noon));
    }
}

/// <summary>
/// The digest of a conversation credential.
/// </summary>
public sealed class WebChatAccessTokenHashTests
{
    [Fact]
    public void A_digest_is_exactly_the_length_of_the_hash_this_platform_uses()
    {
        Assert.Throws<DomainValidationException>(
            () => WebChatAccessTokenHash.From(new byte[31]));

        Assert.Throws<DomainValidationException>(
            () => WebChatAccessTokenHash.From(new byte[33]));
    }

    [Fact]
    public void Two_digests_of_the_same_bytes_are_equal_under_every_spelling()
    {
        // A security check must not depend on which comparison the author reached
        // for, so == and Equals must agree.
        byte[] bytes = RandomNumberGenerator.GetBytes(32);

        WebChatAccessTokenHash left = WebChatAccessTokenHash.From(bytes);
        WebChatAccessTokenHash right = WebChatAccessTokenHash.From(bytes);

        Assert.True(left == right);
        Assert.True(left.Equals(right));
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
        Assert.False(left != right);
    }

    [Fact]
    public void Different_digests_are_not_equal()
    {
        WebChatAccessTokenHash left = WebChatAccessTokenHash.From(new byte[32]);
        WebChatAccessTokenHash right = WebChatAccessTokenHash.From(
            Enumerable.Repeat((byte)1, 32).ToArray());

        Assert.True(left != right);
    }

    [Fact]
    public void The_stored_bytes_cannot_be_reached_and_altered()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(32);
        WebChatAccessTokenHash hash = WebChatAccessTokenHash.From(bytes);

        byte[] copy = hash.ToArray();
        copy[0] ^= 0xFF;

        Assert.True(hash == WebChatAccessTokenHash.From(bytes));
    }

    [Fact]
    public void A_digest_never_prints_itself()
    {
        // Nothing that could be matched against a stolen list should reach a log
        // line through ordinary string interpolation.
        WebChatAccessTokenHash hash = WebChatAccessTokenHash.From(
            RandomNumberGenerator.GetBytes(32));

        Assert.Equal("WebChatAccessTokenHash", hash.ToString());
    }
}
