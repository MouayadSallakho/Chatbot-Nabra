using System.Globalization;
using System.Text.Json.Serialization;
using OmniAI.Application.Analytics;

namespace OmniAI.Api.Analytics;

/// <summary>The period a dashboard describes.</summary>
/// <param name="TimeZone">
/// The IANA zone every day of the response is a local day of: the workspace's
/// zone, <c>UTC</c> unless somebody chose another (Phase 37.6). Published rather
/// than assumed, because a reader looking at a day bucket needs to know which day
/// it is.
/// </param>
public sealed record AnalyticsPeriodResponse(
    string Range,
    DateTimeOffset From,
    DateTimeOffset To,
    int Days,
    string TimeZone);

/// <summary>What happened during the period.</summary>
/// <param name="ConversationsAnsweredByAi">
/// Of <c>conversationsStarted</c>, those the AI replied in and no person replied in
/// or took over, as of the read (Phase 39.1, BA39-1). With <c>compare=previous</c>
/// the previous period's summary carries the same figure.
/// </param>
public sealed record AnalyticsSummaryResponse(
    long ConversationsStarted,
    long CustomerMessages,
    long AiMessages,
    long HumanMessages,
    long SystemMessages,
    long ConversationsAnsweredByAi,
    FirstReplyTimes? FirstReply);

/// <summary>What the workspace looks like now, regardless of the period.</summary>
public sealed record AnalyticsSnapshotResponse(
    long OpenConversations,
    long OpenHandledByAi,
    long OpenHandledByHuman);

/// <summary>One local day.</summary>
/// <param name="Date">An ISO date, <c>yyyy-MM-dd</c>, in the period's time zone.</param>
public sealed record AnalyticsDayResponse(
    string Date,
    long ConversationsStarted,
    long CustomerMessages,
    long AiMessages,
    long HumanMessages);

/// <summary>One channel's share.</summary>
public sealed record AnalyticsChannelResponse(
    string Channel,
    long ConversationsStarted,
    long CustomerMessages,
    long AiMessages,
    long HumanMessages);

/// <summary>One agent's share.</summary>
public sealed record AnalyticsAgentResponse(
    Guid AiAgentId,
    string Name,
    long ConversationsStarted,
    long CustomerMessages,
    long AiMessages,
    long HumanMessages);

/// <summary>How AI turns ended.</summary>
public sealed record AnalyticsAiOperationsResponse(
    long Attempts,
    long Completed,
    long Failed,
    long InProgress);

/// <summary>Where outbound WhatsApp messages got to.</summary>
public sealed record AnalyticsWhatsAppDeliveryResponse(
    long Pending,
    long Sending,
    long Sent,
    long Delivered,
    long Read,
    long Failed,
    long Unknown);


/// <summary>
/// Rated provider spend in one currency.
/// </summary>
/// <remarks>
/// <strong>An internal estimate, and labelled as one everywhere it is shown.</strong>
/// It is what this platform reckons it paid a provider, computed from configured
/// rates. It is not an invoice amount, not a customer charge and not an amount
/// due.
/// </remarks>
public sealed record AnalyticsRatedCostResponse(
    string Currency,
    decimal EstimatedCost,
    long Calls);

/// <summary>
/// One purpose's share of the provider calls (Phase 37.6, row 30).
/// </summary>
/// <param name="Purpose">
/// A stable code: <c>ConversationGeneration</c> (a reply to a customer),
/// <c>ConversationRetrievalEmbedding</c> (finding knowledge for that reply),
/// <c>KnowledgeIndexEmbedding</c> (indexing a document), <c>AgentPreviewGeneration</c>
/// (an agent preview) or <c>AgentRetrievalTest</c> (the knowledge retrieval tester).
/// Retrieval tests made before Phase 37.6 were recorded, and stay counted, as
/// <c>ConversationRetrievalEmbedding</c>.
/// </param>
/// <param name="RatedCost">Per currency, never added across currencies.</param>
public sealed record AnalyticsPurposeUsageResponse(
    string Purpose,
    long Calls,
    long Succeeded,
    long Failed,
    long Unresolved,
    long InputTokens,
    long OutputTokens,
    long UnratedCalls,
    IReadOnlyList<AnalyticsRatedCostResponse> RatedCost);

