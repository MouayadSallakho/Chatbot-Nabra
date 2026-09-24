using System.IO.Compression;
using System.Text;
using CsvHelper;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using OmniAI.Application.Reports;
using OmniAI.Domain.Reports;
using OmniAI.Infrastructure.Reports.Rendering;
using UglyToad.PdfPig;

namespace OmniAI.Infrastructure.IntegrationTests.Reports;

/// <summary>
/// Real files, opened by real parsers.
/// </summary>
/// <remarks>
/// A renderer that produced something with the right extension proves nothing.
/// Each format here is read back by an independent parser — CsvHelper, the OpenXML
/// SDK, PdfPig — and the security properties are asserted on what those parsers
/// see: no formula cell, no evaluated prefix, no script, no active PDF content.
/// </remarks>
public sealed class ReportRenderingTests
{
    /// <summary>The synthetic values every spreadsheet must treat as plain text.</summary>
    public static readonly string[] Hostile =
    [
        "=HYPERLINK(\"https://evil.example/\",\"click\")",
        "+1+1",
        "-1+1",
        "@SUM(A1:A2)",
        "<script>alert(1)</script>",
        "\tTabbed",
        "Plain, with \"quotes\" and a comma",
    ];

    [Fact]
    public async Task Csv_is_utf8_with_bom_and_parses_back_to_the_rows()
    {
        byte[] bytes = await Render(new CsvReportRenderer(), Document());

        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);

        List<string[]> rows = ReadCsv(bytes);

