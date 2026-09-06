using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace OmniAI.Api.IntegrationTests.Authorization;

/// <summary>
/// The conditional-request contract for roles.
/// </summary>
/// <remarks>
/// A permission editor is a screen somebody leaves open while they think about
/// who should be able to do what, so client think time is the normal case rather
/// than the edge one. Losing an update here means quietly restoring authority
/// that somebody deliberately removed.
/// </remarks>
public sealed class RoleConcurrencyApiTests(RoleApiFixture api) : IClassFixture<RoleApiFixture>
{
    /// <summary>A permission set these cases pass to the API.</summary>
    /// <remarks>
    /// A field rather than an inline array: the analyzer objects to a
    /// constant array allocated afresh on every call, and naming the set
    /// also says what the case is actually about.
    /// </remarks>
    private static readonly string[] RolesView = ["roles.view"];

    /// <summary>A permission set these cases pass to the API.</summary>
    /// <remarks>
    /// A field rather than an inline array: the analyzer objects to a
    /// constant array allocated afresh on every call, and naming the set
    /// also says what the case is actually about.
    /// </remarks>
    private static readonly string[] AiAgentsEdit = ["ai-agents.edit"];

    /// <summary>A permission set these cases pass to the API.</summary>
    /// <remarks>
    /// A field rather than an inline array: the analyzer objects to a
    /// constant array allocated afresh on every call, and naming the set
    /// also says what the case is actually about.
    /// </remarks>
    private static readonly string[] AiAgentsView = ["ai-agents.view"];

    [Fact]
    public async Task A_rename_without_if_match_is_refused_with_428()
    {
        (HttpClient owner, Guid tenantId, Guid roleId, _) = await ArrangeAsync();

        using HttpResponseMessage refused = await owner.PutAsJsonAsync(
            $"/api/tenants/{tenantId}/roles/{roleId}/name",
            new { name = "Support supervisor" });

        // Not 400 and not a silent unconditional write. The request is
        // well-formed; what is missing is the caller's statement of what they
        // believed they were editing.
        Assert.Equal(HttpStatusCode.PreconditionRequired, refused.StatusCode);
    }

    [Fact]
    public async Task A_permission_replacement_without_if_match_is_refused_with_428()
    {
        (HttpClient owner, Guid tenantId, Guid roleId, _) = await ArrangeAsync();

        using HttpResponseMessage refused = await owner.PutAsJsonAsync(
            $"/api/tenants/{tenantId}/roles/{roleId}/permissions",
            new { permissions = RolesView });

        Assert.Equal(HttpStatusCode.PreconditionRequired, refused.StatusCode);
    }

