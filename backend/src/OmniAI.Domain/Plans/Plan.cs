using OmniAI.Domain.Tenants;

namespace OmniAI.Domain.Plans;

/// <summary>
/// What a workspace on this plan is allowed to consume.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Platform configuration, not workspace data.</strong> A plan is not
/// owned by any tenant, is shared by many, and no workspace administrator can
/// create or alter one. There is deliberately no tenant-facing endpoint that
/// writes here: a workspace that could edit its own limits would not have limits.
/// </para>
/// <para>
/// <strong>Immutable once created.</strong> There is no mutator on this class.
/// Changing what "Starter" means underneath the workspaces already on it would
/// silently rewrite an agreement they never saw change; a new plan is introduced
/// instead and workspaces are moved to it deliberately. That keeps Phase 24
/// honest without building plan-version history, which belongs with the
/// commercial machinery in Phase 25.
/// </para>
/// <para>
/// <strong>No price, anywhere.</strong> A plan here says what may be used, never
/// what it costs. Money is Phase 25's subject and none of its vocabulary appears
/// on this aggregate.
/// </para>
/// </remarks>
public sealed class Plan
{
    private Plan(
        PlanId id,
        PlanCode code,
        PlanDisplayName displayName,
        PlanLimit maxMembers,
        PlanLimit maxAiAgents,
        PlanLimit maxKnowledgeDocuments,
        PlanLimit maxWebChatEndpoints,
        PlanLimit maxWhatsAppConnections,
        PlanLimit maxMonthlyProviderOperations,
        DateTimeOffset createdAt)
    {
        Id = id;
        Code = code;
        DisplayName = displayName;
        MaxMembersValue = maxMembers.Maximum;
        MaxAiAgentsValue = maxAiAgents.Maximum;
        MaxKnowledgeDocumentsValue = maxKnowledgeDocuments.Maximum;
        MaxWebChatEndpointsValue = maxWebChatEndpoints.Maximum;
        MaxWhatsAppConnectionsValue = maxWhatsAppConnections.Maximum;
        MaxMonthlyProviderOperationsValue = maxMonthlyProviderOperations.Maximum;
        CreatedAt = createdAt;
    }

    /// <summary>
    /// The constructor persistence uses.
    /// </summary>
    /// <remarks>
    /// Takes the stored <c>int?</c> caps directly, because those are what the
    /// columns hold. The rich constructor above cannot be used for rehydration:
    /// its parameters are <see cref="PlanLimit"/>, which is not what is stored,
    /// and binding it would mean persistence knowing how to build one.
    /// </remarks>
    private Plan(
        PlanId id,
        PlanCode code,
        PlanDisplayName displayName,
        int? maxMembersValue,
        int? maxAiAgentsValue,
        int? maxKnowledgeDocumentsValue,
        int? maxWebChatEndpointsValue,
        int? maxWhatsAppConnectionsValue,
        int? maxMonthlyProviderOperationsValue,
        DateTimeOffset createdAt)
    {
        Id = id;
        Code = code;
        DisplayName = displayName;
        MaxMembersValue = maxMembersValue;
        MaxAiAgentsValue = maxAiAgentsValue;
        MaxKnowledgeDocumentsValue = maxKnowledgeDocumentsValue;
        MaxWebChatEndpointsValue = maxWebChatEndpointsValue;
        MaxWhatsAppConnectionsValue = maxWhatsAppConnectionsValue;
        MaxMonthlyProviderOperationsValue = maxMonthlyProviderOperationsValue;
        CreatedAt = createdAt;
    }

    /// <summary>
    /// The stored caps, where absent means unlimited.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Private and <c>int?</c> because that is what the column is, and the column
    /// has to be genuinely nullable: unlimited is the absence of a cap, not a
    /// large number standing in for one. Persistence maps these; nothing else
    /// sees them.
    /// </para>
    /// <para>
    /// The public surface stays <see cref="PlanLimit"/>, so callers get
    /// <c>IsUnlimited</c> and <c>AllowsAnother</c> rather than having to remember
    /// what a null means every time they ask.
    /// </para>
    /// </remarks>
    private int? MaxMembersValue { get; init; }

    private int? MaxAiAgentsValue { get; init; }

    private int? MaxKnowledgeDocumentsValue { get; init; }

    private int? MaxWebChatEndpointsValue { get; init; }

    private int? MaxWhatsAppConnectionsValue { get; init; }

    private int? MaxMonthlyProviderOperationsValue { get; init; }

    public PlanId Id { get; }

    public PlanCode Code { get; }

    public PlanDisplayName DisplayName { get; }

    public PlanLimit MaxMembers => PlanLimit.From(MaxMembersValue);

    public PlanLimit MaxAiAgents => PlanLimit.From(MaxAiAgentsValue);

    public PlanLimit MaxKnowledgeDocuments => PlanLimit.From(MaxKnowledgeDocumentsValue);

    public PlanLimit MaxWebChatEndpoints => PlanLimit.From(MaxWebChatEndpointsValue);

    public PlanLimit MaxWhatsAppConnections => PlanLimit.From(MaxWhatsAppConnectionsValue);

