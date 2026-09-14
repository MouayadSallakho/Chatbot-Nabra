using OmniAI.Application.Authorization;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Knowledge.Persistence;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Knowledge;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Knowledge.SetKnowledgeDocumentStatus;

public sealed record SetKnowledgeDocumentStatusCommand(
    Guid TenantId,
    Guid KnowledgeDocumentId,
    bool Enabled,
    ConcurrencyToken Expected);

/// <summary>
/// Switches a document on or off.
/// </summary>
/// <remarks>
/// Disabling keeps the text and keeps the chunks. It is the control somebody
/// reaches for when a policy has just changed and the old one must stop being
/// quoted immediately -- deleting the index they paid for would punish them for
/// acting quickly, and they can enable it again the moment the new text is
/// indexed.
/// </remarks>
public sealed class SetKnowledgeDocumentStatusHandler(
    IKnowledgeDocumentRepository documents,
    IKnowledgeSourceRepository sources,
    TenantAuthorizer authorizer,
    ITextEmbeddingGenerator embeddings,
    TimeProvider clock)
{
    private readonly IKnowledgeDocumentRepository _documents =
        documents ?? throw new ArgumentNullException(nameof(documents));

    private readonly IKnowledgeSourceRepository _sources =
        sources ?? throw new ArgumentNullException(nameof(sources));

    private readonly TenantAuthorizer _authorizer =
        authorizer ?? throw new ArgumentNullException(nameof(authorizer));

    private readonly ITextEmbeddingGenerator _embeddings =
        embeddings ?? throw new ArgumentNullException(nameof(embeddings));

    private readonly TimeProvider _clock =
        clock ?? throw new ArgumentNullException(nameof(clock));

    public async Task<Versioned<KnowledgeDocumentView>?> HandleAsync(
        SetKnowledgeDocumentStatusCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        TenantId tenantId = TenantId.From(command.TenantId);
        KnowledgeDocumentId documentId = KnowledgeDocumentId.From(command.KnowledgeDocumentId);

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

        KnowledgeSource? source = await _sources
            .FindAsync(tenantId, documentId, cancellationToken)
            .ConfigureAwait(false);

        DateTimeOffset now = _clock.GetUtcNow();

        if (command.Enabled)
        {
            stored.Value.Enable(now);
        }
        else
        {
            stored.Value.Disable(now);
        }

        ConcurrencyToken version = await _documents
            .UpdateAsync(stored.Value, command.Expected, cancellationToken)
            .ConfigureAwait(false);

        return new Versioned<KnowledgeDocumentView>(
            KnowledgeDocumentView.From(stored.Value, _embeddings.Profile.Id, source),
            version);
    }
}
