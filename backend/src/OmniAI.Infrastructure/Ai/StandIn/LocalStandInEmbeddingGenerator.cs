using System.Globalization;
using OmniAI.Application.Ai.Usage;
using OmniAI.Application.Knowledge;
using OmniAI.Domain.Ai;
using OmniAI.Infrastructure.Knowledge;

namespace OmniAI.Infrastructure.Ai.StandIn;

/// <summary>
/// The local AI stand-in's embedding generator (Phase 37.2).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Hashed word counts, normalised.</strong> Each normalised word (see
/// <see cref="LocalStandInText"/>) is hashed with 64-bit FNV-1a, over its UTF-16
/// code units, into one of the
/// <see cref="EmbeddingOptions.Dimensions"/> buckets, weighted 1 + ln(count), and
/// the vector is scaled to unit length. Cosine similarity then rises with the
/// words two texts share, so a question retrieves the passages that use its
/// words, in Arabic and in English. FNV-1a rather than
/// <see cref="string.GetHashCode()"/>, which is randomised per process: a vector
/// written by one run must match a query embedded by the next.
/// </para>
/// <para>
/// <strong>Its own profile.</strong> <see cref="LocalAiStandInOptions.ProfileId"/>
/// keeps these vectors apart from real embeddings: retrieval compares only the
/// active profile, so they never mix.
/// </para>
/// <para>
/// It validates a batch exactly as the real adapter does, so a caller that works
/// here works against the provider, and it records one ledger row per batch.
/// </para>
/// </remarks>
internal sealed class LocalStandInEmbeddingGenerator(
    EmbeddingProfile profile,
    IAiProviderUsageLedger ledger)
    : ITextEmbeddingGenerator
{
    private const ulong FnvOffset = 14_695_981_039_346_656_037;

    private const ulong FnvPrime = 1_099_511_628_211;

    private readonly IAiProviderUsageLedger _ledger =
        ledger ?? throw new ArgumentNullException(nameof(ledger));

    public EmbeddingProfile Profile { get; } =
        profile ?? throw new ArgumentNullException(nameof(profile));

    public async Task<IReadOnlyList<EmbeddingVector>> GenerateAsync(
        IReadOnlyList<string> inputs,
        AiUsageContext usage,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(usage);

        if (inputs.Count == 0)
        {
            return [];
        }

        if (inputs.Count > Profile.MaxBatchSize)
        {
            throw new ArgumentException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"At most {Profile.MaxBatchSize} inputs may be embedded in one request; {inputs.Count} were supplied."),
                nameof(inputs));
        }

        foreach (string input in inputs)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                throw new ArgumentException(
                    "An input to embed must not be blank.", nameof(inputs));
            }

            if (input.Length > Profile.MaxInputCharacters)
            {
                throw new ArgumentException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"An input to embed must be at most {Profile.MaxInputCharacters} characters; one has {input.Length}."),
                    nameof(inputs));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        AiProviderUsageId operation = await _ledger
            .BeginAsync(
                usage,
                AiProvider.DevelopmentStandIn,
                AiOperationType.Embedding,
                ModelName.Create(LocalAiStandInOptions.Model),
                cancellationToken)
            .ConfigureAwait(false);

        EmbeddingVector[] vectors = new EmbeddingVector[inputs.Count];
        long words = 0;

        for (int index = 0; index < inputs.Count; index++)
        {
            List<string> inputWords = LocalStandInText.Words(inputs[index]);

            words += inputWords.Count;
            vectors[index] = EmbeddingVector.Create(
                Embed(inputWords, inputs[index]),
                Profile.Dimensions);
        }

        await _ledger
            .SucceedAsync(
                operation,
                ProviderTokenUsage.Create(inputTokens: words, totalTokens: words),
                ModelName.Create(LocalAiStandInOptions.Model),
                CancellationToken.None)
            .ConfigureAwait(false);

        return vectors;
    }

    /// <summary>The unit vector for these words.</summary>
    /// <remarks>
    /// Text with no letters or digits at all (only punctuation, say) still gets a
    /// vector, from the whole trimmed text as one feature: a zero vector has no
    /// direction, and its cosine distance to anything is undefined.
    /// </remarks>
    internal float[] Embed(IReadOnlyList<string> words, string text)
    {
        Dictionary<int, int> counts = [];

        if (words.Count == 0)
        {
            counts[Bucket(text.Trim())] = 1;
        }

        foreach (string word in words)
        {
            int bucket = Bucket(word);

            counts[bucket] = counts.GetValueOrDefault(bucket) + 1;
        }

        double[] weights = new double[Profile.Dimensions];
        double squares = 0;

        foreach ((int bucket, int count) in counts)
        {
            double weight = 1 + Math.Log(count);

            weights[bucket] = weight;
            squares += weight * weight;
        }

        double length = Math.Sqrt(squares);
        float[] vector = new float[Profile.Dimensions];

        for (int index = 0; index < vector.Length; index++)
        {
            vector[index] = (float)(weights[index] / length);
        }

        return vector;
    }

    private int Bucket(string word)
    {
        ulong hash = FnvOffset;

        foreach (char character in word)
        {
            hash ^= character;
            hash *= FnvPrime;
        }

        return (int)(hash % (ulong)Profile.Dimensions);
    }
}
