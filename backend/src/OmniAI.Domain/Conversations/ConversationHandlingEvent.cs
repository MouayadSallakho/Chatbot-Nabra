using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Domain.Conversations;

/// <summary>What changed about who answers a conversation.</summary>
/// <remarks>There is no zero member, so an uninitialised value is detectable.</remarks>
public enum ConversationHandlingEventKind
{
    /// <summary>A person took the conversation over from the AI.</summary>
    TakenOver = 1,

    /// <summary>A person handed the conversation back to the AI.</summary>
    ReturnedToAi = 2,
}

/// <summary>
/// A person taking a conversation over, or handing it back to the AI: who, when,
/// and where in the transcript.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Its own record, never a message.</strong> A <c>System</c> message would
/// put "Sara took over" into the transcript every public transport serves, so a
/// customer would read which employee picked up their chat. These rows are read
/// by the workspace's own transcript and by nothing a visitor can reach.
/// </para>
/// <para>
/// <strong>Raised only by <see cref="Conversation"/>, and only when handling
/// actually changes.</strong> Pressing Take over on a conversation a person is
/// already handling changes nothing, so it records nothing: the history says what
/// happened, not what was clicked.
/// </para>
/// <para>
/// <strong>History starts when this record was introduced.</strong> Handoffs that
/// happened earlier left no trace that could be turned into one, and inventing
/// them would be the platform asserting things nobody saw.
/// </para>
/// Written once and never changed.
/// </remarks>
public sealed class ConversationHandlingEvent
{
    private ConversationHandlingEvent(
        ConversationHandlingEventId id,
        ConversationId conversationId,
        TenantId tenantId,
        ConversationHandlingEventKind kind,
        UserId actor,
        DateTimeOffset occurredAt,
        long afterSequence)
    {
        Id = id;
        ConversationId = conversationId;
        TenantId = tenantId;
        Kind = kind;
        Actor = actor;
        OccurredAt = occurredAt;
        AfterSequence = afterSequence;
    }

    /// <summary>Identity.</summary>
    public ConversationHandlingEventId Id { get; }

    /// <summary>The conversation whose handling changed.</summary>
    public ConversationId ConversationId { get; }

    /// <summary>
    /// The conversation's workspace, carried so the actor's membership can be a
    /// composite key into this workspace.
    /// </summary>
    public TenantId TenantId { get; }

    /// <summary>Taken over, or returned to the AI.</summary>
    public ConversationHandlingEventKind Kind { get; }

    /// <summary>The member who did it.</summary>
    public UserId Actor { get; }

    /// <summary>When, in UTC.</summary>
    public DateTimeOffset OccurredAt { get; }

    /// <summary>
    /// The ingestion sequence of the newest message when the change happened, or
    /// zero when the conversation had no message yet.
    /// </summary>
    /// <remarks>
    /// Read under the conversation lock, so a reply the AI was still producing when
    /// somebody pressed Take over is already stored and the event lands after it,
    /// which is the order the customer saw.
    /// </remarks>
    public long AfterSequence { get; }

    /// <summary>Records a change. Called by <see cref="Conversation"/> only.</summary>
    internal static ConversationHandlingEvent Record(
        ConversationId conversationId,
        TenantId tenantId,
        ConversationHandlingEventKind kind,
        UserId actor,
        DateTimeOffset occurredAt,
        long afterSequence)
    {
        RequireValid(actor, afterSequence);

        return new ConversationHandlingEvent(
            ConversationHandlingEventId.New(),
            conversationId,
            tenantId,
            kind,
            actor,
            occurredAt.ToUniversalTime(),
            afterSequence);
    }

    /// <summary>
    /// Checks the arguments of a change before the aggregate applies it, so a bad
    /// argument never leaves handling changed with nothing recorded.
    /// </summary>
    /// <returns>Always <see langword="true"/>; it throws instead of returning false.</returns>
    internal static bool RequireValid(UserId actor, long afterSequence)
    {
        if (actor.IsEmpty)
        {
            throw new DomainValidationException("A handling change must name the member who made it.");
        }

        if (afterSequence < 0)
        {
            throw new DomainValidationException("A transcript position cannot be negative.");
        }

        return true;
    }
}
