namespace OmniAI.Domain.Conversations;

/// <summary>
/// Who is answering a conversation right now: the workspace's AI, or one of its
/// people.
/// </summary>
/// <remarks>
/// <strong>Deliberately not folded into <see cref="ConversationStatus"/>.</strong>
/// Open/Closed says whether the conversation accepts messages at all. This says
/// who produces the replies while it is open. They are independent — a
/// conversation being handled by a person is still open, and closing one says
/// nothing about who was answering it — and a single enum spanning both would
/// force every reader to know which members were lifecycle and which were
/// routing. The first time somebody wrote <c>Status == Human</c> next to
/// <c>Status == Closed</c> the mistake would already be shipped.
/// <para>
/// <strong>Two members, because two is what the platform can actually do.</strong>
/// No <c>Pending</c>, no <c>Requested</c>, no <c>Queued</c>: there is no customer
/// "request a human" flow, no queue and no assignment-driven state machine in
/// this phase, so a member representing one would be a state nothing can enter
/// and nothing can leave. Assignment is a separate, optional fact and does not
/// move this value.
/// </para>
/// <para>
/// There is no zero member, so an uninitialised value is detectable rather than
/// silently meaning "AI".
/// </para>
/// </remarks>
public enum ConversationHandling
{
    /// <summary>
    /// The AI answers new customer turns, using the full retrieval and
    /// generation runtime.
    /// </summary>
    Ai = 1,

    /// <summary>
    /// A person answers. New customer turns are recorded and left for a human:
    /// nothing is retrieved, nothing is generated, and nothing is spent.
    /// </summary>
    Human = 2,
}
