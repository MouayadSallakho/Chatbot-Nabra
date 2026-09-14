using OmniAI.Api.IntegrationTests.Authorization;
using OmniAI.Application.Knowledge.Files;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace OmniAI.Api.IntegrationTests.Knowledge;

/// <summary>
/// A hosted application with deliberately small upload limits.
/// </summary>
/// <remarks>
/// <strong>The bounds are shrunk so the tests can reach them.</strong> Proving
/// that a file over the limit is refused should not mean generating ten megabytes
/// on every test run; the production defaults are a deployment setting, and what
/// matters here is that the limit is enforced wherever it is set.
/// <para>
/// Nothing else is substituted. The real parsers read the real bytes, and the
/// embedding provider is unconfigured exactly as it is for every other suite.
/// </para>
/// </remarks>
public sealed class KnowledgeFileApiFixture : RoleApiFixture
{
    /// <summary>Small enough for a test to exceed cheaply.</summary>
    public const int MaxFileBytes = 32 * 1024;

    private readonly RecordingEmbeddingProvider _embeddings = new();

    /// <summary>
    /// The provider this host would spend money at.
    /// </summary>
    /// <remarks>
    /// Present so "it was never called" is something a test states rather than
    /// assumes. Uploading must cost nothing.
    /// </remarks>
    internal RecordingEmbeddingProvider Embeddings => _embeddings;

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        base.ConfigureTestServices(services);

        services.Replace(ServiceDescriptor.Singleton(new KnowledgeFilePolicy
        {
            MaxFileBytes = MaxFileBytes,
        }));

        services.Replace(
            ServiceDescriptor.Singleton<Application.Knowledge.ITextEmbeddingGenerator>(_embeddings));
    }

    /// <summary>A tiny PDF with one page of real text.</summary>
    public static byte[] TextPdf(string line) => Pdf(Content(line));

    /// <summary>A PDF with a page and no text layer, standing in for a scan.</summary>
    public static byte[] EmptyPdf() => Pdf(string.Empty);

    /// <summary>A tiny DOCX with one paragraph.</summary>
    public static byte[] Docx(string paragraph)
    {
        using MemoryStream buffer = new();

        using (System.IO.Compression.ZipArchive archive = new(
            buffer,
            System.IO.Compression.ZipArchiveMode.Create,
            leaveOpen: true))
        {
            Write(archive, "[Content_Types].xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                + "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
                + "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>"
                + "<Default Extension=\"xml\" ContentType=\"application/xml\"/>"
                + "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>"
                + "</Types>");

            Write(archive, "_rels/.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>"
                + "</Relationships>");

            Write(archive, "word/document.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                + "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">"
                + "<w:body><w:p><w:r><w:t xml:space=\"preserve\">"
                + System.Security.SecurityElement.Escape(paragraph)
                + "</w:t></w:r></w:p></w:body></w:document>");
        }

        return buffer.ToArray();
    }

    private static void Write(System.IO.Compression.ZipArchive archive, string path, string content)
    {
        System.IO.Compression.ZipArchiveEntry entry = archive.CreateEntry(path);

        using StreamWriter writer = new(entry.Open(), new System.Text.UTF8Encoding(false));

        writer.Write(content);
    }

    private static string Content(string line) =>
        line.Length == 0
            ? string.Empty
            : $"BT /F1 12 Tf 20 170 Td ({line.Replace("(", "\\(", StringComparison.Ordinal)
                .Replace(")", "\\)", StringComparison.Ordinal)}) Tj ET";

    /// <summary>
    /// One page, written out with a real cross-reference table.
    /// </summary>
    /// <remarks>
    /// The table is not optional: a reader finds objects by byte offset, so a PDF
    /// without one is refused as damaged and this fixture would be testing the
    /// malformed path rather than the one it means to.
    /// </remarks>
    private static byte[] Pdf(string contentStream)
    {
        string[] objects =
        [
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]"
                + "/Resources<</Font<</F1 5 0 R>>>>/Contents 4 0 R>>",
            $"<</Length {contentStream.Length}>>stream\n{contentStream}\nendstream",
            "<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>",
        ];

        System.Text.StringBuilder pdf = new();

        pdf.Append("%PDF-1.4\n");

        List<int> offsets = [];

        for (int index = 0; index < objects.Length; index++)
        {
            offsets.Add(pdf.Length);

            pdf.Append(index + 1).Append(" 0 obj").Append(objects[index]).Append("\nendobj\n");
        }

        int xref = pdf.Length;

        pdf.Append("xref\n0 ").Append(objects.Length + 1).Append('\n');
        pdf.Append("0000000000 65535 f \n");

        foreach (int offset in offsets)
        {
            pdf
                .Append(offset.ToString("D10", System.Globalization.CultureInfo.InvariantCulture))
                .Append(" 00000 n \n");
        }

        pdf
            .Append("trailer<</Size ")
            .Append(objects.Length + 1)
            .Append("/Root 1 0 R>>\nstartxref\n")
            .Append(xref)
            .Append("\n%%EOF");

        return System.Text.Encoding.ASCII.GetBytes(pdf.ToString());
    }
}

/// <summary>
/// An embedding provider that records rather than calls.
/// </summary>
/// <remarks>
/// It refuses to generate. These tests are about uploading, and uploading must
/// never reach a provider — so a call is a failure rather than something to be
/// stubbed out politely.
/// </remarks>
internal sealed class RecordingEmbeddingProvider : Application.Knowledge.ITextEmbeddingGenerator
{
    public Application.Knowledge.EmbeddingProfile Profile { get; } =
        Application.Knowledge.EmbeddingProfile.Create(
            Domain.Knowledge.EmbeddingProfileId.Create("test:fake:8"),
            dimensions: 8,
            maxBatchSize: 4,
            maxInputCharacters: 8_000);

    /// <summary>How many times a provider request was made.</summary>
    public int Calls { get; private set; }

    public void Reset() => Calls = 0;

    public Task<IReadOnlyList<Application.Knowledge.EmbeddingVector>> GenerateAsync(
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken = default)
    {
        Calls++;

        throw new Application.Knowledge.EmbeddingUnavailableException(
            "No embedding provider is configured for this deployment.");
    }
}
