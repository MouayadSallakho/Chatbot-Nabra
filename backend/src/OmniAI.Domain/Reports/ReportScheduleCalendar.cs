using OmniAI.Domain.Tenants;

namespace OmniAI.Domain.Reports;

/// <summary>
/// When scheduled reports are due, and which completed period each one covers.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Boundaries, not intervals.</strong> A weekly schedule is due at the
/// start of every Monday and covers the seven days before it; a monthly one is due
/// at the start of the first of every month and covers the calendar month before
/// it. Both are local days of the workspace's zone (Phase 37.6, <c>UTC</c> unless
/// chosen). A period is always derived from a boundary, never from the instant a
/// worker happened to run, so a late or repeated run produces the same period —
/// and the same occurrence identity — as a punctual one.
/// </para>
/// <para>
/// <strong>Counted in dates, then turned into instants.</strong> The next boundary
/// is seven local days or one local month on, not 168 hours: a week that contains
/// a clock change is 167 or 169 hours long, and its boundary is still Monday's
/// start.
/// </para>
/// <para>
/// <strong>Only completed periods.</strong> The period an occurrence covers ends
/// at the boundary, which is in the past by the time it is due. Nothing here can
/// produce a period that includes the future.
/// </para>
/// </remarks>
public static class ReportScheduleCalendar
{
    /// <summary>The latest boundary at or before <paramref name="now"/>.</summary>
    public static DateTimeOffset PreviousBoundary(
        ReportFrequency frequency,
        DateTimeOffset now,
        WorkspaceTimeZone? timeZone = null)
    {
        WorkspaceTimeZone zone = timeZone ?? WorkspaceTimeZone.Utc;

        return zone.StartOf(PreviousBoundaryDate(frequency, zone.DateOf(now)));
    }

    /// <summary>The first boundary strictly after <paramref name="now"/>.</summary>
    public static DateTimeOffset NextBoundary(
        ReportFrequency frequency,
        DateTimeOffset now,
        WorkspaceTimeZone? timeZone = null)
    {
        WorkspaceTimeZone zone = timeZone ?? WorkspaceTimeZone.Utc;
        DateOnly previous = PreviousBoundaryDate(frequency, zone.DateOf(now));

        return zone.StartOf(frequency == ReportFrequency.Weekly ? previous.AddDays(7) : previous.AddMonths(1));
    }

    /// <summary>The completed period an occurrence at <paramref name="boundary"/> covers.</summary>
    public static ReportPeriod PeriodEndingAt(
        ReportFrequency frequency,
        DateTimeOffset boundary,
        WorkspaceTimeZone? timeZone = null)
    {
        WorkspaceTimeZone zone = timeZone ?? WorkspaceTimeZone.Utc;
        DateOnly end = zone.DateOf(boundary);

        return frequency == ReportFrequency.Weekly
            ? ReportPeriod.Create(end.AddDays(-7), end, zone)
            : ReportPeriod.Create(end.AddMonths(-1), end, zone);
    }

    private static DateOnly PreviousBoundaryDate(ReportFrequency frequency, DateOnly today) =>
        frequency switch
        {
            // DayOfWeek.Monday is 1; Sunday is 0 and belongs to the week that
            // started six days earlier.
            ReportFrequency.Weekly => today.AddDays(-(((int)today.DayOfWeek + 6) % 7)),
            ReportFrequency.Monthly => new DateOnly(today.Year, today.Month, 1),
            _ => throw new DomainValidationException("That report frequency is not recognised.")
            {
                Code = "reports.frequency.invalid",
            },
        };
}
