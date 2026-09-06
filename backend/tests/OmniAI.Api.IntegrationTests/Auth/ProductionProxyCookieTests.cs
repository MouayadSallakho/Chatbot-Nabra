using System.Net;
using System.Net.Http.Json;

namespace OmniAI.Api.IntegrationTests.Auth;

/// <summary>
/// Cookie security as a real deployment gets it: Production, behind a trusted
/// TLS-terminating proxy.
/// </summary>
/// <remarks>
/// <strong>These are the tests the remediation exists for.</strong> The first
/// attempt at this phase made the antiforgery cookies <c>SameAsRequest</c> so that
/// ASP.NET Core would stop throwing when <c>Request.IsHttps</c> was false behind a
/// proxy. That removed a guarantee to silence a symptom: the browser would then
/// have been permitted to send those cookies over plain HTTP.
/// <para>
/// The fix is to make the browser's scheme survive the proxy hop, so
/// <c>Request.IsHttps</c> is true and <c>Secure</c> both holds and works. Every
/// assertion below runs over a plain-HTTP connection to Kestrel — exactly what a
/// TLS-terminating proxy produces — with the scheme carried in
/// <c>X-Forwarded-Proto</c> from an address the deployment trusts.
/// </para>
/// </remarks>
public sealed class ProductionProxyCookieTests(TrustedProxyApiFixture api)
    : IClassFixture<TrustedProxyApiFixture>
{
    /// <summary>What the proxy sends to say the browser used TLS.</summary>
    private const string ForwardedProto = "X-Forwarded-Proto";

    [Fact]
    public async Task The_antiforgery_bootstrap_succeeds_over_a_proxied_connection()
    {
        // The request that used to fail. ASP.NET Core's antiforgery service throws
        // when its cookie policy is Always and the request is not HTTPS; with the
        // forwarded scheme restored it is HTTPS, and nothing throws.
        using HttpClient client = api.CreateClient();

        using HttpResponseMessage response = await ProxiedAsync(client, HttpMethod.Get, "/api/auth/csrf", []);

        Assert.True(
            response.IsSuccessStatusCode,
            $"The antiforgery bootstrap answered {(int)response.StatusCode}. {api.ServerErrors}");
    }

    [Fact]
    public async Task The_session_cookie_demands_a_secure_connection()
    {
        string cookie = await CookieAsync("omniai.session");

        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_session_cookie_keeps_every_other_protection_too()
    {
        // Secure is not a substitute for the rest: a cookie can be sent only over
        // TLS and still be readable by script, or still ride along with a
        // cross-site POST.
        string cookie = await CookieAsync("omniai.session");

        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("omniai.session=", cookie, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_antiforgery_secret_cookie_is_secure_and_hidden_from_script()
    {
        string cookie = await AntiforgerySecretCookieAsync();

        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_xsrf_request_cookie_is_secure_but_deliberately_readable()
    {
        // The two properties are unrelated, and conflating them was the mistake the
        // first attempt made. Script must be able to read this half so the client
        // can echo it into a header; that is no reason to let it travel in the
        // clear.
        using HttpClient client = api.CreateClient();

        using HttpResponseMessage response =
            await ProxiedAsync(client, HttpMethod.Get, "/api/auth/csrf", []);

        string cookie = CookieNamed(response, "XSRF-TOKEN");

        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Every_cookie_this_deployment_sets_requires_a_secure_connection()
    {
        // Stated over the whole set rather than one at a time, so a cookie added
        // later without Secure fails here even if nobody wrote it a test.
        using HttpClient client = api.CreateClient();

        using HttpResponseMessage csrf = await ProxiedAsync(client, HttpMethod.Get, "/api/auth/csrf", []);

        Assert.All(
            csrf.Headers.GetValues("Set-Cookie"),
            cookie => Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase));

        using HttpResponseMessage registered = await RegisterAsync(client);

        Assert.All(
            registered.Headers.GetValues("Set-Cookie"),
            cookie => Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task The_openapi_document_is_not_published_outside_development()
    {
        using HttpClient client = api.CreateClient();

        using HttpResponseMessage response =
            await ProxiedAsync(client, HttpMethod.Get, "/openapi/v1.json", []);

        Assert.False(
            response.IsSuccessStatusCode,
            $"The OpenAPI document answered {(int)response.StatusCode} in Production.");
    }

    /// <summary>The named <c>Set-Cookie</c> line from a fresh registration.</summary>
    private async Task<string> CookieAsync(string name)
    {
        using HttpClient client = api.CreateClient();

        using HttpResponseMessage response = await RegisterAsync(client);

        return CookieNamed(response, name);
    }

    /// <summary>The framework's antiforgery cookie, whose name carries a hash.</summary>
    private async Task<string> AntiforgerySecretCookieAsync()
    {
        using HttpClient client = api.CreateClient();

        using HttpResponseMessage response =
            await ProxiedAsync(client, HttpMethod.Get, "/api/auth/csrf", []);

        string[] cookies = [.. response.Headers.GetValues("Set-Cookie")];

        string? secret = Array.Find(
            cookies,
            cookie => cookie.StartsWith(".AspNetCore.Antiforgery.", StringComparison.Ordinal));

        Assert.True(secret is not null, $"No antiforgery secret cookie was set. {api.ServerErrors}");

        return secret!;
    }

    /// <summary>Registers an account through the proxied connection.</summary>
    private async Task<HttpResponseMessage> RegisterAsync(HttpClient client)
    {
        Dictionary<string, string> jar = [];
        string token;

        using (HttpResponseMessage csrf =
            await ProxiedAsync(client, HttpMethod.Get, "/api/auth/csrf", jar))
        {
            Assert.True(
                csrf.IsSuccessStatusCode,
                $"The antiforgery bootstrap answered {(int)csrf.StatusCode}. {api.ServerErrors}");

            token = jar["XSRF-TOKEN"];
        }

        HttpResponseMessage response = await ProxiedAsync(
            client,
            HttpMethod.Post,
            "/api/auth/register",
            jar,
            token,
            new
            {
                email = ApiAuthentication.UniqueEmail(),
                password = ApiAuthentication.Password,
            });

        Assert.True(
            response.IsSuccessStatusCode,
            $"Registration answered {(int)response.StatusCode}. {api.ServerErrors}");

        return response;
    }

    /// <summary>
    /// Sends a request the way a TLS-terminating proxy would.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Plain HTTP to Kestrel, with the browser's scheme in <c>X-Forwarded-Proto</c>.
    /// The connection's apparent peer address is set by the fixture, which is what
    /// decides whether this header is believed at all.
    /// </para>
    /// <para>
    /// <strong>Cookies are carried by hand, and that is faithful rather than
    /// convenient.</strong> This client is the proxy, not the browser: the browser
    /// spoke HTTPS, so it happily returns <c>Secure</c> cookies, and the proxy
    /// forwards the <c>Cookie</c> header verbatim over its own plain-HTTP hop. An
    /// automatic cookie jar would model a browser talking HTTP and would drop every
    /// <c>Secure</c> cookie — testing the harness instead of the application.
    /// </para>
    /// </remarks>
    private static async Task<HttpResponseMessage> ProxiedAsync(
        HttpClient client,
        HttpMethod method,
        string route,
        Dictionary<string, string> jar,
        string? antiforgeryToken = null,
        object? body = null)
    {
        using HttpRequestMessage request = new(method, route);

        request.Headers.TryAddWithoutValidation(ForwardedProto, "https");

        if (jar.Count > 0)
        {
            request.Headers.TryAddWithoutValidation(
                "Cookie",
                string.Join("; ", jar.Select(entry => $"{entry.Key}={entry.Value}")));
        }

        if (antiforgeryToken is not null)
        {
            request.Headers.TryAddWithoutValidation("X-XSRF-TOKEN", antiforgeryToken);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        HttpResponseMessage response = await client.SendAsync(request);

        Remember(response, jar);

        return response;
    }

    /// <summary>Records the cookies a response set, as a browser would.</summary>
    private static void Remember(HttpResponseMessage response, Dictionary<string, string> jar)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? cookies))
        {
            return;
        }

        foreach (string cookie in cookies)
        {
            string pair = cookie.Split(';')[0];
            int separator = pair.IndexOf('=', StringComparison.Ordinal);

            if (separator > 0)
            {
                jar[pair[..separator]] = pair[(separator + 1)..];
            }
        }
    }

    /// <summary>One named <c>Set-Cookie</c> line, or a failure naming what was there.</summary>
    private static string CookieNamed(HttpResponseMessage response, string name)
    {
        string[] cookies = [.. response.Headers.GetValues("Set-Cookie")];

        string? found = Array.Find(
            cookies,
            cookie => cookie.StartsWith(name + "=", StringComparison.Ordinal));

        Assert.True(
            found is not null,
            $"No '{name}' cookie was set. Cookies present: "
            + string.Join(", ", cookies.Select(cookie => cookie.Split('=')[0])));

        return found!;
    }
}
