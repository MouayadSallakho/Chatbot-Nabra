using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace OmniAI.Api.IntegrationTests.Authorization;

/// <summary>
/// The role administration endpoints, over real HTTP.
/// </summary>
public sealed class RoleEndpointsTests(RoleApiFixture api) : IClassFixture<RoleApiFixture>
{
    /// <summary>A permission set these cases pass to the API.</summary>
    /// <remarks>
    /// A field rather than an inline array: the analyzer objects to a
    /// constant array allocated afresh on every call, and naming the set
    /// also says what the case is actually about.
    /// </remarks>
    private static readonly string[] AiAgentsView = ["ai-agents.view"];

    /// <summary>A permission set these cases pass to the API.</summary>
    /// <remarks>
    /// A field rather than an inline array: the analyzer objects to a
    /// constant array allocated afresh on every call, and naming the set
    /// also says what the case is actually about.
    /// </remarks>
    private static readonly string[] IntegrationsManage = ["integrations.manage"];

    /// <summary>A permission set these cases pass to the API.</summary>
    /// <remarks>
    /// A field rather than an inline array: the analyzer objects to a
    /// constant array allocated afresh on every call, and naming the set
    /// also says what the case is actually about.
    /// </remarks>
    private static readonly string[] AiAgentsViewAndIntegrationsManage = ["ai-agents.view", "integrations.manage"];

    /// <summary>A permission set these cases pass to the API.</summary>
    /// <remarks>
    /// A field rather than an inline array: the analyzer objects to a
    /// constant array allocated afresh on every call, and naming the set
    /// also says what the case is actually about.
    /// </remarks>
    private static readonly string[] AiAgentsView2 = ["ai-agents.view", "ai-agents.view", "AI-AGENTS.VIEW"];

    [Fact]
    public async Task A_new_workspace_already_has_a_protected_owner_role()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        using HttpResponseMessage response = await owner.GetAsync($"/api/tenants/{tenantId}/roles");

        response.EnsureSuccessStatusCode();

        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        JsonElement role = Assert.Single(body.RootElement.EnumerateArray());

