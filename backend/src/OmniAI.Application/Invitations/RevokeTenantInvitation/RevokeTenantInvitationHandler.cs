using OmniAI.Application.Authorization;
using OmniAI.Application.Authorization.Persistence;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Invitations.Persistence;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Invitations;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Invitations.RevokeTenantInvitation;

/// <summary>Withdraws an invitation.</summary>
/// <param name="TenantId">The workspace.</param>
/// <param name="InvitationId">The invitation.</param>
public sealed record RevokeTenantInvitationCommand(Guid TenantId, Guid InvitationId);

/// <summary>
/// Withdraws an invitation so its link stops working.
/// </summary>
/// <remarks>
/// <strong>The ceiling applies to revocation too, and it is not obvious
/// why.</strong> Revoking takes nothing away from anybody who has it, so it looks
/// harmless. It is not: an invitation carrying authority beyond the actor's own
/// is a decision made by somebody more senior, and letting a junior administrator
/// cancel it would let them quietly veto staffing they have no say over -- and,
/// paired with the ability to issue their own, substitute their choice for it.
/// The rule is the same as everywhere else in this platform: you may act on
/// authority you hold.
/// <para>
/// <strong>Terminal and monotonic, so no entity tag is required.</strong> Unlike
/// a role or a member's access, there is nothing here to overwrite -- a second
/// revocation of an already-withdrawn invitation is refused by the aggregate, and
/// a revocation racing an acceptance is settled by whichever transaction moves it
/// out of pending first. A precondition would add a round trip and protect
/// nothing.
/// </para>
/// </remarks>
public sealed class RevokeTenantInvitationHandler(
    ITenantInvitationRepository invitations,
    ITenantRoleRepository roles,
    TenantAuthorizer authorizer,
    TimeProvider clock)
{
    private readonly ITenantInvitationRepository _invitations =
        invitations ?? throw new ArgumentNullException(nameof(invitations));

    private readonly ITenantRoleRepository _roles =
        roles ?? throw new ArgumentNullException(nameof(roles));

    private readonly TenantAuthorizer _authorizer =
        authorizer ?? throw new ArgumentNullException(nameof(authorizer));

    private readonly TimeProvider _clock =
        clock ?? throw new ArgumentNullException(nameof(clock));

    /// <summary>Revokes it.</summary>
    /// <returns>
    /// <see langword="false"/> when the caller does not belong to this workspace,
    /// or it has no such invitation.
    /// </returns>
    /// <exception cref="Security.NotAuthenticatedException">Nobody is signed in.</exception>
    /// <exception cref="PermissionDeniedException">
    /// The caller lacks <c>members.invite</c>, or the invitation carries authority
    /// beyond their own.
    /// </exception>
    /// <exception cref="Domain.DomainStateException">
    /// It has already been accepted or closed.
    /// </exception>
    public async Task<bool> HandleAsync(
        RevokeTenantInvitationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        TenantId tenantId = TenantId.From(command.TenantId);
        TenantInvitationId invitationId = TenantInvitationId.From(command.InvitationId);

        if (!await _authorizer
            .RequireAsync(tenantId, PlatformPermissions.MembersInvite, cancellationToken)
            .ConfigureAwait(false))
        {
            return false;
        }

        TenantInvitation? invitation = await _invitations
            .FindAsync(tenantId, invitationId, cancellationToken)
            .ConfigureAwait(false);

        if (invitation is null)
        {
            return false;
        }

        IReadOnlyList<PermissionCode> ceiling = await _authorizer
            .EffectivePermissionsAsync(tenantId, cancellationToken)
            .ConfigureAwait(false) ?? [];

        await RequireWithinAsync(tenantId, invitation, ceiling, cancellationToken)
            .ConfigureAwait(false);

        invitation.Revoke(_clock.GetUtcNow());

        await _invitations.UpdateAsync(invitation, cancellationToken).ConfigureAwait(false);

        return true;
    }

    /// <summary>
    /// Refuses when the invitation would grant more than the caller holds.
    /// </summary>
    /// <remarks>
    /// Judged on the roles as they stand now, not as they stood when the
    /// invitation was written -- the same current-state rule acceptance uses, so
    /// an invitation that has grown beyond the caller's authority is out of their
    /// reach in both directions.
    /// <para>
    /// A role that has disappeared contributes nothing rather than blocking:
    /// there is no authority left to protect, and an administrator should still be
    /// able to tidy away an invitation that can no longer be accepted.
    /// </para>
    /// </remarks>
    private async Task RequireWithinAsync(
        TenantId tenantId,
        TenantInvitation invitation,
        IReadOnlyList<PermissionCode> ceiling,
        CancellationToken cancellationToken)
    {
        List<TenantRole> intended = [];

        foreach (TenantRoleId roleId in invitation.RoleIds)
        {
            Versioned<TenantRole>? stored = await _roles
                .FindAsync(tenantId, roleId, cancellationToken)
                .ConfigureAwait(false);

            if (stored is not null)
            {
                intended.Add(stored.Value);
            }
        }

        PermissionCode? beyond =
            EffectivePermissions.FirstBeyond(EffectivePermissions.Union(intended), ceiling);

        if (beyond is not null)
        {
            throw PermissionDeniedException.ForPermission(beyond.Value);
        }
    }
}
