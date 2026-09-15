using OmniAI.Application.Authorization;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Knowledge.Persistence;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Knowledge;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Ai;
using OmniAI.Application.Ai.Usage;

namespace OmniAI.Application.Knowledge.IndexKnowledgeDocument;

public sealed record IndexKnowledgeDocumentCommand(
    Guid TenantId,
    Guid KnowledgeDocumentId,
    ConcurrencyToken Expected);

/// <summary>
/// What an index attempt did.
/// </summary>
/// <remarks>
/// <paramref name="EmbeddingCalls"/> is the number of provider requests this
/// attempt actually made. It is reported rather than inferred so an operator can
/// see for themselves that re-indexing unchanged text cost nothing.
/// </remarks>
public sealed record KnowledgeIndexResult(
    KnowledgeDocumentView Document,
    int ChunkCount,
    int EmbeddingCalls,
    bool AlreadyCurrent);

/// <summary>
/// Rebuilds the vectors behind one document.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The order is the design, and each step is where it is for a reason.</strong>
/// </para>
/// <list type="number">
/// <item>
/// The document is read and its version checked before anything else. A caller
/// working from a stale view is refused here, having spent nothing.
/// </item>
/// <item>
/// If the content is already indexed under the profile in use, this returns
/// having made <strong>zero</strong> provider calls. Pressing Reindex twice
/// costs what it should: nothing the second time. This is the single most
/// important cost property in the feature, because a re-index is one button and
/// the temptation to press it again is constant.
/// </item>
/// <item>
/// The text is chunked deterministically. No provider is involved, so the same
/// document always produces the same pieces.
/// </item>
/// <item>
/// The embeddings are produced <strong>outside any database transaction</strong>,
/// in batches the profile allows. A transaction held open across a call to a
/// third party is how a slow provider becomes a database outage: the locks are
/// held for as long as the provider takes to answer, and under load the
/// connection pool empties.
/// </item>
/// <item>
/// Every vector is validated as it is built. A wrong count, a wrong width or a
/// value that is not a finite number stops the attempt before anything is
/// written.
/// </item>
/// <item>
/// Only then, in one short transaction, is the whole chunk set replaced and the
/// document marked as indexed -- with the version re-checked, because the
/// document may have been edited while the embeddings were being made.
/// </item>
/// </list>
/// <para>
/// <strong>Every failure leaves the document exactly as it was.</strong> A
/// provider that is unavailable, times out or answers with nonsense produces a
/// status code and nothing else: the content is still saved, the document is
/// still reported as needing an index, and no chunk of a half-finished set is
/// left behind for retrieval to find.
/// </para>
/// </remarks>
public sealed class IndexKnowledgeDocumentHandler(
    IKnowledgeDocumentRepository documents,
    IKnowledgeSourceRepository sources,
    IKnowledgeIndexStore index,
    ITextEmbeddingGenerator embeddings,
    KnowledgeChunker chunker,
    TenantAuthorizer authorizer,
    TimeProvider clock)
{
    private readonly IKnowledgeDocumentRepository _documents =
        documents ?? throw new ArgumentNullException(nameof(documents));

    private readonly IKnowledgeSourceRepository _sources =
        sources ?? throw new ArgumentNullException(nameof(sources));

    private readonly IKnowledgeIndexStore _index =
        index ?? throw new ArgumentNullException(nameof(index));

    private readonly ITextEmbeddingGenerator _embeddings =
        embeddings ?? throw new ArgumentNullException(nameof(embeddings));

    private readonly KnowledgeChunker _chunker =
        chunker ?? throw new ArgumentNullException(nameof(chunker));

    private readonly TenantAuthorizer _authorizer =
        authorizer ?? throw new ArgumentNullException(nameof(authorizer));

    private readonly TimeProvider _clock =
        clock ?? throw new ArgumentNullException(nameof(clock));

    public async Task<Versioned<KnowledgeIndexResult>?> HandleAsync(
        IndexKnowledgeDocumentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        TenantId tenantId = TenantId.From(command.TenantId);
        KnowledgeDocumentId documentId = KnowledgeDocumentId.From(command.KnowledgeDocumentId);

        if (!await _authorizer
            .RequireAsync(tenantId, PlatformPermissions.KnowledgeManage, cancellationToken)
            .ConfigureAwait(false))
        {
            return null;
        }

        Versioned<KnowledgeDocument>? stored = await _documents
            .FindAsync(tenantId, documentId, cancellationToken)
            .ConfigureAwait(false);

        if (stored is null)
        {
            return null;
        }

        KnowledgeConcurrency.RequireExpectedVersion(stored, command.Expected);

        KnowledgeDocument document = stored.Value;
        EmbeddingProfile profile = _embeddings.Profile;

        // Read before any provider work, so both the nothing-to-do answer and
        // the indexed answer describe the document the same way.
        KnowledgeSource? source = await _sources
            .FindAsync(tenantId, documentId, cancellationToken)
            .ConfigureAwait(false);

        if (document.IsIndexCurrent(profile.Id))
        {
            // Nothing to do, and nothing spent. Reported honestly rather than
            // pretended to be work, so the screen can say so.
            return new Versioned<KnowledgeIndexResult>(
                new KnowledgeIndexResult(
                    KnowledgeDocumentView.From(document, profile.Id, source),
                    ChunkCount: 0,
                    EmbeddingCalls: 0,
                    AlreadyCurrent: true),
                stored.Version);
        }

        IReadOnlyList<KnowledgeChunk> chunks = _chunker.Split(document.Content.Value);

        if (chunks.Count == 0)
        {
            // Unreachable: content is never blank. Raised rather than papered
            // over, because writing an empty index while marking the document
            // ready would produce a document that answers nothing and says it can.
            throw new EmbeddingResponseInvalidException(
                "This document produced no text to index. Nothing was written.");
        }

        (IReadOnlyList<KnowledgeChunkWrite> writes, int calls) =
            await EmbedAsync(chunks, profile, tenantId, cancellationToken)
                .ConfigureAwait(false);

        document.MarkIndexed(profile.Id, _clock.GetUtcNow());

        ConcurrencyToken version = await _index
            .ReplaceIndexAsync(document, command.Expected, writes, cancellationToken)
            .ConfigureAwait(false);

        return new Versioned<KnowledgeIndexResult>(
            new KnowledgeIndexResult(
                KnowledgeDocumentView.From(document, profile.Id, source),
                writes.Count,
                calls,
                AlreadyCurrent: false),
            version);
    }

    /// <summary>Embeds every chunk, in batches the profile allows.</summary>
    /// <remarks>
    /// Sequential rather than parallel. Firing every batch at once would multiply
    /// the load this platform puts on a shared provider quota by the number of
    /// batches, and the first thing that happens when a quota is exceeded is that
    /// somebody else's live conversation stops being answered.
    /// </remarks>
    private async Task<(IReadOnlyList<KnowledgeChunkWrite> Writes, int Calls)> EmbedAsync(
        IReadOnlyList<KnowledgeChunk> chunks,
        EmbeddingProfile profile,
        TenantId tenantId,
        CancellationToken cancellationToken)
    {
        List<KnowledgeChunkWrite> writes = new(chunks.Count);
        int calls = 0;

        for (int start = 0; start < chunks.Count; start += profile.MaxBatchSize)
        {
            int size = Math.Min(profile.MaxBatchSize, chunks.Count - start);

            string[] inputs = new string[size];

            for (int offset = 0; offset < size; offset++)
            {
                inputs[offset] = chunks[start + offset].Text;
            }

            calls++;

            // One batch, one provider request, and therefore one accounted
            // operation -- not one per chunk.
            IReadOnlyList<EmbeddingVector> vectors = await _embeddings
                .GenerateAsync(
                    inputs,
                    AiUsageContext.For(tenantId, AiUsagePurpose.KnowledgeIndexEmbedding),
                    cancellationToken)
                .ConfigureAwait(false);

            if (vectors is null || vectors.Count != size)
            {
                throw new EmbeddingResponseInvalidException(
                    "The embedding provider returned a different number of vectors than the "
                    + "number of pieces it was given. Nothing was indexed: pairing them up by "
                    + "position would attach the wrong vector to the wrong text.");
            }

            for (int offset = 0; offset < size; offset++)
            {
                KnowledgeChunk chunk = chunks[start + offset];

                writes.Add(new KnowledgeChunkWrite(
                    chunk.Ordinal,
                    chunk.Text,
                    vectors[offset] ?? throw new EmbeddingResponseInvalidException()));
            }
        }

        return (writes, calls);
    }
}
