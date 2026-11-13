using OmniAI.Domain.Knowledge;

namespace OmniAI.Application.Knowledge.Files;

/// <summary>
/// An uploaded file, as the Application sees one.
/// </summary>
/// <param name="FileName">Whatever the browser sent. Not yet sanitised.</param>
/// <param name="DeclaredContentType">
/// What the client claimed the file is. A hint and never more than that: it is
/// chosen by the sender.
/// </param>
/// <param name="Content">
/// The bytes, already read and already bounded by the transport.
/// </param>
/// <remarks>
/// <strong>An array rather than a stream, on purpose.</strong> Every supported
/// parser needs to seek, the size is bounded well below anything worth streaming,
/// and a stream would mean the bytes outlive the request in a way this phase
/// deliberately avoids. When the request ends, this object is the only thing that
/// held the file, and it goes with it.
/// </remarks>
public sealed record KnowledgeFileUpload(
    string? FileName,
    string? DeclaredContentType,
    byte[] Content);

/// <summary>Why a file was refused.</summary>
/// <remarks>
/// The reason is carried as a value rather than inferred from a message, because
/// the HTTP edge maps each to a different status and a string comparison there
/// would be a bug waiting for somebody to reword an error.
/// </remarks>
public enum KnowledgeFileRejection
{
    /// <summary>The file is larger than this platform accepts.</summary>
    TooLarge = 1,

    /// <summary>The format is not one of the four this phase supports.</summary>
    UnsupportedType = 2,

    /// <summary>
    /// The file is not what it claims to be: the bytes do not match the extension
    /// or the declared type.
    /// </summary>
    TypeMismatch = 3,

    /// <summary>The file is empty, corrupt, or could not be read as its format.</summary>
    Unreadable = 4,

    /// <summary>
    /// The file was read and contained no text — a scanned PDF, or an empty
    /// document.
    /// </summary>
    NoText = 5,

    /// <summary>
    /// The file is structurally sound but exceeds a parser bound: too many pages,
    /// too many rows, or more text than a knowledge document may hold.
    /// </summary>
    TooMuchContent = 6,
}

/// <summary>
/// A file this platform will not ingest, with wording safe to show anybody.
/// </summary>
/// <remarks>
/// <strong>The message never carries parser detail.</strong> A stack trace, an
/// offset, or a library's own wording would describe this deployment's internals
/// to whoever uploaded the file — and the person reading it needs to know what to
/// do, not what threw.
/// <para>
/// <strong>The reason picks the status; the code names the rule (Phase 37.5a).</strong>
/// Six reasons are enough to choose between 413, 415 and 422, and far too few to
/// tell a reader what to do: "too many pages" and "a cell is too long" are both
/// <see cref="KnowledgeFileRejection.TooMuchContent"/>. Each throw site names its
/// <see cref="KnowledgeFileRefusals"/> code, and the bound it broke where it has
/// one, so a client words the refusal without reading the English sentence.
/// </para>
/// </remarks>
public sealed class KnowledgeFileRejectedException : Exception
{
    /// <summary>A refusal with the reason's general code.</summary>
    public KnowledgeFileRejectedException(KnowledgeFileRejection reason, string message)
        : this(reason, KnowledgeFileRefusals.For(reason), message)
    {
    }

    /// <summary>A refusal naming the exact rule, and the bound it broke.</summary>
    /// <param name="reason">Decides the status.</param>
    /// <param name="code">One of <see cref="KnowledgeFileRefusals"/>.</param>
    /// <param name="message">The English sentence for a person.</param>
    /// <param name="limit">The bound, in the rule's own unit, when it has one.</param>
    /// <param name="limitUnit">
    /// One of <see cref="KnowledgeFileLimitUnits"/>; required with
    /// <paramref name="limit"/> and refused without it (Phase 38.7b, BA-12).
    /// </param>
    public KnowledgeFileRejectedException(
        KnowledgeFileRejection reason,
        string code,
        string message,
        long? limit = null,
        string? limitUnit = null)
        : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        // A number without its unit is the ambiguity BA-12 removed: an archive
        // bound can count entries or bytes. Both or neither, decided here.
        if ((limit is null) != (limitUnit is null))
        {
            throw new ArgumentException("A limit and its unit are given together, or not at all.", nameof(limitUnit));
        }

        if (limitUnit is not null && !KnowledgeFileLimitUnits.All.Contains(limitUnit))
        {
            throw new ArgumentOutOfRangeException(nameof(limitUnit), limitUnit, "Not a published limit unit.");
        }

        Reason = reason;
        Code = code;
        Limit = limit;
        LimitUnit = limitUnit;
    }

    public KnowledgeFileRejection Reason { get; }

    /// <summary>The stable code, published as the problem's <c>code</c>.</summary>
    public string Code { get; }

    /// <summary>
    /// The bound that was broken — bytes, pages, characters, rows, columns or
    /// entries, as the code says — published as the problem's <c>limit</c>.
    /// </summary>
    public long? Limit { get; }

    /// <summary>
    /// What <see cref="Limit"/> counts, published as the problem's <c>limitUnit</c>
    /// beside it (Phase 38.7b, BA-12); <see langword="null"/> exactly when
    /// <see cref="Limit"/> is.
    /// </summary>
    public string? LimitUnit { get; }
}

