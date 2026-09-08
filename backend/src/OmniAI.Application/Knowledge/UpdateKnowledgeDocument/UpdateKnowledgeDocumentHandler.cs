using OmniAI.Application.Authorization;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Knowledge.Persistence;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Knowledge;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Knowledge.UpdateKnowledgeDocument;

public sealed record UpdateKnowledgeDocumentCommand(
    Guid TenantId,
    Guid KnowledgeDocumentId,
    string? Title,
    string? Content,
    ConcurrencyToken Expected);

/// <summary>
/// Replaces a document's title and text.
/// </summary>
/// <remarks>
/// One operation covering both fields, because they are edited on one screen and
/// saved by one button. The aggregate decides what that means for readiness:
/// changing the text moves the fingerprint and the document stops being current,
/// while changing only the title leaves the index exactly as valid as it was.
/// </remarks>
public sealed class UpdateKnowledgeDocumentHandler(
    IKnowledgeDocumentRepository documents,
    TenantAuthorizer authorizer,
    ITextEmbeddingGenerator embeddings,
    TimeProvider clock)
{
    private readonly IKnowledgeDocumentRepository _documents =
        documents ?? throw new ArgumentNullException(nameof(documents));

    private readonly TenantAuthorizer _authorizer =
        authorizer ?? throw new ArgumentNullException(nameof(authorizer));

    private readonly ITextEmbeddingGenerator _embeddings =
        embeddings ?? throw new ArgumentNullException(nameof(embeddings));

    private readonly TimeProvider _clock =
        clock ?? throw new ArgumentNullException(nameof(clock));

    public async Task<Versioned<KnowledgeDocumentView>?> HandleAsync(
        UpdateKnowledgeDocumentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        TenantId tenantId = TenantId.From(command.TenantId);
        KnowledgeDocumentId documentId = KnowledgeDocumentId.From(command.KnowledgeDocumentId);
        KnowledgeTitle title = KnowledgeTitle.Create(command.Title);
        KnowledgeContent content = KnowledgeContent.Create(command.Content);

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

        KnowledgeConcurrency.RequireExpectedVersion(stored, command.Expected);

        stored.Value.Revise(title, content, _clock.GetUtcNow());

        ConcurrencyToken version = await _documents
            .UpdateAsync(stored.Value, command.Expected, cancellationToken)
            .ConfigureAwait(false);

        return new Versioned<KnowledgeDocumentView>(
            KnowledgeDocumentView.From(stored.Value, _embeddings.Profile.Id),
            version);
    }
}
