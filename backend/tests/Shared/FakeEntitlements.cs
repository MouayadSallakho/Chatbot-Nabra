using OmniAI.Application.Plans;
using OmniAI.Domain.Plans;
using OmniAI.Domain.Tenants;

namespace OmniAI.Testing;

/// <summary>
/// An entitlement service with a configurable plan, in memory.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Unlimited by default, which is the point.</strong> Phase 24 must not
/// change how anything behaves when a workspace's plan does not cap it, so every
/// suite that predates plans gets this double with no limits and is expected to
/// go on passing unchanged. A suite that starts failing because of it has found a
/// real regression, not a test that needs adjusting.
/// </para>
/// <para>
/// <strong>Not a substitute for the real thing where concurrency matters.</strong>
/// This double serialises nothing and counts nothing: it answers from numbers a
/// test set. Every claim about races, locks and the last remaining slot is proved
/// against real PostgreSQL in the Infrastructure suite, because a double that
/// pretended to be atomic would prove only that it agrees with itself.
/// </para>
/// </remarks>
public sealed class FakeEntitlements : IWorkspaceEntitlementService
{
    private readonly Dictionary<EntitlementKind, int> _limits = [];

    private readonly Dictionary<EntitlementKind, long> _used = [];

    /// <summary>How many times a slot was taken, per entitlement.</summary>
    public List<EntitlementKind> Reserved { get; } = [];

    public string PlanCode { get; set; } = "test-unlimited";

    public string PlanName { get; set; } = "Test (unlimited)";

    /// <summary>Caps one entitlement. Anything not capped stays unlimited.</summary>
    public FakeEntitlements Limit(EntitlementKind kind, int maximum, long used = 0)
    {
        _limits[kind] = maximum;
        _used[kind] = used;

        return this;
    }

    /// <summary>Makes one entitlement already full.</summary>
    public FakeEntitlements Exhaust(EntitlementKind kind, int maximum = 1) =>
        Limit(kind, maximum, maximum);

    public Task<WorkspaceEntitlements> DescribeAsync(
        TenantId tenantId,
        CancellationToken cancellationToken = default)
    {
        List<WorkspaceEntitlementView> views = [];

        foreach (EntitlementKind kind in Enum.GetValues<EntitlementKind>())
        {
            EntitlementUsage usage = EntitlementUsage.Create(kind, Used(kind), LimitOf(kind));

            views.Add(new WorkspaceEntitlementView(
                usage.Kind,
                usage.Used,
                usage.Limit.Maximum,
                usage.Limit.IsUnlimited,
                usage.PercentUsed,
                usage.State,
                usage.Remaining,
                kind == EntitlementKind.MonthlyProviderOperations
                    ? EntitlementPeriod.MonthOf(DateTimeOffset.UtcNow)
                    : null));
        }

        return Task.FromResult(new WorkspaceEntitlements(
            PlanCode,
            PlanName,
            DateTimeOffset.UnixEpoch,
            views));
    }

    public async Task<T> ReserveAsync<T>(
        TenantId tenantId,
        EntitlementKind kind,
        Func<CancellationToken, Task<T>> mutate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutate);

        PlanLimit limit = LimitOf(kind);
        long used = Used(kind);

        if (!limit.AllowsAnother(used))
        {
            throw EntitlementExceededException.For(kind, used, limit.Maximum!.Value);
        }

        T result = await mutate(cancellationToken).ConfigureAwait(false);

        Reserved.Add(kind);

        if (_limits.ContainsKey(kind))
        {
            _used[kind] = used + 1;
        }

        return result;
    }

    private PlanLimit LimitOf(EntitlementKind kind) =>
        _limits.TryGetValue(kind, out int maximum) ? PlanLimit.Of(maximum) : PlanLimit.Unlimited;

    private long Used(EntitlementKind kind) => _used.GetValueOrDefault(kind);
}

/// <summary>
/// A plan catalogue holding whatever a test put in it.
/// </summary>
/// <remarks>
/// Enough to exercise the default-plan resolver without a database: the
/// resolver's job is to turn a configured code into a plan or refuse, and both
/// outcomes are reachable from here.
/// </remarks>
public sealed class FakePlanCatalog : IPlanCatalog
{
    private readonly Dictionary<string, Plan> _plans = [];

    /// <summary>A catalogue with one unlimited plan under the given code.</summary>
    public static FakePlanCatalog With(string code = "test-unlimited")
    {
        FakePlanCatalog catalog = new();

        catalog.Add(Plan.Create(
            PlanCode.Create(code),
            PlanDisplayName.Create("Test (unlimited)"),
            PlanLimit.Unlimited,
            PlanLimit.Unlimited,
            PlanLimit.Unlimited,
            PlanLimit.Unlimited,
            PlanLimit.Unlimited,
            PlanLimit.Unlimited,
            DateTimeOffset.UnixEpoch));

        return catalog;
    }

    /// <summary>A catalogue with nothing in it. Every lookup misses.</summary>
    public static FakePlanCatalog Empty() => new();

    public FakePlanCatalog Add(Plan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        _plans[plan.Code.Value] = plan;

        return this;
    }

    public Task<Plan?> FindByCodeAsync(
        PlanCode code,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_plans.GetValueOrDefault(code.Value));

    public Task<Plan?> FindByIdAsync(
        PlanId planId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_plans.Values.FirstOrDefault(plan => plan.Id == planId));
}

/// <summary>Builders for the default-plan resolver under test.</summary>
public static class TestPlans
{
    /// <summary>A resolver that finds an unlimited plan under the configured code.</summary>
    public static DefaultPlanResolver Resolver(string code = "test-unlimited") =>
        new(FakePlanCatalog.With(code), new DefaultPlanOptions { DefaultPlanCode = code });

    /// <summary>A resolver for a deployment that configured nothing.</summary>
    public static DefaultPlanResolver Unconfigured() =>
        new(FakePlanCatalog.Empty(), new DefaultPlanOptions());

    /// <summary>A resolver naming a plan this deployment does not have.</summary>
    public static DefaultPlanResolver Missing(string code = "no-such-plan") =>
        new(FakePlanCatalog.Empty(), new DefaultPlanOptions { DefaultPlanCode = code });
}

/// <summary>Identities of the plans the model seeds (PlanConfiguration).</summary>
/// <remarks>
/// Fixtures that write a real workspace to PostgreSQL must name a plan that is
/// actually there; a generated id would be refused by the foreign key, which is
/// the constraint doing its job.
/// </remarks>
public static class SeededPlans
{
    /// <summary>The unlimited plan every pre-existing workspace was backfilled onto.</summary>
    public static readonly PlanId Legacy =
        PlanId.From(new Guid("01a12000-0000-7000-8000-000000000001"));

    /// <summary>The unlimited plan development and test deployments name.</summary>
    public static readonly PlanId Development =
        PlanId.From(new Guid("01a12000-0000-7000-8000-000000000002"));

    /// <summary>A real, finite plan. Codes are the stable handles.</summary>
    public const string StarterCode = "starter";

    public const string DevelopmentCode = "development";

    public const string LegacyCode = "legacy";
}
