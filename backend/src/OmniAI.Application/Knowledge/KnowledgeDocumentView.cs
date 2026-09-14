using OmniAI.Application.Knowledge.Files;
using OmniAI.Domain.Knowledge;

namespace OmniAI.Application.Knowledge;

/// <summary>
/// One knowledge document, as an administrator reads it.
/// </summary>
/// <remarks>
/// <para>
/// <strong>There is no vector here, and there never will be.</strong> Neither
/// this view nor the summary beside it carries an embedding, a chunk, a distance
/// or a profile identifier. A browser has no use for any of them, they are the
/// bulkiest thing this feature stores, and publishing them would hand anybody who
/// could read one document a way to compare it against others by arithmetic.
/// </para>
/// <para>
/// <see cref="Readiness"/> is computed from the document against the profile the
/// deployment is currently running, never read from a stored status column. That
/// is what makes it impossible for a screen to show "Ready" for a document whose
/// index describes an older draft.
/// </para>
/// </remarks>
/// <param name="Source">
/// The file this text was read out of, or <see langword="null"/> for a document
/// somebody typed. Provenance rather than content: the text above is what gets
/// indexed either way.
/// </param>
public sealed record KnowledgeDocumentView(
    Guid Id,
    Guid TenantId,
    string Title,
    string Content,
    bool IsEnabled,
    KnowledgeReadiness Readiness,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? IndexedAt,
    KnowledgeSourceView? Source)
{
    internal static KnowledgeDocumentView From(
        KnowledgeDocument document,
        EmbeddingProfileId profile,
        KnowledgeSource? source = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        return new KnowledgeDocumentView(
            document.Id.Value,
            document.TenantId.Value,
            document.Title.Value,
            document.Content.Value,
            document.IsEnabled,
            document.ReadinessFor(profile),
            document.CreatedAt,
            document.UpdatedAt,
            document.IndexedAt,
            source is null ? null : KnowledgeSourceView.From(source));
    }
}

/// <summary>
/// One document as a list publishes it.
/// </summary>
/// <remarks>
/// No content. A workspace with two hundred documents of a hundred thousand
/// characters each would otherwise send twenty megabytes to render a list of
/// titles. The editor reads the one document somebody opened.
/// </remarks>
/// <param name="SourceFileType">
/// The kind of file this document came from, or <see langword="null"/> when it
/// was written by hand. Enough for a list to say where a document came from
/// without carrying a whole source record per row.
/// </param>
public sealed record KnowledgeDocumentSummary(
    Guid Id,
    Guid TenantId,
    string Title,
    bool IsEnabled,
    KnowledgeReadiness Readiness,
    int ContentLength,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? IndexedAt,
    KnowledgeFileType? SourceFileType)
{
    internal static KnowledgeDocumentSummary From(
        KnowledgeDocument document,
        EmbeddingProfileId profile,
        KnowledgeSource? source = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        return new KnowledgeDocumentSummary(
            document.Id.Value,
            document.TenantId.Value,
            document.Title.Value,
            document.IsEnabled,
            document.ReadinessFor(profile),
            document.Content.Value.Length,
            document.CreatedAt,
            document.UpdatedAt,
            document.IndexedAt,
            source?.FileType);
    }
}
