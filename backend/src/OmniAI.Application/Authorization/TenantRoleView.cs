using OmniAI.Domain.Authorization;

namespace OmniAI.Application.Authorization;

/// <summary>
/// One role, as the Application publishes it.
/// </summary>
/// <param name="Id">The role's identity.</param>
/// <param name="TenantId">The workspace it belongs to.</param>
/// <param name="Name">Its display name.</param>
/// <param name="Kind">Whether it is the protected system role or a custom one.</param>
/// <param name="IsSystem">
/// Whether it is protected from renaming and permission changes. Derived from
/// <paramref name="Kind"/> and published beside it because a client should not
/// have to know which kinds are protected in order to decide whether to render an
/// editor.
/// </param>
/// <param name="Permissions">
/// What holding this role actually permits, in the platform's published order.
/// For an Owner this is the complete current catalogue.
/// </param>
/// <param name="CreatedAt">When it was created.</param>
/// <param name="UpdatedAt">When its name or permissions last changed.</param>
/// <remarks>
/// <strong>A read model, not the aggregate.</strong> It carries primitives and
/// permission codes rather than <c>TenantRole</c>, so the API layer never
/// serialises a Domain object and cannot accidentally publish a field the Domain
/// adds later. It also carries no version: which stored revision this was read at
/// travels beside it in a <c>Versioned&lt;T&gt;</c>, so the version stays out of
/// the JSON body and lives in the <c>ETag</c> header where it belongs.
/// <para>
/// <strong>Effective permissions, not stored grants.</strong> The distinction
/// matters for exactly one role: an Owner stores none and permits everything.
/// Publishing the rows would show an empty list beside a role that can do
/// anything, which is the most misleading thing this screen could say.
/// </para>
/// <para>
/// Nothing here identifies a member. A role says what somebody holding it may do;
/// who holds it is a different question, and this phase publishes no answer to it.
/// </para>
/// </remarks>
public sealed record TenantRoleView(
    Guid Id,
    Guid TenantId,
    string Name,
    TenantRoleKind Kind,
    bool IsSystem,
    IReadOnlyList<PermissionCode> Permissions,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    /// <summary>Projects a stored role.</summary>
    public static TenantRoleView From(TenantRole role)
    {
        ArgumentNullException.ThrowIfNull(role);

        return new TenantRoleView(
            role.Id.Value,
            role.TenantId.Value,
            role.Name.Value,
            role.Kind,
            role.IsSystem,
            role.EffectivePermissions,
            role.CreatedAt,
            role.UpdatedAt);
    }
}
