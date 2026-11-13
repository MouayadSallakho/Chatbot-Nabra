using System.Text;
using OmniAI.Application.Knowledge.Files;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Exceptions;

namespace OmniAI.Infrastructure.Knowledge.Files;

/// <summary>
/// Reads the text out of a PDF.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Text-bearing PDFs only. There is no OCR, and that is a boundary
/// rather than a gap.</strong> A scanned page is a picture; recognising
/// characters in it is a different capability with its own accuracy, cost and
/// failure modes, and a platform that quietly guessed at one would produce
/// knowledge nobody could trust. A PDF with no text layer is refused with a
/// sentence saying exactly that.
/// </para>
/// <para>
/// <strong>Nothing in the file is executed or followed.</strong> Only page text
/// is read: no embedded JavaScript, no launch actions, no remote references, no
/// attachments, no form submission targets. PdfPig parses; it does not run a
/// viewer.
/// </para>
/// </remarks>
internal static class PdfTextReader
{
    /// <summary>Reads the text, page by page.</summary>
    /// <exception cref="KnowledgeFileRejectedException">
    /// The file is encrypted, malformed, larger than the page bound, or has no
    /// text in it.
    /// </exception>
    public static string Read(byte[] content, KnowledgeFilePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        using PdfDocument document = Open(content);

        if (document.NumberOfPages > policy.MaxPdfPages)
        {
            throw new KnowledgeFileRejectedException(
                KnowledgeFileRejection.TooMuchContent,
                KnowledgeFileRefusals.TooManyPages,
                $"That PDF has {document.NumberOfPages} pages and the limit is "
                + $"{policy.MaxPdfPages}. Split it into smaller documents.",
                limit: policy.MaxPdfPages,
                limitUnit: KnowledgeFileLimitUnits.Pages);
        }

        StringBuilder text = new();

        foreach (Page page in document.GetPages())
        {
            string pageText = ReadPage(page);

            if (pageText.Length == 0)
            {
                continue;
            }

            if (text.Length > 0)
            {
                // A blank line between pages. Chunking is paragraph-aware, so
                // this keeps a sentence that ends one page from being glued to
                // one that begins the next.
                text.Append("\n\n");
            }

            text.Append(pageText);

            if (text.Length > policy.MaxExtractedCharacters)
            {
                throw new KnowledgeFileRejectedException(
                    KnowledgeFileRejection.TooMuchContent,
                    KnowledgeFileRefusals.TooManyCharacters,
                    $"That PDF holds more than {policy.MaxExtractedCharacters:N0} characters of "
                    + "text. Split it into smaller documents.",
                    limit: policy.MaxExtractedCharacters,
                    limitUnit: KnowledgeFileLimitUnits.Characters);
            }
        }

        string extracted = text.ToString().Trim();

        return extracted.Length == 0
            ? throw new KnowledgeFileRejectedException(
                KnowledgeFileRejection.NoText,
                KnowledgeFileRefusals.NoText,
                "No extractable text was found in that PDF. Scanned documents are not supported "
                + "yet — this platform does not read text out of images.")
            : extracted;
    }

    /// <summary>
    /// Opens the document, turning every parser failure into one safe sentence.
    /// </summary>
    /// <remarks>
    /// <strong>No parser detail reaches the caller.</strong> An offset, an object
    /// number or a library's own wording describes this deployment's internals to
    /// whoever chose the file — and tells them how their malformed input was
    /// handled, which is precisely what somebody probing a parser wants to learn.
    /// </remarks>
    private static PdfDocument Open(byte[] content)
    {
        try
        {
            return PdfDocument.Open(content);
        }
        catch (PdfDocumentEncryptedException)
        {
            // The one parser failure worth telling apart (Phase 37.5a): the fix is
            // "unlock it", not "repair it". Same status and the same sentence as
            // a damaged file; only the code differs, and it says nothing about the
            // file beyond what its owner already knows.
            throw Unreadable(KnowledgeFileRefusals.Encrypted);
        }
#pragma warning disable CA1031 // Every parser failure has one safe answer.
        catch (Exception)
#pragma warning restore CA1031
        {
            throw Unreadable(KnowledgeFileRefusals.Damaged);
        }
    }

    private static KnowledgeFileRejectedException Unreadable(string code) =>
        new(
            KnowledgeFileRejection.Unreadable,
            code,
            "That PDF could not be read. It may be damaged, or password-protected — "
            + "protected files must be unlocked before uploading.");

    /// <summary>
    /// One page's text, with words rejoined into lines.
    /// </summary>
    /// <remarks>
    /// PdfPig's raw text follows the order operators appear in the content
    /// stream, which for a multi-column layout interleaves the columns. Reading
    /// words and grouping them by their vertical position produces something a
    /// person would recognise, and — more to the point — something deterministic:
    /// the same file must always produce the same text, or its fingerprint moves
    /// and it re-indexes for nothing.
    /// <para>
    /// <strong>The baseline is what says which line a word sits on, not its
    /// bounding box.</strong> A glyph box is as tall as the tallest letter in it,
    /// so on one printed line "Goods" and "may" have tops several points apart --
    /// far enough to look like two lines and break a sentence into fragments that
    /// retrieval then has to make sense of. Every word on a printed line shares a
    /// baseline, whatever letters happen to be in it.
    /// </para>
    /// </remarks>
    private static string ReadPage(Page page)
    {
        List<Word> words = [.. page.GetWords()];

        if (words.Count == 0)
        {
            return string.Empty;
        }

        StringBuilder line = new();
        StringBuilder text = new();

        double? baseline = null;

        foreach (Word word in words)
        {
            double current = BaselineOf(word);

            // A new line when the vertical position moves by more than a rounding
            // wobble. Two points is far below any real line height and far above
            // the jitter within one.
            if (baseline is double previous && Math.Abs(previous - current) > 2)
            {
                text.Append(line).Append('\n');
                line.Clear();
            }

            baseline = current;

            if (line.Length > 0)
            {
                line.Append(' ');
            }

            line.Append(word.Text);
        }

        text.Append(line);

        return text.ToString().Trim();
    }

    /// <summary>The line a word sits on.</summary>
    /// <remarks>
    /// Taken from the first letter, which carries the baseline the whole word was
    /// drawn along. The bounding box is the fallback for the case PdfPig cannot
    /// give letters for; it is worse, but it is better than no line at all.
    /// </remarks>
    private static double BaselineOf(Word word) =>
        word.Letters.Count > 0 ? word.Letters[0].StartBaseLine.Y : word.BoundingBox.Bottom;
}
