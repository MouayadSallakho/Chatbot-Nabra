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
/// <para>
/// Each carries its own stable <see cref="Code"/> (Phase 38.7b, BA-4), so a client
/// words the two differently without reading the sentence.
/// </para>
/// </remarks>
public sealed class ConversationAssigneeIneligibleException : Exception
{
    private ConversationAssigneeIneligibleException(string code, string message)
        : base(message) =>
        Code = code;

    /// <summary>The stable code, such as <c>conversations.assignee.not_member</c>.</summary>
    public string Code { get; }

    /// <summary>The named person does not belong to this workspace.</summary>
    public static ConversationAssigneeIneligibleException NotAMember() =>
        new(
            "conversations.assignee.not_member",
            "That person is not a member of this workspace.");

    /// <summary>The named member holds no permission to reply.</summary>
    public static ConversationAssigneeIneligibleException CannotReply() =>
        new(
            "conversations.assignee.cannot_reply",
            "That member cannot reply to conversations, so assigning this one to "
            + "them would leave the customer waiting. Give them the reply "
            + "permission first.");
}

/// <summary>
/// A conversation read or read marker was asked for in a way that cannot be
/// served: an unknown filter, contradictory cursors, an unknown <c>include</c>,
/// or a negative sequence.
/// </summary>
/// <remarks>
/// Carries a stable <see cref="Code"/> for the problem body, so the client words
/// the refusal by code (problem-codes.md). The message stays English and names no
/// conversation, workspace or person.
/// </remarks>
public sealed class ConversationQueryInvalidException : Exception
{
    private ConversationQueryInvalidException(string code, string message)
        : base(message) =>
        Code = code;

    /// <summary>The stable code, such as <c>conversations.filter.invalid</c>.</summary>
    public string Code { get; }

    /// <summary>The list was asked for a filter it does not know.</summary>
    public static ConversationQueryInvalidException UnknownFilter() =>
        new(
            "conversations.filter.invalid",
            "That filter is not known. Use needs-person, or leave the filter out.");

    /// <summary><c>after</c> was combined with <c>before</c> or <c>tail</c>.</summary>
    public static ConversationQueryInvalidException CursorConflict() =>
        new(
            "transcript.cursor.conflict",
            "Read forwards with after, or backwards with before or tail, not both.");

    /// <summary><c>include</c> named something the transcript cannot add.</summary>
    public static ConversationQueryInvalidException UnknownInclude() =>
        new(
            "transcript.include.invalid",
            "That include is not known. Use events, or leave include out.");

    /// <summary>The read marker was missing or negative.</summary>
    public static ConversationQueryInvalidException InvalidReadSequence() =>
        new(
            "conversations.read.sequence.invalid",
            "The sequence must be a message sequence of zero or more.");
}
