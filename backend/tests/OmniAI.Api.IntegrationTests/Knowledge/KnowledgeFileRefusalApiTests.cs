using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using OmniAI.Api.IntegrationTests.WebChat;

namespace OmniAI.Api.IntegrationTests.Knowledge;

/// <summary>
/// Phase 37.5a, row 17: a refused upload says which rule refused it, with a
/// stable code a client words in the reader's language.
/// </summary>
/// <remarks>
/// The statuses and the English details are the ones the current frontend already
/// shows; these tests pin that they did not move.
/// </remarks>
public sealed class KnowledgeFileRefusalApiTests(KnowledgeFileApiFixture fixture)
    : IClassFixture<KnowledgeFileApiFixture>
{
    [Fact]
    public async Task A_request_that_is_not_multipart_is_refused_with_its_code()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        HttpResponseMessage response = await owner.PostAsJsonAsync(Files(tenantId), new { title = "x" });

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal("knowledge.file.multipart_required", (await ProblemAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_form_without_a_file_is_refused_with_its_code()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        HttpResponseMessage response = await UploadAsync(owner, tenantId, "empty.txt", []);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        JsonElement problem = await ProblemAsync(response);
        Assert.Equal("knowledge.file.required", problem.GetProperty("code").GetString());
        Assert.Equal("Choose a file to upload.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task A_file_past_the_limit_carries_the_code_and_the_limit_in_bytes()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        HttpResponseMessage response = await UploadAsync(
            owner,
            tenantId,
            "big.txt",
            Encoding.UTF8.GetBytes(new string('x', KnowledgeFileApiFixture.MaxFileBytes + 1)));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);

        JsonElement problem = await ProblemAsync(response);
        Assert.Equal("knowledge.file.too_large", problem.GetProperty("code").GetString());
        Assert.Equal(KnowledgeFileApiFixture.MaxFileBytes, problem.GetProperty("limit").GetInt64());
        Assert.Equal("bytes", problem.GetProperty("limitUnit").GetString());
    }

    [Fact]
    public async Task An_archive_with_too_many_parts_names_its_limit_in_entries()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        HttpResponseMessage response = await UploadAsync(
            owner,
            tenantId,
            "parts.docx",
            Archive(KnowledgeFileApiFixture.MaxArchiveEntries + 4, bytesPerPart: 4));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        JsonElement problem = await ProblemAsync(response);
        Assert.Equal("knowledge.file.archive_too_large", problem.GetProperty("code").GetString());
        Assert.Equal(KnowledgeFileApiFixture.MaxArchiveEntries, problem.GetProperty("limit").GetInt64());
        Assert.Equal("entries", problem.GetProperty("limitUnit").GetString());
    }

    [Fact]
    public async Task An_archive_that_expands_too_far_names_its_limit_in_bytes()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        // One part of 768 KB of spaces: a few kilobytes on the wire.
        HttpResponseMessage response = await UploadAsync(
            owner,
            tenantId,
            "large.docx",
            Archive(1, bytesPerPart: 768 * 1024));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        JsonElement problem = await ProblemAsync(response);
        Assert.Equal("knowledge.file.archive_too_large", problem.GetProperty("code").GetString());
        Assert.Equal(KnowledgeFileApiFixture.MaxArchiveExpandedBytes, problem.GetProperty("limit").GetInt64());
        Assert.Equal("bytes", problem.GetProperty("limitUnit").GetString());
    }

    [Fact]
    public async Task A_refusal_without_a_bound_carries_neither_limit_nor_unit()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        JsonElement problem = await ProblemAsync(
            await UploadAsync(owner, tenantId, "setup.exe", Encoding.UTF8.GetBytes("MZ")));

        Assert.False(problem.TryGetProperty("limit", out _));
        Assert.False(problem.TryGetProperty("limitUnit", out _));
    }

    public static TheoryData<string, byte[], HttpStatusCode, string> ParserRefusals => new()
    {
        { "scan.pdf", KnowledgeFileApiFixture.EmptyPdf(), HttpStatusCode.UnprocessableEntity, "knowledge.file.no_text" },
        { "invoice.pdf", Encoding.UTF8.GetBytes("just words"), HttpStatusCode.UnprocessableEntity, "knowledge.file.type_mismatch" },
        { "notes.docx", Encoding.ASCII.GetBytes("PK\u0003\u0004 not really a zip"), HttpStatusCode.UnprocessableEntity, "knowledge.file.damaged" },
        { "plans.csv", Encoding.UTF8.GetBytes("Plan,Price"), HttpStatusCode.UnprocessableEntity, "knowledge.file.no_text" },
        { "hello.txt", [0x48, 0x65, 0x6C, 0x6C, 0x6F, 0xC3, 0x28], HttpStatusCode.UnprocessableEntity, "knowledge.file.not_utf8" },
        { "setup.exe", Encoding.UTF8.GetBytes("MZ"), HttpStatusCode.UnsupportedMediaType, "knowledge.file.unsupported_type" },
    };

    [Theory]
    [MemberData(nameof(ParserRefusals))]
    public async Task Each_parser_refusal_keeps_its_status_and_names_its_rule(
        string fileName,
        byte[] content,
        HttpStatusCode status,
        string code)
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        HttpResponseMessage response = await UploadAsync(owner, tenantId, fileName, content);

        Assert.Equal(status, response.StatusCode);

        JsonElement problem = await ProblemAsync(response);
        Assert.Equal(code, problem.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("detail").GetString()));
    }

    /// <summary>A zip with this many parts, each this many spaces long.</summary>
    private static byte[] Archive(int parts, int bytesPerPart)
    {
        using MemoryStream buffer = new();

        using (System.IO.Compression.ZipArchive archive = new(
            buffer,
            System.IO.Compression.ZipArchiveMode.Create,
            leaveOpen: true))
        {
            byte[] content = new byte[bytesPerPart];
            Array.Fill(content, (byte)' ');

            for (int index = 0; index < parts; index++)
            {
                using Stream stream = archive.CreateEntry($"word/part{index}.xml").Open();
                stream.Write(content);
            }
        }

        return buffer.ToArray();
    }

    private static Uri Files(Guid tenantId) =>
        WebChatApiFixture.Relative($"/api/tenants/{tenantId}/knowledge/files");

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid tenantId, string name, byte[] content)
    {
        using MultipartFormDataContent form = [];
        ByteArrayContent file = new(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", name);

        return await client.PostAsync(Files(tenantId), form);
    }

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.Clone();
    }
}
