using OmniAI.Domain.Tenants;

namespace OmniAI.Domain.Knowledge;

/// <summary>Where one indexing attempt got to.</summary>
/// <remarks>
/// <see cref="Unknown"/> is the honest state for an attempt whose process
/// stopped while it was running: embedding requests may have been sent, and
/// nothing was written to the index. It is never retried automatically — a
/// member indexes again through the normal workflow if they want to.
/// </remarks>
public enum KnowledgeIndexAttemptStatus
{
    Started = 1,
    Succeeded = 2,
    Failed = 3,
    Unknown = 4,
}

/// <summary>Why an attempt did not succeed, as stable codes. Never exception text.</summary>
public static class KnowledgeIndexAttemptFailures
{
    /// <summary>The embedding provider could not be reached or is not configured.</summary>
    public const string ProviderUnavailable = "provider_unavailable";

    /// <summary>The embedding provider did not answer in time.</summary>
    public const string ProviderTimeout = "provider_timeout";

    /// <summary>The embedding provider answered with something unusable.</summary>
    public const string ProviderResponseInvalid = "provider_response_invalid";

    /// <summary>The workspace's monthly AI operation quota refused the embedding.</summary>
    public const string QuotaExhausted = "quota_exhausted";

    /// <summary>The document changed while it was being indexed; the newer version wins.</summary>
    public const string DocumentChanged = "document_changed";

    /// <summary>The requester cancelled; the provider may have received some batches.</summary>
    public const string Cancelled = "cancelled";

    /// <summary>The process stopped mid-attempt, found by the stale-attempt reconciler.</summary>
    public const string Interrupted = "interrupted";

    /// <summary>Anything else. The detail is in the server log, never here.</summary>
    public const string IndexingError = "indexing_error";

    public const int MaxLength = 40;
}

/// <summary>
/// One real attempt to index a knowledge document: embedding work was about to
/// be sent to a provider.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Written before the first embedding request, and committed.</strong>
/// That is the whole point: if the process dies mid-attempt, this row survives
/// as <see cref="KnowledgeIndexAttemptStatus.Started"/>, and a reconciler can
/// settle it truthfully and raise the operational alert exactly once.
/// </para>
/// <para>
/// <strong>Nothing but identity and outcome.</strong> No document content, no
/// chunk text, no vector, no provider message and no stack trace — a failure is
/// one of the codes in <see cref="KnowledgeIndexAttemptFailures"/>.
/// </para>
/// <para>
/// The unchanged-document path never creates one: an index that is already
/// current costs nothing and needs no bookkeeping.
/// </para>
/// </remarks>
public sealed class KnowledgeIndexAttempt
{
    private KnowledgeIndexAttempt(
        Guid id,
        TenantId tenantId,
        KnowledgeDocumentId documentId,
        KnowledgeIndexAttemptStatus status,
        string? failureCode,
        DateTimeOffset startedAt,
        DateTimeOffset? completedAt)
    {
        Id = id;
        TenantId = tenantId;
        DocumentId = documentId;
        Status = status;
        FailureCode = failureCode;
        StartedAt = startedAt;
        CompletedAt = completedAt;
    }

    public Guid Id { get; }

    public TenantId TenantId { get; }

    public KnowledgeDocumentId DocumentId { get; }

    public KnowledgeIndexAttemptStatus Status { get; }

    public string? FailureCode { get; }

    public DateTimeOffset StartedAt { get; }

    public DateTimeOffset? CompletedAt { get; }

    public static KnowledgeIndexAttempt Start(TenantId tenantId, KnowledgeDocumentId documentId, DateTimeOffset startedAt)
    {
        if (tenantId.IsEmpty)
        {
            throw new DomainValidationException("An indexing attempt belongs to a workspace.")
            {
                Code = "knowledge.tenant.required",
            };
        }

        return new KnowledgeIndexAttempt(
            Guid.CreateVersion7(),
            tenantId,
            documentId,
            KnowledgeIndexAttemptStatus.Started,
            failureCode: null,
            startedAt.ToUniversalTime(),
            completedAt: null);
    }
}
