using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OmniAI.Api.IntegrationTests.Reports;
using OmniAI.Api.IntegrationTests.WebChat;

namespace OmniAI.Api.IntegrationTests.Tenants;

/// <summary>
/// Phase 38.7b, BA-9: the workspace read tells whoever may change the zone how many
/// report schedules a change would move, and tells nobody else anything.
/// </summary>
public sealed class WorkspaceScheduleCountApiTests(ReportApiFixture fixture)
    : IClassFixture<ReportApiFixture>
{
    [Fact]
    public async Task A_manager_without_reports_view_gets_the_count()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        await ScheduleAsync(owner, tenantId, "Weekly");
        await ScheduleAsync(owner, tenantId, "Monthly");

        HttpClient manager = await fixture.CreateRestrictedMemberAsync(tenantId, "workspace.view", "workspace.manage");

        // The schedules themselves are closed to this manager...
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await manager.GetAsync(Url($"/api/tenants/{tenantId}/reports/schedules"))).StatusCode);

        // ...and the count is not.
        JsonElement workspace = await ReadAsync(manager, tenantId);
        Assert.Equal(2, workspace.GetProperty("scheduleCount").GetInt32());

        Assert.Equal(2, (await ReadAsync(owner, tenantId)).GetProperty("scheduleCount").GetInt32());
    }

    [Fact]
    public async Task A_manager_of_a_workspace_without_schedules_reads_zero()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        Assert.Equal(0, (await ReadAsync(owner, tenantId)).GetProperty("scheduleCount").GetInt32());
    }

    [Fact]
    public async Task A_member_without_workspace_manage_learns_nothing()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        await ScheduleAsync(owner, tenantId, "Weekly");

        // Even one who may read the schedules: the count belongs to the zone change.
        HttpClient viewer = await fixture.CreateRestrictedMemberAsync(tenantId, "workspace.view", "reports.view");

        JsonElement workspace = await ReadAsync(viewer, tenantId);

        Assert.False(workspace.TryGetProperty("scheduleCount", out _));
    }

    [Fact]
    public async Task No_count_crosses_workspaces()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        await ScheduleAsync(owner, tenantId, "Weekly");

        (HttpClient rival, Guid rivalTenantId, _) = await fixture.CreateOwnerAsync("Rival Trading");

        Assert.Equal(HttpStatusCode.NotFound, (await rival.GetAsync(Url($"/api/tenants/{tenantId}"))).StatusCode);
        Assert.Equal(0, (await ReadAsync(rival, rivalTenantId)).GetProperty("scheduleCount").GetInt32());
    }

    [Fact]
    public async Task The_etag_still_names_the_workspace_and_writes_carry_no_count()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        HttpResponseMessage before = await owner.GetAsync(Url($"/api/tenants/{tenantId}"));
        await ScheduleAsync(owner, tenantId, "Weekly");
        HttpResponseMessage after = await owner.GetAsync(Url($"/api/tenants/{tenantId}"));

        // A schedule is not a change to the workspace: the version a rename or a
        // zone change must match is the same, while the count moved.
        Assert.Equal(before.Headers.ETag, after.Headers.ETag);

        using HttpRequestMessage rename = new(HttpMethod.Put, Url($"/api/tenants/{tenantId}/name"))
        {
            Content = JsonContent.Create(new { name = "Renamed" }),
        };
        rename.Headers.TryAddWithoutValidation("If-Match", after.Headers.ETag!.ToString());

        HttpResponseMessage renamed = await owner.SendAsync(rename);

        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);

        using JsonDocument body = JsonDocument.Parse(await renamed.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.TryGetProperty("scheduleCount", out _));
    }

    private static async Task ScheduleAsync(HttpClient client, Guid tenantId, string frequency)
    {
        HttpResponseMessage created = await client.PostAsJsonAsync(
            Url($"/api/tenants/{tenantId}/reports/schedules"),
            new { name = $"Channels {frequency} {Guid.NewGuid():N}"[..30], reportType = "ChannelUsage", format = "Csv", frequency, enabled = true });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
    }

    private static async Task<JsonElement> ReadAsync(HttpClient client, Guid tenantId)
    {
        HttpResponseMessage read = await client.GetAsync(Url($"/api/tenants/{tenantId}"));

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        using JsonDocument body = JsonDocument.Parse(await read.Content.ReadAsStringAsync());

        return body.RootElement.Clone();
    }

    private static Uri Url(string path) => WebChatApiFixture.Relative(path);
}
