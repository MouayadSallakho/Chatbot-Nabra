using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OmniAI.Api.IntegrationTests.Authorization;

namespace OmniAI.Api.IntegrationTests.Team;

/// <summary>
/// Invitations over real HTTP, against real PostgreSQL.
/// </summary>
/// <remarks>
/// <strong>An invitation is a bearer secret with an intent attached.</strong>
/// Holding the token is one half of what it takes to use it; being signed in as
/// the address it names is the other. These prove both halves, that the secret is
/// never stored or returned again, and that the authority behind an invitation is
/// re-checked at the moment somebody accepts rather than trusted from when it was
/// written.
/// </remarks>
public sealed class InvitationApiTests(RoleApiFixture api) : IClassFixture<RoleApiFixture>
{
    private static readonly string[] AgentsView = ["ai-agents.view"];

    private static readonly string[] AgentsEdit = ["ai-agents.edit"];

    private static readonly string[] Inviter = ["members.view", "members.invite"];

    private static readonly string[] FullInviter =
        ["members.view", "members.invite", "members.roles.manage", "ai-agents.view"];

    // ------------------------------------------------------- token security

    [Fact]
    public async Task Creating_an_invitation_returns_the_token_once_and_forbids_caching_it()
    {
        // The response carries a bearer credential. Without no-store a proxy or a
        // browser cache would be entitled to keep a copy on disk, and the link
        // would outlive the moment it was shown.
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        using HttpResponseMessage created = await InviteAsync(owner, tenantId, Unique());

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Contains("no-store", created.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.Ordinal);

        using JsonDocument body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());

