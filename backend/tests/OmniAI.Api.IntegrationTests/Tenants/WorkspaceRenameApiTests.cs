using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OmniAI.Api.IntegrationTests.WebChat;

namespace OmniAI.Api.IntegrationTests.Tenants;

/// <summary>
/// Phase 37.5a, row 20a (owner decision D-3): the owner renames the workspace
/// whose name visitors now see.
/// </summary>
public sealed class WorkspaceRenameApiTests(WebChatApiFixture fixture)
    : IClassFixture<WebChatApiFixture>
{
    private const string EmbedOrigin = "https://shop.example.com";

    [Fact]
    public async Task The_workspace_read_now_carries_an_entity_tag_and_an_unchanged_body()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync("Acme Trading");

        HttpResponseMessage read = await owner.GetAsync(Workspace(tenantId));

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.NotNull(read.Headers.ETag);
        Assert.False(read.Headers.ETag!.IsWeak);

        JsonElement body = await ReadAsync(read);
        Assert.Equal(
            ["id", "name", "slug", "status"],
            body.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_rename_answers_the_workspace_and_a_new_tag_and_is_audited_once()
    {
        (HttpClient owner, Guid tenantId, Guid ownerId) = await fixture.CreateOwnerAsync("Acme Trading");
        string tag = await TagAsync(owner, tenantId);

        HttpResponseMessage renamed = await RenameAsync(owner, tenantId, "  Acme Gulf  ", tag);

        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal("Acme Gulf", (await ReadAsync(renamed)).GetProperty("name").GetString());
        Assert.NotEqual(tag, renamed.Headers.ETag!.ToString());
        Assert.Equal(renamed.Headers.ETag!.ToString(), await TagAsync(owner, tenantId));

        await using Infrastructure.Persistence.OmniAiDbContext db = fixture.CreateDbContext();
        List<Guid?> actors = await db.Database
            .SqlQuery<Guid?>($"SELECT user_id AS \"Value\" FROM security_events WHERE kind = 'workspace.renamed' AND tenant_id = {tenantId}")
            .ToListAsync();

        Assert.Equal([ownerId], actors);
    }

    [Fact]
    public async Task Renaming_to_the_current_name_changes_nothing_and_records_nothing()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync("Same Name Co");
        string tag = await TagAsync(owner, tenantId);

        HttpResponseMessage renamed = await RenameAsync(owner, tenantId, "Same Name Co", tag);

        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal(tag, renamed.Headers.ETag!.ToString());
        Assert.Equal(0, await AuditCountAsync(tenantId));
    }

    [Fact]
    public async Task Without_If_Match_the_rename_is_refused_as_428()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        HttpResponseMessage refused = await RenameAsync(owner, tenantId, "New name", tag: null);

        Assert.Equal(HttpStatusCode.PreconditionRequired, refused.StatusCode);
        Assert.Equal("precondition.required", await CodeAsync(refused));
    }

    [Fact]
    public async Task A_stale_tag_is_refused_as_412_and_writes_neither_the_name_nor_the_audit()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync("First Name");
        string stale = await TagAsync(owner, tenantId);

        Assert.Equal(HttpStatusCode.OK, (await RenameAsync(owner, tenantId, "Second Name", stale)).StatusCode);

        HttpResponseMessage refused = await RenameAsync(owner, tenantId, "Third Name", stale);

        Assert.Equal(HttpStatusCode.PreconditionFailed, refused.StatusCode);
        Assert.Equal("precondition.failed", await CodeAsync(refused));
        Assert.Equal("Second Name", (await ReadAsync(await owner.GetAsync(Workspace(tenantId)))).GetProperty("name").GetString());
        Assert.Equal(1, await AuditCountAsync(tenantId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Tab\tinside")]
    public async Task A_name_the_creation_rules_refuse_is_400_with_its_code(string name)
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        string tag = await TagAsync(owner, tenantId);

        HttpResponseMessage refused = await RenameAsync(owner, tenantId, name, tag);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("workspace.name.invalid", await CodeAsync(refused));
    }

    [Fact]
    public async Task A_name_longer_than_200_characters_is_400_with_its_code()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        HttpResponseMessage refused = await RenameAsync(owner, tenantId, new string('n', 201), await TagAsync(owner, tenantId));

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("workspace.name.invalid", await CodeAsync(refused));
    }

    [Fact]
    public async Task A_member_without_workspace_manage_is_refused_with_403()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync("Kept Name");
        string tag = await TagAsync(owner, tenantId);

        HttpClient viewer = await fixture.CreateRestrictedMemberAsync(tenantId, "workspace.view");

        Assert.Equal(HttpStatusCode.Forbidden, (await RenameAsync(viewer, tenantId, "Taken over", tag)).StatusCode);
        Assert.Equal("Kept Name", (await ReadAsync(await owner.GetAsync(Workspace(tenantId)))).GetProperty("name").GetString());
    }

    [Fact]
    public async Task A_custom_role_granted_workspace_manage_may_rename()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        string tag = await TagAsync(owner, tenantId);

        HttpClient manager = await fixture.CreateRestrictedMemberAsync(tenantId, "workspace.manage");

        Assert.Equal(HttpStatusCode.OK, (await RenameAsync(manager, tenantId, "Managed Name", tag)).StatusCode);
    }

    [Fact]
    public async Task Another_workspace_is_told_it_does_not_exist_and_its_tag_buys_nothing()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync("Mine");
        string tag = await TagAsync(owner, tenantId);

        (HttpClient other, _, _) = await fixture.CreateOwnerAsync("Theirs");

        Assert.Equal(HttpStatusCode.NotFound, (await RenameAsync(other, tenantId, "Hijacked", tag)).StatusCode);
        Assert.Equal("Mine", (await ReadAsync(await owner.GetAsync(Workspace(tenantId)))).GetProperty("name").GetString());
    }

    [Fact]
    public async Task The_owner_holds_workspace_manage_without_any_data_step()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        HttpResponseMessage mine = await owner.GetAsync(WebChatApiFixture.Relative($"/api/me/tenants/{tenantId}/permissions"));

        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
        Assert.Contains("\"workspace.manage\"", await mine.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // And the role editor's catalogue offers it, in the Workspace group.
        HttpResponseMessage catalogue = await owner.GetAsync(WebChatApiFixture.Relative($"/api/tenants/{tenantId}/permissions"));
        JsonElement entry = (await ReadAsync(catalogue)).EnumerateArray()
            .Single(permission => permission.GetProperty("code").GetString() == "workspace.manage");

        Assert.Equal("Workspace", entry.GetProperty("group").GetString());
    }

    [Fact]
    public async Task Visitors_see_the_new_name_on_the_hosted_and_the_embedded_read_at_once()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync("Before Rename");
        (string publicKey, Guid endpointId) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Helper");
        await AllowAsync(owner, tenantId, endpointId, EmbedOrigin);

        HttpClient visitor = fixture.CreateClient();

        Assert.Equal("Before Rename", (await HostedAsync(visitor, publicKey)).GetProperty("workspaceName").GetString());
        Assert.Equal("Before Rename", (await EmbeddedAsync(visitor, publicKey)).GetProperty("workspaceName").GetString());

        Assert.Equal(HttpStatusCode.OK, (await RenameAsync(owner, tenantId, "After Rename", await TagAsync(owner, tenantId))).StatusCode);

        Assert.Equal("After Rename", (await HostedAsync(visitor, publicKey)).GetProperty("workspaceName").GetString());
        Assert.Equal("After Rename", (await EmbeddedAsync(visitor, publicKey)).GetProperty("workspaceName").GetString());
    }

    private static Uri Workspace(Guid tenantId) => WebChatApiFixture.Relative($"/api/tenants/{tenantId}");

    private static async Task<string> TagAsync(HttpClient client, Guid tenantId)
    {
        HttpResponseMessage read = await client.GetAsync(Workspace(tenantId));

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        return read.Headers.ETag!.ToString();
    }

    private static async Task<HttpResponseMessage> RenameAsync(HttpClient client, Guid tenantId, string name, string? tag)
    {
        HttpRequestMessage request = new(HttpMethod.Put, WebChatApiFixture.Relative($"/api/tenants/{tenantId}/name"))
        {
            Content = JsonContent.Create(new { name }),
        };

        if (tag is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", tag);
        }

        return await client.SendAsync(request);
    }

    private async Task<int> AuditCountAsync(Guid tenantId)
    {
        await using Infrastructure.Persistence.OmniAiDbContext db = fixture.CreateDbContext();

        return await db.Database
            .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM security_events WHERE kind = 'workspace.renamed' AND tenant_id = {tenantId}")
            .SingleAsync();
    }

    private static async Task AllowAsync(HttpClient owner, Guid tenantId, Guid endpointId, string origin)
    {
        Uri widget = WebChatApiFixture.Relative($"/api/tenants/{tenantId}/website-chat/{endpointId}/widget");
        HttpResponseMessage read = await owner.GetAsync(widget);

        HttpRequestMessage write = new(HttpMethod.Put, widget)
        {
            Content = JsonContent.Create(new
            {
                greeting = "Hello!",
                accentColor = "#2563eb",
                launcherPosition = "Right",
                theme = "System",
                allowedOrigins = new[] { origin },
            }),
        };
        write.Headers.TryAddWithoutValidation("If-Match", read.Headers.ETag!.ToString());

        Assert.Equal(HttpStatusCode.OK, (await owner.SendAsync(write)).StatusCode);
    }

    private static async Task<JsonElement> HostedAsync(HttpClient visitor, string publicKey)
    {
        HttpResponseMessage response = await visitor.GetAsync(WebChatApiFixture.Relative($"/api/public/webchat/{publicKey}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await ReadAsync(response);
    }

    private static async Task<JsonElement> EmbeddedAsync(HttpClient visitor, string publicKey)
    {
        HttpRequestMessage request = new(HttpMethod.Get, WebChatApiFixture.Relative($"/api/public/embed/{publicKey}"));
        request.Headers.TryAddWithoutValidation("X-OmniAI-Embed-Origin", EmbedOrigin);

        HttpResponseMessage response = await visitor.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await ReadAsync(response);
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        JsonElement body = await ReadAsync(response);

        return body.TryGetProperty("code", out JsonElement code) ? code.GetString() : null;
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.Clone();
    }
}
