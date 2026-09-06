using OmniAI.Application.AiAgents.ListAiAgents;
using OmniAI.Domain;
using OmniAI.Application.Tests.Security;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Tests.AiAgents;

/// <summary>
/// Listing a tenant's AI agents.
/// </summary>
/// <remarks>
/// Two things are worth proving at this level and neither needs a database: that
/// the tenant reaches the store rather than being applied to results afterwards,
/// and that the paging contract is enforced before anything is read. The real
/// SQL ordering and projection are proved against PostgreSQL in the
/// Infrastructure suite, where they can actually be observed.
/// </remarks>
public sealed class ListAiAgentsHandlerTests
{
    private static readonly DateTimeOffset Created = new(2026, 5, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_tenant_sees_only_its_own_agents()
    {
        TenantId mine = TenantId.New();
        TenantId theirs = TenantId.New();

        InMemoryAiAgentRepository agents = new InMemoryAiAgentRepository()
            .Seed(NewAgent(mine, "Sales Assistant", Created))
            .Seed(NewAgent(theirs, "Their Assistant", Created.AddMinutes(1)))
            .Seed(NewAgent(mine, "Support Assistant", Created.AddMinutes(2)));

        AiAgentPage page = await new ListAiAgentsHandler(agents, TestAuthority.Anywhere())
            .HandleAsync(new ListAiAgentsQuery(mine.Value));

        Assert.Equal(2, page.TotalCount);
        Assert.All(page.Items, agent => Assert.Equal(mine.Value, agent.TenantId));
        Assert.DoesNotContain(page.Items, agent => agent.Name == "Their Assistant");
    }

    [Fact]
    public async Task Another_tenants_count_never_leaks_into_the_total()
    {
        // The total is what a pager displays. Counting unscoped would tell a
        // reader how many agents somebody else has, without ever showing one.
        TenantId mine = TenantId.New();
        TenantId theirs = TenantId.New();

        InMemoryAiAgentRepository agents = new InMemoryAiAgentRepository()
            .Seed(NewAgent(mine, "Mine", Created))
            .Seed(NewAgent(theirs, "Theirs One", Created))
            .Seed(NewAgent(theirs, "Theirs Two", Created));

        AiAgentPage page = await new ListAiAgentsHandler(agents, TestAuthority.Anywhere())
            .HandleAsync(new ListAiAgentsQuery(mine.Value));

        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task A_tenant_with_no_agents_gets_an_empty_page_rather_than_a_failure()
    {
        AiAgentPage page = await new ListAiAgentsHandler(new InMemoryAiAgentRepository(), TestAuthority.Anywhere())
            .HandleAsync(new ListAiAgentsQuery(Guid.CreateVersion7()));

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task A_tenant_that_does_not_exist_is_answered_exactly_like_one_with_no_agents()
    {
        // Deliberately indistinguishable. A difference here would turn the
        // listing endpoint into a way to discover which tenant ids are real.
        InMemoryAiAgentRepository agents = new InMemoryAiAgentRepository()
            .Seed(NewAgent(TenantId.New(), "Somebody else's", Created));

        AiAgentPage unknown = await new ListAiAgentsHandler(agents, TestAuthority.Anywhere())
            .HandleAsync(new ListAiAgentsQuery(Guid.CreateVersion7()));

        Assert.Empty(unknown.Items);
        Assert.Equal(0, unknown.TotalCount);
    }

    [Fact]
    public async Task The_newest_change_comes_first()
    {
        TenantId tenant = TenantId.New();

        InMemoryAiAgentRepository agents = new InMemoryAiAgentRepository()
            .Seed(NewAgent(tenant, "Oldest", Created))
            .Seed(NewAgent(tenant, "Newest", Created.AddHours(2)))
            .Seed(NewAgent(tenant, "Middle", Created.AddHours(1)));

        AiAgentPage page = await new ListAiAgentsHandler(agents, TestAuthority.Anywhere())
            .HandleAsync(new ListAiAgentsQuery(tenant.Value));

        Assert.Equal(["Newest", "Middle", "Oldest"], page.Items.Select(agent => agent.Name));
    }

    [Fact]
    public async Task Agents_sharing_a_timestamp_are_still_ordered_deterministically()
    {
        // Two agents created in one transaction share an updatedAt exactly.
        // Without a tie-break their relative order is whatever storage felt like,
        // and consecutive pages can then repeat or skip a row.
        TenantId tenant = TenantId.New();

        InMemoryAiAgentRepository agents = new InMemoryAiAgentRepository()
            .Seed(NewAgent(tenant, "A", Created))
            .Seed(NewAgent(tenant, "B", Created))
            .Seed(NewAgent(tenant, "C", Created));

        AiAgentPage first = await new ListAiAgentsHandler(agents, TestAuthority.Anywhere())
            .HandleAsync(new ListAiAgentsQuery(tenant.Value));
        AiAgentPage again = await new ListAiAgentsHandler(agents, TestAuthority.Anywhere())
            .HandleAsync(new ListAiAgentsQuery(tenant.Value));

        Assert.Equal(first.Items.Select(agent => agent.Id), again.Items.Select(agent => agent.Id));
        Assert.Equal(
            first.Items.Select(agent => agent.Id).Order(),
            first.Items.Select(agent => agent.Id));
    }

    [Fact]
    public async Task Paging_splits_the_tenants_agents_without_repeating_or_losing_one()
    {
        TenantId tenant = TenantId.New();
        InMemoryAiAgentRepository agents = new();

        for (int index = 0; index < 5; index++)
        {
            agents.Seed(NewAgent(tenant, $"Agent {index}", Created.AddMinutes(index)));
        }

        AiAgentPage first = await new ListAiAgentsHandler(agents, TestAuthority.Anywhere())
            .HandleAsync(new ListAiAgentsQuery(tenant.Value, Page: 1, PageSize: 2));
        AiAgentPage second = await new ListAiAgentsHandler(agents, TestAuthority.Anywhere())
            .HandleAsync(new ListAiAgentsQuery(tenant.Value, Page: 2, PageSize: 2));
        AiAgentPage third = await new ListAiAgentsHandler(agents, TestAuthority.Anywhere())
            .HandleAsync(new ListAiAgentsQuery(tenant.Value, Page: 3, PageSize: 2));

        Assert.Equal(2, first.Items.Count);
        Assert.Equal(2, second.Items.Count);
        Assert.Single(third.Items);
        Assert.Equal(5, first.TotalCount);

        Guid[] seen =
        [
            .. first.Items.Concat(second.Items).Concat(third.Items).Select(agent => agent.Id)
        ];

        Assert.Equal(5, seen.Distinct().Count());
    }

    [Fact]
    public async Task A_page_beyond_the_end_is_empty_but_still_reports_the_total()
    {
        TenantId tenant = TenantId.New();
        InMemoryAiAgentRepository agents = new InMemoryAiAgentRepository()
            .Seed(NewAgent(tenant, "Only one", Created));

        AiAgentPage page = await new ListAiAgentsHandler(agents, TestAuthority.Anywhere())
            .HandleAsync(new ListAiAgentsQuery(tenant.Value, Page: 9));

        Assert.Empty(page.Items);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(9, page.Page);
    }

    [Fact]
    public async Task Omitting_the_paging_arguments_takes_the_documented_defaults()
    {
        AiAgentPage page = await new ListAiAgentsHandler(new InMemoryAiAgentRepository(), TestAuthority.Anywhere())
            .HandleAsync(new ListAiAgentsQuery(Guid.CreateVersion7()));

        Assert.Equal(AiAgentPage.DefaultPage, page.Page);
        Assert.Equal(AiAgentPage.DefaultPageSize, page.PageSize);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task A_page_below_the_first_is_refused(int page)
    {
        // Not reinterpreted as page 1. A caller sending ?page=0 made a mistake,
        // and silently correcting it hides the mistake rather than the symptom.
        await Assert.ThrowsAsync<AiAgentPageValidationException>(() =>
            new ListAiAgentsHandler(new InMemoryAiAgentRepository(), TestAuthority.Anywhere())
                .HandleAsync(new ListAiAgentsQuery(Guid.CreateVersion7(), Page: page)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task A_page_size_below_one_is_refused(int pageSize)
    {
        await Assert.ThrowsAsync<AiAgentPageValidationException>(() =>
            new ListAiAgentsHandler(new InMemoryAiAgentRepository(), TestAuthority.Anywhere())
                .HandleAsync(new ListAiAgentsQuery(Guid.CreateVersion7(), PageSize: pageSize)));
    }

    [Fact]
    public async Task A_page_size_beyond_the_maximum_is_refused_rather_than_clamped()
    {
        // The important half of the assertion is that it throws rather than
        // returning a hundred: a caller that asked for more and silently received
        // the maximum cannot tell a truncated page from the end of the list.
        AiAgentPageValidationException refused =
            await Assert.ThrowsAsync<AiAgentPageValidationException>(() =>
                new ListAiAgentsHandler(new InMemoryAiAgentRepository(), TestAuthority.Anywhere())
                    .HandleAsync(new ListAiAgentsQuery(
                        Guid.CreateVersion7(),
                        PageSize: AiAgentPage.MaxPageSize + 1)));

        Assert.Contains(
            AiAgentPage.MaxPageSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
            refused.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_maximum_page_size_itself_is_allowed()
    {
        AiAgentPage page = await new ListAiAgentsHandler(new InMemoryAiAgentRepository(), TestAuthority.Anywhere())
            .HandleAsync(new ListAiAgentsQuery(
                Guid.CreateVersion7(),
                PageSize: AiAgentPage.MaxPageSize));

        Assert.Equal(AiAgentPage.MaxPageSize, page.PageSize);
    }

    [Fact]
    public async Task An_empty_tenant_identifier_is_rejected_as_malformed_input()
    {
        // Not a lookup that happens to find nothing: an empty GUID is a
        // programming mistake, and answering it with an empty list would hide it.
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            new ListAiAgentsHandler(new InMemoryAiAgentRepository(), TestAuthority.Anywhere())
                .HandleAsync(new ListAiAgentsQuery(Guid.Empty)));
    }

    [Fact]
    public async Task The_summary_carries_no_instructions()
    {
        // The type has no such property, so this is really a guard against
        // somebody adding one later: a list must not publish the tenant's
        // configured business behaviour, and the query behind it does not even
        // select the column.
        TenantId tenant = TenantId.New();
        InMemoryAiAgentRepository agents = new InMemoryAiAgentRepository()
            .Seed(NewAgent(tenant, "Sales Assistant", Created));

        AiAgentPage page = await new ListAiAgentsHandler(agents, TestAuthority.Anywhere())
            .HandleAsync(new ListAiAgentsQuery(tenant.Value));

        Assert.DoesNotContain(
            typeof(AiAgentSummary).GetProperties(),
            property => property.Name.Contains("Instruction", StringComparison.OrdinalIgnoreCase));
        Assert.Single(page.Items);
    }

    private static AiAgent NewAgent(TenantId tenant, string name, DateTimeOffset at) =>
        AiAgent.Create(
            tenant,
            AiAgentName.Create(name),
            AgentInstructions.Create("Help customers choose suitable products."),
            at);
}
