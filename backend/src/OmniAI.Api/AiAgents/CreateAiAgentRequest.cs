namespace OmniAI.Api.AiAgents;

/// <summary>
/// Request body for <c>POST /api/tenants/{tenantId}/ai-agents</c>.
/// </summary>
/// <param name="Name">Display name of the agent, for example <c>Sales Assistant</c>.</param>
/// <param name="Instructions">What the agent is for, in the business's own words.</param>
/// <remarks>
/// Both properties are nullable strings and carry no validation attributes. That
/// is deliberate: <c>AiAgentName</c> and <c>AgentInstructions</c> already own the
/// rules, and a <c>[Required]</c> or <c>[StringLength]</c> here would be a second
/// copy of them that can disagree with the first. What the transport layer does
/// check is transport-level only -- that the body is syntactically valid JSON of
/// this shape -- and a malformed body is rejected before this type exists.
/// <para>
/// Two fields, and no others. The owning tenant comes from the route, not the
/// body, so a caller cannot address one tenant and write into another. There is
/// no <c>id</c>, no <c>status</c>, no <c>createdAt</c> and no <c>updatedAt</c>,
/// so a caller can choose neither identity, nor lifecycle state, nor history.
/// There is also no <c>model</c>, <c>provider</c>, <c>temperature</c>,
/// <c>assistantId</c> or any other provider field: none exists in the Domain, and
/// accepting one here would publish a contract with nothing behind it.
/// </para>
/// </remarks>
public sealed record CreateAiAgentRequest(string? Name, string? Instructions);
