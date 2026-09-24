using System.Globalization;
using System.Reflection;
using System.Text;
using SkiaSharp;
using SkiaSharp.HarfBuzz;
using Unicode.Bidi;

namespace OmniAI.Infrastructure.Reports.Rendering;

/// <summary>
/// Measures and draws one line of report text, correctly, in any script the
/// bundled font covers.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Two standard algorithms, neither of them written here.</strong> The
/// Unicode bidirectional algorithm (UAX #9, via <c>Unicode.Bidi</c>) splits the
/// line into runs in visual order, each with its embedding level. HarfBuzz then
/// shapes each run in its own direction using the font's OpenType tables, which
/// is where Arabic joining, the lam-alef ligature, mark placement and Latin
/// kerning come from. Nothing reverses a string and nothing picks letter forms by
/// hand.
/// </para>
/// <para>
/// <strong>The text travels with the glyphs.</strong> Each run is written with
/// its UTF-8 source and cluster map, so the PDF's text layer says what the
/// report says: it can be searched, copied and read by assistive technology,
/// rather than being a picture of letters.
/// </para>
/// <para>
/// <strong>One font, bundled.</strong> DejaVu Sans (regular and bold) is read from
/// this assembly's resources. Nothing is taken from the host's fonts and nothing
/// is fetched, so a report renders identically in the Linux container and on a
/// developer's machine.
/// </para>
/// </remarks>
internal sealed class PdfText : IDisposable
{
    private const string Ellipsis = "…";

    private static readonly Lazy<SKTypeface> RegularFace = new(() => Load("OmniAI.Reports.Fonts.DejaVuSans.ttf"));
    private static readonly Lazy<SKTypeface> BoldFace = new(() => Load("OmniAI.Reports.Fonts.DejaVuSans-Bold.ttf"));

    private readonly SKShaper _regular = new(RegularFace.Value);
    private readonly SKShaper _bold = new(BoldFace.Value);
    private readonly Dictionary<(float Size, bool Bold), SKFont> _fonts = [];
    private readonly Dictionary<(string Text, float Size, bool Bold), float> _widths = [];

    /// <summary>The regular face, for tests that need to compare glyphs.</summary>
    internal static SKTypeface Regular => RegularFace.Value;

    /// <summary>
    /// Text that can be laid out safely.
    /// </summary>
    /// <remarks>
    /// Control characters become spaces. Explicit bidirectional embeddings,
    /// overrides and isolates are removed: a label must not be able to reorder the
    /// characters around it — the "Trojan Source" trick — and nothing in a report
    /// needs them, because the algorithm already orders ordinary text correctly.
    /// </remarks>
    public static string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        StringBuilder clean = new(text.Length);

        foreach (char character in text)
        {
            if (character is (>= '\u202A' and <= '\u202E') or (>= '\u2066' and <= '\u2069'))
            {
                continue;
            }

            clean.Append(char.IsControl(character) ? ' ' : character);
        }

