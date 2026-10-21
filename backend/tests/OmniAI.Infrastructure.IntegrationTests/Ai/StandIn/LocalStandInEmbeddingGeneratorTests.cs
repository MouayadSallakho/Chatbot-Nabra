using OmniAI.Application.Ai.Usage;
using OmniAI.Application.Knowledge;
using OmniAI.Domain.Ai;
using OmniAI.Domain.Tenants;
using OmniAI.Infrastructure.Ai.StandIn;
using OmniAI.Infrastructure.Ai.Usage;
using OmniAI.Infrastructure.Knowledge;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Ai.StandIn;

/// <summary>
/// The local AI stand-in's embeddings (Phase 37.2): hashed word counts,
/// normalised, at the width of the vector column.
/// </summary>
public sealed class LocalStandInEmbeddingGeneratorTests
{
    private static readonly AiUsageContext Usage =
        AiUsageContext.For(TenantId.New(), AiUsagePurpose.KnowledgeIndexEmbedding);

    private static (LocalStandInEmbeddingGenerator Generator, RecordingUsageLedger Ledger) Create()
    {
        RecordingUsageLedger ledger = new(
            AiProviderPricingCatalog.Create(null, includeLocalStandIn: true));

        EmbeddingProfile profile = EmbeddingProfile.Create(
            LocalAiStandInOptions.ProfileId,
            EmbeddingOptions.Dimensions,
            EmbeddingOptions.DefaultMaxBatchSize,
            EmbeddingOptions.DefaultMaxInputCharacters);

        return (new LocalStandInEmbeddingGenerator(profile, ledger), ledger);
    }

    private static async Task<float[][]> EmbedAsync(params string[] texts)
    {
        (LocalStandInEmbeddingGenerator generator, _) = Create();

        IReadOnlyList<EmbeddingVector> vectors = await generator.GenerateAsync(texts, Usage);

        return [.. vectors.Select(vector => vector.ToArray())];
    }

    private static double Cosine(float[] left, float[] right)
    {
        double dot = 0;

        for (int index = 0; index < left.Length; index++)
        {
            dot += left[index] * right[index];
        }

        // Both are unit vectors, so the dot product is the cosine.
        return dot;
    }

    /// <summary>The passages ordered as retrieval would order them for a question.</summary>
    private static async Task<string[]> RankAsync(string question, params string[] passages)
    {
        float[][] vectors = await EmbedAsync([question, .. passages]);

        return
        [
            .. passages
                .Select((passage, index) => (passage, score: Cosine(vectors[0], vectors[index + 1])))
                .OrderByDescending(pair => pair.score)
                .Select(pair => pair.passage),
        ];
    }

    [Fact]
    public async Task Every_vector_has_the_column_width_and_unit_length()
    {
        float[][] vectors = await EmbedAsync("Refunds take five days.", "سياسة الإرجاع", "?!");

        Assert.All(vectors, vector =>
        {
            Assert.Equal(EmbeddingOptions.Dimensions, vector.Length);
            Assert.Equal(1.0, Math.Sqrt(vector.Sum(value => (double)value * value)), precision: 5);
        });
    }

    [Fact]
    public void The_profile_is_the_stand_in_s_own()
    {
        (LocalStandInEmbeddingGenerator generator, _) = Create();

        Assert.Equal("dev:standin:1536", generator.Profile.Id.Value);
        Assert.NotEqual(new EmbeddingOptions().ProfileId, generator.Profile.Id);
    }

    [Fact]
    public async Task The_same_text_always_gets_the_same_vector()
    {
        float[][] first = await EmbedAsync("Opening hours on Friday");
        float[][] second = await EmbedAsync("Opening hours on Friday");

        Assert.Equal(first[0], second[0]);
    }

    [Fact]
    public async Task An_english_question_ranks_the_passage_sharing_its_words_first()
    {
        string[] ranked = await RankAsync(
            "How long does a refund take?",
            "Orders ship the next working day from our warehouse.",
            "A refund takes five working days to reach your card.",
            "Our shop opens at nine on weekdays.");

        Assert.Equal("A refund takes five working days to reach your card.", ranked[0]);
    }

    [Fact]
    public async Task An_arabic_question_ranks_the_passage_sharing_its_words_first()
    {
        // «الاسترداد» in the question and «استرداد» in the passage are one word
        // once the article is dropped.
        string[] ranked = await RankAsync(
            "كم يستغرق الاسترداد إلى البطاقة؟",
            "نشحن الطلبات في يوم العمل التالي.",
            "يستغرق استرداد المبلغ إلى البطاقة خمسة أيام عمل.",
            "يفتح المتجر في التاسعة صباحاً.");

        Assert.Equal("يستغرق استرداد المبلغ إلى البطاقة خمسة أيام عمل.", ranked[0]);
    }

    [Fact]
    public async Task Arabic_diacritics_and_tatweel_do_not_change_a_word()
    {
        float[][] vectors = await EmbedAsync("الشحن", "الشَّحْن", "الشــحن", "شحن");

        Assert.Equal(vectors[0], vectors[1]);
        Assert.Equal(vectors[0], vectors[2]);
        Assert.Equal(vectors[0], vectors[3]);
    }

    [Fact]
    public async Task A_batch_is_one_ledger_row_at_zero_cost_with_word_counts()
    {
        (LocalStandInEmbeddingGenerator generator, RecordingUsageLedger ledger) = Create();

        IReadOnlyList<EmbeddingVector> vectors =
            await generator.GenerateAsync(["one two three", "four five"], Usage);

        Assert.Equal(2, vectors.Count);

        AiProviderUsage row = ledger.Only;

        Assert.Equal(AiProvider.DevelopmentStandIn, row.Provider);
        Assert.Equal(AiOperationType.Embedding, row.OperationType);
        Assert.Equal(AiUsagePurpose.KnowledgeIndexEmbedding, row.Purpose);
        Assert.Equal(AiOperationStatus.Succeeded, row.Status);
        Assert.Equal(5, row.Usage.InputTokens);
        Assert.Equal(0m, row.EstimatedCost);
    }

    [Fact]
    public async Task An_oversized_batch_is_refused_before_anything_is_recorded()
    {
        (LocalStandInEmbeddingGenerator generator, RecordingUsageLedger ledger) = Create();

        string[] inputs = [.. Enumerable.Repeat("word", EmbeddingOptions.DefaultMaxBatchSize + 1)];

        await Assert.ThrowsAsync<ArgumentException>(() => generator.GenerateAsync(inputs, Usage));

        Assert.Equal(0, ledger.Count);
    }

    [Fact]
    public async Task An_empty_batch_is_answered_without_a_ledger_row()
    {
        (LocalStandInEmbeddingGenerator generator, RecordingUsageLedger ledger) = Create();

        Assert.Empty(await generator.GenerateAsync([], Usage));
        Assert.Equal(0, ledger.Count);
    }
}
