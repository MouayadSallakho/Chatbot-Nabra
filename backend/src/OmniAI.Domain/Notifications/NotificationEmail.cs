using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Domain.Notifications;

/// <summary>
/// One operational email a member asked for, queued to be sent.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Separate from the notification it mirrors.</strong> The in-app
/// notification is written first and stands on its own; whether its email is
/// sent, skipped or lost never changes it, nor the business state behind it.
/// </para>
/// <para>
/// <strong>No address, no body, no credential.</strong> The recipient's address
/// is read from their account when the email is sent, and the content is
/// rendered from the notification then. What is stored is where the delivery got
/// to and a stable reason code — never an exception, an SMTP transcript or a
/// password.
/// </para>
/// <para>
/// Claiming, settling and abandoning are written by the email queue with
/// conditional SQL on a claim token, so a worker whose lease lapsed cannot
/// overwrite what the worker that took over recorded.
/// </para>
/// </remarks>
public sealed class NotificationEmail
{
    public const int MaxFailureCodeLength = 40;

    private NotificationEmail(
        NotificationEmailId id,
        NotificationId notificationId,
        TenantId tenantId,
        UserId recipientUserId,
        NotificationCategory category,
        NotificationEmailStatus status,
        DateTimeOffset createdAt,
        DateTimeOffset availableAt,
        int attempts,
        DateTimeOffset? sentAt,
        DateTimeOffset? settledAt,
        string? failureCode)
    {
        Id = id;
        NotificationId = notificationId;
        TenantId = tenantId;
        RecipientUserId = recipientUserId;
        Category = category;
        Status = status;
        CreatedAt = createdAt;
        AvailableAt = availableAt;
        Attempts = attempts;
        SentAt = sentAt;
        SettledAt = settledAt;
        FailureCode = failureCode;
    }

    public NotificationEmailId Id { get; }

    public NotificationId NotificationId { get; }

    public TenantId TenantId { get; }

    public UserId RecipientUserId { get; }

    /// <summary>The preference and permission re-checked at send time.</summary>
    public NotificationCategory Category { get; }

    public NotificationEmailStatus Status { get; }

    public DateTimeOffset CreatedAt { get; }

    /// <summary>Not claimed before this; moved on after a retriable failure.</summary>
    public DateTimeOffset AvailableAt { get; }

    public int Attempts { get; }

    public DateTimeOffset? SentAt { get; }

    /// <summary>When it reached Failed, Unknown or Skipped.</summary>
    public DateTimeOffset? SettledAt { get; }

    /// <summary>A code from <see cref="NotificationEmailFailures"/>. Never exception text.</summary>
    public string? FailureCode { get; }

    public static NotificationEmail Queue(Notification notification, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(notification);

        return new NotificationEmail(
            NotificationEmailId.New(),
            notification.Id,
            notification.TenantId,
            notification.RecipientUserId,
            notification.Category,
            NotificationEmailStatus.Pending,
            now.ToUniversalTime(),
            now.ToUniversalTime(),
            attempts: 0,
            sentAt: null,
            settledAt: null,
            failureCode: null);
    }
}

/// <summary>
/// Why an email was not sent, as stable codes.
/// </summary>
/// <remarks>
/// A code rather than a sentence from the transport, which can carry a host
/// name, an address or a server banner.
/// </remarks>
public static class NotificationEmailFailures
{
    public const string AccountUnavailable = "account_unavailable";
    public const string NotAMember = "not_a_member";
    public const string PermissionRevoked = "permission_revoked";
    public const string PreferenceDisabled = "preference_disabled";
    public const string NotificationGone = "notification_gone";
    public const string EmailNotConfigured = "email_not_configured";
    public const string LiveEmailDisabled = "live_email_disabled";
    public const string ConnectFailed = "connect_failed";
    public const string AuthenticationFailed = "authentication_failed";
    public const string Rejected = "rejected";
    public const string Ambiguous = "outcome_unknown";
    public const string Abandoned = "abandoned_while_sending";
    public const string AttemptsExhausted = "attempts_exhausted";
}
