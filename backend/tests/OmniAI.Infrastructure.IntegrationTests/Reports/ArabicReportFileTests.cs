using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using OmniAI.Application.Reports;
using OmniAI.Domain.Reports;
using OmniAI.Domain.Users;
using OmniAI.Infrastructure.Reports.Rendering;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using Page = UglyToad.PdfPig.Content.Page;

namespace OmniAI.Infrastructure.IntegrationTests.Reports;

/// <summary>
/// Arabic report files, opened by real parsers (Phase 37.7).
/// </summary>
/// <remarks>
/// Every report type is built in Arabic from <see cref="ReportFixtures"/> and
/// rendered in every format, then read back by an independent parser: CsvHelper
/// for the header and the encoding, the OpenXML SDK for the sheets' direction,
/// names, headers and number formats, and PdfPig for the text layer's order and
/// the page's direction. The English counterparts are the golden files
/// (<see cref="ReportGoldenFileTests"/>).
/// </remarks>
public sealed class ArabicReportFileTests
{
    private const float PageWidth = 841.89f;

    public static TheoryData<ReportType> Types()
    {
        TheoryData<ReportType> types = [];

        foreach (ReportType type in Enum.GetValues<ReportType>())
        {
            types.Add(type);
        }

        return types;
    }

    // ------------------------------------------------------------ the document

    [Theory]
    [MemberData(nameof(Types))]
    public async Task Every_word_the_platform_writes_is_arabic(ReportType type)
    {
        ReportDocument document = await Arabic(type);

        Assert.Equal(UserLanguage.Arabic, document.Language);
        Assert.True(HasArabic(document.Title), document.Title);

        foreach (ReportFact fact in document.Summary)
        {
            Assert.True(HasArabic(fact.Label), fact.Label);
        }

        foreach (ReportTable table in document.Tables)
        {
            Assert.True(HasArabic(table.Name), table.Name);
            Assert.True(HasArabic(table.Title), table.Title);

            foreach (ReportColumn column in table.Columns)
            {
                Assert.True(HasArabic(column.Header), column.Header);
            }
        }

        Assert.All(document.Notes, note => Assert.True(HasArabic(note), note));
    }

    [Theory]
    [MemberData(nameof(Types))]
    public async Task Figures_dates_and_stored_names_are_the_same_in_both_languages(ReportType type)
    {
        ReportDocument arabic = await Arabic(type);
        ReportDocument english = await ReportFixtures.BuildAsync(type);

        Assert.Equal(english.Tables.Count, arabic.Tables.Count);
        Assert.Equal(english.Summary.Select(fact => fact.Value), arabic.Summary.Select(fact => fact.Value));

        for (int table = 0; table < english.Tables.Count; table++)
        {
            Assert.Equal(english.Tables[table].Columns.Select(column => column.Kind), arabic.Tables[table].Columns.Select(column => column.Kind));
            Assert.Equal(english.Tables[table].Rows.Count, arabic.Tables[table].Rows.Count);

            for (int row = 0; row < english.Tables[table].Rows.Count; row++)
            {
                for (int cell = 0; cell < english.Tables[table].Rows[row].Count; cell++)
                {
                    ReportValue before = english.Tables[table].Rows[row][cell];
                    ReportValue after = arabic.Tables[table].Rows[row][cell];

                    Assert.Equal(before.Kind, after.Kind);

                    // Only the platform's own words in text cells may change, and
                    // only into Arabic: numbers, dates and stored names do not.
                    if (before.Kind != ReportValueKind.Text || before.TextValue == after.TextValue)
                    {
                        Assert.Equal(before, after);
                    }
                    else
                    {
                        Assert.True(HasArabic(after.TextValue!), $"{before.TextValue} → {after.TextValue}");
                    }
                }
            }
        }
    }

    // --------------------------------------------------------------------- CSV

