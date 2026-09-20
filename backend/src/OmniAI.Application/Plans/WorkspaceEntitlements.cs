using OmniAI.Domain.Plans;

namespace OmniAI.Application.Plans;

/// <summary>
/// The window a monthly entitlement is measured over.
/// </summary>
/// <remarks>
/// Half-open and in UTC, like every other window in this platform: an operation
/// at exactly <paramref name="To"/> belongs to the next month, so no operation is
/// counted twice and none falls between two months.
/// </remarks>
public sealed record EntitlementPeriod(DateTimeOffset From, DateTimeOffset To)
{
    /// <summary>
    /// The UTC calendar month containing an instant.
    /// </summary>
    /// <remarks>
    /// A calendar month, not a rolling thirty days. A rolling window means a
    /// workspace's allowance recovers a little every day, which nobody can
    /// predict and nobody asked for; a calendar month resets on a date somebody
    /// can put in a diary.
    /// </remarks>
    public static EntitlementPeriod MonthOf(DateTimeOffset instant)
    {
        DateTimeOffset utc = instant.ToUniversalTime();

        DateTimeOffset from = new(utc.Year, utc.Month, 1, 0, 0, 0, TimeSpan.Zero);

        return new EntitlementPeriod(from, from.AddMonths(1));
    }

    public bool Contains(DateTimeOffset instant)
    {
        DateTimeOffset utc = instant.ToUniversalTime();

        return utc >= From && utc < To;
    }
}

/// <summary>One entitlement as a screen needs it.</summary>
public sealed record WorkspaceEntitlementView(
    EntitlementKind Kind,
    long Used,

    /// <summary>The cap, or null when this plan does not cap it.</summary>
    int? Limit,
    bool IsUnlimited,
    int? PercentUsed,
    EntitlementState State,
    long? Remaining,

    /// <summary>Set only for entitlements measured over a period.</summary>
    EntitlementPeriod? Period);

/// <summary>
/// Everything a workspace's plan allows, and how much of it is gone.
/// </summary>
/// <remarks>
/// <strong>A snapshot, not a stored document.</strong> Every figure is counted
/// from the real rows when asked, so there is no counter to fall out of step with
/// what the workspace actually holds.
/// </remarks>
public sealed record WorkspaceEntitlements(
    string PlanCode,
    string PlanName,
    DateTimeOffset AssignedAt,
    IReadOnlyList<WorkspaceEntitlementView> Entitlements)
{
    public WorkspaceEntitlementView? Find(EntitlementKind kind) =>
        Entitlements.FirstOrDefault(entitlement => entitlement.Kind == kind);
}
