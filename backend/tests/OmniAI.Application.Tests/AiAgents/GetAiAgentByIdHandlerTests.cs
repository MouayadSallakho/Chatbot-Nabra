using OmniAI.Application.AiAgents.GetAiAgentById;
using OmniAI.Application.AiAgents;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Tests.Security;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Tenants;
using OmniAI.Domain;

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

        Versioned<AiAgentView>? found = await new GetAiAgentByIdHandler(agents, TestAuthority.Anywhere())
            .HandleAsync(new GetAiAgentByIdQuery(tenant.Value, agent.Id.Value));

        Assert.NotNull(found);
        Assert.Equal(agent.Id.Value, found.Value.Id);
        Assert.Equal("Sales Assistant", found.Value.Name);
        Assert.Equal(tenant.Value, found.Value.TenantId);
    }

    [Fact]
    public async Task Every_modelled_field_is_projected()
    {
        TenantId tenant = TenantId.New();
        AiAgent agent = NewAgent(tenant, "Sales Assistant");
        agent.Activate(Created.AddHours(1));
        InMemoryAiAgentRepository agents = new InMemoryAiAgentRepository().Seed(agent);

        Versioned<AiAgentView> found = await new GetAiAgentByIdHandler(agents, TestAuthority.Anywhere())
            .HandleAsync(new GetAiAgentByIdQuery(tenant.Value, agent.Id.Value))
            ?? throw new InvalidOperationException();

        Assert.Equal(agent.Instructions.Value, found.Value.Instructions);
        Assert.Equal(AiAgentStatus.Active, found.Value.Status);
        Assert.Equal(Created, found.Value.CreatedAt);
        Assert.Equal(Created.AddHours(1), found.Value.UpdatedAt);
    }

    [Fact]
    public async Task An_unknown_agent_is_absent_rather_than_an_error()
    {
        InMemoryAiAgentRepository agents = new();

        Versioned<AiAgentView>? found = await new GetAiAgentByIdHandler(agents, TestAuthority.Anywhere())
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

        Versioned<AiAgentView>? found = await new GetAiAgentByIdHandler(agents, TestAuthority.Anywhere())
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

        GetAiAgentByIdHandler handler = new(agents, TestAuthority.Anywhere());

        Versioned<AiAgentView>? crossTenant = await handler
            .HandleAsync(new GetAiAgentByIdQuery(tenantA.Value, agentB.Id.Value));

        Versioned<AiAgentView>? nonExistent = await handler
            .HandleAsync(new GetAiAgentByIdQuery(tenantA.Value, Guid.CreateVersion7()));

        Assert.Equal(nonExistent, crossTenant);
        Assert.Null(crossTenant);
    }

    [Fact]
    public async Task An_empty_tenant_id_is_rejected_as_invalid_input()
    {
        InMemoryAiAgentRepository agents = new();

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            new GetAiAgentByIdHandler(agents, TestAuthority.Anywhere())
                .HandleAsync(new GetAiAgentByIdQuery(Guid.Empty, Guid.CreateVersion7())));
    }

    [Fact]
    public async Task An_empty_agent_id_is_rejected_as_invalid_input()
    {
        InMemoryAiAgentRepository agents = new();

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            new GetAiAgentByIdHandler(agents, TestAuthority.Anywhere())
                .HandleAsync(new GetAiAgentByIdQuery(Guid.CreateVersion7(), Guid.Empty)));
    }

    [Fact]
    public async Task A_null_query_is_rejected()
    {
        InMemoryAiAgentRepository agents = new();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            new GetAiAgentByIdHandler(agents, TestAuthority.Anywhere()).HandleAsync(null!));
    }

    [Fact]
    public void A_null_repository_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => new GetAiAgentByIdHandler(null!, TestAuthority.Anywhere()));
    }

    internal static AiAgent NewAgent(TenantId tenant, string name) =>
        AiAgent.Create(
            tenant,
            AiAgentName.Create(name),
            AgentInstructions.Create("Help customers choose suitable products."),
            Created);
}
