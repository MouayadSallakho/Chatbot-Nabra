using System.Globalization;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Tenants;
using OmniAI.Domain;
using OmniAI.Domain.Notifications;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Application.Notifications;

/// <summary>A member's preferences, and whether this deployment can email at all.</summary>
public sealed record NotificationPreferencesView(NotificationPreferences Preferences, bool EmailConfigured);

/// <summary>What a member chose. Every category is required: there is no partial update.</summary>
public sealed record SaveNotificationPreferencesCommand(
    Guid TenantId,
    bool? Reports,
    bool? Usage,
    bool? Billing,
    bool? Knowledge,
    bool? ChannelHealth,
    ConcurrencyToken Expected);

/// <summary>
/// The notification centre and email preferences, for the signed-in member only.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Always the caller's own.</strong> Every operation is addressed by the
/// workspace in the route and the member in the session. There is no parameter
/// naming another member, so there is no endpoint through which one member reads,
/// marks or configures another's notifications — administrators included.
/// </para>
/// <para>
/// <strong>Membership is checked first, and is the only gate.</strong> A member
/// sees their own notifications whatever their permissions, because each one was
/// only ever written for somebody entitled to it. A non-member is told the
/// workspace is not there, and learns nothing about its notifications.
/// </para>
/// <para>
/// <strong>Reading is monotonic.</strong> Marking a notification read twice, or
/// marking all read with nothing unread, succeeds and changes nothing. No version
/// is needed: there is no edit to lose.
/// </para>
/// </remarks>
public sealed class NotificationHandlers(
    TenantAccessGuard access,
    INotificationStore store,
    IOperationalEmailSender email,
    INotificationRealtime realtime,
    TimeProvider clock)
{
    private readonly TenantAccessGuard _access = access ?? throw new ArgumentNullException(nameof(access));

    private readonly INotificationStore _store = store ?? throw new ArgumentNullException(nameof(store));

    private readonly IOperationalEmailSender _email = email ?? throw new ArgumentNullException(nameof(email));

    private readonly INotificationRealtime _realtime = realtime ?? throw new ArgumentNullException(nameof(realtime));

    private readonly TimeProvider _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    /// <returns>The page, or <see langword="null"/> when the caller is not a member.</returns>
    public async Task<NotificationPage?> ListAsync(
        Guid tenantId,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken = default)
    {
        int number = page ?? 1;
        int size = pageSize ?? NotificationPage.DefaultPageSize;

        if (number < 1)
        {
            throw new DomainValidationException("Page must be 1 or greater.");
        }

        if (size < 1 || size > NotificationPage.MaxPageSize)
        {
            throw new DomainValidationException(
                string.Create(CultureInfo.InvariantCulture, $"Page size must be between 1 and {NotificationPage.MaxPageSize}."));
        }

        if (await MemberAsync(tenantId, cancellationToken).ConfigureAwait(false) is not (TenantId workspace, UserId me))
        {
            return null;
        }

        (IReadOnlyList<Notification> items, int total) = await _store
            .ListAsync(workspace, me, number, size, cancellationToken)
            .ConfigureAwait(false);

        int unread = await _store.CountUnreadAsync(workspace, me, cancellationToken).ConfigureAwait(false);

        return new NotificationPage(items, number, size, total, unread);
    }

    public async Task<int?> UnreadCountAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        if (await MemberAsync(tenantId, cancellationToken).ConfigureAwait(false) is not (TenantId workspace, UserId me))
        {
            return null;
        }

        return await _store.CountUnreadAsync(workspace, me, cancellationToken).ConfigureAwait(false);
    }

    /// <returns>
    /// <see langword="null"/> for a non-member; <see langword="false"/> when the
    /// member has no such notification.
    /// </returns>
    public async Task<bool?> MarkReadAsync(
        Guid tenantId,
        Guid notificationId,
        CancellationToken cancellationToken = default)
    {
        if (await MemberAsync(tenantId, cancellationToken).ConfigureAwait(false) is not (TenantId workspace, UserId me))
        {
            return null;
        }

        if (notificationId == Guid.Empty)
        {
            return false;
        }

        bool found = await _store
            .MarkReadAsync(workspace, me, NotificationId.From(notificationId), _clock.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);

        if (found)
        {
            // The member's other tabs refresh their count too.
            await _realtime.NotificationsChangedAsync(workspace, [me], cancellationToken).ConfigureAwait(false);
        }

        return found;
    }

    /// <returns>How many were marked, or <see langword="null"/> for a non-member.</returns>
    public async Task<int?> MarkAllReadAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        if (await MemberAsync(tenantId, cancellationToken).ConfigureAwait(false) is not (TenantId workspace, UserId me))
        {
            return null;
        }

        int marked = await _store.MarkAllReadAsync(workspace, me, _clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);

        if (marked > 0)
        {
            await _realtime.NotificationsChangedAsync(workspace, [me], cancellationToken).ConfigureAwait(false);
        }

        return marked;
    }

    public async Task<Versioned<NotificationPreferencesView>?> PreferencesAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        if (await MemberAsync(tenantId, cancellationToken).ConfigureAwait(false) is not (TenantId workspace, UserId me))
        {
            return null;
        }

        Versioned<NotificationPreferences> stored = await _store
            .PreferencesAsync(workspace, me, _clock.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);

        return new Versioned<NotificationPreferencesView>(
            new NotificationPreferencesView(stored.Value, _email.IsConfigured),
            stored.Version);
    }

    /// <exception cref="NotificationPreferencesVersionMismatchException">Changed since it was read.</exception>
    public async Task<Versioned<NotificationPreferencesView>?> SavePreferencesAsync(
        SaveNotificationPreferencesCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Reports is not bool reports
            || command.Usage is not bool usage
            || command.Billing is not bool billing
            || command.Knowledge is not bool knowledge
            || command.ChannelHealth is not bool channelHealth)
        {
            throw new DomainValidationException(
                "Say whether each category is emailed: reports, usage, billing, knowledge and channelHealth.");
        }

        if (await MemberAsync(command.TenantId, cancellationToken).ConfigureAwait(false) is not (TenantId workspace, UserId me))
        {
            return null;
        }

        DateTimeOffset now = _clock.GetUtcNow();

        Versioned<NotificationPreferences> stored = await _store
            .PreferencesAsync(workspace, me, now, cancellationToken)
            .ConfigureAwait(false);

        NotificationPreferences preferences = stored.Value;
        preferences.Choose(reports, usage, billing, knowledge, channelHealth, now);

        ConcurrencyToken version = await _store
            .SavePreferencesAsync(preferences, command.Expected, cancellationToken)
            .ConfigureAwait(false);

        return new Versioned<NotificationPreferencesView>(
            new NotificationPreferencesView(preferences, _email.IsConfigured),
            version);
    }

    private async Task<(TenantId Workspace, UserId Me)?> MemberAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        UserId me = _access.RequireCurrentUser();
        TenantId workspace = TenantId.From(tenantId);

        return await _access.IsCurrentUserMemberAsync(workspace, cancellationToken).ConfigureAwait(false)
            ? (workspace, me)
            : null;
    }
}
