using System.Net;
using System.Net.Http.Json;
using OmniAI.Api.IntegrationTests.Tenants;

namespace OmniAI.Api.IntegrationTests.Auth;

/// <summary>
/// The attributes of the cookies this API sets.
/// </summary>
/// <remarks>
/// <strong>Asserted on the attributes, never on the value.</strong> The session
/// cookie's contents are an encrypted ticket that changes on every issue, so a
/// test that looked at it would be brittle and would also be printing a session
/// into a log. What matters is what the browser is told to do with it.
/// <para>
/// <strong>These run as Development</strong>, because that is how
/// <c>WebApplicationFactory</c> hosts an application and it is the configuration
/// a developer actually runs. Development is the one environment allowed to serve
/// cookies over plain HTTP, so the <c>Secure</c> attribute is asserted separately
/// against a host started as Production behind a trusted proxy -- see
/// <see cref="ProductionProxyCookieTests"/>. Everything else about the cookies is
/// environment-independent and is asserted here.
/// </para>
/// <para>
/// They also stand as the proof that the remediation did not break local
/// development. Forwarded headers and the secure-transport fail-safe are both
/// scoped to non-Development environments; if either leaked into Development,
/// every request in this file would be refused.
/// </para>
/// </remarks>
public sealed class SessionCookieTests(TenantApiFixture api) : IClassFixture<TenantApiFixture>
{
    [Fact]
    public async Task Local_development_over_plain_http_still_establishes_antiforgery_state()
    {
        // The explicit statement of the requirement, rather than leaving it implied
        // by the rest of the suite. No proxy, no forwarded header, plain HTTP to
        // localhost -- and the antiforgery bootstrap must still work, because this
        // is what every developer's browser does.
        using HttpClient client = api.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/api/auth/csrf");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        string[] cookies = [.. response.Headers.GetValues("Set-Cookie")];

        Assert.Contains(cookies, cookie => cookie.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal));
        Assert.Contains(
            cookies,
            cookie => cookie.StartsWith(".AspNetCore.Antiforgery.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Local_development_can_still_register_sign_in_and_write()
    {
        // The whole local loop, over plain HTTP: bootstrap, register, then a
        // protected unsafe request with the token. If the fail-safe were not
        // scoped to non-Development, none of this would answer.
        using HttpClient client = api.CreateClient();

        await ApiAuthentication.RegisterAsync(client);

        using HttpResponseMessage me = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);

        using HttpResponseMessage created = await client.PostAsJsonAsync(
            "/api/tenants",
            new { name = "Local Trading", slug = $"l{Guid.NewGuid():N}"[..20] });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
    }

    [Fact]
    public async Task Csrf_enforcement_is_not_relaxed_in_development_either()
    {
        // Development relaxes cookie *transport*, and nothing else. An unsafe
        // request without a token is still refused.
        using HttpClient client = api.CreateClient();

        await ApiAuthentication.RegisterAsync(client);

        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");

        using HttpResponseMessage refused = await client.PostAsJsonAsync(
            "/api/tenants",
            new { name = "Forged Trading", slug = $"f{Guid.NewGuid():N}"[..20] });

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    [Fact]
    public async Task The_session_cookie_is_not_readable_by_script()
    {
        // The single most important attribute on this cookie. Script that can read
        // it can impersonate the person holding it, which would turn any XSS
        // defect anywhere on the page into account takeover.
        string cookie = await SessionCookieAsync();

        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_session_cookie_is_scoped_to_the_whole_application()
    {
        string cookie = await SessionCookieAsync();

        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_session_cookie_is_same_site_lax()
    {
        // Lax rather than Strict: the admin is same-origin, so every request that
        // needs this cookie is same-site anyway, and Strict would show a
        // signed-out application to somebody following a link in from elsewhere.
        // Lax still withholds it from cross-site POSTs, which is the case that
        // matters -- and the antiforgery token covers those regardless.
        string cookie = await SessionCookieAsync();

        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_session_cookie_is_allowed_over_plain_http_only_in_development()
    {
        // Development is hosting this, and the request is plain HTTP, so
        // SameAsRequest omits Secure -- which is what makes local development work
        // in a browser at all. This asserts the relaxation is real and is scoped
        // to the request's own scheme rather than being unconditional; the
        // production side of the same policy is asserted in ProductionProxyCookieTests.
        string cookie = await SessionCookieAsync();

        Assert.DoesNotContain("secure", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_session_cookie_does_not_announce_the_framework()
    {
        string cookie = await SessionCookieAsync();

        Assert.StartsWith("omniai.session=", cookie, StringComparison.Ordinal);
        Assert.DoesNotContain("AspNetCore", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_session_cookie_expires_with_the_browser()
    {
        // No "remember me" in this phase, so no Expires and no Max-Age: closing
        // the browser ends the session. Persisting a sign-in across restarts is a
        // decision about how long a stolen device stays useful, and it is not one
        // to make in passing.
        string cookie = await SessionCookieAsync();

        Assert.DoesNotContain("expires=", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("max-age=", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_antiforgery_request_cookie_is_deliberately_readable()
    {
        // The one cookie that must NOT be HttpOnly. The double-submit pattern
        // requires the client to copy this value into a header, which it cannot do
        // if script cannot read it. It is safe because it authenticates nobody on
        // its own: it is only accepted alongside the secret half, which stays
        // HttpOnly.
        using HttpClient client = api.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/api/auth/csrf");

        string cookie = CookieNamed(response, "XSRF-TOKEN");

        Assert.DoesNotContain("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_secret_half_of_the_antiforgery_pair_stays_hidden_from_script()
    {
        // Two cookies are involved and only one of them is readable. Confusing
        // them would hand script the value that actually proves the request came
        // from this origin.
        using HttpClient client = api.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/api/auth/csrf");

        string[] hidden =
        [
            .. response.Headers.GetValues("Set-Cookie")
                .Where(cookie => !cookie.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal))
        ];

        Assert.NotEmpty(hidden);
        Assert.All(hidden, cookie =>
            Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task No_cookie_carries_the_password_or_the_address()
    {
        // A cookie is stored on disk and travels with every request. Neither of
        // these belongs in one.
        using HttpClient client = api.CreateClient();
        await ApiAuthentication.BootstrapAntiforgeryAsync(client);

        string email = ApiAuthentication.UniqueEmail();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new { email, password = ApiAuthentication.Password });

        response.EnsureSuccessStatusCode();

        foreach (string cookie in response.Headers.GetValues("Set-Cookie"))
        {
            Assert.DoesNotContain(ApiAuthentication.Password, cookie, StringComparison.Ordinal);
            Assert.DoesNotContain(email, cookie, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>The <c>Set-Cookie</c> line for the session, from a fresh sign-in.</summary>
    private async Task<string> SessionCookieAsync()
    {
        using HttpClient client = api.CreateClient();
        await ApiAuthentication.BootstrapAntiforgeryAsync(client);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new
            {
                email = ApiAuthentication.UniqueEmail(),
                password = ApiAuthentication.Password,
            });

        response.EnsureSuccessStatusCode();

        return CookieNamed(response, "omniai.session");
    }

    /// <summary>One named <c>Set-Cookie</c> line, or a failure that names what was there.</summary>
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
