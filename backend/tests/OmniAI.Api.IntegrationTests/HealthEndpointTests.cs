using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace OmniAI.Api.IntegrationTests;

/// <summary>
/// Hosts the real API in memory and exercises its only endpoint over HTTP.
/// This proves the composition root boots and the pipeline serves requests.
/// </summary>
public sealed class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Health_endpoint_returns_ok_with_healthy_status()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/health", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        HealthResponse? body = await response.Content.ReadFromJsonAsync<HealthResponse>();

        Assert.Equal("healthy", body?.Status);
    }

    private sealed record HealthResponse(string Status);
}
