using Microsoft.EntityFrameworkCore;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Tenants;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// The two EF Core behaviours the ETag design depends on.
/// </summary>
/// <remarks>
/// Both are assumptions about a provider, so they are asserted rather than
/// trusted. If either stops holding after an EF Core or Npgsql upgrade, the
/// concurrency contract breaks in a way that would otherwise only show up as a
/// missing or stale <c>ETag</c> header in production.
/// </remarks>
public sealed class XminReadBackProbeTests(PostgresTestDatabase database)
    : IClassFixture<PostgresTestDatabase>
{
    private static readonly DateTimeOffset Created = new(2026, 5, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task The_row_version_is_available_after_an_insert_and_after_an_update()
    {
        // Needed because the create and mutation responses must carry the ETag of
        // the version that was just written. If EF did not read xmin back, every
        // one of those headers would be wrong.
        Tenant tenant = AiAgentPersistenceTests.NewTenant();
        AiAgent agent = AiAgentPersistenceTests.NewAgent(tenant.Id, "Sales Assistant");

        await using OmniAiDbContext context = database.CreateContext();
        context.Tenants.Add(tenant);
        context.AiAgents.Add(agent);
        await context.SaveChangesAsync();

        uint afterInsert = context.Entry(agent).Property<uint>("xmin").CurrentValue;

        Assert.NotEqual(0u, afterInsert);

        agent.Rename(AiAgentName.Create("Renamed"), Created.AddHours(1));
        await context.SaveChangesAsync();

        uint afterUpdate = context.Entry(agent).Property<uint>("xmin").CurrentValue;

        Assert.NotEqual(0u, afterUpdate);
        Assert.NotEqual(afterInsert, afterUpdate);
    }

    [Fact]
    public async Task An_overridden_original_row_version_is_what_the_update_predicate_uses()
    {
        // The mechanic the whole remediation rests on. Setting OriginalValue makes
        // the CLIENT's expected version -- not the version this request happened
        // to load -- the one PostgreSQL checks. Without it, a request that loads
        // fresh state can never conflict, which is precisely the lost update this
        // work exists to prevent.
        Tenant tenant = AiAgentPersistenceTests.NewTenant();
        AiAgent agent = AiAgentPersistenceTests.NewAgent(tenant.Id, "Sales Assistant");

        await using (OmniAiDbContext seeding = database.CreateContext())
        {
            seeding.Tenants.Add(tenant);
            seeding.AiAgents.Add(agent);
            await seeding.SaveChangesAsync();
        }

        await using OmniAiDbContext context = database.CreateContext();
        AiAgent loaded = await context.AiAgents.SingleAsync(a => a.Id == agent.Id);

        uint loadedVersion = context.Entry(loaded).Property<uint>("xmin").CurrentValue;

        loaded.Rename(AiAgentName.Create("Renamed"), Created.AddHours(1));

        // Pretend the caller presented a version that is not the one on the row.
        context.Entry(loaded).Property<uint>("xmin").OriginalValue = loadedVersion - 1;

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => context.SaveChangesAsync());

        object? stored = await database.ExecuteScalarAsync(
            $"SELECT name FROM ai_agents WHERE id = '{agent.Id.Value}'");

        Assert.Equal("Sales Assistant", stored as string);
    }
}
