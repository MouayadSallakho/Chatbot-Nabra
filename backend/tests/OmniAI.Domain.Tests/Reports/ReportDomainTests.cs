using System.Globalization;
using OmniAI.Domain.Reports;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Domain.Tests.Reports;

/// <summary>
/// The rules a report request, period and schedule obey regardless of storage.
/// </summary>
public sealed class ReportDomainTests
{
    private static readonly TenantId Workspace = TenantId.From(Guid.NewGuid());
    private static readonly UserId Member = UserId.From(Guid.NewGuid());

    // ----------------------------------------------------------------- periods

    [Fact]
    public void A_period_is_half_open_whole_utc_days()
    {
        ReportPeriod period = ReportPeriod.Create(new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1));

        Assert.Equal(30, period.Days);
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), period.From);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), period.To);
        Assert.Equal(new DateOnly(2026, 9, 30), period.LastDate);
    }

    [Theory]
    [InlineData("2026-09-10", "2026-09-10")]
    [InlineData("2026-09-10", "2026-09-09")]
    public void An_empty_or_reversed_period_is_refused(string from, string to) =>
        Assert.Throws<DomainValidationException>(() => ReportPeriod.Create(
            DateOnly.Parse(from, CultureInfo.InvariantCulture), DateOnly.Parse(to, CultureInfo.InvariantCulture)));

    [Fact]
    public void A_period_may_cover_at_most_366_days()
    {
        DateOnly start = new(2025, 1, 1);

        Assert.Equal(366, ReportPeriod.Create(start, start.AddDays(366)).Days);
        Assert.Throws<DomainValidationException>(() => ReportPeriod.Create(start, start.AddDays(367)));
    }

    [Fact]
    public void There_is_no_all_time_period() =>
        Assert.Throws<DomainValidationException>(
            () => ReportPeriod.Create(new DateOnly(1999, 12, 31), new DateOnly(2000, 6, 1)));

    [Fact]
    public void A_preset_covers_the_same_days_as_the_dashboard_preset()
    {
        DateTimeOffset noon = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

        ReportPeriod seven = ReportPeriod.LastDays(7, noon);

        Assert.Equal(new DateOnly(2026, 9, 18), seven.FromDate);
        Assert.Equal(new DateOnly(2026, 9, 25), seven.ToExclusiveDate);
        Assert.Equal(7, seven.Days);
    }

    // --------------------------------------------------------------- calendar

    [Theory]
    [InlineData("2026-09-24T12:00:00Z", "2026-09-21T00:00:00Z")] // Thursday
    [InlineData("2026-09-21T00:00:00Z", "2026-09-21T00:00:00Z")] // Monday midnight exactly
    [InlineData("2026-09-27T23:59:59Z", "2026-09-21T00:00:00Z")] // Sunday
    public void The_weekly_boundary_is_the_latest_monday_midnight(string now, string expected) =>
        Assert.Equal(
            DateTimeOffset.Parse(expected, System.Globalization.CultureInfo.InvariantCulture),
            ReportScheduleCalendar.PreviousBoundary(
                ReportFrequency.Weekly,
                DateTimeOffset.Parse(now, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void A_weekly_occurrence_covers_the_seven_completed_days_before_its_boundary()
    {
        ReportPeriod period = ReportScheduleCalendar.PeriodEndingAt(
            ReportFrequency.Weekly,
            new DateTimeOffset(2026, 9, 21, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateOnly(2026, 9, 14), period.FromDate);
        Assert.Equal(new DateOnly(2026, 9, 21), period.ToExclusiveDate);
    }

    [Fact]
    public void A_monthly_occurrence_covers_the_previous_calendar_month()
    {
        DateTimeOffset now = new(2026, 3, 1, 0, 0, 30, TimeSpan.Zero);

        DateTimeOffset boundary = ReportScheduleCalendar.PreviousBoundary(ReportFrequency.Monthly, now);
        ReportPeriod period = ReportScheduleCalendar.PeriodEndingAt(ReportFrequency.Monthly, boundary);

        Assert.Equal(new DateOnly(2026, 2, 1), period.FromDate);
        Assert.Equal(new DateOnly(2026, 3, 1), period.ToExclusiveDate);
        Assert.Equal(28, period.Days);
        Assert.Equal(new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero), ReportScheduleCalendar.NextBoundary(ReportFrequency.Monthly, now));
    }

    [Fact]
    public void An_occurrence_period_never_includes_the_future()
    {
        DateTimeOffset now = new(2026, 9, 24, 8, 0, 0, TimeSpan.Zero);

        foreach (ReportFrequency frequency in Enum.GetValues<ReportFrequency>())
        {
            ReportPeriod period = ReportScheduleCalendar.PeriodEndingAt(
                frequency,
                ReportScheduleCalendar.PreviousBoundary(frequency, now));

            Assert.True(period.To <= now);
        }
    }

    // --------------------------------------------------------------- requests

    [Fact]
    public void A_request_starts_pending_and_needs_an_idempotency_key()
    {
        ReportPeriod period = ReportPeriod.Create(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 8));

        ReportExecution execution = ReportExecution.Request(
            Workspace, ReportType.ChannelUsage, ReportFormat.Csv, period, Member, Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.Equal(ReportExecutionStatus.Pending, execution.Status);
        Assert.Equal(0, execution.Attempts);
        Assert.True(execution.SameRequestAs(ReportType.ChannelUsage, ReportFormat.Csv, period));
        Assert.False(execution.SameRequestAs(ReportType.ChannelUsage, ReportFormat.Pdf, period));

        Assert.Throws<DomainValidationException>(() => ReportExecution.Request(
            Workspace, ReportType.ChannelUsage, ReportFormat.Csv, period, Member, Guid.Empty, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void An_unknown_type_or_format_is_refused()
    {
        ReportPeriod period = ReportPeriod.Create(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 8));

        Assert.Throws<DomainValidationException>(() => ReportExecution.Request(
            Workspace, (ReportType)99, ReportFormat.Csv, period, Member, Guid.NewGuid(), DateTimeOffset.UtcNow));
        Assert.Throws<DomainValidationException>(() => ReportExecution.Request(
            Workspace, ReportType.ChannelUsage, (ReportFormat)99, period, Member, Guid.NewGuid(), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Every_failure_code_has_a_sentence_and_an_unknown_one_gets_the_generic_one()
    {
        foreach (string code in new[] { ReportFailures.TooLarge, ReportFailures.PermissionRevoked, ReportFailures.StorageUnavailable })
        {
            Assert.NotEqual(ReportFailures.Describe(null), ReportFailures.Describe(code));
        }

        Assert.Contains("exceeds the supported export size", ReportFailures.Describe(ReportFailures.TooLarge), StringComparison.Ordinal);
        Assert.Equal(ReportFailures.Describe(ReportFailures.GenerationFailed), ReportFailures.Describe("C:\\secret\\path"));
    }

    // -------------------------------------------------------------- schedules

    [Fact]
    public void A_new_schedule_is_due_at_the_next_boundary_and_owned_by_its_creator()
    {
        DateTimeOffset now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

        ReportSchedule schedule = ReportSchedule.Create(
            Workspace, "  Weekly channels  ", ReportType.ChannelUsage, ReportFormat.Xlsx, ReportFrequency.Weekly, true, Member, now);

        Assert.Equal("Weekly channels", schedule.Name);
        Assert.Equal(new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.Zero), schedule.NextRunAt);
        Assert.Equal(Member, schedule.OwnerUserId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad\u0007name")]
    public void A_schedule_needs_a_clean_name(string name) =>
        Assert.Throws<DomainValidationException>(() => ReportSchedule.Create(
            Workspace, name, ReportType.ChannelUsage, ReportFormat.Csv, ReportFrequency.Weekly, true, Member, DateTimeOffset.UtcNow));

    [Fact]
    public void A_schedule_name_is_bounded() =>
        Assert.Throws<DomainValidationException>(() => ReportSchedule.Create(
            Workspace, new string('x', ReportSchedule.MaxNameLength + 1), ReportType.ChannelUsage, ReportFormat.Csv,
            ReportFrequency.Weekly, true, Member, DateTimeOffset.UtcNow));

    [Fact]
    public void Editing_transfers_ownership_and_changing_frequency_reschedules()
    {
        DateTimeOffset now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);
        UserId editor = UserId.From(Guid.NewGuid());

        ReportSchedule schedule = ReportSchedule.Create(
            Workspace, "Channels", ReportType.ChannelUsage, ReportFormat.Csv, ReportFrequency.Weekly, true, Member, now);

        schedule.Update("Channels monthly", ReportType.ChannelUsage, ReportFormat.Pdf, ReportFrequency.Monthly, true, editor, now);

        Assert.Equal(editor, schedule.OwnerUserId);
        Assert.Equal(Member, schedule.CreatedBy);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), schedule.NextRunAt);
    }

    [Fact]
    public void Re_enabling_clears_the_platform_reason_and_moves_the_next_run_forward()
    {
        DateTimeOffset created = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset later = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

        ReportSchedule schedule = ReportSchedule.Create(
            Workspace, "Channels", ReportType.ChannelUsage, ReportFormat.Csv, ReportFrequency.Weekly, false, Member, created);

        schedule.SetEnabled(true, Member, later);

        Assert.True(schedule.Enabled);
        Assert.Null(schedule.DisabledReason);
        Assert.Equal(new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.Zero), schedule.NextRunAt);
    }

    [Fact]
    public void An_artifact_is_never_empty_and_always_expires()
    {
        ReportExecutionId execution = ReportExecutionId.New();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        ReportArtifact artifact = ReportArtifact.Create(
            Workspace, execution, ReportFormat.Csv, "text/csv", 10, new string('a', 64), new string('b', 32), now, TimeSpan.FromDays(30));

        Assert.Equal(now.AddDays(30), artifact.ExpiresAt);

        Assert.Throws<DomainValidationException>(() => ReportArtifact.Create(
            Workspace, execution, ReportFormat.Csv, "text/csv", 0, new string('a', 64), new string('b', 32), now, TimeSpan.FromDays(30)));
    }
}
