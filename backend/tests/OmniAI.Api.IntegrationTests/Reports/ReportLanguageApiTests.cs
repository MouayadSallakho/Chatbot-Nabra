using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace OmniAI.Api.IntegrationTests.Reports;

/// <summary>
/// Which language a report file is written in, over HTTP (Phase 37.7, D-6).
/// </summary>
/// <remarks>
/// The contract is additive: an optional <c>language</c> on the report request and
/// on a schedule's create and edit bodies, and a read-only <c>language</c> on every
/// execution and schedule. A request that names none behaves as before 37.7 for an
/// account that has not chosen a language: English.
/// </remarks>
public sealed class ReportLanguageApiTests(ReportApiFixture fixture) : IClassFixture<ReportApiFixture>
{
    private const string View = "reports.view";
    private const string Generate = "reports.generate";
    private const string Analytics = "analytics.view";

    // ---------------------------------------------------------- executions

    [Fact]
    public async Task Without_a_language_a_report_is_in_the_requester_s_language_else_english()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        // An account that never chose a language: English, as before 37.7.
        using JsonDocument unset = await ReadJsonAsync(await Request(owner, tenantId));
        Assert.Equal("en", unset.RootElement.GetProperty("language").GetString());

        await ChooseLanguageAsync(owner, "ar");

