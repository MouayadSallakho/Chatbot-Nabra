using OmniAI.Application.AiAgents.Persistence;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Tenants;
using OmniAI.Application.Tenants.Persistence;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.AiAgents.CreateAiAgent;

/// <summary>
/// Creates an AI agent under a tenant.
/// </summary>
/// <remarks>
/// A plain class with constructor dependencies, resolved by name at the
/// composition root. There is still no mediator, command bus or pipeline: a
/// dispatcher would add indirection, reflection and a registration convention
/// while removing nothing, and it would hide a call graph that is currently
/// readable in one hop.
/// </remarks>
public sealed class CreateAiAgentHandler(
    ITenantRepository tenants,
    IAiAgentRepository agents,
    TimeProvider clock)
{
    private readonly ITenantRepository _tenants =
        tenants ?? throw new ArgumentNullException(nameof(tenants));

    private readonly IAiAgentRepository _agents =
        agents ?? throw new ArgumentNullException(nameof(agents));

    private readonly TimeProvider _clock =
        clock ?? throw new ArgumentNullException(nameof(clock));

    /// <summary>Creates the agent and returns its read model and version.</summary>
    /// <exception cref="Domain.DomainValidationException">
    /// The tenant id is empty, or the name or instructions break a Domain rule.
    /// Raised by the value objects, not re-implemented here.
    /// </exception>
    /// <exception cref="TenantNotFoundException">No such tenant.</exception>
    public async Task<VersionedAiAgentView> HandleAsync(
        CreateAiAgentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Input first, existence second. A malformed name is wrong however the
        // rest of the world looks and can be answered without touching the
        // database, whereas whether a tenant exists is a question about stored
        // state. So a request that is both malformed and aimed at a missing
        // tenant is reported as malformed, which is the ordinary precedence.
        TenantId tenantId = TenantId.From(command.TenantId);
        AiAgentName name = AiAgentName.Create(command.Name);
        AgentInstructions instructions = AgentInstructions.Create(command.Instructions);

        // The ordinary answer to "create an agent under a tenant that is not
        // there". Without it the insert would fail on the foreign key, and a
        // routine mistake would surface as an unexplained server error instead of
        // a plain "no such tenant". The foreign key still exists and still
        // refuses the row -- it is the defence that survives this check being
        // raced -- but it is no longer what a normal caller meets.
        if (await _tenants.FindByIdAsync(tenantId, cancellationToken).ConfigureAwait(false) is null)
        {
            throw TenantNotFoundException.ForId(tenantId.Value);
        }

        // Identity, initial status and the update mark come from the aggregate.
        // The use case supplies only the instant, and does not read the clock
        // itself: TimeProvider is injected so the same code is deterministic
        // under test and correct in production.
        AiAgent agent = AiAgent.Create(tenantId, name, instructions, _clock.GetUtcNow());

        // The version the stored agent now has. Returned so the caller can make a
        // conditional write immediately, without reading the agent back first.
        ConcurrencyToken version =
            await _agents.AddAsync(agent, cancellationToken).ConfigureAwait(false);

        return new VersionedAiAgentView(AiAgentView.From(agent), version);
    }
}
