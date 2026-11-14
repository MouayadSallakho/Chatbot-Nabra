using OmniAI.Application.Authorization;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Knowledge.Persistence;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Knowledge;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Knowledge.GetKnowledgeDocumentById;

public sealed record GetKnowledgeDocumentByIdQuery(Guid TenantId, Guid KnowledgeDocumentId);

/// <summary>Reads one document, with the version needed to change it.</summary>
/// <remarks>
/// A document uploaded as a file carries its provenance with it. A document
/// somebody typed carries none, and that absence is the answer rather than a
/// gap: the two are told apart by whether there is a source at all.
/// </remarks>
public sealed class GetKnowledgeDocumentByIdHandler(
    IKnowledgeDocumentRepository documents,
    IKnowledgeSourceRepository sources,
    TenantAuthorizer authorizer,
    ITextEmbeddingGenerator embeddings,
    IKnowledgeIndexAttemptStore attempts)
{
    private readonly IKnowledgeDocumentRepository _documents =
        documents ?? throw new ArgumentNullException(nameof(documents));

    private readonly IKnowledgeSourceRepository _sources =
        sources ?? throw new ArgumentNullException(nameof(sources));

    private readonly TenantAuthorizer _authorizer =
        authorizer ?? throw new ArgumentNullException(nameof(authorizer));

    private readonly ITextEmbeddingGenerator _embeddings =
        embeddings ?? throw new ArgumentNullException(nameof(embeddings));

    private readonly IKnowledgeIndexAttemptStore _attempts =
        attempts ?? throw new ArgumentNullException(nameof(attempts));

    public async Task<Versioned<KnowledgeDocumentView>?> HandleAsync(
        GetKnowledgeDocumentByIdQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        TenantId tenantId = TenantId.From(query.TenantId);
        KnowledgeDocumentId documentId = KnowledgeDocumentId.From(query.KnowledgeDocumentId);

        if (!await _authorizer
            .RequireAsync(tenantId, PlatformPermissions.KnowledgeView, cancellationToken)
            .ConfigureAwait(false))
        {
            return null;
        }

        // What the last press of "Index" did (Phase 37.5a). One indexed read, and
        // only in a workspace the caller may view (the read is scoped by it).
        //
        // Read before the document (Phase 39.1, Revision 1). An attempt commits
        // the document's index first and its Succeeded outcome after, in two
        // statements, and so does this read: in the writer's order a settlement
        // landing in between showed Succeeded beside "needs an index". In the
        // reverse order, an outcome seen here was committed after the index, so
        // the document read next already carries it.
        KnowledgeIndexAttemptView? lastAttempt = await _attempts
            .FindLatestAsync(tenantId, documentId, cancellationToken)
            .ConfigureAwait(false);

        Versioned<KnowledgeDocument>? stored = await _documents
            .FindAsync(tenantId, documentId, cancellationToken)
            .ConfigureAwait(false);

        if (stored is null)
        {
            return null;
        }

        // Where the text came from is part of what this document is, so it is
        // read here rather than left to a second request the screen would have to
        // remember to make.
        KnowledgeSource? source = await _sources
            .FindAsync(tenantId, documentId, cancellationToken)
            .ConfigureAwait(false);

        return new Versioned<KnowledgeDocumentView>(
            KnowledgeDocumentView.From(stored.Value, _embeddings.Profile.Id, source) with
            {
                LastIndexAttempt = lastAttempt,
            },
            stored.Version);
    }
}
