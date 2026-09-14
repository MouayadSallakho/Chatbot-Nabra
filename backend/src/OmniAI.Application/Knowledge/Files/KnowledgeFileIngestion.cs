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
/// </remarks>
public sealed class KnowledgeFileRejectedException(KnowledgeFileRejection reason, string message)
    : Exception(message)
{
    public KnowledgeFileRejection Reason { get; } = reason;
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