    [Fact]
    public async Task A_rename_with_the_current_version_succeeds_and_returns_a_new_etag()
    {
        (HttpClient owner, Guid tenantId, Guid roleId, string version) = await ArrangeAsync();

        using HttpResponseMessage renamed = await ConditionalPutAsync(
            owner,
            $"/api/tenants/{tenantId}/roles/{roleId}/name",
            new { name = "Support supervisor" },
            version);

        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.NotNull(renamed.Headers.ETag);
        Assert.NotEqual(version, renamed.Headers.ETag!.Tag);

        using JsonDocument body = JsonDocument.Parse(await renamed.Content.ReadAsStringAsync());

        Assert.Equal("Support supervisor", body.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public async Task A_stale_version_is_refused_with_412_and_changes_nothing()
    {
        (HttpClient owner, Guid tenantId, Guid roleId, string version) = await ArrangeAsync();

        // Somebody else edits it first.
        using HttpResponseMessage first = await ConditionalPutAsync(
            owner,
            $"/api/tenants/{tenantId}/roles/{roleId}/name",
            new { name = "Changed elsewhere" },
            version);

        first.EnsureSuccessStatusCode();

        using HttpResponseMessage stale = await ConditionalPutAsync(
            owner,
            $"/api/tenants/{tenantId}/roles/{roleId}/name",
            new { name = "From a stale screen" },
            version);

        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);

        // Nothing was retried and nothing was overwritten: the other writer's
        // change stands.
        using HttpResponseMessage read =
            await owner.GetAsync($"/api/tenants/{tenantId}/roles/{roleId}");

        using JsonDocument body = JsonDocument.Parse(await read.Content.ReadAsStringAsync());

        Assert.Equal("Changed elsewhere", body.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public async Task A_stale_permission_replacement_leaves_the_other_writers_set_in_place()
    {
        (HttpClient owner, Guid tenantId, Guid roleId, string version) = await ArrangeAsync();

        using HttpResponseMessage first = await ConditionalPutAsync(
            owner,
            $"/api/tenants/{tenantId}/roles/{roleId}/permissions",
            new { permissions = RolesView },
            version);

        first.EnsureSuccessStatusCode();

        using HttpResponseMessage stale = await ConditionalPutAsync(
            owner,
            $"/api/tenants/{tenantId}/roles/{roleId}/permissions",
            new { permissions = AiAgentsEdit },
            version);

        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);

        using HttpResponseMessage read =
            await owner.GetAsync($"/api/tenants/{tenantId}/roles/{roleId}");

        using JsonDocument body = JsonDocument.Parse(await read.Content.ReadAsStringAsync());

        Assert.Equal(
            ["roles.view"],
            body.RootElement.GetProperty("permissions")
                .EnumerateArray()
                .Select(entry => entry.GetString()));
    }

    [Fact]
    public async Task Renaming_the_owner_role_is_refused_with_409()
    {
        (HttpClient owner, Guid tenantId, _, _) = await ArrangeAsync();
        (Guid ownerRoleId, string ownerVersion) = await OwnerRoleAsync(owner, tenantId);

        using HttpResponseMessage refused = await ConditionalPutAsync(
            owner,
            $"/api/tenants/{tenantId}/roles/{ownerRoleId}/name",
            new { name = "Superuser" },
            ownerVersion);

        // 409, not 412 and not 500: nothing about the request is malformed and
        // the caller's version was current -- the operation simply will never be
        // allowed on this role.
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
    }

    [Fact]
    public async Task Replacing_the_owner_permissions_is_refused_with_409()
    {
        (HttpClient owner, Guid tenantId, _, _) = await ArrangeAsync();
        (Guid ownerRoleId, string ownerVersion) = await OwnerRoleAsync(owner, tenantId);

        using HttpResponseMessage refused = await ConditionalPutAsync(
            owner,
            $"/api/tenants/{tenantId}/roles/{ownerRoleId}/permissions",
            new { permissions = RolesView },
            ownerVersion);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
    }

    [Fact]
    public async Task A_stale_version_against_the_owner_role_is_reported_as_stale_first()
    {
        // The caller's precondition genuinely failed. Answering the protection
        // rule first would describe a role they have never seen -- and would
        // confirm, to somebody holding an out-of-date version, exactly which role
        // they had hold of.
        (HttpClient owner, Guid tenantId, _, _) = await ArrangeAsync();
        (Guid ownerRoleId, _) = await OwnerRoleAsync(owner, tenantId);

        using HttpResponseMessage refused = await ConditionalPutAsync(
            owner,
            $"/api/tenants/{tenantId}/roles/{ownerRoleId}/name",
            new { name = "Superuser" },
            "\"AAAAAA\"");

        Assert.Equal(HttpStatusCode.PreconditionFailed, refused.StatusCode);
    }

    [Fact]
    public async Task The_owner_role_is_unchanged_after_every_refused_attempt()
    {
        (HttpClient owner, Guid tenantId, _, _) = await ArrangeAsync();
        (Guid ownerRoleId, string ownerVersion) = await OwnerRoleAsync(owner, tenantId);

        using (HttpResponseMessage rename = await ConditionalPutAsync(
            owner,
            $"/api/tenants/{tenantId}/roles/{ownerRoleId}/name",
            new { name = "Superuser" },
            ownerVersion))
        {
            Assert.Equal(HttpStatusCode.Conflict, rename.StatusCode);
        }

        using HttpResponseMessage read =
            await owner.GetAsync($"/api/tenants/{tenantId}/roles/{ownerRoleId}");

        using JsonDocument body = JsonDocument.Parse(await read.Content.ReadAsStringAsync());

        Assert.Equal("Owner", body.RootElement.GetProperty("name").GetString());
        Assert.Equal(ownerVersion, read.Headers.ETag!.Tag);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("W/\"abc\"")]
    [InlineData("\"one\", \"two\"")]
    [InlineData("not-a-tag")]
    public async Task A_malformed_precondition_is_refused_as_a_bad_request(string ifMatch)
    {
        // A wildcard would permit an update based on any earlier version, which
        // is the lost update this precondition exists to prevent; a weak tag
        // asserts semantic equivalence rather than byte equality; a list cannot
        // express an exact-version precondition.
        (HttpClient owner, Guid tenantId, Guid roleId, _) = await ArrangeAsync();

        using HttpRequestMessage request = new(
            HttpMethod.Put,
            $"/api/tenants/{tenantId}/roles/{roleId}/name")
        {
            Content = JsonContent.Create(new { name = "Support supervisor" }),
        };

        request.Headers.TryAddWithoutValidation("If-Match", ifMatch);

        using HttpResponseMessage refused = await owner.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    [Fact]
    public async Task A_well_formed_tag_this_platform_never_issued_is_a_precondition_failure()
    {
        // Not a bad request: the header is syntactically valid and simply does not
        // match. 400 would say the client is broken; 412 says it is out of date,
        // which is the only one that describes an action that recovers.
        (HttpClient owner, Guid tenantId, Guid roleId, _) = await ArrangeAsync();

        using HttpResponseMessage refused = await ConditionalPutAsync(
            owner,
            $"/api/tenants/{tenantId}/roles/{roleId}/name",
            new { name = "Support supervisor" },
            "\"invented\"");

        Assert.Equal(HttpStatusCode.PreconditionFailed, refused.StatusCode);
    }

    [Fact]
    public async Task Two_administrators_editing_one_role_produce_exactly_one_winner()
    {
        // One person with the editor open in two tabs, which is the shape this
        // whole mechanism exists for.
        (HttpClient first, Guid tenantId, Guid roleId, string version) = await ArrangeAsync();
        HttpClient second = await api.CreateSecondClientForAsync(first);

        using HttpResponseMessage winner = await ConditionalPutAsync(
            first,
            $"/api/tenants/{tenantId}/roles/{roleId}/permissions",
            new { permissions = RolesView },
            version);

        using HttpResponseMessage loser = await ConditionalPutAsync(
            second,
            $"/api/tenants/{tenantId}/roles/{roleId}/permissions",
            new { permissions = AiAgentsEdit },
            version);

        Assert.Equal(HttpStatusCode.OK, winner.StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, loser.StatusCode);
    }

    /// <summary>An owner, a workspace and a custom role with its current version.</summary>
    private async Task<(HttpClient Owner, Guid TenantId, Guid RoleId, string Version)> ArrangeAsync()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        using HttpResponseMessage created = await owner.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/roles",
            new { name = "Support agent", permissions = AiAgentsView });

        created.EnsureSuccessStatusCode();

        using JsonDocument body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());

        return (
            owner,
            tenantId,
            body.RootElement.GetProperty("id").GetGuid(),
            created.Headers.ETag!.Tag);
    }

    /// <summary>The workspace's Owner role and the version it reads at.</summary>
    private static async Task<(Guid RoleId, string Version)> OwnerRoleAsync(
        HttpClient client,
        Guid tenantId)
    {
        using HttpResponseMessage roles = await client.GetAsync($"/api/tenants/{tenantId}/roles");

        roles.EnsureSuccessStatusCode();

        using JsonDocument body = JsonDocument.Parse(await roles.Content.ReadAsStringAsync());

        Guid roleId = body.RootElement.EnumerateArray()
            .First(entry => entry.GetProperty("isSystem").GetBoolean())
            .GetProperty("id")
            .GetGuid();

        using HttpResponseMessage detail =
            await client.GetAsync($"/api/tenants/{tenantId}/roles/{roleId}");

        detail.EnsureSuccessStatusCode();

        return (roleId, detail.Headers.ETag!.Tag);
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
