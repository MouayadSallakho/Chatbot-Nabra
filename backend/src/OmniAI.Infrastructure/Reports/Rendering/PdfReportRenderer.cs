using System.Globalization;
using OmniAI.Application.Reports;
using OmniAI.Domain.Reports;
using SkiaSharp;

namespace OmniAI.Infrastructure.Reports.Rendering;

/// <summary>
/// A report as a restrained, printable PDF, in any script the report contains.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Written by Skia's PDF backend, in process.</strong> No browser, no
/// HTML, no print-to-PDF and no external process. Text is placed as shaped
/// glyphs: a value like <c>&lt;script&gt;</c> is eight glyphs on a page, because
/// nothing here interprets markup.
/// </para>
/// <para>
/// <strong>Real text, not substituted text.</strong> Every line goes through
/// <see cref="PdfText"/>: the Unicode bidirectional algorithm splits it into
/// visual runs, and HarfBuzz shapes each run with the bundled font's OpenType
/// tables — so Arabic is joined, ordered right to left, and sits correctly
/// beside Latin text, numbers and dates. Accented Latin is exact.
/// </para>
/// <para>
/// <strong>An Arabic report is laid out right to left (Phase 37.7).</strong> The
/// page is mirrored: the title, headings, notes and labels start at the right
/// margin, each label's value sits to its left, table columns run from the right,
/// text cells start at a column's right edge and numbers end at its left edge, and
/// the footer puts the title on the right and the page number on the left. The
/// report's own words are drawn as right-to-left paragraphs; every value keeps its
/// own direction, so names, e-mail addresses, ids and numbers are isolated from
/// the Arabic around them. Digits stay Western and dates ISO, as in English. An
/// English report is drawn exactly as it was before (golden files).
/// </para>
/// <para>
/// <strong>Nothing active, nothing remote.</strong> The only font is the one
/// bundled in this assembly, embedded in the file (whole, which is why a PDF is
/// about 0.75 MB before any data; Skia's Linux build does not subset). No image,
/// no link annotation,
/// no JavaScript, no form, no open action, and nothing is fetched when the
/// report is generated or opened.
/// </para>
/// </remarks>
internal sealed class PdfReportRenderer : IReportRenderer
{
    private const float PageWidth = 841.89f;
    private const float PageHeight = 595.28f;
    private const float Margin = 40;
    private const float ContentWidth = PageWidth - (2 * Margin);
    private const float Bottom = PageHeight - Margin - 24;

    private const float TitleSize = 16;
    private const float HeadingSize = 12;
    private const float BodySize = 9.5f;
    private const float SmallSize = 8;
    private const float RowHeight = 15;

    private static readonly SKColor Ink = new(0x1F, 0x23, 0x2B);
    private static readonly SKColor Muted = new(0x6E, 0x6E, 0x6E);
    private static readonly SKColor Rule = new(0x78, 0x78, 0x78);
    private static readonly SKColor Shade = new(0xF4, 0xF5, 0xF7);

    public ReportFormat Format => ReportFormat.Pdf;

