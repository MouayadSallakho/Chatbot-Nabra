using OmniAI.Application.Conversations;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Tests.Conversations;

/// <summary>
/// A dispatcher for conversations that need no delivery.
/// </summary>
/// <remarks>
/// Website chat's real behaviour, not a stub standing in for something: a
/// visitor's browser is already reading the transcript, so storing the reply is
/// the whole of sending it. It records what it was asked to send so a test can
/// assert that nothing was.
/// </remarks>
internal sealed class NoOutboundDelivery : IOutboundReplyDispatcher
{
    /// <summary>Every message this was asked to deliver.</summary>
    public List<MessageId> Dispatched { get; } = [];

    public Task DispatchAsync(
        TenantId tenantId,
        ConversationId conversationId,
        MessageId messageId,
        string text,
        CancellationToken cancellationToken = default)
    {
        Dispatched.Add(messageId);

        return Task.CompletedTask;
    }
}
