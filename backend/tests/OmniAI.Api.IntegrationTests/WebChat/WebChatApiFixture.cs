using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OmniAI.Api.IntegrationTests.Authorization;
using OmniAI.Application.Ai;
using OmniAI.Testing;

namespace OmniAI.Api.IntegrationTests.WebChat;

/// <summary>
/// A hosted application with a provider this suite controls.
/// </summary>
/// <remarks>
/// <strong>The generator is a double, and its call count is an assertion.</strong>
/// Every reply in production costs the workspace money, so "was the provider
/// reached, and how many times" is a property these tests check rather than an
/// implementation detail — and the only way to check it is to be the provider.
/// No test here reaches the internet or reads a real API key.
/// </remarks>
public sealed class WebChatApiFixture : RoleApiFixture
{
    private readonly RecordingAiTextGenerator _generator = new();

    internal RecordingAiTextGenerator Generator => _generator;

    /// <summary>Clears recorded calls between tests sharing this fixture.</summary>
    internal RecordingAiTextGenerator ResetGenerator()
    {
        _generator.Reset();

        return _generator;
    }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        base.ConfigureTestServices(services);

        services.Replace(ServiceDescriptor.Singleton<IAiTextGenerator>(_generator));
    }

    /// <summary>Reads one cookie's value from a response, ignoring its attributes.</summary>
    public static string? ReadCookie(HttpResponseMessage response, string name)
    {
        ArgumentNullException.ThrowIfNull(response);

        return ReadSetCookie(response, name) is string header
            ? header.Split(';')[0].Split('=', 2)[1]
            : null;
    }

    /// <summary>
    /// The <c>Set-Cookie</c> line that actually sets a value, with its attributes.
    /// </summary>
    /// <remarks>
    /// <strong>Skips deletions, and has to.</strong> Starting a conversation emits
    /// two headers of the same name from Phase 16: one expiring whatever an
    /// earlier release left at the broader path, and one carrying the credential
    /// at this conversation's own path. Taking the first match would read the
    /// expiry and assert on a cookie with no value, which is exactly the failure
    /// that led here.
    /// </remarks>
    public static string? ReadSetCookie(HttpResponseMessage response, string name) =>
        ReadSetCookies(response, name).FirstOrDefault(cookie =>
            !cookie.StartsWith(name + "=;", StringComparison.Ordinal));

    /// <summary>
    /// Every <c>Set-Cookie</c> line of this name, deletions included.
    /// </summary>
    /// <remarks>
    /// For the one test that asserts the legacy cookie really is expired, which is
    /// invisible to <see cref="ReadSetCookie"/> by design.
    /// </remarks>
    public static IReadOnlyList<string> ReadSetCookies(HttpResponseMessage response, string name)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (!response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? cookies))
        {
            return [];
        }

        return
        [
            .. cookies.Where(cookie => cookie.StartsWith(name + "=", StringComparison.Ordinal)),
        ];
    }

    /// <summary>Creates an active agent and an active chat endpoint for it.</summary>
    /// <returns>The endpoint's public key and its administrative id.</returns>
    public static async Task<(string PublicKey, Guid EndpointId)> PublishChatAsync(
        HttpClient owner,
        Guid tenantId,
        string agentName = "Support")
    {
        ArgumentNullException.ThrowIfNull(owner);

        Guid agentId = await CreateActiveAgentAsync(owner, tenantId, agentName);

        HttpResponseMessage created = await owner.PostAsJsonAsync(
            Relative($"/api/tenants/{tenantId}/website-chat"),
            new { aiAgentId = agentId });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        WebChatEndpointPayload? endpoint =
            await created.Content.ReadFromJsonAsync<WebChatEndpointPayload>();

        Assert.NotNull(endpoint);

        string tag = created.Headers.ETag?.ToString() ?? throw new InvalidOperationException(
            "The create response carried no entity tag.");

        HttpRequestMessage activate = new(
            HttpMethod.Post,
            Relative($"/api/tenants/{tenantId}/website-chat/{endpoint.Id}/activate"));

        activate.Headers.TryAddWithoutValidation("If-Match", tag);

        HttpResponseMessage activated = await owner.SendAsync(activate);

        Assert.Equal(HttpStatusCode.OK, activated.StatusCode);

        return (endpoint.PublicKey, endpoint.Id);
    }

    /// <summary>Creates an agent and puts it into service.</summary>
    public static async Task<Guid> CreateActiveAgentAsync(
        HttpClient owner,
        Guid tenantId,
        string name = "Support")
    {
        ArgumentNullException.ThrowIfNull(owner);

        HttpResponseMessage created = await owner.PostAsJsonAsync(
            Relative($"/api/tenants/{tenantId}/ai-agents"),
            new { name, instructions = "Answer politely and briefly." });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        AgentPayload? agent = await created.Content.ReadFromJsonAsync<AgentPayload>();

        Assert.NotNull(agent);

        string tag = created.Headers.ETag?.ToString() ?? throw new InvalidOperationException(
            "The create response carried no entity tag.");

        HttpRequestMessage activate = new(
            HttpMethod.Post,
            Relative($"/api/tenants/{tenantId}/ai-agents/{agent.Id}/activate"));

        activate.Headers.TryAddWithoutValidation("If-Match", tag);

        Assert.Equal(HttpStatusCode.OK, (await owner.SendAsync(activate)).StatusCode);

        return agent.Id;
    }

    /// <summary>A relative request URI, written once rather than at every call site.</summary>
    public static Uri Relative(string path) => new(path, UriKind.Relative);

    private sealed record WebChatEndpointPayload(Guid Id, string PublicKey, string Status);

    private sealed record AgentPayload(Guid Id);
}
