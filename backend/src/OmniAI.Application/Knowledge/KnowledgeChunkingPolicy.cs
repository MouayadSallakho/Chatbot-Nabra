namespace OmniAI.Application.Knowledge;

/// <summary>
/// The numbers the chunker cuts by.
/// </summary>
/// <remarks>
/// <para>
/// A type rather than four constants inside the chunker, so tests can exercise
/// the boundaries with small values instead of building hundred-thousand
/// character documents to reach them. The defaults are what runs.
/// </para>
/// <para>
/// <strong>Changing any of these changes what gets indexed.</strong> They are not
/// exposed as configuration for that reason: a deployment that quietly chunked
/// differently would produce an index incomparable with one produced anywhere
/// else, and nothing would say so. If they ever do become configurable, the
/// values will have to become part of the embedding profile marker.
/// </para>
/// </remarks>
public sealed record KnowledgeChunkingPolicy
{
    /// <summary>
    /// The most characters one chunk may carry. Default 1,500.
    /// </summary>
    /// <remarks>
    /// Roughly two or three paragraphs of prose, and comfortably inside the input
    /// limit of every current embedding model. Large enough that a chunk carries
    /// a whole thought rather than a fragment, and small enough that six of them
    /// fit inside a sensible share of a generation request.
    /// </remarks>
    public int MaxChunkCharacters { get; init; } = 1_500;

    /// <summary>
    /// How much of the previous window a hard-split chunk repeats. Default 200.
    /// </summary>
    /// <remarks>
    /// About two sentences. Enough that a statement straddling a cut survives
    /// whole in one of the two pieces, and small enough that the duplication is a
    /// rounding error on the cost of indexing rather than a second copy of the
    /// document.
    /// </remarks>
    public int OverlapCharacters { get; init; } = 200;

    /// <summary>
    /// The most chunks one document may produce. Default 400.
    /// </summary>
    /// <remarks>
    /// A backstop, not a working limit. The content bound is 100,000 characters,
    /// which the rules above turn into at most about eighty pieces even in the
    /// worst shape a document can take, so reaching 400 means something has gone
    /// wrong rather than that somebody wrote a long document. It exists so a
    /// pathological input is refused with an explanation instead of becoming
    /// hundreds of paid provider calls.
    /// </remarks>
    public int MaxChunks { get; init; } = 400;
}
