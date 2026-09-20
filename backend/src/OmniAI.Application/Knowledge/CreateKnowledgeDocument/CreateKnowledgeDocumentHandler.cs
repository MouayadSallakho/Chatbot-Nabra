using OmniAI.Application.Authorization;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Knowledge.Persistence;
using OmniAI.Application.Tenants;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Knowledge;
using OmniAI.Domain.Tenants;

using OmniAI.Application.Plans;
using OmniAI.Domain.Plans;
namespace OmniAI.Application.Knowledge.CreateKnowledgeDocument;

public sealed record CreateKnowledgeDocumentCommand(Guid TenantId, string? Title, string? Content);

/// <summary>
/// Writes a new knowledge document.
/// </summary>
/// <remarks>
/// <strong>It spends nothing.</strong> No embedding is made and no chunk is
/// written: the document is saved and reported as needing an index, which
/// somebody then asks for deliberately. Indexing on save would mean every typo
/// corrected in a hundred-thousand-character document silently bought a fresh set
/// of embeddings.
/// </remarks>
public sealed class CreateKnowledgeDocumentHandler(
    IWorkspaceEntitlementService entitlements,
    IKnowledgeDocumentRepository documents,
    TenantAuthorizer authorizer,
    ITextEmbeddingGenerator embeddings,
    TimeProvider clock)
{
    private readonly IWorkspaceEntitlementService _entitlements =
        entitlements ?? throw new ArgumentNullException(nameof(entitlements));

    private readonly IKnowledgeDocumentRepository _documents =
        documents ?? throw new ArgumentNullException(nameof(documents));

    private readonly TenantAuthorizer _authorizer =
        authorizer ?? throw new ArgumentNullException(nameof(authorizer));

    private readonly ITextEmbeddingGenerator _embeddings =
        embeddings ?? throw new ArgumentNullException(nameof(embeddings));

    private readonly TimeProvider _clock =
        clock ?? throw new ArgumentNullException(nameof(clock));

    public async Task<Versioned<KnowledgeDocumentView>> HandleAsync(
        CreateKnowledgeDocumentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        TenantId tenantId = TenantId.From(command.TenantId);

        // Validated before the workspace is consulted, so a malformed document is
        // refused identically whether the workspace is reachable or not.
        KnowledgeTitle title = KnowledgeTitle.Create(command.Title);
        KnowledgeContent content = KnowledgeContent.Create(command.Content);

        if (!await _authorizer
            .RequireAsync(tenantId, PlatformPermissions.KnowledgeManage, cancellationToken)
            .ConfigureAwait(false))
        {
            throw TenantNotFoundException.ForId(tenantId.Value);
        }

        KnowledgeDocument document =
            KnowledgeDocument.Create(tenantId, title, content, _clock.GetUtcNow());

        // Permission said this person may create one; the plan says whether the
        // workspace may hold another. Two questions, asked in that order, and
        // neither answers the other. The count and the insert happen together
        // under a workspace-scoped database lock, so two requests arriving at the
        // last free slot cannot both pass the count.
        //
        // A manual document and a file-backed one are the same row in the same
        // table, so they consume the same slot -- which is the intended rule
        // rather than an accident of storage. Replacing an existing document's
        // file edits that row and consumes nothing further.
        return await _entitlements.ReserveAsync(
            tenantId,
            EntitlementKind.KnowledgeDocuments,
            async token =>
            {
                ConcurrencyToken version = await _documents
                    .AddAsync(document, token)
                    .ConfigureAwait(false);

                return new Versioned<KnowledgeDocumentView>(
                    KnowledgeDocumentView.From(document, _embeddings.Profile.Id),
                    version);
            },
            cancellationToken)
            .ConfigureAwait(false);
    }
}
