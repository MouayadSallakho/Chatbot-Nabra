using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace OmniAI.Api.IntegrationTests.Authorization;

/// <summary>
/// The delegation ceiling, over real HTTP against real PostgreSQL.
/// </summary>
/// <remarks>
/// <strong>An actor may only hand out authority they already hold.</strong> The
/// escalation this suite exists to prevent is subtle because it needs no
/// assignment change: a role can be assigned to the person editing it, so adding
/// a permission there grants it to them the instant it is saved.
/// <para>
/// Every case re-reads the actor's own effective permissions afterwards and
/// attempts the capability for real. A refusal that returned <c>403</c> while the
/// row had already changed would pass a status assertion and still be the defect.
/// </para>
/// </remarks>
public sealed class RoleDelegationApiTests(RoleApiFixture api) : IClassFixture<RoleApiFixture>
{
    /// <summary>
    /// A delegated role administrator: may see roles and change them, and holds
    /// no product capability at all.
    /// </summary>
    /// <remarks>
    /// <c>roles.view</c> is included because this platform has no implication
    /// between codes -- without it the actor could not read the role to obtain its
    /// version, and the attempt would be stopped by a missing precondition rather
    /// than by authority. Not knowing an identifier is not an access control, and
    /// a test that leaned on it would prove nothing about the ceiling.
    /// </remarks>
    private static readonly string[] RoleAdministrator = ["roles.view", "roles.manage"];

    /// <summary>Their effective permissions, in the catalogue's published order.</summary>
    private static readonly string[] RoleAdministratorEffective = ["roles.view", "roles.manage"];

    /// <summary>The same set as a role publishes it: sorted.</summary>
    private static readonly string[] RoleAdministratorGrants = ["roles.manage", "roles.view"];

    private static readonly string[] RolesManageAndAgentsEdit =
        ["roles.view", "roles.manage", "ai-agents.edit"];

    private static readonly string[] RolesManageAndAgentsLifecycle =
        ["roles.manage", "ai-agents.lifecycle"];

    private static readonly string[] AgentsViewAndPreview =
        ["ai-agents.view", "ai-agents.preview"];

    private static readonly string[] AgentsView = ["ai-agents.view"];

    private static readonly string[] AgentsEditAndLifecycle =
        ["ai-agents.edit", "ai-agents.lifecycle"];

    private static readonly string[] Invented = ["integrations.manage"];

    // ------------------------------------------- the self-assigned-role case

    [Fact]
    public async Task Bob_cannot_grant_himself_a_capability_through_the_role_he_holds()
    {
        (_, Guid tenantId, _) = await api.CreateOwnerAsync();

        (HttpClient bob, Guid roleId) = await api.CreateRestrictedMemberWithRoleAsync(
            tenantId,
            RoleAdministrator);

        Assert.Equal(RoleAdministratorEffective, await PermissionsOfAsync(bob, tenantId));

        // Reading his own role is within his authority, so he holds a current
        // version and the refusal below cannot be an accident of concurrency.
        using HttpResponseMessage read =
            await bob.GetAsync($"/api/tenants/{tenantId}/roles/{roleId}");

        read.EnsureSuccessStatusCode();

        using HttpResponseMessage widened = await ConditionalPutAsync(
            bob,
            $"/api/tenants/{tenantId}/roles/{roleId}/permissions",
            new { permissions = RolesManageAndAgentsEdit },
            read.Headers.ETag!.Tag);

        Assert.Equal(HttpStatusCode.Forbidden, widened.StatusCode);

        // The refusal names the capability he does not hold, not roles.manage --
        // which he does.
        using JsonDocument problem =
            JsonDocument.Parse(await widened.Content.ReadAsStringAsync());

        Assert.Contains(
            "ai-agents.edit",
            problem.RootElement.GetProperty("detail").GetString(),
            StringComparison.Ordinal);

        // And the state did not move: not his authority, and not the role.
        Assert.Equal(RoleAdministratorEffective, await PermissionsOfAsync(bob, tenantId));

        using HttpResponseMessage reread =
            await bob.GetAsync($"/api/tenants/{tenantId}/roles/{roleId}");

        reread.EnsureSuccessStatusCode();

        Assert.Equal(RoleAdministratorGrants, await PermissionsInBodyAsync(reread));
    }

