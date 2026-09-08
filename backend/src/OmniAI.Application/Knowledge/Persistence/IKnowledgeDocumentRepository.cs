using OmniAI.Application.Concurrency;
using OmniAI.Application.Knowledge.ListKnowledgeDocuments;
using OmniAI.Domain.Knowledge;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Knowledge.Persistence;

/// <summary>
/// Stored knowledge documents.
/// </summary>
/// <remarks>
/// Every method takes the workspace, and none offers a way to read a document by
/// id alone. That is the isolation rule expressed in the signature rather than in
/// a comment asking callers to remember it: a handler that has an id but not a
/// workspace cannot reach anything.
/// </remarks>
public interface IKnowledgeDocumentRepository
{
    Task<Versioned<KnowledgeDocument>?> FindAsync(
        TenantId tenantId,
        KnowledgeDocumentId documentId,
        CancellationToken cancellationToken = default);

    Task<KnowledgeDocumentPage> ListAsync(
        TenantId tenantId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every document in the workspace, oldest first, for the assignment screen.
    /// </summary>
    /// <remarks>
    /// Unpaged on purpose, and bounded by <paramref name="limit"/>. Choosing which
    /// documents an agent may draw on is a decision taken across the whole set at
    /// once; paging it would mean somebody could tick boxes on page one, move to
    /// page two, and lose the first choice or save a partial one.
    /// </remarks>
    Task<IReadOnlyList<KnowledgeDocument>> ListAllAsync(
        TenantId tenantId,
        int limit,
        CancellationToken cancellationToken = default);

    Task<ConcurrencyToken> AddAsync(
        KnowledgeDocument document,
        CancellationToken cancellationToken = default);

    Task<ConcurrencyToken> UpdateAsync(
        KnowledgeDocument document,
        ConcurrencyToken expected,
        CancellationToken cancellationToken = default);
}
