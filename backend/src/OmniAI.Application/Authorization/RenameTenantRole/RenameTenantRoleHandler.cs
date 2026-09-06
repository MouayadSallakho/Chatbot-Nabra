using OmniAI.Application.Authorization.Persistence;
using OmniAI.Application.Concurrency;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Authorization.RenameTenantRole;

/// <summary>
/// Changes a custom role's name, if it is still at the version the caller
/// expected.
/// </summary>
public sealed class RenameTenantRoleHandler(
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

    /// <summary>Renames the role.</summary>
    /// <returns>
    /// The updated role and its new version, or <see langword="null"/> when this
    /// workspace has no role with that id, or the caller does not belong to the
    /// workspace.
    /// </returns>
    /// <remarks>
    /// <strong>The order of the four steps below is the contract.</strong>
    /// Authorisation, then the lookup, then the precondition, then the Domain
    /// rule. A stale write against the Owner role therefore reports the stale
    /// version rather than the protection rule -- the caller's precondition
    /// genuinely failed, and answering the protection rule first would describe a
    /// role they have not seen.
    /// </remarks>
    /// <exception cref="Security.NotAuthenticatedException">Nobody is signed in.</exception>
    /// <exception cref="PermissionDeniedException">
    /// The caller is a member without <c>roles.manage</c>.
    /// </exception>
    /// <exception cref="TenantRoleVersionMismatchException">The version is stale.</exception>
    /// <exception cref="Domain.DomainStateException">
    /// The role is the protected Owner role.
    /// </exception>
    /// <exception cref="TenantRoleNameAlreadyExistsException">The name is taken.</exception>
    /// <exception cref="Domain.DomainValidationException">
    /// An identifier is empty, or the name breaks a Domain rule.
    /// </exception>
    public async Task<Versioned<TenantRoleView>?> HandleAsync(
        RenameTenantRoleCommand command,
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

        TenantRoleName name = TenantRoleName.Create(command.Name);

        Versioned<TenantRole>? stored = await _roles
            .FindAsync(tenantId, roleId, cancellationToken)
            .ConfigureAwait(false);

        if (stored is null)
        {
            return null;
        }

        // Before the Domain operation, so a caller who has not seen the current
        // role is told that rather than being told a rule about state they have
        // never looked at.
        TenantRoleConcurrency.RequireExpectedVersion(stored, command.Expected);

        // The same manageable-role rule replacing permissions applies. A rename
        // grants nobody anything, but a role carrying authority the caller does
        // not hold is not theirs to administer at all -- and letting them retitle
        // it is how a higher-authority role gets quietly disguised as something
        // routine.
        await _authorizer
            .RequireDelegatableAsync(
                tenantId,
                stored.Value.EffectivePermissions,
                cancellationToken)
            .ConfigureAwait(false);

        // Refuses the protected Owner role. The rule lives in the aggregate, not
        // here: an endpoint-level check could be forgotten by a second caller,
        // and there is no path to a role that does not go through this method.
        stored.Value.Rename(name, _clock.GetUtcNow());

        ConcurrencyToken version = await _roles
            .UpdateAsync(stored.Value, command.Expected, cancellationToken)
            .ConfigureAwait(false);

        return new Versioned<TenantRoleView>(TenantRoleView.From(stored.Value), version);
    }
}
