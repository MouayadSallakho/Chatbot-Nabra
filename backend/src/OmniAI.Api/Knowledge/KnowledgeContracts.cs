using OmniAI.Application.Knowledge;
using OmniAI.Application.Knowledge.Files;
using OmniAI.Application.Knowledge.AgentKnowledge;
using OmniAI.Application.Knowledge.IndexKnowledgeDocument;
using OmniAI.Application.Knowledge.ListKnowledgeDocuments;
using OmniAI.Application.Knowledge.SearchAgentKnowledge;

namespace OmniAI.Api.Knowledge;

/// <summary>What creating or replacing a knowledge document requires.</summary>
public sealed record KnowledgeDocumentRequest(string? Title, string? Content);

/// <summary>Which documents an agent may draw on.</summary>
public sealed record AgentKnowledgeRequest(IReadOnlyList<Guid>? KnowledgeDocumentIds);

/// <summary>What the admin retrieval tester asks.</summary>
public sealed record KnowledgeSearchRequest(string? Question);

/// <summary>
/// One knowledge document, as the API publishes it.
/// </summary>
/// <remarks>
/// <strong>Readiness is a word, not a pair of flags.</strong> "Ready",
/// "NeedsIndex" and "Disabled" are what a screen actually branches on, and
/// publishing <c>isEnabled</c> plus <c>indexedAt</c> and asking every client to
/// re-derive the rule would guarantee that two clients eventually derived it
/// differently. <c>isEnabled</c> travels as well because the enable and disable
/// controls need to know which one to offer.
/// <para>
/// No vector, no chunk, no embedding profile. A browser has no use for any of
/// them.
/// </para>
/// </remarks>
public sealed record KnowledgeDocumentResponse(
    Guid Id,
    Guid TenantId,
    string Title,
    string Content,
    bool IsEnabled,
    string Readiness,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? IndexedAt,
    KnowledgeSourceResponse? Source)
{
    public static KnowledgeDocumentResponse From(KnowledgeDocumentView document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return new KnowledgeDocumentResponse(
            document.Id,
            document.TenantId,
            document.Title,
            document.Content,
            document.IsEnabled,
            document.Readiness.ToString(),
            document.CreatedAt,
            document.UpdatedAt,
            document.IndexedAt,
            document.Source is null ? null : KnowledgeSourceResponse.From(document.Source));
    }
}

/// <summary>
/// The file a document's text was read out of, on the wire.
/// </summary>
/// <remarks>
/// <strong>Metadata only, and there is no way to get the file back.</strong> The
/// upload was read during its request and discarded: there is no download route,
/// no storage key and no path — by design, so this platform never becomes a place
/// a workspace's original documents accumulate.
/// <para>
/// The fingerprint is not published either. It is an internal comparison value,
/// and exposing it would let anybody who can read one document test whether a
/// file they hold is the same one.
/// </para>
/// </remarks>
public sealed record KnowledgeSourceResponse(
    string FileName,
    string FileType,
    long ByteLength,
    DateTimeOffset ExtractedAt)
{
    public static KnowledgeSourceResponse From(KnowledgeSourceView source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new KnowledgeSourceResponse(
            source.FileName,
            source.FileType.ToString(),
            source.ByteLength,
            source.ExtractedAt);
    }
}

/// <summary>One document as the list publishes it. No content.</summary>
/// <param name="SourceByteLength">
/// The size of the file the text came from, in bytes; <see langword="null"/> for a
/// typed document (Phase 37.5a).
/// </param>
/// <param name="LastIndexAttempt">
/// What the most recent indexing attempt did; <see langword="null"/> when the
/// document was never indexed (Phase 37.5a).
/// </param>
public sealed record KnowledgeDocumentSummaryResponse(
    Guid Id,
    Guid TenantId,
    string Title,
    bool IsEnabled,
    string Readiness,
    int ContentLength,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? IndexedAt,
    string? SourceFileType,
    long? SourceByteLength,
    KnowledgeIndexAttemptResponse? LastIndexAttempt);

/// <summary>
/// What the most recent indexing attempt on a document did (Phase 37.5a, row 15).
/// </summary>
/// <remarks>
/// <c>status</c> is <c>Started</c> (indexing now), <c>Succeeded</c>, <c>Failed</c>
/// or <c>Unknown</c> (the process stopped before it could say). <c>failureCode</c>
/// is a stable word such as <c>provider_unavailable</c> or <c>quota_exhausted</c>,
/// listed in <c>knowledge-base-rag.md</c>, and never a provider's own message.
/// Readiness is unchanged and still derived: this says what the last attempt did,
/// which readiness cannot.
/// </remarks>
public sealed record KnowledgeIndexAttemptResponse(
    string Status,
    string? FailureCode,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt)
{
    public static KnowledgeIndexAttemptResponse? From(KnowledgeIndexAttemptView? attempt) =>
        attempt is null
            ? null
            : new KnowledgeIndexAttemptResponse(
                attempt.Status.ToString(),
                attempt.FailureCode,
                attempt.StartedAt,
                attempt.CompletedAt);
}

