using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OmniAI.Api.IntegrationTests.Analytics;
using OmniAI.Application.Ai;
using OmniAI.Testing;

namespace OmniAI.Api.IntegrationTests.Reports;

/// <summary>
/// A hosted application with report storage on a temporary disk and a provider
/// that records every call — so a test can prove reports made none.
/// </summary>
public sealed class ReportApiFixture : AnalyticsApiFixture
{
    public string StorageRoot { get; } = Path.Combine(Path.GetTempPath(), $"omniai-report-api-{Guid.NewGuid():N}");

    internal RecordingAiTextGenerator Generator { get; } = new();

    protected override IReadOnlyDictionary<string, string> HostSettings =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Reports:Storage:RootPath"] = StorageRoot,
            ["Reports:Workers:IdleInterval"] = "00:00:00.200",
        };

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);

        services.Replace(ServiceDescriptor.Singleton<IAiTextGenerator>(Generator));
    }
}

/// <summary>
/// The Reports HTTP surface: permissions, the generation flow, downloads, schedules.
/// </summary>
public sealed class ReportApiTests(ReportApiFixture fixture) : IClassFixture<ReportApiFixture>
{
    private const string View = "reports.view";
    private const string Generate = "reports.generate";
    private const string Schedule = "reports.schedule";
    private const string Analytics = "analytics.view";
    private const string Customers = "customers.view";

    private static readonly string[] Unsettled = ["Pending", "Running", "Completed"];

    // ------------------------------------------------------------ access

