using OmniAI.Application.AiAgents.Persistence;
using OmniAI.Application.Tenants;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.AiAgents.GetAiAgentById;

/// <summary>
/// Reads one AI agent within a tenant.
/// </summary>
public sealed class GetAiAgentByIdHandler(IAiAgentRepository agents, TenantAccessGuard access)
{
    private readonly IAiAgentRepository _agents =
        agents ?? throw new ArgumentNullException(nameof(agents));

    private readonly TenantAccessGuard _access =
        access ?? throw new ArgumentNullException(nameof(access));

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

        // Membership is checked before the agent is read, and before any
        // precondition is evaluated. A caller who does not belong to this tenant
        // is answered exactly as they would be for an agent that does not exist,
        // so a valid entity tag for somebody else's agent buys nothing: it never
        // reaches the version comparison.
        if (!await _access.IsCurrentUserMemberAsync(tenantId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

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
