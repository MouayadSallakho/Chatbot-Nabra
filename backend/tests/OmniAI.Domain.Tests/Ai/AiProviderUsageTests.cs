using OmniAI.Domain;
using OmniAI.Domain.Ai;
using OmniAI.Domain.Tenants;

namespace OmniAI.Domain.Tests.Ai;

/// <summary>
/// The accounting aggregate: what it refuses, and what it refuses to invent.
/// </summary>
/// <remarks>
/// Most of these are about the difference between <em>absent</em> and
/// <em>zero</em>. That distinction is the entire value of the feature: a figure
/// that quietly reads "no tokens" where the truth is "nobody told us" produces a
/// spend report that is confidently wrong.
/// </remarks>
public sealed class AiProviderUsageTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    private static readonly ModelName Model = ModelName.Create("test-model-x");

    // --------------------------------------------------------------- beginning

    [Fact]
    public void An_operation_begins_unresolved_and_unrated()
    {
        AiProviderUsage operation = Begin();

        Assert.Equal(AiOperationStatus.Started, operation.Status);
        Assert.False(operation.IsSettled);
        Assert.Null(operation.CompletedAt);
        Assert.False(operation.IsRated);
        Assert.Null(operation.EstimatedCost);

        // Nothing reported yet, and nothing invented.
        Assert.Null(operation.Usage.InputTokens);
        Assert.Null(operation.Usage.OutputTokens);
        Assert.False(operation.Usage.IsReported);
    }

    [Fact]
    public void An_operation_must_be_attributed_to_a_workspace()
    {
        // Unattributed spend is spend nobody can reconcile. There is deliberately
        // no way to record one.
        Assert.Throws<DomainValidationException>(() => AiProviderUsage.Begin(
            default,
            AiProvider.OpenAi,
            AiOperationType.TextGeneration,
            AiUsagePurpose.ConversationGeneration,
            Model,
            Noon));
    }

    [Fact]
    public void Each_operation_gets_its_own_identity_from_this_platform()
    {
        // Generated here, before the provider is called -- there is nothing else
        // it could come from, and an id taken from a response would be missing in
        // exactly the ambiguous case that most needs one.
        Assert.NotEqual(Begin().Id, Begin().Id);
    }

    // --------------------------------------------------------------- settling

    [Fact]
    public void A_successful_call_records_exactly_what_the_provider_reported()
    {
        AiProviderUsage operation = Begin();

        ProviderTokenUsage reported = ProviderTokenUsage.Create(
            inputTokens: 1_200,
            cachedInputTokens: 800,
            outputTokens: 300,
            reasoningTokens: 120,
            totalTokens: 1_500);

        Assert.True(operation.Succeed(
            reported, ModelName.Create("test-model-x-2026-09-01"), null, Noon.AddSeconds(2)));

        Assert.Equal(AiOperationStatus.Succeeded, operation.Status);
        Assert.Equal(1_200, operation.Usage.InputTokens);
        Assert.Equal(800, operation.Usage.CachedInputTokens);
        Assert.Equal(300, operation.Usage.OutputTokens);
        Assert.Equal(120, operation.Usage.ReasoningTokens);
        Assert.Equal(1_500, operation.Usage.TotalTokens);

        // The requested name is kept beside the resolved one rather than replaced.
        Assert.Equal("test-model-x", operation.RequestedModel.Value);
        Assert.Equal("test-model-x-2026-09-01", operation.ResolvedModel?.Value);
    }

    [Fact]
    public void A_failure_records_no_usage_and_no_cost()
    {
        AiProviderUsage operation = Begin();

        Assert.True(operation.Fail(Noon.AddSeconds(1)));

        Assert.Equal(AiOperationStatus.Failed, operation.Status);
        Assert.False(operation.Usage.IsReported);
        Assert.Null(operation.EstimatedCost);
    }

    [Fact]
    public void An_ambiguous_outcome_is_unknown_rather_than_failed_or_free()
    {
        AiProviderUsage operation = Begin();

        Assert.True(operation.MarkUnknown(Noon.AddSeconds(30)));

        // The provider may have executed and billed this. Calling it a failure
        // would understate spend; calling it zero tokens would be an invention.
        Assert.Equal(AiOperationStatus.Unknown, operation.Status);
        Assert.False(operation.Usage.IsReported);
        Assert.Null(operation.EstimatedCost);
    }

    // ------------------------------------------------------------ idempotency

    [Fact]
    public void Settling_twice_changes_nothing_and_says_so()
    {
        AiProviderUsage operation = Begin();

        ProviderTokenUsage reported = ProviderTokenUsage.Create(inputTokens: 100, outputTokens: 50);

        Assert.True(operation.Succeed(reported, null, Rates(), Noon.AddSeconds(1)));

        decimal? first = operation.EstimatedCost;

        // The same finalisation arriving again -- a retried handler, two threads
        // racing on one response. It must not double the cost.
        Assert.False(operation.Succeed(reported, null, Rates(), Noon.AddSeconds(2)));

        Assert.Equal(first, operation.EstimatedCost);
        Assert.Equal(Noon.AddSeconds(1), operation.CompletedAt);
    }

    [Theory]
    [InlineData(AiOperationStatus.Succeeded)]
    [InlineData(AiOperationStatus.Failed)]
    [InlineData(AiOperationStatus.Unknown)]
    public void A_settled_operation_refuses_every_other_outcome(AiOperationStatus first)
    {
        AiProviderUsage operation = Begin();

        switch (first)
        {
            case AiOperationStatus.Succeeded:
                operation.Succeed(ProviderTokenUsage.Unreported, null, null, Noon);

                break;

            case AiOperationStatus.Failed:
                operation.Fail(Noon);

                break;

            default:
                operation.MarkUnknown(Noon);

                break;
        }

        Assert.False(operation.Succeed(
            ProviderTokenUsage.Create(inputTokens: 9_000), null, Rates(), Noon.AddMinutes(1)));

        Assert.False(operation.Fail(Noon.AddMinutes(1)));
        Assert.False(operation.MarkUnknown(Noon.AddMinutes(1)));

        Assert.Equal(first, operation.Status);
    }

    // -------------------------------------------------------------- the money

    [Fact]
    public void A_rated_call_snapshots_the_rates_it_was_priced_with()
    {
        AiProviderUsage operation = Begin();

        operation.Succeed(
            ProviderTokenUsage.Create(inputTokens: 1_000_000, outputTokens: 500_000),
            null,
            Rates(),
            Noon.AddSeconds(1));

        // 1M input at 2.00 + 0.5M output at 8.00 = 2.00 + 4.00.
        Assert.Equal(6.00m, operation.EstimatedCost);
        Assert.Equal("USD", operation.Currency?.Value);

        // The rates are copied onto the row so a later price change cannot
        // rewrite what this call was rated with.
        Assert.Equal(2.00m, operation.InputRatePerMillion);
        Assert.Equal(0.50m, operation.CachedInputRatePerMillion);
        Assert.Equal(8.00m, operation.OutputRatePerMillion);
        Assert.Equal(Rates().EffectiveFrom, operation.RatesEffectiveFrom);
    }

    [Fact]
    public void Cached_input_is_priced_at_the_cached_rate_and_the_rest_at_the_standard_one()
    {
        AiProviderUsage operation = Begin();

        operation.Succeed(
            ProviderTokenUsage.Create(
                inputTokens: 1_000_000,
                cachedInputTokens: 600_000,
                outputTokens: 0),
            null,
            Rates(),
            Noon.AddSeconds(1));

        // 600k cached at 0.50 = 0.30; 400k uncached at 2.00 = 0.80.
        Assert.Equal(1.10m, operation.EstimatedCost);
    }

    [Fact]
    public void Reasoning_tokens_are_never_charged_a_second_time()
    {
        AiProviderUsage withReasoning = Begin();
        AiProviderUsage without = Begin();

        withReasoning.Succeed(
            ProviderTokenUsage.Create(inputTokens: 0, outputTokens: 1_000_000, reasoningTokens: 900_000),
            null,
            Rates(),
            Noon.AddSeconds(1));

        without.Succeed(
            ProviderTokenUsage.Create(inputTokens: 0, outputTokens: 1_000_000),
            null,
            Rates(),
            Noon.AddSeconds(1));

        // Reasoning is already inside the output count under the current
        // contract. Adding it again would bill the same tokens twice.
        Assert.Equal(without.EstimatedCost, withReasoning.EstimatedCost);
        Assert.Equal(8.00m, withReasoning.EstimatedCost);
    }

    [Fact]
    public void No_configured_rate_means_unrated_and_never_free()
    {
        AiProviderUsage operation = Begin();

        operation.Succeed(
            ProviderTokenUsage.Create(inputTokens: 5_000, outputTokens: 2_000),
            null,
            rates: null,
            Noon.AddSeconds(1));

        Assert.Equal(AiOperationStatus.Succeeded, operation.Status);

        // The call happened and the tokens are exact. What is missing is a price,
        // and a missing price is not a price of nothing.
        Assert.False(operation.IsRated);
        Assert.Null(operation.EstimatedCost);
        Assert.Null(operation.Currency);
        Assert.Equal(5_000, operation.Usage.InputTokens);
    }

    [Fact]
    public void A_rate_that_cannot_price_the_whole_call_prices_none_of_it()
    {
        AiProviderUsage operation = Begin();

        // An embedding rate has no output price. A generation that produced
        // output cannot be priced with it.
        AiModelRates embeddingRates = AiModelRates.Create(
            AiProvider.OpenAi,
            Model,
            AiOperationType.Embedding,
            CurrencyCode.Create("usd"),
            Noon.AddDays(-30),
            inputPerMillion: 0.02m);

        operation.Succeed(
            ProviderTokenUsage.Create(inputTokens: 1_000, outputTokens: 500),
            null,
            embeddingRates,
            Noon.AddSeconds(1));

        // Half a cost is worse than none: it looks like a total.
        Assert.Null(operation.EstimatedCost);
    }

    [Fact]
    public void A_call_the_provider_said_nothing_about_is_unrated()
    {
        AiProviderUsage operation = Begin();

        operation.Succeed(ProviderTokenUsage.Unreported, null, Rates(), Noon.AddSeconds(1));

        Assert.Equal(AiOperationStatus.Succeeded, operation.Status);
        Assert.Null(operation.EstimatedCost);
    }

    // ----------------------------------------------------------- what it holds

    /// <remarks>
    /// The privacy guarantee, asserted structurally rather than trusted. If
    /// somebody adds a property that could carry a prompt, a completion, a key or
    /// a vector, this fails.
    /// </remarks>
    [Fact]
    public void The_aggregate_has_nowhere_to_put_content()
    {
        // Words that would mean content had found its way in. "Requested" and
        // "resolved" are deliberately absent from this list: they qualify a model
        // name, which is a published identifier rather than anything anybody said.
        string[] forbidden =
        [
            "prompt", "completion", "text", "message", "content", "instruction",
            "grounding", "excerpt", "vector", "embedding", "apikey", "secret",
            "body", "payload", "customer", "email", "phone", "header",
        ];

        foreach (System.Reflection.PropertyInfo property in typeof(AiProviderUsage).GetProperties())
        {
            string name = property.Name.ToLowerInvariant();

            Assert.DoesNotContain(forbidden, word => name.Contains(word, StringComparison.Ordinal));

            // And no free-form string that a caller could stuff anything into.
            // Model names are their own bounded type; everything else is a
            // number, an instant, an enum or an identifier.
            Assert.NotEqual(typeof(string), property.PropertyType);
        }
    }

    /// <remarks>
    /// Money is <see langword="decimal"/> everywhere. A cost held in binary
    /// floating point cannot be reconciled against an invoice.
    /// </remarks>
    [Fact]
    public void Money_is_never_binary_floating_point()
    {
        foreach (System.Reflection.PropertyInfo property in typeof(AiProviderUsage).GetProperties())
        {
            Assert.NotEqual(typeof(double), property.PropertyType);
            Assert.NotEqual(typeof(double?), property.PropertyType);
            Assert.NotEqual(typeof(float), property.PropertyType);
            Assert.NotEqual(typeof(float?), property.PropertyType);
        }

        Assert.Equal(typeof(decimal?), typeof(AiProviderUsage)
            .GetProperty(nameof(AiProviderUsage.EstimatedCost))!.PropertyType);
    }

    private static AiProviderUsage Begin() => AiProviderUsage.Begin(
        TenantId.New(),
        AiProvider.OpenAi,
        AiOperationType.TextGeneration,
        AiUsagePurpose.ConversationGeneration,
        Model,
        Noon);

    /// <summary>Invented rates, easy to check by hand. Not anybody's real prices.</summary>
    private static AiModelRates Rates() => AiModelRates.Create(
        AiProvider.OpenAi,
        Model,
        AiOperationType.TextGeneration,
        CurrencyCode.Create("USD"),
        Noon.AddDays(-30),
        inputPerMillion: 2.00m,
        cachedInputPerMillion: 0.50m,
        outputPerMillion: 8.00m);
}
