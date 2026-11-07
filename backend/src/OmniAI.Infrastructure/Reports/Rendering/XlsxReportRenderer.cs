using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using OmniAI.Application.Reports;
using OmniAI.Domain.Reports;

namespace OmniAI.Infrastructure.Reports.Rendering;

/// <summary>
/// A report as a real Office Open XML workbook.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Written with Microsoft's OpenXML SDK, in process.</strong> No Excel,
/// no COM, no LibreOffice and no external process: the workbook is a ZIP of XML
/// parts this renderer writes itself, and nothing is executed to produce it.
/// </para>
/// <para>
/// <strong>No formula can come from workspace data.</strong> Every text value is
/// written as an inline string cell, which a spreadsheet displays verbatim; this
/// renderer never writes a formula element at all, so a customer named
/// <c>=HYPERLINK(...)</c> is a customer with an odd name, not a link. There are
/// no macros, no external links and no data connections.
/// </para>
/// <para>
/// <strong>Numbers are numbers.</strong> Counts and money are numeric cells, dates
/// are real dates with a fixed <c>yyyy-mm-dd</c> format, and every money column
/// sits beside the currency it is in. Header rows are bold and frozen, columns are
/// sized to their content within limits.
/// </para>
/// </remarks>
internal sealed class XlsxReportRenderer : IReportRenderer
{
    private const uint StyleDefault = 0;
    private const uint StyleHeader = 1;
    private const uint StyleDate = 2;
    private const uint StyleInstant = 3;
    private const uint StyleMoney = 4;
    private const uint StyleTitle = 5;
    private const uint StyleCount = 6;

    public ReportFormat Format => ReportFormat.Xlsx;

    public async Task RenderAsync(ReportDocument document, Stream output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(output);

        // The packaging layer wants a seekable stream; the artifact store's is a
        // forward-only one that counts and hashes. The workbook is bounded by the
        // export row limit, so building it in memory first is safe.
        using MemoryStream buffer = new();

        using (SpreadsheetDocument workbook = SpreadsheetDocument.Create(buffer, SpreadsheetDocumentType.Workbook))
        {
            WorkbookPart part = workbook.AddWorkbookPart();
            part.Workbook = new Workbook();

            WorkbookStylesPart styles = part.AddNewPart<WorkbookStylesPart>();
            styles.Stylesheet = Stylesheet();

            Sheets sheets = part.Workbook.AppendChild(new Sheets());

            uint sheetId = 1;

            AddSheet(part, sheets, sheetId++, "Summary", SummaryRows(document), [34, 60], frozen: false);

            foreach (ReportTable table in document.Tables)
            {
                cancellationToken.ThrowIfCancellationRequested();

                AddSheet(
                    part,
                    sheets,
                    sheetId++,
                    SheetName(table.Name),
                    TableRows(table),
                    Widths(table),
                    frozen: true);
            }

            part.Workbook.Save();
        }

        buffer.Position = 0;

        await buffer.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
    }

    private static IEnumerable<Row> SummaryRows(ReportDocument document)
    {
        uint index = 1;

        yield return RowOf(index++, TextCell(document.Title, StyleTitle));

        if (document.WorkspaceName is not null)
        {
            yield return RowOf(index++, TextCell("Workspace", StyleHeader), TextCell(document.WorkspaceName));
        }

        yield return RowOf(index++, TextCell($"Period start ({document.Period.TimeZone})", StyleHeader), DateCell(document.Period.FromDate));
        yield return RowOf(index++, TextCell($"Period end ({document.Period.TimeZone}, inclusive)", StyleHeader), DateCell(document.Period.LastDate));
        yield return RowOf(index++, TextCell("Generated (UTC)", StyleHeader), InstantCell(document.GeneratedAt));

        index++;

        foreach (ReportFact fact in document.Summary)
        {
            yield return RowOf(index++, TextCell(fact.Label, StyleHeader), ValueCell(fact.Value));
        }

        index++;

        foreach (string note in document.Notes)
        {
            yield return RowOf(index++, TextCell(note));
        }
    }

    private static IEnumerable<Row> TableRows(ReportTable table)
    {
        uint index = 1;

        yield return RowOf(index++, [.. table.Columns.Select(column => TextCell(column.Header, StyleHeader))]);

        foreach (IReadOnlyList<ReportValue> row in table.Rows)
        {
            yield return RowOf(index++, [.. row.Select(ValueCell)]);
        }
    }

    private static void AddSheet(
        WorkbookPart workbook,
        Sheets sheets,
        uint sheetId,
        string name,
        IEnumerable<Row> rows,
        IReadOnlyList<double> widths,
        bool frozen)
    {
        WorksheetPart part = workbook.AddNewPart<WorksheetPart>();

        Worksheet worksheet = new();

        if (frozen)
        {
            worksheet.Append(new SheetViews(new SheetView(
                new Pane
                {
                    VerticalSplit = 1D,
                    TopLeftCell = "A2",
                    ActivePane = PaneValues.BottomLeft,
                    State = PaneStateValues.Frozen,
                })
            {
                WorkbookViewId = 0U,
            }));
        }

        worksheet.Append(new Columns(widths.Select((width, index) => new Column
        {
            Min = (uint)(index + 1),
            Max = (uint)(index + 1),
            Width = width,
            CustomWidth = true,
        })));

        SheetData data = new();

        foreach (Row row in rows)
        {
            data.Append(row);
        }

        worksheet.Append(data);
        part.Worksheet = worksheet;

        sheets.Append(new Sheet
        {
            Id = workbook.GetIdOfPart(part),
            SheetId = sheetId,
            Name = name,
        });
    }

