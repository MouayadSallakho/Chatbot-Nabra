using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using OmniAI.Api.IntegrationTests.AiAgents;

namespace OmniAI.Api.IntegrationTests.Reliability;

/// <summary>
/// Phase 33: probes, baseline headers and the antiforgery refusal contract.
/// </summary>
public sealed class OperationalSurfaceTests(AiAgentApiFixture api) : IClassFixture<AiAgentApiFixture>
{
    // ---- Liveness and readiness -------------------------------------------

    [Fact]
    public async Task Liveness_answers_one_word_and_consults_nothing()
    {
        using HttpClient client = api.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/health/live", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{\"status\":\"Healthy\"}", await response.Content.ReadAsStringAsync());
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Readiness_is_healthy_when_postgres_and_redis_answer()
    {
        using HttpClient client = api.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        // Minimal: one property, no check names, no timings, no hosts.
        Assert.Equal("Healthy", body.RootElement.GetProperty("status").GetString());
        Assert.Single(body.RootElement.EnumerateObject());
    }

    [Fact]
    public async Task Readiness_fails_without_the_database_and_says_nothing_about_why()
    {
        await using WebApplicationFactory<Program> broken = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting(
                    "ConnectionStrings:OmniAiDatabase",
                    "Host=127.0.0.1;Port=1;Username=nobody;Password=canary-db-password;Database=none;Timeout=2");
                builder.UseSetting("OPENAI_API_KEY", string.Empty);
            });

        using HttpClient client = broken.CreateClient();

        using HttpResponseMessage ready = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));
        string body = await ready.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.Equal("{\"status\":\"Unhealthy\"}", body);
        Assert.DoesNotContain("canary", body, StringComparison.Ordinal);

        // Liveness is unaffected: restarting a process cannot fix a database.
        using HttpResponseMessage live = await client.GetAsync(new Uri("/health/live", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
    }

    [Fact]
    public async Task Readiness_never_depends_on_a_third_party_provider()
    {
        // The fixture has no OpenAI key, no Meta credentials, no Stripe key and no
        // SMTP server, and readiness is still healthy.
        using HttpClient client = api.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---- Headers ----------------------------------------------------------

    [Theory]
    [InlineData("/api/auth/me")]
    [InlineData("/health/ready")]
    [InlineData("/api/tenants/00000000-0000-0000-0000-000000000001/ai-agents")]
    public async Task The_private_api_is_never_renderable_or_frameable(string route)
    {
        using HttpClient client = api.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri(route, UriKind.Relative));

        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.Equal("no-referrer", Header(response, "Referrer-Policy"));
        Assert.Equal("DENY", Header(response, "X-Frame-Options"));
        Assert.Contains("frame-ancestors 'none'", Header(response, "Content-Security-Policy"), StringComparison.Ordinal);
        Assert.Contains("camera=()", Header(response, "Permissions-Policy"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/widget/v1.js")]
    [InlineData("/api/public/embed/00000000000000000000000000000000")]
    [InlineData("/api/public/webchat/00000000000000000000000000000000")]
    public async Task The_public_widget_and_embed_surface_gets_no_framing_policy(string route)
    {
        // The Phase 18 embed: the widget script is included by customer sites,
        // and the public chat API answers the platform's own framed pages. A
        // framing policy here would protect nothing and could break them.
        using HttpClient client = api.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri(route, UriKind.Relative));

        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.Null(Header(response, "X-Frame-Options"));
        Assert.Null(Header(response, "Content-Security-Policy"));
    }

    [Fact]
    public async Task No_cross_origin_access_is_granted_to_anybody()
    {
        using HttpClient client = api.CreateClient();
        using HttpRequestMessage request = new(HttpMethod.Get, "/api/auth/me");

        request.Headers.Add("Origin", "https://attacker.example");

        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Null(Header(response, "Access-Control-Allow-Origin"));
        Assert.Null(Header(response, "Access-Control-Allow-Credentials"));
    }

    // ---- Antiforgery contract ---------------------------------------------

    [Fact]
    public async Task A_refused_token_carries_a_stable_code_and_withholds_the_reason()
    {
        using HttpClient client = api.CreateClient();

        using HttpResponseMessage csrf = await client.GetAsync(new Uri("/api/auth/csrf", UriKind.Relative));
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", "CfDJ8-a-token-from-another-key-ring");

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = "nobody@example.test", password = "irrelevant-password" });

        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using JsonDocument problem = JsonDocument.Parse(body);

        Assert.Equal("antiforgery.invalid", problem.RootElement.GetProperty("code").GetString());

        // The traceId correlates it with the server log; the log, not the body,
        // says which half of the pair failed.
        Assert.True(problem.RootElement.TryGetProperty("traceId", out _));
        Assert.DoesNotContain("decrypt", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cookie", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_fresh_token_after_a_refusal_works_and_nothing_is_replayed_by_the_server()
    {
        using HttpClient client = api.CreateClient();

        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", "stale");

        string email = $"recover-{Guid.NewGuid():N}@example.test";

        using HttpResponseMessage refused = await client.PostAsJsonAsync(
            "/api/auth/register",
            new { email, password = "correct-horse-battery-staple" });

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        object? created = await api.Database.ExecuteScalarAsync(
            $"SELECT count(*) FROM users WHERE email = '{email}'");

        Assert.Equal(0L, created as long?);

        // The client's recovery: a safe GET, then the reader's own second attempt.
        using HttpResponseMessage csrf = await client.GetAsync(new Uri("/api/auth/csrf", UriKind.Relative));
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", WebChat.WebChatApiFixture.ReadCookie(csrf, "XSRF-TOKEN")!);

        using HttpResponseMessage retried = await client.PostAsJsonAsync(
            "/api/auth/register",
            new { email, password = "correct-horse-battery-staple" });

        Assert.Equal(HttpStatusCode.Created, retried.StatusCode);
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out IEnumerable<string>? values)
        || response.Content.Headers.TryGetValues(name, out values)
            ? string.Join(",", values)
            : null;
}
