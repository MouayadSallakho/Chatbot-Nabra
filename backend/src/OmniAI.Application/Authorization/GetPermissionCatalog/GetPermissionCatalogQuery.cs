namespace OmniAI.Application.Authorization.GetPermissionCatalog;

/// <summary>Asks for the capabilities a role in this workspace may grant.</summary>
/// <param name="TenantId">The workspace whose role editor is asking.</param>
/// <remarks>
/// The catalogue is the same for every workspace -- it is the product's set of
/// capabilities, not a tenant-authored list. The tenant is still named, because
/// the answer is only published to somebody entitled to see that workspace's
/// roles: an endpoint with no tenant would have nothing to authorise against and
/// would publish the platform's capability list to any signed-in caller.
/// </remarks>
public sealed record GetPermissionCatalogQuery(Guid TenantId);
