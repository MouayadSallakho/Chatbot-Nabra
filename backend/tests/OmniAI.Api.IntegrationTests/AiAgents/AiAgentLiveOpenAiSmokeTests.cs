using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static OmniAI.Api.IntegrationTests.AiAgents.AiAgentEndpointsTests;

namespace OmniAI.Api.IntegrationTests.AiAgents;

/// <summary>
/// One real call to OpenAI, through the real endpoint.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Gated, and off by default.</strong> It does nothing unless
/// <c>OMNIAI_LIVE_OPENAI=1</c> is set <em>and</em> a credential is configured.
/// Every other test in this solution runs offline against a canned socket; this
/// is the only one that can reach the internet, the only one that can cost money,
/// and it has to be asked for twice.
/// </para>
/// <para>
/// What it proves that nothing else can: that the request this platform builds is
/// one OpenAI actually accepts. A canned transport proves the shape is what this
/// code intended; only the provider can say whether that shape is right.
/// </para>
/// <para>
/// <strong>The credential is never read by this file.</strong> Its presence is
/// tested through configuration, its value is never fetched, never printed, never
/// logged and never asserted on. The response is validated for content, not
/// echoed.
/// </para>
/// <para>
/// One request is enough. It is deliberately tiny — a fixed instruction, a
/// one-line message and a bounded output — so the cost of running it is
/// negligible.
/// </para>
/// </remarks>
public sealed class AiAgentLiveOpenAiSmokeTests(AiAgentLiveOpenAiFixture api)
    : IClassFixture<AiAgentLiveOpenAiFixture>
{
    /// <summary>The token the agent is instructed to reply with.</summary>
    private const string Expected = "OMNIAI_OPENAI_OK";

    [Fact]
    public async Task A_real_preview_reaches_openai_and_comes_back()
    {
        if (!AiAgentLiveOpenAiFixture.Enabled)
        {
            // Not gated on. Nothing is asserted and nothing is called: the
            // deterministic suites carry the behavioural coverage, and the phase
            // report states plainly whether this ran.
            return;
        }

        using HttpClient client = await api.CreateSignedInClientAsync();

        Guid tenantId = await api.CreateTenantAsync(client, "Live Smoke Tenant");

        using HttpResponseMessage created = await CreateAsync(
            client,
            tenantId,
            "Smoke Agent",
            $"You are a connectivity probe. Reply with exactly: {Expected}");

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        Guid agentId = (await ReadAgentAsync(created)).Id;

        // HTTP -> PreviewAiAgent -> IAiTextGenerator -> OpenAiTextGenerator
        //      -> OpenAI Responses API -> reply
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            ActionRoute(tenantId, agentId, "preview"),
            new { message = "Run the connectivity check." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        string reply = body.RootElement.GetProperty("reply").GetString() ?? string.Empty;

        Assert.False(string.IsNullOrWhiteSpace(reply), "OpenAI returned an empty reply.");
        Assert.Contains(Expected, reply, StringComparison.Ordinal);

        // The contract holds on the live path too: one field, no provider
        // metadata, no entity tag.
        Assert.Equal(
            "reply",
            Assert.Single(body.RootElement.EnumerateObject().Select(field => field.Name)));

        Assert.Null(response.Headers.ETag);
    }
}

/// <summary>
/// The API with whatever AI provider the environment really configures.
/// </summary>
/// <remarks>
/// The only fixture in the solution that does not force the provider off. It is
/// used by exactly one gated test.
/// </remarks>
public sealed class AiAgentLiveOpenAiFixture : AiAgentApiFixture
{
    /// <summary>Environment variable that must be <c>1</c> for the live test to run.</summary>
    private const string GateVariable = "OMNIAI_LIVE_OPENAI";

    /// <summary>Whether the live smoke test is switched on.</summary>
    /// <remarks>
    /// Two conditions, both required: the operator asked for it, and a credential
    /// exists. Only presence is tested — the value is never read.
    /// </remarks>
    internal static bool Enabled =>
        string.Equals(
            Environment.GetEnvironmentVariable(GateVariable), "1", StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_API_KEY"));

    /// <summary>Lets the real credential through, only when the gate is on.</summary>
    protected override bool DisableAiProvider => !Enabled;
}
