using Microsoft.EntityFrameworkCore;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Tenants;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// Proves that two competing writers cannot silently overwrite each other's
/// changes to an AI agent.
/// </summary>
/// <remarks>
/// A separate class from <c>ConcurrencyTests</c>, which covers the tenant and
/// conversation aggregates accepted in earlier phases: the mechanism is the same
/// but the realistic races are different, and an agent has three independently
/// mutable things -- name, instructions and status -- that can each be lost.
/// <para>
/// An agent is edited by people rather than by webhooks: two operators in the
/// same business open the same agent, and one saves while the other is still
/// typing. Detection is the database's -- the UPDATE carries the row's
/// <c>xmin</c> in its WHERE clause, so a stale writer matches no row and EF
/// reports it -- and the point of every test here is that the earlier writer's
/// work survives.
/// </para>
/// </remarks>
public sealed class AiAgentConcurrencyTests(PostgresTestDatabase database)
    : IClassFixture<PostgresTestDatabase>
{
    private static readonly DateTimeOffset Created = new(2026, 5, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_stale_rename_is_rejected_rather_than_silently_applied()
    {
        (Tenant tenant, AiAgent agent) = await SeedAsync();

        await using OmniAiDbContext first = database.CreateContext();
        await using OmniAiDbContext second = database.CreateContext();

        AiAgent loadedByFirst = await LoadAsync(first, agent.Id);
        AiAgent loadedBySecond = await LoadAsync(second, agent.Id);

        loadedByFirst.Rename(AiAgentName.Create("Renamed by first"), Created.AddHours(1));
        await first.SaveChangesAsync();

        // The second writer's snapshot is now stale.
        loadedBySecond.Rename(AiAgentName.Create("Renamed by second"), Created.AddHours(2));

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());

        Assert.Equal("Renamed by first", (await ReloadAsync(agent.Id)).Name.Value);
    }

    [Fact]
    public async Task A_stale_instructions_update_is_rejected_and_the_earlier_text_survives()
    {
        // The most damaging version of this race: instructions are the agent's
        // actual behaviour, so a lost update means a business quietly running on
        // rules somebody thought they had replaced.
        (Tenant tenant, AiAgent agent) = await SeedAsync();

        await using OmniAiDbContext first = database.CreateContext();
        await using OmniAiDbContext second = database.CreateContext();

        AiAgent loadedByFirst = await LoadAsync(first, agent.Id);
        AiAgent loadedBySecond = await LoadAsync(second, agent.Id);

        loadedByFirst.UpdateInstructions(
            AgentInstructions.Create("Never quote a discount."),
            Created.AddHours(1));
        await first.SaveChangesAsync();

        loadedBySecond.UpdateInstructions(
            AgentInstructions.Create("Offer a 20 percent discount."),
            Created.AddHours(2));

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());

        Assert.Equal("Never quote a discount.", (await ReloadAsync(agent.Id)).Instructions.Value);
    }

    [Fact]
    public async Task A_stale_activation_is_rejected()
    {
        // A genuine activation from a stale snapshot: the agent is disabled, one
        // operator edits it, and another -- still holding the disabled copy --
        // switches it on. Losing this would put an agent back in front of
        // customers on the strength of a snapshot somebody had already replaced.
        //
        // The competing write is a rename rather than a status change, so the
        // second writer's Activate is a real Disabled -> Active transition and
        // produces an UPDATE. Had its own snapshot already read Active, the call
        // would have been an idempotent no-op with nothing to conflict with.
        (Tenant tenant, AiAgent agent) = await SeedAsync(AiAgentStatus.Disabled);

        await using OmniAiDbContext renamer = database.CreateContext();
        await using OmniAiDbContext activator = database.CreateContext();

        AiAgent loadedByRenamer = await LoadAsync(renamer, agent.Id);
        AiAgent loadedByActivator = await LoadAsync(activator, agent.Id);

        loadedByRenamer.Rename(AiAgentName.Create("Renamed by first"), Created.AddHours(1));
        await renamer.SaveChangesAsync();

        loadedByActivator.Activate(Created.AddHours(2));

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => activator.SaveChangesAsync());

        AiAgent reloaded = await ReloadAsync(agent.Id);

        Assert.Equal(AiAgentStatus.Disabled, reloaded.Status);
        Assert.Equal("Renamed by first", reloaded.Name.Value);
    }

    [Fact]
    public async Task A_stale_disable_is_rejected()
    {
        // The other direction, and the race across two different fields: the token
        // is per row rather than per column, so a rename and a status change still
        // conflict. That is correct -- both writers computed their change from the
        // same snapshot, and neither can know the other was compatible.
        (Tenant tenant, AiAgent agent) = await SeedAsync(AiAgentStatus.Active);

        await using OmniAiDbContext renamer = database.CreateContext();
        await using OmniAiDbContext disabler = database.CreateContext();

        AiAgent loadedByRenamer = await LoadAsync(renamer, agent.Id);
        AiAgent loadedByDisabler = await LoadAsync(disabler, agent.Id);

        loadedByRenamer.Rename(AiAgentName.Create("Renamed by first"), Created.AddHours(1));
        await renamer.SaveChangesAsync();

        loadedByDisabler.Disable(Created.AddHours(2));

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => disabler.SaveChangesAsync());

        AiAgent reloaded = await ReloadAsync(agent.Id);

        Assert.Equal(AiAgentStatus.Active, reloaded.Status);
        Assert.Equal("Renamed by first", reloaded.Name.Value);
    }

    [Fact]
    public async Task A_rejected_write_leaves_the_row_exactly_as_the_winner_left_it()
    {
        // Not merely "the update failed": nothing partial landed either.
        (Tenant tenant, AiAgent agent) = await SeedAsync();

        await using OmniAiDbContext first = database.CreateContext();
        await using OmniAiDbContext second = database.CreateContext();

        AiAgent loadedByFirst = await LoadAsync(first, agent.Id);
        AiAgent loadedBySecond = await LoadAsync(second, agent.Id);

        loadedByFirst.Rename(AiAgentName.Create("Winner"), Created.AddHours(1));
        await first.SaveChangesAsync();

        loadedBySecond.Rename(AiAgentName.Create("Loser"), Created.AddHours(2));
        loadedBySecond.UpdateInstructions(AgentInstructions.Create("Loser rules."), Created.AddHours(2));

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());

        object? stored = await database.ExecuteScalarAsync(
            $"SELECT name || '|' || instructions FROM ai_agents WHERE id = '{agent.Id.Value}'");

        Assert.Equal("Winner|Help customers choose suitable products.", stored as string);
    }

    [Fact]
    public async Task Sequential_updates_from_a_fresh_load_succeed()
    {
        // The token must not block ordinary, non-competing edits.
        (Tenant tenant, AiAgent agent) = await SeedAsync();

        for (int round = 1; round <= 3; round++)
        {
            await using OmniAiDbContext context = database.CreateContext();
            AiAgent loaded = await LoadAsync(context, agent.Id);

            loaded.Rename(AiAgentName.Create($"Assistant {round}"), Created.AddHours(round));
            await context.SaveChangesAsync();
        }

        Assert.Equal("Assistant 3", (await ReloadAsync(agent.Id)).Name.Value);
    }

    [Fact]
    public async Task An_idempotent_lifecycle_call_cannot_produce_a_spurious_conflict()
    {
        // Activating an already active agent changes nothing, so EF issues no
        // UPDATE and there is no predicate to fail -- even from a snapshot that is
        // genuinely stale. A design that stamped UpdatedAt unconditionally would
        // turn this harmless call into a 409.
        (Tenant tenant, AiAgent agent) = await SeedAsync(AiAgentStatus.Active);

        await using OmniAiDbContext first = database.CreateContext();
        await using OmniAiDbContext second = database.CreateContext();

        AiAgent loadedByFirst = await LoadAsync(first, agent.Id);
        AiAgent loadedBySecond = await LoadAsync(second, agent.Id);

        loadedByFirst.Rename(AiAgentName.Create("Renamed by first"), Created.AddHours(1));
        await first.SaveChangesAsync();

        loadedBySecond.Activate(Created.AddHours(2));

        await second.SaveChangesAsync();

        Assert.Equal("Renamed by first", (await ReloadAsync(agent.Id)).Name.Value);
    }

    private static async Task<AiAgent> LoadAsync(OmniAiDbContext context, AiAgentId id) =>
        await context.AiAgents.SingleAsync(agent => agent.Id == id);

    private async Task<AiAgent> ReloadAsync(AiAgentId id)
    {
        await using OmniAiDbContext verification = database.CreateContext();

        return await verification.AiAgents.SingleAsync(agent => agent.Id == id);
    }

    private async Task<(Tenant Tenant, AiAgent Agent)> SeedAsync(
        AiAgentStatus status = AiAgentStatus.Draft)
    {
        Tenant tenant = AiAgentPersistenceTests.NewTenant();
        AiAgent agent = AiAgentPersistenceTests.NewAgent(tenant.Id, "Sales Assistant");

        // Driven through the real lifecycle rather than assigned, because there is
        // no way to assign it -- which is the point of the aggregate.
        if (status is not AiAgentStatus.Draft)
        {
            agent.Activate(Created.AddMinutes(1));
        }

        if (status is AiAgentStatus.Disabled)
        {
            agent.Disable(Created.AddMinutes(2));
        }

        await using OmniAiDbContext context = database.CreateContext();

        context.Tenants.Add(tenant);
        context.AiAgents.Add(agent);

        await context.SaveChangesAsync();

        return (tenant, agent);
    }
}
