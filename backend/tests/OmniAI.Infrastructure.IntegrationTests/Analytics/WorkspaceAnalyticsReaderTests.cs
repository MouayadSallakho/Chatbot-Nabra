using Microsoft.EntityFrameworkCore;
using OmniAI.Application.Analytics;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.WhatsApp;
using OmniAI.Infrastructure.Analytics;
using OmniAI.Infrastructure.IntegrationTests.Persistence;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Analytics;

/// <summary>
/// The analytics reader, against a real PostgreSQL.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This suite exists because aggregate queries lie convincingly.</strong>
/// A <c>GROUP BY</c> that silently evaluates on the client returns the right
/// answer on ten rows and empties the connection pool on a million; a boundary
/// written <c>&lt;=</c> instead of <c>&lt;</c> counts one event in two adjacent
/// periods. Neither is visible without a database and rows somebody counted by
/// hand.
/// </para>
/// <para>
/// Every test builds its own workspace, so two running together cannot see each
/// other's rows.
/// </para>
/// </remarks>
public sealed class WorkspaceAnalyticsReaderTests(PostgresTestDatabase database)
    : IClassFixture<PostgresTestDatabase>
{
    /// <summary>The instant every window in this suite ends at.</summary>
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    // ------------------------------------------------------------------ empty

    [Fact]
    public async Task A_workspace_with_no_activity_reports_zero_rather_than_nothing()
    {
        AnalyticsWorld world = await NewWorldAsync();

        WorkspaceAnalytics analytics = await ReadAsync(world);

        Assert.Equal(0, analytics.Summary.ConversationsStarted);
        Assert.Equal(0, analytics.Summary.CustomerMessages);
        Assert.Equal(0, analytics.Summary.AiMessages);
        Assert.Equal(0, analytics.Summary.HumanMessages);
        Assert.Equal(0, analytics.Current.OpenConversations);
        Assert.Empty(analytics.Channels);
        Assert.Empty(analytics.Agents);
        Assert.Equal(0, analytics.AiOperations.Attempts);

        // A workspace that has never used WhatsApp gets no delivery panel at all,
        // rather than a row of noughts somebody would go looking for an outage
        // behind.
        Assert.Null(analytics.WhatsAppDelivery);

        // The days are still there. A chart of an empty period is a flat line,
        // not a missing axis.
        Assert.Equal(30, analytics.DailyActivity.Count);
        Assert.All(analytics.DailyActivity, day => Assert.Equal(0, day.CustomerMessages));
    }

    // ---------------------------------------------------------------- counting

    [Fact]
    public async Task One_conversation_is_counted_once_with_its_messages()
    {
        AnalyticsWorld world = await NewWorldAsync();
        AiAgent agent = await world.AgentAsync("Support", Now.AddDays(-5));
        Conversation conversation = await world.WebChatAsync(agent, Now.AddDays(-2));

        await world.SayAsync(
            conversation,
            (MessageSender.Customer, Now.AddDays(-2)),
            (MessageSender.AiAgent, Now.AddDays(-2)),
            (MessageSender.Customer, Now.AddDays(-2)),
            (MessageSender.HumanAgent, Now.AddDays(-2)),
            (MessageSender.System, Now.AddDays(-2)));

        WorkspaceAnalytics analytics = await ReadAsync(world);

        Assert.Equal(1, analytics.Summary.ConversationsStarted);
        Assert.Equal(2, analytics.Summary.CustomerMessages);
        Assert.Equal(1, analytics.Summary.AiMessages);
        Assert.Equal(1, analytics.Summary.HumanMessages);

        // Kept apart rather than folded in: a handover note is neither a question
        // asked nor an answer given, and adding it to either would overstate the
        // work done.
        Assert.Equal(1, analytics.Summary.SystemMessages);
    }

    [Fact]
    public async Task Open_and_closed_conversations_are_told_apart_right_now()
    {
        AnalyticsWorld world = await NewWorldAsync();
        AiAgent agent = await world.AgentAsync("Support", Now.AddDays(-5));

        Conversation open = await world.WebChatAsync(agent, Now.AddDays(-3));
        Conversation humanHandled = await world.WebChatAsync(agent, Now.AddDays(-3));
        Conversation closed = await world.WebChatAsync(agent, Now.AddDays(-3));

        await world.SetStateAsync(humanHandled, human: true);
        await world.SetStateAsync(closed, closed: true);

        WorkspaceAnalytics analytics = await ReadAsync(world);

        Assert.Equal(2, analytics.Current.OpenConversations);
        Assert.Equal(1, analytics.Current.OpenHandledByAi);
        Assert.Equal(1, analytics.Current.OpenHandledByHuman);

        // All three still started in the period, whatever became of them.
        Assert.Equal(3, analytics.Summary.ConversationsStarted);

        _ = open;
    }

    [Fact]
    public async Task The_snapshot_ignores_the_period_because_open_means_open_now()
    {
        AnalyticsWorld world = await NewWorldAsync();
        AiAgent agent = await world.AgentAsync("Support", Now.AddDays(-200));

        // Older than any range this platform offers, and still waiting. This is
        // exactly the conversation somebody needs to see.
        await world.WebChatAsync(agent, Now.AddDays(-150));

        WorkspaceAnalytics analytics = await ReadAsync(world, AnalyticsRange.SevenDays);

        Assert.Equal(0, analytics.Summary.ConversationsStarted);
        Assert.Equal(1, analytics.Current.OpenConversations);
    }

    // -------------------------------------------------------------- boundaries

    [Fact]
    public async Task An_event_on_the_first_instant_of_the_window_is_inside_it()
    {
        AnalyticsWorld world = await NewWorldAsync();
        AiAgent agent = await world.AgentAsync("Support", Now.AddDays(-40));

        AnalyticsWindow window = AnalyticsRange.ThirtyDays.Window(Now);

        await world.WebChatAsync(agent, window.From);

        WorkspaceAnalytics analytics = await ReadAsync(world);

        // Half-open at the start means inclusive: the boundary belongs to exactly
        // one of two adjacent periods, and this is the one.
        Assert.Equal(1, analytics.Summary.ConversationsStarted);
    }

    [Fact]
    public async Task An_event_one_tick_before_the_window_is_outside_it()
    {
        AnalyticsWorld world = await NewWorldAsync();
        AiAgent agent = await world.AgentAsync("Support", Now.AddDays(-40));

        AnalyticsWindow window = AnalyticsRange.ThirtyDays.Window(Now);

        await world.WebChatAsync(agent, window.From.AddTicks(-1));

        WorkspaceAnalytics analytics = await ReadAsync(world);

        Assert.Equal(0, analytics.Summary.ConversationsStarted);
    }

    [Theory]
    [InlineData(AnalyticsRange.SevenDays, 7)]
    [InlineData(AnalyticsRange.ThirtyDays, 30)]
    [InlineData(AnalyticsRange.NinetyDays, 90)]
    public async Task Each_range_reaches_exactly_as_far_back_as_it_says(
        AnalyticsRange range,
        int days)
    {
        AnalyticsWorld world = await NewWorldAsync();
        AiAgent agent = await world.AgentAsync("Support", Now.AddDays(-120));

        // One conversation on each of the last hundred days, a minute before the
        // instant the window ends. A minute, not zero: the window is half-open at
        // the top, so a conversation starting exactly at `Now` belongs to the next
        // period rather than this one.
        for (int back = 0; back < 100; back++)
        {
            await world.WebChatAsync(agent, Now.AddDays(-back).AddMinutes(-1));
        }

        WorkspaceAnalytics analytics = await ReadAsync(world, range);

        Assert.Equal(days, analytics.Summary.ConversationsStarted);
        Assert.Equal(days, analytics.DailyActivity.Count);
        Assert.All(analytics.DailyActivity, day => Assert.Equal(1, day.ConversationsStarted));
    }

    // --------------------------------------------------------------- out of order

    /// <remarks>
    /// <strong>The distinction this test defends is the whole of §7.</strong>
    /// Analytics buckets a message by the instant it was sent, because that is
    /// what a day means. A transcript orders messages by the sequence they were
    /// ingested in, because that is what a conversation means. A message whose
    /// <c>SentAt</c> is older than the one before it must land in the earlier
    /// bucket and must <em>not</em> move in the transcript.
    /// </remarks>
    [Fact]
    public async Task An_out_of_order_timestamp_moves_the_bucket_and_never_the_transcript()
    {
        AnalyticsWorld world = await NewWorldAsync();
        AiAgent agent = await world.AgentAsync("Support", Now.AddDays(-10));
        Conversation conversation = await world.WebChatAsync(agent, Now.AddDays(-5));

        DateTimeOffset later = Now.AddDays(-2);
        DateTimeOffset earlier = Now.AddDays(-4);

        // Ingested later-then-earlier, which is what a channel redelivering an old
        // webhook produces.
        await world.SayAsync(
            conversation,
            (MessageSender.Customer, later),
            (MessageSender.Customer, earlier));

        WorkspaceAnalytics analytics = await ReadAsync(world);

        DateOnly earlierDay = DateOnly.FromDateTime(earlier.UtcDateTime);
        DateOnly laterDay = DateOnly.FromDateTime(later.UtcDateTime);

        Assert.Equal(
            1,
            analytics.DailyActivity.Single(day => day.Date == earlierDay).CustomerMessages);

        Assert.Equal(
            1,
            analytics.DailyActivity.Single(day => day.Date == laterDay).CustomerMessages);

        // And the transcript is untouched: the order is still the order they
        // arrived in, not the order their timestamps claim.
        await using OmniAiDbContext read = database.CreateContext();

        List<DateTimeOffset> transcript = await read
            .Set<Message>()
            .AsNoTracking()
            .Where(message => message.ConversationId == conversation.Id)
            .OrderBy(message =>
                EF.Property<long>(message, PersistenceSchema.MessageIngestionSequence))
            .Select(message => message.SentAt)
            .ToListAsync();

        Assert.Equal([later, earlier], transcript);
    }

    // ----------------------------------------------------------------- channels

    [Fact]
    public async Task Channels_are_broken_down_by_the_channel_the_conversation_is_on()
    {
        AnalyticsWorld world = await NewWorldAsync();
        AiAgent agent = await world.AgentAsync("Support", Now.AddDays(-10));

        Conversation web = await world.WebChatAsync(agent, Now.AddDays(-3));

        WhatsAppConnection connection = await world.WhatsAppConnectionAsync(agent, Now.AddDays(-10));
        Conversation whatsApp = await world.WhatsAppAsync(connection, Now.AddDays(-3));
        Conversation whatsAppTwo = await world.WhatsAppAsync(connection, Now.AddDays(-2));

        await world.SayAsync(web, (MessageSender.Customer, Now.AddDays(-3)));

        await world.SayAsync(
            whatsApp,
            (MessageSender.Customer, Now.AddDays(-3)),
            (MessageSender.AiAgent, Now.AddDays(-3)));

        WorkspaceAnalytics analytics = await ReadAsync(world);

        AnalyticsChannel webRow = analytics.Channels.Single(row => row.Channel == ChannelKind.WebChat);
        AnalyticsChannel waRow = analytics.Channels.Single(row => row.Channel == ChannelKind.WhatsApp);

        Assert.Equal(1, webRow.ConversationsStarted);
        Assert.Equal(1, webRow.CustomerMessages);
        Assert.Equal(0, webRow.AiMessages);

        Assert.Equal(2, waRow.ConversationsStarted);
        Assert.Equal(1, waRow.CustomerMessages);
        Assert.Equal(1, waRow.AiMessages);

        // Only the channels the workspace actually used. A row of zeroes for
        // Instagram would advertise a channel that does not exist yet.
        Assert.Equal(2, analytics.Channels.Count);

        _ = whatsAppTwo;
    }

    // ------------------------------------------------------------------- agents

    [Fact]
    public async Task Agents_are_attributed_through_whichever_channel_opened_the_conversation()
    {
        AnalyticsWorld world = await NewWorldAsync();

        AiAgent sales = await world.AgentAsync("Sales", Now.AddDays(-20));
        AiAgent support = await world.AgentAsync("Support", Now.AddDays(-20));

        Conversation viaWeb = await world.WebChatAsync(sales, Now.AddDays(-4));
        await world.SayAsync(
            viaWeb,
            (MessageSender.Customer, Now.AddDays(-4)),
            (MessageSender.AiAgent, Now.AddDays(-4)));

        WhatsAppConnection connection =
            await world.WhatsAppConnectionAsync(support, Now.AddDays(-20));

        Conversation viaWhatsApp = await world.WhatsAppAsync(connection, Now.AddDays(-4));
        await world.SayAsync(
            viaWhatsApp,
            (MessageSender.Customer, Now.AddDays(-4)),
            (MessageSender.HumanAgent, Now.AddDays(-4)));

        WorkspaceAnalytics analytics = await ReadAsync(world);

        AnalyticsAgent salesRow = analytics.Agents.Single(row => row.Name == "Sales");
        AnalyticsAgent supportRow = analytics.Agents.Single(row => row.Name == "Support");

        Assert.Equal(1, salesRow.ConversationsStarted);
        Assert.Equal(1, salesRow.CustomerMessages);
        Assert.Equal(1, salesRow.AiMessages);
        Assert.Equal(0, salesRow.HumanMessages);

        Assert.Equal(1, supportRow.ConversationsStarted);
        Assert.Equal(1, supportRow.CustomerMessages);
        Assert.Equal(0, supportRow.AiMessages);

        // A person replying in a conversation the agent was handling is work on
        // that agent's watch, and is reported there.
        Assert.Equal(1, supportRow.HumanMessages);
    }

    [Fact]
    public async Task The_agent_breakdown_is_bounded_and_ordered_by_volume()
    {
        AnalyticsWorld world = await NewWorldAsync();

        // Twelve agents, each busier than the last.
        for (int index = 1; index <= 12; index++)
        {
            AiAgent agent = await world.AgentAsync($"Agent {index:00}", Now.AddDays(-20));

            for (int conversation = 0; conversation < index; conversation++)
            {
                await world.WebChatAsync(agent, Now.AddDays(-3));
            }
        }

        WorkspaceAnalytics analytics = await ReadAsync(world);

        Assert.Equal(10, analytics.Agents.Count);
        Assert.Equal("Agent 12", analytics.Agents[0].Name);
        Assert.Equal(12, analytics.Agents[0].ConversationsStarted);

        // The two quietest fell off the end, which is what bounded means.
        Assert.DoesNotContain(analytics.Agents, agent => agent.Name == "Agent 01");
        Assert.DoesNotContain(analytics.Agents, agent => agent.Name == "Agent 02");
    }

    // ------------------------------------------------------------- ai operations

    [Fact]
    public async Task Ai_operations_are_counted_from_the_states_the_runtime_recorded()
    {
        AnalyticsWorld world = await NewWorldAsync();
        AiAgent agent = await world.AgentAsync("Support", Now.AddDays(-10));
        Conversation conversation = await world.WebChatAsync(agent, Now.AddDays(-5));

        await world.TurnAsync(conversation, ConversationTurnStatus.Completed, Now.AddDays(-4));
        await world.TurnAsync(conversation, ConversationTurnStatus.Completed, Now.AddDays(-3));
        await world.TurnAsync(conversation, ConversationTurnStatus.Failed, Now.AddDays(-2));
        await world.TurnAsync(conversation, ConversationTurnStatus.Generating, Now.AddDays(-1));

        WorkspaceAnalytics analytics = await ReadAsync(world);

        Assert.Equal(4, analytics.AiOperations.Attempts);
        Assert.Equal(2, analytics.AiOperations.Completed);
        Assert.Equal(1, analytics.AiOperations.Failed);
        Assert.Equal(1, analytics.AiOperations.InProgress);

        // The parts add up to the whole, which is the only reason these four
        // numbers may sit next to each other.
        Assert.Equal(
            analytics.AiOperations.Attempts,
            analytics.AiOperations.Completed
                + analytics.AiOperations.Failed
                + analytics.AiOperations.InProgress);
    }

    // --------------------------------------------------------------- whatsapp

    [Fact]
    public async Task Whatsapp_deliveries_are_counted_in_every_state_they_can_hold()
    {
        AnalyticsWorld world = await NewWorldAsync();
        AiAgent agent = await world.AgentAsync("Support", Now.AddDays(-10));

        WhatsAppConnection connection = await world.WhatsAppConnectionAsync(agent, Now.AddDays(-10));
        Conversation conversation = await world.WhatsAppAsync(connection, Now.AddDays(-5));

        await world.DeliveryAsync(
            connection, conversation, WhatsAppDeliveryStatus.Pending, Now.AddDays(-4));

        await world.DeliveryAsync(
            connection, conversation, WhatsAppDeliveryStatus.Sent, Now.AddDays(-3));

        await world.DeliveryAsync(
            connection, conversation, WhatsAppDeliveryStatus.Sent, Now.AddDays(-3));

        await world.DeliveryAsync(
            connection, conversation, WhatsAppDeliveryStatus.Failed, Now.AddDays(-2));

        await world.DeliveryAsync(
            connection, conversation, WhatsAppDeliveryStatus.Unknown, Now.AddDays(-1));

        WorkspaceAnalytics analytics = await ReadAsync(world);

        Assert.NotNull(analytics.WhatsAppDelivery);
        Assert.Equal(1, analytics.WhatsAppDelivery.Pending);
        Assert.Equal(2, analytics.WhatsAppDelivery.Sent);
        Assert.Equal(1, analytics.WhatsAppDelivery.Failed);
        Assert.Equal(1, analytics.WhatsAppDelivery.Unknown);
        Assert.Equal(0, analytics.WhatsAppDelivery.Delivered);
    }

    // --------------------------------------------------------------- isolation

    /// <remarks>
    /// Two workspaces built identically. If any predicate underneath were missing
    /// its tenant filter, these numbers would be double.
    /// </remarks>
    [Fact]
    public async Task One_workspace_never_sees_another_workspaces_activity()
    {
        AnalyticsWorld mine = await NewWorldAsync();
        AnalyticsWorld theirs = await NewWorldAsync();

        foreach (AnalyticsWorld world in new[] { mine, theirs })
        {
            AiAgent agent = await world.AgentAsync("Support", Now.AddDays(-10));
            Conversation conversation = await world.WebChatAsync(agent, Now.AddDays(-3));

            await world.SayAsync(
                conversation,
                (MessageSender.Customer, Now.AddDays(-3)),
                (MessageSender.AiAgent, Now.AddDays(-3)));

            await world.TurnAsync(conversation, ConversationTurnStatus.Completed, Now.AddDays(-3));

            WhatsAppConnection connection =
                await world.WhatsAppConnectionAsync(agent, Now.AddDays(-10));

            Conversation whatsApp = await world.WhatsAppAsync(connection, Now.AddDays(-3));

            await world.DeliveryAsync(
                connection, whatsApp, WhatsAppDeliveryStatus.Sent, Now.AddDays(-3));
        }

        WorkspaceAnalytics analytics = await ReadAsync(mine);

        // Exactly what this workspace wrote, counted by hand: two conversations;
        // two customer messages (one said, one asked in the AI turn); three AI
        // messages (one said, one answered in the turn, one sent to WhatsApp).
        Assert.Equal(2, analytics.Summary.ConversationsStarted);
        Assert.Equal(2, analytics.Summary.CustomerMessages);
        Assert.Equal(3, analytics.Summary.AiMessages);
        Assert.Equal(2, analytics.Current.OpenConversations);
        Assert.Equal(1, analytics.AiOperations.Attempts);
        Assert.Single(analytics.Agents);
        Assert.Equal(1, analytics.WhatsAppDelivery!.Sent);
    }

    // ------------------------------------------------------------------- shape

    /// <remarks>
    /// The claim in the reader's own documentation, checked rather than asserted
    /// in prose: PostgreSQL does the counting.
    /// </remarks>
    [Fact]
    public async Task Every_count_is_computed_by_postgresql_and_not_in_memory()
    {
        AnalyticsWorld world = await NewWorldAsync();
        AiAgent agent = await world.AgentAsync("Support", Now.AddDays(-10));
        Conversation conversation = await world.WebChatAsync(agent, Now.AddDays(-3));

        await world.SayAsync(conversation, (MessageSender.Customer, Now.AddDays(-3)));

        RecordedSql recorded = new();

        await using (OmniAiDbContext context = database.CreateContext(recorded))
        {
            await new WorkspaceAnalyticsReader(context).ReadAsync(
                world.Tenant.Id,
                AnalyticsRange.ThirtyDays.Window(Now),
                AnalyticsBreakdown.Dashboard);
        }

        // Anything that reads an operational table counts there. The one
        // statement that does not aggregate is the agent-name lookup, which is a
        // single bounded SELECT over the ids that already came back — the
        // alternative being a name lookup per agent, which is the N+1 this reader
        // exists to avoid.
        List<string> overOperationalTables =
        [
            .. recorded.Statements.Where(statement =>
                statement.Contains("FROM conversations", StringComparison.OrdinalIgnoreCase)
                || statement.Contains("FROM messages", StringComparison.OrdinalIgnoreCase)
                || statement.Contains("FROM conversation_turns", StringComparison.OrdinalIgnoreCase)
                || statement.Contains("FROM whatsapp_deliveries", StringComparison.OrdinalIgnoreCase)),
        ];

        Assert.NotEmpty(overOperationalTables);

        Assert.All(
            overOperationalTables,
            statement => Assert.Contains("count(", statement, StringComparison.OrdinalIgnoreCase));

        // And no statement drags rows back to be counted here.
        Assert.DoesNotContain(
            recorded.Statements,
            statement => statement.Contains("SELECT m.text", StringComparison.Ordinal));

        // A dashboard is a fixed number of statements, whatever the workspace
        // holds. If this ever grows with the data, something became an N+1.
        //
        // Phase 23 added five: the usage buckets, the rated cost per currency,
        // the unrated count, the model breakdown and its costs. Still a constant.
        Assert.InRange(recorded.Statements.Count, 1, 21);
    }

    // ----------------------------------------------------------------- helpers

    private async Task<AnalyticsWorld> NewWorldAsync() =>
        await new AnalyticsWorld(database).StartAsync();

    private async Task<WorkspaceAnalytics> ReadAsync(
        AnalyticsWorld world,
        AnalyticsRange range = AnalyticsRange.ThirtyDays)
    {
        await using OmniAiDbContext context = database.CreateContext();

        return await new WorkspaceAnalyticsReader(context)
            .ReadAsync(world.Tenant.Id, range.Window(Now), AnalyticsBreakdown.Dashboard);
    }
}
