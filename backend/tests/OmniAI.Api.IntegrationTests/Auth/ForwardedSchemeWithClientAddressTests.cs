using System.Net;

namespace OmniAI.Api.IntegrationTests.Auth;

/// <summary>
/// The scheme still survives the proxy hop now that a second header is processed.
/// </summary>
/// <remarks>
/// <strong>This suite exists because enabling <c>X-Forwarded-For</c> could have
/// silently broken <c>X-Forwarded-Proto</c>, and the failure would have looked
/// like something else entirely.</strong> The forwarded-headers middleware
/// compares the entry counts of the headers it processes, and with
/// <c>RequireHeaderSymmetry</c> on it discards <em>all</em> of them when they
/// disagree — so a proxy that forwards the scheme but not the address, or a probe
/// that sends only the scheme, would have lost the scheme too and been refused as
/// insecure transport.
/// <para>
/// The framework default for that option is off and Phase 16 deliberately leaves
/// it there. These tests pin the consequence rather than the setting: every
/// combination of the two headers a real deployment can produce must still yield a
/// request the platform treats as HTTPS.
/// </para>
/// <para>
/// All of it runs over a plain-HTTP connection to Kestrel from an address the
/// deployment trusts, which is exactly what a TLS-terminating proxy produces.
/// </para>
/// </remarks>
public sealed class ForwardedSchemeWithClientAddressTests(TrustedProxyApiFixture api)
    : IClassFixture<TrustedProxyApiFixture>
{
    private const string ForwardedProto = "X-Forwarded-Proto";
    private const string ForwardedFor = "X-Forwarded-For";

    [Fact]
    public async Task The_scheme_is_restored_when_both_headers_arrive()
    {
        // The ordinary production request from Phase 16 onwards.
        using HttpClient client = api.CreateClient();

        using HttpResponseMessage response = await ProxiedAsync(
            client,
            proto: "https",
            forwardedFor: "203.0.113.10");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(response.Headers.Contains("Set-Cookie"), api.ServerErrors);
    }

    [Fact]
    public async Task The_scheme_is_restored_when_the_client_address_is_absent()
    {
        // The regression this suite is really for. A proxy that forwards the
        // scheme but not the address is a common configuration, and it must not
        // lose the scheme -- which is what header symmetry would have caused.
        using HttpClient client = api.CreateClient();

        using HttpResponseMessage response = await ProxiedAsync(
            client,
            proto: "https",
            forwardedFor: null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(response.Headers.Contains("Set-Cookie"), api.ServerErrors);
    }

    [Fact]
    public async Task The_scheme_is_restored_when_the_client_address_is_a_chain()
    {
        // Several hops upstream of the trusted proxy. The counts differ between the
        // two headers, which is precisely the case that must not discard either.
        using HttpClient client = api.CreateClient();

        using HttpResponseMessage response = await ProxiedAsync(
            client,
            proto: "https",
            forwardedFor: "198.18.0.1, 198.18.0.2, 203.0.113.10");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(response.Headers.Contains("Set-Cookie"), api.ServerErrors);
    }

    [Fact]
    public async Task A_client_address_alone_does_not_make_a_request_secure()
    {
        // The other direction. Forwarding an address says nothing about the
        // scheme, so the fail-safe must still refuse -- and must not be talked out
        // of it by a header that happens to be present.
        using HttpClient client = api.CreateClient();

        using HttpResponseMessage response = await ProxiedAsync(
            client,
            proto: null,
            forwardedFor: "203.0.113.10");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task A_malformed_client_address_does_not_cost_the_request_its_scheme()
    {
        // Rubbish in one header must not take the other down with it. The scheme is
        // a security decision and cannot be made to depend on somebody sending a
        // well-formed address.
        using HttpClient client = api.CreateClient();

        using HttpResponseMessage response = await ProxiedAsync(
            client,
            proto: "https",
            forwardedFor: "not-an-address");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(response.Headers.Contains("Set-Cookie"), api.ServerErrors);
    }

    [Fact]
    public async Task A_forwarded_host_is_still_not_processed()
    {
        // Two headers is the whole opt-in. Nothing reconstructs absolute URLs, so
        // accepting a third would widen the trusted-input surface for nothing --
        // and a request carrying one must be served exactly as if it had not.
        using HttpClient client = api.CreateClient();

        using HttpRequestMessage request = new(HttpMethod.Get, "/api/auth/csrf");

        request.Headers.TryAddWithoutValidation(ForwardedProto, "https");
        request.Headers.TryAddWithoutValidation(ForwardedFor, "203.0.113.10");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Host", "evil.example");

        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // The forged host reaches nothing: no cookie is scoped to it, and no
        // response header quotes it.
        string headers = string.Join(
            " ",
            response.Headers.Select(header => $"{header.Key}:{string.Join(",", header.Value)}"));

        Assert.DoesNotContain("evil.example", headers, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>One proxied request, with whichever headers the case needs.</summary>
    private static async Task<HttpResponseMessage> ProxiedAsync(
        HttpClient client,
        string? proto,
        string? forwardedFor)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, "/api/auth/csrf");

        if (proto is not null)
        {
            request.Headers.TryAddWithoutValidation(ForwardedProto, proto);
        }

        if (forwardedFor is not null)
        {
            request.Headers.TryAddWithoutValidation(ForwardedFor, forwardedFor);
        }

        return await client.SendAsync(request);
    }
}
