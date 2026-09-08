using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace OmniAI.Api.IntegrationTests.Knowledge;

/// <summary>
/// The knowledge slice over HTTP.
/// </summary>
public sealed class KnowledgeEndpointsTests(KnowledgeApiFixture api)
    : IClassFixture<KnowledgeApiFixture>
{
    private const string ProblemJson = "application/problem+json";

    private const string Content = "Goods may be returned within 30 days of delivery.";

    // ------------------------------------------------------------------ create

    [Fact]
    public async Task Creating_a_document_returns_201_with_a_strong_etag()
    {
        using HttpClient client = await api.CreateSignedInClientAsync();
        Guid tenantId = await api.CreateTenantAsync(client);

        using HttpResponseMessage response = await CreateAsync(client, tenantId);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        AssertStrongETag(response);

        JsonElement created = await ReadAsync(response);

        Assert.Equal("Returns policy", created.GetProperty("title").GetString());
        Assert.Equal(Content, created.GetProperty("content").GetString());
        Assert.True(created.GetProperty("isEnabled").GetBoolean());
    }

    [Fact]
    public async Task A_new_document_needs_an_index()
    {
        // Nothing was spent creating it, so it cannot answer anything yet, and
        // the API says so rather than leaving a client to work it out.
        using HttpClient client = await api.CreateSignedInClientAsync();
        Guid tenantId = await api.CreateTenantAsync(client);

        using HttpResponseMessage response = await CreateAsync(client, tenantId);

        JsonElement created = await ReadAsync(response);

        Assert.Equal("NeedsIndex", created.GetProperty("readiness").GetString());
        Assert.Equal(JsonValueKind.Null, created.GetProperty("indexedAt").ValueKind);
    }

    [Fact]
    public async Task A_document_without_a_title_is_rejected()
    {
        using HttpClient client = await api.CreateSignedInClientAsync();
        Guid tenantId = await api.CreateTenantAsync(client);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            KnowledgeUrl(tenantId),
            new { title = "  ", content = Content });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Content_beyond_the_bound_is_rejected_rather_than_truncated()
    {
        using HttpClient client = await api.CreateSignedInClientAsync();
        Guid tenantId = await api.CreateTenantAsync(client);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            KnowledgeUrl(tenantId),
            new { title = "Huge", content = new string('a', 100_001) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // -------------------------------------------------------------------- read

    [Fact]
    public async Task Reading_a_document_returns_it_with_the_version_needed_to_change_it()
    {
        using HttpClient client = await api.CreateSignedInClientAsync();
        Guid tenantId = await api.CreateTenantAsync(client);

        (Guid id, string etag) = await CreatedAsync(client, tenantId);

        using HttpResponseMessage response =
            await client.GetAsync(DocumentUrl(tenantId, id));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(etag, response.Headers.ETag?.ToString());
    }

    [Fact]
    public async Task The_list_omits_the_content_and_reports_its_length()
    {
        // A workspace with two hundred long documents would otherwise move
        // megabytes to render a list of titles.
        using HttpClient client = await api.CreateSignedInClientAsync();
        Guid tenantId = await api.CreateTenantAsync(client);

        await CreatedAsync(client, tenantId);

        using HttpResponseMessage response = await client.GetAsync(KnowledgeUrl(tenantId));

        JsonElement page = await ReadAsync(response);

        JsonElement item = page.GetProperty("items").EnumerateArray().First();

        Assert.False(item.TryGetProperty("content", out _));
        Assert.Equal(Content.Length, item.GetProperty("contentLength").GetInt32());
    }

    [Fact]
    public async Task A_document_never_publishes_a_vector_or_an_embedding_profile()
    {
        // Neither is any use to a browser, they are the bulkiest thing this
        // feature stores, and publishing one would let anybody who can read a
        // document compare it arithmetically against every other.
        using HttpClient client = await api.CreateSignedInClientAsync();
        Guid tenantId = await api.CreateTenantAsync(client);

        (Guid id, _) = await CreatedAsync(client, tenantId);

        using HttpResponseMessage detail = await client.GetAsync(DocumentUrl(tenantId, id));
        using HttpResponseMessage list = await client.GetAsync(KnowledgeUrl(tenantId));

        foreach (HttpResponseMessage response in new[] { detail, list })
        {
            string body = await response.Content.ReadAsStringAsync();

            foreach (string word in new[] { "embedding", "vector", "chunk", "fingerprint", "profile" })
            {
                Assert.DoesNotContain(word, body, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    // ------------------------------------------------------------------ update

    [Fact]
    public async Task Updating_a_document_requires_a_precondition()
    {
        using HttpClient client = await api.CreateSignedInClientAsync();
        Guid tenantId = await api.CreateTenantAsync(client);

        (Guid id, _) = await CreatedAsync(client, tenantId);

        using HttpResponseMessage response = await client.PutAsJsonAsync(
            DocumentUrl(tenantId, id),
            new { title = "Returns policy", content = "Changed." });

        Assert.Equal(HttpStatusCode.PreconditionRequired, response.StatusCode);
    }

    [Fact]
    public async Task Updating_with_a_stale_precondition_is_refused()
    {
        using HttpClient client = await api.CreateSignedInClientAsync();
        Guid tenantId = await api.CreateTenantAsync(client);

        (Guid id, string etag) = await CreatedAsync(client, tenantId);

        using (HttpResponseMessage first =
            await UpdateAsync(client, tenantId, id, etag, "First change."))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        }

        using HttpResponseMessage second =
            await UpdateAsync(client, tenantId, id, etag, "Second change.");

        Assert.Equal(HttpStatusCode.PreconditionFailed, second.StatusCode);
    }

    [Fact]
    public async Task A_successful_update_returns_a_new_version()
    {
        using HttpClient client = await api.CreateSignedInClientAsync();
        Guid tenantId = await api.CreateTenantAsync(client);

        (Guid id, string etag) = await CreatedAsync(client, tenantId);

        using HttpResponseMessage response =
            await UpdateAsync(client, tenantId, id, etag, "Changed.");

        AssertStrongETag(response);

        Assert.NotEqual(etag, response.Headers.ETag?.ToString());
    }

    [Fact]
    public async Task Editing_the_content_puts_the_document_back_to_needing_an_index()
    {
        using HttpClient client = await api.CreateSignedInClientAsync();
        Guid tenantId = await api.CreateTenantAsync(client);

        (Guid id, string etag) = await CreatedAsync(client, tenantId);

        using HttpResponseMessage response =
            await UpdateAsync(client, tenantId, id, etag, "Completely different.");

        JsonElement updated = await ReadAsync(response);

        Assert.Equal("NeedsIndex", updated.GetProperty("readiness").GetString());
    }

    // ------------------------------------------------------------------- index

    [Fact]
    public async Task Indexing_requires_a_precondition()
    {
        // Indexing is a write: it replaces the chunk set and moves the version.
        using HttpClient client = await api.CreateSignedInClientAsync();
        Guid tenantId = await api.CreateTenantAsync(client);

        (Guid id, _) = await CreatedAsync(client, tenantId);

        using HttpResponseMessage response = await SendAsync(
            client,
            HttpMethod.Post,
            $"{DocumentUrl(tenantId, id)}/index",
            etag: null);

        Assert.Equal(HttpStatusCode.PreconditionRequired, response.StatusCode);
    }

    [Fact]
    public async Task Indexing_without_a_provider_answers_503_and_changes_nothing()
    {
        // Exactly what a deployment with no credential does. The content stays
        // saved, the document does not falsely become ready, and no partial index
        // is left behind.
        using HttpClient client = await api.CreateSignedInClientAsync();
        Guid tenantId = await api.CreateTenantAsync(client);

        (Guid id, string etag) = await CreatedAsync(client, tenantId);

        using (HttpResponseMessage index = await SendAsync(
            client,
            HttpMethod.Post,
            $"{DocumentUrl(tenantId, id)}/index",
            etag))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, index.StatusCode);
            Assert.Equal(ProblemJson, index.Content.Headers.ContentType?.MediaType);
        }

        using HttpResponseMessage after = await client.GetAsync(DocumentUrl(tenantId, id));

        JsonElement document = await ReadAsync(after);

        Assert.Equal(Content, document.GetProperty("content").GetString());
        Assert.Equal("NeedsIndex", document.GetProperty("readiness").GetString());
        Assert.Equal(JsonValueKind.Null, document.GetProperty("indexedAt").ValueKind);
        Assert.Equal(etag, after.Headers.ETag?.ToString());
    }

    // ------------------------------------------------------------- enable

    [Fact]
    public async Task Disabling_and_enabling_move_the_version_and_the_readiness()
    {
        using HttpClient client = await api.CreateSignedInClientAsync();
        Guid tenantId = await api.CreateTenantAsync(client);

        (Guid id, string etag) = await CreatedAsync(client, tenantId);

        string next;

        using (HttpResponseMessage disabled = await SendAsync(
            client,
            HttpMethod.Post,
            $"{DocumentUrl(tenantId, id)}/disable",
            etag))
        {
            Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);

            JsonElement body = await ReadAsync(disabled);

            Assert.False(body.GetProperty("isEnabled").GetBoolean());
            Assert.Equal("Disabled", body.GetProperty("readiness").GetString());

            next = disabled.Headers.ETag!.ToString();
            Assert.NotEqual(etag, next);
        }

        using HttpResponseMessage enabled = await SendAsync(
            client,
            HttpMethod.Post,
            $"{DocumentUrl(tenantId, id)}/enable",
            next);

        Assert.Equal(HttpStatusCode.OK, enabled.StatusCode);
        Assert.True((await ReadAsync(enabled)).GetProperty("isEnabled").GetBoolean());
    }

    [Fact]
    public async Task Disabling_a_disabled_document_is_a_conflict()
    {
        using HttpClient client = await api.CreateSignedInClientAsync();
        Guid tenantId = await api.CreateTenantAsync(client);

        (Guid id, string etag) = await CreatedAsync(client, tenantId);

        string next;

        using (HttpResponseMessage first = await SendAsync(
            client,
            HttpMethod.Post,
            $"{DocumentUrl(tenantId, id)}/disable",
            etag))
        {
            next = first.Headers.ETag!.ToString();
        }

        using HttpResponseMessage again = await SendAsync(
            client,
            HttpMethod.Post,
            $"{DocumentUrl(tenantId, id)}/disable",
            next);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    // -------------------------------------------------------------- isolation

    [Fact]
    public async Task Another_workspace_document_is_not_found()
    {
        using HttpClient client = await api.CreateSignedInClientAsync();

        Guid mine = await api.CreateTenantAsync(client, "Mine");
        Guid theirs = await api.CreateTenantAsync(client, "Theirs");

        (Guid id, _) = await CreatedAsync(client, theirs);

        using HttpResponseMessage response = await client.GetAsync(DocumentUrl(mine, id));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_workspace_the_caller_does_not_belong_to_is_not_found()
    {
        using HttpClient owner = await api.CreateSignedInClientAsync();
        Guid tenantId = await api.CreateTenantAsync(owner);

        (Guid id, _) = await CreatedAsync(owner, tenantId);

        using HttpClient stranger = await api.CreateSignedInClientAsync();

        using HttpResponseMessage detail = await stranger.GetAsync(DocumentUrl(tenantId, id));
        using HttpResponseMessage list = await stranger.GetAsync(KnowledgeUrl(tenantId));

        Assert.Equal(HttpStatusCode.NotFound, detail.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, list.StatusCode);
    }

    [Fact]
    public async Task A_document_that_does_not_exist_is_not_found()
    {
        using HttpClient client = await api.CreateSignedInClientAsync();
        Guid tenantId = await api.CreateTenantAsync(client);

        using HttpResponseMessage response =
            await client.GetAsync(DocumentUrl(tenantId, Guid.CreateVersion7()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ------------------------------------------------------------ permissions

    [Fact]
    public async Task An_anonymous_caller_is_refused()
    {
        // The public may be answered from knowledge and can never read it.
        using HttpClient owner = await api.CreateSignedInClientAsync();
        Guid tenantId = await api.CreateTenantAsync(owner);

        using HttpClient anonymous = api.CreateClient();

        using HttpResponseMessage response = await anonymous.GetAsync(KnowledgeUrl(tenantId));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_member_without_knowledge_view_is_refused_with_403()
    {
        using HttpClient owner = await api.CreateSignedInClientAsync();
        Guid tenantId = await api.CreateTenantAsync(owner);

        using HttpClient limited =
            await api.CreateRestrictedMemberAsync(tenantId, "workspace.view");

        using HttpResponseMessage response = await limited.GetAsync(KnowledgeUrl(tenantId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Knowledge_view_does_not_carry_the_right_to_write()
    {
        // Reading a workspace's documents and rewriting them are different
        // capabilities, and one is deliberately not implied by the other.
        using HttpClient owner = await api.CreateSignedInClientAsync();
        Guid tenantId = await api.CreateTenantAsync(owner);

        (Guid id, string etag) = await CreatedAsync(owner, tenantId);

        using HttpClient reader =
            await api.CreateRestrictedMemberAsync(tenantId, "knowledge.view");

        using (HttpResponseMessage read = await reader.GetAsync(DocumentUrl(tenantId, id)))
        {
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        }

        using HttpResponseMessage write =
            await UpdateAsync(reader, tenantId, id, etag, "Changed by a reader.");

        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
    }

    [Fact]
    public async Task Knowledge_manage_does_not_carry_the_right_to_index_someone_elses_workspace()
    {
        using HttpClient owner = await api.CreateSignedInClientAsync();
        Guid mine = await api.CreateTenantAsync(owner, "Mine");

        (Guid id, string etag) = await CreatedAsync(owner, mine);

        using HttpClient stranger = await api.CreateSignedInClientAsync();

        using HttpResponseMessage response = await SendAsync(
            stranger,
            HttpMethod.Post,
            $"{DocumentUrl(mine, id)}/index",
            etag);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_write_without_the_antiforgery_token_is_refused()
    {
        // Every unsafe admin write stays CSRF protected, knowledge included.
        using HttpClient client = await api.CreateSignedInClientAsync();
        Guid tenantId = await api.CreateTenantAsync(client);

        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            KnowledgeUrl(tenantId),
            new { title = "Returns policy", content = Content });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ------------------------------------------------------------------ paging

    [Fact]
    public async Task An_invalid_page_is_rejected()
    {
        using HttpClient client = await api.CreateSignedInClientAsync();
        Guid tenantId = await api.CreateTenantAsync(client);

        using HttpResponseMessage response =
            await client.GetAsync($"{KnowledgeUrl(tenantId)}?page=0");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_oversized_page_is_rejected()
    {
        using HttpClient client = await api.CreateSignedInClientAsync();
        Guid tenantId = await api.CreateTenantAsync(client);

        using HttpResponseMessage response =
            await client.GetAsync($"{KnowledgeUrl(tenantId)}?pageSize=101");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ------------------------------------------------------------------ helpers

    private static string KnowledgeUrl(Guid tenantId) =>
        string.Create(CultureInfo.InvariantCulture, $"/api/tenants/{tenantId}/knowledge");

    private static string DocumentUrl(Guid tenantId, Guid documentId) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"/api/tenants/{tenantId}/knowledge/{documentId}");

    private static Task<HttpResponseMessage> CreateAsync(
        HttpClient client,
        Guid tenantId,
        string title = "Returns policy",
        string content = Content) =>
        client.PostAsJsonAsync(KnowledgeUrl(tenantId), new { title, content });

    private static async Task<(Guid Id, string ETag)> CreatedAsync(
        HttpClient client,
        Guid tenantId,
        string title = "Returns policy",
        string content = Content)
    {
        using HttpResponseMessage response = await CreateAsync(client, tenantId, title, content);

        response.EnsureSuccessStatusCode();

        JsonElement body = await ReadAsync(response);

        return (body.GetProperty("id").GetGuid(), response.Headers.ETag!.ToString());
    }

    private static Task<HttpResponseMessage> UpdateAsync(
        HttpClient client,
        Guid tenantId,
        Guid id,
        string etag,
        string content,
        string title = "Returns policy") =>
        SendAsync(
            client,
            HttpMethod.Put,
            DocumentUrl(tenantId, id),
            etag,
            new { title, content });

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string url,
        string? etag,
        object? body = null)
    {
        using HttpRequestMessage request = new(method, url);

        if (etag is not null)
        {
            request.Headers.IfMatch.Add(EntityTagHeaderValue.Parse(etag));
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        using JsonDocument document =
            JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.Clone();
    }

    private static void AssertStrongETag(HttpResponseMessage response)
    {
        EntityTagHeaderValue tag = Assert.IsType<EntityTagHeaderValue>(response.Headers.ETag);

        Assert.False(tag.IsWeak);
        Assert.StartsWith("\"", tag.Tag, StringComparison.Ordinal);
        Assert.EndsWith("\"", tag.Tag, StringComparison.Ordinal);
    }
}
