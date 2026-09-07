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

    /// <summary>A day later: the platform's default public session lifetime.</summary>
    private static readonly DateTimeOffset Tomorrow = Noon.AddHours(24);

    private static WebChatAccessTokenHash Digest(byte seed) =>
        WebChatAccessTokenHash.From(Enumerable.Repeat(seed, 32).ToArray());

    private static WebChatSession Open(WebChatAccessTokenHash hash) =>
        WebChatSession.Open(
            ConversationId.New(),
            TenantId.New(),
            WebChatEndpointId.New(),
            AiAgentId.New(),
            hash,
            Noon,
            Tomorrow);

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
            Noon,
            Tomorrow);

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
            default, workspace, endpoint, agent, Digest(1), Noon, Tomorrow));

        Assert.Throws<DomainValidationException>(() => WebChatSession.Open(
            conversation, default, endpoint, agent, Digest(1), Noon, Tomorrow));

        Assert.Throws<DomainValidationException>(() => WebChatSession.Open(
            conversation, workspace, default, agent, Digest(1), Noon, Tomorrow));

        Assert.Throws<DomainValidationException>(() => WebChatSession.Open(
            conversation, workspace, endpoint, default, Digest(1), Noon, Tomorrow));

        Assert.Throws<ArgumentNullException>(() => WebChatSession.Open(
            conversation, workspace, endpoint, agent, null!, Noon, Tomorrow));
    }

    [Fact]
    public void A_session_records_the_expiry_it_was_opened_with()
    {
        // Stored, not derived. This is what makes a change to the platform's
        // session lifetime apply to new sessions only, instead of silently
        // extending or revoking credentials people are already holding.
        WebChatSession session = Open(Digest(3));

        Assert.Equal(Tomorrow, session.ExpiresAt);
    }

    [Fact]
    public void A_session_is_live_until_its_expiry_and_not_after()
    {
        WebChatSession session = Open(Digest(3));

        Assert.False(session.HasExpired(Noon));
        Assert.False(session.HasExpired(Tomorrow.AddTicks(-1)));

        // Inclusive at the boundary: at the stated instant the claim is over. The
        // alternative would leave a credential valid *at* the moment it expires,
        // which is a needlessly confusing thing for an expiry to mean.
        Assert.True(session.HasExpired(Tomorrow));
        Assert.True(session.HasExpired(Tomorrow.AddHours(1)));
    }

    [Fact]
    public void Expiry_is_a_separate_question_from_the_credential()
    {
        // An expired session still knows which secret opened it. Keeping the two
        // apart is what lets the access check refuse an expired session with the
        // same not-found answer an unknown conversation gets, rather than folding
        // a clock into a constant-time digest comparison.
        WebChatSession session = Open(Digest(4));

        Assert.True(session.Admits(Digest(4)));
        Assert.True(session.HasExpired(Tomorrow));
    }

    [Fact]
    public void A_session_cannot_expire_before_it_begins()
    {
        // A credential that never worked is a configuration mistake worth refusing
        // loudly rather than a conversation nobody can continue.
        Assert.Throws<DomainValidationException>(() => WebChatSession.Open(
            ConversationId.New(),
            TenantId.New(),
            WebChatEndpointId.New(),
            AiAgentId.New(),
            Digest(1),
            Noon,
            Noon));

        Assert.Throws<DomainValidationException>(() => WebChatSession.Open(
            ConversationId.New(),
            TenantId.New(),
            WebChatEndpointId.New(),
            AiAgentId.New(),
            Digest(1),
            Noon,
            Noon.AddSeconds(-1)));
    }

    [Fact]
    public void Both_instants_are_stored_in_utc()
    {
        // Everything downstream compares them against a UTC clock, and a stored
        // offset would make the comparison depend on where the process was running.
        DateTimeOffset local = new(2026, 9, 7, 14, 0, 0, TimeSpan.FromHours(2));

        WebChatSession session = WebChatSession.Open(
            ConversationId.New(),
            TenantId.New(),
            WebChatEndpointId.New(),
            AiAgentId.New(),
            Digest(1),
            local,
            local.AddHours(24));

        Assert.Equal(TimeSpan.Zero, session.CreatedAt.Offset);
        Assert.Equal(TimeSpan.Zero, session.ExpiresAt.Offset);
        Assert.Equal(local.ToUniversalTime(), session.CreatedAt);
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
