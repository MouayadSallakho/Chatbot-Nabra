using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace OmniAI.Infrastructure.IntegrationTests.Knowledge;

/// <summary>
/// Small synthetic files, built in memory.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Generated rather than committed.</strong> Nothing here is a real
/// business document, nothing came from anybody's machine, and no binary blob
/// enters the repository. A PDF is a few hundred bytes of hand-written syntax and
/// a DOCX is a zip assembled at runtime, which also makes the malformed and
/// oversized cases a matter of changing a number rather than of finding a file
/// that happens to be broken in the right way.
/// </para>
/// <para>
/// The text in them is deliberately dull and invented. A fixture that looked like
/// a real policy would eventually be mistaken for one.
/// </para>
/// </remarks>
internal static class KnowledgeFileFixtures
{
    /// <summary>A minimal PDF with one page of real text.</summary>
    /// <remarks>
    /// Written out by hand because every library that produces PDFs is heavier
    /// than the file it would produce. This is the format's own syntax: a
    /// catalogue, a page tree, one page with a content stream, and a font. Any
    /// reader that handles PDFs at all handles this.
    /// </remarks>
    public static byte[] TextPdf(params string[] lines)
    {
        string content = BuildPdfContentStream(lines.Length == 0 ? ["Hello from a test PDF."] : lines);

        return Assemble(content);
    }

    /// <summary>A structurally valid PDF with one page and nothing drawn on it.</summary>
    /// <remarks>
    /// Stands in for a scanned document: the file opens, the page exists, and
    /// there is no text layer to read. This is the case that must produce an
    /// honest "no text" rather than an empty document.
    /// </remarks>
    public static byte[] EmptyPdf() => Assemble(string.Empty);

