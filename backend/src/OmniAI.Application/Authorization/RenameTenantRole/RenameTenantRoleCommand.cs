using OmniAI.Application.Concurrency;

namespace OmniAI.Application.Authorization.RenameTenantRole;

/// <summary>Asks for a custom role's name to be changed.</summary>
/// <param name="TenantId">The workspace that must own the role.</param>
/// <param name="TenantRoleId">The role to rename.</param>
/// <param name="Name">The new name. Validated by the Domain.</param>
/// <param name="Expected">
/// The version the caller believes it is changing, read from the request's
/// <c>If-Match</c> header. Required: without it the server cannot tell a
/// deliberate rename from one computed against a screen somebody opened before
/// the role was last changed.
/// </param>
public sealed record RenameTenantRoleCommand(
    Guid TenantId,
    Guid TenantRoleId,
    string? Name,
    ConcurrencyToken Expected);
