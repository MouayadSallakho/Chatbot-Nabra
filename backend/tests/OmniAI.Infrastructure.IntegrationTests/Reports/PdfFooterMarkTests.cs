using System.Text;
using OmniAI.Application.Reports;
using OmniAI.Domain.Reports;
using OmniAI.Domain.Users;
using OmniAI.Infrastructure.Reports.Rendering;
using SkiaSharp;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace OmniAI.Infrastructure.IntegrationTests.Reports;

/// <summary>
/// The brand in a report file's footer (Phase 39.8a): the two circles drawn as
/// vectors and the product's name in the reader's language, at the start of the
/// footer line in each direction, before the title; the page number unchanged.
/// </summary>
public sealed class PdfFooterMarkTests
{
    private const double PageWidth = 841.89;
    private const double Margin = 40;
    private const double FooterBaseline = 40; // PdfPig's y runs up from the bottom edge.

    [Fact]
    public async Task An_english_footer_starts_with_the_mark_and_nabra_then_the_title()
    {
        ReportDocument document = await ReportFixtures.BuildAsync(ReportType.ConversationActivity);
        using PdfDocument pdf = PdfDocument.Open(await Render(document));

        foreach (Page page in pdf.GetPages())
        {
            List<Word> footer = Footer(page);
            Word name = Assert.Single(footer, word => word.Text == "Nabra");

            // The mark's box fills the space before the name, from the left margin.
            Assert.Equal(Margin + PdfReportRenderer.FooterMarkWidth + 3, name.Letters[0].StartBaseLine.X, 1);
            Assert.DoesNotContain(footer, word => word.BoundingBox.Left < name.BoundingBox.Left - 0.5);

            // Then the title, then the page number at the right margin.
            Word title = footer.First(word => word.Text == document.Title.Split(' ')[0]);
            Assert.True(title.BoundingBox.Left > name.BoundingBox.Right + 5);
            Word last = footer.MaxBy(word => word.BoundingBox.Right)!;
            Assert.Equal(PageWidth - Margin, last.BoundingBox.Right, 0);
            Assert.Contains(footer, word => word.Text == "Page");
        }
    }

    [Fact]
    public async Task An_arabic_footer_starts_at_the_right_with_the_mark_and_the_arabic_name()
    {
        ReportDocument document = await ReportFixtures.BuildAsync(ReportType.ConversationActivity, language: UserLanguage.Arabic);
        using PdfDocument pdf = PdfDocument.Open(await Render(document));
        Page page = pdf.GetPage(1);

        List<Letter> footer = [.. page.Letters.Where(letter => Math.Abs(letter.StartBaseLine.Y - FooterBaseline) < 1.5 && !string.IsNullOrWhiteSpace(letter.Value))];
        double rightmost = footer.Max(letter => Math.Max(letter.StartBaseLine.X, letter.EndBaseLine.X));

        // The name ends where the mark's box begins, at the right margin.
        Assert.Equal(PageWidth - Margin - PdfReportRenderer.FooterMarkWidth - 3, rightmost, 1);

        // The Arabic name is in the text layer beside the mark (in base or
        // presentation forms, as the font maps them), and the Latin one is not.
        Letter nearest = footer.MaxBy(letter => Math.Max(letter.StartBaseLine.X, letter.EndBaseLine.X))!;
        Assert.True(IsArabic(nearest.Value), nearest.Value);
        Assert.DoesNotContain("Nabra", string.Concat(footer.Select(letter => letter.Value)), StringComparison.Ordinal);

        // The page number keeps the left margin.
        Assert.Equal(Margin, footer.Min(letter => Math.Min(letter.StartBaseLine.X, letter.EndBaseLine.X)), 0);
    }

    [Fact]
    public void The_mark_is_the_two_circles_and_their_overlap_never_mirrored()
    {
        // Drawn at 20x so each part is many pixels wide.
        using SKBitmap bitmap = new(400, 300);
        using SKCanvas canvas = new(bitmap);

        canvas.Clear(SKColors.White);
        canvas.Scale(20);
        PdfReportRenderer.DrawMark(canvas, 1, 12);
        canvas.Flush();

        float box = PdfReportRenderer.FooterMarkWidth;
        float scale = box / 140;
        float middle = 12 - (8 * 0.36f);

        SKColor At(float markX) => bitmap.GetPixel((int)((1 + (markX * scale)) * 20), (int)(middle * 20));

        // Emerald on the left, the deep overlap in the middle, gold on the right.
        Assert.Equal(new SKColor(0x23, 0x53, 0x47), At(26));
        Assert.Equal(new SKColor(0x0B, 0x2B, 0x26), At(70));
        Assert.Equal(new SKColor(0xC8, 0xA9, 0x6A), At(114));
    }

    [Fact]
    public async Task The_mark_is_paths_not_an_image()
    {
        foreach (UserLanguage language in new[] { UserLanguage.English, UserLanguage.Arabic })
        {
            string raw = Encoding.Latin1.GetString(await Render(await ReportFixtures.BuildAsync(ReportType.ProviderUsage, language: language)));

            Assert.DoesNotContain("/Subtype /Image", raw, StringComparison.Ordinal);
        }
    }

    private static bool IsArabic(string value) =>
        value.Any(c => c is >= '؀' and <= 'ۿ' or >= 'ﭐ' and <= '﷿' or >= 'ﹰ' and <= '﻿');

    private static Task<byte[]> Render(ReportDocument document) =>
        ReportRenderingTests.Render(new PdfReportRenderer(), document);

    private static List<Word> Footer(Page page) =>
        [.. page.GetWords().Where(word => Math.Abs(word.Letters[0].StartBaseLine.Y - FooterBaseline) < 1.5)];
}
