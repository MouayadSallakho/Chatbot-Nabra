using Microsoft.EntityFrameworkCore;
using OmniAI.Application.Analytics;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.WhatsApp;
using OmniAI.Infrastructure.Persistence;

namespace OmniAI.Infrastructure.Analytics;

/// <summary>
/// One workspace's dashboard, aggregated by PostgreSQL.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Every count is a <c>COUNT</c> with a <c>GROUP BY</c>.</strong> Nothing
/// here materialises a conversation or a message: the rows that come back are one
/// per day, one per channel, one per sender or one per agent, which is a handful
/// even for a busy workspace on a ninety-day window. The merging done in C# is of
/// those small grouped results, never of the records behind them.
/// </para>
/// <para>
/// <strong>Each query is written to translate.</strong> Earlier phases were bitten
/// by expressions that looked fine and silently evaluated on the client, so the
/// shapes here are deliberately plain — a join, a filter, a group, a count — and
/// every one of them is exercised against a real PostgreSQL in the test suite
/// rather than against an in-memory provider that would accept anything.
/// </para>
/// <para>
/// <strong>The workspace is in every predicate.</strong> Messages and conversation
/// turns carry no tenant column of their own, so they are reached only by joining
/// through <c>conversations</c>, which does. There is no path here that reads a
/// message without having proved which workspace owns its conversation.
/// </para>
/// </remarks>
internal sealed class WorkspaceAnalyticsReader(OmniAiDbContext context)
    : IWorkspaceAnalyticsReader
{
    /// <summary>How many agents a breakdown will name.</summary>
    /// <remarks>
    /// Ten. A bounded list is the point: an unbounded "every agent" would grow a
    /// response with the workspace, and the eleventh agent by volume is not what
    /// somebody opens a dashboard to find.
    /// </remarks>
    private const int MaxAgents = 10;

    private readonly OmniAiDbContext _context =
        context ?? throw new ArgumentNullException(nameof(context));

    public async Task<WorkspaceAnalytics> ReadAsync(
        TenantId tenantId,
        AnalyticsWindow window,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(window);

        AnalyticsSummary summary =
            await SummaryAsync(tenantId, window, cancellationToken).ConfigureAwait(false);

        AnalyticsSnapshot current =
            await SnapshotAsync(tenantId, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<AnalyticsDay> daily =
            await DailyAsync(tenantId, window, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<AnalyticsChannel> channels =
            await ChannelsAsync(tenantId, window, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<AnalyticsAgent> agents =
            await AgentsAsync(tenantId, window, cancellationToken).ConfigureAwait(false);

        AnalyticsAiOperations operations =
            await AiOperationsAsync(tenantId, window, cancellationToken).ConfigureAwait(false);

        AnalyticsWhatsAppDelivery delivery =
            await WhatsAppAsync(tenantId, window, cancellationToken).ConfigureAwait(false);

        return new WorkspaceAnalytics(
            new AnalyticsPeriod(
                window.Label,
                window.From,
                window.To,
                window.Range.Days(),
                AnalyticsWindow.TimeZone),
            summary,
            current,
            daily,
            channels,
            agents,
            operations,

            // Absent rather than a row of zeroes for a workspace that has never
            // used WhatsApp: a delivery panel full of noughts invites somebody to
            // go looking for the outage behind it.
            delivery.HasAny ? delivery : null);
    }

    // ------------------------------------------------------------------ pieces

    private async Task<AnalyticsSummary> SummaryAsync(
        TenantId tenantId,
        AnalyticsWindow window,
        CancellationToken cancellationToken)
    {
        int started = await Conversations(tenantId, window)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);

        // One trip, grouped by sender, rather than one COUNT per sender. Four
        // round trips to answer one question is how a dashboard becomes slow.
        List<SenderCount> bySender = await Messages(tenantId, window)
            .GroupBy(message => message.Sender)
            .Select(group => new SenderCount(group.Key, group.Count()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new AnalyticsSummary(
            started,
            Count(bySender, MessageSender.Customer),
            Count(bySender, MessageSender.AiAgent),
            Count(bySender, MessageSender.HumanAgent),
            Count(bySender, MessageSender.System));
    }

    /// <remarks>
    /// No date filter, and that is the point: see <c>AnalyticsSnapshot</c>.
    /// </remarks>
    private async Task<AnalyticsSnapshot> SnapshotAsync(
        TenantId tenantId,
        CancellationToken cancellationToken)
    {
        List<HandlingCount> open = await _context.Conversations
            .AsNoTracking()
            .Where(conversation =>
                conversation.TenantId == tenantId
                && conversation.Status == ConversationStatus.Open)
            .GroupBy(conversation => conversation.Handling)
            .Select(group => new HandlingCount(group.Key, group.Count()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        long ai = open
            .Where(row => row.Handling == ConversationHandling.Ai)
            .Sum(row => (long)row.Count);

        long human = open
            .Where(row => row.Handling == ConversationHandling.Human)
            .Sum(row => (long)row.Count);

        return new AnalyticsSnapshot(ai + human, ai, human);
    }

    /// <remarks>
    /// <strong>Two grouped queries, then a join in memory over at most ninety
    /// rows each.</strong> Doing it as one SQL statement would need a full outer
    /// join between two aggregates over different tables, which EF translates
    /// poorly and a reader understands worse.
    /// <para>
    /// The day list comes from the window rather than from the rows, so a quiet
    /// Sunday is a zero instead of a hole in the chart.
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyList<AnalyticsDay>> DailyAsync(
        TenantId tenantId,
        AnalyticsWindow window,
        CancellationToken cancellationToken)
    {
        List<DayCount> startedByDay = await Conversations(tenantId, window)
            .GroupBy(conversation => DateOnly.FromDateTime(conversation.StartedAt.UtcDateTime))
            .Select(group => new DayCount(group.Key, group.Count()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<DaySenderCount> messagesByDay = await Messages(tenantId, window)
            .GroupBy(message => new
            {
                Day = DateOnly.FromDateTime(message.SentAt.UtcDateTime),
                message.Sender,
            })
            .Select(group => new DaySenderCount(group.Key.Day, group.Key.Sender, group.Count()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<DateOnly, int> started = startedByDay
            .ToDictionary(row => row.Day, row => row.Count);

        Dictionary<(DateOnly Day, MessageSender Sender), int> messages = messagesByDay
            .ToDictionary(row => (row.Day, row.Sender), row => row.Count);

        return
        [
            .. window.Days().Select(day => new AnalyticsDay(
                day,
                started.GetValueOrDefault(day),
                messages.GetValueOrDefault((day, MessageSender.Customer)),
                messages.GetValueOrDefault((day, MessageSender.AiAgent)),
                messages.GetValueOrDefault((day, MessageSender.HumanAgent)))),
        ];
    }

    private async Task<IReadOnlyList<AnalyticsChannel>> ChannelsAsync(
        TenantId tenantId,
        AnalyticsWindow window,
        CancellationToken cancellationToken)
    {
        List<ChannelCount> startedByChannel = await Conversations(tenantId, window)
            .GroupBy(conversation => conversation.Channel)
            .Select(group => new ChannelCount(group.Key, group.Count()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Messages are attributed to the channel of the conversation they are in,
        // which is the only channel they can have: a message has no channel of its
        // own and inventing one would be a second, disagreeing answer.
        List<ChannelSenderCount> messagesByChannel = await
            (from message in _context.Set<Message>().AsNoTracking()
             join conversation in _context.Conversations.AsNoTracking()
                 on message.ConversationId equals conversation.Id
             where conversation.TenantId == tenantId
                 && message.SentAt >= window.From
                 && message.SentAt < window.To
             group message by new { conversation.Channel, message.Sender } into grouped
             select new ChannelSenderCount(
                 grouped.Key.Channel,
                 grouped.Key.Sender,
                 grouped.Count()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Every channel the workspace actually used, and no others. A row of
        // zeroes for Instagram would advertise a channel this platform does not
        // yet implement.
        IEnumerable<ChannelKind> used = startedByChannel
            .Select(row => row.Channel)
            .Concat(messagesByChannel.Select(row => row.Channel))
            .Distinct()
            .OrderBy(channel => channel);

        return
        [
            .. used.Select(channel => new AnalyticsChannel(
                channel,
                startedByChannel
                    .Where(row => row.Channel == channel)
                    .Sum(row => (long)row.Count),
                CountIn(messagesByChannel, channel, MessageSender.Customer),
                CountIn(messagesByChannel, channel, MessageSender.AiAgent),
                CountIn(messagesByChannel, channel, MessageSender.HumanAgent))),
        ];
    }

    /// <remarks>
    /// <para>
    /// <strong>A conversation's agent is not stored on the conversation.</strong>
    /// It is reached through whichever channel opened it: a web chat session names
    /// the agent directly, and a WhatsApp conversation names it through the
    /// contact mapping to the connection. So attribution is two queries per
    /// metric, one per channel, merged over a handful of agents.
    /// </para>
    /// <para>
    /// Deliberately not a single query with conditional joins. That shape reads
    /// badly, translates unpredictably once strongly typed ids and their value
    /// converters are involved, and buys nothing: these result sets have one row
    /// per agent.
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyList<AnalyticsAgent>> AgentsAsync(
        TenantId tenantId,
        AnalyticsWindow window,
        CancellationToken cancellationToken)
    {
        List<AgentCount> webConversations = await
            (from conversation in Conversations(tenantId, window)
             join session in _context.WebChatSessions.AsNoTracking()
                 on conversation.Id equals session.ConversationId
             group conversation by session.AiAgentId into grouped
             select new AgentCount(grouped.Key, grouped.Count()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<AgentCount> whatsAppConversations = await
            (from conversation in Conversations(tenantId, window)
             join contact in _context.WhatsAppContacts.AsNoTracking()
                 on conversation.Id equals contact.ConversationId
             join connection in _context.WhatsAppConnections.AsNoTracking()
                 on contact.ConnectionId equals connection.Id
             group conversation by connection.AiAgentId into grouped
             select new AgentCount(grouped.Key, grouped.Count()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<AgentSenderCount> webMessages = await
            (from message in _context.Set<Message>().AsNoTracking()
             join conversation in _context.Conversations.AsNoTracking()
                 on message.ConversationId equals conversation.Id
             join session in _context.WebChatSessions.AsNoTracking()
                 on conversation.Id equals session.ConversationId
             where conversation.TenantId == tenantId
                 && message.SentAt >= window.From
                 && message.SentAt < window.To
             group message by new { session.AiAgentId, message.Sender } into grouped
             select new AgentSenderCount(
                 grouped.Key.AiAgentId,
                 grouped.Key.Sender,
                 grouped.Count()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<AgentSenderCount> whatsAppMessages = await
            (from message in _context.Set<Message>().AsNoTracking()
             join conversation in _context.Conversations.AsNoTracking()
                 on message.ConversationId equals conversation.Id
             join contact in _context.WhatsAppContacts.AsNoTracking()
                 on conversation.Id equals contact.ConversationId
             join connection in _context.WhatsAppConnections.AsNoTracking()
                 on contact.ConnectionId equals connection.Id
             where conversation.TenantId == tenantId
                 && message.SentAt >= window.From
                 && message.SentAt < window.To
             group message by new { connection.AiAgentId, message.Sender } into grouped
             select new AgentSenderCount(
                 grouped.Key.AiAgentId,
                 grouped.Key.Sender,
                 grouped.Count()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<AgentCount> conversations = [.. webConversations, .. whatsAppConversations];
        List<AgentSenderCount> messages = [.. webMessages, .. whatsAppMessages];

        List<AiAgentId> ids =
        [
            .. conversations
                .Select(row => row.AgentId)
                .Concat(messages.Select(row => row.AgentId))
                .Distinct(),
        ];

        if (ids.Count == 0)
        {
            return [];
        }

        // Names, in one trip, for the agents that actually appear. A per-agent
        // lookup here would be the N+1 this reader exists to avoid.
        Dictionary<AiAgentId, string> names = await _context.AiAgents
            .AsNoTracking()
            .Where(agent => agent.TenantId == tenantId && ids.Contains(agent.Id))
            .Select(agent => new { agent.Id, Name = agent.Name.Value })
            .ToDictionaryAsync(row => row.Id, row => row.Name, cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. ids
                .Select(id => new AnalyticsAgent(
                    id.Value,

                    // An agent deleted out from under its conversations still has
                    // numbers; it is named honestly rather than dropped, which
                    // would make the breakdown stop adding up.
                    names.GetValueOrDefault(id, "Removed agent"),
                    conversations.Where(row => row.AgentId == id).Sum(row => (long)row.Count),
                    CountFor(messages, id, MessageSender.Customer),
                    CountFor(messages, id, MessageSender.AiAgent),
                    CountFor(messages, id, MessageSender.HumanAgent)))
                .OrderByDescending(agent => agent.ConversationsStarted)
                .ThenByDescending(agent => agent.CustomerMessages)
                .ThenBy(agent => agent.Name, StringComparer.Ordinal)
                .Take(MaxAgents),
        ];
    }

    /// <remarks>
    /// Turns are reached through their conversation, which is what carries the
    /// workspace. Their own <c>created_at</c> is the attempt's instant, so a turn
    /// that started inside the window and finished outside it is still counted as
    /// an attempt made in the window.
    /// </remarks>
    private async Task<AnalyticsAiOperations> AiOperationsAsync(
        TenantId tenantId,
        AnalyticsWindow window,
        CancellationToken cancellationToken)
    {
        List<TurnCount> byStatus = await
            (from turn in _context.ConversationTurns.AsNoTracking()
             join conversation in _context.Conversations.AsNoTracking()
                 on turn.ConversationId equals conversation.Id
             where conversation.TenantId == tenantId
                 && turn.CreatedAt >= window.From
                 && turn.CreatedAt < window.To
             group turn by turn.Status into grouped
             select new TurnCount(grouped.Key, grouped.Count()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        long completed = Count(byStatus, ConversationTurnStatus.Completed);
        long failed = Count(byStatus, ConversationTurnStatus.Failed);
        long generating = Count(byStatus, ConversationTurnStatus.Generating);

        return new AnalyticsAiOperations(
            completed + failed + generating,
            completed,
            failed,
            generating);
    }

    /// <remarks>
    /// Deliveries carry their own workspace column, so this one needs no join.
    /// Provider message ids are never read: they identify a message at Meta and
    /// have no place in an aggregate.
    /// </remarks>
    private async Task<AnalyticsWhatsAppDelivery> WhatsAppAsync(
        TenantId tenantId,
        AnalyticsWindow window,
        CancellationToken cancellationToken)
    {
        List<DeliveryCount> byStatus = await _context.WhatsAppDeliveries
            .AsNoTracking()
            .Where(delivery =>
                delivery.TenantId == tenantId
                && delivery.CreatedAt >= window.From
                && delivery.CreatedAt < window.To)
            .GroupBy(delivery => delivery.Status)
            .Select(group => new DeliveryCount(group.Key, group.Count()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new AnalyticsWhatsAppDelivery(
            Count(byStatus, WhatsAppDeliveryStatus.Pending),
            Count(byStatus, WhatsAppDeliveryStatus.Sending),
            Count(byStatus, WhatsAppDeliveryStatus.Sent),
            Count(byStatus, WhatsAppDeliveryStatus.Delivered),
            Count(byStatus, WhatsAppDeliveryStatus.Read),
            Count(byStatus, WhatsAppDeliveryStatus.Failed),
            Count(byStatus, WhatsAppDeliveryStatus.Unknown));
    }

    // ------------------------------------------------------------------- bases

    /// <summary>Conversations this workspace started inside the window.</summary>
    private IQueryable<Conversation> Conversations(TenantId tenantId, AnalyticsWindow window) =>
        _context.Conversations
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
    private IQueryable<Message> Messages(TenantId tenantId, AnalyticsWindow window) =>
        from message in _context.Set<Message>().AsNoTracking()
        join conversation in _context.Conversations.AsNoTracking()
            on message.ConversationId equals conversation.Id
        where conversation.TenantId == tenantId
            && message.SentAt >= window.From
            && message.SentAt < window.To
        select message;

    // ------------------------------------------------------------------ totals

    private static long Count(IEnumerable<SenderCount> rows, MessageSender sender) =>
        rows.Where(row => row.Sender == sender).Sum(row => (long)row.Count);

    private static long Count(IEnumerable<TurnCount> rows, ConversationTurnStatus status) =>
        rows.Where(row => row.Status == status).Sum(row => (long)row.Count);

    private static long Count(IEnumerable<DeliveryCount> rows, WhatsAppDeliveryStatus status) =>
        rows.Where(row => row.Status == status).Sum(row => (long)row.Count);

    private static long CountIn(
        IEnumerable<ChannelSenderCount> rows,
        ChannelKind channel,
        MessageSender sender) =>
        rows
            .Where(row => row.Channel == channel && row.Sender == sender)
            .Sum(row => (long)row.Count);

    private static long CountFor(
        IEnumerable<AgentSenderCount> rows,
        AiAgentId agentId,
        MessageSender sender) =>
        rows
            .Where(row => row.AgentId == agentId && row.Sender == sender)
            .Sum(row => (long)row.Count);

    // The shapes PostgreSQL returns. Named rather than anonymous so the queries
    // read as what they are and so nothing accidentally leaves this file.
    private sealed record SenderCount(MessageSender Sender, int Count);

    private sealed record HandlingCount(ConversationHandling Handling, int Count);

    // A date, not a timestamp. PostgreSQL is asked for a `date` and hands one
    // back; taking it as a DateTime would make EF coerce a value it should not
    // have to, which is how this query failed the first time it was written.
    private sealed record DayCount(DateOnly Day, int Count);

    private sealed record DaySenderCount(DateOnly Day, MessageSender Sender, int Count);

    private sealed record ChannelCount(ChannelKind Channel, int Count);

    private sealed record ChannelSenderCount(ChannelKind Channel, MessageSender Sender, int Count);

    private sealed record AgentCount(AiAgentId AgentId, int Count);

    private sealed record AgentSenderCount(AiAgentId AgentId, MessageSender Sender, int Count);

    private sealed record TurnCount(ConversationTurnStatus Status, int Count);

    private sealed record DeliveryCount(WhatsAppDeliveryStatus Status, int Count);
}
