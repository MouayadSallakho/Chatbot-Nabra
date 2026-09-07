using System.Buffers.Text;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using OmniAI.Application.WebChat.Protection;

namespace OmniAI.Infrastructure.WebChat.Protection;

/// <summary>
/// Turns a client address into a pseudonym that can be counted but not read.
/// </summary>
/// <remarks>
/// <strong>The requirement is uncomfortable and worth stating plainly.</strong>
/// Rate limiting by network needs a value that is the same for the same visitor
/// and different for a different one — which is, definitionally, an identifier
/// for a person's network location. The question is not whether to have one, but
/// what it is allowed to be. Here it is a keyed digest, so the counter store,
/// every log line near it and every screenshot of that log contain something that
/// cannot be turned back into an address by anybody who reads them.
/// <para>
/// <strong>Keyed, not merely hashed.</strong> A plain SHA-256 of an IPv4 address
/// is reversible by brute force in seconds: there are only four billion of them,
/// and an attacker who obtained the counter keys could recover every visitor's
/// address with a laptop. An HMAC under a secret this deployment holds makes that
/// attack require the secret as well, which is why the secret exists and why it
/// is a dedicated one.
/// </para>
/// <para>
/// <strong>A dedicated secret, never a shared one.</strong> Not the data
/// protection key that signs the authentication cookie, and not the AI provider's
/// API key. Both would mean one leaked value compromised two unrelated things,
/// and it would mean rotating either for its own reasons silently reset every
/// rate-limit bucket. Rotating <em>this</em> one does reset the IP-keyed buckets —
/// old keys simply stop being produced and expire on their own TTLs — which is an
/// acceptable, momentary loss of counting and is recorded as such.
/// </para>
/// <para>
/// <strong>Truncated to 128 bits.</strong> Ample against collision for a key
/// space of network addresses, and it keeps the counter names short. Truncating
/// an HMAC is a standard, safe construction; it does not weaken the one-way
/// property.
/// </para>
/// <para>
/// <strong>The digest never leaves the server.</strong> It is not returned in a
/// response, not put in a cookie, not shown to a visitor, and not written into
/// any persisted business record. It exists to name a counter.
/// </para>
/// </remarks>
internal sealed class HmacClientNetworkKeys : IClientNetworkKeys, IDisposable
{
    /// <summary>How much of the digest becomes the key.</summary>
    private const int KeyBytes = 16;

    /// <summary>
    /// Bytes of an IPv6 address that identify the network rather than the host.
    /// </summary>
    /// <remarks>
    /// <strong>IPv6 is bucketed by /64, and IPv4 is not.</strong> A single IPv6
    /// customer is routinely handed a whole /64 — eighteen quintillion addresses —
    /// and can pick a fresh one for every request without leaving their own
    /// network. Keying on the full address would make every per-network limit
    /// free to bypass for exactly the visitors who have IPv6, which is most of
    /// them on mobile. A /64 is the smallest unit an operator assigns, so it is
    /// the smallest unit worth counting.
    /// <para>
    /// The cost is that genuinely separate people behind one /64 share an
    /// allowance. That is already true of everybody behind one IPv4 address, which
    /// is most corporate and mobile traffic, and the endpoint-aggregate limit is
    /// what keeps the per-network numbers generous enough for it not to matter.
    /// </para>
    /// </remarks>
    private const int IPv6NetworkBytes = 8;

    private readonly HMACSHA256? _mac;
    private readonly string? _unavailable;

    /// <summary>
    /// Builds a key source from this deployment's secret.
    /// </summary>
    /// <param name="secret">
    /// The configured secret, or <see langword="null"/> when there is none.
    /// </param>
    /// <param name="unavailable">
    /// Why no key can be derived, when <paramref name="secret"/> is absent. The
    /// operator's explanation, never shown to a visitor.
    /// </param>
    private HmacClientNetworkKeys(byte[]? secret, string? unavailable)
    {
        _mac = secret is null ? null : new HMACSHA256(secret);
        _unavailable = unavailable;
    }

