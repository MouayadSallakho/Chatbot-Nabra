using Microsoft.AspNetCore.SignalR;
using OmniAI.Application.Conversations.Realtime;
using OmniAI.Domain.WebChat;

namespace OmniAI.Api.Realtime;

/// <summary>
/// Live updates for the visitor having one conversation.
/// </summary>
/// <remarks>
/// <strong>Anonymous, and authorised by a ticket the connection carries.</strong>
/// There is no session cookie to rely on for the embedded transport — it runs on
/// somebody else's website, where third-party cookies do not exist — and the
/// hosted transport's cookie is <c>HttpOnly</c> and scoped to a path this
/// connection is not on. The ticket is what a visitor has already exchanged their
/// real credential for, through a route that authorised them fully.
/// <para>
/// <strong>The group a connection joins is decided by the ticket, never by the
/// client.</strong> The ticket names exactly one conversation, so a visitor
/// cannot listen to another one by asking: there is no method here to ask with.
/// The hub has no callable methods at all, which is the strongest form of "a
/// listener cannot do anything".
/// </para>
/// <para>
/// <strong>The transport is part of the ticket and is checked.</strong> A hosted
/// ticket admits a hosted listener and an embedded one admits an embedded
/// listener, so the separation between the two transports is not bridged here.
/// </para>
/// <para>
/// <strong>What a connected listener receives is a conversation id.</strong> Not
/// a message, not a sender, not a workspace. Everything else is fetched
/// afterwards through the transcript route, which re-checks the credential, the
/// session's expiry and — for an embedded session — whether the website is still
/// on the workspace's allowlist. So a website removed from the allowlist a second
/// after this connection opened can still be told that something changed, and
/// still cannot read what.
/// </para>
/// </remarks>
internal sealed class PublicChatHub : Hub
{
    /// <summary>
    /// The query parameter SignalR puts a bearer token in.
    /// </summary>
    /// <remarks>
    /// Fixed by the JavaScript client, which appends <c>access_token</c> when the
    /// transport cannot set headers — which is every browser WebSocket. This is
    /// precisely why the value in it is a ticket and never the conversation
    /// credential: it lands in proxy logs, access logs and the address bar of
    /// anybody's developer tools.
    /// </remarks>
    internal const string TicketParameter = "access_token";

    public override async Task OnConnectedAsync()
    {
        IRealtimeTickets tickets =
            Context.GetHttpContext()?.RequestServices.GetService<IRealtimeTickets>()
            ?? throw new HubException("Live updates are not available.");

        string? presented = Context.GetHttpContext()?.Request.Query[TicketParameter];

        RealtimeTicketClaim? claim = await tickets
            .RedeemAsync(presented, Context.ConnectionAborted)
            .ConfigureAwait(false);

        if (claim is null)
        {
            // Unknown, expired and malformed are one answer. Telling them apart
            // would let somebody probe for which of their guesses had ever been
            // a real ticket.
            throw new HubException("Live updates are not available.");
        }

        if (!Enum.IsDefined(claim.Mode) || claim.Mode is not (WebChatClientMode.Hosted or WebChatClientMode.Embedded))
        {
            throw new HubException("Live updates are not available.");
        }

        await Groups
            .AddToGroupAsync(
                Context.ConnectionId,
                RealtimeGroups.ForConversation(claim.ConversationId.Value),
                Context.ConnectionAborted)
            .ConfigureAwait(false);

        await base.OnConnectedAsync().ConfigureAwait(false);
    }
}
