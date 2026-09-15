using System.ClientModel.Primitives;
using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using OmniAI.Application.Ai.Usage;
using OmniAI.Application.Knowledge;
using OmniAI.Domain.Ai;
using OmniAI.Domain.Tenants;
using OmniAI.Infrastructure.Ai.OpenAi;
using OmniAI.Infrastructure.IntegrationTests.Ai.OpenAi;
using OmniAI.Infrastructure.Knowledge;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Knowledge;

/// <summary>
/// What the embedding adapter records, against a canned transport.
/// </summary>
/// <remarks>
/// <strong>The batch rule is the one worth reading.</strong> One provider request
/// is one accounted operation however many inputs it carried, because that is
/// what an invoice shows. Counting vectors instead would make a single indexing
/// run look like ninety-six purchases.
/// </remarks>
public sealed class OpenAiEmbeddingAccountingTests
{
    private const int Dimensions = 1536;

    private static readonly TenantId Workspace = TenantId.New();

    // ------------------------------------------------------- the cost invariant

    [Fact]
    public async Task A_ledger_that_cannot_record_the_call_stops_it_reaching_the_provider()
    {
        CannedOpenAiTransport transport = new() { ResponseBody = Vectors(1, input: 5) };

        RecordingUsageLedger ledger = new() { FailBegin = true };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Generator(transport, ledger).GenerateAsync(["a question"], Context()));