        Assert.Equal("Owner", role.GetProperty("name").GetString());
        Assert.Equal("System", role.GetProperty("type").GetString());
        Assert.True(role.GetProperty("isSystem").GetBoolean());
    }

    [Fact]
    public async Task The_owner_role_reports_every_platform_permission()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        JsonElement listed = await OwnerRowAsync(owner, tenantId);
        Guid roleId = listed.GetProperty("id").GetGuid();

        using HttpResponseMessage detail =
            await owner.GetAsync($"/api/tenants/{tenantId}/roles/{roleId}");

        detail.EnsureSuccessStatusCode();

        using JsonDocument body = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());

        string[] permissions =
        [
            .. body.RootElement.GetProperty("permissions")
                .EnumerateArray()
                .Select(entry => entry.GetString()!)
        ];

        Assert.Equal(listed.GetProperty("permissionCount").GetInt32(), permissions.Length);
        Assert.Contains("roles.manage", permissions);
        Assert.Contains("ai-agents.preview", permissions);
    }

    [Fact]
    public async Task Creating_a_role_returns_201_with_a_location_and_an_etag()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        using HttpResponseMessage created = await owner.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/roles",
            new { name = "Support agent", permissions = AiAgentsView });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.NotNull(created.Headers.Location);
        Assert.NotNull(created.Headers.ETag);

        // Strong, never weak: the tag identifies one exact stored revision, which
        // is the only thing a lost-update precondition can be built on.
        Assert.False(created.Headers.ETag!.IsWeak);
    }

    [Fact]
    public async Task A_created_role_can_be_read_back_at_its_location()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        using HttpResponseMessage created = await owner.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/roles",
            new { name = "Support agent", permissions = AiAgentsView });

        using HttpResponseMessage read = await owner.GetAsync(created.Headers.Location);

        read.EnsureSuccessStatusCode();

        using JsonDocument body = JsonDocument.Parse(await read.Content.ReadAsStringAsync());

        Assert.Equal("Support agent", body.RootElement.GetProperty("name").GetString());
        Assert.Equal("Custom", body.RootElement.GetProperty("type").GetString());
        Assert.False(body.RootElement.GetProperty("isSystem").GetBoolean());
    }

    [Fact]
    public async Task A_role_may_be_created_granting_nothing()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        using HttpResponseMessage created = await owner.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/roles",
            new { name = "Placeholder" });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using JsonDocument body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());

        Assert.Empty(body.RootElement.GetProperty("permissions").EnumerateArray());
    }

    [Fact]
    public async Task An_unknown_permission_code_is_rejected_as_a_bad_request()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        using HttpResponseMessage refused = await owner.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/roles",
            new { name = "Invented", permissions = IntegrationsManage });

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    [Fact]
    public async Task A_forged_permission_code_never_becomes_a_stored_grant()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        using HttpResponseMessage refused = await owner.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/roles",
            new { name = "Invented", permissions = AiAgentsViewAndIntegrationsManage });

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        // Not even the recognised half of the request survived: the set is
        // validated whole, before anything is written.
        using HttpResponseMessage roles = await owner.GetAsync($"/api/tenants/{tenantId}/roles");

        using JsonDocument body = JsonDocument.Parse(await roles.Content.ReadAsStringAsync());

        Assert.DoesNotContain(
            body.RootElement.EnumerateArray(),
            entry => entry.GetProperty("name").GetString() == "Invented");
    }

    [Fact]
    public async Task Duplicate_permission_codes_are_deduplicated()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        using HttpResponseMessage created = await owner.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/roles",
            new
            {
                name = "Support agent",
                permissions = AiAgentsView2,
            });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using JsonDocument body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());

        Assert.Equal(
            ["ai-agents.view"],
            body.RootElement.GetProperty("permissions")
                .EnumerateArray()
                .Select(entry => entry.GetString()));
    }

    [Fact]
    public async Task A_name_already_taken_in_this_workspace_is_a_conflict()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        using HttpResponseMessage first = await owner.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/roles",
            new { name = "Support agent" });

        first.EnsureSuccessStatusCode();

        using HttpResponseMessage second = await owner.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/roles",
            new { name = "SUPPORT AGENT" });

        // 409, not 412: two roles cannot share a name however recently the caller
        // read the workspace, so re-reading and retrying unchanged would fail
        // again. The caller must choose a different name.
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task A_name_conflict_discloses_no_database_internals()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        using HttpResponseMessage first = await owner.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/roles",
            new { name = "Support agent" });

        first.EnsureSuccessStatusCode();

        using HttpResponseMessage second = await owner.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/roles",
            new { name = "Support agent" });

        string body = await second.Content.ReadAsStringAsync();

        foreach (string leak in
            (string[])["ux_tenant_roles", "23505", "duplicate key", "INSERT", "Npgsql", "pg_"])
        {
            Assert.DoesNotContain(leak, body, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task The_same_role_name_is_free_in_a_different_workspace()
    {
        (HttpClient first, Guid firstTenant, _) = await api.CreateOwnerAsync();
        (HttpClient second, Guid secondTenant, _) = await api.CreateOwnerAsync("Globex");

        using HttpResponseMessage created = await first.PostAsJsonAsync(
            $"/api/tenants/{firstTenant}/roles",
            new { name = "Support agent" });

        created.EnsureSuccessStatusCode();

        using HttpResponseMessage elsewhere = await second.PostAsJsonAsync(
            $"/api/tenants/{secondTenant}/roles",
            new { name = "Support agent" });

        Assert.Equal(HttpStatusCode.Created, elsewhere.StatusCode);
    }

    [Fact]
    public async Task A_caller_cannot_ask_for_a_system_role()
    {
        // There is no field in the contract to ask with, so a body carrying one is
        // simply ignored -- the role is Custom whatever the caller sent. That is
        // stronger than a validation rule, which could be moved or reordered.
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        using HttpResponseMessage created = await owner.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/roles",
            new
            {
                name = "Pretender",
                type = "System",
                isSystem = true,
                kind = "Owner",
                id = Guid.CreateVersion7(),
                tenantId = Guid.CreateVersion7(),
            });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using JsonDocument body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());

        Assert.Equal("Custom", body.RootElement.GetProperty("type").GetString());
        Assert.False(body.RootElement.GetProperty("isSystem").GetBoolean());

        // And the workspace still has exactly one system role.
        using HttpResponseMessage roles = await owner.GetAsync($"/api/tenants/{tenantId}/roles");
        using JsonDocument listed = JsonDocument.Parse(await roles.Content.ReadAsStringAsync());

        Assert.Single(
            listed.RootElement.EnumerateArray(),
            entry => entry.GetProperty("isSystem").GetBoolean());
    }

    [Fact]
    public async Task A_created_role_belongs_to_the_workspace_in_the_route()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        using HttpResponseMessage created = await owner.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/roles",
            new { name = "Support agent", tenantId = Guid.CreateVersion7() });

        using JsonDocument body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());

        Assert.Equal(tenantId, body.RootElement.GetProperty("tenantId").GetGuid());
    }

    [Fact]
    public async Task The_role_list_puts_owner_first_then_custom_roles_by_name()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        foreach (string name in (string[])["Support agent", "Analyst"])
        {
            using HttpResponseMessage created = await owner.PostAsJsonAsync(
                $"/api/tenants/{tenantId}/roles",
                new { name });

            created.EnsureSuccessStatusCode();
        }

        using HttpResponseMessage roles = await owner.GetAsync($"/api/tenants/{tenantId}/roles");

        using JsonDocument body = JsonDocument.Parse(await roles.Content.ReadAsStringAsync());

        Assert.Equal(
            ["Owner", "Analyst", "Support agent"],
            body.RootElement.EnumerateArray().Select(entry => entry.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task The_permission_catalogue_publishes_metadata_a_person_can_read()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        using HttpResponseMessage response =
            await owner.GetAsync($"/api/tenants/{tenantId}/permissions");

        response.EnsureSuccessStatusCode();

        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.All(body.RootElement.EnumerateArray(), entry =>
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("code").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("group").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("label").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("description").GetString()));
        });
    }

    [Fact]
    public async Task The_permission_catalogue_exposes_no_internal_type_names()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        using HttpResponseMessage response =
            await owner.GetAsync($"/api/tenants/{tenantId}/permissions");

        string body = await response.Content.ReadAsStringAsync();

        foreach (string leak in
            (string[])["OmniAI", "PermissionDefinition", "Handler", "Endpoint", "Domain"])
        {
            Assert.DoesNotContain(leak, body, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task A_role_id_from_another_workspace_is_not_a_route_to_reading_it()
    {
        (HttpClient first, Guid firstTenant, _) = await api.CreateOwnerAsync();
        (HttpClient second, Guid secondTenant, _) = await api.CreateOwnerAsync("Globex");

        using HttpResponseMessage created = await second.PostAsJsonAsync(
            $"/api/tenants/{secondTenant}/roles",
            new { name = "Their role" });

        using JsonDocument body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        Guid theirRole = body.RootElement.GetProperty("id").GetGuid();

        // A genuine role id, named through a workspace the caller does own.
        using HttpResponseMessage refused =
            await first.GetAsync($"/api/tenants/{firstTenant}/roles/{theirRole}");

        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
    }

    [Fact]
    public async Task An_unknown_role_id_is_not_found()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        using HttpResponseMessage response =
            await owner.GetAsync($"/api/tenants/{tenantId}/roles/{Guid.CreateVersion7()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task There_is_no_delete_and_no_member_assignment_endpoint()
    {
        // The absences are part of this phase's contract. Role deletion has an
        // unanswered question behind it, and member administration is a phase of
        // its own -- half of it here would be a liability rather than a head
        // start.
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        JsonElement ownerRole = await OwnerRowAsync(owner, tenantId);
        Guid roleId = ownerRole.GetProperty("id").GetGuid();

        using HttpResponseMessage deleted =
            await owner.DeleteAsync($"/api/tenants/{tenantId}/roles/{roleId}");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, deleted.StatusCode);

        using HttpResponseMessage assigned = await owner.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/roles/{roleId}/members",
            new { userId = Guid.CreateVersion7() });

        Assert.Equal(HttpStatusCode.NotFound, assigned.StatusCode);
    }

    /// <summary>The Owner row from a workspace's role list.</summary>
    private static async Task<JsonElement> OwnerRowAsync(HttpClient client, Guid tenantId)
    {
        using HttpResponseMessage response = await client.GetAsync($"/api/tenants/{tenantId}/roles");

        response.EnsureSuccessStatusCode();

        JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return body.RootElement.EnumerateArray()
            .First(entry => entry.GetProperty("isSystem").GetBoolean())
            .Clone();
    }
}
