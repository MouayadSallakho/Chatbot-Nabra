using OmniAI.Application.Authorization;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Knowledge.Persistence;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Knowledge;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Ai;
using OmniAI.Application.Ai.Usage;
using OmniAI.Application.Plans;

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
/// <para>
/// <strong>Phase 38.7a.</strong> The HTTP route runs an attempt through
/// <see cref="HandleDetachedAsync"/>, so it does not depend on the browser that
/// asked for it (BA-10); and an attempt's start and settlement are announced to
/// the workspace's knowledge viewers, structurally and quietly (BA-11).
/// </para>
/// </remarks>
public sealed class IndexKnowledgeDocumentHandler(
    IKnowledgeDocumentRepository documents,
    IKnowledgeSourceRepository sources,
    IKnowledgeIndexStore index,
    ITextEmbeddingGenerator embeddings,
    KnowledgeChunker chunker,
    TenantAuthorizer authorizer,
    TimeProvider clock,
    IKnowledgeIndexAttemptStore? attempts = null,
    IKnowledgeRealtimeNotifier? realtime = null,
    KnowledgeIndexingLimits? limits = null)
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

    private readonly IKnowledgeIndexAttemptStore? _attempts = attempts;

    private readonly IKnowledgeRealtimeNotifier? _realtime = realtime;

    private readonly KnowledgeIndexingLimits _limits = limits ?? KnowledgeIndexingLimits.Default;

    /// <summary>Runs one attempt for a caller whose cancellation is the person's own.</summary>
    public Task<Versioned<KnowledgeIndexResult>?> HandleAsync(
        IndexKnowledgeDocumentCommand command,
        CancellationToken cancellationToken = default) =>
        RunAsync(command, deadline: null, cancellationToken);

    /// <summary>
    /// Runs one attempt to its end, whatever happens to the request that asked
    /// for it (Phase 38.7a, BA-10).
    /// </summary>
    /// <param name="stopping">The host's shutdown token, never a request's.</param>
    /// <remarks>
    /// <para>
    /// Bounded by <see cref="KnowledgeIndexingLimits.AttemptTimeout"/> instead of
    /// by the browser. Running past it settles the attempt Failed with
    /// <see cref="KnowledgeIndexAttemptFailures.TimedOut"/> and the alert: the
    /// server stopped it, not the person. A host shutting down settles it Unknown
    /// with <see cref="KnowledgeIndexAttemptFailures.Interrupted"/> and the alert,
    /// exactly what the stale-attempt reconciler records for a process that
    /// stopped mid-attempt.
    /// </para>
    /// <para>
    /// Everything else is unchanged: the version check, the single flight, the
    /// attempt record, its alert, and the answer to a caller who is still there.
    /// </para>
    /// </remarks>
    public async Task<Versioned<KnowledgeIndexResult>?> HandleDetachedAsync(
        IndexKnowledgeDocumentCommand command,
        CancellationToken stopping)
    {
        using CancellationTokenSource deadline = new(_limits.AttemptTimeout, _clock);

        try
        {
            return await RunAsync(command, deadline, stopping).ConfigureAwait(false);
        }
        catch (OperationCanceledException failure)
            when (deadline.IsCancellationRequested && !stopping.IsCancellationRequested)
        {
            // Already settled as timed_out. A caller still waiting is told what a
            // provider timeout tells it (504), not that the server crashed.
            throw new EmbeddingTimeoutException(
                "Indexing took longer than this server allows for one attempt. Nothing was "
                + "indexed; the document still needs an index.",
                failure);
        }
    }

    /// <param name="deadline">
    /// The server's own limit, for a detached attempt; <see langword="null"/>
    /// when the caller's token is the person's cancellation.
    /// </param>
    private async Task<Versioned<KnowledgeIndexResult>?> RunAsync(
        IndexKnowledgeDocumentCommand command,
        CancellationTokenSource? deadline,
        CancellationToken caller)
    {
        ArgumentNullException.ThrowIfNull(command);

        using CancellationTokenSource? bounded = deadline is null
            ? null
            : CancellationTokenSource.CreateLinkedTokenSource(caller, deadline.Token);

        CancellationToken cancellationToken = bounded?.Token ?? caller;

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

        // A real attempt: embedding work is about to be sent. Its durable record
        // is committed first, so a process that dies from here on leaves a Started
        // row the stale-attempt reconciler can settle and alert on exactly once.
        KnowledgeIndexAttempt? attempt = null;

        if (_attempts is not null)
        {
            attempt = KnowledgeIndexAttempt.Start(tenantId, documentId, _clock.GetUtcNow());

            await _attempts.StartAsync(attempt, cancellationToken).ConfigureAwait(false);

            // Started, and the single flight is held: knowledge viewers re-read.
            await _realtime.AnnounceQuietlyAsync(tenantId, documentId, CancellationToken.None)
                .ConfigureAwait(false);
        }

        IReadOnlyList<KnowledgeChunkWrite> writes;
        int calls;
        ConcurrencyToken version;

        try
        {
            if (attempt is not null)
            {
                // Phase 35: holding the single-flight slot, look again. An attempt
                // that ran while this request was on its way has, by the time this
                // one could start, already written its index (it settles only
                // after writing), so the version read above is stale and nothing
                // is sent. Refused as a changed document: that is what happened.
                Versioned<KnowledgeDocument>? current = await _documents
                    .FindAsync(tenantId, documentId, cancellationToken)
                    .ConfigureAwait(false)
                    ?? throw new KnowledgeDocumentVersionMismatchException();

                KnowledgeConcurrency.RequireExpectedVersion(current, stored.Version);
            }

            (writes, calls) = await EmbedAsync(chunks, profile, tenantId, cancellationToken)
                .ConfigureAwait(false);

            document.MarkIndexed(profile.Id, _clock.GetUtcNow());

            version = await _index
                .ReplaceIndexAsync(document, command.Expected, writes, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception failure) when (attempt is not null)
        {
            // Settled with a stable code and, for a genuine indexing failure, the
            // operational alert in the same statement. The caller still gets the
            // original error: recording the outcome must never replace it.
            await SettleFailedAsync(attempt, failure, deadline, caller).ConfigureAwait(false);

            await _realtime.AnnounceQuietlyAsync(tenantId, documentId, CancellationToken.None)
                .ConfigureAwait(false);

            throw;
        }

        if (attempt is not null)
        {
            // Not cancellable: the index is written, and the record must say so.
            await _attempts!.SucceedAsync(attempt.Id, _clock.GetUtcNow(), CancellationToken.None).ConfigureAwait(false);

            await _realtime.AnnounceQuietlyAsync(tenantId, documentId, CancellationToken.None)
                .ConfigureAwait(false);
        }

        return new Versioned<KnowledgeIndexResult>(
            new KnowledgeIndexResult(
                KnowledgeDocumentView.From(document, profile.Id, source),
                writes.Count,
                calls,
                AlreadyCurrent: false),
            version);
    }

    /// <summary>
    /// Settles an attempt that did not succeed, and decides whether it is an
    /// operational alert.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Provider failures and unexpected indexing errors alert the workspace's
    /// knowledge managers. A document edited meanwhile, a quota refusal (which has
    /// its own usage alert) and the requester's own cancellation do not: each is
    /// already visible to the person concerned, and alerting on them would be noise.
    /// </para>
    /// <para>
    /// Recording is best effort only in the sense that its own failure must not
    /// replace the original error; if it cannot be written, the attempt stays
    /// Started and the stale-attempt reconciler settles it later — so the alert is
    /// still never lost.
    /// </para>
    /// <para>
    /// A cancellation is told apart by whose token fired (Phase 38.7a): the
    /// person's (<c>cancelled</c>), the host shutting down under a detached attempt
    /// (<c>interrupted</c>), or the server's own deadline (<c>timed_out</c>).
    /// </para>
    /// </remarks>
    private async Task SettleFailedAsync(
        KnowledgeIndexAttempt attempt,
        Exception failure,
        CancellationTokenSource? deadline,
        CancellationToken caller)
    {
        (KnowledgeIndexAttemptStatus status, string code, bool alert) = failure switch
        {
            EmbeddingUnavailableException => (KnowledgeIndexAttemptStatus.Failed, KnowledgeIndexAttemptFailures.ProviderUnavailable, true),
            EmbeddingTimeoutException => (KnowledgeIndexAttemptStatus.Failed, KnowledgeIndexAttemptFailures.ProviderTimeout, true),
            EmbeddingResponseInvalidException => (KnowledgeIndexAttemptStatus.Failed, KnowledgeIndexAttemptFailures.ProviderResponseInvalid, true),
            EntitlementExceededException => (KnowledgeIndexAttemptStatus.Failed, KnowledgeIndexAttemptFailures.QuotaExhausted, false),
            KnowledgeDocumentVersionMismatchException => (KnowledgeIndexAttemptStatus.Failed, KnowledgeIndexAttemptFailures.DocumentChanged, false),
            OperationCanceledException when caller.IsCancellationRequested && deadline is not null =>
                (KnowledgeIndexAttemptStatus.Unknown, KnowledgeIndexAttemptFailures.Interrupted, true),
            OperationCanceledException when caller.IsCancellationRequested =>
                (KnowledgeIndexAttemptStatus.Unknown, KnowledgeIndexAttemptFailures.Cancelled, false),
            OperationCanceledException when deadline is { IsCancellationRequested: true } =>
                (KnowledgeIndexAttemptStatus.Failed, KnowledgeIndexAttemptFailures.TimedOut, true),
            _ => (KnowledgeIndexAttemptStatus.Failed, KnowledgeIndexAttemptFailures.IndexingError, true),
        };

        try
        {
            await _attempts!
                .SettleAsync(attempt.Id, status, code, alert, _clock.GetUtcNow(), CancellationToken.None)
                .ConfigureAwait(false);
        }
#pragma warning disable CA1031 // The indexing failure is what the caller must see; the reconciler settles a row left Started.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
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
