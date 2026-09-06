using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace OmniAI.Api.IntegrationTests.Auth;

/// <summary>
/// The whole authenticated, antiforgery-protected flow, through the proxy model.
/// </summary>
/// <remarks>
/// <strong>Getting the cookies to render is not the same as the application
/// working.</strong> The cookie assertions next door prove the attributes are
/// right; these prove that a real reader can still sign in and change something
/// afterwards, and that CSRF enforcement did not quietly become decorative when
/// forwarded headers were introduced.
/// </remarks>
public sealed class ProductionProxyCsrfTests(TrustedProxyApiFixture api)
    : IClassFixture<TrustedProxyApiFixture>
{
    [Fact]
    public async Task An_authenticated_unsafe_request_succeeds_with_a_valid_token()
    {
        // Antiforgery bootstrap, registration, then a protected state-changing
        // request -- the complete path, all of it over a plain-HTTP proxy hop with
        // the browser's scheme forwarded.
        ProxySession session = new(api);

        await session.BootstrapAsync();
        await session.RegisterAsync();

        using HttpResponseMessage created = await session.SendAsync(
            HttpMethod.Post,
            "/api/tenants",
            new { name = "Proxied Trading", slug = $"p{Guid.NewGuid():N}"[..20] });

        Assert.True(
            created.StatusCode == HttpStatusCode.Created,
            $"Creating a workspace answered {(int)created.StatusCode}. {api.ServerErrors}");
    }

    [Fact]
    public async Task The_created_workspace_is_immediately_readable_by_its_creator()
    {
        // Proves the session cookie survived the round trip as well as the token:
        // this read is authenticated and membership-checked.
        ProxySession session = new(api);

        await session.BootstrapAsync();
        await session.RegisterAsync();

        Guid tenantId;

        using (HttpResponseMessage created = await session.SendAsync(
            HttpMethod.Post,
            "/api/tenants",
            new { name = "Proxied Trading", slug = $"p{Guid.NewGuid():N}"[..20] }))
        {
            created.EnsureSuccessStatusCode();

            using JsonDocument body =
                JsonDocument.Parse(await created.Content.ReadAsStringAsync());

            tenantId = body.RootElement.GetProperty("id").GetGuid();
        }

        using HttpResponseMessage read =
            await session.SendAsync(HttpMethod.Get, $"/api/tenants/{tenantId}");

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
    }

    [Fact]
    public async Task An_authenticated_unsafe_request_without_a_token_is_still_rejected()
    {
        // The assertion that forwarded-header support did not weaken CSRF. Same
        // session, same cookies, same proxy -- only the header is withheld.
        ProxySession session = new(api);

        await session.BootstrapAsync();
        await session.RegisterAsync();

        using HttpResponseMessage refused = await session.SendAsync(
            HttpMethod.Post,
            "/api/tenants",
            new { name = "Forged Trading", slug = $"f{Guid.NewGuid():N}"[..20] },
            sendAntiforgeryToken: false);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    [Fact]
    public async Task Registration_itself_is_still_antiforgery_protected()
    {
        ProxySession session = new(api);

        await session.BootstrapAsync();

        using HttpResponseMessage refused = await session.SendAsync(
            HttpMethod.Post,
            "/api/auth/register",
            new
            {
                email = ApiAuthentication.UniqueEmail(),
                password = ApiAuthentication.Password,
            },
            sendAntiforgeryToken: false);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    [Fact]
    public async Task An_anonymous_caller_is_still_refused_behind_the_proxy()
    {
        // Authentication is unaffected by any of this: a session is still required.
        ProxySession session = new(api);

        await session.BootstrapAsync();

        using HttpResponseMessage refused =
            await session.SendAsync(HttpMethod.Get, "/api/me/tenants");

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
    }
}
