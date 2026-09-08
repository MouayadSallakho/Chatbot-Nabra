using OmniAI.Application.Concurrency;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Knowledge;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Knowledge.Persistence;

/// <summary>
/// Which documents each AI agent may draw on.
/// </summary>
public interface IAgentKnowledgeRepository
{
    /// <summary>The documents currently assigned to one agent.</summary>
    Task<IReadOnlyList<KnowledgeDocumentId>> ListAssignedAsync(
        TenantId tenantId,
        AiAgentId aiAgentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces an agent's whole assignment set, and moves the agent's version
    /// with it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A replacement rather than add and remove, because that is what the screen
    /// does: somebody reads the current set, ticks and unticks, and saves what
    /// they now want. Sending the difference would mean the client computing it
    /// from a view that may already be out of date.
    /// </para>
    /// <para>
    /// The agent row is updated in the same transaction so its entity tag
    /// advances. Writing only the join table would let a second editor save
    /// against a version that no longer describes the agent, and neither person
    /// would ever be told.
    /// </para>
    /// </remarks>
    /// <returns>The agent's new version.</returns>
    /// <exception cref="OmniAI.Application.AiAgents.AiAgentVersionMismatchException">
    /// The agent changed since the caller read it. Nothing was written.
    /// </exception>
    Task<ConcurrencyToken> ReplaceAsync(
        AiAgent agent,
        ConcurrencyToken expected,
        IReadOnlyList<KnowledgeDocumentId> documentIds,
        DateTimeOffset assignedAt,
        CancellationToken cancellationToken = default);
}
