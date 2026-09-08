using OmniAI.Application.AiAgents.Persistence;
using OmniAI.Application.Authorization;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Knowledge.Persistence;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Knowledge;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Knowledge.AgentKnowledge;

public sealed record GetAgentKnowledgeQuery(Guid TenantId, Guid AiAgentId);

/// <summary>
/// One document offered to an agent, and whether it is currently ticked.
/// </summary>
/// <remarks>
/// <paramref name="Readiness"/> travels with it so the screen can be honest.
/// Assigning a document that has never been indexed is allowed and does nothing
/// yet; showing it as though it were live would leave somebody believing their
/// agent knows something it does not.
/// </remarks>
public sealed record AgentKnowledgeOption(
    Guid KnowledgeDocumentId,
    string Title,
    bool IsAssigned,
    KnowledgeReadiness Readiness);

/// <summary>What an agent may draw on, and what else is available to it.</summary>
public sealed record AgentKnowledgeView(
    Guid AiAgentId,
    IReadOnlyList<AgentKnowledgeOption> Documents);

/// <summary>
/// Reads an agent's knowledge assignments.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Two permissions, both required.</strong> This answer is about a
/// specific agent and about the workspace's knowledge, so somebody who may not
/// read one of them may not read it through the other.
/// </para>
/// <para>
/// It returns the agent's entity tag, not one of its own. Changing the
/// assignments is a change to the agent's configuration and is written against
/// the agent's version, so the version this hands out has to be the one the write
/// will be checked against.
/// </para>
/// </remarks>
public sealed class GetAgentKnowledgeHandler(
    IAiAgentRepository agents,
    IKnowledgeDocumentRepository documents,
    IAgentKnowledgeRepository assignments,
    TenantAuthorizer authorizer,
    ITextEmbeddingGenerator embeddings)
{
    /// <summary>
    /// The most documents the assignment screen will offer.
    /// </summary>
    /// <remarks>
    /// The same bound as a full page of the knowledge list, times five. A
    /// workspace that outgrows it needs a screen that can search rather than a
    /// longer list, and returning everything regardless would eventually send a
    /// megabyte of titles to render a set of checkboxes.
    /// </remarks>
    public const int MaxDocuments = 500;

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

    public async Task<Versioned<AgentKnowledgeView>?> HandleAsync(
        GetAgentKnowledgeQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        TenantId tenantId = TenantId.From(query.TenantId);
        AiAgentId agentId = AiAgentId.From(query.AiAgentId);

        if (!await _authorizer
            .RequireAsync(tenantId, PlatformPermissions.KnowledgeView, cancellationToken)
            .ConfigureAwait(false))
        {
            return null;
        }

        if (!await _authorizer
            .RequireAsync(tenantId, PlatformPermissions.AiAgentsView, cancellationToken)
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

        IReadOnlyList<KnowledgeDocumentId> assigned = await _assignments
            .ListAssignedAsync(tenantId, agentId, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<KnowledgeDocument> available = await _documents
            .ListAllAsync(tenantId, MaxDocuments, cancellationToken)
            .ConfigureAwait(false);

        HashSet<KnowledgeDocumentId> ticked = [.. assigned];

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
            agent.Version);
    }
}
