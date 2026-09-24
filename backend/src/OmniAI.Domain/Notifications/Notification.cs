using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Domain.Notifications;

/// <summary>
/// One member being told that something happened in their workspace.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Not the source of truth for anything.</strong> A report execution, a
/// subscription, the usage ledger and a WhatsApp delivery remain authoritative for
/// their own state. A notification records that a member was told; nothing ever
/// reads one back to decide what that state is.
/// </para>
/// <para>
/// <strong>Addressed to one member.</strong> There is no workspace-wide row every
/// member can read. Each recipient gets their own, chosen by the kind's recipient
/// policy from their permissions when the notification was created.
/// </para>
/// <para>
/// <strong>Server-written text only.</strong> Title and body come from templates
/// in code and carry no message text, knowledge content, phone number, provider
/// identifier, payment detail or exception text. The action is an internal route.
/// </para>
/// </remarks>
public sealed class Notification
{
    public const int MaxTitleLength = 120;
    public const int MaxBodyLength = 400;
    public const int MaxDeduplicationKeyLength = 200;

    private Notification(
        NotificationId id,
        TenantId tenantId,
        UserId recipientUserId,
        NotificationOccurrenceId occurrenceId,
        NotificationKind kind,
        NotificationCategory category,
        NotificationSeverity severity,
        string title,
        string body,
        string? actionPath,
        string deduplicationKey,
        DateTimeOffset createdAt,
        DateTimeOffset? readAt,
        DateTimeOffset expiresAt)
    {
        Id = id;
        TenantId = tenantId;
        RecipientUserId = recipientUserId;
        OccurrenceId = occurrenceId;
        Kind = kind;
        Category = category;
        Severity = severity;
        Title = title;
        Body = body;
        ActionPath = actionPath;
        DeduplicationKey = deduplicationKey;
        CreatedAt = createdAt;
        ReadAt = readAt;
        ExpiresAt = expiresAt;
    }

    public NotificationId Id { get; }

    public TenantId TenantId { get; }

    public UserId RecipientUserId { get; }

    /// <summary>The transition this notification tells the member about.</summary>
    public NotificationOccurrenceId OccurrenceId { get; }

    public NotificationKind Kind { get; }

    public NotificationCategory Category { get; }

    public NotificationSeverity Severity { get; }

    public string Title { get; }

    public string Body { get; }

    /// <summary>An internal route, or null when there is nowhere useful to go.</summary>
    public string? ActionPath { get; }

    /// <summary>
    /// Unique per workspace and recipient. The same occurrence processed twice
    /// produces one row, because the database refuses the second.
    /// </summary>
    public string DeduplicationKey { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset? ReadAt { get; private set; }

    /// <summary>When the retention policy removes this row.</summary>
    public DateTimeOffset ExpiresAt { get; }

    public bool IsRead => ReadAt is not null;

    public static Notification Create(
        TenantId tenantId,
        UserId recipientUserId,
        NotificationOccurrenceId occurrenceId,
        NotificationKind kind,
        string title,
        string body,
        string? actionPath,
        string deduplicationKey,
        DateTimeOffset createdAt,
        TimeSpan retention)
    {
        if (tenantId.IsEmpty)
        {
            throw new DomainValidationException("A notification belongs to a workspace.");
        }

        if (!Enum.IsDefined(kind))
        {
            throw new DomainValidationException("That notification kind is not recognised.");
        }

        if (retention <= TimeSpan.Zero)
        {
            throw new DomainValidationException("A notification must be kept for a positive length of time.");
        }

        return new Notification(
            NotificationId.New(),
            tenantId,
            recipientUserId,
            occurrenceId,
            kind,
            NotificationKinds.CategoryOf(kind),
            NotificationKinds.SeverityOf(kind),
            Bounded(title, MaxTitleLength, "title"),
            Bounded(body, MaxBodyLength, "body"),
            actionPath is null ? null : NotificationActionPath.Require(actionPath),
            Bounded(deduplicationKey, MaxDeduplicationKeyLength, "deduplication key"),
            createdAt.ToUniversalTime(),
            readAt: null,
            createdAt.ToUniversalTime().Add(retention));
    }

    /// <summary>
    /// Marks the notification read. Monotonic: reading it again changes nothing.
    /// </summary>
    /// <returns><see langword="true"/> when this call is the one that read it.</returns>
    public bool MarkRead(DateTimeOffset now)
    {
        if (ReadAt is not null)
        {
            return false;
        }

        ReadAt = now.ToUniversalTime();

        return true;
    }

    private static string Bounded(string value, int max, string what)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainValidationException($"A notification {what} must not be empty.");
        }

        if (value.Length > max)
        {
            throw new DomainValidationException($"A notification {what} must be at most {max} characters.");
        }

        return value;
    }
}
