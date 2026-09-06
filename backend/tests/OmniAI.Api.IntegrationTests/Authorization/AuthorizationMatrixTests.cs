using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace OmniAI.Api.IntegrationTests.Authorization;

/// <summary>
/// The 401 / 404 / 403 contract, over real HTTP.
/// </summary>
/// <remarks>
/// <strong>Three callers, three answers, and mixing any two of them up is a
/// security defect.</strong> An anonymous caller has not said who they are and
/// gets <c>401</c>. A caller from another workspace gets <c>404</c>, the same
/// answer as for a workspace that never existed, so the route cannot be used to
/// discover which workspaces are real. A member without the permission gets
/// <c>403</c>, because they have already proved they belong here and can act on
/// being told what is missing.
/// </remarks>
public sealed class AuthorizationMatrixTests(RoleApiFixture api) : IClassFixture<RoleApiFixture>
{
    /// <summary>A permission set these cases pass to the API.</summary>
    /// <remarks>
    /// A field rather than an inline array: the analyzer objects to a
    /// constant array allocated afresh on every call, and naming the set
    /// also says what the case is actually about.
    /// </remarks>
    private static readonly string[] AiAgentsView = ["ai-agents.view"];

    [Fact]
    public async Task An_owner_can_use_every_current_capability()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        using HttpResponseMessage workspace = await owner.GetAsync($"/api/tenants/{tenantId}");
        Assert.Equal(HttpStatusCode.OK, workspace.StatusCode);

        using HttpResponseMessage roles = await owner.GetAsync($"/api/tenants/{tenantId}/roles");
        Assert.Equal(HttpStatusCode.OK, roles.StatusCode);

        using HttpResponseMessage catalogue =
            await owner.GetAsync($"/api/tenants/{tenantId}/permissions");
        Assert.Equal(HttpStatusCode.OK, catalogue.StatusCode);

