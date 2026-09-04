namespace OmniAI.Application.AiAgents.PreviewAiAgent;

/// <summary>
/// What a preview produced.
/// </summary>
/// <param name="Reply">The agent's generated answer.</param>
/// <remarks>
/// <para>
/// One field. In particular it does <strong>not</strong> carry the instructions
/// that produced it. Those are business configuration -- they can name internal
/// policies, pricing rules, escalation paths and things the agent is told never to
/// say -- and the caller already had to be entitled to the agent to get here, so
/// echoing them back adds nothing and creates a second place they can leak from.
/// The person previewing can read the agent's instructions through the agent
/// endpoint if they want them.
/// </para>
/// <para>
/// It carries no model name, provider name, token count, cost, latency or request
/// id either. The result contract has none of those yet, and this is not where
/// they should quietly appear.
/// </para>
/// <para>
/// Nothing about this type is persisted. There is no preview id, because there is
/// no preview record: the request was answered and forgotten.
/// </para>
/// </remarks>
public sealed record AiAgentPreview(string Reply);
