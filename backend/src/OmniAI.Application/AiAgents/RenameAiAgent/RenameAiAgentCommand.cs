using OmniAI.Application.Concurrency;

namespace OmniAI.Application.AiAgents.RenameAiAgent;

/// <summary>
/// Request to change an AI agent's display name.
/// </summary>
/// <param name="TenantId">The tenant that must own the agent, from the route.</param>
/// <param name="AiAgentId">The agent to rename, from the route.</param>
/// <param name="Name">The new display name, exactly as the caller supplied it.</param>
/// <param name="Expected">The version of the agent the caller believes it is changing.</param>
/// <remarks>
/// One field, not a patch document. A focused command says what it does, and the
/// absence of every other field is the guarantee that this operation cannot
/// quietly change instructions or status as a side effect. Generic partial-update
/// semantics -- where a missing field might mean "leave alone" or might mean "set
/// to null" -- are deliberately not modelled.
/// </remarks>
public sealed record RenameAiAgentCommand(
    Guid TenantId,
    Guid AiAgentId,
    string? Name,
    ConcurrencyToken Expected);
