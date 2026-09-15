using OmniAI.Domain.Ai;
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


/// <summary>
/// What this workspace's AI providers were actually asked to do, and what it cost.
/// </summary>
/// <remarks>
/// <para>
/// <strong>These are calls, not attempts.</strong> A turn refused before dispatch
/// — rate limited, handed to a person, a duplicate replayed from store — spends
/// nothing and appears here not at all. The AI-operations panel above counts
/// attempts; this counts money.
/// </para>
/// <para>
/// <strong>Tokens are the providers' own figures.</strong> Nothing here is
/// derived from text, and a call whose usage a provider did not report contributes
/// nothing to these sums rather than a zero.
/// </para>
/// <para>
/// <strong>This is accounting, not billing.</strong> An internal estimate of what
/// the platform spent with a provider: no customer charge, no markup, no plan, no
/// invoice.
/// </para>
/// </remarks>
/// <param name="Calls">Real provider requests dispatched in the period.</param>
/// <param name="Unresolved">
/// Dispatched and never settled, or settled as unknown. The provider may have
/// executed and billed these; they are neither successes nor zeroes.
/// </param>
/// <param name="UnratedCalls">
/// Succeeded calls this deployment has no configured price for. Real spend that
/// cannot be costed — never shown as free.
/// </param>
public sealed record AnalyticsAiUsage(
    long Calls,
    long GenerationCalls,
    long EmbeddingCalls,
    long Succeeded,
    long Failed,
    long Unresolved,
    long GenerationInputTokens,
    long GenerationCachedInputTokens,
    long GenerationOutputTokens,
    long GenerationReasoningTokens,
    long EmbeddingInputTokens,
    long UnratedCalls,
    IReadOnlyList<AnalyticsRatedCost> RatedCost)
{
    /// <summary>Whether this workspace called a provider at all in the period.</summary>
    public bool HasAny => Calls > 0;
}

/// <summary>
/// Rated spend in one currency.
/// </summary>
/// <remarks>
/// <strong>A list, never a single figure.</strong> Currencies are not added
/// together and nothing is converted: there is no exchange rate in this platform,
/// and inventing one would produce a total that is wrong by an amount nobody can
/// determine. Two currencies means two rows.
/// </remarks>
public sealed record AnalyticsRatedCost(string Currency, decimal EstimatedCost, long Calls);

/// <summary>
/// One provider, model and operation type's share of the spend.
/// </summary>
/// <remarks>
/// Bounded, like the agent breakdown: the tenth busiest model is the last one
/// worth a row. Carries no key, no prompt and no response body — a model name is
/// a published identifier.
/// </remarks>
public sealed record AnalyticsModelUsage(
    string Provider,
    string Model,
    AiOperationType OperationType,
    long Calls,
    long InputTokens,
    long OutputTokens,
    decimal? EstimatedCost,
    string? Currency,
    long UnratedCalls);

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
    AnalyticsWhatsAppDelivery? WhatsAppDelivery,
    AnalyticsAiUsage AiUsage,
    IReadOnlyList<AnalyticsModelUsage> Models);