        using HttpResponseMessage created = await owner.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/roles",
            new { name = "Support agent", permissions = AiAgentsView });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using HttpResponseMessage agents =
            await owner.GetAsync($"/api/tenants/{tenantId}/ai-agents");
        Assert.Equal(HttpStatusCode.OK, agents.StatusCode);

        using HttpResponseMessage agent = await owner.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/ai-agents",
            new { name = "Sales Assistant", instructions = "Answer sales questions." });
        Assert.Equal(HttpStatusCode.Created, agent.StatusCode);
    }

    [Fact]
    public async Task An_owner_holds_every_permission_the_platform_defines()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        using HttpResponseMessage mine =
            await owner.GetAsync($"/api/me/tenants/{tenantId}/permissions");

        mine.EnsureSuccessStatusCode();

        string[] held = (await mine.Content.ReadFromJsonAsync<string[]>())!;

        using HttpResponseMessage catalogue =
            await owner.GetAsync($"/api/tenants/{tenantId}/permissions");

        using JsonDocument body =
            JsonDocument.Parse(await catalogue.Content.ReadAsStringAsync());

        string[] everything =
        [
            .. body.RootElement.EnumerateArray()
                .Select(entry => entry.GetProperty("code").GetString()!)
        ];

        Assert.Equal(everything.Order(), held.Order());
    }

    [Fact]
    public async Task A_limited_member_may_do_what_they_were_granted()
    {
        (_, Guid tenantId, _) = await api.CreateOwnerAsync();

        HttpClient limited = await api.CreateRestrictedMemberAsync(
            tenantId,
            "workspace.view",
            "ai-agents.view");

        using HttpResponseMessage workspace = await limited.GetAsync($"/api/tenants/{tenantId}");
        Assert.Equal(HttpStatusCode.OK, workspace.StatusCode);

        using HttpResponseMessage agents =
            await limited.GetAsync($"/api/tenants/{tenantId}/ai-agents");
        Assert.Equal(HttpStatusCode.OK, agents.StatusCode);
    }

    [Fact]
    public async Task A_limited_member_is_refused_what_they_were_not_granted()
    {
        (_, Guid tenantId, _) = await api.CreateOwnerAsync();

        HttpClient limited = await api.CreateRestrictedMemberAsync(tenantId, "ai-agents.view");

        // 403, not 404: they belong here, and telling them their own workspace
        // had vanished would be a lie they cannot act on.
        using HttpResponseMessage workspace = await limited.GetAsync($"/api/tenants/{tenantId}");
        Assert.Equal(HttpStatusCode.Forbidden, workspace.StatusCode);

        using HttpResponseMessage roles = await limited.GetAsync($"/api/tenants/{tenantId}/roles");
        Assert.Equal(HttpStatusCode.Forbidden, roles.StatusCode);

        using HttpResponseMessage created = await limited.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/ai-agents",
            new { name = "Sales Assistant", instructions = "Answer sales questions." });
        Assert.Equal(HttpStatusCode.Forbidden, created.StatusCode);
    }

    [Fact]
    public async Task A_member_with_no_roles_at_all_can_still_read_their_own_permissions()
    {
        // A real state: somebody belongs to a workspace and has been given nothing
        // to do in it. The admin application has to be able to explain that, so
        // this endpoint answers rather than refusing.
        (_, Guid tenantId, _) = await api.CreateOwnerAsync();

        HttpClient nothing = await api.CreateRestrictedMemberAsync(tenantId);

        using HttpResponseMessage mine =
            await nothing.GetAsync($"/api/me/tenants/{tenantId}/permissions");

        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
        Assert.Empty((await mine.Content.ReadFromJsonAsync<string[]>())!);
    }

    [Fact]
    public async Task A_member_with_no_roles_is_refused_everything_else()
    {
        (_, Guid tenantId, _) = await api.CreateOwnerAsync();

        HttpClient nothing = await api.CreateRestrictedMemberAsync(tenantId);

        using HttpResponseMessage workspace = await nothing.GetAsync($"/api/tenants/{tenantId}");
        Assert.Equal(HttpStatusCode.Forbidden, workspace.StatusCode);

        using HttpResponseMessage agents =
            await nothing.GetAsync($"/api/tenants/{tenantId}/ai-agents");
        Assert.Equal(HttpStatusCode.Forbidden, agents.StatusCode);
    }

    [Fact]
    public async Task A_caller_from_another_workspace_is_answered_as_though_it_did_not_exist()
    {
        (_, Guid tenantId, _) = await api.CreateOwnerAsync();

        HttpClient stranger = await api.CreateOtherTenantUserAsync();

        // 404 everywhere, never 403: a 403 would confirm the workspace is real.
        foreach (string path in (string[])
        [
            $"/api/tenants/{tenantId}",
            $"/api/tenants/{tenantId}/roles",
            $"/api/tenants/{tenantId}/permissions",
            $"/api/tenants/{tenantId}/ai-agents",
            $"/api/me/tenants/{tenantId}/permissions",
        ])
        {
            using HttpResponseMessage response = await stranger.GetAsync(path);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Fact]
    public async Task A_workspace_that_never_existed_is_answered_identically()
    {
        // The comparison that makes the previous test mean something: a real
        // workspace somebody does not belong to and an invented id produce the
        // same status, so nothing can be probed.
        HttpClient stranger = await api.CreateOtherTenantUserAsync();
        Guid invented = Guid.CreateVersion7();

        (_, Guid real, _) = await api.CreateOwnerAsync();

        using HttpResponseMessage toReal = await stranger.GetAsync($"/api/tenants/{real}/roles");
        using HttpResponseMessage toInvented =
            await stranger.GetAsync($"/api/tenants/{invented}/roles");

        Assert.Equal(toInvented.StatusCode, toReal.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, toReal.StatusCode);
    }

    [Fact]
    public async Task An_anonymous_caller_is_told_to_sign_in()
    {
        (_, Guid tenantId, _) = await api.CreateOwnerAsync();

        using HttpClient anonymous = api.CreateClient();

        foreach (string path in (string[])
        [
            $"/api/tenants/{tenantId}/roles",
            $"/api/tenants/{tenantId}/permissions",
            $"/api/me/tenants/{tenantId}/permissions",
        ])
        {
            using HttpResponseMessage response = await anonymous.GetAsync(path);

            // 401, never 404: the caller has not said who they are, and doing so
            // may change the answer entirely.
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task A_denial_names_the_permission_and_nothing_else()
    {
        (_, Guid tenantId, _) = await api.CreateOwnerAsync();

        HttpClient limited = await api.CreateRestrictedMemberAsync(tenantId, "ai-agents.view");

        using HttpResponseMessage refused = await limited.GetAsync($"/api/tenants/{tenantId}/roles");

        string body = await refused.Content.ReadAsStringAsync();

        // The code is already public -- it is in the catalogue every role editor
        // reads -- so naming it discloses nothing while telling the caller exactly
        // what is missing.
        Assert.Contains("roles.view", body, StringComparison.Ordinal);

        // And nothing about how the workspace is organised.
        foreach (string leak in (string[])["Owner", "Limited", "tenant_role", "SELECT", "Npgsql"])
        {
            Assert.DoesNotContain(leak, body, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Membership_alone_is_enough_to_see_a_workspace_in_the_switcher()
    {
        // Discovering that you belong to a workspace must not require
        // workspace.view: somebody may belong to a tenant while currently holding
        // no product permissions at all, and they still need to be able to select
        // it and be told why it is empty.
        (_, Guid tenantId, _) = await api.CreateOwnerAsync();

        HttpClient nothing = await api.CreateRestrictedMemberAsync(tenantId);

        using HttpResponseMessage mine = await nothing.GetAsync("/api/me/tenants");

        mine.EnsureSuccessStatusCode();

        using JsonDocument body = JsonDocument.Parse(await mine.Content.ReadAsStringAsync());

        Assert.Contains(
            body.RootElement.EnumerateArray(),
            entry => entry.GetProperty("id").GetGuid() == tenantId);
    }
}
