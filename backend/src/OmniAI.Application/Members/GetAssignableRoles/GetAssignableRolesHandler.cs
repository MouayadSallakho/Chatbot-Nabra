using OmniAI.Application.Authorization;
using OmniAI.Application.Authorization.Persistence;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Members.GetAssignableRoles;

/// <summary>The roles this caller may actually hand out here.</summary>
/// <param name="TenantId">The workspace.</param>
public sealed record GetAssignableRolesQuery(Guid TenantId);

/// <summary>One role a caller may assign.</summary>
/// <param name="Id">The role's identity.</param>
/// <param name="Name">Its display name.</param>
/// <param name="PermissionCount">How many capabilities it grants.</param>
/// <remarks>
/// Deliberately minimal. The Team screen needs to label a checkbox and say
/// roughly how much authority it carries; it does not need the permission list,
/// which the Roles screen already publishes to anybody entitled to read it.
/// </remarks>
public sealed record AssignableRole(Guid Id, string Name, int PermissionCount);

/// <summary>
/// Which roles the caller may give to somebody.
/// </summary>
/// <remarks>
/// <strong>This is a convenience, not a control.</strong> Every mutation
/// re-evaluates the ceiling against the roles' current permissions, so a role
/// that grows more powerful between this call and a save is refused at the save
/// -- which is exactly the window this endpoint cannot close and must not pretend
/// to. What it buys is a screen that does not offer choices certain to be
/// refused.
/// <para>
/// <strong>Owner is never here.</strong> Not because of the ceiling -- an Owner's
/// own ceiling contains everything Owner grants, so a subset test alone would
/// happily offer it to them -- but because ownership is not assigned through team
/// management at all. It is excluded structurally, by kind, so no arrangement of
/// permissions can cause it to appear.
/// </para>
/// </remarks>
public sealed class GetAssignableRolesHandler(
    ITenantRoleRepository roles,
    TenantAuthorizer authorizer)
{
    private readonly ITenantRoleRepository _roles =
        roles ?? throw new ArgumentNullException(nameof(roles));

    private readonly TenantAuthorizer _authorizer =
        authorizer ?? throw new ArgumentNullException(nameof(authorizer));

    /// <summary>Lists them.</summary>
    /// <returns>
    /// <see langword="null"/> when the caller does not belong to this workspace.
    /// </returns>
    /// <exception cref="Security.NotAuthenticatedException">Nobody is signed in.</exception>
    /// <exception cref="PermissionDeniedException">
    /// The caller is a member without <c>members.roles.manage</c>.
    /// </exception>
    public async Task<IReadOnlyList<AssignableRole>?> HandleAsync(
        GetAssignableRolesQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        TenantId tenantId = TenantId.From(query.TenantId);

        if (!await _authorizer
            .RequireAsync(tenantId, PlatformPermissions.MembersRolesManage, cancellationToken)
            .ConfigureAwait(false))
        {
            return null;
        }

        IReadOnlyList<PermissionCode> ceiling = await _authorizer
            .EffectivePermissionsAsync(tenantId, cancellationToken)
            .ConfigureAwait(false) ?? [];

        IReadOnlyList<TenantRoleSummary> all = await _roles
            .ListAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);

        // Summaries carry a count, not a permission set, so the ceiling has to be
        // tested against the role itself. Read individually rather than adding a
        // bulk query for a list that is a handful of rows in any real workspace --
        // and if that stops being true, the fix is a repository method, not a
        // weaker check here.
        List<AssignableRole> assignable = [];

        foreach (TenantRoleSummary summary in all.Where(role => role.Kind == TenantRoleKind.Custom))
        {
            Concurrency.Versioned<TenantRole>? stored = await _roles
                .FindAsync(tenantId, TenantRoleId.From(summary.Id), cancellationToken)
                .ConfigureAwait(false);

            if (stored is not null
                && EffectivePermissions.Within(stored.Value.EffectivePermissions, ceiling))
            {
                assignable.Add(new AssignableRole(
                    summary.Id,
                    summary.Name,
                    stored.Value.EffectivePermissions.Count));
            }
        }

        return assignable;
    }
}
