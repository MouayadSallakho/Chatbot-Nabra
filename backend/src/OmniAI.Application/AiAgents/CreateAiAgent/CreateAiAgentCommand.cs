namespace OmniAI.Application.AiAgents.CreateAiAgent;

/// <summary>
/// Request to create an AI agent for a tenant.
/// </summary>
/// <param name="TenantId">The owning tenant, taken from the route.</param>
/// <param name="Name">Display name, exactly as the caller supplied it.</param>
/// <param name="Instructions">Instruction text, exactly as the caller supplied it.</param>
/// <remarks>
/// The strings are raw and unvalidated on purpose. Validating them here would
/// duplicate rules that <c>AiAgentName</c> and <c>AgentInstructions</c> already
/// own, and two copies of a rule drift. The handler converts them into value
/// objects and the Domain decides whether they are acceptable.
/// <para>
/// <c>TenantId</c> is a <see cref="Guid"/> rather than a <c>TenantId</c> because
/// at this point it is still untrusted route input; the handler converts it, so
/// an empty identifier is rejected by the Domain rule instead of being silently
/// accepted as <c>default</c>.
/// </para>
/// <para>
/// There is no agent id, no status, no timestamp and no provider, model or
/// external-assistant field, so a caller can choose none of them. A
/// client-chosen agent id would let one caller collide with -- or guess --
/// another agent identity; a client-chosen status would let an agent switch
/// itself on at creation, which is precisely the decision the Domain reserves for
/// the tenant.
/// </para>
/// </remarks>
public sealed record CreateAiAgentCommand(Guid TenantId, string? Name, string? Instructions);
