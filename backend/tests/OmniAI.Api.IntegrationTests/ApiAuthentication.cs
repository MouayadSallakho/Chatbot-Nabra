using System.Net.Http.Json;
using System.Text.Json;

namespace OmniAI.Api.IntegrationTests;

/// <summary>
/// Registers accounts and completes the antiforgery handshake, for the suites
/// that need a client the API will actually answer.
/// </summary>
/// <remarks>
/// <strong>Everything here goes through the real endpoints.</strong> No test
/// forges a cookie, installs a fake principal, replaces the authentication
/// scheme or substitutes <c>ICurrentUser</c>. If it did, every authorization test
/// in this project would be proving that a test double works. The account is
/// created by <c>POST /api/auth/register</c>, the session is the one Identity
/// issued, and the antiforgery token is the one the server generated.
/// <para>
/// Cookies persist because <c>WebApplicationFactory</c>'s default client keeps a
/// cookie container. The antiforgery <em>header</em> is not automatic, so it is
/// copied from the readable cookie exactly as a browser client would -- which
/// means these tests exercise the same double-submit the Angular application
/// does.
/// </para>
/// </remarks>
internal static class ApiAuthentication
{
    /// <summary>
    /// The password every test account uses.
    /// </summary>
    /// <remarks>
    /// <strong>Not a credential.</strong> It exists only inside a randomly named
    /// database that is created for one test class and dropped when it finishes,
    /// for accounts with randomly generated addresses that no deployment has ever
    /// seen. It is written out rather than randomised so a failing test can be
    /// reproduced by hand, and it is long enough to satisfy the real policy --
    /// which is itself worth asserting, since a fixture that quietly used a
    /// shorter one would mean the policy was not being applied.
    /// </remarks>
    public const string Password = "correct-horse-battery-staple";

    /// <summary>The cookie the server writes the antiforgery request token into.</summary>
    private const string XsrfCookie = "XSRF-TOKEN";

    /// <summary>The header the server expects it echoed in.</summary>
    private const string XsrfHeader = "X-XSRF-TOKEN";

    /// <summary>
    /// Registers a fresh account on this client and returns its identity.
    /// </summary>
    /// <remarks>
    /// A new address per call, so classes running in parallel cannot collide on
    /// the unique email index and no test depends on another having run first.
    /// </remarks>
    public static async Task<Guid> RegisterAsync(HttpClient client, string? email = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        await BootstrapAntiforgeryAsync(client);

        string address = email ?? UniqueEmail();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new { email = address, password = Password });

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Registering '{address}' failed with {(int)response.StatusCode}: "
                + await response.Content.ReadAsStringAsync());
        }

        // Registration signs the account in, which changes the identity the
        // antiforgery token is bound to -- so the token issued a moment ago is
        // already stale. The endpoint reissues it; this picks the new one up.
        CaptureAntiforgeryToken(client, response);

        return await ReadUserIdAsync(response);
    }

    /// <summary>Signs an existing account in on this client.</summary>
    public static async Task<HttpResponseMessage> SignInAsync(
        HttpClient client,
        string email,
        string password)
    {
        ArgumentNullException.ThrowIfNull(client);

        await BootstrapAntiforgeryAsync(client);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email, password });

        // Captured even on failure: a rejected sign-in still leaves the anonymous
        // token in place, and a test that follows one failure with another must
        // not start failing for want of a token instead.
        CaptureAntiforgeryToken(client, response);

        return response;
    }

    /// <summary>Obtains an antiforgery token and attaches it to this client.</summary>
    public static async Task BootstrapAntiforgeryAsync(HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        using HttpResponseMessage response = await client.GetAsync("/api/auth/csrf");

        response.EnsureSuccessStatusCode();

        CaptureAntiforgeryToken(client, response);
    }

    /// <summary>An address no other test will use.</summary>
    public static string UniqueEmail() => $"user-{Guid.NewGuid():N}@example.test";

    /// <summary>
    /// Copies the readable antiforgery cookie into the request header.
    /// </summary>
    /// <remarks>
    /// Exactly what a browser client does, and exactly what an attacker on another
    /// origin cannot: the cookie travels automatically, but reading it to build
    /// this header requires being same-origin.
    /// <para>
    /// A response that carries no such cookie leaves the current header alone
    /// rather than clearing it -- most responses do not reissue the token, and
    /// treating that as "the token is gone" would break the request after every
    /// ordinary call.
    /// </para>
    /// </remarks>
    private static void CaptureAntiforgeryToken(HttpClient client, HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? cookies))
        {
            return;
        }

        foreach (string cookie in cookies)
        {
            if (!cookie.StartsWith(XsrfCookie + "=", StringComparison.Ordinal))
            {
                continue;
            }

            string value = cookie[(XsrfCookie.Length + 1)..].Split(';')[0];

            if (value.Length == 0)
            {
                continue;
            }

            client.DefaultRequestHeaders.Remove(XsrfHeader);
            client.DefaultRequestHeaders.Add(XsrfHeader, value);

            return;
        }
    }

    /// <summary>Reads the account id out of an auth response body.</summary>
    private static async Task<Guid> ReadUserIdAsync(HttpResponseMessage response)
    {
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return body.RootElement.GetProperty("id").GetGuid();
    }
}
