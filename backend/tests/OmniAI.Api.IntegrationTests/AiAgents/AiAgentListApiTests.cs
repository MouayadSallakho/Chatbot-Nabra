using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static OmniAI.Api.IntegrationTests.AiAgents.AiAgentEndpointsTests;

namespace OmniAI.Api.IntegrationTests.AiAgents;

/// <summary>
/// <c>GET /api/tenants/{tenantId}/ai-agents</c> over the real stack.
/// </summary>
/// <remarks>
/// The published contract, exercised through HTTP against real PostgreSQL: the
/// envelope, the projection, the ordering, the paging bounds, and -- the one that
/// matters most -- that no request can produce another tenant's agent.
/// </remarks>
public sealed class AiAgentListApiTests(AiAgentApiFixture api) : IClassFixture<AiAgentApiFixture>
{
    private const string ProblemJson = "application/problem+json";
    private const string Instructions = "Help customers choose suitable products.";

    [Fact]
    public async Task A_tenant_with_no_agents_gets_an_empty_page_not_a_404()
    {
        using HttpClient client = await api.CreateSignedInClientAsync();
        Guid tenantId = await api.CreateTenantAsync(client);

        using HttpResponseMessage response = await client.GetAsync(AgentsRoute(tenantId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        AgentPage page = await ReadPageAsync(response);

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
        Assert.Equal(1, page.Page);
        Assert.Equal(20, page.PageSize);
    }

    /// <summary>
    /// A tenant that exists but is somebody else's, and a tenant that was never
    /// created, are answered identically.
    /// </summary>
    /// <remarks>
    /// <strong>This replaces the Phase 11 rule, deliberately, and the replacement
    /// is stronger.</strong> Phase 11 answered <c>200</c> with an empty page for
    /// an unknown tenant, so that a caller could not tell a real id from an
    /// invented one. With no authentication that was the best available answer.
    /// It is now the weaker one: it would hand a signed-in stranger a successful
    /// response for a workspace they have no relationship with. Membership is the
    /// boundary now, so both cases are <c>404</c> -- existence stays hidden, and
    /// it stays hidden behind a rule that does not depend on guessing.
    /// <para>
    /// The old assertion was narrowed rather than deleted. The property it
    /// protected -- that these two situations are indistinguishable -- is still
    /// asserted here; only the status they share has changed.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_tenant_the_caller_does_not_belong_to_is_answered_like_one_that_never_existed()
    {
        using HttpClient client = await api.CreateSignedInClientAsync();

        // A real tenant, really in the database, that this account has no
        // membership for.
        Guid somebodyElses = await api.CreateUnownedTenantAsync();

        Guid neverIssued = Guid.CreateVersion7();

        using HttpResponseMessage foreign = await client.GetAsync(AgentsRoute(somebodyElses));
        using HttpResponseMessage invented = await client.GetAsync(AgentsRoute(neverIssued));

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, invented.StatusCode);

        // Not merely the same status: the same words. The two bodies differ in
        // exactly two places, and neither is a disclosure -- each echoes back the
        // tenant id its own caller supplied, and each carries its own trace id.
        // Everything a caller could compare to tell the two situations apart is
        // asserted identical here.
        (string foreignTitle, string foreignDetail) = await ProblemAsync(foreign, somebodyElses);
        (string inventedTitle, string inventedDetail) = await ProblemAsync(invented, neverIssued);

        Assert.Equal(inventedTitle, foreignTitle);
        Assert.Equal(inventedDetail, foreignDetail);
    }

    /// <summary>
    /// A problem response's title and its detail with the tenant id blanked out.
    /// </summary>
    private static async Task<(string Title, string Detail)> ProblemAsync(
        HttpResponseMessage response,
        Guid tenantId)
    {
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        string detail = body.RootElement.GetProperty("detail").GetString() ?? string.Empty;

        return (
            body.RootElement.GetProperty("title").GetString() ?? string.Empty,
            detail.Replace(tenantId.ToString(), "<tenant>", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_anonymous_caller_is_told_to_authenticate_rather_than_that_it_is_missing()
    {
        // 401, not 404. The distinction matters to a client: one says "sign in
        // and ask again", the other says "there is nothing here". A signed-out
        // browser tab must be able to tell them apart.
        using HttpClient client = api.CreateClient();
        using HttpClient owner = await api.CreateSignedInClientAsync();

        Guid tenantId = await api.CreateTenantAsync(owner);

        using HttpResponseMessage response = await client.GetAsync(AgentsRoute(tenantId));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_page_carries_the_agents_a_list_needs_and_nothing_more()
    {
        using HttpClient client = await api.CreateSignedInClientAsync();
        Guid tenantId = await api.CreateTenantAsync(client);

        using HttpResponseMessage created =
            await CreateAsync(client, tenantId, "Sales Assistant", Instructions);
        Guid agentId = (await ReadAgentAsync(created)).Id;

        using HttpResponseMessage response = await client.GetAsync(AgentsRoute(tenantId));
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        JsonElement item = body.RootElement.GetProperty("items")[0];

        Assert.Equal(
            ["id", "tenantId", "name", "status", "createdAt", "updatedAt"],
            item.EnumerateObject().Select(field => field.Name));

        Assert.Equal(agentId, item.GetProperty("id").GetGuid());
        Assert.Equal(tenantId, item.GetProperty("tenantId").GetGuid());
        Assert.Equal("Sales Assistant", item.GetProperty("name").GetString());
    }

    [Fact]
    public async Task The_list_never_carries_instructions()
    {
        // The confidentiality and cost claim, asserted on the bytes a client
        // actually receives rather than on a type's shape.
        using HttpClient client = await api.CreateSignedInClientAsync();
        Guid tenantId = await api.CreateTenantAsync(client);

        const string secret = "Never quote a discount above fifteen percent.";

        using (HttpResponseMessage created = await CreateAsync(client, tenantId, "Sales", secret))
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        using HttpResponseMessage response = await client.GetAsync(AgentsRoute(tenantId));
        string json = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain(secret, json, StringComparison.Ordinal);
        Assert.DoesNotContain("instructions", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_list_carries_no_entity_tag()
    {
        // A page is not something a caller may write against, so there is no
        // version to publish. Offering one would invite a conditional write
        // against a representation the client only half read.
        using HttpClient client = await api.CreateSignedInClientAsync();
        Guid tenantId = await api.CreateTenantAsync(client);

        using HttpResponseMessage response = await client.GetAsync(AgentsRoute(tenantId));

        Assert.Null(response.Headers.ETag);
    }

    [Fact]
    public async Task Status_appears_as_a_name_never_as_an_integer()
    {
        using HttpClient client = await api.CreateSignedInClientAsync();
        Guid tenantId = await api.CreateTenantAsync(client);

        using HttpResponseMessage created =
            await CreateAsync(client, tenantId, "Sales Assistant", Instructions);
        Guid agentId = (await ReadAgentAsync(created)).Id;

        await ActAsync(client, tenantId, agentId, "activate", ETagOf(created));

        using HttpResponseMessage response = await client.GetAsync(AgentsRoute(tenantId));
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        JsonElement status = body.RootElement.GetProperty("items")[0].GetProperty("status");

        Assert.Equal(JsonValueKind.String, status.ValueKind);
        Assert.Equal("Active", status.GetString());
    }

    [Fact]
    public async Task Every_lifecycle_state_has_a_published_name()
    {
        using HttpClient client = await api.CreateSignedInClientAsync();
        Guid tenantId = await api.CreateTenantAsync(client);

        using HttpResponseMessage draft = await CreateAsync(client, tenantId, "Draft one", Instructions);
        Guid draftId = (await ReadAgentAsync(draft)).Id;

        using HttpResponseMessage second = await CreateAsync(client, tenantId, "To disable", Instructions);
        Guid disabledId = (await ReadAgentAsync(second)).Id;

        (_, string activated) = await ActAsync(client, tenantId, disabledId, "activate", ETagOf(second));
        await ActAsync(client, tenantId, disabledId, "disable", activated);

        AgentPage page = await ReadPageAsync(await client.GetAsync(AgentsRoute(tenantId)));

        Assert.Equal("Draft", page.Items.Single(agent => agent.Id == draftId).Status);
        Assert.Equal("Disabled", page.Items.Single(agent => agent.Id == disabledId).Status);
    }

    [Fact]
    public async Task One_tenants_list_never_contains_another_tenants_agent()
    {
        using HttpClient client = await api.CreateSignedInClientAsync();
        Guid mine = await api.CreateTenantAsync(client, "Mine");
        Guid theirs = await api.CreateTenantAsync(client, "Theirs");

        using HttpResponseMessage hidden =
            await CreateAsync(client, theirs, "Their Secret Assistant", Instructions);
        Guid hiddenId = (await ReadAgentAsync(hidden)).Id;

        using (HttpResponseMessage ours = await CreateAsync(client, mine, "My Assistant", Instructions))
        {
            Assert.Equal(HttpStatusCode.Created, ours.StatusCode);
        }

        using HttpResponseMessage response = await client.GetAsync(AgentsRoute(mine));
        string json = await response.Content.ReadAsStringAsync();
        AgentPage page = await ReadPageAsync(response);

        Assert.Equal(1, page.TotalCount);
        Assert.DoesNotContain(page.Items, agent => agent.Id == hiddenId);
        Assert.DoesNotContain("Their Secret Assistant", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_newest_change_comes_first()
    {
        using HttpClient client = await api.CreateSignedInClientAsync();
        Guid tenantId = await api.CreateTenantAsync(client);

        using HttpResponseMessage first = await CreateAsync(client, tenantId, "First", Instructions);
        using HttpResponseMessage secondCreated = await CreateAsync(client, tenantId, "Second", Instructions);

        // Touch the first one, which should move it back to the top.
        Guid firstId = (await ReadAgentAsync(first)).Id;
        await SendAsync(
            client,
            HttpMethod.Put,
            $"{AgentRoute(tenantId, firstId)}/name",
            new { name = "First, renamed" },
            ETagOf(first));

        AgentPage page = await ReadPageAsync(await client.GetAsync(AgentsRoute(tenantId)));

        Assert.Equal(["First, renamed", "Second"], page.Items.Select(agent => agent.Name));
        Assert.Equal((await ReadAgentAsync(secondCreated)).Id, page.Items[1].Id);
    }

    [Fact]
    public async Task Paging_splits_the_list_and_reports_the_whole_total()
    {
        using HttpClient client = await api.CreateSignedInClientAsync();
        Guid tenantId = await api.CreateTenantAsync(client);

        for (int index = 0; index < 3; index++)
        {
            using HttpResponseMessage created =
                await CreateAsync(client, tenantId, $"Agent {index}", Instructions);

            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        AgentPage first = await ReadPageAsync(await client.GetAsync($"{AgentsRoute(tenantId)}?page=1&pageSize=2"));
        AgentPage second = await ReadPageAsync(await client.GetAsync($"{AgentsRoute(tenantId)}?page=2&pageSize=2"));

        Assert.Equal(2, first.Items.Count);
        Assert.Single(second.Items);
        Assert.Equal(3, first.TotalCount);
        Assert.Equal(3, second.TotalCount);
        Assert.Equal(2, first.PageSize);
        Assert.Equal(2, second.Page);

        Assert.Empty(first.Items.Select(agent => agent.Id).Intersect(second.Items.Select(agent => agent.Id)));
    }

    [Theory]
    [InlineData("?page=0")]
    [InlineData("?page=-1")]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=-3")]
    [InlineData("?pageSize=101")]
    [InlineData("?page=0&pageSize=0")]
    public async Task An_out_of_range_page_request_is_refused_as_problem_details(string query)
    {
        using HttpClient client = await api.CreateSignedInClientAsync();
        Guid tenantId = await api.CreateTenantAsync(client);

        using HttpResponseMessage response = await client.GetAsync($"{AgentsRoute(tenantId)}{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task An_oversized_page_is_refused_rather_than_quietly_reduced()
    {
        // The important half: a caller that asked for more must not receive the
        // maximum and be unable to tell truncation from the end of the list.
        using HttpClient client = await api.CreateSignedInClientAsync();
        Guid tenantId = await api.CreateTenantAsync(client);

        using HttpResponseMessage response =
            await client.GetAsync($"{AgentsRoute(tenantId)}?pageSize=500");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();

        Assert.Contains("100", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_maximum_page_size_is_accepted()
    {
        using HttpClient client = await api.CreateSignedInClientAsync();
        Guid tenantId = await api.CreateTenantAsync(client);

        using HttpResponseMessage response =
            await client.GetAsync($"{AgentsRoute(tenantId)}?pageSize=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(100, (await ReadPageAsync(response)).PageSize);
    }

    [Fact]
    public async Task A_page_value_that_is_not_a_number_is_rejected_without_reaching_the_handler()
    {
        using HttpClient client = await api.CreateSignedInClientAsync();
        Guid tenantId = await api.CreateTenantAsync(client);

        using HttpResponseMessage response =
            await client.GetAsync($"{AgentsRoute(tenantId)}?page=first");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_error_body_names_no_database_or_provider_internals()
    {
        using HttpClient client = await api.CreateSignedInClientAsync();
        Guid tenantId = await api.CreateTenantAsync(client);

        using HttpResponseMessage response = await client.GetAsync($"{AgentsRoute(tenantId)}?page=0");
        string body = await response.Content.ReadAsStringAsync();

        foreach (string forbidden in Internals)
        {
            Assert.DoesNotContain(forbidden, body, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Words that must never reach a client in an error body.</summary>
    private static readonly string[] Internals =
        ["npgsql", "postgres", "xmin", "sql", "openai", "stack", "System."];

    // ------------------------------------------------------------------ helpers

    private static async Task<AgentPage> ReadPageAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadFromJsonAsync<AgentPage>()
            ?? throw new InvalidOperationException("The response carried no page.");
    }

    private static string AgentsRoute(Guid tenantId) =>
        string.Create(CultureInfo.InvariantCulture, $"/api/tenants/{tenantId}/ai-agents");

    private sealed record AgentPage(
        IReadOnlyList<Summary> Items,
        int Page,
        int PageSize,
        int TotalCount);

    private sealed record Summary(
        Guid Id,
        Guid TenantId,
        string Name,
        string Status,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
