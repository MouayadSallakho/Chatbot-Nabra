using System.Reflection;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Tenants;

namespace OmniAI.Domain.Tests.AiAgents;

public sealed class AiAgentTests
{
    private static readonly DateTimeOffset Created = new(2026, 5, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = new(2026, 5, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset LaterStill = new(2026, 5, 1, 11, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Earlier = new(2026, 4, 1, 9, 0, 0, TimeSpan.Zero);

    private static AiAgent CreateAgent(
        string name = "Sales Assistant",
        string instructions = "You are the sales assistant for Acme Trading.",
        DateTimeOffset? createdAt = null) =>
        AiAgent.Create(
            TenantId.New(),
            AiAgentName.Create(name),
            AgentInstructions.Create(instructions),
            createdAt ?? Created);

    // ------------------------------------------------------------------ creation

    [Fact]
    public void Create_produces_an_agent_with_the_supplied_name_and_instructions()
    {
        AiAgent agent = CreateAgent("Booking Assistant", "Take table reservations.");

        Assert.Equal("Booking Assistant", agent.Name.Value);
        Assert.Equal("Take table reservations.", agent.Instructions.Value);
    }

    [Fact]
    public void Create_assigns_a_non_empty_identity()
    {
        Assert.False(CreateAgent().Id.IsEmpty);
    }

    [Fact]
    public void Create_assigns_a_distinct_identity_to_each_agent()
    {
        Assert.NotEqual(CreateAgent().Id, CreateAgent().Id);
    }

    [Fact]
    public void Create_records_the_owning_tenant()
    {
        TenantId tenant = TenantId.New();

        AiAgent agent = AiAgent.Create(
            tenant,
            AiAgentName.Create("Sales Assistant"),
            AgentInstructions.Create("Sell things."),
            Created);

        Assert.Equal(tenant, agent.TenantId);
    }

    [Fact]
    public void Create_starts_the_agent_as_a_draft()
    {
        // Not Active: deciding when an agent starts serving customers belongs to
        // the tenant, and must not be a side effect of saving it.
        Assert.Equal(AiAgentStatus.Draft, CreateAgent().Status);
    }

    [Fact]
    public void Create_normalises_the_name_and_instructions()
    {
        AiAgent agent = CreateAgent("  Sales Assistant  ", "\n  Sell things.  \n");

        Assert.Equal("Sales Assistant", agent.Name.Value);
        Assert.Equal("Sell things.", agent.Instructions.Value);
    }

    [Fact]
    public void Create_rejects_an_empty_tenant_id()
    {
        // default(TenantId) is the one identity C# lets a caller fabricate, so the
        // boundary rejects it explicitly.
        DomainValidationException failure = Assert.Throws<DomainValidationException>(() =>
            AiAgent.Create(
                default,
                AiAgentName.Create("Sales Assistant"),
                AgentInstructions.Create("Sell things."),
                Created));

        Assert.Contains("must belong to a tenant", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_rejects_a_null_name()
    {
        Assert.Throws<ArgumentNullException>(() =>
            AiAgent.Create(TenantId.New(), null!, AgentInstructions.Create("Sell things."), Created));
    }

    [Fact]
    public void Create_rejects_null_instructions()
    {
        Assert.Throws<ArgumentNullException>(() =>
            AiAgent.Create(TenantId.New(), AiAgentName.Create("Sales Assistant"), null!, Created));
    }

    [Fact]
    public void Create_rejects_an_unset_creation_instant()
    {
        Assert.Throws<DomainValidationException>(() =>
            AiAgent.Create(
                TenantId.New(),
                AiAgentName.Create("Sales Assistant"),
                AgentInstructions.Create("Sell things."),
                default));
    }

    // ---------------------------------------------------------------------- time

    [Fact]
    public void Create_records_the_supplied_creation_instant()
    {
        AiAgent agent = CreateAgent(createdAt: Created);

        Assert.Equal(Created, agent.CreatedAt);
    }

    [Fact]
    public void Create_starts_the_update_mark_at_the_creation_instant()
    {
        AiAgent agent = CreateAgent(createdAt: Created);

        Assert.Equal(Created, agent.UpdatedAt);
        Assert.Equal(agent.CreatedAt, agent.UpdatedAt);
    }

    [Fact]
    public void Instants_are_normalised_to_utc()
    {
        DateTimeOffset withOffset = new(2026, 5, 1, 12, 0, 0, TimeSpan.FromHours(3));

        AiAgent agent = CreateAgent(createdAt: withOffset);

        Assert.Equal(TimeSpan.Zero, agent.CreatedAt.Offset);
        Assert.Equal(withOffset.UtcDateTime, agent.CreatedAt.UtcDateTime);
    }

    [Fact]
    public void The_domain_never_reads_the_clock_so_behaviour_is_deterministic()
    {
        // Two agents created with the same supplied instant have identical
        // timestamps. If any operation called UtcNow, this could not hold.
        AiAgent first = CreateAgent(createdAt: Created);
        AiAgent second = CreateAgent(createdAt: Created);

        Assert.Equal(first.CreatedAt, second.CreatedAt);
        Assert.Equal(first.UpdatedAt, second.UpdatedAt);
    }

    [Fact]
    public void The_creation_instant_never_changes()
    {
        AiAgent agent = CreateAgent(createdAt: Created);

        agent.Rename(AiAgentName.Create("Renamed"), Later);
        agent.UpdateInstructions(AgentInstructions.Create("Different."), LaterStill);
        agent.Activate(LaterStill);

        Assert.Equal(Created, agent.CreatedAt);
    }

    [Fact]
    public void The_update_mark_never_regresses_when_an_earlier_instant_is_supplied()
    {
        // High-water-mark semantics, consistent with the accepted
        // Conversation.LastActivityAt decision.
        AiAgent agent = CreateAgent(createdAt: Created);

        agent.Rename(AiAgentName.Create("Renamed"), LaterStill);
        Assert.Equal(LaterStill, agent.UpdatedAt);

        agent.UpdateInstructions(AgentInstructions.Create("Arrived out of order."), Earlier);

        Assert.Equal(LaterStill, agent.UpdatedAt);
    }

    [Fact]
    public void A_mutation_with_an_earlier_instant_is_still_applied()
    {
        // The mark is protected; the edit is not discarded. The Domain records
        // what it is told and does not silently drop a change.
        AiAgent agent = CreateAgent(createdAt: Created);
        agent.Rename(AiAgentName.Create("Renamed"), LaterStill);

        agent.UpdateInstructions(AgentInstructions.Create("Arrived out of order."), Earlier);

        Assert.Equal("Arrived out of order.", agent.Instructions.Value);
    }

    // -------------------------------------------------------------------- rename

    [Fact]
    public void Rename_changes_the_name()
    {
        AiAgent agent = CreateAgent("Sales Assistant");

        agent.Rename(AiAgentName.Create("Sales & Pre-sales"), Later);

        Assert.Equal("Sales & Pre-sales", agent.Name.Value);
    }

    [Fact]
    public void Rename_advances_the_update_mark()
    {
        AiAgent agent = CreateAgent(createdAt: Created);

        agent.Rename(AiAgentName.Create("Renamed"), Later);

        Assert.Equal(Later, agent.UpdatedAt);
    }

    [Fact]
    public void Rename_to_the_same_name_changes_nothing()
    {
        AiAgent agent = CreateAgent("Sales Assistant", createdAt: Created);

        agent.Rename(AiAgentName.Create("  Sales Assistant  "), Later);

        Assert.Equal("Sales Assistant", agent.Name.Value);
        Assert.Equal(Created, agent.UpdatedAt);
    }

    [Fact]
    public void Rename_rejects_a_null_name()
    {
        AiAgent agent = CreateAgent();

        Assert.Throws<ArgumentNullException>(() => agent.Rename(null!, Later));
    }

    [Fact]
    public void Rename_rejects_an_unset_instant()
    {
        AiAgent agent = CreateAgent();

        Assert.Throws<DomainValidationException>(
            () => agent.Rename(AiAgentName.Create("Renamed"), default));
    }

    [Fact]
    public void Rename_leaves_a_rejected_name_with_no_effect()
    {
        // The value object rejects the name before the aggregate is touched.
        AiAgent agent = CreateAgent("Sales Assistant", createdAt: Created);

        Assert.Throws<DomainValidationException>(() => AiAgentName.Create("   "));

        Assert.Equal("Sales Assistant", agent.Name.Value);
        Assert.Equal(Created, agent.UpdatedAt);
    }

    [Fact]
    public void Rename_does_not_change_the_owning_tenant()
    {
        TenantId tenant = TenantId.New();
        AiAgent agent = AiAgent.Create(
            tenant,
            AiAgentName.Create("Sales Assistant"),
            AgentInstructions.Create("Sell things."),
            Created);

        agent.Rename(AiAgentName.Create("Renamed"), Later);

        Assert.Equal(tenant, agent.TenantId);
    }

    [Fact]
    public void Rename_does_not_change_the_identity_or_status()
    {
        AiAgent agent = CreateAgent();
        AiAgentId id = agent.Id;

        agent.Rename(AiAgentName.Create("Renamed"), Later);

        Assert.Equal(id, agent.Id);
        Assert.Equal(AiAgentStatus.Draft, agent.Status);
    }

    // ------------------------------------------------------- update instructions

    [Fact]
    public void UpdateInstructions_replaces_the_instructions()
    {
        AiAgent agent = CreateAgent(instructions: "Sell things.");

        agent.UpdateInstructions(AgentInstructions.Create("Answer support questions."), Later);

        Assert.Equal("Answer support questions.", agent.Instructions.Value);
    }

    [Fact]
    public void UpdateInstructions_advances_the_update_mark()
    {
        AiAgent agent = CreateAgent(createdAt: Created);

        agent.UpdateInstructions(AgentInstructions.Create("Different."), Later);

        Assert.Equal(Later, agent.UpdatedAt);
    }

    [Fact]
    public void UpdateInstructions_preserves_multiline_content()
    {
        const string multiline = "Rules:\n1. Be brief.\n2. Escalate refunds.\n\n\tNever quote a discount.";
        AiAgent agent = CreateAgent();

        agent.UpdateInstructions(AgentInstructions.Create(multiline), Later);

        Assert.Equal(multiline, agent.Instructions.Value);
    }

    [Fact]
    public void UpdateInstructions_to_the_same_value_changes_nothing()
    {
        AiAgent agent = CreateAgent(instructions: "Sell things.", createdAt: Created);

        agent.UpdateInstructions(AgentInstructions.Create("  Sell things.  "), Later);

        Assert.Equal("Sell things.", agent.Instructions.Value);
        Assert.Equal(Created, agent.UpdatedAt);
    }

    [Fact]
    public void UpdateInstructions_rejects_null_instructions()
    {
        AiAgent agent = CreateAgent();

        Assert.Throws<ArgumentNullException>(() => agent.UpdateInstructions(null!, Later));
    }

    [Fact]
    public void UpdateInstructions_rejects_an_unset_instant()
    {
        AiAgent agent = CreateAgent();

        Assert.Throws<DomainValidationException>(
            () => agent.UpdateInstructions(AgentInstructions.Create("Different."), default));
    }

    [Fact]
    public void UpdateInstructions_is_allowed_while_the_agent_is_active()
    {
        // Correcting a live agent's behaviour must not require taking it off the air.
        AiAgent agent = CreateAgent();
        agent.Activate(Later);

        agent.UpdateInstructions(AgentInstructions.Create("Corrected."), LaterStill);

        Assert.Equal("Corrected.", agent.Instructions.Value);
        Assert.Equal(AiAgentStatus.Active, agent.Status);
    }

    [Fact]
    public void UpdateInstructions_does_not_change_the_owning_tenant()
    {
        TenantId tenant = TenantId.New();
        AiAgent agent = AiAgent.Create(
            tenant,
            AiAgentName.Create("Sales Assistant"),
            AgentInstructions.Create("Sell things."),
            Created);

        agent.UpdateInstructions(AgentInstructions.Create("Different."), Later);

        Assert.Equal(tenant, agent.TenantId);
    }

    // ----------------------------------------------------------------- lifecycle

    [Fact]
    public void A_draft_agent_can_be_activated()
    {
        AiAgent agent = CreateAgent();

        agent.Activate(Later);

        Assert.Equal(AiAgentStatus.Active, agent.Status);
        Assert.Equal(Later, agent.UpdatedAt);
    }

    [Fact]
    public void An_active_agent_can_be_disabled()
    {
        AiAgent agent = CreateAgent();
        agent.Activate(Later);

        agent.Disable(LaterStill);

        Assert.Equal(AiAgentStatus.Disabled, agent.Status);
        Assert.Equal(LaterStill, agent.UpdatedAt);
    }

    [Fact]
    public void A_disabled_agent_can_be_activated_again()
    {
        AiAgent agent = CreateAgent();
        agent.Activate(Later);
        agent.Disable(LaterStill);

        agent.Activate(LaterStill.AddHours(1));

        Assert.Equal(AiAgentStatus.Active, agent.Status);
    }

    [Fact]
    public void Activating_an_already_active_agent_is_a_no_op()
    {
        AiAgent agent = CreateAgent();
        agent.Activate(Later);

        agent.Activate(LaterStill);

        Assert.Equal(AiAgentStatus.Active, agent.Status);

        // Nothing changed, so the update mark did not move.
        Assert.Equal(Later, agent.UpdatedAt);
    }

    [Fact]
    public void Disabling_an_already_disabled_agent_is_a_no_op()
    {
        AiAgent agent = CreateAgent();
        agent.Activate(Later);
        agent.Disable(LaterStill);

        agent.Disable(LaterStill.AddHours(1));

        Assert.Equal(AiAgentStatus.Disabled, agent.Status);
        Assert.Equal(LaterStill, agent.UpdatedAt);
    }

    [Fact]
    public void A_draft_agent_cannot_be_disabled()
    {
        // The one lifecycle rule that refuses rather than shrugging: a draft has
        // never been switched on, and there is no path back from Disabled to
        // Draft, so accepting this would strand the agent.
        AiAgent agent = CreateAgent();

        DomainStateException failure =
            Assert.Throws<DomainStateException>(() => agent.Disable(Later));

        Assert.Contains("still a draft", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(AiAgentStatus.Draft, agent.Status);
    }

    [Fact]
    public void A_refused_disable_leaves_the_update_mark_alone()
    {
        AiAgent agent = CreateAgent(createdAt: Created);

        Assert.Throws<DomainStateException>(() => agent.Disable(Later));

        Assert.Equal(Created, agent.UpdatedAt);
    }

    [Fact]
    public void Activate_rejects_an_unset_instant()
    {
        AiAgent agent = CreateAgent();

        Assert.Throws<DomainValidationException>(() => agent.Activate(default));
    }

    [Fact]
    public void Disable_rejects_an_unset_instant()
    {
        AiAgent agent = CreateAgent();
        agent.Activate(Later);

        Assert.Throws<DomainValidationException>(() => agent.Disable(default));
    }

    [Fact]
    public void Activation_never_fails_on_domain_configuration_because_it_cannot_be_invalid()
    {
        // Name and Instructions are non-null value objects that validated
        // themselves, and no operation can leave either missing, so a freshly
        // created agent is always activatable. This documents why Activate carries
        // no configuration guard: such a guard could not fail today.
        AiAgent agent = CreateAgent();

        agent.Activate(Later);

        Assert.Equal(AiAgentStatus.Active, agent.Status);
        Assert.False(string.IsNullOrWhiteSpace(agent.Name.Value));
        Assert.False(string.IsNullOrWhiteSpace(agent.Instructions.Value));
    }

    [Fact]
    public void A_full_lifecycle_round_trip_ends_active_with_the_latest_instant()
    {
        AiAgent agent = CreateAgent(createdAt: Created);

        agent.Activate(Later);
        agent.Disable(LaterStill);
        agent.Activate(LaterStill.AddHours(2));

        Assert.Equal(AiAgentStatus.Active, agent.Status);
        Assert.Equal(LaterStill.AddHours(2), agent.UpdatedAt);
        Assert.Equal(Created, agent.CreatedAt);
    }

    [Fact]
    public void The_status_enum_exposes_exactly_the_three_agreed_states()
    {
        // Guards against a hidden Deleted, Archived, Training, Error, Publishing,
        // Suspended or Trial state appearing later without a decision.
        Assert.Equal(
            [AiAgentStatus.Draft, AiAgentStatus.Active, AiAgentStatus.Disabled],
            Enum.GetValues<AiAgentStatus>());
    }

    [Fact]
    public void The_status_enum_has_no_zero_member()
    {
        // So an uninitialised value is detectable rather than silently Draft.
        Assert.False(Enum.IsDefined((AiAgentStatus)0));
    }

    // -------------------------------------------------------------- immutability

    [Fact]
    public void No_property_exposes_a_public_setter()
    {
        PropertyInfo[] settable =
        [
            .. typeof(AiAgent).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.GetSetMethod(nonPublic: false) is not null)
        ];

        Assert.Empty(settable);
    }

    [Fact]
    public void The_owning_tenant_has_no_setter_at_all_not_even_a_private_one()
    {
        // Stronger than "no public setter": there is no code path anywhere,
        // including inside the aggregate, that can reassign tenant ownership.
        PropertyInfo tenantId = typeof(AiAgent).GetProperty(nameof(AiAgent.TenantId))!;

        Assert.Null(tenantId.GetSetMethod(nonPublic: true));
        Assert.Null(tenantId.SetMethod);
    }

    [Fact]
    public void The_identity_and_creation_instant_have_no_setters_either()
    {
        foreach (string name in (string[])[nameof(AiAgent.Id), nameof(AiAgent.CreatedAt)])
        {
            PropertyInfo property = typeof(AiAgent).GetProperty(name)!;

            Assert.Null(property.SetMethod);
        }
    }

    [Fact]
    public void There_is_no_operation_that_moves_an_agent_between_tenants()
    {
        // Tenant ownership is the isolation boundary, so the absence of such an
        // operation is a security property worth asserting rather than assuming.
        string[] reassignments =
        [
            .. typeof(AiAgent).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                // Property accessors are not operations; get_TenantId is the
                // getter this aggregate is supposed to have.
                .Where(method => !method.IsSpecialName)
                .Select(method => method.Name)
                .Where(name =>
                    name.Contains("Tenant", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("Transfer", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("Move", StringComparison.OrdinalIgnoreCase))
        ];

        Assert.Empty(reassignments);
    }

    [Fact]
    public void The_aggregate_exposes_only_the_agreed_operations()
    {
        string[] operations =
        [
            .. typeof(AiAgent).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(method => !method.IsSpecialName)
                .Select(method => method.Name)
                .Order(StringComparer.Ordinal)
        ];

        Assert.Equal(["Activate", "Disable", "Rename", "UpdateInstructions"], operations);
    }

    // ------------------------------------------------- provider independence

    [Fact]
    public void The_aggregate_carries_no_provider_or_model_configuration()
    {
        // The central promise of this type. If a model name, provider id,
        // temperature, token limit or vector reference is ever added, this fails.
        string[] properties =
        [
            .. typeof(AiAgent).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal)
        ];

        Assert.Equal(
            ["CreatedAt", "Id", "Instructions", "Name", "Status", "TenantId", "UpdatedAt"],
            properties);
    }

    [Fact]
    public void The_agent_namespace_references_no_provider_type()
    {
        string[] providerShaped =
        [
            .. typeof(AiAgent).Assembly.GetTypes()
                .Where(type => type.Namespace == typeof(AiAgent).Namespace)
                .Select(type => type.Name)
                .Where(name =>
                    name.Contains("OpenAi", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("Anthropic", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("Model", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("Prompt", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("Embedding", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("Vector", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("Tool", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("Knowledge", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("Channel", StringComparison.OrdinalIgnoreCase))
        ];

        Assert.Empty(providerShaped);
    }
}
