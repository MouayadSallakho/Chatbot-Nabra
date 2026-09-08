namespace OmniAI.Application.Knowledge;

/// <summary>
/// How much knowledge a single answer may draw on.
/// </summary>
/// <remarks>
/// <para>
/// <strong>These numbers are a budget shared with the conversation, not an
/// addition to it.</strong> The conversation window was reduced when knowledge
/// arrived rather than left alone: history keeps 16,000 characters, knowledge may
/// contribute 8,000, and a request therefore stays inside the same 24,000 that
/// the platform sent before any of this existed. Bolting retrieval on top of a
/// full history would have raised the cost of every single turn by half without
/// anybody deciding to.
/// </para>
/// <para>
/// <strong>Six chunks.</strong> Enough that a question answered across two or
/// three passages finds all of them, and few enough that the least relevant
/// result is still plausibly about the question. Retrieving twenty would mostly
/// add text that dilutes what matters and is paid for by the token.
/// </para>
/// </remarks>
public sealed record KnowledgeRetrievalPolicy
{
    /// <summary>How many chunks a search asks the database for. Default 6.</summary>
    public int TopK { get; init; } = 6;

    /// <summary>
    /// The most characters of knowledge one request may carry. Default 8,000.
    /// </summary>
    /// <remarks>
    /// Whole chunks are dropped from the bottom of the ranking until the rest
    /// fits. Nothing is ever cut short: half a passage is worse than no passage,
    /// because a model reading a sentence that stops mid-clause will finish it
    /// itself.
    /// </remarks>
    public int MaxContextCharacters { get; init; } = 8_000;
}
