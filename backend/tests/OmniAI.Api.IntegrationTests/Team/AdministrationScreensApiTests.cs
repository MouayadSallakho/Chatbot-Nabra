using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OmniAI.Api.IntegrationTests.Authorization;
using OmniAI.Domain.Invitations;
using OmniAI.Infrastructure.Persistence;

namespace OmniAI.Api.IntegrationTests.Team;

/// <summary>
/// Phase 37.5b: what the redesigned team, roles and permission screens asked for.
/// Members per role (row 23), the invitation status filter (row 24), the
/// catalogue's group keys (row 26), and the stable codes of these areas (row 28b),
/// each with its permission, tenant isolation and validation.
/// </summary>
public sealed class AdministrationScreensApiTests(RoleApiFixture api) : IClassFixture<RoleApiFixture>
{
    private static readonly string[] AgentsView = ["ai-agents.view"];
    private static readonly string[] Unknown = ["nothing.real"];
    private static readonly string[] Malformed = ["NOT A CODE"];

    // ---- members per role (row 23) -----------------------------------------

    [Fact]
    public async Task Roles_say_how_many_members_hold_them_and_follow_a_role_change()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();
        (_, Guid member) = await api.CreateMemberWithEmailAsync(tenantId, ApiAuthentication.UniqueEmail(), "ai-agents.view");
        Guid support = await CreateRoleAsync(owner, tenantId, "Support", "ai-agents.view");

        JsonElement[] before = await RolesAsync(owner, tenantId);
        Assert.Equal(1, Named(before, "Owner").GetProperty("memberCount").GetInt32());
        Assert.Equal(0, Named(before, "Support").GetProperty("memberCount").GetInt32());
        Assert.Equal(1, Limited(before).GetProperty("memberCount").GetInt32());

