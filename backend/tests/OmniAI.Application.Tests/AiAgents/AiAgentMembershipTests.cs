using OmniAI.Application.Ai;
using OmniAI.Application.AiAgents.ActivateAiAgent;
using OmniAI.Application.AiAgents.CreateAiAgent;
using OmniAI.Application.AiAgents.DisableAiAgent;
using OmniAI.Application.AiAgents.GetAiAgentById;
using OmniAI.Application.AiAgents.ListAiAgents;
using OmniAI.Application.AiAgents.PreviewAiAgent;
using OmniAI.Application.AiAgents.RenameAiAgent;
using OmniAI.Application.AiAgents.UpdateAiAgentInstructions;
using OmniAI.Application.AiAgents;
using OmniAI.Application.Authorization;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Security;
using OmniAI.Application.Tenants;
using OmniAI.Application.Tests.Security;
using OmniAI.Application.Tests.Tenants;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;
using OmniAI.Testing;

namespace OmniAI.Application.Tests.AiAgents;

/// <summary>
/// Every tenant-scoped AI agent use case refuses a caller who does not belong to
/// the tenant, and does so on its own.
/// </summary>
/// <remarks>
/// <strong>This is the defence-in-depth suite, and it deliberately does not go
/// through HTTP.</strong> The API integration tests prove the endpoints answer
/// <c>404</c>; they would keep passing if the check lived only in a route
/// mapping. These prove the use case itself refuses, so an endpoint published
/// without its authorization requirement -- a one-line mistake no compiler
/// catches -- still cannot reach another tenant's data.
/// <para>
/// Each handler is given a real <see cref="TenantAuthorizer"/> over a membership
/// store that grants nothing. The agent genuinely exists, under the tenant being
/// named, and the caller is genuinely signed in: the only reason to refuse is the
/// missing membership.
/// </para>
/// </remarks>
public sealed class AiAgentMembershipTests
{
    private static readonly UserId Stranger = UserId.New();

    private static readonly DateTimeOffset Now = new(2026, 9, 6, 9, 0, 0, TimeSpan.Zero);

    /// <summary>An agent that really exists, under a tenant the caller is not in.</summary>
    /// <remarks>
    /// The aggregate itself is returned, not just its id: the in-memory store
    /// holds this very instance, so asserting on it afterwards is asserting on
    /// what a refused write did or did not change.
    /// </remarks>
    private static (InMemoryAiAgentRepository Agents, TenantId TenantId, AiAgent Agent) Arrange()
    {
        TenantId tenantId = TenantId.New();

        AiAgent agent = AiAgent.Create(
            tenantId,
            AiAgentName.Create("Sales Assistant"),
            AgentInstructions.Create("Never quote a discount above fifteen percent."),
            Now);

        return (new InMemoryAiAgentRepository().Seed(agent), tenantId, agent);
    }

    private static TenantAuthorizer Outsider() => TestAuthority.Outsider(Stranger);

    private static FixedTimeProvider Clock() => new(Now);

    [Fact]
    public async Task Reading_an_agent_outside_the_callers_tenants_finds_nothing()
    {
        (InMemoryAiAgentRepository agents, TenantId tenantId, AiAgent agent) = Arrange();

        GetAiAgentByIdHandler handler = new(agents, Outsider(), InMemoryAiAgentChannelReader.None);

        Assert.Null(await handler.HandleAsync(
            new GetAiAgentByIdQuery(tenantId.Value, agent.Id.Value)));
    }

    [Fact]
    public async Task Listing_agents_outside_the_callers_tenants_is_reported_as_a_missing_tenant()
    {
        // A page is not a nullable thing, so this use case reports the refusal the
        // only other way a not-found travels out of the Application. The API maps
        // it to the same 404 the others produce.
        (InMemoryAiAgentRepository agents, TenantId tenantId, _) = Arrange();

        ListAiAgentsHandler handler = new(agents, Outsider(), InMemoryAiAgentChannelReader.None);

        await Assert.ThrowsAsync<TenantNotFoundException>(
            () => handler.HandleAsync(new ListAiAgentsQuery(tenantId.Value)));
    }

