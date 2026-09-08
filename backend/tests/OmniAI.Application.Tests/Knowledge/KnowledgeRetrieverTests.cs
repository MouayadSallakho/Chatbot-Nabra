using OmniAI.Application.Knowledge;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Tests.Knowledge;

/// <summary>
/// Retrieval: what it costs, what it keeps, and what it refuses to widen.
/// </summary>
public sealed class KnowledgeRetrieverTests
{
    private static readonly TenantId Workspace = TenantId.New();
    private static readonly AiAgentId Agent = AiAgentId.New();

    private static (KnowledgeRetriever Retriever, FakeKnowledgeSearchStore Store,
        RecordingEmbeddingGenerator Embeddings) Build(KnowledgeRetrievalPolicy? policy = null)
    {
        FakeKnowledgeSearchStore store = new();
        RecordingEmbeddingGenerator embeddings = new();

        return (
            new KnowledgeRetriever(store, embeddings, policy ?? new KnowledgeRetrievalPolicy()),
            store,
            embeddings);
    }

    [Fact]
    public async Task An_agent_with_no_knowledge_costs_no_embedding_call()
    {
        // The most important cost property in the feature. Every agent that
        // existed before knowledge did has none assigned, and every one of them
        // must keep answering at exactly the price it did before.
        (KnowledgeRetriever retriever, FakeKnowledgeSearchStore store,
            RecordingEmbeddingGenerator embeddings) = Build();

        IReadOnlyList<KnowledgeExcerpt> excerpts =
            await retriever.RetrieveAsync(Workspace, Agent, "Do you deliver?");

        Assert.Empty(excerpts);
        Assert.Equal(0, embeddings.Calls);
        Assert.Equal(1, store.EligibilityChecks);
        Assert.Equal(0, store.Searches);
    }

    [Fact]
    public async Task A_blank_question_costs_nothing_at_all()
    {
        (KnowledgeRetriever retriever, FakeKnowledgeSearchStore store,
            RecordingEmbeddingGenerator embeddings) = Build();

        Assert.Empty(await retriever.RetrieveAsync(Workspace, Agent, "   "));
        Assert.Equal(0, embeddings.Calls);
        Assert.Equal(0, store.EligibilityChecks);
    }

    [Fact]
    public async Task An_agent_with_knowledge_embeds_the_question_once()
    {
        (KnowledgeRetriever retriever, FakeKnowledgeSearchStore store,
            RecordingEmbeddingGenerator embeddings) = Build();

        store.AddRun(Workspace, Agent, count: 3, textLength: 50);

        IReadOnlyList<KnowledgeExcerpt> excerpts =
            await retriever.RetrieveAsync(Workspace, Agent, "Do you deliver?");

        Assert.Equal(3, excerpts.Count);
        Assert.Equal(1, embeddings.Calls);
        Assert.Equal(["Do you deliver?"], embeddings.Inputs);
    }

    [Fact]
    public async Task Only_the_top_k_are_asked_for()
    {
        (KnowledgeRetriever retriever, FakeKnowledgeSearchStore store, _) =
            Build(new KnowledgeRetrievalPolicy { TopK = 2 });

        store.AddRun(Workspace, Agent, count: 10, textLength: 20);

        Assert.Equal(2, (await retriever.RetrieveAsync(Workspace, Agent, "Anything?")).Count);
    }

    [Fact]
    public async Task Results_come_back_closest_first()
    {
        (KnowledgeRetriever retriever, FakeKnowledgeSearchStore store, _) = Build();

        store.AddRun(Workspace, Agent, count: 4, textLength: 20);

        IReadOnlyList<KnowledgeExcerpt> excerpts =
            await retriever.RetrieveAsync(Workspace, Agent, "Anything?");

        Assert.Equal(
            excerpts.Select(excerpt => excerpt.Similarity).OrderByDescending(value => value),
            excerpts.Select(excerpt => excerpt.Similarity));
    }

