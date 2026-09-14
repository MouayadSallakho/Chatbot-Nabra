using OmniAI.Application.Authorization;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Knowledge.Persistence;
using OmniAI.Application.Tenants;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Knowledge;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Knowledge.Files;

/// <summary>Create a knowledge document from an uploaded file.</summary>
/// <param name="Title">
/// What to call it. Optional: left out, the file's own name is used.
/// </param>
public sealed record UploadKnowledgeFileCommand(
    Guid TenantId,
    string? Title,
    KnowledgeFileUpload Upload);

/// <summary>A document and the file it was read from.</summary>
public sealed record KnowledgeFileResult(
    KnowledgeDocumentView Document,
    KnowledgeSourceView Source);

/// <summary>
/// Reads text out of an uploaded file and stores it as a knowledge document.
/// </summary>
/// <remarks>
/// <para>
/// <strong>It spends nothing.</strong> No embedding is made and no chunk is
/// written; the document is saved as needing an index, which somebody then asks
/// for deliberately. That is the same rule a typed document follows, and it is
/// what stops dropping a folder of PDFs onto the page from quietly buying a few
/// hundred embeddings.
/// </para>
/// <para>
/// <strong>Extraction happens before anything is written.</strong> Parsing is the
/// slow, failure-prone step and it touches no database: a file that turns out to
/// be a scan, a zip bomb or a corrupt container is refused with nothing created.
/// Only once there is real text does the short write happen.
/// </para>
/// <para>
/// <strong>The order of the permission check is deliberate.</strong> Authority is
/// established before the file is parsed, so somebody who may not manage
/// knowledge cannot make this deployment do parsing work on bytes of their
/// choosing.
/// </para>
/// </remarks>
public sealed class UploadKnowledgeFileHandler(
    IKnowledgeSourceRepository sources,
    IKnowledgeFileExtractor extractor,
    TenantAuthorizer authorizer,
    ITextEmbeddingGenerator embeddings,
    TimeProvider clock)
{
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

    /// <exception cref="KnowledgeFileRejectedException">
    /// The file is not one this platform will ingest.
    /// </exception>
    /// <exception cref="TenantNotFoundException">
    /// The caller does not belong to this workspace.
    /// </exception>
    public async Task<Versioned<KnowledgeFileResult>> HandleAsync(
        UploadKnowledgeFileCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Upload);

        TenantId tenantId = TenantId.From(command.TenantId);

        // Sanitised before anything else reads it: a name with a path or a control
        // character in it is refused here rather than three layers further in.
        SourceFileName fileName = SourceFileName.Create(command.Upload.FileName);

        if (!await _authorizer
            .RequireAsync(tenantId, PlatformPermissions.KnowledgeManage, cancellationToken)
            .ConfigureAwait(false))
        {
            throw TenantNotFoundException.ForId(tenantId.Value);
        }

        // Outside any transaction, and before anything is created.
        KnowledgeExtraction extraction = _extractor.Extract(command.Upload);

        KnowledgeTitle title = TitleFor(command.Title, fileName);
        KnowledgeContent content = KnowledgeContent.Create(extraction.Text);

        DateTimeOffset now = _clock.GetUtcNow();

        KnowledgeDocument document = KnowledgeDocument.Create(tenantId, title, content, now);

        KnowledgeSource source = KnowledgeSource.Create(
            tenantId,
            document.Id,
            fileName,
            extraction.FileType,
            command.Upload.Content.LongLength,
            FingerprintOf(command.Upload.Content),
            now);

        ConcurrencyToken version = await _sources
            .AddAsync(document, source, cancellationToken)
            .ConfigureAwait(false);

        return new Versioned<KnowledgeFileResult>(
            new KnowledgeFileResult(
                KnowledgeDocumentView.From(document, _embeddings.Profile.Id, source),
                KnowledgeSourceView.From(source)),
            version);
    }

    /// <summary>
    /// The title to give a file-backed document.
    /// </summary>
    /// <remarks>
    /// An explicit title wins. Without one the file's own name is used, minus its
    /// extension — <c>Refund policy.pdf</c> becomes <c>Refund policy</c>, which is
    /// what somebody would have typed. The name is already sanitised, so nothing
    /// dangerous reaches the title; a name too long for a title is cut to fit,
    /// because a document called something slightly shorter is better than a
    /// refused upload.
    /// </remarks>
    private static KnowledgeTitle TitleFor(string? requested, SourceFileName fileName)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            return KnowledgeTitle.Create(requested);
        }

        string derived = fileName.WithoutExtension();

        return KnowledgeTitle.Create(
            derived.Length > KnowledgeTitle.MaxLength
                ? derived[..KnowledgeTitle.MaxLength]
                : derived);
    }

    /// <summary>A digest of the uploaded bytes.</summary>
    internal static ContentFingerprint FingerprintOf(byte[] content) =>
        ContentFingerprint.From(System.Security.Cryptography.SHA256.HashData(content));
}
