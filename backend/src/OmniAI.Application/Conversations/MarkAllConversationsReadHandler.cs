using OmniAI.Application.Authorization;
using OmniAI.Application.Conversations.Persistence;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Application.Conversations;

/// <summary>Mark every conversation of a workspace read, for the caller (Phase 39.1).</summary>
public sealed record MarkAllConversationsReadCommand(Guid TenantId);

/// <summary>How many conversations of a workspace are unread for the caller (Phase 39.1).</summary>
public sealed record CountUnreadConversationsQuery(Guid TenantId);

/// <summary>
/// "Mark all as read" for the inbox, and the count its confirmation shows (note 6).
/// </summary>
/// <remarks>
/// <para>
/// <strong>The whole workspace, for the caller only</strong> (owner decision Q2).
/// <c>conversations.view</c> and nothing else, like the single marker: a member who
/// may read the conversations may say they have read them. The markers are the
/// caller's own, resolved from the session; there is no field that names a member,
/// and another workspace's conversations are never in the statement.
/// </para>
/// <para>
/// <strong>One statement, no loop, no <c>If-Match</c>.</strong> Markers only move
/// forward, so a repeat, a retry and two tabs all converge, and the second call
/// answers zero.
/// </para>
/// <para>
/// <strong>Announced to the caller's own tabs only</strong>, structurally, and only
/// when something moved. The single marker announces nothing (one tab, one
/// conversation); marking everything changes every row of the caller's other open
/// inboxes, so they are told to re-read. The announcement never fails the write.
/// </para>
/// </remarks>
public sealed class MarkAllConversationsReadHandler(
    TenantAuthorizer authorizer,
    IConversationInboxStore store,
    IConversationReadRealtime realtime,
    TimeProvider clock)
{
    private readonly TenantAuthorizer _authorizer =
        authorizer ?? throw new ArgumentNullException(nameof(authorizer));

    private readonly IConversationInboxStore _store =
        store ?? throw new ArgumentNullException(nameof(store));

    private readonly IConversationReadRealtime _realtime =
        realtime ?? throw new ArgumentNullException(nameof(realtime));

    private readonly TimeProvider _clock =
        clock ?? throw new ArgumentNullException(nameof(clock));

    /// <returns>
    /// How many conversations were marked, or <see langword="null"/> when the caller
    /// is not a member of this workspace.
    /// </returns>
    /// <exception cref="PermissionDeniedException">A member without <c>conversations.view</c>.</exception>
    public async Task<int?> MarkAllAsync(
        MarkAllConversationsReadCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        TenantId tenantId = TenantId.From(command.TenantId);

        if (!await _authorizer
            .RequireAsync(tenantId, PlatformPermissions.ConversationsView, cancellationToken)
            .ConfigureAwait(false))
        {
            return null;
        }

        UserId reader = _authorizer.RequireCurrentUser();

        int marked = await _store
            .MarkAllReadAsync(tenantId, reader, _clock.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);

        if (marked > 0)
        {
            await AnnounceQuietlyAsync(tenantId, reader, cancellationToken).ConfigureAwait(false);
        }

        return marked;
    }

    /// <returns>
    /// The count, or <see langword="null"/> when the caller is not a member of this
    /// workspace.
    /// </returns>
    /// <exception cref="PermissionDeniedException">A member without <c>conversations.view</c>.</exception>
    public async Task<int?> CountUnreadAsync(
        CountUnreadConversationsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        TenantId tenantId = TenantId.From(query.TenantId);

        if (!await _authorizer
            .RequireAsync(tenantId, PlatformPermissions.ConversationsView, cancellationToken)
            .ConfigureAwait(false))
        {
            return null;
        }

        return await _store
            .CountUnreadAsync(tenantId, _authorizer.RequireCurrentUser(), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Announces, and never throws: the markers are already stored.</summary>
    private async Task AnnounceQuietlyAsync(TenantId tenantId, UserId reader, CancellationToken cancellationToken)
    {
        try
        {
            await _realtime.ConversationsReadAsync(tenantId, reader, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Deliberately broad and silent, as RealtimeAnnouncements is: the
            // transport logs its own failures, and the next read finds the markers.
        }
    }
}