    [Fact]
    public async Task Bob_still_cannot_edit_an_agent_after_the_refused_attempt()
    {
        // The proof that matters. A response can say 403 and still have changed
        // something; this asks the platform to actually let him act.
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        Guid agentId = await CreateAgentAsync(owner, tenantId);

        (HttpClient bob, Guid roleId) = await api.CreateRestrictedMemberWithRoleAsync(
            tenantId,
            RoleAdministrator);

        using HttpResponseMessage read =
            await bob.GetAsync($"/api/tenants/{tenantId}/roles/{roleId}");

        read.EnsureSuccessStatusCode();

        using HttpResponseMessage widened = await ConditionalPutAsync(
            bob,
            $"/api/tenants/{tenantId}/roles/{roleId}/permissions",
            new { permissions = RolesManageAndAgentsEdit },
            read.Headers.ETag!.Tag);

        Assert.Equal(HttpStatusCode.Forbidden, widened.StatusCode);

        using HttpResponseMessage rename = await ConditionalPutAsync(
            bob,
            $"/api/tenants/{tenantId}/ai-agents/{agentId}/name",
            new { name = "Seized" },
            "\"whatever\"");

        Assert.Equal(HttpStatusCode.Forbidden, rename.StatusCode);
    }

    // ------------------------------------------------------ create-then-hold

    [Fact]
    public async Task A_role_carrying_more_than_the_creator_holds_is_refused()
    {
        (_, Guid tenantId, _) = await api.CreateOwnerAsync();

        HttpClient bob = await api.CreateRestrictedMemberAsync(
            tenantId,
            "roles.view",
            "roles.manage");

        using HttpResponseMessage created = await bob.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/roles",
            new { name = "Escalation", permissions = RolesManageAndAgentsLifecycle });

        Assert.Equal(HttpStatusCode.Forbidden, created.StatusCode);

        // Nothing persisted -- neither the role nor a partial grant.
        using HttpResponseMessage list = await bob.GetAsync($"/api/tenants/{tenantId}/roles");

        list.EnsureSuccessStatusCode();

        Assert.DoesNotContain("Escalation", await list.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_unrecognised_code_is_a_bad_request_not_a_refusal()
    {
        // The distinction the contract turns on. An Owner asks for a capability
        // the platform does not have: that is malformed, and must not be reported
        // as authority they lack -- they lack nothing.
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        using HttpResponseMessage created = await owner.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/roles",
            new { name = "Invented", permissions = Invented });

        Assert.Equal(HttpStatusCode.BadRequest, created.StatusCode);
    }

    // ------------------------------------------------ higher-authority roles

