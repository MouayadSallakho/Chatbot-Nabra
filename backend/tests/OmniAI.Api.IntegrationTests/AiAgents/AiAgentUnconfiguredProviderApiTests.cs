using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using OmniAI.Application.Ai;
using static OmniAI.Api.IntegrationTests.AiAgents.AiAgentEndpointsTests;

namespace OmniAI.Api.IntegrationTests.AiAgents;

/// <summary>
/// What a deployment with no AI provider actually does.
/// </summary>
/// <remarks>
/// <para>
/// The most important suite in this phase, because it tests the configuration
/// every deployment currently has. It uses the plain
/// <see cref="AiAgentApiFixture"/>, which substitutes nothing: the composition
/// root runs exactly as it would in production, and
/// <c>IAiTextGenerator</c> resolves to the real implementation.
/// </para>
/// <para>
/// Two properties matter here. The application must still start and serve
/// everything else -- an unconfigured capability is not a broken application --
/// and the one operation that needs a provider must say so plainly rather than
/// inventing an answer.
/// </para>
/// </remarks>
public sealed class AiAgentUnconfiguredProviderApiTests(AiAgentApiFixture api)
    : IClassFixture<AiAgentApiFixture>
{
    private const string Instructions = "Help customers choose suitable products.";

    [Fact]
    public async Task The_application_starts_with_no_ai_provider_configured()
    {
        // The whole agent management slice keeps working. AI being unconfigured
        // must not take the rest of the platform down with it.
        using HttpClient client = api.CreateClient();

        using HttpResponseMessage health = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);

        (Guid tenantId, Guid agentId) = await ArrangeAsync(client);

        using HttpResponseMessage agent = await client.GetAsync(AgentRoute(tenantId, agentId));
        Assert.Equal(HttpStatusCode.OK, agent.StatusCode);
    }

    [Fact]
    public void The_registered_generator_is_the_unconfigured_one()
    {
        // Proves the substitution used by the other preview suite is a test-only
        // arrangement, and that no fake is registered as production behaviour.
        using IServiceScope scope = api.Services.CreateScope();

        IAiTextGenerator generator = scope.ServiceProvider.GetRequiredService<IAiTextGenerator>();

        Assert.Equal("UnconfiguredAiTextGenerator", generator.GetType().Name);
    }

    [Fact]
    public async Task A_preview_returns_503()
    {
        // Not 500: nothing failed unexpectedly. Not 404: the endpoint exists. The
        // capability is simply not available on this deployment, and the same
        // request will succeed once a provider is configured.
        using HttpClient client = api.CreateClient();
        (Guid tenantId, Guid agentId) = await ArrangeAsync(client);

        using HttpResponseMessage response = await PreviewAsync(client, tenantId, agentId, "Hello?");

        await AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task No_answer_is_ever_fabricated()
    {
        // The failure this behaviour exists to prevent: a canned reply, a
        // template, or an echo of the caller's own message returned as though an
        // agent had produced it. Somebody evaluating whether their agent works
        // would conclude that it does.
        using HttpClient client = api.CreateClient();
        (Guid tenantId, Guid agentId) = await ArrangeAsync(client);

        const string Question = "Which laptop suits a student?";

        using HttpResponseMessage response = await PreviewAsync(client, tenantId, agentId, Question);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("reply", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Question, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_failure_names_no_provider_and_no_configuration_key()
    {
        using HttpClient client = api.CreateClient();
        (Guid tenantId, Guid agentId) = await ArrangeAsync(client);

        using HttpResponseMessage response = await PreviewAsync(client, tenantId, agentId, "Hello?");

        string detail = await AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable);

        // A client must not learn which vendor this platform intends to use, what
        // the setting is called, or whether a key is present but rejected.
        foreach (string forbidden in (string[])
            ["openai", "anthropic", "azure", "api key", "apikey", "OPENAI_API_KEY",
             "connection", "environment variable", "appsettings", "Unconfigured"])
        {
            Assert.DoesNotContain(forbidden, detail, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task No_retry_after_header_is_advertised()
    {
        // The platform has no idea when a provider will be configured -- that is a
        // deployment decision, not a queue that drains -- so inviting clients to
        // poll on a schedule would be inventing a promise.
        using HttpClient client = api.CreateClient();
        (Guid tenantId, Guid agentId) = await ArrangeAsync(client);

        using HttpResponseMessage response = await PreviewAsync(client, tenantId, agentId, "Hello?");

        Assert.Empty(response.Headers.RetryAfter?.ToString() ?? string.Empty);
    }

    [Fact]
    public async Task A_bad_request_is_still_answered_before_the_missing_provider()
    {
        // Input validation runs first, so a caller with an unusable message is
        // told what is wrong with it rather than being told AI is unavailable --
        // which would send them to fix the wrong thing.
        using HttpClient client = api.CreateClient();
        (Guid tenantId, Guid agentId) = await ArrangeAsync(client);

        using HttpResponseMessage response = await PreviewAsync(client, tenantId, agentId, "   ");

        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_cross_tenant_preview_is_still_404_rather_than_503()
    {
        // Ownership is resolved before the runtime is reached, so a caller cannot
        // learn that an agent exists in another tenant by observing which failure
        // comes back.
        using HttpClient client = api.CreateClient();
        (Guid _, Guid agentB) = await ArrangeAsync(client);
        Guid tenantA = await api.CreateTenantAsync("Other Tenant");

        using HttpResponseMessage response = await PreviewAsync(client, tenantA, agentB, "Hello?");

        await AssertProblemAsync(response, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_failed_preview_changes_nothing()
    {
        using HttpClient client = api.CreateClient();
        (Guid tenantId, Guid agentId) = await ArrangeAsync(client);

        using HttpResponseMessage before = await client.GetAsync(AgentRoute(tenantId, agentId));
        AgentPayload stored = await ReadAgentAsync(before);
        string version = ETagOf(before);

        using (HttpResponseMessage failed = await PreviewAsync(client, tenantId, agentId, "Hello?"))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        }

        using HttpResponseMessage after = await client.GetAsync(AgentRoute(tenantId, agentId));

        Assert.Equal(stored, await ReadAgentAsync(after));
        Assert.Equal(version, ETagOf(after));
    }

    // ------------------------------------------------------------------- helpers

    private static Task<HttpResponseMessage> PreviewAsync(
        HttpClient client,
        Guid tenantId,
        Guid agentId,
        string? message) =>
        client.PostAsJsonAsync(ActionRoute(tenantId, agentId, "preview"), new { message });

    private async Task<(Guid TenantId, Guid AgentId)> ArrangeAsync(HttpClient client)
    {
        Guid tenantId = await api.CreateTenantAsync();

        using HttpResponseMessage created =
            await CreateAsync(client, tenantId, "Sales Assistant", Instructions);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        return (tenantId, (await ReadAgentAsync(created)).Id);
    }
}
