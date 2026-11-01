using OmniAI.Application.Authorization;
using OmniAI.Application.Invitations;
using OmniAI.Application.Invitations.AcceptTenantInvitation;
using OmniAI.Application.Invitations.CreateTenantInvitation;
using OmniAI.Application.Invitations.RevokeTenantInvitation;
using OmniAI.Application.Tests.Authorization;
using OmniAI.Application.Tests.Security;
using OmniAI.Application.Tenants;
using OmniAI.Application.Tests.Tenants;
using OmniAI.Domain;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Invitations;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;
using OmniAI.Testing;

namespace OmniAI.Application.Tests.Team;

/// <summary>
/// Issuing, withdrawing and accepting invitations.
/// </summary>
/// <remarks>
/// <strong>The whole point of this suite is the gap between issue and
/// acceptance.</strong> Everything about an invitation is checked when it is
/// written, and none of those answers is still trustworthy days later: the
/// inviter may have left, their authority may have been reduced, and the roles
/// the invitation names may have grown beyond what they could ever have
/// delegated. Granting it as written would be a delayed escalation with no
/// visible actor at the moment it lands.
/// </remarks>
public sealed class InvitationSecurityTests
{
    // The workspace is built first and its identifiers derived from it, so the
    // tenant the acceptance handler looks up is the same one these tests name.
    private static readonly Domain.Tenants.Tenant WorkspaceTenant =
        Domain.Tenants.Tenant.Create(
            TenantName.Create("Acme Trading"),
            TenantSlug.Create("acme"));

