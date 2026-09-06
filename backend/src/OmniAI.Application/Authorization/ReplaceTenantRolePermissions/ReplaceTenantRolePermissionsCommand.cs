using OmniAI.Application.Concurrency;

namespace OmniAI.Application.Authorization.ReplaceTenantRolePermissions;

/// <summary>Asks for a custom role's entire permission set to be replaced.</summary>
/// <param name="TenantId">The workspace that must own the role.</param>
/// <param name="TenantRoleId">The role to change.</param>
/// <param name="Permissions">
/// The complete intended set, as codes. Not a delta: what arrives is what the
/// role will grant, so omitting a code removes it.
/// </param>
/// <param name="Expected">
/// The version the caller believes it is changing, read from the request's
/// <c>If-Match</c> header.
/// </param>
/// <remarks>
/// <strong>Replace rather than grant and revoke, and the precondition is why.</strong>
/// Individual grant and revoke endpoints would each need their own version, and
/// a person editing a set of checkboxes would be issuing several conditional
/// writes for one decision -- any of which could fail halfway, leaving the role
/// granting something nobody chose. One set, one version, one commit.
/// </remarks>
public sealed record ReplaceTenantRolePermissionsCommand(
    Guid TenantId,
    Guid TenantRoleId,
    IReadOnlyList<string?>? Permissions,
    ConcurrencyToken Expected);