/// <summary>
/// Real calls to AI providers, and what they cost.
/// </summary>
/// <remarks>
/// Counts and the providers' own token figures. No prompt, no completion, no
/// customer content, no API key and no provider request or response body appears
/// here — a model name is a published identifier and nothing else travels.
/// </remarks>
/// <param name="ByPurpose">
/// The same calls by purpose, one row per purpose in a fixed order, zeroes
/// included; the rows add up to the totals (Phase 37.6).
/// </param>
public sealed record AnalyticsAiUsageResponse(
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
    IReadOnlyList<AnalyticsRatedCostResponse> RatedCost,
    IReadOnlyList<AnalyticsPurposeUsageResponse> ByPurpose);

/// <summary>
/// The period just before the dashboard's, for comparison (Phase 37.6, row 31).
/// </summary>
/// <remarks>
/// As many whole local days, ending where the current period starts, in the same
/// zone. Includes period totals and a bucket for each local date.
/// </remarks>
public sealed record AnalyticsComparisonResponse(
    AnalyticsPeriodResponse Period,
    AnalyticsSummaryResponse Summary,
    AnalyticsAiOperationsResponse AiOperations,
    IReadOnlyList<AnalyticsDayResponse> DailyActivity);

/// <summary>One provider model's share of the spend.</summary>
public sealed record AnalyticsModelUsageResponse(
    string Provider,
    string Model,
    string OperationType,
    long Calls,
    long InputTokens,
    long OutputTokens,
    decimal? EstimatedCost,
    string? Currency,
    long UnratedCalls);

