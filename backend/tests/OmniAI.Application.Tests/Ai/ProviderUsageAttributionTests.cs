using OmniAI.Application.Ai.Usage;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Knowledge;
using OmniAI.Application.Knowledge.IndexKnowledgeDocument;
using OmniAI.Application.Tests.Knowledge;
using OmniAI.Domain.Ai;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Tests.Ai;

/// <summary>
/// Which flows spend money, whose money it is, and what for.
/// </summary>
/// <remarks>
/// <para>
/// One accounted operation is one provider request — the adapters prove that
/// against a canned transport. So at this level the question is the other half:
/// <em>how many provider requests does each flow actually make, and what does it
/// say they are for?</em> The two together are the whole chain from a customer's
/// message to a line on an invoice.
/// </para>
/// <para>
/// The negative cases matter as much as the positive ones. A flow that refuses
/// before dispatch must spend nothing, and "nothing" has to mean no provider call
/// rather than a call that was cheap.
/// </para>
/// </remarks>
public sealed class ProviderUsageAttributionTests
{
    private static readonly TenantId Workspace = TenantId.New();

    private static readonly AiAgentId Agent = AiAgentId.New();

    // ---------------------------------------------------------- RAG retrieval

    [Fact]
    public async Task An_agent_with_no_eligible_knowledge_never_pays_to_embed_a_question()
    {
        // The rule Phase 17 established and Phase 23 must not disturb: no
        // eligible knowledge, no query embedding, and therefore no accounted
        // operation at all.
        (KnowledgeRetriever retriever, FakeKnowledgeSearchStore store,
            RecordingEmbeddingGenerator embeddings) = Retrieval();

        await retriever.RetrieveAsync(Workspace, Agent, "Do you deliver on Sundays?");

        Assert.Equal(0, embeddings.Calls);
        Assert.Empty(embeddings.Usages);

        _ = store;
    }

    [Fact]
    public async Task A_retrieval_embedding_is_attributed_to_the_asking_workspace()
    {
        (KnowledgeRetriever retriever, FakeKnowledgeSearchStore store,
            RecordingEmbeddingGenerator embeddings) = Retrieval();

        store.AddRun(Workspace, Agent, count: 3, textLength: 200);

        await retriever.RetrieveAsync(Workspace, Agent, "Do you deliver on Sundays?");

        // Exactly one provider request for the question, and it says whose
        // question it was.
        Assert.Equal(1, embeddings.Calls);

        AiUsageContext attribution = Assert.Single(embeddings.Usages);

        Assert.Equal(Workspace, attribution.TenantId);
        Assert.Equal(AiUsagePurpose.ConversationRetrievalEmbedding, attribution.Purpose);
    }

    [Fact]
    public async Task A_blank_question_is_refused_before_anything_is_dispatched()
    {
        (KnowledgeRetriever retriever, FakeKnowledgeSearchStore store,
            RecordingEmbeddingGenerator embeddings) = Retrieval();

        store.AddRun(Workspace, Agent, count: 3, textLength: 200);

        Assert.Empty(await retriever.RetrieveAsync(Workspace, Agent, "   "));

        Assert.Equal(0, embeddings.Calls);
    }

    // -------------------------------------------------------- index batching

    /// <remarks>
    /// <strong>The batch rule, at the level that decides it.</strong> Accounting
    /// must follow provider requests, not chunks: a document split into many
    /// pieces and sent in a few batches is a few operations, and reporting one per
    /// chunk would overstate the call count by an order of magnitude.
    /// </remarks>
    [Fact]
    public async Task Indexing_pays_once_per_batch_and_not_once_per_chunk()
    {
        // Enough text to split into many chunks, against a batch size of four:
        // several provider requests are genuinely needed.
        KnowledgeIndexHarness world = KnowledgeIndexHarness.Build(
            content: KnowledgeSamples.Words(9_000),
            chunking: new KnowledgeChunkingPolicy
            {
                MaxChunkCharacters = 1_000,
                OverlapCharacters = 100,
                MaxChunks = 50,
            });

        Versioned<KnowledgeIndexResult>? result = await world.IndexAsync();

        Assert.NotNull(result);

        Assert.True(result.Value.ChunkCount > world.Embeddings.Calls,
            "The document must split into more chunks than there were provider requests, or "
            + "this test is not exercising batching at all.");

        // Calls, not chunks. And the handler's own report agrees with what the
        // provider double actually saw.
        Assert.Equal(world.Embeddings.Calls, result.Value.EmbeddingCalls);

        Assert.All(world.Embeddings.Usages, usage =>
        {
            Assert.Equal(world.Workspace, usage.TenantId);
            Assert.Equal(AiUsagePurpose.KnowledgeIndexEmbedding, usage.Purpose);
        });
    }

    [Fact]
    public async Task Indexing_never_asks_a_text_generator_for_anything()
    {
        KnowledgeIndexHarness world = KnowledgeIndexHarness.Build();

        await world.IndexAsync();

        // Indexing is an embedding flow. A generation charge appearing here would
        // mean something had started writing prose nobody asked for.
        Assert.All(world.Embeddings.Usages, usage =>
            Assert.Equal(AiUsagePurpose.KnowledgeIndexEmbedding, usage.Purpose));
    }

    [Fact]
    public async Task Re_indexing_unchanged_text_makes_no_provider_request()
    {
        KnowledgeIndexHarness world = KnowledgeIndexHarness.Build();

        await world.IndexAsync();

        int afterFirst = world.Embeddings.Calls;

        Versioned<KnowledgeIndexResult>? again = await world.IndexAsync();

        Assert.NotNull(again);
        Assert.True(again.Value.AlreadyCurrent);

        // Nothing dispatched means nothing accounted. "Already up to date" has to
        // cost nothing, or the honesty of the message evaporates.
        Assert.Equal(afterFirst, world.Embeddings.Calls);
        Assert.Equal(0, again.Value.EmbeddingCalls);
    }

    private static (KnowledgeRetriever Retriever, FakeKnowledgeSearchStore Store,
        RecordingEmbeddingGenerator Embeddings) Retrieval()
    {
        FakeKnowledgeSearchStore store = new();
        RecordingEmbeddingGenerator embeddings = new();

        return (new KnowledgeRetriever(store, embeddings, new KnowledgeRetrievalPolicy()),
            store,
            embeddings);
    }

}
