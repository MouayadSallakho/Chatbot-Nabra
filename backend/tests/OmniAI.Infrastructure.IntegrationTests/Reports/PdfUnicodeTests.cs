using System.Text;
using OmniAI.Application.Reports;
using OmniAI.Domain.Reports;
using OmniAI.Infrastructure.Reports.Rendering;
using SkiaSharp;
using UglyToad.PdfPig;

namespace OmniAI.Infrastructure.IntegrationTests.Reports;

/// <summary>
/// PDF text in the scripts OmniAI's workspaces actually use.
/// </summary>
/// <remarks>
/// <para>
/// Two layers of proof. The shaping layer is asserted on glyphs: HarfBuzz must
/// have chosen contextual Arabic forms, formed the lam-alef ligature, used no
/// missing glyph, and laid Arabic runs out right to left while Latin runs go left
/// to right. The document layer is asserted on the finished PDF, read back by an
/// independent parser: the text layer carries the exact Unicode the report
/// contained, with no placeholder substituted.
/// </para>
/// <para>
/// A glyph test cannot prove the page <em>looks</em> right, and a text-layer test
/// cannot prove the letters are joined. Together, with the rendered page inspected
/// once by eye in Chrome, they cover what can go wrong.
/// </para>
/// </remarks>
public sealed class PdfUnicodeTests
{
    private const string ArabicName = "أحمد محمد";
    private const string ArabicWorkspace = "شركة النور";
    private const string Mixed = "Support — خدمة العملاء";

    [Fact]
    public void Arabic_is_shaped_into_joined_forms_with_no_missing_glyph()
    {
        using PdfText text = new();
        using SKFont font = new(PdfText.Arabic, 12);

        foreach (string word in new[] { "محمد", "النور", "شركة" })
        {
            PdfText.ShapedRun run = Assert.Single(text.Runs(word, 12, bold: false));

            ushort[] nominal = font.GetGlyphs(word);
            uint[] shaped = run.Shaped.Codepoints;

            Assert.True(run.RightToLeft);
            Assert.DoesNotContain(0u, shaped);

            // Joining letters must not be drawn in their isolated, dictionary
            // forms: at least one glyph is a contextual form the cmap never names.
            Assert.Contains(shaped, glyph => !nominal.Contains((ushort)glyph));
        }
    }

    [Fact]
    public void Lam_alef_becomes_one_ligature()
    {
        using PdfText text = new();

        PdfText.ShapedRun run = Assert.Single(text.Runs("لا", 12, bold: false));

        Assert.Single(run.Shaped.Codepoints);
        Assert.NotEqual(0u, run.Shaped.Codepoints[0]);
    }

    [Fact]
    public void An_arabic_run_is_laid_out_right_to_left()
    {
        using PdfText text = new();

        PdfText.ShapedRun run = Assert.Single(text.Runs(ArabicName, 12, bold: false));

        // Visual order: the first glyph drawn is the last logical character, so
        // the source clusters fall as the glyphs advance to the right.
        uint[] clusters = run.Shaped.Clusters;

        Assert.True(clusters[0] > clusters[^1]);
        Assert.True(clusters.Zip(clusters.Skip(1)).All(pair => pair.First >= pair.Second));
        Assert.True(PdfText.IsRightToLeft(ArabicName));
    }

    [Fact]
    public void Mixed_text_is_split_by_the_bidi_algorithm_into_directed_runs()
    {
        using PdfText text = new();

        IReadOnlyList<PdfText.ShapedRun> runs = text.Runs(Mixed, 12, bold: false);

        // An English-first line: left to right overall, Latin first, Arabic after.
        Assert.False(PdfText.IsRightToLeft(Mixed));
        Assert.Equal(2, runs.Count);
        Assert.False(runs[0].RightToLeft);
        Assert.StartsWith("Support", runs[0].Text, StringComparison.Ordinal);
        Assert.True(runs[1].RightToLeft);
        Assert.Equal("خدمة العملاء", runs[1].Text);
    }

    [Fact]
    public void Numbers_beside_arabic_keep_their_own_left_to_right_order()
    {
        using PdfText text = new();

        IReadOnlyList<PdfText.ShapedRun> runs = text.Runs("شركة النور 2026 USD", 12, bold: false);

        Assert.True(PdfText.IsRightToLeft("شركة النور 2026 USD"));

        PdfText.ShapedRun digits = Assert.Single(runs, run => run.Text.Contains("2026", StringComparison.Ordinal));
        Assert.False(digits.RightToLeft);
        Assert.Contains(runs, run => run.RightToLeft && run.Text.Contains("النور", StringComparison.Ordinal));
    }

    [Fact]
    public void Accented_latin_uses_real_glyphs()
    {
        using PdfText text = new();
        using SKFont font = new(PdfText.Latin, 12);

        foreach (string name in new[] { "José García", "François Müller" })
        {
            PdfText.ShapedRun run = Assert.Single(text.Runs(name, 12, bold: false));

            Assert.False(run.RightToLeft);
            Assert.DoesNotContain(0u, run.Shaped.Codepoints);
            Assert.DoesNotContain(font.GetGlyph('?'), run.Shaped.Codepoints.Select(glyph => (ushort)glyph));
        }
    }

    [Fact]
    public void Bidi_overrides_in_user_text_are_removed_before_layout()
    {
        // U+202E would otherwise reverse everything after it on the page.
        string hostile = "invoice" + '\u202E' + "fdp.exe";

        Assert.Equal("invoicefdp.exe", PdfText.Clean(hostile));
        Assert.Equal("a b", PdfText.Clean("a\u0007b"));
    }

