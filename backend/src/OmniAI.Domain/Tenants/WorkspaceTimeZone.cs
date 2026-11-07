using System.Text.RegularExpressions;

namespace OmniAI.Domain.Tenants;

/// <summary>
/// The time zone a workspace counts its days in: an IANA name (Phase 37.6, row 34).
/// </summary>
/// <remarks>
/// <para>
/// <strong>A name, not an offset.</strong> <c>Asia/Riyadh</c> or
/// <c>Europe/Berlin</c>, never <c>+03:00</c>. An offset cannot know that Berlin's
/// clocks go back on the last Sunday of October, so a fixed offset would put
/// every winter day an hour wrong.
/// </para>
/// <para>
/// <strong>Days are local, instants are not.</strong> A day in a zone runs from
/// its first local midnight to the next one, converted to instants. Those are
/// usually 24 hours apart, but 23 on the day clocks go forward and 25 on the day
/// they go back, and this type says so rather than assuming 24.
/// </para>
/// <para>
/// <strong>Two checks guard a new value.</strong> This type accepts only a name
/// the runtime knows as an IANA zone, spelt exactly as the zone database spells
/// it. The use case then asks PostgreSQL whether it knows the same name, because
/// the database is what converts every stored instant into a local day.
/// </para>
/// <para>
/// <strong>A stored name is trusted to be readable.</strong> Reading a workspace
/// never fails because of its zone: the zone's rules are looked up only when a day
/// is first computed. A name the runtime no longer knows then fails loudly rather
/// than being quietly counted in UTC.
/// </para>
/// </remarks>
public sealed partial class WorkspaceTimeZone : IEquatable<WorkspaceTimeZone>
{
    /// <summary>The longest name accepted, and the column's length.</summary>
    public const int MaxLength = 64;

    /// <summary>The problem code for a name that is not an accepted zone.</summary>
    public const string InvalidCode = "workspace.time_zone.invalid";

    /// <summary>Every workspace's zone until somebody chooses another.</summary>
    public static readonly WorkspaceTimeZone Utc = new("UTC", TimeZoneInfo.Utc);

    private TimeZoneInfo? _rules;

    private WorkspaceTimeZone(string name, TimeZoneInfo? rules)
    {
        Name = name;
        _rules = rules;
    }

    /// <summary>The IANA name, exactly as the zone database spells it.</summary>
    public string Name { get; }

    /// <summary>Whether this is UTC, the default.</summary>
    public bool IsUtc => string.Equals(Name, Utc.Name, StringComparison.Ordinal);

    private TimeZoneInfo Rules => _rules ??= Resolve(Name);

    /// <summary>A zone somebody chose.</summary>
    /// <exception cref="DomainValidationException">
    /// The name is missing, longer than <see cref="MaxLength"/>, not an IANA zone
    /// this runtime knows, or not spelt as the zone database spells it.
    /// </exception>
    public static WorkspaceTimeZone Create(string? name)
    {
        string candidate = name?.Trim() ?? string.Empty;

        if (string.Equals(candidate, Utc.Name, StringComparison.Ordinal))
        {
            return Utc;
        }

        if (candidate.Length == 0
            || candidate.Length > MaxLength
            || !Shape().IsMatch(candidate)
            || candidate.StartsWith("posix/", StringComparison.Ordinal)
            || candidate.StartsWith("right/", StringComparison.Ordinal)
            || !TimeZoneInfo.TryFindSystemTimeZoneById(candidate, out TimeZoneInfo? rules)
            || !rules.HasIanaId
            || !string.Equals(rules.Id, candidate, StringComparison.Ordinal))
        {
            throw new DomainValidationException(
                "Choose a time zone by its IANA name, such as Asia/Riyadh or Europe/Berlin.")
            {
                Code = InvalidCode,
            };
        }

        return new WorkspaceTimeZone(candidate, rules);
    }

    /// <summary>Rehydrates a stored name, without looking its rules up yet.</summary>
    public static WorkspaceTimeZone FromStored(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return string.Equals(name, Utc.Name, StringComparison.Ordinal) ? Utc : new WorkspaceTimeZone(name, null);
    }

    /// <summary>The local date an instant falls on.</summary>
    public DateOnly DateOf(DateTimeOffset instant) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, Rules).DateTime);

    /// <summary>
    /// The first instant of a local date.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Usually local midnight. Where clocks go forward <em>at</em> midnight, that
    /// midnight never happens, and the day starts at the first local minute that
    /// does. Where clocks go back across midnight, midnight happens twice, and the
    /// day starts at the earlier one.
    /// </para>
    /// <para>The result is always expressed in UTC.</para>
    /// </remarks>
    public DateTimeOffset StartOf(DateOnly date)
    {
        DateTime local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        DateTime limit = local.AddDays(1);

        while (Rules.IsInvalidTime(local))
        {
            local = local.AddMinutes(1);

            if (local >= limit)
            {
                throw new InvalidOperationException(
                    $"The time zone {Name} has no instant on {date:yyyy-MM-dd}.");
            }
        }

        TimeSpan offset = Rules.IsAmbiguousTime(local)
            ? Rules.GetAmbiguousTimeOffsets(local).Max()
            : Rules.GetUtcOffset(local);

        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    public bool Equals(WorkspaceTimeZone? other) =>
        other is not null && string.Equals(Name, other.Name, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as WorkspaceTimeZone);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Name);

    public static bool operator ==(WorkspaceTimeZone? left, WorkspaceTimeZone? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(WorkspaceTimeZone? left, WorkspaceTimeZone? right) => !(left == right);

    public override string ToString() => Name;

    private static TimeZoneInfo Resolve(string name) =>
        TimeZoneInfo.TryFindSystemTimeZoneById(name, out TimeZoneInfo? rules)
            ? rules
            : throw new InvalidOperationException(
                $"The stored time zone {name} is not known to this runtime's zone database.");

    // Area/Location[/Sub], or a single name such as UTC or EST5EDT; the characters
    // the IANA database uses and nothing else.
    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_+-]*(/[A-Za-z0-9_+-]+){0,2}$", RegexOptions.CultureInvariant)]
    private static partial Regex Shape();
}