        // Moved from their seeded role to Support through the team endpoint.
        using HttpResponseMessage moved = await ReplaceMemberRolesAsync(owner, tenantId, member, support);
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);

        JsonElement[] after = await RolesAsync(owner, tenantId);
        Assert.Equal(1, Named(after, "Support").GetProperty("memberCount").GetInt32());
        Assert.Equal(0, Limited(after).GetProperty("memberCount").GetInt32());

        using JsonDocument detail = await JsonAsync(owner, $"/api/tenants/{tenantId}/roles/{support}");
        Assert.Equal(1, detail.RootElement.GetProperty("memberCount").GetInt32());
        Assert.Equal(["ai-agents.view"], detail.RootElement.GetProperty("permissions").EnumerateArray().Select(code => code.GetString()));
    }

    [Fact]
    public async Task A_role_write_keeps_its_exact_shape_without_a_member_count()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        using HttpResponseMessage created = await owner.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/roles",
            new { name = "Writers", permissions = AgentsView });
        using JsonDocument body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.False(body.RootElement.TryGetProperty("memberCount", out _));
    }

    [Fact]
    public async Task Member_counts_need_roles_view_and_stay_in_their_workspace()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();
        Guid support = await CreateRoleAsync(owner, tenantId, "Support", "ai-agents.view");

        HttpClient withoutRolesView = await api.CreateRestrictedMemberAsync(tenantId, "members.view");
        HttpClient stranger = await api.CreateOtherTenantUserAsync();

        using HttpResponseMessage refused = await withoutRolesView.GetAsync($"/api/tenants/{tenantId}/roles");
        using HttpResponseMessage strangerList = await stranger.GetAsync($"/api/tenants/{tenantId}/roles");
        using HttpResponseMessage strangerDetail = await stranger.GetAsync($"/api/tenants/{tenantId}/roles/{support}");

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, strangerList.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, strangerDetail.StatusCode);
    }

    // ---- the invitation status filter (row 24) -----------------------------

    [Fact]
    public async Task The_invitation_list_filters_by_the_status_it_shows()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        Guid pending = await InviteAsync(owner, tenantId, ApiAuthentication.UniqueEmail());
        Guid stale = await InviteAsync(owner, tenantId, ApiAuthentication.UniqueEmail());
        Guid revoked = await InviteAsync(owner, tenantId, ApiAuthentication.UniqueEmail());

        string inviteeEmail = ApiAuthentication.UniqueEmail();
        (string token, Guid accepted) = await InviteWithTokenAsync(owner, tenantId, inviteeEmail);
        (HttpClient invitee, _) = await api.CreateAccountAsync(inviteeEmail);
        using (HttpResponseMessage accept = await invitee.PostAsJsonAsync("/api/invitations/accept", new { token }))
        {
            Assert.Equal(HttpStatusCode.OK, accept.StatusCode);
        }

        using (HttpResponseMessage revoke = await owner.PostAsync($"/api/tenants/{tenantId}/invitations/{revoked}/revoke", null))
        {
            Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        }

        await AgeAsync(stale);

        // Newest first; the aged invitation was moved ten days back.
        Assert.Equal([accepted, revoked, pending, stale], await ListedAsync(owner, tenantId, null, total: 4));
        Assert.Equal([pending], await ListedAsync(owner, tenantId, "Pending", total: 1));
        Assert.Equal([stale], await ListedAsync(owner, tenantId, "Expired", total: 1));
        Assert.Equal([accepted], await ListedAsync(owner, tenantId, "Accepted", total: 1));
        Assert.Equal([revoked], await ListedAsync(owner, tenantId, "revoked", total: 1));

        // Paging works inside a filter, and total counts the filter.
        using JsonDocument page = await JsonAsync(owner, $"/api/tenants/{tenantId}/invitations?status=Expired&page=2&pageSize=1");
        Assert.Equal(0, page.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(1, page.RootElement.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task An_unknown_status_is_refused_with_its_code_identically_for_anybody()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();
        HttpClient stranger = await api.CreateOtherTenantUserAsync();

        using HttpResponseMessage forOwner = await owner.GetAsync($"/api/tenants/{tenantId}/invitations?status=Open");
        using HttpResponseMessage forStranger = await stranger.GetAsync($"/api/tenants/{tenantId}/invitations?status=Open");
        using HttpResponseMessage numeric = await owner.GetAsync($"/api/tenants/{tenantId}/invitations?status=0");

        foreach (HttpResponseMessage refused in (HttpResponseMessage[])[forOwner, forStranger, numeric])
        {
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Equal("invitations.status.invalid", await CodeAsync(refused));
        }

        Assert.Equal(WithoutTrace(await forOwner.Content.ReadAsStringAsync()), WithoutTrace(await forStranger.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task A_valid_status_keeps_the_list_s_permission_and_isolation()
    {
        (_, Guid tenantId, _) = await api.CreateOwnerAsync();
        HttpClient withoutMembersView = await api.CreateRestrictedMemberAsync(tenantId, "roles.view");
        HttpClient stranger = await api.CreateOtherTenantUserAsync();

        using HttpResponseMessage refused = await withoutMembersView.GetAsync($"/api/tenants/{tenantId}/invitations?status=Pending");
        using HttpResponseMessage hidden = await stranger.GetAsync($"/api/tenants/{tenantId}/invitations?status=Pending");

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
    }

    // ---- the catalogue's group keys (row 26) -------------------------------

    [Fact]
    public async Task Every_catalogue_entry_carries_its_group_key()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        using JsonDocument catalogue = await JsonAsync(owner, $"/api/tenants/{tenantId}/permissions");
        JsonElement[] entries = [.. catalogue.RootElement.EnumerateArray()];

        Assert.All(entries, entry => Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("groupKey").GetString())));
        Assert.Equal("ai-agents", entries.Single(entry => entry.GetProperty("code").GetString() == "ai-agents.edit").GetProperty("groupKey").GetString());
        Assert.Equal("channels", entries.Single(entry => entry.GetProperty("code").GetString() == "whatsapp.manage").GetProperty("groupKey").GetString());
        Assert.Equal(
            ["workspace", "ai-agents", "roles", "members", "conversations", "customers", "channels", "knowledge", "analytics", "reports", "billing"],
            entries.Select(entry => entry.GetProperty("groupKey").GetString()).Distinct());

        // The English fields are still there, unchanged.
        Assert.All(entries, entry => Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("group").GetString())));
    }

    // ---- stable codes for roles, invitations and team (row 28b) -----------

    [Fact]
    public async Task Role_refusals_reach_the_client_with_their_codes()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();
        await CreateRoleAsync(owner, tenantId, "Support", "ai-agents.view");

        (object Body, HttpStatusCode Status, string Code)[] cases =
        [
            (new { name = "" }, HttpStatusCode.BadRequest, "roles.name.required"),
            (new { name = new string('x', 300) }, HttpStatusCode.BadRequest, "roles.name.too_long"),
            (new { name = "Line\nbreak" }, HttpStatusCode.BadRequest, "roles.name.control_characters"),
            (new { name = "support" }, HttpStatusCode.Conflict, "roles.name.taken"),
            (new { name = "Readers", permissions = Unknown }, HttpStatusCode.BadRequest, "roles.permission.unknown"),
            (new { name = "Readers", permissions = Malformed }, HttpStatusCode.BadRequest, "roles.permission.invalid"),
        ];

        foreach ((object body, HttpStatusCode status, string code) in cases)
        {
            using HttpResponseMessage refused = await owner.PostAsync($"/api/tenants/{tenantId}/roles", JsonContent.Create(body, body.GetType()));

            Assert.Equal(status, refused.StatusCode);
            Assert.Equal(code, await CodeAsync(refused));
        }

        Guid ownerRole = Named(await RolesAsync(owner, tenantId), "Owner").GetProperty("id").GetGuid();
        using HttpResponseMessage read = await owner.GetAsync($"/api/tenants/{tenantId}/roles/{ownerRole}");
        using HttpRequestMessage rename = new(HttpMethod.Put, $"/api/tenants/{tenantId}/roles/{ownerRole}/name")
        {
            Content = JsonContent.Create(new { name = "Boss" }),
        };
        rename.Headers.IfMatch.Add(new EntityTagHeaderValue(read.Headers.ETag!.Tag));

        using HttpResponseMessage protectedRole = await owner.SendAsync(rename);

        Assert.Equal(HttpStatusCode.Conflict, protectedRole.StatusCode);
        Assert.Equal("roles.system.protected", await CodeAsync(protectedRole));
    }

    [Fact]
    public async Task Invitation_and_team_refusals_reach_the_client_with_their_codes()
    {
        (HttpClient owner, Guid tenantId, Guid ownerId) = await api.CreateOwnerAsync();
        string ownerEmail = await RoleApiFixture.CurrentEmailAsync(owner);
        (_, Guid member) = await api.CreateMemberWithEmailAsync(tenantId, ApiAuthentication.UniqueEmail(), "ai-agents.view");

        string invited = ApiAuthentication.UniqueEmail();
        Guid invitation = await InviteAsync(owner, tenantId, invited);

        await AssertCodeAsync(await PostInviteAsync(owner, tenantId, "not-an-address"), HttpStatusCode.BadRequest, "email.invalid");
        await AssertCodeAsync(await PostInviteAsync(owner, tenantId, "two words@example.test"), HttpStatusCode.BadRequest, "email.invalid_characters");
        await AssertCodeAsync(await PostInviteAsync(owner, tenantId, ""), HttpStatusCode.BadRequest, "email.required");
        await AssertCodeAsync(await PostInviteAsync(owner, tenantId, invited), HttpStatusCode.Conflict, "invitations.email.already_invited");
        await AssertCodeAsync(await PostInviteAsync(owner, tenantId, ownerEmail), HttpStatusCode.Conflict, "invitations.email.already_member");
        await AssertCodeAsync(await PostInviteAsync(owner, tenantId, ApiAuthentication.UniqueEmail(), Guid.CreateVersion7()), HttpStatusCode.BadRequest, "invitations.role.unassignable");

        using (HttpResponseMessage first = await owner.PostAsync($"/api/tenants/{tenantId}/invitations/{invitation}/revoke", null))
        {
            Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        }

        await AssertCodeAsync(
            await owner.PostAsync($"/api/tenants/{tenantId}/invitations/{invitation}/revoke", null),
            HttpStatusCode.Conflict,
            "invitations.revoke.closed");

        await AssertCodeAsync(await ReplaceMemberRolesAsync(owner, tenantId, ownerId), HttpStatusCode.Conflict, "team.member.self");
        await AssertCodeAsync(await ReplaceMemberRolesAsync(owner, tenantId, member, Guid.CreateVersion7()), HttpStatusCode.BadRequest, "team.role.unassignable");
    }

    [Fact]
    public async Task The_owner_s_access_is_protected_with_its_own_code()
    {
        (_, Guid tenantId, Guid ownerId) = await api.CreateOwnerAsync();

        // A second administrator, so the request is not refused as self-editing.
        HttpClient admin = await api.CreateRestrictedMemberAsync(tenantId, "members.view", "members.roles.manage", "ai-agents.view");

        await AssertCodeAsync(await ReplaceMemberRolesAsync(admin, tenantId, ownerId), HttpStatusCode.Conflict, "team.member.owner_protected");
    }

    // ---- arranging ---------------------------------------------------------

    private static async Task AssertCodeAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        using (response)
        {
            Assert.Equal(status, response.StatusCode);
            Assert.Equal(code, await CodeAsync(response));
        }
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return body.RootElement.TryGetProperty("code", out JsonElement code) ? code.GetString() : null;
    }

    private static string WithoutTrace(string body)
    {
        using JsonDocument problem = JsonDocument.Parse(body);

        return JsonSerializer.Serialize(
            problem.RootElement.EnumerateObject()
                .Where(property => property.Name != "traceId")
                .ToDictionary(property => property.Name, property => property.Value.ToString()));
    }

    private static async Task<JsonDocument> JsonAsync(HttpClient client, string path)
    {
        using HttpResponseMessage response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static async Task<JsonElement[]> RolesAsync(HttpClient client, Guid tenantId)
    {
        using JsonDocument roles = await JsonAsync(client, $"/api/tenants/{tenantId}/roles");

        return [.. roles.RootElement.EnumerateArray().Select(role => role.Clone())];
    }

    private static JsonElement Named(JsonElement[] roles, string name) =>
        roles.Single(role => role.GetProperty("name").GetString() == name);

    /// <summary>The role the fixture seeded for a member ("Limited …").</summary>
    private static JsonElement Limited(JsonElement[] roles) =>
        roles.Single(role => role.GetProperty("name").GetString()!.StartsWith("Limited", StringComparison.Ordinal));

    private static async Task<Guid> CreateRoleAsync(HttpClient client, Guid tenantId, string name, params string[] permissions)
    {
        using HttpResponseMessage created = await client.PostAsJsonAsync($"/api/tenants/{tenantId}/roles", new { name, permissions });
        created.EnsureSuccessStatusCode();

        using JsonDocument body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());

        return body.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<HttpResponseMessage> ReplaceMemberRolesAsync(HttpClient client, Guid tenantId, Guid userId, params Guid[] roleIds)
    {
        using HttpResponseMessage read = await client.GetAsync($"/api/tenants/{tenantId}/members/{userId}");
        read.EnsureSuccessStatusCode();

        using HttpRequestMessage request = new(HttpMethod.Put, $"/api/tenants/{tenantId}/members/{userId}/roles")
        {
            Content = JsonContent.Create(new { roleIds }),
        };
        request.Headers.IfMatch.Add(new EntityTagHeaderValue(read.Headers.ETag!.Tag));

        return await client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> PostInviteAsync(HttpClient client, Guid tenantId, string email, params Guid[] roleIds) =>
        client.PostAsJsonAsync($"/api/tenants/{tenantId}/invitations", new { email, roleIds });

    private static async Task<Guid> InviteAsync(HttpClient client, Guid tenantId, string email) =>
        (await InviteWithTokenAsync(client, tenantId, email)).InvitationId;

    private static async Task<(string Token, Guid InvitationId)> InviteWithTokenAsync(HttpClient client, Guid tenantId, string email)
    {
        using HttpResponseMessage created = await PostInviteAsync(client, tenantId, email);
        created.EnsureSuccessStatusCode();

        using JsonDocument body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());

        return (
            body.RootElement.GetProperty("token").GetString()!,
            body.RootElement.GetProperty("invitation").GetProperty("id").GetGuid());
    }

    /// <summary>
    /// Moves an invitation's life into the past, still stored as pending: the case
    /// the filter must call expired. Test arrangement only.
    /// </summary>
    private async Task AgeAsync(Guid invitationId)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        await using OmniAiDbContext context = api.CreateDbContext();

        int changed = await context.TenantInvitations
            .Where(invitation => invitation.Id == TenantInvitationId.From(invitationId))
            .ExecuteUpdateAsync(set => set
                .SetProperty(invitation => invitation.CreatedAt, now.AddDays(-10))
                .SetProperty(invitation => invitation.ExpiresAt, now.AddDays(-3)));

        Assert.Equal(1, changed);
    }

    private static async Task<Guid[]> ListedAsync(HttpClient client, Guid tenantId, string? status, int total)
    {
        string query = status is null ? string.Empty : $"&status={status}";

        using JsonDocument page = await JsonAsync(client, $"/api/tenants/{tenantId}/invitations?pageSize=100{query}");

        Assert.Equal(total, page.RootElement.GetProperty("total").GetInt32());

        if (status is not null)
        {
            Assert.All(
                page.RootElement.GetProperty("items").EnumerateArray(),
                item => Assert.Equal(status, item.GetProperty("status").GetString(), ignoreCase: true));
        }

        return
        [
            .. page.RootElement.GetProperty("items").EnumerateArray()
                .Select(item => item.GetProperty("id").GetGuid())
                .Where(id => id != Guid.Empty),
        ];
    }
}
