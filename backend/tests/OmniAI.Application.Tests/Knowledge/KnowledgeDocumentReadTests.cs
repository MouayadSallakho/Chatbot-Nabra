using OmniAI.Application.Concurrency;
using OmniAI.Application.Knowledge;
using OmniAI.Application.Knowledge.GetKnowledgeDocumentById;
using OmniAI.Application.Knowledge.ListKnowledgeDocuments;
using OmniAI.Application.Knowledge.Persistence;
using OmniAI.Application.Tests.Security;
using OmniAI.Domain.Knowledge;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Application.Tests.Knowledge;

/// <summary>
/// Phase 39.1, Revision 1: one read of a document never shows its last attempt
/// Succeeded beside a readiness that still needs an index.
/// </summary>
/// <remarks>
/// An attempt ends in two commits, in this order: the index and the document's
/// readiness, then the Succeeded outcome (and only then the announcement). The
/// read takes the document and the attempt in separate statements, so a
/// settlement can land between them. These doubles answer each statement from
/// what is committed at that moment, and land the settlement right after the
/// reader's first statement, whichever store that is.
/// </remarks>
public sealed class KnowledgeDocumentReadTests
{
    private static readonly DateTimeOffset Noon =
        new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_read_that_straddles_a_settlement_never_shows_succeeded_beside_needs_index()
    {
        TenantId workspace = TenantId.New();
        RecordingEmbeddingGenerator embeddings = new();
        KnowledgeDocument unindexed = KnowledgeSamples.Document(workspace, at: Noon);

        KnowledgeDocument indexed = KnowledgeDocument.Restore(
            unindexed.Id,
            workspace,
            unindexed.Title,
            unindexed.Content,
            isEnabled: true,
            unindexed.Fingerprint,
            embeddings.Profile.Id,
            indexedAt: Noon,
            unindexed.CreatedAt,
            updatedAt: Noon);

        SettlingStore store = new(unindexed, indexed, Noon);

        GetKnowledgeDocumentByIdHandler handler = new(
            store,
            new InMemoryKnowledgeSourceRepository(new InMemoryKnowledgeDocumentRepository()),
            TestAuthority.Owner(UserId.New(), workspace.Value),
            embeddings,
            store);

        Versioned<KnowledgeDocumentView>? read = await handler.HandleAsync(
            new GetKnowledgeDocumentByIdQuery(workspace.Value, unindexed.Id.Value));

        Assert.NotNull(read);
        Assert.True(store.Settled);

        KnowledgeDocumentView view = read.Value;

        if (view.LastIndexAttempt?.Status == KnowledgeIndexAttemptStatus.Succeeded)
        {
            Assert.Equal(KnowledgeReadiness.Ready, view.Readiness);
        }
    }

    /// <summary>
    /// One document and its one attempt, as committed; the attempt settles
    /// Succeeded right after the first statement anybody reads.
    /// </summary>
    private sealed class SettlingStore(
        KnowledgeDocument unindexed,
        KnowledgeDocument indexed,
        DateTimeOffset startedAt)
        : IKnowledgeDocumentRepository, IKnowledgeIndexAttemptStore
    {
        private KnowledgeDocument _document = unindexed;

        private KnowledgeIndexAttemptView _attempt =
            new(KnowledgeIndexAttemptStatus.Started, null, startedAt, null);

        public bool Settled { get; private set; }

        public Task<Versioned<KnowledgeDocument>?> FindAsync(
            TenantId tenantId,
            KnowledgeDocumentId documentId,
            CancellationToken cancellationToken = default)
        {
            Versioned<KnowledgeDocument> read = new(_document, ConcurrencyToken.From(Settled ? "2" : "1"));

            Settle();

            return Task.FromResult<Versioned<KnowledgeDocument>?>(read);
        }

        public Task<KnowledgeIndexAttemptView?> FindLatestAsync(
            TenantId tenantId,
            KnowledgeDocumentId documentId,
            CancellationToken cancellationToken = default)
        {
            KnowledgeIndexAttemptView read = _attempt;

            Settle();

            return Task.FromResult<KnowledgeIndexAttemptView?>(read);
        }

        /// <summary>The writer's two commits, in the writer's order.</summary>
        private void Settle()
        {
            if (Settled)
            {
                return;
            }

            _document = indexed;
            _attempt = _attempt with
            {
                Status = KnowledgeIndexAttemptStatus.Succeeded,
                CompletedAt = _attempt.StartedAt,
            };

            Settled = true;
        }

        public Task<KnowledgeDocumentPage> ListAsync(
            TenantId tenantId,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<KnowledgeDocument>> ListAllAsync(
            TenantId tenantId,
            int limit,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ConcurrencyToken> AddAsync(
            KnowledgeDocument document,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ConcurrencyToken> UpdateAsync(
            KnowledgeDocument document,
            ConcurrencyToken expected,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task StartAsync(KnowledgeIndexAttempt attempt, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> SucceedAsync(Guid attemptId, DateTimeOffset now, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> SettleAsync(
            Guid attemptId,
            KnowledgeIndexAttemptStatus status,
            string failureCode,
            bool alert,
            DateTimeOffset now,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
