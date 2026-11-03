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
/// <remarks>
/// Phase 37.5a adds what the embedded widget already shows — the greeting, the
/// accent colour and the theme — and, by owner decision D-3, the workspace's
/// display name. Each passes the billboard test: a visitor sees all of them on
/// the page anyway.
/// </remarks>
public sealed record PublicWebChatView(string AgentName, bool Available)
{
    /// <summary>The workspace's display name (D-3).</summary>
    public string WorkspaceName { get; init; } = string.Empty;

    /// <summary>The greeting the panel opens with, or <see langword="null"/>.</summary>
    public string? Greeting { get; init; }

    /// <summary>The accent colour, as <c>#rrggbb</c> or <c>#rgb</c>.</summary>
    public string AccentColor { get; init; } = Domain.WebChat.WebChatAccentColor.Default.Value;

    /// <summary><c>System</c>, <c>Light</c> or <c>Dark</c>.</summary>
    public string Theme { get; init; } = nameof(Domain.WebChat.WebChatTheme.System);
}

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
/// <param name="Handling">
/// Who is answering right now: <c>"Ai"</c> or <c>"Human"</c>.
/// <para>
/// <strong>Sent so a visitor can be told the truth after a takeover.</strong>
/// Without it the widget learns a person has stepped in only when that person
/// happens to reply, which can be several minutes of a customer watching an AI
/// that has stopped answering. Re-reading the transcript is what a client does
/// when it hears the conversation changed, so this is where the mode belongs.
/// </para>
/// <para>
/// It is the mode and nothing else — no employee, no assignment, no workspace.
/// </para>
/// </param>
public sealed record PublicChatMessagePage(
    IReadOnlyList<PublicChatMessageView> Items,
    long? NextCursor,
    string Handling);

/// <summary>The outcome of one visitor turn.</summary>
/// <param name="CustomerMessage">What the visitor said, as stored.</param>
/// <param name="Reply">
/// The agent's answer, or <see langword="null"/> when a person is handling this
/// conversation and no AI reply was produced — or ever will be, for this message.
/// </param>
/// <param name="Handling">
/// Who is answering: <c>"Ai"</c> or <c>"Human"</c>.
/// <para>
/// <strong>Sent so the visitor's client can tell the truth rather than guess
/// it.</strong> Without it, a turn that came back with no reply is
/// indistinguishable from one that failed, and the widget would have to choose
/// between showing an error for a message that arrived perfectly and showing
/// nothing at all. Neither is what happened.
/// </para>
/// <para>
/// It is the enum's name and nothing else. It carries no employee, no
/// assignment, no workspace and no identity — a visitor is told that a person is
/// looking after them, never which person.
/// </para>
/// </param>
public sealed record WebChatTurnResult(
    PublicChatMessageView CustomerMessage,
    PublicChatMessageView? Reply,
    string Handling);
