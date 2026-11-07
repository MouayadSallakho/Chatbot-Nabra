using OmniAI.Application.Concurrency;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Notifications;
using OmniAI.Domain.Reports;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Application.Notifications;

/// <summary>
/// How long notifications are kept, and how much of them one request may read.
/// </summary>
public sealed class NotificationOptions
{
    public const string Section = "Notifications";

    /// <summary>How long a notification is kept before the retention worker removes it.</summary>
    public int RetentionDays { get; set; } = 90;

    public TimeSpan Retention => TimeSpan.FromDays(RetentionDays);

    public void Validate()
    {
        if (RetentionDays is < 1 or > 730)
        {
            throw new InvalidOperationException($"{Section}:RetentionDays must be between 1 and 730.");
        }
    }
}

/// <summary>A page of one member's notifications, newest first.</summary>
/// <param name="ReportTypes">
/// The report type behind each report notification on the page, derived from the
/// execution its occurrence names (Phase 37.6, row 25). A notification is absent
/// when it is not about a report, or its report can no longer be found.
/// </param>
public sealed record NotificationPage(
    IReadOnlyList<Notification> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int UnreadCount,
    IReadOnlyDictionary<NotificationId, ReportType>? ReportTypes = null)
{
    public const int DefaultPageSize = 20;

    public const int MaxPageSize = 50;
}

