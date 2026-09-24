using System.Globalization;

namespace OmniAI.Domain.Reports;

/// <summary>
/// The period a report describes: whole UTC days, half-open.
/// </summary>
/// <remarks>
/// <para>
/// <strong><c>[From, To)</c>, both at midnight UTC.</strong> The same boundary
/// rule the analytics dashboard uses, so a report and a dashboard over the same
/// days count the same events. UTC because this platform has no per-workspace
/// timezone yet; every report says so on its face.
/// </para>
/// <para>
/// <strong>Bounded.</strong> At most <see cref="MaxDays"/> days, never empty and
/// never earlier than <see cref="Earliest"/>. There is no "all time": an export
/// whose cost grows with a workspace's age is a denial of service somebody can
/// schedule.
/// </para>
/// </remarks>
public sealed record ReportPeriod
{
    /// <summary>The longest period a report may cover. One leap year.</summary>
    public const int MaxDays = 366;

    /// <summary>No workspace has data before this platform existed.</summary>
    public static readonly DateOnly Earliest = new(2020, 1, 1);

    private ReportPeriod(DateOnly from, DateOnly toExclusive)
    {
        FromDate = from;
        ToExclusiveDate = toExclusive;
    }

    /// <summary>The first day covered.</summary>
    public DateOnly FromDate { get; }

    /// <summary>The first day <em>not</em> covered.</summary>
    public DateOnly ToExclusiveDate { get; }

    /// <summary>The last day covered, for display and file names.</summary>
    public DateOnly LastDate => ToExclusiveDate.AddDays(-1);

    public DateTimeOffset From => At(FromDate);

    public DateTimeOffset To => At(ToExclusiveDate);

    public int Days => ToExclusiveDate.DayNumber - FromDate.DayNumber;

    /// <summary>A validated period.</summary>
    /// <exception cref="DomainValidationException">The period is empty, reversed, too long or too early.</exception>
    public static ReportPeriod Create(DateOnly from, DateOnly toExclusive)
    {
        if (toExclusive <= from)
        {
            throw new DomainValidationException("A report period must end after it starts.");
        }

        if (toExclusive.DayNumber - from.DayNumber > MaxDays)
        {
            throw new DomainValidationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"A report period may cover at most {MaxDays} days."));
        }

        if (from < Earliest)
        {
            throw new DomainValidationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"A report period may not start before {Earliest:yyyy-MM-dd}."));
        }

        return new ReportPeriod(from, toExclusive);
    }

    /// <summary>Rehydrates a stored period from its two instants.</summary>
    public static ReportPeriod FromInstants(DateTimeOffset from, DateTimeOffset to) =>
        Create(DateOnly.FromDateTime(from.UtcDateTime), DateOnly.FromDateTime(to.UtcDateTime));

    /// <summary>
    /// The last <paramref name="days"/> UTC days, including today.
    /// </summary>
    /// <remarks>
    /// The same days the dashboard's matching preset covers. The period ends at
    /// tomorrow's midnight rather than at the request instant, so a report is a
    /// set of whole days; nothing exists after "now" to be counted anyway.
    /// </remarks>
    public static ReportPeriod LastDays(int days, DateTimeOffset now)
    {
        DateOnly today = DateOnly.FromDateTime(now.UtcDateTime);

        return Create(today.AddDays(-(days - 1)), today.AddDays(1));
    }

    private static DateTimeOffset At(DateOnly date) =>
        new(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{FromDate:yyyy-MM-dd}..{ToExclusiveDate:yyyy-MM-dd})");
}