    [Fact]
    public async Task An_anonymous_caller_is_refused()
    {
        (_, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await fixture.CreateClient().GetAsync(Catalog(tenantId))).StatusCode);
    }

    [Fact]
    public async Task A_stranger_is_told_nothing_exists()
    {
        (_, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (HttpClient stranger, _, _) = await fixture.CreateOwnerAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(Catalog(tenantId))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(Executions(tenantId))).StatusCode);
    }

    [Fact]
    public async Task Without_reports_view_the_catalogue_and_the_list_are_refused()
    {
        (_, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        HttpClient member = await fixture.CreateRestrictedMemberAsync(tenantId, Analytics, Generate);

        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync(Catalog(tenantId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync(Executions(tenantId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync(Schedules(tenantId))).StatusCode);
    }

    [Fact]
    public async Task Reports_view_alone_lists_but_cannot_generate_or_schedule()
    {
        (_, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        HttpClient member = await fixture.CreateRestrictedMemberAsync(tenantId, View, Analytics);

        using JsonDocument catalog = await ReadJsonAsync(await member.GetAsync(Catalog(tenantId)));
        Assert.Equal(0, catalog.RootElement.GetProperty("reports").GetArrayLength());

        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync(Executions(tenantId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Request(member, tenantId, "ChannelUsage", "Csv")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await CreateSchedule(member, tenantId, "ChannelUsage")).StatusCode);
    }

    [Fact]
    public async Task Generating_needs_the_reports_own_data_permission()
    {
        (_, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        HttpClient member = await fixture.CreateRestrictedMemberAsync(tenantId, View, Generate, Customers);

        using JsonDocument catalog = await ReadJsonAsync(await member.GetAsync(Catalog(tenantId)));

        // Only the report whose data this member may read is offered.
        Assert.Equal(
            ["CustomerActivity"],
            catalog.RootElement.GetProperty("reports").EnumerateArray().Select(entry => entry.GetProperty("type").GetString()));

        Assert.Equal(HttpStatusCode.Forbidden, (await Request(member, tenantId, "ProviderCost", "Pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await Request(member, tenantId, "CustomerActivity", "Csv")).StatusCode);

        // Generating is allowed; scheduling still needs reports.schedule.
        Assert.Equal(HttpStatusCode.Forbidden, (await CreateSchedule(member, tenantId, "CustomerActivity")).StatusCode);
    }

    [Fact]
    public async Task Scheduling_needs_the_underlying_permission_too()
    {
        (_, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        HttpClient member = await fixture.CreateRestrictedMemberAsync(tenantId, View, Generate, Schedule, Analytics);

        Assert.Equal(HttpStatusCode.Created, (await CreateSchedule(member, tenantId, "ChannelUsage")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await CreateSchedule(member, tenantId, "CustomerActivity")).StatusCode);
    }

    // ---------------------------------------------------------- the flow

    [Fact]
    public async Task A_report_is_accepted_generated_and_downloaded_through_the_api()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        await fixture.SeedAsync(tenantId, "Support", customerMessages: 3, aiMessages: 2, text: "SECRET-TRANSCRIPT-TEXT");
        fixture.Generator.Reset();
        int embeddingsBefore = fixture.Embeddings.Calls;

        using HttpResponseMessage accepted = await Request(owner, tenantId, "ConversationActivity", "Csv");

        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.NotNull(accepted.Headers.Location);

        using JsonDocument queued = await ReadJsonAsync(accepted);
        Guid id = queued.RootElement.GetProperty("id").GetGuid();
        Assert.Contains(queued.RootElement.GetProperty("status").GetString(), Unsettled);

        JsonElement done = await WaitAsync(owner, tenantId, id);

        Assert.Equal("Completed", done.GetProperty("status").GetString());
        Assert.True(done.GetProperty("canDownload").GetBoolean());
        Assert.True(done.GetProperty("byteLength").GetInt64() > 0);

        using HttpResponseMessage download = await owner.GetAsync(Download(tenantId, id));

        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("text/csv", download.Content.Headers.ContentType!.MediaType);
        Assert.Equal("utf-8", download.Content.Headers.ContentType.CharSet);

        ContentDispositionHeaderValue disposition = download.Content.Headers.ContentDisposition!;
        Assert.Equal("attachment", disposition.DispositionType);
        Assert.Matches(@"^conversation-activity-\d{4}-\d{2}-\d{2}-to-\d{4}-\d{2}-\d{2}\.csv$", disposition.FileName!.Trim('"'));

        Assert.Contains("no-store", download.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
        Assert.Equal("nosniff", download.Headers.GetValues("X-Content-Type-Options").Single());

        byte[] bytes = await download.Content.ReadAsByteArrayAsync();
        Assert.Equal(done.GetProperty("byteLength").GetInt64(), bytes.LongLength);
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);

        string text = System.Text.Encoding.UTF8.GetString(bytes);
        Assert.Contains("Conversations started", text, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET-TRANSCRIPT-TEXT", text, StringComparison.Ordinal);

        // The stored hash is of exactly these bytes.
        await using (Infrastructure.Persistence.OmniAiDbContext context = fixture.CreateDbContext())
        {
            Domain.Reports.ReportArtifact artifact = context.ReportArtifacts.Single(
                candidate => candidate.ExecutionId == Domain.Reports.ReportExecutionId.From(id));

            Assert.Equal(artifact.Sha256, Convert.ToHexStringLower(SHA256.HashData(bytes)));

            // And no response anywhere mentions where the file lives.
            string json = done.GetRawText();
            Assert.DoesNotContain(artifact.StorageKey, json, StringComparison.Ordinal);
            Assert.DoesNotContain(fixture.StorageRoot, json, StringComparison.Ordinal);
            Assert.DoesNotContain("storageKey", json, StringComparison.OrdinalIgnoreCase);
        }

        // Generating spent nothing.
        Assert.Equal(0, fixture.Generator.Calls);
        Assert.Equal(embeddingsBefore, fixture.Embeddings.Calls);
    }

    [Fact]
    public async Task A_repeated_request_is_one_execution_and_a_reused_key_for_another_report_is_refused()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        Guid key = Guid.NewGuid();

        using JsonDocument first = await ReadJsonAsync(await Request(owner, tenantId, "ChannelUsage", "Csv", key));
        using JsonDocument second = await ReadJsonAsync(await Request(owner, tenantId, "ChannelUsage", "Csv", key));

        Assert.Equal(first.RootElement.GetProperty("id").GetGuid(), second.RootElement.GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.Conflict, (await Request(owner, tenantId, "ChannelUsage", "Pdf", key)).StatusCode);

        using JsonDocument page = await ReadJsonAsync(await owner.GetAsync(Executions(tenantId)));
        Assert.Equal(1, page.RootElement.GetProperty("totalCount").GetInt32());
    }

    [Theory]
    [InlineData("{\"reportType\":\"ChannelUsage\",\"format\":\"Csv\",\"range\":\"30d\"}")]
    [InlineData("{\"reportType\":\"Everything\",\"format\":\"Csv\",\"range\":\"30d\",\"clientRequestId\":\"8a3b8f0e-7f5e-4a58-9a5e-1f4c1d7b0a11\"}")]
    [InlineData("{\"reportType\":\"4\",\"format\":\"Csv\",\"range\":\"30d\",\"clientRequestId\":\"8a3b8f0e-7f5e-4a58-9a5e-1f4c1d7b0a12\"}")]
    [InlineData("{\"reportType\":\"ChannelUsage\",\"format\":\"Docx\",\"range\":\"30d\",\"clientRequestId\":\"8a3b8f0e-7f5e-4a58-9a5e-1f4c1d7b0a13\"}")]
    [InlineData("{\"reportType\":\"ChannelUsage\",\"format\":\"Csv\",\"range\":\"all\",\"clientRequestId\":\"8a3b8f0e-7f5e-4a58-9a5e-1f4c1d7b0a14\"}")]
    [InlineData("{\"reportType\":\"ChannelUsage\",\"format\":\"Csv\",\"range\":\"custom\",\"from\":\"2024-01-01\",\"to\":\"2025-06-01\",\"clientRequestId\":\"8a3b8f0e-7f5e-4a58-9a5e-1f4c1d7b0a15\"}")]
    [InlineData("{\"reportType\":\"ChannelUsage\",\"format\":\"Csv\",\"range\":\"custom\",\"from\":\"2026-09-10\",\"to\":\"2026-09-10\",\"clientRequestId\":\"8a3b8f0e-7f5e-4a58-9a5e-1f4c1d7b0a16\"}")]
    [InlineData("{\"reportType\":\"ChannelUsage\",\"format\":\"Csv\",\"range\":\"custom\",\"from\":\"2026-09-10\",\"to\":\"2099-01-01\",\"clientRequestId\":\"8a3b8f0e-7f5e-4a58-9a5e-1f4c1d7b0a17\"}")]
    public async Task A_malformed_or_unbounded_request_is_refused(string body)
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        using StringContent content = new(body, System.Text.Encoding.UTF8, "application/json");

        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsync(Reports(tenantId), content)).StatusCode);
    }

    [Fact]
    public async Task Another_workspace_cannot_see_or_download_a_report_even_with_its_id()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (HttpClient rival, Guid rivalTenant, _) = await fixture.CreateOwnerAsync("Rival Trading");

        using JsonDocument queued = await ReadJsonAsync(await Request(owner, tenantId, "ChannelUsage", "Csv"));
        Guid id = queued.RootElement.GetProperty("id").GetGuid();
        await WaitAsync(owner, tenantId, id);

        Assert.Equal(HttpStatusCode.NotFound, (await rival.GetAsync(Download(tenantId, id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await rival.GetAsync(Download(rivalTenant, id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await rival.GetAsync(Execution(rivalTenant, id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(Download(tenantId, Guid.NewGuid()))).StatusCode);
    }

    [Fact]
    public async Task A_download_re_checks_the_data_permission_at_download_time()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        HttpClient viewer = await fixture.CreateRestrictedMemberAsync(tenantId, View);

        using JsonDocument queued = await ReadJsonAsync(await Request(owner, tenantId, "CustomerActivity", "Xlsx"));
        Guid id = queued.RootElement.GetProperty("id").GetGuid();
        await WaitAsync(owner, tenantId, id);

        // Listed, honestly marked as not downloadable, and refused if tried.
        using JsonDocument seen = await ReadJsonAsync(await viewer.GetAsync(Execution(tenantId, id)));
        Assert.False(seen.RootElement.GetProperty("canDownload").GetBoolean());
        Assert.False(seen.RootElement.GetProperty("requestedByMe").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync(Download(tenantId, id))).StatusCode);

        using HttpResponseMessage allowed = await owner.GetAsync(Download(tenantId, id));
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            allowed.Content.Headers.ContentType!.MediaType);

        byte[] bytes = await allowed.Content.ReadAsByteArrayAsync();
        Assert.Equal((byte)'P', bytes[0]);
        Assert.Equal((byte)'K', bytes[1]);
    }

    // --------------------------------------------------------- schedules

    [Fact]
    public async Task A_schedule_is_created_versioned_and_guarded_against_lost_updates()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        using HttpResponseMessage created = await CreateSchedule(owner, tenantId, "ChannelUsage", "Monthly");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        string etag = created.Headers.ETag!.Tag;
        using JsonDocument body = await ReadJsonAsync(created);
        Guid id = body.RootElement.GetProperty("id").GetGuid();

        Assert.Equal("Monthly", body.RootElement.GetProperty("frequency").GetString());
        Assert.Equal("PreviousMonth", body.RootElement.GetProperty("rangePreset").GetString());
        Assert.False(body.RootElement.TryGetProperty("recipients", out _));
        Assert.True(body.RootElement.GetProperty("nextRunAt").GetDateTimeOffset() > DateTimeOffset.UtcNow);

        object edit = new { name = "Channels, monthly", reportType = "ChannelUsage", format = "Pdf", frequency = "Weekly", enabled = true };

        // No precondition: refused before anything is changed.
        Assert.Equal((HttpStatusCode)428, (await owner.PutAsJsonAsync(ScheduleUrl(tenantId, id), edit)).StatusCode);

        using HttpResponseMessage saved = await Put(owner, ScheduleUrl(tenantId, id), edit, etag);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        string fresh = saved.Headers.ETag!.Tag;
        Assert.NotEqual(etag, fresh);

        // The tab that still holds the first version loses, and says so.
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await Put(owner, ScheduleUrl(tenantId, id), edit, etag)).StatusCode);

        using HttpResponseMessage off = await Put(owner, ScheduleUrl(tenantId, id) + "/enabled", new { enabled = false }, fresh);
        Assert.Equal(HttpStatusCode.OK, off.StatusCode);

        using JsonDocument disabled = await ReadJsonAsync(off);
        Assert.False(disabled.RootElement.GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task A_viewer_sees_schedules_but_cannot_change_them()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        HttpClient viewer = await fixture.CreateRestrictedMemberAsync(tenantId, View, Analytics);

        using HttpResponseMessage created = await CreateSchedule(owner, tenantId, "WhatsAppDelivery");
        string etag = created.Headers.ETag!.Tag;
        Guid id = (await ReadJsonAsync(created)).RootElement.GetProperty("id").GetGuid();

        using JsonDocument list = await ReadJsonAsync(await viewer.GetAsync(Schedules(tenantId)));
        Assert.Equal(1, list.RootElement.GetProperty("items").GetArrayLength());
        Assert.False(list.RootElement.GetProperty("items")[0].GetProperty("canEdit").GetBoolean());

        Assert.Equal(HttpStatusCode.Forbidden, (await Put(viewer, ScheduleUrl(tenantId, id) + "/enabled", new { enabled = false }, etag)).StatusCode);
    }

    // ----------------------------------------------------------- helpers

    private static string Reports(Guid tenantId) => $"/api/tenants/{tenantId}/reports";

    private static string Catalog(Guid tenantId) => $"{Reports(tenantId)}/catalog";

    private static string Executions(Guid tenantId) => $"{Reports(tenantId)}/executions";

    private static string Execution(Guid tenantId, Guid id) => $"{Executions(tenantId)}/{id}";

    private static string Download(Guid tenantId, Guid id) => $"{Execution(tenantId, id)}/download";

    private static string Schedules(Guid tenantId) => $"{Reports(tenantId)}/schedules";

    private static string ScheduleUrl(Guid tenantId, Guid id) => $"{Schedules(tenantId)}/{id}";

    private static Task<HttpResponseMessage> Request(
        HttpClient client, Guid tenantId, string type, string format, Guid? key = null) =>
        client.PostAsJsonAsync(
            Reports(tenantId),
            new { reportType = type, format, range = "30d", clientRequestId = key ?? Guid.NewGuid() });

    private static Task<HttpResponseMessage> CreateSchedule(
        HttpClient client, Guid tenantId, string type, string frequency = "Weekly") =>
        client.PostAsJsonAsync(
            Schedules(tenantId),
            new { name = $"{type} {frequency}", reportType = type, format = "Csv", frequency, enabled = true });

    private static async Task<HttpResponseMessage> Put(HttpClient client, string url, object body, string etag)
    {
        using HttpRequestMessage request = new(HttpMethod.Put, url) { Content = JsonContent.Create(body) };
        request.Headers.TryAddWithoutValidation("If-Match", etag);

        return await client.SendAsync(request);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        string text = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {text}");

        return JsonDocument.Parse(text);
    }

    /// <summary>Polls one execution until the worker settles it. Bounded.</summary>
    private static async Task<JsonElement> WaitAsync(HttpClient client, Guid tenantId, Guid id)
    {
        for (int attempt = 0; attempt < 150; attempt++)
        {
            using JsonDocument current = await ReadJsonAsync(await client.GetAsync(Execution(tenantId, id)));

            string? status = current.RootElement.GetProperty("status").GetString();

            if (status is "Completed" or "Failed")
            {
                return current.RootElement.Clone();
            }

            await Task.Delay(200);
        }

        throw new TimeoutException("The report worker never settled the execution.");
    }
}
