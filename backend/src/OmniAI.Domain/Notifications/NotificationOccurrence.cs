using OmniAI.Domain.Tenants;

namespace OmniAI.Domain.Notifications;

/// <summary>
/// One authoritative state transition that members are to be told about.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Written with the transition, in the same transaction.</strong> A
/// report completing, a subscription entering PastDue or a delivery becoming
/// Unknown records its occurrence in the statement or transaction that makes the
/// change. A crash can lose neither without the other, and no process-local queue
/// is ever the only place a notification exists.
/// </para>
/// <para>
/// <strong>Identity, not payload.</strong> A kind, the key that makes this
/// occurrence unique, and the id of the source row. There is no JSON, no copied
/// text and no recipient list: who is told, and what they read, is worked out
/// later from the source row and from permissions as they stand then.
/// </para>
/// <para>
/// <strong>The key separates a replay from a recurrence.</strong> Processing the
/// same transition twice produces the same key, which the database accepts once.
/// A subscription that goes PastDue, recovers and goes PastDue again produces a
/// second key, because it is a second transition.
/// </para>
/// </remarks>
public sealed class NotificationOccurrence
{
    public const int MaxKeyLength = 160;

    private NotificationOccurrence(
        NotificationOccurrenceId id,
        TenantId tenantId,
        NotificationKind kind,
        string occurrenceKey,
        Guid? sourceId,
        DateTimeOffset occurredAt,
        DateTimeOffset? processedAt,
        int attempts)
    {
        Id = id;
        TenantId = tenantId;
        Kind = kind;
        OccurrenceKey = occurrenceKey;
        SourceId = sourceId;
        OccurredAt = occurredAt;
        ProcessedAt = processedAt;
        Attempts = attempts;
    }

    public NotificationOccurrenceId Id { get; }

    public TenantId TenantId { get; }

    public NotificationKind Kind { get; }

    /// <summary>Unique per workspace and kind.</summary>
    public string OccurrenceKey { get; }

    /// <summary>The row the transition happened to, when there is one.</summary>
    public Guid? SourceId { get; }

    public DateTimeOffset OccurredAt { get; }

    /// <summary>When its notifications were written. Null while still to do.</summary>
    public DateTimeOffset? ProcessedAt { get; }

    public int Attempts { get; }

    public static NotificationOccurrence Record(
        TenantId tenantId,
        NotificationKind kind,
        string occurrenceKey,
        Guid? sourceId,
        DateTimeOffset occurredAt)
    {
        if (tenantId.IsEmpty)
        {
            throw new DomainValidationException("An occurrence belongs to a workspace.");
        }

        if (!Enum.IsDefined(kind))
        {
            throw new DomainValidationException("That notification kind is not recognised.");
        }

        if (string.IsNullOrWhiteSpace(occurrenceKey) || occurrenceKey.Length > MaxKeyLength)
        {
            throw new DomainValidationException(
                $"An occurrence key must be between 1 and {MaxKeyLength} characters.");
        }

        return new NotificationOccurrence(
            NotificationOccurrenceId.New(),
            tenantId,
            kind,
            occurrenceKey,
            sourceId,
            occurredAt.ToUniversalTime(),
            processedAt: null,
            attempts: 0);
    }
}
