using OmniAI.Application.Authorization.Persistence;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Tenants;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Authorization.CreateTenantRole;

/// <summary>
/// Creates a custom role in a workspace.
/// </summary>
/// <remarks>
/// <strong>It cannot create a system role, and no check is what stops it.</strong>
/// The command has no kind field, and <see cref="TenantRole.CreateCustom"/> is the
/// only factory this handler can reach that takes a name -- so a second Owner is
/// not refused here, it is unrepresentable. That matters more than a guard would:
/// a guard can be moved, reordered or made conditional by a later edit, and this
/// cannot.
/// <para>
/// <strong>A creator may only compose authority they already hold.</strong> An
/// earlier version of this comment argued that creating a role grants the creator
/// nothing, because holding a role requires an assignment and this phase exposes
/// no way to make one. That was true of creation and false as a security
/// property: the same administrator can edit a role that is already assigned to
/// them, and a permission added there lands on them immediately. So the rule is
/// not "creation is harmless" but the ceiling in
/// <see cref="TenantAuthorizer.RequireDelegatableAsync"/> -- an actor may
/// delegate only what they themselves may do. Phase 14's member assignment must
/// reuse it rather than reason about creation again.
/// </para>
/// </remarks>
public sealed class CreateTenantRoleHandler(
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

    /// <summary>Creates the role and returns its read model and version.</summary>
    /// <exception cref="Security.NotAuthenticatedException">Nobody is signed in.</exception>
    /// <exception cref="TenantNotFoundException">
    /// The caller does not belong to this workspace -- whether it exists or not.
    /// </exception>
    /// <exception cref="PermissionDeniedException">
    /// The caller is a member without <c>roles.manage</c>.
    /// </exception>
    /// <exception cref="Domain.DomainValidationException">
    /// The tenant id is empty, the name breaks a Domain rule, or a permission code
    /// is malformed or not one this platform recognises.
    /// </exception>
    /// <exception cref="TenantRoleNameAlreadyExistsException">The name is taken.</exception>
    public async Task<Versioned<TenantRoleView>> HandleAsync(
        CreateTenantRoleCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        TenantId tenantId = TenantId.From(command.TenantId);

        // Authorisation before validation, unlike the agent slice, and the
        // difference is deliberate. An agent's name rules are already public --
        // they are in the client that submits the form. The permission catalogue
        // is not: it is published only to callers with roles.view. Validating a
        // permission list first would let a member without roles.manage learn
        // which codes this platform recognises by watching which ones come back
        // as invalid, so nothing about the request is inspected until the caller
        // is entitled to be composing roles at all.
        if (!await _authorizer
            .RequireAsync(tenantId, PlatformPermissions.RolesManage, cancellationToken)
            .ConfigureAwait(false))
        {
            throw TenantNotFoundException.ForId(tenantId.Value);
        }

        TenantRoleName name = TenantRoleName.Create(command.Name);
        IReadOnlyList<PermissionCode> permissions = PermissionCodes.Read(command.Permissions);

        // Recognised first, then delegatable, and the order is the contract. A
        // code this platform does not have is a malformed request; a code it does
        // have that the caller does not hold is a refusal. Asking the ceiling
        // first would report every unknown code as a refusal, which misdescribes
        // the problem and would let somebody map the catalogue by watching which
        // invented codes came back as one answer rather than the other.
        PlatformPermissions.RequireRecognised(permissions);

        await _authorizer
            .RequireDelegatableAsync(tenantId, permissions, cancellationToken)
            .ConfigureAwait(false);

        // The courtesy check. It is not the guarantee -- two concurrent creations
        // both pass it -- but it means the ordinary "that name is taken" answer
        // does not have to travel through a constraint violation. The unique index
        // decides, and AddAsync reports the loser identically.
        if (await _roles
            .NameExistsAsync(tenantId, name.Normalised, cancellationToken)
            .ConfigureAwait(false))
        {
            throw TenantRoleNameAlreadyExistsException.ForName(name.Value);
        }

        // Identity, kind and both timestamps come from the aggregate and the
        // injected clock. The use case supplies only the instant, and does not
        // read the clock itself, so the same code is deterministic under test.
        TenantRole role = TenantRole.CreateCustom(
            tenantId,
            name,
            permissions,
            _clock.GetUtcNow());

        ConcurrencyToken version =
            await _roles.AddAsync(role, cancellationToken).ConfigureAwait(false);

        return new Versioned<TenantRoleView>(TenantRoleView.From(role), version);
    }
}
