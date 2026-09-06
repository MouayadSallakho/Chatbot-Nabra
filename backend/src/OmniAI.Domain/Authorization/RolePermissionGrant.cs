namespace OmniAI.Domain.Authorization;

/// <summary>
/// The fact that a role grants one permission.
/// </summary>
/// <remarks>
/// <strong>A row, and a business statement.</strong> "The Support agent role
/// grants ai-agents.view" is a thing a workspace decided, not a persistence
/// artefact, so it is modelled rather than hidden -- and modelling it is what
/// lets the database hold the rule that a role cannot grant the same permission
/// twice, as the primary key <c>(tenant_role_id, permission)</c>.
/// <para>
/// <strong>Why not a collection of codes on the role.</strong> A list of strings
/// stored in one column -- a PostgreSQL array or a JSON document -- would be
/// smaller, and would make duplicate grants and unknown codes something only
/// application code could prevent. A relational child table lets the database
/// state both: the composite key refuses a duplicate outright, and the row is
/// individually addressable if a future phase ever needs to record who granted
/// what and when.
/// </para>
/// <para>
/// <strong>It is not part of the public shape of a role.</strong> Nothing outside
/// the aggregate constructs one, and no use case takes one as an argument;
/// <see cref="TenantRole.EffectivePermissions"/> publishes codes, which is the
/// security contract. This type exists so the aggregate has something to own.
/// </para>
/// <para>
/// Owner roles have none of these rows at all -- see
/// <see cref="TenantRole"/> for why storing them would be the fragile design.
/// </para>
/// </remarks>
public sealed class RolePermissionGrant
{
    private RolePermissionGrant(TenantRoleId tenantRoleId, PermissionCode permission)
    {
        TenantRoleId = tenantRoleId;
        Permission = permission;
    }

    /// <summary>The role that grants it.</summary>
    public TenantRoleId TenantRoleId { get; }

    /// <summary>The capability granted.</summary>
    public PermissionCode Permission { get; }

    /// <summary>
    /// Records a grant.
    /// </summary>
    /// <remarks>
    /// Internal, because the aggregate is the only thing allowed to decide what a
    /// role grants. A public factory would let a caller assemble grants and hand
    /// them to a repository, bypassing the catalogue check that
    /// <see cref="TenantRole"/> performs -- which is precisely the path by which
    /// an invented permission code would become a stored row.
    /// </remarks>
    internal static RolePermissionGrant For(TenantRoleId tenantRoleId, PermissionCode permission) =>
        new(tenantRoleId, permission);
}
