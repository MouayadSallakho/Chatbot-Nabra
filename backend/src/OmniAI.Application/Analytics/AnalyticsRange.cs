using System.Globalization;

namespace OmniAI.Application.Analytics;

/// <summary>
/// How far back a dashboard looks.
/// </summary>
/// <remarks>
/// <strong>A closed set, not a number the caller chooses.</strong> Three presets
/// mean three query shapes whose cost is known in advance. An arbitrary
/// <c>?days=100000</c> would let anybody who can read analytics ask this
/// deployment to aggregate every row it has ever stored, which is a denial of
/// service wearing a query string.
/// </remarks>
public enum AnalyticsRange
{
    /// <summary>The last seven days.</summary>
    SevenDays = 7,

    /// <summary>The last thirty days. The default.</summary>
    ThirtyDays = 30,

    /// <summary>The last ninety days, and the longest this platform will read.</summary>
    NinetyDays = 90,
}

/// <summary>Reading and writing the range a caller asks for.</summary>
public static class AnalyticsRanges
{
    /// <summary>What a caller gets without asking.</summary>
    public const AnalyticsRange Default = AnalyticsRange.ThirtyDays;

    /// <summary>How many whole days a range covers.</summary>
    public static int Days(this AnalyticsRange range) => (int)range;

    /// <summary>The wire form: <c>7d</c>, <c>30d</c>, <c>90d</c>.</summary>
    public static string ToWire(this AnalyticsRange range) =>
        FormattableString.Invariant($"{(int)range}d");

    /// <summary>
    /// Reads a range from the query string.
    /// </summary>
    /// <remarks>
    /// Absent means the default. Anything present but unrecognised is refused
    /// rather than quietly rounded to the nearest preset: a caller who asked for
    /// 45 days and silently received 30 would draw conclusions from a period they
    /// did not choose.
    /// </remarks>
    public static bool TryParse(string? value, out AnalyticsRange range)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            range = Default;

            return true;
        }

        switch (value.Trim().ToLowerInvariant())
        {
            case "7d":
                range = AnalyticsRange.SevenDays;

                return true;

            case "30d":
                range = AnalyticsRange.ThirtyDays;

                return true;

            case "90d":
                range = AnalyticsRange.NinetyDays;

                return true;

            default:
                range = Default;

                return false;
        }
    }

    /// <summary>The recognised values, for an error message that helps.</summary>
    public static string Allowed =>
        string.Join(
            ", ",
            new[] { AnalyticsRange.SevenDays, AnalyticsRange.ThirtyDays, AnalyticsRange.NinetyDays }
                .Select(range => range.ToWire()));

    /// <summary>
    /// The window a range covers, ending at the given instant.
    /// </summary>
    /// <remarks>
    /// <strong>Whole UTC days, half-open.</strong> The window runs from midnight
    /// UTC <em>n-1</em> days before today up to the instant of the request, so the
    /// first and last buckets are real days rather than fragments of two. It is
    /// half-open — <c>[from, to)</c> — which is the only boundary rule that counts
    /// an event exactly once when two adjacent ranges are compared.
    /// <para>
    /// UTC, deliberately. This platform has no per-workspace timezone yet, and
    /// inventing one here would make a bucket boundary that no other part of the
    /// system agrees with. The screen says so rather than leaving it implied.
    /// </para>
    /// </remarks>
    public static AnalyticsWindow Window(this AnalyticsRange range, DateTimeOffset now)
    {
        DateTimeOffset utcNow = now.ToUniversalTime();

        DateTimeOffset today = new(utcNow.Date, TimeSpan.Zero);

        return new AnalyticsWindow(today.AddDays(-(range.Days() - 1)), utcNow, range);
    }
}

/// <summary>
/// The period a dashboard or a report describes.
/// </summary>
/// <param name="From">Inclusive start, midnight UTC.</param>
/// <param name="To">
/// Exclusive end: the instant the request was served for a dashboard, or a
/// midnight UTC for a report.
/// </param>
/// <param name="Range">The dashboard preset, or <see langword="null"/> for an explicit report period.</param>
/// <remarks>
/// <strong>One window type for both, deliberately.</strong> A report is the same
/// counts over an explicit, bounded set of whole days, and it reaches them
/// through the same reader. Two window types would be the first step towards two
/// definitions of "conversations started".
/// </remarks>
public sealed record AnalyticsWindow(DateTimeOffset From, DateTimeOffset To, AnalyticsRange? Range)
{
    /// <summary>
    /// An explicit period of whole UTC days, <c>[from, toExclusive)</c>.
    /// </summary>
    /// <remarks>Bounds are the caller's to enforce; the report period type does.</remarks>
    public static AnalyticsWindow ForDays(DateOnly from, DateOnly toExclusive)
    {
        if (toExclusive <= from)
        {
            throw new ArgumentOutOfRangeException(nameof(toExclusive), "A window must end after it starts.");
        }

        return new AnalyticsWindow(Midnight(from), Midnight(toExclusive), Range: null);
    }

    /// <summary>How many UTC days the window touches.</summary>
    public int DayCount
    {
        get
        {
            // A dashboard's window ends at the request instant, which may be
            // exactly midnight; its preset is what says how many days it shows.
            if (Range is AnalyticsRange preset)
            {
                return preset.Days();
            }

            DateOnly first = DateOnly.FromDateTime(From.UtcDateTime);
            DateOnly last = DateOnly.FromDateTime(To.UtcDateTime.AddTicks(-1));

            return last.DayNumber - first.DayNumber + 1;
        }
    }

    /// <summary>Every day in the window, in order, as UTC dates.</summary>
    /// <remarks>
    /// Produced here rather than inferred from the rows, so a day on which
    /// nothing happened is a zero rather than a gap. A chart with missing days is
    /// a chart that lies about its own shape.
    /// </remarks>
    public IReadOnlyList<DateOnly> Days()
    {
        DateOnly first = DateOnly.FromDateTime(From.UtcDateTime);

        return [.. Enumerable.Range(0, DayCount).Select(first.AddDays)];
    }

    /// <summary>The period as the API publishes it.</summary>
    public string Label => Range?.ToWire() ?? "custom";

    /// <summary>The zone every bucket boundary is taken in.</summary>
    public static string TimeZone => "UTC";

    /// <summary>For diagnostics; never shown to a reader.</summary>
    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{From:O}..{To:O} ({Label})");

    private static DateTimeOffset Midnight(DateOnly date) =>
        new(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
}

/// <summary>
/// How many rows a breakdown may name.
/// </summary>
/// <remarks>
/// A dashboard shows the ten busiest agents and models; a report lists all of
/// them up to its own export bound. The limit is the only thing that differs —
/// the query, and therefore the meaning of every number in the row, is shared.
/// </remarks>
public sealed record AnalyticsBreakdown(int MaxAgents, int MaxModels)
{
    /// <summary>What the dashboard shows.</summary>
    public static readonly AnalyticsBreakdown Dashboard = new(10, 10);
}
