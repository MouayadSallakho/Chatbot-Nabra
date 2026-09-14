using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Conversations;

/// <summary>
/// Announcing a change without letting the announcement break the change.
/// </summary>
/// <remarks>
/// <strong>Best effort has to be enforced at the call site, not promised in a
/// comment.</strong> <see cref="IConversationRealtimeNotifier"/> documents that
/// implementations swallow their own failures, and the shipped one does — but a
/// contract kept only by convention is kept only until somebody writes a second
/// implementation, or a test double, or a decorator that logs. Every one of
/// those would then be able to fail a write that had already succeeded.
/// <para>
/// The failure that matters is concrete: a reply is stored, the backplane is
/// unreachable, the announcement throws, and the caller reports an error for a
/// message the customer has already been sent. A reply that was stored and not
/// announced is a reply the next read finds. A reply refused because the
/// announcement failed is a reply nobody ever gets.
/// </para>
/// <para>
/// So every caller in this layer announces through here, and the guarantee holds
/// whatever is behind the interface.
/// </para>
/// </remarks>
internal static class RealtimeAnnouncements
{
    /// <summary>
    /// Announces a conversation change, and never throws.
    /// </summary>
    /// <remarks>
    /// Cancellation is deliberately not special-cased into a rethrow. A caller
    /// whose request was abandoned still wants the change it already committed
    /// to be announced if it can be, and does not want the abandonment reported
    /// twice.
    /// </remarks>
    internal static async Task AnnounceQuietlyAsync(
        this IConversationRealtimeNotifier realtime,
        TenantId tenantId,
        ConversationId conversationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(realtime);

        try
        {
            await realtime
                .ConversationChangedAsync(tenantId, conversationId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Deliberately broad, and deliberately silent here. What went wrong
            // is the transport's business and the transport logs it; this layer
            // knows only that the write it just made is safe either way.
        }
    }
}