    [Theory]
    [MemberData(nameof(Types))]
    public async Task Csv_has_arabic_headers_and_a_byte_order_mark_and_the_same_values(ReportType type)
    {
        ReportDocument document = await Arabic(type);
        byte[] arabic = await ReportRenderingTests.Render(new CsvReportRenderer(), document);
        byte[] english = await ReportRenderingTests.Render(new CsvReportRenderer(), await ReportFixtures.BuildAsync(type));

        Assert.Equal([0xEF, 0xBB, 0xBF], arabic[..3]);

        // Strict UTF-8: a decoder that throws on any invalid sequence reads it all.
        _ = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(arabic, 3, arabic.Length - 3);

        List<string[]> rows = ReportRenderingTests.ReadCsv(arabic);
        List<string[]> englishRows = ReportRenderingTests.ReadCsv(english);

        Assert.Equal(document.Primary.Columns.Select(column => column.Header), rows[0]);
        Assert.All(rows[0], header => Assert.True(HasArabic(header), header));
        Assert.Equal(englishRows.Count, rows.Count);

        // Every number, date and instant cell is written exactly as in English.
        for (int column = 0; column < document.Primary.Columns.Count; column++)
        {
            if (document.Primary.Columns[column].Kind != ReportValueKind.Text)
            {
                Assert.Equal(englishRows.Skip(1).Select(row => row[column]), rows.Skip(1).Select(row => row[column]));
            }
        }
    }

    // ------------------------------------------------------------------- Excel

    [Theory]
    [MemberData(nameof(Types))]
    public async Task Excel_opens_right_to_left_with_arabic_sheets_and_headers_and_unchanged_formats(ReportType type)
    {
        ReportDocument document = await Arabic(type);
        byte[] arabic = await ReportRenderingTests.Render(new XlsxReportRenderer(), document);
        byte[] english = await ReportRenderingTests.Render(new XlsxReportRenderer(), await ReportFixtures.BuildAsync(type));

        using MemoryStream arabicStream = new(arabic);
        using SpreadsheetDocument workbook = SpreadsheetDocument.Open(arabicStream, isEditable: false);
        using MemoryStream englishStream = new(english);
        using SpreadsheetDocument englishWorkbook = SpreadsheetDocument.Open(englishStream, isEditable: false);

        List<Sheet> sheets = [.. workbook.WorkbookPart!.Workbook!.Sheets!.Elements<Sheet>()];

        Assert.Equal(["الملخص", .. document.Tables.Select(table => table.Name)], sheets.Select(sheet => sheet.Name!.Value!));

        foreach (Sheet sheet in sheets)
        {
            Assert.True(HasArabic(sheet.Name!.Value!));

            Worksheet worksheet = ((WorksheetPart)workbook.WorkbookPart.GetPartById(sheet.Id!.Value!)).Worksheet!;
            SheetView view = worksheet.Descendants<SheetView>().Single();

            Assert.True(view.RightToLeft?.Value, $"{sheet.Name} is not right to left.");
        }

        // The English workbook carries no direction at all.
        Assert.All(
            englishWorkbook.WorkbookPart!.WorksheetParts,
            part => Assert.DoesNotContain(part.Worksheet!.Descendants<SheetView>(), view => view.RightToLeft is not null));

        // The headers of each table sheet are the document's Arabic headers.
        for (int index = 0; index < document.Tables.Count; index++)
        {
            Worksheet worksheet = ((WorksheetPart)workbook.WorkbookPart.GetPartById(sheets[index + 1].Id!.Value!)).Worksheet!;
            Row header = worksheet.Descendants<Row>().First();

            Assert.Equal(
                document.Tables[index].Columns.Select(column => column.Header),
                header.Elements<Cell>().Select(cell => cell.InlineString!.InnerText));
        }

        // Number and date formats are unchanged: the same stylesheet, and every
        // cell has the same type and style as its English counterpart.
        Assert.Equal(
            englishWorkbook.WorkbookPart.WorkbookStylesPart!.Stylesheet!.OuterXml,
            workbook.WorkbookPart.WorkbookStylesPart!.Stylesheet!.OuterXml);

        List<WorksheetPart> englishParts = [.. englishWorkbook.WorkbookPart.Workbook!.Sheets!.Elements<Sheet>()
            .Select(sheet => (WorksheetPart)englishWorkbook.WorkbookPart.GetPartById(sheet.Id!.Value!))];

        for (int index = 0; index < sheets.Count; index++)
        {
            List<Cell> arabicCells = [.. ((WorksheetPart)workbook.WorkbookPart.GetPartById(sheets[index].Id!.Value!)).Worksheet!.Descendants<Cell>()];
            List<Cell> englishCells = [.. englishParts[index].Worksheet!.Descendants<Cell>()];

            Assert.Equal(englishCells.Select(cell => (cell.CellReference?.Value, cell.DataType?.Value, cell.StyleIndex?.Value)),
                arabicCells.Select(cell => (cell.CellReference?.Value, cell.DataType?.Value, cell.StyleIndex?.Value)));
            Assert.Equal(
                englishCells.Where(cell => cell.DataType?.Value == CellValues.Number).Select(cell => cell.CellValue!.Text),
                arabicCells.Where(cell => cell.DataType?.Value == CellValues.Number).Select(cell => cell.CellValue!.Text));
        }
    }