    private static Row RowOf(uint index, params Cell[] cells)
    {
        Row row = new() { RowIndex = index };

        for (int column = 0; column < cells.Length; column++)
        {
            cells[column].CellReference = ColumnName(column) + index.ToString(CultureInfo.InvariantCulture);
            row.Append(cells[column]);
        }

        return row;
    }

    private static Cell ValueCell(ReportValue value) => value.Kind switch
    {
        ReportValueKind.Text => TextCell(value.TextValue),
        ReportValueKind.Count when value.CountValue is long count => NumberCell(count, StyleCount),
        ReportValueKind.Money when value.MoneyValue is decimal money => NumberCell(money, StyleMoney),
        ReportValueKind.Date when value.DateValue is DateOnly date => DateCell(date),
        ReportValueKind.Instant when value.InstantValue is DateTimeOffset instant => InstantCell(instant),
        _ => TextCell(null),
    };

    /// <summary>
    /// An inline string: displayed exactly as written, never evaluated.
    /// </summary>
    private static Cell TextCell(string? text, uint style = StyleDefault) =>
        new()
        {
            DataType = CellValues.InlineString,
            StyleIndex = style,
            InlineString = new InlineString(new Text(Clean(text)) { Space = SpaceProcessingModeValues.Preserve }),
        };

    private static Cell NumberCell(decimal value, uint style) =>
        new()
        {
            DataType = CellValues.Number,
            StyleIndex = style,
            CellValue = new CellValue(value),
        };

    private static Cell DateCell(DateOnly date) =>
        NumberCell((decimal)date.ToDateTime(TimeOnly.MinValue).ToOADate(), StyleDate);

    private static Cell InstantCell(DateTimeOffset instant) =>
        NumberCell(Math.Round((decimal)instant.UtcDateTime.ToOADate(), 8), StyleInstant);

    /// <summary>
    /// Text XML can carry.
    /// </summary>
    /// <remarks>
    /// A label may contain a control character XML 1.0 forbids; the workbook would
    /// be corrupt rather than merely odd, so such characters are dropped.
    /// </remarks>
    private static string Clean(string? text) =>
        string.IsNullOrEmpty(text)
            ? string.Empty
            : new string([.. text.Where(character => character is '\t' or '\n' or '\r' || !char.IsControl(character))]);

    /// <summary>Content-based widths, within bounds so one long label cannot make a sheet unreadable.</summary>
    private static double[] Widths(ReportTable table) =>
    [
        .. table.Columns.Select((column, index) =>
        {
            int widest = table.Rows
                .Select(row => index < row.Count ? Measure(row[index]) : 0)
                .Append(column.Header.Length)
                .Max();

            return Math.Clamp(widest + 2, 10, 60);
        }),
    ];

    private static int Measure(ReportValue value) => value.Kind switch
    {
        ReportValueKind.Text => value.TextValue?.Length ?? 0,
        ReportValueKind.Instant => 19,
        ReportValueKind.Date => 10,
        ReportValueKind.Money => 14,
        _ => 12,
    };

    /// <summary>Sheet names are fixed by the builder; this only enforces Excel's own rules.</summary>
    private static string SheetName(string name)
    {
        string cleaned = new([.. name.Where(character => "[]:*?/\\".IndexOf(character, StringComparison.Ordinal) < 0)]);

        return cleaned.Length > 31 ? cleaned[..31] : cleaned;
    }

    private static string ColumnName(int index)
    {
        string name = string.Empty;

        for (int remaining = index + 1; remaining > 0; remaining = (remaining - 1) / 26)
        {
            name = (char)('A' + ((remaining - 1) % 26)) + name;
        }

        return name;
    }

    private static Stylesheet Stylesheet() =>
        new(
            new NumberingFormats(
                new NumberingFormat { NumberFormatId = 164U, FormatCode = "yyyy-mm-dd" },
                new NumberingFormat { NumberFormatId = 165U, FormatCode = "yyyy-mm-dd hh:mm:ss" },
                new NumberingFormat { NumberFormatId = 166U, FormatCode = "0.000000" })
            {
                Count = 3U,
            },
            new Fonts(
                new Font(new FontSize { Val = 11D }),
                new Font(new Bold(), new FontSize { Val = 11D }),
                new Font(new Bold(), new FontSize { Val = 14D }))
            {
                Count = 3U,
            },
            new Fills(
                new Fill(new PatternFill { PatternType = PatternValues.None }),
                new Fill(new PatternFill { PatternType = PatternValues.Gray125 }))
            {
                Count = 2U,
            },
            new Borders(new Border()) { Count = 1U },
            new CellFormats(
                new CellFormat(),
                new CellFormat { FontId = 1U, ApplyFont = true },
                new CellFormat { NumberFormatId = 164U, ApplyNumberFormat = true },
                new CellFormat { NumberFormatId = 165U, ApplyNumberFormat = true },
                new CellFormat { NumberFormatId = 166U, ApplyNumberFormat = true },
                new CellFormat { FontId = 2U, ApplyFont = true },
                new CellFormat { NumberFormatId = 1U, ApplyNumberFormat = true })
            {
                Count = 7U,
            });
}
