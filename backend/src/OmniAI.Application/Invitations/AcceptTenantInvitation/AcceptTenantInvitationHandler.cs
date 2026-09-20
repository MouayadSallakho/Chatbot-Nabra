using OmniAI.Application.Authorization;
using OmniAI.Application.Authorization.Persistence;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Invitations.Persistence;
using OmniAI.Application.Security;
using OmniAI.Application.Tenants.Persistence;
using OmniAI.Application.Plans;
using OmniAI.Domain.Plans;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Invitations;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Application.Invitations.AcceptTenantInvitation;

/// <summary>Accepts an invitation.</summary>
/// <param name="Token">The secret from the invitation link.</param>
/// <remarks>
/// <strong>One field, and that is the security design.</strong> The token says
/// which invitation; the session says who is accepting. There is no tenant id, no
/// user id, no email and no role list, because every one of those would be a
/// value the caller chose about a decision that is not theirs to make.
/// </remarks>
public sealed record AcceptTenantInvitationCommand(string? Token);

/// <summary>What somebody joined.</summary>
/// <param name="TenantId">The workspace.</param>
/// <param name="TenantName">Its name.</param>
/// <param name="TenantSlug">Its slug.</param>
public sealed record AcceptedInvitation(Guid TenantId, string TenantName, string TenantSlug);

