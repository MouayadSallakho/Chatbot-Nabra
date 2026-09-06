using OmniAI.Application.AiAgents;
using OmniAI.Application.AiAgents.ActivateAiAgent;
using OmniAI.Application.Concurrency;
using OmniAI.Domain;
using OmniAI.Application.Tests.Security;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Tenants;
using OmniAI.Testing;
using static OmniAI.Application.Tests.AiAgents.RenameAiAgentHandlerTests;

namespace OmniAI.Application.Tests.AiAgents;

public sealed class ActivateAiAgentHandlerTests
{
    private static readonly DateTimeOffset Created = new(2026, 5, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = new(2026, 5, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset LaterStill = new(2026, 5, 1, 11, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_draft_agent_becomes_active()
    {
        (TenantId tenant, AiAgent agent, InMemoryAiAgentRepository agents) = Arrange();
        ConcurrencyToken before = agents.VersionOf(agent);

        Versioned<AiAgentView>? activated = await Handler(agents, Later).HandleAsync(
            new ActivateAiAgentCommand(tenant.Value, agent.Id.Value, before));

        Assert.NotNull(activated);
        Assert.Equal(AiAgentStatus.Active, activated.Value.Status);
        Assert.Equal(Later, activated.Value.UpdatedAt);
        Assert.NotEqual(before, activated.Version);
    }

    [Fact]
    public async Task Activation_requires_nothing_beyond_the_agent_itself()
    {
        // The accepted Phase 6 decision, exercised at the use case level: a
        // freshly created agent is always activatable, because it cannot exist
        // through public Domain APIs with an invalid name or invalid instructions.
        // No provider, model, channel, tool or credential is consulted -- none of
        // those concepts exists, and no stub stands in for them.
        (TenantId tenant, AiAgent agent, InMemoryAiAgentRepository agents) = Arrange();

        Versioned<AiAgentView> activated = await Handler(agents, Later).HandleAsync(
            new ActivateAiAgentCommand(tenant.Value, agent.Id.Value, agents.VersionOf(agent)))
            ?? throw new InvalidOperationException();

        Assert.Equal(AiAgentStatus.Active, activated.Value.Status);
    }

    [Fact]
    public async Task A_disabled_agent_can_be_activated_again()
    {
        (TenantId tenant, AiAgent agent, InMemoryAiAgentRepository agents) = Arrange(
            setUp: stored =>
            {
                stored.Activate(Created.AddMinutes(1));
                stored.Disable(Created.AddMinutes(2));
            });

        Versioned<AiAgentView> reactivated = await Handler(agents, LaterStill).HandleAsync(
            new ActivateAiAgentCommand(tenant.Value, agent.Id.Value, agents.VersionOf(agent)))
            ?? throw new InvalidOperationException();

        Assert.Equal(AiAgentStatus.Active, reactivated.Value.Status);
        Assert.Equal(LaterStill, reactivated.Value.UpdatedAt);
    }

    [Fact]
    public async Task Activating_an_already_active_agent_is_an_informative_no_op()
    {
        // Idempotent, and the response says so: the status is what the caller
        // asked for, the update mark did not move, and neither did the version --
        // the truthful account of a request that changed nothing.
        (TenantId tenant, AiAgent agent, InMemoryAiAgentRepository agents) =
            Arrange(setUp: stored => stored.Activate(Later));

        ConcurrencyToken before = agents.VersionOf(agent);

        Versioned<AiAgentView> again = await Handler(agents, LaterStill).HandleAsync(
            new ActivateAiAgentCommand(tenant.Value, agent.Id.Value, before))
            ?? throw new InvalidOperationException();

        Assert.Equal(AiAgentStatus.Active, again.Value.Status);
        Assert.Equal(Later, again.Value.UpdatedAt);
        Assert.Equal(before, again.Version);
    }

    // ------------------------------------------------------------- preconditions

    [Fact]
    public async Task An_idempotent_call_with_a_stale_version_is_still_refused()
    {
        // The case the database alone can never catch. An already-active agent
        // writes nothing, so no statement is issued and no predicate can fail --
        // and without an explicit check a caller holding a hopelessly out-of-date
        // version would get a cheerful success. A precondition is about the
        // resource, not about whether the request would have changed it.
        (TenantId tenant, AiAgent agent, InMemoryAiAgentRepository agents) = Arrange();
        ConcurrencyToken stale = agents.VersionOf(agent);

        // Somebody else moves the agent on, so the caller's version is now old.
        await Handler(agents, Later).HandleAsync(
            new ActivateAiAgentCommand(tenant.Value, agent.Id.Value, stale));

        await Assert.ThrowsAsync<AiAgentVersionMismatchException>(() =>
            Handler(agents, LaterStill).HandleAsync(
                new ActivateAiAgentCommand(tenant.Value, agent.Id.Value, stale)));

        Assert.Equal(1, agents.UpdateAttempts);
    }

    [Fact]
    public async Task A_stale_expected_version_is_refused_and_nothing_is_written()
    {
        (TenantId tenant, AiAgent agent, InMemoryAiAgentRepository agents) = Arrange();

        await Assert.ThrowsAsync<AiAgentVersionMismatchException>(() =>
            Handler(agents, Later).HandleAsync(new ActivateAiAgentCommand(
                tenant.Value, agent.Id.Value, ConcurrencyToken.From("stale"))));

        Assert.Equal(AiAgentStatus.Draft, agent.Status);
        Assert.Equal(Created, agent.UpdatedAt);
        Assert.Equal(0, agents.UpdateAttempts);
    }

    [Fact]
    public async Task A_race_during_the_save_reaches_the_caller_and_is_not_retried()
    {
        (TenantId tenant, AiAgent agent, InMemoryAiAgentRepository agents) =
            Arrange(raceDuringSave: true);

        await Assert.ThrowsAsync<AiAgentVersionMismatchException>(() =>
            Handler(agents, Later).HandleAsync(
                new ActivateAiAgentCommand(tenant.Value, agent.Id.Value, agents.VersionOf(agent))));

        Assert.Equal(1, agents.UpdateAttempts);
    }

    // ------------------------------------------------------------------ the rest

    [Fact]
    public async Task Activating_an_unknown_agent_is_absent_rather_than_an_error()
    {
        InMemoryAiAgentRepository agents = new();

        Versioned<AiAgentView>? activated = await Handler(agents, Later).HandleAsync(
            new ActivateAiAgentCommand(Guid.CreateVersion7(), Guid.CreateVersion7(), SomeVersion));

        Assert.Null(activated);
        Assert.Equal(0, agents.UpdateAttempts);
    }

    [Fact]
    public async Task Activating_another_tenants_agent_is_hidden_even_with_that_agents_real_version()
    {
        // Switching on somebody else's agent would put it in front of their
        // customers, so this is the isolation case with the most direct
        // consequence -- and knowing the exact version must not help.
        TenantId tenantA = TenantId.New();
        AiAgent agentB = GetAiAgentByIdHandlerTests.NewAgent(TenantId.New(), "Tenant B Assistant");
        InMemoryAiAgentRepository agents = new InMemoryAiAgentRepository().Seed(agentB);

        Versioned<AiAgentView>? activated = await Handler(agents, Later).HandleAsync(
            new ActivateAiAgentCommand(tenantA.Value, agentB.Id.Value, agents.VersionOf(agentB)));

        Assert.Null(activated);
        Assert.Equal(AiAgentStatus.Draft, agentB.Status);
        Assert.Equal(Created, agentB.UpdatedAt);
        Assert.Equal(0, agents.UpdateAttempts);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_empty_identifier_is_rejected_as_invalid_input(bool emptyTenant)
    {
        InMemoryAiAgentRepository agents = new();

        ActivateAiAgentCommand command = emptyTenant
            ? new ActivateAiAgentCommand(Guid.Empty, Guid.CreateVersion7(), SomeVersion)
            : new ActivateAiAgentCommand(Guid.CreateVersion7(), Guid.Empty, SomeVersion);

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            Handler(agents, Later).HandleAsync(command));
    }

    [Fact]
    public async Task A_null_command_is_rejected()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            Handler(new InMemoryAiAgentRepository(), Later).HandleAsync(null!));
    }

    private static ActivateAiAgentHandler Handler(
        InMemoryAiAgentRepository agents,
        DateTimeOffset now) =>
        new(agents, TestAuthority.Anywhere(), new FixedTimeProvider(now));
}
