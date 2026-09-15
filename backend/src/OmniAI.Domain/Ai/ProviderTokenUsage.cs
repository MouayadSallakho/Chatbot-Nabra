using System.Globalization;

namespace OmniAI.Domain.Ai;

/// <summary>
/// What a provider said it charged for, in tokens.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Every count is nullable, and that is the whole point.</strong> These
/// numbers are only ever the provider's own report. Nothing here is derived from
/// string length, a tokenizer approximation or a character count: an estimate
/// that looks like a measurement is worse than a gap, because somebody will
/// reconcile an invoice against it.
/// </para>
/// <para>
/// <strong>Absent is not zero.</strong> A provider that reported nothing is
/// recorded as having reported nothing. Writing zero would claim a free call.
/// </para>
/// <para>
/// The invariants below reject values that cannot be true. They deliberately do
/// <em>not</em> require <c>total == input + output</c>: no current provider
/// contract guarantees that identity, and enforcing it would reject honest
/// responses.
/// </para>
/// </remarks>
public sealed record ProviderTokenUsage
{
    private ProviderTokenUsage(
        long? inputTokens,
        long? cachedInputTokens,
        long? outputTokens,
        long? reasoningTokens,
        long? totalTokens)
    {
        InputTokens = inputTokens;
        CachedInputTokens = cachedInputTokens;
        OutputTokens = outputTokens;
        ReasoningTokens = reasoningTokens;
        TotalTokens = totalTokens;
    }

    /// <summary>Tokens the provider counted as input, cached ones included.</summary>
    public long? InputTokens { get; }

    /// <summary>
    /// The part of the input the provider served from its cache.
    /// </summary>
    /// <remarks>
    /// A subset of <see cref="InputTokens"/>, priced differently. Kept separately
    /// rather than netted off, because the uncached remainder is what the
    /// standard rate applies to and both halves have to be visible to explain a
    /// figure.
    /// </remarks>
    public long? CachedInputTokens { get; }

    /// <summary>Tokens the provider counted as output.</summary>
    public long? OutputTokens { get; }

    /// <summary>
    /// The part of the output spent on reasoning.
    /// </summary>
    /// <remarks>
    /// A subset of <see cref="OutputTokens"/> under the current contract, so it is
    /// reported for visibility and <strong>never added to a cost again</strong> —
    /// doing so would bill the same tokens twice.
    /// </remarks>
    public long? ReasoningTokens { get; }

    /// <summary>The provider's own total, when it gave one.</summary>
    public long? TotalTokens { get; }

    /// <summary>Whether the provider reported anything at all.</summary>
    public bool IsReported =>
        InputTokens is not null
        || OutputTokens is not null
        || TotalTokens is not null;

    /// <summary>The input the standard rate applies to.</summary>
    /// <remarks>
    /// Null when input was not reported: an uncached figure invented from a
    /// missing total would be the estimate this type exists to refuse.
    /// </remarks>
    public long? UncachedInputTokens =>
        InputTokens is long input ? input - (CachedInputTokens ?? 0) : null;

    /// <summary>Nothing was reported.</summary>
    public static ProviderTokenUsage Unreported { get; } =
        new(null, null, null, null, null);

    /// <summary>
    /// Reads what a provider reported, refusing what cannot be true.
    /// </summary>
    /// <exception cref="ProviderUsageInvalidException">
    /// A count is negative, or a subset exceeds the whole it belongs to.
    /// </exception>
    public static ProviderTokenUsage Create(
        long? inputTokens = null,
        long? cachedInputTokens = null,
        long? outputTokens = null,
        long? reasoningTokens = null,
        long? totalTokens = null)
    {
        RequireNotNegative(inputTokens, "input");
        RequireNotNegative(cachedInputTokens, "cached input");
        RequireNotNegative(outputTokens, "output");
        RequireNotNegative(reasoningTokens, "reasoning");
        RequireNotNegative(totalTokens, "total");

        // Cached input is part of the input. A provider claiming otherwise has
        // sent something this platform cannot price, and pricing it anyway would
        // produce a negative uncached remainder.
        if (cachedInputTokens is long cached && cached > (inputTokens ?? 0))
        {
            throw new ProviderUsageInvalidException(string.Create(
                CultureInfo.InvariantCulture,
                $"The provider reported {cached} cached input tokens but only "
                + $"{inputTokens ?? 0} input tokens. Cached input is part of the input."));
        }

        // Reasoning is part of the output under the current contract.
        if (reasoningTokens is long reasoning && reasoning > (outputTokens ?? 0))
        {
            throw new ProviderUsageInvalidException(string.Create(
                CultureInfo.InvariantCulture,
                $"The provider reported {reasoning} reasoning tokens but only "
                + $"{outputTokens ?? 0} output tokens. Reasoning is part of the output."));
        }

        return new ProviderTokenUsage(
            inputTokens,
            cachedInputTokens,
            outputTokens,
            reasoningTokens,
            totalTokens);
    }

    private static void RequireNotNegative(long? value, string what)
    {
        if (value is long amount && amount < 0)
        {
            throw new ProviderUsageInvalidException(
                $"A provider reported {amount} {what} tokens. A token count cannot be negative.");
        }
    }
}

/// <summary>
/// Raised when a provider reports usage that cannot be true.
/// </summary>
/// <remarks>
/// Its own exception because the response is not merely unusable — it is
/// evidence that either the adapter's mapping or the provider's contract has
/// changed, and both want investigating rather than retrying.
/// </remarks>
public sealed class ProviderUsageInvalidException(string message) : Exception(message);
