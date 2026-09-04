using OmniAI.Application.AiAgents;
using OmniAI.Application.AiAgents.GetAiAgentById;
using OmniAI.Domain;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Tests.AiAgents;

public sealed class GetAiAgentByIdHandlerTests
{
    private static readonly DateTimeOffset Created = new(2026, 5, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task An_agent_in_the_tenant_is_returned()
    {
        TenantId tenant = TenantId.New();
        AiAgent agent = NewAgent(tenant, "Sales Assistant");
        InMemoryAiAgentRepository agents = new InMemoryAiAgentRepository().Seed(agent);

        VersionedAiAgentView? found = await new GetAiAgentByIdHandler(agents)
            .HandleAsync(new GetAiAgentByIdQuery(tenant.Value, agent.Id.Value));

        Assert.NotNull(found);
        Assert.Equal(agent.Id.Value, found.View.Id);
        Assert.Equal("Sales Assistant", found.View.Name);
        Assert.Equal(tenant.Value, found.View.TenantId);
    }

    [Fact]
    public async Task Every_modelled_field_is_projected()
    {
        TenantId tenant = TenantId.New();
        AiAgent agent = NewAgent(tenant, "Sales Assistant");
        agent.Activate(Created.AddHours(1));
        InMemoryAiAgentRepository agents = new InMemoryAiAgentRepository().Seed(agent);

        VersionedAiAgentView found = await new GetAiAgentByIdHandler(agents)
            .HandleAsync(new GetAiAgentByIdQuery(tenant.Value, agent.Id.Value))
            ?? throw new InvalidOperationException();

        Assert.Equal(agent.Instructions.Value, found.View.Instructions);
        Assert.Equal(AiAgentStatus.Active, found.View.Status);
        Assert.Equal(Created, found.View.CreatedAt);
        Assert.Equal(Created.AddHours(1), found.View.UpdatedAt);
    }

    [Fact]
    public async Task An_unknown_agent_is_absent_rather_than_an_error()
    {
        InMemoryAiAgentRepository agents = new();

        VersionedAiAgentView? found = await new GetAiAgentByIdHandler(agents)
            .HandleAsync(new GetAiAgentByIdQuery(Guid.CreateVersion7(), Guid.CreateVersion7()));

        Assert.Null(found);
    }

    [Fact]
    public async Task An_agent_belonging_to_another_tenant_is_hidden()
    {
        // The isolation rule at the use-case level: knowing a real agent id is not
        // enough, because the tenant in the query has to own it.
        TenantId tenantA = TenantId.New();
        TenantId tenantB = TenantId.New();
        AiAgent agentB = NewAgent(tenantB, "Tenant B Assistant");
        InMemoryAiAgentRepository agents = new InMemoryAiAgentRepository().Seed(agentB);

        VersionedAiAgentView? found = await new GetAiAgentByIdHandler(agents)
            .HandleAsync(new GetAiAgentByIdQuery(tenantA.Value, agentB.Id.Value));

        Assert.Null(found);
    }

    [Fact]
    public async Task A_hidden_agent_is_indistinguishable_from_one_that_does_not_exist()
    {
        // Both answers are the same value, so no caller can tell the two apart --
        // which is what stops this endpoint confirming that an id is real
        // somewhere else in the platform.
        TenantId tenantA = TenantId.New();
        AiAgent agentB = NewAgent(TenantId.New(), "Tenant B Assistant");
        InMemoryAiAgentRepository agents = new InMemoryAiAgentRepository().Seed(agentB);

        GetAiAgentByIdHandler handler = new(agents);

        VersionedAiAgentView? crossTenant = await handler
            .HandleAsync(new GetAiAgentByIdQuery(tenantA.Value, agentB.Id.Value));

        VersionedAiAgentView? nonExistent = await handler
            .HandleAsync(new GetAiAgentByIdQuery(tenantA.Value, Guid.CreateVersion7()));

        Assert.Equal(nonExistent, crossTenant);
        Assert.Null(crossTenant);
    }

    [Fact]
    public async Task An_empty_tenant_id_is_rejected_as_invalid_input()
    {
        InMemoryAiAgentRepository agents = new();

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            new GetAiAgentByIdHandler(agents)
                .HandleAsync(new GetAiAgentByIdQuery(Guid.Empty, Guid.CreateVersion7())));
    }

    [Fact]
    public async Task An_empty_agent_id_is_rejected_as_invalid_input()
    {
        InMemoryAiAgentRepository agents = new();

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            new GetAiAgentByIdHandler(agents)
                .HandleAsync(new GetAiAgentByIdQuery(Guid.CreateVersion7(), Guid.Empty)));
    }

    [Fact]
    public async Task A_null_query_is_rejected()
    {
        InMemoryAiAgentRepository agents = new();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            new GetAiAgentByIdHandler(agents).HandleAsync(null!));
    }

    [Fact]
    public void A_null_repository_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => new GetAiAgentByIdHandler(null!));
    }

    internal static AiAgent NewAgent(TenantId tenant, string name) =>
        AiAgent.Create(
            tenant,
            AiAgentName.Create(name),
            AgentInstructions.Create("Help customers choose suitable products."),
            Created);
}
