namespace OmniAI.Application.Knowledge;

/// <summary>
/// Another indexing attempt for this document is already running (Phase 35).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Refused before anything is spent.</strong> Phase 34 indexed one document
/// four times at once: every request passed the "already current?" check, because
/// none had finished, and all four sent paid embedding batches for one version.
/// Only one of them could ever be written. The database now allows one Started
/// attempt per document, so the losers stop here instead.
/// </para>
/// <para>
/// Not a failure of the document. Whoever is waiting sees the running attempt's
/// result once it settles; pressing Reindex again afterwards costs nothing when the
/// index is current. An attempt a crash left Started holds the document until the
/// stale-attempt reconciler settles it.
/// </para>
/// </remarks>
public sealed class KnowledgeIndexingInProgressException : Exception
{
    private const string Default =
        "This document is already being indexed. Nothing was sent; try again once that finishes.";

    public KnowledgeIndexingInProgressException()
        : base(Default)
    {
    }

    public KnowledgeIndexingInProgressException(string message)
        : base(message)
    {
    }

    public KnowledgeIndexingInProgressException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
