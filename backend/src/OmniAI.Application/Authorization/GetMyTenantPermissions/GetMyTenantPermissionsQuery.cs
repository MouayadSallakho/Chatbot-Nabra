namespace OmniAI.Application.Authorization.GetMyTenantPermissions;

/// <summary>Asks what the signed-in caller may do in one workspace.</summary>
/// <param name="TenantId">The workspace.</param>
/// <remarks>
/// There is deliberately no user id. The answer is always about the caller, so a
/// field naming somebody else would be a field an endpoint could be persuaded to
/// fill in -- and this is precisely the query that must never be able to report
/// on a colleague.
/// </remarks>
public sealed record GetMyTenantPermissionsQuery(Guid TenantId);