        Assert.Equal(0, transport.Calls);
        Assert.Empty(ledger.Operations);
    }

    // ----------------------------------------------------------------- batching

    [Fact]
    public async Task One_provider_request_is_one_accounted_operation_however_many_inputs()
    {
        CannedOpenAiTransport transport = new() { ResponseBody = Vectors(8, input: 640) };

        RecordingUsageLedger ledger = new();

        IReadOnlyList<EmbeddingVector> vectors = await Generator(transport, ledger)
            .GenerateAsync(Inputs(8), Context());

        Assert.Equal(8, vectors.Count);

        // Eight vectors, one HTTP request, one row. Not eight.
        Assert.Equal(1, transport.Calls);
        Assert.Single(ledger.Operations);
        Assert.Equal(640, ledger.Only.Usage.InputTokens);
    }

    [Fact]
    public async Task An_empty_batch_calls_nothing_and_records_nothing()
    {
        CannedOpenAiTransport transport = new();

        RecordingUsageLedger ledger = new();

        Assert.Empty(await Generator(transport, ledger).GenerateAsync([], Context()));

        // An empty batch has a correct answer, and paying a provider to agree is
        // not it -- so there is no operation to account for either.
        Assert.Equal(0, transport.Calls);
        Assert.Empty(ledger.Operations);
    }

    // ------------------------------------------------------------------ mapping

    [Fact]
    public async Task Exactly_the_input_usage_the_provider_reported_is_recorded()
    {
        CannedOpenAiTransport transport = new() { ResponseBody = Vectors(2, input: 123) };

        RecordingUsageLedger ledger = new();

        await Generator(transport, ledger).GenerateAsync(Inputs(2), Context());

        AiProviderUsage operation = ledger.Only;

        Assert.Equal(AiOperationType.Embedding, operation.OperationType);
        Assert.Equal(AiUsagePurpose.KnowledgeIndexEmbedding, operation.Purpose);
        Assert.Equal(Workspace, operation.TenantId);
        Assert.Equal(123, operation.Usage.InputTokens);
        Assert.Equal(123, operation.Usage.TotalTokens);

        // Embeddings produce no output and no reasoning. Recording zero would
        // invite an output rate to be applied to it.
        Assert.Null(operation.Usage.OutputTokens);
        Assert.Null(operation.Usage.ReasoningTokens);
    }

    // ----------------------------------------------------------------- failures

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task A_definitive_refusal_resolves_the_operation_as_failed(HttpStatusCode status)
    {
        CannedOpenAiTransport transport = new()
        {
            Status = status,
            ResponseBody = OpenAiCannedResponses.Error("canned", "Refused."),
        };

        RecordingUsageLedger ledger = new();

        await Assert.ThrowsAnyAsync<Exception>(() =>
            Generator(transport, ledger).GenerateAsync(Inputs(1), Context()));

        Assert.Equal(AiOperationStatus.Failed, ledger.Only.Status);

        // Exactly one request. A hidden retry would be several real bills against
        // one accounted operation.
        Assert.Equal(1, transport.Calls);
    }

    [Fact]
    public async Task A_timeout_after_dispatch_leaves_the_operation_unresolved()
    {
        CannedOpenAiTransport transport = new()
        {
            ThrowInstead = () => new TaskCanceledException("The request timed out."),
        };

        RecordingUsageLedger ledger = new();

        await Assert.ThrowsAsync<EmbeddingTimeoutException>(() =>
            Generator(transport, ledger).GenerateAsync(Inputs(1), Context()));

        Assert.Equal(AiOperationStatus.Unknown, ledger.Only.Status);
        Assert.False(ledger.Only.Usage.IsReported);
    }

    [Fact]
    public async Task A_caller_who_gives_up_before_dispatch_records_nothing()
    {
        CannedOpenAiTransport transport = new();

        RecordingUsageLedger ledger = new();

        using CancellationTokenSource source = new();

        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Generator(transport, ledger).GenerateAsync(Inputs(1), Context(), source.Token));

        Assert.Equal(0, transport.Calls);
        Assert.Empty(ledger.Operations);
    }

    [Fact]
    public async Task A_batch_the_platform_refuses_is_never_dispatched_or_recorded()
    {
        CannedOpenAiTransport transport = new();

        RecordingUsageLedger ledger = new();

        EmbeddingOptions options = new() { MaxBatchSize = 2 };

        // Refused by this platform before the provider is involved, so there is
        // no spend and nothing to account for.
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Generator(transport, ledger, options).GenerateAsync(Inputs(3), Context()));

        Assert.Equal(0, transport.Calls);
        Assert.Empty(ledger.Operations);
    }

    // ------------------------------------------------------------------ rating

    [Fact]
    public async Task An_embedding_call_is_priced_on_its_input_alone()
    {
        CannedOpenAiTransport transport = new() { ResponseBody = Vectors(1, input: 2_000_000) };

        AiModelRates rates = AiModelRates.Create(
            AiProvider.OpenAi,
            ModelName.Create("text-embedding-canned"),
            AiOperationType.Embedding,
            CurrencyCode.Create("USD"),
            DateTimeOffset.UtcNow.AddYears(-1),
            inputPerMillion: 0.05m);

        RecordingUsageLedger ledger = new(new FixedPricing(rates));

        await Generator(transport, ledger).GenerateAsync(Inputs(1), Context());

        // 2M input tokens at 0.05 per million.
        Assert.Equal(0.10m, ledger.Only.EstimatedCost);
        Assert.Equal("USD", ledger.Only.Currency?.Value);
    }

    private static AiUsageContext Context() =>
        AiUsageContext.For(Workspace, AiUsagePurpose.KnowledgeIndexEmbedding);

    private static string[] Inputs(int count) =>
        [.. Enumerable.Range(0, count).Select(index => $"Some text number {index}.")];

    private static OpenAiTextEmbeddingGenerator Generator(
        CannedOpenAiTransport transport,
        IAiProviderUsageLedger ledger,
        EmbeddingOptions? options = null)
    {
        options ??= new EmbeddingOptions();

        PipelineTransport pipeline = new HttpClientPipelineTransport(new HttpClient(transport));

        return new OpenAiTextEmbeddingGenerator(
            OpenAiEmbeddingClientFactory.Create(OpenAiTestHarness.DummyApiKey, options, pipeline),
            options,
            EmbeddingProfile.Create(
                options.ProfileId,
                Dimensions,
                options.MaxBatchSize,
                options.MaxInputCharacters),
            ledger,
            NullLogger<OpenAiTextEmbeddingGenerator>.Instance);
    }

    /// <summary>An embeddings response with the given vector count and usage.</summary>
    private static string Vectors(int count, long input)
    {
        string zeros = string.Join(",", Enumerable.Repeat("0.0", Dimensions));

        string data = string.Join(",", Enumerable.Range(0, count).Select(index =>
            $$"""{"object":"embedding","index":{{index}},"embedding":[{{zeros}}]}"""));

        return $$"""
        {
          "object": "list",
          "data": [{{data}}],
          "model": "text-embedding-canned",
          "usage": { "prompt_tokens": {{input}}, "total_tokens": {{input}} }
        }
        """;
    }
}
