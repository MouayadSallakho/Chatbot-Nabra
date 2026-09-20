using Microsoft.EntityFrameworkCore;
using OmniAI.Application.Plans;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Plans;
using OmniAI.Domain.Tenants;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Plans;

/// <summary>
/// Entitlements, against a real PostgreSQL.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The concurrency claims are the reason this suite exists.</strong>
/// "Two requests at the last free slot, only one succeeds" is a statement about
/// what the database does when two transactions meet. An in-memory double would
/// serialise them by accident and prove only that it agrees with itself.
/// </para>
/// <para>
/// Every test builds its own workspace and its own plan, so two running together
/// cannot see each other's rows or wait on each other's locks.
/// </para>
/// </remarks>
public sealed class WorkspaceEntitlementServiceTests(PostgresTestDatabase database)
    : IClassFixture<PostgresTestDatabase>
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

    // ---------------------------------------------------------------- reading

    [Fact]
    public async Task A_workspace_reports_the_plan_it_is_actually_on()
    {
        PlanWorld world = await New(agents: 3);

        (IWorkspaceEntitlementService service, OmniAiDbContext context) = world.Service();

        await using (context)
        {
            WorkspaceEntitlements entitlements = await service.DescribeAsync(world.Tenant.Id);

            Assert.Equal(world.Plan.Code.Value, entitlements.PlanCode);
            Assert.Equal("Test plan", entitlements.PlanName);

            // Every entitlement is described, not only the capped ones: a screen
            // should not have to guess which ones exist.
            Assert.Equal(6, entitlements.Entitlements.Count);
        }
    }

    [Fact]
    public async Task Usage_is_counted_from_the_resources_themselves()
    {
        PlanWorld world = await New(agents: 10, documents: 10, webChat: 10, whatsApp: 10);

        await world.AddAgentsAsync(2);
        await world.AddDocumentsAsync(3);
        await world.AddWebChatAsync(1);
        await world.AddWhatsAppAsync(1);

        WorkspaceEntitlements entitlements = await Describe(world);

        // The web chat and WhatsApp helpers each create an agent too, so agents
        // are 2 + 1 + 1. Counted from the rows, not from a counter somebody
        // remembered to increment.
        Assert.Equal(4, Used(entitlements, EntitlementKind.AiAgents));
        Assert.Equal(3, Used(entitlements, EntitlementKind.KnowledgeDocuments));
        Assert.Equal(1, Used(entitlements, EntitlementKind.WebChatEndpoints));
        Assert.Equal(1, Used(entitlements, EntitlementKind.WhatsAppConnections));
    }

    [Fact]
    public async Task An_unlimited_entitlement_reports_no_limit_and_no_percentage()
    {
        PlanWorld world = await New();

        await world.AddAgentsAsync(50);

        WorkspaceEntitlementView agents = View(await Describe(world), EntitlementKind.AiAgents);

        Assert.True(agents.IsUnlimited);
        Assert.Null(agents.Limit);
        Assert.Null(agents.PercentUsed);
        Assert.Null(agents.Remaining);
        Assert.Equal(EntitlementState.Normal, agents.State);
    }

    [Fact]
    public async Task A_monthly_entitlement_carries_the_month_it_covers()
    {
        PlanWorld world = await New(monthlyOperations: 100);

        WorkspaceEntitlementView operations =
            View(await Describe(world, Noon), EntitlementKind.MonthlyProviderOperations);

        Assert.NotNull(operations.Period);
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), operations.Period.From);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), operations.Period.To);

        // Only the monthly one has a period. The rest are a count of what exists
        // now, not of what happened during some window.
        Assert.Null(View(await Describe(world, Noon), EntitlementKind.AiAgents).Period);
    }

    [Fact]
    public async Task A_workspace_with_no_plan_is_refused_rather_than_treated_as_unlimited()
    {
        PlanWorld world = await New(agents: 1);

        await using (OmniAiDbContext context = database.CreateContext())
        {
            await context.WorkspacePlanAssignments
                .Where(assignment => assignment.TenantId == world.Tenant.Id)
                .ExecuteDeleteAsync();
        }

        (IWorkspaceEntitlementService service, OmniAiDbContext scoped) = world.Service();

        await using (scoped)
        {
            // The convenient guess would be "unlimited", and it is the one nobody
            // would ever notice: a production workspace would quietly get
            // everything for free.
            await Assert.ThrowsAsync<WorkspacePlanMissingException>(
                () => service.DescribeAsync(world.Tenant.Id));
        }
    }

    // ---------------------------------------------------------- tenant safety

    [Fact]
    public async Task One_workspaces_usage_is_never_another_workspaces()
    {
        PlanWorld mine = await New(agents: 2);
        PlanWorld theirs = await New(agents: 2);

        await mine.AddAgentsAsync(2);

        Assert.Equal(2, Used(await Describe(mine), EntitlementKind.AiAgents));
        Assert.Equal(0, Used(await Describe(theirs), EntitlementKind.AiAgents));

        // Mine is full; theirs is untouched by that.
        Assert.True(View(await Describe(mine), EntitlementKind.AiAgents).State
            == EntitlementState.Exhausted);

        await Reserve(theirs, EntitlementKind.AiAgents);

        Assert.Equal(1, await theirs.CountAgentsAsync());
    }

    [Fact]
    public async Task Two_workspaces_may_share_one_plan()
    {
        PlanWorld first = await New(agents: 1);

        Tenant second = Tenant.Create(
            TenantName.Create("Globex"),
            TenantSlug.Create($"t{Guid.NewGuid():N}"[..20]));

        await using (OmniAiDbContext context = database.CreateContext())
        {
            context.Tenants.Add(second);
            context.WorkspacePlanAssignments.Add(
                WorkspacePlanAssignment.Create(second.Id, first.Plan.Id, DateTimeOffset.UtcNow));

            await context.SaveChangesAsync();
        }

        (IWorkspaceEntitlementService service, OmniAiDbContext scoped) = first.Service();

        await using (scoped)
        {
            // A plan is platform configuration, shared by many workspaces. Their
            // usage of it is entirely separate.
            Assert.Equal(first.Plan.Code.Value, (await service.DescribeAsync(second.Id)).PlanCode);
            Assert.Equal(0, Used(await service.DescribeAsync(second.Id), EntitlementKind.AiAgents));
        }
    }

    // ------------------------------------------------------------ enforcement

    [Fact]
    public async Task A_reservation_under_the_limit_performs_the_mutation()
    {
        PlanWorld world = await New(agents: 2);

        await Reserve(world, EntitlementKind.AiAgents);

        Assert.Equal(1, await world.CountAgentsAsync());
    }

    [Fact]
    public async Task A_reservation_at_the_limit_is_refused_and_writes_nothing()
    {
        PlanWorld world = await New(agents: 2);

        await world.AddAgentsAsync(2);

        EntitlementExceededException refused =
            await Assert.ThrowsAsync<EntitlementExceededException>(
                () => Reserve(world, EntitlementKind.AiAgents));

        Assert.Equal(EntitlementKind.AiAgents, refused.Kind);
        Assert.Equal(2, refused.Current);
        Assert.Equal(2, refused.Limit);

        // The mutation ran inside the reservation, so a refusal leaves nothing
        // behind rather than an agent nobody was allowed to create.
        Assert.Equal(2, await world.CountAgentsAsync());
    }

    [Fact]
    public async Task A_limit_of_zero_refuses_the_first_one()
    {
        PlanWorld world = await New(agents: 0);

        await Assert.ThrowsAsync<EntitlementExceededException>(
            () => Reserve(world, EntitlementKind.AiAgents));

        Assert.Equal(0, await world.CountAgentsAsync());
    }

    [Fact]
    public async Task An_unlimited_entitlement_never_refuses()
    {
        PlanWorld world = await New();

        for (int index = 0; index < 5; index++)
        {
            await Reserve(world, EntitlementKind.AiAgents);
        }

        Assert.Equal(5, await world.CountAgentsAsync());
    }

    [Fact]
    public async Task A_failing_mutation_leaves_nothing_behind_and_frees_the_slot()
    {
        PlanWorld world = await New(agents: 2);

        (IWorkspaceEntitlementService service, OmniAiDbContext context) = world.Service();

        await using (context)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.ReserveAsync<int>(
                    world.Tenant.Id,
                    EntitlementKind.AiAgents,
                    _ => throw new InvalidOperationException("The mutation failed.")));
        }

        // Nothing was written, and the workspace still has both its slots.
        Assert.Equal(0, await world.CountAgentsAsync());

        await Reserve(world, EntitlementKind.AiAgents);
        await Reserve(world, EntitlementKind.AiAgents);

        Assert.Equal(2, await world.CountAgentsAsync());
    }

    // ----------------------------------------------------------- concurrency

    /// <remarks>
    /// <strong>The claim the whole design rests on.</strong> Counting, returning,
    /// and inserting afterwards would let both of these pass the count and both
    /// create an agent. The lock is what makes the second one wait, count again,
    /// and lose.
    /// </remarks>
    [Fact]
    public async Task Two_creates_racing_for_the_last_slot_produce_exactly_one()
    {
        PlanWorld world = await New(agents: 3);

        await world.AddAgentsAsync(2);

        Task<int> first = Reserve(world, EntitlementKind.AiAgents);
        Task<int> second = Reserve(world, EntitlementKind.AiAgents);

        Exception?[] outcomes = await Outcomes(first, second);

        Assert.Single(outcomes, outcome => outcome is null);
        Assert.Single(outcomes, outcome => outcome is EntitlementExceededException);

        Assert.Equal(3, await world.CountAgentsAsync());
    }

    [Fact]
    public async Task Eight_creates_racing_for_two_slots_produce_exactly_two()
    {
        PlanWorld world = await New(agents: 2);

        Task<int>[] attempts =
            [.. Enumerable.Range(0, 8).Select(_ => Reserve(world, EntitlementKind.AiAgents))];

        Exception?[] outcomes = await Outcomes(attempts);

        Assert.Equal(2, outcomes.Count(outcome => outcome is null));
        Assert.Equal(6, outcomes.Count(outcome => outcome is EntitlementExceededException));

        Assert.Equal(2, await world.CountAgentsAsync());
    }

    [Fact]
    public async Task A_race_in_one_workspace_does_not_block_another()
    {
        PlanWorld mine = await New(agents: 1);
        PlanWorld theirs = await New(agents: 1);

        // Different workspaces take different locks, so these do not queue behind
        // each other -- and both succeed, because each has its own free slot.
        Exception?[] outcomes = await Outcomes(
            Reserve(mine, EntitlementKind.AiAgents),
            Reserve(theirs, EntitlementKind.AiAgents));

        Assert.All(outcomes, Assert.Null);
        Assert.Equal(1, await mine.CountAgentsAsync());
        Assert.Equal(1, await theirs.CountAgentsAsync());
    }

    // ------------------------------------------------------- monthly quota

    [Fact]
    public async Task Provider_operations_are_counted_for_the_utc_month_in_progress()
    {
        PlanWorld world = await New(monthlyOperations: 100);

        await world.AddOperationsAsync(3, Noon);

        // Last month and next month are somebody else's problem.
        await world.AddOperationsAsync(5, new DateTimeOffset(2026, 8, 31, 23, 59, 59, TimeSpan.Zero));
        await world.AddOperationsAsync(7, new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(
            3,
            Used(await Describe(world, Noon), EntitlementKind.MonthlyProviderOperations));
    }

    [Fact]
    public async Task The_month_is_half_open_at_both_ends()
    {
        PlanWorld world = await New(monthlyOperations: 100);

        // The first instant of September belongs to September.
        await world.AddOperationsAsync(1, new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));

        // The last instant before October does too.
        await world.AddOperationsAsync(
            1, new DateTimeOffset(2026, 9, 30, 23, 59, 59, TimeSpan.Zero));

        // The first instant of October does not.
        await world.AddOperationsAsync(1, new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(
            2,
            Used(await Describe(world, Noon), EntitlementKind.MonthlyProviderOperations));
    }

    /// <remarks>
    /// A calendar month, not a rolling thirty days. The allowance resets on a date
    /// somebody can put in a diary, and nothing historical is deleted to make it
    /// happen.
    /// </remarks>
    [Fact]
    public async Task A_quota_reached_on_the_last_day_is_available_again_next_month()
    {
        PlanWorld world = await New(monthlyOperations: 2);

        DateTimeOffset lastInstant = new(2026, 9, 30, 23, 59, 59, TimeSpan.Zero);

        await world.AddOperationsAsync(2, lastInstant);

        await Assert.ThrowsAsync<EntitlementExceededException>(
            () => Reserve(world, EntitlementKind.MonthlyProviderOperations, lastInstant));

        // One second later it is October, and the September rows are still there.
        DateTimeOffset october = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

        await Reserve(world, EntitlementKind.MonthlyProviderOperations, october);

        Assert.Equal(
            1,
            Used(await Describe(world, october), EntitlementKind.MonthlyProviderOperations));

        // Nothing was deleted to free the quota.
        Assert.Equal(3, await world.CountOperationsAsync());
    }

    [Fact]
    public async Task Two_provider_operations_racing_for_the_last_one_produce_exactly_one()
    {
        PlanWorld world = await New(monthlyOperations: 10);

        await world.AddOperationsAsync(9, Noon);

        Exception?[] outcomes = await Outcomes(
            Reserve(world, EntitlementKind.MonthlyProviderOperations, Noon),
            Reserve(world, EntitlementKind.MonthlyProviderOperations, Noon));

        Assert.Single(outcomes, outcome => outcome is null);
        Assert.Single(outcomes, outcome => outcome is EntitlementExceededException);

        // Ten, never eleven.
        Assert.Equal(10, await world.CountOperationsAsync());
    }

    // ----------------------------------------------------------------- helpers

    private async Task<PlanWorld> New(
        int? members = null,
        int? agents = null,
        int? documents = null,
        int? webChat = null,
        int? whatsApp = null,
        int? monthlyOperations = null) =>
        await new PlanWorld(database).StartAsync(
            members, agents, documents, webChat, whatsApp, monthlyOperations);

    private static async Task<WorkspaceEntitlements> Describe(
        PlanWorld world,
        DateTimeOffset? now = null)
    {
        (IWorkspaceEntitlementService service, OmniAiDbContext context) = world.Service(now);

        await using (context)
        {
            return await service.DescribeAsync(world.Tenant.Id);
        }
    }

    /// <summary>Takes a slot and writes the row that uses it.</summary>
    private static async Task<int> Reserve(
        PlanWorld world,
        EntitlementKind kind,
        DateTimeOffset? now = null)
    {
        (IWorkspaceEntitlementService service, OmniAiDbContext context) = world.Service(now);

        await using (context)
        {
            return await service.ReserveAsync(
                world.Tenant.Id,
                kind,
                async token =>
                {
                    if (kind == EntitlementKind.MonthlyProviderOperations)
                    {
                        context.AiProviderUsages.Add(Domain.Ai.AiProviderUsage.Begin(
                            world.Tenant.Id,
                            Domain.Ai.AiProvider.OpenAi,
                            Domain.Ai.AiOperationType.TextGeneration,
                            Domain.Ai.AiUsagePurpose.ConversationGeneration,
                            Domain.Ai.ModelName.Create("test-model-x"),
                            now ?? DateTimeOffset.UtcNow));
                    }
                    else
                    {
                        context.AiAgents.Add(AiAgent.Create(
                            world.Tenant.Id,
                            AiAgentName.Create($"Agent {Guid.NewGuid():N}"[..20]),
                            AgentInstructions.Create("Be helpful."),
                            DateTimeOffset.UtcNow));
                    }

                    return await context.SaveChangesAsync(token);
                });
        }
    }

    /// <summary>Runs competing attempts and reports how each one ended.</summary>
    private static async Task<Exception?[]> Outcomes(params Task<int>[] attempts)
    {
        Exception?[] outcomes = new Exception?[attempts.Length];

        for (int index = 0; index < attempts.Length; index++)
        {
            try
            {
                await attempts[index];
            }
            catch (Exception failure)
            {
                outcomes[index] = failure;
            }
        }

        return outcomes;
    }

    private static long Used(WorkspaceEntitlements entitlements, EntitlementKind kind) =>
        View(entitlements, kind).Used;

    private static WorkspaceEntitlementView View(
        WorkspaceEntitlements entitlements,
        EntitlementKind kind)
    {
        WorkspaceEntitlementView? view = entitlements.Find(kind);

        Assert.NotNull(view);

        return view;
    }
}
