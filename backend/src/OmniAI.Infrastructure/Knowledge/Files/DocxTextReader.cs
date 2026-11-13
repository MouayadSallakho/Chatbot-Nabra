using System.IO.Compression;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using OmniAI.Application.Knowledge.Files;

namespace OmniAI.Infrastructure.Knowledge.Files;

/// <summary>
/// Reads the text out of a Word document.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A DOCX is a zip, and a zip from outside is a weapon until
/// checked.</strong> A few kilobytes can declare gigabytes of content, and a
/// reader that believes the declaration allocates them. So the container is
/// inspected before any parser opens it: entry count, declared expanded size,
/// and compression ratio. That check is the reason this file exists rather than
/// a one-line call to the OpenXML SDK.
/// </para>
/// <para>
/// <strong>Only the main document part is read.</strong> Not headers, not
/// footnotes, not comments, not embedded objects, and certainly not macros —
/// this platform never asks the SDK for the VBA part, and the SDK does not run
/// anything in any case. Text a person would see in the body is what becomes
/// knowledge; everything else is ignored rather than refused, because a
/// perfectly ordinary document often carries one.
/// </para>
/// <para>
/// Word and LibreOffice are not involved. There is no process launched, no shell
/// invoked and no file written; the bytes are parsed in memory and discarded.
/// </para>
/// </remarks>
internal static class DocxTextReader
{
    /// <summary>
    /// The most a container may claim to expand by.
    /// </summary>
    /// <remarks>
    /// A real document's XML compresses well — twenty to one is unremarkable —
    /// but a zip bomb aims for thousands to one. Two hundred is far above any
    /// honest document and far below anything worth defending against.
    /// </remarks>
    private const long MaxCompressionRatio = 200;

    /// <summary>Reads the body text.</summary>
    /// <exception cref="KnowledgeFileRejectedException">
    /// The container is malformed, unsafe, or holds more text than a knowledge
    /// document may.
    /// </exception>
    public static string Read(byte[] content, KnowledgeFilePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(policy);

        InspectArchive(content, policy);

        using MemoryStream stream = new(content, writable: false);

        string text = ReadBody(stream, policy);

        return text.Length == 0
            ? throw new KnowledgeFileRejectedException(
                KnowledgeFileRejection.NoText,
                KnowledgeFileRefusals.NoText,
                "No text was found in that document. If its content is images, this platform "
                + "cannot read it — there is no text recognition.")
            : text;
    }

    /// <summary>
    /// Checks the container before anything parses it.
    /// </summary>
    /// <remarks>
    /// Reads only the central directory: entry names and their declared sizes.
    /// Nothing is decompressed here, so a hostile archive is refused without a
    /// byte of it being expanded.
    /// </remarks>
    private static void InspectArchive(byte[] content, KnowledgeFilePolicy policy)
    {
        ZipArchive archive;

        try
        {
            archive = new ZipArchive(new MemoryStream(content, writable: false), ZipArchiveMode.Read);
        }
#pragma warning disable CA1031 // Every container failure has one safe answer.
        catch (Exception)
#pragma warning restore CA1031
        {
            throw Malformed();
        }

        using (archive)
        {
            try
            {
                if (archive.Entries.Count > policy.MaxArchiveEntries)
                {
                    throw new KnowledgeFileRejectedException(
                        KnowledgeFileRejection.TooMuchContent,
                        KnowledgeFileRefusals.ArchiveTooLarge,
                        "That document contains too many parts to read safely.",
                        limit: policy.MaxArchiveEntries,
                        limitUnit: KnowledgeFileLimitUnits.Entries);
                }

                long declared = 0;

                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    declared += entry.Length;

                    if (declared > policy.MaxArchiveExpandedBytes)
                    {
                        throw new KnowledgeFileRejectedException(
                            KnowledgeFileRejection.TooMuchContent,
                            KnowledgeFileRefusals.ArchiveTooLarge,
                            "That document expands to more content than this platform will read.",
                            limit: policy.MaxArchiveExpandedBytes,
                            limitUnit: KnowledgeFileLimitUnits.Bytes);
                    }

                    // A single entry claiming an implausible ratio is the shape
                    // of a zip bomb, whatever the total says.
                    if (entry.CompressedLength > 0
                        && entry.Length / entry.CompressedLength > MaxCompressionRatio)
                    {
                        throw new KnowledgeFileRejectedException(
                            KnowledgeFileRejection.TooMuchContent,
                            KnowledgeFileRefusals.Damaged,
                            "That document could not be read safely. It may be damaged.");
                    }
                }
            }
            catch (InvalidDataException)
            {
                throw Malformed();
            }
        }
    }

    /// <summary>The body's paragraphs, one per line.</summary>
    private static string ReadBody(Stream stream, KnowledgeFilePolicy policy)
    {
        try
        {
            using WordprocessingDocument document =
                WordprocessingDocument.Open(stream, isEditable: false);

            Body? body = document.MainDocumentPart?.Document?.Body;

            if (body is null)
            {
                throw Malformed();
            }

            StringBuilder text = new();

            // Paragraph by paragraph, because a paragraph is the unit a person
            // wrote and the unit chunking later looks for. Runs within one are
            // joined without separation: Word splits a sentence into runs
            // wherever formatting changes, and a space at each boundary would put
            // one inside every bolded word.
            foreach (Paragraph paragraph in body.Descendants<Paragraph>())
            {
                string line = ReadParagraph(paragraph);

                if (line.Length == 0)
                {
                    continue;
                }

                text.Append(line).Append('\n');

                if (text.Length > policy.MaxExtractedCharacters)
                {
                    throw new KnowledgeFileRejectedException(
                        KnowledgeFileRejection.TooMuchContent,
                        KnowledgeFileRefusals.TooManyCharacters,
                        $"That document holds more than {policy.MaxExtractedCharacters:N0} "
                        + "characters of text. Split it into smaller documents.",
                        limit: policy.MaxExtractedCharacters,
                        limitUnit: KnowledgeFileLimitUnits.Characters);
                }
            }

            return text.ToString().Trim();
        }
        catch (KnowledgeFileRejectedException)
        {
            throw;
        }
#pragma warning disable CA1031 // Every parser failure has one safe answer.
        catch (Exception)
#pragma warning restore CA1031
        {
            throw Malformed();
        }
    }

    /// <summary>
    /// One paragraph's visible text.
    /// </summary>
    /// <remarks>
    /// Text runs and tabs only. A drawing, a chart, an embedded spreadsheet or an
    /// OLE object contributes nothing rather than failing the upload: a document
    /// with a logo in it is an ordinary document, and its words are still worth
    /// reading.
    /// </remarks>
    private static string ReadParagraph(Paragraph paragraph)
    {
        StringBuilder line = new();

        foreach (OpenXmlElement element in paragraph.Descendants())
        {
            switch (element)
            {
                case Text run:
                    line.Append(run.Text);
                    break;

                case TabChar:
                    line.Append('\t');
                    break;

                case Break:
                    line.Append(' ');
                    break;

                default:
                    break;
            }
        }

        return line.ToString().Trim();
    }

    private static KnowledgeFileRejectedException Malformed() =>
        new(
            KnowledgeFileRejection.Unreadable,
            KnowledgeFileRefusals.Damaged,
            "That Word document could not be read. It may be damaged, or saved in a format this "
            + "platform does not support — re-save it as .docx and try again.");
}
