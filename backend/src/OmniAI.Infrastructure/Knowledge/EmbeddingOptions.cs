using OmniAI.Domain.Knowledge;

namespace OmniAI.Infrastructure.Knowledge;

/// <summary>
/// How this deployment is set up to produce embeddings.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The dimension is 1,536, and it is not casually configurable.</strong>
/// It is the width the database column is declared for, so changing it needs a
/// migration as well as a settings change -- which is correct: vectors of
/// different widths cannot be compared, and a deployment that silently switched
/// would have an index half of which was meaningless. The profile identifier
/// carries the width for exactly that reason, so the mismatch is caught by data
/// rather than by hope.
/// </para>
/// <para>
/// <strong>Why 1,536 rather than something larger.</strong> It is the native
/// width of the current small embedding model, it is well inside pgvector&#39;s
/// 2,000-dimension limit for an HNSW index -- 3,072 is not, and would silently
/// lose the index -- and it costs a quarter of the storage and a third of the
/// query time of the large model for a difference in retrieval quality that
/// nothing in this product would notice.
/// </para>
/// <para>
/// The model name appears here and in the profile identifier and nowhere else.
/// Neither Domain nor Application ever sees it.
/// </para>
/// </remarks>
internal sealed class EmbeddingOptions
{
    public const string SectionName = "Embeddings";

    /// <summary>The only dimension this schema supports.</summary>
    /// <remarks>
    /// A constant rather than a setting. The migration declares
    /// <c>vector(1536)</c>, and a configuration value that disagreed with the
    /// column would fail on the first insert, deep inside an index transaction,
    /// with a message about a column type.
    /// </remarks>
    public const int Dimensions = 1_536;

    public const string DefaultModel = "text-embedding-3-small";

    /// <summary>
    /// The most pieces sent in one request. Default 32.
    /// </summary>
    /// <remarks>
    /// Well under what the provider accepts. A larger batch is a larger payload
    /// whose failure loses more work, and the whole document is indexed in a
    /// handful of requests either way.
    /// </remarks>
    public const int DefaultMaxBatchSize = 32;

    /// <summary>
    /// The most characters in one piece. Default 8,000.
    /// </summary>
    /// <remarks>
    /// Comfortably above the chunk bound, which is 1,500. It exists as a guard on
    /// the query side, where the text is a question from a channel this platform
    /// may not have bounded itself.
    /// </remarks>
    public const int DefaultMaxInputCharacters = 8_000;

    public const int DefaultTimeoutSeconds = 30;

    public string Model { get; set; } = DefaultModel;

    public int MaxBatchSize { get; set; } = DefaultMaxBatchSize;

    public int MaxInputCharacters { get; set; } = DefaultMaxInputCharacters;

    public int TimeoutSeconds { get; set; } = DefaultTimeoutSeconds;

    public TimeSpan Timeout => TimeSpan.FromSeconds(TimeoutSeconds);

    /// <summary>
    /// The opaque marker stored beside every vector made under these settings.
    /// </summary>
    /// <remarks>
    /// Provider, model and width, joined. Changing any of them changes the
    /// identifier, which makes every document indexed under the old one report as
    /// needing an index rather than being compared against vectors it cannot
    /// meaningfully be compared with.
    /// </remarks>
    public EmbeddingProfileId ProfileId =>
        EmbeddingProfileId.Create($"openai:{Model}:{Dimensions}");

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Model))
        {
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(Model)} must name an embedding model.");
        }

        if (MaxBatchSize is <= 0 or > 256)
        {
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(MaxBatchSize)} must be between 1 and 256.");
        }

        if (MaxInputCharacters <= 0)
        {
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(MaxInputCharacters)} must be greater than zero.");
        }

        if (TimeoutSeconds <= 0)
        {
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(TimeoutSeconds)} must be greater than zero.");
        }
    }
}
