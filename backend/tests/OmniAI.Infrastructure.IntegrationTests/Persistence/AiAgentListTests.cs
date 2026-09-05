using Microsoft.EntityFrameworkCore;
using OmniAI.Application.AiAgents.ListAiAgents;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Tenants;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Infrastructure.Persistence.Repositories;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// Listing AI agents against real PostgreSQL.
/// </summary>
/// <remarks>
/// The claims worth testing here cannot be tested anywhere else. That the tenant
/// predicate reaches the SQL rather than filtering results afterwards. That the
/// order really is total, which needs rows that genuinely share a timestamp in
/// the database. And that the projection does not select the instructions
/// column -- a fake repository would happily "not return" a value it had already
/// loaded, so only the real query can show the column was never read.
/// </remarks>
public sealed class AiAgentListTests(PostgresTestDatabase database)
    : IClassFixture<PostgresTestDatabase>
{
    private static readonly DateTimeOffset Created = new(2026, 5, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task One_tenants_list_never_contains_another_tenants_agent()
    {
        Tenant mine = AiAgentPersistenceTests.NewTenant();
        Tenant theirs = AiAgentPersistenceTests.NewTenant();

        await SeedAsync(
            [mine, theirs],
            [
                NewAgent(mine.Id, "Mine A", Created),
                NewAgent(theirs.Id, "Theirs A", Created.AddMinutes(5)),
                NewAgent(theirs.Id, "Theirs B", Created.AddMinutes(6)),
                NewAgent(mine.Id, "Mine B", Created.AddMinutes(1)),
            ]);

        AiAgentPage page = await ListAsync(mine.Id);

        Assert.Equal(2, page.TotalCount);
        Assert.Equal(["Mine B", "Mine A"], page.Items.Select(agent => agent.Name));
        Assert.All(page.Items, agent => Assert.Equal(mine.Id.Value, agent.TenantId));
    }

    [Fact]
    public async Task The_total_count_is_tenant_scoped_too()
    {
        // Counting unscoped would tell a reader how many agents somebody else has
        // without ever showing one of them.
        Tenant mine = AiAgentPersistenceTests.NewTenant();
        Tenant theirs = AiAgentPersistenceTests.NewTenant();

        await SeedAsync(
            [mine, theirs],
            [
                NewAgent(mine.Id, "Mine", Created),
                NewAgent(theirs.Id, "Theirs A", Created),
                NewAgent(theirs.Id, "Theirs B", Created),
                NewAgent(theirs.Id, "Theirs C", Created),
            ]);

        Assert.Equal(1, (await ListAsync(mine.Id)).TotalCount);
        Assert.Equal(3, (await ListAsync(theirs.Id)).TotalCount);
    }

    [Fact]
    public async Task A_tenant_with_no_agents_gets_an_empty_page()
    {
        Tenant tenant = AiAgentPersistenceTests.NewTenant();
        await SeedAsync([tenant], []);

        AiAgentPage page = await ListAsync(tenant.Id);

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task A_tenant_that_does_not_exist_reads_as_empty_rather_than_failing()
    {
        AiAgentPage page = await ListAsync(TenantId.New());

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task The_most_recently_changed_agent_comes_first()
    {
        Tenant tenant = AiAgentPersistenceTests.NewTenant();

        await SeedAsync(
            [tenant],
            [
                NewAgent(tenant.Id, "Oldest", Created),
                NewAgent(tenant.Id, "Newest", Created.AddHours(3)),
                NewAgent(tenant.Id, "Middle", Created.AddHours(1)),
            ]);

        AiAgentPage page = await ListAsync(tenant.Id);

        Assert.Equal(["Newest", "Middle", "Oldest"], page.Items.Select(agent => agent.Name));
    }

    [Fact]
    public async Task Rows_sharing_a_timestamp_are_ordered_by_identity_and_stay_that_way()
    {
        // Real rows with a genuinely identical updated_at. Without the tie-break
        // PostgreSQL is free to return these in any order, and it does not have to
        // pick the same one twice.
        Tenant tenant = AiAgentPersistenceTests.NewTenant();
        AiAgent[] agents =
        [
            NewAgent(tenant.Id, "A", Created),
            NewAgent(tenant.Id, "B", Created),
            NewAgent(tenant.Id, "C", Created),
            NewAgent(tenant.Id, "D", Created),
        ];

        await SeedAsync([tenant], agents);

        AiAgentPage first = await ListAsync(tenant.Id);
        AiAgentPage second = await ListAsync(tenant.Id);

        Assert.Equal(
            first.Items.Select(agent => agent.Id),
            second.Items.Select(agent => agent.Id));
        Assert.Equal(
            first.Items.Select(agent => agent.Id).Order(),
            first.Items.Select(agent => agent.Id));
    }

    [Fact]
    public async Task Paging_covers_every_agent_exactly_once()
    {
        Tenant tenant = AiAgentPersistenceTests.NewTenant();
        AiAgent[] agents =
        [
            .. Enumerable
                .Range(0, 7)
                .Select(index => NewAgent(tenant.Id, $"Agent {index}", Created.AddMinutes(index)))
        ];

        await SeedAsync([tenant], agents);

        AiAgentPage first = await ListAsync(tenant.Id, page: 1, pageSize: 3);
        AiAgentPage second = await ListAsync(tenant.Id, page: 2, pageSize: 3);
        AiAgentPage third = await ListAsync(tenant.Id, page: 3, pageSize: 3);

        Assert.Equal(3, first.Items.Count);
        Assert.Equal(3, second.Items.Count);
        Assert.Single(third.Items);
        Assert.Equal(7, first.TotalCount);

        Guid[] seen =
        [
            .. first.Items.Concat(second.Items).Concat(third.Items).Select(agent => agent.Id)
        ];

        Assert.Equal(7, seen.Distinct().Count());
    }

    [Fact]
    public async Task A_page_size_limits_how_many_rows_come_back()
    {
        Tenant tenant = AiAgentPersistenceTests.NewTenant();
        AiAgent[] agents =
        [
            .. Enumerable
                .Range(0, 6)
                .Select(index => NewAgent(tenant.Id, $"Agent {index}", Created.AddMinutes(index)))
        ];

        await SeedAsync([tenant], agents);

        AiAgentPage page = await ListAsync(tenant.Id, page: 1, pageSize: 2);

        Assert.Equal(2, page.Items.Count);
        Assert.Equal(6, page.TotalCount);
        Assert.Equal(2, page.PageSize);
    }

    [Fact]
    public async Task A_page_past_the_end_is_empty_but_still_reports_the_total()
    {
        Tenant tenant = AiAgentPersistenceTests.NewTenant();
        await SeedAsync([tenant], [NewAgent(tenant.Id, "Only one", Created)]);

        AiAgentPage page = await ListAsync(tenant.Id, page: 4, pageSize: 10);

        Assert.Empty(page.Items);
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task The_instructions_column_is_never_selected()
    {
        // The claim that matters for cost and for confidentiality, and the only
        // place it can actually be observed. The generated SQL is captured and
        // inspected: a projection that merely discarded the value after loading
        // it would pass every behavioural assertion and still read twenty
        // thousand characters per row off disk.
        Tenant tenant = AiAgentPersistenceTests.NewTenant();
        await SeedAsync([tenant], [NewAgent(tenant.Id, "Sales Assistant", Created)]);

        List<string> commands = [];

        // A context of this test's own, so the SQL EF actually sends can be read.
        // Built here rather than by adding a logging hook to the shared fixture:
        // one test needs this, and the fixture is used by dozens.
        DbContextOptions<OmniAiDbContext> options =
            new DbContextOptionsBuilder<OmniAiDbContext>()
                .UseNpgsql(database.ConnectionString)
                .LogTo(
                    commands.Add,
                    [Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.CommandExecuted])
                .Options;

        await using (OmniAiDbContext context = new(options))
        {
            await new AiAgentRepository(context).ListAsync(tenant.Id, 1, 20);
        }

        string select = Assert.Single(
            commands,
            command => command.Contains("FROM ai_agents", StringComparison.OrdinalIgnoreCase)
                && !command.Contains("count(", StringComparison.OrdinalIgnoreCase));

        Assert.DoesNotContain("instructions", select, StringComparison.OrdinalIgnoreCase);

        // The columns a list does show are all there, so this is not passing
        // because the query selected nothing useful.
        foreach (string column in (string[])["id", "tenant_id", "name", "status", "created_at", "updated_at"])
        {
            Assert.Contains(column, select, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task A_summary_carries_the_fields_a_list_needs()
    {
        Tenant tenant = AiAgentPersistenceTests.NewTenant();
        AiAgent agent = NewAgent(tenant.Id, "Sales Assistant", Created);
        agent.Activate(Created.AddHours(1));

        await SeedAsync([tenant], [agent]);

        AiAgentSummary summary = Assert.Single((await ListAsync(tenant.Id)).Items);

        Assert.Equal(agent.Id.Value, summary.Id);
        Assert.Equal(tenant.Id.Value, summary.TenantId);
        Assert.Equal("Sales Assistant", summary.Name);
        Assert.Equal(AiAgentStatus.Active, summary.Status);
        Assert.Equal(Created, summary.CreatedAt);
        Assert.Equal(Created.AddHours(1), summary.UpdatedAt);
    }

    // ------------------------------------------------------------------ helpers

    private async Task<AiAgentPage> ListAsync(TenantId tenantId, int page = 1, int pageSize = 20)
    {
        await using OmniAiDbContext context = database.CreateContext();

        return await new AiAgentRepository(context).ListAsync(tenantId, page, pageSize);
    }

    private static AiAgent NewAgent(TenantId tenantId, string name, DateTimeOffset at) =>
        AiAgent.Create(
            tenantId,
            AiAgentName.Create(name),
            AgentInstructions.Create("Help customers choose suitable products."),
            at);

    private async Task SeedAsync(Tenant[] tenants, AiAgent[] agents)
    {
        await using OmniAiDbContext context = database.CreateContext();

        context.Tenants.AddRange(tenants);
        context.AiAgents.AddRange(agents);

        await context.SaveChangesAsync();
    }
}
