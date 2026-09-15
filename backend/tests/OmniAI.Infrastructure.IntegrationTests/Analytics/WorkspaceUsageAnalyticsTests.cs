using OmniAI.Application.Analytics;
using OmniAI.Domain.Ai;
using OmniAI.Infrastructure.Analytics;
using OmniAI.Infrastructure.IntegrationTests.Persistence;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Analytics;

/// <summary>
/// The provider usage half of the dashboard, against a real PostgreSQL.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is internal accounting, not billing.</strong> Nothing here is an
/// invoice, a charge, or an amount anybody owes; it is what this workspace's
/// calls to a provider are estimated to have cost us, at rates an operator
/// configured. The tests are written to that standard: a figure that cannot be
/// derived honestly must come back absent rather than convenient.
/// </para>
/// <para>
/// Summing nullable columns is the whole difficulty. PostgreSQL ignores NULLs in
/// a <c>SUM</c>, which is exactly right — a provider that reported nothing must
/// not drag a total down — but it is only right if the columns really are
/// nullable and the query really runs on the server. Both need a database.
/// </para>
/// </remarks>
public sealed class WorkspaceUsageAnalyticsTests(PostgresTestDatabase database)
    : IClassFixture<PostgresTestDatabase>
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    // ------------------------------------------------------------------ empty

    [Fact]
    public async Task A_workspace_that_never_called_a_provider_reports_no_usage_at_all()
    {
        AnalyticsWorld world = await NewWorldAsync();

        AnalyticsAiUsage usage = (await ReadAsync(world)).AiUsage;

        // Not a panel of noughts. A workspace with no provider calls has nothing
        // to say about spend, and saying "0.00" would imply it had been measured.
        Assert.False(usage.HasAny);
        Assert.Equal(0, usage.Calls);
        Assert.Empty(usage.RatedCost);
        Assert.Empty((await ReadAsync(world)).Models);
    }

    // --------------------------------------------------------------- counting

    [Fact]
    public async Task Generation_and_embedding_are_counted_apart()
    {
        AnalyticsWorld world = await NewWorldAsync();

        await world.UsageAsync(Now.AddDays(-2));
        await world.UsageAsync(Now.AddDays(-2));
        await world.UsageAsync(
            Now.AddDays(-2),
            AiOperationType.Embedding,
            AiUsagePurpose.KnowledgeIndexEmbedding,
            model: "test-embed-a");

        AnalyticsAiUsage usage = (await ReadAsync(world)).AiUsage;

        Assert.True(usage.HasAny);
        Assert.Equal(3, usage.Calls);
        Assert.Equal(2, usage.GenerationCalls);
        Assert.Equal(1, usage.EmbeddingCalls);
    }

    [Fact]
    public async Task Every_outcome_is_visible_including_the_ones_nobody_resolved()
    {
        AnalyticsWorld world = await NewWorldAsync();

        await world.UsageAsync(Now.AddDays(-2), status: AiOperationStatus.Succeeded);
        await world.UsageAsync(Now.AddDays(-2), status: AiOperationStatus.Failed);
        await world.UsageAsync(Now.AddDays(-2), status: AiOperationStatus.Unknown);

        // A process that died between dispatch and the provider's answer. The row
        // stays as it was left.
        await world.UsageAsync(Now.AddDays(-2), status: AiOperationStatus.Started);

        AnalyticsAiUsage usage = (await ReadAsync(world)).AiUsage;

        Assert.Equal(4, usage.Calls);
        Assert.Equal(1, usage.Succeeded);
        Assert.Equal(1, usage.Failed);

        // Unknown and Started together: both mean the provider may well have been
        // paid, and pretending otherwise would understate what was spent.
        Assert.Equal(2, usage.Unresolved);
    }

    [Fact]
    public async Task Tokens_are_summed_per_kind_of_operation()
    {
        AnalyticsWorld world = await NewWorldAsync();

        await world.UsageAsync(
            Now.AddDays(-2),
            reported: ProviderTokenUsage.Create(
                inputTokens: 1_000,
                cachedInputTokens: 400,
                outputTokens: 200,
                reasoningTokens: 60));

        await world.UsageAsync(
            Now.AddDays(-2),
            reported: ProviderTokenUsage.Create(
                inputTokens: 500,
                cachedInputTokens: 100,
                outputTokens: 50,
                reasoningTokens: 10));

        await world.UsageAsync(
            Now.AddDays(-2),
            AiOperationType.Embedding,
            AiUsagePurpose.KnowledgeIndexEmbedding,
            model: "test-embed-a",
            reported: ProviderTokenUsage.Create(inputTokens: 9_000));

        AnalyticsAiUsage usage = (await ReadAsync(world)).AiUsage;

        Assert.Equal(1_500, usage.GenerationInputTokens);
        Assert.Equal(500, usage.GenerationCachedInputTokens);
        Assert.Equal(250, usage.GenerationOutputTokens);
        Assert.Equal(70, usage.GenerationReasoningTokens);

        // Embedding input is kept to itself. Adding it to generation input would
        // make a document index look like a conversation.
        Assert.Equal(9_000, usage.EmbeddingInputTokens);
    }

    /// <remarks>
    /// <strong>The distinction the whole feature rests on.</strong> A provider
    /// that reported no usage contributes nothing to a total — not a zero that
    /// would make the average look better, and not a gap that breaks the sum.
    /// </remarks>
    [Fact]
    public async Task A_call_the_provider_reported_nothing_for_does_not_drag_a_total_down()
    {
        AnalyticsWorld world = await NewWorldAsync();

        await world.UsageAsync(
            Now.AddDays(-2),
            reported: ProviderTokenUsage.Create(inputTokens: 1_000, outputTokens: 100));

        await world.UsageAsync(Now.AddDays(-2), reported: ProviderTokenUsage.Unreported);

        AnalyticsAiUsage usage = (await ReadAsync(world)).AiUsage;

        // Two calls happened; one of them can be quantified.
        Assert.Equal(2, usage.Calls);
        Assert.Equal(1_000, usage.GenerationInputTokens);
        Assert.Equal(100, usage.GenerationOutputTokens);
    }

    [Fact]
    public async Task A_failed_call_still_counts_as_a_call()
    {
        AnalyticsWorld world = await NewWorldAsync();

        await world.UsageAsync(Now.AddDays(-2), status: AiOperationStatus.Failed);

        AnalyticsAiUsage usage = (await ReadAsync(world)).AiUsage;

        // It reached the provider. Whether it produced anything useful is a
        // separate question from whether it happened.
        Assert.Equal(1, usage.Calls);
        Assert.Equal(1, usage.Failed);
        Assert.Equal(0, usage.GenerationInputTokens);
    }

    // -------------------------------------------------------- estimated cost

    [Fact]
    public async Task Rated_calls_are_totalled_in_their_own_currency()
    {
        AnalyticsWorld world = await NewWorldAsync();

        // 1M in at 2.00 and 1M out at 8.00, twice over.
        for (int index = 0; index < 2; index++)
        {
            await world.UsageAsync(
                Now.AddDays(-2),
                reported: ProviderTokenUsage.Create(
                    inputTokens: 1_000_000,
                    outputTokens: 1_000_000),
                rates: AnalyticsWorld.Rates());
        }

        AnalyticsAiUsage usage = (await ReadAsync(world)).AiUsage;

        AnalyticsRatedCost cost = Assert.Single(usage.RatedCost);

        Assert.Equal("USD", cost.Currency);
        Assert.Equal(20.00m, cost.EstimatedCost);
        Assert.Equal(2, cost.Calls);
        Assert.Equal(0, usage.UnratedCalls);
    }

    /// <remarks>
    /// There is no exchange rate anywhere in this platform, and there must not be
    /// one: a made-up conversion would turn an estimate into a fiction.
    /// </remarks>
    [Fact]
    public async Task Two_currencies_are_reported_side_by_side_and_never_added()
    {
        AnalyticsWorld world = await NewWorldAsync();

        await world.UsageAsync(
            Now.AddDays(-2),
            reported: ProviderTokenUsage.Create(inputTokens: 1_000_000),
            rates: AnalyticsWorld.Rates());

        await world.UsageAsync(
            Now.AddDays(-2),
            model: "test-model-eu",
            reported: ProviderTokenUsage.Create(inputTokens: 1_000_000),
            rates: AnalyticsWorld.Rates(model: "test-model-eu", currency: "EUR"));

        AnalyticsAiUsage usage = (await ReadAsync(world)).AiUsage;

        Assert.Equal(2, usage.RatedCost.Count);

        Assert.Equal(2.00m, usage.RatedCost.Single(cost => cost.Currency == "EUR").EstimatedCost);
        Assert.Equal(2.00m, usage.RatedCost.Single(cost => cost.Currency == "USD").EstimatedCost);
    }

    /// <remarks>
    /// <strong>Unrated is not free.</strong> A model nobody configured a price for
    /// produced real tokens and cost real money; reporting it inside the rated
    /// total at nought would make the total quietly wrong, and reporting it
    /// nowhere would make the total quietly incomplete.
    /// </remarks>
    [Fact]
    public async Task Calls_with_no_configured_rate_are_counted_rather_than_priced_at_nothing()
    {
        AnalyticsWorld world = await NewWorldAsync();

        await world.UsageAsync(
            Now.AddDays(-2),
            reported: ProviderTokenUsage.Create(inputTokens: 1_000_000),
            rates: AnalyticsWorld.Rates());

        await world.UsageAsync(
            Now.AddDays(-2),
            model: "test-model-nobody-priced",
            reported: ProviderTokenUsage.Create(inputTokens: 5_000_000));

        AnalyticsAiUsage usage = (await ReadAsync(world)).AiUsage;

        AnalyticsRatedCost cost = Assert.Single(usage.RatedCost);

        // The rated total covers one of the two calls, and says so.
        Assert.Equal(2.00m, cost.EstimatedCost);
        Assert.Equal(1, cost.Calls);
        Assert.Equal(1, usage.UnratedCalls);

        // The tokens are still counted. Not knowing the price is not the same as
        // not knowing the usage.
        Assert.Equal(6_000_000, usage.GenerationInputTokens);
    }

    [Fact]
    public async Task Money_survives_the_round_trip_without_a_rounding_error()
    {
        AnalyticsWorld world = await NewWorldAsync();

        // Three calls of 1,234 input tokens at 2.00 per million: 0.002468 each,
        // which no binary float represents exactly.
        for (int index = 0; index < 3; index++)
        {
            await world.UsageAsync(
                Now.AddDays(-2),
                reported: ProviderTokenUsage.Create(inputTokens: 1_234),
                rates: AnalyticsWorld.Rates());
        }

        AnalyticsRatedCost cost = Assert.Single((await ReadAsync(world)).AiUsage.RatedCost);

        Assert.Equal(0.007404m, cost.EstimatedCost);
    }

    // ------------------------------------------------------- model breakdown

    [Fact]
    public async Task The_model_the_provider_says_it_used_is_the_one_reported()
    {
        AnalyticsWorld world = await NewWorldAsync();

        await world.UsageAsync(
            Now.AddDays(-2),
            model: "test-model-x",
            reported: ProviderTokenUsage.Create(inputTokens: 100, outputTokens: 10),
            resolvedModel: "test-model-x-2026-09-01");

        AnalyticsModelUsage model = Assert.Single((await ReadAsync(world)).Models);

        // An alias resolved to a dated build belongs under the build that was
        // actually billed, not under the alias somebody typed.
        Assert.Equal("test-model-x-2026-09-01", model.Model);
        Assert.Equal("OpenAi", model.Provider);
        Assert.Equal(AiOperationType.TextGeneration, model.OperationType);
        Assert.Equal(1, model.Calls);
        Assert.Equal(100, model.InputTokens);
        Assert.Equal(10, model.OutputTokens);
    }

    [Fact]
    public async Task A_model_that_was_never_resolved_appears_under_what_was_asked_for()
    {
        AnalyticsWorld world = await NewWorldAsync();

        await world.UsageAsync(Now.AddDays(-2), model: "test-model-x", status: AiOperationStatus.Failed);

        Assert.Equal("test-model-x", Assert.Single((await ReadAsync(world)).Models).Model);
    }

    [Fact]
    public async Task The_same_model_used_two_ways_is_two_rows()
    {
        AnalyticsWorld world = await NewWorldAsync();

        await world.UsageAsync(Now.AddDays(-2), model: "test-model-dual");
        await world.UsageAsync(
            Now.AddDays(-2),
            AiOperationType.Embedding,
            AiUsagePurpose.KnowledgeIndexEmbedding,
            model: "test-model-dual");

        IReadOnlyList<AnalyticsModelUsage> models = (await ReadAsync(world)).Models;

        // Generation and embedding are different products at different prices.
        // Folding them together would produce a per-call figure meaning nothing.
        Assert.Equal(2, models.Count);
        Assert.All(models, model => Assert.Equal("test-model-dual", model.Model));
        Assert.Single(models, model => model.OperationType == AiOperationType.Embedding);
    }

    [Fact]
    public async Task A_model_billed_in_two_currencies_reports_no_single_figure()
    {
        AnalyticsWorld world = await NewWorldAsync();

        await world.UsageAsync(
            Now.AddDays(-2),
            model: "test-model-x",
            reported: ProviderTokenUsage.Create(inputTokens: 1_000_000),
            rates: AnalyticsWorld.Rates());

        await world.UsageAsync(
            Now.AddDays(-2),
            model: "test-model-x",
            reported: ProviderTokenUsage.Create(inputTokens: 1_000_000),
            rates: AnalyticsWorld.Rates(currency: "EUR"));

        AnalyticsModelUsage model = Assert.Single((await ReadAsync(world)).Models);

        // Both calls counted, both lots of tokens counted, and no cost — because
        // there is no honest number to put there.
        Assert.Equal(2, model.Calls);
        Assert.Equal(2_000_000, model.InputTokens);
        Assert.Null(model.EstimatedCost);
        Assert.Null(model.Currency);

        // The workspace total still reports both, apart.
        Assert.Equal(2, (await ReadAsync(world)).AiUsage.RatedCost.Count);
    }

    [Fact]
    public async Task The_breakdown_is_bounded_however_many_models_a_workspace_used()
    {
        AnalyticsWorld world = await NewWorldAsync();

        for (int index = 0; index < 14; index++)
        {
            await world.UsageAsync(Now.AddDays(-2), model: $"test-model-{index:00}");
        }

        IReadOnlyList<AnalyticsModelUsage> models = (await ReadAsync(world)).Models;

        // A dashboard is not a data export. Ten busiest, and the totals above it
        // still cover every call.
        Assert.Equal(10, models.Count);
        Assert.Equal(14, (await ReadAsync(world)).AiUsage.Calls);
    }

    // --------------------------------------------------- windows and isolation

    [Fact]
    public async Task A_call_is_counted_in_the_period_it_was_dispatched_in()
    {
        AnalyticsWorld world = await NewWorldAsync();

        await world.UsageAsync(Now.AddDays(-3));
        await world.UsageAsync(Now.AddDays(-20));
        await world.UsageAsync(Now.AddDays(-60));

        Assert.Equal(1, (await ReadAsync(world, AnalyticsRange.SevenDays)).AiUsage.Calls);
        Assert.Equal(2, (await ReadAsync(world, AnalyticsRange.ThirtyDays)).AiUsage.Calls);
        Assert.Equal(3, (await ReadAsync(world, AnalyticsRange.NinetyDays)).AiUsage.Calls);
    }

    /// <remarks>
    /// Half-open, as everywhere else in analytics: a call on the boundary belongs
    /// to exactly one period. The alternative is a call that appears in two
    /// reports and a total that never reconciles.
    /// </remarks>
    [Fact]
    public async Task A_call_on_the_boundary_belongs_to_exactly_one_period()
    {
        AnalyticsWorld world = await NewWorldAsync();

        AnalyticsWindow window = AnalyticsRange.SevenDays.Window(Now);

        await world.UsageAsync(window.From);
        await world.UsageAsync(window.From.AddTicks(-1));

        Assert.Equal(1, (await ReadAsync(world, AnalyticsRange.SevenDays)).AiUsage.Calls);
    }

    [Fact]
    public async Task A_call_dispatched_after_the_window_closes_is_not_counted_yet()
    {
        AnalyticsWorld world = await NewWorldAsync();

        await world.UsageAsync(Now.AddHours(1));

        Assert.Equal(0, (await ReadAsync(world)).AiUsage.Calls);
    }

    [Fact]
    public async Task One_workspace_never_sees_another_workspaces_spend()
    {
        AnalyticsWorld mine = await NewWorldAsync();
        AnalyticsWorld theirs = await NewWorldAsync();

        await mine.UsageAsync(
            Now.AddDays(-2),
            reported: ProviderTokenUsage.Create(inputTokens: 1_000_000),
            rates: AnalyticsWorld.Rates());

        for (int index = 0; index < 5; index++)
        {
            await theirs.UsageAsync(
                Now.AddDays(-2),
                reported: ProviderTokenUsage.Create(inputTokens: 1_000_000),
                rates: AnalyticsWorld.Rates());
        }

        Assert.Equal(1, (await ReadAsync(mine)).AiUsage.Calls);
        Assert.Equal(2.00m, Assert.Single((await ReadAsync(mine)).AiUsage.RatedCost).EstimatedCost);

        Assert.Equal(5, (await ReadAsync(theirs)).AiUsage.Calls);
    }

    // ------------------------------------------------------------- the queries

    /// <remarks>
    /// The same claim the rest of the reader makes, checked for the usage
    /// queries: a workspace with a year of provider calls must not pull them all
    /// into memory to add up.
    /// </remarks>
    [Fact]
    public async Task Usage_is_aggregated_by_postgresql_and_not_in_memory()
    {
        AnalyticsWorld world = await NewWorldAsync();

        await world.UsageAsync(
            Now.AddDays(-2),
            reported: ProviderTokenUsage.Create(inputTokens: 1_000),
            rates: AnalyticsWorld.Rates());

        RecordedSql recorded = new();

        await using (OmniAiDbContext context = database.CreateContext(recorded))
        {
            await new WorkspaceAnalyticsReader(context).ReadAsync(
                world.Tenant.Id,
                AnalyticsRange.ThirtyDays.Window(Now));
        }

        List<string> overUsage =
        [
            .. recorded.Statements.Where(statement => statement.Contains(
                "FROM ai_provider_usages", StringComparison.OrdinalIgnoreCase)),
        ];

        Assert.NotEmpty(overUsage);

        Assert.All(overUsage, statement => Assert.True(
            statement.Contains("count(", StringComparison.OrdinalIgnoreCase)
            || statement.Contains("sum(", StringComparison.OrdinalIgnoreCase),
            $"A usage statement that neither counts nor sums is reading rows: {statement}"));
    }

    private async Task<AnalyticsWorld> NewWorldAsync() =>
        await new AnalyticsWorld(database).StartAsync();

    private async Task<WorkspaceAnalytics> ReadAsync(
        AnalyticsWorld world,
        AnalyticsRange range = AnalyticsRange.ThirtyDays)
    {
        await using OmniAiDbContext context = database.CreateContext();

        return await new WorkspaceAnalyticsReader(context)
            .ReadAsync(world.Tenant.Id, range.Window(Now));
    }
}
