using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Conversations;

/// <summary>
/// Gets a reply a person wrote to wherever the customer actually is.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The inbox composer does not know which channel it is writing
/// to.</strong> A colleague types a reply, it is appended to the transcript, and
/// this decides whether anything further has to happen to get it in front of the
/// customer. For website chat the answer is nothing: the visitor's browser is
/// already reading the transcript. For WhatsApp it is a queued delivery.
/// </para>
/// <para>
/// <strong>Best effort, and never able to fail a reply.</strong> The reply is
/// already stored and already announced by the time this is called. An
/// implementation that threw would turn a delivery problem into a failed request
/// for a message the customer's transcript already contains, and the colleague
/// would send it again.
/// </para>
/// </remarks>
public interface IOutboundReplyDispatcher
{
    /// <summary>Arranges for a stored reply to reach the customer.</summary>
    /// <remarks>
    /// Does nothing for a conversation on a channel that needs no delivery, and
    /// nothing for a message already queued: sending one reply twice is worse
    /// than not sending it at all.
    /// </remarks>
    Task DispatchAsync(
        TenantId tenantId,
        ConversationId conversationId,
        MessageId messageId,
        string text,
        CancellationToken cancellationToken = default);
}
