using System.Globalization;
using OmniAI.Application.Analytics;

namespace OmniAI.Api.Analytics;

/// <summary>The period a dashboard describes.</summary>
/// <param name="TimeZone">
/// Always <c>UTC</c>. Published rather than assumed, because a reader in
/// Singapore looking at a day bucket needs to know which day it is.
/// </param>
public sealed record AnalyticsPeriodResponse(
    string Range,
    DateTimeOffset From,
    DateTimeOffset To,
    int Days,
    string TimeZone);

/// <summary>What happened during the period.</summary>
public sealed record AnalyticsSummaryResponse(
    long ConversationsStarted,
    long CustomerMessages,
    long AiMessages,
    long HumanMessages,
    long SystemMessages);

/// <summary>What the workspace looks like now, regardless of the period.</summary>
public sealed record AnalyticsSnapshotResponse(
    long OpenConversations,
    long OpenHandledByAi,
    long OpenHandledByHuman);

/// <summary>One UTC day.</summary>
/// <param name="Date">An ISO date, <c>yyyy-MM-dd</c>, in UTC.</param>
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
    AnalyticsWhatsAppDeliveryResponse? WhatsappDelivery)
{
    public static AnalyticsDashboardResponse From(WorkspaceAnalytics analytics)
    {
        ArgumentNullException.ThrowIfNull(analytics);

        return new AnalyticsDashboardResponse(
            new AnalyticsPeriodResponse(
                analytics.Period.Range,
                analytics.Period.From,
                analytics.Period.To,
                analytics.Period.Days,
                analytics.Period.TimeZone),
            new AnalyticsSummaryResponse(
                analytics.Summary.ConversationsStarted,
                analytics.Summary.CustomerMessages,
                analytics.Summary.AiMessages,
                analytics.Summary.HumanMessages,
                analytics.Summary.SystemMessages),
            new AnalyticsSnapshotResponse(
                analytics.Current.OpenConversations,
                analytics.Current.OpenHandledByAi,
                analytics.Current.OpenHandledByHuman),
            [
                .. analytics.DailyActivity.Select(day => new AnalyticsDayResponse(
                    day.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    day.ConversationsStarted,
                    day.CustomerMessages,
                    day.AiMessages,
                    day.HumanMessages)),
            ],
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
            new AnalyticsAiOperationsResponse(
                analytics.AiOperations.Attempts,
                analytics.AiOperations.Completed,
                analytics.AiOperations.Failed,
                analytics.AiOperations.InProgress),
            analytics.WhatsAppDelivery is null
                ? null
                : new AnalyticsWhatsAppDeliveryResponse(
                    analytics.WhatsAppDelivery.Pending,
                    analytics.WhatsAppDelivery.Sending,
                    analytics.WhatsAppDelivery.Sent,
                    analytics.WhatsAppDelivery.Delivered,
                    analytics.WhatsAppDelivery.Read,
                    analytics.WhatsAppDelivery.Failed,
                    analytics.WhatsAppDelivery.Unknown));
    }
}
