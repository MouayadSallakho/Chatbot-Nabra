using OmniAI.Domain.Tenants;

namespace OmniAI.Domain.Knowledge;

/// <summary>
/// Where a knowledge document's text came from.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Provenance, not content.</strong> The document remains the one
/// authoritative text: it is what gets chunked, embedded and retrieved. This says
/// which file that text was read out of, so an administrator opening a document
/// months later can tell whether somebody typed it or uploaded it, and what they
/// uploaded.
/// </para>
/// <para>
/// <strong>There is no file here, and there is nowhere to put one.</strong> No
/// path, no bytes, no stream, no storage key. Phase 21 reads the upload, extracts
/// text from it in memory, and discards the original when the request ends — so
/// what survives is this row and the document's text. That is a deliberate
/// product decision (no original-file download, no upload folder, no object
/// store), and the shape of this type is what keeps it honest: a future change of
/// mind has to add a field here and answer for it.
/// </para>
/// <para>
/// <strong>The fingerprint is of the file, not of the extracted text.</strong>
/// The document already fingerprints its own content — that is what decides
/// whether an index is current. This one answers a different question: is this
/// the same file somebody uploaded last time. The two can disagree in both
/// directions: two different PDFs can extract to identical text, and one PDF can
/// extract differently after a parser upgrade.
/// </para>
/// <para>
/// A document has at most one source, and a source belongs to exactly one
/// document. Replacing the file replaces this row's contents rather than adding
/// a second, because "which file is this document from" has one answer.
/// </para>
/// </remarks>
public sealed class KnowledgeSource
{
    private KnowledgeSource(
        KnowledgeSourceId id,
        TenantId tenantId,
        KnowledgeDocumentId knowledgeDocumentId,
        SourceFileName fileName,
        KnowledgeFileType fileType,
        long byteLength,
        ContentFingerprint fingerprint,
        DateTimeOffset extractedAt,
        DateTimeOffset createdAt)
    {
        Id = id;
        TenantId = tenantId;
        KnowledgeDocumentId = knowledgeDocumentId;
        FileName = fileName;
        FileType = fileType;
        ByteLength = byteLength;
        Fingerprint = fingerprint;
        ExtractedAt = extractedAt;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public KnowledgeSourceId Id { get; }

    /// <summary>
    /// The owning workspace.
    /// </summary>
    /// <remarks>
    /// Carried here as well as on the document, so the database can enforce that a
    /// source and its document belong to the same workspace with a composite
    /// foreign key rather than with a check somebody has to remember to write.
    /// </remarks>
    public TenantId TenantId { get; }

    /// <summary>The document whose text was read out of this file.</summary>
    public KnowledgeDocumentId KnowledgeDocumentId { get; }

    /// <summary>The file's name, as a person recognises it.</summary>
    public SourceFileName FileName { get; private set; }

    public KnowledgeFileType FileType { get; private set; }

    /// <summary>How large the uploaded file was, in bytes.</summary>
    public long ByteLength { get; private set; }

    /// <summary>
    /// A digest of the uploaded bytes.
    /// </summary>
    /// <remarks>
    /// Compared, never interpreted. It is how a re-upload of the identical file
    /// is recognised as identical without keeping the file to compare against.
    /// </remarks>
    public ContentFingerprint Fingerprint { get; private set; }

    /// <summary>When text was last read out of a file for this document.</summary>
    public DateTimeOffset ExtractedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Records the file a new document was created from.</summary>
    /// <exception cref="DomainValidationException">
    /// An identity is missing, the byte length is not positive, or the instant is
    /// not a real point in time.
    /// </exception>
    public static KnowledgeSource Create(
        TenantId tenantId,
        KnowledgeDocumentId knowledgeDocumentId,
        SourceFileName fileName,
        KnowledgeFileType fileType,
        long byteLength,
        ContentFingerprint fingerprint,
        DateTimeOffset extractedAt)
    {
        if (tenantId.IsEmpty)
        {
            throw new DomainValidationException("A knowledge source must belong to a tenant.");
        }

        if (knowledgeDocumentId.IsEmpty)
        {
            throw new DomainValidationException("A knowledge source must belong to a document.");
        }

        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(fingerprint);

        RequireDefined(fileType);
        RequireByteLength(byteLength);

        return new KnowledgeSource(
            KnowledgeSourceId.New(),
            tenantId,
            knowledgeDocumentId,
            fileName,
            fileType,
            byteLength,
            fingerprint,
            RequireInstant(extractedAt),
            RequireInstant(extractedAt));
    }

    /// <summary>Rehydrates a stored source. Persistence only.</summary>
    public static KnowledgeSource Restore(
        KnowledgeSourceId id,
        TenantId tenantId,
        KnowledgeDocumentId knowledgeDocumentId,
        SourceFileName fileName,
        KnowledgeFileType fileType,
        long byteLength,
        ContentFingerprint fingerprint,
        DateTimeOffset extractedAt,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt) =>
        new(
            id,
            tenantId,
            knowledgeDocumentId,
            fileName,
            fileType,
            byteLength,
            fingerprint,
            extractedAt,
            createdAt)
        {
            UpdatedAt = updatedAt,
        };

    /// <summary>
    /// Records that a different file now backs this document.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when anything about the source actually changed.
    /// </returns>
    /// <remarks>
    /// <strong>Re-uploading the identical file changes nothing and says so.</strong>
    /// Same bytes, same name, same type means the same source, and returning false
    /// lets the caller skip a write that would move the document's version for no
    /// reason — which matters because a moved version invalidates every entity tag
    /// an administrator's open screen is holding.
    /// <para>
    /// The extraction timestamp is deliberately part of "nothing changed": it
    /// records when this document's text was last read out of a file, and
    /// re-reading the same file does not produce a new fact.
    /// </para>
    /// </remarks>
    public bool Replace(
        SourceFileName fileName,
        KnowledgeFileType fileType,
        long byteLength,
        ContentFingerprint fingerprint,
        DateTimeOffset extractedAt)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(fingerprint);

        RequireDefined(fileType);
        RequireByteLength(byteLength);

        DateTimeOffset when = RequireInstant(extractedAt);

        if (Fingerprint == fingerprint
            && FileType == fileType
            && string.Equals(FileName.Value, fileName.Value, StringComparison.Ordinal))
        {
            return false;
        }

        FileName = fileName;
        FileType = fileType;
        ByteLength = byteLength;
        Fingerprint = fingerprint;
        ExtractedAt = when;
        UpdatedAt = when;

        return true;
    }

    private static void RequireDefined(KnowledgeFileType fileType)
    {
        if (!Enum.IsDefined(fileType))
        {
            throw new DomainValidationException("That is not a knowledge file type.");
        }
    }

    private static void RequireByteLength(long byteLength)
    {
        if (byteLength <= 0)
        {
            throw new DomainValidationException("An uploaded file must not be empty.");
        }
    }

    private static DateTimeOffset RequireInstant(DateTimeOffset instant)
    {
        if (instant == default)
        {
            throw new DomainValidationException("A knowledge source needs a real instant.");
        }

        return instant.ToUniversalTime();
    }
}
