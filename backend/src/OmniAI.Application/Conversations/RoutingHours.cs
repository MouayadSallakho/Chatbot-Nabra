using OmniAI.Domain;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Conversations;

// Day uses DayOfWeek (Sunday = 0). An earlier end continues into the following day.
public sealed record RoutingShift(int Day, int StartMinute, int EndMinute);

public sealed record RoutingHours(bool Enabled, IReadOnlyList<RoutingShift> Shifts, Guid? AfterHoursRoleId)
{
    public static RoutingHours Default => new(false, Enumerable.Range(1, 5).Select(day => new RoutingShift(day, 540, 1020)).ToArray(), null);

    public void Validate()
    {
        if (AfterHoursRoleId == Guid.Empty || Shifts is null || Shifts.Count > 7 ||
            Shifts.Any(s => s is null || s.Day is < 0 or > 6 || s.StartMinute is < 0 or >= 1440 ||
                s.EndMinute is < 0 or > 1440 || s.StartMinute == s.EndMinute) ||
            Shifts.Select(s => s.Day).Distinct().Count() != Shifts.Count)
        {
            throw new DomainValidationException("Choose one valid opening interval per day.") { Code = "conversations.routing.hours_invalid" };
        }
    }

    public bool IsOpen(DateTimeOffset instant, WorkspaceTimeZone zone)
    {
        if (!Enabled)
        {
            return true;
        }
        var local = zone.LocalTimeOf(instant);
        int day = (int)local.DayOfWeek;
        int minute = local.Hour * 60 + local.Minute;
        return Shifts.Any(s => s.StartMinute < s.EndMinute
            ? s.Day == day && minute >= s.StartMinute && minute < s.EndMinute
            : (s.Day == day && minute >= s.StartMinute) || ((s.Day + 1) % 7 == day && minute < s.EndMinute));
    }
}
