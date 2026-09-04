using OmniAI.Application.Concurrency;

namespace OmniAI.Application.AiAgents.DisableAiAgent;

/// <summary>
/// Request to switch an AI agent off.
/// </summary>
/// <param name="TenantId">The tenant that must own the agent, from the route.</param>
/// <param name="AiAgentId">The agent to disable, from the route.</param>
/// <param name="Expected">The version of the agent the caller believes it is changing.</param>
/// <remarks>
/// No payload, for the same reason as activation: this is a lifecycle decision
/// about an existing agent rather than a change to its content.
/// <para>
/// Disabling is not deletion. The agent and its configuration remain, and it can
/// be switched on again. Deletion and archival are deferred and have their own
/// retention questions.
/// </para>
/// </remarks>
public sealed record DisableAiAgentCommand(
    Guid TenantId,
    Guid AiAgentId,
    ConcurrencyToken Expected);
