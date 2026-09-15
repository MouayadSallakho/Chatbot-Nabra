using System.ClientModel;
using System.Net;
using OmniAI.Application.Ai;
using OmniAI.Application.Ai.Usage;
using OmniAI.Domain.Ai;
using OmniAI.Domain.Tenants;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Ai.OpenAi;

/// <summary>
/// What the text adapter records, against a canned transport.
/// </summary>
/// <remarks>
/// <para>
/// <strong>No live provider, ever.</strong> Every response here is a string in
/// this repository, served by a local handler. Nothing in this suite can reach
/// OpenAI, which is what makes it safe to assert on money.
/// </para>
/// <para>
/// The most important test in the file is the first one: a ledger that cannot
/// write must stop the call. Everything else is about not inventing numbers.
/// </para>
/// </remarks>
public sealed class OpenAiUsageAccountingTests
{
    private static readonly TenantId Workspace = TenantId.New();

    // ------------------------------------------------------- the cost invariant

    /// <remarks>
    /// <strong>THE COST-SAFETY INVARIANT.</strong> Money spent with no record of
    /// it is invisible until somebody questions an invoice, so the accounting row
    /// is committed first and a failure to write it stops everything.
    /// </remarks>
    [Fact]
    public async Task A_ledger_that_cannot_record_the_call_stops_it_reaching_the_provider()
    {
        CannedOpenAiTransport transport = new();

        RecordingUsageLedger ledger = new() { FailBegin = true };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            OpenAiTestHarness
                .Generator(transport, ledger: ledger)
                .GenerateAsync(OpenAiTestHarness.SimpleRequest(), Context()));

