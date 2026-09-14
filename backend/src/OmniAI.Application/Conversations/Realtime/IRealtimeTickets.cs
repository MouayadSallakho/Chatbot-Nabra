using OmniAI.Domain.Conversations;
using OmniAI.Domain.WebChat;

namespace OmniAI.Application.Conversations.Realtime;

/// <summary>
/// A ticket, as it is handed to the browser that asked for one.
/// </summary>
/// <param name="Value">
/// The raw ticket. Held in memory by the page that minted it, put in the
/// realtime connection request, and never written anywhere — not to a log, not
/// to storage, and not to the database.
/// </param>
/// <param name="ExpiresInSeconds">
/// How long it stays usable, so the client can mint another before reconnecting
/// rather than discovering the old one died.
/// </param>
public sealed record RealtimeTicket(string Value, int ExpiresInSeconds);

/// <summary>
/// What a presented ticket turns out to authorise.
/// </summary>
/// <param name="ConversationId">The one conversation it may listen to.</param>
/// <param name="Mode">The transport it was minted for.</param>
public sealed record RealtimeTicketClaim(ConversationId ConversationId, WebChatClientMode Mode);

/// <summary>
/// Short-lived permission to listen to one conversation, and nothing else.
/// </summary>
/// <remarks>
/// <strong>This type exists so a conversation credential never has to travel in a
/// URL.</strong> A realtime connection is not an ordinary request: the browser
/// APIs that open one cannot set headers, so anything authorising it ends up in
/// the query string — where it is logged by every proxy, kept in every access
/// log, and visible in the developer tools of the page that opened it. A hosted
/// chat credential lives in an <c>HttpOnly</c> cookie precisely so that script
/// cannot read it; putting it in a URL would undo that in one line.
/// <para>
/// So the credential stays where it is, and is spent once — through the existing,
/// fully authorised transports — to mint this instead. Everything that makes the
/// conversation credential worth protecting is absent here: a ticket reads no
/// API, writes nothing, names one conversation, expires in about a minute, and
/// cannot be exchanged back for the credential that produced it. The worst a
/// leaked one buys, for the minute it lives, is being told that one conversation
/// changed.
/// </para>
/// <para>
/// <strong>The transport is part of what a ticket says.</strong> A hosted ticket
/// admits a hosted listener and an embedded ticket admits an embedded one, so
/// the separation Phase 18 built between the two transports is not quietly
/// bridged by the realtime channel.
/// </para>
/// <para>
/// <strong>Only a digest is stored.</strong> Whatever holds these has the same
/// problem every credential store has: somebody reading it must not learn a
/// usable secret. The raw value exists in the response and in the client, and
/// the server keeps a hash it can compare against.
/// </para>
/// </remarks>
public interface IRealtimeTickets
{
    /// <summary>
    /// Mints a ticket for a conversation the caller has already been authorised
    /// on.
    /// </summary>
    /// <remarks>
    /// This performs no authorisation of its own and must never be reached
    /// before the caller's credential has been checked. It is the last step of an
    /// authorised request, not an alternative to being one.
    /// </remarks>
    Task<RealtimeTicket> IssueAsync(
        ConversationId conversationId,
        WebChatClientMode mode,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a presented ticket.
    /// </summary>
    /// <returns>
    /// What it authorises, or <see langword="null"/> when it is unknown, expired
    /// or malformed — deliberately indistinguishably.
    /// </returns>
    Task<RealtimeTicketClaim?> RedeemAsync(
        string? ticket,
        CancellationToken cancellationToken = default);
}
