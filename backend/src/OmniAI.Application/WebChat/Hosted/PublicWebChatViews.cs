namespace OmniAI.Application.WebChat.Hosted;

/// <summary>
/// What a hosted chat page is told about the endpoint it is rendering.
/// </summary>
/// <remarks>
/// <strong>The smallest thing that makes a usable page.</strong> A visitor needs
/// to know who they are talking to and whether they can talk at all. Everything
/// else a workspace knows about itself stays behind the admin boundary: there is
/// no tenant id, no agent id, no instructions, no model, no provider, no member,
/// no role and no other endpoint. Anybody may fetch this by knowing a public key,
/// so the rule is that nothing here would matter if it were printed on a
/// billboard.
/// </remarks>
/// <param name="AgentName">The display name the visitor sees.</param>
/// <param name="Available">
/// Whether a conversation can be started right now. False covers a disabled
/// endpoint, an agent that is not active, and an endpoint still in draft, without
/// saying which — the page shows one unavailable state and the visitor learns
/// nothing about the workspace's internal configuration.
/// </param>
public sealed record PublicWebChatView(string AgentName, bool Available);

/// <summary>A conversation that has just been started by a visitor.</summary>
/// <remarks>
/// The credential is deliberately absent. It leaves through a cookie the browser
/// cannot read, never through a response body a script could keep.
/// </remarks>
/// <param name="ConversationId">
/// The conversation's identity. Not a credential: knowing it grants nothing.
/// </param>
/// <param name="AgentName">The agent answering, so the page can render at once.</param>
public sealed record StartedWebChatConversation(Guid ConversationId, string AgentName);

/// <summary>One message as the hosted page renders it.</summary>
/// <param name="Id">The message's identity.</param>
/// <param name="Sender">
/// <c>Customer</c> or <c>AiAgent</c>. A name rather than a number, so the page
/// never depends on an enum's ordinal.
/// </param>
/// <param name="Text">Plain text. Never markup, and never rendered as markup.</param>
/// <param name="SentAt">For display beside the message.</param>
public sealed record PublicChatMessageView(
    Guid Id,
    string Sender,
    string Text,
    DateTimeOffset SentAt);

/// <summary>A page of a visitor's own transcript.</summary>
/// <param name="Items">Messages in ingestion order, oldest first.</param>
/// <param name="NextCursor">
/// Where to continue, or <see langword="null"/> at the end of the conversation.
/// </param>
public sealed record PublicChatMessagePage(
    IReadOnlyList<PublicChatMessageView> Items,
    long? NextCursor);

/// <summary>The outcome of one visitor turn.</summary>
/// <param name="CustomerMessage">What the visitor said, as stored.</param>
/// <param name="Reply">The agent's answer.</param>
public sealed record WebChatTurnResult(
    PublicChatMessageView CustomerMessage,
    PublicChatMessageView Reply);
