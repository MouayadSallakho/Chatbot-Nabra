using OmniAI.Application.AiAgents.Persistence;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.AiAgents.GetAiAgentById;

/// <summary>
/// Reads one AI agent within a tenant.
/// </summary>
public sealed class GetAiAgentByIdHandler(IAiAgentRepository agents)
{
    private readonly IAiAgentRepository _agents =
        agents ?? throw new ArgumentNullException(nameof(agents));

    /// <summary>Finds the agent.</summary>
    /// <returns>
    /// The agent's read model and version, or <see langword="null"/> when this tenant has no
    /// agent with that id. Absence is a result, not a failure: it needs no
    /// exception type, produces no stack unwind and no error-level log, and the
    /// API maps it to its own not-found response.
    /// </returns>
    /// <remarks>
    /// There is deliberately no check that the tenant exists. A request naming a
    /// tenant that is not there finds no agent, and is answered exactly like a
    /// request naming a real tenant that does not own the agent -- so this
    /// endpoint cannot be used to discover which tenant ids are real. Adding an
    /// existence check would create that difference for no benefit, since the
    /// answer in both cases is the same.
    /// </remarks>
    /// <exception cref="Domain.DomainValidationException">
    /// Either identifier is empty. Rejected rather than treated as a lookup that
    /// happens to find nothing, because an empty GUID is malformed input rather
    /// than a resource that does not exist.
    /// </exception>
    public async Task<VersionedAiAgentView?> HandleAsync(
        GetAiAgentByIdQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        TenantId tenantId = TenantId.From(query.TenantId);
        AiAgentId agentId = AiAgentId.From(query.AiAgentId);

        VersionedAiAgent? stored = await _agents
            .FindAsync(tenantId, agentId, cancellationToken)
            .ConfigureAwait(false);

        // The version travels with the representation, so a caller that reads an
        // agent always has what it needs to change it safely afterwards.
        return stored is null
            ? null
            : new VersionedAiAgentView(AiAgentView.From(stored.Agent), stored.Version);
    }
}
