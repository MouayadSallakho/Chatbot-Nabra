using OmniAI.Application.Authorization;
using OmniAI.Application.Notifications;
using OmniAI.Application.Security;
using OmniAI.Domain.Accounts;
using OmniAI.Application.Authorization.Persistence;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Invitations.Persistence;
using OmniAI.Application.Tenants;
using OmniAI.Domain;
using OmniAI.Application.Plans;
using OmniAI.Domain.Plans;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Invitations;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Application.Invitations.CreateTenantInvitation;

/// <summary>Invites somebody to a workspace.</summary>
/// <param name="TenantId">The workspace.</param>
/// <param name="Email">The address to invite.</param>
/// <param name="RoleIds">The roles they should receive. May be empty.</param>
public sealed record CreateTenantInvitationCommand(
    Guid TenantId,
    string? Email,
    IReadOnlyList<Guid>? RoleIds,
    UserLanguage? Language = null);

/// <summary>
/// An invitation, and the one time its secret is visible.
/// </summary>
/// <param name="Invitation">The invitation as stored.</param>
/// <param name="Token">
/// The raw secret. It exists in this object and in the response built from it,
/// and nowhere else -- not in the database, not in a log, and not in any later
/// read.
/// </param>
/// <param name="Roles">The roles it will grant, for the response.</param>
public sealed record IssuedInvitation(
    TenantInvitation Invitation,
    string Token,
    IReadOnlyList<TenantRole> Roles);