/// <summary>
/// Turns an invitation into a membership.
/// </summary>
/// <remarks>
/// <strong>An invitation is a proposal, not a promise, and this is where that
/// matters.</strong> Everything about it was checked when it was written, and
/// none of those answers is still trustworthy: days may have passed, the inviter
/// may have left, their authority may have been reduced, and the roles the
/// invitation names may have been given capabilities the inviter could never have
/// delegated. Granting the invitation as written would let anybody who could once
/// invite arrange for authority to arrive later -- a delayed escalation with no
/// visible actor at the moment it lands.
/// <para>
/// So the entire question is asked again, now, against current state. If any part
/// of it no longer holds, no membership is created. Nothing is grandfathered.
/// </para>
/// <para>
/// <strong>The invitee is told very little.</strong> A refusal caused by the
/// inviter's authority is not the invitee's situation and not theirs to know, so
/// it reads the same as any other unusable invitation. What they are told is what
/// to do: ask for a new one.
/// </para>
/// </remarks>
public sealed class AcceptTenantInvitationHandler(
    IWorkspaceEntitlementService entitlements,
    ITenantInvitationRepository invitations,
    ITenantRoleRepository roles,
    ITenantMembershipRepository memberships,
    ITenantRepository tenants,
    IAccountDirectory accounts,
    IInvitationTokens tokens,
    ICurrentUser currentUser,
    TimeProvider clock)
{
    private readonly ITenantInvitationRepository _invitations =
        invitations ?? throw new ArgumentNullException(nameof(invitations));

    private readonly ITenantRoleRepository _roles =
        roles ?? throw new ArgumentNullException(nameof(roles));

    private readonly IWorkspaceEntitlementService _entitlements =
        entitlements ?? throw new ArgumentNullException(nameof(entitlements));

    private readonly ITenantMembershipRepository _memberships =
        memberships ?? throw new ArgumentNullException(nameof(memberships));

    private readonly ITenantRepository _tenants =
        tenants ?? throw new ArgumentNullException(nameof(tenants));

    private readonly IAccountDirectory _accounts =
        accounts ?? throw new ArgumentNullException(nameof(accounts));

    private readonly IInvitationTokens _tokens =
        tokens ?? throw new ArgumentNullException(nameof(tokens));

    private readonly ICurrentUser _currentUser =
        currentUser ?? throw new ArgumentNullException(nameof(currentUser));

    private readonly TimeProvider _clock =
        clock ?? throw new ArgumentNullException(nameof(clock));

    /// <summary>Accepts it.</summary>
    /// <returns>
    /// The workspace that was joined, or <see langword="null"/> when no invitation
    /// matches the token at all -- a wrong, mistyped or invented value, answered
    /// the same way whether or not it was ever real.
    /// </returns>
    /// <exception cref="NotAuthenticatedException">Nobody is signed in.</exception>
    /// <exception cref="InvitationNoLongerUsableException">
    /// It has been used, withdrawn, has expired, or the authority that issued it
    /// no longer stands.
    /// </exception>
    /// <exception cref="InvitationNotForThisAccountException">
    /// The signed-in account is not the invited address.
    /// </exception>
    public async Task<AcceptedInvitation?> HandleAsync(
        AcceptTenantInvitationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        UserId acceptorId = _currentUser.Id ?? throw new NotAuthenticatedException();

        // A value that could not be a token this platform issued never reaches the
        // database.
        InvitationTokenHash? hash = _tokens.Recognise(command.Token);

        if (hash is null)
        {
            return null;
        }

        TenantInvitation? invitation = await _invitations
            .FindByTokenHashAsync(hash, cancellationToken)
            .ConfigureAwait(false);

        if (invitation is null)
        {
            return null;
        }

        DateTimeOffset now = _clock.GetUtcNow();

        RequireUsable(invitation, now);

        // Who the account actually is, read from the account store rather than
        // from a claim or the request, so an address changed since sign-in is
        // judged as it stands now.
        EmailAddress? acceptorEmail = await _accounts
            .FindEmailAsync(acceptorId, cancellationToken)
            .ConfigureAwait(false);

        if (acceptorEmail is null || !invitation.Email.Matches(acceptorEmail.Value))
        {
            throw new InvitationNotForThisAccountException();
        }

        await RequireInviterStillEntitledAsync(invitation, cancellationToken).ConfigureAwait(false);

        Tenant? tenant = await _tenants
            .FindByIdAsync(invitation.TenantId, cancellationToken)
            .ConfigureAwait(false);

        if (tenant is null)
        {
            throw InvitationNoLongerUsableException.NoLongerValid();
        }

        TenantMembership membership =
            TenantMembership.Create(acceptorId, invitation.TenantId, now);

        // Acceptance is where a seat is actually taken, so it is where the limit
        // is actually enforced. Two people holding invitations against one
        // remaining seat both reach this line; the workspace's seat lock lets one
        // through and refuses the other, and the refused one creates no
        // membership.
        //
        // The reservation encloses the whole acceptance. One transaction: the
        // seat count, the membership, its roles and the invitation's ending are a
        // single fact. A second acceptance of the *same* invitation, or a
        // revocation racing this one, finds the invitation already concluded and
        // loses on that instead.
        bool accepted = await _entitlements.ReserveAsync(
            invitation.TenantId,
            EntitlementKind.Members,
            async token => await _invitations
                .AcceptAsync(
                    invitation,
                    membership,
                    invitation.RoleIds,
                    acceptorId,
                    now,
                    token)
                .ConfigureAwait(false),
            cancellationToken)
            .ConfigureAwait(false);

        if (!accepted)
        {
            throw InvitationNoLongerUsableException.AlreadyAccepted();
        }

        return new AcceptedInvitation(tenant.Id.Value, tenant.Name.Value, tenant.Slug.Value);
    }

    /// <summary>Refuses an invitation that has already concluded or aged out.</summary>
    private static void RequireUsable(TenantInvitation invitation, DateTimeOffset now)
    {
        if (invitation.IsUsableAt(now))
        {
            return;
        }

        throw invitation.Status switch
        {
            TenantInvitationStatus.Accepted => InvitationNoLongerUsableException.AlreadyAccepted(),
            TenantInvitationStatus.Revoked => InvitationNoLongerUsableException.Revoked(),
            _ => InvitationNoLongerUsableException.Expired(),
        };
    }

    /// <summary>
    /// Re-asks, now, every authority question the invitation depended on.
    /// </summary>
    /// <remarks>
    /// <strong>Four things, and all of them can have changed.</strong> The inviter
    /// may have left the workspace; they may have lost the permission to invite;
    /// if the invitation carries roles they may have lost the permission to hand
    /// roles out; and the roles themselves may have been given capabilities the
    /// inviter never had. The last is the subtle one and the reason this exists:
    /// an administrator with a narrow ceiling can invite somebody to a modest
    /// role, and if that role is later widened by an Owner, accepting would
    /// deliver authority the inviter could not have granted on the day they
    /// asked.
    /// <para>
    /// Roles are re-read rather than trusted, so a role deleted or replaced is
    /// caught too. Owner is refused by kind, in case a role became the system role
    /// by some future path that does not exist today.
    /// </para>
    /// </remarks>
    private async Task RequireInviterStillEntitledAsync(
        TenantInvitation invitation,
        CancellationToken cancellationToken)
    {
        bool stillAMember = await _memberships
            .ExistsAsync(invitation.InvitedByUserId, invitation.TenantId, cancellationToken)
            .ConfigureAwait(false);

        if (!stillAMember)
        {
            throw InvitationNoLongerUsableException.NoLongerValid();
        }

        IReadOnlyList<TenantRole> inviterRoles = await _roles
            .ListAssignedRolesAsync(invitation.InvitedByUserId, invitation.TenantId, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<PermissionCode> ceiling = EffectivePermissions.Union(inviterRoles);

        if (!ceiling.Contains(PlatformPermissions.MembersInvite))
        {
            throw InvitationNoLongerUsableException.NoLongerValid();
        }

        IReadOnlyList<TenantRoleId> roleIds = invitation.RoleIds;

        if (roleIds.Count == 0)
        {
            return;
        }

        if (!ceiling.Contains(PlatformPermissions.MembersRolesManage))
        {
            throw InvitationNoLongerUsableException.NoLongerValid();
        }

        List<TenantRole> intended = [];

        foreach (TenantRoleId roleId in roleIds)
        {
            Versioned<TenantRole>? stored = await _roles
                .FindAsync(invitation.TenantId, roleId, cancellationToken)
                .ConfigureAwait(false);

            if (stored is null || stored.Value.IsSystem)
            {
                throw InvitationNoLongerUsableException.NoLongerValid();
            }

            intended.Add(stored.Value);
        }

        if (!EffectivePermissions.Within(EffectivePermissions.Union(intended), ceiling))
        {
            throw InvitationNoLongerUsableException.NoLongerValid();
        }
    }
}
