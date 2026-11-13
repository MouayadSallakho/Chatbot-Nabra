namespace OmniAI.Domain.Notifications;

/// <summary>
/// The product area an operational notification is about.
/// </summary>
/// <remarks>
/// Deliberately five values, and the unit an email preference is chosen by. A
/// category is not a permission: who is told is decided by the kind's recipient
/// policy, from permissions that already exist.
/// </remarks>
public enum NotificationCategory
{
    Reports = 1,
    Usage = 2,
    Billing = 3,
    Knowledge = 4,
    ChannelHealth = 5,
}

/// <summary>
/// How urgently a notification deserves attention.
/// </summary>
/// <remarks>
/// Presentation and triage only. A severity grants nothing, changes no business
/// state and is never read back to decide anything.
/// </remarks>
public enum NotificationSeverity
{
    Information = 1,
    Warning = 2,
    Critical = 3,
}

/// <summary>
/// Every operational event this platform tells a member about.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A closed catalogue, not an event bus.</strong> Each value names one
/// authoritative state transition that already exists in the product — a report
/// execution settling, a Phase 24 threshold being crossed, a Phase 25
/// subscription moving, a Phase 20 delivery becoming Unknown. There is no
/// user-defined kind, no rule editor and no generic payload.
/// </para>
/// <para>
/// Nothing here is notified for ordinary traffic: customer messages, AI replies,
/// successful indexing and Sent/Delivered/Read receipts have no kind at all.
/// </para>
/// </remarks>
public enum NotificationKind
{
    ReportCompleted = 1,
    ReportFailed = 2,
    ScheduledReportCompleted = 3,
    ScheduledReportFailed = 4,
    UsageWarning = 5,
    UsageCritical = 6,
    UsageExhausted = 7,
    BillingPastDue = 8,
    BillingSuspended = 9,
    BillingRecovered = 10,
    BillingCanceled = 11,
    KnowledgeIndexingFailed = 12,
    WhatsAppDeliveryUnknown = 13,
}

/// <summary>
/// Where one operational email got to.
/// </summary>
/// <remarks>
/// <para>
/// <strong><see cref="Unknown"/> is the honest state.</strong> An SMTP
/// conversation that broke after the message was handed over may or may not have
/// been accepted. Sending it again could deliver it twice, so it is recorded as
/// unknown and never retried automatically.
/// </para>
/// <para>
/// <see cref="Skipped"/> means the platform decided, at send time, that the email
/// must not go: the member left, lost the permission, switched the category off,
/// or has no usable address. It is a decision, not a failure.
/// </para>
/// </remarks>
public enum NotificationEmailStatus
{
    Pending = 1,
    Sending = 2,
    Sent = 3,
    Failed = 4,
    Unknown = 5,
    Skipped = 6,
}

/// <summary>What each kind is: its category and its severity.</summary>
public static class NotificationKinds
{
    public static NotificationCategory CategoryOf(NotificationKind kind) => kind switch
    {
        NotificationKind.ReportCompleted
            or NotificationKind.ReportFailed
            or NotificationKind.ScheduledReportCompleted
            or NotificationKind.ScheduledReportFailed => NotificationCategory.Reports,

        NotificationKind.UsageWarning
            or NotificationKind.UsageCritical
            or NotificationKind.UsageExhausted => NotificationCategory.Usage,

        NotificationKind.BillingPastDue
            or NotificationKind.BillingSuspended
            or NotificationKind.BillingRecovered
            or NotificationKind.BillingCanceled => NotificationCategory.Billing,

        NotificationKind.KnowledgeIndexingFailed => NotificationCategory.Knowledge,

        NotificationKind.WhatsAppDeliveryUnknown => NotificationCategory.ChannelHealth,

        _ => throw new DomainValidationException("That notification kind is not recognised.")
        {
            Code = "notifications.kind.unknown",
        },
    };

    /// <remarks>
    /// Recovery and ready reports inform. Anything somebody should look at soon
    /// warns. Critical is kept for the states that already stop something: a
    /// quota at or near its hard limit and a subscription whose plan was withdrawn.
    /// </remarks>
    public static NotificationSeverity SeverityOf(NotificationKind kind) => kind switch
    {
        NotificationKind.ReportCompleted
            or NotificationKind.ScheduledReportCompleted
            or NotificationKind.BillingRecovered => NotificationSeverity.Information,

        NotificationKind.ReportFailed
            or NotificationKind.ScheduledReportFailed
            or NotificationKind.UsageWarning
            or NotificationKind.BillingPastDue
            or NotificationKind.BillingCanceled
            or NotificationKind.KnowledgeIndexingFailed
            or NotificationKind.WhatsAppDeliveryUnknown => NotificationSeverity.Warning,

        NotificationKind.UsageCritical
            or NotificationKind.UsageExhausted
            or NotificationKind.BillingSuspended => NotificationSeverity.Critical,

        _ => throw new DomainValidationException("That notification kind is not recognised.")
        {
            Code = "notifications.kind.unknown",
        },
    };
}
