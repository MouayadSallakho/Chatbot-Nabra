using OmniAI.Application.Concurrency;

namespace OmniAI.Application.Conversations;

/// <summary>
/// A conversation was changed between being read and being written.
/// </summary>
/// <remarks>
/// Two people looking at the same inbox is the normal case, not the exceptional
/// one, and the second to press a button must be told rather than silently
/// undoing the first. Handing a conversation back to the AI because somebody's
/// screen was showing a version from a minute ago is exactly what a precondition
/// exists to prevent.
/// </remarks>
public sealed class ConversationVersionMismatchException(ConcurrencyToken expected)
    : Exception(
        "This conversation was changed somewhere else after it was read. "
        + "Reload it and try again.")
{
    /// <summary>The version the caller presented.</summary>
    public ConcurrencyToken Expected { get; } = expected;
}

/// <summary>
/// A reply was attempted on a conversation the AI is still answering.
/// </summary>
/// <remarks>
/// <strong>Pressing Send does not quietly take a conversation over.</strong>
/// Taking over is a decision with consequences for the customer — the AI stops
/// answering them, indefinitely, until somebody notices — and inferring it from
/// a keystroke would mean a colleague who opened the wrong tab and typed a note
/// silently stopped a working automation. The two actions stay two actions.
/// </remarks>
public sealed class ConversationNotHandledByHumanException()
    : Exception(
        "This conversation is being answered by the AI. Take it over first, then reply.");

/// <summary>
/// A conversation was routed to somebody who cannot take it.
/// </summary>
/// <remarks>
/// Covers both "not a member of this workspace" and "a member who may not reply",
/// deliberately with different sentences: the first is a mistake worth naming
/// plainly because it should be impossible from the UI, and the second is a
/// routine consequence of how roles are set up that an administrator can fix.
/// </remarks>
public sealed class ConversationAssigneeIneligibleException : Exception
{
    private ConversationAssigneeIneligibleException(string message)
        : base(message)
    {
    }

    /// <summary>The named person does not belong to this workspace.</summary>
    public static ConversationAssigneeIneligibleException NotAMember() =>
        new("That person is not a member of this workspace.");

    /// <summary>The named member holds no permission to reply.</summary>
    public static ConversationAssigneeIneligibleException CannotReply() =>
        new(
            "That member cannot reply to conversations, so assigning this one to "
            + "them would leave the customer waiting. Give them the reply "
            + "permission first.");
}
