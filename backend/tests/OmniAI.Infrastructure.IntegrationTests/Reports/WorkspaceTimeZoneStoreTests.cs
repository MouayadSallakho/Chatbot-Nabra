using Microsoft.EntityFrameworkCore;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Reports;
using OmniAI.Application.Tenants;
using OmniAI.Domain.Reports;
using OmniAI.Domain.Tenants;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Infrastructure.Persistence.Repositories;
using OmniAI.Infrastructure.Reports;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Reports;

/// <summary>
/// The workspace time zone in PostgreSQL (Phase 37.6, row 34): the conditional
/// change that moves schedules with it, the database's own zone list, and a
/// scheduled report generated in the Gulf.
/// </summary>
public sealed class WorkspaceTimeZoneStoreTests(PostgresTestDatabase database) : IClassFixture<PostgresTestDatabase>
{
    private static readonly DateTimeOffset LongAgo = new(2020, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Wednesday = new(2026, 11, 4, 10, 0, 0, TimeSpan.Zero);
    private static readonly WorkspaceTimeZone Riyadh = WorkspaceTimeZone.Create("Asia/Riyadh");

    [Fact]
    public async Task A_new_workspace_and_every_existing_one_read_utc()
    {
        ReportWorld world = await ReportWorld.CreateAsync(database);

        await using OmniAiDbContext context = database.CreateContext();

        Assert.Equal(WorkspaceTimeZone.Utc, await new WorkspaceTimeZones(context).OfAsync(world.Tenant));
        Assert.Equal(WorkspaceTimeZone.Utc, await new WorkspaceTimeZones(context).OfAsync(TenantId.New()));
        Assert.Equal(
            "UTC",
            await context.Database
                .SqlQuery<string>($"SELECT time_zone AS \"Value\" FROM tenants WHERE id = {world.Tenant.Value}")
                .SingleAsync());
    }

    [Theory]
    [InlineData("UTC", true)]
    [InlineData("Asia/Riyadh", true)]
    [InlineData("Europe/Berlin", true)]
    [InlineData("asia/riyadh", false)]
    [InlineData("Mars/Olympus_Mons", false)]
    public async Task Postgresql_is_asked_for_the_exact_name(string name, bool known)
    {
        await using OmniAiDbContext context = database.CreateContext();

        Assert.Equal(known, await new WorkspaceTimeZones(context).DatabaseKnowsAsync(WorkspaceTimeZone.FromStored(name)));
    }

    [Fact]
    public async Task Changing_the_zone_moves_the_schedules_still_to_come_in_the_same_transaction()
    {
        ReportWorld world = await ReportWorld.CreateAsync(database);
        ReportSchedule upcoming = await AddAsync(world, Wednesday);
        ReportSchedule due = await AddAsync(world, LongAgo);
        ReportSchedule switchedOff = await AddAsync(world, Wednesday, enabled: false);

        DateTimeOffset now = Wednesday.AddHours(1);

        await using (OmniAiDbContext context = database.CreateContext())
        {
            TenantRepository tenants = new(context);
            Versioned<Tenant> stored = (await tenants.FindVersionedAsync(world.Tenant))!;

            Assert.True(stored.Value.ChangeTimeZone(Riyadh));

            ConcurrencyToken next = await tenants.ChangeTimeZoneAsync(stored.Value, stored.Version, now);

            Assert.NotEqual(stored.Version, next);
        }

        await using OmniAiDbContext check = database.CreateContext();

        Assert.Equal(Riyadh, await new WorkspaceTimeZones(check).OfAsync(world.Tenant));

        // Monday's start in Riyadh, three hours before Monday's start in UTC.
        Assert.Equal(new DateTimeOffset(2026, 11, 8, 21, 0, 0, TimeSpan.Zero), await NextRunAsync(check, upcoming));
        Assert.Equal(new DateTimeOffset(2026, 11, 8, 21, 0, 0, TimeSpan.Zero), await NextRunAsync(check, switchedOff));

        // Already due: left for the worker, not skipped.
        Assert.Equal(due.NextRunAt, await NextRunAsync(check, due));
    }

    [Fact]
    public async Task The_schedule_count_is_exactly_what_a_zone_change_moves()
    {
        // Phase 38.7b (BA-9): the count the workspace read gives a manager.
        ReportWorld world = await ReportWorld.CreateAsync(database);
        ReportWorld elsewhere = await ReportWorld.CreateAsync(database);
        ReportSchedule upcoming = await AddAsync(world, Wednesday);
        ReportSchedule due = await AddAsync(world, LongAgo);
        ReportSchedule switchedOff = await AddAsync(world, Wednesday, enabled: false);
        await AddAsync(elsewhere, Wednesday);

        DateTimeOffset now = Wednesday.AddHours(1);

        await using (OmniAiDbContext context = database.CreateContext())
        {
            // Upcoming and switched off, not the one already due, and nothing
            // of another workspace.
            Assert.Equal(2, await new TenantRepository(context).CountSchedulesToRezoneAsync(world.Tenant, now));
            Assert.Equal(0, await new TenantRepository(context).CountSchedulesToRezoneAsync(TenantId.New(), now));
        }

        await using (OmniAiDbContext context = database.CreateContext())
        {
            TenantRepository tenants = new(context);
            Versioned<Tenant> stored = (await tenants.FindVersionedAsync(world.Tenant))!;
            Assert.True(stored.Value.ChangeTimeZone(Riyadh));
            await tenants.ChangeTimeZoneAsync(stored.Value, stored.Version, now);
        }

        // The change moved exactly the two it counted.
        await using OmniAiDbContext check = database.CreateContext();

        Assert.NotEqual(upcoming.NextRunAt, await NextRunAsync(check, upcoming));
        Assert.NotEqual(switchedOff.NextRunAt, await NextRunAsync(check, switchedOff));
        Assert.Equal(due.NextRunAt, await NextRunAsync(check, due));
    }

    [Fact]
    public async Task A_stale_version_writes_neither_the_zone_nor_any_schedule()
    {
        ReportWorld world = await ReportWorld.CreateAsync(database);
        ReportSchedule upcoming = await AddAsync(world, Wednesday);

        await using (OmniAiDbContext context = database.CreateContext())
        {
            TenantRepository tenants = new(context);
            Versioned<Tenant> stored = (await tenants.FindVersionedAsync(world.Tenant))!;

            // Somebody else changes the workspace first.
            await using (OmniAiDbContext other = database.CreateContext())
            {
                TenantRepository theirs = new(other);
                Versioned<Tenant> fresh = (await theirs.FindVersionedAsync(world.Tenant))!;
                fresh.Value.Rename(TenantName.Create("Renamed first"));
                await theirs.RenameAsync(
                    fresh.Value,
                    fresh.Version,
                    Domain.Security.SecurityEvent.Record(
                        Domain.Security.SecurityEventKinds.WorkspaceRenamed,
                        DateTimeOffset.UtcNow,
                        userId: world.Owner,
                        tenantId: world.Tenant));
            }

            stored.Value.ChangeTimeZone(Riyadh);

            await Assert.ThrowsAsync<TenantVersionMismatchException>(
                () => tenants.ChangeTimeZoneAsync(stored.Value, stored.Version, Wednesday.AddHours(1)));
        }

        await using OmniAiDbContext check = database.CreateContext();

        Assert.Equal(WorkspaceTimeZone.Utc, await new WorkspaceTimeZones(check).OfAsync(world.Tenant));
        Assert.Equal(upcoming.NextRunAt, await NextRunAsync(check, upcoming));
    }

    [Fact]
    public async Task A_gulf_schedule_queues_the_gulf_week_and_its_report_keeps_the_zone()
    {
        ReportWorld world = await ReportWorld.CreateAsync(database);
        await SetZoneAsync(world, Riyadh);

        ReportSchedule schedule = await AddAsync(world, LongAgo);

        // Sunday 22:30 UTC: already Monday 01:30 in Riyadh, so the Gulf week of
        // 2 to 8 November has ended.
        DateTimeOffset now = new(2026, 11, 8, 22, 30, 0, TimeSpan.Zero);

        await using (OmniAiDbContext context = database.CreateContext())
        {
            ReportScheduleRunner runner = new(
                new ReportScheduleStore(context),
                new ReportAuthority(new TenantMembershipRepository(context), new TenantRoleRepository(context)),
                new FixedTimeProvider(now));

            while (await runner.RunDueAsync(50) == 50)
            {
            }
        }

        await using OmniAiDbContext check = database.CreateContext();

        ReportExecution occurrence = await check.ReportExecutions
            .AsNoTracking()
            .SingleAsync(execution => execution.ScheduleId == schedule.Id);

        Assert.Equal(Riyadh, occurrence.TimeZone);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 21, 0, 0, TimeSpan.Zero), occurrence.PeriodFrom);
        Assert.Equal(new DateTimeOffset(2026, 11, 8, 21, 0, 0, TimeSpan.Zero), occurrence.PeriodTo);
        Assert.Equal(new DateOnly(2026, 11, 2), occurrence.Period.FromDate);
        Assert.Equal(new DateOnly(2026, 11, 9), occurrence.Period.ToExclusiveDate);

