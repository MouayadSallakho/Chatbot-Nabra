using OmniAI.Application.Concurrency;
using OmniAI.Domain.Knowledge;

namespace OmniAI.Application.Knowledge.Persistence;

/// <summary>One chunk about to be written, with the vector that was made for it.</summary>
public sealed record KnowledgeChunkWrite(int Ordinal, string Text, EmbeddingVector Embedding);

/// <summary>
/// Replaces a document's index, all at once or not at all.
/// </summary>
/// <remarks>
/// <para>
/// <strong>One method, because there is only one safe operation.</strong> There is
/// no "delete the chunks" and no "add a chunk": either of them alone would leave a
/// document that is marked ready sitting on a half-written index, and a customer
/// would be answered from it. What the implementation does inside a single
/// transaction is: check the document is still at the version the caller read,
/// record the index on it, remove every chunk it had, and write the new set.
/// </para>
/// <para>
/// <strong>The embeddings are made before this is called, never inside it.</strong>
/// A database transaction that spans a call to an external provider holds locks
/// for as long as the provider takes to answer, which under load is how a slow
/// third party becomes a database outage. That is why the version is passed in
/// and re-checked here: the document may have been edited while the embeddings
/// were being produced, and if it was, this refuses rather than attaching vectors
/// of one draft to the text of another.
/// </para>
/// </remarks>
public interface IKnowledgeIndexStore
{
    /// <summary>
    /// Commits a freshly computed index for a document.
    /// </summary>
    /// <returns>The document's new version.</returns>
    /// <exception cref="KnowledgeDocumentVersionMismatchException">
    /// The document changed while the embeddings were being produced. Nothing was
    /// written.
    /// </exception>
    Task<ConcurrencyToken> ReplaceIndexAsync(
        KnowledgeDocument document,
        ConcurrencyToken expected,
        IReadOnlyList<KnowledgeChunkWrite> chunks,
        CancellationToken cancellationToken = default);
}