    [Fact]
    public async Task A_role_that_outranks_the_actor_cannot_be_renamed_or_re_permissioned()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        // A role the Owner composes, carrying more than Bob will hold.
        using HttpResponseMessage senior = await owner.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/roles",
            new { name = "Senior", permissions = AgentsEditAndLifecycle });

        senior.EnsureSuccessStatusCode();

        using JsonDocument body = JsonDocument.Parse(await senior.Content.ReadAsStringAsync());
        Guid seniorId = body.RootElement.GetProperty("id").GetGuid();
        string version = senior.Headers.ETag!.Tag;

        HttpClient bob = await api.CreateRestrictedMemberAsync(
            tenantId,
            "roles.view",
            "roles.manage",
            "ai-agents.view");

        using HttpResponseMessage rename = await ConditionalPutAsync(
            bob,
            $"/api/tenants/{tenantId}/roles/{seniorId}/name",
            new { name = "Harmless" },
            version);

        Assert.Equal(HttpStatusCode.Forbidden, rename.StatusCode);

        // Even narrowing it is refused: being able to rewrite a role that
        // outranks you is how you take it over.
        using HttpResponseMessage narrowed = await ConditionalPutAsync(
            bob,
            $"/api/tenants/{tenantId}/roles/{seniorId}/permissions",
            new { permissions = AgentsView },
            version);

        Assert.Equal(HttpStatusCode.Forbidden, narrowed.StatusCode);

        using HttpResponseMessage reread =
            await bob.GetAsync($"/api/tenants/{tenantId}/roles/{seniorId}");

        reread.EnsureSuccessStatusCode();

        using JsonDocument current = JsonDocument.Parse(await reread.Content.ReadAsStringAsync());

        Assert.Equal("Senior", current.RootElement.GetProperty("name").GetString());
        Assert.Equal(AgentsEditAndLifecycle, await PermissionsInBodyAsync(reread));
    }

    // ------------------------------------------------------ within the ceiling

    [Fact]
    public async Task An_actor_may_delegate_exactly_what_they_hold()
    {
        (_, Guid tenantId, _) = await api.CreateOwnerAsync();

        HttpClient bob = await api.CreateRestrictedMemberAsync(
            tenantId,
            "roles.view",
            "roles.manage",
            "ai-agents.view",
            "ai-agents.preview");

        using HttpResponseMessage created = await bob.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/roles",
            new { name = "Support", permissions = AgentsViewAndPreview });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using JsonDocument body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        Guid roleId = body.RootElement.GetProperty("id").GetGuid();

        using HttpResponseMessage narrowed = await ConditionalPutAsync(
            bob,
            $"/api/tenants/{tenantId}/roles/{roleId}/permissions",
            new { permissions = AgentsView },
            created.Headers.ETag!.Tag);

        narrowed.EnsureSuccessStatusCode();

        Assert.Equal(AgentsView, await PermissionsInBodyAsync(narrowed));

        // Renaming a role within the ceiling still works too.
        using HttpResponseMessage renamed = await ConditionalPutAsync(
            bob,
            $"/api/tenants/{tenantId}/roles/{roleId}/name",
            new { name = "Support readers" },
            narrowed.Headers.ETag!.Tag);

        renamed.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task An_owner_may_still_compose_and_edit_anything()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        using HttpResponseMessage catalogue =
            await owner.GetAsync($"/api/tenants/{tenantId}/permissions");

        catalogue.EnsureSuccessStatusCode();

        using JsonDocument published =
            JsonDocument.Parse(await catalogue.Content.ReadAsStringAsync());

        string[] everything =
        [
            .. published.RootElement.EnumerateArray()
                .Select(definition => definition.GetProperty("code").GetString()!)
        ];

        using HttpResponseMessage created = await owner.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/roles",
            new { name = "Everything", permissions = everything });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using JsonDocument body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        Guid roleId = body.RootElement.GetProperty("id").GetGuid();

        using HttpResponseMessage edited = await ConditionalPutAsync(
            owner,
            $"/api/tenants/{tenantId}/roles/{roleId}/permissions",
            new { permissions = AgentsEditAndLifecycle },
            created.Headers.ETag!.Tag);

        edited.EnsureSuccessStatusCode();
    }

    // ------------------------------------------------------------ concurrency

    [Fact]
    public async Task The_precondition_is_still_answered_before_the_ceiling()
    {
        // A caller working from a version somebody else has replaced is told that
        // first. The permissions they would be judged against are not the ones
        // they were looking at, so the stale answer is the accurate one.
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        using HttpResponseMessage senior = await owner.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/roles",
            new { name = "Senior", permissions = AgentsEditAndLifecycle });

        senior.EnsureSuccessStatusCode();

        using JsonDocument body = JsonDocument.Parse(await senior.Content.ReadAsStringAsync());
        Guid seniorId = body.RootElement.GetProperty("id").GetGuid();
        string stale = senior.Headers.ETag!.Tag;

        // The Owner moves it on, so the tag Bob would present is out of date.
        using HttpResponseMessage moved = await ConditionalPutAsync(
            owner,
            $"/api/tenants/{tenantId}/roles/{seniorId}/name",
            new { name = "Senior staff" },
            stale);

        moved.EnsureSuccessStatusCode();

        HttpClient bob = await api.CreateRestrictedMemberAsync(
            tenantId,
            "roles.view",
            "roles.manage");

        using HttpResponseMessage refused = await ConditionalPutAsync(
            bob,
            $"/api/tenants/{tenantId}/roles/{seniorId}/permissions",
            new { permissions = AgentsView },
            stale);

        Assert.Equal(HttpStatusCode.PreconditionFailed, refused.StatusCode);
    }

    [Fact]
    public async Task A_missing_precondition_is_still_answered_before_the_ceiling()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        using HttpResponseMessage senior = await owner.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/roles",
            new { name = "Senior", permissions = AgentsEditAndLifecycle });

        senior.EnsureSuccessStatusCode();

        using JsonDocument body = JsonDocument.Parse(await senior.Content.ReadAsStringAsync());
        Guid seniorId = body.RootElement.GetProperty("id").GetGuid();

        HttpClient bob = await api.CreateRestrictedMemberAsync(
            tenantId,
            "roles.view",
            "roles.manage");

        using HttpResponseMessage refused = await bob.PutAsJsonAsync(
            $"/api/tenants/{tenantId}/roles/{seniorId}/permissions",
            new { permissions = AgentsView });

        Assert.Equal(HttpStatusCode.PreconditionRequired, refused.StatusCode);
    }

    [Fact]
    public async Task A_role_in_another_workspace_is_still_answered_as_absent()
    {
        // The ceiling must not become a way to learn that somebody else's role
        // exists. Cross-tenant stays 404, ahead of every permission question.
        (HttpClient theirs, Guid theirTenant, _) = await api.CreateOwnerAsync("Globex");

        using HttpResponseMessage senior = await theirs.PostAsJsonAsync(
            $"/api/tenants/{theirTenant}/roles",
            new { name = "Senior", permissions = AgentsEditAndLifecycle });

        senior.EnsureSuccessStatusCode();

        using JsonDocument body = JsonDocument.Parse(await senior.Content.ReadAsStringAsync());
        Guid seniorId = body.RootElement.GetProperty("id").GetGuid();

        (_, Guid myTenant, _) = await api.CreateOwnerAsync();

        HttpClient bob = await api.CreateRestrictedMemberAsync(
            myTenant,
            "roles.view",
            "roles.manage");

        using HttpResponseMessage refused = await ConditionalPutAsync(
            bob,
            $"/api/tenants/{theirTenant}/roles/{seniorId}/permissions",
            new { permissions = AgentsView },
            senior.Headers.ETag!.Tag);

        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
    }

    // ------------------------------------------------------------- arranging

    private static async Task<string[]> PermissionsOfAsync(HttpClient client, Guid tenantId)
    {
        using HttpResponseMessage response =
            await client.GetAsync($"/api/me/tenants/{tenantId}/permissions");

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<string[]>())!;
    }

    private static async Task<string[]> PermissionsInBodyAsync(HttpResponseMessage response)
    {
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return
        [
            .. body.RootElement.GetProperty("permissions")
                .EnumerateArray()
                .Select(code => code.GetString()!)
        ];
    }

    private static async Task<Guid> CreateAgentAsync(HttpClient owner, Guid tenantId)
    {
        using HttpResponseMessage created = await owner.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/ai-agents",
            new { name = "Sales Assistant", instructions = "Answer sales questions." });

        created.EnsureSuccessStatusCode();

        using JsonDocument body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());

        return body.RootElement.GetProperty("id").GetGuid();
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
