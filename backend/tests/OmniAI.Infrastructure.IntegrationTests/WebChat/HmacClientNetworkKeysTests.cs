using System.Net;
using System.Security.Cryptography;
using OmniAI.Application.WebChat.Protection;
using OmniAI.Infrastructure.WebChat.Protection;

namespace OmniAI.Infrastructure.IntegrationTests.WebChat;

/// <summary>
/// The pseudonym that makes rate limiting possible without keeping addresses.
/// </summary>
/// <remarks>
/// <strong>Two properties in tension, and both have to hold.</strong> The key must
/// be <em>stable</em> — the same visitor always lands in the same bucket, or
/// counting is meaningless — and it must be <em>one-way</em>, or the counter store
/// and every log line near it becomes a register of who visited. These tests
/// assert both, plus the normalisation without which "the same visitor" is not
/// well defined.
/// </remarks>
public sealed class HmacClientNetworkKeysTests
{
    private static readonly string Secret =
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    [Fact]
    public void The_same_address_always_yields_the_same_key()
    {
        // Without this there is no counting at all: every request would be a fresh
        // bucket and every limit would be infinite.
        using HmacClientNetworkKeys keys = HmacClientNetworkKeys.From(Secret);

        ClientNetworkKey first = keys.Derive(IPAddress.Parse("203.0.113.7"));
        ClientNetworkKey second = keys.Derive(IPAddress.Parse("203.0.113.7"));

        Assert.Equal(first, second);
        Assert.False(first.IsEmpty);
    }

    [Fact]
    public void Different_addresses_yield_different_keys()
    {
        using HmacClientNetworkKeys keys = HmacClientNetworkKeys.From(Secret);

        Assert.NotEqual(
            keys.Derive(IPAddress.Parse("203.0.113.7")),
            keys.Derive(IPAddress.Parse("203.0.113.8")));
    }

