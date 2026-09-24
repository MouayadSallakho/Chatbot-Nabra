using Microsoft.EntityFrameworkCore;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Reports;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Reports;
using OmniAI.Domain.Users;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Infrastructure.Persistence.Repositories;
using OmniAI.Infrastructure.Reports;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Reports;

/// <summary>
/// Schedules against a real PostgreSQL: versions, due claiming, occurrences and authority.
/// </summary>
public sealed class ReportScheduleTests(PostgresTestDatabase database) : IClassFixture<PostgresTestDatabase>
{
    /// <summary>Long enough ago that every schedule created at it is due now.</summary>
    private static readonly DateTimeOffset LongAgo = new(2020, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly PermissionCode[] Scheduler =
    [
        PlatformPermissions.ReportsView,
        PlatformPermissions.ReportsGenerate,
        PlatformPermissions.ReportsSchedule,
        PlatformPermissions.AnalyticsView,
    ];

    [Fact]
    public async Task A_schedule_update_needs_the_version_it_was_read_at()
    {
        ReportWorld world = await ReportWorld.CreateAsync(database);
        ReportSchedule schedule = await AddAsync(world, DateTimeOffset.UtcNow);

        await using OmniAiDbContext context = database.CreateContext();
        ReportScheduleStore store = new(context);

        Versioned<ReportSchedule> read = (await store.FindAsync(world.Tenant, schedule.Id))!;

        read.Value.Update("Renamed", ReportType.ChannelUsage, ReportFormat.Pdf, ReportFrequency.Monthly, true, world.Owner, DateTimeOffset.UtcNow);
        ConcurrencyToken next = await store.UpdateAsync(read.Value, read.Version);

        Assert.NotEqual(read.Version, next);

        // The version it was read at is now stale.
        read.Value.Update("Again", ReportType.ChannelUsage, ReportFormat.Pdf, ReportFrequency.Monthly, true, world.Owner, DateTimeOffset.UtcNow);
        await Assert.ThrowsAsync<ReportScheduleVersionMismatchException>(() => store.UpdateAsync(read.Value, read.Version));
    }

    [Fact]
    public async Task Two_editors_of_one_version_cannot_both_win()
    {
        ReportWorld world = await ReportWorld.CreateAsync(database);
        ReportSchedule schedule = await AddAsync(world, DateTimeOffset.UtcNow);

        await using OmniAiDbContext first = database.CreateContext();
        await using OmniAiDbContext second = database.CreateContext();

        Versioned<ReportSchedule> a = (await new ReportScheduleStore(first).FindAsync(world.Tenant, schedule.Id))!;
        Versioned<ReportSchedule> b = (await new ReportScheduleStore(second).FindAsync(world.Tenant, schedule.Id))!;

        Assert.Equal(a.Version, b.Version);

        a.Value.SetEnabled(false, world.Owner, DateTimeOffset.UtcNow);
        b.Value.Update("Tab two", ReportType.ChannelUsage, ReportFormat.Csv, ReportFrequency.Weekly, true, world.Owner, DateTimeOffset.UtcNow);

        await new ReportScheduleStore(first).UpdateAsync(a.Value, a.Version);

        await Assert.ThrowsAsync<ReportScheduleVersionMismatchException>(
            () => new ReportScheduleStore(second).UpdateAsync(b.Value, b.Version));

        await using OmniAiDbContext check = database.CreateContext();
        ReportSchedule stored = await check.ReportSchedules.AsNoTracking().SingleAsync(candidate => candidate.Id == schedule.Id);

        Assert.False(stored.Enabled);
        Assert.NotEqual("Tab two", stored.Name);
    }

    [Fact]
    public async Task The_list_publishes_real_versions_and_the_last_occurrence()
    {
        ReportWorld world = await ReportWorld.CreateAsync(database);
        ReportSchedule schedule = await AddAsync(world, DateTimeOffset.UtcNow);

        await using OmniAiDbContext context = database.CreateContext();
        ReportScheduleStore store = new(context);

        ReportScheduleRecord record = Assert.Single(await store.ListAsync(world.Tenant));
        Versioned<ReportSchedule> found = (await store.FindAsync(world.Tenant, schedule.Id))!;

        Assert.Equal(found.Version, record.Schedule.Version);
        Assert.Null(record.LastExecution);
    }

    [Fact]
    public async Task Two_instances_claiming_due_schedules_at_once_get_disjoint_sets()
    {
        ReportWorld world = await ReportWorld.CreateAsync(database);

        List<ReportScheduleId> mine = [];

        for (int index = 0; index < 6; index++)
        {
            mine.Add((await AddAsync(world, LongAgo)).Id);
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;

        await using OmniAiDbContext left = database.CreateContext();
        await using OmniAiDbContext right = database.CreateContext();

        // Both held open at the same time, as two instances' transactions would be.
        await using IDueReportScheduleBatch a = await new ReportScheduleStore(left).ClaimDueAsync(now, 500);
        await using IDueReportScheduleBatch b = await new ReportScheduleStore(right).ClaimDueAsync(now, 500);

        HashSet<ReportScheduleId> fromA = [.. a.Schedules.Select(schedule => schedule.Id).Where(mine.Contains)];
        HashSet<ReportScheduleId> fromB = [.. b.Schedules.Select(schedule => schedule.Id).Where(mine.Contains)];

        Assert.Empty(fromA.Intersect(fromB));
        Assert.Equal(mine.Count, fromA.Count + fromB.Count);
    }

    [Fact]
    public async Task A_due_schedule_queues_one_occurrence_for_the_previous_completed_week()
    {
        ReportWorld world = await ReportWorld.CreateAsync(database);
        UserId owner = await world.MemberAsync(Scheduler);
        ReportSchedule schedule = await AddAsync(world, LongAgo, owner);

        DateTimeOffset thursday = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

        await RunAsync(thursday);

        await using OmniAiDbContext context = database.CreateContext();

        ReportExecution occurrence = await context.ReportExecutions.AsNoTracking()
            .SingleAsync(execution => execution.ScheduleId == schedule.Id);

        Assert.Equal(new DateOnly(2026, 9, 14), occurrence.Period.FromDate);
        Assert.Equal(new DateOnly(2026, 9, 21), occurrence.Period.ToExclusiveDate);
        Assert.Equal(owner, occurrence.RequestedBy);
        Assert.Equal(ReportExecutionStatus.Pending, occurrence.Status);

        ReportSchedule advanced = await context.ReportSchedules.AsNoTracking().SingleAsync(candidate => candidate.Id == schedule.Id);
        Assert.Equal(new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.Zero), advanced.NextRunAt);
    }

    [Fact]
    public async Task Repeating_an_occurrence_never_queues_it_twice()
    {
        ReportWorld world = await ReportWorld.CreateAsync(database);
        UserId owner = await world.MemberAsync(Scheduler);
        ReportSchedule schedule = await AddAsync(world, LongAgo, owner);

        DateTimeOffset thursday = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

        await RunAsync(thursday);

        // As if a crash had rolled next_run_at back, or a second instance had read
        // the schedule before the first committed.
        await using (OmniAiDbContext rewind = database.CreateContext())
        {
            await rewind.ReportSchedules
                .Where(candidate => candidate.Id == schedule.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.NextRunAt, LongAgo));
        }

        await RunAsync(thursday.AddHours(1));
        await RunAsync(thursday.AddHours(1));

        await using OmniAiDbContext context = database.CreateContext();

        Assert.Equal(1, await context.ReportExecutions.CountAsync(execution => execution.ScheduleId == schedule.Id));
    }

