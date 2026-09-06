using OmniAI.Application.Authorization.Persistence;
using OmniAI.Application.Security;
using OmniAI.Application.Tenants;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Application.Authorization;

/// <summary>
/// Decides whether the current caller may perform a given operation in a given
/// workspace.
/// </summary>
/// <remarks>
/// <strong>Every tenant-scoped use case goes through here, and that is the
/// point.</strong> An <c>[Authorize]</c> attribute proves somebody is signed in;
/// an Angular route guard runs in a browser its reader controls. Neither says
/// anything about which workspace this caller may address or what they may do
/// inside it, and the tenant id arrives in the route where the caller put it. So
/// the check lives in the execution path, below HTTP, where no route mapping can
/// forget it and no client can skip it.
/// <para>
/// <strong>It knows nothing about HTTP.</strong> No <c>HttpContext</c>, no
/// <c>ClaimsPrincipal</c>, no ASP.NET Core authorization policy, no requirement
/// handler, no attribute and no status code. It also knows nothing about EF Core,
/// Npgsql or PostgreSQL. Executable guards assert that the Application assembly
/// references no web framework and no data-access library at all, so this cannot
/// quietly acquire either later.
/// </para>
/// <para>
/// <strong>Two layers, asked in order.</strong> Membership first, through the
/// accepted <see cref="TenantAccessGuard"/>; permissions second, from the roles
/// the member holds. The order is the security property: somebody who is not a
/// member never reaches a permission check, so nothing about a workspace's roles
/// or contents is consulted on their behalf, and their answer is the same
/// not-found a member gets for a workspace that was never created.
/// </para>
/// <para>
/// <strong>Union of roles, and nothing else.</strong> A member's authority is the
/// union of what their roles permit. There is no deny, no priority, no
/// inheritance and no implication between codes -- so what somebody can do is
/// readable from the roles they hold, without evaluating a closure. An Owner's
/// roles answer with the whole catalogue, which is why an Owner cannot fall
/// behind a capability added later.
/// </para>
/// <para>
/// <strong>Nothing is cached.</strong> No permission set is held between
/// requests, stored in the session cookie or memoised in a static. A cached
/// decision would keep granting authority after a role changed, and inventing an
/// invalidation protocol before there is any measured need for one is how a
/// platform acquires stale-authorisation bugs it cannot reproduce. The volume is
/// one indexed read per authorised request; correctness first.
/// </para>
/// <para>
/// A plain class rather than an interface, matching
/// <see cref="TenantAccessGuard"/>. Nothing substitutes it, tests exercise it
/// against real fakes of its dependencies, and an interface would make it
/// possible to register a permissive implementation by accident -- which for this
/// type would be a platform-wide authorisation bypass registered in one line.
/// </para>
/// </remarks>
public sealed class TenantAuthorizer(
    TenantAccessGuard access,
    ITenantRoleRepository roles)
{
    private readonly TenantAccessGuard _access =
        access ?? throw new ArgumentNullException(nameof(access));

    private readonly ITenantRoleRepository _roles =
        roles ?? throw new ArgumentNullException(nameof(roles));

    /// <summary>
    /// The signed-in caller, or a refusal.
    /// </summary>
    /// <exception cref="NotAuthenticatedException">Nobody is signed in.</exception>
    /// <remarks>
    /// For the use cases that need the identity itself rather than a permission --
    /// creating a workspace, and listing the caller's own workspaces. Neither is
    /// inside a tenant, so neither has a tenant to be authorised against.
    /// </remarks>
    public UserId RequireCurrentUser() => _access.RequireCurrentUser();

    /// <summary>
    /// Whether the current caller may perform this operation in this workspace.
    /// </summary>
    /// <returns>
    /// <see cref="TenantAccessDecision.NotAMember"/>,
    /// <see cref="TenantAccessDecision.PermissionDenied"/> or
    /// <see cref="TenantAccessDecision.Granted"/>. The first two are never merged;
    /// see <see cref="TenantAccessDecision"/> for why.
    /// </returns>
    /// <exception cref="NotAuthenticatedException">
    /// Nobody is signed in. Thrown rather than returned as a decision, because it
    /// is not a decision about access: the caller has not yet said who they are,
    /// and doing so may change the answer entirely.
    /// </exception>
    public async Task<TenantAccessDecision> AuthorizeAsync(
        TenantId tenantId,
        PermissionCode permission,
        CancellationToken cancellationToken = default)
    {
        UserId userId = _access.RequireCurrentUser();

        if (!await _access
            .IsCurrentUserMemberAsync(tenantId, cancellationToken)
            .ConfigureAwait(false))
        {
            return TenantAccessDecision.NotAMember;
        }

        IReadOnlyList<TenantRole> assigned = await _roles
            .ListAssignedRolesAsync(userId, tenantId, cancellationToken)
            .ConfigureAwait(false);

        // Any, not All: authority is the union of the member's roles, so one role
        // granting the permission is enough. A member with no roles at all falls
        // through to denied, which is correct -- they belong to the workspace and
        // have been given nothing to do in it.
        bool granted = assigned.Any(role => role.EffectivePermissions.Contains(permission));

        return granted ? TenantAccessDecision.Granted : TenantAccessDecision.PermissionDenied;
    }

    /// <summary>
    /// Requires a permission, in the shape the tenant-scoped use cases want.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the operation is permitted;
    /// <see langword="false"/> when the caller is not a member of this workspace,
    /// which each use case answers with its own ordinary not-found.
    /// </returns>
    /// <remarks>
    /// <strong>The two failures leave by different doors on purpose.</strong> Not
    /// being a member is returned, because it is an ordinary outcome that every
    /// caller already handles as absence -- and answering it any other way would
    /// tell a stranger which workspaces exist. Lacking a permission throws,
    /// because it is not absence: the resource is there and the caller is entitled
    /// to know they need something they have not got.
    /// <para>
    /// This is a convenience over <see cref="AuthorizeAsync"/> and not a
    /// replacement for it: the three-valued decision remains available and is what
    /// the authorizer's own tests exercise. Handlers use this one because it maps
    /// exactly onto the idiom the accepted use cases already read as -- check
    /// access, return absence -- with the permission dimension added and nothing
    /// else changed.
    /// </para>
    /// </remarks>
    /// <exception cref="PermissionDeniedException">
    /// The caller belongs to this workspace but no role of theirs grants this
    /// permission.
    /// </exception>
    /// <exception cref="NotAuthenticatedException">Nobody is signed in.</exception>
    public async Task<bool> RequireAsync(
        TenantId tenantId,
        PermissionCode permission,
        CancellationToken cancellationToken = default)
    {
        TenantAccessDecision decision = await AuthorizeAsync(tenantId, permission, cancellationToken)
            .ConfigureAwait(false);

        return decision switch
        {
            TenantAccessDecision.Granted => true,
            TenantAccessDecision.NotAMember => false,
            _ => throw PermissionDeniedException.ForPermission(permission),
        };
    }

    /// <summary>
    /// Requires that every permission in a set is one the current caller already
    /// holds in this workspace.
    /// </summary>
    /// <remarks>
    /// <strong>The delegation ceiling: nobody may hand out authority they do not
    /// have.</strong> <c>roles.manage</c> says somebody may perform role
    /// administration. On its own that is not a licence to mint capabilities --
    /// and without this check it was one, because a role can be assigned to the
    /// person editing it. Adding a permission to a role you hold grants it to you
    /// the moment it is saved, with no assignment row changing anywhere and
    /// nothing to notice. That made <c>roles.manage</c> equivalent to every
    /// permission in the catalogue.
    /// <para>
    /// <strong>Owner needs no special case.</strong> An Owner's effective
    /// permissions are the whole catalogue, so the subset rule admits everything
    /// they could ask for, and it will keep doing so when a capability is added
    /// later. An explicit <c>if (owner) skip</c> would be a second way to be
    /// authorised, sitting beside the first and able to drift from it.
    /// </para>
    /// <para>
    /// <strong>The union, not the role that carried <c>roles.manage</c>.</strong>
    /// Authority is the union of every role the caller holds, so somebody whose
    /// administrative permission and whose editing permission live in different
    /// roles may still delegate both -- they genuinely have both.
    /// </para>
    /// <para>
    /// Codes are checked in the catalogue's published order so that a request
    /// exceeding the ceiling in several ways always names the same one first.
    /// </para>
    /// <para>
    /// This asks the store a second time within one request, after the base
    /// permission check. Two indexed reads, and deliberately not optimised into
    /// one shared snapshot: the alternative is a permission set passed between
    /// steps, which is precisely the sort of cached authority this class refuses
    /// to keep. Correctness first, as everywhere else here.
    /// </para>
    /// </remarks>
    /// <exception cref="PermissionDeniedException">
    /// The caller does not hold one of the permissions, and so may not delegate
    /// it. Names that permission, which is already public.
    /// </exception>
    /// <exception cref="NotAuthenticatedException">Nobody is signed in.</exception>
    public async Task RequireDelegatableAsync(
        TenantId tenantId,
        IEnumerable<PermissionCode> permissions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        IReadOnlyList<PermissionCode>? held = await EffectivePermissionsAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);

        // A non-member cannot reach here: every caller has already established
        // membership through the base permission check, which answers absence
        // before this is asked. An empty ceiling is still the correct reading if
        // it ever did, and refuses everything except the empty set.
        HashSet<PermissionCode> ceiling = [.. held ?? []];

        foreach (PermissionCode permission in
            PlatformPermissions.AllCodes.Where(permissions.ToHashSet().Contains))
        {
            if (!ceiling.Contains(permission))
            {
                throw PermissionDeniedException.ForPermission(permission);
            }
        }
    }

    /// <summary>
    /// Everything the current caller may do in this workspace.
    /// </summary>
    /// <returns>
    /// The union of their roles' permissions, deduplicated and in the platform's
    /// published order, or <see langword="null"/> when they do not belong to this
    /// workspace.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <strong>Only ever about the caller.</strong> There is no parameter naming a
    /// user, so there is no shape of call that asks what somebody else may do.
    /// That is what stops this from becoming a way to enumerate a colleague's
    /// authority.
    /// </para>
    /// <para>
    /// <strong>Membership alone is enough to ask.</strong> No permission is
    /// required to learn one's own permissions -- requiring one would be circular,
    /// and a member with no permissions at all must still be able to be told so,
    /// or the admin application could not explain why it is showing them nothing.
    /// </para>
    /// <para>
    /// Ordered by the catalogue rather than alphabetically, so the answer matches
    /// the order the permission catalogue publishes and two members with the same
    /// authority receive byte-identical responses.
    /// </para>
    /// </remarks>
    /// <exception cref="NotAuthenticatedException">Nobody is signed in.</exception>
    public async Task<IReadOnlyList<PermissionCode>?> EffectivePermissionsAsync(
        TenantId tenantId,
        CancellationToken cancellationToken = default)
    {
        UserId userId = _access.RequireCurrentUser();

        if (!await _access
            .IsCurrentUserMemberAsync(tenantId, cancellationToken)
            .ConfigureAwait(false))
        {
            return null;
        }

        IReadOnlyList<TenantRole> assigned = await _roles
            .ListAssignedRolesAsync(userId, tenantId, cancellationToken)
            .ConfigureAwait(false);

        HashSet<PermissionCode> held = [.. assigned.SelectMany(role => role.EffectivePermissions)];

        return [.. PlatformPermissions.AllCodes.Where(held.Contains)];
    }
}