        Assert.False(string.IsNullOrEmpty(body.RootElement.GetProperty("token").GetString()));
    }

    [Fact]
    public async Task The_token_is_long_and_opaque_rather_than_an_identifier()
    {
        // Not a GUID and not derived from the invitation's id: a UUIDv7 embeds the
        // millisecond it was created and leaves far fewer unpredictable bits, so
        // guessing around a known invitation time would be realistic.
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        (string token, Guid invitationId) = await CreateInvitationAsync(owner, tenantId, Unique());

        Assert.True(token.Length >= 40);
        Assert.DoesNotContain(invitationId.ToString(), token, StringComparison.OrdinalIgnoreCase);
        Assert.Matches("^[A-Za-z0-9_-]+$", token);
    }

    [Fact]
    public async Task Two_invitations_never_share_a_token()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        (string first, _) = await CreateInvitationAsync(owner, tenantId, Unique());
        (string second, _) = await CreateInvitationAsync(owner, tenantId, Unique());

        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task No_later_read_ever_returns_the_token_or_its_digest()
    {
        // If a listing could show it, every administrator could accept every
        // outstanding invitation.
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        (string token, _) = await CreateInvitationAsync(owner, tenantId, Unique());

        using HttpResponseMessage listed =
            await owner.GetAsync($"/api/tenants/{tenantId}/invitations");

        listed.EnsureSuccessStatusCode();

        string body = await listed.Content.ReadAsStringAsync();

        Assert.DoesNotContain(token, body, StringComparison.Ordinal);
        Assert.DoesNotContain("token", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hash", body, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------- who may invite

    [Fact]
    public async Task Inviting_needs_the_invite_permission()
    {
        (_, Guid tenantId, _) = await api.CreateOwnerAsync();

        HttpClient viewer = await api.CreateRestrictedMemberAsync(tenantId, "members.view");

        using HttpResponseMessage refused = await InviteAsync(viewer, tenantId, Unique());

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    [Fact]
    public async Task An_anonymous_caller_cannot_invite()
    {
        (_, Guid tenantId, _) = await api.CreateOwnerAsync();

        using HttpClient stranger = api.CreateClient();

        using HttpResponseMessage refused = await stranger.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/invitations",
            new { email = Unique(), roleIds = Array.Empty<Guid>() });

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
    }

    [Fact]
    public async Task A_non_member_is_answered_as_though_the_workspace_did_not_exist()
    {
        (_, Guid tenantId, _) = await api.CreateOwnerAsync();

        HttpClient stranger = await api.CreateOtherTenantUserAsync();

        using HttpResponseMessage refused = await InviteAsync(stranger, tenantId, Unique());

        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
    }

    [Fact]
    public async Task Inviting_with_no_roles_needs_only_the_invite_permission()
    {
        // Legitimate and useful: they arrive able to see the workspace and do
        // nothing, and an administrator grants access afterwards.
        (_, Guid tenantId, _) = await api.CreateOwnerAsync();

        HttpClient inviter = await api.CreateRestrictedMemberAsync(tenantId, Inviter);

        using HttpResponseMessage created = await InviteAsync(inviter, tenantId, Unique());

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
    }

    [Fact]
    public async Task Inviting_with_roles_needs_the_assign_permission_as_well()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        Guid support = await CreateRoleAsync(owner, tenantId, "Support", AgentsView);

        HttpClient inviter = await api.CreateRestrictedMemberAsync(tenantId, Inviter);

        using HttpResponseMessage refused =
            await InviteAsync(inviter, tenantId, Unique(), support);

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    [Fact]
    public async Task An_inviter_cannot_offer_authority_they_do_not_hold()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        Guid senior = await CreateRoleAsync(owner, tenantId, "Senior", AgentsEdit);

        HttpClient inviter = await api.CreateRestrictedMemberAsync(tenantId, FullInviter);

        using HttpResponseMessage refused = await InviteAsync(inviter, tenantId, Unique(), senior);

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    [Fact]
    public async Task Inviting_somebody_already_on_the_team_is_a_conflict()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        string email = Unique();
        await api.CreateMemberWithEmailAsync(tenantId, email);

        using HttpResponseMessage refused = await InviteAsync(owner, tenantId, email);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
    }

    [Fact]
    public async Task A_second_open_invitation_for_one_address_is_a_conflict()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        string email = Unique();

        using HttpResponseMessage first = await InviteAsync(owner, tenantId, email);

        first.EnsureSuccessStatusCode();

        // Differing only in case: the rule is canonical, so this is the same
        // address.
        using HttpResponseMessage second =
            await InviteAsync(owner, tenantId, email.ToUpperInvariant());

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Concurrent_invitations_for_one_address_produce_exactly_one_winner()
    {
        // The partial unique index is what decides, not a preceding read: two
        // administrators inviting the same person at once both pass any
        // check-then-insert.
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        string email = Unique();

        HttpResponseMessage[] answers = await Task.WhenAll(
            InviteAsync(owner, tenantId, email),
            InviteAsync(owner, tenantId, email));

        try
        {
            Assert.Single(answers, answer => answer.StatusCode == HttpStatusCode.Created);
            Assert.Single(answers, answer => answer.StatusCode == HttpStatusCode.Conflict);
        }
        finally
        {
            foreach (HttpResponseMessage answer in answers)
            {
                answer.Dispose();
            }
        }
    }

    // ------------------------------------------------------------ acceptance

    [Fact]
    public async Task The_invited_account_joins_and_receives_the_intended_roles()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        Guid support = await CreateRoleAsync(owner, tenantId, "Support", AgentsView);

        string email = Unique();
        (string token, _) = await CreateInvitationAsync(owner, tenantId, email, support);

        (HttpClient invitee, _) = await api.CreateAccountAsync(email);

        using HttpResponseMessage accepted = await AcceptAsync(invitee, token);

        accepted.EnsureSuccessStatusCode();

        using JsonDocument body = JsonDocument.Parse(await accepted.Content.ReadAsStringAsync());

        Assert.Equal(tenantId, body.RootElement.GetProperty("tenantId").GetGuid());

        // They can now see the workspace, and hold exactly what was intended.
        using HttpResponseMessage permissions =
            await invitee.GetAsync($"/api/me/tenants/{tenantId}/permissions");

        permissions.EnsureSuccessStatusCode();

        Assert.Equal(
            AgentsView,
            await permissions.Content.ReadFromJsonAsync<string[]>());
    }

    [Fact]
    public async Task A_different_account_cannot_use_a_genuine_invitation()
    {
        // The second half of what makes an invitation safe. Without it a forwarded
        // link, a shared screen or a mail archive would be enough for anybody.
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        (string token, _) = await CreateInvitationAsync(owner, tenantId, Unique());

        (HttpClient mallory, Guid malloryId) = await api.CreateAccountAsync(Unique());

        using HttpResponseMessage refused = await AcceptAsync(mallory, token);

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        // And no membership was created for them.
        using HttpResponseMessage workspaces = await mallory.GetAsync("/api/me/tenants");

        workspaces.EnsureSuccessStatusCode();

        Assert.Empty(await workspaces.Content.ReadFromJsonAsync<object[]>() ?? []);
        Assert.NotEqual(Guid.Empty, malloryId);
    }

    [Fact]
    public async Task An_anonymous_caller_cannot_accept()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        (string token, _) = await CreateInvitationAsync(owner, tenantId, Unique());

        using HttpClient stranger = api.CreateClient();

        await ApiAuthentication.BootstrapAntiforgeryAsync(stranger);

        using HttpResponseMessage refused =
            await stranger.PostAsJsonAsync("/api/invitations/accept", new { token });

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
    }

    [Fact]
    public async Task A_token_that_matches_nothing_is_answered_as_absence()
    {
        (HttpClient invitee, _) = await api.CreateAccountAsync(Unique());

        using HttpResponseMessage refused =
            await AcceptAsync(invitee, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");

        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
    }

    [Fact]
    public async Task A_token_cannot_be_used_twice()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        string email = Unique();
        (string token, _) = await CreateInvitationAsync(owner, tenantId, email);

        (HttpClient invitee, _) = await api.CreateAccountAsync(email);

        using HttpResponseMessage first = await AcceptAsync(invitee, token);

        first.EnsureSuccessStatusCode();

        using HttpResponseMessage second = await AcceptAsync(invitee, token);

        Assert.Equal(HttpStatusCode.Gone, second.StatusCode);
    }

    [Fact]
    public async Task Concurrent_acceptance_of_one_token_admits_exactly_one()
    {
        // The invitation is concluded by a conditional update with the pending
        // state in its predicate, so whichever transaction commits first wins and
        // the other finds nothing to conclude.
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        string email = Unique();
        (string token, _) = await CreateInvitationAsync(owner, tenantId, email);

        (HttpClient invitee, _) = await api.CreateAccountAsync(email);

        HttpResponseMessage[] answers = await Task.WhenAll(
            AcceptAsync(invitee, token),
            AcceptAsync(invitee, token));

        try
        {
            Assert.Single(answers, answer => answer.StatusCode == HttpStatusCode.OK);
            Assert.Single(answers, answer => answer.StatusCode != HttpStatusCode.OK);
        }
        finally
        {
            foreach (HttpResponseMessage answer in answers)
            {
                answer.Dispose();
            }
        }

        // Exactly one membership, whatever the race did.
        using HttpResponseMessage workspaces = await invitee.GetAsync("/api/me/tenants");

        workspaces.EnsureSuccessStatusCode();

        Assert.Single(await workspaces.Content.ReadFromJsonAsync<object[]>() ?? []);
    }

    // ------------------------------------------------------------- revocation

    [Fact]
    public async Task A_revoked_invitation_cannot_be_accepted()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        string email = Unique();
        (string token, Guid invitationId) = await CreateInvitationAsync(owner, tenantId, email);

        using HttpResponseMessage revoked = await owner.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/invitations/{invitationId}/revoke",
            new { });

        revoked.EnsureSuccessStatusCode();

        (HttpClient invitee, _) = await api.CreateAccountAsync(email);

        using HttpResponseMessage refused = await AcceptAsync(invitee, token);

        Assert.Equal(HttpStatusCode.Gone, refused.StatusCode);
    }

    [Fact]
    public async Task Revoking_needs_the_invite_permission()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        (_, Guid invitationId) = await CreateInvitationAsync(owner, tenantId, Unique());

        HttpClient viewer = await api.CreateRestrictedMemberAsync(tenantId, "members.view");

        using HttpResponseMessage refused = await viewer.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/invitations/{invitationId}/revoke",
            new { });

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    [Fact]
    public async Task A_lower_authority_actor_cannot_withdraw_a_more_powerful_invitation()
    {
        // Revoking takes nothing from anybody, which is why it looks harmless. It
        // is not: cancelling staffing decided by somebody more senior, and issuing
        // your own instead, substitutes your judgement for theirs.
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        Guid senior = await CreateRoleAsync(owner, tenantId, "Senior", AgentsEdit);
        (_, Guid invitationId) = await CreateInvitationAsync(owner, tenantId, Unique(), senior);

        HttpClient inviter = await api.CreateRestrictedMemberAsync(tenantId, FullInviter);

        using HttpResponseMessage refused = await inviter.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/invitations/{invitationId}/revoke",
            new { });

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    [Fact]
    public async Task An_invitation_from_another_workspace_is_answered_as_absence()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();
        (HttpClient theirs, Guid theirTenant, _) = await api.CreateOwnerAsync("Globex");

        (_, Guid theirInvitation) = await CreateInvitationAsync(theirs, theirTenant, Unique());

        using HttpResponseMessage refused = await owner.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/invitations/{theirInvitation}/revoke",
            new { });

        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
    }

    // ------------------------------ the authority may change before acceptance

    [Fact]
    public async Task An_invitation_does_not_outlive_the_inviter_s_membership()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        HttpClient inviter = await api.CreateRestrictedMemberAsync(tenantId, Inviter);
        Guid inviterId = await AiAgents.AiAgentApiFixture.CurrentUserIdAsync(inviter);

        string email = Unique();
        (string token, _) = await CreateInvitationAsync(inviter, tenantId, email);

        await api.RemoveMembershipAsync(tenantId, inviterId);

        (HttpClient invitee, _) = await api.CreateAccountAsync(email);

        using HttpResponseMessage refused = await AcceptAsync(invitee, token);

        Assert.Equal(HttpStatusCode.Gone, refused.StatusCode);
    }

    [Fact]
    public async Task An_invitation_does_not_outlive_the_permission_that_issued_it()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        HttpClient inviter = await api.CreateRestrictedMemberAsync(tenantId, Inviter);
        Guid inviterId = await AiAgents.AiAgentApiFixture.CurrentUserIdAsync(inviter);

        string email = Unique();
        (string token, _) = await CreateInvitationAsync(inviter, tenantId, email);

        await api.ReplacePermissionsAsync(tenantId, inviterId, "members.view");

        (HttpClient invitee, _) = await api.CreateAccountAsync(email);

        using HttpResponseMessage refused = await AcceptAsync(invitee, token);

        Assert.Equal(HttpStatusCode.Gone, refused.StatusCode);
    }

    [Fact]
    public async Task A_role_that_grew_beyond_the_inviter_s_ceiling_cannot_be_delivered()
    {
        // The mandatory scenario, and the subtle one: an administrator with a
        // narrow ceiling invites somebody to a modest role, an Owner later widens
        // that role, and accepting would deliver authority the inviter could never
        // have granted -- days after they asked, with no visible actor.
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        Guid support = await CreateRoleAsync(owner, tenantId, "Support", AgentsView);

        HttpClient inviter = await api.CreateRestrictedMemberAsync(tenantId, FullInviter);

        string email = Unique();
        (string token, _) = await CreateInvitationAsync(inviter, tenantId, email, support);

        // The Owner widens the role beyond what the inviter holds.
        await ReplaceRolePermissionsAsync(owner, tenantId, support, "ai-agents.view", "ai-agents.edit");

        (HttpClient invitee, _) = await api.CreateAccountAsync(email);

        using HttpResponseMessage refused = await AcceptAsync(invitee, token);

        Assert.Equal(HttpStatusCode.Gone, refused.StatusCode);

        // Nothing was created: no membership, no assignment.
        using HttpResponseMessage workspaces = await invitee.GetAsync("/api/me/tenants");

        workspaces.EnsureSuccessStatusCode();

        Assert.Empty(await workspaces.Content.ReadFromJsonAsync<object[]>() ?? []);
    }

    // ---------------------------------------------------------------- listing

    [Fact]
    public async Task Listing_invitations_needs_the_view_permission()
    {
        (_, Guid tenantId, _) = await api.CreateOwnerAsync();

        HttpClient limited = await api.CreateRestrictedMemberAsync(tenantId, "ai-agents.view");

        using HttpResponseMessage refused =
            await limited.GetAsync($"/api/tenants/{tenantId}/invitations");

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    [Fact]
    public async Task An_accepted_invitation_is_reported_as_accepted()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        string email = Unique();
        (string token, _) = await CreateInvitationAsync(owner, tenantId, email);

        (HttpClient invitee, _) = await api.CreateAccountAsync(email);

        using HttpResponseMessage accepted = await AcceptAsync(invitee, token);

        accepted.EnsureSuccessStatusCode();

        using HttpResponseMessage listed =
            await owner.GetAsync($"/api/tenants/{tenantId}/invitations");

        listed.EnsureSuccessStatusCode();

        using JsonDocument body = JsonDocument.Parse(await listed.Content.ReadAsStringAsync());

        Assert.Equal(
            "Accepted",
            body.RootElement.GetProperty("items")[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task Invitation_creation_requires_the_antiforgery_token()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        owner.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");

        using HttpResponseMessage refused = await InviteAsync(owner, tenantId, Unique());

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    // ------------------------------------------------------------- arranging

    private static string Unique() => $"invitee-{Guid.NewGuid():N}@example.test";

    private static Task<HttpResponseMessage> InviteAsync(
        HttpClient client,
        Guid tenantId,
        string email,
        params Guid[] roleIds) =>
        client.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/invitations",
            new { email, roleIds });

    private static Task<HttpResponseMessage> AcceptAsync(HttpClient client, string token) =>
        client.PostAsJsonAsync("/api/invitations/accept", new { token });

    /// <summary>Creates an invitation and returns its secret and its id.</summary>
    private static async Task<(string Token, Guid InvitationId)> CreateInvitationAsync(
        HttpClient client,
        Guid tenantId,
        string email,
        params Guid[] roleIds)
    {
        using HttpResponseMessage created = await InviteAsync(client, tenantId, email, roleIds);

        created.EnsureSuccessStatusCode();

        using JsonDocument body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());

        return (
            body.RootElement.GetProperty("token").GetString()!,
            body.RootElement.GetProperty("invitation").GetProperty("id").GetGuid());
    }

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

    private static async Task ReplaceRolePermissionsAsync(
        HttpClient client,
        Guid tenantId,
        Guid roleId,
        params string[] permissions)
    {
        using HttpResponseMessage read =
            await client.GetAsync($"/api/tenants/{tenantId}/roles/{roleId}");

        read.EnsureSuccessStatusCode();

        using HttpRequestMessage request = new(
            HttpMethod.Put,
            $"/api/tenants/{tenantId}/roles/{roleId}/permissions")
        {
            Content = JsonContent.Create(new { permissions }),
        };

        request.Headers.IfMatch.Add(
            new System.Net.Http.Headers.EntityTagHeaderValue(read.Headers.ETag!.Tag));

        using HttpResponseMessage saved = await client.SendAsync(request);

        saved.EnsureSuccessStatusCode();
    }
}