/// <summary>
/// What a refused file's <c>limit</c> counts: the <c>limitUnit</c> member beside it
/// (Phase 38.7b, BA-12).
/// </summary>
/// <remarks>
/// Kept apart from <see cref="KnowledgeFileRefusals"/>, whose constants are all
/// problem codes. Listed in <c>docs/architecture/problem-codes.md</c>; a unit, once
/// published, keeps its meaning.
/// </remarks>
public static class KnowledgeFileLimitUnits
{
    public const string Bytes = "bytes";
    public const string Pages = "pages";
    public const string Characters = "characters";
    public const string Rows = "rows";
    public const string Columns = "columns";
    public const string Entries = "entries";

    /// <summary>Every published unit.</summary>
    public static IReadOnlySet<string> All { get; } =
        new HashSet<string>(StringComparer.Ordinal) { Bytes, Pages, Characters, Rows, Columns, Entries };
}

/// <summary>
/// The stable codes on a refused knowledge file (Phase 37.5a, row 17).
/// </summary>
/// <remarks>
/// Listed in <c>docs/architecture/problem-codes.md</c>. A code, once published,
/// keeps its meaning; a new rule gets a new code.
/// </remarks>
public static class KnowledgeFileRefusals
{
    /// <summary>Larger than the upload limit (<c>limit</c>: bytes). 413.</summary>
    public const string TooLarge = "knowledge.file.too_large";

    /// <summary>Not PDF, DOCX, TXT or CSV. 415.</summary>
    public const string UnsupportedType = "knowledge.file.unsupported_type";

    /// <summary>The bytes contradict the name or the declared type. 422.</summary>
    public const string TypeMismatch = "knowledge.file.type_mismatch";

    /// <summary>The file has no bytes, or a CSV has not even a header row. 422.</summary>
    public const string Empty = "knowledge.file.empty";

    /// <summary>A text or CSV file is not valid UTF-8. 422.</summary>
    public const string NotUtf8 = "knowledge.file.not_utf8";

    /// <summary>The file could not be parsed as its format. 422.</summary>
    public const string Damaged = "knowledge.file.damaged";

    /// <summary>A PDF is password-protected. 422.</summary>
    public const string Encrypted = "knowledge.file.encrypted";

    /// <summary>The file was read and holds no text (a scan, images only, no records). 422.</summary>
    public const string NoText = "knowledge.file.no_text";

    /// <summary>A PDF has more pages than the limit (<c>limit</c>: pages). 422.</summary>
    public const string TooManyPages = "knowledge.file.too_many_pages";

    /// <summary>More text than a document may hold (<c>limit</c>: characters). 422.</summary>
    public const string TooManyCharacters = "knowledge.file.too_many_characters";

    /// <summary>A CSV has more rows than the limit (<c>limit</c>: rows). 422.</summary>
    public const string TooManyRows = "knowledge.file.too_many_rows";

    /// <summary>A CSV has more columns than the limit (<c>limit</c>: columns). 422.</summary>
    public const string TooManyColumns = "knowledge.file.too_many_columns";

    /// <summary>A CSV cell is longer than the limit (<c>limit</c>: characters). 422.</summary>
    public const string CellTooLong = "knowledge.file.cell_too_long";

    /// <summary>
    /// A DOCX has too many parts or expands too far (<c>limit</c>: entries or
    /// bytes, as <c>limitUnit</c> says). 422.
    /// </summary>
    public const string ArchiveTooLarge = "knowledge.file.archive_too_large";

    /// <summary>A CSV has no usable header row naming its columns. 422.</summary>
    public const string HeaderRequired = "knowledge.file.header_required";

    /// <summary>The multipart form has no file, or an empty one. 400.</summary>
    public const string Required = "knowledge.file.required";

    /// <summary>The request is not <c>multipart/form-data</c>. 415.</summary>
    public const string MultipartRequired = "knowledge.file.multipart_required";

    /// <summary>The general code for a reason, used when a site names no rule.</summary>
    public static string For(KnowledgeFileRejection reason) => reason switch
    {
        KnowledgeFileRejection.TooLarge => TooLarge,
        KnowledgeFileRejection.UnsupportedType => UnsupportedType,
        KnowledgeFileRejection.TypeMismatch => TypeMismatch,
        KnowledgeFileRejection.NoText => NoText,
        KnowledgeFileRejection.TooMuchContent => TooManyCharacters,
        _ => Damaged,
    };
}

/// <summary>
/// What a parser produced.
/// </summary>
/// <param name="Text">The extracted text, already normalised for line endings.</param>
/// <param name="FileType">What the file turned out to be.</param>
public sealed record KnowledgeExtraction(string Text, KnowledgeFileType FileType);

/// <summary>
/// Turns an uploaded file into plain text.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The Application knows no parser.</strong> It knows that bytes with a
/// name go in and text comes out, and that a file this platform will not accept
/// raises <see cref="KnowledgeFileRejectedException"/> with a reason and a
/// sentence. Which library reads a PDF, and whether that changes next year, is
/// an Infrastructure concern.
/// </para>
/// <para>
/// <strong>Nothing here executes anything.</strong> No macro, no formula, no
/// embedded object, no external reference. Implementations read text and stop —
/// which is why the supported set is four formats rather than everything a
/// library could open.
/// </para>
/// </remarks>
public interface IKnowledgeFileExtractor
{
    /// <summary>
    /// Reads text out of an upload.
    /// </summary>
    /// <exception cref="KnowledgeFileRejectedException">
    /// The file is too large, not a supported format, not what it claims to be,
    /// unreadable, empty of text, or larger than a parser bound allows.
    /// </exception>
    KnowledgeExtraction Extract(KnowledgeFileUpload upload);
}
