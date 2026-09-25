using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using OmniAI.Api.IntegrationTests.AiAgents;
using OmniAI.Testing;

namespace OmniAI.Api.IntegrationTests.Reliability;

/// <summary>
/// Phase 33: what survives the API process being replaced.
/// </summary>
/// <remarks>
/// <para>
/// Each test starts one host, uses it, disposes it, and starts a <em>new</em>
/// host over the same PostgreSQL — the in-process equivalent of a container being
/// redeployed. Before Phase 33 the Data Protection key ring lived in the
/// process's own filesystem, so every restart invalidated every antiforgery token
/// and every session cookie; the first sign-in from a tab left open across a
/// deploy was refused with a 400 (reproduced in Chrome against 1f67259).
/// </para>
/// <para>
/// A dedicated database, not the shared fixture's: the key-ring assertions need
/// to know every key in it was written by these hosts.
/// </para>
/// </remarks>
public sealed class RestartRecoveryTests : IAsyncLifetime
{
    // Synthetic, test-only, 32 zero-ish bytes. Never a deployment value.
    private const string MasterKey = "AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=";

    private readonly PostgresTestDatabase _database = new();

    public Task InitializeAsync() => _database.InitializeAsync();

    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task A_token_issued_before_a_restart_still_validates_after_it()
    {
        CookieContainer browser = new();
        string token;

        await using (WebApplicationFactory<Program> before = Host())
        {
            using HttpClient tab = Browser(before, browser);

            token = await CsrfAsync(tab);
        }

        // The tab stays open; the API is replaced underneath it.
        await using WebApplicationFactory<Program> after = Host();
        using HttpClient same = Browser(after, browser);

        same.DefaultRequestHeaders.Add("X-XSRF-TOKEN", token);

        using HttpResponseMessage register = await same.PostAsJsonAsync(
            "/api/auth/register",
            new { email = $"restart-{Guid.NewGuid():N}@example.test", password = "correct-horse-battery-staple" });

        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
    }

    [Fact]
    public async Task A_session_survives_a_restart()
    {
        CookieContainer browser = new();

        await using (WebApplicationFactory<Program> before = Host())
        {
            using HttpClient tab = Browser(before, browser);

            tab.DefaultRequestHeaders.Add("X-XSRF-TOKEN", await CsrfAsync(tab));

            using HttpResponseMessage register = await tab.PostAsJsonAsync(
                "/api/auth/register",
                new { email = $"session-{Guid.NewGuid():N}@example.test", password = "correct-horse-battery-staple" });

            Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        }

        await using WebApplicationFactory<Program> after = Host();
        using HttpClient same = Browser(after, browser);

        using HttpResponseMessage me = await same.GetAsync(new Uri("/api/auth/me", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task A_token_is_still_bound_to_its_session_after_a_restart()
    {
        // Persistence must not weaken the binding: a token from one browser does
        // not become valid for another just because both survived a restart.
        CookieContainer victim = new();
        CookieContainer attacker = new();
        string victimToken;

        await using (WebApplicationFactory<Program> before = Host())
        {
            using HttpClient tab = Browser(before, victim);
            victimToken = await CsrfAsync(tab);

            using HttpClient other = Browser(before, attacker);
            await CsrfAsync(other);
        }

        await using WebApplicationFactory<Program> after = Host();
        using HttpClient forged = Browser(after, attacker);

        forged.DefaultRequestHeaders.Add("X-XSRF-TOKEN", victimToken);

        using HttpResponseMessage response = await forged.PostAsJsonAsync(
            "/api/auth/register",
            new { email = $"forged-{Guid.NewGuid():N}@example.test", password = "correct-horse-battery-staple" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("antiforgery.invalid", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_persisted_key_ring_is_encrypted_under_the_master_key()
    {
        await using (WebApplicationFactory<Program> host = Host())
        {
            using HttpClient tab = Browser(host, new CookieContainer());

            await CsrfAsync(tab);
        }

        object? keys = await _database.ExecuteScalarAsync("SELECT count(*) FROM data_protection_keys");
        object? plaintext = await _database.ExecuteScalarAsync(
            "SELECT count(*) FROM data_protection_keys WHERE xml LIKE '%<masterKey%' OR xml NOT LIKE '%omniaiEncryptedKey%'");

        Assert.True(keys is long count && count >= 1);
        Assert.Equal(0L, plaintext as long?);
    }

    // ---- Helpers ----------------------------------------------------------

    private WebApplicationFactory<Program> Host() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:OmniAiDatabase", _database.ConnectionString);
            builder.UseSetting("OPENAI_API_KEY", string.Empty);
            builder.UseSetting("Plans:DefaultPlanCode", SeededPlans.DevelopmentCode);
            builder.UseSetting("OMNIAI_SECRET_MASTER_KEY", MasterKey);
        });

    /// <summary>One browser's cookie jar, pointed at whichever host is running now.</summary>
    private static HttpClient Browser(WebApplicationFactory<Program> host, CookieContainer cookies) =>
        host.CreateDefaultClient(new Uri("http://localhost"), new CookieContainerHandler(cookies));

    private static async Task<string> CsrfAsync(HttpClient client)
    {
        using HttpResponseMessage csrf = await client.GetAsync(new Uri("/api/auth/csrf", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NoContent, csrf.StatusCode);

        return WebChat.WebChatApiFixture.ReadCookie(csrf, "XSRF-TOKEN")
            ?? throw new InvalidOperationException("No antiforgery token was issued.");
    }
}