    [Fact]
    public async Task Similarity_is_one_minus_the_distance()
    {
        (KnowledgeRetriever retriever, FakeKnowledgeSearchStore store, _) = Build();

        store.Add(Workspace, Agent, Guid.CreateVersion7(), "Policy", 0, "Text.", distance: 0.25d);

        KnowledgeExcerpt only = Assert.Single(
            await retriever.RetrieveAsync(Workspace, Agent, "Anything?"));

        Assert.Equal(0.75d, only.Similarity, precision: 10);
    }

    [Fact]
    public async Task Whole_chunks_are_dropped_when_the_budget_is_reached()
    {
        // Dropped, never shortened. Half a passage is worse than no passage,
        // because a model reading a sentence that stops mid-clause finishes it
        // itself.
        (KnowledgeRetriever retriever, FakeKnowledgeSearchStore store, _) =
            Build(new KnowledgeRetrievalPolicy { TopK = 6, MaxContextCharacters = 250 });

        store.AddRun(Workspace, Agent, count: 6, textLength: 100);

        IReadOnlyList<KnowledgeExcerpt> excerpts =
            await retriever.RetrieveAsync(Workspace, Agent, "Anything?");

        Assert.Equal(2, excerpts.Count);
        Assert.All(excerpts, excerpt => Assert.Equal(100, excerpt.Text.Length));
        Assert.True(excerpts.Sum(excerpt => excerpt.Text.Length) <= 250);
    }

    [Fact]
    public async Task The_ones_kept_are_the_closest_ones()
    {
        (KnowledgeRetriever retriever, FakeKnowledgeSearchStore store, _) =
            Build(new KnowledgeRetrievalPolicy { TopK = 6, MaxContextCharacters = 120 });

        store.AddRun(Workspace, Agent, count: 6, textLength: 100);

        KnowledgeExcerpt only = Assert.Single(
            await retriever.RetrieveAsync(Workspace, Agent, "Anything?"));

        Assert.Equal(0, only.Ordinal);
    }

    [Fact]
    public async Task Another_workspace_knowledge_is_never_returned()
    {
        // The store applies the workspace filter, and this proves the retriever
        // passes it through rather than searching across everything and trimming.
        (KnowledgeRetriever retriever, FakeKnowledgeSearchStore store, _) = Build();

        store.AddRun(TenantId.New(), Agent, count: 3, textLength: 20);

        Assert.Empty(await retriever.RetrieveAsync(Workspace, Agent, "Anything?"));
    }

    [Fact]
    public async Task Another_agent_knowledge_is_never_returned()
    {
        (KnowledgeRetriever retriever, FakeKnowledgeSearchStore store, _) = Build();

        store.AddRun(Workspace, AiAgentId.New(), count: 3, textLength: 20);

        Assert.Empty(await retriever.RetrieveAsync(Workspace, Agent, "Anything?"));
    }

    [Fact]
    public async Task A_failed_embedding_call_is_reported_rather_than_swallowed()
    {
        // Answering with an empty result would look identical to "nothing
        // matched", and an agent would silently answer without the knowledge it
        // was supposed to have.
        (KnowledgeRetriever retriever, FakeKnowledgeSearchStore store,
            RecordingEmbeddingGenerator embeddings) = Build();

        store.AddRun(Workspace, Agent, count: 1, textLength: 20);
        embeddings.PretendUnavailable = true;

        await Assert.ThrowsAsync<EmbeddingUnavailableException>(
            () => retriever.RetrieveAsync(Workspace, Agent, "Anything?"));

        Assert.Equal(0, store.Searches);
    }

    [Fact]
    public async Task A_question_longer_than_the_provider_accepts_is_shortened_before_it_is_sent()
    {
        (KnowledgeRetriever retriever, FakeKnowledgeSearchStore store,
            RecordingEmbeddingGenerator embeddings) = Build();

        store.AddRun(Workspace, Agent, count: 1, textLength: 20);

        await retriever.RetrieveAsync(Workspace, Agent, new string('q', 20_000));

        Assert.Equal(
            embeddings.Profile.MaxInputCharacters,
            Assert.Single(embeddings.Inputs).Length);
    }
}
