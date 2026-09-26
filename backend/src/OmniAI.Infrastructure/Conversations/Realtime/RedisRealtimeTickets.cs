using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using StackExchange.Redis;
using OmniAI.Application.Conversations.Realtime;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.WebChat;

namespace OmniAI.Infrastructure.Conversations.Realtime;

/// <summary>
/// Short-lived realtime tickets, in Redis.
/// </summary>
/// <remarks>
/// <strong>Redis rather than the database, because a ticket is not a
/// record.</strong> It is worthless in ninety seconds and nobody will ever want
/// to know that one was issued. Putting it in PostgreSQL would mean a table that
/// grows for ever unless something sweeps it, a sweeper to write and schedule,
/// and a write to durable storage on every widget load. An expiring key needs
/// none of that: the store forgets on its own, exactly on time.
/// <para>
/// <strong>Only a digest is stored, and the raw ticket is never logged.</strong>
/// Anybody who can read the key space learns which conversations have listeners
/// and nothing they could use to become one. Because the value is 256 bits of
/// cryptographic randomness rather than something guessable, a plain SHA-256 is
/// enough here — there is no dictionary to run against it, which is what makes
/// this different from the network-key pseudonym, where the input space is small
/// enough to enumerate and an HMAC under a secret is required.
/// </para>
/// <para>
/// <strong>Not single-use.</strong> A SignalR connection presents its token more
/// than once — negotiate, then the transport — and burning it on first sight
/// would break every connection it was supposed to authorise. The lifetime is the
/// bound instead, and it is short.
/// </para>
/// <para>
/// <strong>Failure is refusal, not fallback.</strong> If Redis cannot be reached
/// the ticket cannot be issued or checked, and a listener is turned away. That
/// costs a visitor their live updates for as long as the outage lasts; the
/// alternative — admitting an unverified listener — costs somebody their
/// transcript.
/// </para>
/// </remarks>
internal sealed class RedisRealtimeTickets : IRealtimeTickets
{
    /// <summary>
    /// How long a ticket lives.
    /// </summary>
    /// <remarks>
    /// Ninety seconds: long enough to cover a page still loading, a slow
    /// handshake and a clock a little out of step, and short enough that a value
    /// captured from a URL in a proxy log is useless by the time anybody reads it.
    /// A client that needs to reconnect mints a new one, which costs one
    /// already-authorised request.
    /// </remarks>
    internal const int LifetimeSeconds = 90;

    /// <summary>The number of random bytes in a ticket.</summary>
    /// <remarks>
    /// Thirty-two, the same strength as the conversation credential it stands in
    /// for. A short-lived secret is still a secret, and there is no reason to make
    /// this one weaker than the thing it protects.
    /// </remarks>
    private const int TicketBytes = 32;

    private readonly IConnectionMultiplexer _redis;
    private readonly string _keyPrefix;

    public RedisRealtimeTickets(IConnectionMultiplexer redis, string keyPrefix)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _keyPrefix = keyPrefix ?? throw new ArgumentNullException(nameof(keyPrefix));
    }

    public async Task<RealtimeTicket> IssueAsync(
        ConversationId conversationId,
        WebChatClientMode mode,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string ticket = NewTicket();

        // The transport is part of the stored value, so an embedded ticket
        // cannot admit a hosted listener or the other way round. Phase 18 kept
        // those two transports apart deliberately, and a realtime channel that
        // ignored the distinction would be a bridge between them.
        string claim = string.Create(
            CultureInfo.InvariantCulture,
            $"{mode}:{conversationId.Value:D}");

        IDatabase database = _redis.GetDatabase();

        try
        {
            await database
                .StringSetAsync(
                    KeyFor(ticket),
                    claim,
                    TimeSpan.FromSeconds(LifetimeSeconds))
                .ConfigureAwait(false);
        }
        catch (Exception failure) when (
            WebChat.Protection.RedisPublicChatProtection.IsProtectionStoreFailure(failure, cancellationToken))
        {
            // Phase 34: with the store down this used to escape as a 500. The
            // truthful answer is the one an unconfigured deployment already gives:
            // live updates are unavailable; messages still work.
            throw new RealtimeUnavailableException();
        }

        return new RealtimeTicket(ticket, LifetimeSeconds);
    }

    public async Task<RealtimeTicketClaim?> RedeemAsync(
        string? ticket,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Shape is checked before Redis is touched, so a flood of junk tickets
        // cannot be turned into a flood of round trips.
        if (string.IsNullOrWhiteSpace(ticket) || !LooksLikeTicket(ticket))
        {
            return null;
        }

        IDatabase database = _redis.GetDatabase();

        RedisValue stored = await database.StringGetAsync(KeyFor(ticket)).ConfigureAwait(false);

        if (stored.IsNullOrEmpty)
        {
            return null;
        }

        string claim = stored.ToString();
        int separator = claim.IndexOf(':', StringComparison.Ordinal);

        if (separator <= 0)
        {
            return null;
        }

        if (!Enum.TryParse(claim[..separator], ignoreCase: false, out WebChatClientMode mode)
            || !Enum.IsDefined(mode))
        {
            return null;
        }

        return Guid.TryParse(claim[(separator + 1)..], out Guid conversation)
            ? new RealtimeTicketClaim(ConversationId.From(conversation), mode)
            : null;
    }

    private static string NewTicket() =>
        Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TicketBytes));

    /// <summary>
    /// A cheap shape check, so obviously invalid input never reaches Redis.
    /// </summary>
    private static bool LooksLikeTicket(string ticket)
    {
        if (ticket.Length is < 32 or > 64)
        {
            return false;
        }

        foreach (char character in ticket)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The key a ticket is stored under: its digest, never the ticket itself.
    /// </summary>
    private string KeyFor(string ticket)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(ticket));

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{_keyPrefix}realtime:{Convert.ToHexStringLower(digest)}");
    }
}
