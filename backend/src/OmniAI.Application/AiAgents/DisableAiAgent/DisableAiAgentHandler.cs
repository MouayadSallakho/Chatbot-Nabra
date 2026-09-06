using OmniAI.Application.AiAgents.Persistence;
using OmniAI.Application.Authorization;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Tenants;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.AiAgents.DisableAiAgent;

/// <summary>
/// Switches an AI agent off.
/// </summary>
public sealed class DisableAiAgentHandler(
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

    /// <summary>Disables the agent.</summary>
    /// <returns>
    /// The updated read model and its new version, or <see langword="null"/> when this tenant has no
    /// agent with that id -- including when the agent exists under a different
    /// tenant, which is indistinguishable here because the lookup itself is
    /// tenant-scoped.
    /// </returns>
    /// <remarks>
    /// Disabling an already disabled agent is a no-op that writes nothing.
    /// Disabling a <c>Draft</c> agent is refused by the Domain rather than
    /// silently accepted: a draft has never been switched on, and there is no path
    /// back from <c>Disabled</c> to <c>Draft</c>, so quietly accepting it would
    /// strand the agent in a state the tenant did not ask for.
    /// </remarks>
    /// <exception cref="Domain.DomainValidationException">An identifier is empty.</exception>
    /// <exception cref="Domain.DomainStateException">
    /// The agent is still a draft. Deliberately allowed to propagate: the Domain
    /// owns the rule, and the edge decides how to report it.
    /// </exception>
    /// <exception cref="AiAgentVersionMismatchException">
    /// The agent is no longer at the expected version.
    /// </exception>
    public async Task<Versioned<AiAgentView>?> HandleAsync(
        DisableAiAgentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        TenantId tenantId = TenantId.From(command.TenantId);
        AiAgentId agentId = AiAgentId.From(command.AiAgentId);

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
            .RequireAsync(tenantId, PlatformPermissions.AiAgentsLifecycle, cancellationToken)
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

        // Before the Domain rule, deliberately. A caller whose version is stale
        // has not seen this agent as it is now, so it is told that rather than
        // being told about a draft rule describing a state it has never seen.
        AiAgentConcurrency.RequireExpectedVersion(stored, command.Expected);

        // Throws for a draft, before anything is written. Nothing is persisted
        // and no partial change is left behind.
        stored.Value.Disable(_clock.GetUtcNow());

        ConcurrencyToken version = await _agents
            .UpdateAsync(stored.Value, command.Expected, cancellationToken)
            .ConfigureAwait(false);

        return new Versioned<AiAgentView>(AiAgentView.From(stored.Value), version);
    }
}
