using OmniAI.Domain.Conversations;

namespace OmniAI.Application.Analytics;

/// <summary>
/// What happened in the workspace during the period.
/// </summary>
/// <param name="ConversationsStarted">Conversations whose first moment falls in the period.</param>
/// <param name="CustomerMessages">Messages a customer sent in the period.</param>
/// <param name="AiMessages">Replies an AI agent produced in the period.</param>
/// <param name="HumanMessages">Replies a person sent in the period.</param>
/// <remarks>
/// System messages are counted separately and not folded into any of these:
/// a handover note is not something a customer asked or an agent answered, and
/// adding it to either would overstate the work done.
/// </remarks>
public sealed record AnalyticsSummary(
    long ConversationsStarted,
    long CustomerMessages,
    long AiMessages,
    long HumanMessages,
    long SystemMessages);

/// <summary>
/// What the workspace looks like right now.
/// </summary>
/// <remarks>
/// <strong>Deliberately not filtered by the period.</strong> "Open right now" is
/// a fact about the present; restricting it to the last thirty days would hide a
/// conversation that has been waiting for two months, which is exactly the one
/// somebody needs to see.
/// </remarks>
public sealed record AnalyticsSnapshot(
    long OpenConversations,
    long OpenHandledByAi,
    long OpenHandledByHuman);

/// <summary>One UTC day.</summary>
public sealed record AnalyticsDay(
    DateOnly Date,
    long ConversationsStarted,
    long CustomerMessages,
    long AiMessages,
    long HumanMessages);

/// <summary>One channel's share of the period.</summary>
public sealed record AnalyticsChannel(
    ChannelKind Channel,
    long ConversationsStarted,
    long CustomerMessages,
    long AiMessages,
    long HumanMessages);

/// <summary>
/// One AI agent's share of the period.
/// </summary>
/// <remarks>
/// <strong>Human replies are attributed to the agent's conversations, not to the
/// agent.</strong> A person replying in a conversation an agent was handling is
/// work that happened on that agent's watch; it is not something the agent did.
/// The column name says "in its conversations" for that reason.
/// </remarks>
public sealed record AnalyticsAgent(
    Guid AiAgentId,
    string Name,
    long ConversationsStarted,
    long CustomerMessages,
    long AiMessages,
    long HumanMessages);

/// <summary>
/// How AI turns ended.
/// </summary>
/// <remarks>
/// <strong>These are exact, because the runtime already records them.</strong> A
/// conversation turn is created before the provider is called and is settled
/// afterwards as completed or failed, so counting rows by status answers the
/// question without inference. <see cref="InProgress"/> is the unsettled
/// remainder — a turn still running, or one whose process died mid-flight.
/// <para>
/// There is deliberately no token count, no duration and no cost here. The
/// runtime does not persist them, and a number this platform cannot stand behind
/// is worse than a missing one.
/// </para>
/// </remarks>
public sealed record AnalyticsAiOperations(
    long Attempts,
    long Completed,
    long Failed,
    long InProgress);

/// <summary>
/// Where outbound WhatsApp messages got to.
/// </summary>
/// <remarks>
/// Every state the delivery model actually has, including <c>Sending</c> and
/// <c>Unknown</c>. Folding either into a neighbour would make the columns add up
/// to something other than the number of messages sent.
/// </remarks>
public sealed record AnalyticsWhatsAppDelivery(
    long Pending,
    long Sending,
    long Sent,
    long Delivered,
    long Read,
    long Failed,
    long Unknown)
{
    /// <summary>Whether this workspace has any outbound WhatsApp history at all.</summary>
    public bool HasAny =>
        Pending + Sending + Sent + Delivered + Read + Failed + Unknown > 0;
}

/// <summary>The period a dashboard describes, as the API publishes it.</summary>
public sealed record AnalyticsPeriod(
    string Range,
    DateTimeOffset From,
    DateTimeOffset To,
    int Days,
    string TimeZone);

/// <summary>
/// One workspace's dashboard.
/// </summary>
/// <remarks>
/// <strong>Counts and safe dimensions only.</strong> Nothing here carries message
/// text, knowledge text, a customer's phone number, a WhatsApp id, an email, a
/// token, a ticket or a filename. Agent names are workspace-authored labels an
/// administrator already reads on every other screen.
/// </remarks>
public sealed record WorkspaceAnalytics(
    AnalyticsPeriod Period,
    AnalyticsSummary Summary,
    AnalyticsSnapshot Current,
    IReadOnlyList<AnalyticsDay> DailyActivity,
    IReadOnlyList<AnalyticsChannel> Channels,
    IReadOnlyList<AnalyticsAgent> Agents,
    AnalyticsAiOperations AiOperations,
    AnalyticsWhatsAppDelivery? WhatsAppDelivery);
