using OmniAI.Application.AiAgents;
using OmniAI.Application.AiAgents.RenameAiAgent;
using OmniAI.Application.Concurrency;
using OmniAI.Domain;
using OmniAI.Application.Tests.Security;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Tenants;
using OmniAI.Testing;

namespace OmniAI.Application.Tests.AiAgents;

public sealed class RenameAiAgentHandlerTests
{
    private static readonly DateTimeOffset Created = new(2026, 5, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = new(2026, 5, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Renaming_with_the_current_version_returns_the_updated_representation()
    {
        (TenantId tenant, AiAgent agent, InMemoryAiAgentRepository agents) = Arrange();

        VersionedAiAgentView? renamed = await Handler(agents, Later).HandleAsync(
            new RenameAiAgentCommand(
                tenant.Value, agent.Id.Value, "Pre-sales Assistant", agents.VersionOf(agent)));

        Assert.NotNull(renamed);
        Assert.Equal("Pre-sales Assistant", renamed.View.Name);
        Assert.Equal("Pre-sales Assistant", agent.Name.Value);
    }

    [Fact]
    public async Task A_successful_rename_returns_a_new_version()
    {
        // The caller needs the new version to make its next edit without reading
        // the agent back.
        (TenantId tenant, AiAgent agent, InMemoryAiAgentRepository agents) = Arrange();
        ConcurrencyToken before = agents.VersionOf(agent);

        VersionedAiAgentView renamed = await Handler(agents, Later).HandleAsync(
            new RenameAiAgentCommand(tenant.Value, agent.Id.Value, "Renamed", before))
            ?? throw new InvalidOperationException();

        Assert.NotEqual(before, renamed.Version);
        Assert.Equal(agents.VersionOf(agent), renamed.Version);
    }

    [Fact]
    public async Task Renaming_advances_the_update_mark_to_the_clock_instant()
    {
        (TenantId tenant, AiAgent agent, InMemoryAiAgentRepository agents) = Arrange();

        VersionedAiAgentView renamed = await Handler(agents, Later).HandleAsync(
            new RenameAiAgentCommand(tenant.Value, agent.Id.Value, "Renamed", agents.VersionOf(agent)))
            ?? throw new InvalidOperationException();

        Assert.Equal(Later, renamed.View.UpdatedAt);
        Assert.Equal(Created, renamed.View.CreatedAt);
    }

    [Fact]
    public async Task Renaming_to_the_current_name_changes_nothing_and_keeps_the_same_version()
    {
        // Nothing was written, so there is no new version to report -- exactly as
        // PostgreSQL leaves xmin alone when no row is written.
        (TenantId tenant, AiAgent agent, InMemoryAiAgentRepository agents) = Arrange();
        ConcurrencyToken before = agents.VersionOf(agent);

        VersionedAiAgentView renamed = await Handler(agents, Later).HandleAsync(
            new RenameAiAgentCommand(tenant.Value, agent.Id.Value, "  Sales Assistant  ", before))
            ?? throw new InvalidOperationException();

        Assert.Equal("Sales Assistant", renamed.View.Name);
        Assert.Equal(Created, renamed.View.UpdatedAt);
        Assert.Equal(before, renamed.Version);
    }

    // ------------------------------------------------------------- preconditions

    [Fact]
    public async Task A_stale_expected_version_is_refused_and_nothing_is_written()
    {
        // The lost update this mechanism exists to prevent, at the use-case level:
        // somebody else has already changed the agent since this caller read it.
        (TenantId tenant, AiAgent agent, InMemoryAiAgentRepository agents) = Arrange();
        ConcurrencyToken stale = agents.VersionOf(agent);

        // Another writer moves the agent on.
        await Handler(agents, Later).HandleAsync(
            new RenameAiAgentCommand(tenant.Value, agent.Id.Value, "Renamed by somebody else", stale));

        AiAgentVersionMismatchException failure =
            await Assert.ThrowsAsync<AiAgentVersionMismatchException>(() =>
                Handler(agents, Later.AddHours(1)).HandleAsync(
                    new RenameAiAgentCommand(
                        tenant.Value, agent.Id.Value, "Renamed from a stale screen", stale)));

        Assert.Equal(agent.Id.Value, failure.AiAgentId);

        // The other writer's change survived.
        Assert.Equal("Renamed by somebody else", agent.Name.Value);
        Assert.Equal(Later, agent.UpdatedAt);
    }

    [Fact]
    public async Task A_stale_expected_version_is_refused_before_the_aggregate_is_touched()
    {
        // Not merely "the save failed": the precondition is checked first, so the
        // aggregate is never mutated and no write is attempted.
        (TenantId tenant, AiAgent agent, InMemoryAiAgentRepository agents) = Arrange();

        await Assert.ThrowsAsync<AiAgentVersionMismatchException>(() =>
            Handler(agents, Later).HandleAsync(new RenameAiAgentCommand(
                tenant.Value, agent.Id.Value, "Hijacked", ConcurrencyToken.From("not-the-current-version"))));

        Assert.Equal("Sales Assistant", agent.Name.Value);
        Assert.Equal(Created, agent.UpdatedAt);
        Assert.Equal(0, agents.UpdateAttempts);
    }

    [Fact]
    public async Task A_race_during_the_save_reaches_the_caller_and_is_not_retried()
    {
        // The store reports a mismatch even though the expected version looked
        // current when it was read. The handler must not swallow it, and must not
        // replay the write against state the caller has never seen.
        (TenantId tenant, AiAgent agent, InMemoryAiAgentRepository agents) =
            Arrange(raceDuringSave: true);

        await Assert.ThrowsAsync<AiAgentVersionMismatchException>(() =>
            Handler(agents, Later).HandleAsync(new RenameAiAgentCommand(
                tenant.Value, agent.Id.Value, "Renamed", agents.VersionOf(agent))));

        Assert.Equal(1, agents.UpdateAttempts);
    }

    // ------------------------------------------------------------------ the rest

    [Fact]
    public async Task An_out_of_order_instant_still_renames_but_never_drags_the_mark_backwards()
    {
        // The accepted high-water-mark rule, exercised through the use case.
        (TenantId tenant, AiAgent agent, InMemoryAiAgentRepository agents) = Arrange();

        await Handler(agents, Later).HandleAsync(new RenameAiAgentCommand(
            tenant.Value, agent.Id.Value, "Renamed once", agents.VersionOf(agent)));

        VersionedAiAgentView late = await Handler(agents, Created.AddMinutes(-30)).HandleAsync(
            new RenameAiAgentCommand(
                tenant.Value, agent.Id.Value, "Arrived out of order", agents.VersionOf(agent)))
            ?? throw new InvalidOperationException();

        Assert.Equal("Arrived out of order", late.View.Name);
        Assert.Equal(Later, late.View.UpdatedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Sales\tAssistant")]
    public async Task An_unacceptable_name_is_rejected_by_the_domain(string? name)
    {
        (TenantId tenant, AiAgent agent, InMemoryAiAgentRepository agents) = Arrange();

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            Handler(agents, Later).HandleAsync(
                new RenameAiAgentCommand(tenant.Value, agent.Id.Value, name, agents.VersionOf(agent))));

        Assert.Equal("Sales Assistant", agent.Name.Value);
        Assert.Equal(0, agents.UpdateAttempts);
    }

    [Fact]
    public async Task Renaming_an_unknown_agent_is_absent_rather_than_an_error()
    {
        InMemoryAiAgentRepository agents = new();

        VersionedAiAgentView? renamed = await Handler(agents, Later).HandleAsync(
            new RenameAiAgentCommand(
                Guid.CreateVersion7(), Guid.CreateVersion7(), "Renamed", SomeVersion));

        Assert.Null(renamed);
        Assert.Equal(0, agents.UpdateAttempts);
    }

    [Fact]
    public async Task Renaming_another_tenants_agent_is_hidden_and_changes_nothing()
    {
        // The mutation half of the isolation rule, and the important half: a
        // cross-tenant write must not merely be reported as not-found, it must not
        // happen.
        TenantId tenantA = TenantId.New();
        AiAgent agentB = GetAiAgentByIdHandlerTests.NewAgent(TenantId.New(), "Tenant B Assistant");
        InMemoryAiAgentRepository agents = new InMemoryAiAgentRepository().Seed(agentB);

        VersionedAiAgentView? renamed = await Handler(agents, Later).HandleAsync(
            new RenameAiAgentCommand(tenantA.Value, agentB.Id.Value, "Hijacked", SomeVersion));

        Assert.Null(renamed);
        Assert.Equal("Tenant B Assistant", agentB.Name.Value);
        Assert.Equal(Created, agentB.UpdatedAt);
        Assert.Equal(0, agents.UpdateAttempts);
    }

    [Fact]
    public async Task Another_tenants_agent_is_hidden_even_with_that_agents_real_version()
    {
        // A version marker is not an access path. Knowing the exact current
        // version of somebody else's agent must not make it reachable, and must
        // not produce a different answer from an invented one either.
        TenantId tenantA = TenantId.New();
        AiAgent agentB = GetAiAgentByIdHandlerTests.NewAgent(TenantId.New(), "Tenant B Assistant");
        InMemoryAiAgentRepository agents = new InMemoryAiAgentRepository().Seed(agentB);

        VersionedAiAgentView? withRealVersion = await Handler(agents, Later).HandleAsync(
            new RenameAiAgentCommand(
                tenantA.Value, agentB.Id.Value, "Hijacked", agents.VersionOf(agentB)));

        VersionedAiAgentView? withInventedVersion = await Handler(agents, Later).HandleAsync(
            new RenameAiAgentCommand(tenantA.Value, agentB.Id.Value, "Hijacked", SomeVersion));

        Assert.Null(withRealVersion);
        Assert.Equal(withInventedVersion, withRealVersion);
        Assert.Equal("Tenant B Assistant", agentB.Name.Value);
        Assert.Equal(0, agents.UpdateAttempts);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_empty_identifier_is_rejected_as_invalid_input(bool emptyTenant)
    {
        InMemoryAiAgentRepository agents = new();

        RenameAiAgentCommand command = emptyTenant
            ? new RenameAiAgentCommand(Guid.Empty, Guid.CreateVersion7(), "Renamed", SomeVersion)
            : new RenameAiAgentCommand(Guid.CreateVersion7(), Guid.Empty, "Renamed", SomeVersion);

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            Handler(agents, Later).HandleAsync(command));
    }

    [Fact]
    public async Task A_null_command_is_rejected()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            Handler(new InMemoryAiAgentRepository(), Later).HandleAsync(null!));
    }

    /// <summary>A version this fake never issues, for cases where none is relevant.</summary>
    internal static ConcurrencyToken SomeVersion => ConcurrencyToken.From("some-other-version");

    /// <summary>
    /// A tenant, an agent and a store holding it.
    /// </summary>
    /// <param name="raceDuringSave">Make the store report a race on the next write.</param>
    /// <param name="setUp">
    /// Lifecycle set-up applied <em>before</em> the agent is stored. It runs first
    /// on purpose: a real database already holds whatever state the test is
    /// arranging, so its version reflects that state. Mutating the aggregate after
    /// storing it would leave the store's idea of the row one version behind, and
    /// the tests that assert an unchanged version would fail for a reason that
    /// exists only in the fake.
    /// </param>
    internal static (TenantId Tenant, AiAgent Agent, InMemoryAiAgentRepository Agents) Arrange(
        bool raceDuringSave = false,
        Action<AiAgent>? setUp = null)
    {
        TenantId tenant = TenantId.New();
        AiAgent agent = GetAiAgentByIdHandlerTests.NewAgent(tenant, "Sales Assistant");

        setUp?.Invoke(agent);

        InMemoryAiAgentRepository agents =
            new InMemoryAiAgentRepository { PretendRaceDuringSave = raceDuringSave }.Seed(agent);

        return (tenant, agent, agents);
    }

    private static RenameAiAgentHandler Handler(
        InMemoryAiAgentRepository agents,
        DateTimeOffset now) =>
        new(agents, TestAccess.Anywhere(), new FixedTimeProvider(now));
}
