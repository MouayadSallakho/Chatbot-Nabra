using OmniAI.Application.Authorization.Persistence;
using OmniAI.Application.Concurrency;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Authorization.ReplaceTenantRolePermissions;

/// <summary>
/// Replaces a custom role's entire permission set, if it is still at the version
/// the caller expected.
/// </summary>
/// <remarks>
/// <strong>Whole set, one version, one commit.</strong> The editor shows a group
/// of checkboxes and saves them together, so what arrives is the complete intended
/// state rather than a delta. Merging would make removing a permission impossible
/// through this operation, and would turn two administrators editing at once into
/// a silent union of their intentions -- an accumulation of authority nobody
/// chose. The precondition is what makes the second case a reported conflict
/// instead.
/// </remarks>
public sealed class ReplaceTenantRolePermissionsHandler(
    ITenantRoleRepository roles,
    TenantAuthorizer authorizer,
    TimeProvider clock)
{
    private readonly ITenantRoleRepository _roles =
        roles ?? throw new ArgumentNullException(nameof(roles));

    private readonly TenantAuthorizer _authorizer =
        authorizer ?? throw new ArgumentNullException(nameof(authorizer));

    private readonly TimeProvider _clock =
        clock ?? throw new ArgumentNullException(nameof(clock));

    /// <summary>Replaces the permission set.</summary>
    /// <returns>
    /// The updated role and its new version, or <see langword="null"/> when this
    /// workspace has no role with that id, or the caller does not belong to the
    /// workspace.
    /// </returns>
    /// <remarks>
    /// <strong>An unrecognised code changes nothing.</strong> The whole set is
    /// validated by the aggregate before a single grant is removed, so a request
    /// naming one capability this platform does not have leaves the role exactly
    /// as it was -- rather than emptying it and then failing.
    /// </remarks>
    /// <exception cref="Security.NotAuthenticatedException">Nobody is signed in.</exception>
    /// <exception cref="PermissionDeniedException">
    /// The caller is a member without <c>roles.manage</c>.
    /// </exception>
    /// <exception cref="TenantRoleVersionMismatchException">The version is stale.</exception>
    /// <exception cref="Domain.DomainStateException">
    /// The role is the protected Owner role.
    /// </exception>
    /// <exception cref="Domain.DomainValidationException">
    /// An identifier is empty, or a permission code is malformed or not one this
    /// platform recognises.
    /// </exception>
    public async Task<Versioned<TenantRoleView>?> HandleAsync(
        ReplaceTenantRolePermissionsCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        TenantId tenantId = TenantId.From(command.TenantId);
        TenantRoleId roleId = TenantRoleId.From(command.TenantRoleId);

        if (!await _authorizer
            .RequireAsync(tenantId, PlatformPermissions.RolesManage, cancellationToken)
            .ConfigureAwait(false))
        {
            return null;
        }

        IReadOnlyList<PermissionCode> permissions = PermissionCodes.Read(command.Permissions);

        Versioned<TenantRole>? stored = await _roles
            .FindAsync(tenantId, roleId, cancellationToken)
            .ConfigureAwait(false);

        if (stored is null)
        {
            return null;
        }

        TenantRoleConcurrency.RequireExpectedVersion(stored, command.Expected);

        // A code this platform does not have is a malformed request, and must be
        // reported as one rather than as a refusal.
        PlatformPermissions.RequireRecognised(permissions);

        // The ceiling, on both sides. The set being asked for, because nobody may
        // grant authority they do not hold; and the set the role already carries,
        // because a role that outranks the caller is not theirs to take over --
        // being able to rewrite it would be a way to seize it. Both after the
        // precondition, so a caller working from a version somebody else has
        // already replaced is told that first: it is the more accurate answer,
        // and the current permissions they are being judged against are not the
        // ones they were looking at.
        await _authorizer
            .RequireDelegatableAsync(
                tenantId,
                [.. stored.Value.EffectivePermissions, .. permissions],
                cancellationToken)
            .ConfigureAwait(false);

        // Refuses the protected Owner role, and validates the whole set against
        // the catalogue before touching a single grant.
        stored.Value.ReplacePermissions(permissions, _clock.GetUtcNow());

        ConcurrencyToken version = await _roles
            .UpdateAsync(stored.Value, command.Expected, cancellationToken)
            .ConfigureAwait(false);

        return new Versioned<TenantRoleView>(TenantRoleView.From(stored.Value), version);
    }
}
