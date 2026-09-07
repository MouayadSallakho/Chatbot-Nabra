namespace OmniAI.Application.WebChat;

/// <summary>
/// The bounds this platform puts on anonymous website chat.
/// </summary>
/// <remarks>
/// <strong>One place, because these are product decisions rather than
/// incidental constants.</strong> Each of them is a limit an anonymous stranger
/// runs into, so each needs a reason written next to it rather than a number
/// buried in a handler.
/// </remarks>
public sealed class WebChatPolicy
{
    /// <summary>
    /// The longest message a visitor may send, in characters.
    /// </summary>
    /// <remarks>
    /// <strong>Deliberately far below the Domain's own 32,000-character
    /// bound.</strong> That bound exists to keep any message from becoming a
    /// storage and rendering problem, and it applies to messages this platform
    /// ingests from channels it trusts. This one applies to an unauthenticated
    /// browser that nobody has vouched for, and it is a different question: how
    /// much text should a stranger be able to push into a workspace's database,
    /// and into a paid provider call, per request. Four thousand characters is
    /// several times longer than anybody types into a chat box and small enough
    /// that abusing it is not worth anybody's while.
    /// <para>
    /// It also sits comfortably under <c>AiTurn.MaxTextLength</c>, so every
    /// message this surface accepts can actually be replayed to the provider as a
    /// turn. A bound above it would have let somebody store a message that could
    /// never be part of the conversation's context.
    /// </para>
    /// <para>
    /// Oversized input is refused, never trimmed. Silently cutting somebody's
    /// message in half and answering the half is worse than telling them it was
    /// too long.
    /// </para>
    /// </remarks>
    public int MaxIncomingMessageLength { get; init; } = 4_000;

    /// <summary>
    /// The most messages a public reader may ask for in one page.
    /// </summary>
    public int MaxMessagePageSize { get; init; } = 100;

    /// <summary>The page size used when the caller does not ask for one.</summary>
    public int DefaultMessagePageSize { get; init; } = 50;
}
