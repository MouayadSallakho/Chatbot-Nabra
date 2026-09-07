using System.Net;
using System.Net.Http.Json;

namespace OmniAI.Api.IntegrationTests.WebChat;

/// <summary>
/// Administering website chat over HTTP: who may, and what a stale write does.
/// </summary>
public sealed class WebChatAdminApiTests(WebChatApiFixture fixture)
    : IClassFixture<WebChatApiFixture>
{
    private static Uri Endpoints(Guid tenantId) =>
        WebChatApiFixture.Relative($"/api/tenants/{tenantId}/website-chat");

    private sealed record EndpointPayload(
        Guid Id,
        Guid AiAgentId,
        string AiAgentName,
        string AiAgentStatus,
        string PublicKey,
        string Status);

    // ---- Authorization matrix -------------------------------------------

    [Fact]
    public async Task An_anonymous_caller_is_refused_without_being_told_anything()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Anon probe");

        HttpClient stranger = fixture.CreateClient();

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await stranger.GetAsync(Endpoints(tenantId))).StatusCode);
    }

    [Fact]
    public async Task A_non_member_gets_not_found_rather_than_forbidden()
    {
        // Membership is answered before permission, so somebody else's workspace
        // is indistinguishable from a workspace that does not exist.
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Outsider probe");

        (HttpClient outsider, _, _) = await fixture.CreateOwnerAsync("Rival Trading");

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await outsider.GetAsync(Endpoints(tenantId))).StatusCode);
    }

    [Fact]
    public async Task A_member_without_the_permission_is_forbidden()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Member probe");

        HttpClient member = await fixture.CreateRestrictedMemberAsync(
            tenantId,
            "workspace.view");

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await member.GetAsync(Endpoints(tenantId))).StatusCode);
    }

    [Fact]
    public async Task Reading_needs_only_the_view_permission()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Reader probe");

        HttpClient reader = await fixture.CreateRestrictedMemberAsync(
            tenantId,
            "website-chat.view");

        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync(Endpoints(tenantId))).StatusCode);
    }

    [Fact]
    public async Task Reading_does_not_imply_publishing()
    {
        // The two are separate capabilities. Seeing that a chat endpoint exists
        // and deciding whether the world can talk to it are different
        // responsibilities.
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        Guid agentId = await WebChatApiFixture.CreateActiveAgentAsync(owner, tenantId, "Read only");

        HttpClient reader = await fixture.CreateRestrictedMemberAsync(
            tenantId,
            "website-chat.view");

        HttpResponseMessage refused = await reader.PostAsJsonAsync(
            Endpoints(tenantId),
            new { aiAgentId = agentId });

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    [Fact]
    public async Task Managing_does_not_imply_reading_conversations()
    {
        // Administering the channel is not the right to read what customers said
        // on it.
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Channel manager");

        HttpClient manager = await fixture.CreateRestrictedMemberAsync(
            tenantId,
            "website-chat.view",
            "website-chat.manage");

        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync(Endpoints(tenantId))).StatusCode);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await manager.GetAsync(
                WebChatApiFixture.Relative($"/api/tenants/{tenantId}/conversations"))).StatusCode);
    }

    // ---- Lifecycle -------------------------------------------------------

    [Fact]
    public async Task A_created_endpoint_is_a_draft_and_carries_a_public_key()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        Guid agentId = await WebChatApiFixture.CreateActiveAgentAsync(owner, tenantId, "Draft check");

        HttpResponseMessage created = await owner.PostAsJsonAsync(
            Endpoints(tenantId),
            new { aiAgentId = agentId });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.NotNull(created.Headers.ETag);

        EndpointPayload? endpoint = await created.Content.ReadFromJsonAsync<EndpointPayload>();

        Assert.NotNull(endpoint);
        Assert.Equal("Draft", endpoint.Status);
        Assert.Equal(22, endpoint.PublicKey.Length);
        Assert.Equal(agentId, endpoint.AiAgentId);
    }

    [Fact]
    public async Task An_agent_from_another_workspace_cannot_be_published()
    {
        // The only check between a guessed agent id and somebody else's agent
        // answering the public on this workspace's URL.
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (HttpClient rival, Guid rivalTenantId, _) = await fixture.CreateOwnerAsync("Rival Trading");

        Guid rivalAgent = await WebChatApiFixture.CreateActiveAgentAsync(rival, rivalTenantId, "Theirs");

        HttpResponseMessage refused = await owner.PostAsJsonAsync(
            Endpoints(tenantId),
            new { aiAgentId = rivalAgent });

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
    }

    [Fact]
    public async Task One_agent_gets_one_chat_link()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        Guid agentId = await WebChatApiFixture.CreateActiveAgentAsync(owner, tenantId, "Only once");

        Assert.Equal(
            HttpStatusCode.Created,
            (await owner.PostAsJsonAsync(Endpoints(tenantId), new { aiAgentId = agentId }))
                .StatusCode);

        Assert.Equal(
            HttpStatusCode.Conflict,
            (await owner.PostAsJsonAsync(Endpoints(tenantId), new { aiAgentId = agentId }))
                .StatusCode);
    }

    [Fact]
    public async Task Creating_without_naming_an_agent_is_a_bad_request()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await owner.PostAsJsonAsync(Endpoints(tenantId), new { })).StatusCode);
    }

    // ---- Conditional writes ---------------------------------------------

    [Fact]
    public async Task Activating_without_a_precondition_is_refused_with_428()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        Guid agentId = await WebChatApiFixture.CreateActiveAgentAsync(owner, tenantId, "Precondition");

        EndpointPayload endpoint = await CreateAsync(owner, tenantId, agentId);

        HttpResponseMessage refused = await owner.PostAsync(
            WebChatApiFixture.Relative(
                $"/api/tenants/{tenantId}/website-chat/{endpoint.Id}/activate"),
            content: null);

        Assert.Equal(HttpStatusCode.PreconditionRequired, refused.StatusCode);
    }

    [Fact]
    public async Task A_stale_precondition_is_refused_with_412_and_changes_nothing()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        Guid agentId = await WebChatApiFixture.CreateActiveAgentAsync(owner, tenantId, "Stale");

        EndpointPayload endpoint = await CreateAsync(owner, tenantId, agentId);

        string firstTag = await ReadTagAsync(owner, tenantId, endpoint.Id);

        // Somebody else activates it first.
        Assert.Equal(
            HttpStatusCode.OK,
            (await SendConditionalAsync(owner, tenantId, endpoint.Id, "activate", firstTag))
                .StatusCode);

        // The stale caller now tries to withdraw it using the tag they read.
        HttpResponseMessage stale =
            await SendConditionalAsync(owner, tenantId, endpoint.Id, "disable", firstTag);

        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);

        // Still active: the refused write did nothing.
        Assert.Equal("Active", (await ReadAsync(owner, tenantId, endpoint.Id)).Status);
    }

    [Fact]
    public async Task A_successful_change_returns_a_new_entity_tag()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        Guid agentId = await WebChatApiFixture.CreateActiveAgentAsync(owner, tenantId, "New tag");

        EndpointPayload endpoint = await CreateAsync(owner, tenantId, agentId);

        string before = await ReadTagAsync(owner, tenantId, endpoint.Id);

        HttpResponseMessage activated =
            await SendConditionalAsync(owner, tenantId, endpoint.Id, "activate", before);

        Assert.Equal(HttpStatusCode.OK, activated.StatusCode);

        string after = activated.Headers.ETag?.ToString()
            ?? throw new InvalidOperationException("No entity tag was returned.");

        Assert.NotEqual(before, after);

        // And the new tag is the one that works next.
        Assert.Equal(
            HttpStatusCode.OK,
            (await SendConditionalAsync(owner, tenantId, endpoint.Id, "disable", after)).StatusCode);
    }

    [Fact]
    public async Task Disabling_and_activating_move_the_status_both_ways()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (_, Guid endpointId) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Both ways");

        Assert.Equal("Active", (await ReadAsync(owner, tenantId, endpointId)).Status);

        string tag = await ReadTagAsync(owner, tenantId, endpointId);

        await SendConditionalAsync(owner, tenantId, endpointId, "disable", tag);

        Assert.Equal("Disabled", (await ReadAsync(owner, tenantId, endpointId)).Status);
    }

    [Fact]
    public async Task An_endpoint_of_another_workspace_is_not_found()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (_, Guid endpointId) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Foreign");

        (HttpClient rival, Guid rivalTenantId, _) = await fixture.CreateOwnerAsync("Rival Trading");

        HttpResponseMessage refused = await rival.GetAsync(
            WebChatApiFixture.Relative(
                $"/api/tenants/{rivalTenantId}/website-chat/{endpointId}"));

        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
    }

    // ---- Helpers ---------------------------------------------------------

    private static async Task<EndpointPayload> CreateAsync(
        HttpClient owner,
        Guid tenantId,
        Guid agentId)
    {
        HttpResponseMessage created = await owner.PostAsJsonAsync(
            Endpoints(tenantId),
            new { aiAgentId = agentId });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        return await created.Content.ReadFromJsonAsync<EndpointPayload>()
            ?? throw new InvalidOperationException("No endpoint was returned.");
    }

    private static async Task<EndpointPayload> ReadAsync(
        HttpClient owner,
        Guid tenantId,
        Guid endpointId)
    {
        HttpResponseMessage read = await owner.GetAsync(
            WebChatApiFixture.Relative($"/api/tenants/{tenantId}/website-chat/{endpointId}"));

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        return await read.Content.ReadFromJsonAsync<EndpointPayload>()
            ?? throw new InvalidOperationException("No endpoint was returned.");
    }

    private static async Task<string> ReadTagAsync(
        HttpClient owner,
        Guid tenantId,
        Guid endpointId)
    {
        HttpResponseMessage read = await owner.GetAsync(
            WebChatApiFixture.Relative($"/api/tenants/{tenantId}/website-chat/{endpointId}"));

        return read.Headers.ETag?.ToString()
            ?? throw new InvalidOperationException("No entity tag was returned.");
    }

    private static Task<HttpResponseMessage> SendConditionalAsync(
        HttpClient owner,
        Guid tenantId,
        Guid endpointId,
        string action,
        string tag)
    {
        HttpRequestMessage request = new(
            HttpMethod.Post,
            WebChatApiFixture.Relative(
                $"/api/tenants/{tenantId}/website-chat/{endpointId}/{action}"));

        request.Headers.TryAddWithoutValidation("If-Match", tag);

        return owner.SendAsync(request);
    }
}
