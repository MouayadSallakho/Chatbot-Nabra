using System.ClientModel.Primitives;
using Microsoft.Extensions.Logging.Abstractions;
using OmniAI.Application.Ai;
using OmniAI.Application.Ai.Usage;
using OmniAI.Testing;
using OmniAI.Infrastructure.Ai.OpenAi;

namespace OmniAI.Infrastructure.IntegrationTests.Ai.OpenAi;

/// <summary>
/// Builds the real OpenAI adapter over a canned transport.
/// </summary>
/// <remarks>
/// <para>
/// The production <c>OpenAiTextGenerator</c>, the production
/// <c>OpenAiClientFactory</c> and the production <c>ResponsesClient</c> — with a
/// dummy credential and no socket. Nothing about the adapter under test is
/// substituted, so these tests exercise the same code a deployment runs.
/// </para>
/// <para>
/// The credential is an obvious placeholder that could not authenticate anywhere.
/// No real key is used, read or referenced by any deterministic test.
/// </para>
/// </remarks>
internal static class OpenAiTestHarness
{
    /// <summary>A credential that is plainly not a real key.</summary>
    internal const string DummyApiKey = "test-key-not-a-real-credential";

    /// <summary>A request with one user turn, for the common case.</summary>
    internal static AiTextGenerationRequest SimpleRequest(
        string instructions = "Help customers choose products. Never quote a discount.",
        string message = "Which laptop suits a student?") =>
        AiTextGenerationRequest.Create(instructions, [AiTurn.FromUser(message)]);

    /// <summary>
    /// The real adapter, wired to the supplied canned transport.
    /// </summary>
    /// <summary>A context for calls whose accounting is not what is under test.</summary>
    internal static AiUsageContext Usage => TestUsage.Context();

    internal static IAiTextGenerator Generator(
        CannedOpenAiTransport transport,
        OpenAiOptions? options = null,
        IAiProviderUsageLedger? ledger = null)
    {
        ArgumentNullException.ThrowIfNull(transport);

        options ??= new OpenAiOptions();

        PipelineTransport pipeline = new HttpClientPipelineTransport(new HttpClient(transport));

        return new OpenAiTextGenerator(
            OpenAiClientFactory.Create(DummyApiKey, options, pipeline),
            options,
            ledger ?? new RecordingUsageLedger(),
            NullLogger<OpenAiTextGenerator>.Instance);
    }
}
