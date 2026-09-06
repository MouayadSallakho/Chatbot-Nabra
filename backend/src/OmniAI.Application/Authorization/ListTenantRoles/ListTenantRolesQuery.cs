namespace OmniAI.Application.Authorization.ListTenantRoles;

/// <summary>Asks for a workspace's roles.</summary>
/// <param name="TenantId">The workspace. Validated by the Domain, not here.</param>
/// <remarks>
/// It carries no user id. Who is asking comes from the authenticated session, so
/// there is no field an endpoint, a test or a future caller could fill in with
/// somebody else's identity.
/// </remarks>
public sealed record ListTenantRolesQuery(Guid TenantId);
