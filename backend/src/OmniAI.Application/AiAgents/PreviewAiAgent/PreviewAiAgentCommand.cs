namespace OmniAI.Application.AiAgents.PreviewAiAgent;

/// <summary>
/// Request to try an AI agent out with a single message.
/// </summary>
/// <param name="TenantId">The tenant that must own the agent, from the route.</param>
/// <param name="AiAgentId">The agent to try, from the route.</param>
/// <param name="Message">The message to send it, exactly as the caller supplied it.</param>
/// <remarks>
/// <para>
/// <strong>There is no instructions field, and that is a security boundary rather
/// than an omission.</strong> A caller cannot supply, override, extend or prefix
/// the agent's instructions through this command, because there is nowhere to put
/// them: the handler reads them from the persisted, tenant-scoped agent and from
/// nowhere else. If this record had an <c>Instructions</c> property, the preview
/// endpoint would become a way to run arbitrary instructions on this platform's
/// AI budget, and a way to make an agent behave in a manner its owner never
/// configured -- so the property does not exist, and no future edit can add one
/// without the reviewer seeing exactly that.
/// </para>
/// <para>
/// The tenant is part of the request, not an ambient value. Previewing an agent by
/// identity alone is not expressible.
/// </para>
/// <para>
/// One message, not a conversation. Phase 8's preview is stateless and
/// single-turn: nothing is stored, nothing is recalled, and there is no history
/// for a caller to submit. The underlying generation contract can carry an ordered
/// conversation so a later runtime can reuse it, but this command deliberately
/// cannot -- accepting a caller-supplied history here would mean accepting
/// caller-written <em>assistant</em> turns, which is a way to put words in the
/// agent's mouth and steer what it says next.
/// </para>
/// </remarks>
public sealed record PreviewAiAgentCommand(Guid TenantId, Guid AiAgentId, string? Message);
