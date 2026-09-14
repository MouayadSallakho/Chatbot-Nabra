using OmniAI.Application.WebChat;
using OmniAI.Application.WebChat.Hosted;

namespace OmniAI.Api.WebChat;

/// <summary>Publish a chat link for one of the workspace's agents.</summary>
public sealed record CreateWebChatEndpointRequest(Guid? AiAgentId);

/// <summary>One website chat endpoint, on the wire.</summary>
/// <remarks>
/// <strong>The public key is published here on purpose, and it is the only
/// key-shaped value in this file.</strong> There is no access token, no session
/// and no visitor data in any admin response — those belong to the people having
/// the conversations, not to the people administering the channel.
/// </remarks>
public sealed record WebChatEndpointResponse(
    Guid Id,
    Guid AiAgentId,
    string AiAgentName,
    string AiAgentStatus,
    string PublicKey,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static WebChatEndpointResponse From(WebChatEndpointView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        return new WebChatEndpointResponse(
            view.Id,
            view.AiAgentId,
            view.AiAgentName,
            view.AiAgentStatus,
            view.PublicKey,
            view.Status,
            view.CreatedAt,
            view.UpdatedAt);
    }
}

/// <summary>What the hosted chat page is told about where it is running.</summary>
public sealed record PublicWebChatResponse(string AgentName, bool Available)
{
    public static PublicWebChatResponse From(PublicWebChatView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        return new PublicWebChatResponse(view.AgentName, view.Available);
    }
}

/// <summary>A conversation a visitor has just started.</summary>
/// <remarks>
/// Carries no credential. The secret that continues this conversation leaves in a
/// cookie the browser's script cannot read, and there is deliberately no field
/// here it could be put in instead.
/// </remarks>
public sealed record StartedConversationResponse(Guid ConversationId, string AgentName);

/// <summary>A visitor's message.</summary>
/// <remarks>
/// <strong>Two fields, and neither of them is anything the server owns.</strong>
/// There is no sender, no tenant, no agent, no timestamp, no message id and no
/// ingestion sequence, because a visitor must not be able to choose any of them —
/// a request shape that accepted a sender would be a request shape in which a
/// visitor could write the agent's replies.
/// </remarks>
public sealed record SendChatMessageRequest(string? ClientMessageId, string? Text);

/// <summary>One message, as the hosted page renders it.</summary>
public sealed record ChatMessageResponse(
    Guid Id,
    string Sender,
    string Text,
    DateTimeOffset SentAt)
{
    public static ChatMessageResponse From(PublicChatMessageView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        return new ChatMessageResponse(view.Id, view.Sender, view.Text, view.SentAt);
    }

    /// <summary>The same, for a reply that may legitimately not exist.</summary>
    /// <remarks>
    /// Separate from <see cref="From"/> rather than relaxing it, so the one case
    /// where null is expected is the one place it is allowed. A null reply
    /// anywhere else is still a bug worth throwing over.
    /// </remarks>
    public static ChatMessageResponse? FromOrNull(PublicChatMessageView? view) =>
        view is null ? null : From(view);
}

/// <summary>A page of a visitor's own transcript.</summary>
public sealed record ChatMessagePageResponse(
    IReadOnlyList<ChatMessageResponse> Items,
    long? NextCursor,
    string Handling);

/// <summary>What one visitor turn produced.</summary>
/// <param name="Reply">
/// The agent's answer, or <see langword="null"/> when a person is handling this
/// conversation and no AI reply was produced.
/// </param>
/// <param name="Handling">
/// <c>"Ai"</c> or <c>"Human"</c>, so the visitor's client can say what is
/// actually happening instead of inferring it from a missing field. A turn with
/// no reply and no explanation is indistinguishable from a broken one, and
/// showing an error for a message that arrived perfectly is the wrong answer.
/// <para>
/// It names a mode and never a person: no employee, no assignment, no workspace.
/// </para>
/// </param>
public sealed record ChatTurnResponse(
    ChatMessageResponse CustomerMessage,
    ChatMessageResponse? Reply,
    string Handling);

/// <summary>Permission to listen to one conversation, briefly.</summary>
/// <param name="Ticket">
/// Opaque, single-purpose and short-lived. It reads no API and writes nothing;
/// all it does is admit a listener to one conversation's notifications.
/// </param>
/// <param name="ExpiresInSeconds">
/// So a client can mint a fresh one before reconnecting rather than discovering
/// the old one died mid-handshake.
/// </param>
public sealed record RealtimeTicketResponse(string Ticket, int ExpiresInSeconds);
