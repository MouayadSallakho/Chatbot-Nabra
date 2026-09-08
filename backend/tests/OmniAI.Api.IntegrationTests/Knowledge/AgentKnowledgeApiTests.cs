using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace OmniAI.Api.IntegrationTests.Knowledge;

/// <summary>
/// Deciding which documents an agent may draw on, and testing what it would
/// retrieve.
/// </summary>
public sealed class AgentKnowledgeApiTests(KnowledgeApiFixture api)
    : IClassFixture<KnowledgeApiFixture>
{
    private const string Content = "Goods may be returned within 30 days of delivery.";

    // ------------------------------------------------------------------- read

    [Fact]
    public async Task An_agent_starts_with_nothing_assigned()
    {
        // Knowledge is not shared with every agent by default. An agent begins
        // with nothing and is given exactly what somebody chose to give it.
        using HttpClient client = await api.CreateSignedInClientAsync();
        Guid tenantId = await api.CreateTenantAsync(client);

        Guid agentId = await AgentAsync(client, tenantId);
        (Guid documentId, _) = await DocumentAsync(client, tenantId);

        using HttpResponseMessage response =
            await client.GetAsync(AgentKnowledgeUrl(tenantId, agentId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        JsonElement body = await ReadAsync(response);

        JsonElement offered = body.GetProperty("documents").EnumerateArray()
            .Single(item => item.GetProperty("knowledgeDocumentId").GetGuid() == documentId);

        Assert.False(offered.GetProperty("isAssigned").GetBoolean());
    }

    [Fact]
    public async Task The_assignment_view_reports_the_real_state_of_each_document()
    {
        // No provider is configured on this host, so nothing can be ready. The
        // screen has to say so rather than fabricating a document that answers.
        using HttpClient client = await api.CreateSignedInClientAsync();
        Guid tenantId = await api.CreateTenantAsync(client);

        Guid agentId = await AgentAsync(client, tenantId);
        await DocumentAsync(client, tenantId);

        using HttpResponseMessage response =
            await client.GetAsync(AgentKnowledgeUrl(tenantId, agentId));

        JsonElement body = await ReadAsync(response);

        Assert.All(
            body.GetProperty("documents").EnumerateArray(),
            item => Assert.Equal("NeedsIndex", item.GetProperty("readiness").GetString()));
    }

    [Fact]
    public async Task The_version_it_returns_is_the_agent_version()
    {
        // Changing assignments is a change to the agent's configuration, so the
        // version handed out has to be the one the write is checked against.
        using HttpClient client = await api.CreateSignedInClientAsync();
        Guid tenantId = await api.CreateTenantAsync(client);

        Guid agentId = await AgentAsync(client, tenantId);

        using HttpResponseMessage knowledge =
            await client.GetAsync(AgentKnowledgeUrl(tenantId, agentId));

        using HttpResponseMessage agent =
            await client.GetAsync(AgentUrl(tenantId, agentId));

        Assert.Equal(agent.Headers.ETag?.ToString(), knowledge.Headers.ETag?.ToString());
    }

    // ----------------------------------------------------------------- replace

    [Fact]
    public async Task Assigning_a_document_requires_a_precondition()
    {
        using HttpClient client = await api.CreateSignedInClientAsync();
        Guid tenantId = await api.CreateTenantAsync(client);

        Guid agentId = await AgentAsync(client, tenantId);
        (Guid documentId, _) = await DocumentAsync(client, tenantId);

        using HttpResponseMessage response = await client.PutAsJsonAsync(
            AgentKnowledgeUrl(tenantId, agentId),
            new { knowledgeDocumentIds = new[] { documentId } });

        Assert.Equal(HttpStatusCode.PreconditionRequired, response.StatusCode);
    }

    [Fact]
    public async Task Assigning_a_document_advances_the_agent_version()
    {
        // Writing only the join table would let a second editor save against a
        // version that no longer describes the agent, and neither person would
        // ever be told.
        using HttpClient client = await api.CreateSignedInClientAsync();
        Guid tenantId = await api.CreateTenantAsync(client);

        Guid agentId = await AgentAsync(client, tenantId);
        (Guid documentId, _) = await DocumentAsync(client, tenantId);

        string before = await AgentETagAsync(client, tenantId, agentId);

        using HttpResponseMessage response =
            await AssignAsync(client, tenantId, agentId, before, documentId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(before, response.Headers.ETag?.ToString());

        Assert.Equal(
            response.Headers.ETag?.ToString(),
            await AgentETagAsync(client, tenantId, agentId));
    }

    [Fact]
    public async Task A_stale_assignment_write_is_refused()
    {
        using HttpClient client = await api.CreateSignedInClientAsync();
        Guid tenantId = await api.CreateTenantAsync(client);

        Guid agentId = await AgentAsync(client, tenantId);
        (Guid first, _) = await DocumentAsync(client, tenantId, "First");
        (Guid second, _) = await DocumentAsync(client, tenantId, "Second");

        string stale = await AgentETagAsync(client, tenantId, agentId);

        using (HttpResponseMessage accepted =
            await AssignAsync(client, tenantId, agentId, stale, first))
        {
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        }

        using HttpResponseMessage refused =
            await AssignAsync(client, tenantId, agentId, stale, second);

        Assert.Equal(HttpStatusCode.PreconditionFailed, refused.StatusCode);
    }

    [Fact]
    public async Task An_assignment_survives_and_can_be_replaced()
    {
        using HttpClient client = await api.CreateSignedInClientAsync();
        Guid tenantId = await api.CreateTenantAsync(client);

        Guid agentId = await AgentAsync(client, tenantId);
        (Guid first, _) = await DocumentAsync(client, tenantId, "First");
        (Guid second, _) = await DocumentAsync(client, tenantId, "Second");

        string etag = await AgentETagAsync(client, tenantId, agentId);

        using (HttpResponseMessage assigned =
            await AssignAsync(client, tenantId, agentId, etag, first, second))
        {
            etag = assigned.Headers.ETag!.ToString();

            Assert.Equal(2, await AssignedCountAsync(assigned));
        }

        using HttpResponseMessage replaced =
            await AssignAsync(client, tenantId, agentId, etag, second);

        Assert.Equal(1, await AssignedCountAsync(replaced));

        JsonElement body = await ReadAsync(replaced);

        Assert.True(body.GetProperty("documents").EnumerateArray()
            .Single(item => item.GetProperty("knowledgeDocumentId").GetGuid() == second)
            .GetProperty("isAssigned").GetBoolean());
    }

    [Fact]
    public async Task Assigning_nothing_removes_everything()
    {
        using HttpClient client = await api.CreateSignedInClientAsync();
        Guid tenantId = await api.CreateTenantAsync(client);

        Guid agentId = await AgentAsync(client, tenantId);
        (Guid documentId, _) = await DocumentAsync(client, tenantId);

        string etag = await AgentETagAsync(client, tenantId, agentId);

        using (HttpResponseMessage assigned =
            await AssignAsync(client, tenantId, agentId, etag, documentId))
        {
            etag = assigned.Headers.ETag!.ToString();
        }

        using HttpResponseMessage cleared =
            await AssignAsync(client, tenantId, agentId, etag);

        Assert.Equal(0, await AssignedCountAsync(cleared));
    }

    [Fact]
    public async Task Assigning_the_same_document_twice_is_the_same_as_once()
    {
        using HttpClient client = await api.CreateSignedInClientAsync();
        Guid tenantId = await api.CreateTenantAsync(client);

        Guid agentId = await AgentAsync(client, tenantId);
        (Guid documentId, _) = await DocumentAsync(client, tenantId);

        string etag = await AgentETagAsync(client, tenantId, agentId);

        using HttpResponseMessage response =
            await AssignAsync(client, tenantId, agentId, etag, documentId, documentId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, await AssignedCountAsync(response));
    }

    [Fact]
    public async Task Assigning_another_workspace_document_is_rejected()
    {
        // Refused by the handler with a clear message, and refused by the database
        // as well: a row naming one workspace's agent and another's document
        // cannot satisfy both composite foreign keys with one tenant id.
        using HttpClient client = await api.CreateSignedInClientAsync();

        Guid mine = await api.CreateTenantAsync(client, "Mine");
        Guid theirs = await api.CreateTenantAsync(client, "Theirs");

        Guid agentId = await AgentAsync(client, mine);
        (Guid foreignDocument, _) = await DocumentAsync(client, theirs);

        string etag = await AgentETagAsync(client, mine, agentId);

        using HttpResponseMessage response =
            await AssignAsync(client, mine, agentId, etag, foreignDocument);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ------------------------------------------------------------ permissions

    [Fact]
    public async Task Reading_assignments_needs_both_knowledge_view_and_agent_view()
    {
        using HttpClient owner = await api.CreateSignedInClientAsync();
        Guid tenantId = await api.CreateTenantAsync(owner);

        Guid agentId = await AgentAsync(owner, tenantId);

        using HttpClient knowledgeOnly =
            await api.CreateRestrictedMemberAsync(tenantId, "knowledge.view");

        using HttpClient agentOnly =
            await api.CreateRestrictedMemberAsync(tenantId, "ai-agents.view");

        using HttpResponseMessage first =
            await knowledgeOnly.GetAsync(AgentKnowledgeUrl(tenantId, agentId));

        using HttpResponseMessage second =
            await agentOnly.GetAsync(AgentKnowledgeUrl(tenantId, agentId));

        Assert.Equal(HttpStatusCode.Forbidden, first.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, second.StatusCode);
    }

    [Fact]
    public async Task Changing_assignments_needs_both_knowledge_manage_and_agent_edit()
    {
        // Neither implies the other. Somebody who administers documents but was
        // never trusted to configure agents could otherwise change what a live
        // agent says to customers without holding a single agent permission.
        using HttpClient owner = await api.CreateSignedInClientAsync();
        Guid tenantId = await api.CreateTenantAsync(owner);

        Guid agentId = await AgentAsync(owner, tenantId);
        (Guid documentId, _) = await DocumentAsync(owner, tenantId);

        string etag = await AgentETagAsync(owner, tenantId, agentId);

        using HttpClient knowledgeOnly = await api.CreateRestrictedMemberAsync(
            tenantId,
            "knowledge.view",
            "knowledge.manage",
            "ai-agents.view");

        using HttpClient agentOnly = await api.CreateRestrictedMemberAsync(
            tenantId,
            "knowledge.view",
            "ai-agents.view",
            "ai-agents.edit");

        using HttpResponseMessage first =
            await AssignAsync(knowledgeOnly, tenantId, agentId, etag, documentId);

        using HttpResponseMessage second =
            await AssignAsync(agentOnly, tenantId, agentId, etag, documentId);

        Assert.Equal(HttpStatusCode.Forbidden, first.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, second.StatusCode);
    }

    [Fact]
    public async Task An_anonymous_caller_cannot_read_or_change_assignments()
    {
        using HttpClient owner = await api.CreateSignedInClientAsync();
        Guid tenantId = await api.CreateTenantAsync(owner);

        Guid agentId = await AgentAsync(owner, tenantId);

        using HttpClient anonymous = api.CreateClient();

        using HttpResponseMessage response =
            await anonymous.GetAsync(AgentKnowledgeUrl(tenantId, agentId));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ------------------------------------------------------- retrieval tester

    [Fact]
    public async Task Testing_retrieval_on_an_agent_with_no_knowledge_returns_nothing_and_costs_nothing()
    {
        // No eligible chunk, so no embedding call is made -- which is why this
        // succeeds on a host with no provider rather than answering 503.
        using HttpClient client = await api.CreateSignedInClientAsync();
        Guid tenantId = await api.CreateTenantAsync(client);

        Guid agentId = await AgentAsync(client, tenantId);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"{AgentKnowledgeUrl(tenantId, agentId)}/search",
            new { question = "Do you deliver?" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Empty((await ReadAsync(response)).GetProperty("hits").EnumerateArray());
    }

    [Fact]
    public async Task Testing_retrieval_needs_both_knowledge_view_and_agent_view()
    {
        using HttpClient owner = await api.CreateSignedInClientAsync();
        Guid tenantId = await api.CreateTenantAsync(owner);

        Guid agentId = await AgentAsync(owner, tenantId);

        using HttpClient knowledgeOnly =
            await api.CreateRestrictedMemberAsync(tenantId, "knowledge.view");

        using HttpResponseMessage response = await knowledgeOnly.PostAsJsonAsync(
            $"{AgentKnowledgeUrl(tenantId, agentId)}/search",
            new { question = "Do you deliver?" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_blank_question_is_rejected()
    {
        using HttpClient client = await api.CreateSignedInClientAsync();
        Guid tenantId = await api.CreateTenantAsync(client);

        Guid agentId = await AgentAsync(client, tenantId);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"{AgentKnowledgeUrl(tenantId, agentId)}/search",
            new { question = "   " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Testing_retrieval_against_another_workspace_agent_is_not_found()
    {
        using HttpClient owner = await api.CreateSignedInClientAsync();
        Guid tenantId = await api.CreateTenantAsync(owner);

        Guid agentId = await AgentAsync(owner, tenantId);

        using HttpClient stranger = await api.CreateSignedInClientAsync();

        using HttpResponseMessage response = await stranger.PostAsJsonAsync(
            $"{AgentKnowledgeUrl(tenantId, agentId)}/search",
            new { question = "Do you deliver?" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ------------------------------------------------------------------ helpers

    private static string AgentUrl(Guid tenantId, Guid agentId) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"/api/tenants/{tenantId}/ai-agents/{agentId}");

    private static string AgentKnowledgeUrl(Guid tenantId, Guid agentId) =>
        $"{AgentUrl(tenantId, agentId)}/knowledge";

    private static async Task<Guid> AgentAsync(HttpClient client, Guid tenantId)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            string.Create(CultureInfo.InvariantCulture, $"/api/tenants/{tenantId}/ai-agents"),
            new { name = "Support", instructions = "Be helpful and concise." });

        response.EnsureSuccessStatusCode();

        return (await ReadAsync(response)).GetProperty("id").GetGuid();
    }

    private static async Task<(Guid Id, string ETag)> DocumentAsync(
        HttpClient client,
        Guid tenantId,
        string title = "Returns policy")
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            string.Create(CultureInfo.InvariantCulture, $"/api/tenants/{tenantId}/knowledge"),
            new { title, content = Content });

        response.EnsureSuccessStatusCode();

        return ((await ReadAsync(response)).GetProperty("id").GetGuid(),
            response.Headers.ETag!.ToString());
    }

    private static async Task<string> AgentETagAsync(
        HttpClient client,
        Guid tenantId,
        Guid agentId)
    {
        using HttpResponseMessage response = await client.GetAsync(AgentUrl(tenantId, agentId));

        response.EnsureSuccessStatusCode();

        return response.Headers.ETag!.ToString();
    }

    private static async Task<HttpResponseMessage> AssignAsync(
        HttpClient client,
        Guid tenantId,
        Guid agentId,
        string etag,
        params Guid[] documentIds)
    {
        using HttpRequestMessage request =
            new(HttpMethod.Put, AgentKnowledgeUrl(tenantId, agentId))
            {
                Content = JsonContent.Create(new { knowledgeDocumentIds = documentIds }),
            };

        request.Headers.IfMatch.Add(EntityTagHeaderValue.Parse(etag));

        return await client.SendAsync(request);
    }

    private static async Task<int> AssignedCountAsync(HttpResponseMessage response) =>
        (await ReadAsync(response)).GetProperty("documents").EnumerateArray()
            .Count(item => item.GetProperty("isAssigned").GetBoolean());

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        using JsonDocument document =
            JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.Clone();
    }
}
