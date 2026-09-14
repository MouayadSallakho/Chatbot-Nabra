using OmniAI.Application.Concurrency;
using OmniAI.Application.Knowledge;
using OmniAI.Application.Knowledge.IndexKnowledgeDocument;
using OmniAI.Application.Knowledge.Persistence;
using OmniAI.Domain;
using OmniAI.Domain.Knowledge;
using OmniAI.Application.Authorization;
using OmniAI.Application.Tests.Security;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;
using OmniAI.Testing;

namespace OmniAI.Application.Tests.Knowledge;

/// <summary>
/// Indexing: what it costs, what order it does things in, and what it leaves
/// behind when it fails.
/// </summary>
public sealed class IndexKnowledgeDocumentTests
{
    private static readonly DateTimeOffset Noon =
        new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Indexing_a_new_document_embeds_it_and_makes_it_ready()
    {
        KnowledgeIndexHarness harness = KnowledgeIndexHarness.Build();

        Versioned<KnowledgeIndexResult>? result = await harness.IndexAsync();

        Assert.NotNull(result);
        Assert.False(result.Value.AlreadyCurrent);
        Assert.Equal(1, harness.Embeddings.Calls);
        Assert.Equal("Ready", result.Value.Document.Readiness.ToString());
        Assert.Equal(result.Value.ChunkCount, harness.Index.LastChunks.Count);
    }

    [Fact]
    public async Task Re_indexing_unchanged_content_makes_no_provider_call_at_all()
    {
        // The most important cost property of the screen. Reindex is one button
        // and the temptation to press it again is constant; the second press has
        // to be free.
        KnowledgeIndexHarness harness = KnowledgeIndexHarness.Build();

        await harness.IndexAsync();

        int after = harness.Embeddings.Calls;

        Versioned<KnowledgeIndexResult>? again = await harness.IndexAsync();

        Assert.NotNull(again);
        Assert.True(again.Value.AlreadyCurrent);
        Assert.Equal(0, again.Value.EmbeddingCalls);
        Assert.Equal(after, harness.Embeddings.Calls);
        Assert.Equal(1, harness.Index.Replacements);
    }

    [Fact]
    public async Task Editing_the_content_makes_the_next_index_do_real_work()
    {
        KnowledgeIndexHarness harness = KnowledgeIndexHarness.Build();

        await harness.IndexAsync();

        // At the harness clock's own instant. The handler marks the document
        // indexed using that clock, and an aggregate refuses an update dated
        // before its last one -- which is the invariant, not a nuisance.
        harness.Document.Revise(
            harness.Document.Title,
            KnowledgeContent.Create("Completely different text."),
            Noon);

        Versioned<KnowledgeIndexResult>? again = await harness.IndexAsync();

        Assert.NotNull(again);
        Assert.False(again.Value.AlreadyCurrent);
        Assert.Equal(1, again.Value.EmbeddingCalls);
    }

    [Fact]
    public async Task A_long_document_is_embedded_in_bounded_batches()
    {
        // The profile allows four inputs per request. Nine chunks therefore cost
        // three requests, and no request is oversized.
        KnowledgeIndexHarness harness = KnowledgeIndexHarness.Build(
            content: KnowledgeSamples.Words(9_000),
            chunking: new KnowledgeChunkingPolicy
            {
                MaxChunkCharacters = 1_000,
                OverlapCharacters = 100,
                MaxChunks = 50,
            });

        Versioned<KnowledgeIndexResult>? result = await harness.IndexAsync();

        Assert.NotNull(result);
        Assert.True(result.Value.ChunkCount >= 9);
        Assert.Equal(harness.Embeddings.Calls, result.Value.EmbeddingCalls);
        Assert.All(
            harness.Embeddings.Batches,
            batch => Assert.True(batch.Count <= harness.Embeddings.Profile.MaxBatchSize));
    }

    [Fact]
    public async Task Chunks_are_written_in_order_with_their_own_vectors()
    {
        KnowledgeIndexHarness harness = KnowledgeIndexHarness.Build(
            content: KnowledgeSamples.Words(4_000),
            chunking: new KnowledgeChunkingPolicy
            {
                MaxChunkCharacters = 500,
                OverlapCharacters = 50,
                MaxChunks = 50,
            });

        await harness.IndexAsync();

        IReadOnlyList<KnowledgeChunkWrite> written = harness.Index.LastChunks;

        Assert.Equal(
            Enumerable.Range(0, written.Count),
            written.Select(chunk => chunk.Ordinal));

        Assert.All(written, chunk => Assert.Equal(
            harness.Embeddings.Profile.Dimensions,
            chunk.Embedding.Dimensions));
    }

    [Fact]
    public async Task An_unavailable_provider_leaves_the_content_saved_and_the_index_untouched()
    {
        // The exact behaviour a deployment with no credential shows in a browser:
        // a clear refusal, the text still there, and no half-written index.
        KnowledgeIndexHarness harness = KnowledgeIndexHarness.Build();

        harness.Embeddings.PretendUnavailable = true;

        await Assert.ThrowsAsync<EmbeddingUnavailableException>(() => harness.IndexAsync());

        Assert.Equal(0, harness.Index.Replacements);
        Assert.Equal(KnowledgeReadiness.NeedsIndex, harness.Document.ReadinessFor(harness.Profile));
        Assert.Null(harness.Document.IndexedAt);
        Assert.Equal("Goods may be returned in 30 days.", harness.Document.Content.Value);
    }

