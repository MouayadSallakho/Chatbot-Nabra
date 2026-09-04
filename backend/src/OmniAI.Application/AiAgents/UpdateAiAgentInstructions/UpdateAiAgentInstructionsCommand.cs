using OmniAI.Application.Concurrency;

namespace OmniAI.Application.AiAgents.UpdateAiAgentInstructions;

/// <summary>
/// Request to replace an AI agent's instructions.
/// </summary>
/// <param name="TenantId">The tenant that must own the agent, from the route.</param>
/// <param name="AiAgentId">The agent to update, from the route.</param>
/// <param name="Instructions">The new instruction text, exactly as the caller supplied it.</param>
/// <param name="Expected">The version of the agent the caller believes it is changing.</param>
/// <remarks>
/// The text is carried through untouched. Interior line breaks, blank lines,
/// indentation and tabs are meaningful in instructions, so nothing in this layer
/// normalises, re-wraps or collapses them -- the Domain value object trims only
/// the outside and preserves the rest exactly.
/// <para>
/// A replacement, not a patch: the new text stands alone rather than being merged
/// into what is stored. There is no version, no expected-current-value and no
/// diff, because instruction history is not modelled -- see the deferred list --
/// and pretending otherwise would imply a rollback this platform cannot perform.
/// </para>
/// </remarks>
public sealed record UpdateAiAgentInstructionsCommand(
    Guid TenantId,
    Guid AiAgentId,
    string? Instructions,
    ConcurrencyToken Expected);
