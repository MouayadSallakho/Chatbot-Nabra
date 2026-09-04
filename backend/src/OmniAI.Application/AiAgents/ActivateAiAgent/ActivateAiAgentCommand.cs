using OmniAI.Application.Concurrency;

namespace OmniAI.Application.AiAgents.ActivateAiAgent;

/// <summary>
/// Request to switch an AI agent on.
/// </summary>
/// <param name="TenantId">The tenant that must own the agent, from the route.</param>
/// <param name="AiAgentId">The agent to activate, from the route.</param>
/// <param name="Expected">The version of the agent the caller believes it is changing.</param>
/// <remarks>
/// No payload. Activation is a lifecycle decision about an existing agent, not a
/// change to its content, and giving this command a body would invite exactly the
/// mixing of concerns the separate rename and instructions operations exist to
/// avoid.
/// <para>
/// <strong>Activation is Domain lifecycle activation only.</strong> No AI
/// provider is contacted, no model is selected, no channel is published to and no
/// credential is read -- none of those concepts exist yet. When they do, whether
/// an agent is additionally <em>ready</em> for a given channel becomes a
/// separate question answered by whatever orchestrates publishing, not by this
/// command.
/// </para>
/// </remarks>
public sealed record ActivateAiAgentCommand(
    Guid TenantId,
    Guid AiAgentId,
    ConcurrencyToken Expected);
