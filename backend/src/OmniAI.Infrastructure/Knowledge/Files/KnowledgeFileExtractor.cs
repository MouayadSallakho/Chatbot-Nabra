using OmniAI.Application.Knowledge.Files;
using OmniAI.Domain.Knowledge;

namespace OmniAI.Infrastructure.Knowledge.Files;

/// <summary>
/// Turns an uploaded file into plain text.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The order is: bound, identify, read.</strong> Size is checked before
/// anything looks at the bytes, the format is decided from the bytes rather than
/// the name, and only then does a parser see the file. Each step refuses rather
/// than repairs — nothing here truncates, converts or guesses.
/// </para>
/// <para>
/// <strong>The file does not survive this call.</strong> It arrives as an array,
/// is read, and goes out of scope with the request. Nothing is written to disk,
/// nothing is cached, and there is no code path that could keep it: what remains
/// of an upload is the extracted text and a digest of the bytes.
/// </para>
/// <para>
/// <strong>Nothing here logs.</strong> The content is a workspace's business
/// information and the filename may itself be sensitive — a customer's name on an
/// invoice, a project nobody has announced. What an operator needs in order to
/// diagnose an extraction problem travels back in the rejection reason.
/// </para>
/// </remarks>
internal sealed class KnowledgeFileExtractor(KnowledgeFilePolicy policy) : IKnowledgeFileExtractor
{
    private readonly KnowledgeFilePolicy _policy =
        policy ?? throw new ArgumentNullException(nameof(policy));

    public KnowledgeExtraction Extract(KnowledgeFileUpload upload)
    {
        ArgumentNullException.ThrowIfNull(upload);
        ArgumentNullException.ThrowIfNull(upload.Content);

        if (upload.Content.LongLength > _policy.MaxFileBytes)
        {
            throw new KnowledgeFileRejectedException(
                KnowledgeFileRejection.TooLarge,
                $"That file is larger than {_policy.MaxFileMegabytes} MB. Split it into smaller "
                + "documents, or upload the part that matters.");
        }

        KnowledgeFileType type = KnowledgeFileSignature.Identify(upload);

        string text = type switch
        {
            KnowledgeFileType.Pdf => PdfTextReader.Read(upload.Content, _policy),
            KnowledgeFileType.Docx => DocxTextReader.Read(upload.Content, _policy),
            KnowledgeFileType.Text => ReadPlainText(upload.Content),
            KnowledgeFileType.Csv => CsvTextReader.Read(ReadPlainText(upload.Content), _policy),
            _ => throw new KnowledgeFileRejectedException(
                KnowledgeFileRejection.UnsupportedType,
                "That kind of file is not supported. Upload a PDF, DOCX, TXT or CSV."),
        };

        // Checked once more after extraction, because a parser bound is about how
        // much a parser will read and this is about what a document may hold. A
        // PDF of a thousand short pages passes the page bound and can still
        // produce more text than a document stores.
        if (text.Length > _policy.MaxExtractedCharacters)
        {
            throw new KnowledgeFileRejectedException(
                KnowledgeFileRejection.TooMuchContent,
                $"That file holds more than {_policy.MaxExtractedCharacters:N0} characters of "
                + "text. Split it into smaller documents.");
        }

        return new KnowledgeExtraction(text, type);
    }

    /// <summary>
    /// Reads a text or CSV file.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>UTF-8, with or without a byte order mark, and nothing else.</strong>
    /// Guessing an encoding means getting it wrong for some file, and the symptom
    /// is a workspace's knowledge quietly full of mojibake that nobody notices
    /// until an agent quotes it. A file in another encoding is refused with a
    /// sentence telling the person how to fix it, which they can.
    /// </para>
    /// <para>
    /// Binary content named <c>.txt</c> is refused before decoding. A NUL byte is
    /// the test: it cannot appear in text somebody wrote, and it appears almost
    /// immediately in anything that is not text.
    /// </para>
    /// </remarks>
    private static string ReadPlainText(byte[] content)
    {
        if (!KnowledgeFileSignature.LooksTextual(content))
        {
            throw new KnowledgeFileRejectedException(
                KnowledgeFileRejection.TypeMismatch,
                "That file is named like text but contains binary data. Upload a PDF, DOCX, TXT "
                + "or CSV.");
        }

        string text = KnowledgeFileSignature.DecodeUtf8(content);

        // Folded here as well as in the document's own value object, so the text
        // a CSV reader sees has already had its line endings settled and a file
        // saved on Windows fingerprints the same as one saved on a Mac.
        return text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
    }
}
