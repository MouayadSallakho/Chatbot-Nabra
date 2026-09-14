using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using OmniAI.Application.Authorization;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Tenants;

namespace OmniAI.Api.Realtime;

/// <summary>
/// Live updates for the people who work at a workspace.
/// </summary>
/// <remarks>
/// <strong>The connection is the existing session, and nothing else.</strong>
/// The hub is <c>[Authorize]</c>, so the same cookie that authenticates every
/// admin request authenticates this one; there is no hub token, no separate
/// login and no second way in. A browser that is not signed in cannot connect.
/// <para>
/// <strong>Joining a workspace's group is a server decision.</strong> A client
/// asks to subscribe and names a workspace; the server checks membership and
/// <c>conversations.view</c> before adding the connection to anything. A client
/// cannot name a group directly — SignalR has no such call — and a client that
/// names a workspace it does not belong to is refused rather than quietly
/// ignored, because silently succeeding would leave a screen waiting for events
/// that were never coming.
/// </para>
/// <para>
/// <strong>Nothing is checked once and trusted for ever, because nothing is sent
/// that would matter.</strong> The only message is a conversation id, and every
/// piece of content behind it is fetched afterwards through the ordinary HTTP
/// reads, which re-authorise from scratch. A member whose permission is revoked
/// while connected keeps receiving identifiers and can no longer read a single
/// message behind them.
/// </para>
/// </remarks>
[Authorize]
internal sealed class WorkspaceInboxHub(TenantAuthorizer authorizer) : Hub
{
    private readonly TenantAuthorizer _authorizer =
        authorizer ?? throw new ArgumentNullException(nameof(authorizer));

    /// <summary>
    /// Asks to receive this workspace's conversation events.
    /// </summary>
    /// <exception cref="HubException">
    /// The caller does not belong to that workspace, or may not read its
    /// conversations. One message for both, for the same reason the HTTP reads
    /// answer 404 for both: whether a workspace exists is not something an
    /// outsider gets to learn.
    /// </exception>
    public async Task Subscribe(Guid tenantId)
    {
        TenantId tenant = TenantId.From(tenantId);

        bool member = await _authorizer
            .RequireAsync(tenant, PlatformPermissions.ConversationsView, Context.ConnectionAborted)
            .ConfigureAwait(false);

        if (!member)
        {
            throw new HubException("No such workspace.");
        }

        await Groups
            .AddToGroupAsync(
                Context.ConnectionId,
                RealtimeGroups.ForTenant(tenantId),
                Context.ConnectionAborted)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Stops receiving a workspace's events, for a client that has navigated away.
    /// </summary>
    /// <remarks>
    /// Needs no authorisation: leaving a group you are not in does nothing, and
    /// leaving one you are in is always something you are allowed to do.
    /// </remarks>
    public Task Unsubscribe(Guid tenantId) =>
        Groups.RemoveFromGroupAsync(
            Context.ConnectionId,
            RealtimeGroups.ForTenant(tenantId),
            Context.ConnectionAborted);
}
