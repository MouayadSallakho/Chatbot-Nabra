using OmniAI.Domain.Plans;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Plans;

/// <summary>
/// What a workspace's plan allows, and the gate that holds it to it.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A permission is not an entitlement.</strong> A member holding
/// <c>ai-agents.create</c> has been trusted by their workspace to create agents;
/// it says nothing about whether the workspace may hold another one. The two
/// questions are asked separately, in that order, and neither answers the other.
/// Plan limits are therefore never modelled as permissions — granting somebody a
/// permission must never raise a limit, and a workspace at its cap must not be
/// able to buy its way past it by editing a role.
/// </para>
/// <para>
/// <strong>Deliberately narrow.</strong> Not a generic quota repository, not a
/// plan CRUD surface, not a rules engine. Two jobs: describe the workspace's
/// entitlements, and let a mutation take a slot atomically.
/// </para>
/// </remarks>
public interface IWorkspaceEntitlementService
{
    /// <summary>
    /// Everything this workspace's plan allows and how much of it is used.
    /// </summary>
    /// <remarks>
    /// <strong>Reads only, and is not the enforcement path.</strong> A screen may
    /// use this to warn somebody before they try; nothing may use it to decide
    /// whether a mutation is allowed, because between this read and that mutation
    /// another request can take the last slot. Enforcement is
    /// <see cref="ReserveAsync"/>, which holds a lock across the decision.
    /// </remarks>
    /// <exception cref="WorkspacePlanMissingException">
    /// The workspace has no plan assignment. A workspace without one is a bug in
    /// onboarding or backfill, and is refused rather than treated as unlimited.
    /// </exception>
    Task<WorkspaceEntitlements> DescribeAsync(
        TenantId tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes one slot of an entitlement and performs the mutation that uses it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>The count and the insert are one atomic step.</strong> Counting,
    /// returning, and inserting afterwards is the classic check-then-act race: two
    /// requests both see four of five agents and both create one. Here the
    /// workspace's slot for this entitlement is locked in the database for the
    /// duration, so the second request waits, counts five, and is refused.
    /// </para>
    /// <para>
    /// <paramref name="mutate"/> runs inside that transaction and must use the
    /// same unit of work. If it throws, nothing is committed and the slot is
    /// released.
    /// </para>
    /// </remarks>
    /// <exception cref="EntitlementExceededException">
    /// The workspace is already at its limit. Nothing was mutated.
    /// </exception>
    /// <exception cref="WorkspacePlanMissingException">No plan is assigned.</exception>
    Task<T> ReserveAsync<T>(
        TenantId tenantId,
        EntitlementKind kind,
        Func<CancellationToken, Task<T>> mutate,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A workspace has reached what its plan allows.
/// </summary>
/// <remarks>
/// <para>
/// Not an authorisation failure and not a rate limit. The caller is permitted to
/// do this and is asking at a reasonable rate; the workspace simply already holds
/// as many as it may. That is a conflict with stored state, which is why it
/// becomes a 409 rather than a 403, a 429 or a 402.
/// </para>
/// <para>
/// <strong>Carries no commercial claim.</strong> What it holds is the
/// entitlement, the count, the cap and — for a monthly one — the period. No
/// price, no plan to buy, no payment link, because none of that exists yet.
/// </para>
/// </remarks>
public sealed class EntitlementExceededException : Exception
{
    private EntitlementExceededException(
        EntitlementKind kind,
        long current,
        int limit,
        EntitlementPeriod? period,
        string message)
        : base(message)
    {
        Kind = kind;
        Current = current;
        Limit = limit;
        Period = period;
    }

    public EntitlementKind Kind { get; }

    public long Current { get; }

    public int Limit { get; }

    /// <summary>The month the count covers, for monthly entitlements only.</summary>
    public EntitlementPeriod? Period { get; }

    /// <summary>A stable code a client can branch on without reading English.</summary>
    public string Code => $"entitlement.{Kind.ToString().ToLowerInvariant()}.exhausted";

    public static EntitlementExceededException For(
        EntitlementKind kind,
        long current,
        int limit,
        EntitlementPeriod? period = null) =>
        new(kind, current, limit, period, Describe(kind, limit));

    /// <summary>
    /// What a person is told.
    /// </summary>
    /// <remarks>
    /// Product-neutral on purpose. "Payment required", "card declined" and
    /// "subscription expired" would all be false — there is no payment, no card
    /// and no subscription in this platform — and a workspace reading any of them
    /// would go looking for a billing screen that does not exist.
    /// </remarks>
    private static string Describe(EntitlementKind kind, int limit) => kind switch
    {
        EntitlementKind.Members => limit == 0
            ? "This workspace's plan does not include team members."
            : $"This workspace has reached its plan limit of {limit} members.",

        EntitlementKind.AiAgents => limit == 0
            ? "This workspace's plan does not include AI agents."
            : $"This workspace has reached its plan limit of {limit} AI agents.",

        EntitlementKind.KnowledgeDocuments => limit == 0
            ? "This workspace's plan does not include knowledge documents."
            : $"This workspace has reached its plan limit of {limit} knowledge documents.",

        EntitlementKind.WebChatEndpoints => limit == 0
            ? "This workspace's plan does not include website chat."
            : $"This workspace has reached its plan limit of {limit} website chat endpoints.",

        EntitlementKind.WhatsAppConnections => limit == 0
            ? "This workspace's plan does not include WhatsApp."
            : $"This workspace has reached its plan limit of {limit} WhatsApp connections.",

        EntitlementKind.MonthlyProviderOperations => limit == 0
            ? "This workspace's plan does not include AI operations."
            : "This workspace has reached its monthly AI operation limit.",

        _ => "This workspace has reached what its plan allows.",
    };
}

/// <summary>
/// A workspace has no plan, which should be impossible.
/// </summary>
/// <remarks>
/// Onboarding assigns one atomically and the migration backfills every workspace
/// that predates plans, so a workspace without one means a bug rather than a
/// configuration choice. It is refused loudly instead of being treated as
/// unlimited — the failure mode of guessing "unlimited" is that a production
/// workspace quietly gets everything for free and nobody finds out.
/// </remarks>
public sealed class WorkspacePlanMissingException(Guid tenantId)
    : Exception($"Workspace '{tenantId}' has no plan assigned.")
{
    public Guid TenantId { get; } = tenantId;
}
