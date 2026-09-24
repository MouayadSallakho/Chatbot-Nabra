using Microsoft.EntityFrameworkCore;
using OmniAI.Application.Analytics;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;
using OmniAI.Infrastructure.Persistence;

namespace OmniAI.Infrastructure.Analytics;

/// <summary>
/// The two base sets every activity number is counted over.
/// </summary>
/// <remarks>
/// <para>
/// <strong>One definition, shared by the dashboard and by reports.</strong> What
/// "a conversation started in the period" and "a message sent in the period" mean
/// is decided here, once. The analytics reader and the report readers both count
/// over these, so a number cannot mean one thing on a chart and another in an
/// export of it.
/// </para>
/// <para>
/// <strong>The workspace is in every predicate.</strong> Messages carry no tenant
/// column of their own, so they are reached only by joining through
/// <c>conversations</c>, which does.
/// </para>
/// </remarks>
internal static class AnalyticsQueries
{
    /// <summary>Conversations this workspace started inside the window.</summary>
    public static IQueryable<Conversation> ConversationsStarted(
        OmniAiDbContext context,
        TenantId tenantId,
        AnalyticsWindow window) =>
        context.Conversations
            .AsNoTracking()
            .Where(conversation =>
                conversation.TenantId == tenantId
                && conversation.StartedAt >= window.From
                && conversation.StartedAt < window.To);

    /// <summary>
    /// Messages sent inside the window, in this workspace's conversations.
    /// </summary>
    /// <remarks>
    /// <strong>The message's own instant, not its conversation's.</strong> A
    /// conversation opened in March and still running in June contributes its June
    /// messages to June. Note that this is the <em>analytics</em> reading of time
    /// and has nothing to do with transcript order, which is and remains the
    /// ingestion sequence.
    /// </remarks>
    public static IQueryable<Message> MessagesSent(
        OmniAiDbContext context,
        TenantId tenantId,
        AnalyticsWindow window) =>
        from message in context.Set<Message>().AsNoTracking()
        join conversation in context.Conversations.AsNoTracking()
            on message.ConversationId equals conversation.Id
        where conversation.TenantId == tenantId
            && message.SentAt >= window.From
            && message.SentAt < window.To
        select message;
}
