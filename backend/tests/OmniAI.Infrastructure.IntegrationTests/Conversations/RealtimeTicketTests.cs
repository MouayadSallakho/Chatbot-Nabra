using StackExchange.Redis;
using OmniAI.Application.Conversations.Realtime;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.WebChat;
using OmniAI.Infrastructure.Conversations.Realtime;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Conversations;

/// <summary>
/// The short-lived permission a visitor's browser uses to listen.
/// </summary>
/// <remarks>
/// Against a real Redis, because the properties being asserted are the store's:
/// that a ticket expires, that only its digest is kept, and that one conversation
/// cannot be listened to with another's ticket.
/// </remarks>
public sealed class RealtimeTicketTests(RedisTestServer redis)
    : IClassFixture<RedisTestServer>, IAsyncLifetime
{
    private readonly RedisTestServer _redis = redis;

    private ConnectionMultiplexer _client = null!;
    private RedisRealtimeTickets _tickets = null!;

    public async Task InitializeAsync()
    {
        _client = await _redis.ConnectAsync().ConfigureAwait(false);
        _tickets = new RedisRealtimeTickets(_client, _redis.KeyPrefix);
    }

    public async Task DisposeAsync()
    {
        await _redis.ClearAsync().ConfigureAwait(false);
        await _client.DisposeAsync().ConfigureAwait(false);
    }

    [Fact]
    public async Task A_ticket_admits_the_conversation_it_was_minted_for()
    {
        ConversationId conversation = ConversationId.New();

        RealtimeTicket ticket = await _tickets.IssueAsync(conversation, WebChatClientMode.Hosted);

        RealtimeTicketClaim? claim = await _tickets.RedeemAsync(ticket.Value);

        Assert.NotNull(claim);
        Assert.Equal(conversation, claim.ConversationId);
        Assert.Equal(WebChatClientMode.Hosted, claim.Mode);
    }

    [Fact]
    public async Task A_ticket_names_one_conversation_and_not_another()
    {
        // The whole point of binding. Holding a ticket for your own conversation
        // must not admit you to somebody else's.
        ConversationId mine = ConversationId.New();
        ConversationId theirs = ConversationId.New();

        RealtimeTicket ticket = await _tickets.IssueAsync(mine, WebChatClientMode.Embedded);

        RealtimeTicketClaim? claim = await _tickets.RedeemAsync(ticket.Value);

        Assert.Equal(mine, claim!.ConversationId);
        Assert.NotEqual(theirs, claim.ConversationId);
    }

    [Fact]
    public async Task The_transport_is_part_of_what_a_ticket_says()
    {
        // Phase 18 kept the hosted and embedded transports apart deliberately.
        // A realtime channel that ignored the distinction would be a bridge
        // between them.
        ConversationId conversation = ConversationId.New();

        RealtimeTicket hosted = await _tickets.IssueAsync(conversation, WebChatClientMode.Hosted);
        RealtimeTicket embedded = await _tickets.IssueAsync(conversation, WebChatClientMode.Embedded);

        Assert.Equal(
            WebChatClientMode.Hosted,
            (await _tickets.RedeemAsync(hosted.Value))!.Mode);

        Assert.Equal(
            WebChatClientMode.Embedded,
            (await _tickets.RedeemAsync(embedded.Value))!.Mode);

        // And they are different values: minting for the other transport does
        // not hand back the first ticket.
        Assert.NotEqual(hosted.Value, embedded.Value);
    }

    [Fact]
    public async Task An_unknown_ticket_admits_nothing()
    {
        Assert.Null(await _tickets.RedeemAsync("Zm9yZ2VkLXRpY2tldC12YWx1ZS1ub3QtcmVhbA"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("short")]
    [InlineData("has spaces in it and is far too long to be a ticket value at all")]
    [InlineData("contains/slashes/and+plus=signs/which/are/not/base64url/at/all!!")]
    public async Task A_malformed_ticket_admits_nothing(string? presented)
    {
        // Refused on shape before Redis is touched, so a flood of junk cannot be
        // turned into a flood of round trips.
        Assert.Null(await _tickets.RedeemAsync(presented));
    }

    [Fact]
    public async Task A_conversation_id_is_not_a_ticket()
    {
        // Holding the conversation id -- which is not a secret, and appears in an
        // administrator's list -- must admit nobody.
        ConversationId conversation = ConversationId.New();

        await _tickets.IssueAsync(conversation, WebChatClientMode.Hosted);

        Assert.Null(await _tickets.RedeemAsync(conversation.Value.ToString("N")));
        Assert.Null(await _tickets.RedeemAsync(conversation.Value.ToString("D")));
    }

    [Fact]
    public async Task The_raw_ticket_is_never_stored()
    {
        // Anybody who can read the key space learns which conversations have
        // listeners, and nothing they could use to become one.
        ConversationId conversation = ConversationId.New();

        RealtimeTicket ticket = await _tickets.IssueAsync(conversation, WebChatClientMode.Hosted);

        IReadOnlyList<string> keys = await _redis.KeysAsync();

        Assert.NotEmpty(keys);
        Assert.DoesNotContain(keys, key => key.Contains(ticket.Value, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_ticket_carries_a_short_lifetime_and_expires()
    {
        ConversationId conversation = ConversationId.New();

        RealtimeTicket ticket = await _tickets.IssueAsync(conversation, WebChatClientMode.Hosted);

        // Short enough that a value captured from a proxy log is useless by the
        // time anybody reads it.
        Assert.InRange(ticket.ExpiresInSeconds, 60, 120);

        // And the store really does expire it, rather than merely reporting
        // that it will: the one key it wrote carries a TTL inside that bound.
        IReadOnlyList<string> keys = await _redis.KeysAsync();

        string key = Assert.Single(keys);

        TimeSpan? remaining = await _redis.TimeToLiveAsync(key);

        Assert.NotNull(remaining);
        Assert.InRange(remaining.Value.TotalSeconds, 1, 120);
    }

    [Fact]
    public async Task Two_tickets_are_never_the_same_value()
    {
        ConversationId conversation = ConversationId.New();

        RealtimeTicket first = await _tickets.IssueAsync(conversation, WebChatClientMode.Hosted);
        RealtimeTicket second = await _tickets.IssueAsync(conversation, WebChatClientMode.Hosted);

        Assert.NotEqual(first.Value, second.Value);
    }
}
