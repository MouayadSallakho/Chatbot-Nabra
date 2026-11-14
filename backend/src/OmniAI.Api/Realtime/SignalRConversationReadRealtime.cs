using Microsoft.AspNetCore.SignalR;
using OmniAI.Application.Conversations;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Api.Realtime;

/// <summary>
/// Tells one member's open admin sessions that their read markers moved
/// (Phase 39.1, <c>conversationsRead</c>).
/// </summary>
/// <remarks>
/// The same shape as <see cref="SignalRNotificationRealtime"/>: the member's own
/// group, the workspace id as the whole payload, through the Redis backplane when
/// one is configured, and a failure logged and swallowed.
/// </remarks>
internal sealed partial class SignalRConversationReadRealtime(
    IHubContext<WorkspaceInboxHub> hub,
    ILogger<SignalRConversationReadRealtime> logger) : IConversationReadRealtime
{
    private readonly IHubContext<WorkspaceInboxHub> _hub = hub ?? throw new ArgumentNullException(nameof(hub));

    private readonly ILogger<SignalRConversationReadRealtime> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task ConversationsReadAsync(
        TenantId tenantId,
        UserId member,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _hub.Clients
                .Group(RealtimeGroups.ForMember(tenantId.Value, member.Value))
                .SendAsync(RealtimeEvents.ConversationsRead, tenantId.Value, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            LogSignalFailed(_logger, failure);
        }
    }

    [LoggerMessage(
        EventId = 4111,
        Level = LogLevel.Warning,
        Message = "Could not tell a member's other sessions that their conversations were marked read. They will see it on their next read.")]
    private static partial void LogSignalFailed(ILogger logger, Exception failure);
}
