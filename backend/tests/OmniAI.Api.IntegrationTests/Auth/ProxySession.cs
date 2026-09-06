using System.Net.Http.Json;

namespace OmniAI.Api.IntegrationTests.Auth;

/// <summary>
/// One browser's worth of state, driven through a reverse proxy.
/// </summary>
/// <remarks>
/// <strong>This plays the proxy, not the browser.</strong> Every request goes to
/// Kestrel over plain HTTP carrying <c>X-Forwarded-Proto: https</c>, which is
/// exactly what a TLS-terminating proxy produces. Cookies are held and replayed by
/// hand because a proxy forwards the <c>Cookie</c> header verbatim: the browser
/// spoke HTTPS and therefore returns <c>Secure</c> cookies happily, while an
/// automatic cookie jar on this side would model a browser talking HTTP and drop
/// every one of them.
/// <para>
/// The antiforgery token is copied from the readable cookie into the header, which
/// is what the Angular client does. That it can be copied at all is the reason
/// that cookie is deliberately not <c>HttpOnly</c>.
/// </para>
/// </remarks>
internal sealed class ProxySession(ProductionApiFixture api)
{
    private const string ForwardedProto = "X-Forwarded-Proto";
    private const string XsrfCookie = "XSRF-TOKEN";
    private const string XsrfHeader = "X-XSRF-TOKEN";

    private readonly ProductionApiFixture _api =
        api ?? throw new ArgumentNullException(nameof(api));

    private readonly HttpClient _client = api.CreateClient();
    private readonly Dictionary<string, string> _cookies = [];

    /// <summary>The account this session registered, once it has one.</summary>
    public string? Email { get; private set; }

    /// <summary>Establishes the antiforgery cookie pair.</summary>
    public async Task BootstrapAsync()
    {
        using HttpResponseMessage response = await SendAsync(HttpMethod.Get, "/api/auth/csrf");

        Assert.True(
            response.IsSuccessStatusCode,
            $"The antiforgery bootstrap answered {(int)response.StatusCode}. {_api.ServerErrors}");

        Assert.True(
            _cookies.ContainsKey(XsrfCookie),
            $"No readable antiforgery cookie was issued. {_api.ServerErrors}");
    }

    /// <summary>Registers an account and adopts the session it returns.</summary>
    public async Task RegisterAsync()
    {
        string email = ApiAuthentication.UniqueEmail();

        using HttpResponseMessage response = await SendAsync(
            HttpMethod.Post,
            "/api/auth/register",
            new { email, password = ApiAuthentication.Password });

        Assert.True(
            response.IsSuccessStatusCode,
            $"Registration answered {(int)response.StatusCode}. {_api.ServerErrors}");

        Email = email;
    }

    /// <summary>Sends one request as the proxy would forward it.</summary>
    /// <param name="sendAntiforgeryToken">
    /// Withholding it is how the CSRF tests prove enforcement survived: everything
    /// else about the request stays identical.
    /// </param>
    public async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string route,
        object? body = null,
        bool sendAntiforgeryToken = true)
    {
        using HttpRequestMessage request = new(method, route);

        request.Headers.TryAddWithoutValidation(ForwardedProto, "https");

        if (_cookies.Count > 0)
        {
            request.Headers.TryAddWithoutValidation(
                "Cookie",
                string.Join("; ", _cookies.Select(entry => $"{entry.Key}={entry.Value}")));
        }

        if (sendAntiforgeryToken && _cookies.TryGetValue(XsrfCookie, out string? token))
        {
            request.Headers.TryAddWithoutValidation(XsrfHeader, token);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        HttpResponseMessage response = await _client.SendAsync(request);

        Remember(response);

        return response;
    }

    /// <summary>Records the cookies a response set, as a browser would.</summary>
    private void Remember(HttpResponseMessage response)
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
                _cookies[pair[..separator]] = pair[(separator + 1)..];
            }
        }
    }
}
