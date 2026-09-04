namespace OmniAI.Api.AiAgents;

/// <summary>
/// Request body for
/// <c>PUT /api/tenants/{tenantId}/ai-agents/{aiAgentId}/instructions</c>.
/// </summary>
/// <param name="Instructions">The new instruction text.</param>
/// <remarks>
/// One field, addressing the instructions sub-resource directly, for the same
/// reason as the rename request.
/// <para>
/// The text arrives as an ordinary JSON string and is passed through untouched.
/// Interior line breaks, blank lines, indentation and tabs survive exactly as the
/// author wrote them -- JSON escapes them on the wire, the serialiser restores
/// them, and nothing in this layer re-wraps or collapses whitespace.
/// </para>
/// </remarks>
public sealed record UpdateAiAgentInstructionsRequest(string? Instructions);
