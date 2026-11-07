using OmniAI.Application.Analytics;
using OmniAI.Application.Tests.Security;
using OmniAI.Application.Tests.Tenants;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;
using OmniAI.Testing;

namespace OmniAI.Application.Tests.Analytics;

/// <summary>
/// Dashboard windows in the workspace's zone, and the previous period beside them
/// (Phase 37.6, rows 31 and 34).
/// </summary>
public sealed class AnalyticsTimeZoneTests
{
    private static readonly Guid Workspace = Guid.CreateVersion7();
    private static readonly WorkspaceTimeZone Riyadh = WorkspaceTimeZone.Create("Asia/Riyadh");
    private static readonly WorkspaceTimeZone Berlin = WorkspaceTimeZone.Create("Europe/Berlin");

    // ------------------------------------------------------------------ windows

    [Fact]
    public void A_utc_window_is_exactly_what_it_was_before_zones_existed()
    {
        DateTimeOffset now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

        AnalyticsWindow window = AnalyticsRange.SevenDays.Window(now);

        Assert.Equal(new DateTimeOffset(2026, 9, 9, 0, 0, 0, TimeSpan.Zero), window.From);
        Assert.Equal(now, window.To);
        Assert.Equal(WorkspaceTimeZone.Utc, window.TimeZone);
        Assert.Equal(new DateOnly(2026, 9, 9), window.Days()[0]);
        Assert.Equal(new DateOnly(2026, 9, 15), window.Days()[^1]);
    }

    [Fact]
    public void A_gulf_window_starts_at_gulf_midnight_and_its_days_are_gulf_days()
    {
        // 22:30 UTC on 3 November is 01:30 on 4 November in Riyadh: today there is
        // the 4th, so a seven-day window covers 29 October to 4 November.
        DateTimeOffset now = new(2026, 11, 3, 22, 30, 0, TimeSpan.Zero);

        AnalyticsWindow window = AnalyticsRange.SevenDays.Window(now, Riyadh);

        Assert.Equal(new DateTimeOffset(2026, 10, 28, 21, 0, 0, TimeSpan.Zero), window.From);
        Assert.Equal(7, window.DayCount);
        Assert.Equal(new DateOnly(2026, 10, 29), window.Days()[0]);
        Assert.Equal(new DateOnly(2026, 11, 4), window.Days()[^1]);
    }

    [Fact]
    public void A_report_window_in_berlin_spans_the_long_day_whole()
    {
        AnalyticsWindow window = AnalyticsWindow.ForDays(new DateOnly(2026, 10, 24), new DateOnly(2026, 10, 27), Berlin);

        Assert.Equal(3, window.DayCount);
        Assert.Equal([new DateOnly(2026, 10, 24), new DateOnly(2026, 10, 25), new DateOnly(2026, 10, 26)], window.Days());
        Assert.Equal(TimeSpan.FromHours(73), window.To - window.From);
    }

    // ----------------------------------------------------------------- previous

    [Theory]
    [InlineData(AnalyticsRange.SevenDays)]
    [InlineData(AnalyticsRange.ThirtyDays)]
    [InlineData(AnalyticsRange.NinetyDays)]
    public void The_previous_window_is_adjacent_as_long_and_whole_days(AnalyticsRange range)
    {
        DateTimeOffset now = new(2026, 11, 3, 22, 30, 0, TimeSpan.Zero);

        AnalyticsWindow current = range.Window(now, Riyadh);
        AnalyticsWindow previous = current.Previous();

        // Adjacent and half-open: one ends where the other starts, so an event on
        // the boundary belongs to exactly one of them.
        Assert.Equal(current.From, previous.To);
        Assert.Equal(range.Days(), previous.DayCount);
        Assert.Equal(current.Days()[0].AddDays(-1), previous.Days()[^1]);
        Assert.Equal(Riyadh, previous.TimeZone);
        Assert.Equal(range, previous.Range);

        // Whole days: it starts at a local midnight, as many days earlier.
        Assert.Equal(Riyadh.StartOf(current.Days()[0].AddDays(-range.Days())), previous.From);
    }

    [Fact]
    public void The_previous_week_across_the_berlin_clock_change_is_seven_days_of_169_hours()
    {
        // Monday 2 November 2026: the previous week of a seven-day window ending
        // today is 20 to 26 October, which holds the 25-hour Sunday.
        DateTimeOffset now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

        AnalyticsWindow previous = AnalyticsRange.SevenDays.Window(now, Berlin).Previous();

        Assert.Equal(new DateOnly(2026, 10, 20), previous.Days()[0]);
        Assert.Equal(new DateOnly(2026, 10, 26), previous.Days()[^1]);
        Assert.Equal(TimeSpan.FromHours(169), previous.To - previous.From);
    }

    // ------------------------------------------------------------------ handler

    [Fact]
    public async Task The_dashboard_is_read_in_the_workspace_zone()
    {
        RecordingAnalyticsReader reader = new();
        DateTimeOffset noon = new(2026, 11, 3, 22, 30, 0, TimeSpan.Zero);

        GetWorkspaceAnalyticsHandler handler = new(
            reader,
            new FixedWorkspaceTimeZones(Riyadh),
            TestAuthority.Owner(UserId.New(), Workspace),
            new FixedTimeProvider(noon));

        WorkspaceAnalytics? analytics = await handler.HandleAsync(
            new GetWorkspaceAnalyticsQuery(Workspace, AnalyticsRange.SevenDays));

        Assert.NotNull(analytics);
        Assert.Equal(Riyadh, reader.LastWindow!.TimeZone);
        Assert.Equal(AnalyticsRange.SevenDays.Window(noon, Riyadh), reader.LastWindow);
        Assert.Equal("Asia/Riyadh", analytics.Period.TimeZone);

        // No comparison unless asked for.
        Assert.Null(reader.LastComparisonWindow);
        Assert.Null(analytics.Previous);
    }

    [Fact]
    public async Task Compare_previous_reads_the_adjacent_window_and_nothing_else()
    {
        RecordingAnalyticsReader reader = new();
        DateTimeOffset noon = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

        GetWorkspaceAnalyticsHandler handler = new(
            reader,
            new FixedWorkspaceTimeZones(Riyadh),
            TestAuthority.Owner(UserId.New(), Workspace),
            new FixedTimeProvider(noon));

        WorkspaceAnalytics? analytics = await handler.HandleAsync(
            new GetWorkspaceAnalyticsQuery(Workspace, AnalyticsRange.ThirtyDays, ComparePrevious: true));

        Assert.Equal(reader.LastWindow!.Previous(), reader.LastComparisonWindow);
        Assert.Equal(1, reader.Reads);
        Assert.NotNull(analytics!.Previous);
        Assert.Equal(analytics.Period.From, analytics.Previous.Period.To);
    }

    [Fact]
    public async Task A_non_member_learns_nothing_and_no_zone_is_read()
    {
        RecordingAnalyticsReader reader = new();

        GetWorkspaceAnalyticsHandler handler = new(
            reader,
            new FixedWorkspaceTimeZones(Riyadh),
            TestAuthority.Outsider(UserId.New()),
            new FixedTimeProvider(DateTimeOffset.UnixEpoch));

        Assert.Null(await handler.HandleAsync(
            new GetWorkspaceAnalyticsQuery(Workspace, AnalyticsRange.SevenDays, ComparePrevious: true)));

        Assert.Equal(0, reader.Reads);
        Assert.Null(reader.LastComparisonWindow);
    }
}
