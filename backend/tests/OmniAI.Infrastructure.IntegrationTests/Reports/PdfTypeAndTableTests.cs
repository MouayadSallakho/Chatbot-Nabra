using System.Text;
using OmniAI.Application.Reports;
using OmniAI.Domain.Reports;
using OmniAI.Domain.Users;
using OmniAI.Infrastructure.Reports.Rendering;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using Page = UglyToad.PdfPig.Content.Page;

namespace OmniAI.Infrastructure.IntegrationTests.Reports;

/// <summary>
/// The report PDF's type and tables (Phase 39.7b, Phase 38 note 2).
/// </summary>
/// <remarks>
/// <para>
/// Type: Latin text and every digit in Geist, Arabic in IBM Plex Sans Arabic, both
/// embedded, with DejaVu Sans only for scripts neither draws; figures tabular; and
/// each font's OFL licence copied beside the build.
/// </para>
/// <para>
/// Tables, read back with PdfPig: a heading too long for its column wraps and is
/// never cut; a number column's heading ends where its numbers end (in Arabic both
/// start at the column's left edge); and a small table is as wide as its content,
/// at its reader's margin, instead of being stretched across the page.
/// </para>
/// </remarks>
public sealed class PdfTypeAndTableTests
{
    private const float PageWidth = 841.89f;
    private const float Margin = 40;

    // ------------------------------------------------------------------- type

    [Fact]
    public void Arabic_is_set_in_plex_arabic_and_latin_and_digits_in_geist()
    {
        using PdfText text = new();

        IReadOnlyList<PdfText.ShapedRun> runs = text.Runs("شركة النور 2026 USD", 12, bold: false, rightToLeftParagraph: true);

        Assert.All(
            runs.Where(run => run.Text.Contains("النور", StringComparison.Ordinal) || run.Text.Contains("شركة", StringComparison.Ordinal)),
            run => Assert.Equal("IBMPlexSansArabic-Regular.ttf", run.Face.Name));
        Assert.Equal("Geist-Regular.ttf", Assert.Single(runs, run => run.Text.Contains("2026", StringComparison.Ordinal)).Face.Name);

        IReadOnlyList<PdfText.ShapedRun> bold = text.Runs("الملخص Summary", 12, bold: true);
        Assert.Contains(bold, run => run.Face.Name == "IBMPlexSansArabic-SemiBold.ttf");
        Assert.Contains(bold, run => run.Face.Name == "Geist-SemiBold.ttf");
    }

    [Fact]
    public void Spaces_and_punctuation_stay_with_the_letters_around_them()
    {
        using PdfText text = new();

        // One Arabic phrase is one piece in one face, spaces included, so it is
        // shaped as a whole; the same for a Latin phrase with a dash.
        PdfText.ShapedRun arabic = Assert.Single(text.Runs("خدمة العملاء", 12, bold: false));
        Assert.Equal("IBMPlexSansArabic-Regular.ttf", arabic.Face.Name);

        PdfText.ShapedRun latin = Assert.Single(text.Runs("Support — Sales, 2026", 12, bold: false));
        Assert.Equal("Geist-Regular.ttf", latin.Face.Name);
    }

    [Theory]
    [InlineData("Ελένη Παπαδοπούλου")]
    [InlineData("Алексей Смирнов")]
    [InlineData("שלום")]
    public void A_script_neither_brand_font_draws_is_set_whole_in_dejavu(string name)
    {
        using PdfText text = new();

        IReadOnlyList<PdfText.ShapedRun> runs = text.Runs(name, 12, bold: false);

        // A name in one script is never split across two fonts, and every
        // character is a real glyph.
        Assert.All(runs, run => Assert.Equal("DejaVuSans.ttf", run.Face.Name));
        Assert.All(runs, run => Assert.DoesNotContain(0u, run.Shaped.Codepoints));
    }

    [Fact]
    public void Figures_are_tabular_so_numbers_line_up()
    {
        using PdfText text = new();

        foreach (bool bold in new[] { false, true })
        {
            float ones = text.Measure("1111", 9.5f, bold);

            Assert.Equal(ones, text.Measure("0000", 9.5f, bold), 3);
            Assert.Equal(ones, text.Measure("7777", 9.5f, bold), 3);
        }
    }