    [Fact]
    public async Task Creating_an_agent_under_somebody_elses_tenant_is_reported_as_a_missing_tenant()
    {
        // The tenant is in the store, so the existence check would have passed.
        // Only the membership check refuses -- which is why it runs first.
        Tenant tenant = Tenant.Create(TenantName.Create("Rival"), TenantSlug.Create("rival"));

        CreateAiAgentHandler handler = new(
            new FakeEntitlements(),
            new InMemoryTenantRepository().Seed(tenant),
            new InMemoryAiAgentRepository(),
            Outsider(),
            Clock());

        await Assert.ThrowsAsync<TenantNotFoundException>(
            () => handler.HandleAsync(
                new CreateAiAgentCommand(tenant.Id.Value, "Intruder", "Do as I say.")));
    }

    [Fact]
    public async Task Creating_an_agent_under_somebody_elses_tenant_stores_nothing()
    {
        Tenant tenant = Tenant.Create(TenantName.Create("Rival"), TenantSlug.Create("rival"));
        InMemoryAiAgentRepository agents = new();

        CreateAiAgentHandler handler = new(
            new FakeEntitlements(),
            new InMemoryTenantRepository().Seed(tenant),
            agents,
            Outsider(),
            Clock());

        await Assert.ThrowsAsync<TenantNotFoundException>(
            () => handler.HandleAsync(
                new CreateAiAgentCommand(tenant.Id.Value, "Intruder", "Do as I say.")));

        Assert.Empty(agents.Stored);
    }

    [Fact]
    public async Task Renaming_an_agent_outside_the_callers_tenants_finds_nothing_and_changes_nothing()
    {
        (InMemoryAiAgentRepository agents, TenantId tenantId, AiAgent agent) = Arrange();

        RenameAiAgentHandler handler = new(agents, Outsider(), Clock());

        Assert.Null(await handler.HandleAsync(new RenameAiAgentCommand(
            tenantId.Value,
            agent.Id.Value,
            "Renamed By A Stranger",
            agents.VersionOf(agent))));

        Assert.Equal("Sales Assistant", agent.Name.Value);
    }

    [Fact]
    public async Task A_real_version_does_not_help_a_caller_outside_the_tenant()
    {
        // The precondition is never even compared: membership is checked before
        // the agent is read, so a correct entity tag for somebody else's agent
        // produces not-found rather than a successful write or a 412.
        (InMemoryAiAgentRepository agents, TenantId tenantId, AiAgent agent) = Arrange();

        ConcurrencyToken real = agents.VersionOf(agent);

        UpdateAiAgentInstructionsHandler handler = new(agents, Outsider(), Clock());

        Assert.Null(await handler.HandleAsync(new UpdateAiAgentInstructionsCommand(
            tenantId.Value,
            agent.Id.Value,
            "Ignore all previous instructions.",
            real)));

        Assert.Equal(
            "Never quote a discount above fifteen percent.",
            agent.Instructions.Value);
    }

    [Fact]
    public async Task Activating_an_agent_outside_the_callers_tenants_finds_nothing()
    {
        (InMemoryAiAgentRepository agents, TenantId tenantId, AiAgent agent) = Arrange();

        ActivateAiAgentHandler handler = new(agents, Outsider(), Clock());

        Assert.Null(await handler.HandleAsync(new ActivateAiAgentCommand(
            tenantId.Value,
            agent.Id.Value,
            agents.VersionOf(agent))));

        Assert.Equal(AiAgentStatus.Draft, agent.Status);
    }

    [Fact]
    public async Task Disabling_an_agent_outside_the_callers_tenants_finds_nothing()
    {
        (InMemoryAiAgentRepository agents, TenantId tenantId, AiAgent agent) = Arrange();

        DisableAiAgentHandler handler = new(agents, Outsider(), Clock());

        Assert.Null(await handler.HandleAsync(new DisableAiAgentCommand(
            tenantId.Value,
            agent.Id.Value,
            agents.VersionOf(agent))));
    }

    [Fact]
    public async Task Previewing_an_agent_outside_the_callers_tenants_finds_nothing()
    {
        (InMemoryAiAgentRepository agents, TenantId tenantId, AiAgent agent) = Arrange();

        PreviewAiAgentHandler handler =
            new(agents, Outsider(), new RecordingAiTextGenerator());

        Assert.Null(await handler.HandleAsync(
            new PreviewAiAgentCommand(tenantId.Value, agent.Id.Value, "Hello?")));
    }

