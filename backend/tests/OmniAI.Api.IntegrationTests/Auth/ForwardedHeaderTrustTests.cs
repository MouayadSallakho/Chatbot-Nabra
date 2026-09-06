using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace OmniAI.Api.IntegrationTests.Auth;

/// <summary>
/// The trust boundary: who is allowed to tell this server what scheme the browser
/// used.
/// </summary>
/// <remarks>
/// <strong>Forwarded headers are attacker-controlled input.</strong> Anything that
/// can open a TCP connection to Kestrel can send
/// <c>X-Forwarded-Proto: https</c>. The only thing separating a real proxy from a
/// liar is where the connection came from, so trusting the header unconditionally
/// would hand any caller the ability to declare their own connection secure.
/// <para>
/// <strong>These two suites differ in exactly one thing.</strong> Same
/// application, same Production environment, same configured trust list, same
/// header, same requests — only the simulated peer address changes. Nothing in the
/// implementation was relaxed or parameterised to make that possible; the fixture
/// sets <c>Connection.RemoteIpAddress</c> through an <c>IStartupFilter</c>, which
/// is the connection-level fact the middleware actually consults.
/// </para>
/// </remarks>
public sealed class ForwardedHeaderTrustTests(UntrustedPeerApiFixture api)
    : IClassFixture<UntrustedPeerApiFixture>
{
    private const string ForwardedProto = "X-Forwarded-Proto";

    [Fact]
    public async Task A_spoofed_forwarded_scheme_from_an_untrusted_peer_is_ignored()
    {
        // The header says https. The connection did not come from a trusted proxy,
        // so it is not believed, Request.IsHttps stays false, and the fail-safe
        // refuses the request. A 204 here would mean any caller could declare their
        // own connection secure.
        using HttpClient client = api.CreateClient();

        using HttpResponseMessage response = await SpoofedAsync(client, "/api/auth/csrf");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task The_refusal_issues_no_cookie_at_all()
    {
        // The property that matters more than the status code. The alternative
        // design -- relaxing Secure so the request could succeed -- would have set
        // three cookies here, all of them sendable over plain HTTP.
        using HttpClient client = api.CreateClient();

        using HttpResponseMessage response = await SpoofedAsync(client, "/api/auth/csrf");

        Assert.False(
            response.Headers.Contains("Set-Cookie"),
            "A refused request set cookies: "
                + string.Join(", ", response.Headers.TryGetValues("Set-Cookie", out var cookies)
                    ? cookies
                    : []));
    }

    [Fact]
    public async Task Registration_is_refused_rather_than_served_insecurely()
    {
        // Not a 400 from antiforgery and not a 201: the request never reaches the
        // endpoint, so no account is created and no session cookie is minted.
        using HttpClient client = api.CreateClient();

        using HttpResponseMessage response = await SpoofedAsync(
            client,
            "/api/auth/register",
            new
            {
                email = ApiAuthentication.UniqueEmail(),
                password = ApiAuthentication.Password,
            });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task A_request_with_no_forwarded_header_at_all_is_refused_the_same_way()
    {
        // The ordinary misconfiguration -- a proxy that terminates TLS but forgets
        // to forward the scheme -- lands in the same place as a spoof. There is no
        // path through which a non-HTTPS production request gets served.
        using HttpClient client = api.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/api/auth/csrf");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task The_refusal_says_nothing_about_the_topology()
    {
        // A caller cannot fix this, and an attacker should not be handed a
        // description of the deployment. No mention of proxies, headers, trusted
        // addresses or which check failed.
        using HttpClient client = api.CreateClient();

        using HttpResponseMessage response = await SpoofedAsync(client, "/api/auth/csrf");

        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        string title = body.RootElement.GetProperty("title").GetString() ?? string.Empty;
        string detail = body.RootElement.GetProperty("detail").GetString() ?? string.Empty;

        Assert.Equal("Insecure transport", title);

        foreach (string leak in
            (string[])["proxy", "forwarded", "x-forwarded", "127.0.0.1", "203.0.113", "known"])
        {
            Assert.DoesNotContain(leak, detail, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task The_operator_is_told_what_to_fix_even_though_the_caller_is_not()
    {
        // The other half of the same decision: the response is uninformative, the
        // log is not. An operator needs the peer address and the setting to change.
        using HttpClient client = api.CreateClient();

        using (HttpResponseMessage response = await SpoofedAsync(client, "/api/auth/csrf"))
        {
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }

        string logged = api.ServerErrors;

        Assert.Contains("X-Forwarded-Proto", logged, StringComparison.Ordinal);
        Assert.Contains("ForwardedHeaders", logged, StringComparison.Ordinal);
        Assert.Contains("203.0.113.7", logged, StringComparison.Ordinal);
        Assert.Contains("No cookie was issued", logged, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_liveness_probe_still_answers_over_the_internal_hop()
    {
        // The single exemption, and it has to exist: an orchestrator calls /health
        // over the plain-HTTP hop, and a probe that could not answer would report a
        // healthy process as dead. It sets no cookie and reads no session.
        using HttpClient client = api.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    /// <summary>A request claiming HTTPS from a peer the deployment does not trust.</summary>
    private static async Task<HttpResponseMessage> SpoofedAsync(
        HttpClient client,
        string route,
        object? body = null)
    {
        using HttpRequestMessage request = new(
            body is null ? HttpMethod.Get : HttpMethod.Post,
            route);

        request.Headers.TryAddWithoutValidation(ForwardedProto, "https");

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await client.SendAsync(request);
    }
}
