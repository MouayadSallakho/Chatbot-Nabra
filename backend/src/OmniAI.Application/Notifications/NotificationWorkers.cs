using OmniAI.Application.Reports;
using OmniAI.Application.Security;
using OmniAI.Application.Tenants.Persistence;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Notifications;
using OmniAI.Domain.Reports;
using OmniAI.Domain.Users;

namespace OmniAI.Application.Notifications;

/// <summary>How one claimed item ended.</summary>
public enum NotificationWorkOutcome
{
    Done = 1,

    /// <summary>A transient failure; handed back to be tried again.</summary>
    Released = 2,

    /// <summary>The claim was lost to another worker; nothing was recorded.</summary>
    Superseded = 3,
}

/// <param name="Fault">For the worker's log only. Never stored and never shown.</param>
public sealed record NotificationWorkResult(NotificationWorkOutcome Outcome, Exception? Fault = null);

/// <summary>
/// Turns one occurrence into notifications for the members who should hear about it.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Recipients are decided now, from permissions as they stand now.</strong>
/// The occurrence records only what happened. Who is told is read fresh when it is
/// dispatched, so a member who lost the permission a moment before is not told,
/// and one who gained it is.
/// </para>
/// <para>
/// <strong>Once per recipient, whatever happens.</strong> Each notification's
/// deduplication key is unique per workspace and recipient, so an occurrence
/// dispatched twice — after a crash, or by two instances racing past a lapsed
/// lease — writes each row once. The occurrence is marked processed in the same
/// transaction as its notifications and emails.
/// </para>
/// <para>
/// <strong>Email is queued, never sent here.</strong> A queued email is a row in
/// the same transaction; sending happens later, in its own worker, and whatever
/// becomes of it changes neither the notification nor the business state.
/// </para>
/// </remarks>
public sealed class NotificationDispatcher(
    INotificationOccurrenceStore queue,
    INotificationSources sources,
    INotificationRecipientDirectory directory,
    ReportAuthority authority,
    INotificationStore store,
    IOperationalEmailSender email,
    INotificationRealtime realtime,
    NotificationOptions options,
    TimeProvider clock)
{
    private readonly INotificationOccurrenceStore _queue = queue ?? throw new ArgumentNullException(nameof(queue));

    private readonly INotificationSources _sources = sources ?? throw new ArgumentNullException(nameof(sources));

    private readonly INotificationRecipientDirectory _directory =
        directory ?? throw new ArgumentNullException(nameof(directory));

    private readonly ReportAuthority _authority = authority ?? throw new ArgumentNullException(nameof(authority));

    private readonly INotificationStore _store = store ?? throw new ArgumentNullException(nameof(store));

    private readonly IOperationalEmailSender _email = email ?? throw new ArgumentNullException(nameof(email));

    private readonly INotificationRealtime _realtime = realtime ?? throw new ArgumentNullException(nameof(realtime));

    private readonly NotificationOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    private readonly TimeProvider _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    public async Task<NotificationWorkResult> DispatchAsync(
        ClaimedOccurrence occurrence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(occurrence);

        IReadOnlyList<UserId>? written;

        try
        {
            written = await _queue
                .CompleteAsync(occurrence, await OutcomeAsync(occurrence, cancellationToken).ConfigureAwait(false), _clock.GetUtcNow(), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // A failure is handed back to be retried, never lost with the worker.
        catch (Exception fault)
#pragma warning restore CA1031
        {
            await _queue.ReleaseAsync(occurrence.Id, occurrence.ClaimToken, CancellationToken.None).ConfigureAwait(false);

            return new(NotificationWorkOutcome.Released, fault);
        }

        if (written is null)
        {
            return new(NotificationWorkOutcome.Superseded);
        }

        if (written.Count > 0)
        {
            await _realtime.NotificationsChangedAsync(occurrence.TenantId, written, cancellationToken).ConfigureAwait(false);
        }

        return new(NotificationWorkOutcome.Done);
    }

    private async Task<OccurrenceOutcome> OutcomeAsync(ClaimedOccurrence occurrence, CancellationToken cancellationToken)
    {
        NotificationCategory category = NotificationKinds.CategoryOf(occurrence.Kind);
        PermissionCode required = NotificationCatalog.RequiredPermission(category);

        IReadOnlyList<UserId> recipients;
        NotificationText text;

        if (NotificationCatalog.IsPersonal(occurrence.Kind))
        {
            (UserId RequestedBy, ReportType Type, bool Scheduled)? report = occurrence.SourceId is Guid execution
                ? await _sources.ReportAsync(occurrence.TenantId, execution, cancellationToken).ConfigureAwait(false)
                : null;

            if (report is not { } found)
            {
                return new OccurrenceOutcome([], new HashSet<UserId>());
            }

            // The member the report was generated for, and only while they may
            // still see reports. Being in the workspace is not enough.
            recipients = await _authority
                .StillHoldsAsync(occurrence.TenantId, found.RequestedBy, [required], cancellationToken)
                .ConfigureAwait(false)
                ? [found.RequestedBy]
                : [];

            text = NotificationCatalog.Describe(occurrence.Kind, reportType: found.Type);
        }
        else
        {
            recipients = await _directory
                .MembersHoldingAsync(occurrence.TenantId, required, cancellationToken)
                .ConfigureAwait(false);

            Guid? connection = occurrence.Kind == NotificationKind.WhatsAppDeliveryUnknown && occurrence.SourceId is Guid delivery
                ? await _sources.WhatsAppConnectionOfDeliveryAsync(occurrence.TenantId, delivery, cancellationToken).ConfigureAwait(false)
                : null;

            text = NotificationCatalog.Describe(occurrence.Kind, sourceId: occurrence.SourceId, connectionId: connection);
        }

        DateTimeOffset now = _clock.GetUtcNow();
        string key = string.Concat(occurrence.Kind.ToString(), ":", occurrence.OccurrenceKey);

        List<Notification> notifications =
        [
            .. recipients.Select(recipient => Notification.Create(
                occurrence.TenantId,
                recipient,
                occurrence.Id,
                occurrence.Kind,
                text.Title,
                text.Body,
                text.ActionPath,
                key,
                now,
                _options.Retention)),
        ];

        HashSet<UserId> emailed = [];

        if (_email.IsConfigured && category != NotificationCategory.Inbox)
        {
            foreach (UserId recipient in recipients)
            {
                NotificationPreferences preferences = await _store
                    .PreferencesForSendingAsync(occurrence.TenantId, recipient, now, cancellationToken)
                    .ConfigureAwait(false);

                if (preferences.EmailsFor(category))
                {
                    emailed.Add(recipient);
                }
            }
        }

        return new OccurrenceOutcome(notifications, emailed);
    }
}

/// <summary>
/// Sends one queued operational email, after asking again whether it still should.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A queued email is not an authorisation.</strong> Between queueing and
/// sending, a member can leave the workspace, lose the permission, switch the
/// category off or lose their account. Each is checked again, in that order, and
/// any of them skips the email with a stable code rather than sending it.
/// </para>
/// <para>
/// <strong>Truthful about ambiguity.</strong> A failure that certainly sent
/// nothing — no connection, a refused sign-in — may be retried, a bounded number
/// of times. A failure after the message may have been handed over is recorded as
/// Unknown and never retried, because the second attempt could be a duplicate.
/// </para>
/// <para>
/// <strong>The address is read from the account now.</strong> It is not copied
/// into the queue, and never comes from a request. So is the language the email is
/// worded in (Phase 37.3), and the report type of a report email, derived from the
/// notification's occurrence rather than stored.
/// </para>
/// </remarks>
public sealed class OperationalEmailDispatcher(
    INotificationEmailOutbox queue,
    INotificationStore store,
    ReportAuthority authority,
    IAccountDirectory accounts,
    INotificationSources sources,
    ITenantRepository tenants,
    IOperationalEmailSender sender,
    OperationalEmailLinks links,
    TimeProvider clock)
{
    /// <summary>How long a retriable failure waits, per attempt already made.</summary>
    public static readonly TimeSpan RetryBackoff = TimeSpan.FromMinutes(1);

    private readonly INotificationEmailOutbox _queue = queue ?? throw new ArgumentNullException(nameof(queue));

    private readonly INotificationStore _store = store ?? throw new ArgumentNullException(nameof(store));

    private readonly ReportAuthority _authority = authority ?? throw new ArgumentNullException(nameof(authority));

    private readonly IAccountDirectory _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));

    private readonly INotificationSources _sources = sources ?? throw new ArgumentNullException(nameof(sources));

    private readonly ITenantRepository _tenants = tenants ?? throw new ArgumentNullException(nameof(tenants));

    private readonly IOperationalEmailSender _sender = sender ?? throw new ArgumentNullException(nameof(sender));

    private readonly OperationalEmailLinks _links = links ?? throw new ArgumentNullException(nameof(links));

    private readonly TimeProvider _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    public async Task<NotificationEmailStatus?> SendAsync(
        ClaimedEmail claimed,
        int maxAttempts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claimed);

        if (!_sender.IsConfigured)
        {
            return await SkipAsync(claimed, NotificationEmailFailures.EmailNotConfigured, cancellationToken).ConfigureAwait(false);
        }

        Notification? notification = await _store
            .FindAsync(claimed.TenantId, claimed.NotificationId, cancellationToken)
            .ConfigureAwait(false);

        if (notification is null || notification.RecipientUserId != claimed.RecipientUserId)
        {
            return await SkipAsync(claimed, NotificationEmailFailures.NotificationGone, cancellationToken).ConfigureAwait(false);
        }

        IReadOnlyList<PermissionCode>? held = await _authority
            .PermissionsOfAsync(claimed.TenantId, claimed.RecipientUserId, cancellationToken)
            .ConfigureAwait(false);

        if (held is null)
        {
            return await SkipAsync(claimed, NotificationEmailFailures.NotAMember, cancellationToken).ConfigureAwait(false);
        }

        if (!held.Contains(NotificationCatalog.RequiredPermission(claimed.Category)))
        {
            return await SkipAsync(claimed, NotificationEmailFailures.PermissionRevoked, cancellationToken).ConfigureAwait(false);
        }

        NotificationPreferences preferences = await _store
            .PreferencesForSendingAsync(claimed.TenantId, claimed.RecipientUserId, _clock.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);

        if (!preferences.EmailsFor(claimed.Category))
        {
            return await SkipAsync(claimed, NotificationEmailFailures.PreferenceDisabled, cancellationToken).ConfigureAwait(false);
        }

        EmailAddress? address = await _accounts
            .FindEmailAsync(claimed.RecipientUserId, cancellationToken)
            .ConfigureAwait(false);

        if (address is not EmailAddress to || to.IsEmpty)
        {
            return await SkipAsync(claimed, NotificationEmailFailures.AccountUnavailable, cancellationToken).ConfigureAwait(false);
        }

        string? workspace = (await _tenants.FindByIdAsync(claimed.TenantId, cancellationToken).ConfigureAwait(false))?.Name.Value;

        // Worded now, in the language the recipient holds now (Phase 37.3): a
        // member who switched since the notification was created reads the new one.
        UserLanguage language = UserLanguages.OrDefault(
            await _accounts.FindLanguageAsync(claimed.RecipientUserId, cancellationToken).ConfigureAwait(false));

        ReportType? reportType = notification.Category == NotificationCategory.Reports
            ? await _sources.ReportTypeOfOccurrenceAsync(claimed.TenantId, notification.OccurrenceId, cancellationToken).ConfigureAwait(false)
            : null;

        OperationalEmailMessage message = OperationalEmailRenderer.Render(notification, to, workspace, _links, language, reportType);

        // The dispatch boundary (Phase 35), crossed immediately before the message
        // is handed to the SMTP server and never earlier. Up to here a crash leaves
        // the email Pending and reclaimable; from here a crash can only end in
        // Unknown, never a resend. If the email was reclaimed meanwhile, its new
        // owner sends it and this worker must not.
        if (!await _queue.BeginDispatchAsync(claimed.Id, claimed.ClaimToken, _clock.GetUtcNow(), CancellationToken.None).ConfigureAwait(false))
        {
            return null;
        }

        OperationalEmailResult result = await _sender.SendAsync(message, cancellationToken).ConfigureAwait(false);

        DateTimeOffset now = _clock.GetUtcNow();

        switch (result.Outcome)
        {
            case OperationalEmailOutcome.Sent:
                return await SettleAsync(claimed, NotificationEmailStatus.Sent, null, now, cancellationToken).ConfigureAwait(false);

            case OperationalEmailOutcome.NotSent when claimed.Attempts < maxAttempts:
                bool released = await _queue
                    .RetryLaterAsync(
                        claimed.Id,
                        claimed.ClaimToken,
                        result.FailureCode ?? NotificationEmailFailures.ConnectFailed,
                        now + (RetryBackoff * claimed.Attempts),
                        CancellationToken.None)
                    .ConfigureAwait(false);

                return released ? NotificationEmailStatus.Pending : null;

            case OperationalEmailOutcome.NotSent:
                return await SettleAsync(claimed, NotificationEmailStatus.Failed, NotificationEmailFailures.AttemptsExhausted, now, cancellationToken).ConfigureAwait(false);

            case OperationalEmailOutcome.Rejected:
                return await SettleAsync(claimed, NotificationEmailStatus.Failed, result.FailureCode ?? NotificationEmailFailures.Rejected, now, cancellationToken).ConfigureAwait(false);

            default:
                return await SettleAsync(claimed, NotificationEmailStatus.Unknown, NotificationEmailFailures.Ambiguous, now, cancellationToken).ConfigureAwait(false);
        }
    }

    private Task<NotificationEmailStatus?> SkipAsync(ClaimedEmail claimed, string code, CancellationToken cancellationToken) =>
        SettleAsync(claimed, NotificationEmailStatus.Skipped, code, _clock.GetUtcNow(), cancellationToken);

    /// <returns>The status recorded, or null when the claim was no longer this worker's.</returns>
    private async Task<NotificationEmailStatus?> SettleAsync(
        ClaimedEmail claimed,
        NotificationEmailStatus status,
        string? code,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        // Recorded even while the host is stopping: an outcome the transport
        // reported must not be lost to a cancelled token.
        await _queue.SettleAsync(claimed.Id, claimed.ClaimToken, status, code, now, CancellationToken.None).ConfigureAwait(false)
            ? status
            : null;
}

