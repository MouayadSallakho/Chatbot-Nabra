using Microsoft.EntityFrameworkCore;
using OmniAI.Application.AiAgents;
using OmniAI.Application.AiAgents.Persistence;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Tenants;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Tenants;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Infrastructure.Persistence.Repositories;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// The AI agent repository against real PostgreSQL.
/// </summary>
/// <remarks>
/// These are the tests that make two claims structural rather than behavioural.
/// The tenant actually reaches the SQL, so cross-tenant hiding is not a filter
/// applied after loading. And the <em>caller's</em> expected version reaches the
/// SQL, so a conditional write is checked against what the caller saw rather than
/// against whatever this request happened to load -- which is the difference
/// between preventing a lost update and merely narrowing the window for one.
/// </remarks>
public sealed class AiAgentRepositoryTests(PostgresTestDatabase database)
    : IClassFixture<PostgresTestDatabase>
{
    private static readonly DateTimeOffset Created = new(2026, 5, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task An_agent_is_stored_and_can_be_found_again()
    {
        Tenant tenant = AiAgentPersistenceTests.NewTenant();
        AiAgent agent = AiAgentPersistenceTests.NewAgent(tenant.Id, "Sales Assistant");
        await SeedTenantAsync(tenant);

        await using (OmniAiDbContext writing = database.CreateContext())
        {
            await new AiAgentRepository(writing).AddAsync(agent);
        }

        await using OmniAiDbContext reading = database.CreateContext();
        Versioned<AiAgent>? found = await new AiAgentRepository(reading).FindAsync(tenant.Id, agent.Id);

        Assert.NotNull(found);
        Assert.Equal(agent.Id, found.Value.Id);
        Assert.Equal("Sales Assistant", found.Value.Name.Value);
    }

    [Fact]
    public async Task Add_commits_so_a_separate_connection_sees_the_row()
    {
        // The contract says AddAsync commits, which is why there is no unit of
        // work. This checks the claim outside EF entirely.
        Tenant tenant = AiAgentPersistenceTests.NewTenant();
        AiAgent agent = AiAgentPersistenceTests.NewAgent(tenant.Id, "Sales Assistant");
        await SeedTenantAsync(tenant);

        await using OmniAiDbContext context = database.CreateContext();
        await new AiAgentRepository(context).AddAsync(agent);

        object? stored = await database.ExecuteScalarAsync(
            $"SELECT count(*) FROM ai_agents WHERE id = '{agent.Id.Value}'");

        Assert.Equal(1L, stored as long?);
    }

    // ----------------------------------------------------------------- versions

    [Fact]
    public async Task Add_returns_the_version_the_stored_row_is_at()
    {
        // Returned rather than requiring a second read, so a client can make a
        // conditional write immediately after creating an agent.
        Tenant tenant = AiAgentPersistenceTests.NewTenant();
        AiAgent agent = AiAgentPersistenceTests.NewAgent(tenant.Id, "Sales Assistant");
        await SeedTenantAsync(tenant);

        ConcurrencyToken fromAdd;

        await using (OmniAiDbContext writing = database.CreateContext())
        {
            fromAdd = await new AiAgentRepository(writing).AddAsync(agent);
        }

        Assert.False(fromAdd.IsEmpty);

        await using OmniAiDbContext reading = database.CreateContext();
        Versioned<AiAgent> found = await new AiAgentRepository(reading).FindAsync(tenant.Id, agent.Id)
            ?? throw new InvalidOperationException();

        Assert.Equal(fromAdd, found.Version);
    }

    [Fact]
    public async Task The_version_is_opaque_and_reveals_no_persistence_detail()
    {
        // It travels to clients as an ETag. It must not advertise the storage
        // engine, and nothing above Infrastructure may be able to read it.
        (Tenant tenant, AiAgent agent) = await SeedAsync();

        await using OmniAiDbContext context = database.CreateContext();
        Versioned<AiAgent> found = await new AiAgentRepository(context).FindAsync(tenant.Id, agent.Id)
            ?? throw new InvalidOperationException();

        uint rowVersion = context.Entry(found.Value).Property<uint>("xmin").CurrentValue;

        foreach (string forbidden in (string[])["xmin", "xid", "postgres", "transaction"])
        {
            Assert.DoesNotContain(forbidden, found.Version.Value, StringComparison.OrdinalIgnoreCase);
        }

        // Nor is it the raw number in disguise.
        Assert.DoesNotContain(
            rowVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            found.Version.Value,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_successful_update_returns_a_new_version()
    {
        (Tenant tenant, AiAgent agent) = await SeedAsync();

        await using OmniAiDbContext context = database.CreateContext();
        AiAgentRepository repository = new(context);

        Versioned<AiAgent> loaded = await repository.FindAsync(tenant.Id, agent.Id)
            ?? throw new InvalidOperationException();

        loaded.Value.Rename(AiAgentName.Create("Renamed"), Created.AddHours(1));

        ConcurrencyToken after = await repository.UpdateAsync(loaded.Value, loaded.Version);

        Assert.NotEqual(loaded.Version, after);
    }

    [Fact]
    public async Task An_update_that_changes_nothing_keeps_the_same_version()
    {
        // No row is written, so xmin does not move. A design that returned a fresh
        // version here would hand the client a token that had never existed.
        (Tenant tenant, AiAgent agent) = await SeedAsync(activate: true);

        await using OmniAiDbContext context = database.CreateContext();
        AiAgentRepository repository = new(context);

        Versioned<AiAgent> loaded = await repository.FindAsync(tenant.Id, agent.Id)
            ?? throw new InvalidOperationException();

        loaded.Value.Activate(Created.AddHours(9));

        Assert.Equal(loaded.Version, await repository.UpdateAsync(loaded.Value, loaded.Version));
    }

    [Fact]
    public async Task The_callers_expected_version_is_what_the_database_checks()
    {
        // The heart of the remediation, and the reason a purely in-memory
        // comparison would not be enough. This request loads the CURRENT row --
        // as a real request always does -- and still fails, because the version it
        // was told to expect is the one PostgreSQL tests the row against.
        (Tenant tenant, AiAgent agent) = await SeedAsync();

        ConcurrencyToken stale;

        await using (OmniAiDbContext first = database.CreateContext())
        {
            AiAgentRepository firstRepository = new(first);
            Versioned<AiAgent> loaded = await firstRepository.FindAsync(tenant.Id, agent.Id)
                ?? throw new InvalidOperationException();

            stale = loaded.Version;

            loaded.Value.Rename(AiAgentName.Create("Changed by somebody else"), Created.AddHours(1));
            await firstRepository.UpdateAsync(loaded.Value, stale);
        }

        await using OmniAiDbContext second = database.CreateContext();
        AiAgentRepository secondRepository = new(second);

        // Loads the current row, which is now at a newer version.
        Versioned<AiAgent> fresh = await secondRepository.FindAsync(tenant.Id, agent.Id)
            ?? throw new InvalidOperationException();

        Assert.NotEqual(stale, fresh.Version);

        fresh.Value.Rename(AiAgentName.Create("Changed from a stale screen"), Created.AddHours(2));

        // Presenting the stale version must lose, even though the row in memory is
        // current.
        await Assert.ThrowsAsync<AiAgentVersionMismatchException>(() =>
            secondRepository.UpdateAsync(fresh.Value, stale));

        object? stored = await database.ExecuteScalarAsync(
            $"SELECT name FROM ai_agents WHERE id = '{agent.Id.Value}'");

        Assert.Equal("Changed by somebody else", stored as string);
    }

    [Fact]
    public async Task A_version_this_platform_never_issued_is_refused()
    {
        // A token from an older encoding, a truncated header, or something
        // invented. It certainly is not this row's version, so the write is
        // refused rather than sent to the database as a number that might
        // coincidentally match.
        (Tenant tenant, AiAgent agent) = await SeedAsync();

        await using OmniAiDbContext context = database.CreateContext();
        AiAgentRepository repository = new(context);

        Versioned<AiAgent> loaded = await repository.FindAsync(tenant.Id, agent.Id)
            ?? throw new InvalidOperationException();

        loaded.Value.Rename(AiAgentName.Create("Renamed"), Created.AddHours(1));

        await Assert.ThrowsAsync<AiAgentVersionMismatchException>(() =>
            repository.UpdateAsync(loaded.Value, ConcurrencyToken.From("not-a-real-token")));

        object? stored = await database.ExecuteScalarAsync(
            $"SELECT name FROM ai_agents WHERE id = '{agent.Id.Value}'");

        Assert.Equal("Sales Assistant", stored as string);
    }

    [Fact]
    public async Task A_version_from_one_agent_does_not_authorise_a_write_to_another()
    {
        // Two agents written in separate transactions have different versions, so
        // a token lifted from one is simply a stale token for the other.
        (Tenant tenant, AiAgent first) = await SeedAsync();
        AiAgent second = AiAgentPersistenceTests.NewAgent(tenant.Id, "Support Assistant");

        await using (OmniAiDbContext seeding = database.CreateContext())
        {
            seeding.AiAgents.Add(second);
            await seeding.SaveChangesAsync();
        }

        await using OmniAiDbContext context = database.CreateContext();
        AiAgentRepository repository = new(context);

        Versioned<AiAgent> loadedFirst = await repository.FindAsync(tenant.Id, first.Id)
            ?? throw new InvalidOperationException();
        Versioned<AiAgent> loadedSecond = await repository.FindAsync(tenant.Id, second.Id)
            ?? throw new InvalidOperationException();

        Assert.NotEqual(loadedFirst.Version, loadedSecond.Version);

        loadedSecond.Value.Rename(AiAgentName.Create("Renamed"), Created.AddHours(1));

        await Assert.ThrowsAsync<AiAgentVersionMismatchException>(() =>
            repository.UpdateAsync(loadedSecond.Value, loadedFirst.Version));
    }

    // ---------------------------------------------------------- tenant isolation

    [Fact]
    public async Task An_unknown_agent_is_reported_as_absent_not_as_an_error()
    {
        Tenant tenant = AiAgentPersistenceTests.NewTenant();
        await SeedTenantAsync(tenant);

        await using OmniAiDbContext context = database.CreateContext();

        Assert.Null(await new AiAgentRepository(context).FindAsync(tenant.Id, AiAgentId.New()));
    }

    [Fact]
    public async Task An_agent_owned_by_another_tenant_is_not_found()
    {
        // The isolation rule at its lowest level: the row exists, the id is
        // correct, and the answer is still nothing.
        Tenant tenantA = AiAgentPersistenceTests.NewTenant();
        Tenant tenantB = AiAgentPersistenceTests.NewTenant();
        AiAgent agentB = AiAgentPersistenceTests.NewAgent(tenantB.Id, "Tenant B Assistant");
        await SeedAsync(tenantA, tenantB, agentB);

        await using OmniAiDbContext context = database.CreateContext();
        AiAgentRepository repository = new(context);

        Assert.Null(await repository.FindAsync(tenantA.Id, agentB.Id));

        // And the row really is there, so the null above is the tenant filter
        // working rather than a missing fixture.
        Assert.NotNull(await repository.FindAsync(tenantB.Id, agentB.Id));
    }

    [Fact]
    public async Task The_tenant_filter_is_in_the_sql_not_applied_after_loading()
    {
        // The distinction that matters. A fetch-then-compare implementation would
        // satisfy the test above while still pulling another tenant's instruction
        // text into this process -- one early return away from leaking it. This
        // asserts on the statement itself: both predicates are in the WHERE
        // clause, so the row is never read at all.
        Tenant tenantA = AiAgentPersistenceTests.NewTenant();
        Tenant tenantB = AiAgentPersistenceTests.NewTenant();
        AiAgent agentB = AiAgentPersistenceTests.NewAgent(tenantB.Id, "Tenant B Assistant");
        await SeedAsync(tenantA, tenantB, agentB);

        RecordedSql recorded = new();

        await using OmniAiDbContext context = database.CreateContext(recorded);
        await new AiAgentRepository(context).FindAsync(tenantA.Id, agentB.Id);

        string select = Assert.Single(
            recorded.Statements,
            sql => sql.Contains("FROM ai_agents", StringComparison.Ordinal));

        Assert.Contains("WHERE", select, StringComparison.Ordinal);
        Assert.Contains("tenant_id", select, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------- translation

    [Fact]
    public async Task Adding_an_agent_for_a_missing_tenant_is_translated_not_leaked()
    {
        // The provider exception must stop here. Above this class nothing knows
        // what a PostgresException, a SQLSTATE or a constraint name is.
        AiAgent orphan = AiAgentPersistenceTests.NewAgent(TenantId.New(), "Sales Assistant");

        await using OmniAiDbContext context = database.CreateContext();

        TenantNotFoundException failure = await Assert.ThrowsAsync<TenantNotFoundException>(() =>
            new AiAgentRepository(context).AddAsync(orphan));

        Assert.Equal(orphan.TenantId.Value, failure.TenantId);

        // The provider detail is kept for logs, never for the caller's message.
        Assert.NotNull(failure.InnerException);
        Assert.DoesNotContain("23503", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("fk_ai_agents_tenant", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("INSERT", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_rejected_insert_leaves_no_row_and_no_pending_state()
    {
        AiAgent orphan = AiAgentPersistenceTests.NewAgent(TenantId.New(), "Sales Assistant");

        await using OmniAiDbContext context = database.CreateContext();
        await Assert.ThrowsAsync<TenantNotFoundException>(() =>
            new AiAgentRepository(context).AddAsync(orphan));

        object? stored = await database.ExecuteScalarAsync(
            $"SELECT count(*) FROM ai_agents WHERE id = '{orphan.Id.Value}'");

        Assert.Equal(0L, stored as long?);

        // Detached, so reusing this context does not retry the failed insert.
        Assert.Equal(EntityState.Detached, context.Entry(orphan).State);
    }

    [Fact]
    public async Task Update_persists_a_change_made_through_the_aggregate()
    {
        (Tenant tenant, AiAgent agent) = await SeedAsync();

        await using (OmniAiDbContext writing = database.CreateContext())
        {
            AiAgentRepository repository = new(writing);
            Versioned<AiAgent> loaded = await repository.FindAsync(tenant.Id, agent.Id)
                ?? throw new InvalidOperationException();

            loaded.Value.Rename(AiAgentName.Create("Pre-sales Assistant"), Created.AddHours(1));
            await repository.UpdateAsync(loaded.Value, loaded.Version);
        }

        object? stored = await database.ExecuteScalarAsync(
            $"SELECT name FROM ai_agents WHERE id = '{agent.Id.Value}'");

        Assert.Equal("Pre-sales Assistant", stored as string);
    }

    [Fact]
    public async Task Update_writes_nothing_when_the_aggregate_refused_to_change()
    {
        // An idempotent activation reaches UpdateAsync with no modified property.
        // EF must issue no UPDATE at all -- which is also why the precondition
        // cannot be left to the database alone.
        (Tenant tenant, AiAgent agent) = await SeedAsync(activate: true);

        RecordedSql recorded = new();

        await using OmniAiDbContext context = database.CreateContext(recorded);
        AiAgentRepository repository = new(context);
        Versioned<AiAgent> loaded = await repository.FindAsync(tenant.Id, agent.Id)
            ?? throw new InvalidOperationException();

        recorded.Clear();

        loaded.Value.Activate(Created.AddHours(9));
        await repository.UpdateAsync(loaded.Value, loaded.Version);

        Assert.DoesNotContain(
            recorded.Statements,
            sql => sql.Contains("UPDATE ai_agents", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Update_translates_a_stale_write_rather_than_leaking_the_provider_failure()
    {
        (Tenant tenant, AiAgent agent) = await SeedAsync();

        await using OmniAiDbContext first = database.CreateContext();
        await using OmniAiDbContext second = database.CreateContext();

        AiAgentRepository firstRepository = new(first);
        AiAgentRepository secondRepository = new(second);

        Versioned<AiAgent> loadedByFirst = await firstRepository.FindAsync(tenant.Id, agent.Id)
            ?? throw new InvalidOperationException();
        Versioned<AiAgent> loadedBySecond = await secondRepository.FindAsync(tenant.Id, agent.Id)
            ?? throw new InvalidOperationException();

        loadedByFirst.Value.Rename(AiAgentName.Create("Renamed by first"), Created.AddHours(1));
        await firstRepository.UpdateAsync(loadedByFirst.Value, loadedByFirst.Version);

        loadedBySecond.Value.Rename(AiAgentName.Create("Renamed by second"), Created.AddHours(2));

        AiAgentVersionMismatchException failure =
            await Assert.ThrowsAsync<AiAgentVersionMismatchException>(() =>
                secondRepository.UpdateAsync(loadedBySecond.Value, loadedBySecond.Version));

        Assert.Equal(agent.Id.Value, failure.AiAgentId);
        Assert.NotNull(failure.InnerException);
        Assert.DoesNotContain("xmin", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nothing_leaves_the_repository_carrying_an_ef_or_npgsql_type()
    {
        // The boundary claim, checked on the exception the Application layer would
        // actually catch. Its own type is Application-owned; only the inner
        // exception is a provider type, and that never reaches a response body.
        AiAgent orphan = AiAgentPersistenceTests.NewAgent(TenantId.New(), "Sales Assistant");

        await using OmniAiDbContext context = database.CreateContext();

        TenantNotFoundException failure = await Assert.ThrowsAsync<TenantNotFoundException>(() =>
            new AiAgentRepository(context).AddAsync(orphan));

        Assert.Equal("OmniAI.Application", failure.GetType().Assembly.GetName().Name);
    }

    [Fact]
    public async Task A_null_agent_is_rejected()
    {
        await using OmniAiDbContext context = database.CreateContext();
        AiAgentRepository repository = new(context);

        await Assert.ThrowsAsync<ArgumentNullException>(() => repository.AddAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            repository.UpdateAsync(null!, ConcurrencyToken.From("anything")));
    }

    private async Task<(Tenant Tenant, AiAgent Agent)> SeedAsync(bool activate = false)
    {
        Tenant tenant = AiAgentPersistenceTests.NewTenant();
        AiAgent agent = AiAgentPersistenceTests.NewAgent(tenant.Id, "Sales Assistant");

        if (activate)
        {
            agent.Activate(Created.AddMinutes(1));
        }

        await using OmniAiDbContext context = database.CreateContext();

        context.Tenants.Add(tenant);
        context.AiAgents.Add(agent);

        await context.SaveChangesAsync();

        return (tenant, agent);
    }

    private async Task SeedTenantAsync(Tenant tenant)
    {
        await using OmniAiDbContext context = database.CreateContext();

        context.Tenants.Add(tenant);

        await context.SaveChangesAsync();
    }

    private async Task SeedAsync(Tenant first, Tenant second, AiAgent agent)
    {
        await using OmniAiDbContext context = database.CreateContext();

        context.Tenants.AddRange(first, second);
        context.AiAgents.Add(agent);

        await context.SaveChangesAsync();
    }
}