    [Fact]
    public async Task An_english_file_embeds_only_geist()
    {
        string raw = Encoding.Latin1.GetString(await Render(Simple(UserLanguage.English, "Rana Haddad")));

        Assert.Contains("Geist-Regular", raw, StringComparison.Ordinal);
        Assert.Contains("Geist-SemiBold", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("IBMPlexSansArabic", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("DejaVu", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_arabic_file_embeds_plex_arabic_and_geist_and_dejavu_only_when_a_value_needs_it()
    {
        string arabic = Encoding.Latin1.GetString(await Render(Simple(UserLanguage.Arabic, "رنا حداد")));

        Assert.Contains("IBMPlexSansArabic", arabic, StringComparison.Ordinal);
        Assert.Contains("Geist-Regular", arabic, StringComparison.Ordinal);
        Assert.DoesNotContain("DejaVu", arabic, StringComparison.Ordinal);

        string greek = Encoding.Latin1.GetString(await Render(Simple(UserLanguage.English, "Ελένη")));

        Assert.Contains("DejaVuSans", greek, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("OFL-Geist.txt")]
    [InlineData("OFL-IBMPlexSansArabic.txt")]
    public void Each_brand_font_s_licence_is_copied_beside_the_build(string file)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Reports", "Rendering", "Fonts", file);

        Assert.True(File.Exists(path), path);
        Assert.Contains("SIL Open Font License", File.ReadAllText(path), StringComparison.Ordinal);
    }

    // ----------------------------------------------------------------- tables

    [Fact]
    public async Task A_heading_too_long_for_its_column_wraps_and_is_never_cut_in_english()
    {
        Page page = FirstPage(await Render(Wide(UserLanguage.English)));
        string text = page.Text;

        Assert.DoesNotContain("…", text, StringComparison.Ordinal);

        foreach (string header in WideHeaders)
        {
            // Lines of one heading follow each other in the text layer.
            Assert.Contains(header.Replace(" ", string.Empty, StringComparison.Ordinal), text.Replace(" ", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        }

        List<Letter> headings = Headings(page);

        Assert.True(headings.Select(letter => Math.Round(letter.StartBaseLine.Y)).Distinct().Count() > 1, "The headings should wrap onto more than one line.");
        Assert.InRange(headings.Min(letter => letter.StartBaseLine.X), Margin - 0.5, PageWidth);
        Assert.InRange(headings.Max(letter => letter.EndBaseLine.X), 0, PageWidth - Margin + 0.5);
    }

    [Fact]
    public async Task A_heading_too_long_for_its_column_wraps_and_is_never_cut_in_arabic()
    {
        Page page = FirstPage(await Render(Wide(UserLanguage.Arabic)));

        Assert.DoesNotContain("…", page.Text, StringComparison.Ordinal);

        List<Letter> headings = Headings(page);

        Assert.True(headings.Select(letter => Math.Round(letter.StartBaseLine.Y)).Distinct().Count() > 1, "The headings should wrap onto more than one line.");
        Assert.InRange(headings.Min(letter => letter.StartBaseLine.X), Margin - 0.5, PageWidth);
        Assert.InRange(headings.Max(letter => letter.EndBaseLine.X), 0, PageWidth - Margin + 0.5);

        // Every value is whole: the widest number of each column is there.
        foreach (string number in new[] { "123,456", "98,765", "4,321" })
        {
            Assert.Contains(page.GetWords(), word => word.Text == number);
        }
    }

    [Fact]
    public async Task A_number_column_s_heading_ends_where_its_numbers_end_in_english()
    {
        Page page = FirstPage(await Render(Simple(UserLanguage.English, "Rana Haddad")));
        List<Word> words = [.. page.GetWords()];

        (double headingLeft, double headingRight) = Span(words.Single(word => word.Text == "Messages"));
        (double bigLeft, double bigRight) = Span(words.Single(word => word.Text == "12,345"));
        (_, double smallRight) = Span(words.Single(word => word.Text == "7"));

        Assert.Equal(bigRight, headingRight, 1);
        Assert.Equal(bigRight, smallRight, 1);
        Assert.True(headingLeft < bigLeft, "The heading is wider than its numbers here, so it should start before them.");

        // A text column's heading starts where its values start.
        List<Letter> customer = [.. Headings(page).Where(letter => letter.StartBaseLine.X < headingLeft)];
        double customerLeft = customer.Min(letter => letter.StartBaseLine.X);
        (double nameLeft, _) = Span(words.First(word => word.Text == "Rana"));
        Assert.Equal(nameLeft, customerLeft, 1);
    }

    [Fact]
    public async Task A_number_column_s_heading_starts_where_its_numbers_start_in_arabic()
    {
        Page page = FirstPage(await Render(Simple(UserLanguage.Arabic, "رنا حداد")));
        List<Word> words = [.. page.GetWords()];

        // The number column's heading is the only Arabic heading in this table.
        List<Letter> heading = [.. Headings(page).Where(letter => IsArabic(letter.Value))];
        Assert.NotEmpty(heading);

        (double bigLeft, double bigRight) = Span(words.Single(word => word.Text == "12,345"));
        (double smallLeft, _) = Span(words.Single(word => word.Text == "7"));

        Assert.Equal(bigLeft, heading.Min(letter => letter.StartBaseLine.X), 1);
        Assert.Equal(bigLeft, smallLeft, 1);
        Assert.NotEqual(bigRight, heading.Max(letter => letter.EndBaseLine.X), 1);

        // The text column's heading (here Latin) ends where its values end: at the right.
        List<Letter> customer = [.. Headings(page).Where(letter => !IsArabic(letter.Value))];
        Assert.Equal(PageWidth - Margin - 5, customer.Max(letter => letter.EndBaseLine.X), 1);
    }

    [Fact]
    public async Task A_small_table_is_as_wide_as_its_content_at_its_reader_s_margin()
    {
        Page english = FirstPage(await Render(Simple(UserLanguage.English, "Rana Haddad")));
        Page arabic = FirstPage(await Render(Simple(UserLanguage.Arabic, "رنا حداد")));

        // Two short columns: in English they end well before the middle of the
        // page; in Arabic they start well after it.
        Assert.True(Span(english.GetWords().Single(word => word.Text == "12,345")).Right < PageWidth / 2);
        Assert.True(Span(arabic.GetWords().Single(word => word.Text == "12,345")).Left > PageWidth / 2);
    }

    // ---------------------------------------------------------------- helpers

    private static readonly string[] WideHeaders =
    [
        "Messages sent by customers during the whole period",
        "Conversations started by customers during the whole period",
        "Conversations answered by the AI during the whole period",
        "Conversations answered by the team during the whole period",
    ];

    private static readonly string[] WideArabicHeaders =
    [
        "الرسائل التي أرسلها العملاء خلال الفترة كاملة",
        "المحادثات التي بدأها العملاء خلال الفترة كاملة",
        "المحادثات التي ردّ عليها الذكاء الاصطناعي خلال الفترة كاملة",
        "المحادثات التي ردّ عليها الفريق خلال الفترة كاملة",
    ];

    private static Task<byte[]> Render(ReportDocument document) =>
        ReportRenderingTests.Render(new PdfReportRenderer(), document);

    private static Page FirstPage(byte[] bytes) => PdfDocument.Open(bytes).GetPage(1);

    /// <summary>Column headings: the only semibold letters at body size (no summary facts in these documents).</summary>
    private static List<Letter> Headings(Page page) =>
    [
        .. page.Letters.Where(letter =>
            Math.Abs(letter.PointSize - 9.5) < 0.01
            && letter.FontName is not null
            && (letter.FontName.Contains("SemiBold", StringComparison.Ordinal) || letter.FontName.Contains("SmBld", StringComparison.Ordinal))
            && !string.IsNullOrWhiteSpace(letter.Value)),
    ];

    /// <summary>A word's extent by its glyphs' origins and advances, as the renderer places them.</summary>
    private static (double Left, double Right) Span(Word word) =>
        (word.Letters.Min(letter => letter.StartBaseLine.X), word.Letters.Max(letter => letter.EndBaseLine.X));

    private static bool IsArabic(string text) =>
        text.Any(character => character is (>= 'ء' and <= 'ۿ') or (>= 'ﭐ' and <= 'ﻼ'));

    private static ReportDocument Simple(UserLanguage language, string name)
    {
        string customer = "Customer";
        string messages = language == UserLanguage.Arabic ? "الرسائل" : "Messages";

        return Document(
            language,
            [new ReportColumn(customer, ReportValueKind.Text), new ReportColumn(messages, ReportValueKind.Count)],
            [
                [ReportValue.Text(name), ReportValue.Count(12345)],
                [ReportValue.Text(name), ReportValue.Count(7)],
            ]);
    }

    private static ReportDocument Wide(UserLanguage language)
    {
        string[] headers = language == UserLanguage.Arabic ? WideArabicHeaders : WideHeaders;

        return Document(
            language,
            [
                new ReportColumn(language == UserLanguage.Arabic ? "العميل" : "Customer", ReportValueKind.Text),
                .. headers.Select(header => new ReportColumn(header, ReportValueKind.Count)),
                new ReportColumn(language == UserLanguage.Arabic ? "آخر نشاط" : "Last activity", ReportValueKind.Instant),
            ],
            [
                [
                    ReportValue.Text("Rana Haddad"),
                    ReportValue.Count(123456),
                    ReportValue.Count(98765),
                    ReportValue.Count(4321),
                    ReportValue.Count(12),
                    ReportValue.Instant(new DateTimeOffset(2026, 10, 5, 14, 15, 0, TimeSpan.Zero)),
                ],
            ]);
    }

    private static ReportDocument Document(UserLanguage language, ReportColumn[] columns, IReadOnlyList<ReportValue>[] rows) =>
        new(
            ReportType.CustomerActivity,
            language == UserLanguage.Arabic ? "نشاط العملاء" : "Customer activity",
            "Northwind Trading",
            ReportPeriod.Create(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 8)),
            new DateTimeOffset(2026, 11, 7, 9, 30, 15, TimeSpan.Zero),
            [],
            [new ReportTable(language == UserLanguage.Arabic ? "العملاء" : "Customers", language == UserLanguage.Arabic ? "العملاء" : "Customers", columns, rows)],
            [],
            language);
}
