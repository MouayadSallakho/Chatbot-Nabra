using Microsoft.AspNetCore.SignalR;
using OmniAI.Application.Knowledge;
using OmniAI.Domain.Knowledge;
using OmniAI.Domain.Tenants;

namespace OmniAI.Api.Realtime;

/// <summary>
/// Tells a workspace's knowledge viewers that a document's indexing started or
/// settled (Phase 38.7a, BA-11).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Structural invalidation, not delivery.</strong> What travels is the
/// event name and the document id: no title, no content, no outcome. The client
/// re-reads the document through the authorised HTTP read.
/// </para>
/// <para>
/// <strong>Addressed to the knowledge group only.</strong> Joining it needs
/// membership and <c>knowledge.view</c> (<c>SubscribeKnowledge</c>). Through the
/// Redis backplane when one is configured, so every instance's connections hear it.
/// </para>
/// <para>
/// <strong>Never fails the caller.</strong> A missed signal leaves a document's
/// state stale until the next read; it is logged and swallowed, like the inbox
/// notifier's.
/// </para>
/// </remarks>
internal sealed partial class SignalRKnowledgeNotifier(
    IHubContext<WorkspaceInboxHub> hub,
    ILogger<SignalRKnowledgeNotifier> logger) : IKnowledgeRealtimeNotifier
{
    private readonly IHubContext<WorkspaceInboxHub> _hub = hub ?? throw new ArgumentNullException(nameof(hub));

    private readonly ILogger<SignalRKnowledgeNotifier> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task KnowledgeChangedAsync(
        TenantId tenantId,
        KnowledgeDocumentId documentId,
        CancellationToken cancellationToken = default)
    {
        Guid document = documentId.Value;

        try
        {
            await _hub.Clients
                .Group(RealtimeGroups.ForKnowledge(tenantId.Value))
                .SendAsync(RealtimeEvents.KnowledgeChanged, document, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            LogSignalFailed(_logger, document, failure);
        }
    }

    [LoggerMessage(
        EventId = 4120,
        Level = LogLevel.Warning,
        Message = "Could not announce that indexing of knowledge document {DocumentId} changed. It was recorded; clients will see it on their next read.")]
    private static partial void LogSignalFailed(ILogger logger, Guid documentId, Exception failure);
}
