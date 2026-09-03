namespace OmniAI.Domain.Conversations;

/// <summary>
/// Lifecycle state of a <see cref="Conversation"/>.
/// </summary>
/// <remarks>
/// Deliberately two states. Waiting-for-agent, assigned, escalated, bot-handling
/// and human-handling are all part of the human handoff design, which has not
/// been done; inventing those states now would encode guesses about routing rules
/// that do not exist. Archival is a retention concern.
/// There is no zero member, so an uninitialised value is detectable.
/// </remarks>
public enum ConversationStatus
{
    /// <summary>The conversation is live and accepts new messages.</summary>
    Open = 1,

    /// <summary>The conversation is finished and accepts no new messages.</summary>
    Closed = 2,
}