    /// <summary>A working key source over a configured secret.</summary>
    /// <remarks>
    /// The secret is taken as text and consumed as its UTF-8 bytes. No structure
    /// is required of it and none is assumed: an operator may supply a long random
    /// string, a base64 blob, or a passphrase, and all of them are keys to an HMAC.
    /// A short one is refused, because a guessable key is the same as no key.
    /// </remarks>
    public static HmacClientNetworkKeys From(string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);

        return new HmacClientNetworkKeys(Encoding.UTF8.GetBytes(secret), unavailable: null);
    }

    /// <summary>
    /// A key source that refuses, because this deployment configured no secret.
    /// </summary>
    /// <remarks>
    /// Fails closed at the point of use rather than at startup, matching how this
    /// platform already treats a missing database connection string and a missing
    /// AI provider key: the application starts, the health probe answers, the
    /// admin works, and the capability that needs the setting refuses explicitly
    /// and says what is missing. A startup crash would take down a whole
    /// deployment's administration over a setting only the public chat needs.
    /// </remarks>
    public static HmacClientNetworkKeys Unavailable(string reason) =>
        new(secret: null, unavailable: reason);

    public ClientNetworkKey Derive(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (_mac is null)
        {
            throw new PublicChatProtectionUnavailableException(
                _unavailable ?? "No client network key secret is configured.");
        }

        byte[] network = NetworkBytes(address);

        Span<byte> digest = stackalloc byte[HMACSHA256.HashSizeInBytes];

        // The HMAC instance is not thread-safe for concurrent ComputeHash calls,
        // and this is a singleton serving every request. The one-time key schedule
        // is what makes reusing the instance worthwhile, so the hash itself is
        // taken under a lock rather than by allocating a new HMAC per request.
        lock (_mac)
        {
            _mac.TryComputeHash(network, digest, out _);
        }

        Span<char> encoded = stackalloc char[Base64Url.GetEncodedLength(KeyBytes) + 4];

        if (!Base64Url.TryEncodeToChars(digest[..KeyBytes], encoded, out int written))
        {
            // Unreachable for a fixed 16-byte input. Thrown rather than truncated:
            // a key that failed to encode must never become a shorter one that
            // collides with somebody else's.
            throw new InvalidOperationException("A client network key could not be encoded.");
        }

        return ClientNetworkKey.From(new string(encoded[..written]));
    }

    public void Dispose() => _mac?.Dispose();

    /// <summary>
    /// The network part of an address, in a single canonical form.
    /// </summary>
    /// <remarks>
    /// <strong>Normalised before hashing, because a digest of an inconsistent
    /// input is an inconsistent key.</strong> The same visitor must not land in
    /// two buckets because Kestrel reported their address one way on one
    /// connection and another way on the next.
    /// <para>
    /// Two things are canonicalised. An IPv4-mapped IPv6 address —
    /// <c>::ffff:203.0.113.7</c>, which is how a dual-stack socket reports an IPv4
    /// client — is folded to the IPv4 address it denotes, so one client is not
    /// counted twice depending on how the listener is configured. And an IPv6
    /// scope id, which appears on link-local addresses and varies by interface, is
    /// dropped by taking the raw bytes rather than the textual form.
    /// </para>
    /// <para>
    /// The bytes are hashed rather than the string. Text has variants — case,
    /// zero-compression, a scope suffix — and every one of them would have been a
    /// second bucket for the same visitor.
    /// </para>
    /// </remarks>
    private static byte[] NetworkBytes(IPAddress address)
    {
        IPAddress canonical = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

        byte[] bytes = canonical.GetAddressBytes();

        if (canonical.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return bytes;
        }

        // Keep the routing prefix and discard the host part, so a visitor cannot
        // simply choose a different address inside their own /64.
        Array.Clear(bytes, IPv6NetworkBytes, bytes.Length - IPv6NetworkBytes);

        return bytes;
    }
}