/// <summary>
/// Removes notifications the retention policy no longer keeps.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Only rows past their expiry, and only notification rows.</strong> A
/// notification's emails go with it. Occurrences are removed once processed and
/// as old as the retention period. No report, subscription, ledger row, document
/// or delivery is ever touched: those are the sources, and this is only the record
/// that somebody was told.
/// </para>
/// <para>
/// <strong>Safe to run on every instance at once.</strong> Each pass deletes a
/// bounded batch of rows chosen by expiry; two instances deleting the same row is
/// one delete that matched and one that matched nothing.
/// </para>
/// </remarks>
public sealed class NotificationRetention(
    INotificationRetentionStore store,
    NotificationOptions options,
    TimeProvider clock)
{
    private readonly INotificationRetentionStore _store = store ?? throw new ArgumentNullException(nameof(store));

    private readonly NotificationOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    private readonly TimeProvider _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    /// <returns>How many rows were removed.</returns>
    public async Task<int> SweepAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _clock.GetUtcNow();

        int notifications = await _store.DeleteExpiredNotificationsAsync(now, batchSize, cancellationToken).ConfigureAwait(false);

        int occurrences = await _store
            .DeleteProcessedOccurrencesAsync(now - _options.Retention, batchSize, cancellationToken)
            .ConfigureAwait(false);

        return notifications + occurrences;
    }
}
