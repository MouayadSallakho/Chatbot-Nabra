using System.Globalization;
using System.Reflection;
using System.Text;
using HarfBuzzSharp;
using SkiaSharp;
using SkiaSharp.HarfBuzz;
using Unicode.Bidi;
using Buffer = HarfBuzzSharp.Buffer;

namespace OmniAI.Infrastructure.Reports.Rendering;

/// <summary>
/// Measures and draws one line of report text, correctly, in any script the
/// bundled fonts cover.
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
/// <strong>The brand's fonts, bundled (Phase 39.7b).</strong> Latin letters and
/// every digit are set in Geist, Arabic in IBM Plex Sans Arabic, as in the app;
/// headings use each family's 600 weight. Both are under the SIL Open Font
/// License, whose text sits beside the files. Each bidi run is split by script:
/// an Arabic letter goes to Plex Arabic, a Latin letter or a digit to Geist, and
/// spaces, punctuation and marks stay with the letters around them, so a word is
/// never set in two fonts. DejaVu Sans, the 37.7 font, stays only as the last
/// resort for the scripts neither brand font draws (Greek, Cyrillic, Hebrew and
/// the like), so a customer's name in those scripts is still real letters. A
/// font that no glyph on a page uses is not embedded in that file. Geist's
/// figures are set tabular (<c>tnum</c>), so numbers line up in their columns.
/// Nothing is taken from the host's fonts and nothing is fetched, so a report
/// renders identically in the Linux container and on a developer's machine.
/// </para>
/// <para>
/// <strong>Two kinds of line (Phase 37.7).</strong> By default a line takes its
/// direction from its first strong character, as an isolate does: that is how
/// every value is drawn, so a Latin name, an e-mail address or a number inside an
/// Arabic report keeps its own order. An Arabic report's own words (titles,
/// labels, headings, notes) are drawn with <c>rightToLeft</c>, a right-to-left
/// paragraph, so a label such as «التاريخ (بتوقيت Asia/Riyadh)» keeps its
/// parentheses and the zone name where an Arabic reader expects them.
/// </para>
/// </remarks>
internal sealed class PdfText : IDisposable
{
    private const string Ellipsis = "…";

    /// <summary>HarfBuzz works in integer units: 16,384 per em keeps positions exact to a hundredth of a point.</summary>
    private const int ShapingScale = 1 << 14;

    /// <summary>Tabular figures. Plex Arabic's figures are already tabular, so only Geist changes.</summary>
    private static readonly Feature[] Features = [Feature.Parse("tnum")];

    private static readonly Lazy<ReportFont> LatinRegular = new(() => ReportFont.Load("Geist-Regular.ttf"));
    private static readonly Lazy<ReportFont> LatinBold = new(() => ReportFont.Load("Geist-SemiBold.ttf"));
    private static readonly Lazy<ReportFont> ArabicRegular = new(() => ReportFont.Load("IBMPlexSansArabic-Regular.ttf"));
    private static readonly Lazy<ReportFont> ArabicBold = new(() => ReportFont.Load("IBMPlexSansArabic-SemiBold.ttf"));
    private static readonly Lazy<ReportFont> FallbackRegular = new(() => ReportFont.Load("DejaVuSans.ttf"));
    private static readonly Lazy<ReportFont> FallbackBold = new(() => ReportFont.Load("DejaVuSans-Bold.ttf"));

    private readonly Dictionary<ReportFont, HarfBuzzSharp.Font> _shapers = [];
    private readonly Dictionary<(ReportFont Face, float Size), SKFont> _fonts = [];
    private readonly Dictionary<(string Text, float Size, bool Bold, bool RightToLeft), float> _widths = [];

    private enum Family
    {
        Neutral,
        Latin,
        Arabic,
        Other,
    }

    /// <summary>The Latin face (Geist Regular), for tests that need to compare glyphs.</summary>
    internal static SKTypeface Latin => LatinRegular.Value.Typeface;

