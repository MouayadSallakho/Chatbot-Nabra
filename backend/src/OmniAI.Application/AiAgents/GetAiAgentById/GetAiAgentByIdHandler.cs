using OmniAI.Application.AiAgents.Persistence;
using OmniAI.Application.Authorization;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Tenants;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.AiAgents.GetAiAgentById;

/// <summary>
/// Reads one AI agent within a tenant.
/// </summary>
public sealed class GetAiAgentByIdHandler(IAiAgentRepository agents, TenantAuthorizer authorizer)
{
    private readonly IAiAgentRepository _agents =
        agents ?? throw new ArgumentNullException(nameof(agents));

    private readonly TenantAuthorizer _authorizer =
        authorizer ?? throw new ArgumentNullException(nameof(authorizer));

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
    public async Task<Versioned<AiAgentView>?> HandleAsync(
        GetAiAgentByIdQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        TenantId tenantId = TenantId.From(query.TenantId);
        AiAgentId agentId = AiAgentId.From(query.AiAgentId);

        // Authorisation is resolved before the agent is read, and that ordering
        // is the disclosure rule rather than an optimisation. A member without
        // ai-agents.view is refused identically whether the id they named is
        // real, belongs to another tenant, or was invented -- nothing is looked
        // up before the refusal, so there is no difference for them to observe.
        // Checking the permission after the lookup would answer 403 for an agent
        // that exists and 404 for one that does not, turning a permission they
        // lack into a way to enumerate the ids they cannot see.
        if (!await _authorizer
            .RequireAsync(tenantId, PlatformPermissions.AiAgentsView, cancellationToken)
            .ConfigureAwait(false))
        {
            return null;
        }

        Versioned<AiAgent>? stored = await _agents
            .FindAsync(tenantId, agentId, cancellationToken)
            .ConfigureAwait(false);

        // The version travels with the representation, so a caller that reads an
        // agent always has what it needs to change it safely afterwards.
        return stored is null
            ? null
            : new Versioned<AiAgentView>(AiAgentView.From(stored.Value), stored.Version);
    }
}
