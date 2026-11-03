using OmniAI.Application.Authorization;
using OmniAI.Application.Authorization.Persistence;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Members.Persistence;
using OmniAI.Domain;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Application.Members.ReplaceTenantMemberRoles;

/// <summary>Sets exactly which roles a member holds.</summary>
/// <param name="TenantId">The workspace.</param>
/// <param name="UserId">The member being reassigned.</param>
/// <param name="RoleIds">The complete intended set. May be empty.</param>
/// <param name="Expected">The version the caller read.</param>
public sealed record ReplaceTenantMemberRolesCommand(
    Guid TenantId,
    Guid UserId,
    IReadOnlyList<Guid>? RoleIds,
    ConcurrencyToken Expected);

/// <summary>
/// Replaces a member's role assignments.
/// </summary>
/// <remarks>
/// <strong>The operation this whole phase exists to make safe.</strong> Everything
/// else in Team management reads; this one hands out authority, and it is the
/// only place in the platform where one person's action changes what a different
/// person may do.
/// <para>
/// <strong>The ceiling applies to both sides of the change.</strong> Somebody may
/// only give away authority they hold -- that half is obvious. They may also only
/// modify a member whose <em>current</em> authority they hold, which is less
/// obvious and just as necessary: without it, a junior administrator could
/// replace a senior colleague's roles with a set of their own choosing, which is
/// taking their access away rather than granting it. Both unions are recomputed
/// here from stored roles; neither is taken from the request.
/// </para>
/// <para>
/// <strong>The order of the checks below is the contract.</strong> Membership,
/// then permission, then the lookup, then the precondition, then the protected
/// rules, then the two ceilings, then the write. The precondition sits ahead of
/// the authority checks for the reason the role slice records: a caller working
/// from a version somebody else has already replaced is being judged against a
/// role set they were never shown, so the stale answer is the accurate one.
/// </para>
/// </remarks>
public sealed class ReplaceTenantMemberRolesHandler(
    ITenantMemberRepository members,
    ITenantRoleRepository roles,
    TenantAuthorizer authorizer,
    TimeProvider clock)
{
    private readonly ITenantMemberRepository _members =
        members ?? throw new ArgumentNullException(nameof(members));

    private readonly ITenantRoleRepository _roles =
        roles ?? throw new ArgumentNullException(nameof(roles));

    private readonly TenantAuthorizer _authorizer =
        authorizer ?? throw new ArgumentNullException(nameof(authorizer));

    private readonly TimeProvider _clock =
        clock ?? throw new ArgumentNullException(nameof(clock));

    /// <summary>Applies the new set.</summary>
    /// <returns>
    /// The member as they now stand and their new version, or
    /// <see langword="null"/> when the caller does not belong to this workspace or
    /// it has no such member.
    /// </returns>
    /// <exception cref="Security.NotAuthenticatedException">Nobody is signed in.</exception>
    /// <exception cref="PermissionDeniedException">
    /// The caller lacks <c>members.roles.manage</c>, or the change would move
    /// authority beyond what they hold themselves.
    /// </exception>
    /// <exception cref="TenantMemberVersionMismatchException">The version is stale.</exception>
    /// <exception cref="ProtectedMembershipException">
    /// The target is the Owner, or is the caller.
    /// </exception>
    /// <exception cref="DomainValidationException">
    /// A role id names nothing this workspace can assign.
    /// </exception>
    public async Task<Versioned<TenantMemberView>?> HandleAsync(
        ReplaceTenantMemberRolesCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        TenantId tenantId = TenantId.From(command.TenantId);
        UserId targetId = UserId.From(command.UserId);

        UserId actorId = _authorizer.RequireCurrentUser();

        if (!await _authorizer
            .RequireAsync(tenantId, PlatformPermissions.MembersRolesManage, cancellationToken)
            .ConfigureAwait(false))
        {
            return null;
        }

        Versioned<TenantMemberRecord>? stored = await _members
            .FindAsync(tenantId, targetId, cancellationToken)
            .ConfigureAwait(false);

        if (stored is null)
        {
            return null;
        }

        TenantMemberConcurrency.RequireExpectedVersion(stored, command.Expected);

        // Protected rules before the ceilings, because they are absolute: an Owner
        // is not reassigned here by anybody, and nobody edits their own access,
        // whatever authority either party holds. Reporting a ceiling refusal for
        // one of these would suggest the operation becomes possible with more
        // permission, and it does not.
        if (actorId == targetId)
        {
            throw ProtectedMembershipException.Self();
        }

        if (TenantMembers.IsOwner(stored.Value.Roles))
        {
            throw ProtectedMembershipException.Owner();
        }

        IReadOnlyList<PermissionCode> ceiling = await _authorizer
            .EffectivePermissionsAsync(tenantId, cancellationToken)
            .ConfigureAwait(false) ?? [];

        // What the target can do today. Refusing here is what stops a junior
        // administrator from seizing a senior colleague's account by rewriting it.
        RequireWithin(EffectivePermissions.Union(stored.Value.Roles), ceiling);

        IReadOnlyList<TenantRole> desired = await ResolveAsync(
            tenantId,
            command.RoleIds ?? [],
            cancellationToken).ConfigureAwait(false);

        // And what they would be able to do afterwards.
        RequireWithin(EffectivePermissions.Union(desired), ceiling);

        ConcurrencyToken version = await _members
            .ReplaceRolesAsync(
                tenantId,
                targetId,
                [.. desired.Select(role => role.Id)],
                command.Expected,
                _clock.GetUtcNow(),
                cancellationToken)
            .ConfigureAwait(false);

        TenantMemberRecord updated = stored.Value with
        {
            Roles = desired,
            AccessUpdatedAt = _clock.GetUtcNow(),
        };

        return new Versioned<TenantMemberView>(
            TenantMembers.Describe(updated, tenantId.Value),
            version);
    }

    /// <summary>
    /// Turns requested ids into roles this workspace can actually assign.
    /// </summary>
    /// <remarks>
    /// <strong>A role from another workspace is answered exactly like one that
    /// never existed.</strong> The lookup is scoped to this tenant, so a caller
    /// transposing an id they saw elsewhere learns nothing about whether it is
    /// real -- which is what stops the assignment endpoint becoming a way to probe
    /// another workspace's roles.
    /// <para>
    /// Owner is refused by kind rather than by ceiling. An Owner's own ceiling
    /// contains everything Owner grants, so a subset test would let them assign it
    /// -- and ownership is not handed over through team management by anybody.
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyList<TenantRole>> ResolveAsync(
        TenantId tenantId,
        IReadOnlyList<Guid> roleIds,
        CancellationToken cancellationToken)
    {
        List<TenantRole> resolved = [];

        foreach (Guid id in roleIds.Distinct())
        {
            TenantRoleId roleId = TenantRoleId.From(id);

            Versioned<TenantRole>? stored = await _roles
                .FindAsync(tenantId, roleId, cancellationToken)
                .ConfigureAwait(false);

            if (stored is null)
            {
                throw new DomainValidationException(
                    "One of the roles requested is not a role this workspace can assign.")
                {
                    Code = "team.role.unassignable",
                };
            }

            if (stored.Value.IsSystem)
            {
                throw ProtectedMembershipException.Owner();
            }

            resolved.Add(stored.Value);
        }

        return resolved;
    }

    private static void RequireWithin(
        IReadOnlyList<PermissionCode> permissions,
        IReadOnlyList<PermissionCode> ceiling)
    {
        PermissionCode? beyond = EffectivePermissions.FirstBeyond(permissions, ceiling);

        if (beyond is not null)
        {
            throw PermissionDeniedException.ForPermission(beyond.Value);
        }
    }
}