/// <summary>
/// One workspace's dashboard.
/// </summary>
/// <remarks>
/// <strong>Counts and safe dimensions only.</strong> No message text, no
/// knowledge text, no customer identifier, no phone number, no WhatsApp id, no
/// provider message id, no email, no token and no ticket appears anywhere in this
/// shape — not as a field, and not as a key. An agent's name is a label the
/// workspace wrote and already reads on every other screen.
/// </remarks>
public sealed record AnalyticsDashboardResponse(
    AnalyticsPeriodResponse Period,
    AnalyticsSummaryResponse Summary,
    AnalyticsSnapshotResponse Current,
    IReadOnlyList<AnalyticsDayResponse> DailyActivity,
    IReadOnlyList<AnalyticsChannelResponse> Channels,
    IReadOnlyList<AnalyticsAgentResponse> Agents,
    AnalyticsAiOperationsResponse AiOperations,

    // Spelt with a small "a" deliberately. The camel-case policy would turn
    // WhatsAppDelivery into "whatsAppDelivery", which reads as a typo on the
    // wire; this name serialises as "whatsappDelivery".
    AnalyticsWhatsAppDeliveryResponse? WhatsappDelivery,

    // Null for a workspace that has never called a provider, rather than a block
    // of zeroes: no spend and spend of nothing read very differently.
    AnalyticsAiUsageResponse? AiUsage,
    IReadOnlyList<AnalyticsModelUsageResponse> Models,

    // Phase 37.6 (row 33): the local date of the first conversation, or null.
    string? FirstActivityOn,

    // Phase 37.6 (row 31): present only with compare=previous, so a response to
    // the existing request gains no member it did not ask for.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    AnalyticsComparisonResponse? Previous)
{
    public static AnalyticsDashboardResponse From(WorkspaceAnalytics analytics)
    {
        ArgumentNullException.ThrowIfNull(analytics);

        return new AnalyticsDashboardResponse(
            PeriodOf(analytics.Period),
            SummaryOf(analytics.Summary),
            new AnalyticsSnapshotResponse(
                analytics.Current.OpenConversations,
                analytics.Current.OpenHandledByAi,
                analytics.Current.OpenHandledByHuman),
            DaysOf(analytics.DailyActivity),
            [
                .. analytics.Channels.Select(channel => new AnalyticsChannelResponse(
                    channel.Channel.ToString(),
                    channel.ConversationsStarted,
                    channel.CustomerMessages,
                    channel.AiMessages,
                    channel.HumanMessages)),
            ],
            [
                .. analytics.Agents.Select(agent => new AnalyticsAgentResponse(
                    agent.AiAgentId,
                    agent.Name,
                    agent.ConversationsStarted,
                    agent.CustomerMessages,
                    agent.AiMessages,
                    agent.HumanMessages)),
            ],
            OperationsOf(analytics.AiOperations),
            analytics.WhatsAppDelivery is null
                ? null
                : new AnalyticsWhatsAppDeliveryResponse(
                    analytics.WhatsAppDelivery.Pending,
                    analytics.WhatsAppDelivery.Sending,
                    analytics.WhatsAppDelivery.Sent,
                    analytics.WhatsAppDelivery.Delivered,
                    analytics.WhatsAppDelivery.Read,
                    analytics.WhatsAppDelivery.Failed,
                    analytics.WhatsAppDelivery.Unknown),
            analytics.AiUsage.HasAny
                ? new AnalyticsAiUsageResponse(
                    analytics.AiUsage.Calls,
                    analytics.AiUsage.GenerationCalls,
                    analytics.AiUsage.EmbeddingCalls,
                    analytics.AiUsage.Succeeded,
                    analytics.AiUsage.Failed,
                    analytics.AiUsage.Unresolved,
                    analytics.AiUsage.GenerationInputTokens,
                    analytics.AiUsage.GenerationCachedInputTokens,
                    analytics.AiUsage.GenerationOutputTokens,
                    analytics.AiUsage.GenerationReasoningTokens,
                    analytics.AiUsage.EmbeddingInputTokens,
                    analytics.AiUsage.UnratedCalls,
                    RatedOf(analytics.AiUsage.RatedCost),
                    [
                        .. analytics.AiUsage.ByPurpose.Select(purpose =>
                            new AnalyticsPurposeUsageResponse(
                                purpose.Purpose.ToString(),
                                purpose.Calls,
                                purpose.Succeeded,
                                purpose.Failed,
                                purpose.Unresolved,
                                purpose.InputTokens,
                                purpose.OutputTokens,
                                purpose.UnratedCalls,
                                RatedOf(purpose.RatedCost))),
                    ])
                : null,
            [
                .. analytics.Models.Select(model => new AnalyticsModelUsageResponse(
                    model.Provider,
                    model.Model,
                    model.OperationType.ToString(),
                    model.Calls,
                    model.InputTokens,
                    model.OutputTokens,
                    model.EstimatedCost,
                    model.Currency,
                    model.UnratedCalls)),
            ],
            analytics.FirstActivityOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            analytics.Previous is null
                ? null
                : new AnalyticsComparisonResponse(
                    PeriodOf(analytics.Previous.Period),
                    SummaryOf(analytics.Previous.Summary),
                    OperationsOf(analytics.Previous.AiOperations),
                    DaysOf(analytics.Previous.DailyActivity)));
    }

    private static AnalyticsDayResponse[] DaysOf(IReadOnlyList<AnalyticsDay> days) =>
        [.. days.Select(day => new AnalyticsDayResponse(day.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            day.ConversationsStarted, day.CustomerMessages, day.AiMessages, day.HumanMessages))];

    private static AnalyticsPeriodResponse PeriodOf(AnalyticsPeriod period) =>
        new(period.Range, period.From, period.To, period.Days, period.TimeZone);

    private static AnalyticsSummaryResponse SummaryOf(AnalyticsSummary summary) =>
        new(
            summary.ConversationsStarted,
            summary.CustomerMessages,
            summary.AiMessages,
            summary.HumanMessages,
            summary.SystemMessages,
            summary.ConversationsAnsweredByAi,
            summary.FirstReply);

    private static AnalyticsAiOperationsResponse OperationsOf(AnalyticsAiOperations operations) =>
        new(operations.Attempts, operations.Completed, operations.Failed, operations.InProgress);

    private static IReadOnlyList<AnalyticsRatedCostResponse> RatedOf(IReadOnlyList<AnalyticsRatedCost> costs) =>
        [.. costs.Select(cost => new AnalyticsRatedCostResponse(cost.Currency, cost.EstimatedCost, cost.Calls))];
}
