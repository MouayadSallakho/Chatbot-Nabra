using OmniAI.Application.AiAgents.Persistence;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Tenants;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.AiAgents.RenameAiAgent;

/// <summary>
/// Changes an AI agent's display name, if it is still at the version the caller
/// expected.
/// </summary>
public sealed class RenameAiAgentHandler(
    IAiAgentRepository agents,
    TenantAccessGuard access,
    TimeProvider clock)
{
    private readonly IAiAgentRepository _agents =
        agents ?? throw new ArgumentNullException(nameof(agents));

    private readonly TenantAccessGuard _access =
        access ?? throw new ArgumentNullException(nameof(access));

    private readonly TimeProvider _clock =
        clock ?? throw new ArgumentNullException(nameof(clock));

    /// <summary>Renames the agent.</summary>
    /// <returns>
    /// The updated read model and its new version, or <see langword="null"/> when
    /// this tenant has no agent with that id -- including when the agent exists
    /// under a different tenant, which is indistinguishable here because the
    /// lookup itself is tenant-scoped.
    /// </returns>
    /// <exception cref="Domain.DomainValidationException">
    /// An identifier is empty, or the name breaks a Domain rule. The Domain value
    /// object remains the only authority on what a name may be; this handler
    /// re-implements none of it.
    /// </exception>
    /// <exception cref="AiAgentVersionMismatchException">
    /// The agent is no longer at the expected version.
    /// </exception>
    public async Task<VersionedAiAgentView?> HandleAsync(
        RenameAiAgentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        TenantId tenantId = TenantId.From(command.TenantId);
        AiAgentId agentId = AiAgentId.From(command.AiAgentId);
        AiAgentName name = AiAgentName.Create(command.Name);

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

        if (stored is null)
        {
            return null;
        }

        AiAgentConcurrency.RequireExpectedVersion(stored, command.Expected);

        // Renaming to the name it already has is a no-op inside the aggregate,
        // including the update mark, so UpdateAsync then has nothing to write.
        stored.Agent.Rename(name, _clock.GetUtcNow());

        ConcurrencyToken version = await _agents
            .UpdateAsync(stored.Agent, command.Expected, cancellationToken)
            .ConfigureAwait(false);

        return new VersionedAiAgentView(AiAgentView.From(stored.Agent), version);
    }
}
