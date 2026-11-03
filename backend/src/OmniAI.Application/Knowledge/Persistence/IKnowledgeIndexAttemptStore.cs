using OmniAI.Domain.Knowledge;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Knowledge.Persistence;

/// <summary>
/// The durable record of real indexing attempts.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Started is committed before any embedding request.</strong> It is the
/// crash-recovery anchor: a process that dies mid-attempt leaves a Started row the
/// stale-attempt reconciler settles and alerts on, exactly once.
/// </para>
/// <para>
/// <strong>Every settlement is conditional on the row still being Started.</strong>
/// A reconciler and a late original can never both settle one attempt, and a
/// settled attempt never changes again.
/// </para>
/// </remarks>
public interface IKnowledgeIndexAttemptStore
{
    /// <summary>Records and commits a Started attempt.</summary>
    /// <exception cref="KnowledgeIndexingInProgressException">
    /// Another attempt for the same document is still Started (Phase 35). Nothing
    /// was written, and the caller must not send any embedding request.
    /// </exception>
    Task StartAsync(KnowledgeIndexAttempt attempt, CancellationToken cancellationToken = default);

    /// <returns><see langword="false"/> when the attempt was no longer Started.</returns>
    Task<bool> SucceedAsync(Guid attemptId, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>
    /// Settles the attempt as Failed or Unknown with a stable code, and — when
    /// <paramref name="alert"/> — records the operational notification occurrence
    /// in the same statement, so the outcome and its alert commit together.
    /// </summary>
    /// <returns><see langword="false"/> when the attempt was no longer Started.</returns>
    Task<bool> SettleAsync(
        Guid attemptId,
        KnowledgeIndexAttemptStatus status,
        string failureCode,
        bool alert,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The document's most recent attempt by start time, or <see langword="null"/>
    /// when it was never indexed (Phase 37.5a, row 15).
    /// </summary>
    /// <remarks>
    /// Scoped by the workspace as well as the document, so an id from another
    /// workspace finds nothing.
    /// </remarks>
    Task<KnowledgeIndexAttemptView?> FindLatestAsync(
        TenantId tenantId,
        KnowledgeDocumentId documentId,
        CancellationToken cancellationToken = default);
}
