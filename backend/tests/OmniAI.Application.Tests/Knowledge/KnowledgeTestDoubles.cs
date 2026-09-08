using System.Globalization;
using OmniAI.Application.Knowledge;
using OmniAI.Application.Knowledge.Persistence;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Knowledge;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Tests.Knowledge;

/// <summary>
/// An embedding generator that counts what it was asked to do.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The call count is the point.</strong> Most of what Phase 17 has to
/// prove about cost is a number: zero embedding calls when a turn is rate
/// limited, zero when a duplicate is replayed, zero when an agent has no
/// knowledge, zero when re-indexing unchanged text. A double that counts is the
/// only way to assert those, and asserting them is the only way they stay true.
/// </para>
/// <para>
/// The vectors it returns are derived from the text so that two identical inputs
/// embed identically and two different ones do not. That is enough for the
/// Application suite, which is about orchestration; genuine cosine ordering is
/// proved against real pgvector in the Infrastructure suite, where it belongs.
/// </para>
/// </remarks>
internal sealed class RecordingEmbeddingGenerator : ITextEmbeddingGenerator
{
    /// <summary>A deliberately tiny width, so a test vector is readable.</summary>
    public const int TestDimensions = 8;

    private readonly List<IReadOnlyList<string>> _batches = [];

    public RecordingEmbeddingGenerator(EmbeddingProfile? profile = null) =>
        Profile = profile ?? EmbeddingProfile.Create(
            EmbeddingProfileId.Create("test:fake:8"),
            TestDimensions,
            maxBatchSize: 4,
            maxInputCharacters: 8_000);

    public EmbeddingProfile Profile { get; }

    /// <summary>How many times a provider request was made.</summary>
    public int Calls { get; private set; }

    /// <summary>Every batch, in the order it was sent.</summary>
    public IReadOnlyList<IReadOnlyList<string>> Batches => _batches;

    /// <summary>Every input across every batch.</summary>
    public IReadOnlyList<string> Inputs => [.. _batches.SelectMany(batch => batch)];

    /// <summary>Makes the next call fail the way an unconfigured provider does.</summary>
    public bool PretendUnavailable { get; set; }

    /// <summary>Makes the next call return one vector too few.</summary>
    public bool PretendShortResponse { get; set; }

    public void Reset()
    {
        _batches.Clear();
        Calls = 0;
        PretendUnavailable = false;
        PretendShortResponse = false;
    }

    public Task<IReadOnlyList<EmbeddingVector>> GenerateAsync(
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        Calls++;
        _batches.Add([.. inputs]);

        cancellationToken.ThrowIfCancellationRequested();

        if (PretendUnavailable)
        {
            throw new EmbeddingUnavailableException();
        }

        if (inputs.Count > Profile.MaxBatchSize)
        {
            throw new ArgumentException("Batch too large.", nameof(inputs));
        }

        // Note there is deliberately no "return a vector of the wrong width"
        // switch. EmbeddingVector validates on construction, so this double
        // physically cannot produce one -- which is the contract working as
        // intended. That failure is proved in the Infrastructure suite, against a
        // canned provider response.
        int produce = PretendShortResponse ? Math.Max(0, inputs.Count - 1) : inputs.Count;

        List<EmbeddingVector> vectors = new(produce);

        for (int index = 0; index < produce; index++)
        {
            vectors.Add(EmbeddingVector.Create(
                Derive(inputs[index], Profile.Dimensions),
                Profile.Dimensions));
        }

        return Task.FromResult<IReadOnlyList<EmbeddingVector>>(vectors);
    }

    /// <summary>
    /// A stable vector for a piece of text.
    /// </summary>
    /// <remarks>
    /// Derived from the string's characters so it is a pure function of the input:
    /// the same text always embeds to the same vector, which is what the
    /// unchanged-reindex tests depend on.
    /// </remarks>
    private static float[] Derive(string text, int width)
    {
        float[] values = new float[width];

        for (int index = 0; index < text.Length; index++)
        {
            values[index % width] += (text[index] % 32) / 32f;
        }

        return values;
    }
}

