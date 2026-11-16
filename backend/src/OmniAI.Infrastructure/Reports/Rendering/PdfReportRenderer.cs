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
/// the Arabic around them. Digits stay Western and dates ISO, as in English.
/// </para>
/// <para>
/// <strong>The brand's type and tables sized by content (Phase 39.7b).</strong>
/// Latin text and digits are set in Geist, Arabic in IBM Plex Sans Arabic, titles
/// and headings in each family's 600 weight, with tabular figures (see
/// <see cref="PdfText"/>). A table's columns are as wide as their content and the
/// table starts at its reader's margin; a column heading wraps rather than being
/// cut; and a number column's heading aligns with its numbers, at the column's
/// end (its left edge in Arabic).
/// </para>
/// <para>
/// <strong>The brand, small and quiet (Phase 39.8a).</strong> Every page's
/// footer starts with the mark, the two circles drawn as vectors in their
/// light-ground colours, and the product's name in the reader's language
/// ("Nabra", or the Arabic name), before the title; at the left margin in
/// English and the right one in Arabic. The mark is a logo: it is never
/// mirrored, so its emerald circle stays on the left in both.
/// </para>
/// <para>
/// <strong>Nothing active, nothing remote.</strong> The only fonts are the ones
/// bundled in this assembly, embedded in the file (only those a page uses). No
/// image (the mark is paths), no link annotation,
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
    private const float RowHeight = 16;
    private const float RowLift = 1;
    private const float HeaderLine = 12.5f;
    private const float CellPadding = 5;
    private const float MinimumTextColumn = 60;

    private static readonly SKColor Ink = new(0x1F, 0x23, 0x2B);
    private static readonly SKColor Muted = new(0x6E, 0x6E, 0x6E);
    private static readonly SKColor Rule = new(0x78, 0x78, 0x78);
    private static readonly SKColor Shade = new(0xF4, 0xF5, 0xF7);

    // The mark (brand.md): circles r 40 at (52, 60) and (88, 60) in a 140 x 120
    // box, their overlap the one tone; light-ground colours.
    private const float MarkBoxWidth = 140;
    private const float MarkBoxHeight = 120;
    private const string MarkLens = "M70 24.28 A40 40 0 0 1 70 95.72 A40 40 0 0 1 70 24.28 Z";
    private static readonly SKColor MarkAi = new(0x23, 0x53, 0x47);
    private static readonly SKColor MarkTeam = new(0xC8, 0xA9, 0x6A);
    private static readonly SKColor MarkLensColour = new(0x0B, 0x2B, 0x26);

    // In the footer: the mark's box is 1.2 times the footer's type (brand.md's
    // lockup rule), then the name, then the title.
    internal const float FooterMarkHeight = SmallSize * 1.2f;
    internal const float FooterMarkWidth = FooterMarkHeight * MarkBoxWidth / MarkBoxHeight;
    private const float FooterNameGap = 3;
    private const float FooterTitleGap = 10;

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
                string product = words.Product;
                float name = text.Measure(product, SmallSize, bold: false, rightToLeft: rtl);
                float lead = FooterMarkWidth + FooterNameGap + name + FooterTitleGap;

                if (rtl)
                {
                    // Mirrored: the mark and the name at the right margin, then the
                    // title to their left, the page number at the left margin.
                    DrawMark(canvas, PageWidth - Margin - FooterMarkWidth, footer);
                    text.DrawInBox(canvas, product, Margin, ContentWidth - FooterMarkWidth - FooterNameGap, footer, SmallSize, bold: false, Muted, againstRight: true, rightToLeft: true);
                    text.DrawInBox(canvas, document.Title, Margin, ContentWidth - lead, footer, SmallSize, bold: false, Muted, againstRight: true, rightToLeft: true);
                    text.DrawInBox(canvas, page, Margin, ContentWidth, footer, SmallSize, bold: false, Muted, againstRight: false, rightToLeft: true);
                }
                else
                {
                    DrawMark(canvas, Margin, footer);
                    text.Draw(canvas, product, Margin + FooterMarkWidth + FooterNameGap, footer, SmallSize, bold: false, Muted);
                    text.Draw(canvas, document.Title, Margin + lead, footer, SmallSize, bold: false, Muted);
                    text.Draw(canvas, page, PageWidth - Margin - text.Measure(page, SmallSize, bold: false), footer, SmallSize, bold: false, Muted);
                }

                pdf.EndPage();
            }

            pdf.Close();
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// The mark as vector paths, its box <see cref="FooterMarkWidth"/> wide with
    /// its left edge at <paramref name="left"/>, its circles centred on the
    /// footer's x-height so it sits with the text beside it.
    /// </summary>
    internal static void DrawMark(SKCanvas canvas, float left, float baseline)
    {
        float scale = FooterMarkHeight / MarkBoxHeight;
        float middle = baseline - (SmallSize * 0.36f);

        canvas.Save();
        canvas.Translate(left, middle - (60 * scale));
        canvas.Scale(scale);

        using SKPaint paint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };

        paint.Color = MarkAi;
        canvas.DrawCircle(52, 60, 40, paint);
        paint.Color = MarkTeam;
        canvas.DrawCircle(88, 60, 40, paint);

        using SKPath lens = SKPath.ParseSvgPathData(MarkLens)
            ?? throw new InvalidOperationException("The mark's lens path could not be parsed.");

        paint.Color = MarkLensColour;
        canvas.DrawPath(lens, paint);
        canvas.Restore();
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

        /// <summary>
        /// The distance from one line to the next. Arabic letters reach higher and
        /// lower than Latin ones, so an Arabic report's lines get room in
        /// proportion to their size.
        /// </summary>
        public float Advance(float size) => _rtl ? Math.Max(size + 5, size * 1.6f) : size + 5;

        public void Heading(string text)
        {
            Ensure(Advance(HeadingSize) + RowHeight);
            Text(text, HeadingSize, bold: true);
            _y += 2;
        }

        public void Text(string text, float size, bool bold = false)
        {
            foreach (string line in _text.Wrap(text, size, bold, ContentWidth, _rtl))
            {
                float advance = Advance(size);

                Ensure(advance);

                // Any extra room goes above the letters, where Arabic needs it.
                float baseline = _y + advance - 5;

                if (_rtl)
                {
                    Current.Add(canvas => _text.DrawInBox(canvas, line, Margin, ContentWidth, baseline, size, bold, Ink, againstRight: true, rightToLeft: true));
                }
                else
                {
                    Current.Add(canvas => _text.DrawAligned(canvas, line, Margin, ContentWidth, baseline, size, bold, Ink, numeric: false));
                }

                _y += advance;
            }
        }

        public void Pair(string label, string value, bool bold)
        {
            Ensure(RowHeight);

            float baseline = _y + BodySize + RowLift;

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
            // Sized before the title is placed, so a title never ends a page
            // without its headings and first row under it.
            Column[] columns = Columns(table);
            float headerHeight = HeaderHeight(columns);

            Ensure(Advance(HeadingSize) + 2 + headerHeight + RowHeight);
            Heading(table.Title);

            Header(columns, headerHeight);

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
                    Header(columns, headerHeight);
                }

                Cells(columns, [.. row.Select(Display)], shaded);

                shaded = !shaded;
            }
        }

        private static float HeaderHeight(Column[] columns) =>
            (columns.Max(column => column.Heading.Length) * HeaderLine) + 5;

        /// <summary>
        /// The headings, wrapped inside their columns and set on a shared last line.
        /// </summary>
        /// <remarks>
        /// A heading is never cut: it wraps at its spaces, its column is never
        /// narrower than its longest word, and every line of it aligns as the
        /// column's values do: a text column's at its start, a number column's at
        /// its end, over the numbers (in Arabic both mirrored, so numbers and their
        /// heading end at the column's left edge).
        /// </remarks>
        private void Header(Column[] columns, float height)
        {
            Ensure(height + RowHeight);

            float top = _y;
            int lines = columns.Max(column => column.Heading.Length);
            (float tableLeft, float tableWidth) = Extent(columns);

            List<(string Text, float Left, float Width, float Baseline, bool Numeric)> placed = [];

            foreach (Column column in columns)
            {
                // Bottom-aligned: a one-line heading sits on the last line of a wrapped one.
                int offset = lines - column.Heading.Length;

                for (int line = 0; line < column.Heading.Length; line++)
                {
                    float baseline = top + ((offset + line) * HeaderLine) + BodySize + 1;
                    placed.Add((column.Heading[line], column.Left + CellPadding, column.Width - (2 * CellPadding), baseline, column.Numeric));
                }
            }

            float rule = top + height - 2;

            Current.Add(canvas =>
            {
                foreach ((string text, float left, float width, float baseline, bool numeric) in placed)
                {
                    if (_rtl)
                    {
                        _text.DrawInBox(canvas, text, left, width, baseline, BodySize, bold: true, Ink, againstRight: !numeric, rightToLeft: true);
                    }
                    else
                    {
                        _text.DrawAligned(canvas, text, left, width, baseline, BodySize, bold: true, Ink, numeric, alignRightToLeft: false);
                    }
                }

                using SKPaint paint = new() { Color = Rule, StrokeWidth = 0.6f, IsAntialias = true };
                canvas.DrawLine(tableLeft, rule, tableLeft + tableWidth, rule, paint);
            });

            _y += height;
        }

        private void Cells(Column[] columns, string[] values, bool shaded)
        {
            float top = _y;
            float baseline = _y + BodySize + 1 + RowLift;
            (float tableLeft, float tableWidth) = Extent(columns);

            List<(string Text, float Left, float Width, bool Numeric)> placed = [];

            for (int index = 0; index < columns.Length; index++)
            {
                Column column = columns[index];
                string value = index < values.Length ? values[index] : string.Empty;
                float inner = column.Width - (2 * CellPadding);

                placed.Add((_text.Fit(value, BodySize, bold: false, inner), column.Left + CellPadding, inner, column.Numeric));
            }

            Current.Add(canvas =>
            {
                if (shaded)
                {
                    using SKPaint fill = new() { Color = Shade, Style = SKPaintStyle.Fill };
                    canvas.DrawRect(tableLeft, top, tableWidth, RowHeight, fill);
                }

                foreach ((string text, float left, float width, bool isNumeric) in placed)
                {
                    if (_rtl)
                    {
                        // Mirrored: text from the cell's right edge, numbers to its left edge;
                        // every value keeps its own direction.
                        _text.DrawInBox(canvas, text, left, width, baseline, BodySize, bold: false, Ink, againstRight: !isNumeric, rightToLeft: false);
                    }
                    else
                    {
                        _text.DrawAligned(canvas, text, left, width, baseline, BodySize, bold: false, Ink, isNumeric);
                    }
                }
            });

            _y += RowHeight;
        }

        private static (float Left, float Width) Extent(Column[] columns)
        {
            float left = columns.Min(column => column.Left);

            return (left, columns.Max(column => column.Left + column.Width) - left);
        }

        /// <summary>
        /// Each column as wide as its content, the table no wider than the page.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A column wants its widest value (of the first 200 rows) or its heading on
        /// one line, whichever is wider, plus padding. The table is then as wide as
        /// its columns and starts at the margin its reader starts from (the left in
        /// English, the right in Arabic), rather than being stretched across the
        /// page with the spare width spread over every column.
        /// </para>
        /// <para>
        /// When the columns want more than the page, headings wrap first: each
        /// column keeps at least its widest value and its heading's longest word,
        /// and the width left over is shared in proportion to how much each heading
        /// still wants. Only when even that does not fit do the widest text columns
        /// give up width (the narrow ones keep theirs), never below a readable
        /// minimum, and their longest values end with an ellipsis. A number is
        /// never shortened.
        /// </para>
        /// </remarks>
        private Column[] Columns(ReportTable table)
        {
            int count = table.Columns.Count;
            float[] need = new float[count];
            float[] want = new float[count];
            bool[] numeric = new bool[count];

            for (int index = 0; index < count; index++)
            {
                ReportColumn column = table.Columns[index];
                int at = index;

                numeric[index] = column.Kind != ReportValueKind.Text;

                float value = table.Rows
                    .Take(200)
                    .Select(row => at < row.Count ? _text.Measure(Display(row[at]), BodySize, bold: false) : 0)
                    .DefaultIfEmpty(0)
                    .Max();

                float word = _text.WidestWord(column.Header, BodySize, bold: true, _rtl);
                float whole = _text.Measure(column.Header, BodySize, bold: true, _rtl);

                // Half a point of room so a measured line is never refitted with an ellipsis.
                need[index] = Math.Max(value, word) + (2 * CellPadding) + 0.5f;
                want[index] = Math.Max(value, whole) + (2 * CellPadding) + 0.5f;
            }

            float[] widths;

            if (want.Sum() <= ContentWidth)
            {
                widths = want;
            }
            else if (need.Sum() <= ContentWidth)
            {
                float spare = ContentWidth - need.Sum();
                float wanting = want.Sum() - need.Sum();

                widths = [.. need.Select((width, index) => width + (spare * (want[index] - width) / wanting))];
            }
            else
            {
                // Numbers keep their width. Text columns share what is left by a
                // water level: the narrow ones keep their whole width and only the
                // widest are shortened, to the same width, never below the minimum.
                float numbers = need.Where((_, index) => numeric[index]).Sum();
                float room = Math.Max(0, ContentWidth - numbers);
                float level = Level([.. need.Where((_, index) => !numeric[index])], room);

                widths = [.. need.Select((width, index) => numeric[index] ? width : Math.Max(MinimumTextColumn, Math.Min(width, level)))];

                float total = widths.Sum();

                if (total > ContentWidth)
                {
                    widths = [.. widths.Select(width => width * ContentWidth / total)];
                }
            }

            Column[] columns = new Column[count];
            float x = _rtl ? PageWidth - Margin : Margin;

            for (int index = 0; index < count; index++)
            {
                float left = _rtl ? x - widths[index] : x;
                string[] heading = [.. _text.Wrap(table.Columns[index].Header, BodySize, bold: true, widths[index] - (2 * CellPadding), _rtl)];

                columns[index] = new Column(left, widths[index], numeric[index], heading);
                x = _rtl ? x - widths[index] : x + widths[index];
            }

            return columns;
        }

        /// <summary>The largest width <c>L</c> for which the columns, each capped at <c>L</c>, fit in <paramref name="room"/>.</summary>
        private static float Level(float[] widths, float room)
        {
            float[] sorted = [.. widths.Order()];
            float used = 0;

            for (int index = 0; index < sorted.Length; index++)
            {
                int remaining = sorted.Length - index;

                if (used + (sorted[index] * remaining) > room)
                {
                    return (room - used) / remaining;
                }

                used += sorted[index];
            }

            return sorted.Length == 0 ? room : sorted[^1];
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

        /// <summary>One table column as laid out: its box, its alignment and its heading's lines.</summary>
        private sealed record Column(float Left, float Width, bool Numeric, string[] Heading);
    }
}
