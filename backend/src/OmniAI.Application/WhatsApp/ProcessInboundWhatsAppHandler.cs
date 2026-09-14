using System.Security.Cryptography;
using System.Text;
using OmniAI.Application.AiAgents.Persistence;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Conversations;
using OmniAI.Application.WebChat.Persistence;
using OmniAI.Application.WhatsApp.Persistence;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.WhatsApp;

namespace OmniAI.Application.WhatsApp;

/// <summary>
/// Takes one stored inbound message through the ordinary conversation runtime.
/// </summary>
/// <remarks>
/// <strong>This is a channel adapter and nothing more.</strong> It resolves
/// which conversation a WhatsApp customer's message belongs to, finds the agent,
/// and hands a <see cref="CustomerTurn"/> to the shared runner. Every decision
/// that matters — the advisory lock, idempotency, whether the AI or a person is
/// answering, retrieval, generation, ingestion order, the realtime announcement
/// — happens in the runner, exactly as it does for website chat.
/// <para>
/// <strong>An AI reply is queued for delivery, never sent from here.</strong>
/// Calling Meta inside this method would put an HTTP request to another company
/// into the same unit of work as the transcript write. The reply is stored, a
/// delivery row is queued beside it, and a separate worker does the sending.
/// </para>
/// <para>
/// <strong>No generation budget hook.</strong> Phase 16's budget meters
/// anonymous public traffic arriving from a browser; a WhatsApp message arrives
/// through a signed webhook on a number the workspace deliberately connected.
/// Applying the public-chat limiter here would let an unrelated flood of web
/// traffic silence a workspace's WhatsApp customers.
/// </para>
/// </remarks>
public sealed class ProcessInboundWhatsAppHandler(
    IWhatsAppConnectionRepository connections,
    IWhatsAppContactStore contacts,
    IWhatsAppOutbox outbox,
    IAiAgentRepository agents,
    CustomerTurnRunner runner,
    TimeProvider clock)
{
    private readonly IWhatsAppConnectionRepository _connections =
        connections ?? throw new ArgumentNullException(nameof(connections));

    private readonly IWhatsAppContactStore _contacts =
        contacts ?? throw new ArgumentNullException(nameof(contacts));

    private readonly IWhatsAppOutbox _outbox =
        outbox ?? throw new ArgumentNullException(nameof(outbox));

    private readonly IAiAgentRepository _agents =
        agents ?? throw new ArgumentNullException(nameof(agents));

    private readonly CustomerTurnRunner _runner =
        runner ?? throw new ArgumentNullException(nameof(runner));

    private readonly TimeProvider _clock =
        clock ?? throw new ArgumentNullException(nameof(clock));

    /// <summary>Processes one claimed message.</summary>
    /// <remarks>
    /// Returning normally means the message is settled and the worker should
    /// mark it processed. Anything the runtime raises propagates: the worker
    /// decides whether that is worth another attempt, because only it knows how
    /// many have already been made.
    /// </remarks>
    public async Task HandleAsync(
        ClaimedInboundMessage claimed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claimed);

        // Routing is re-read rather than trusted from the claim. A connection
        // can be disabled between a webhook arriving and a worker reaching it,
        // and a message that arrived while the channel was live must not be
        // answered after somebody switched it off.
        WhatsAppRoute? route = await _connections
            .FindRouteAsync(claimed.PhoneNumberId, cancellationToken)
            .ConfigureAwait(false);

        if (route is null || !route.IsServing || route.ConnectionId != claimed.ConnectionId)
        {
            // Settled, not retried. The channel is off; waiting will not switch
            // it back on, and the customer's message is already stored.
            return;
        }

        Versioned<AiAgent>? agent = await _agents
            .FindAsync(claimed.TenantId, route.AiAgentId, cancellationToken)
            .ConfigureAwait(false);

        if (agent is null || agent.Value.Status != AiAgentStatus.Active)
        {
            // The agent was withdrawn. There is nothing to answer with, and a
            // retry does not bring it back.
            return;
        }

        ConversationId conversationId = await _contacts
            .ResolveConversationAsync(route, claimed.CustomerId, _clock.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);

        CustomerTurnOutcome outcome = await _runner
            .RunAsync(
                new CustomerTurn(
                    claimed.TenantId,
                    conversationId,
                    agent.Value,
                    ClientMessageId.Create(KeyFor(claimed.ProviderMessageId)),
                    claimed.Text,
                    claimed.SentAt),
                beforeGeneration: null,
                cancellationToken)
            .ConfigureAwait(false);

        // A replay wrote nothing new, so there is nothing new to deliver.
        if (outcome is { Reply: ConversationMessageRef reply, WasReplay: false })
        {
            await _outbox
                .QueueAsync(
                    WhatsAppDelivery.Queue(
                        claimed.ConnectionId,
                        claimed.TenantId,
                        conversationId,
                        MessageId.From(reply.Id),
                        claimed.CustomerId,
                        reply.Text,
                        _clock.GetUtcNow()),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Meta's message id, reduced to what a client message id may hold.
    /// </summary>
    /// <remarks>
    /// A <c>wamid</c> is longer than the 64 characters a client message id
    /// allows and contains characters it does not. Rather than widen a value
    /// object the whole platform shares — and with it every index and every
    /// other channel's assumptions — the id is hashed to a short stable key:
    /// deterministic, so a redelivery produces the same key and replays rather
    /// than duplicating, and collision-free in practice because the input is
    /// already provider-unique.
    /// </remarks>
    internal static string KeyFor(ProviderMessageId providerMessageId)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(providerMessageId.Value));

        return "wa-" + Convert.ToHexStringLower(digest)[..40];
    }
}
