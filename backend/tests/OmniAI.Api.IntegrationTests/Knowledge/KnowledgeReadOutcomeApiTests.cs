using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using OmniAI.Api.IntegrationTests.WebChat;

namespace OmniAI.Api.IntegrationTests.Knowledge;

/// <summary>
/// Phase 37.5a, rows 15 and 16: the last indexing outcome and the source file's
/// size on the knowledge reads.
/// </summary>
/// <remarks>
/// The fixture's embedding provider always refuses, so every index press here is a
/// real attempt that starts, reaches the provider and settles as
/// <c>Failed</c> with <c>provider_unavailable</c> -- the outcome a screen most needs
/// to show after a reload.
/// </remarks>
public sealed class KnowledgeReadOutcomeApiTests(KnowledgeFileApiFixture fixture)
    : IClassFixture<KnowledgeFileApiFixture>
{
    [Fact]
    public async Task A_document_never_indexed_reads_no_attempt_on_the_list_and_the_detail()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (Guid id, _) = await TypedAsync(owner, tenantId);

        JsonElement item = await ListItemAsync(owner, tenantId, id);
        JsonElement detail = await ReadAsync(await owner.GetAsync(Document(tenantId, id)));

        Assert.Equal(JsonValueKind.Null, item.GetProperty("lastIndexAttempt").ValueKind);
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("lastIndexAttempt").ValueKind);
    }

    [Fact]
    public async Task A_failed_attempt_reads_back_with_its_stable_code_after_a_reload()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (Guid id, string tag) = await TypedAsync(owner, tenantId);

        HttpResponseMessage indexed = await SendAsync(owner, HttpMethod.Post, $"{Document(tenantId, id)}/index", tag);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, indexed.StatusCode);

        foreach (JsonElement attempt in new[]
        {
            (await ListItemAsync(owner, tenantId, id)).GetProperty("lastIndexAttempt"),
            (await ReadAsync(await owner.GetAsync(Document(tenantId, id)))).GetProperty("lastIndexAttempt"),
        })
        {
            Assert.Equal("Failed", attempt.GetProperty("status").GetString());
            Assert.Equal("provider_unavailable", attempt.GetProperty("failureCode").GetString());
            Assert.Equal(JsonValueKind.String, attempt.GetProperty("startedAt").ValueKind);
            Assert.Equal(JsonValueKind.String, attempt.GetProperty("completedAt").ValueKind);
        }

        // Readiness is unchanged and still derived: a failed attempt leaves the
        // document needing an index, exactly as before.
        Assert.Equal(
            "NeedsIndex",
            (await ListItemAsync(owner, tenantId, id)).GetProperty("readiness").GetString());
    }

    [Fact]
    public async Task The_latest_attempt_wins()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (Guid id, string tag) = await TypedAsync(owner, tenantId);

        await SendAsync(owner, HttpMethod.Post, $"{Document(tenantId, id)}/index", tag);
        DateTimeOffset first = (await ListItemAsync(owner, tenantId, id))
            .GetProperty("lastIndexAttempt").GetProperty("startedAt").GetDateTimeOffset();

        await SendAsync(owner, HttpMethod.Post, $"{Document(tenantId, id)}/index", tag);
        DateTimeOffset second = (await ListItemAsync(owner, tenantId, id))
            .GetProperty("lastIndexAttempt").GetProperty("startedAt").GetDateTimeOffset();

        Assert.True(second > first);
    }

    [Fact]
    public async Task The_writes_keep_their_shape_and_carry_no_attempt()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        HttpResponseMessage created = await owner.PostAsJsonAsync(
            Knowledge(tenantId),
            new { title = "Opening hours", content = "Nine to five." });

        JsonElement body = await ReadAsync(created);

        Assert.False(body.TryGetProperty("lastIndexAttempt", out _));
    }

    [Fact]
    public async Task The_list_says_how_large_the_source_file_was_and_null_for_typed_text()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        byte[] bytes = Encoding.UTF8.GetBytes("We open at nine and close at five.");
        HttpResponseMessage uploaded = await UploadAsync(owner, tenantId, "hours.txt", bytes);

        Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);

        Guid fromFile = (await ReadAsync(uploaded)).GetProperty("id").GetGuid();
        (Guid typed, _) = await TypedAsync(owner, tenantId);

        Assert.Equal(bytes.Length, (await ListItemAsync(owner, tenantId, fromFile)).GetProperty("sourceByteLength").GetInt64());
        Assert.Equal(JsonValueKind.Null, (await ListItemAsync(owner, tenantId, typed)).GetProperty("sourceByteLength").ValueKind);
    }

    [Fact]
    public async Task Another_workspace_sees_neither_the_document_nor_its_attempts()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (Guid id, string tag) = await TypedAsync(owner, tenantId);
        await SendAsync(owner, HttpMethod.Post, $"{Document(tenantId, id)}/index", tag);

        (HttpClient other, Guid otherTenant, _) = await fixture.CreateOwnerAsync("Other company");

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(Document(tenantId, id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(Document(otherTenant, id))).StatusCode);

        JsonElement theirs = await ReadAsync(await other.GetAsync(Knowledge(otherTenant)));
        Assert.Equal(0, theirs.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task A_member_without_the_view_permission_is_refused_both_reads()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (Guid id, _) = await TypedAsync(owner, tenantId);

        HttpClient member = await fixture.CreateRestrictedMemberAsync(tenantId, "workspace.view");

        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync(Knowledge(tenantId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync(Document(tenantId, id))).StatusCode);
    }

    private static Uri Knowledge(Guid tenantId) =>
        WebChatApiFixture.Relative($"/api/tenants/{tenantId}/knowledge");

    private static Uri Document(Guid tenantId, Guid id) =>
        WebChatApiFixture.Relative($"/api/tenants/{tenantId}/knowledge/{id}");

    private static async Task<(Guid Id, string Tag)> TypedAsync(HttpClient owner, Guid tenantId)
    {
        HttpResponseMessage created = await owner.PostAsJsonAsync(
            Knowledge(tenantId),
            new { title = "Returns policy", content = "Returns are accepted within thirty days." });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        return ((await ReadAsync(created)).GetProperty("id").GetGuid(), created.Headers.ETag!.ToString());
    }

    private static async Task<JsonElement> ListItemAsync(HttpClient client, Guid tenantId, Guid id)
    {
        JsonElement page = await ReadAsync(await client.GetAsync(
            WebChatApiFixture.Relative($"/api/tenants/{tenantId}/knowledge?page=1&pageSize=100")));

        return page.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("id").GetGuid() == id);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string url, string tag)
    {
        using HttpRequestMessage request = new(method, url);
        request.Headers.IfMatch.Add(EntityTagHeaderValue.Parse(tag));

        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid tenantId, string name, byte[] content)
    {
        using MultipartFormDataContent form = [];
        ByteArrayContent file = new(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", name);

        return await client.PostAsync(
            WebChatApiFixture.Relative($"/api/tenants/{tenantId}/knowledge/files"),
            form);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.Clone();
    }
}