    [Fact]
    public async Task The_pdf_text_layer_carries_every_script_exactly()
    {
        byte[] bytes = await ReportRenderingTests.Render(new PdfReportRenderer(), Document());

        Assert.Equal("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));

        using PdfDocument pdf = PdfDocument.Open(bytes);

        string all = string.Concat(pdf.GetPages().Select(page => page.Text));

        foreach (string expected in new[]
        {
            "José García",
            "François Müller",
            "<script>alert(1)</script>",
        })
        {
            Assert.Contains(expected, all, StringComparison.Ordinal);
        }

        // Arabic is drawn as shaped glyphs, and the text layer names each glyph
        // by its Unicode presentation form, in visual order. So the PDF itself can
        // be checked for correct joining and direction: each word must appear as
        // exactly the contextual forms a reader expects, right to left. (NFKC
        // would fold these back to plain letters, but this platform runs with
        // invariant globalization, so the forms are asserted directly.)
        string glyphs = all.Replace(" ", string.Empty, StringComparison.Ordinal);

        // النور: reh isolated, waw final, noon medial, lam initial, alef isolated.
        Assert.Contains("\u0631\uFEEE\uFEE8\uFEDF\u0627", glyphs, StringComparison.Ordinal);

        // شركة: teh marbuta final, kaf medial, then IBM Plex Sans Arabic's
        // sheen-reh ligature (Phase 39.7b), which the text layer names by its
        // compatibility character U+FD0D, the same two letters under NFKC.
        Assert.Contains("\uFE94\uFEDB\uFD0D", glyphs, StringComparison.Ordinal);

        // أحمد محمد: two words, each dal final, meem medial, hah, meem/alef-hamza.
        Assert.Contains("\uFEAA\uFEE4\uFEA4\uFEE3\uFEAA\uFEE4\uFEA3\u0623", glyphs, StringComparison.Ordinal);

        // العملاء: the lam-alef ligature, formed and in place.
        Assert.Contains("\u0621\uFEFC\uFEE4\uFECC\uFEDF\u0627", glyphs, StringComparison.Ordinal);

        Assert.DoesNotContain('?', all.Replace("<script>alert(1)</script>", string.Empty, StringComparison.Ordinal));
        Assert.DoesNotContain('\uFFFD', all);
    }

    [Fact]
    public async Task The_pdf_embeds_its_own_font_and_nothing_active_or_remote()
    {
        byte[] bytes = await ReportRenderingTests.Render(new PdfReportRenderer(), Document());
        string raw = Encoding.Latin1.GetString(bytes);

        // The bundled font is embedded, so no reader has to substitute one.
        Assert.Matches("/FontFile[23]?", raw);

        foreach (string forbidden in new[] { "/JavaScript", "/JS ", "/OpenAction", "/AA ", "/Launch", "/URI", "/SubmitForm", "/EmbeddedFile", "/Annots", "http://", "https://" })
        {
            Assert.DoesNotContain(forbidden, raw, StringComparison.Ordinal);
        }

        using PdfDocument pdf = PdfDocument.Open(bytes);
        Assert.Empty(pdf.GetPages().SelectMany(page => page.GetImages()));
    }

    [Fact]
    public async Task The_pdf_names_nabra_as_its_creator_and_never_the_code_name()
    {
        // A reader's document properties show these, so they are words a person
        // reads like any other.
        byte[] bytes = await ReportRenderingTests.Render(new PdfReportRenderer(), Document());

        using PdfDocument pdf = PdfDocument.Open(bytes);

        Assert.Equal("Nabra reports", pdf.Information.Creator);
        Assert.Equal("Nabra", pdf.Information.Producer);

        string all = string.Concat(pdf.GetPages().Select(page => page.Text));

        Assert.DoesNotContain("omniai", all, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OmniAI", Encoding.Latin1.GetString(bytes), StringComparison.Ordinal);
    }

    private static ReportDocument Document()
    {
        ReportValue[] Row(string label) =>
        [
            ReportValue.Text(label),
            ReportValue.Count(12),
            ReportValue.Money(0.0036m),
            ReportValue.Text("USD"),
            ReportValue.Date(new DateOnly(2026, 9, 1)),
        ];

        return new ReportDocument(
            ReportType.CustomerActivity,
            "Customer activity",
            ArabicWorkspace,
            ReportPeriod.Create(new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1)),
            new DateTimeOffset(2026, 10, 1, 6, 0, 0, TimeSpan.Zero),
            [new ReportFact("Active customers", ReportValue.Count(5))],
            [
                new ReportTable(
                    "Customers",
                    "Active customers",
                    [
                        new ReportColumn("Customer", ReportValueKind.Text),
                        new ReportColumn("Messages", ReportValueKind.Count),
                        new ReportColumn("Estimated provider cost", ReportValueKind.Money),
                        new ReportColumn("Currency", ReportValueKind.Text),
                        new ReportColumn("Day", ReportValueKind.Date),
                    ],
                    [
                        Row(ArabicName),
                        Row("José García"),
                        Row("François Müller"),
                        Row(Mixed),
                        Row("<script>alert(1)</script>"),
                    ]),
            ],
            ["Period: 2026-09-01 to 2026-09-30 inclusive, in UTC whole days."]);
    }
}