/// <summary>
/// Issues an invitation.
/// </summary>
/// <remarks>
/// <strong>Two permissions, checked separately, because they are two different
/// powers.</strong> <c>members.invite</c> brings a person into the workspace;
/// <c>members.roles.manage</c> gives them the ability to do something once they
/// arrive. Somebody with only the first can still invite a colleague -- they
/// arrive with no capabilities, and an administrator grants access afterwards --
/// which is a genuinely useful thing to allow and keeps people from asking for
/// the broader permission just to add a teammate.
/// <para>
/// <strong>The ceiling applies at issue and again at acceptance.</strong> Checking
/// here stops an inviter composing authority they never held. It cannot stop the
/// world changing afterwards, so acceptance re-checks everything: see the
/// acceptance use case, which is where the guarantee actually lives.
/// </para>
/// </remarks>
public sealed class CreateTenantInvitationHandler(
    IWorkspaceEntitlementService entitlements,
    ITenantInvitationRepository invitations,
    ITenantRoleRepository roles,
    TenantAuthorizer authorizer,
    IInvitationTokens tokens,
    InvitationPolicy policy,
    IAccountDirectory accounts,
    IOperationalEmailSender email,
    TimeProvider clock)
{
    private readonly IWorkspaceEntitlementService _entitlements =
        entitlements ?? throw new ArgumentNullException(nameof(entitlements));

    private readonly ITenantInvitationRepository _invitations =
        invitations ?? throw new ArgumentNullException(nameof(invitations));

    private readonly ITenantRoleRepository _roles =
        roles ?? throw new ArgumentNullException(nameof(roles));

    private readonly TenantAuthorizer _authorizer =
        authorizer ?? throw new ArgumentNullException(nameof(authorizer));

    private readonly IInvitationTokens _tokens =
        tokens ?? throw new ArgumentNullException(nameof(tokens));

    private readonly InvitationPolicy _policy =
        policy ?? throw new ArgumentNullException(nameof(policy));

    private readonly IAccountDirectory _accounts =
        accounts ?? throw new ArgumentNullException(nameof(accounts));

    private readonly IOperationalEmailSender _email =
        email ?? throw new ArgumentNullException(nameof(email));

    private readonly TimeProvider _clock =
        clock ?? throw new ArgumentNullException(nameof(clock));

    /// <summary>Creates it.</summary>
    /// <exception cref="Security.NotAuthenticatedException">Nobody is signed in.</exception>
    /// <exception cref="TenantNotFoundException">
    /// The caller does not belong to this workspace -- whether it exists or not.
    /// </exception>
    /// <exception cref="PermissionDeniedException">
    /// The caller lacks <c>members.invite</c>, lacks <c>members.roles.manage</c>
    /// while naming roles, or named a role beyond their own authority.
    /// </exception>
    /// <exception cref="AlreadyAMemberException">That address is already here.</exception>
    /// <exception cref="InvitationAlreadyOpenException">
    /// An invitation for that address is already open.
    /// </exception>
    /// <exception cref="DomainValidationException">
    /// The address is unusable, or a role id names nothing assignable.
    /// </exception>
    public async Task<IssuedInvitation> HandleAsync(
        CreateTenantInvitationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        TenantId tenantId = TenantId.From(command.TenantId);
        UserId inviterId = _authorizer.RequireCurrentUser();

        // Authorisation before the request is inspected, matching the role slice:
        // validating first would let a member without members.invite learn which
        // addresses are already members, and which role ids are real, by reading
        // the errors.
        if (!await _authorizer
            .RequireAsync(tenantId, PlatformPermissions.MembersInvite, cancellationToken)
            .ConfigureAwait(false))
        {
            throw TenantNotFoundException.ForId(tenantId.Value);
        }

        IReadOnlyList<Guid> requested = command.RoleIds ?? [];

        if (requested.Count > 0)
        {
            // Handing out authority is the separate power, and it is only required
            // when authority is actually being handed out.
            await _authorizer
                .RequireAsync(tenantId, PlatformPermissions.MembersRolesManage, cancellationToken)
                .ConfigureAwait(false);
        }

        EmailAddress email = EmailAddress.Create(command.Email);

        if (await _invitations
            .IsAlreadyMemberAsync(tenantId, email, cancellationToken)
            .ConfigureAwait(false))
        {
            throw new AlreadyAMemberException();
        }

        IReadOnlyList<TenantRole> intended =
            await ResolveAsync(tenantId, requested, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<PermissionCode> ceiling = await _authorizer
            .EffectivePermissionsAsync(tenantId, cancellationToken)
            .ConfigureAwait(false) ?? [];

        PermissionCode? beyond =
            EffectivePermissions.FirstBeyond(EffectivePermissions.Union(intended), ceiling);

        if (beyond is not null)
        {
            throw PermissionDeniedException.ForPermission(beyond.Value);
        }

        // A courtesy, not the guard. Sending an invitation to a workspace that is
        // already full produces something nobody can use, so it is refused here --
        // but the seat is not taken until somebody accepts, and acceptance is
        // where the limit is actually enforced. Several invitations may therefore
        // be outstanding against one free seat, and only the first acceptance
        // gets it.
        //
        // A pending invitation deliberately does not consume a seat. If it did, an
        // administrator could lock their own team out by inviting people who never
        // arrive, and revoking would be the only way back.
        WorkspaceEntitlements plan = await _entitlements
            .DescribeAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);

        WorkspaceEntitlementView? seats = plan.Find(EntitlementKind.Members);

        if (seats is not null && seats.State == EntitlementState.Exhausted)
        {
            throw EntitlementExceededException.For(
                EntitlementKind.Members,
                seats.Used,
                seats.Limit ?? 0);
        }

        DateTimeOffset now = _clock.GetUtcNow();
        NewInvitationToken token = _tokens.Create();

        // Phase 37.3 (row 8a): the language the invitation email is written in.
        // What the inviter asked for, else the inviter's own, else none (English).
        UserLanguage? language = command.Language
            ?? await _accounts.FindLanguageAsync(inviterId, cancellationToken).ConfigureAwait(false);

        TenantInvitation invitation = TenantInvitation.Issue(
            tenantId,
            email,
            inviterId,
            token.Hash,
            intended.Select(role => role.Id),
            now,
            _policy.ExpiresAfter(now),
            language);

        // One invitation email, queued with the invitation itself, so neither
        // exists without the other. Its token is minted when it is sent; the link
        // the inviter copies keeps working beside it. A deployment that cannot
        // send email queues nothing, as for operational email.
        AccountEmail? queued = _email.IsConfigured ? AccountEmail.ForInvitation(invitation, now) : null;

        await _invitations.AddAsync(invitation, now, queued, cancellationToken).ConfigureAwait(false);

        return new IssuedInvitation(invitation, token.Token, intended);
    }

    /// <summary>Turns requested ids into roles this workspace can grant.</summary>
    /// <remarks>
    /// Scoped to the tenant, so a role id borrowed from another workspace is
    /// answered exactly like one that never existed. Owner is refused by kind:
    /// ownership is not something an invitation can confer, by anybody.
    /// </remarks>
    private async Task<IReadOnlyList<TenantRole>> ResolveAsync(
        TenantId tenantId,
        IReadOnlyList<Guid> roleIds,
        CancellationToken cancellationToken)
    {
        List<TenantRole> resolved = [];

        foreach (Guid id in roleIds.Distinct())
        {
            Versioned<TenantRole>? stored = await _roles
                .FindAsync(tenantId, TenantRoleId.From(id), cancellationToken)
                .ConfigureAwait(false);

            if (stored is null)
            {
                throw new DomainValidationException(
                    "One of the roles requested is not a role this workspace can assign.")
                {
                    Code = "invitations.role.unassignable",
                };
            }

            if (stored.Value.IsSystem)
            {
                throw Members.ProtectedMembershipException.Owner();
            }

            resolved.Add(stored.Value);
        }

        return resolved;
    }
}
