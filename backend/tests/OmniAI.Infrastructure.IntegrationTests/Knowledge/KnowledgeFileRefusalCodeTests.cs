using System.Globalization;
using System.Text;
using OmniAI.Application.Knowledge.Files;
using OmniAI.Infrastructure.Knowledge.Files;

namespace OmniAI.Infrastructure.IntegrationTests.Knowledge;

/// <summary>
/// Phase 37.5a, row 17: every parser refusal names its rule with a stable code,
/// and the bound it broke where it has one.
/// </summary>
/// <remarks>
/// The reasons -- and so the statuses -- are those the extractor tests already
/// pin; these add the code a client words the refusal by. Every file is
/// generated here, as there.
/// </remarks>
public sealed class KnowledgeFileRefusalCodeTests
{
    private static readonly KnowledgeFilePolicy Policy = new();

    public static TheoryData<string, string, byte[], string> Refusals => new()
    {
        { "pdf named but text", "invoice.pdf", KnowledgeFileFixtures.Text("just some words"), KnowledgeFileRefusals.TypeMismatch },
        { "docx named pdf", "report.pdf", KnowledgeFileFixtures.Docx("A paragraph."), KnowledgeFileRefusals.TypeMismatch },
        { "unsupported", "setup.exe", KnowledgeFileFixtures.Text("MZ"), KnowledgeFileRefusals.UnsupportedType },
        { "pdf without text", "scan.pdf", KnowledgeFileFixtures.EmptyPdf(), KnowledgeFileRefusals.NoText },
        { "malformed pdf", "broken.pdf", KnowledgeFileFixtures.MalformedPdf(), KnowledgeFileRefusals.Damaged },
        { "zip that is not a document", "notes.docx", KnowledgeFileFixtures.MalformedDocx(), KnowledgeFileRefusals.Damaged },
        { "docx without text", "blank.docx", KnowledgeFileFixtures.EmptyDocx(), KnowledgeFileRefusals.NoText },
        { "binary named text", "a.txt", KnowledgeFileFixtures.BinaryLookingText(), KnowledgeFileRefusals.TypeMismatch },
        { "not utf-8", "a.txt", KnowledgeFileFixtures.InvalidUtf8(), KnowledgeFileRefusals.NotUtf8 },
        { "csv header only", "plans.csv", KnowledgeFileFixtures.Text("Plan,Price"), KnowledgeFileRefusals.NoText },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public void A_refusal_names_its_rule(string because, string fileName, byte[] content, string code)
    {
        KnowledgeFileRejectedException refused = Refuse(Policy, fileName, content);

        Assert.True(code == refused.Code, $"{because}: expected {code}, got {refused.Code}");
    }

    [Fact]
    public void An_empty_csv_says_it_is_empty()
    {
        // Bytes, but not one line: the CSV reader skips blank lines and finds no
        // header row at all.
        KnowledgeFileRejectedException refused = Refuse(Policy, "empty.csv", KnowledgeFileFixtures.Text("\n\n"));

        Assert.Equal(KnowledgeFileRefusals.Empty, refused.Code);
    }

    [Fact]
    public void A_file_past_the_size_limit_carries_the_limit_in_bytes()
    {
        KnowledgeFilePolicy tiny = new() { MaxFileBytes = 64 };

        KnowledgeFileRejectedException refused = Refuse(tiny, "big.txt", KnowledgeFileFixtures.Text(new string('x', 200)));

        Assert.Equal(KnowledgeFileRefusals.TooLarge, refused.Code);
        Assert.Equal(64, refused.Limit);
        Assert.Equal(KnowledgeFileLimitUnits.Bytes, refused.LimitUnit);
    }

    [Fact]
    public void A_pdf_past_the_page_bound_carries_the_page_limit()
    {
        KnowledgeFilePolicy bounded = new() { MaxPdfPages = 3 };

        KnowledgeFileRejectedException refused = Refuse(bounded, "long.pdf", KnowledgeFileFixtures.PdfWithPages(5));

        Assert.Equal(KnowledgeFileRefusals.TooManyPages, refused.Code);
        Assert.Equal(3, refused.Limit);
        Assert.Equal(KnowledgeFileLimitUnits.Pages, refused.LimitUnit);
    }

    [Fact]
    public void Text_past_the_character_bound_carries_the_character_limit()
    {
        KnowledgeFilePolicy bounded = new() { MaxExtractedCharacters = 100 };

        KnowledgeFileRejectedException refused = Refuse(bounded, "long.txt", KnowledgeFileFixtures.Text(new string('y', 500)));

        Assert.Equal(KnowledgeFileRefusals.TooManyCharacters, refused.Code);
        Assert.Equal(100, refused.Limit);
        Assert.Equal(KnowledgeFileLimitUnits.Characters, refused.LimitUnit);
    }

    [Fact]
    public void A_csv_past_its_bounds_names_which_one()
    {
        string wide = string.Join(',', Enumerable.Range(1, 10).Select(n => $"c{n}")) + "\n"
            + string.Join(',', Enumerable.Range(1, 10).Select(n => $"v{n}"));
        string tall = "Plan\n" + string.Join('\n', Enumerable.Range(1, 10).Select(n => $"p{n}"));
        string cell = "Item,Note\nWidget,\"" + new string('x', 200) + "\"";

        KnowledgeFileRejectedException columns = Refuse(new() { MaxCsvColumns = 3 }, "wide.csv", KnowledgeFileFixtures.Text(wide));
        KnowledgeFileRejectedException rows = Refuse(new() { MaxCsvRows = 3 }, "tall.csv", KnowledgeFileFixtures.Text(tall));
        KnowledgeFileRejectedException cells = Refuse(new() { MaxCsvCellCharacters = 50 }, "cell.csv", KnowledgeFileFixtures.Text(cell));

        Assert.Equal((KnowledgeFileRefusals.TooManyColumns, 3L), (columns.Code, columns.Limit!.Value));
        Assert.Equal((KnowledgeFileRefusals.TooManyRows, 3L), (rows.Code, rows.Limit!.Value));
        Assert.Equal((KnowledgeFileRefusals.CellTooLong, 50L), (cells.Code, cells.Limit!.Value));
        Assert.Equal(KnowledgeFileLimitUnits.Columns, columns.LimitUnit);
        Assert.Equal(KnowledgeFileLimitUnits.Rows, rows.LimitUnit);
        Assert.Equal(KnowledgeFileLimitUnits.Characters, cells.LimitUnit);
    }

    [Fact]
    public void A_docx_that_expands_too_far_names_the_archive_bound()
    {
        KnowledgeFilePolicy bounded = new() { MaxArchiveExpandedBytes = 2 * 1024 * 1024 };

        KnowledgeFileRejectedException refused = Refuse(bounded, "bomb.docx", KnowledgeFileFixtures.DocxExpandingTo(4));

        Assert.Equal(KnowledgeFileRefusals.ArchiveTooLarge, refused.Code);

        // Phase 38.7b (BA-12): the same code counts bytes here...
        Assert.Equal(2 * 1024 * 1024, refused.Limit);
        Assert.Equal(KnowledgeFileLimitUnits.Bytes, refused.LimitUnit);
    }

    [Fact]
    public void A_docx_with_too_many_parts_names_the_archive_bound_in_entries()
    {
        KnowledgeFilePolicy bounded = new() { MaxArchiveEntries = 8 };

        KnowledgeFileRejectedException refused = Refuse(bounded, "parts.docx", KnowledgeFileFixtures.DocxWithEntries(12));

        // ...and entries here: the unit is what tells the two apart.
        Assert.Equal(KnowledgeFileRefusals.ArchiveTooLarge, refused.Code);
        Assert.Equal(8, refused.Limit);
        Assert.Equal(KnowledgeFileLimitUnits.Entries, refused.LimitUnit);
    }

    [Fact]
    public void A_limit_never_travels_without_its_unit()
    {
        Assert.Throws<ArgumentException>(() => new KnowledgeFileRejectedException(
            KnowledgeFileRejection.TooMuchContent, KnowledgeFileRefusals.ArchiveTooLarge, "x", limit: 3));

        Assert.Throws<ArgumentException>(() => new KnowledgeFileRejectedException(
            KnowledgeFileRejection.TooMuchContent, KnowledgeFileRefusals.ArchiveTooLarge, "x", limitUnit: KnowledgeFileLimitUnits.Entries));

        Assert.Throws<ArgumentOutOfRangeException>(() => new KnowledgeFileRejectedException(
            KnowledgeFileRejection.TooMuchContent, KnowledgeFileRefusals.ArchiveTooLarge, "x", limit: 3, limitUnit: "parts"));
    }

    [Fact]
    public void A_password_protected_pdf_is_told_apart_from_a_damaged_one()
    {
        KnowledgeFileRejectedException refused = Refuse(Policy, "locked.pdf", EncryptedPdf());

        Assert.Equal(KnowledgeFileRefusals.Encrypted, refused.Code);
        Assert.Equal(KnowledgeFileRejection.Unreadable, refused.Reason);

        // The same sentence as a damaged file: only the code says more.
        KnowledgeFileRejectedException damaged = Refuse(Policy, "broken.pdf", KnowledgeFileFixtures.MalformedPdf());
        Assert.Equal(damaged.Message, refused.Message);
    }

    private static KnowledgeFileRejectedException Refuse(KnowledgeFilePolicy policy, string fileName, byte[] content) =>
        Assert.Throws<KnowledgeFileRejectedException>(
            () => new KnowledgeFileExtractor(policy).Extract(new KnowledgeFileUpload(fileName, null, content)));

    /// <summary>
    /// A one-page PDF whose trailer names a standard security handler that no
    /// empty password opens: the shape a password-protected file has.
    /// </summary>
    private static byte[] EncryptedPdf()
    {
        string padding = new('A', 64);
        string[] objects =
        [
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]/Contents 4 0 R>>",
            "<</Length 0>>stream\n\nendstream",
            $"<</Filter/Standard/V 1/R 2/O<{padding}>/U<{padding}>/P -4>>",
        ];

        StringBuilder pdf = new("%PDF-1.4\n");
        List<int> offsets = [];

        for (int index = 0; index < objects.Length; index++)
        {
            offsets.Add(pdf.Length);
            pdf.Append(index + 1).Append(" 0 obj").Append(objects[index]).Append("\nendobj\n");
        }

        int xref = pdf.Length;
        pdf.Append("xref\n0 ").Append(objects.Length + 1).Append("\n0000000000 65535 f \n");

        foreach (int offset in offsets)
        {
            pdf.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        pdf.Append("trailer<</Size ").Append(objects.Length + 1)
            .Append("/Root 1 0 R/Encrypt 5 0 R/ID[<0123456789ABCDEF0123456789ABCDEF><0123456789ABCDEF0123456789ABCDEF>]>>\n")
            .Append("startxref\n").Append(xref).Append("\n%%EOF");

        return Encoding.ASCII.GetBytes(pdf.ToString());
    }
}