/// <summary>
/// One knowledge document as <c>GET …/knowledge/{id}</c> publishes it: the
/// document, and what its last indexing attempt did (Phase 37.5a).
/// </summary>
/// <remarks>
/// A separate record so the writes keep their exact shape: a write's response
/// would otherwise have to claim <c>lastIndexAttempt: null</c> for a document that
/// has been indexed many times.
/// </remarks>
public sealed record KnowledgeDocumentDetailResponse(
    Guid Id,
    Guid TenantId,
    string Title,
    string Content,
    bool IsEnabled,
    string Readiness,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? IndexedAt,
    KnowledgeSourceResponse? Source,
    KnowledgeIndexAttemptResponse? LastIndexAttempt)
{
    public static KnowledgeDocumentDetailResponse From(KnowledgeDocumentView document)
    {
        ArgumentNullException.ThrowIfNull(document);

        KnowledgeDocumentResponse published = KnowledgeDocumentResponse.From(document);

        return new KnowledgeDocumentDetailResponse(
            published.Id,
            published.TenantId,
            published.Title,
            published.Content,
            published.IsEnabled,
            published.Readiness,
            published.CreatedAt,
            published.UpdatedAt,
            published.IndexedAt,
            published.Source,
            KnowledgeIndexAttemptResponse.From(document.LastIndexAttempt));
    }
}

/// <summary>One page of knowledge documents.</summary>
public sealed record KnowledgeDocumentPageResponse(
    IReadOnlyList<KnowledgeDocumentSummaryResponse> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public static KnowledgeDocumentPageResponse From(KnowledgeDocumentPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new KnowledgeDocumentPageResponse(
            [
                .. page.Items.Select(item => new KnowledgeDocumentSummaryResponse(
                    item.Id,
                    item.TenantId,
                    item.Title,
                    item.IsEnabled,
                    item.Readiness.ToString(),
                    item.ContentLength,
                    item.CreatedAt,
                    item.UpdatedAt,
                    item.IndexedAt,
                    item.SourceFileType?.ToString(),
                    item.SourceByteLength,
                    KnowledgeIndexAttemptResponse.From(item.LastIndexAttempt))),
            ],
            page.Page,
            page.PageSize,
            page.TotalCount);
    }
}

/// <summary>
/// What an index attempt did.
/// </summary>
/// <remarks>
/// <c>embeddingCalls</c> is published on purpose. Cost is the thing an operator
/// most needs to be able to see for themselves, and a screen that can say "this
/// cost nothing, it was already up to date" is far more trustworthy than one
/// asserting it.
/// </remarks>
public sealed record KnowledgeIndexResponse(
    KnowledgeDocumentResponse Document,
    int ChunkCount,
    int EmbeddingCalls,
    bool AlreadyCurrent)
{
    public static KnowledgeIndexResponse From(KnowledgeIndexResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new KnowledgeIndexResponse(
            KnowledgeDocumentResponse.From(result.Document),
            result.ChunkCount,
            result.EmbeddingCalls,
            result.AlreadyCurrent);
    }
}

/// <summary>One document offered to an agent, and whether it is ticked.</summary>
public sealed record AgentKnowledgeOptionResponse(
    Guid KnowledgeDocumentId,
    string Title,
    bool IsAssigned,
    string Readiness);

/// <summary>What an agent may draw on.</summary>
public sealed record AgentKnowledgeResponse(
    Guid AiAgentId,
    IReadOnlyList<AgentKnowledgeOptionResponse> Documents)
{
    public static AgentKnowledgeResponse From(AgentKnowledgeView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        return new AgentKnowledgeResponse(
            view.AiAgentId,
            [
                .. view.Documents.Select(document => new AgentKnowledgeOptionResponse(
                    document.KnowledgeDocumentId,
                    document.Title,
                    document.IsAssigned,
                    document.Readiness.ToString())),
            ]);
    }
}

/// <summary>
/// One retrieval result.
/// </summary>
/// <remarks>
/// <strong>There is no vector field, and adding one would be a mistake.</strong>
/// The similarity is published because somebody diagnosing a wrong answer needs
/// to see how close the match actually was; the embedding it was computed from
/// would be several kilobytes of numbers that no screen can use and that would
/// let anybody who can read one document compare it arithmetically against every
/// other.
/// </remarks>
public sealed record KnowledgeSearchHitResponse(
    Guid KnowledgeDocumentId,
    string Title,
    int Ordinal,
    string Excerpt,
    int Rank,
    double Similarity);

/// <summary>What a question would have retrieved.</summary>
public sealed record KnowledgeSearchResponse(IReadOnlyList<KnowledgeSearchHitResponse> Hits)
{
    public static KnowledgeSearchResponse From(KnowledgeSearchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new KnowledgeSearchResponse(
        [
            .. result.Hits.Select(hit => new KnowledgeSearchHitResponse(
                hit.KnowledgeDocumentId,
                hit.Title,
                hit.Ordinal,
                hit.Excerpt,
                hit.Rank,
                hit.Similarity)),
        ]);
    }
}
