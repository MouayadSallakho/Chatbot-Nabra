using OmniAI.Application.WebChat.Hosted;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.WebChat;

namespace OmniAI.Application.Conversations.Realtime;

/// <summary>Ask for permission to listen to one conversation.</summary>
/// <param name="AccessToken">
/// The existing conversation credential, presented exactly as it is for every
/// other read: from the hosted cookie, or from the embedded header.
/// </param>
public sealed record IssueRealtimeTicketCommand(
    Guid ConversationId,
    string? AccessToken,
    WebChatClientMode Mode);

/// <summary>
/// Exchanges a conversation credential for a short-lived realtime ticket.
/// </summary>
/// <remarks>
/// <strong>The exchange is the security property.</strong> The credential is
/// checked here by the same authoriser every other public read goes through, so
/// everything that stops a read stops a ticket: an unknown conversation, a
/// credential that does not match, a session that has expired, a transport that
/// does not match the session, and — for an embedded session — a website that is
/// no longer on the workspace's allowlist. A conversation id on its own mints
/// nothing, because on its own it authorises nothing.
/// <para>
/// <strong>The allowlist is re-read at this moment, not trusted from when the
/// widget loaded.</strong> A website removed while a visitor has the widget open
/// stops being able to mint tickets immediately, which is the same answer
/// removing it gives to reads and sends.
/// </para>
/// <para>
/// It returns nothing about the conversation. Not the transcript, not the
/// workspace, not the agent — the response is a ticket and a lifetime, and a
/// caller learns only that their credential worked.
/// </para>
/// </remarks>
public sealed class IssueRealtimeTicketHandler(
    PublicWebChatAccess access,
    IRealtimeTickets tickets)
{
    private readonly PublicWebChatAccess _access =
        access ?? throw new ArgumentNullException(nameof(access));

    private readonly IRealtimeTickets _tickets =
        tickets ?? throw new ArgumentNullException(nameof(tickets));

    /// <returns>
    /// A ticket, or <see langword="null"/> when the credential does not open this
    /// conversation — the same answer, and the same 404 at the edge, as every
    /// other refusal on these routes.
    /// </returns>
    public async Task<RealtimeTicket?> HandleAsync(
        IssueRealtimeTicketCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        ConversationId conversationId = ConversationId.From(command.ConversationId);

        WebChatSession? session = await _access
            .AuthoriseAsync(conversationId, command.AccessToken, command.Mode, cancellationToken)
            .ConfigureAwait(false);

        if (session is null)
        {
            return null;
        }

        // Minted against the session's own conversation rather than the one the
        // request named. They are the same value here -- the authoriser looked
        // the session up by it -- and taking it from the authorised object is
        // what keeps them the same if that ever stops being true.
        return await _tickets
            .IssueAsync(session.ConversationId, command.Mode, cancellationToken)
            .ConfigureAwait(false);
    }
}