    public async Task RenderAsync(ReportDocument document, Stream output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(output);

        byte[] bytes = Build(document, cancellationToken);

        await output.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    private static byte[] Build(ReportDocument document, CancellationToken cancellationToken)
    {
        using PdfText text = new();

        ReportWording words = document.Wording;
        bool rtl = words.IsRightToLeft;

        // Layout first, so the page count is known before a page is drawn: every
        // page carries "Page n of m", and m has to be right on page one.
        Layout layout = new(text, rtl);

        Compose(document, words, layout, cancellationToken);

        using MemoryStream buffer = new();

        SKDocumentPdfMetadata metadata = new()
        {
            Title = PdfText.Clean(document.Title),
            Creator = "Nabra reports",
            Producer = "Nabra",
            Creation = document.GeneratedAt.UtcDateTime,
            Modified = document.GeneratedAt.UtcDateTime,
            RasterDpi = SKDocumentPdfMetadata.DefaultRasterDpi,
            EncodingQuality = SKDocumentPdfMetadata.DefaultEncodingQuality,
        };

        using (SKDocument pdf = SKDocument.CreatePdf(buffer, metadata))
        {
            int total = layout.Pages.Count;

            for (int number = 1; number <= total; number++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                SKCanvas canvas = pdf.BeginPage(PageWidth, PageHeight);

                foreach (Action<SKCanvas> draw in layout.Pages[number - 1])
                {
                    draw(canvas);
                }

                float footer = PageHeight - Margin;
                string page = words.PageOf(number, total);

                if (rtl)
                {
                    // Mirrored: the title at the right margin, the page number at the left.
                    text.DrawInBox(canvas, document.Title, Margin, ContentWidth, footer, SmallSize, bold: false, Muted, againstRight: true, rightToLeft: true);
                    text.DrawInBox(canvas, page, Margin, ContentWidth, footer, SmallSize, bold: false, Muted, againstRight: false, rightToLeft: true);
                }
                else
                {
                    text.Draw(canvas, document.Title, Margin, footer, SmallSize, bold: false, Muted);
                    text.Draw(canvas, page, PageWidth - Margin - text.Measure(page, SmallSize, bold: false), footer, SmallSize, bold: false, Muted);
                }

                pdf.EndPage();
            }

            pdf.Close();
        }

        return buffer.ToArray();
    }

    private static void Compose(ReportDocument document, ReportWording words, Layout layout, CancellationToken cancellationToken)
    {
        layout.Text(document.Title, TitleSize, bold: true);
        layout.Gap(4);

        if (document.WorkspaceName is not null)
        {
            layout.Pair(words["Workspace"], document.WorkspaceName, bold: false);
        }

        layout.Pair(words.PeriodLabel(document.Period.TimeZone), words.PeriodRange(document.Period), bold: false);
        layout.Pair(
            words["Generated (UTC)"],
            string.Create(CultureInfo.InvariantCulture, $"{document.GeneratedAt.UtcDateTime:yyyy-MM-dd HH:mm:ss}"),
            bold: false);
        layout.Gap(10);

        layout.Heading(words["Summary"]);

        foreach (ReportFact fact in document.Summary)
        {
            layout.Pair(fact.Label, Display(fact.Value), bold: true);
        }

        foreach (ReportTable table in document.Tables)
        {
            cancellationToken.ThrowIfCancellationRequested();

            layout.Gap(12);
            layout.Table(table, words["No rows for this period."]);
        }

        layout.Gap(12);
        layout.Heading(words["Notes"]);

        foreach (string note in document.Notes)
        {
            layout.Text(note, SmallSize);
        }
    }

    private static string Display(ReportValue value) => value.Kind switch
    {
        ReportValueKind.Text => value.TextValue ?? string.Empty,
        ReportValueKind.Count => value.CountValue?.ToString("N0", CultureInfo.InvariantCulture) ?? string.Empty,
        ReportValueKind.Money => value.MoneyValue?.ToString("0.000000", CultureInfo.InvariantCulture) ?? string.Empty,
        ReportValueKind.Date => value.DateValue?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
        ReportValueKind.Instant =>
            value.InstantValue?.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? string.Empty,
        _ => string.Empty,
    };

    /// <summary>
    /// Flows content down pages, recording what to draw rather than drawing it.
    /// </summary>
    /// <remarks>
    /// Top-down coordinates, as Skia's canvas uses them. With <c>rtl</c> every
    /// horizontal position is mirrored about the page's centre; the vertical flow
    /// and the page breaks are the same in both directions.
    /// </remarks>
    private sealed class Layout
    {
        private readonly PdfText _text;
        private readonly bool _rtl;
        private float _y;

        public Layout(PdfText text, bool rtl)
        {
            _text = text;
            _rtl = rtl;
            NewPage();
        }

        public List<List<Action<SKCanvas>>> Pages { get; } = [];

        private List<Action<SKCanvas>> Current => Pages[^1];

        public void Gap(float points) => _y += points;

        public void Heading(string text)
        {
            Ensure(RowHeight * 2);
            Text(text, HeadingSize, bold: true);
            _y += 2;
        }

        public void Text(string text, float size, bool bold = false)
        {
            foreach (string line in _text.Wrap(text, size, bold, ContentWidth, _rtl))
            {
                Ensure(size + 5);

                float baseline = _y + size;

                if (_rtl)
                {
                    Current.Add(canvas => _text.DrawInBox(canvas, line, Margin, ContentWidth, baseline, size, bold, Ink, againstRight: true, rightToLeft: true));
                }
                else
                {
                    Current.Add(canvas => _text.DrawAligned(canvas, line, Margin, ContentWidth, baseline, size, bold, Ink, numeric: false));
                }

                _y += size + 5;
            }
        }

        public void Pair(string label, string value, bool bold)
        {
            Ensure(RowHeight);

            float baseline = _y + BodySize;

            if (_rtl)
            {
                // The label against the right margin; its value starts just to its left
                // and keeps its own direction.
                string rightLabel = _text.Fit(label, BodySize, bold: false, 320, rightToLeft: true);
                string leftValue = _text.Fit(value, BodySize, bold, ContentWidth - 340);

                Current.Add(canvas =>
                {
                    _text.DrawInBox(canvas, rightLabel, PageWidth - Margin - 320, 320, baseline, BodySize, bold: false, Ink, againstRight: true, rightToLeft: true);
                    _text.DrawInBox(canvas, leftValue, Margin, ContentWidth - 340, baseline, BodySize, bold, Ink, againstRight: true, rightToLeft: false);
                });

                _y += RowHeight;

                return;
            }

            string left = _text.Fit(label, BodySize, bold: false, 320);
            string right = _text.Fit(value, BodySize, bold, ContentWidth - 340);

            Current.Add(canvas =>
            {
                _text.DrawAligned(canvas, left, Margin, 320, baseline, BodySize, bold: false, Ink, numeric: false);
                _text.DrawAligned(canvas, right, Margin + 340, ContentWidth - 340, baseline, BodySize, bold, Ink, numeric: false, alignRightToLeft: false);
            });

            _y += RowHeight;
        }

        public void Table(ReportTable table, string noRows)
        {
            Heading(table.Title);

            float[] widths = ColumnWidths(table);

            Header(table, widths);

            if (table.Rows.Count == 0)
            {
                Text(noRows, BodySize);

                return;
            }

            bool shaded = false;

            foreach (IReadOnlyList<ReportValue> row in table.Rows)
            {
                if (_y + RowHeight > Bottom)
                {
                    NewPage();
                    Header(table, widths);
                }

                Cells(
                    [.. row.Select(Display)],
                    [.. row.Select(value => value.Kind != ReportValueKind.Text)],
                    widths,
                    bold: false,
                    shaded,
                    header: false);

                shaded = !shaded;
            }
        }

        private void Header(ReportTable table, float[] widths)
        {
            Ensure(RowHeight * 2);

            Cells(
                [.. table.Columns.Select(column => column.Header)],
                [.. table.Columns.Select(_ => false)],
                widths,
                bold: true,
                shaded: false,
                header: true);

            float y = _y - 3;
            Current.Add(canvas =>
            {
                using SKPaint rule = new() { Color = Rule, StrokeWidth = 0.6f, IsAntialias = true };
                canvas.DrawLine(Margin, y, Margin + ContentWidth, y, rule);
            });
        }

        private void Cells(string[] values, bool[] numeric, float[] widths, bool bold, bool shaded, bool header)
        {
            float top = _y;
            float baseline = _y + BodySize + 1;

            List<(string Text, float X, float Width, bool Numeric)> placed = [];

            // Columns run from the left in English and from the right in Arabic.
            float x = _rtl ? PageWidth - Margin : Margin;

            for (int index = 0; index < widths.Length; index++)
            {
                string value = index < values.Length ? values[index] : string.Empty;

                float cellLeft = _rtl ? x - widths[index] : x;

                // Headings are the report's own words; cells are values, each in its own direction.
                placed.Add((_text.Fit(value, BodySize, bold, widths[index] - 8, rightToLeft: _rtl && header), cellLeft + 4, widths[index] - 8, numeric.Length > index && numeric[index]));
                x = _rtl ? x - widths[index] : x + widths[index];
            }

            Current.Add(canvas =>
            {
                if (shaded)
                {
                    using SKPaint fill = new() { Color = Shade, Style = SKPaintStyle.Fill };
                    canvas.DrawRect(Margin, top, ContentWidth, RowHeight, fill);
                }

                foreach ((string text, float left, float width, bool isNumeric) in placed)
                {
                    if (_rtl)
                    {
                        // Mirrored: text from the cell's right edge, numbers to its left edge.
                        _text.DrawInBox(canvas, text, left, width, baseline, BodySize, bold, Ink, againstRight: !isNumeric, rightToLeft: header);
                    }
                    else
                    {
                        _text.DrawAligned(canvas, text, left, width, baseline, BodySize, bold, Ink, isNumeric);
                    }
                }
            });

            _y += RowHeight;
        }

        /// <summary>Proportional to each column's widest value, never below a readable minimum.</summary>
        private float[] ColumnWidths(ReportTable table)
        {
            float[] natural =
            [
                .. table.Columns.Select((column, index) =>
                    table.Rows
                        .Take(200)
                        .Select(row => index < row.Count ? _text.Measure(Display(row[index]), BodySize, bold: false) : 0)
                        .Append(_text.Measure(column.Header, BodySize, bold: true, _rtl))
                        .Max() + 10),
            ];

            float sum = natural.Sum();

            return sum <= ContentWidth
                ? [.. natural.Select(width => width + ((ContentWidth - sum) / natural.Length))]
                : [.. natural.Select(width => Math.Max(50, width * ContentWidth / sum))];
        }

        private void Ensure(float needed)
        {
            if (_y + needed > Bottom)
            {
                NewPage();
            }
        }

        private void NewPage()
        {
            Pages.Add([]);
            _y = Margin;
        }
    }
}
