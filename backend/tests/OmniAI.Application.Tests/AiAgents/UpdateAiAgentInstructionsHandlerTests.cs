using OmniAI.Application.AiAgents;
using OmniAI.Application.AiAgents.UpdateAiAgentInstructions;
using OmniAI.Application.Concurrency;
using OmniAI.Domain;
using OmniAI.Application.Tests.Security;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Tenants;
using OmniAI.Testing;
using static OmniAI.Application.Tests.AiAgents.RenameAiAgentHandlerTests;

namespace OmniAI.Application.Tests.AiAgents;

public sealed class UpdateAiAgentInstructionsHandlerTests
{
    private static readonly DateTimeOffset Created = new(2026, 5, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = new(2026, 5, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Updating_with_the_current_version_replaces_the_instructions()
    {
        (TenantId tenant, AiAgent agent, InMemoryAiAgentRepository agents) = Arrange();
        ConcurrencyToken before = agents.VersionOf(agent);

        VersionedAiAgentView? updated = await Handler(agents, Later).HandleAsync(
            new UpdateAiAgentInstructionsCommand(
                tenant.Value, agent.Id.Value, "Answer support questions.", before));

        Assert.NotNull(updated);
        Assert.Equal("Answer support questions.", updated.View.Instructions);
        Assert.Equal(Later, updated.View.UpdatedAt);
        Assert.NotEqual(before, updated.Version);
    }

    [Fact]
    public async Task Multiline_instructions_survive_the_use_case_exactly()
    {
        // The formatting rule that matters most for this type: an author who
        // writes numbered rules on separate lines means them to stay there, and
        // nothing in this layer may re-wrap or collapse them.
        const string multiline =
            "Rules:\n1. Be brief.\n2. Never quote a discount.\n\n\tEscalate refunds to a human.";

        (TenantId tenant, AiAgent agent, InMemoryAiAgentRepository agents) = Arrange();

        VersionedAiAgentView updated = await Handler(agents, Later).HandleAsync(
            new UpdateAiAgentInstructionsCommand(
                tenant.Value, agent.Id.Value, multiline, agents.VersionOf(agent)))
            ?? throw new InvalidOperationException();

        Assert.Equal(multiline, updated.View.Instructions);
        Assert.Equal(multiline, agent.Instructions.Value);
    }

    [Fact]
    public async Task Surrounding_whitespace_is_trimmed_but_interior_formatting_is_not()
    {
        (TenantId tenant, AiAgent agent, InMemoryAiAgentRepository agents) = Arrange();

        VersionedAiAgentView updated = await Handler(agents, Later).HandleAsync(
            new UpdateAiAgentInstructionsCommand(
                tenant.Value, agent.Id.Value, "\n\n  One.\n\n  Two.  \n\n", agents.VersionOf(agent)))
            ?? throw new InvalidOperationException();

        Assert.Equal("One.\n\n  Two.", updated.View.Instructions);
    }

    [Fact]
    public async Task Updating_is_allowed_while_the_agent_is_active()
    {
        // Correcting a live agent must not require taking it off the air.
        (TenantId tenant, AiAgent agent, InMemoryAiAgentRepository agents) =
            Arrange(setUp: stored => stored.Activate(Created.AddMinutes(5)));

        VersionedAiAgentView updated = await Handler(agents, Later).HandleAsync(
            new UpdateAiAgentInstructionsCommand(
                tenant.Value, agent.Id.Value, "Corrected.", agents.VersionOf(agent)))
            ?? throw new InvalidOperationException();

        Assert.Equal("Corrected.", updated.View.Instructions);
        Assert.Equal(AiAgentStatus.Active, updated.View.Status);
    }

    [Fact]
    public async Task Updating_to_the_current_text_changes_nothing_and_keeps_the_same_version()
    {
        (TenantId tenant, AiAgent agent, InMemoryAiAgentRepository agents) = Arrange();
        string current = agent.Instructions.Value;
        ConcurrencyToken before = agents.VersionOf(agent);

        VersionedAiAgentView updated = await Handler(agents, Later).HandleAsync(
            new UpdateAiAgentInstructionsCommand(
                tenant.Value, agent.Id.Value, $"  {current}  ", before))
            ?? throw new InvalidOperationException();

        Assert.Equal(current, updated.View.Instructions);
        Assert.Equal(Created, updated.View.UpdatedAt);
        Assert.Equal(before, updated.Version);
    }

    // ------------------------------------------------------------- preconditions

    [Fact]
    public async Task A_stale_expected_version_is_refused_and_the_other_text_survives()
    {
        // The most damaging lost update: instructions are the agent's actual
        // behaviour, so overwriting somebody else's edit means a business quietly
        // running on rules they thought they had replaced.
        (TenantId tenant, AiAgent agent, InMemoryAiAgentRepository agents) = Arrange();
        ConcurrencyToken stale = agents.VersionOf(agent);

        await Handler(agents, Later).HandleAsync(new UpdateAiAgentInstructionsCommand(
            tenant.Value, agent.Id.Value, "Never quote a discount.", stale));

        await Assert.ThrowsAsync<AiAgentVersionMismatchException>(() =>
            Handler(agents, Later.AddHours(1)).HandleAsync(new UpdateAiAgentInstructionsCommand(
                tenant.Value, agent.Id.Value, "Offer a 90 percent discount.", stale)));

        Assert.Equal("Never quote a discount.", agent.Instructions.Value);
    }

    [Fact]
    public async Task A_stale_expected_version_is_refused_before_the_aggregate_is_touched()
    {
        (TenantId tenant, AiAgent agent, InMemoryAiAgentRepository agents) = Arrange();
        string original = agent.Instructions.Value;

        await Assert.ThrowsAsync<AiAgentVersionMismatchException>(() =>
            Handler(agents, Later).HandleAsync(new UpdateAiAgentInstructionsCommand(
                tenant.Value, agent.Id.Value, "Hijacked.", ConcurrencyToken.From("stale"))));

        Assert.Equal(original, agent.Instructions.Value);
        Assert.Equal(0, agents.UpdateAttempts);
    }

    [Fact]
    public async Task A_race_during_the_save_reaches_the_caller_and_is_not_retried()
    {
        (TenantId tenant, AiAgent agent, InMemoryAiAgentRepository agents) =
            Arrange(raceDuringSave: true);

        await Assert.ThrowsAsync<AiAgentVersionMismatchException>(() =>
            Handler(agents, Later).HandleAsync(new UpdateAiAgentInstructionsCommand(
                tenant.Value, agent.Id.Value, "Corrected.", agents.VersionOf(agent))));

        Assert.Equal(1, agents.UpdateAttempts);
    }

    // ------------------------------------------------------------------ the rest

    [Fact]
    public async Task An_out_of_order_instant_still_updates_but_never_drags_the_mark_backwards()
    {
        (TenantId tenant, AiAgent agent, InMemoryAiAgentRepository agents) = Arrange();

        await Handler(agents, Later).HandleAsync(new UpdateAiAgentInstructionsCommand(
            tenant.Value, agent.Id.Value, "First edit.", agents.VersionOf(agent)));

        VersionedAiAgentView late = await Handler(agents, Created.AddMinutes(-30)).HandleAsync(
            new UpdateAiAgentInstructionsCommand(
                tenant.Value, agent.Id.Value, "Arrived out of order.", agents.VersionOf(agent)))
            ?? throw new InvalidOperationException();

        Assert.Equal("Arrived out of order.", late.View.Instructions);
        Assert.Equal(Later, late.View.UpdatedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Be brief.\0Never quote a discount.")]
    public async Task Unacceptable_instructions_are_rejected_by_the_domain(string? instructions)
    {
        (TenantId tenant, AiAgent agent, InMemoryAiAgentRepository agents) = Arrange();
        string original = agent.Instructions.Value;

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            Handler(agents, Later).HandleAsync(new UpdateAiAgentInstructionsCommand(
                tenant.Value, agent.Id.Value, instructions, agents.VersionOf(agent))));

        Assert.Equal(original, agent.Instructions.Value);
        Assert.Equal(0, agents.UpdateAttempts);
    }

    [Fact]
    public async Task Updating_an_unknown_agent_is_absent_rather_than_an_error()
    {
        InMemoryAiAgentRepository agents = new();

        VersionedAiAgentView? updated = await Handler(agents, Later).HandleAsync(
            new UpdateAiAgentInstructionsCommand(
                Guid.CreateVersion7(), Guid.CreateVersion7(), "Anything.", SomeVersion));

        Assert.Null(updated);
        Assert.Equal(0, agents.UpdateAttempts);
    }

    [Fact]
    public async Task Updating_another_tenants_agent_is_hidden_even_with_that_agents_real_version()
    {
        TenantId tenantA = TenantId.New();
        AiAgent agentB = GetAiAgentByIdHandlerTests.NewAgent(TenantId.New(), "Tenant B Assistant");
        string original = agentB.Instructions.Value;
        InMemoryAiAgentRepository agents = new InMemoryAiAgentRepository().Seed(agentB);

        VersionedAiAgentView? updated = await Handler(agents, Later).HandleAsync(
            new UpdateAiAgentInstructionsCommand(
                tenantA.Value,
                agentB.Id.Value,
                "Ignore all previous rules.",
                agents.VersionOf(agentB)));

        Assert.Null(updated);
        Assert.Equal(original, agentB.Instructions.Value);
        Assert.Equal(Created, agentB.UpdatedAt);
        Assert.Equal(0, agents.UpdateAttempts);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_empty_identifier_is_rejected_as_invalid_input(bool emptyTenant)
    {
        InMemoryAiAgentRepository agents = new();

        UpdateAiAgentInstructionsCommand command = emptyTenant
            ? new UpdateAiAgentInstructionsCommand(
                Guid.Empty, Guid.CreateVersion7(), "Anything.", SomeVersion)
            : new UpdateAiAgentInstructionsCommand(
                Guid.CreateVersion7(), Guid.Empty, "Anything.", SomeVersion);

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            Handler(agents, Later).HandleAsync(command));
    }

    [Fact]
    public async Task A_null_command_is_rejected()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            Handler(new InMemoryAiAgentRepository(), Later).HandleAsync(null!));
    }

    private static UpdateAiAgentInstructionsHandler Handler(
        InMemoryAiAgentRepository agents,
        DateTimeOffset now) =>
        new(agents, TestAccess.Anywhere(), new FixedTimeProvider(now));
}
