namespace OmniAI.Domain.Reports;

/// <summary>
/// When scheduled reports are due, and which completed period each one covers.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Boundaries, not intervals.</strong> A weekly schedule is due at every
/// Monday 00:00 UTC and covers the seven days before it; a monthly one is due at
/// 00:00 UTC on the first of every month and covers the calendar month before
/// it. A period is always derived from a boundary, never from the instant a
/// worker happened to run, so a late or repeated run produces the same period —
/// and the same occurrence identity — as a punctual one.
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
    public static DateTimeOffset PreviousBoundary(ReportFrequency frequency, DateTimeOffset now)
    {
        DateOnly today = DateOnly.FromDateTime(now.UtcDateTime);

        DateOnly boundary = frequency switch
        {
            // DayOfWeek.Monday is 1; Sunday is 0 and belongs to the week that
            // started six days earlier.
            ReportFrequency.Weekly => today.AddDays(-(((int)today.DayOfWeek + 6) % 7)),
            ReportFrequency.Monthly => new DateOnly(today.Year, today.Month, 1),
            _ => throw new DomainValidationException("That report frequency is not recognised."),
        };

        return At(boundary);
    }

    /// <summary>The first boundary strictly after <paramref name="now"/>.</summary>
    public static DateTimeOffset NextBoundary(ReportFrequency frequency, DateTimeOffset now)
    {
        DateTimeOffset previous = PreviousBoundary(frequency, now);

        return frequency == ReportFrequency.Weekly ? previous.AddDays(7) : previous.AddMonths(1);
    }

    /// <summary>The completed period an occurrence at <paramref name="boundary"/> covers.</summary>
    public static ReportPeriod PeriodEndingAt(ReportFrequency frequency, DateTimeOffset boundary)
    {
        DateOnly end = DateOnly.FromDateTime(boundary.UtcDateTime);

        return frequency == ReportFrequency.Weekly
            ? ReportPeriod.Create(end.AddDays(-7), end)
            : ReportPeriod.Create(end.AddMonths(-1), end);
    }

    private static DateTimeOffset At(DateOnly date) =>
        new(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
}