    private static readonly TenantId Tenant = WorkspaceTenant.Id;
    private static readonly Guid Workspace = Tenant.Value;
    private static readonly UserId Inviter = UserId.New();
    private static readonly UserId Invitee = UserId.New();
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 9, 0, 0, TimeSpan.Zero);

    private static readonly TenantView WorkspaceView =
        new(Workspace, "Acme Trading", "acme", TenantStatus.Active);

    // ------------------------------------------------------------- issuing

    [Fact]
    public async Task An_inviter_may_offer_a_role_they_hold()
    {
        TenantRole support = Custom("Support", PlatformPermissions.AiAgentsView);

        Fixture world = new Fixture().WithRole(support);

        IssuedInvitation issued = await world
            .Creator(PlatformPermissions.MembersInvite, PlatformPermissions.MembersRolesManage, PlatformPermissions.AiAgentsView)
            .HandleAsync(new CreateTenantInvitationCommand(
                Workspace,
                "bob@example.test",
                [support.Id.Value]));

        Assert.Equal("bob@example.test", issued.Invitation.Email.Value);
        Assert.Equal([support.Id], issued.Invitation.RoleIds);
        Assert.NotEmpty(issued.Token);
    }

    [Fact]
    public async Task Inviting_with_no_roles_needs_only_the_invite_permission()
    {
        // Somebody who may invite but not hand out authority can still bring a
        // colleague in. Refusing this would push people to ask for the broader
        // permission just to add a teammate.
        Fixture world = new();

        IssuedInvitation issued = await world
            .Creator(PlatformPermissions.MembersInvite)
            .HandleAsync(new CreateTenantInvitationCommand(Workspace, "bob@example.test", []));

        Assert.Empty(issued.Invitation.RoleIds);
    }

    [Fact]
    public async Task Inviting_with_roles_needs_the_assign_permission_as_well()
    {
        TenantRole support = Custom("Support", PlatformPermissions.AiAgentsView);

        Fixture world = new Fixture().WithRole(support);

        await Assert.ThrowsAsync<PermissionDeniedException>(() =>
            world
                .Creator(PlatformPermissions.MembersInvite, PlatformPermissions.AiAgentsView)
                .HandleAsync(new CreateTenantInvitationCommand(
                    Workspace,
                    "bob@example.test",
                    [support.Id.Value])));
    }

    [Fact]
    public async Task An_inviter_cannot_offer_authority_they_lack()
    {
        TenantRole senior = Custom("Senior", PlatformPermissions.AiAgentsEdit);

        Fixture world = new Fixture().WithRole(senior);

        PermissionDeniedException denied =
            await Assert.ThrowsAsync<PermissionDeniedException>(() =>
                world
                    .Creator(PlatformPermissions.MembersInvite, PlatformPermissions.MembersRolesManage)
                    .HandleAsync(new CreateTenantInvitationCommand(
                        Workspace,
                        "bob@example.test",
                        [senior.Id.Value])));

        Assert.Equal(PlatformPermissions.AiAgentsEdit.Value, denied.RequiredPermission);
        Assert.Empty(world.Invitations.All);
    }

    [Fact]
    public async Task The_owner_role_can_never_be_invited_with()
    {
        // Even by an Owner. Ownership transfer is a separate, deferred concern.
        TenantRole owner = TenantRole.CreateOwner(Tenant, Now);

        Fixture world = new Fixture().WithRole(owner);

        await Assert.ThrowsAsync<Members.ProtectedMembershipException>(() =>
            world
                .Creator([.. PlatformPermissions.AllCodes])
                .HandleAsync(new CreateTenantInvitationCommand(
                    Workspace,
                    "bob@example.test",
                    [owner.Id.Value])));
    }

    [Fact]
    public async Task Inviting_somebody_already_on_the_team_is_refused()
    {
        Fixture world = new();

        world.Invitations.WithMember(Tenant, "BOB@EXAMPLE.TEST");

        await Assert.ThrowsAsync<AlreadyAMemberException>(() =>
            world
                .Creator(PlatformPermissions.MembersInvite)
                .HandleAsync(new CreateTenantInvitationCommand(
                    Workspace,
                    "bob@example.test",
                    [])));
    }

    [Fact]
    public async Task A_second_open_invitation_for_one_address_is_refused()
    {
        Fixture world = new();

        await world
            .Creator(PlatformPermissions.MembersInvite)
            .HandleAsync(new CreateTenantInvitationCommand(Workspace, "bob@example.test", []));

        await Assert.ThrowsAsync<InvitationAlreadyOpenException>(() =>
            world
                .Creator(PlatformPermissions.MembersInvite)
                .HandleAsync(new CreateTenantInvitationCommand(
                    Workspace,
                    "BOB@example.test",
                    [])));
    }

    [Fact]
    public async Task An_expired_invitation_does_not_block_a_replacement()
    {
        // Expiry is recorded lazily, so issuing the replacement is the moment the
        // old one is closed. Without that the open-invitation rule would lock the
        // address out permanently.
        Fixture world = new();

        await world
            .Creator(PlatformPermissions.MembersInvite)
            .HandleAsync(new CreateTenantInvitationCommand(Workspace, "bob@example.test", []));

        world.Clock.Now = Now.AddDays(30);

        IssuedInvitation replacement = await world
            .Creator(PlatformPermissions.MembersInvite)
            .HandleAsync(new CreateTenantInvitationCommand(Workspace, "bob@example.test", []));

        Assert.Equal(2, world.Invitations.All.Count);
        Assert.Equal(TenantInvitationStatus.Expired, world.Invitations.All[0].Status);
        Assert.True(replacement.Invitation.IsUsableAt(world.Clock.Now));
    }

    [Fact]
    public async Task A_non_member_is_told_the_workspace_does_not_exist()
    {
        Fixture world = new();

        await Assert.ThrowsAsync<TenantNotFoundException>(() =>
            new CreateTenantInvitationHandler(
            new FakeEntitlements(),
                world.Invitations,
                world.Roles,
                TestAuthority.Outsider(Inviter),
                world.Tokens,
                new InvitationPolicy(),
                world.Accounts,
                world.Email,
                world.Clock)
            .HandleAsync(new CreateTenantInvitationCommand(Workspace, "bob@example.test", [])));
    }

    // ---------------------------------------------------------- acceptance

    [Fact]
    public async Task The_invited_account_joins_with_the_roles_intended()
    {
        TenantRole support = Custom("Support", PlatformPermissions.AiAgentsView);

        Fixture world = new Fixture().WithRole(support).WithInviterHolding(
            PlatformPermissions.MembersInvite,
            PlatformPermissions.MembersRolesManage,
            PlatformPermissions.AiAgentsView);

        string token = await world.IssueAsync(support);

        AcceptedInvitation? accepted = await world.Acceptor(Invitee, "bob@example.test")
            .HandleAsync(new AcceptTenantInvitationCommand(token));

        Assert.NotNull(accepted);
        Assert.Equal(Workspace, accepted.TenantId);

        (TenantMembership membership, IReadOnlyList<TenantRoleId> roles) =
            Assert.Single(world.Invitations.Accepted);

        Assert.Equal(Invitee, membership.UserId);
        Assert.Equal([support.Id], roles);
    }

    [Fact]
    public async Task A_different_account_cannot_accept_it()
    {
        // The second half of what makes an invitation safe. The token proves
        // somebody was given the link; this proves they are who it was written
        // to. Without it a forwarded link would be enough for anybody.
        Fixture world = new Fixture().WithInviterHolding(PlatformPermissions.MembersInvite);

        string token = await world.IssueAsync();

        await Assert.ThrowsAsync<InvitationNotForThisAccountException>(() =>
            world.Acceptor(UserId.New(), "mallory@example.test")
                .HandleAsync(new AcceptTenantInvitationCommand(token)));

        Assert.Empty(world.Invitations.Accepted);
    }

    [Fact]
    public async Task The_address_is_matched_canonically_rather_than_literally()
    {
        // Somebody whose account is Bob@Example.Test must be able to accept an
        // invitation written to bob@example.test.
        Fixture world = new Fixture().WithInviterHolding(PlatformPermissions.MembersInvite);

        string token = await world.IssueAsync();

        Assert.NotNull(
            await world.Acceptor(Invitee, "BOB@Example.TEST")
                .HandleAsync(new AcceptTenantInvitationCommand(token)));
    }

    [Fact]
    public async Task A_token_that_matches_nothing_is_answered_as_absence()
    {
        Fixture world = new();

        Assert.Null(
            await world.Acceptor(Invitee, "bob@example.test")
                .HandleAsync(new AcceptTenantInvitationCommand("no-such-token")));
    }

    [Fact]
    public async Task A_token_cannot_be_used_twice()
    {
        Fixture world = new Fixture().WithInviterHolding(PlatformPermissions.MembersInvite);

        string token = await world.IssueAsync();

        Assert.NotNull(
            await world.Acceptor(Invitee, "bob@example.test")
                .HandleAsync(new AcceptTenantInvitationCommand(token)));

        await Assert.ThrowsAsync<InvitationNoLongerUsableException>(() =>
            world.Acceptor(UserId.New(), "bob@example.test")
                .HandleAsync(new AcceptTenantInvitationCommand(token)));

        Assert.Single(world.Invitations.Accepted);
    }

    [Fact]
    public async Task A_revoked_invitation_cannot_be_accepted()
    {
        Fixture world = new Fixture().WithInviterHolding(PlatformPermissions.MembersInvite);

        string token = await world.IssueAsync();

        await world.Revoker(PlatformPermissions.MembersInvite)
            .HandleAsync(new RevokeTenantInvitationCommand(
                Workspace,
                world.Invitations.All[0].Id.Value));

        await Assert.ThrowsAsync<InvitationNoLongerUsableException>(() =>
            world.Acceptor(Invitee, "bob@example.test")
                .HandleAsync(new AcceptTenantInvitationCommand(token)));

        Assert.Empty(world.Invitations.Accepted);
    }

    [Fact]
    public async Task An_expired_invitation_cannot_be_accepted()
    {
        Fixture world = new Fixture().WithInviterHolding(PlatformPermissions.MembersInvite);

        string token = await world.IssueAsync();

        world.Clock.Now = Now.AddDays(30);

        await Assert.ThrowsAsync<InvitationNoLongerUsableException>(() =>
            world.Acceptor(Invitee, "bob@example.test")
                .HandleAsync(new AcceptTenantInvitationCommand(token)));
    }

    // ------------------------------------- the authority may change meanwhile

    [Fact]
    public async Task An_invitation_does_not_outlive_the_inviter_s_membership()
    {
        Fixture world = new Fixture().WithInviterHolding(PlatformPermissions.MembersInvite);

        string token = await world.IssueAsync();

        world.InviterLeaves();

        await Assert.ThrowsAsync<InvitationNoLongerUsableException>(() =>
            world.Acceptor(Invitee, "bob@example.test")
                .HandleAsync(new AcceptTenantInvitationCommand(token)));

        Assert.Empty(world.Invitations.Accepted);
    }

    [Fact]
    public async Task An_invitation_does_not_outlive_the_permission_that_issued_it()
    {
        Fixture world = new Fixture().WithInviterHolding(PlatformPermissions.MembersInvite);

        string token = await world.IssueAsync();

        world.InviterNowHolds(PlatformPermissions.MembersView);

        await Assert.ThrowsAsync<InvitationNoLongerUsableException>(() =>
            world.Acceptor(Invitee, "bob@example.test")
                .HandleAsync(new AcceptTenantInvitationCommand(token)));
    }

    [Fact]
    public async Task An_invitation_carrying_roles_needs_the_assign_permission_at_acceptance_too()
    {
        TenantRole support = Custom("Support", PlatformPermissions.AiAgentsView);

        Fixture world = new Fixture().WithRole(support).WithInviterHolding(
            PlatformPermissions.MembersInvite,
            PlatformPermissions.MembersRolesManage,
            PlatformPermissions.AiAgentsView);

        string token = await world.IssueAsync(support);

        world.InviterNowHolds(PlatformPermissions.MembersInvite, PlatformPermissions.AiAgentsView);

        await Assert.ThrowsAsync<InvitationNoLongerUsableException>(() =>
            world.Acceptor(Invitee, "bob@example.test")
                .HandleAsync(new AcceptTenantInvitationCommand(token)));
    }

    [Fact]
    public async Task A_role_that_grew_beyond_the_inviter_s_ceiling_cannot_be_delivered()
    {
        // The mandatory scenario, and the subtle one. Bob invites somebody to a
        // modest role; an Owner later widens that role; accepting would deliver
        // authority Bob could never have granted, days after he asked.
        TenantRole support = Custom("Support", PlatformPermissions.AiAgentsView);

        Fixture world = new Fixture().WithRole(support).WithInviterHolding(
            PlatformPermissions.MembersInvite,
            PlatformPermissions.MembersRolesManage,
            PlatformPermissions.AiAgentsView);

        string token = await world.IssueAsync(support);

        // Somebody with more authority widens the role.
        support.ReplacePermissions(
            [PlatformPermissions.AiAgentsView, PlatformPermissions.AiAgentsEdit],
            Now.AddDays(1));

        await Assert.ThrowsAsync<InvitationNoLongerUsableException>(() =>
            world.Acceptor(Invitee, "bob@example.test")
                .HandleAsync(new AcceptTenantInvitationCommand(token)));

        Assert.Empty(world.Invitations.Accepted);
    }

    [Fact]
    public async Task A_role_that_disappeared_stops_the_acceptance()
    {
        TenantRole support = Custom("Support", PlatformPermissions.AiAgentsView);

        Fixture world = new Fixture().WithRole(support).WithInviterHolding(
            PlatformPermissions.MembersInvite,
            PlatformPermissions.MembersRolesManage,
            PlatformPermissions.AiAgentsView);

        string token = await world.IssueAsync(support);

        world.Roles.Forget(support);

        await Assert.ThrowsAsync<InvitationNoLongerUsableException>(() =>
            world.Acceptor(Invitee, "bob@example.test")
                .HandleAsync(new AcceptTenantInvitationCommand(token)));
    }

    // ---------------------------------------------------------- revocation

    [Fact]
    public async Task A_lower_authority_actor_cannot_withdraw_a_more_powerful_invitation()
    {
        // Revoking looks harmless because it takes nothing from anybody. It is
        // not: cancelling staffing decided by somebody more senior, and issuing
        // your own instead, is substituting your judgement for theirs.
        TenantRole senior = Custom("Senior", PlatformPermissions.AiAgentsEdit);

        Fixture world = new Fixture().WithRole(senior).WithInviterHolding(
            [.. PlatformPermissions.AllCodes]);

        await world.IssueAsync(senior);

        await Assert.ThrowsAsync<PermissionDeniedException>(() =>
            world.Revoker(PlatformPermissions.MembersInvite)
                .HandleAsync(new RevokeTenantInvitationCommand(
                    Workspace,
                    world.Invitations.All[0].Id.Value)));

        Assert.Equal(TenantInvitationStatus.Pending, world.Invitations.All[0].Status);
    }

    [Fact]
    public async Task An_invitation_within_the_actor_s_authority_can_be_withdrawn()
    {
        Fixture world = new Fixture().WithInviterHolding(PlatformPermissions.MembersInvite);

        await world.IssueAsync();

        Assert.True(
            await world.Revoker(PlatformPermissions.MembersInvite)
                .HandleAsync(new RevokeTenantInvitationCommand(
                    Workspace,
                    world.Invitations.All[0].Id.Value)));

        Assert.Equal(TenantInvitationStatus.Revoked, world.Invitations.All[0].Status);
    }

    [Fact]
    public async Task Revoking_needs_the_invite_permission()
    {
        Fixture world = new Fixture().WithInviterHolding(PlatformPermissions.MembersInvite);

        await world.IssueAsync();

        await Assert.ThrowsAsync<PermissionDeniedException>(() =>
            world.Revoker(PlatformPermissions.MembersView)
                .HandleAsync(new RevokeTenantInvitationCommand(
                    Workspace,
                    world.Invitations.All[0].Id.Value)));
    }

    [Fact]
    public async Task An_invitation_from_another_workspace_is_answered_as_absence()
    {
        Fixture world = new Fixture().WithInviterHolding(PlatformPermissions.MembersInvite);

        Assert.False(
            await world.Revoker(PlatformPermissions.MembersInvite)
                .HandleAsync(new RevokeTenantInvitationCommand(
                    Workspace,
                    Guid.CreateVersion7())));
    }

    private static TenantRole Custom(string name, params PermissionCode[] permissions) =>
        TenantRole.CreateCustom(Tenant, TenantRoleName.Create(name), permissions, Now);

    /// <summary>One workspace, its roles, its invitations and its accounts.</summary>
    // ----------------------------------------------- invitation email (37.3)

    [Fact]
    public async Task An_invitation_keeps_the_language_asked_for_and_queues_one_email()
    {
        Fixture world = new();
        world.Accounts.Speaking(Inviter, UserLanguage.English);

        IssuedInvitation issued = await world
            .Creator(PlatformPermissions.MembersInvite)
            .HandleAsync(new CreateTenantInvitationCommand(Workspace, "bob@example.test", [], UserLanguage.Arabic));

        Assert.Equal(UserLanguage.Arabic, issued.Invitation.Language);

        OmniAI.Domain.Accounts.AccountEmail queued = Assert.Single(world.Invitations.QueuedEmails);
        Assert.Equal(issued.Invitation.Id, queued.InvitationId);
        Assert.Equal(issued.Invitation.TenantId, queued.TenantId);
        Assert.Null(queued.UserId);
    }

    [Fact]
    public async Task Without_a_language_an_invitation_takes_the_inviter_s_and_else_none()
    {
        Fixture speaking = new();
        speaking.Accounts.Speaking(Inviter, UserLanguage.Arabic);

        IssuedInvitation inherited = await speaking
            .Creator(PlatformPermissions.MembersInvite)
            .HandleAsync(new CreateTenantInvitationCommand(Workspace, "bob@example.test", []));

        Assert.Equal(UserLanguage.Arabic, inherited.Invitation.Language);

        IssuedInvitation unset = await new Fixture()
            .Creator(PlatformPermissions.MembersInvite)
            .HandleAsync(new CreateTenantInvitationCommand(Workspace, "carol@example.test", []));

        Assert.Null(unset.Invitation.Language);
    }

    [Fact]
    public async Task A_deployment_that_cannot_send_email_queues_no_invitation_email()
    {
        Fixture world = new(emailConfigured: false);

        await world
            .Creator(PlatformPermissions.MembersInvite)
            .HandleAsync(new CreateTenantInvitationCommand(Workspace, "bob@example.test", []));

        Assert.Empty(world.Invitations.QueuedEmails);
    }

    private sealed class Fixture
    {
        public Fixture(bool emailConfigured = true)
        {
            Email = new StubEmailSender(emailConfigured);
            Accounts.With(Inviter, "inviter@example.test");
            Memberships.Grant(Inviter, WorkspaceView);
        }

        public InMemoryInvitationRepository Invitations { get; } = new();

        public RecordingRoleRepository Roles { get; } = new();

        public InMemoryAccountDirectory Accounts { get; } = new();

        public PredictableInvitationTokens Tokens { get; } = new();

        /// <summary>Whether invitation emails are queued (Phase 37.3).</summary>
        public StubEmailSender Email { get; }

        public FixedTimeProvider Clock { get; } = new(Now);

        public InMemoryTenantMembershipRepository Memberships { get; } = new();

        public InMemoryTenantRepository Tenants { get; } = new();

        public Fixture WithRole(TenantRole role)
        {
            Roles.AddRole(role);

            return this;
        }

        /// <summary>Gives the inviter a role carrying exactly these permissions.</summary>
        public Fixture WithInviterHolding(params PermissionCode[] permissions)
        {
            InviterNowHolds(permissions);

            return this;
        }

        public void InviterNowHolds(params PermissionCode[] permissions)
        {
            Roles.ReplaceAssignments(
                Inviter,
                TenantRole.CreateCustom(
                    Tenant,
                    TenantRoleName.Create($"Inviter {Guid.NewGuid():N}"[..20]),
                    permissions,
                    Now));
        }

        public void InviterLeaves() => Memberships.Forget(Inviter);

        public CreateTenantInvitationHandler Creator(params PermissionCode[] held) =>
            new(
                new FakeEntitlements(),
                Invitations,
                Roles,
                TestAuthority.With(Inviter, Workspace, held),
                Tokens,
                new InvitationPolicy(),
                Accounts,
                Email,
                Clock);

        public RevokeTenantInvitationHandler Revoker(params PermissionCode[] held) =>
            new(Invitations, Roles, TestAuthority.With(Inviter, Workspace, held), Clock);

        public AcceptTenantInvitationHandler Acceptor(UserId userId, string email)
        {
            Accounts.With(userId, email);
            Tenants.Seed(WorkspaceTenant);

            return new AcceptTenantInvitationHandler(
            new FakeEntitlements(),
                Invitations,
                Roles,
                Memberships,
                Tenants,
                Accounts,
                Tokens,
                FakeCurrentUser.SignedInAs(userId),
                Clock);
        }

        /// <summary>Issues an invitation directly, returning its raw token.</summary>
        public async Task<string> IssueAsync(params TenantRole[] roles)
        {
            NewInvitationToken token = Tokens.Create();

            TenantInvitation invitation = TenantInvitation.Issue(
                Tenant,
                EmailAddress.Create("bob@example.test"),
                Inviter,
                token.Hash,
                roles.Select(role => role.Id),
                Clock.GetUtcNow(),
                new InvitationPolicy().ExpiresAfter(Clock.GetUtcNow()));

            Invitations.Seed(invitation);

            return await Task.FromResult(token.Token);
        }
    }
}