    [Fact]
    public async Task A_schedule_whose_owner_lost_authority_is_switched_off_and_generates_nothing()
    {
        ReportWorld world = await ReportWorld.CreateAsync(database);
        UserId owner = await world.MemberAsync(Scheduler);
        ReportSchedule schedule = await AddAsync(world, LongAgo, owner);

        await world.RevokeAsync(owner);

        await RunAsync(DateTimeOffset.UtcNow);

        await using OmniAiDbContext context = database.CreateContext();

        ReportSchedule stored = await context.ReportSchedules.AsNoTracking().SingleAsync(candidate => candidate.Id == schedule.Id);

        Assert.False(stored.Enabled);
        Assert.Equal(ReportSchedule.OwnerLostPermission, stored.DisabledReason);
        Assert.Equal(0, await context.ReportExecutions.CountAsync(execution => execution.ScheduleId == schedule.Id));
    }

    [Fact]
    public async Task An_owner_without_the_reports_data_permission_cannot_keep_a_customer_schedule_running()
    {
        ReportWorld world = await ReportWorld.CreateAsync(database);

        // Everything a schedule needs except customers.view.
        UserId owner = await world.MemberAsync(Scheduler);
        ReportSchedule schedule = await AddAsync(world, LongAgo, owner, ReportType.CustomerActivity);

        await RunAsync(DateTimeOffset.UtcNow);

        await using OmniAiDbContext context = database.CreateContext();

        ReportSchedule stored = await context.ReportSchedules.AsNoTracking().SingleAsync(candidate => candidate.Id == schedule.Id);

        Assert.False(stored.Enabled);
        Assert.Equal(0, await context.ReportExecutions.CountAsync(execution => execution.ScheduleId == schedule.Id));
    }

    [Fact]
    public async Task A_disabled_schedule_is_never_due()
    {
        ReportWorld world = await ReportWorld.CreateAsync(database);
        ReportSchedule schedule = await AddAsync(world, LongAgo, enabled: false);

        await RunAsync(DateTimeOffset.UtcNow);

        await using OmniAiDbContext context = database.CreateContext();

        Assert.Equal(0, await context.ReportExecutions.CountAsync(execution => execution.ScheduleId == schedule.Id));
    }

    // ---------------------------------------------------------------- helpers

    private async Task<ReportSchedule> AddAsync(
        ReportWorld world,
        DateTimeOffset createdAt,
        UserId? owner = null,
        ReportType type = ReportType.ChannelUsage,
        bool enabled = true)
    {
        ReportSchedule schedule = ReportSchedule.Create(
            world.Tenant,
            $"Schedule {Guid.NewGuid():N}"[..20],
            type,
            ReportFormat.Csv,
            ReportFrequency.Weekly,
            enabled,
            owner ?? world.Owner,
            createdAt);

        await using OmniAiDbContext context = database.CreateContext();

        await new ReportScheduleStore(context).AddAsync(schedule);

        return schedule;
    }

    /// <summary>One pass of the schedule worker, at a chosen instant.</summary>
    private async Task RunAsync(DateTimeOffset now)
    {
        await using OmniAiDbContext context = database.CreateContext();

        ReportScheduleRunner runner = new(
            new ReportScheduleStore(context),
            new ReportAuthority(new TenantMembershipRepository(context), new TenantRoleRepository(context)),
            new FixedTimeProvider(now));

        // Drain: other tests in this class leave due schedules behind too.
        while (await runner.RunDueAsync(50) == 50)
        {
        }
    }
}