    // --------------------------------------------------------------------- PDF

    [Theory]
    [MemberData(nameof(Types))]
    public async Task Pdf_lays_the_page_out_right_to_left(ReportType type)
    {
        Page arabic = FirstPage(await ReportRenderingTests.Render(new PdfReportRenderer(), await Arabic(type)));
        Page english = FirstPage(await ReportRenderingTests.Render(new PdfReportRenderer(), await ReportFixtures.BuildAsync(type)));

        // The title is the largest text on the page. In English it starts at the left
        // margin; in Arabic it ends at the right margin.
        List<Letter> arabicTitle = Title(arabic);
        List<Letter> englishTitle = Title(english);

        Assert.InRange(englishTitle.Min(letter => letter.StartBaseLine.X), 39, 42);
        Assert.InRange(arabicTitle.Max(letter => letter.EndBaseLine.X), PageWidth - 42, PageWidth - 39);
        Assert.True(arabicTitle.Min(letter => letter.StartBaseLine.X) > PageWidth / 2, "An Arabic title should sit on the right half.");

        // The workspace's name is a value: Latin, kept in its own order, placed to
        // the left of its Arabic label.
        Assert.Contains("Northwind Trading", arabic.Text, StringComparison.Ordinal);
        Word northwind = arabic.GetWords().First(word => word.Text == "Northwind");
        Assert.True(northwind.BoundingBox.Right < PageWidth / 2 + 300 && northwind.BoundingBox.Left > PageWidth / 4);

        // Dates inside Arabic sentences keep their own left-to-right order.
        Assert.Contains("2026-10-01", arabic.Text, StringComparison.Ordinal);
        Assert.Contains("2026-10-07", arabic.Text, StringComparison.Ordinal);

        // Footer: page number at the left margin, title at the right.
        Letter[] footer = [.. arabic.Letters.Where(letter => letter.StartBaseLine.Y < 60)];
        Assert.NotEmpty(footer);
        Assert.InRange(footer.Min(letter => letter.StartBaseLine.X), 39, 42);
        Assert.InRange(footer.Max(letter => letter.EndBaseLine.X), PageWidth - 42, PageWidth - 39);

        // No Arabic-Indic digit anywhere: localisation decision 3.
        Assert.DoesNotContain(arabic.Letters, letter => letter.Value.Any(character => character is >= '\u0660' and <= '\u0669'));
        Assert.DoesNotContain('\uFFFD', arabic.Text);
    }

    [Fact]
    public async Task Pdf_text_layer_reads_arabic_in_visual_order_and_isolates_latin_values()
    {
        Page page = FirstPage(await ReportRenderingTests.Render(new PdfReportRenderer(), await Arabic(ReportType.CustomerActivity)));
        string glyphs = page.Text.Replace(" ", string.Empty, StringComparison.Ordinal);

        // العملاء (in the title نشاط العملاء), as shaped glyphs in visual order, right
        // to left: the lam-alef ligature, formed and in place.
        Assert.Contains("\u0621\uFEFC\uFEE4\uFECC\uFEDF\u0627", glyphs, StringComparison.Ordinal);

        // أحمد محمد, a stored name, joined and right to left.
        Assert.Contains("\uFEAA\uFEE4\uFEA4\uFEE3\uFEAA\uFEE4\uFEA3\u0623", glyphs, StringComparison.Ordinal);

        // Latin and mixed values keep their own order inside the Arabic page.
        Assert.Contains("JoséGarcía", glyphs, StringComparison.Ordinal);
        Assert.Contains("5f0d1e2c-0000-4000-8000-00", glyphs, StringComparison.Ordinal);

        // Table columns run from the right: the first column (the customer id) is
        // on the right half of the page, the last (last activity) on the left.
        Word id = page.GetWords().First(word => word.Text.StartsWith("5f0d1e2c-", StringComparison.Ordinal));
        Assert.True(id.BoundingBox.Left > PageWidth / 2, $"The id column starts at {id.BoundingBox.Left}.");

        Word lastActivity = page.GetWords()
            .Where(word => word.Text == "2026-10-05")
            .OrderBy(word => word.BoundingBox.Left)
            .First();
        Assert.True(lastActivity.BoundingBox.Left < PageWidth / 4, $"The last column starts at {lastActivity.BoundingBox.Left}.");
    }

