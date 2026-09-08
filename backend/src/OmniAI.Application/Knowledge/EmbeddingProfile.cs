using OmniAI.Domain.Knowledge;

namespace OmniAI.Application.Knowledge;

/// <summary>
/// Everything this platform needs to know about how embeddings are being made,
/// without knowing who is making them.
/// </summary>
/// <remarks>
/// <para>
/// The identifier is opaque and is the marker stored beside every vector; the
/// numbers are the constraints callers have to respect. No provider name, model
/// name, endpoint or credential appears in this type or anywhere above the
/// adapter that builds it.
/// </para>
/// <para>
/// <strong>The profile exists even when no provider is configured.</strong> It is
/// a statement about how this deployment is set up, not about whether a
/// credential happens to be present -- so a deployment with no key still knows
/// which profile its documents would need to be indexed under, and can honestly
/// report every one of them as needing an index rather than showing nothing at
/// all.
/// </para>
/// </remarks>
public sealed record EmbeddingProfile
{
    private EmbeddingProfile(
        EmbeddingProfileId id,
        int dimensions,
        int maxBatchSize,
        int maxInputCharacters)
    {
        Id = id;
        Dimensions = dimensions;
        MaxBatchSize = maxBatchSize;
        MaxInputCharacters = maxInputCharacters;
    }

    /// <summary>The opaque marker stored with every vector produced under it.</summary>
    public EmbeddingProfileId Id { get; }

    /// <summary>How wide every vector is. The database column is declared for it.</summary>
    public int Dimensions { get; }

    /// <summary>
    /// The most inputs that may be sent in one request.
    /// </summary>
    /// <remarks>
    /// A bound rather than a suggestion. Sending a whole document as one request
    /// would be a single enormous payload whose failure loses all of it, and
    /// providers refuse oversized batches anyway -- discovering that limit from a
    /// rejected request in the middle of an index is far worse than respecting it
    /// up front.
    /// </remarks>
    public int MaxBatchSize { get; }

    /// <summary>The most characters a single input may carry.</summary>
    public int MaxInputCharacters { get; }

    public static EmbeddingProfile Create(
        EmbeddingProfileId id,
        int dimensions,
        int maxBatchSize,
        int maxInputCharacters)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dimensions);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBatchSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxInputCharacters);

        return new EmbeddingProfile(id, dimensions, maxBatchSize, maxInputCharacters);
    }
}
