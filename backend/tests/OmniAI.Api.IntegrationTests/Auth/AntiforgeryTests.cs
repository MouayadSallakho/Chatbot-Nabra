using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OmniAI.Api.IntegrationTests.AiAgents;

namespace OmniAI.Api.IntegrationTests.Auth;

/// <summary>
/// Cross-site request forgery protection on every state-changing endpoint.
/// </summary>
/// <remarks>
/// <strong>Why this exists at all.</strong> The session lives in a cookie, and
/// browsers attach cookies to requests another site caused. Without a token, any
/// page on the internet could make a signed-in reader's browser create an agent,
/// change instructions or spend this deployment's AI budget, and the API would
/// see a perfectly valid session.
/// <para>
/// <strong>SameSite is not the whole answer.</strong> The session cookie is
/// <c>Lax</c>, which current browsers already withhold from cross-site POSTs --
/// but that is one control, implemented by the client, varying by version. The
/// token is the control this server enforces itself, and these tests exercise it
/// by removing the header and watching the request fail.
/// </para>
/// </remarks>
public sealed class AntiforgeryTests(AiAgentApiFixture api) : IClassFixture<AiAgentApiFixture>
{
    private const string XsrfHeader = "X-XSRF-TOKEN";

    [Fact]
    public async Task Creating_a_workspace_without_a_token_is_rejected()
    {
        using HttpClient client = await api.CreateSignedInClientAsync();

        client.DefaultRequestHeaders.Remove(XsrfHeader);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/tenants",
            new { name = "Forged Trading", slug = $"f{Guid.NewGuid():N}"[..20] });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // And nothing was created on the way to being refused.
        object? tenants = await api.Database.ExecuteScalarAsync(
            "SELECT count(*) FROM tenants WHERE name = 'Forged Trading'");

