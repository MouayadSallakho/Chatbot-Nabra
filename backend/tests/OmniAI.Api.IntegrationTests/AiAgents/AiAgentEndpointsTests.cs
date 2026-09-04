using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace OmniAI.Api.IntegrationTests.AiAgents;

/// <summary>
/// The AI agent slice over HTTP, end to end, against real PostgreSQL.
/// </summary>
public sealed class AiAgentEndpointsTests(AiAgentApiFixture api) : IClassFixture<AiAgentApiFixture>
{
    private const string ProblemJson = "application/problem+json";

    private const string Instructions = "Help customers choose suitable products.";

    // ------------------------------------------------------------------- create

    [Fact]
    public async Task Creating_an_agent_returns_201_with_the_created_representation()
    {
        using HttpClient client = api.CreateClient();
        Guid tenantId = await api.CreateTenantAsync();

        using HttpResponseMessage response =
            await CreateAsync(client, tenantId, "Sales Assistant", Instructions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        AgentPayload created = await ReadAgentAsync(response);

        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal(tenantId, created.TenantId);
        Assert.Equal("Sales Assistant", created.Name);
        Assert.Equal(Instructions, created.Instructions);
        Assert.Equal("Draft", created.Status);
    }

    [Fact]
    public async Task Creating_an_agent_returns_a_strong_etag()
    {
        // Returned on creation so a client can make its first conditional write
        // without reading the agent back.
        using HttpClient client = api.CreateClient();
        Guid tenantId = await api.CreateTenantAsync();

        using HttpResponseMessage response =
            await CreateAsync(client, tenantId, "Sales Assistant", Instructions);

        AssertStrongETag(response);
    }

    [Fact]
    public async Task The_etag_from_create_matches_the_one_a_read_returns()
    {
        // The version in the create response must describe the row that was
        // actually stored, or the client's first conditional write would fail
        // against state nobody changed.
        using HttpClient client = api.CreateClient();
        Guid tenantId = await api.CreateTenantAsync();

        using HttpResponseMessage created =
            await CreateAsync(client, tenantId, "Sales Assistant", Instructions);
        AgentPayload payload = await ReadAgentAsync(created);

        using HttpResponseMessage read = await client.GetAsync(AgentRoute(tenantId, payload.Id));

        Assert.Equal(ETagOf(created), ETagOf(read));
    }

    [Fact]
    public async Task Creating_an_agent_sets_a_location_header_pointing_at_the_tenant_scoped_read_endpoint()
    {
        using HttpClient client = api.CreateClient();
        Guid tenantId = await api.CreateTenantAsync();

        using HttpResponseMessage response =
            await CreateAsync(client, tenantId, "Sales Assistant", Instructions);
        AgentPayload created = await ReadAgentAsync(response);

        Assert.NotNull(response.Headers.Location);

        // CreatedAtRoute emits an absolute URI, which is valid; what matters is
        // that it addresses the read endpoint for this agent under this tenant --
        // not a tenant-less path that would bypass the isolation boundary.
        Assert.Equal(
            $"/api/tenants/{tenantId}/ai-agents/{created.Id}",
            response.Headers.Location.AbsolutePath,
            ignoreCase: true);

        using HttpResponseMessage followed = await client.GetAsync(response.Headers.Location);

        Assert.Equal(HttpStatusCode.OK, followed.StatusCode);
    }

    [Fact]
    public async Task A_created_agent_is_in_the_database()
    {
        using HttpClient client = api.CreateClient();
        Guid tenantId = await api.CreateTenantAsync();

        using HttpResponseMessage response =
            await CreateAsync(client, tenantId, "Sales Assistant", Instructions);
        AgentPayload created = await ReadAgentAsync(response);

        // Read straight from PostgreSQL, bypassing the API entirely.
        object? stored = await api.Database.ExecuteScalarAsync(
            "SELECT name || '|' || status || '|' || tenant_id::text FROM ai_agents " +
            $"WHERE id = '{created.Id}'");

        Assert.Equal($"Sales Assistant|Draft|{tenantId}", stored as string);
    }

    [Fact]
    public async Task Creation_stores_and_returns_the_domain_canonical_values()
    {
        using HttpClient client = api.CreateClient();
        Guid tenantId = await api.CreateTenantAsync();

        using HttpResponseMessage response = await CreateAsync(
            client, tenantId, "   Sales Assistant   ", "\n  Be brief.\n\n  Escalate refunds.  \n");

        AgentPayload created = await ReadAgentAsync(response);

        Assert.Equal("Sales Assistant", created.Name);
        Assert.Equal("Be brief.\n\n  Escalate refunds.", created.Instructions);
    }

    [Fact]
    public async Task Multiline_instructions_survive_the_whole_round_trip()
    {
        // JSON escaping, model binding, the value object, the value converter,
        // PostgreSQL, and back out again.
        const string multiline =
            "Rules:\n1. Be brief.\n2. Never quote a discount.\n\n\tEscalate refunds to a human.";

        using HttpClient client = api.CreateClient();
        Guid tenantId = await api.CreateTenantAsync();

        using HttpResponseMessage response =
            await CreateAsync(client, tenantId, "Sales Assistant", multiline);
        AgentPayload created = await ReadAgentAsync(response);

        using HttpResponseMessage read = await client.GetAsync(AgentRoute(tenantId, created.Id));

        Assert.Equal(multiline, (await ReadAgentAsync(read)).Instructions);

        object? stored = await api.Database.ExecuteScalarAsync(
            $"SELECT instructions FROM ai_agents WHERE id = '{created.Id}'");

        Assert.Equal(multiline, stored as string);
    }

    [Fact]
    public async Task A_created_agent_has_matching_created_and_updated_instants_in_utc()
    {
        using HttpClient client = api.CreateClient();
        Guid tenantId = await api.CreateTenantAsync();

        AgentPayload created = await ReadAgentAsync(
            await CreateAsync(client, tenantId, "Sales Assistant", Instructions));

        Assert.Equal(created.CreatedAt, created.UpdatedAt);
        Assert.Equal(TimeSpan.Zero, created.CreatedAt.Offset);
    }

    [Fact]
    public async Task The_caller_cannot_choose_the_identity_status_or_timestamps()
    {
        using HttpClient client = api.CreateClient();
        Guid tenantId = await api.CreateTenantAsync();
        Guid attempted = Guid.CreateVersion7();

        string body = $$"""
            {
              "id": "{{attempted}}",
              "tenantId": "{{Guid.CreateVersion7()}}",
              "name": "Sales Assistant",
              "instructions": "{{Instructions}}",
              "status": "Active",
              "createdAt": "2000-01-01T00:00:00Z",
              "model": "some-model",
              "temperature": 0.7
            }
            """;

        using HttpResponseMessage response = await client.PostAsync(
            AgentsRoute(tenantId),
            new StringContent(body, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        AgentPayload created = await ReadAgentAsync(response);

        Assert.NotEqual(attempted, created.Id);
        Assert.Equal(tenantId, created.TenantId);
        Assert.Equal("Draft", created.Status);
        Assert.NotEqual(2000, created.CreatedAt.Year);
    }

    [Fact]
    public async Task Creation_needs_no_if_match()
    {
        // There is no previous version of a resource that does not exist yet, so
        // there is nothing to be out of date about.
        using HttpClient client = api.CreateClient();
        Guid tenantId = await api.CreateTenantAsync();

        using HttpResponseMessage response =
            await CreateAsync(client, tenantId, "Sales Assistant", Instructions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // ---------------------------------------------------------------------- get

    [Fact]
    public async Task Reading_an_agent_returns_200_with_a_strong_etag()
    {
        using HttpClient client = api.CreateClient();
        (Guid tenantId, Guid agentId, _) = await ArrangeAsync(client);

        using HttpResponseMessage response = await client.GetAsync(AgentRoute(tenantId, agentId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertStrongETag(response);
    }

    [Fact]
    public async Task Repeated_reads_of_an_unchanged_agent_return_the_same_etag()
    {
        using HttpClient client = api.CreateClient();
        (Guid tenantId, Guid agentId, _) = await ArrangeAsync(client);

        using HttpResponseMessage first = await client.GetAsync(AgentRoute(tenantId, agentId));
        using HttpResponseMessage second = await client.GetAsync(AgentRoute(tenantId, agentId));

        Assert.Equal(ETagOf(first), ETagOf(second));
    }

    [Fact]
    public async Task Reading_needs_no_if_match()
    {
        using HttpClient client = api.CreateClient();
        (Guid tenantId, Guid agentId, _) = await ArrangeAsync(client);

        using HttpResponseMessage response = await client.GetAsync(AgentRoute(tenantId, agentId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_agent_returns_404_as_problem_details()
    {
        using HttpClient client = api.CreateClient();
        Guid tenantId = await api.CreateTenantAsync();

        using HttpResponseMessage response =
            await client.GetAsync(AgentRoute(tenantId, Guid.CreateVersion7()));

        await AssertProblemAsync(response, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_response_body_carries_no_concurrency_or_persistence_metadata()
    {
        // The version travels in a header. Seven fields, and no xmin, version,
        // rowVersion, transaction id, provider, model or external identifier.
        using HttpClient client = api.CreateClient();
        (Guid tenantId, Guid agentId, _) = await ArrangeAsync(client);

        using HttpResponseMessage response = await client.GetAsync(AgentRoute(tenantId, agentId));

        string json = await response.Content.ReadAsStringAsync();
        using JsonDocument document = JsonDocument.Parse(json);

        string[] properties =
        [
            .. document.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)
        ];

        Assert.Equal(
            ["createdAt", "id", "instructions", "name", "status", "tenantId", "updatedAt"],
            properties);

        foreach (string forbidden in (string[])["xmin", "version", "rowVersion", "etag", "transactionId"])
        {
            Assert.DoesNotContain(forbidden, json, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task The_status_is_a_stable_name_not_an_integer_ordinal()
    {
        using HttpClient client = api.CreateClient();
        (Guid tenantId, Guid agentId, _) = await ArrangeAsync(client);

        using HttpResponseMessage response = await client.GetAsync(AgentRoute(tenantId, agentId));

        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement status = document.RootElement.GetProperty("status");

        Assert.Equal(JsonValueKind.String, status.ValueKind);
        Assert.Equal("Draft", status.GetString());
    }

    // --------------------------------------------------------------- validation

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Sales\nAssistant")]
    public async Task An_unacceptable_name_returns_400_as_problem_details(string? name)
    {
        using HttpClient client = api.CreateClient();
        Guid tenantId = await api.CreateTenantAsync();

        using HttpResponseMessage response = await CreateAsync(client, tenantId, name, Instructions);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Unacceptable_instructions_return_400_as_problem_details(string? instructions)
    {
        using HttpClient client = api.CreateClient();
        Guid tenantId = await api.CreateTenantAsync();

        using HttpResponseMessage response =
            await CreateAsync(client, tenantId, "Sales Assistant", instructions);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_over_long_name_returns_400_and_is_not_truncated()
    {
        using HttpClient client = api.CreateClient();
        Guid tenantId = await api.CreateTenantAsync();

        using HttpResponseMessage response =
            await CreateAsync(client, tenantId, new string('a', 121), Instructions);

        string detail = await AssertProblemAsync(response, HttpStatusCode.BadRequest);

        Assert.Contains("not truncated", detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_malformed_body_is_rejected_at_the_transport_layer()
    {
        using HttpClient client = api.CreateClient();
        Guid tenantId = await api.CreateTenantAsync();

        using HttpResponseMessage response = await client.PostAsync(
            AgentsRoute(tenantId),
            new StringContent("{ this is not json", Encoding.UTF8, "application/json"));

        string detail = await AssertProblemAsync(response, HttpStatusCode.BadRequest);

        Assert.DoesNotContain("this is not json", detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_empty_tenant_guid_in_the_route_returns_400()
    {
        using HttpClient client = api.CreateClient();

        using HttpResponseMessage response =
            await CreateAsync(client, Guid.Empty, "Sales Assistant", Instructions);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_empty_agent_guid_in_the_route_returns_400()
    {
        using HttpClient client = api.CreateClient();
        Guid tenantId = await api.CreateTenantAsync();

        using HttpResponseMessage response = await client.GetAsync(AgentRoute(tenantId, Guid.Empty));

        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_non_guid_route_segment_does_not_match_the_route()
    {
        using HttpClient client = api.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/api/tenants/not-a-guid/ai-agents/also-not");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Creating_an_agent_for_an_unknown_tenant_returns_404()
    {
        using HttpClient client = api.CreateClient();

        using HttpResponseMessage response =
            await CreateAsync(client, Guid.CreateVersion7(), "Sales Assistant", Instructions);

        await AssertProblemAsync(response, HttpStatusCode.NotFound);
    }

    // ---------------------------------------------------------------- lifecycle

    [Fact]
    public async Task An_agent_moves_through_draft_active_disabled_and_back_to_active()
    {
        using HttpClient client = api.CreateClient();
        (Guid tenantId, Guid agentId, string version) = await ArrangeAsync(client);

        (AgentPayload active, string v2) = await ActAsync(client, tenantId, agentId, "activate", version);
        Assert.Equal("Active", active.Status);

        (AgentPayload disabled, string v3) = await ActAsync(client, tenantId, agentId, "disable", v2);
        Assert.Equal("Disabled", disabled.Status);

        (AgentPayload again, _) = await ActAsync(client, tenantId, agentId, "activate", v3);
        Assert.Equal("Active", again.Status);

        object? stored = await api.Database.ExecuteScalarAsync(
            $"SELECT status FROM ai_agents WHERE id = '{agentId}'");

        Assert.Equal("Active", stored as string);
    }

    [Fact]
    public async Task Every_successful_lifecycle_step_returns_a_new_etag()
    {
        using HttpClient client = api.CreateClient();
        (Guid tenantId, Guid agentId, string version) = await ArrangeAsync(client);

        (_, string afterActivate) = await ActAsync(client, tenantId, agentId, "activate", version);
        (_, string afterDisable) = await ActAsync(client, tenantId, agentId, "disable", afterActivate);

        Assert.NotEqual(version, afterActivate);
        Assert.NotEqual(afterActivate, afterDisable);
    }

    [Fact]
    public async Task Disabling_a_draft_agent_returns_409()
    {
        // The Domain state rule surfacing as a conflict. Distinct from 412: the
        // caller's version is current, so it has seen the agent as it is -- the
        // operation simply contradicts that state.
        using HttpClient client = api.CreateClient();
        (Guid tenantId, Guid agentId, string version) = await ArrangeAsync(client);

        using HttpResponseMessage response =
            await SendAsync(client, HttpMethod.Post, ActionRoute(tenantId, agentId, "disable"), null, version);

        string detail = await AssertProblemAsync(response, HttpStatusCode.Conflict);

        Assert.Contains("draft", detail, StringComparison.OrdinalIgnoreCase);

        object? stored = await api.Database.ExecuteScalarAsync(
            $"SELECT status FROM ai_agents WHERE id = '{agentId}'");

        Assert.Equal("Draft", stored as string);
    }

    [Fact]
    public async Task Activating_an_already_active_agent_is_an_idempotent_200_with_an_unchanged_etag()
    {
        // Nothing was written, so the version did not move -- and the response
        // says so, which is the truthful account of a request that changed
        // nothing.
        using HttpClient client = api.CreateClient();
        (Guid tenantId, Guid agentId, string version) = await ArrangeAsync(client);

        (AgentPayload first, string afterActivate) =
            await ActAsync(client, tenantId, agentId, "activate", version);
        (AgentPayload again, string afterRepeat) =
            await ActAsync(client, tenantId, agentId, "activate", afterActivate);

        Assert.Equal("Active", again.Status);
        Assert.Equal(first.UpdatedAt, again.UpdatedAt);
        Assert.Equal(afterActivate, afterRepeat);
    }

    [Fact]
    public async Task Disabling_an_already_disabled_agent_is_an_idempotent_200()
    {
        using HttpClient client = api.CreateClient();
        (Guid tenantId, Guid agentId, string version) = await ArrangeAsync(client);

        (_, string v2) = await ActAsync(client, tenantId, agentId, "activate", version);
        (AgentPayload first, string v3) = await ActAsync(client, tenantId, agentId, "disable", v2);
        (AgentPayload again, string v4) = await ActAsync(client, tenantId, agentId, "disable", v3);

        Assert.Equal("Disabled", again.Status);
        Assert.Equal(first.UpdatedAt, again.UpdatedAt);
        Assert.Equal(v3, v4);
    }

    [Fact]
    public async Task Lifecycle_operations_on_an_unknown_agent_return_404()
    {
        using HttpClient client = api.CreateClient();
        (Guid tenantId, _, string version) = await ArrangeAsync(client);
        Guid unknown = Guid.CreateVersion7();

        foreach (string action in (string[])["activate", "disable"])
        {
            using HttpResponseMessage response = await SendAsync(
                client, HttpMethod.Post, ActionRoute(tenantId, unknown, action), null, version);

            await AssertProblemAsync(response, HttpStatusCode.NotFound);
        }
    }

    // ------------------------------------------------------------------- rename

    [Fact]
    public async Task Renaming_with_the_current_etag_returns_200_and_persists_it()
    {
        using HttpClient client = api.CreateClient();
        (Guid tenantId, Guid agentId, string version) = await ArrangeAsync(client);

        using HttpResponseMessage response = await SendAsync(
            client,
            HttpMethod.Put,
            $"{AgentRoute(tenantId, agentId)}/name",
            new { name = "Pre-sales Assistant" },
            version);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertStrongETag(response);
        Assert.NotEqual(version, ETagOf(response));

        Assert.Equal("Pre-sales Assistant", (await ReadAgentAsync(response)).Name);

        object? stored = await api.Database.ExecuteScalarAsync(
            $"SELECT name FROM ai_agents WHERE id = '{agentId}'");

        Assert.Equal("Pre-sales Assistant", stored as string);
    }

    [Fact]
    public async Task A_rename_etag_matches_what_the_next_read_returns()
    {
        // Consecutive edits must be possible without re-reading, so the version a
        // mutation hands back has to be the one the resource is actually at.
        using HttpClient client = api.CreateClient();
        (Guid tenantId, Guid agentId, string version) = await ArrangeAsync(client);

        using HttpResponseMessage renamed = await SendAsync(
            client, HttpMethod.Put, $"{AgentRoute(tenantId, agentId)}/name",
            new { name = "Renamed" }, version);

        using HttpResponseMessage read = await client.GetAsync(AgentRoute(tenantId, agentId));

        Assert.Equal(ETagOf(renamed), ETagOf(read));
    }

    [Fact]
    public async Task Consecutive_edits_can_chain_etags_without_re_reading()
    {
        using HttpClient client = api.CreateClient();
        (Guid tenantId, Guid agentId, string version) = await ArrangeAsync(client);

        using HttpResponseMessage first = await SendAsync(
            client, HttpMethod.Put, $"{AgentRoute(tenantId, agentId)}/name",
            new { name = "First" }, version);

        using HttpResponseMessage second = await SendAsync(
            client, HttpMethod.Put, $"{AgentRoute(tenantId, agentId)}/name",
            new { name = "Second" }, ETagOf(first));

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal("Second", (await ReadAgentAsync(second)).Name);
    }

    [Fact]
    public async Task Renaming_to_an_unacceptable_name_returns_400_and_changes_nothing()
    {
        using HttpClient client = api.CreateClient();
        (Guid tenantId, Guid agentId, string version) = await ArrangeAsync(client);

        using HttpResponseMessage response = await SendAsync(
            client, HttpMethod.Put, $"{AgentRoute(tenantId, agentId)}/name",
            new { name = "   " }, version);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest);

        object? stored = await api.Database.ExecuteScalarAsync(
            $"SELECT name FROM ai_agents WHERE id = '{agentId}'");

        Assert.Equal("Sales Assistant", stored as string);
    }

    [Fact]
    public async Task Renaming_an_unknown_agent_returns_404()
    {
        using HttpClient client = api.CreateClient();
        (Guid tenantId, _, string version) = await ArrangeAsync(client);

        using HttpResponseMessage response = await SendAsync(
            client, HttpMethod.Put, $"{AgentRoute(tenantId, Guid.CreateVersion7())}/name",
            new { name = "Renamed" }, version);

        await AssertProblemAsync(response, HttpStatusCode.NotFound);
    }

    // ------------------------------------------------------------- instructions

    [Fact]
    public async Task Updating_instructions_returns_200_and_persists_the_exact_text()
    {
        const string replacement = "Answer support questions.\n\n\tEscalate billing to a human.";

        using HttpClient client = api.CreateClient();
        (Guid tenantId, Guid agentId, string version) = await ArrangeAsync(client);

        using HttpResponseMessage response = await SendAsync(
            client, HttpMethod.Put, $"{AgentRoute(tenantId, agentId)}/instructions",
            new { instructions = replacement }, version);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertStrongETag(response);
        Assert.Equal(replacement, (await ReadAgentAsync(response)).Instructions);

        object? stored = await api.Database.ExecuteScalarAsync(
            $"SELECT instructions FROM ai_agents WHERE id = '{agentId}'");

        Assert.Equal(replacement, stored as string);
    }

    [Fact]
    public async Task Instructions_can_be_updated_while_the_agent_is_active()
    {
        using HttpClient client = api.CreateClient();
        (Guid tenantId, Guid agentId, string version) = await ArrangeAsync(client);

        (_, string activated) = await ActAsync(client, tenantId, agentId, "activate", version);

        using HttpResponseMessage response = await SendAsync(
            client, HttpMethod.Put, $"{AgentRoute(tenantId, agentId)}/instructions",
            new { instructions = "Corrected." }, activated);

        AgentPayload updated = await ReadAgentAsync(response);

        Assert.Equal("Corrected.", updated.Instructions);
        Assert.Equal("Active", updated.Status);
    }

    [Fact]
    public async Task Updating_instructions_to_a_blank_value_returns_400_and_changes_nothing()
    {
        using HttpClient client = api.CreateClient();
        (Guid tenantId, Guid agentId, string version) = await ArrangeAsync(client);

        using HttpResponseMessage response = await SendAsync(
            client, HttpMethod.Put, $"{AgentRoute(tenantId, agentId)}/instructions",
            new { instructions = "   " }, version);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest);

        object? stored = await api.Database.ExecuteScalarAsync(
            $"SELECT instructions FROM ai_agents WHERE id = '{agentId}'");

        Assert.Equal(Instructions, stored as string);
    }

    [Fact]
    public async Task Updating_instructions_on_an_unknown_agent_returns_404()
    {
        using HttpClient client = api.CreateClient();
        (Guid tenantId, _, string version) = await ArrangeAsync(client);

        using HttpResponseMessage response = await SendAsync(
            client, HttpMethod.Put, $"{AgentRoute(tenantId, Guid.CreateVersion7())}/instructions",
            new { instructions = "Anything." }, version);

        await AssertProblemAsync(response, HttpStatusCode.NotFound);
    }

    // ------------------------------------------------------------------ routing

    [Fact]
    public async Task Delete_and_list_are_not_exposed()
    {
        using HttpClient client = api.CreateClient();
        (Guid tenantId, Guid agentId, _) = await ArrangeAsync(client);

        using HttpResponseMessage deleted = await client.DeleteAsync(AgentRoute(tenantId, agentId));
        using HttpResponseMessage listed = await client.GetAsync(AgentsRoute(tenantId));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, listed.StatusCode);

        object? stored = await api.Database.ExecuteScalarAsync(
            $"SELECT count(*) FROM ai_agents WHERE id = '{agentId}'");

        Assert.Equal(1L, stored as long?);
    }

    [Fact]
    public async Task Patch_is_not_exposed()
    {
        using HttpClient client = api.CreateClient();
        (Guid tenantId, Guid agentId, _) = await ArrangeAsync(client);

        using HttpRequestMessage request = new(HttpMethod.Patch, AgentRoute(tenantId, agentId))
        {
            Content = JsonContent.Create(new { name = "Patched" }),
        };

        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public void The_api_is_talking_to_the_test_database()
    {
        string resolved = api.ResolvedConnectionString();

        Assert.Contains("omniai_test_", resolved, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ helpers

    internal static string AgentsRoute(Guid tenantId) =>
        string.Create(CultureInfo.InvariantCulture, $"/api/tenants/{tenantId}/ai-agents");

    internal static string AgentRoute(Guid tenantId, Guid agentId) =>
        string.Create(CultureInfo.InvariantCulture, $"/api/tenants/{tenantId}/ai-agents/{agentId}");

    internal static string ActionRoute(Guid tenantId, Guid agentId, string action) =>
        $"{AgentRoute(tenantId, agentId)}/{action}";

    internal static Task<HttpResponseMessage> CreateAsync(
        HttpClient client,
        Guid tenantId,
        string? name,
        string? instructions) =>
        client.PostAsJsonAsync(AgentsRoute(tenantId), new { name, instructions });

    /// <summary>
    /// Sends a request, optionally with an <c>If-Match</c> precondition.
    /// </summary>
    /// <remarks>
    /// The header is set with <c>TryAddWithoutValidation</c> so a test can send a
    /// deliberately malformed value. Validating it here would make the client
    /// refuse to send exactly the requests the malformed-precondition tests exist
    /// to exercise.
    /// </remarks>
    internal static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string route,
        object? body,
        string? ifMatch)
    {
        using HttpRequestMessage request = new(method, route);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        return await client.SendAsync(request);
    }

    /// <summary>Performs a lifecycle action and returns the agent and its new version.</summary>
    internal static async Task<(AgentPayload Agent, string ETag)> ActAsync(
        HttpClient client,
        Guid tenantId,
        Guid agentId,
        string action,
        string ifMatch)
    {
        using HttpResponseMessage response =
            await SendAsync(client, HttpMethod.Post, ActionRoute(tenantId, agentId, action), null, ifMatch);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await ReadAgentAsync(response), ETagOf(response));
    }

    /// <summary>Creates a tenant and one draft agent, and returns its current version.</summary>
    internal async Task<(Guid TenantId, Guid AgentId, string ETag)> ArrangeAsync(HttpClient client)
    {
        Guid tenantId = await api.CreateTenantAsync();

        using HttpResponseMessage created =
            await CreateAsync(client, tenantId, "Sales Assistant", Instructions);

        return (tenantId, (await ReadAgentAsync(created)).Id, ETagOf(created));
    }

    internal static async Task<AgentPayload> ReadAgentAsync(HttpResponseMessage response)
    {
        AgentPayload? payload = await response.Content.ReadFromJsonAsync<AgentPayload>();

        return payload ?? throw new InvalidOperationException("The response carried no agent.");
    }

    /// <summary>The entity tag a response carried, quotes included.</summary>
    internal static string ETagOf(HttpResponseMessage response)
    {
        EntityTagHeaderValue tag = response.Headers.ETag
            ?? throw new InvalidOperationException("The response carried no ETag.");

        return tag.ToString();
    }

    /// <summary>Asserts the response carries a usable strong entity tag.</summary>
    internal static void AssertStrongETag(HttpResponseMessage response)
    {
        EntityTagHeaderValue tag = response.Headers.ETag
            ?? throw new InvalidOperationException("The response carried no ETag.");

        // Strong, never W/: a weak tag asserts semantic equivalence rather than
        // byte equality, which cannot carry a lost-update precondition.
        Assert.False(tag.IsWeak, "The ETag must be strong.");
        Assert.StartsWith("\"", tag.Tag, StringComparison.Ordinal);
        Assert.EndsWith("\"", tag.Tag, StringComparison.Ordinal);
        Assert.True(tag.Tag.Length > 2, "The ETag must not be empty.");

        // Opaque: it must not advertise the storage engine.
        foreach (string forbidden in (string[])["xmin", "xid", "postgres", "npgsql"])
        {
            Assert.DoesNotContain(forbidden, tag.Tag, StringComparison.OrdinalIgnoreCase);
        }
    }

    internal static async Task<string> AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);

        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal((int)expected, document.RootElement.GetProperty("status").GetInt32());

        string detail = document.RootElement.TryGetProperty("detail", out JsonElement element)
            ? element.GetString() ?? string.Empty
            : string.Empty;

        // Nothing internal is ever disclosed, whatever the outcome. Identifiers
        // and type names are matched without regard to case, because none of them
        // is an ordinary English word.
        foreach (string forbidden in
            (string[])["Npgsql", "SQLSTATE", "at OmniAI.", "xmin", "xid",
                       "DbUpdate", "EntityFramework", "ai_agents"])
        {
            Assert.DoesNotContain(forbidden, detail, StringComparison.OrdinalIgnoreCase);
        }

        // SQL keywords are matched case-sensitively, and deliberately so. EF Core
        // and Npgsql emit them upper-case, whereas "update" is a word these
        // messages legitimately use when explaining to a client what went wrong
        // with its update. Matching loosely here would forbid plain English rather
        // than leaked SQL.
        foreach (string statement in (string[])["SELECT ", "UPDATE ", "INSERT ", "DELETE ", "WHERE "])
        {
            Assert.DoesNotContain(statement, detail, StringComparison.Ordinal);
        }

        return detail;
    }

    /// <summary>The published contract, as a client sees it.</summary>
    internal sealed record AgentPayload(
        Guid Id,
        Guid TenantId,
        string Name,
        string Instructions,
        string Status,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
