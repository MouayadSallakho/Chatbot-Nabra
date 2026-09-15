using OmniAI.Domain.Ai;

namespace OmniAI.Domain.Tests.Ai;

/// <summary>
/// Provider-reported token counts, and the values that cannot be true.
/// </summary>
/// <remarks>
/// The validation here is defensive rather than pedantic. A count that
/// contradicts itself means either the adapter's mapping or the provider's
/// contract has changed, and both are worth stopping for — a cached-input figure
/// larger than the input would otherwise produce a negative uncached remainder
/// and a cost below zero.
/// </remarks>
public sealed class ProviderTokenUsageTests
{
    [Fact]
    public void Nothing_reported_is_recorded_as_nothing_reported()
    {
        ProviderTokenUsage usage = ProviderTokenUsage.Unreported;

        Assert.False(usage.IsReported);
        Assert.Null(usage.InputTokens);
        Assert.Null(usage.OutputTokens);
        Assert.Null(usage.TotalTokens);

        // Not zero. Zero would be a claim that the call was free.
        Assert.NotEqual(0, usage.InputTokens);
    }

    [Fact]
    public void The_uncached_remainder_is_the_input_minus_the_cache()
    {
        ProviderTokenUsage usage = ProviderTokenUsage.Create(
            inputTokens: 1_000,
            cachedInputTokens: 250);

        Assert.Equal(750, usage.UncachedInputTokens);
    }

    [Fact]
    public void An_unreported_input_has_no_uncached_remainder_either()
    {
        // Deriving one from a total would be the estimate this type exists to
        // refuse.
        Assert.Null(ProviderTokenUsage.Create(totalTokens: 900).UncachedInputTokens);
    }

    [Fact]
    public void A_negative_count_is_refused_wherever_it_appears()
    {
        Assert.Throws<ProviderUsageInvalidException>(() =>
            ProviderTokenUsage.Create(inputTokens: -1));

        Assert.Throws<ProviderUsageInvalidException>(() =>
            ProviderTokenUsage.Create(cachedInputTokens: -5));

        Assert.Throws<ProviderUsageInvalidException>(() =>
            ProviderTokenUsage.Create(outputTokens: -2));

        Assert.Throws<ProviderUsageInvalidException>(() =>
            ProviderTokenUsage.Create(reasoningTokens: -3));

        Assert.Throws<ProviderUsageInvalidException>(() =>
            ProviderTokenUsage.Create(totalTokens: -7));
    }

    [Fact]
    public void Cached_input_larger_than_the_input_is_refused()
    {
        // Cached input is part of the input. Accepting this would make the
        // uncached remainder negative and the cost with it.
        Assert.Throws<ProviderUsageInvalidException>(() =>
            ProviderTokenUsage.Create(inputTokens: 100, cachedInputTokens: 101));
    }

    [Fact]
    public void Reasoning_larger_than_the_output_is_refused()
    {
        // Reasoning is a subset of output under the current contract.
        Assert.Throws<ProviderUsageInvalidException>(() =>
            ProviderTokenUsage.Create(outputTokens: 10, reasoningTokens: 11));
    }

    [Fact]
    public void Cached_input_equal_to_the_input_is_accepted()
    {
        // Entirely cached is a real and ordinary outcome.
        ProviderTokenUsage usage = ProviderTokenUsage.Create(
            inputTokens: 500,
            cachedInputTokens: 500);

        Assert.Equal(0, usage.UncachedInputTokens);
    }

    /// <remarks>
    /// <strong>Deliberately permitted.</strong> No current provider contract
    /// guarantees that the total equals input plus output — some include
    /// categories the two do not cover — and enforcing the identity would reject
    /// honest responses.
    /// </remarks>
    [Fact]
    public void A_total_that_is_not_the_sum_of_input_and_output_is_accepted()
    {
        ProviderTokenUsage usage = ProviderTokenUsage.Create(
            inputTokens: 100,
            outputTokens: 50,
            totalTokens: 170);

        Assert.Equal(170, usage.TotalTokens);
    }
}