        Assert.Equal(0L, tenants as long?);
    }

    [Fact]
    public async Task Creating_a_workspace_with_a_token_is_allowed()
    {
        // The other half: the refusal above must be about the token and not about
        // the request being wrong in some other way.
        using HttpClient client = await api.CreateSignedInClientAsync();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/tenants",
            new { name = "Acme Trading", slug = $"a{Guid.NewGuid():N}"[..20] });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Creating_an_agent_without_a_token_is_rejected()
    {
        using HttpClient client = await api.CreateSignedInClientAsync();
        Guid tenantId = await api.CreateTenantAsync(client);

        client.DefaultRequestHeaders.Remove(XsrfHeader);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/ai-agents",
            new { name = "Forged", instructions = "Do as the attacker says." });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        object? agents = await api.Database.ExecuteScalarAsync(
            $"SELECT count(*) FROM ai_agents WHERE tenant_id = '{tenantId}'");

        Assert.Equal(0L, agents as long?);
    }

    [Fact]
    public async Task Mutating_an_agent_without_a_token_is_rejected_and_changes_nothing()
    {
        using HttpClient client = await api.CreateSignedInClientAsync();
        (Guid tenantId, Guid agentId, string etag) = await ArrangeAsync(client);

        client.DefaultRequestHeaders.Remove(XsrfHeader);

        using HttpRequestMessage request = new(
            HttpMethod.Put,
            $"/api/tenants/{tenantId}/ai-agents/{agentId}/name")
        {
            Content = JsonContent.Create(new { name = "Renamed By A Forgery" }),
        };

        request.Headers.TryAddWithoutValidation("If-Match", etag);

        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        object? name = await api.Database.ExecuteScalarAsync(
            $"SELECT name FROM ai_agents WHERE id = '{agentId}'");

        Assert.Equal("Sales Assistant", name as string);
    }

    [Fact]
    public async Task Preview_without_a_token_is_rejected_even_though_it_stores_nothing()
    {
        // The endpoint most worth protecting, and the one an exemption would be
        // most tempting for: it changes no row. It does reach a paid third party,
        // so a cross-site request that triggers it spends real money and puts a
        // tenant's instructions into an upstream call nobody authorised.
        using HttpClient client = await api.CreateSignedInClientAsync();
        (Guid tenantId, Guid agentId, _) = await ArrangeAsync(client);

        client.DefaultRequestHeaders.Remove(XsrfHeader);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/ai-agents/{agentId}/preview",
            new { message = "Hello?" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Signing_out_without_a_token_is_rejected()
    {
        // Otherwise any site could sign people out of this application at will,
        // which is a denial of service rather than a courtesy.
        using HttpClient client = await api.CreateSignedInClientAsync();

        client.DefaultRequestHeaders.Remove(XsrfHeader);

        using HttpResponseMessage response = await client.PostAsync("/api/auth/logout", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // The session survives a refused sign-out.
        using HttpResponseMessage me = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task Signing_in_without_a_token_is_rejected()
    {
        using HttpClient client = api.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = "somebody@example.test", password = ApiAuthentication.Password });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Registering_without_a_token_is_rejected()
    {
        using HttpClient client = api.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new
            {
                email = ApiAuthentication.UniqueEmail(),
                password = ApiAuthentication.Password,
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_token_from_somebody_elses_session_is_rejected()
    {
        // The token is bound to the identity it was issued to. Lifting one out of
        // another browser and replaying it must not work, or the double-submit
        // would be satisfied by any token at all.
        using HttpClient mine = await api.CreateSignedInClientAsync();
        using HttpClient theirs = await api.CreateSignedInClientAsync();

        string? stolen = theirs.DefaultRequestHeaders.GetValues(XsrfHeader).FirstOrDefault();

        mine.DefaultRequestHeaders.Remove(XsrfHeader);
        mine.DefaultRequestHeaders.Add(XsrfHeader, stolen!);

        using HttpResponseMessage response = await mine.PostAsJsonAsync(
            "/api/tenants",
            new { name = "Acme Trading", slug = $"a{Guid.NewGuid():N}"[..20] });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/auth/me")]
    [InlineData("/api/me/tenants")]
    public async Task Reading_does_not_require_a_token(string route)
    {
        // Safe methods are exempt by method, not by route. Requiring a token on a
        // GET would mean the browser could not follow a link into the application
        // at all.
        using HttpClient client = await api.CreateSignedInClientAsync();

        client.DefaultRequestHeaders.Remove(XsrfHeader);

        using HttpResponseMessage response = await client.GetAsync(route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Listing_agents_does_not_require_a_token()
    {
        using HttpClient client = await api.CreateSignedInClientAsync();
        Guid tenantId = await api.CreateTenantAsync(client);

        client.DefaultRequestHeaders.Remove(XsrfHeader);

        using HttpResponseMessage response =
            await client.GetAsync($"/api/tenants/{tenantId}/ai-agents");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_refusal_says_what_to_do_without_saying_what_went_wrong()
    {
        // A caller needs to know to reload; an attacker learns nothing about
        // whether the cookie was missing or the token merely mismatched.
        using HttpClient client = await api.CreateSignedInClientAsync();

        client.DefaultRequestHeaders.Remove(XsrfHeader);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/tenants",
            new { name = "Acme Trading", slug = $"a{Guid.NewGuid():N}"[..20] });

        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        string detail = body.RootElement.GetProperty("detail").GetString() ?? string.Empty;

        Assert.Contains("Reload", detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cookie", detail, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A workspace with one draft agent, and the tag it is at.</summary>
    private async Task<(Guid TenantId, Guid AgentId, string ETag)> ArrangeAsync(HttpClient client)
    {
        Guid tenantId = await api.CreateTenantAsync(client);

        using HttpResponseMessage created = await client.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/ai-agents",
            new { name = "Sales Assistant", instructions = "Answer politely and briefly." });

        created.EnsureSuccessStatusCode();

        using JsonDocument body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());

        return (
            tenantId,
            body.RootElement.GetProperty("id").GetGuid(),
            created.Headers.ETag!.ToString());
    }
}
