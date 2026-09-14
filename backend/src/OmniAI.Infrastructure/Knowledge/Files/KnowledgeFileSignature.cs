using System.Text;
using OmniAI.Application.Knowledge.Files;
using OmniAI.Domain.Knowledge;

namespace OmniAI.Infrastructure.Knowledge.Files;

/// <summary>
/// Works out what an uploaded file actually is.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The bytes decide, not the name.</strong> An extension is chosen by
/// whoever uploaded the file and a content type is chosen by their browser;
/// neither is evidence. A file called <c>policy.pdf</c> that begins
/// <c>PK\x03\x04</c> is a zip, and handing it to a PDF reader on the strength of
/// its name is how a parser ends up somewhere it was never meant to go.
/// </para>
/// <para>
/// <strong>The name still matters, for one thing.</strong> Plain text and CSV are
/// the same bytes — there is no signature that tells them apart, because a CSV
/// <em>is</em> text. So the extension picks between those two and only those two,
/// and both are read by code that treats the content as inert either way. The
/// worst outcome of getting it wrong is a CSV rendered as prose.
/// </para>
/// <para>
/// A file whose signature contradicts its extension is refused rather than
/// silently reinterpreted. Somebody who uploaded a spreadsheet named
/// <c>.docx</c> needs to be told, not to have it quietly fail later.
/// </para>
/// </remarks>
internal static class KnowledgeFileSignature
{
    /// <summary>A PDF begins with this, per the specification.</summary>
    private static ReadOnlySpan<byte> PdfMagic => "%PDF-"u8;

    /// <summary>Every zip-based container begins with a local file header.</summary>
    private static ReadOnlySpan<byte> ZipMagic => [0x50, 0x4B, 0x03, 0x04];

    /// <summary>The UTF-8 byte order mark.</summary>
    internal static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];

    /// <summary>
    /// Decides what this upload is, or refuses it.
    /// </summary>
    /// <exception cref="KnowledgeFileRejectedException">
    /// The format is not supported, or the bytes contradict the name.
    /// </exception>
    public static KnowledgeFileType Identify(KnowledgeFileUpload upload)
    {
        ArgumentNullException.ThrowIfNull(upload);

        ReadOnlySpan<byte> content = upload.Content;

        if (content.IsEmpty)
        {
            throw new KnowledgeFileRejectedException(
                KnowledgeFileRejection.Unreadable,
                "That file is empty.");
        }

        string extension = ExtensionOf(upload.FileName);

        bool looksPdf = content.StartsWith(PdfMagic);
        bool looksZip = content.StartsWith(ZipMagic);

        // Signature first, where there is one. A file that is unmistakably a PDF
        // or a zip container is treated as what it is, and the name only has to
        // agree.
        if (looksPdf)
        {
            return Agreeing(extension, ".pdf", KnowledgeFileType.Pdf);
        }

        if (looksZip)
        {
            return Agreeing(extension, ".docx", KnowledgeFileType.Docx);
        }

        // No binary signature. Only the two text formats are left, and the
        // extension is what distinguishes them.
        return extension switch
        {
            ".txt" => KnowledgeFileType.Text,
            ".csv" => KnowledgeFileType.Csv,
            ".pdf" or ".docx" => throw new KnowledgeFileRejectedException(
                KnowledgeFileRejection.TypeMismatch,
                $"That file is named like a {extension.TrimStart('.').ToUpperInvariant()} but is "
                + "not one. Check the file and try again."),
            _ => throw new KnowledgeFileRejectedException(
                KnowledgeFileRejection.UnsupportedType,
                "That kind of file is not supported. Upload a PDF, DOCX, TXT or CSV."),
        };
    }

    /// <summary>
    /// Whether these bytes look like text rather than something binary.
    /// </summary>
    /// <remarks>
    /// <strong>A NUL byte is the test.</strong> It cannot occur in valid UTF-8
    /// text a person wrote, and it occurs almost immediately in every binary
    /// format. Anything subtler — counting control characters, guessing at
    /// encodings — starts making decisions about files it does not understand,
    /// and the honest answer for those is a refusal rather than a guess.
    /// </remarks>
    public static bool LooksTextual(ReadOnlySpan<byte> content) => !content.Contains((byte)0);

    /// <summary>The lower-cased final extension of a name, including the dot.</summary>
    private static string ExtensionOf(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return string.Empty;
        }

        int dot = fileName.LastIndexOf('.');

        return dot < 0 || dot == fileName.Length - 1
            ? string.Empty
            : fileName[dot..].ToLowerInvariant();
    }

    /// <summary>
    /// Accepts a signature-identified type when the name does not contradict it.
    /// </summary>
    /// <remarks>
    /// A missing extension is allowed: plenty of legitimate uploads have none,
    /// and the signature has already settled what the file is. A <em>wrong</em>
    /// extension is refused, because that is a file somebody has mislabelled and
    /// they should know.
    /// </remarks>
    private static KnowledgeFileType Agreeing(
        string extension,
        string expected,
        KnowledgeFileType type)
    {
        if (extension.Length == 0 || string.Equals(extension, expected, StringComparison.Ordinal))
        {
            return type;
        }

        throw new KnowledgeFileRejectedException(
            KnowledgeFileRejection.TypeMismatch,
            $"That file is a {expected.TrimStart('.').ToUpperInvariant()} but is named "
            + $"\"{extension}\". Rename it and try again.");
    }

    /// <summary>Decodes UTF-8, skipping a byte order mark if one is present.</summary>
    /// <exception cref="KnowledgeFileRejectedException">
    /// The bytes are not valid UTF-8.
    /// </exception>
    public static string DecodeUtf8(ReadOnlySpan<byte> content)
    {
        ReadOnlySpan<byte> body = content.StartsWith(Utf8Bom) ? content[Utf8Bom.Length..] : content;

        try
        {
            // Throwing rather than substituting. A replacement character quietly
            // inserted into a workspace's knowledge is a corruption nobody sees
            // until an agent answers with it.
            return new UTF8Encoding(false, true).GetString(body);
        }
        catch (DecoderFallbackException)
        {
            throw new KnowledgeFileRejectedException(
                KnowledgeFileRejection.Unreadable,
                "That file is not valid UTF-8 text. Save it as UTF-8 and try again.");
        }
    }
}
