using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Conversations;

/// <summary>
/// Tells whoever is watching that a conversation is no longer what they last
/// read — and nothing else about it.
/// </summary>
/// <remarks>
/// <strong>An identifier is the entire payload, on purpose.</strong> The
/// temptation is to push the new message down the wire and let the client append
/// it, which would be faster and is the wrong shape. A realtime fan-out is not an
/// authorisation boundary: the moment transcript text travels on it, every
/// question the HTTP reads answer carefully — is this credential still valid, is
/// this website still approved, has this session expired, does this employee
/// still have permission — has to be answered again, correctly, on a connection
/// that was authorised once, minutes ago. Sending only "something changed" means
/// the worst a stale listener can learn is that something changed.
/// <para>
/// <strong>Arrival order is not transcript order.</strong> Two instances behind a
/// backplane deliver when they deliver. The client re-reads through the ordinary
/// API and the ingestion sequence decides what the transcript is, exactly as it
/// does for a reader who pressed Refresh.
/// </para>
/// <para>
/// <strong>Delivery is best effort and callers must treat it as such.</strong>
/// Nothing here is allowed to fail a write. A reply that was stored and not
/// announced is a reply the next read finds; a reply that was refused because the
/// announcement failed is a reply the customer never got. Implementations
/// swallow their own failures, and callers do not await this before committing.
/// </para>
/// </remarks>
public interface IConversationRealtimeNotifier
{
    /// <summary>
    /// Announces that a conversation changed, to the workspace's staff and to the
    /// visitor having it.
    /// </summary>
    /// <param name="tenantId">The workspace whose staff should re-read.</param>
    /// <param name="conversationId">The conversation that changed.</param>
    Task ConversationChangedAsync(
        TenantId tenantId,
        ConversationId conversationId,
        CancellationToken cancellationToken = default);
}
