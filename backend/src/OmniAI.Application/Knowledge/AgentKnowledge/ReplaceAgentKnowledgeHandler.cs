using OmniAI.Application.AiAgents;
using OmniAI.Application.AiAgents.Persistence;
using OmniAI.Application.Authorization;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Knowledge.Persistence;
using OmniAI.Domain;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Knowledge;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Knowledge.AgentKnowledge;

public sealed record ReplaceAgentKnowledgeCommand(
    Guid TenantId,
    Guid AiAgentId,
    IReadOnlyList<Guid>? KnowledgeDocumentIds,
    ConcurrencyToken Expected);

/// <summary>
/// Decides which documents an agent may draw on.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Both permissions are required, and neither implies the other.</strong>
/// It is a change to knowledge and a change to an agent at the same time.
/// Somebody who administers documents but was never trusted to configure agents
/// could otherwise change what a live agent says to customers without holding a
/// single agent permission.
/// </para>
/// <para>
/// <strong>The write is checked against the agent's version and advances it.</strong>
/// Assignments are stored in their own table, but they are part of how the agent
/// behaves and are edited beside its instructions. Writing them without moving
/// the agent's version would let two editors silently overwrite each other's
/// understanding of the same agent.
/// </para>
/// <para>
/// Every id is verified to name a document in this workspace before anything is
/// written, so a request naming another workspace's document is a not-found
/// rather than a foreign key error. The database refuses it as well, on a
/// composite key that a cross-workspace pair cannot satisfy -- the check here is
/// for a clear answer, not for safety.
/// </para>
/// </remarks>
public sealed class ReplaceAgentKnowledgeHandler(
    IAiAgentRepository agents,
    IKnowledgeDocumentRepository documents,
    IAgentKnowledgeRepository assignments,
    TenantAuthorizer authorizer,
    ITextEmbeddingGenerator embeddings,
    TimeProvider clock)
{
    private readonly IAiAgentRepository _agents =
        agents ?? throw new ArgumentNullException(nameof(agents));

    private readonly IKnowledgeDocumentRepository _documents =
        documents ?? throw new ArgumentNullException(nameof(documents));

    private readonly IAgentKnowledgeRepository _assignments =
        assignments ?? throw new ArgumentNullException(nameof(assignments));

    private readonly TenantAuthorizer _authorizer =
        authorizer ?? throw new ArgumentNullException(nameof(authorizer));

    private readonly ITextEmbeddingGenerator _embeddings =
        embeddings ?? throw new ArgumentNullException(nameof(embeddings));

    private readonly TimeProvider _clock =
        clock ?? throw new ArgumentNullException(nameof(clock));

    public async Task<Versioned<AgentKnowledgeView>?> HandleAsync(
        ReplaceAgentKnowledgeCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        TenantId tenantId = TenantId.From(command.TenantId);
        AiAgentId agentId = AiAgentId.From(command.AiAgentId);

        IReadOnlyList<KnowledgeDocumentId> requested = Read(command.KnowledgeDocumentIds);

        if (!await _authorizer
            .RequireAsync(tenantId, PlatformPermissions.KnowledgeManage, cancellationToken)
            .ConfigureAwait(false))
        {
            return null;
        }

        if (!await _authorizer
            .RequireAsync(tenantId, PlatformPermissions.AiAgentsEdit, cancellationToken)
            .ConfigureAwait(false))
        {
            return null;
        }

        Versioned<AiAgent>? agent = await _agents
            .FindAsync(tenantId, agentId, cancellationToken)
            .ConfigureAwait(false);

        if (agent is null)
        {
            return null;
        }

        AiAgentConcurrency.RequireExpectedVersion(agent, command.Expected);

        IReadOnlyList<KnowledgeDocument> available = await _documents
            .ListAllAsync(tenantId, GetAgentKnowledgeHandler.MaxDocuments, cancellationToken)
            .ConfigureAwait(false);

        Dictionary<KnowledgeDocumentId, KnowledgeDocument> known =
            available.ToDictionary(document => document.Id);

        foreach (KnowledgeDocumentId documentId in requested)
        {
            if (!known.ContainsKey(documentId))
            {
                throw new DomainValidationException(
                    $"This workspace has no knowledge document with id '{documentId}'. "
                    + "No assignment was changed.");
            }
        }

        DateTimeOffset now = _clock.GetUtcNow();

        agent.Value.MarkConfigurationChanged(now);

        ConcurrencyToken version = await _assignments
            .ReplaceAsync(agent.Value, command.Expected, requested, now, cancellationToken)
            .ConfigureAwait(false);

        HashSet<KnowledgeDocumentId> ticked = [.. requested];
        EmbeddingProfileId profile = _embeddings.Profile.Id;

        AgentKnowledgeOption[] options =
        [
            .. available.Select(document => new AgentKnowledgeOption(
                document.Id.Value,
                document.Title.Value,
                ticked.Contains(document.Id),
                document.ReadinessFor(profile))),
        ];

        return new Versioned<AgentKnowledgeView>(
            new AgentKnowledgeView(agentId.Value, options),
            version);
    }

    /// <summary>
    /// Reads the requested ids, refusing a set larger than the screen can offer.
    /// </summary>
    /// <remarks>
    /// Duplicates are collapsed rather than rejected: a client sending the same
    /// document twice means the same thing as sending it once, and the stored set
    /// cannot hold it twice anyway.
    /// </remarks>
    private static IReadOnlyList<KnowledgeDocumentId> Read(IReadOnlyList<Guid>? ids)
    {
        if (ids is null || ids.Count == 0)
        {
            return [];
        }

        if (ids.Count > GetAgentKnowledgeHandler.MaxDocuments)
        {
            throw new DomainValidationException(
                $"An AI agent may be assigned at most {GetAgentKnowledgeHandler.MaxDocuments} "
                + $"knowledge documents; {ids.Count} were supplied.");
        }

        return [.. ids.Select(KnowledgeDocumentId.From).Distinct()];
    }
}
