using System.Net;
using OmniAI.Api.IntegrationTests.Authorization;

namespace OmniAI.Api.IntegrationTests.WebChat;

/// <summary>
/// Which address the platform believes, and what that belief is worth.
/// </summary>
/// <remarks>
/// <strong>The rate-limit identity is only as good as the address it is derived
/// from, and that address is the one thing an attacker can try to choose.</strong>
/// Anything that can open a connection to this process can send
/// <c>X-Forwarded-For: 1.2.3.4</c>. If that were believed, every per-network limit
/// in the platform would be bypassable in one line: a fresh forged value for every
/// request is a fresh bucket every time.
/// <para>
/// <strong>The identity is invisible in a response, so it is measured through the
/// limits.</strong> Nothing in this platform ever tells a caller which network it
/// thinks they are on — quite deliberately. So these tests ask the question the
/// only honest way available: two requests carrying different forged addresses
/// either share an allowance (the forgery was ignored) or do not (it was believed).
/// A per-network start limit of one makes the answer a status code.
/// </para>
/// <para>
/// <strong>Two hosts, differing in one setting.</strong> The same application, the
/// same simulated peer, the same headers, the same routes — only the configured
/// trusted-proxy list changes. Nothing in the implementation was relaxed or
/// parameterised to make that possible.
/// </para>
/// </remarks>
public sealed class ClientNetworkIdentityApiTests(
    TrustedProxyIdentityFixture trusted,
    UntrustedProxyIdentityFixture untrusted)
    : IClassFixture<TrustedProxyIdentityFixture>,
      IClassFixture<UntrustedProxyIdentityFixture>,
      IAsyncLifetime
{
    private const string ChatRoot = "/api/public/webchat";
    private const string ForwardedFor = "X-Forwarded-For";

    public async Task InitializeAsync()
    {
        await trusted.Redis.ClearAsync();
        await untrusted.Redis.ClearAsync();
    }

    public async Task DisposeAsync()
    {
        await trusted.Redis.ClearAsync();
        await untrusted.Redis.ClearAsync();
    }

    // ---- A trusted proxy is believed -------------------------------------

    [Fact]
    public async Task Behind_a_trusted_proxy_two_clients_get_two_allowances()
    {
        // The reason this header is processed at all. Behind a TLS-terminating
        // proxy every request appears to come from the proxy itself, which would
        // put the entire internet into one bucket -- one visitor's flood would
        // refuse everybody else's first message.
        string key = await PublishAsync(trusted);

        Assert.Equal(HttpStatusCode.OK, await StartAsync(trusted, key, "203.0.113.10"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await StartAsync(trusted, key, "203.0.113.10"));

        // A different client behind the same proxy, unaffected.
        Assert.Equal(HttpStatusCode.OK, await StartAsync(trusted, key, "203.0.113.11"));
    }

    [Fact]
    public async Task Behind_a_trusted_proxy_one_client_shares_one_allowance()
    {
        // The other half: the forwarded address really is what is counted, not
        // merely something that varies the bucket.
        string key = await PublishAsync(trusted);

        Assert.Equal(HttpStatusCode.OK, await StartAsync(trusted, key, "203.0.113.20"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await StartAsync(trusted, key, "203.0.113.20"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await StartAsync(trusted, key, "203.0.113.20"));
    }

    [Fact]
    public async Task Only_the_rightmost_forwarded_entry_is_believed()
    {
        // One proxy hop, which is this platform's topology. The middleware takes
        // the last entry -- the address the trusted proxy itself observed -- and
        // ignores everything to its left, which is whatever the caller chose to
        // prepend. Believing the leftmost is the classic spoofable configuration.
        string key = await PublishAsync(trusted);

        // Two requests whose prepended values differ and whose rightmost entry is
        // the same. If the leftmost were believed these would be two buckets.
        Assert.Equal(
            HttpStatusCode.OK,
            await StartAsync(trusted, key, "9.9.9.9, 203.0.113.30"));

        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            await StartAsync(trusted, key, "8.8.8.8, 203.0.113.30"));
    }

    // ---- An untrusted peer is not -----------------------------------------

    [Fact]
    public async Task From_an_untrusted_peer_a_forged_address_buys_nothing()
    {
        // The security property. Every request here carries a different forged
        // address and they all land in one bucket, because the connection did not
        // come from a proxy this deployment trusts. If the header were believed,
        // the second call would answer OK and every limit in the platform would be
        // one header away from useless.
        string key = await PublishAsync(untrusted);

        Assert.Equal(HttpStatusCode.OK, await StartAsync(untrusted, key, "203.0.113.40"));

        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            await StartAsync(untrusted, key, "203.0.113.41"));

        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            await StartAsync(untrusted, key, "198.18.0.1"));

        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            await StartAsync(untrusted, key, "10.0.0.99"));
    }

    [Fact]
    public async Task A_forged_address_cannot_impersonate_a_different_visitor_either()
    {
        // The mirror image: an attacker who forges somebody else's address does
        // not get to spend that person's allowance, because the forgery is ignored
        // in both directions.
        string key = await PublishAsync(untrusted);

        // The attacker exhausts their own bucket while claiming to be somebody else.
        Assert.Equal(HttpStatusCode.OK, await StartAsync(untrusted, key, "203.0.113.50"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await StartAsync(untrusted, key, "203.0.113.50"));

        // The peer really is the same for both hosts' clients, so the second
        // request being refused is the point -- there is exactly one bucket here.
        Assert.Equal(HttpStatusCode.TooManyRequests, await StartAsync(untrusted, key, null));
    }

    // ---- Direct connections ------------------------------------------------

    [Fact]
    public async Task A_direct_connection_is_identified_by_its_own_address()
    {
        // No forwarded header at all: the connection's own address is used, and it
        // is still bounded. A deployment reached directly must not be unlimited.
        string key = await PublishAsync(untrusted);

        Assert.Equal(HttpStatusCode.OK, await StartAsync(untrusted, key, null));
        Assert.Equal(HttpStatusCode.TooManyRequests, await StartAsync(untrusted, key, null));
    }

    [Fact]
    public async Task Two_direct_connections_from_different_addresses_are_two_visitors()
    {
        string key = await PublishAsync(untrusted);

        HttpClient first = await untrusted.CreateVisitorAsync(
            IPAddress.Parse("198.51.100.71"));

        HttpClient second = await untrusted.CreateVisitorAsync(
            IPAddress.Parse("198.51.100.72"));

        Assert.Equal(HttpStatusCode.OK, await PostStartAsync(first, key, null));
        Assert.Equal(HttpStatusCode.TooManyRequests, await PostStartAsync(first, key, null));

        Assert.Equal(HttpStatusCode.OK, await PostStartAsync(second, key, null));
    }

    [Fact]
    public async Task Behind_a_trusted_proxy_a_missing_forwarded_address_still_identifies_somebody()
    {
        // The ordinary misconfiguration -- a proxy that forwards the scheme but
        // forgets the address. The peer is used instead, so everybody behind that
        // proxy shares one allowance: more restrictive than intended rather than
        // less. A degradation in fairness, never in protection.
        string key = await PublishAsync(trusted);

        Assert.Equal(HttpStatusCode.OK, await StartAsync(trusted, key, null));
        Assert.Equal(HttpStatusCode.TooManyRequests, await StartAsync(trusted, key, null));
    }

    // ---- Malformed input ---------------------------------------------------

    [Theory]
    [InlineData("not-an-address")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(",,,")]
    [InlineData("203.0.113.999")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("203.0.113.10; DROP TABLE conversations")]
    public async Task A_malformed_forwarded_address_fails_safe_rather_than_open(string forged)
    {
        // Two properties at once. It must not fault -- a header nobody validates is
        // a header an attacker will send rubbish in -- and it must not become a
        // fresh unmetered bucket, which is what "fails open" would mean here.
        string key = await PublishAsync(trusted);

        HttpStatusCode first = await StartAsync(trusted, key, forged);
        HttpStatusCode second = await StartAsync(trusted, key, forged);

        Assert.Equal(HttpStatusCode.OK, first);
        Assert.Equal(HttpStatusCode.TooManyRequests, second);
    }

    [Fact]
    public async Task Rubbish_in_the_forwarded_header_shares_a_bucket_with_the_peer()
    {
        // Which is the safe direction: an unparseable value leaves the peer address
        // alone, so the caller is counted as themselves rather than as somebody
        // new. A fresh bucket per unparseable value would have been a bypass.
        string key = await PublishAsync(trusted);

        Assert.Equal(HttpStatusCode.OK, await StartAsync(trusted, key, "garbage-one"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await StartAsync(trusted, key, "garbage-two"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await StartAsync(trusted, key, null));
    }

    // ---- Nothing about the address is published ---------------------------

    [Fact]
    public async Task No_response_ever_echoes_the_address_the_platform_resolved()
    {
        // Neither the peer nor the forwarded value, in a body or a header. A
        // platform that told a caller which network it had been identified as would
        // be publishing the thing the pseudonymisation exists to keep.
        string key = await PublishAsync(trusted);

        HttpClient visitor = await trusted.CreateVisitorAsync();

        visitor.DefaultRequestHeaders.TryAddWithoutValidation(ForwardedFor, "203.0.113.60");

        foreach (string route in (string[])
        [
            $"{ChatRoot}/{key}",
            $"{ChatRoot}/conversations/{Guid.CreateVersion7()}/messages",
        ])
        {
            using HttpResponseMessage response =
                await visitor.GetAsync(WebChatApiFixture.Relative(route));

            string body = await response.Content.ReadAsStringAsync();
            string headers = string.Join(
                " ",
                response.Headers.Select(header =>
                    $"{header.Key}:{string.Join(",", header.Value)}"));

            foreach (string leak in (string[])["203.0.113.60", "198.51.100", "X-Original-For"])
            {
                Assert.DoesNotContain(leak, body, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(leak, headers, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public async Task No_redis_key_carries_the_address_the_platform_resolved()
    {
        // The privacy claim, asserted where the address would actually have ended
        // up living: the counter names. Only a keyed digest of it is ever written.
        string key = await PublishAsync(trusted);

        await StartAsync(trusted, key, "203.0.113.70");

        IReadOnlyList<string> keys = await trusted.Redis.KeysAsync();

        Assert.NotEmpty(keys);

        foreach (string redisKey in keys)
        {
            foreach (string leak in (string[])["203.0.113", "198.51.100", "127.0.0.1"])
            {
                Assert.DoesNotContain(leak, redisKey, StringComparison.Ordinal);
            }
        }
    }

    // ---- Arrangement -----------------------------------------------------

    private static async Task<string> PublishAsync(RoleApiFixture api)
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Identity");

        return key;
    }

    /// <summary>
    /// Attempts one conversation start, optionally claiming a forwarded address.
    /// </summary>
    /// <remarks>
    /// A fresh visitor each time, so the only thing carried between calls is the
    /// server-side counter -- which is the thing under test.
    /// </remarks>
    private static async Task<HttpStatusCode> StartAsync(
        RoleApiFixture api,
        string key,
        string? forwardedFor) =>
        await PostStartAsync(await api.CreateVisitorAsync(), key, forwardedFor);

    private static async Task<HttpStatusCode> PostStartAsync(
        HttpClient visitor,
        string key,
        string? forwardedFor)
    {
        using HttpRequestMessage request = new(
            HttpMethod.Post,
            WebChatApiFixture.Relative($"{ChatRoot}/{key}/conversations"));

        if (forwardedFor is not null)
        {
            request.Headers.TryAddWithoutValidation(ForwardedFor, forwardedFor);
        }

        using HttpResponseMessage response = await visitor.SendAsync(request);

        return response.StatusCode;
    }
}

/// <summary>
/// A host that trusts the proxy its requests appear to come from.
/// </summary>
/// <remarks>
/// The simulated peer for this suite is the harness default, and this fixture
/// names exactly that address as a trusted proxy — so a forwarded address arriving
/// here is one the deployment has said it will believe.
/// </remarks>
public sealed class TrustedProxyIdentityFixture : RoleApiFixture
{
    protected override IReadOnlyDictionary<string, string> HostSettings =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ForwardedHeaders:KnownProxies:0"] = "198.51.100.10",

            // One conversation start per network per endpoint, which is what makes
            // the resolved identity observable at all.
            ["PublicChatProtection:ConversationStartsPerClient:Limit"] = "1",
            ["PublicChatProtection:ConversationStartsPerClient:WindowSeconds"] = "120",
            ["PublicChatProtection:ConversationStartsPerEndpoint:Limit"] = "500",
            ["PublicChatProtection:RequestsPerClient:Limit"] = "500",
        };
}

/// <summary>
/// A host that trusts a proxy its requests do not come from.
/// </summary>
/// <remarks>
/// <strong>Identical to the trusted fixture but for one address.</strong> The
/// deployment names <c>10.0.0.1</c> as its proxy; the connections arrive from the
/// harness default instead. So every forwarded header reaching this host is, by
/// definition, one the deployment has not agreed to believe — which is the state
/// any internet caller reaching Kestrel without passing through the proxy is in.
/// </remarks>
public sealed class UntrustedProxyIdentityFixture : RoleApiFixture
{
    protected override IReadOnlyDictionary<string, string> HostSettings =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ForwardedHeaders:KnownProxies:0"] = "10.0.0.1",
            ["PublicChatProtection:ConversationStartsPerClient:Limit"] = "1",
            ["PublicChatProtection:ConversationStartsPerClient:WindowSeconds"] = "120",
            ["PublicChatProtection:ConversationStartsPerEndpoint:Limit"] = "500",
            ["PublicChatProtection:RequestsPerClient:Limit"] = "500",
        };
}