    /// <summary>The Arabic face (IBM Plex Sans Arabic Regular), for tests that need to compare glyphs.</summary>
    internal static SKTypeface Arabic => ArabicRegular.Value.Typeface;

    /// <summary>The last-resort face (DejaVu Sans), for tests that need to compare glyphs.</summary>
    internal static SKTypeface Fallback => FallbackRegular.Value.Typeface;

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
            if (character is (>= '‪' and <= '‮') or (>= '⁦' and <= '⁩'))
            {
                continue;
            }

            clean.Append(char.IsControl(character) ? ' ' : character);
        }

        return clean.ToString();
    }

    /// <summary>The shaped width of a line.</summary>
    /// <param name="rightToLeft">Lay the line out as a right-to-left paragraph (Phase 37.7).</param>
    public float Measure(string text, float size, bool bold, bool rightToLeft = false)
    {
        string clean = Clean(text);

        if (clean.Length == 0)
        {
            return 0;
        }

        if (_widths.TryGetValue((clean, size, bold, rightToLeft), out float known))
        {
            return known;
        }

        float width = Runs(clean, size, bold, rightToLeft).Sum(run => run.Shaped.Width);

        _widths[(clean, size, bold, rightToLeft)] = width;

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
    /// <param name="rightToLeft">Lay the line out as a right-to-left paragraph (Phase 37.7).</param>
    public void Draw(
        SKCanvas canvas,
        string text,
        float x,
        float baseline,
        float size,
        bool bold,
        SKColor color,
        bool rightToLeft = false)
    {
        ArgumentNullException.ThrowIfNull(canvas);

        string clean = Clean(text);

        if (clean.Length == 0)
        {
            return;
        }

        using SKTextBlobBuilder builder = new();

        float advance = 0;

        foreach (ShapedRun run in Runs(clean, size, bold, rightToLeft))
        {
            AddRun(builder, Font(run.Face, size), run, advance);
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
    /// Draws a line inside a box, against its right edge or its left edge (Phase 37.7).
    /// </summary>
    /// <remarks>
    /// For Arabic reports, where the layout is mirrored: text starts at a box's
    /// right edge and numbers end at its left edge, the opposite of the English
    /// layout, rather than each line being placed by its own direction.
    /// </remarks>
    public void DrawInBox(
        SKCanvas canvas,
        string text,
        float left,
        float width,
        float baseline,
        float size,
        bool bold,
        SKColor color,
        bool againstRight,
        bool rightToLeft)
    {
        float x = againstRight ? left + width - Measure(text, size, bold, rightToLeft) : left;

        Draw(canvas, text, x, baseline, size, bold, color, rightToLeft);
    }

    /// <summary>
    /// The line, shortened with an ellipsis if it does not fit.
    /// </summary>
    /// <remarks>
    /// Shortened by whole text elements of the logical string and shaped again, so
    /// an Arabic word is never cut inside a letter and keeps joining correctly.
    /// </remarks>
    public string Fit(string text, float size, bool bold, float available, bool rightToLeft = false)
    {
        string clean = Clean(text);

        if (Measure(clean, size, bold, rightToLeft) <= available)
        {
            return clean;
        }

        int[] boundaries = StringInfo.ParseCombiningCharacters(clean);

        for (int count = boundaries.Length - 1; count > 0; count--)
        {
            string candidate = clean[..boundaries[count]].TrimEnd() + Ellipsis;

            if (Measure(candidate, size, bold, rightToLeft) <= available)
            {
                return candidate;
            }
        }

        return Ellipsis;
    }

    /// <summary>Lines of at most <paramref name="available"/> points, broken at spaces.</summary>
    public IEnumerable<string> Wrap(string text, float size, bool bold, float available, bool rightToLeft = false)
    {
        string line = string.Empty;

        foreach (string word in Clean(text).Split(' '))
        {
            string candidate = line.Length == 0 ? word : line + " " + word;

            if (line.Length > 0 && Measure(candidate, size, bold, rightToLeft) > available)
            {
                yield return Fit(line, size, bold, available, rightToLeft);
                line = word;
            }
            else
            {
                line = candidate;
            }
        }

        yield return Fit(line, size, bold, available, rightToLeft);
    }

    /// <summary>The width of the line's widest word: the narrowest box it can wrap into uncut.</summary>
    public float WidestWord(string text, float size, bool bold, bool rightToLeft = false) =>
        Clean(text).Split(' ').Select(word => Measure(word, size, bold, rightToLeft)).DefaultIfEmpty(0).Max();

    /// <summary>
    /// The shaped runs of a line, in visual order.
    /// </summary>
    /// <remarks>
    /// Internal for tests, which assert on the glyphs rather than on a picture.
    /// </remarks>
    internal IReadOnlyList<ShapedRun> Runs(string clean, float size, bool bold, bool rightToLeftParagraph = false)
    {
        // Phase 37.7: an Arabic report's own words are a right-to-left paragraph;
        // everything else takes its direction from its first strong character.
        BidiInfo bidi = BidiInfo.Create(clean, rightToLeftParagraph ? Level.Rtl() : null);

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

                // One bidi run can need more than one font; its pieces are shaped
                // separately and, in a right-to-left run, placed last piece first.
                List<(string Text, ReportFont Face)> pieces = Itemize(segment, rightToLeft, bold);

                if (rightToLeft)
                {
                    pieces.Reverse();
                }

                foreach ((string text, ReportFont face) in pieces)
                {
                    runs.Add(new ShapedRun(text, rightToLeft, Shape(text, rightToLeft, face, size), face));
                }
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

        foreach (HarfBuzzSharp.Font shaper in _shapers.Values)
        {
            shaper.Dispose();
        }
    }

    /// <summary>
    /// Splits one bidi run, in logical order, into pieces that each use one font.
    /// </summary>
    /// <remarks>
    /// Arabic letters go to Plex Arabic; Latin letters and digits to Geist; letters
    /// of any other script to DejaVu. Spaces, punctuation, symbols and marks take the
    /// family of the letter before them (or after them, at the start of a run), so a
    /// mark stays on its letter and the spaces of an Arabic phrase are Arabic spaces.
    /// A run with no letter at all takes Plex Arabic when it runs right to left and
    /// Geist otherwise. Then, character by character, a face that has no glyph for
    /// it hands over to the next one that does (Geist, Plex Arabic, DejaVu), so no
    /// character the fonts can draw becomes a missing-glyph box.
    /// </remarks>
    private List<(string Text, ReportFont Face)> Itemize(string segment, bool rightToLeft, bool bold)
    {
        List<(int Start, int Length, int CodePoint, Family Family)> characters = [];

        for (int index = 0; index < segment.Length;)
        {
            bool pair = char.IsHighSurrogate(segment[index]) && index + 1 < segment.Length && char.IsLowSurrogate(segment[index + 1]);
            int length = pair ? 2 : 1;
            int codePoint = pair ? char.ConvertToUtf32(segment[index], segment[index + 1]) : segment[index];
            characters.Add((index, length, codePoint, Classify(codePoint)));
            index += length;
        }

        Family fallback = rightToLeft ? Family.Arabic : Family.Latin;
        Family next = characters.Select(character => character.Family).FirstOrDefault(family => family != Family.Neutral, fallback);
        Family previous = next;

        List<(string Text, ReportFont Face)> pieces = [];
        StringBuilder piece = new();
        ReportFont? current = null;

        foreach ((int start, int length, int codePoint, Family family) in characters)
        {
            Family resolved = family == Family.Neutral ? previous : family;
            previous = resolved;

            ReportFont face = Covering(codePoint, resolved, bold, keep: family == Family.Neutral ? current : null);

            if (current is not null && !ReferenceEquals(face, current))
            {
                pieces.Add((piece.ToString(), current));
                piece.Clear();
            }

            current = face;
            piece.Append(segment, start, length);
        }

        if (current is not null)
        {
            pieces.Add((piece.ToString(), current));
        }

        return pieces;
    }

    /// <summary>The face for one character: its family's first, then whichever bundled face has it.</summary>
    /// <param name="keep">A neutral character stays in the piece it follows when that face can draw it.</param>
    private ReportFont Covering(int codePoint, Family family, bool bold, ReportFont? keep)
    {
        if (keep is not null && Has(keep, codePoint))
        {
            return keep;
        }

        ReportFont latin = (bold ? LatinBold : LatinRegular).Value;
        ReportFont arabic = (bold ? ArabicBold : ArabicRegular).Value;
        ReportFont other = (bold ? FallbackBold : FallbackRegular).Value;

        ReportFont[] order = family switch
        {
            Family.Arabic => [arabic, latin, other],
            Family.Other => [other, latin, arabic],
            _ => [latin, arabic, other],
        };

        // Default-ignorable characters (joiners, direction marks) have no glyph in
        // some faces; HarfBuzz hides them, so they never need a face of their own.
        if (IsDefaultIgnorable(codePoint))
        {
            return keep ?? order[0];
        }

        return order.FirstOrDefault(face => Has(face, codePoint)) ?? order[0];
    }

    private bool Has(ReportFont face, int codePoint) => Shaper(face).TryGetNominalGlyph((uint)codePoint, out uint glyph) && glyph != 0;

    private static Family Classify(int codePoint)
    {
        if (IsArabic(codePoint))
        {
            return Family.Arabic;
        }

        if (codePoint is >= '0' and <= '9')
        {
            return Family.Latin;
        }

        UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(codePoint);

        bool letter = category is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter
            or UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter;

        if (!letter)
        {
            return Family.Neutral;
        }

        return IsLatin(codePoint) ? Family.Latin : Family.Other;
    }

    private static bool IsArabic(int codePoint) =>
        codePoint is (>= 0x0600 and <= 0x06FF) or (>= 0x0750 and <= 0x077F) or (>= 0x0870 and <= 0x08FF)
            or (>= 0xFB50 and <= 0xFDFF) or (>= 0xFE70 and <= 0xFEFF);

    private static bool IsLatin(int codePoint) =>
        codePoint is <= 0x024F or (>= 0x0250 and <= 0x02FF) or (>= 0x1D00 and <= 0x1DBF) or (>= 0x1E00 and <= 0x1EFF)
            or (>= 0x2C60 and <= 0x2C7F) or (>= 0xA720 and <= 0xA7FF) or (>= 0xAB30 and <= 0xAB6F) or (>= 0xFB00 and <= 0xFB06)
            or (>= 0xFF21 and <= 0xFF5A);

    private static bool IsDefaultIgnorable(int codePoint) =>
        codePoint is 0x00AD or 0x034F or 0x061C or (>= 0x115F and <= 0x1160) or (>= 0x17B4 and <= 0x17B5)
            or (>= 0x180B and <= 0x180F) or (>= 0x200B and <= 0x200F) or (>= 0x202A and <= 0x202E)
            or (>= 0x2060 and <= 0x206F) or 0x3164 or (>= 0xFE00 and <= 0xFE0F) or 0xFEFF or 0xFFA0;

    private ShapedGlyphs Shape(string text, bool rightToLeft, ReportFont face, float size)
    {
        using Buffer buffer = new();
        buffer.AddUtf16(text);
        buffer.GuessSegmentProperties();

        // The run's direction comes from the bidi algorithm, not from a guess
        // about its script: a run of digits inside Arabic, or of punctuation, must
        // go the way its embedding level says.
        buffer.Direction = rightToLeft ? HarfBuzzSharp.Direction.RightToLeft : HarfBuzzSharp.Direction.LeftToRight;

        Shaper(face).Shape(buffer, Features);

        ReadOnlySpan<GlyphInfo> infos = buffer.GetGlyphInfoSpan();
        ReadOnlySpan<GlyphPosition> positions = buffer.GetGlyphPositionSpan();
        int count = infos.Length;

        float scale = size / ShapingScale;
        uint[] codepoints = new uint[count];
        uint[] clusters = new uint[count];
        SKPoint[] points = new SKPoint[count];

        float x = 0;
        float y = 0;

        for (int index = 0; index < count; index++)
        {
            codepoints[index] = infos[index].Codepoint;
            clusters[index] = infos[index].Cluster;
            points[index] = new SKPoint(x + (positions[index].XOffset * scale), y - (positions[index].YOffset * scale));
            x += positions[index].XAdvance * scale;
            y += positions[index].YAdvance * scale;
        }

        return new ShapedGlyphs(codepoints, clusters, points, x);
    }

    private HarfBuzzSharp.Font Shaper(ReportFont face)
    {
        if (!_shapers.TryGetValue(face, out HarfBuzzSharp.Font? shaper))
        {
            shaper = face.CreateShaper(ShapingScale);

            _shapers[face] = shaper;
        }

        return shaper;
    }

    private SKFont Font(ReportFont face, float size)
    {
        if (!_fonts.TryGetValue((face, size), out SKFont? font))
        {
            font = new SKFont(face.Typeface, size)
            {
                Subpixel = true,
                Edging = SKFontEdging.Antialias,
            };

            _fonts[(face, size)] = font;
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
        ShapedGlyphs shaped = run.Shaped;
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

    /// <summary>A run of one direction in one face, shaped.</summary>
    internal sealed record ShapedRun(string Text, bool RightToLeft, ShapedGlyphs Shaped, ReportFont Face);

    /// <summary>HarfBuzz's output for a run: glyph ids, source clusters, positions in points, and the advance.</summary>
    internal sealed record ShapedGlyphs(uint[] Codepoints, uint[] Clusters, SKPoint[] Points, float Width);

    /// <summary>
    /// One bundled font file: Skia's typeface for drawing, and HarfBuzz fonts over
    /// the same bytes for shaping.
    /// </summary>
    /// <remarks>
    /// The typeface is read once per process from this assembly's resources. Each
    /// <see cref="PdfText"/> makes its own HarfBuzz font from it, as Skia's shaper
    /// did before 39.7b.
    /// </remarks>
    internal sealed class ReportFont
    {
        private ReportFont(string name, SKTypeface typeface)
        {
            Name = name;
            Typeface = typeface;
        }

        public string Name { get; }

        public SKTypeface Typeface { get; }

        public static ReportFont Load(string file)
        {
            string resource = "OmniAI.Reports.Fonts." + file;

            using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException($"The report font {resource} is missing from the assembly.");

            SKTypeface typeface = SKTypeface.FromStream(stream)
                ?? throw new InvalidOperationException($"The report font {resource} could not be read.");

            return new ReportFont(file, typeface);
        }

        /// <summary>
        /// A HarfBuzz font over this typeface's own bytes, built exactly as Skia's
        /// <c>SKShaper</c> builds one; the caller owns and disposes it.
        /// </summary>
        public HarfBuzzSharp.Font CreateShaper(int scale)
        {
            using SKStreamAsset asset = Typeface.OpenStream(out int index);
            using Blob blob = asset.ToHarfBuzzBlob();
            using Face face = new(blob, index)
            {
                Index = index,
                UnitsPerEm = Typeface.UnitsPerEm,
            };

            HarfBuzzSharp.Font shaper = new(face);
            shaper.SetScale(scale, scale);
            shaper.SetFunctionsOpenType();

            return shaper;
        }

        public override string ToString() => Name;
    }
}