    [Fact]
    public async Task A_short_provider_response_is_refused_rather_than_paired_up_by_position()
    {
        // Zipping as far as the response goes would attach the wrong vector to the
        // wrong text for every chunk after the gap, and nothing downstream could
        // tell.
        KnowledgeIndexHarness harness = KnowledgeIndexHarness.Build(
            content: KnowledgeSamples.Words(3_000),
            chunking: new KnowledgeChunkingPolicy
            {
                MaxChunkCharacters = 500,
                OverlapCharacters = 50,
                MaxChunks = 50,
            });

        harness.Embeddings.PretendShortResponse = true;

        await Assert.ThrowsAsync<EmbeddingResponseInvalidException>(() => harness.IndexAsync());

        Assert.Equal(0, harness.Index.Replacements);
    }

    // A vector of the wrong width cannot be produced at this level at all:
    // EmbeddingVector validates on construction, so there is no way for a double
    // to hand the handler an invalid one. That is the design working, and it is
    // why the case is proved in the Infrastructure suite instead -- against the
    // canned transport, where a response carrying the wrong number of floats can
    // genuinely be fed in. See OpenAiEmbeddingTests.

    [Fact]
    public async Task A_stale_caller_is_refused_before_a_single_provider_call()
    {
        // Refused at the version check, having spent nothing. The expensive
        // variant -- the document moving while embeddings are being produced -- is
        // the store's job and is proved against real PostgreSQL.
        KnowledgeIndexHarness harness = KnowledgeIndexHarness.Build();

        await Assert.ThrowsAsync<KnowledgeDocumentVersionMismatchException>(
            () => harness.IndexAsync(ConcurrencyToken.From("a-stale-tag")));

        Assert.Equal(0, harness.Embeddings.Calls);
        Assert.Equal(0, harness.Index.Replacements);
    }

    [Fact]
    public async Task A_document_in_another_workspace_is_not_found()
    {
        KnowledgeIndexHarness harness = KnowledgeIndexHarness.Build();

        Assert.Null(await harness.IndexAsync(workspace: TenantId.New()));
        Assert.Equal(0, harness.Embeddings.Calls);
    }

    [Fact]
    public async Task A_document_too_large_to_chunk_is_refused_before_any_provider_call()
    {
        KnowledgeIndexHarness harness = KnowledgeIndexHarness.Build(
            content: KnowledgeSamples.Words(5_000),
            chunking: new KnowledgeChunkingPolicy
            {
                MaxChunkCharacters = 100,
                OverlapCharacters = 20,
                MaxChunks = 3,
            });

        await Assert.ThrowsAsync<DomainValidationException>(() => harness.IndexAsync());

        Assert.Equal(0, harness.Embeddings.Calls);
        Assert.Equal(0, harness.Index.Replacements);
    }
}

/// <summary>One indexing scenario, wired the way the composition root wires it.</summary>
internal sealed class KnowledgeIndexHarness
{
    private static readonly DateTimeOffset Noon =
        new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    public required TenantId Workspace { get; init; }

    public required KnowledgeDocument Document { get; init; }

    public required InMemoryKnowledgeDocumentRepository Documents { get; init; }

    public required RecordingKnowledgeIndexStore Index { get; init; }

    public required RecordingEmbeddingGenerator Embeddings { get; init; }

    public required IndexKnowledgeDocumentHandler Handler { get; init; }

    public EmbeddingProfileId Profile => Embeddings.Profile.Id;

    public static KnowledgeIndexHarness Build(
        string content = "Goods may be returned in 30 days.",
        KnowledgeChunkingPolicy? chunking = null)
    {
        TenantId workspace = TenantId.New();

        KnowledgeDocument document = KnowledgeSamples.Document(workspace, content: content, at: Noon);

        InMemoryKnowledgeDocumentRepository documents = new();
        documents.Seed(document);

        RecordingKnowledgeIndexStore index = new(documents);
        RecordingEmbeddingGenerator embeddings = new();

        return new KnowledgeIndexHarness
        {
            Workspace = workspace,
            Document = document,
            Documents = documents,
            Index = index,
            Embeddings = embeddings,
            Handler = new IndexKnowledgeDocumentHandler(
                documents,
                new InMemoryKnowledgeSourceRepository(documents),
                index,
                embeddings,
                new KnowledgeChunker(chunking),
                Owner(workspace),
                new FixedTimeProvider(Noon)),
        };
    }

    /// <summary>An Owner of the workspace under test, holding every permission.</summary>
    /// <remarks>
    /// A real authorizer over real role aggregates, never a permissive stub. A
    /// stub would let every one of these tests pass while proving nothing about
    /// whether the handler consults authorisation at all.
    /// </remarks>
    private static TenantAuthorizer Owner(TenantId workspace) =>
        TestAuthority.Owner(UserId.New(), workspace.Value);

    public Task<Versioned<KnowledgeIndexResult>?> IndexAsync(
        ConcurrencyToken? expected = null,
        TenantId? workspace = null) =>
        Handler.HandleAsync(new IndexKnowledgeDocumentCommand(
            (workspace ?? Workspace).Value,
            Document.Id.Value,
            expected ?? Documents.VersionOf(Document)));
}
