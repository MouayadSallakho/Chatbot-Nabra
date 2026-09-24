using Microsoft.AspNetCore.SignalR;
using OmniAI.Application.Notifications;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Api.Realtime;

/// <summary>
/// Tells a member's open admin sessions that their notifications changed.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Structural invalidation, not delivery.</strong> What travels is the
/// event name and the workspace id — no title, no body, no notification id, no
/// count. The client re-reads its list and unread count through the ordinary
/// authorised API, which is the only place the content lives.
/// </para>
/// <para>
/// <strong>Addressed to member groups only.</strong> Each recipient's group is
/// their own (workspace, member) pair; nothing is broadcast to the workspace.
/// Through the Redis backplane when one is configured, so every API instance's
/// connections hear it.
/// </para>
/// <para>
/// <strong>Never fails the caller.</strong> A missed signal leaves a badge a few
/// seconds stale until the next read; it is logged and swallowed, like the inbox
/// notifier's.
/// </para>
/// </remarks>
internal sealed partial class SignalRNotificationRealtime(
    IHubContext<WorkspaceInboxHub> hub,
    ILogger<SignalRNotificationRealtime> logger) : INotificationRealtime
{
    private readonly IHubContext<WorkspaceInboxHub> _hub = hub ?? throw new ArgumentNullException(nameof(hub));

    private readonly ILogger<SignalRNotificationRealtime> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task NotificationsChangedAsync(
        TenantId tenantId,
        IReadOnlyCollection<UserId> recipients,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recipients);

        if (recipients.Count == 0)
        {
            return;
        }

        try
        {
            await _hub.Clients
                .Groups([.. recipients.Select(recipient => RealtimeGroups.ForMember(tenantId.Value, recipient.Value))])
                .SendAsync(RealtimeEvents.NotificationsChanged, tenantId.Value, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            LogSignalFailed(_logger, recipients.Count, failure);
        }
    }

    [LoggerMessage(
        EventId = 4110,
        Level = LogLevel.Warning,
        Message = "Could not signal {Count} members that their notifications changed. They will see them on their next read.")]
    private static partial void LogSignalFailed(ILogger logger, int count, Exception failure);
}
