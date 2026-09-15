using OmniAI.Application.Analytics;

namespace OmniAI.Application.Tests.Analytics;

/// <summary>
/// The range and the window it opens.
/// </summary>
/// <remarks>
/// Small, and worth having. A window is where an off-by-one becomes a number on
/// a screen that nobody can reproduce by hand, and the boundary rule — half-open,
/// whole UTC days — is the thing that makes two adjacent periods add up.
/// </remarks>
public sealed class AnalyticsRangeTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Nothing_asked_for_means_thirty_days()
    {
        Assert.True(AnalyticsRanges.TryParse(null, out AnalyticsRange range));
        Assert.Equal(AnalyticsRange.ThirtyDays, range);

        Assert.True(AnalyticsRanges.TryParse("   ", out range));
        Assert.Equal(AnalyticsRange.ThirtyDays, range);
    }

    [Theory]
    [InlineData("7d", AnalyticsRange.SevenDays)]
    [InlineData("30d", AnalyticsRange.ThirtyDays)]
    [InlineData("90d", AnalyticsRange.NinetyDays)]
    [InlineData("90D", AnalyticsRange.NinetyDays)]
    public void The_three_presets_are_read(string wire, AnalyticsRange expected)
    {
        Assert.True(AnalyticsRanges.TryParse(wire, out AnalyticsRange range));
        Assert.Equal(expected, range);
    }

    [Theory]
    [InlineData("45d")]
    [InlineData("1d")]
    [InlineData("365d")]
    [InlineData("all")]
    [InlineData("30")]
    [InlineData("-7d")]
    public void Anything_else_is_refused_rather_than_rounded(string wire)
    {
        // Silently giving somebody thirty days when they asked for forty-five
        // would have them draw conclusions about a period they did not choose.
        Assert.False(AnalyticsRanges.TryParse(wire, out _));
    }

    [Fact]
    public void A_window_starts_at_midnight_utc_and_ends_now()
    {
        AnalyticsWindow window = AnalyticsRange.SevenDays.Window(Noon);

        Assert.Equal(new DateTimeOffset(2026, 9, 9, 0, 0, 0, TimeSpan.Zero), window.From);
        Assert.Equal(Noon, window.To);
        Assert.Equal(7, window.Days().Count);
    }

    [Fact]
    public void A_window_is_taken_in_utc_whatever_offset_the_caller_holds()
    {
        // The same instant, written two ways. A dashboard read from Auckland and
        // one read from Los Angeles describe the same period.
        DateTimeOffset elsewhere = Noon.ToOffset(TimeSpan.FromHours(9));

        Assert.Equal(
            AnalyticsRange.ThirtyDays.Window(Noon).From,
            AnalyticsRange.ThirtyDays.Window(elsewhere).From);

        Assert.Equal("UTC", AnalyticsWindow.TimeZone);
    }

    [Fact]
    public void The_days_of_a_window_are_consecutive_and_end_today()
    {
        IReadOnlyList<DateOnly> days = AnalyticsRange.SevenDays.Window(Noon).Days();

        Assert.Equal(new DateOnly(2026, 9, 9), days[0]);
        Assert.Equal(new DateOnly(2026, 9, 15), days[^1]);

        // Produced from the window rather than from the rows, so a day nothing
        // happened on is a zero instead of a hole in the chart.
        Assert.Equal(7, days.Distinct().Count());
    }

    [Theory]
    [InlineData(AnalyticsRange.SevenDays, 7)]
    [InlineData(AnalyticsRange.ThirtyDays, 30)]
    [InlineData(AnalyticsRange.NinetyDays, 90)]
    public void A_range_covers_exactly_the_days_its_name_claims(AnalyticsRange range, int days)
    {
        Assert.Equal(days, range.Days());
        Assert.Equal(days, range.Window(Noon).Days().Count);
        Assert.Equal($"{days}d", range.ToWire());
    }

    [Fact]
    public void Ninety_days_is_as_far_as_this_platform_will_look()
    {
        // The bound is the point: an arbitrary ?days= would let anybody who can
        // read analytics ask the deployment to aggregate everything it has.
        Assert.Equal(
            90,
            Enum.GetValues<AnalyticsRange>().Max(range => range.Days()));
    }
}