        return clean.ToString();
    }

    /// <summary>The shaped width of a line.</summary>
    public float Measure(string text, float size, bool bold)
    {
        string clean = Clean(text);

        if (clean.Length == 0)
        {
            return 0;
        }

        if (_widths.TryGetValue((clean, size, bold), out float known))
        {
            return known;
        }

        float width = Runs(clean, size, bold).Sum(run => run.Shaped.Width);

        _widths[(clean, size, bold)] = width;

        return width;
    }

    /// <summary>Whether the line reads right to left as a whole.</summary>
    public static bool IsRightToLeft(string text)
    {
        string clean = Clean(text);

        if (clean.Length == 0)
        {
            return false;
        }

        BidiInfo bidi = BidiInfo.Create(clean, null);

        return bidi.Paragraphs.Count > 0 && bidi.Paragraphs[0].Level.IsRtl();
    }

    /// <summary>Draws a line with its left edge at <paramref name="x"/>.</summary>
    public void Draw(SKCanvas canvas, string text, float x, float baseline, float size, bool bold, SKColor color)
    {
        ArgumentNullException.ThrowIfNull(canvas);

        string clean = Clean(text);

        if (clean.Length == 0)
        {
            return;
        }

        SKFont font = Font(size, bold);
        using SKTextBlobBuilder builder = new();

        float advance = 0;

        foreach (ShapedRun run in Runs(clean, size, bold))
        {
            AddRun(builder, font, run, advance);
            advance += run.Shaped.Width;
        }

        using SKTextBlob? blob = builder.Build();

        if (blob is null)
        {
            return;
        }

        using SKPaint paint = new() { Color = color, IsAntialias = true };

        canvas.DrawText(blob, x, baseline, paint);
    }

    /// <summary>
    /// Draws a line inside a box of the given width.
    /// </summary>
    /// <remarks>
    /// Numbers align to the end of their column, as in the other formats. In a
    /// table cell, a line whose paragraph direction is right to left aligns to the
    /// cell's right edge, where a reader of that script starts. A value beside a
    /// label is placed at the start of its column instead, so it stays next to the
    /// label it belongs to rather than drifting to the far margin.
    /// </remarks>
    public void DrawAligned(
        SKCanvas canvas,
        string text,
        float left,
        float width,
        float baseline,
        float size,
        bool bold,
        SKColor color,
        bool numeric,
        bool alignRightToLeft = true)
    {
        float x = numeric || (alignRightToLeft && IsRightToLeft(text))
            ? left + width - Measure(text, size, bold)
            : left;

        Draw(canvas, text, x, baseline, size, bold, color);
    }

    /// <summary>
    /// The line, shortened with an ellipsis if it does not fit.
    /// </summary>
    /// <remarks>
    /// Shortened by whole text elements of the logical string and shaped again, so
    /// an Arabic word is never cut inside a letter and keeps joining correctly.
    /// </remarks>
    public string Fit(string text, float size, bool bold, float available)
    {
        string clean = Clean(text);

        if (Measure(clean, size, bold) <= available)
        {
            return clean;
        }

        int[] boundaries = StringInfo.ParseCombiningCharacters(clean);

        for (int count = boundaries.Length - 1; count > 0; count--)
        {
            string candidate = clean[..boundaries[count]].TrimEnd() + Ellipsis;

            if (Measure(candidate, size, bold) <= available)
            {
                return candidate;
            }
        }

        return Ellipsis;
    }

    /// <summary>Lines of at most <paramref name="available"/> points, broken at spaces.</summary>
    public IEnumerable<string> Wrap(string text, float size, bool bold, float available)
    {
        string line = string.Empty;

        foreach (string word in Clean(text).Split(' '))
        {
            string candidate = line.Length == 0 ? word : line + " " + word;

            if (line.Length > 0 && Measure(candidate, size, bold) > available)
            {
                yield return line;
                line = word;
            }
            else
            {
                line = candidate;
            }
        }

        yield return Fit(line, size, bold, available);
    }

    /// <summary>
    /// The shaped runs of a line, in visual order.
    /// </summary>
    /// <remarks>
    /// Internal for tests, which assert on the glyphs rather than on a picture.
    /// </remarks>
    internal IReadOnlyList<ShapedRun> Runs(string clean, float size, bool bold)
    {
        BidiInfo bidi = BidiInfo.Create(clean, null);
        SKShaper shaper = bold ? _bold : _regular;
        SKFont font = Font(size, bold);

        List<ShapedRun> runs = [];

        foreach (ParagraphInfo paragraph in bidi.Paragraphs)
        {
            (Level[] levels, TextRange[] visual) = bidi.VisualRuns(paragraph, paragraph.Range);

            foreach (TextRange range in visual)
            {
                if (range.IsEmpty)
                {
                    continue;
                }

                bool rightToLeft = levels[range.Start].IsRtl();
                string segment = clean.Substring(range.Start, range.Length);

                using HarfBuzzSharp.Buffer buffer = new();
                buffer.AddUtf16(segment);
                buffer.GuessSegmentProperties();

                // The run's direction comes from the bidi algorithm, not from a
                // guess about its script: a run of digits inside Arabic, or of
                // punctuation, must go the way its embedding level says.
                buffer.Direction = rightToLeft ? HarfBuzzSharp.Direction.RightToLeft : HarfBuzzSharp.Direction.LeftToRight;

                runs.Add(new ShapedRun(segment, rightToLeft, shaper.Shape(buffer, font)));
            }
        }

        return runs;
    }

    public void Dispose()
    {
        foreach (SKFont font in _fonts.Values)
        {
            font.Dispose();
        }

        _regular.Dispose();
        _bold.Dispose();
    }

    private SKFont Font(float size, bool bold)
    {
        if (!_fonts.TryGetValue((size, bold), out SKFont? font))
        {
            font = new SKFont(bold ? BoldFace.Value : RegularFace.Value, size)
            {
                Subpixel = true,
                Edging = SKFontEdging.Antialias,
            };

            _fonts[(size, bold)] = font;
        }

        return font;
    }

    /// <summary>
    /// One run as positioned glyphs plus the UTF-8 text they came from.
    /// </summary>
    /// <remarks>
    /// HarfBuzz reports clusters as UTF-16 offsets into the run; Skia wants UTF-8
    /// byte offsets into the text it is given. The map between them is built once
    /// per run.
    /// </remarks>
    private static void AddRun(SKTextBlobBuilder builder, SKFont font, ShapedRun run, float advance)
    {
        SKShaper.Result shaped = run.Shaped;
        int count = shaped.Codepoints.Length;

        if (count == 0)
        {
            return;
        }

        byte[] utf8 = Encoding.UTF8.GetBytes(run.Text);
        int[] byteOffset = new int[run.Text.Length + 1];

        for (int index = 0, bytes = 0; index < run.Text.Length; index++)
        {
            byteOffset[index] = bytes;

            bool pair = char.IsHighSurrogate(run.Text[index]) && index + 1 < run.Text.Length;
            bytes += Encoding.UTF8.GetByteCount(run.Text.AsSpan(index, pair ? 2 : 1));

            if (pair)
            {
                byteOffset[++index] = bytes;
            }
        }

        SKPositionedTextRunBuffer buffer = builder.AllocatePositionedTextRun(font, count, utf8.Length, null);

        Span<ushort> glyphs = buffer.Glyphs;
        Span<SKPoint> positions = buffer.Positions;
        Span<uint> clusters = buffer.Clusters;

        for (int index = 0; index < count; index++)
        {
            glyphs[index] = (ushort)shaped.Codepoints[index];
            positions[index] = new SKPoint(shaped.Points[index].X + advance, shaped.Points[index].Y);
            clusters[index] = (uint)byteOffset[Math.Min((int)shaped.Clusters[index], run.Text.Length)];
        }

        utf8.CopyTo(buffer.Text);
    }

    private static SKTypeface Load(string resource)
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"The report font {resource} is missing from the assembly.");

        return SKTypeface.FromStream(stream)
            ?? throw new InvalidOperationException($"The report font {resource} could not be read.");
    }

    /// <summary>A run of one direction, shaped.</summary>
    internal sealed record ShapedRun(string Text, bool RightToLeft, SKShaper.Result Shaped);
}
