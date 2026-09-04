using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Tenants;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// The AI agent mapping against real PostgreSQL.
/// </summary>
/// <remarks>
/// Everything asserted here is PostgreSQL-specific -- native <c>uuid</c> columns,
/// <c>timestamptz</c>, name-based enum storage, a foreign key with a delete rule,
/// and column lengths that mirror the Domain limits. An in-memory provider
/// implements none of it, so these tests would pass against it while proving
/// nothing.
/// </remarks>
public sealed class AiAgentPersistenceTests(PostgresTestDatabase database)
    : IClassFixture<PostgresTestDatabase>
{
    private static readonly DateTimeOffset Created = new(2026, 5, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task An_agent_round_trips_with_every_field_intact()
    {
        Tenant tenant = NewTenant();
        AiAgent agent = NewAgent(tenant.Id, "Sales Assistant");
        await SaveAsync(tenant, agent);

        await using OmniAiDbContext reading = database.CreateContext();
        AiAgent reloaded = await reading.AiAgents.SingleAsync(a => a.Id == agent.Id);

        Assert.Equal(agent.Id, reloaded.Id);
        Assert.Equal(tenant.Id, reloaded.TenantId);
        Assert.Equal("Sales Assistant", reloaded.Name.Value);
        Assert.Equal(agent.Instructions.Value, reloaded.Instructions.Value);
        Assert.Equal(AiAgentStatus.Draft, reloaded.Status);
        Assert.Equal(Created, reloaded.CreatedAt);
        Assert.Equal(Created, reloaded.UpdatedAt);
    }

    [Fact]
    public async Task Reconstitution_needed_no_domain_change()
    {
        // The aggregate has a private constructor, get-only Id, TenantId and
        // CreatedAt, and private setters elsewhere. EF binds the constructor by
        // parameter name and writes the rest through the private setters, so no
        // public setter, no parameterless constructor, no EF attribute and no
        // persistence property had to be added to the Domain for this to work.
        // If that ever stops being true, this test is where it shows up.
        Tenant tenant = NewTenant();
        AiAgent agent = NewAgent(tenant.Id, "Sales Assistant");
        agent.Activate(Created.AddHours(1));
        await SaveAsync(tenant, agent);

        await using OmniAiDbContext reading = database.CreateContext();
        AiAgent reloaded = await reading.AiAgents.SingleAsync(a => a.Id == agent.Id);

        // A fully reconstituted aggregate still behaves like one.
        reloaded.Disable(Created.AddHours(2));

        Assert.Equal(AiAgentStatus.Disabled, reloaded.Status);
        Assert.Equal(Created.AddHours(2), reloaded.UpdatedAt);
    }

    [Fact]
    public async Task The_identity_is_stored_as_a_native_uuid()
    {
        Tenant tenant = NewTenant();
        AiAgent agent = NewAgent(tenant.Id, "Sales Assistant");
        await SaveAsync(tenant, agent);

        object? types = await database.ExecuteScalarAsync(
            "SELECT string_agg(data_type, ',' ORDER BY column_name) FROM information_schema.columns " +
            "WHERE table_name = 'ai_agents' AND column_name IN ('id', 'tenant_id')");

        Assert.Equal("uuid,uuid", types as string);
    }

    [Fact]
    public async Task The_stored_identity_is_the_domain_identity()
    {
        // Read straight out of PostgreSQL, bypassing EF and the value converter.
        Tenant tenant = NewTenant();
        AiAgent agent = NewAgent(tenant.Id, "Sales Assistant");
        await SaveAsync(tenant, agent);

        object? stored = await database.ExecuteScalarAsync(
            $"SELECT id::text FROM ai_agents WHERE id = '{agent.Id.Value}'");

        Assert.Equal(agent.Id.Value.ToString("D", CultureInfo.InvariantCulture), stored as string);
    }

    [Fact]
    public async Task The_owning_tenant_is_stored_on_the_row()
    {
        Tenant tenant = NewTenant();
        AiAgent agent = NewAgent(tenant.Id, "Sales Assistant");
        await SaveAsync(tenant, agent);

        object? stored = await database.ExecuteScalarAsync(
            $"SELECT tenant_id::text FROM ai_agents WHERE id = '{agent.Id.Value}'");

        Assert.Equal(tenant.Id.Value.ToString("D", CultureInfo.InvariantCulture), stored as string);
    }

    [Fact]
    public async Task The_name_is_a_scalar_column_not_json()
    {
        Tenant tenant = NewTenant();
        AiAgent agent = NewAgent(tenant.Id, "Sales Assistant");
        await SaveAsync(tenant, agent);

        object? stored = await database.ExecuteScalarAsync(
            $"SELECT name FROM ai_agents WHERE id = '{agent.Id.Value}'");

        // Plain text, readable by any SQL client. A JSON column would have stored
        // quotes and forfeited the length constraint.
        Assert.Equal("Sales Assistant", stored as string);
    }

    [Fact]
    public async Task A_non_latin_name_round_trips()
    {
        Tenant tenant = NewTenant();
        AiAgent agent = NewAgent(tenant.Id, "مساعد المبيعات");
        await SaveAsync(tenant, agent);

        await using OmniAiDbContext reading = database.CreateContext();
        AiAgent reloaded = await reading.AiAgents.SingleAsync(a => a.Id == agent.Id);

        Assert.Equal("مساعد المبيعات", reloaded.Name.Value);
    }

    [Fact]
    public async Task Multiline_instructions_round_trip_byte_for_byte()
    {
        // The rule the whole value object exists to protect. If persistence
        // normalised line endings, collapsed blank lines or trimmed indentation,
        // the agent would come back having been told something different.
        const string multiline =
            "Rules:\n1. Be brief.\n2. Never quote a discount.\n\n\tEscalate refunds.\n  Indented note.";

        Tenant tenant = NewTenant();
        AiAgent agent = AiAgent.Create(
            tenant.Id,
            AiAgentName.Create("Sales Assistant"),
            AgentInstructions.Create(multiline),
            Created);
        await SaveAsync(tenant, agent);

        await using OmniAiDbContext reading = database.CreateContext();
        AiAgent reloaded = await reading.AiAgents.SingleAsync(a => a.Id == agent.Id);

        Assert.Equal(multiline, reloaded.Instructions.Value);
    }

    [Fact]
    public async Task Windows_line_endings_in_instructions_are_preserved()
    {
        const string crlf = "First line.\r\nSecond line.";

        Tenant tenant = NewTenant();
        AiAgent agent = AiAgent.Create(
            tenant.Id,
            AiAgentName.Create("Sales Assistant"),
            AgentInstructions.Create(crlf),
            Created);
        await SaveAsync(tenant, agent);

        object? stored = await database.ExecuteScalarAsync(
            $"SELECT instructions FROM ai_agents WHERE id = '{agent.Id.Value}'");

        Assert.Equal(crlf, stored as string);
    }

    [Fact]
    public async Task Instructions_at_the_domain_maximum_fit_the_column()
    {
        // The column length mirrors the Domain limit, so a value the Domain
        // accepts must never be rejected -- or silently truncated -- by storage.
        string atLimit = new('a', AgentInstructions.MaxLength);

        Tenant tenant = NewTenant();
        AiAgent agent = AiAgent.Create(
            tenant.Id,
            AiAgentName.Create("Sales Assistant"),
            AgentInstructions.Create(atLimit),
            Created);
        await SaveAsync(tenant, agent);

        await using OmniAiDbContext reading = database.CreateContext();
        AiAgent reloaded = await reading.AiAgents.SingleAsync(a => a.Id == agent.Id);

        Assert.Equal(AgentInstructions.MaxLength, reloaded.Instructions.Value.Length);
    }

    [Fact]
    public async Task A_name_at_the_domain_maximum_fits_the_column()
    {
        string atLimit = new('a', AiAgentName.MaxLength);

        Tenant tenant = NewTenant();
        AiAgent agent = NewAgent(tenant.Id, atLimit);
        await SaveAsync(tenant, agent);

        await using OmniAiDbContext reading = database.CreateContext();
        AiAgent reloaded = await reading.AiAgents.SingleAsync(a => a.Id == agent.Id);

        Assert.Equal(AiAgentName.MaxLength, reloaded.Name.Value.Length);
    }

    [Theory]
    [InlineData(AiAgentStatus.Draft, "Draft")]
    [InlineData(AiAgentStatus.Active, "Active")]
    [InlineData(AiAgentStatus.Disabled, "Disabled")]
    public async Task The_status_is_stored_by_name_not_by_ordinal(AiAgentStatus status, string expected)
    {
        // An integer column would make the stored meaning depend on the
        // declaration order of the enum, so inserting a member would silently
        // reinterpret every existing row.
        Tenant tenant = NewTenant();
        AiAgent agent = NewAgent(tenant.Id, "Sales Assistant");

        if (status is not AiAgentStatus.Draft)
        {
            agent.Activate(Created.AddMinutes(1));
        }

        if (status is AiAgentStatus.Disabled)
        {
            agent.Disable(Created.AddMinutes(2));
        }

        await SaveAsync(tenant, agent);

        object? stored = await database.ExecuteScalarAsync(
            $"SELECT status FROM ai_agents WHERE id = '{agent.Id.Value}'");

        Assert.Equal(expected, stored as string);
    }

    [Fact]
    public async Task A_status_change_round_trips()
    {
        Tenant tenant = NewTenant();
        AiAgent agent = NewAgent(tenant.Id, "Sales Assistant");
        await SaveAsync(tenant, agent);

        await using (OmniAiDbContext writing = database.CreateContext())
        {
            AiAgent loaded = await writing.AiAgents.SingleAsync(a => a.Id == agent.Id);
            loaded.Activate(Created.AddHours(1));
            await writing.SaveChangesAsync();
        }

        await using OmniAiDbContext reading = database.CreateContext();
        AiAgent reloaded = await reading.AiAgents.SingleAsync(a => a.Id == agent.Id);

        Assert.Equal(AiAgentStatus.Active, reloaded.Status);
        Assert.Equal(Created.AddHours(1), reloaded.UpdatedAt);
    }

    [Fact]
    public async Task Both_instants_are_stored_as_timestamptz()
    {
        object? types = await database.ExecuteScalarAsync(
            "SELECT string_agg(data_type, ',' ORDER BY column_name) FROM information_schema.columns " +
            "WHERE table_name = 'ai_agents' AND column_name IN ('created_at', 'updated_at')");

        Assert.Equal("timestamp with time zone,timestamp with time zone", types as string);
    }

    [Fact]
    public async Task An_instant_supplied_with_an_offset_is_stored_as_the_same_utc_moment()
    {
        DateTimeOffset withOffset = new(2026, 5, 1, 12, 0, 0, TimeSpan.FromHours(3));

        Tenant tenant = NewTenant();
        AiAgent agent = AiAgent.Create(
            tenant.Id,
            AiAgentName.Create("Sales Assistant"),
            AgentInstructions.Create("Help customers."),
            withOffset);
        await SaveAsync(tenant, agent);

        await using OmniAiDbContext reading = database.CreateContext();
        AiAgent reloaded = await reading.AiAgents.SingleAsync(a => a.Id == agent.Id);

        Assert.Equal(withOffset.UtcDateTime, reloaded.CreatedAt.UtcDateTime);
        Assert.Equal(TimeSpan.Zero, reloaded.CreatedAt.Offset);
    }

    [Fact]
    public async Task The_high_water_update_mark_survives_a_round_trip()
    {
        // An out-of-order mutation applied before saving: the edit lands, the mark
        // does not move, and persistence must record exactly that rather than
        // "helpfully" stamping the later value.
        Tenant tenant = NewTenant();
        AiAgent agent = NewAgent(tenant.Id, "Sales Assistant");
        agent.Rename(AiAgentName.Create("Renamed later"), Created.AddHours(5));
        agent.UpdateInstructions(AgentInstructions.Create("Arrived out of order."), Created.AddHours(1));
        await SaveAsync(tenant, agent);

        await using OmniAiDbContext reading = database.CreateContext();
        AiAgent reloaded = await reading.AiAgents.SingleAsync(a => a.Id == agent.Id);

        Assert.Equal("Arrived out of order.", reloaded.Instructions.Value);
        Assert.Equal(Created.AddHours(5), reloaded.UpdatedAt);
    }

    [Fact]
    public async Task An_agent_whose_tenant_does_not_exist_is_rejected_by_the_foreign_key()
    {
        // Defence in depth. The use case checks first, but the database is what
        // makes an orphaned agent impossible rather than merely unlikely.
        AiAgent orphan = NewAgent(TenantId.New(), "Sales Assistant");

        await using OmniAiDbContext context = database.CreateContext();
        context.AiAgents.Add(orphan);

        DbUpdateException failure =
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        PostgresException postgres = Assert.IsType<PostgresException>(failure.InnerException);

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, postgres.SqlState);
        Assert.Equal("fk_ai_agents_tenant", postgres.ConstraintName);
    }

    [Fact]
    public async Task A_tenant_that_still_owns_agents_cannot_be_deleted()
    {
        // Restrict, not Cascade. Deleting a tenant would otherwise destroy every
        // instruction set it ever wrote as an invisible side effect, and tenant
        // deletion semantics have not been decided.
        Tenant tenant = NewTenant();
        AiAgent agent = NewAgent(tenant.Id, "Sales Assistant");
        await SaveAsync(tenant, agent);

        PostgresException failure = await Assert.ThrowsAsync<PostgresException>(() =>
            database.ExecuteScalarAsync($"DELETE FROM tenants WHERE id = '{tenant.Id.Value}'"));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, failure.SqlState);
        Assert.Equal("fk_ai_agents_tenant", failure.ConstraintName);
    }

    [Fact]
    public async Task The_foreign_key_delete_rule_is_restrict()
    {
        // Asserted on the applied schema rather than inferred from behaviour, so a
        // future migration that changes the rule to CASCADE fails here.
        object? rule = await database.ExecuteScalarAsync(
            "SELECT rc.delete_rule FROM information_schema.referential_constraints rc " +
            "WHERE rc.constraint_name = 'fk_ai_agents_tenant'");

        Assert.Equal("RESTRICT", rule as string);
    }

    [Fact]
    public async Task Two_agents_in_one_tenant_may_share_a_name()
    {
        // No uniqueness rule exists, and none was invented. A unique index on
        // (tenant_id, name) would impose a product rule nobody asked for, so this
        // insert must succeed.
        Tenant tenant = NewTenant();
        AiAgent first = NewAgent(tenant.Id, "Sales Assistant");
        AiAgent second = NewAgent(tenant.Id, "Sales Assistant");

        await using OmniAiDbContext context = database.CreateContext();
        context.Tenants.Add(tenant);
        context.AiAgents.AddRange(first, second);
        await context.SaveChangesAsync();

        object? count = await database.ExecuteScalarAsync(
            $"SELECT count(*) FROM ai_agents WHERE tenant_id = '{tenant.Id.Value}'");

        Assert.Equal(2L, count as long?);
    }

    [Fact]
    public async Task Two_tenants_may_each_have_an_agent_with_the_same_name()
    {
        Tenant first = NewTenant();
        Tenant second = NewTenant();

        await using OmniAiDbContext context = database.CreateContext();
        context.Tenants.AddRange(first, second);
        context.AiAgents.AddRange(
            NewAgent(first.Id, "Sales Assistant"),
            NewAgent(second.Id, "Sales Assistant"));

        await context.SaveChangesAsync();

        object? count = await database.ExecuteScalarAsync(
            "SELECT count(*) FROM ai_agents WHERE name = 'Sales Assistant'");

        Assert.True(Convert.ToInt64(count, CultureInfo.InvariantCulture) >= 2);
    }

    [Fact]
    public async Task Every_column_is_not_null()
    {
        object? nullable = await database.ExecuteScalarAsync(
            "SELECT count(*) FROM information_schema.columns " +
            "WHERE table_name = 'ai_agents' AND is_nullable = 'YES'");

        Assert.Equal(0L, nullable as long?);
    }

    [Fact]
    public async Task The_column_lengths_mirror_the_domain_limits()
    {
        object? lengths = await database.ExecuteScalarAsync(
            "SELECT string_agg(column_name || '=' || character_maximum_length, ',' ORDER BY column_name) " +
            "FROM information_schema.columns " +
            "WHERE table_name = 'ai_agents' AND character_maximum_length IS NOT NULL");

        Assert.Equal(
            $"instructions={AgentInstructions.MaxLength},name={AiAgentName.MaxLength},status=32",
            lengths as string);
    }

    [Fact]
    public async Task The_tenant_index_exists()
    {
        // Justified by the other side of the foreign key: without it, enforcing
        // fk_ai_agents_tenant on a tenant delete would sequentially scan this
        // table.
        object? indexes = await database.ExecuteScalarAsync(
            "SELECT count(*) FROM pg_indexes " +
            "WHERE tablename = 'ai_agents' AND indexname = 'ix_ai_agents_tenant'");

        Assert.Equal(1L, indexes as long?);
    }

    [Fact]
    public async Task There_is_no_unique_index_on_the_agent_name()
    {
        // Guards the decision, not just today's schema: a future migration that
        // quietly adds UNIQUE (tenant_id, name) would impose a Domain rule that
        // was explicitly not invented.
        object? unique = await database.ExecuteScalarAsync(
            "SELECT count(*) FROM pg_indexes " +
            "WHERE tablename = 'ai_agents' AND indexdef ILIKE '%UNIQUE%' AND indexdef ILIKE '%name%'");

        Assert.Equal(0L, unique as long?);
    }

    internal static Tenant NewTenant() =>
        Tenant.Create(
            TenantName.Create("Acme Trading"),
            TenantSlug.Create($"t{Guid.NewGuid():N}"[..20]));

    internal static AiAgent NewAgent(TenantId tenantId, string name) =>
        AiAgent.Create(
            tenantId,
            AiAgentName.Create(name),
            AgentInstructions.Create("Help customers choose suitable products."),
            Created);

    private async Task SaveAsync(Tenant tenant, AiAgent agent)
    {
        await using OmniAiDbContext context = database.CreateContext();

        context.Tenants.Add(tenant);
        context.AiAgents.Add(agent);

        await context.SaveChangesAsync();
    }
}
