using OmniAI.Application.Authorization;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Knowledge.Persistence;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Knowledge;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Knowledge.Files;

/// <summary>Replace the file a knowledge document was read from.</summary>
public sealed record ReplaceKnowledgeSourceCommand(
    Guid TenantId,
    Guid KnowledgeDocumentId,
    KnowledgeFileUpload Upload,
    ConcurrencyToken Expected);

/// <summary>
/// Raised when a document has no file to replace.
/// </summary>
/// <remarks>
/// A manually written document is not a file-backed one with a missing file: it
/// is a different kind of document, edited by typing. Turning one into the other
/// by uploading over it would silently discard text somebody wrote, so it is
/// refused and explained.
/// </remarks>
public sealed class KnowledgeDocumentHasNoSourceException()
    : Exception(
        "This document was written by hand rather than uploaded, so there is no file to "
        + "replace. Edit its text directly, or upload the file as a new document.");

/// <summary>
/// Reads text out of a new file and puts it into an existing document.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Extraction happens before the precondition is even read.</strong> The
/// order matters in the other direction from what you might expect: parsing
/// touches nothing, so doing it first means a corrupt file is refused with the
/// document untouched, and a stale entity tag is refused with nothing parsed
/// twice. Either way the existing text survives exactly as it was.
/// </para>
/// <para>
/// <strong>Re-uploading the identical file is not a change.</strong> Same bytes,
/// same name, same type, same extracted text: nothing is written, the version
/// does not move, and the entity tag the caller already holds stays valid. That
/// keeps a double-clicked Upload button from invalidating every other open
/// screen's tag, and — since the extracted text is unchanged — keeps a document
/// that was Ready from being marked as needing an index it does not need.
/// </para>
/// <para>
/// <strong>Anything that did change makes the document need an index again.</strong>
/// The aggregate decides that, from the content's own fingerprint. New text means
/// the stored chunks describe something nobody can see, and the honest state for
/// that is not Ready.
/// </para>
/// </remarks>
public sealed class ReplaceKnowledgeSourceHandler(
    IKnowledgeDocumentRepository documents,
    IKnowledgeSourceRepository sources,
    IKnowledgeFileExtractor extractor,
    TenantAuthorizer authorizer,
    ITextEmbeddingGenerator embeddings,
    TimeProvider clock)
{
    private readonly IKnowledgeDocumentRepository _documents =
        documents ?? throw new ArgumentNullException(nameof(documents));

    private readonly IKnowledgeSourceRepository _sources =
        sources ?? throw new ArgumentNullException(nameof(sources));

    private readonly IKnowledgeFileExtractor _extractor =
        extractor ?? throw new ArgumentNullException(nameof(extractor));

    private readonly TenantAuthorizer _authorizer =
        authorizer ?? throw new ArgumentNullException(nameof(authorizer));

    private readonly ITextEmbeddingGenerator _embeddings =
        embeddings ?? throw new ArgumentNullException(nameof(embeddings));

    private readonly TimeProvider _clock =
        clock ?? throw new ArgumentNullException(nameof(clock));

    /// <returns>
    /// The document and its source, or <see langword="null"/> when the caller is
    /// not entitled or no such document belongs to this workspace.
    /// </returns>
    /// <exception cref="KnowledgeFileRejectedException">
    /// The file is not one this platform will ingest. Nothing is changed.
    /// </exception>
    /// <exception cref="KnowledgeDocumentVersionMismatchException">
    /// Somebody else changed the document first.
    /// </exception>
    /// <exception cref="KnowledgeDocumentHasNoSourceException">
    /// The document was written by hand.
    /// </exception>
    public async Task<Versioned<KnowledgeFileResult>?> HandleAsync(
        ReplaceKnowledgeSourceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Upload);

        TenantId tenantId = TenantId.From(command.TenantId);
        KnowledgeDocumentId documentId = KnowledgeDocumentId.From(command.KnowledgeDocumentId);

        SourceFileName fileName = SourceFileName.Create(command.Upload.FileName);

        if (!await _authorizer
            .RequireAsync(tenantId, PlatformPermissions.KnowledgeManage, cancellationToken)
            .ConfigureAwait(false))
        {
            return null;
        }

        Versioned<KnowledgeDocument>? stored = await _documents
            .FindAsync(tenantId, documentId, cancellationToken)
            .ConfigureAwait(false);

        if (stored is null)
        {
            return null;
        }

        KnowledgeSource? source = await _sources
            .FindAsync(tenantId, documentId, cancellationToken)
            .ConfigureAwait(false);

        if (source is null)
        {
            throw new KnowledgeDocumentHasNoSourceException();
        }

        KnowledgeConcurrency.RequireExpectedVersion(stored, command.Expected);

        // Outside any transaction, and before anything is written. A file that
        // turns out to be unreadable leaves the document exactly as it was.
        KnowledgeExtraction extraction = _extractor.Extract(command.Upload);

        KnowledgeContent content = KnowledgeContent.Create(extraction.Text);

        DateTimeOffset now = _clock.GetUtcNow();

        bool sourceChanged = source.Replace(
            fileName,
            extraction.FileType,
            command.Upload.Content.LongLength,
            UploadKnowledgeFileHandler.FingerprintOf(command.Upload.Content),
            now);

        bool contentChanged = stored.Value.Content != content;

        if (!sourceChanged && !contentChanged)
        {
            // Nothing to record. The caller's entity tag is still the current one.
            return Published(stored.Value, source, command.Expected);
        }

        // Revise rather than assign: the aggregate is what decides that new text
        // means the index no longer describes this document.
        stored.Value.Revise(stored.Value.Title, content, now);

        ConcurrencyToken version = await _sources
            .ReplaceAsync(stored.Value, source, command.Expected, cancellationToken)
            .ConfigureAwait(false);

        return Published(stored.Value, source, version);
    }

    private Versioned<KnowledgeFileResult> Published(
        KnowledgeDocument document,
        KnowledgeSource source,
        ConcurrencyToken version) =>
        new(
            new KnowledgeFileResult(
                KnowledgeDocumentView.From(document, _embeddings.Profile.Id, source),
                KnowledgeSourceView.From(source)),
            version);
}
