using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using OmniAI.Api.IntegrationTests.Authorization;

namespace OmniAI.Api.IntegrationTests.Team;

/// <summary>
/// Team administration over real HTTP, against real PostgreSQL.
/// </summary>
/// <remarks>
/// The status contract this phase adds, proven end to end: <c>401</c> for an
/// anonymous caller, <c>404</c> for a non-member and for anything in another
/// workspace, <c>403</c> for a member whose roles fall short, <c>409</c> for the
/// rules no permission overcomes, and <c>412</c>/<c>428</c> for the conditional
/// write.
/// </remarks>
public sealed class TeamApiTests(RoleApiFixture api) : IClassFixture<RoleApiFixture>
{
    private static readonly string[] MembersView = ["members.view"];

    private static readonly string[] TeamManager =
        ["members.view", "members.roles.manage", "ai-agents.view"];

    private static readonly string[] AgentsView = ["ai-agents.view"];

    private static readonly string[] AgentsEdit = ["ai-agents.edit"];

    // -------------------------------------------------------------- reading

    [Fact]
    public async Task An_anonymous_caller_is_told_to_sign_in()
    {
        (_, Guid tenantId, _) = await api.CreateOwnerAsync();

        using HttpClient stranger = api.CreateClient();

        using HttpResponseMessage refused =
            await stranger.GetAsync($"/api/tenants/{tenantId}/members");

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
    }

    [Fact]
    public async Task A_caller_from_another_workspace_is_answered_as_though_it_did_not_exist()
    {
        (_, Guid tenantId, _) = await api.CreateOwnerAsync();

        HttpClient stranger = await api.CreateOtherTenantUserAsync();

        using HttpResponseMessage refused =
            await stranger.GetAsync($"/api/tenants/{tenantId}/members");

        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
    }

