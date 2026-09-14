using Microsoft.AspNetCore.SignalR;
using OmniAI.Application.Conversations;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;

namespace OmniAI.Api.Realtime;

/// <summary>
/// Announces conversation changes over SignalR.
/// </summary>
/// <remarks>
/// <strong>It never throws, and that is a requirement rather than defensive
/// habit.</strong> This is called from inside the write paths — after a customer's
/// message is committed, after a reply is stored, after a takeover is saved — and
/// a failure to announce must not become a failure to do the thing. A reply that
/// was stored and not announced is a reply the next read finds; a reply refused
/// because Redis was unreachable is a reply the customer never got.
/// <para>
/// <strong>Both audiences hear the same sentence.</strong> The workspace's staff
/// and the visitor are told a conversation changed, and each fetches what they
/// are allowed to see through their own authorised route. That there is only one
/// message shape is what makes it hard to leak something to the wrong one.
/// </para>
/// <para>
/// The failure is logged rather than swallowed in silence: realtime that has
/// quietly stopped working looks exactly like a platform where nothing is
/// happening, and the only way to tell the difference is a log line.
/// </para>
/// </remarks>
internal sealed partial class SignalRConversationNotifier(
    IHubContext<WorkspaceInboxHub> workspace,
    IHubContext<PublicChatHub> visitor,
    ILogger<SignalRConversationNotifier> logger) : IConversationRealtimeNotifier
{
    private readonly IHubContext<WorkspaceInboxHub> _workspace =
        workspace ?? throw new ArgumentNullException(nameof(workspace));

    private readonly IHubContext<PublicChatHub> _visitor =
        visitor ?? throw new ArgumentNullException(nameof(visitor));

    private readonly ILogger<SignalRConversationNotifier> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task ConversationChangedAsync(
        TenantId tenantId,
        ConversationId conversationId,
        CancellationToken cancellationToken = default)
    {
        Guid conversation = conversationId.Value;

        try
        {
            await _workspace.Clients
                .Group(RealtimeGroups.ForTenant(tenantId.Value))
                .SendAsync(RealtimeEvents.ConversationChanged, conversation, cancellationToken)
                .ConfigureAwait(false);

            await _visitor.Clients
                .Group(RealtimeGroups.ForConversation(conversation))
                .SendAsync(RealtimeEvents.ConversationChanged, conversation, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            // Deliberately broad. Anything the transport or the backplane can
            // raise -- a dropped Redis connection, a serialisation fault, a
            // connection closing mid-send -- is a missed notification and not a
            // reason to fail the write that produced it.
            LogAnnouncementFailed(_logger, conversation, failure);
        }
    }

    [LoggerMessage(
        EventId = 4100,
        Level = LogLevel.Warning,
        Message =
            "Could not announce a change to conversation {ConversationId}. The change was saved; "
            + "clients will see it on their next read.")]
    private static partial void LogAnnouncementFailed(
        ILogger logger,
        Guid conversationId,
        Exception failure);
}
