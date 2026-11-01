using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using OmniAI.Api.IntegrationTests.AiAgents;

namespace OmniAI.Api.IntegrationTests.Auth;

/// <summary>
/// What an unauthenticated caller can and cannot reach.
/// </summary>
/// <remarks>
/// <strong>Every route group is checked separately.</strong> Authorization is
/// configured per group, so proving one group answers <c>401</c> proves nothing
/// about another -- a group mapped without <c>RequireAuthorization</c> would be a
/// single missing line that no compiler and no other test would notice.
/// <para>
/// <c>401</c> throughout, never <c>404</c>. Turning an unauthenticated request
/// into a fake not-found would tell a signed-out browser tab that its workspace
/// had disappeared, when what it actually needs to do is sign in again.
/// </para>
/// </remarks>
public sealed class AnonymousAccessTests(AiAgentApiFixture api) : IClassFixture<AiAgentApiFixture>
{
    [Fact]
    public async Task Reading_a_workspace_requires_a_session()
    {
        (Guid tenantId, _, _) = await ArrangeAsync();

        using HttpClient anonymous = api.CreateClient();

        using HttpResponseMessage response = await anonymous.GetAsync($"/api/tenants/{tenantId}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Creating_a_workspace_requires_a_session()
    {
        using HttpClient anonymous = api.CreateClient();
        await ApiAuthentication.BootstrapAntiforgeryAsync(anonymous);

        using HttpResponseMessage response = await anonymous.PostAsJsonAsync(
            "/api/tenants",
            new { name = "Anonymous Trading", slug = $"x{Guid.NewGuid():N}"[..20] });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        object? tenants = await api.Database.ExecuteScalarAsync(
            "SELECT count(*) FROM tenants WHERE name = 'Anonymous Trading'");

        Assert.Equal(0L, tenants as long?);
    }

    [Fact]
    public async Task Listing_agents_requires_a_session()
    {
        (Guid tenantId, _, _) = await ArrangeAsync();

        using HttpClient anonymous = api.CreateClient();

        using HttpResponseMessage response =
            await anonymous.GetAsync($"/api/tenants/{tenantId}/ai-agents");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Reading_one_agent_requires_a_session()
    {
        (Guid tenantId, Guid agentId, _) = await ArrangeAsync();

        using HttpClient anonymous = api.CreateClient();

        using HttpResponseMessage response =
            await anonymous.GetAsync($"/api/tenants/{tenantId}/ai-agents/{agentId}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Mutating_an_agent_requires_a_session()
    {
        (Guid tenantId, Guid agentId, string etag) = await ArrangeAsync();

        using HttpClient anonymous = api.CreateClient();
        await ApiAuthentication.BootstrapAntiforgeryAsync(anonymous);

        using HttpRequestMessage request = new(
            HttpMethod.Put,
            $"/api/tenants/{tenantId}/ai-agents/{agentId}/name")
        {
            Content = JsonContent.Create(new { name = "Renamed By Nobody" }),
        };

        request.Headers.TryAddWithoutValidation("If-Match", etag);

        using HttpResponseMessage response = await anonymous.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        object? name = await api.Database.ExecuteScalarAsync(
            $"SELECT name FROM ai_agents WHERE id = '{agentId}'");

        Assert.Equal("Sales Assistant", name as string);
    }

    [Fact]
    public async Task Preview_requires_a_session()
    {
        // The billable one. An anonymous caller must not be able to spend this
        // deployment's provider budget.
        (Guid tenantId, Guid agentId, _) = await ArrangeAsync();

        using HttpClient anonymous = api.CreateClient();
        await ApiAuthentication.BootstrapAntiforgeryAsync(anonymous);

        using HttpResponseMessage response = await anonymous.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/ai-agents/{agentId}/preview",
            new { message = "Hello?" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_workspace_list_requires_a_session()
    {
        using HttpClient anonymous = api.CreateClient();

        using HttpResponseMessage response = await anonymous.GetAsync("/api/me/tenants");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_current_account_requires_a_session()
    {
        using HttpClient anonymous = api.CreateClient();

        using HttpResponseMessage response = await anonymous.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_unauthenticated_refusal_carries_a_problem_details_body()
    {
        // The cookie scheme's default is a redirect to a login page that does not
        // exist here, which would reach a JSON client as HTML and be reported as a
        // parse failure -- burying an expired session under an unrelated error.
        using HttpClient anonymous = api.CreateClient();

        using HttpResponseMessage response = await anonymous.GetAsync("/api/me/tenants");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Null(response.Headers.Location);
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/api/auth/csrf")]
    public async Task The_intentionally_anonymous_endpoints_still_answer(string route)
    {
        // The complete anonymous surface, besides registration and sign-in which
        // have their own tests. A liveness probe that required a session could not
        // be called by the thing that needs it, and a client that could not obtain
        // an antiforgery token could never sign in.
        using HttpClient anonymous = api.CreateClient();

        using HttpResponseMessage response = await anonymous.GetAsync(route);

        Assert.True(
            response.IsSuccessStatusCode,
            $"{route} answered {(int)response.StatusCode}.");
    }

    [Fact]
    public void The_anonymous_account_surface_is_exactly_five_routes()
    {
        // Phase 37.3 added the two password reset routes; nothing else under
        // /api/auth or /api/me may answer without a session. Read from the
        // endpoints' own metadata, so a new route cannot slip in unnoticed.
        Microsoft.AspNetCore.Routing.EndpointDataSource source =
            api.Services.GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>();

        string[] anonymous =
        [
            .. source.Endpoints
                .OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
                .Where(endpoint => endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.IAllowAnonymous>() is not null)
                .Where(endpoint => endpoint.RoutePattern.RawText is string route
                    && (route.StartsWith("/api/auth", StringComparison.Ordinal) || route.StartsWith("/api/me", StringComparison.Ordinal)))
                .Select(endpoint => string.Concat(
                    endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods[0],
                    " ",
                    endpoint.RoutePattern.RawText))
                .Order(StringComparer.Ordinal),
        ];

        Assert.Equal(
            [
                "GET /api/auth/csrf",
                "POST /api/auth/login",
                "POST /api/auth/password/forgot",
                "POST /api/auth/password/reset",
                "POST /api/auth/register",
            ],
            anonymous);
    }

    /// <summary>A workspace with one agent, created by somebody who is signed in.</summary>
    private async Task<(Guid TenantId, Guid AgentId, string ETag)> ArrangeAsync()
    {
        using HttpClient owner = await api.CreateSignedInClientAsync();

        Guid tenantId = await api.CreateTenantAsync(owner);

        using HttpResponseMessage created = await owner.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/ai-agents",
            new { name = "Sales Assistant", instructions = "Answer politely and briefly." });

        created.EnsureSuccessStatusCode();

        using System.Text.Json.JsonDocument body =
            System.Text.Json.JsonDocument.Parse(await created.Content.ReadAsStringAsync());

        return (
            tenantId,
            body.RootElement.GetProperty("id").GetGuid(),
            created.Headers.ETag!.ToString());
    }
}
