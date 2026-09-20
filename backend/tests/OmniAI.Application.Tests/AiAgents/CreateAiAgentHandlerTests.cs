using OmniAI.Application.AiAgents.CreateAiAgent;
using OmniAI.Application.AiAgents;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Tenants;
using OmniAI.Application.Tests.Security;
using OmniAI.Application.Tests.Tenants;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Tenants;
using OmniAI.Domain;
using OmniAI.Testing;

namespace OmniAI.Application.Tests.AiAgents;

public sealed class CreateAiAgentHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 1, 9, 0, 0, TimeSpan.Zero);

    private const string Instructions = "Help customers choose suitable products.";

    [Fact]
    public async Task Creating_an_agent_returns_the_stored_representation()
    {
        Tenant tenant = SeedTenant(out InMemoryTenantRepository tenants);
        InMemoryAiAgentRepository agents = new();

        Versioned<AiAgentView> created = await Handler(tenants, agents)
            .HandleAsync(new CreateAiAgentCommand(tenant.Id.Value, "Sales Assistant", Instructions));

        Assert.NotEqual(Guid.Empty, created.Value.Id);
        Assert.Equal("Sales Assistant", created.Value.Name);
        Assert.Equal(Instructions, created.Value.Instructions);
    }

    [Fact]
    public async Task A_created_agent_is_stored()
    {
        Tenant tenant = SeedTenant(out InMemoryTenantRepository tenants);
        InMemoryAiAgentRepository agents = new();

        Versioned<AiAgentView> created = await Handler(tenants, agents)
            .HandleAsync(new CreateAiAgentCommand(tenant.Id.Value, "Sales Assistant", Instructions));

        AiAgent stored = Assert.Single(agents.Stored);

        Assert.Equal(created.Value.Id, stored.Id.Value);
    }

    [Fact]
    public async Task A_created_agent_starts_as_a_draft()
    {
        // Not Active. Deciding when an agent starts serving customers belongs to
        // the tenant, and must not be a side effect of creating it.
        Tenant tenant = SeedTenant(out InMemoryTenantRepository tenants);
        InMemoryAiAgentRepository agents = new();

        Versioned<AiAgentView> created = await Handler(tenants, agents)
            .HandleAsync(new CreateAiAgentCommand(tenant.Id.Value, "Sales Assistant", Instructions));

        Assert.Equal(AiAgentStatus.Draft, created.Value.Status);
        Assert.Equal(AiAgentStatus.Draft, agents.Stored[0].Status);
    }

    [Fact]
    public async Task A_created_agent_belongs_to_the_tenant_in_the_command()
    {
        // The ownership that every later authorisation rule will hang from, so it
        // is asserted on the stored aggregate rather than only on the response.
        Tenant tenant = SeedTenant(out InMemoryTenantRepository tenants);
        InMemoryAiAgentRepository agents = new();

        Versioned<AiAgentView> created = await Handler(tenants, agents)
            .HandleAsync(new CreateAiAgentCommand(tenant.Id.Value, "Sales Assistant", Instructions));

        Assert.Equal(tenant.Id.Value, created.Value.TenantId);
        Assert.Equal(tenant.Id, agents.Stored[0].TenantId);
    }

    [Fact]
    public async Task Creating_an_agent_for_an_unknown_tenant_is_reported_as_a_missing_tenant()
    {
        // The ordinary case: a route naming a tenant that is not there. It must
        // not reach the database and come back as a foreign key failure.
        InMemoryTenantRepository tenants = new();
        InMemoryAiAgentRepository agents = new();

        TenantNotFoundException failure = await Assert.ThrowsAsync<TenantNotFoundException>(() =>
            Handler(tenants, agents)
                .HandleAsync(new CreateAiAgentCommand(Guid.CreateVersion7(), "Sales Assistant", Instructions)));

        Assert.Empty(agents.Stored);
        Assert.NotNull(failure.TenantId);
    }

    [Fact]
    public async Task A_tenant_that_vanishes_after_the_check_is_still_reported_as_a_missing_tenant()
    {
        // The window the existence check cannot close. The store -- in production
        // the foreign key -- is what catches it, and the caller must still be told
        // what happened rather than receiving an unexplained failure.
        Tenant tenant = SeedTenant(out InMemoryTenantRepository tenants);
        InMemoryAiAgentRepository agents = new() { PretendTenantVanished = true };

        await Assert.ThrowsAsync<TenantNotFoundException>(() =>
            Handler(tenants, agents)
                .HandleAsync(new CreateAiAgentCommand(tenant.Id.Value, "Sales Assistant", Instructions)));

        Assert.Empty(agents.Stored);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Sales\nAssistant")]
    public async Task An_unacceptable_name_is_rejected_by_the_domain(string? name)
    {
        Tenant tenant = SeedTenant(out InMemoryTenantRepository tenants);
        InMemoryAiAgentRepository agents = new();

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            Handler(tenants, agents)
                .HandleAsync(new CreateAiAgentCommand(tenant.Id.Value, name, Instructions)));

        Assert.Empty(agents.Stored);
    }

    [Fact]
    public async Task An_over_long_name_is_rejected_and_not_truncated()
    {
        Tenant tenant = SeedTenant(out InMemoryTenantRepository tenants);
        InMemoryAiAgentRepository agents = new();

        DomainValidationException failure =
            await Assert.ThrowsAsync<DomainValidationException>(() =>
                Handler(tenants, agents).HandleAsync(new CreateAiAgentCommand(
                    tenant.Id.Value,
                    new string('a', AiAgentName.MaxLength + 1),
                    Instructions)));

        Assert.Contains("not truncated", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Unacceptable_instructions_are_rejected_by_the_domain(string? instructions)
    {
        Tenant tenant = SeedTenant(out InMemoryTenantRepository tenants);
        InMemoryAiAgentRepository agents = new();

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            Handler(tenants, agents)
                .HandleAsync(new CreateAiAgentCommand(tenant.Id.Value, "Sales Assistant", instructions)));

        Assert.Empty(agents.Stored);
    }

    [Fact]
    public async Task Over_long_instructions_are_rejected_and_not_truncated()
    {
        Tenant tenant = SeedTenant(out InMemoryTenantRepository tenants);
        InMemoryAiAgentRepository agents = new();

        DomainValidationException failure =
            await Assert.ThrowsAsync<DomainValidationException>(() =>
                Handler(tenants, agents).HandleAsync(new CreateAiAgentCommand(
                    tenant.Id.Value,
                    "Sales Assistant",
                    new string('a', AgentInstructions.MaxLength + 1))));

        Assert.Contains("not truncated", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_empty_tenant_id_is_rejected_as_invalid_input_not_as_a_missing_tenant()
    {
        // Guid.Empty is malformed input rather than a tenant that does not exist,
        // so it must not be answered as not-found.
        InMemoryTenantRepository tenants = new();
        InMemoryAiAgentRepository agents = new();

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            Handler(tenants, agents)
                .HandleAsync(new CreateAiAgentCommand(Guid.Empty, "Sales Assistant", Instructions)));
    }

    [Fact]
    public async Task Invalid_input_is_reported_before_the_tenant_is_looked_up()
    {
        // Both wrong at once. A malformed name is wrong however the rest of the
        // world looks, so it is the answer -- and the documented precedence.
        InMemoryTenantRepository tenants = new();
        InMemoryAiAgentRepository agents = new();

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            Handler(tenants, agents)
                .HandleAsync(new CreateAiAgentCommand(Guid.CreateVersion7(), "  ", Instructions)));
    }

    [Fact]
    public async Task The_creation_instant_comes_from_the_injected_clock()
    {
        // No handler reads DateTimeOffset.UtcNow, so an exact assertion is
        // possible and the behaviour is reproducible.
        Tenant tenant = SeedTenant(out InMemoryTenantRepository tenants);
        InMemoryAiAgentRepository agents = new();

        Versioned<AiAgentView> created = await Handler(tenants, agents)
            .HandleAsync(new CreateAiAgentCommand(tenant.Id.Value, "Sales Assistant", Instructions));

        Assert.Equal(Now, created.Value.CreatedAt);
    }

    [Fact]
    public async Task A_new_agent_has_the_same_created_and_updated_instants()
    {
        Tenant tenant = SeedTenant(out InMemoryTenantRepository tenants);
        InMemoryAiAgentRepository agents = new();

        Versioned<AiAgentView> created = await Handler(tenants, agents)
            .HandleAsync(new CreateAiAgentCommand(tenant.Id.Value, "Sales Assistant", Instructions));

        Assert.Equal(created.Value.CreatedAt, created.Value.UpdatedAt);
    }

    [Fact]
    public async Task The_domain_canonical_values_are_what_is_stored_and_returned()
    {
        // A padded name and padded instructions. The Domain trims the outside and
        // preserves the inside, and the use case adds no normalisation of its own.
        Tenant tenant = SeedTenant(out InMemoryTenantRepository tenants);
        InMemoryAiAgentRepository agents = new();

        Versioned<AiAgentView> created = await Handler(tenants, agents).HandleAsync(
            new CreateAiAgentCommand(tenant.Id.Value, "  Sales Assistant  ", "\n  Be brief.\n  Escalate refunds.  \n"));

        Assert.Equal("Sales Assistant", created.Value.Name);
        Assert.Equal("Be brief.\n  Escalate refunds.", created.Value.Instructions);
    }

    [Fact]
    public async Task A_null_command_is_rejected()
    {
        InMemoryTenantRepository tenants = new();
        InMemoryAiAgentRepository agents = new();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            Handler(tenants, agents).HandleAsync(null!));
    }

    private static CreateAiAgentHandler Handler(
        InMemoryTenantRepository tenants,
        InMemoryAiAgentRepository agents) =>
        new(
            new FakeEntitlements(),
            tenants,
            agents,
            TestAuthority.Anywhere(),
            new FixedTimeProvider(Now));

    private static Tenant SeedTenant(out InMemoryTenantRepository tenants)
    {
        Tenant tenant = Tenant.Create(
            TenantName.Create("Acme Trading"),
            TenantSlug.Create($"t{Guid.NewGuid():N}"[..20]));

        tenants = new InMemoryTenantRepository();
        tenants.Seed(tenant);

        return tenant;
    }
}
