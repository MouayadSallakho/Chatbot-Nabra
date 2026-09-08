using OmniAI.Application.Authorization;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Knowledge.Persistence;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Knowledge;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Knowledge.GetKnowledgeDocumentById;

public sealed record GetKnowledgeDocumentByIdQuery(Guid TenantId, Guid KnowledgeDocumentId);

/// <summary>Reads one document, with the version needed to change it.</summary>
public sealed class GetKnowledgeDocumentByIdHandler(
    IKnowledgeDocumentRepository documents,
    TenantAuthorizer authorizer,
    ITextEmbeddingGenerator embeddings)
{
    private readonly IKnowledgeDocumentRepository _documents =
        documents ?? throw new ArgumentNullException(nameof(documents));

    private readonly TenantAuthorizer _authorizer =
        authorizer ?? throw new ArgumentNullException(nameof(authorizer));

    private readonly ITextEmbeddingGenerator _embeddings =
        embeddings ?? throw new ArgumentNullException(nameof(embeddings));

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

        Versioned<KnowledgeDocument>? stored = await _documents
            .FindAsync(tenantId, documentId, cancellationToken)
            .ConfigureAwait(false);

        return stored is null
            ? null
            : new Versioned<KnowledgeDocumentView>(
                KnowledgeDocumentView.From(stored.Value, _embeddings.Profile.Id),
                stored.Version);
    }
}
