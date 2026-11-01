using OmniAI.Application.Authorization;
using OmniAI.Application.Conversations.Persistence;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Application.Conversations;

/// <summary>Mark a conversation read, up to a message, for the caller.</summary>
/// <param name="Sequence">The ingestion sequence of the newest message the reader has seen.</param>
public sealed record MarkConversationReadCommand(
    Guid TenantId,
    Guid ConversationId,
    long? Sequence);

/// <summary>
/// Moves the caller's own read marker on a conversation forward.
/// </summary>
/// <remarks>
/// <para>
/// <strong><c>conversations.view</c> and nothing else.</strong> Reading a
/// conversation is what makes it read; a member who may see a transcript may say
/// they have seen it. The marker is the caller's own, resolved from the session:
/// nobody can mark a conversation read for somebody else, and there is no field
/// that names a member.
/// </para>
/// <para>
/// <strong>No <c>If-Match</c>.</strong> The marker only moves forward and is
/// clamped to the newest message, so two tabs, a stale tab and a retried request
/// all converge on the same answer without a version to compare.
/// </para>
/// <para>
/// <strong>No lock and no announcement.</strong> The marker is private to one
/// member and changes nothing anybody else sees, so it neither waits for a turn
/// nor tells the inbox anything.
/// </para>
/// </remarks>
public sealed class MarkConversationReadHandler(
    TenantAuthorizer authorizer,
    IConversationInboxStore store,
    TimeProvider clock)
{
    private readonly TenantAuthorizer _authorizer =
        authorizer ?? throw new ArgumentNullException(nameof(authorizer));

    private readonly IConversationInboxStore _store =
        store ?? throw new ArgumentNullException(nameof(store));

    private readonly TimeProvider _clock =
        clock ?? throw new ArgumentNullException(nameof(clock));

    /// <returns>
    /// <see langword="true"/> when the marker was stored; <see langword="false"/>
    /// when the caller is not a member or no such conversation belongs to this
    /// workspace, indistinguishably.
    /// </returns>
    /// <exception cref="ConversationQueryInvalidException">The sequence is missing or negative.</exception>
    /// <exception cref="PermissionDeniedException">A member without <c>conversations.view</c>.</exception>
    public async Task<bool> HandleAsync(
        MarkConversationReadCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Sequence is not long sequence || sequence < 0)
        {
            throw ConversationQueryInvalidException.InvalidReadSequence();
        }

        TenantId tenantId = TenantId.From(command.TenantId);
        ConversationId conversationId = ConversationId.From(command.ConversationId);

        if (!await _authorizer
            .RequireAsync(tenantId, PlatformPermissions.ConversationsView, cancellationToken)
            .ConfigureAwait(false))
        {
            return false;
        }

        UserId reader = _authorizer.RequireCurrentUser();

        return await _store
            .MarkReadAsync(tenantId, conversationId, reader, sequence, _clock.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);
    }
}
