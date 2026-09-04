namespace OmniAI.Api.AiAgents;

/// <summary>
/// Request body for <c>PUT /api/tenants/{tenantId}/ai-agents/{aiAgentId}/name</c>.
/// </summary>
/// <param name="Name">The new display name.</param>
/// <remarks>
/// One field, and the endpoint it belongs to addresses the name directly. That is
/// what makes this a replacement of a named sub-resource rather than a partial
/// update of the agent: there is no field whose absence has to be interpreted, so
/// the request cannot mean two different things depending on what the client left
/// out. Generic <c>PATCH</c> semantics are deliberately not offered.
/// </remarks>
public sealed record RenameAiAgentRequest(string? Name);
