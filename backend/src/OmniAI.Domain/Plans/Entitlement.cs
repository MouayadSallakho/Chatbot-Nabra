namespace OmniAI.Domain.Plans;

/// <summary>
/// The things a plan caps.
/// </summary>
/// <remarks>
/// <para>
/// A closed set, deliberately. Strings scattered through handlers — "agents",
/// "documents", "users" — drift apart, get misspelled in one place, and make it
/// impossible to ask the compiler whether every entitlement has a counter and a
/// limit. This enum is the list, and adding to it breaks every switch that has
/// not been updated.
/// </para>
/// <para>
/// Bounded to what Phase 24 genuinely enforces. Security bounds — upload size,
/// parser limits, rate limits, session lifetimes — are not entitlements and are
/// not listed here: they protect the platform whatever a workspace pays, and
/// folding them in would make a commercial decision able to weaken a defence.
/// </para>
/// </remarks>
public enum EntitlementKind
{
    /// <summary>Active workspace memberships. Pending invitations are not seats.</summary>
    Members = 1,

    /// <summary>Every AI agent that exists, whatever its status.</summary>
    AiAgents = 2,

    /// <summary>Every knowledge document, manual or file-backed alike.</summary>
    KnowledgeDocuments = 3,

    /// <summary>Every website chat endpoint, whatever its status.</summary>
    WebChatEndpoints = 4,

    /// <summary>Every WhatsApp connection, whatever its status.</summary>
    WhatsAppConnections = 5,

    /// <summary>Provider operations dispatched in the current UTC calendar month.</summary>
    MonthlyProviderOperations = 6,
}

/// <summary>
/// How close a workspace is to a limit.
/// </summary>
/// <remarks>
/// <strong>Informational, except for <see cref="Exhausted"/>.</strong> Warning
/// and Critical exist so somebody can act before they are stopped; nothing is
/// refused at 70% or 90%. Only the hard limit refuses, and the server is what
/// refuses it.
/// </remarks>
public enum EntitlementState
{
    /// <summary>Under 70% used, or unlimited.</summary>
    Normal = 1,

    /// <summary>At or over 70%.</summary>
    Warning = 2,

    /// <summary>At or over 90%.</summary>
    Critical = 3,

    /// <summary>At or over the limit. The next one is refused.</summary>
    Exhausted = 4,
}

/// <summary>
/// One entitlement, what a workspace has used of it, and what it may use.
/// </summary>
/// <remarks>
/// <para>
/// Computed rather than stored. There is no counter table to drift out of step
/// with the rows it claims to count: usage is a <c>COUNT</c> over the real
/// resource, taken when asked.
/// </para>
/// <para>
/// <strong>Percentages are integer arithmetic.</strong> 69.6% must not round up
/// into a warning, so the percentage is floored — except that any usage at or
/// over the limit is Exhausted regardless of what rounding would say.
/// </para>
/// </remarks>
public sealed record EntitlementUsage
{
    /// <summary>At or above this percentage, a workspace is warned.</summary>
    public const int WarningPercent = 70;

    /// <summary>At or above this percentage, a workspace is warned loudly.</summary>
    public const int CriticalPercent = 90;

    private EntitlementUsage(
        EntitlementKind kind,
        long used,
        PlanLimit limit,
        int? percentUsed,
        EntitlementState state)
    {
        Kind = kind;
        Used = used;
        Limit = limit;
        PercentUsed = percentUsed;
        State = state;
    }

    public EntitlementKind Kind { get; }

    /// <summary>How many exist right now.</summary>
    public long Used { get; }

    public PlanLimit Limit { get; }

    /// <summary>
    /// Percentage of the limit consumed, floored — or null when unlimited.
    /// </summary>
    /// <remarks>
    /// Null rather than nought for unlimited: nought would draw an empty progress
    /// bar and imply a limit exists somewhere off to the right.
    /// </remarks>
    public int? PercentUsed { get; }

    public EntitlementState State { get; }

    /// <summary>How many more are allowed, or null when unlimited.</summary>
    public long? Remaining => Limit.Remaining(Used);

    /// <summary>Whether one more would be refused.</summary>
    public bool IsExhausted => State == EntitlementState.Exhausted;

    public static EntitlementUsage Create(EntitlementKind kind, long used, PlanLimit limit)
    {
        if (used < 0)
        {
            throw new DomainValidationException("Usage cannot be negative.")
            {
                Code = "plans.usage.invalid",
            };
        }

        if (limit.IsUnlimited)
        {
            // An unlimited entitlement never warns and is never exhausted. There
            // is nothing to be a percentage of.
            return new EntitlementUsage(kind, used, limit, null, EntitlementState.Normal);
        }

        int maximum = limit.Maximum!.Value;

        if (maximum == 0)
        {
            // A limit of zero is exhausted from the start, and 0/0 is not a
            // percentage anybody should have to interpret.
            return new EntitlementUsage(kind, used, limit, 100, EntitlementState.Exhausted);
        }

        // Floored, so 69.9% stays Normal. Capped for display at 100 even if usage
        // somehow exceeded the limit -- which it can, when a plan's limits are
        // tightened under a workspace that was already above them.
        int percent = (int)Math.Min(100, used * 100 / maximum);

        EntitlementState state = used >= maximum
            ? EntitlementState.Exhausted
            : percent >= CriticalPercent
                ? EntitlementState.Critical
                : percent >= WarningPercent
                    ? EntitlementState.Warning
                    : EntitlementState.Normal;

        return new EntitlementUsage(kind, used, limit, percent, state);
    }
}
