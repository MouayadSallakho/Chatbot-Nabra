using OmniAI.Application.Analytics;
using OmniAI.Domain.Ai;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;
using OmniAI.Infrastructure.Analytics;
using OmniAI.Infrastructure.IntegrationTests.Persistence;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Analytics;

/// <summary>
/// Phase 37.6 against a real PostgreSQL: local days in a workspace's zone, the
/// first-activity date, the previous period, and usage by purpose.
/// </summary>
/// <remarks>
/// The local day of each row is cut by PostgreSQL's <c>AT TIME ZONE</c>, and the
/// window by the runtime's zone rules; these tests prove the two agree where it is
/// hardest — the Gulf's evening, and the day Berlin's clocks go back.
/// </remarks>
public sealed class WorkspaceAnalyticsInsightsTests(PostgresTestDatabase database)
    : IClassFixture<PostgresTestDatabase>
{
    private static readonly WorkspaceTimeZone Riyadh = WorkspaceTimeZone.Create("Asia/Riyadh");
    private static readonly WorkspaceTimeZone Berlin = WorkspaceTimeZone.Create("Europe/Berlin");

    // ------------------------------------------------------------- local days

    [Fact]
    public async Task A_conversation_at_half_past_ten_utc_counts_on_the_next_gulf_day()
    {
        AnalyticsWorld world = await NewWorldAsync();
        AiAgent agent = await world.AgentAsync("Support", new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));

        DateTimeOffset evening = new(2026, 11, 3, 22, 30, 0, TimeSpan.Zero);
        Conversation conversation = await world.WebChatAsync(agent, evening);
        await world.SayAsync(conversation, (MessageSender.Customer, evening));

        DateTimeOffset now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

        WorkspaceAnalytics gulf = await ReadAsync(world, AnalyticsRange.SevenDays.Window(now, Riyadh));
        WorkspaceAnalytics utc = await ReadAsync(world, AnalyticsRange.SevenDays.Window(now));

        AnalyticsDay gulfFourth = gulf.DailyActivity.Single(day => day.Date == new DateOnly(2026, 11, 4));
        AnalyticsDay gulfThird = gulf.DailyActivity.Single(day => day.Date == new DateOnly(2026, 11, 3));

        Assert.Equal(1, gulfFourth.ConversationsStarted);
        Assert.Equal(1, gulfFourth.CustomerMessages);
        Assert.Equal(0, gulfThird.ConversationsStarted);

        Assert.Equal(1, utc.DailyActivity.Single(day => day.Date == new DateOnly(2026, 11, 3)).ConversationsStarted);
        Assert.Equal(0, utc.DailyActivity.Single(day => day.Date == new DateOnly(2026, 11, 4)).ConversationsStarted);

        Assert.Equal("Asia/Riyadh", gulf.Period.TimeZone);
        Assert.Equal("UTC", utc.Period.TimeZone);
    }

    [Fact]
    public async Task The_twenty_five_hour_berlin_day_holds_all_of_its_hours_and_no_more()
    {
        AnalyticsWorld world = await NewWorldAsync();
        AiAgent agent = await world.AgentAsync("Support", new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));

        // In Berlin: 23:30 on the 24th (CEST), 00:30 on the 25th (CEST), 23:30 on
        // the 25th (CET, after the clocks went back) and 00:30 on the 26th (CET).
        DateTimeOffset[] starts =
        [
            new(2026, 10, 24, 21, 30, 0, TimeSpan.Zero),
            new(2026, 10, 24, 22, 30, 0, TimeSpan.Zero),
            new(2026, 10, 25, 22, 30, 0, TimeSpan.Zero),
            new(2026, 10, 25, 23, 30, 0, TimeSpan.Zero),
        ];

        foreach (DateTimeOffset start in starts)
        {
            await world.WebChatAsync(agent, start);
        }

        WorkspaceAnalytics berlin = await ReadAsync(
            world,
            AnalyticsWindow.ForDays(new DateOnly(2026, 10, 24), new DateOnly(2026, 10, 27), Berlin));

        Assert.Equal(
            [(new DateOnly(2026, 10, 24), 1L), (new DateOnly(2026, 10, 25), 2L), (new DateOnly(2026, 10, 26), 1L)],
            berlin.DailyActivity.Select(day => (day.Date, day.ConversationsStarted)));

        Assert.Equal(4, berlin.Summary.ConversationsStarted);
    }

    [Fact]
    public async Task Local_days_are_cut_by_postgresql_in_the_workspace_zone()
    {
        AnalyticsWorld world = await NewWorldAsync();
        RecordedSql recorded = new();

        await using (OmniAiDbContext context = database.CreateContext(recorded))
        {
            await new WorkspaceAnalyticsReader(context).ReadAsync(
                world.Tenant.Id,
                AnalyticsRange.ThirtyDays.Window(DateTimeOffset.UtcNow, Riyadh),
                AnalyticsBreakdown.Dashboard);
        }

        // The two daily statements convert each instant in SQL; nothing is
        // materialised to be bucketed here.
        Assert.Equal(
            2,
            recorded.Statements.Count(statement => statement.Contains("AT TIME ZONE", StringComparison.OrdinalIgnoreCase)));
    }

    // ---------------------------------------------------------- first activity

    [Fact]
    public async Task A_workspace_with_no_conversation_has_no_first_activity()
    {
        AnalyticsWorld world = await NewWorldAsync();

        WorkspaceAnalytics analytics = await ReadAsync(world, AnalyticsRange.ThirtyDays.Window(DateTimeOffset.UtcNow));

        Assert.Null(analytics.FirstActivityOn);
    }

    [Fact]
    public async Task The_first_activity_is_the_earliest_conversation_whenever_it_was_in_the_workspace_zone()
    {
        AnalyticsWorld world = await NewWorldAsync();
        AiAgent agent = await world.AgentAsync("Support", new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));

        // Older than any window, and at 22:30 UTC: the 4th in the Gulf.
        await world.WebChatAsync(agent, new DateTimeOffset(2025, 3, 3, 22, 30, 0, TimeSpan.Zero));
        await world.WebChatAsync(agent, new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero));
        await world.WebChatAsync(agent, new DateTimeOffset(2025, 6, 1, 9, 0, 0, TimeSpan.Zero));

        DateTimeOffset now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(
            new DateOnly(2025, 3, 3),
            (await ReadAsync(world, AnalyticsRange.SevenDays.Window(now))).FirstActivityOn);

        Assert.Equal(
            new DateOnly(2025, 3, 4),
            (await ReadAsync(world, AnalyticsRange.SevenDays.Window(now, Riyadh))).FirstActivityOn);
    }

    [Fact]
    public async Task Another_workspace_s_conversations_are_not_its_first_activity()
    {
        AnalyticsWorld mine = await NewWorldAsync();
        AnalyticsWorld theirs = await NewWorldAsync();
        AiAgent agent = await theirs.AgentAsync("Support", new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));

        await theirs.WebChatAsync(agent, new DateTimeOffset(2025, 3, 3, 9, 0, 0, TimeSpan.Zero));

        Assert.Null((await ReadAsync(mine, AnalyticsRange.SevenDays.Window(DateTimeOffset.UtcNow))).FirstActivityOn);
    }

    // ---------------------------------------------------------- previous period

    [Fact]
    public async Task An_event_on_the_boundary_is_counted_once_in_the_current_period()
    {
        AnalyticsWorld world = await NewWorldAsync();
        AiAgent agent = await world.AgentAsync("Support", new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));

        DateTimeOffset now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
        AnalyticsWindow current = AnalyticsRange.SevenDays.Window(now, Riyadh);
        AnalyticsWindow previous = current.Previous();

        Conversation onBoundary = await world.WebChatAsync(agent, current.From);
        Conversation justBefore = await world.WebChatAsync(agent, current.From.AddTicks(-10));
        Conversation firstOfPrevious = await world.WebChatAsync(agent, previous.From);
        await world.WebChatAsync(agent, previous.From.AddTicks(-10));

        await world.TurnAsync(onBoundary, ConversationTurnStatus.Completed, current.From);
        await world.TurnAsync(justBefore, ConversationTurnStatus.Failed, current.From.AddTicks(-10));

        WorkspaceAnalytics read = await ReadAsync(world, current);

        AnalyticsComparison earlier;
        await using (OmniAiDbContext context = database.CreateContext())
        {
            earlier = await new WorkspaceAnalyticsReader(context).ReadComparisonAsync(world.Tenant.Id, previous);
        }

        Assert.Equal(1, read.Summary.ConversationsStarted);
        Assert.Equal(2, earlier.Summary.ConversationsStarted);

        Assert.Equal(1, read.AiOperations.Completed);
        Assert.Equal(0, read.AiOperations.Failed);
        Assert.Equal(1, earlier.AiOperations.Failed);
        Assert.Equal(0, earlier.AiOperations.Completed);

        Assert.Equal(current.From, earlier.Period.To);
        Assert.Equal(7, earlier.Period.Days);
        Assert.Equal("Asia/Riyadh", earlier.Period.TimeZone);

        _ = firstOfPrevious;
    }

    [Fact]
    public async Task The_comparison_is_four_counting_statements_whatever_the_workspace_holds()
    {
        AnalyticsWorld world = await NewWorldAsync();
        AiAgent agent = await world.AgentAsync("Support", new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));

        for (int index = 0; index < 5; index++)
        {
            Conversation conversation = await world.WebChatAsync(agent, new DateTimeOffset(2026, 10, 20 + index, 9, 0, 0, TimeSpan.Zero));
            await world.SayAsync(conversation, (MessageSender.Customer, conversation.StartedAt));
        }

        RecordedSql recorded = new();

        await using (OmniAiDbContext context = database.CreateContext(recorded))
        {
            await new WorkspaceAnalyticsReader(context).ReadComparisonAsync(
                world.Tenant.Id,
                AnalyticsRange.ThirtyDays.Window(new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero)).Previous());
        }

        // Phase 39.1 (BA39-1): the summary's "answered by the AI" is one more
        // counting statement, whatever the workspace holds.
        Assert.Equal(4, recorded.Statements.Count);
        Assert.All(recorded.Statements, statement => Assert.Contains("count(", statement, StringComparison.OrdinalIgnoreCase));
    }

    // -------------------------------------------------------------- by purpose

    [Fact]
    public async Task Usage_by_purpose_adds_up_to_the_totals_and_keeps_currencies_apart()
    {
        AnalyticsWorld world = await NewWorldAsync();
        DateTimeOffset at = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

        await world.UsageAsync(
            at,
            reported: ProviderTokenUsage.Create(inputTokens: 1_000, outputTokens: 100),
            rates: AnalyticsWorld.Rates());
        await world.UsageAsync(
            at,
            reported: ProviderTokenUsage.Create(inputTokens: 2_000, outputTokens: 200),
            rates: AnalyticsWorld.Rates(currency: "EUR"));
        await world.UsageAsync(at, status: AiOperationStatus.Failed);
        await world.UsageAsync(
            at,
            AiOperationType.Embedding,
            AiUsagePurpose.ConversationRetrievalEmbedding,
            model: "test-embed-a",
            reported: ProviderTokenUsage.Create(inputTokens: 30));
        await world.UsageAsync(
            at,
            AiOperationType.Embedding,
            AiUsagePurpose.KnowledgeIndexEmbedding,
            model: "test-embed-a",
            reported: ProviderTokenUsage.Create(inputTokens: 5_000));
        await world.UsageAsync(
            at,
            AiOperationType.TextGeneration,
            AiUsagePurpose.AgentPreviewGeneration,
            status: AiOperationStatus.Unknown);
        await world.UsageAsync(
            at,
            AiOperationType.Embedding,
            AiUsagePurpose.AgentRetrievalTest,
            model: "test-embed-a",
            reported: ProviderTokenUsage.Create(inputTokens: 12));

        AnalyticsAiUsage usage = (await ReadAsync(
            world,
            AnalyticsRange.SevenDays.Window(new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero)))).AiUsage;

        // Every purpose, in the enum's order, whether or not it was used.
        Assert.Equal(Enum.GetValues<AiUsagePurpose>(), usage.ByPurpose.Select(row => row.Purpose));

        Assert.Equal(usage.Calls, usage.ByPurpose.Sum(row => row.Calls));
        Assert.Equal(usage.Succeeded, usage.ByPurpose.Sum(row => row.Succeeded));
        Assert.Equal(usage.Failed, usage.ByPurpose.Sum(row => row.Failed));
        Assert.Equal(usage.Unresolved, usage.ByPurpose.Sum(row => row.Unresolved));
        Assert.Equal(usage.UnratedCalls, usage.ByPurpose.Sum(row => row.UnratedCalls));
        Assert.Equal(
            usage.GenerationInputTokens + usage.EmbeddingInputTokens,
            usage.ByPurpose.Sum(row => row.InputTokens));
        Assert.Equal(usage.GenerationOutputTokens, usage.ByPurpose.Sum(row => row.OutputTokens));

        AnalyticsPurposeUsage replies = usage.ByPurpose.Single(row => row.Purpose == AiUsagePurpose.ConversationGeneration);
        Assert.Equal(3, replies.Calls);
        Assert.Equal(2, replies.Succeeded);
        Assert.Equal(1, replies.Failed);
        Assert.Equal(3_000, replies.InputTokens);
        Assert.Equal(300, replies.OutputTokens);

        // Two currencies, two figures, never one.
        Assert.Equal(["EUR", "USD"], replies.RatedCost.Select(cost => cost.Currency));
        Assert.All(replies.RatedCost, cost => Assert.Equal(1, cost.Calls));

        Assert.Equal(1, usage.ByPurpose.Single(row => row.Purpose == AiUsagePurpose.AgentRetrievalTest).Calls);
        Assert.Equal(12, usage.ByPurpose.Single(row => row.Purpose == AiUsagePurpose.AgentRetrievalTest).InputTokens);
        Assert.Equal(1, usage.ByPurpose.Single(row => row.Purpose == AiUsagePurpose.AgentPreviewGeneration).Unresolved);
        Assert.Equal(5_000, usage.ByPurpose.Single(row => row.Purpose == AiUsagePurpose.KnowledgeIndexEmbedding).InputTokens);

        // The headline per-currency figures are the sums of the purposes'.
        Assert.Equal(
            usage.RatedCost.Select(cost => (cost.Currency, cost.EstimatedCost, cost.Calls)),
            usage.ByPurpose
                .SelectMany(row => row.RatedCost)
                .GroupBy(cost => cost.Currency)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => (group.Key, group.Sum(cost => cost.EstimatedCost), group.Sum(cost => cost.Calls))));
    }

    /// <remarks>
    /// Phase 37.6's latency finding: token sums reached through the owned
    /// <c>Usage</c> inside a group were translated as one correlated subquery per
    /// sum per group, so adding the purpose key doubled their cost. Each usage
    /// statement now reads the table once.
    /// </remarks>
    [Fact]
    public async Task Usage_statements_read_the_table_once_with_no_correlated_subquery()
    {
        AnalyticsWorld world = await NewWorldAsync();
        await world.UsageAsync(new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero));

        RecordedSql recorded = new();

        await using (OmniAiDbContext context = database.CreateContext(recorded))
        {
            await new WorkspaceAnalyticsReader(context).ReadAsync(
                world.Tenant.Id,
                AnalyticsRange.SevenDays.Window(new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero)),
                AnalyticsBreakdown.Dashboard);
        }

        List<string> usage =
        [
            .. recorded.Statements.Where(statement =>
                statement.Contains("FROM ai_provider_usages", StringComparison.OrdinalIgnoreCase)),
        ];

        Assert.NotEmpty(usage);
        Assert.All(usage, statement => Assert.True(
            System.Text.RegularExpressions.Regex.Count(
                statement,
                "FROM ai_provider_usages",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase) == 1,
            $"A usage statement reads the table more than once: {statement}"));
    }

    [Fact]
    public async Task With_no_calls_every_purpose_is_an_honest_zero()
    {
        AnalyticsWorld world = await NewWorldAsync();

        AnalyticsAiUsage usage = (await ReadAsync(world, AnalyticsRange.SevenDays.Window(DateTimeOffset.UtcNow))).AiUsage;

        Assert.False(usage.HasAny);
        Assert.Equal(Enum.GetValues<AiUsagePurpose>().Length, usage.ByPurpose.Count);
        Assert.All(usage.ByPurpose, row =>
        {
            Assert.Equal(0, row.Calls);
            Assert.Equal(0, row.InputTokens);
            Assert.Empty(row.RatedCost);
        });
    }

    // ----------------------------------------------------------------- helpers

    private async Task<AnalyticsWorld> NewWorldAsync() =>
        await new AnalyticsWorld(database).StartAsync();

    private async Task<WorkspaceAnalytics> ReadAsync(AnalyticsWorld world, AnalyticsWindow window)
    {
        await using OmniAiDbContext context = database.CreateContext();

        return await new WorkspaceAnalyticsReader(context)
            .ReadAsync(world.Tenant.Id, window, AnalyticsBreakdown.Dashboard);
    }
}