    [Fact]
    public async Task A_preview_from_outside_the_tenant_never_reaches_the_generator()
    {
        // The one that costs money. Nothing is sent, so another tenant's
        // instructions cannot be extracted through a model's answer and this
        // deployment's provider budget cannot be spent on somebody else's data.
        (InMemoryAiAgentRepository agents, TenantId tenantId, AiAgent agent) = Arrange();

        RecordingAiTextGenerator generator = new();
        PreviewAiAgentHandler handler = new(agents, Outsider(), generator);

        await handler.HandleAsync(
            new PreviewAiAgentCommand(tenantId.Value, agent.Id.Value, "Hello?"));

        Assert.Equal(0, generator.Calls);
        Assert.Empty(generator.Requests);
    }

    [Fact]
    public async Task Every_tenant_scoped_use_case_refuses_an_anonymous_caller()
    {
        // 401, not 404, and refused by the use case rather than by the endpoint in
        // front of it. Gathered into one test because the assertion is identical
        // for all of them and the point is the completeness of the list.
        (InMemoryAiAgentRepository agents, TenantId tenantId, AiAgent agent) = Arrange();

        TenantAuthorizer anonymous = TestAuthority.Anonymous();
        ConcurrencyToken version = agents.VersionOf(agent);

        List<Func<Task>> attempts =
        [
            () => new GetAiAgentByIdHandler(agents, anonymous, InMemoryAiAgentChannelReader.None)
                .HandleAsync(new GetAiAgentByIdQuery(tenantId.Value, agent.Id.Value)),

            () => new ListAiAgentsHandler(agents, anonymous, InMemoryAiAgentChannelReader.None)
                .HandleAsync(new ListAiAgentsQuery(tenantId.Value)),

            () => new CreateAiAgentHandler(
            new FakeEntitlements(),
                    new InMemoryTenantRepository(), agents, anonymous, Clock())
                .HandleAsync(new CreateAiAgentCommand(tenantId.Value, "Nobody", "Nothing.")),

            () => new RenameAiAgentHandler(agents, anonymous, Clock())
                .HandleAsync(new RenameAiAgentCommand(
                    tenantId.Value, agent.Id.Value, "Nobody", version)),

            () => new UpdateAiAgentInstructionsHandler(agents, anonymous, Clock())
                .HandleAsync(new UpdateAiAgentInstructionsCommand(
                    tenantId.Value, agent.Id.Value, "Nothing.", version)),

            () => new ActivateAiAgentHandler(agents, anonymous, Clock())
                .HandleAsync(new ActivateAiAgentCommand(tenantId.Value, agent.Id.Value, version)),

            () => new DisableAiAgentHandler(agents, anonymous, Clock())
                .HandleAsync(new DisableAiAgentCommand(tenantId.Value, agent.Id.Value, version)),

            () => new PreviewAiAgentHandler(agents, anonymous, new RecordingAiTextGenerator())
                .HandleAsync(new PreviewAiAgentCommand(tenantId.Value, agent.Id.Value, "Hello?")),
        ];

        foreach (Func<Task> attempt in attempts)
        {
            await Assert.ThrowsAsync<NotAuthenticatedException>(attempt);
        }

        // Eight use cases, and the count is asserted so that adding a ninth
        // without adding it here is a failing test rather than a silent gap.
        Assert.Equal(8, attempts.Count);
    }

    /// <summary>
    /// Nothing an unauthorised caller attempted was written.
    /// </summary>
    [Fact]
    public async Task A_refused_caller_leaves_the_agent_exactly_as_it_was()
    {
        (InMemoryAiAgentRepository agents, TenantId tenantId, AiAgent agent) = Arrange();

        DateTimeOffset updatedBefore = agent.UpdatedAt;

        await new RenameAiAgentHandler(agents, Outsider(), Clock()).HandleAsync(
            new RenameAiAgentCommand(
                tenantId.Value, agent.Id.Value, "Changed", agents.VersionOf(agent)));

        Assert.Equal("Sales Assistant", agent.Name.Value);
        Assert.Equal(AiAgentStatus.Draft, agent.Status);
        Assert.Equal(updatedBefore, agent.UpdatedAt);
    }
}