/// <summary>
/// A search store that remembers what it was asked and answers from a list.
/// </summary>
/// <remarks>
/// It applies the workspace and agent filters itself, so the Application tests can
/// prove the retriever passes them through rather than inventing them. The
/// enabled, currency and profile filters are deliberately <em>not</em> modelled
/// here: they are SQL predicates, and a double asserting them would only prove
/// that the double agrees with itself. They are proved against real PostgreSQL in
/// the Infrastructure suite.
/// </remarks>
internal sealed class FakeKnowledgeSearchStore : IKnowledgeSearchStore
{
    private readonly List<StoredChunk> _chunks = [];

    /// <summary>How many times eligibility was checked.</summary>
    public int EligibilityChecks { get; private set; }

    /// <summary>How many times a vector search actually ran.</summary>
    public int Searches { get; private set; }

    public void Add(
        TenantId tenantId,
        AiAgentId agentId,
        Guid documentId,
        string title,
        int ordinal,
        string text,
        double distance) =>
        _chunks.Add(new StoredChunk(tenantId, agentId, documentId, title, ordinal, text, distance));

    /// <summary>Fills the store with chunks of a known size, closest first.</summary>
    public void AddRun(
        TenantId tenantId,
        AiAgentId agentId,
        int count,
        int textLength,
        string title = "Policy")
    {
        Guid documentId = Guid.CreateVersion7();

        for (int index = 0; index < count; index++)
        {
            Add(
                tenantId,
                agentId,
                documentId,
                title,
                index,
                new string((char)('a' + (index % 26)), textLength),
                0.1d * index);
        }
    }

    public Task<bool> HasEligibleChunksAsync(
        TenantId tenantId,
        AiAgentId aiAgentId,
        EmbeddingProfileId profile,
        CancellationToken cancellationToken = default)
    {
        EligibilityChecks++;

        return Task.FromResult(
            _chunks.Exists(chunk => chunk.TenantId == tenantId && chunk.AgentId == aiAgentId));
    }

    public Task<IReadOnlyList<KnowledgeMatch>> SearchAsync(
        TenantId tenantId,
        AiAgentId aiAgentId,
        EmbeddingProfileId profile,
        EmbeddingVector query,
        int limit,
        CancellationToken cancellationToken = default)
    {
        Searches++;

        IReadOnlyList<KnowledgeMatch> matches =
        [
            .. _chunks
                .Where(chunk => chunk.TenantId == tenantId && chunk.AgentId == aiAgentId)
                .OrderBy(chunk => chunk.Distance)
                .Take(limit)
                .Select(chunk => new KnowledgeMatch(
                    chunk.DocumentId,
                    chunk.Title,
                    chunk.Ordinal,
                    chunk.Text,
                    chunk.Distance)),
        ];

        return Task.FromResult(matches);
    }

    private sealed record StoredChunk(
        TenantId TenantId,
        AiAgentId AgentId,
        Guid DocumentId,
        string Title,
        int Ordinal,
        string Text,
        double Distance);
}

/// <summary>Small builders the knowledge suites share.</summary>
internal static class KnowledgeSamples
{
    public static KnowledgeDocument Document(
        TenantId tenantId,
        string title = "Returns policy",
        string content = "Goods may be returned within 30 days.",
        DateTimeOffset? at = null) =>
        KnowledgeDocument.Create(
            tenantId,
            KnowledgeTitle.Create(title),
            KnowledgeContent.Create(content),
            at ?? new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero));

    /// <summary>Text of a known length, made of whole words so it can be split.</summary>
    public static string Words(int approximateLength)
    {
        System.Text.StringBuilder builder = new(approximateLength + 16);

        int word = 0;

        while (builder.Length < approximateLength)
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(CultureInfo.InvariantCulture, $"word{word++}");
        }

        return builder.ToString();
    }
}
