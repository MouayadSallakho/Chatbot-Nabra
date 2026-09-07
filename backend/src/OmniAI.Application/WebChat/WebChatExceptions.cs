using OmniAI.Application.Concurrency;

namespace OmniAI.Application.WebChat;

/// <summary>
/// A website chat endpoint was not at the version the caller said it was.
/// </summary>
/// <remarks>
/// The same conditional-write contract every mutable resource here uses. Two
/// administrators can have the same endpoint open, and the one who saves second
/// must be told rather than silently undoing the first — taking a chat endpoint
/// back out of service by accident is exactly the outcome a precondition exists
/// to prevent.
/// </remarks>
public sealed class WebChatEndpointVersionMismatchException(ConcurrencyToken expected)
    : Exception(
        "This website chat endpoint was changed somewhere else after it was read. "
        + "Reload it and try again.")
{
    /// <summary>The version the caller presented.</summary>
    public ConcurrencyToken Expected { get; } = expected;
}

/// <summary>
/// A website chat endpoint was asked for on an agent that cannot serve one.
/// </summary>
public sealed class WebChatAgentUnusableException : Exception
{
    private WebChatAgentUnusableException(string message)
        : base(message)
    {
    }

    /// <summary>The named agent does not exist in this workspace.</summary>
    public static WebChatAgentUnusableException NotFound() =>
        new("That AI agent does not exist in this workspace.");

    /// <summary>This workspace already publishes a chat link for that agent.</summary>
    public static WebChatAgentUnusableException AlreadyPublished() =>
        new(
            "This agent already has a website chat link. Use the existing one, or "
            + "disable it before creating another.");
}

/// <summary>
/// A public request reached an endpoint that is not serving the public.
/// </summary>
/// <remarks>
/// <strong>One exception for several causes, deliberately.</strong> A draft
/// endpoint, a disabled endpoint, a disabled agent and an agent that has been
/// taken out of service all produce this, and the caller cannot tell which. A
/// visitor has no business learning that a workspace has a chat endpoint it has
/// not published yet, or that its agent was switched off an hour ago — that is
/// internal state, and a public URL is not a status page.
/// </remarks>
public sealed class WebChatUnavailableException()
    : Exception(
        "This chat is not available right now. Please try again later, or contact "
        + "the business another way.");

/// <summary>
/// A message the client has already sent is still being answered.
/// </summary>
/// <remarks>
/// The honest answer to a duplicate that arrives while the first copy is still in
/// flight — a double click, or a retry that fired before the first response came
/// back. It is a conflict rather than an error: nothing is wrong, the same work is
/// simply already happening, and the caller should wait for the answer it is
/// already going to get rather than paying for a second one.
/// </remarks>
public sealed class WebChatTurnInProgressException()
    : Exception("That message is still being answered. Wait for the reply.");

/// <summary>
/// A message the client has already sent was attempted and did not get a reply.
/// </summary>
/// <remarks>
/// <strong>A failed turn is terminal, and re-presenting its id does not retry
/// it.</strong> Every reply costs the workspace money, so the platform will not
/// spend it again on a request that may simply be an automatic retry of something
/// that already failed. Somebody who genuinely wants to try again is sending a new
/// message, which carries a new id and is a new turn.
/// </remarks>
public sealed class WebChatTurnAlreadyFailedException()
    : Exception(
        "That message was already sent and could not be answered. Send it again as "
        + "a new message to try once more.");
