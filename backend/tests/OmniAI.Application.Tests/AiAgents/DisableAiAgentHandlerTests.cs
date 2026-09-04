using OmniAI.Application.AiAgents;
using OmniAI.Application.AiAgents.DisableAiAgent;
using OmniAI.Application.Concurrency;
using OmniAI.Domain;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Tenants;
using OmniAI.Testing;
using static OmniAI.Application.Tests.AiAgents.RenameAiAgentHandlerTests;

namespace OmniAI.Application.Tests.AiAgents;

public sealed class DisableAiAgentHandlerTests
{
    private static readonly DateTimeOffset Created = new(2026, 5, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = new(2026, 5, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset LaterStill = new(2026, 5, 1, 11, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task An_active_agent_becomes_disabled()
    {
        (TenantId tenant, AiAgent agent, InMemoryAiAgentRepository agents) =
            Arrange(setUp: stored => stored.Activate(Later));

        ConcurrencyToken before = agents.VersionOf(agent);

        VersionedAiAgentView? disabled = await Handler(agents, LaterStill).HandleAsync(
            new DisableAiAgentCommand(tenant.Value, agent.Id.Value, before));

        Assert.NotNull(disabled);
        Assert.Equal(AiAgentStatus.Disabled, disabled.View.Status);
        Assert.Equal(LaterStill, disabled.View.UpdatedAt);
        Assert.NotEqual(before, disabled.Version);
    }

    [Fact]
    public async Task Disabling_an_already_disabled_agent_is_an_informative_no_op()
    {
        (TenantId tenant, AiAgent agent, InMemoryAiAgentRepository agents) = Arrange(
            setUp: stored =>
            {
                stored.Activate(Later);
                stored.Disable(LaterStill);
            });

        ConcurrencyToken before = agents.VersionOf(agent);

        VersionedAiAgentView again = await Handler(agents, LaterStill.AddHours(1)).HandleAsync(
            new DisableAiAgentCommand(tenant.Value, agent.Id.Value, before))
            ?? throw new InvalidOperationException();

        Assert.Equal(AiAgentStatus.Disabled, again.View.Status);
        Assert.Equal(LaterStill, again.View.UpdatedAt);
        Assert.Equal(before, again.Version);
    }

    [Fact]
    public async Task Disabling_a_draft_agent_is_refused_by_the_domain()
    {
        // The one lifecycle rule that refuses rather than shrugging, and the
        // handler lets it travel: a draft has never been switched on, and there is
        // no path back from Disabled to Draft, so quietly accepting this would
        // strand the agent.
        (TenantId tenant, AiAgent agent, InMemoryAiAgentRepository agents) = Arrange();

        DomainStateException failure = await Assert.ThrowsAsync<DomainStateException>(() =>
            Handler(agents, Later).HandleAsync(
                new DisableAiAgentCommand(tenant.Value, agent.Id.Value, agents.VersionOf(agent))));

        Assert.Contains("still a draft", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_refused_disable_writes_nothing_and_leaves_the_agent_untouched()
    {
        (TenantId tenant, AiAgent agent, InMemoryAiAgentRepository agents) = Arrange();

        await Assert.ThrowsAsync<DomainStateException>(() =>
            Handler(agents, Later).HandleAsync(
                new DisableAiAgentCommand(tenant.Value, agent.Id.Value, agents.VersionOf(agent))));

        Assert.Equal(AiAgentStatus.Draft, agent.Status);
        Assert.Equal(Created, agent.UpdatedAt);
        Assert.Equal(0, agents.UpdateAttempts);
    }

    // ------------------------------------------------------------- preconditions

    [Fact]
    public async Task A_stale_version_is_reported_before_the_draft_rule()
    {
        // Ordering that matters. A caller whose version is stale has not seen this
        // agent as it is now, so it is told that -- not told about a draft rule
        // describing a state it has never seen. Reporting the draft rule would
        // answer a question the caller did not ask and would disclose the current
        // state of a resource whose version it failed to present.
        (TenantId tenant, AiAgent agent, InMemoryAiAgentRepository agents) = Arrange();

        await Assert.ThrowsAsync<AiAgentVersionMismatchException>(() =>
            Handler(agents, Later).HandleAsync(new DisableAiAgentCommand(
                tenant.Value, agent.Id.Value, ConcurrencyToken.From("stale"))));

        Assert.Equal(AiAgentStatus.Draft, agent.Status);
        Assert.Equal(0, agents.UpdateAttempts);
    }

    [Fact]
    public async Task A_request_whose_intent_matches_the_current_state_is_still_refused_when_stale()
    {
        // Somebody else already disabled the agent, so this caller is asking for
        // something that has in effect already happened. It is still refused,
        // because a precondition is about the version the caller saw and not about
        // whether its intent happens to agree with the outcome. Accepting it would
        // mean a caller could act on a screen minutes out of date whenever it
        // guessed right.
        (TenantId tenant, AiAgent agent, InMemoryAiAgentRepository agents) =
            Arrange(setUp: stored => stored.Activate(Later));

        ConcurrencyToken stale = agents.VersionOf(agent);

        await Handler(agents, LaterStill).HandleAsync(
            new DisableAiAgentCommand(tenant.Value, agent.Id.Value, stale));

        await Assert.ThrowsAsync<AiAgentVersionMismatchException>(() =>
            Handler(agents, LaterStill.AddHours(1)).HandleAsync(
                new DisableAiAgentCommand(tenant.Value, agent.Id.Value, stale)));

        Assert.Equal(AiAgentStatus.Disabled, agent.Status);
        Assert.Equal(1, agents.UpdateAttempts);
    }

    [Fact]
    public async Task A_race_during_the_save_reaches_the_caller_and_is_not_retried()
    {
        (TenantId tenant, AiAgent agent, InMemoryAiAgentRepository agents) =
            Arrange(raceDuringSave: true, setUp: stored => stored.Activate(Later));

        await Assert.ThrowsAsync<AiAgentVersionMismatchException>(() =>
            Handler(agents, LaterStill).HandleAsync(
                new DisableAiAgentCommand(tenant.Value, agent.Id.Value, agents.VersionOf(agent))));

        Assert.Equal(1, agents.UpdateAttempts);
    }

    // ------------------------------------------------------------------ the rest

    [Fact]
    public async Task A_disabled_agent_can_be_activated_and_disabled_again()
    {
        (TenantId tenant, AiAgent agent, InMemoryAiAgentRepository agents) =
            Arrange(setUp: stored => stored.Activate(Later));

        await Handler(agents, LaterStill).HandleAsync(
            new DisableAiAgentCommand(tenant.Value, agent.Id.Value, agents.VersionOf(agent)));
        agent.Activate(LaterStill.AddHours(1));

        VersionedAiAgentView disabledAgain = await Handler(agents, LaterStill.AddHours(2))
            .HandleAsync(new DisableAiAgentCommand(
                tenant.Value, agent.Id.Value, agents.VersionOf(agent)))
            ?? throw new InvalidOperationException();

        Assert.Equal(AiAgentStatus.Disabled, disabledAgain.View.Status);
    }

    [Fact]
    public async Task Disabling_an_unknown_agent_is_absent_rather_than_an_error()
    {
        InMemoryAiAgentRepository agents = new();

        VersionedAiAgentView? disabled = await Handler(agents, Later).HandleAsync(
            new DisableAiAgentCommand(Guid.CreateVersion7(), Guid.CreateVersion7(), SomeVersion));

        Assert.Null(disabled);
        Assert.Equal(0, agents.UpdateAttempts);
    }

    [Fact]
    public async Task Disabling_another_tenants_agent_is_hidden_even_with_that_agents_real_version()
    {
        // Switching off somebody else's live agent would silently take their
        // customer service offline, so the not-found must also mean nothing
        // happened -- and the real version must not change that.
        TenantId tenantA = TenantId.New();
        AiAgent agentB = GetAiAgentByIdHandlerTests.NewAgent(TenantId.New(), "Tenant B Assistant");
        agentB.Activate(Later);

        InMemoryAiAgentRepository agents = new InMemoryAiAgentRepository().Seed(agentB);

        VersionedAiAgentView? disabled = await Handler(agents, LaterStill).HandleAsync(
            new DisableAiAgentCommand(tenantA.Value, agentB.Id.Value, agents.VersionOf(agentB)));

        Assert.Null(disabled);
        Assert.Equal(AiAgentStatus.Active, agentB.Status);
        Assert.Equal(Later, agentB.UpdatedAt);
        Assert.Equal(0, agents.UpdateAttempts);
    }

    [Fact]
    public async Task A_cross_tenant_disable_of_a_draft_is_not_found_rather_than_a_state_error()
    {
        // If the handler reported the draft rule before the ownership check, the
        // difference between that and not-found would tell an outsider both that
        // the id is real and that it is still a draft.
        TenantId tenantA = TenantId.New();
        AiAgent draftB = GetAiAgentByIdHandlerTests.NewAgent(TenantId.New(), "Tenant B Assistant");
        InMemoryAiAgentRepository agents = new InMemoryAiAgentRepository().Seed(draftB);

        VersionedAiAgentView? disabled = await Handler(agents, Later).HandleAsync(
            new DisableAiAgentCommand(tenantA.Value, draftB.Id.Value, agents.VersionOf(draftB)));

        Assert.Null(disabled);
        Assert.Equal(AiAgentStatus.Draft, draftB.Status);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_empty_identifier_is_rejected_as_invalid_input(bool emptyTenant)
    {
        InMemoryAiAgentRepository agents = new();

        DisableAiAgentCommand command = emptyTenant
            ? new DisableAiAgentCommand(Guid.Empty, Guid.CreateVersion7(), SomeVersion)
            : new DisableAiAgentCommand(Guid.CreateVersion7(), Guid.Empty, SomeVersion);

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            Handler(agents, Later).HandleAsync(command));
    }

    [Fact]
    public async Task A_null_command_is_rejected()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            Handler(new InMemoryAiAgentRepository(), Later).HandleAsync(null!));
    }

    private static DisableAiAgentHandler Handler(
        InMemoryAiAgentRepository agents,
        DateTimeOffset now) =>
        new(agents, new FixedTimeProvider(now));
}
