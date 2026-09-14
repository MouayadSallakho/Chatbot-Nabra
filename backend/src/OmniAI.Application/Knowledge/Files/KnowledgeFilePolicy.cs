using OmniAI.Domain.Knowledge;

namespace OmniAI.Application.Knowledge.Files;

/// <summary>
/// What this platform will accept from an upload, and how far it will read.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Every bound here exists because the file is hostile until proven
/// otherwise.</strong> An upload is bytes from outside; a parser handed
/// unbounded input is a way to spend all the memory on the machine, and "it was
/// only a Word document" is exactly what such a file would look like.
/// </para>
/// <para>
/// <strong>Nothing is ever truncated to fit.</strong> A file that exceeds a bound
/// is refused and says so. Silently keeping the first half of somebody's policy
/// document would produce an agent that answers confidently from a text nobody
/// knows is incomplete, which is worse than refusing the upload.
/// </para>
/// <para>
/// The values are deliberately conservative. They are a settable policy rather
/// than constants so a deployment with different needs can raise them
/// deliberately, and so a test can drive a tiny bound without generating a
/// ten-megabyte fixture.
/// </para>
/// </remarks>
public sealed class KnowledgeFilePolicy
{
    /// <summary>
    /// The largest upload accepted, in bytes.
    /// </summary>
    /// <remarks>
    /// Ten megabytes. Far beyond any policy document a business writes, and small
    /// enough that a request can be read into memory without thought. The HTTP
    /// edge enforces the same number, so an oversized body is refused before it
    /// is fully read rather than after.
    /// </remarks>
    public long MaxFileBytes { get; init; } = 10 * 1024 * 1024;

    /// <summary>
    /// The most characters any file may yield.
    /// </summary>
    /// <remarks>
    /// The knowledge document's own maximum, which remains authoritative: text
    /// that would not fit in a document is not text this platform can store, and
    /// discovering that after extraction rather than before would waste the work
    /// and produce a worse message.
    /// </remarks>
    public int MaxExtractedCharacters { get; init; } = KnowledgeContent.MaxLength;

    /// <summary>The most PDF pages read before the file is refused.</summary>
    /// <remarks>
    /// Five hundred pages is a manual, not a policy. A document that long should
    /// be split by the person who owns it, so the chunks retrieval returns are
    /// about one subject rather than about a book.
    /// </remarks>
    public int MaxPdfPages { get; init; } = 500;

    /// <summary>
    /// The most entries a DOCX container may hold.
    /// </summary>
    /// <remarks>
    /// A zip with tens of thousands of entries is not a Word document; it is an
    /// attempt to make the reader spend its time on directory bookkeeping.
    /// </remarks>
    public int MaxArchiveEntries { get; init; } = 512;

    /// <summary>
    /// The most bytes a DOCX container may expand to once decompressed.
    /// </summary>
    /// <remarks>
    /// <strong>This is the decompression-bomb bound.</strong> A few kilobytes of
    /// zip can declare gigabytes of content, and a reader that trusts the
    /// declaration allocates them. Sixty-four megabytes is generous for the XML
    /// behind a real document and bounded enough that the worst case is a refused
    /// upload rather than a dead process.
    /// </remarks>
    public long MaxArchiveExpandedBytes { get; init; } = 64 * 1024 * 1024;

    /// <summary>The most CSV records rendered.</summary>
    public int MaxCsvRows { get; init; } = 5_000;

    /// <summary>The most columns read from a CSV.</summary>
    public int MaxCsvColumns { get; init; } = 64;

    /// <summary>The most characters kept from any one CSV cell.</summary>
    /// <remarks>
    /// A cell longer than this is a document that has been pasted into a
    /// spreadsheet, and rendering it whole would drown every other record in the
    /// file. It is refused rather than trimmed, for the same reason nothing else
    /// here is trimmed.
    /// </remarks>
    public int MaxCsvCellCharacters { get; init; } = 4_000;

    /// <summary>What an administrator is told the limit is.</summary>
    public int MaxFileMegabytes => (int)(MaxFileBytes / (1024 * 1024));
}
