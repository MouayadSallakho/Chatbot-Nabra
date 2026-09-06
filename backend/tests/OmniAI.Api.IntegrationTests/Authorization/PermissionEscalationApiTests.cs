using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace OmniAI.Api.IntegrationTests.Authorization;

/// <summary>
/// Deliberate attempts to acquire authority nobody granted.
/// </summary>
/// <remarks>
/// <strong>The central property: an actor may only delegate authority they
/// already hold.</strong> <c>roles.manage</c> permits role administration; it is
/// not a licence to mint capabilities.
/// <para>
/// These tests originally rested on a weaker argument -- that composing a
/// powerful role is harmless because holding one requires an assignment, and this
/// phase exposes no way to make one. That is true of a role the actor does not
/// hold, and false of one they do: a role can be assigned to the person editing
/// it, so a permission added there lands on them the moment it is saved, with no
/// assignment row changing anywhere. Two cases below asserted the weaker property
/// and are now written against the ceiling that actually holds.
/// </para>
/// </remarks>
public sealed class PermissionEscalationApiTests(RoleApiFixture api)
    : IClassFixture<RoleApiFixture>
{
    /// <summary>Every capability a custom role can be given.</summary>
    /// <remarks>
    /// Deliberately not derived from the catalogue: this set is what a caller
    /// would compose if they were trying to escalate, written out so the test
    /// keeps asking the same question even as the catalogue grows.
    /// </remarks>
    private static readonly string[] EveryProductCapability =
    [
        "workspace.view",
        "ai-agents.view",
        "ai-agents.create",
        "ai-agents.edit",
        "ai-agents.lifecycle",
        "ai-agents.preview",
    ];

    /// <summary>A permission set these cases pass to the API.</summary>
    /// <remarks>
    /// A field rather than an inline array: the analyzer objects to a
    /// constant array allocated afresh on every call, and naming the set
    /// also says what the case is actually about.
    /// </remarks>
    private static readonly string[] RolesManage = ["roles.manage"];

    /// <summary>A permission set these cases pass to the API.</summary>
    /// <remarks>
    /// A field rather than an inline array: the analyzer objects to a
    /// constant array allocated afresh on every call, and naming the set
    /// also says what the case is actually about.
    /// </remarks>
    private static readonly string[] AiAgentsCreate = ["ai-agents.create"];

    /// <summary>A permission set these cases pass to the API.</summary>
    /// <remarks>
    /// A field rather than an inline array: the analyzer objects to a
    /// constant array allocated afresh on every call, and naming the set
    /// also says what the case is actually about.
    /// </remarks>
    private static readonly string[] AiAgentsCreateAndAiAgentsLifecycle = ["ai-agents.create", "ai-agents.lifecycle"];

    /// <summary>A permission set these cases pass to the API.</summary>
    /// <remarks>
    /// A field rather than an inline array: the analyzer objects to a
    /// constant array allocated afresh on every call, and naming the set
    /// also says what the case is actually about.
    /// </remarks>
    private static readonly string[] AiAgentsView = ["ai-agents.view"];

    [Fact]
    public async Task A_member_with_only_roles_view_cannot_create_a_role()
    {
        (_, Guid tenantId, _) = await api.CreateOwnerAsync();

        HttpClient reader = await api.CreateRestrictedMemberAsync(tenantId, "roles.view");

        using HttpResponseMessage refused = await reader.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/roles",
            new { name = "Escalation", permissions = RolesManage });

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    [Fact]
    public async Task A_member_with_only_roles_view_cannot_rename_or_reassign_permissions()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        using HttpResponseMessage created = await owner.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/roles",
            new { name = "Support agent" });

        created.EnsureSuccessStatusCode();

        using JsonDocument body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        Guid roleId = body.RootElement.GetProperty("id").GetGuid();
        string version = created.Headers.ETag!.Tag;

        HttpClient reader = await api.CreateRestrictedMemberAsync(tenantId, "roles.view");

        using HttpResponseMessage rename = await ConditionalPutAsync(
            reader,
            $"/api/tenants/{tenantId}/roles/{roleId}/name",
            new { name = "Escalated" },
            version);

        Assert.Equal(HttpStatusCode.Forbidden, rename.StatusCode);

        using HttpResponseMessage permissions = await ConditionalPutAsync(
            reader,
            $"/api/tenants/{tenantId}/roles/{roleId}/permissions",
            new { permissions = RolesManage },
            version);

        Assert.Equal(HttpStatusCode.Forbidden, permissions.StatusCode);
    }

    [Fact]
    public async Task Managing_roles_does_not_give_the_editor_the_permissions_they_grant()
    {
        // The escalation that matters. A member holding roles.view and
        // roles.manage tries to compose a role granting every product capability.
        // The codes are all real, so this is not a malformed request -- it is
        // authority they do not have to give, and it is refused as one.
        (_, Guid tenantId, _) = await api.CreateOwnerAsync();

        HttpClient manager = await api.CreateRestrictedMemberAsync(
            tenantId,
            "roles.view",
            "roles.manage");

        using HttpResponseMessage created = await manager.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/roles",
            new
            {
                name = "Everything",
                permissions = EveryProductCapability,
            });

        Assert.Equal(HttpStatusCode.Forbidden, created.StatusCode);

        // Nothing was stored, and the manager's own authority is untouched.
        using HttpResponseMessage roles = await manager.GetAsync($"/api/tenants/{tenantId}/roles");

        roles.EnsureSuccessStatusCode();

        Assert.DoesNotContain("Everything", await roles.Content.ReadAsStringAsync());

        using HttpResponseMessage agents =
            await manager.GetAsync($"/api/tenants/{tenantId}/ai-agents");

        Assert.Equal(HttpStatusCode.Forbidden, agents.StatusCode);

        using HttpResponseMessage workspace = await manager.GetAsync($"/api/tenants/{tenantId}");

        Assert.Equal(HttpStatusCode.Forbidden, workspace.StatusCode);
    }

    [Fact]
    public async Task Editing_a_role_does_not_change_the_editors_own_permissions()
    {
        (_, Guid tenantId, _) = await api.CreateOwnerAsync();

        // The editor holds ai-agents.create as well, so composing a role that
        // grants it is within their authority and must work -- the ceiling is a
        // limit, not a prohibition on delegating at all.
        HttpClient manager = await api.CreateRestrictedMemberAsync(
            tenantId,
            "roles.view",
            "roles.manage",
            "ai-agents.create");

        string[] before = await PermissionsOfAsync(manager, tenantId);

        using HttpResponseMessage created = await manager.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/roles",
            new { name = "Everything", permissions = AiAgentsCreate });

        created.EnsureSuccessStatusCode();

        using JsonDocument body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        Guid roleId = body.RootElement.GetProperty("id").GetGuid();

        // Widening it to a capability the editor does not hold is refused, even
        // though they may administer this role and the code is a real one.
        using HttpResponseMessage widened = await ConditionalPutAsync(
            manager,
            $"/api/tenants/{tenantId}/roles/{roleId}/permissions",
            new { permissions = AiAgentsCreateAndAiAgentsLifecycle },
            created.Headers.ETag!.Tag);

        Assert.Equal(HttpStatusCode.Forbidden, widened.StatusCode);

        // Unchanged, both ways: the role kept the set it had, and the editor's own
        // authority did not move.
        using HttpResponseMessage reread =
            await manager.GetAsync($"/api/tenants/{tenantId}/roles/{roleId}");

        reread.EnsureSuccessStatusCode();

        using JsonDocument current = JsonDocument.Parse(await reread.Content.ReadAsStringAsync());

        Assert.Equal(
            AiAgentsCreate,
            current.RootElement.GetProperty("permissions")
                .EnumerateArray()
                .Select(code => code.GetString()));

        Assert.Equal(before, await PermissionsOfAsync(manager, tenantId));
    }

    [Fact]
    public async Task A_role_id_from_another_workspace_is_not_an_alternate_modification_path()
    {
        // The cross-tenant shape. A caller with full authority in their own
        // workspace names somebody else's role through their own route, which is
        // how a transposed identifier would look.
        (HttpClient mine, Guid myTenant, _) = await api.CreateOwnerAsync();
        (HttpClient theirs, Guid theirTenant, _) = await api.CreateOwnerAsync("Globex");

        using HttpResponseMessage created = await theirs.PostAsJsonAsync(
            $"/api/tenants/{theirTenant}/roles",
            new { name = "Their role", permissions = AiAgentsView });

        created.EnsureSuccessStatusCode();

        using JsonDocument body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        Guid theirRole = body.RootElement.GetProperty("id").GetGuid();
        string theirVersion = created.Headers.ETag!.Tag;

        // Through my own workspace: not found, even with their genuine version.
        using HttpResponseMessage throughMine = await ConditionalPutAsync(
            mine,
            $"/api/tenants/{myTenant}/roles/{theirRole}/permissions",
            new { permissions = RolesManage },
            theirVersion);

        Assert.Equal(HttpStatusCode.NotFound, throughMine.StatusCode);

        // Through their workspace: not found either, because I am not a member.
        using HttpResponseMessage throughTheirs = await ConditionalPutAsync(
            mine,
            $"/api/tenants/{theirTenant}/roles/{theirRole}/permissions",
            new { permissions = RolesManage },
            theirVersion);

        Assert.Equal(HttpStatusCode.NotFound, throughTheirs.StatusCode);

        // And their role is untouched.
        using HttpResponseMessage read =
            await theirs.GetAsync($"/api/tenants/{theirTenant}/roles/{theirRole}");

        using JsonDocument after = JsonDocument.Parse(await read.Content.ReadAsStringAsync());

        Assert.Equal(
            ["ai-agents.view"],
            after.RootElement.GetProperty("permissions")
                .EnumerateArray()
                .Select(entry => entry.GetString()));
    }

    [Fact]
    public async Task A_forged_permission_code_is_refused_rather_than_stored()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        foreach (string forged in
            (string[])["tenant.admin", "roles.*", "billing.manage", "ai-agents.delete", "*"])
        {
            using HttpResponseMessage refused = await owner.PostAsJsonAsync(
                $"/api/tenants/{tenantId}/roles",
                new { name = $"Forged {Guid.NewGuid():N}"[..20], permissions = new[] { forged } });

            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        }
    }

    [Fact]
    public async Task An_anonymous_caller_cannot_create_or_edit_a_role()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        using HttpResponseMessage created = await owner.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/roles",
            new { name = "Support agent" });

        created.EnsureSuccessStatusCode();

        using JsonDocument body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        Guid roleId = body.RootElement.GetProperty("id").GetGuid();

        using HttpClient anonymous = api.CreateClient();

        using HttpResponseMessage refused = await anonymous.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/roles",
            new { name = "Anonymous role" });

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);

        using HttpResponseMessage rename = await ConditionalPutAsync(
            anonymous,
            $"/api/tenants/{tenantId}/roles/{roleId}/name",
            new { name = "Anonymous rename" },
            created.Headers.ETag!.Tag);

        Assert.Equal(HttpStatusCode.Unauthorized, rename.StatusCode);
    }

    [Fact]
    public async Task Role_mutations_still_require_the_antiforgery_token()
    {
        // Phase 12's CSRF protection is not weakened by anything in this phase.
        // The session cookie travels on its own, which is exactly why an unsafe
        // request must also carry a token this origin issued.
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        owner.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");

        using HttpResponseMessage refused = await owner.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/roles",
            new { name = "Forged" });

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    /// <summary>What this client may currently do in a workspace.</summary>
    private static async Task<string[]> PermissionsOfAsync(HttpClient client, Guid tenantId)
    {
        using HttpResponseMessage response =
            await client.GetAsync($"/api/me/tenants/{tenantId}/permissions");

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<string[]>())!;
    }

    private static async Task<HttpResponseMessage> ConditionalPutAsync(
        HttpClient client,
        string path,
        object payload,
        string ifMatch)
    {
        using HttpRequestMessage request = new(HttpMethod.Put, path)
        {
            Content = JsonContent.Create(payload),
        };

        request.Headers.IfMatch.Add(new EntityTagHeaderValue(ifMatch));

        return await client.SendAsync(request);
    }
}