    [Fact]
    public void The_key_contains_no_part_of_the_address()
    {
        // The whole point. A key that embedded the address would put it in every
        // Redis key name and in every log line that mentions one.
        using HmacClientNetworkKeys keys = HmacClientNetworkKeys.From(Secret);

        ClientNetworkKey key = keys.Derive(IPAddress.Parse("203.0.113.77"));

        foreach (string fragment in (string[])["203", "113", "77", "203.0.113"])
        {
            Assert.DoesNotContain(fragment, key.Value, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_key_is_short_url_safe_text()
    {
        // It becomes part of a Redis key name, so it must not need quoting or
        // escaping anywhere it is written.
        using HmacClientNetworkKeys keys = HmacClientNetworkKeys.From(Secret);

        string key = keys.Derive(IPAddress.Parse("203.0.113.7")).Value;

        Assert.Equal(22, key.Length);

        foreach (char character in key)
        {
            Assert.True(
                char.IsAsciiLetterOrDigit(character) || character is '-' or '_',
                $"'{character}' is not base64url.");
        }
    }

    [Fact]
    public void A_different_secret_yields_a_different_key_for_the_same_address()
    {
        // Rotation. The documented consequence is that the IP-keyed buckets reset:
        // old keys simply stop being produced and expire on their own TTLs, which
        // is a momentary loss of counting rather than a loss of protection.
        using HmacClientNetworkKeys first = HmacClientNetworkKeys.From(Secret);
        using HmacClientNetworkKeys second = HmacClientNetworkKeys.From(
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));

        IPAddress address = IPAddress.Parse("203.0.113.7");

        Assert.NotEqual(first.Derive(address), second.Derive(address));
    }

    [Fact]
    public void The_same_key_source_is_safe_to_use_from_many_threads()
    {
        // It is a singleton serving every request, and the underlying HMAC is not
        // thread-safe for concurrent hashing. If the lock were ever dropped this
        // would produce corrupted or mismatched keys, which would silently scramble
        // every rate-limit bucket.
        using HmacClientNetworkKeys keys = HmacClientNetworkKeys.From(Secret);

        IPAddress address = IPAddress.Parse("203.0.113.7");
        ClientNetworkKey expected = keys.Derive(address);

        ClientNetworkKey[] derived =
        [
            .. Enumerable.Range(0, 200)
                .AsParallel()
                .WithDegreeOfParallelism(8)
                .Select(_ => keys.Derive(address)),
        ];

        Assert.All(derived, key => Assert.Equal(expected, key));
    }

    // ---- Normalisation ---------------------------------------------------

    [Fact]
    public void An_ipv4_mapped_ipv6_address_is_the_same_visitor_as_the_ipv4_one()
    {
        // How a dual-stack socket reports an IPv4 client. Without folding these
        // together, one visitor would be counted in two buckets depending on how
        // the listener happened to be configured -- which halves every limit's
        // effectiveness for no reason anybody could see.
        using HmacClientNetworkKeys keys = HmacClientNetworkKeys.From(Secret);

        Assert.Equal(
            keys.Derive(IPAddress.Parse("203.0.113.7")),
            keys.Derive(IPAddress.Parse("::ffff:203.0.113.7")));
    }

    [Fact]
    public void An_ipv6_scope_id_does_not_change_the_visitor()
    {
        // Scope ids appear on link-local addresses and vary by interface. Hashing
        // the raw bytes rather than the text is what drops them.
        using HmacClientNetworkKeys keys = HmacClientNetworkKeys.From(Secret);

        Assert.Equal(
            keys.Derive(IPAddress.Parse("fe80::1")),
            keys.Derive(IPAddress.Parse("fe80::1%12")));
    }

    [Fact]
    public void Two_addresses_in_one_ipv6_slash_64_are_one_visitor()
    {
        // The decision that stops IPv6 from being a free bypass. A single customer
        // is routinely handed a whole /64 and can pick a fresh address for every
        // request without leaving their own network; keying on the full address
        // would make every per-network limit free to evade for exactly the visitors
        // who have IPv6, which is most of them on mobile.
        using HmacClientNetworkKeys keys = HmacClientNetworkKeys.From(Secret);

        Assert.Equal(
            keys.Derive(IPAddress.Parse("2001:db8:1234:5678::1")),
            keys.Derive(IPAddress.Parse("2001:db8:1234:5678:aaaa:bbbb:cccc:dddd")));
    }

    [Fact]
    public void Two_different_ipv6_slash_64_networks_are_two_visitors()
    {
        // The other half: bucketing by prefix must not collapse genuinely separate
        // networks into one, or a busy /48 would refuse its own customers.
        using HmacClientNetworkKeys keys = HmacClientNetworkKeys.From(Secret);

        Assert.NotEqual(
            keys.Derive(IPAddress.Parse("2001:db8:1234:5678::1")),
            keys.Derive(IPAddress.Parse("2001:db8:1234:5679::1")));
    }

    [Fact]
    public void An_ipv4_address_is_not_bucketed_by_prefix()
    {
        // IPv4 addresses are scarce and shared, so the whole address is the
        // smallest unit worth counting. Bucketing them by /24 would put unrelated
        // networks in one allowance.
        using HmacClientNetworkKeys keys = HmacClientNetworkKeys.From(Secret);

        Assert.NotEqual(
            keys.Derive(IPAddress.Parse("203.0.113.1")),
            keys.Derive(IPAddress.Parse("203.0.113.2")));
    }

    [Fact]
    public void An_ipv4_and_an_ipv6_address_are_never_confused()
    {
        using HmacClientNetworkKeys keys = HmacClientNetworkKeys.From(Secret);

        Assert.NotEqual(
            keys.Derive(IPAddress.Parse("0.0.0.0")),
            keys.Derive(IPAddress.Parse("::")));
    }

    // ---- Refusals --------------------------------------------------------

    [Fact]
    public void A_key_source_with_no_secret_refuses_rather_than_inventing_one()
    {
        // Fail closed. A fallback key would be a shared bucket every visitor with
        // no derivable identity fell into, which is one unmetered lane through the
        // whole policy.
        using HmacClientNetworkKeys keys = HmacClientNetworkKeys.Unavailable(
            "No secret is configured for this deployment.");

        PublicChatProtectionUnavailableException failure =
            Assert.Throws<PublicChatProtectionUnavailableException>(
                () => keys.Derive(IPAddress.Parse("203.0.113.7")));

        Assert.Contains("No secret is configured", failure.Reason, StringComparison.Ordinal);

        // And the visitor still learns nothing.
        Assert.DoesNotContain("secret", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_blank_secret_is_refused_at_construction()
    {
        Assert.Throws<ArgumentException>(() => HmacClientNetworkKeys.From("   "));
        Assert.Throws<ArgumentException>(() => HmacClientNetworkKeys.From(string.Empty));
    }

    [Fact]
    public void Deriving_from_nothing_is_a_programming_error_rather_than_a_default()
    {
        using HmacClientNetworkKeys keys = HmacClientNetworkKeys.From(Secret);

        Assert.Throws<ArgumentNullException>(() => keys.Derive(null!));
    }

    [Fact]
    public void A_blank_network_key_cannot_be_constructed()
    {
        // The type that carries the pseudonym refuses an empty one, so "no
        // identity" can never masquerade as an identity further down.
        Assert.Throws<ArgumentException>(() => ClientNetworkKey.From(string.Empty));
        Assert.Throws<ArgumentException>(() => ClientNetworkKey.From("  "));

        Assert.True(default(ClientNetworkKey).IsEmpty);
    }
}
