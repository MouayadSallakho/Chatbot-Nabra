using OmniAI.Application.Concurrency;
using OmniAI.Domain.Knowledge;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Knowledge.Persistence;

/// <summary>
/// Where each file-backed document's text came from.
/// </summary>
/// <remarks>
/// <para>
/// Every method takes the workspace, like every other knowledge repository: a
/// handler holding a document id and no workspace can reach nothing.
/// </para>
/// <para>
/// <strong>Writing a source and writing its document is one operation.</strong>
/// A source describing a file whose text was never stored, or a document whose
/// provenance was lost, are both states nothing later can repair — so the two
/// are saved together and the interface offers no way to save one alone.
/// </para>
/// </remarks>
public interface IKnowledgeSourceRepository
{
    /// <summary>The source behind one document, if it has one.</summary>
    /// <returns>
    /// <see langword="null"/> for a manually written document, and for a document
    /// belonging to another workspace — indistinguishably.
    /// </returns>
    Task<KnowledgeSource?> FindAsync(
        TenantId tenantId,
        KnowledgeDocumentId documentId,
        CancellationToken cancellationToken = default);

    /// <summary>The sources behind a page of documents, for a list screen.</summary>
    /// <remarks>
    /// Keyed by document id. A document with no entry was written by hand, which
    /// is what lets a list tell the two apart without one query per row.
    /// </remarks>
    Task<IReadOnlyDictionary<Guid, KnowledgeSource>> ListForAsync(
        TenantId tenantId,
        IReadOnlyCollection<KnowledgeDocumentId> documentIds,
        CancellationToken cancellationToken = default);

    /// <summary>Stores a new document and the file it was read from, together.</summary>
    Task<ConcurrencyToken> AddAsync(
        KnowledgeDocument document,
        KnowledgeSource source,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a changed document and its changed source, if the document is still
    /// at this version.
    /// </summary>
    /// <exception cref="KnowledgeDocumentVersionMismatchException">
    /// Somebody else changed the document in between.
    /// </exception>
    Task<ConcurrencyToken> ReplaceAsync(
        KnowledgeDocument document,
        KnowledgeSource source,
        ConcurrencyToken expected,
        CancellationToken cancellationToken = default);
}
