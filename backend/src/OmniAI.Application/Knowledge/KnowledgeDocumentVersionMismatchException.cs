namespace OmniAI.Application.Knowledge;

/// <summary>
/// A write was attempted against a version of a knowledge document that is no
/// longer current.
/// </summary>
/// <remarks>
/// Raised for an edit, an index, an enable and a disable alike. Indexing is the
/// interesting case: the embeddings for the text somebody read may take seconds
/// to produce, and if the document was edited in that window, attaching them
/// would leave the index describing a draft nobody can see any more. Refusing
/// costs the caller the provider call they already made, which is the honest
/// price of not corrupting the index.
/// </remarks>
public sealed class KnowledgeDocumentVersionMismatchException : Exception
{
    private const string Default =
        "This knowledge document has changed since it was read. No change was applied.";

    public KnowledgeDocumentVersionMismatchException()
        : base(Default)
    {
    }

    public KnowledgeDocumentVersionMismatchException(string message)
        : base(message)
    {
    }

    public KnowledgeDocumentVersionMismatchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public Guid? KnowledgeDocumentId { get; private init; }

    public static KnowledgeDocumentVersionMismatchException ForDocument(
        Guid knowledgeDocumentId,
        Exception? innerException = null)
    {
        string message =
            $"The knowledge document '{knowledgeDocumentId}' has changed since the version you "
            + "supplied was read. Nothing was written -- no content was replaced and no chunk "
            + "was stored. Read it again and reapply the change if it is still wanted.";

        return innerException is null
            ? new KnowledgeDocumentVersionMismatchException(message)
                { KnowledgeDocumentId = knowledgeDocumentId }
            : new KnowledgeDocumentVersionMismatchException(message, innerException)
                { KnowledgeDocumentId = knowledgeDocumentId };
    }
}
