namespace OmniAI.Domain.Conversations;

/// <summary>
/// How far one conversation turn got.
/// </summary>
/// <remarks>
/// Three states, because a turn spans an external call that can fail and the
/// difference between "still running", "answered" and "did not answer" is
/// exactly what a repeated request needs to know.
/// <para>
/// <see cref="Failed"/> is terminal. A turn is identified by the client's own
/// message id, and returning the same failure to a repeat of the same request is
/// what stops a retry loop from spending the workspace's provider budget. Somebody
/// deliberately trying again sends a new message, which is a new turn.
/// </para>
/// There is no zero member, so an uninitialised value is detectable.
/// </remarks>
public enum ConversationTurnStatus
{
    /// <summary>The customer's message is stored and the reply is being produced.</summary>
    Generating = 1,

    /// <summary>A reply was produced and stored.</summary>
    Completed = 2,

    /// <summary>No reply was produced. The customer's message is still stored.</summary>
    Failed = 3,
}