        // The next one is due at the next Gulf Monday.
        Assert.Equal(new DateTimeOffset(2026, 11, 15, 21, 0, 0, TimeSpan.Zero), await NextRunAsync(check, schedule));

        // And the worker that claims it is handed the Gulf period, not a UTC one.
        IReadOnlyList<ClaimedReportExecution> claimed = await new ReportExecutionStore(check)
            .ClaimAsync(500, 3, TimeSpan.FromMinutes(5), DateTimeOffset.UtcNow);

        ClaimedReportExecution mine = Assert.Single(claimed, item => item.Id == occurrence.Id);
        Assert.Equal(occurrence.Period, mine.Period);
        Assert.Equal(Riyadh, mine.Period.TimeZone);
    }

    // ---------------------------------------------------------------- helpers

    private async Task<ReportSchedule> AddAsync(ReportWorld world, DateTimeOffset createdAt, bool enabled = true)
    {
        ReportSchedule schedule = ReportSchedule.Create(
            world.Tenant,
            $"Schedule {Guid.NewGuid():N}"[..20],
            ReportType.ChannelUsage,
            ReportFormat.Csv,
            ReportFrequency.Weekly,
            enabled,
            world.Owner,
            createdAt);

        await using OmniAiDbContext context = database.CreateContext();

        await new ReportScheduleStore(context).AddAsync(schedule);

        return schedule;
    }

    private async Task SetZoneAsync(ReportWorld world, WorkspaceTimeZone zone)
    {
        await using OmniAiDbContext context = database.CreateContext();

        TenantRepository tenants = new(context);
        Versioned<Tenant> stored = (await tenants.FindVersionedAsync(world.Tenant))!;
        stored.Value.ChangeTimeZone(zone);
        await tenants.ChangeTimeZoneAsync(stored.Value, stored.Version, DateTimeOffset.UtcNow);
    }

    private static Task<DateTimeOffset> NextRunAsync(OmniAiDbContext context, ReportSchedule schedule) =>
        context.ReportSchedules
            .AsNoTracking()
            .Where(candidate => candidate.Id == schedule.Id)
            .Select(candidate => candidate.NextRunAt)
            .SingleAsync();
}
