using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Knowledge;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Knowledge.Persistence;

/// <summary>One stored chunk that came back from a vector search.</summary>
/// <remarks>
/// <paramref name="Distance"/> is cosine distance as the database computed it:
/// zero for identical direction, larger for less alike. It is turned into a
/// similarity above rather than here, so this record stays exactly what the
/// database returned.
/// </remarks>
public sealed record KnowledgeMatch(
    Guid DocumentId,
    string DocumentTitle,
    int Ordinal,
    string Text,
    double Distance);

/// <summary>
/// Vector search over a workspace's indexed knowledge.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Similarity is never the boundary.</strong> Both methods take the
/// workspace, the agent and the profile, and the query behind them filters on all
/// three plus the document being enabled and currently indexed. A vector search
/// that ranked across everything and trusted the ranking to keep workspaces apart
/// would leak the moment two workspaces wrote something similar -- which, for
/// businesses in the same industry, is the normal case rather than the unlucky
/// one.
/// </para>
/// <para>
/// The eligibility question exists separately so a turn can find out that there
/// is nothing to search <em>before</em> paying to embed the customer's message.
/// </para>
/// </remarks>
public interface IKnowledgeSearchStore
{
    /// <summary>
    /// Whether this agent has any chunk that could be returned at all.
    /// </summary>
    /// <remarks>
    /// Exactly the filters the search applies, minus the vector. A false answer
    /// means the search would return nothing, so there is no reason to embed
    /// anything.
    /// </remarks>
    Task<bool> HasEligibleChunksAsync(
        TenantId tenantId,
        AiAgentId aiAgentId,
        EmbeddingProfileId profile,
        CancellationToken cancellationToken = default);

    /// <summary>The closest eligible chunks to a query vector, nearest first.</summary>
    Task<IReadOnlyList<KnowledgeMatch>> SearchAsync(
        TenantId tenantId,
        AiAgentId aiAgentId,
        EmbeddingProfileId profile,
        EmbeddingVector query,
        int limit,
        CancellationToken cancellationToken = default);
}
