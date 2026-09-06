namespace OmniAI.Application.Authorization.GetTenantRoleById;

/// <summary>Asks for one role within a workspace.</summary>
/// <param name="TenantId">The workspace that must own the role.</param>
/// <param name="TenantRoleId">The role's identity.</param>
/// <remarks>
/// Both identifiers are required, and the pair is what the lookup uses. There is
/// no shape of this query that names a role without naming the workspace, which
/// is what stops a role id from another workspace being an alternative route to
/// reading it.
/// </remarks>
public sealed record GetTenantRoleByIdQuery(Guid TenantId, Guid TenantRoleId);