    [Fact]
    public async Task Pdf_numbers_end_at_the_left_edge_of_their_column_in_arabic_and_the_right_in_english()
    {
        // Provider cost: the calls column holds 280 (first row) and 6 (third row,
        // the stand-in model). Numbers align to the end of their column: its left
        // edge in Arabic, its right edge in English.
        Page arabic = FirstPage(await ReportRenderingTests.Render(new PdfReportRenderer(), await Arabic(ReportType.ProviderCost)));
        Page english = FirstPage(await ReportRenderingTests.Render(new PdfReportRenderer(), await ReportFixtures.BuildAsync(ReportType.ProviderCost)));

        (Word arabicFirst, Word arabicThird) = CallsColumn(arabic);
        (Word englishFirst, Word englishThird) = CallsColumn(english);

        Assert.Equal(arabicFirst.BoundingBox.Left, arabicThird.BoundingBox.Left, 1);
        Assert.NotEqual(arabicFirst.BoundingBox.Right, arabicThird.BoundingBox.Right, 1);
        Assert.Equal(englishFirst.BoundingBox.Right, englishThird.BoundingBox.Right, 1);
    }

    [Fact]
    public async Task Pdf_names_nabra_in_arabic_and_never_the_code_name()
    {
        byte[] bytes = await ReportRenderingTests.Render(new PdfReportRenderer(), await Arabic(ReportType.ProviderCost));

        using PdfDocument pdf = PdfDocument.Open(bytes);

        Assert.Equal("التكلفة التقديرية للمزوّدين", pdf.Information.Title);
        Assert.Equal("Nabra", pdf.Information.Producer);
        Assert.DoesNotContain("OmniAI", Encoding.Latin1.GetString(bytes), StringComparison.Ordinal);
        Assert.DoesNotContain("omniai", string.Concat(pdf.GetPages().Select(page => page.Text)), StringComparison.OrdinalIgnoreCase);
    }

    // ----------------------------------------------------------------- helpers

    private static Task<ReportDocument> Arabic(ReportType type) =>
        ReportFixtures.BuildAsync(type, language: UserLanguage.Arabic);

    private static Page FirstPage(byte[] bytes)
    {
        PdfDocument pdf = PdfDocument.Open(bytes);

        return pdf.GetPage(1);
    }

    private static List<Letter> Title(Page page)
    {
        double largest = page.Letters.Max(letter => letter.PointSize);

        return [.. page.Letters.Where(letter => Math.Abs(letter.PointSize - largest) < 0.01 && !string.IsNullOrWhiteSpace(letter.Value))];
    }

    private static bool HasArabic(string text) =>
        text.Any(character => character is (>= '\u0600' and <= '\u06FF') or (>= '\uFB50' and <= '\uFEFF'));

    /// <summary>The calls of the first and the third model row of the provider-cost table.</summary>
    private static (Word First, Word Third) CallsColumn(Page page)
    {
        List<Word> words = [.. page.GetWords()];

        Word first = words.Single(word => word.Text == "280");
        Word standIn = words.Single(word => word.Text == "local-stand-in");
        Word third = words
            .Where(word => word.Text == "6" && Math.Abs(word.BoundingBox.Bottom - standIn.BoundingBox.Bottom) < 1)
            .OrderBy(word => Math.Abs(word.BoundingBox.Left - first.BoundingBox.Left))
            .First();

        return (first, third);
    }
}
