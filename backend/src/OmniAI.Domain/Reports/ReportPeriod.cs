using System.Globalization;
using OmniAI.Domain.Tenants;

namespace OmniAI.Domain.Reports;

/// <summary>
/// The period a report describes: whole local days of one time zone, half-open.
/// </summary>
/// <remarks>
/// <para>
/// <strong><c>[From, To)</c>, both at the start of a local day.</strong> The same
/// boundary rule the analytics dashboard uses, so a report and a dashboard over the
/// same days count the same events. The zone is the workspace's (Phase 37.6),
/// <c>UTC</c> unless somebody chose another, and every report says which on its
/// face. A local day is usually 24 hours long, and 23 or 25 on the days clocks
/// change.
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

    private ReportPeriod(DateOnly from, DateOnly toExclusive, WorkspaceTimeZone timeZone)
    {
        FromDate = from;
        ToExclusiveDate = toExclusive;
        TimeZone = timeZone;
    }

    /// <summary>The first day covered.</summary>
    public DateOnly FromDate { get; }

    /// <summary>The first day <em>not</em> covered.</summary>
    public DateOnly ToExclusiveDate { get; }

    /// <summary>The zone the days are counted in.</summary>
    public WorkspaceTimeZone TimeZone { get; }

    /// <summary>The last day covered, for display and file names.</summary>
    public DateOnly LastDate => ToExclusiveDate.AddDays(-1);

    public DateTimeOffset From => TimeZone.StartOf(FromDate);

    public DateTimeOffset To => TimeZone.StartOf(ToExclusiveDate);

    public int Days => ToExclusiveDate.DayNumber - FromDate.DayNumber;

    /// <summary>A validated period, in <paramref name="timeZone"/> (UTC when omitted).</summary>
    /// <exception cref="DomainValidationException">The period is empty, reversed, too long or too early.</exception>
    public static ReportPeriod Create(DateOnly from, DateOnly toExclusive, WorkspaceTimeZone? timeZone = null)
    {
        if (toExclusive <= from)
        {
            throw new DomainValidationException("A report period must end after it starts.")
            {
                Code = "reports.period.reversed",
            };
        }

        if (toExclusive.DayNumber - from.DayNumber > MaxDays)
        {
            throw new DomainValidationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"A report period may cover at most {MaxDays} days."))
            {
                Code = "reports.period.too_long",
            };
        }

        if (from < Earliest)
        {
            throw new DomainValidationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"A report period may not start before {Earliest:yyyy-MM-dd}."))
            {
                Code = "reports.period.too_early",
            };
        }

        return new ReportPeriod(from, toExclusive, timeZone ?? WorkspaceTimeZone.Utc);
    }

    /// <summary>Rehydrates a stored period from its two instants and the zone it was computed in.</summary>
    public static ReportPeriod FromInstants(DateTimeOffset from, DateTimeOffset to, WorkspaceTimeZone? timeZone = null)
    {
        WorkspaceTimeZone zone = timeZone ?? WorkspaceTimeZone.Utc;

        return Create(zone.DateOf(from), zone.DateOf(to), zone);
    }

    /// <summary>
    /// The last <paramref name="days"/> local days, including today.
    /// </summary>
    /// <remarks>
    /// The same days the dashboard's matching preset covers. The period ends at
    /// tomorrow's start rather than at the request instant, so a report is a set
    /// of whole days; nothing exists after "now" to be counted anyway.
    /// </remarks>
    public static ReportPeriod LastDays(int days, DateTimeOffset now, WorkspaceTimeZone? timeZone = null)
    {
        WorkspaceTimeZone zone = timeZone ?? WorkspaceTimeZone.Utc;
        DateOnly today = zone.DateOf(now);

        return Create(today.AddDays(-(days - 1)), today.AddDays(1), zone);
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{FromDate:yyyy-MM-dd}..{ToExclusiveDate:yyyy-MM-dd}) {TimeZone}");
}
