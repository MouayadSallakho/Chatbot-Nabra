using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Application.Conversations;

/// <summary>
/// Tells one member's open admin sessions that their read markers in a workspace
/// moved (Phase 39.1, mark all read).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Structural, and the member's own.</strong> What travels is an event name
/// and the workspace id: no conversation id, no count, no content. It goes to the
/// member's own group, never to the workspace, because a read marker is private to
/// one person and nobody else's screen changes. The client re-reads its list
/// through the authorised HTTP read.
/// </para>
/// <para>
/// <strong>Never fails the write.</strong> Markers that moved and were not
/// announced are found by the next read; implementations swallow their own
/// failures, and the caller swallows anything that escapes.
/// </para>
/// </remarks>
public interface IConversationReadRealtime
{
    Task ConversationsReadAsync(
        TenantId tenantId,
        UserId member,
        CancellationToken cancellationToken = default);
}