/// <summary>
/// One member's notifications and preferences, in the database.
/// </summary>
/// <remarks>
/// Every method is addressed by workspace <em>and</em> recipient. There is no
/// read by notification id alone, so an id from another member or another
/// workspace finds nothing, exactly like an id that was never issued.
/// </remarks>
public interface INotificationStore
{
    /// <summary>Newest first, then by id descending.</summary>
    Task<(IReadOnlyList<Notification> Items, int TotalCount)> ListAsync(
        TenantId tenantId,
        UserId recipient,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<int> CountUnreadAsync(TenantId tenantId, UserId recipient, CancellationToken cancellationToken = default);

    /// <returns>
    /// <see langword="false"/> when no such notification belongs to this member.
    /// Reading one that is already read succeeds and changes nothing.
    /// </returns>
    Task<bool> MarkReadAsync(
        TenantId tenantId,
        UserId recipient,
        NotificationId id,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <returns>How many were unread and are now read.</returns>
    Task<int> MarkAllReadAsync(
        TenantId tenantId,
        UserId recipient,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The member's preferences, or the defaults when they have never chosen.
    /// </summary>
    /// <remarks>
    /// The defaults carry a version that means "nothing stored yet", so the first
    /// save is concurrency-checked like every other: two tabs saving a member's
    /// first choice at once cannot both win.
    /// </remarks>
    Task<Versioned<NotificationPreferences>> PreferencesAsync(
        TenantId tenantId,
        UserId userId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <exception cref="NotificationPreferencesVersionMismatchException">
    /// The stored preferences are not at <paramref name="expected"/>.
    /// </exception>
    Task<ConcurrencyToken> SavePreferencesAsync(
        NotificationPreferences preferences,
        ConcurrencyToken expected,
        CancellationToken cancellationToken = default);

    /// <summary>The preferences as stored, for a worker. Defaults when none are.</summary>
    Task<NotificationPreferences> PreferencesForSendingAsync(
        TenantId tenantId,
        UserId userId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<Notification?> FindAsync(
        TenantId tenantId,
        NotificationId id,
        CancellationToken cancellationToken = default);
}

/// <summary>An occurrence a worker has claimed, with the token that proves it.</summary>
public sealed record ClaimedOccurrence(
    NotificationOccurrenceId Id,
    TenantId TenantId,
    NotificationKind Kind,
    string OccurrenceKey,
    Guid? SourceId,
    Guid ClaimToken,
    int Attempts);

/// <summary>What one dispatch writes, in one transaction.</summary>
/// <param name="Notifications">One per recipient. Rows already present are skipped by their unique key.</param>
/// <param name="EmailRecipients">Recipients whose email is to be queued, if their notification was newly written.</param>
public sealed record OccurrenceOutcome(
    IReadOnlyList<Notification> Notifications,
    IReadOnlySet<UserId> EmailRecipients);

/// <summary>Occurrences waiting to become notifications.</summary>
public interface INotificationOccurrenceStore
{
    /// <summary>
    /// Claims unprocessed occurrences, and ones whose lease has lapsed, with
    /// <c>FOR UPDATE SKIP LOCKED</c>. Occurrences that ran out of attempts are
    /// closed here rather than claimed again.
    /// </summary>
    Task<IReadOnlyList<ClaimedOccurrence>> ClaimAsync(
        int batchSize,
        int maxAttempts,
        TimeSpan lease,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the notifications and queued emails and marks the occurrence
    /// processed, all in one transaction.
    /// </summary>
    /// <returns>
    /// The recipients whose notification was newly written, or
    /// <see langword="null"/> when the claim was lost to another worker — in which
    /// case nothing was written.
    /// </returns>
    Task<IReadOnlyList<UserId>?> CompleteAsync(
        ClaimedOccurrence occurrence,
        OccurrenceOutcome outcome,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<bool> ReleaseAsync(
        NotificationOccurrenceId id,
        Guid claimToken,
        CancellationToken cancellationToken = default);
}

/// <summary>Facts about the rows occurrences point at, read when they are dispatched.</summary>
public interface INotificationSources
{
    /// <returns>The execution's requester, report type and whether it was scheduled; null if it is gone.</returns>
    Task<(UserId RequestedBy, ReportType Type, bool Scheduled)?> ReportAsync(
        TenantId tenantId,
        Guid executionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The report type behind a report notification, derived rather than stored:
    /// the occurrence's source is the execution (Phase 37.3).
    /// </summary>
    /// <returns>The type; null when the occurrence or the execution is gone.</returns>
    Task<ReportType?> ReportTypeOfOccurrenceAsync(
        TenantId tenantId,
        NotificationOccurrenceId occurrenceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The report types behind several report occurrences at once, for one page of
    /// the notification list (Phase 37.6, row 25).
    /// </summary>
    /// <remarks>
    /// Two bounded reads in the workspace, whatever the page holds: the occurrences'
    /// sources, then the executions they name. Never one read per notification.
    /// </remarks>
    /// <returns>A type for each occurrence whose execution still exists.</returns>
    Task<IReadOnlyDictionary<NotificationOccurrenceId, ReportType>> ReportTypesOfOccurrencesAsync(
        TenantId tenantId,
        IReadOnlyCollection<NotificationOccurrenceId> occurrenceIds,
        CancellationToken cancellationToken = default);

    /// <returns>The connection a delivery was sent through; null if it is gone.</returns>
    Task<Guid?> WhatsAppConnectionOfDeliveryAsync(
        TenantId tenantId,
        Guid deliveryId,
        CancellationToken cancellationToken = default);
}

/// <summary>Who in a workspace currently holds a permission.</summary>
public interface INotificationRecipientDirectory
{
    /// <summary>
    /// Current members holding <paramref name="permission"/> through any of their
    /// roles, read fresh. Ordered by user id, so dispatch is deterministic.
    /// </summary>
    Task<IReadOnlyList<UserId>> MembersHoldingAsync(
        TenantId tenantId,
        PermissionCode permission,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Tells a member's open admin sessions that their notifications changed.
/// </summary>
/// <remarks>
/// A structural signal and nothing more: no title, no body, no id. The client
/// re-reads through the ordinary authorised API. Failure to signal is never an
/// error — the next read is correct regardless.
/// </remarks>
public interface INotificationRealtime
{
    Task NotificationsChangedAsync(
        TenantId tenantId,
        IReadOnlyCollection<UserId> recipients,
        CancellationToken cancellationToken = default);
}

/// <summary>An email a worker has claimed.</summary>
public sealed record ClaimedEmail(
    NotificationEmailId Id,
    NotificationId NotificationId,
    TenantId TenantId,
    UserId RecipientUserId,
    NotificationCategory Category,
    Guid ClaimToken,
    int Attempts);

/// <summary>Queued operational emails.</summary>
public interface INotificationEmailOutbox
{
    /// <summary>
    /// Takes ownership of pending emails that are due, with <c>FOR UPDATE SKIP
    /// LOCKED</c> and a fresh claim token. They stay <c>Pending</c>: owning an email
    /// is not having tried to send it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Phase 35.</strong> Claiming used to move the whole batch to
    /// <c>Sending</c>, so a worker killed before its first SMTP attempt left every
    /// claimed email to be reconciled as <c>Unknown</c> and never delivered, though
    /// nothing had been handed to any server. Ownership and "dispatch may have
    /// happened" are now separate: see <see cref="BeginDispatchAsync"/>.
    /// </para>
    /// <para>
    /// A pending email whose claim is older than <paramref name="reclaimClaimedBefore"/>
    /// belonged to a worker that stopped before dispatching it, and is claimed again.
    /// Its old owner cannot then dispatch or settle it: both are conditional on the
    /// token, which the new claim replaced.
    /// </para>
    /// <para>
    /// <see cref="ClaimedEmail.Attempts"/> is the attempt number the dispatch will
    /// be, which is what the retry arithmetic needs.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<ClaimedEmail>> ClaimAsync(
        int batchSize,
        int maxAttempts,
        DateTimeOffset now,
        DateTimeOffset reclaimClaimedBefore,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The dispatch boundary: moves one owned email to <c>Sending</c> immediately
    /// before it is handed to the SMTP server, counting the attempt.
    /// </summary>
    /// <remarks>
    /// Returns false when this worker no longer owns it (it was reclaimed); the
    /// caller must then not send. From <c>Sending</c> onwards a crash can only end in
    /// <c>Unknown</c> — never a resend (<see cref="AbandonStaleAsync"/>).
    /// </remarks>
    Task<bool> BeginDispatchAsync(
        NotificationEmailId id,
        Guid claimToken,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a final outcome. Conditional on the claim. <c>Skipped</c> settles an
    /// owned email that was never dispatched; every other outcome settles one that was.
    /// </summary>
    Task<bool> SettleAsync(
        NotificationEmailId id,
        Guid claimToken,
        NotificationEmailStatus status,
        string? failureCode,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>Hands an email back after a failure that certainly sent nothing.</summary>
    Task<bool> RetryLaterAsync(
        NotificationEmailId id,
        Guid claimToken,
        string failureCode,
        DateTimeOffset availableAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks emails a crashed worker left in Sending as Unknown. Only emails that
    /// crossed the dispatch boundary are in Sending (Phase 35).
    /// </summary>
    /// <remarks>
    /// Never back to Pending: the worker may have handed the message over before
    /// it died, and a second send would be a duplicate in somebody's inbox.
    /// </remarks>
    Task<int> AbandonStaleAsync(
        DateTimeOffset claimedBefore,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}

/// <summary>Removes what the retention policy no longer keeps.</summary>
public interface INotificationRetentionStore
{
    /// <summary>Deletes expired notifications, and their emails with them.</summary>
    Task<int> DeleteExpiredNotificationsAsync(
        DateTimeOffset now,
        int batchSize,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes processed occurrences older than <paramref name="before"/>.</summary>
    Task<int> DeleteProcessedOccurrencesAsync(
        DateTimeOffset before,
        int batchSize,
        CancellationToken cancellationToken = default);
}

/// <summary>A member's preferences changed since they were read.</summary>
public sealed class NotificationPreferencesVersionMismatchException : Exception
{
    public NotificationPreferencesVersionMismatchException()
        : base("Your notification preferences were changed somewhere else since you opened them. Reload and try again.")
    {
    }

    public NotificationPreferencesVersionMismatchException(string message)
        : base(message)
    {
    }

    public NotificationPreferencesVersionMismatchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