    /// <summary>
    /// Provider operations allowed per UTC calendar month.
    /// </summary>
    /// <remarks>
    /// Operations, not tokens. Exact token counts are only known after a provider
    /// has answered, so a token cap could not be enforced before dispatch without
    /// estimating — and estimating usage is precisely what the accounting layer
    /// refuses to do.
    /// </remarks>
    public PlanLimit MaxMonthlyProviderOperations =>
        PlanLimit.From(MaxMonthlyProviderOperationsValue);

    public DateTimeOffset CreatedAt { get; }

    public static Plan Create(
        PlanCode code,
        PlanDisplayName displayName,
        PlanLimit maxMembers,
        PlanLimit maxAiAgents,
        PlanLimit maxKnowledgeDocuments,
        PlanLimit maxWebChatEndpoints,
        PlanLimit maxWhatsAppConnections,
        PlanLimit maxMonthlyProviderOperations,
        DateTimeOffset createdAt)
    {
        if (code.IsEmpty)
        {
            throw new DomainValidationException("A plan needs a code.");
        }

        return new Plan(
            PlanId.New(),
            code,
            displayName,
            maxMembers,
            maxAiAgents,
            maxKnowledgeDocuments,
            maxWebChatEndpoints,
            maxWhatsAppConnections,
            maxMonthlyProviderOperations,
            RequireInstant(createdAt));
    }

    /// <summary>Rehydrates a stored plan. Persistence only.</summary>
    public static Plan Restore(
        PlanId id,
        PlanCode code,
        PlanDisplayName displayName,
        PlanLimit maxMembers,
        PlanLimit maxAiAgents,
        PlanLimit maxKnowledgeDocuments,
        PlanLimit maxWebChatEndpoints,
        PlanLimit maxWhatsAppConnections,
        PlanLimit maxMonthlyProviderOperations,
        DateTimeOffset createdAt) =>
        new(
            id,
            code,
            displayName,
            maxMembers,
            maxAiAgents,
            maxKnowledgeDocuments,
            maxWebChatEndpoints,
            maxWhatsAppConnections,
            maxMonthlyProviderOperations,
            createdAt);

    /// <summary>The cap this plan places on one entitlement.</summary>
    /// <remarks>
    /// A switch rather than a dictionary, so adding an entitlement is a compile
    /// error here rather than a silent "unlimited" at runtime.
    /// </remarks>
    public PlanLimit LimitFor(EntitlementKind kind) => kind switch
    {
        EntitlementKind.Members => MaxMembers,
        EntitlementKind.AiAgents => MaxAiAgents,
        EntitlementKind.KnowledgeDocuments => MaxKnowledgeDocuments,
        EntitlementKind.WebChatEndpoints => MaxWebChatEndpoints,
        EntitlementKind.WhatsAppConnections => MaxWhatsAppConnections,
        EntitlementKind.MonthlyProviderOperations => MaxMonthlyProviderOperations,
        _ => throw new DomainValidationException($"'{kind}' is not an entitlement this plan caps."),
    };

    private static DateTimeOffset RequireInstant(DateTimeOffset instant) =>
        instant == default
            ? throw new DomainValidationException("A plan must record when it was created.")
            : instant.ToUniversalTime();
}

/// <summary>
/// The plan a workspace is on.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Exactly one per workspace, enforced by the primary key.</strong> The
/// tenant id <em>is</em> the key, so "which plan is this workspace on" cannot
/// have two answers and no query needs an ordering or a "current" flag to pick
/// between rows.
/// </para>
/// <para>
/// <strong>A workspace cannot assign its own.</strong> Nothing tenant-facing
/// writes this. Phase 24 assigns it at onboarding and backfills existing
/// workspaces; deliberate transitions between plans are Phase 25's, along with
/// whatever authority is supposed to approve them.
/// </para>
/// </remarks>
public sealed class WorkspacePlanAssignment
{
    private WorkspacePlanAssignment(TenantId tenantId, PlanId planId, DateTimeOffset assignedAt)
    {
        TenantId = tenantId;
        PlanId = planId;
        AssignedAt = assignedAt;
    }

    public TenantId TenantId { get; }

    public PlanId PlanId { get; }

    public DateTimeOffset AssignedAt { get; }

    public static WorkspacePlanAssignment Create(
        TenantId tenantId,
        PlanId planId,
        DateTimeOffset assignedAt)
    {
        if (tenantId.IsEmpty)
        {
            throw new DomainValidationException("A plan assignment needs a workspace.");
        }

        if (planId.IsEmpty)
        {
            throw new DomainValidationException("A plan assignment needs a plan.");
        }

        return new WorkspacePlanAssignment(
            tenantId,
            planId,
            assignedAt == default
                ? throw new DomainValidationException(
                    "A plan assignment must record when it was made.")
                : assignedAt.ToUniversalTime());
    }

    /// <summary>Rehydrates a stored assignment. Persistence only.</summary>
    public static WorkspacePlanAssignment Restore(
        TenantId tenantId,
        PlanId planId,
        DateTimeOffset assignedAt) =>
        new(tenantId, planId, assignedAt);
}
