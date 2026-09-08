using OmniAI.Application.Authorization;
using OmniAI.Application.Knowledge.Persistence;
using OmniAI.Application.Tenants;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Knowledge.ListKnowledgeDocuments;

public sealed record ListKnowledgeDocumentsQuery(
    Guid TenantId,
    int? Page = null,
    int? PageSize = null);

/// <summary>Reads one page of a workspace's knowledge.</summary>
/// <remarks>
/// A non-member is refused with the same not-found a missing workspace produces,
/// so the two are indistinguishable. A member without <c>knowledge.view</c>
/// leaves by the other door with a 403: they have already proved they belong
/// here, so telling them their workspace had vanished would be a lie.
/// </remarks>
public sealed class ListKnowledgeDocumentsHandler(
    IKnowledgeDocumentRepository documents,
    TenantAuthorizer authorizer)
{
    private readonly IKnowledgeDocumentRepository _documents =
        documents ?? throw new ArgumentNullException(nameof(documents));

    private readonly TenantAuthorizer _authorizer =
        authorizer ?? throw new ArgumentNullException(nameof(authorizer));

    public async Task<KnowledgeDocumentPage> HandleAsync(
        ListKnowledgeDocumentsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        int page = query.Page ?? KnowledgeDocumentPage.DefaultPage;
        int pageSize = query.PageSize ?? KnowledgeDocumentPage.DefaultPageSize;

        if (page < KnowledgeDocumentPage.DefaultPage)
        {
            throw KnowledgeDocumentPageValidationException.ForPage(page);
        }

        if (pageSize < 1 || pageSize > KnowledgeDocumentPage.MaxPageSize)
        {
            throw KnowledgeDocumentPageValidationException.ForPageSize(pageSize);
        }

        TenantId tenantId = TenantId.From(query.TenantId);

        if (!await _authorizer
            .RequireAsync(tenantId, PlatformPermissions.KnowledgeView, cancellationToken)
            .ConfigureAwait(false))
        {
            throw TenantNotFoundException.ForId(tenantId.Value);
        }

        return await _documents
            .ListAsync(tenantId, page, pageSize, cancellationToken)
            .ConfigureAwait(false);
    }
}