        Assert.Equal(["Customer", "Messages", "Cost", "Day", "Last activity (UTC)"], rows[0]);
        Assert.Equal(Hostile.Length + 2, rows.Count);
        Assert.Equal("Añyā بدر", rows[^1][0]);
        Assert.Equal("2026-09-01", rows[1][3]);
        Assert.Equal("2026-09-02T08:30:00Z", rows[1][4]);
    }

    [Fact]
    public async Task Csv_neutralises_every_spreadsheet_formula_prefix()
    {
        List<string[]> rows = ReadCsv(await Render(new CsvReportRenderer(), Document()));

        Dictionary<string, string> written = Hostile
            .Select((value, index) => (value, rows[index + 1][0]))
            .ToDictionary(pair => pair.value, pair => pair.Item2);

        Assert.Equal("'=HYPERLINK(\"https://evil.example/\",\"click\")", written[Hostile[0]]);
        Assert.Equal("'+1+1", written["+1+1"]);
        Assert.Equal("'-1+1", written["-1+1"]);
        Assert.Equal("'@SUM(A1:A2)", written["@SUM(A1:A2)"]);
        Assert.Equal("'\tTabbed", written["\tTabbed"]);

        // Markup is not a spreadsheet risk, and is left exactly as it was: plain text.
        Assert.Equal("<script>alert(1)</script>", written["<script>alert(1)</script>"]);
        Assert.Equal("Plain, with \"quotes\" and a comma", written["Plain, with \"quotes\" and a comma"]);

        foreach (string[] row in rows.Skip(1))
        {
            Assert.False(row[0].Length > 0 && row[0][0] is '=' or '+' or '-' or '@', row[0]);
        }
    }

    [Fact]
    public async Task Xlsx_is_a_real_workbook_with_named_sheets_and_a_frozen_header()
    {
        byte[] bytes = await Render(new XlsxReportRenderer(), Document());

        // A ZIP container, not a renamed CSV.
        Assert.Equal((byte)'P', bytes[0]);
        Assert.Equal((byte)'K', bytes[1]);

        using MemoryStream stream = new(bytes);
        using SpreadsheetDocument workbook = SpreadsheetDocument.Open(stream, isEditable: false);

        List<string> names = [.. workbook.WorkbookPart!.Workbook!.Sheets!.Elements<Sheet>().Select(sheet => sheet.Name!.Value!)];

        Assert.Equal(["Summary", "Customers"], names);

        Worksheet customers = Sheet(workbook, "Customers");

        Pane pane = customers.Descendants<Pane>().Single();
        Assert.Equal(1D, pane.VerticalSplit!.Value);
        Assert.Equal(PaneStateValues.Frozen, pane.State!.Value);

        List<Row> rows = [.. customers.Descendants<Row>()];
        Assert.Equal(Hostile.Length + 2, rows.Count);

        // Numbers are numbers; money sits beside nothing but its own number format.
        Cell messages = rows[1].Elements<Cell>().ElementAt(1);
        Assert.Equal(CellValues.Number, messages.DataType!.Value);
        Assert.Equal("7", messages.CellValue!.Text);

        Cell money = rows[1].Elements<Cell>().ElementAt(2);
        Assert.Equal(CellValues.Number, money.DataType!.Value);
        Assert.Equal(1.234567m, decimal.Parse(money.CellValue!.Text, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Xlsx_writes_hostile_text_as_literal_text_and_contains_no_formula_or_macro()
    {
        byte[] bytes = await Render(new XlsxReportRenderer(), Document());

        using (MemoryStream stream = new(bytes))
        using (SpreadsheetDocument workbook = SpreadsheetDocument.Open(stream, isEditable: false))
        {
            Assert.Empty(workbook.WorkbookPart!.WorksheetParts.SelectMany(part => part.Worksheet!.Descendants<CellFormula>()));
            Assert.Null(workbook.WorkbookPart.VbaProjectPart);
            Assert.Empty(workbook.WorkbookPart.ExternalWorkbookParts);

            List<Cell> first = [.. Sheet(workbook, "Customers").Descendants<Row>().Skip(1).Select(row => row.Elements<Cell>().First())];

            for (int index = 0; index < Hostile.Length; index++)
            {
                Assert.Equal(CellValues.InlineString, first[index].DataType!.Value);
                Assert.Equal(Hostile[index], first[index].InlineString!.InnerText);
            }
        }

        // And at the byte level: not a single <f> element anywhere in the package.
        using ZipArchive zip = new(new MemoryStream(bytes), ZipArchiveMode.Read);

        foreach (ZipArchiveEntry entry in zip.Entries.Where(entry => entry.FullName.EndsWith(".xml", StringComparison.Ordinal)))
        {
            using StreamReader reader = new(entry.Open());
            string xml = await reader.ReadToEndAsync();

            Assert.DoesNotContain("<x:f>", xml, StringComparison.Ordinal);
            Assert.DoesNotContain("<f>", xml, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(zip.Entries, entry => entry.FullName.Contains("vbaProject", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Pdf_is_a_real_document_with_the_title_period_and_page_numbers()
    {
        byte[] bytes = await Render(new PdfReportRenderer(), Document(rows: 120));

        Assert.Equal("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));

        using PdfDocument pdf = PdfDocument.Open(bytes);

        Assert.True(pdf.NumberOfPages >= 2);

        string first = pdf.GetPage(1).Text;

        Assert.Contains("Customer activity", first, StringComparison.Ordinal);
        Assert.Contains("2026-09-01", first, StringComparison.Ordinal);
        Assert.Contains("Generated", first, StringComparison.Ordinal);
        Assert.Contains($"Page 1 of {pdf.NumberOfPages}", first, StringComparison.Ordinal);
        Assert.Contains($"Page {pdf.NumberOfPages} of {pdf.NumberOfPages}", pdf.GetPage(pdf.NumberOfPages).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pdf_renders_markup_as_text_and_carries_no_active_or_remote_content()
    {
        byte[] bytes = await Render(new PdfReportRenderer(), Document());

        using PdfDocument pdf = PdfDocument.Open(bytes);

        string text = string.Concat(pdf.GetPages().Select(page => page.Text));

        Assert.Contains("<script>alert(1)</script>", text, StringComparison.Ordinal);

        string raw = Encoding.Latin1.GetString(bytes);

        foreach (string forbidden in new[] { "/JavaScript", "/JS ", "/OpenAction", "/AA ", "/Launch", "/URI", "/SubmitForm", "/EmbeddedFile", "/Annots", "http://", "https://" })
        {
            Assert.DoesNotContain(forbidden, raw.Replace("https://evil.example/", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        }

        // The only font is the bundled one, embedded; nothing refers outside the file.
        Assert.Matches("/FontFile[23]?", raw);
        Assert.Empty(pdf.GetPages().SelectMany(page => page.GetImages()));
    }

    [Fact]
    public async Task Pdf_keeps_non_latin_labels_exactly()
    {
        byte[] bytes = await Render(new PdfReportRenderer(), Document());

        using PdfDocument pdf = PdfDocument.Open(bytes);

        string text = string.Concat(pdf.GetPages().Select(page => page.Text));

        // Accents and Arabic survive; nothing is replaced with a placeholder.
        Assert.Contains("Añyā", text, StringComparison.Ordinal);

        // بدر comes back as its joined forms in visual order: reh isolated (dal never joins
        // leftwards), dal final, beh initial.
        Assert.Contains("\u0631\uFEAA\uFE91", text.Replace(" ", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);

        Assert.DoesNotContain("A?y?", text, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- helpers

    internal static ReportDocument Document(int rows = 0)
    {
        List<IReadOnlyList<ReportValue>> body =
        [
            .. Hostile.Select(value => Row(value)),
            .. Enumerable.Range(0, rows).Select(index => Row($"Customer {index}")),
            Row("Añyā بدر"),
        ];

        return new ReportDocument(
            ReportType.CustomerActivity,
            "Customer activity",
            "Acme <b>Trading</b>",
            ReportPeriod.Create(new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1)),
            new DateTimeOffset(2026, 10, 1, 6, 0, 0, TimeSpan.Zero),
            [new ReportFact("Active customers", ReportValue.Count(body.Count))],
            [
                new ReportTable(
                    "Customers",
                    "Active customers",
                    [
                        new ReportColumn("Customer", ReportValueKind.Text),
                        new ReportColumn("Messages", ReportValueKind.Count),
                        new ReportColumn("Cost", ReportValueKind.Money),
                        new ReportColumn("Day", ReportValueKind.Date),
                        new ReportColumn("Last activity (UTC)", ReportValueKind.Instant),
                    ],
                    body),
            ],
            ["Period: 2026-09-01 to 2026-09-30 inclusive, in UTC whole days."]);
    }

    private static ReportValue[] Row(string label) =>
    [
        ReportValue.Text(label),
        ReportValue.Count(7),
        ReportValue.Money(1.234567m),
        ReportValue.Date(new DateOnly(2026, 9, 1)),
        ReportValue.Instant(new DateTimeOffset(2026, 9, 2, 8, 30, 0, TimeSpan.Zero)),
    ];

    internal static async Task<byte[]> Render(IReportRenderer renderer, ReportDocument document)
    {
        using MemoryStream output = new();

        await renderer.RenderAsync(document, output);

        return output.ToArray();
    }

    internal static List<string[]> ReadCsv(byte[] bytes)
    {
        using StreamReader reader = new(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        using CsvParser parser = new(reader, System.Globalization.CultureInfo.InvariantCulture);

        List<string[]> rows = [];

        while (parser.Read())
        {
            rows.Add(parser.Record!);
        }

        return rows;
    }

    private static Worksheet Sheet(SpreadsheetDocument workbook, string name)
    {
        Sheet sheet = workbook.WorkbookPart!.Workbook!.Sheets!.Elements<Sheet>().Single(candidate => candidate.Name == name);

        return ((WorksheetPart)workbook.WorkbookPart.GetPartById(sheet.Id!.Value!)).Worksheet!;
    }
}
