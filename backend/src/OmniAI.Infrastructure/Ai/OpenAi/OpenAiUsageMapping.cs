using OmniAI.Domain.Ai;
using OpenAI.Embeddings;
using OpenAI.Responses;

// See OpenAiTextGenerator for why OPENAI001 is suppressed in the files that
// touch the Responses surface, and only in those files.
#pragma warning disable OPENAI001

namespace OmniAI.Infrastructure.Ai.OpenAi;

/// <summary>
/// Turns what OpenAI reported into what this platform accounts for.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Only what the response actually carries.</strong> Nothing here counts
/// characters, runs a tokenizer or fills a gap with a plausible number. A field
/// the provider omitted stays absent all the way to the database, where absent
/// and zero are different things and the difference is visible on screen.
/// </para>
/// <para>
/// <strong>The provider's own shape lives here and nowhere else.</strong>
/// <c>ResponseTokenUsage</c>, <c>EmbeddingTokenUsage</c> and their detail types
/// are OpenAI's; the Domain knows only <see cref="ProviderTokenUsage"/>. A
/// provider that renamed a field, or a second provider with a different shape,
/// is a change to this file.
/// </para>
/// <para>
/// Under the current Responses contract cached input is part of the input count
/// and reasoning is part of the output count, so both are reported as the subsets
/// they are. <see cref="ProviderTokenUsage.Create"/> enforces that, and a
/// response violating it is refused rather than stored.
/// </para>
/// </remarks>
internal static class OpenAiUsageMapping
{
    /// <summary>What a Responses call reported.</summary>
    /// <exception cref="ProviderUsageInvalidException">
    /// The provider reported counts that cannot be true.
    /// </exception>
    public static ProviderTokenUsage Read(ResponseTokenUsage? usage)
    {
        if (usage is null)
        {
            // No usage block. Recorded as unreported, never as a free call.
            return ProviderTokenUsage.Unreported;
        }

        return ProviderTokenUsage.Create(
            inputTokens: usage.InputTokenCount,
            cachedInputTokens: usage.InputTokenDetails?.CachedTokenCount,
            outputTokens: usage.OutputTokenCount,
            reasoningTokens: usage.OutputTokenDetails?.ReasoningTokenCount,
            totalTokens: usage.TotalTokenCount);
    }

    /// <summary>What an embeddings call reported.</summary>
    /// <remarks>
    /// Embeddings produce no output tokens and no reasoning, so only input and
    /// total are mapped. Inventing an output count of zero would be indefensible
    /// in the one place it matters: a rate that prices output would then be
    /// applied to it.
    /// </remarks>
    /// <exception cref="ProviderUsageInvalidException">
    /// The provider reported counts that cannot be true.
    /// </exception>
    public static ProviderTokenUsage Read(EmbeddingTokenUsage? usage)
    {
        if (usage is null)
        {
            return ProviderTokenUsage.Unreported;
        }

        return ProviderTokenUsage.Create(
            inputTokens: usage.InputTokenCount,
            totalTokens: usage.TotalTokenCount);
    }

    /// <summary>
    /// The model the provider says it used, when it says.
    /// </summary>
    /// <remarks>
    /// Absent rather than assumed to be the requested one: a provider resolving
    /// an alias to a dated build is the case this field exists to capture.
    /// </remarks>
    public static ModelName? Resolved(string? model) => ModelName.CreateOrNull(model);
}
