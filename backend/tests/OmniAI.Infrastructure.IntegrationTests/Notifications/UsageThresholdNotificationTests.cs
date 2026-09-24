using Microsoft.EntityFrameworkCore;
using OmniAI.Application.Plans;
using OmniAI.Domain.Ai;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Notifications;
using OmniAI.Domain.Plans;
using OmniAI.Domain.Users;
using OmniAI.Infrastructure.IntegrationTests.Reports;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Infrastructure.Plans;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Notifications;

/// <summary>
/// Phase 24 thresholds becoming alerts: once per threshold per UTC month, under
/// concurrency, through the real reservation gate.
/// </summary>
public sealed class UsageThresholdNotificationTests(PostgresTestDatabase database)
    : IClassFixture<PostgresTestDatabase>
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Crossing_each_threshold_records_it_once_and_operations_inside_one_record_nothing()
    {
        (ReportWorld world, _) = await WorkspaceAsync(limit: 10);

        // 1..6 are Normal; the 7th crosses 70%.
        for (int operation = 1; operation <= 6; operation++)
        {
            await ReserveAsync(world);
        }

        Assert.Empty(await Occurrences(world));

        await ReserveAsync(world);
        Assert.Equal([NotificationKind.UsageWarning], await Occurrences(world));

        // The 8th is still Warning: nothing new.
        await ReserveAsync(world);
        Assert.Equal([NotificationKind.UsageWarning], await Occurrences(world));

        // 9 crosses 90%, 10 reaches the limit.
        await ReserveAsync(world);
        await ReserveAsync(world);

        Assert.Equal(
            [NotificationKind.UsageWarning, NotificationKind.UsageCritical, NotificationKind.UsageExhausted],
            await Occurrences(world));

        // The 11th is refused by the quota and records nothing more.
        await Assert.ThrowsAsync<EntitlementExceededException>(() => ReserveAsync(world));

        Assert.Equal(3, (await Occurrences(world)).Count);
    }

    [Fact]
    public async Task Concurrent_operations_crossing_the_same_threshold_produce_one_alert_per_recipient()
    {
        (ReportWorld world, UserId analyst) = await WorkspaceAsync(limit: 100);
        await world.MemberAsync(PlatformPermissions.ConversationsView);

        await AddOperationsAsync(world, 60);

        // Twenty operations racing from 60 to 80: exactly one of them is the one
        // that takes the workspace from 69 to 70.
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => ReserveAsync(world)));

        Assert.Equal([NotificationKind.UsageWarning], await Occurrences(world));

        NotificationHarness harness = new(database);
        await harness.DispatchAllAsync();

        List<Notification> notifications = await harness.NotificationsAsync(world.Tenant);

        // The Owner and the analytics member are told; the member who may only
        // read conversations is not.
        Assert.Equal(2, notifications.Count);
        Assert.Contains(notifications, n => n.RecipientUserId == analyst);
        Assert.Contains(notifications, n => n.RecipientUserId == world.Owner);
        Assert.All(notifications, n =>
        {
            Assert.Equal(NotificationSeverity.Warning, n.Severity);
            Assert.Equal("/plan-usage", n.ActionPath);
        });

        // One ledger, unchanged: the operations are counted where they always were.
        await using OmniAiDbContext context = database.CreateContext();
        Assert.Equal(80, await context.AiProviderUsages.CountAsync(usage => usage.TenantId == world.Tenant));
    }

    [Fact]
    public async Task A_new_month_starts_the_thresholds_again()
    {
        (ReportWorld world, _) = await WorkspaceAsync(limit: 10);

        for (int operation = 1; operation <= 7; operation++)
        {
            await ReserveAsync(world);
        }

        DateTimeOffset october = new(2026, 10, 2, 9, 0, 0, TimeSpan.Zero);

        for (int operation = 1; operation <= 7; operation++)
        {
            await ReserveAsync(world, october);
        }

        await using OmniAiDbContext context = database.CreateContext();

        List<string> keys = await context.NotificationOccurrences
            .Where(occurrence => occurrence.TenantId == world.Tenant)
            .OrderBy(occurrence => occurrence.OccurrenceKey)
            .Select(occurrence => occurrence.OccurrenceKey)
            .ToListAsync();

        Assert.Equal(["2026-09", "2026-10"], keys);
    }

    [Fact]
    public async Task An_unlimited_plan_never_alerts()
    {
        (ReportWorld world, _) = await WorkspaceAsync(limit: null);

        for (int operation = 1; operation <= 5; operation++)
        {
            await ReserveAsync(world);
        }

        Assert.Empty(await Occurrences(world));
    }

    // ----------------------------------------------------------------- helpers

    private async Task<(ReportWorld World, UserId Analyst)> WorkspaceAsync(int? limit)
    {
        ReportWorld world = await ReportWorld.CreateAsync(database);
        UserId analyst = await world.MemberAsync(PlatformPermissions.AnalyticsView);

        Plan plan = Plan.Create(
            PlanCode.Create($"p{Guid.NewGuid():N}"[..20]),
            PlanDisplayName.Create("Threshold plan"),
            PlanLimit.From(null),
            PlanLimit.From(null),
            PlanLimit.From(null),
            PlanLimit.From(null),
            PlanLimit.From(null),
            PlanLimit.From(limit),
            DateTimeOffset.UtcNow);

        await using OmniAiDbContext context = database.CreateContext();

        context.Plans.Add(plan);
        await context.SaveChangesAsync();

        await context.WorkspacePlanAssignments.Where(assignment => assignment.TenantId == world.Tenant).ExecuteDeleteAsync();

        context.WorkspacePlanAssignments.Add(WorkspacePlanAssignment.Create(world.Tenant, plan.Id, DateTimeOffset.UtcNow));
        await context.SaveChangesAsync();

        return (world, analyst);
    }

    private async Task ReserveAsync(ReportWorld world, DateTimeOffset? at = null)
    {
        DateTimeOffset now = at ?? Noon;

        await using OmniAiDbContext context = database.CreateContext();

        await new WorkspaceEntitlementService(context, new FixedTimeProvider(now)).ReserveAsync(
            world.Tenant,
            EntitlementKind.MonthlyProviderOperations,
            async token =>
            {
                context.AiProviderUsages.Add(AiProviderUsage.Begin(
                    world.Tenant,
                    AiProvider.OpenAi,
                    AiOperationType.TextGeneration,
                    AiUsagePurpose.ConversationGeneration,
                    ModelName.Create("test-model-x"),
                    now));

                return await context.SaveChangesAsync(token);
            });
    }

    private async Task AddOperationsAsync(ReportWorld world, int count)
    {
        await using OmniAiDbContext context = database.CreateContext();

        for (int index = 0; index < count; index++)
        {
            context.AiProviderUsages.Add(AiProviderUsage.Begin(
                world.Tenant,
                AiProvider.OpenAi,
                AiOperationType.TextGeneration,
                AiUsagePurpose.ConversationGeneration,
                ModelName.Create("test-model-x"),
                Noon));
        }

        await context.SaveChangesAsync();
    }

    private async Task<List<NotificationKind>> Occurrences(ReportWorld world)
    {
        await using OmniAiDbContext context = database.CreateContext();

        List<NotificationKind> kinds = await context.NotificationOccurrences
            .Where(occurrence => occurrence.TenantId == world.Tenant)
            .Select(occurrence => occurrence.Kind)
            .ToListAsync();

        return [.. kinds.OrderBy(kind => (int)kind)];
    }
}
