using OmniAI.Application.AiAgents.Persistence;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Tenants;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.AiAgents.ActivateAiAgent;

/// <summary>
/// Switches an AI agent on.
/// </summary>
public sealed class ActivateAiAgentHandler(
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

    /// <summary>Activates the agent.</summary>
    /// <returns>
    /// The updated read model and its new version, or <see langword="null"/> when this tenant has no
    /// agent with that id -- including when the agent exists under a different
    /// tenant, which is indistinguishable here because the lookup itself is
    /// tenant-scoped.
    /// </returns>
    /// <remarks>
    /// Works from <c>Draft</c> and from <c>Disabled</c>, and activating an
    /// already active agent is a no-op that writes nothing and leaves the update
    /// mark alone.
    /// <para>
    /// There is no readiness or configuration check here, and that is not an
    /// omission. The accepted Domain decision is that an agent cannot exist
    /// through public Domain APIs with an invalid name or invalid instructions, so
    /// a guard on those could not fail. <c>Draft</c> is a publication state, not
    /// an incomplete aggregate. Provider, channel and knowledge readiness checks
    /// belong to publishing orchestration, and will be written when those
    /// concepts exist rather than stubbed here now.
    /// </para>
    /// </remarks>
    /// <exception cref="Domain.DomainValidationException">An identifier is empty.</exception>
    /// <exception cref="AiAgentVersionMismatchException">
    /// The agent is no longer at the expected version.
    /// </exception>
    public async Task<VersionedAiAgentView?> HandleAsync(
        ActivateAiAgentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        TenantId tenantId = TenantId.From(command.TenantId);
        AiAgentId agentId = AiAgentId.From(command.AiAgentId);

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

        // Checked here as well as by the store, because an idempotent activation
        // writes nothing and so would never reach a database predicate to fail
        // against. See AiAgentConcurrency.
        AiAgentConcurrency.RequireExpectedVersion(stored, command.Expected);

        stored.Agent.Activate(_clock.GetUtcNow());

        ConcurrencyToken version = await _agents
            .UpdateAsync(stored.Agent, command.Expected, cancellationToken)
            .ConfigureAwait(false);

        return new VersionedAiAgentView(AiAgentView.From(stored.Agent), version);
    }
}