    /// <summary>A PDF with the given number of pages, each carrying text.</summary>
    public static byte[] PdfWithPages(int pages)
    {
        // Object 1 is the catalogue and 2 the page tree; each page then takes two
        // objects, and the shared font is last.
        int fontObject = 3 + (pages * 2);

        List<string> objects =
        [
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids["
                + string.Join(' ', Enumerable.Range(0, pages).Select(n => $"{3 + (n * 2)} 0 R"))
                + "]/Count " + pages + ">>",
        ];

        for (int index = 0; index < pages; index++)
        {
            string stream = BuildPdfContentStream([$"Page {index + 1}."]);

            objects.Add(
                "<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]"
                + $"/Resources<</Font<</F1 {fontObject} 0 R>>>>"
                + $"/Contents {3 + (index * 2) + 1} 0 R>>");

            objects.Add($"<</Length {stream.Length}>>stream\n{stream}\nendstream");
        }

        objects.Add("<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>");

        return Serialise(objects);
    }

    /// <summary>Bytes that begin like a PDF and are not one.</summary>
    public static byte[] MalformedPdf() =>
        Encoding.ASCII.GetBytes("%PDF-1.4\nthis is not a pdf at all, it just starts like one");

    /// <summary>A DOCX with the given paragraphs.</summary>
    public static byte[] Docx(params string[] paragraphs) =>
        BuildDocx(DocumentXml(paragraphs.Length == 0 ? ["A paragraph."] : paragraphs));

    /// <summary>A DOCX whose body contains no text at all.</summary>
    public static byte[] EmptyDocx() => BuildDocx(DocumentXml([]));

    /// <summary>A zip that is not a Word document.</summary>
    public static byte[] MalformedDocx()
    {
        using MemoryStream buffer = new();

        using (ZipArchive archive = new(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            ZipArchiveEntry entry = archive.CreateEntry("not-a-document.txt");

            using StreamWriter writer = new(entry.Open());

            writer.Write("This zip has none of the parts a DOCX needs.");
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// A zip whose entries expand far beyond what the policy allows.
    /// </summary>
    /// <remarks>
    /// Not a real zip bomb — a real one would be slow to build and pointless to
    /// commit. This declares more expanded bytes than the bound permits, which is
    /// exactly what the container check reads, and it is refused before anything
    /// decompresses a byte of it.
    /// </remarks>
    public static byte[] DocxExpandingTo(int megabytes)
    {
        using MemoryStream buffer = new();

        using (ZipArchive archive = new(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            ZipArchiveEntry entry = archive.CreateEntry("word/document.xml");

            using Stream stream = entry.Open();

            // Highly compressible, so the archive stays small while the declared
            // expanded length is large.
            byte[] block = new byte[1024 * 1024];

            Array.Fill(block, (byte)' ');

            for (int written = 0; written < megabytes; written++)
            {
                stream.Write(block);
            }
        }

        return buffer.ToArray();
    }

    /// <summary>A DOCX containing more paragraphs than a document may hold.</summary>
    public static byte[] DocxWithCharacters(int characters)
    {
        string paragraph = new('a', 1000);

        int count = (characters / 1000) + 1;

        return BuildDocx(DocumentXml([.. Enumerable.Repeat(paragraph, count)]));
    }

    /// <summary>UTF-8 text, optionally with a byte order mark.</summary>
    public static byte[] Text(string content, bool withBom = false)
    {
        byte[] body = Encoding.UTF8.GetBytes(content);

        return withBom ? [0xEF, 0xBB, 0xBF, .. body] : body;
    }

    /// <summary>Bytes with a NUL in them, which no text file has.</summary>
    public static byte[] BinaryLookingText() =>
        [.. Encoding.UTF8.GetBytes("looks like text"), 0x00, .. Encoding.UTF8.GetBytes("until here")];

    /// <summary>Text that is not valid UTF-8.</summary>
    public static byte[] InvalidUtf8() => [0x48, 0x65, 0x6C, 0x6C, 0x6F, 0xC3, 0x28];

    private static byte[] BuildDocx(string documentXml)
    {
        using MemoryStream buffer = new();

        using (ZipArchive archive = new(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(archive, "[Content_Types].xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                </Types>
                """);

            Write(archive, "_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """);

            Write(archive, "word/document.xml", documentXml);
        }

        return buffer.ToArray();
    }

    private static string DocumentXml(string[] paragraphs)
    {
        StringBuilder body = new();

        foreach (string paragraph in paragraphs)
        {
            body
                .Append("<w:p><w:r><w:t xml:space=\"preserve\">")
                .Append(System.Security.SecurityElement.Escape(paragraph))
                .Append("</w:t></w:r></w:p>");
        }

        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
            + "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">"
            + "<w:body>"
            + body
            + "</w:body></w:document>";
    }

    private static void Write(ZipArchive archive, string path, string content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(path);

        using StreamWriter writer = new(entry.Open(), new UTF8Encoding(false));

        writer.Write(content);
    }

    /// <summary>A content stream drawing the given lines with a standard font.</summary>
    private static string BuildPdfContentStream(string[] lines)
    {
        if (lines.Length == 0)
        {
            return string.Empty;
        }

        StringBuilder stream = new();

        stream.Append("BT /F1 12 Tf 12 TL 20 170 Td\n");

        foreach (string line in lines)
        {
            stream
                .Append('(')
                .Append(line.Replace("\\", "\\\\", StringComparison.Ordinal)
                    .Replace("(", "\\(", StringComparison.Ordinal)
                    .Replace(")", "\\)", StringComparison.Ordinal))
                .Append(") Tj T*\n");
        }

        stream.Append("ET");

        return stream.ToString();
    }

    /// <summary>One page, one font, one content stream.</summary>
    private static byte[] Assemble(string contentStream)
    {
        List<string> objects =
        [
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]"
                + "/Resources<</Font<</F1 5 0 R>>>>/Contents 4 0 R>>",
            $"<</Length {contentStream.Length}>>stream\n{contentStream}\nendstream",
            "<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>",
        ];

        return Serialise(objects);
    }

    /// <summary>
    /// Writes numbered objects out as a real PDF, cross-reference table included.
    /// </summary>
    /// <remarks>
    /// <strong>The xref table is not optional.</strong> A PDF reader finds
    /// objects by byte offset rather than by scanning, so a file without one is
    /// refused as damaged — which is correct behaviour and would make these
    /// fixtures test the malformed path instead of the one they mean to. Building
    /// it here is a few lines and keeps every fixture a genuinely valid file.
    /// </remarks>
    private static byte[] Serialise(List<string> objects)
    {
        StringBuilder pdf = new();

        pdf.Append("%PDF-1.4\n");

        List<int> offsets = [];

        for (int index = 0; index < objects.Count; index++)
        {
            offsets.Add(pdf.Length);

            pdf.Append(index + 1).Append(" 0 obj").Append(objects[index]).Append("\nendobj\n");
        }

        int xref = pdf.Length;

        pdf.Append("xref\n0 ").Append(objects.Count + 1).Append('\n');
        pdf.Append("0000000000 65535 f \n");

        foreach (int offset in offsets)
        {
            pdf.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        pdf
            .Append("trailer<</Size ")
            .Append(objects.Count + 1)
            .Append("/Root 1 0 R>>\nstartxref\n")
            .Append(xref)
            .Append("\n%%EOF");

        // ASCII throughout, so a character index is a byte offset and the table
        // above is correct.
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }
}
