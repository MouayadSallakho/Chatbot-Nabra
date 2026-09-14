using OmniAI.Application.Conversations;
using OmniAI.Application.Conversations.Persistence;
using OmniAI.Application.WhatsApp.Persistence;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.WhatsApp;

namespace OmniAI.Application.WhatsApp;

/// <summary>
/// Queues a colleague's reply for delivery when the customer is on WhatsApp.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The admin never calls Meta.</strong> A reply is appended to the
/// transcript and a delivery row is queued beside it; a worker does the sending.
/// That keeps an HTTP request to another company out of the request a person is
/// waiting on, and it means a reply cannot be lost because the network was down
/// at the moment it was written.
/// </para>
/// <para>
/// <strong>Silent for every other channel.</strong> A website visitor's browser
/// is already reading the transcript, so a reply reaches them by being stored.
/// Asking "is this conversation on WhatsApp" is one indexed read, and it is the
/// only thing this adds to the reply path.
/// </para>
/// </remarks>
public sealed class WhatsAppReplyDispatcher(
    IWhatsAppContactStore contacts,
    IWhatsAppOutbox outbox,
    CustomerServiceWindow window,
    IConversationRepository conversations,
    TimeProvider clock)
    : IOutboundReplyDispatcher
{
    private readonly IWhatsAppContactStore _contacts =
        contacts ?? throw new ArgumentNullException(nameof(contacts));

    private readonly IWhatsAppOutbox _outbox =
        outbox ?? throw new ArgumentNullException(nameof(outbox));

    private readonly CustomerServiceWindow _window =
        window ?? throw new ArgumentNullException(nameof(window));

    private readonly IConversationRepository _conversations =
        conversations ?? throw new ArgumentNullException(nameof(conversations));

    private readonly TimeProvider _clock =
        clock ?? throw new ArgumentNullException(nameof(clock));

    public async Task DispatchAsync(
        TenantId tenantId,
        ConversationId conversationId,
        MessageId messageId,
        string text,
        CancellationToken cancellationToken = default)
    {
        WhatsAppConversationLink? link = await _contacts
            .FindLinkAsync(conversationId, cancellationToken)
            .ConfigureAwait(false);

        if (link is null || link.TenantId != tenantId)
        {
            // Not a WhatsApp conversation. Nothing to deliver, and nothing to
            // report: the reply is already where the customer will read it.
            return;
        }

        DateTimeOffset now = _clock.GetUtcNow();

        WhatsAppDelivery delivery = WhatsAppDelivery.Queue(
            link.ConnectionId,
            tenantId,
            conversationId,
            messageId,
            link.CustomerId,
            text,
            now);

        // <strong>Queued even when the channel is off or the window has
        // closed.</strong> The worker re-checks both immediately before sending
        // and records an honest failure if either still holds. Refusing to queue
        // here would leave a reply the colleague can see in the transcript with
        // no record anywhere of why it never arrived.
        await _outbox.QueueAsync(delivery, cancellationToken).ConfigureAwait(false);

        if (!await _window
            .IsOpenAsync(_conversations, conversationId, cancellationToken)
            .ConfigureAwait(false))
        {
            // Settled now rather than left for the worker, so the inbox shows the
            // reason while the colleague is still looking at the screen they
            // typed it on.
            await _outbox
                .MarkFailedAsync(
                    delivery.Id,
                    CustomerServiceWindow.ClosedReason,
                    now,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
