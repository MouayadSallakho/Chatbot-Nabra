namespace OmniAI.Application.AiAgents.GetAiAgentById;

/// <summary>
/// Request for one AI agent's currently modelled information.
/// </summary>
/// <param name="TenantId">The tenant that must own the agent, from the route.</param>
/// <param name="AiAgentId">The agent's raw identifier, from the route.</param>
/// <remarks>
/// Both are <see cref="Guid"/> rather than Domain identity types, because at this
/// point they are still untrusted input. The handler converts them, so an empty
/// identifier is rejected by the Domain rule instead of being silently accepted
/// as <c>default</c>.
/// <para>
/// The tenant is part of the request, not an ambient value, and not optional.
/// Reading an agent by identity alone is not expressible.
/// </para>
/// </remarks>
public sealed record GetAiAgentByIdQuery(Guid TenantId, Guid AiAgentId);
