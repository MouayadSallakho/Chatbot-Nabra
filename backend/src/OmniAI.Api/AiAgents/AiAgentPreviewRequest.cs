namespace OmniAI.Api.AiAgents;

/// <summary>
/// The body of a preview request.
/// </summary>
/// <param name="Message">The message to send the agent.</param>
/// <remarks>
/// <para>
/// <strong>One field, and the absent one matters more than the present one.</strong>
/// There is no <c>instructions</c> property, so a client cannot supply, replace,
/// extend or prepend the agent's instructions through this endpoint. Model binding
/// ignores any such field a caller sends, and nothing downstream would read it: the
/// handler takes instructions from the persisted, tenant-scoped agent alone.
/// </para>
/// <para>
/// Without that rule, preview would be an open text-generation endpoint wearing an
/// agent's name -- anyone could run instructions of their choosing on this
/// platform's AI account, and could make an agent appear to behave in ways its
/// owner never configured. Keeping the field out of the contract is what makes
/// that impossible rather than merely discouraged.
/// </para>
/// <para>
/// Nullable so that a missing or explicitly null field reaches the use case and is
/// rejected there with an explanation, rather than being bound to an empty string
/// and treated as though somebody had sent a blank message.
/// </para>
/// </remarks>
public sealed record AiAgentPreviewRequest(string? Message);