        // The whole point: the transport saw nothing.
        Assert.Equal(0, transport.Calls);
        Assert.Empty(ledger.Operations);
    }

    [Fact]
    public async Task The_operation_is_recorded_before_the_request_is_dispatched()
    {
        CannedOpenAiTransport transport = new();

        RecordingUsageLedger ledger = new();

        await OpenAiTestHarness
            .Generator(transport, ledger: ledger)
            .GenerateAsync(OpenAiTestHarness.SimpleRequest(), Context());

        Assert.Equal(1, transport.Calls);
        Assert.Single(ledger.Operations);
    }

    // ------------------------------------------------------------- attribution

    [Fact]
    public async Task The_call_is_attributed_to_the_workspace_and_purpose_it_was_given()
    {
        RecordingUsageLedger ledger = new();

        await OpenAiTestHarness
            .Generator(new CannedOpenAiTransport(), ledger: ledger)
            .GenerateAsync(
                OpenAiTestHarness.SimpleRequest(),
                AiUsageContext.For(Workspace, AiUsagePurpose.AgentPreviewGeneration));

        AiProviderUsage operation = ledger.Only;

        Assert.Equal(Workspace, operation.TenantId);
        Assert.Equal(AiUsagePurpose.AgentPreviewGeneration, operation.Purpose);
        Assert.Equal(AiProvider.OpenAi, operation.Provider);

        // Generation, not embedding. They are different products at different
        // prices, and conflating them makes every per-model figure wrong.
        Assert.Equal(AiOperationType.TextGeneration, operation.OperationType);
    }

    // ----------------------------------------------------------------- mapping

    [Fact]
    public async Task Exactly_what_the_provider_reported_is_recorded()
    {
        CannedOpenAiTransport transport = new()
        {
            ResponseBody = UsageResponse(
                input: 1_200, cached: 800, output: 300, reasoning: 120, total: 1_500),
        };

        RecordingUsageLedger ledger = new();

        await OpenAiTestHarness
            .Generator(transport, ledger: ledger)
            .GenerateAsync(OpenAiTestHarness.SimpleRequest(), Context());

        AiProviderUsage operation = ledger.Only;

        Assert.Equal(AiOperationStatus.Succeeded, operation.Status);
        Assert.Equal(1_200, operation.Usage.InputTokens);
        Assert.Equal(800, operation.Usage.CachedInputTokens);
        Assert.Equal(300, operation.Usage.OutputTokens);
        Assert.Equal(120, operation.Usage.ReasoningTokens);
        Assert.Equal(1_500, operation.Usage.TotalTokens);
    }

    [Fact]
    public async Task The_model_the_provider_says_it_used_is_recorded_beside_the_one_asked_for()
    {
        RecordingUsageLedger ledger = new();

        await OpenAiTestHarness
            .Generator(new CannedOpenAiTransport(), ledger: ledger)
            .GenerateAsync(OpenAiTestHarness.SimpleRequest(), Context());

        AiProviderUsage operation = ledger.Only;

        // The canned response resolves to its own model name. Both are kept: an
        // alias answered by a dated build is worth being able to see.
        Assert.Equal("gpt-canned", operation.ResolvedModel?.Value);
        Assert.NotEqual(operation.ResolvedModel?.Value, operation.RequestedModel.Value);
    }

    /// <remarks>
    /// A response with no usage block records nothing rather than zeroes. Zero
    /// would be a claim that the call was free.
    /// </remarks>
    [Fact]
    public async Task A_response_with_no_usage_records_nothing_rather_than_zero()
    {
        CannedOpenAiTransport transport = new() { ResponseBody = WithoutUsage() };

        RecordingUsageLedger ledger = new();

        await OpenAiTestHarness
            .Generator(transport, ledger: ledger)
            .GenerateAsync(OpenAiTestHarness.SimpleRequest(), Context());

        AiProviderUsage operation = ledger.Only;

        Assert.Equal(AiOperationStatus.Succeeded, operation.Status);
        Assert.False(operation.Usage.IsReported);
        Assert.Null(operation.Usage.InputTokens);
    }

    /// <remarks>
    /// Impossible figures are discarded, the call is still recorded as the
    /// success it was, and the customer still gets their answer. Failing the
    /// whole generation because a token count was odd would be a worse trade.
    /// </remarks>
    [Fact]
    public async Task Usage_that_cannot_be_true_is_discarded_without_failing_the_call()
    {
        CannedOpenAiTransport transport = new()
        {
            // More cached input than input: impossible under the current contract.
            ResponseBody = UsageResponse(input: 10, cached: 99, output: 5, reasoning: 0, total: 15),
        };

        RecordingUsageLedger ledger = new();

        AiTextGenerationResult result = await OpenAiTestHarness
            .Generator(transport, ledger: ledger)
            .GenerateAsync(OpenAiTestHarness.SimpleRequest(), Context());

        Assert.False(string.IsNullOrWhiteSpace(result.Text));

        AiProviderUsage operation = ledger.Only;

        Assert.Equal(AiOperationStatus.Succeeded, operation.Status);
        Assert.False(operation.Usage.IsReported);
    }

    // ---------------------------------------------------------------- failures

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task A_status_the_provider_chose_to_send_resolves_the_operation_as_failed(
        HttpStatusCode status)
    {
        CannedOpenAiTransport transport = new()
        {
            Status = status,
            ResponseBody = OpenAiCannedResponses.Error("canned", "Refused."),
        };

        RecordingUsageLedger ledger = new();

        await Assert.ThrowsAnyAsync<Exception>(() =>
            OpenAiTestHarness
                .Generator(transport, ledger: ledger)
                .GenerateAsync(OpenAiTestHarness.SimpleRequest(), Context()));

        // The provider decided, so the operation is decided. And exactly one
        // request was made: a hidden retry would turn one accounted operation
        // into several real bills.
        Assert.Equal(AiOperationStatus.Failed, ledger.Only.Status);
        Assert.Equal(1, transport.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task An_upstream_timeout_leaves_the_operation_unresolved(HttpStatusCode status)
    {
        CannedOpenAiTransport transport = new()
        {
            Status = status,
            ResponseBody = OpenAiCannedResponses.Error("timeout", "Too slow."),
        };

        RecordingUsageLedger ledger = new();

        await Assert.ThrowsAsync<AiRuntimeTimeoutException>(() =>
            OpenAiTestHarness
                .Generator(transport, ledger: ledger)
                .GenerateAsync(OpenAiTestHarness.SimpleRequest(), Context()));

        // Nothing decided anything. The provider may have executed and billed it,
        // so it is unresolved rather than failed and certainly not zero.
        Assert.Equal(AiOperationStatus.Unknown, ledger.Only.Status);
        Assert.False(ledger.Only.Usage.IsReported);
    }

    [Fact]
    public async Task A_connection_lost_after_dispatch_leaves_the_operation_unresolved()
    {
        CannedOpenAiTransport transport = new()
        {
            ThrowInstead = () => new TaskCanceledException("The request timed out."),
        };

        RecordingUsageLedger ledger = new();

        await Assert.ThrowsAsync<AiRuntimeTimeoutException>(() =>
            OpenAiTestHarness
                .Generator(transport, ledger: ledger)
                .GenerateAsync(OpenAiTestHarness.SimpleRequest(), Context()));

        Assert.Equal(AiOperationStatus.Unknown, ledger.Only.Status);
        Assert.Equal(1, transport.Calls);
    }

    [Fact]
    public async Task A_caller_who_gives_up_after_dispatch_leaves_the_operation_unresolved()
    {
        CannedOpenAiTransport transport = new() { ObserveCancellation = true };

        RecordingUsageLedger ledger = new();

        using CancellationTokenSource source = new();

        Task<AiTextGenerationResult> pending = OpenAiTestHarness
            .Generator(transport, ledger: ledger)
            .GenerateAsync(OpenAiTestHarness.SimpleRequest(), Context(), source.Token);

        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);

        // The request was already on the wire. Whether the provider ran it is
        // unknown, and unknown is what gets recorded.
        Assert.Equal(AiOperationStatus.Unknown, ledger.Only.Status);
    }

    [Fact]
    public async Task A_caller_who_gives_up_before_dispatch_records_nothing_at_all()
    {
        CannedOpenAiTransport transport = new();

        RecordingUsageLedger ledger = new();

        using CancellationTokenSource source = new();

        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            OpenAiTestHarness
                .Generator(transport, ledger: ledger)
                .GenerateAsync(OpenAiTestHarness.SimpleRequest(), Context(), source.Token));

        // Nothing was dispatched, so nothing was spent and nothing is recorded.
        Assert.Equal(0, transport.Calls);
        Assert.Empty(ledger.Operations);
    }

    // ------------------------------------------------------------------ rating

    [Fact]
    public async Task A_configured_rate_prices_the_call_and_is_snapshotted_onto_it()
    {
        CannedOpenAiTransport transport = new()
        {
            ResponseBody = UsageResponse(
                input: 1_000_000, cached: 0, output: 1_000_000, reasoning: 0, total: 2_000_000),
        };

        // The canned response resolves to "gpt-canned", which is what a rate has
        // to name for the call to be priced.
        AiModelRates rates = AiModelRates.Create(
            AiProvider.OpenAi,
            ModelName.Create("gpt-canned"),
            AiOperationType.TextGeneration,
            CurrencyCode.Create("USD"),
            DateTimeOffset.UtcNow.AddYears(-1),
            inputPerMillion: 3.00m,
            cachedInputPerMillion: 0.75m,
            outputPerMillion: 9.00m);

        RecordingUsageLedger ledger = new(new FixedPricing(rates));

        await OpenAiTestHarness
            .Generator(transport, ledger: ledger)
            .GenerateAsync(OpenAiTestHarness.SimpleRequest(), Context());

        AiProviderUsage operation = ledger.Only;

        Assert.Equal(12.00m, operation.EstimatedCost);
        Assert.Equal("USD", operation.Currency?.Value);
        Assert.Equal(3.00m, operation.InputRatePerMillion);
        Assert.Equal(9.00m, operation.OutputRatePerMillion);
    }

    [Fact]
    public async Task A_model_with_no_configured_rate_is_unrated_rather_than_free()
    {
        RecordingUsageLedger ledger = new(FixedPricing.None);

        await OpenAiTestHarness
            .Generator(new CannedOpenAiTransport(), ledger: ledger)
            .GenerateAsync(OpenAiTestHarness.SimpleRequest(), Context());

        AiProviderUsage operation = ledger.Only;

        Assert.Equal(AiOperationStatus.Succeeded, operation.Status);
        Assert.True(operation.Usage.IsReported);
        Assert.Null(operation.EstimatedCost);
    }

    // ----------------------------------------------------------------- secrets

    [Fact]
    public async Task The_api_key_never_appears_in_anything_the_adapter_records()
    {
        RecordingUsageLedger ledger = new();

        await OpenAiTestHarness
            .Generator(new CannedOpenAiTransport(), ledger: ledger)
            .GenerateAsync(OpenAiTestHarness.SimpleRequest(), Context());

        AiProviderUsage operation = ledger.Only;

        // There is nowhere for it to go: the aggregate has no string property
        // other than bounded model names, which a Domain test also asserts.
        Assert.Equal("gpt-canned", operation.ResolvedModel?.Value);
        Assert.DoesNotContain(
            OpenAiTestHarness.DummyApiKey,
            operation.RequestedModel.Value,
            StringComparison.Ordinal);
    }

    private static AiUsageContext Context() =>
        AiUsageContext.For(Workspace, AiUsagePurpose.ConversationGeneration);

    /// <summary>A completed response with the token block written by hand.</summary>
    private static string UsageResponse(
        long input,
        long cached,
        long output,
        long reasoning,
        long total) =>
        """
        {
          "id": "resp_usage", "object": "response", "created_at": 1730000000,
          "status": "completed", "model": "gpt-canned",
          "output": [
            { "type": "message", "id": "msg_1", "role": "assistant", "status": "completed",
              "content": [ { "type": "output_text", "text": "An answer.", "annotations": [] } ] }
          ],
          "usage": { "input_tokens": INPUT, "output_tokens": OUTPUT, "total_tokens": TOTAL,
                     "input_tokens_details": { "cached_tokens": CACHED },
                     "output_tokens_details": { "reasoning_tokens": REASONING } }
        }
        """
            .Replace("INPUT", input.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("OUTPUT", output.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("TOTAL", total.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("CACHED", cached.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("REASONING", reasoning.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);

    /// <summary>A completed response that carries no usage block at all.</summary>
    private static string WithoutUsage() =>
        """
        {
          "id": "resp_no_usage", "object": "response", "created_at": 1730000000,
          "status": "completed", "model": "gpt-canned",
          "output": [
            { "type": "message", "id": "msg_1", "role": "assistant", "status": "completed",
              "content": [ { "type": "output_text", "text": "An answer.", "annotations": [] } ] }
          ]
        }
        """;
}
