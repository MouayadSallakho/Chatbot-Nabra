using OmniAI.Application.AiAgents.Persistence;
using OmniAI.Application.Authorization;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Tenants;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.AiAgents.UpdateAiAgentInstructions;

/// <summary>
/// Replaces an AI agent's instructions.
/// </summary>
public sealed class UpdateAiAgentInstructionsHandler(
    IAiAgentRepository agents,
    TenantAuthorizer authorizer,
    TimeProvider clock)
{
    private readonly IAiAgentRepository _agents =
        agents ?? throw new ArgumentNullException(nameof(agents));

    private readonly TenantAuthorizer _authorizer =
        authorizer ?? throw new ArgumentNullException(nameof(authorizer));

    private readonly TimeProvider _clock =
        clock ?? throw new ArgumentNullException(nameof(clock));

    /// <summary>Updates the instructions.</summary>
    /// <returns>
    /// The updated read model and its new version, or <see langword="null"/> when this tenant has no
    /// agent with that id -- including when the agent exists under a different
    /// tenant, which is indistinguishable here because the lookup itself is
    /// tenant-scoped.
    /// </returns>
    /// <remarks>
    /// Permitted in every status, including <c>Active</c>. Correcting a live
    /// agent is the normal way a business fixes its behaviour, and requiring it to
    /// be disabled first would mean taking the agent off the air to fix a typo.
    /// </remarks>
    /// <exception cref="Domain.DomainValidationException">
    /// An identifier is empty, or the instructions break a Domain rule -- blank,
    /// over the platform maximum, or carrying control characters that are not
    /// ordinary formatting.
    /// </exception>
    /// <exception cref="AiAgentVersionMismatchException">
    /// The agent is no longer at the expected version.
    /// </exception>
    public async Task<Versioned<AiAgentView>?> HandleAsync(
        UpdateAiAgentInstructionsCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        TenantId tenantId = TenantId.From(command.TenantId);
        AiAgentId agentId = AiAgentId.From(command.AiAgentId);
        AgentInstructions instructions = AgentInstructions.Create(command.Instructions);

        // Authorisation is resolved before the agent is read, and before any
        // precondition is evaluated. Two different refusals come out of this one
        // line, and they must stay different: a caller who does not belong to
        // this tenant is answered exactly as they would be for an agent that
        // does not exist, while a member whose roles do not carry this
        // permission is told so. Neither reaches the version comparison, so a
        // valid entity tag for somebody else's agent buys nothing -- and a
        // member without the permission cannot use a 412 to discover which
        // agent ids are real.
        if (!await _authorizer
            .RequireAsync(tenantId, PlatformPermissions.AiAgentsEdit, cancellationToken)
            .ConfigureAwait(false))
        {
            return null;
        }

        Versioned<AiAgent>? stored = await _agents
            .FindAsync(tenantId, agentId, cancellationToken)
            .ConfigureAwait(false);

        if (stored is null)
        {
            return null;
        }

        AiAgentConcurrency.RequireExpectedVersion(stored, command.Expected);

        stored.Value.UpdateInstructions(instructions, _clock.GetUtcNow());

        ConcurrencyToken version = await _agents
            .UpdateAsync(stored.Value, command.Expected, cancellationToken)
            .ConfigureAwait(false);

        return new Versioned<AiAgentView>(AiAgentView.From(stored.Value), version);
    }
}
