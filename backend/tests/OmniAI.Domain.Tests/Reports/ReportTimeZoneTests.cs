using OmniAI.Domain.Reports;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Domain.Tests.Reports;

/// <summary>
/// Report periods and schedule boundaries in a workspace's zone (Phase 37.6, row 34).
/// </summary>
public sealed class ReportTimeZoneTests
{
    private static readonly TenantId Workspace = TenantId.From(Guid.NewGuid());
    private static readonly UserId Member = UserId.From(Guid.NewGuid());
    private static readonly WorkspaceTimeZone Riyadh = WorkspaceTimeZone.Create("Asia/Riyadh");
    private static readonly WorkspaceTimeZone Berlin = WorkspaceTimeZone.Create("Europe/Berlin");

    // ----------------------------------------------------------------- periods

    [Fact]
    public void A_period_without_a_zone_is_the_utc_period_it_always_was()
    {
        ReportPeriod period = ReportPeriod.Create(new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1));

        Assert.Equal(WorkspaceTimeZone.Utc, period.TimeZone);
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), period.From);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), period.To);
    }

    [Fact]
    public void A_gulf_period_runs_between_gulf_midnights()
    {
        ReportPeriod period = ReportPeriod.Create(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 4), Riyadh);

        Assert.Equal(new DateTimeOffset(2026, 10, 31, 21, 0, 0, TimeSpan.Zero), period.From);
        Assert.Equal(new DateTimeOffset(2026, 11, 3, 21, 0, 0, TimeSpan.Zero), period.To);
        Assert.Equal(3, period.Days);
    }

    [Fact]
    public void A_stored_period_comes_back_as_the_same_local_days()
    {
        ReportPeriod period = ReportPeriod.Create(new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 26), Berlin);

        ReportPeriod back = ReportPeriod.FromInstants(period.From, period.To, Berlin);

        Assert.Equal(period, back);
        Assert.Equal(TimeSpan.FromHours(7 * 24 + 1), period.To - period.From);
    }

    [Fact]
    public void The_same_dates_in_two_zones_are_two_periods()
    {
        DateOnly from = new(2026, 11, 1);
        DateOnly to = new(2026, 11, 8);

        Assert.NotEqual(ReportPeriod.Create(from, to), ReportPeriod.Create(from, to, Riyadh));
    }

    [Fact]
    public void The_last_days_end_at_the_start_of_the_local_tomorrow()
    {
        // 22:30 UTC on 3 November is already 4 November in Riyadh.
        DateTimeOffset now = new(2026, 11, 3, 22, 30, 0, TimeSpan.Zero);

        ReportPeriod utc = ReportPeriod.LastDays(7, now);
        ReportPeriod gulf = ReportPeriod.LastDays(7, now, Riyadh);

        Assert.Equal(new DateOnly(2026, 11, 3), utc.LastDate);
        Assert.Equal(new DateOnly(2026, 11, 4), gulf.LastDate);
        Assert.Equal(new DateTimeOffset(2026, 11, 4, 21, 0, 0, TimeSpan.Zero), gulf.To);
    }

    // ---------------------------------------------------------------- executions

    [Fact]
    public void An_execution_keeps_the_zone_its_period_was_computed_in()
    {
        ReportPeriod period = ReportPeriod.Create(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 4), Riyadh);

        ReportExecution execution = ReportExecution.Request(
            Workspace, ReportType.ChannelUsage, ReportFormat.Csv, period, Member, Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.Equal(Riyadh, execution.TimeZone);
        Assert.Equal(period, execution.Period);
        Assert.True(execution.SameRequestAs(ReportType.ChannelUsage, ReportFormat.Csv, period));
        Assert.False(execution.SameRequestAs(
            ReportType.ChannelUsage,
            ReportFormat.Csv,
            ReportPeriod.Create(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 4))));
    }

    // ------------------------------------------------------------- the calendar

    [Fact]
    public void A_gulf_week_is_due_at_the_start_of_monday_in_riyadh()
    {
        // Wednesday 4 November 2026, 10:00 UTC.
        DateTimeOffset now = new(2026, 11, 4, 10, 0, 0, TimeSpan.Zero);

        Assert.Equal(
            new DateTimeOffset(2026, 11, 8, 21, 0, 0, TimeSpan.Zero),
            ReportScheduleCalendar.NextBoundary(ReportFrequency.Weekly, now, Riyadh));

        Assert.Equal(
            new DateTimeOffset(2026, 11, 1, 21, 0, 0, TimeSpan.Zero),
            ReportScheduleCalendar.PreviousBoundary(ReportFrequency.Weekly, now, Riyadh));
    }

    [Fact]
    public void Late_on_sunday_utc_is_already_monday_in_the_gulf()
    {
        // Sunday 8 November 2026, 22:30 UTC: Monday 01:30 in Riyadh.
        DateTimeOffset now = new(2026, 11, 8, 22, 30, 0, TimeSpan.Zero);

        DateTimeOffset boundary = ReportScheduleCalendar.PreviousBoundary(ReportFrequency.Weekly, now, Riyadh);
        ReportPeriod period = ReportScheduleCalendar.PeriodEndingAt(ReportFrequency.Weekly, boundary, Riyadh);

        Assert.Equal(new DateTimeOffset(2026, 11, 8, 21, 0, 0, TimeSpan.Zero), boundary);
        Assert.Equal(new DateOnly(2026, 11, 2), period.FromDate);
        Assert.Equal(new DateOnly(2026, 11, 9), period.ToExclusiveDate);
        Assert.Equal(Riyadh, period.TimeZone);
    }

    [Fact]
    public void A_berlin_week_across_the_clock_change_is_169_hours_and_still_ends_at_monday()
    {
        // Tuesday 27 October 2026, after clocks went back on Sunday the 25th.
        DateTimeOffset now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

        DateTimeOffset boundary = ReportScheduleCalendar.PreviousBoundary(ReportFrequency.Weekly, now, Berlin);
        ReportPeriod period = ReportScheduleCalendar.PeriodEndingAt(ReportFrequency.Weekly, boundary, Berlin);

        Assert.Equal(new DateTimeOffset(2026, 10, 25, 23, 0, 0, TimeSpan.Zero), boundary);
        Assert.Equal(new DateTimeOffset(2026, 10, 18, 22, 0, 0, TimeSpan.Zero), period.From);
        Assert.Equal(TimeSpan.FromHours(169), period.To - period.From);
    }

    [Fact]
    public void A_berlin_month_is_due_at_local_midnight_on_the_first()
    {
        DateTimeOffset now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(
            new DateTimeOffset(2026, 10, 31, 23, 0, 0, TimeSpan.Zero),
            ReportScheduleCalendar.NextBoundary(ReportFrequency.Monthly, now, Berlin));
    }

    [Fact]
    public void Without_a_zone_the_calendar_is_the_utc_calendar_it_always_was()
    {
        DateTimeOffset now = new(2026, 11, 4, 10, 0, 0, TimeSpan.Zero);

        Assert.Equal(
            new DateTimeOffset(2026, 11, 9, 0, 0, 0, TimeSpan.Zero),
            ReportScheduleCalendar.NextBoundary(ReportFrequency.Weekly, now));

        Assert.Equal(
            new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero),
            ReportScheduleCalendar.NextBoundary(ReportFrequency.Monthly, now));
    }

    // -------------------------------------------------------------- schedules

    [Fact]
    public void A_schedule_created_in_the_gulf_is_due_at_the_gulf_monday()
    {
        DateTimeOffset now = new(2026, 11, 4, 10, 0, 0, TimeSpan.Zero);

        ReportSchedule schedule = ReportSchedule.Create(
            Workspace, "Weekly", ReportType.ChannelUsage, ReportFormat.Csv, ReportFrequency.Weekly, true, Member, now, Riyadh);

        Assert.Equal(new DateTimeOffset(2026, 11, 8, 21, 0, 0, TimeSpan.Zero), schedule.NextRunAt);
    }

    [Fact]
    public void Rezoning_moves_a_run_still_to_come_and_changes_nothing_a_person_set()
    {
        DateTimeOffset created = new(2026, 11, 4, 10, 0, 0, TimeSpan.Zero);
        ReportSchedule schedule = ReportSchedule.Create(
            Workspace, "Weekly", ReportType.ChannelUsage, ReportFormat.Csv, ReportFrequency.Weekly, true, Member, created);

        DateTimeOffset updated = schedule.UpdatedAt;
        DateTimeOffset now = created.AddHours(1);

        Assert.True(schedule.Rezone(Riyadh, now));

        Assert.Equal(new DateTimeOffset(2026, 11, 8, 21, 0, 0, TimeSpan.Zero), schedule.NextRunAt);
        Assert.Equal(Member, schedule.OwnerUserId);
        Assert.Equal(updated, schedule.UpdatedAt);

        // The same zone again moves nothing.
        Assert.False(schedule.Rezone(Riyadh, now));
    }

    [Fact]
    public void Rezoning_leaves_a_run_already_due_where_it_is()
    {
        DateTimeOffset created = new(2026, 11, 4, 10, 0, 0, TimeSpan.Zero);
        ReportSchedule schedule = ReportSchedule.Create(
            Workspace, "Weekly", ReportType.ChannelUsage, ReportFormat.Csv, ReportFrequency.Weekly, true, Member, created);

        DateTimeOffset due = schedule.NextRunAt;

        // The worker has not reached it yet: moving it would skip a period.
        Assert.False(schedule.Rezone(Riyadh, due.AddMinutes(5)));
        Assert.Equal(due, schedule.NextRunAt);
    }
}