        using JsonDocument chosen = await ReadJsonAsync(await Request(owner, tenantId));
        Assert.Equal("ar", chosen.RootElement.GetProperty("language").GetString());
    }

    [Fact]
    public async Task The_request_s_language_overrides_the_requester_s()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        await ChooseLanguageAsync(owner, "ar");

        using JsonDocument english = await ReadJsonAsync(await Request(owner, tenantId, language: "en"));
        Assert.Equal("en", english.RootElement.GetProperty("language").GetString());

        await ChooseLanguageAsync(owner, "en");

        using JsonDocument arabic = await ReadJsonAsync(await Request(owner, tenantId, language: "ar"));
        Assert.Equal("ar", arabic.RootElement.GetProperty("language").GetString());
    }

    [Fact]
    public async Task An_arabic_report_is_generated_and_downloaded_in_arabic_and_lists_say_so()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        await fixture.SeedAsync(tenantId, "Support", customerMessages: 2, aiMessages: 1, text: "hello");

        using JsonDocument queued = await ReadJsonAsync(await Request(owner, tenantId, language: "ar"));
        Guid id = queued.RootElement.GetProperty("id").GetGuid();

        JsonElement done = await WaitAsync(owner, tenantId, id);
        Assert.Equal("Completed", done.GetProperty("status").GetString());
        Assert.Equal("ar", done.GetProperty("language").GetString());

        using HttpResponseMessage download = await owner.GetAsync($"{Executions(tenantId)}/{id}/download");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);

        byte[] bytes = await download.Content.ReadAsByteArrayAsync();
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);

        string header = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3).Split("\r\n")[0];
        Assert.Equal("القناة,المحادثات التي بدأت,رسائل العملاء,رسائل الذكاء الاصطناعي,رسائل الفريق", header);

        // The download's name is the same server-built ASCII name in every language.
        Assert.Matches(@"^channel-usage-\d{4}-\d{2}-\d{2}-to-\d{4}-\d{2}-\d{2}\.csv$", download.Content.Headers.ContentDisposition!.FileName!.Trim('"'));

        using JsonDocument page = await ReadJsonAsync(await owner.GetAsync(Executions(tenantId)));
        Assert.All(page.RootElement.GetProperty("items").EnumerateArray(), item => Assert.Equal("ar", item.GetProperty("language").GetString()));
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("AR")]
    [InlineData("ar-SA")]
    [InlineData("")]
    [InlineData("english")]
    public async Task An_unsupported_language_is_refused_with_its_code(string language)
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        using HttpResponseMessage refused = await Request(owner, tenantId, language: language);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("language.unsupported", await CodeOf(refused));

        using JsonDocument page = await ReadJsonAsync(await owner.GetAsync(Executions(tenantId)));
        Assert.Equal(0, page.RootElement.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task A_repeat_keeps_its_language_and_a_different_one_is_a_conflict()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        Guid key = Guid.NewGuid();

        using JsonDocument first = await ReadJsonAsync(await Request(owner, tenantId, key, "ar"));
        using JsonDocument same = await ReadJsonAsync(await Request(owner, tenantId, key, "ar"));

        // A repeat that names no language asks for the report already settled, even
        // though the requester's own language is English.
        using JsonDocument unnamed = await ReadJsonAsync(await Request(owner, tenantId, key));

        Guid id = first.RootElement.GetProperty("id").GetGuid();
        Assert.Equal(id, same.RootElement.GetProperty("id").GetGuid());
        Assert.Equal(id, unnamed.RootElement.GetProperty("id").GetGuid());
        Assert.Equal("ar", unnamed.RootElement.GetProperty("language").GetString());

        Assert.Equal(HttpStatusCode.Conflict, (await Request(owner, tenantId, key, "en")).StatusCode);
    }

    [Fact]
    public async Task The_language_changes_no_permission_and_no_workspace_boundary()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        HttpClient viewer = await fixture.CreateRestrictedMemberAsync(tenantId, View, Analytics);
        HttpClient generator = await fixture.CreateRestrictedMemberAsync(tenantId, Generate, Analytics);
        (HttpClient stranger, _, _) = await fixture.CreateOwnerAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await Request(viewer, tenantId, language: "ar")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Request(stranger, tenantId, language: "ar")).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await Request(generator, tenantId, language: "ar")).StatusCode);

        // Checked before membership: the refusal of a bad value says nothing about
        // whether the workspace exists.
        using HttpResponseMessage strangerBadValue = await Request(stranger, tenantId, language: "xx");
        Assert.Equal(HttpStatusCode.BadRequest, strangerBadValue.StatusCode);
        Assert.Equal("language.unsupported", await CodeOf(strangerBadValue));

        using JsonDocument mine = await ReadJsonAsync(await owner.PostAsJsonAsync(
            Reports(tenantId), new { reportType = "ChannelUsage", format = "Csv", range = "7d", clientRequestId = Guid.NewGuid(), language = "ar" }));
        Guid id = mine.RootElement.GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"{Executions(tenantId)}/{id}")).StatusCode);
    }

    // ----------------------------------------------------------- schedules

    [Fact]
    public async Task A_schedule_takes_its_creator_s_language_and_keeps_it_when_they_change_theirs()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        using HttpResponseMessage english = await CreateSchedule(owner, tenantId);
        Assert.Equal("en", (await ReadJsonAsync(english)).RootElement.GetProperty("language").GetString());

        await ChooseLanguageAsync(owner, "ar");

        using HttpResponseMessage created = await CreateSchedule(owner, tenantId);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Guid id = (await ReadJsonAsync(created)).RootElement.GetProperty("id").GetGuid();

        await ChooseLanguageAsync(owner, "en");

        using JsonDocument read = await ReadJsonAsync(await owner.GetAsync($"{Schedules(tenantId)}/{id}"));
        Assert.Equal("ar", read.RootElement.GetProperty("language").GetString());

        using JsonDocument list = await ReadJsonAsync(await owner.GetAsync(Schedules(tenantId)));
        Assert.Equal(["en", "ar"], list.RootElement.GetProperty("items").EnumerateArray()
            .OrderBy(item => item.GetProperty("createdAt").GetDateTimeOffset())
            .Select(item => item.GetProperty("language").GetString()));
    }

    [Fact]
    public async Task A_schedule_s_language_is_chosen_on_create_and_changed_only_by_an_edit_that_names_one()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        using HttpResponseMessage created = await CreateSchedule(owner, tenantId, "ar");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        string etag = created.Headers.ETag!.Tag;
        Guid id = (await ReadJsonAsync(created)).RootElement.GetProperty("id").GetGuid();
        string url = $"{Schedules(tenantId)}/{id}";

        // Today's edit, without a language: the language stays.
        using HttpResponseMessage kept = await Put(owner, url, new { name = "Renamed", reportType = "ChannelUsage", format = "Pdf", frequency = "Weekly", enabled = true }, etag);
        Assert.Equal(HttpStatusCode.OK, kept.StatusCode);
        Assert.Equal("ar", (await ReadJsonAsync(kept)).RootElement.GetProperty("language").GetString());
        etag = kept.Headers.ETag!.Tag;

        // Switching it off and on is not an edit of its language.
        using HttpResponseMessage off = await Put(owner, url + "/enabled", new { enabled = false }, etag);
        Assert.Equal("ar", (await ReadJsonAsync(off)).RootElement.GetProperty("language").GetString());
        etag = off.Headers.ETag!.Tag;

        // An unsupported value is refused, and changes nothing.
        using HttpResponseMessage refused = await Put(owner, url, new { name = "Renamed", reportType = "ChannelUsage", format = "Pdf", frequency = "Weekly", enabled = true, language = "de" }, etag);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("language.unsupported", await CodeOf(refused));

        // An edit that names one changes it, under the same If-Match rules as before.
        using HttpResponseMessage changed = await Put(owner, url, new { name = "Renamed", reportType = "ChannelUsage", format = "Pdf", frequency = "Weekly", enabled = true, language = "en" }, etag);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal("en", (await ReadJsonAsync(changed)).RootElement.GetProperty("language").GetString());

        Assert.Equal(HttpStatusCode.PreconditionFailed, (await Put(owner, url, new { name = "Stale", reportType = "ChannelUsage", format = "Pdf", frequency = "Weekly", enabled = true, language = "ar" }, etag)).StatusCode);

        using HttpResponseMessage badCreate = await CreateSchedule(owner, tenantId, "ar-SA");
        Assert.Equal(HttpStatusCode.BadRequest, badCreate.StatusCode);
        Assert.Equal("language.unsupported", await CodeOf(badCreate));
    }

    [Fact]
    public async Task A_viewer_reads_a_schedule_s_language_but_cannot_change_it()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        HttpClient viewer = await fixture.CreateRestrictedMemberAsync(tenantId, View, Analytics);
        (HttpClient stranger, _, _) = await fixture.CreateOwnerAsync();

        using HttpResponseMessage created = await CreateSchedule(owner, tenantId, "ar");
        string etag = created.Headers.ETag!.Tag;
        Guid id = (await ReadJsonAsync(created)).RootElement.GetProperty("id").GetGuid();

        using JsonDocument seen = await ReadJsonAsync(await viewer.GetAsync($"{Schedules(tenantId)}/{id}"));
        Assert.Equal("ar", seen.RootElement.GetProperty("language").GetString());

        object edit = new { name = "Mine now", reportType = "ChannelUsage", format = "Csv", frequency = "Weekly", enabled = true, language = "en" };
        Assert.Equal(HttpStatusCode.Forbidden, (await Put(viewer, $"{Schedules(tenantId)}/{id}", edit, etag)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"{Schedules(tenantId)}/{id}")).StatusCode);
    }

    // ------------------------------------------------------------- helpers

    private static string Reports(Guid tenantId) => $"/api/tenants/{tenantId}/reports";

    private static string Executions(Guid tenantId) => $"{Reports(tenantId)}/executions";

    private static string Schedules(Guid tenantId) => $"{Reports(tenantId)}/schedules";

    private static Task<HttpResponseMessage> Request(HttpClient client, Guid tenantId, Guid? key = null, string? language = null) =>
        language is null
            ? client.PostAsJsonAsync(Reports(tenantId), new { reportType = "ChannelUsage", format = "Csv", range = "7d", clientRequestId = key ?? Guid.NewGuid() })
            : client.PostAsJsonAsync(Reports(tenantId), new { reportType = "ChannelUsage", format = "Csv", range = "7d", clientRequestId = key ?? Guid.NewGuid(), language });

    private static Task<HttpResponseMessage> CreateSchedule(HttpClient client, Guid tenantId, string? language = null) =>
        language is null
            ? client.PostAsJsonAsync(Schedules(tenantId), new { name = "Channels weekly", reportType = "ChannelUsage", format = "Csv", frequency = "Weekly", enabled = true })
            : client.PostAsJsonAsync(Schedules(tenantId), new { name = "Channels weekly", reportType = "ChannelUsage", format = "Csv", frequency = "Weekly", enabled = true, language });

    private static async Task ChooseLanguageAsync(HttpClient client, string language)
    {
        using HttpResponseMessage response = await client.PutAsJsonAsync("/api/me/language", new { language });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> Put(HttpClient client, string url, object body, string etag)
    {
        using HttpRequestMessage request = new(HttpMethod.Put, url) { Content = JsonContent.Create(body) };
        request.Headers.TryAddWithoutValidation("If-Match", etag);

        return await client.SendAsync(request);
    }

    private static async Task<string?> CodeOf(HttpResponseMessage response)
    {
        using JsonDocument problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return problem.RootElement.TryGetProperty("code", out JsonElement code) ? code.GetString() : null;
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        string text = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {text}");

        return JsonDocument.Parse(text);
    }

    private static async Task<JsonElement> WaitAsync(HttpClient client, Guid tenantId, Guid id)
    {
        for (int attempt = 0; attempt < 150; attempt++)
        {
            using JsonDocument current = await ReadJsonAsync(await client.GetAsync($"{Executions(tenantId)}/{id}"));

            if (current.RootElement.GetProperty("status").GetString() is "Completed" or "Failed")
            {
                return current.RootElement.Clone();
            }

            await Task.Delay(200);
        }

        throw new TimeoutException("The report worker never settled the execution.");
    }
}