    [Fact]
    public async Task A_member_without_the_view_permission_is_refused()
    {
        (_, Guid tenantId, _) = await api.CreateOwnerAsync();

        HttpClient limited = await api.CreateRestrictedMemberAsync(tenantId, "ai-agents.view");

        using HttpResponseMessage refused =
            await limited.GetAsync($"/api/tenants/{tenantId}/members");

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    [Fact]
    public async Task The_owner_sees_themselves_marked_as_owner()
    {
        (HttpClient owner, Guid tenantId, Guid ownerId) = await api.CreateOwnerAsync();

        using HttpResponseMessage listed = await owner.GetAsync($"/api/tenants/{tenantId}/members");

        listed.EnsureSuccessStatusCode();

        using JsonDocument body = JsonDocument.Parse(await listed.Content.ReadAsStringAsync());
        JsonElement member = body.RootElement.GetProperty("items").EnumerateArray().Single();

        Assert.Equal(ownerId, member.GetProperty("userId").GetGuid());
        Assert.True(member.GetProperty("isOwner").GetBoolean());
        Assert.Equal("Owner", member.GetProperty("roles")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task A_member_list_never_carries_authentication_details()
    {
        // The Team screen needs to say who somebody is and what they can do.
        // Nothing about how they prove who they are belongs in an
        // administrator's list, and a field that is never displayed is only a
        // field waiting to be logged.
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        using HttpResponseMessage listed = await owner.GetAsync($"/api/tenants/{tenantId}/members");

        string body = await listed.Content.ReadAsStringAsync();

        foreach (string forbidden in
            (string[])["passwordHash", "securityStamp", "lockout", "accessFailed", "concurrency"])
        {
            Assert.DoesNotContain(forbidden, body, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task A_member_from_another_workspace_is_answered_as_absence()
    {
        // The same answer whether the account exists elsewhere or not at all, so
        // this cannot be used to discover who has registered.
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();
        (_, _, Guid strangerId) = await api.CreateOwnerAsync("Globex");

        using HttpResponseMessage missing =
            await owner.GetAsync($"/api/tenants/{tenantId}/members/{strangerId}");

        using HttpResponseMessage invented =
            await owner.GetAsync($"/api/tenants/{tenantId}/members/{Guid.CreateVersion7()}");

        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, invented.StatusCode);
    }

    // --------------------------------------------------------- assignable roles

    [Fact]
    public async Task Assignable_roles_never_include_the_owner_role()
    {
        // Excluded by kind, not by ceiling: an Owner's own ceiling contains
        // everything Owner grants, so a subset test alone would offer it to them.
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        using HttpResponseMessage listed =
            await owner.GetAsync($"/api/tenants/{tenantId}/members/assignable-roles");

        listed.EnsureSuccessStatusCode();

        Assert.DoesNotContain("Owner", await listed.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Assignable_roles_are_narrowed_to_what_the_caller_could_delegate()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        await CreateRoleAsync(owner, tenantId, "Support", AgentsView);
        await CreateRoleAsync(owner, tenantId, "Senior", AgentsEdit);

        HttpClient manager = await api.CreateRestrictedMemberAsync(tenantId, TeamManager);

        using HttpResponseMessage listed =
            await manager.GetAsync($"/api/tenants/{tenantId}/members/assignable-roles");

        listed.EnsureSuccessStatusCode();

        string body = await listed.Content.ReadAsStringAsync();

        Assert.Contains("Support", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Senior", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Assignable_roles_need_the_assign_permission()
    {
        (_, Guid tenantId, _) = await api.CreateOwnerAsync();

        HttpClient viewer = await api.CreateRestrictedMemberAsync(tenantId, MembersView);

        using HttpResponseMessage refused =
            await viewer.GetAsync($"/api/tenants/{tenantId}/members/assignable-roles");

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    // ------------------------------------------------------------ assignment

    [Fact]
    public async Task An_owner_can_give_a_member_a_role_and_the_version_advances()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        Guid support = await CreateRoleAsync(owner, tenantId, "Support", AgentsView);
        (_, Guid bobId) = await api.CreateMemberWithEmailAsync(tenantId, UniqueEmail());

        (string version, _) = await ReadMemberAsync(owner, tenantId, bobId);

        using HttpResponseMessage saved = await ConditionalPutAsync(
            owner,
            $"/api/tenants/{tenantId}/members/{bobId}/roles",
            new { roleIds = new[] { support } },
            version);

        saved.EnsureSuccessStatusCode();

        Assert.NotNull(saved.Headers.ETag);
        Assert.NotEqual(version, saved.Headers.ETag!.Tag);

        using JsonDocument body = JsonDocument.Parse(await saved.Content.ReadAsStringAsync());

        Assert.Equal(
            "Support",
            body.RootElement.GetProperty("roles")[0].GetProperty("name").GetString());
        Assert.Equal(
            ["ai-agents.view"],
            body.RootElement.GetProperty("permissions")
                .EnumerateArray()
                .Select(code => code.GetString()));
    }

    [Fact]
    public async Task A_missing_precondition_is_refused_with_428()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();
        (_, Guid bobId) = await api.CreateMemberWithEmailAsync(tenantId, UniqueEmail());

        using HttpResponseMessage refused = await owner.PutAsJsonAsync(
            $"/api/tenants/{tenantId}/members/{bobId}/roles",
            new { roleIds = Array.Empty<Guid>() });

        Assert.Equal(HttpStatusCode.PreconditionRequired, refused.StatusCode);
    }

    [Fact]
    public async Task A_stale_precondition_is_refused_with_412_and_changes_nothing()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        Guid support = await CreateRoleAsync(owner, tenantId, "Support", AgentsView);
        Guid other = await CreateRoleAsync(owner, tenantId, "Other", AgentsView);
        (_, Guid bobId) = await api.CreateMemberWithEmailAsync(tenantId, UniqueEmail());

        (string first, _) = await ReadMemberAsync(owner, tenantId, bobId);

        using HttpResponseMessage won = await ConditionalPutAsync(
            owner,
            $"/api/tenants/{tenantId}/members/{bobId}/roles",
            new { roleIds = new[] { support } },
            first);

        won.EnsureSuccessStatusCode();

        using HttpResponseMessage lost = await ConditionalPutAsync(
            owner,
            $"/api/tenants/{tenantId}/members/{bobId}/roles",
            new { roleIds = new[] { other } },
            first);

        Assert.Equal(HttpStatusCode.PreconditionFailed, lost.StatusCode);

        // The first writer's set survives: the loser changed nothing.
        (_, string roles) = await ReadMemberAsync(owner, tenantId, bobId);

        Assert.Contains("Support", roles, StringComparison.Ordinal);
        Assert.DoesNotContain("Other", roles, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Two_writers_from_one_version_produce_exactly_one_winner()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        Guid support = await CreateRoleAsync(owner, tenantId, "Support", AgentsView);
        Guid other = await CreateRoleAsync(owner, tenantId, "Other", AgentsView);
        (_, Guid bobId) = await api.CreateMemberWithEmailAsync(tenantId, UniqueEmail());

        (string version, _) = await ReadMemberAsync(owner, tenantId, bobId);

        using HttpResponseMessage first = await ConditionalPutAsync(
            owner,
            $"/api/tenants/{tenantId}/members/{bobId}/roles",
            new { roleIds = new[] { support } },
            version);

        using HttpResponseMessage second = await ConditionalPutAsync(
            owner,
            $"/api/tenants/{tenantId}/members/{bobId}/roles",
            new { roleIds = new[] { other } },
            version);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, second.StatusCode);
    }

    [Fact]
    public async Task Changing_your_own_access_is_refused_with_409()
    {
        // Not paternalism: removing your own last role in a workspace where you
        // are the only administrator locks everybody out, and there is no
        // recovery path.
        (HttpClient owner, Guid tenantId, Guid ownerId) = await api.CreateOwnerAsync();

        (string version, _) = await ReadMemberAsync(owner, tenantId, ownerId);

        using HttpResponseMessage refused = await ConditionalPutAsync(
            owner,
            $"/api/tenants/{tenantId}/members/{ownerId}/roles",
            new { roleIds = Array.Empty<Guid>() },
            version);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
    }

    [Fact]
    public async Task The_owner_s_access_cannot_be_changed_by_anybody()
    {
        (HttpClient owner, Guid tenantId, Guid ownerId) = await api.CreateOwnerAsync();

        // A second administrator holding the entire catalogue, so the refusal is
        // about the target rather than about the caller's authority.
        HttpClient manager = await api.CreateRestrictedMemberAsync(
            tenantId,
            "members.view",
            "members.roles.manage",
            "workspace.view",
            "ai-agents.view",
            "ai-agents.create",
            "ai-agents.edit",
            "ai-agents.lifecycle",
            "ai-agents.preview",
            "roles.view",
            "roles.manage",
            "members.invite");

        (string version, _) = await ReadMemberAsync(manager, tenantId, ownerId);

        using HttpResponseMessage refused = await ConditionalPutAsync(
            manager,
            $"/api/tenants/{tenantId}/members/{ownerId}/roles",
            new { roleIds = Array.Empty<Guid>() },
            version);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
    }

    [Fact]
    public async Task A_manager_cannot_assign_a_role_beyond_their_own_authority()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        Guid senior = await CreateRoleAsync(owner, tenantId, "Senior", AgentsEdit);
        (_, Guid bobId) = await api.CreateMemberWithEmailAsync(tenantId, UniqueEmail());

        HttpClient manager = await api.CreateRestrictedMemberAsync(tenantId, TeamManager);

        (string version, _) = await ReadMemberAsync(manager, tenantId, bobId);

        using HttpResponseMessage refused = await ConditionalPutAsync(
            manager,
            $"/api/tenants/{tenantId}/members/{bobId}/roles",
            new { roleIds = new[] { senior } },
            version);

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    [Fact]
    public async Task A_manager_cannot_touch_a_member_who_already_outranks_them()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        Guid support = await CreateRoleAsync(owner, tenantId, "Support", AgentsView);
        (_, Guid seniorId) = await api.CreateMemberWithEmailAsync(
            tenantId,
            UniqueEmail(),
            AgentsEdit);

        HttpClient manager = await api.CreateRestrictedMemberAsync(tenantId, TeamManager);

        (string version, _) = await ReadMemberAsync(manager, tenantId, seniorId);

        // Even narrowing them is refused: being able to rewrite somebody who
        // outranks you is taking their access away.
        using HttpResponseMessage refused = await ConditionalPutAsync(
            manager,
            $"/api/tenants/{tenantId}/members/{seniorId}/roles",
            new { roleIds = new[] { support } },
            version);

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    [Fact]
    public async Task A_role_from_another_workspace_is_not_an_alternate_path()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();
        (HttpClient theirs, Guid theirTenant, _) = await api.CreateOwnerAsync("Globex");

        Guid elsewhere = await CreateRoleAsync(theirs, theirTenant, "Theirs", AgentsView);
        (_, Guid bobId) = await api.CreateMemberWithEmailAsync(tenantId, UniqueEmail());

        (string version, _) = await ReadMemberAsync(owner, tenantId, bobId);

        using HttpResponseMessage refused = await ConditionalPutAsync(
            owner,
            $"/api/tenants/{tenantId}/members/{bobId}/roles",
            new { roleIds = new[] { elsewhere } },
            version);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    [Fact]
    public async Task Assignment_requires_the_antiforgery_token()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();
        (_, Guid bobId) = await api.CreateMemberWithEmailAsync(tenantId, UniqueEmail());

        // Phase 12's CSRF protection is not weakened by anything in this phase.
        // The session cookie travels on its own, which is exactly why an unsafe
        // request must also carry a token this origin issued.
        //
        // Removed from the client's default headers, not from the request: the
        // fixture sets it there, so a request-level removal would leave it in
        // place and the assertion would prove nothing.
        (string version, _) = await ReadMemberAsync(owner, tenantId, bobId);

        owner.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");

        using HttpResponseMessage refused = await ConditionalPutAsync(
            owner,
            $"/api/tenants/{tenantId}/members/{bobId}/roles",
            new { roleIds = Array.Empty<Guid>() },
            version);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    // ------------------------------------------------------------- arranging

    private static string UniqueEmail() => $"member-{Guid.NewGuid():N}@example.test";

    private static async Task<Guid> CreateRoleAsync(
        HttpClient client,
        Guid tenantId,
        string name,
        string[] permissions)
    {
        using HttpResponseMessage created = await client.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/roles",
            new { name, permissions });

        created.EnsureSuccessStatusCode();

        using JsonDocument body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());

        return body.RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>A member's current version, and the body as text.</summary>
    private static async Task<(string Version, string Body)> ReadMemberAsync(
        HttpClient client,
        Guid tenantId,
        Guid userId)
    {
        using HttpResponseMessage read =
            await client.GetAsync($"/api/tenants/{tenantId}/members/{userId}");

        read.EnsureSuccessStatusCode();

        return (read.Headers.ETag!.Tag, await read.Content.ReadAsStringAsync());
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
