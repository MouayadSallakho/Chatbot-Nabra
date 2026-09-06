using OmniAI.Domain;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Invitations;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Domain.Tests.Invitations;

/// <summary>
/// The invitation aggregate: what it holds, and how it ends.
/// </summary>
/// <remarks>
/// The security questions -- may this inviter grant this, is this the invited
/// account -- belong to the use case, because they need the workspace's current
/// state and an aggregate can only see itself. What is proven here is narrower
/// and still worth having: an invitation cannot be used twice, cannot be used
/// after it was withdrawn, and cannot be used once it has aged out.
/// </remarks>
public sealed class TenantInvitationTests
{
    private static readonly TenantId Workspace = TenantId.From(Guid.CreateVersion7());
    private static readonly UserId Inviter = UserId.New();
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Expiry = Now.AddDays(7);

    [Fact]
    public void An_invitation_starts_pending_and_usable()
    {
        TenantInvitation invitation = Issue();

        Assert.Equal(TenantInvitationStatus.Pending, invitation.Status);
        Assert.True(invitation.IsUsableAt(Now));
        Assert.Null(invitation.AcceptedAt);
        Assert.Null(invitation.RevokedAt);
    }

    [Fact]
    public void It_carries_the_roles_it_was_given()
    {
        TenantRoleId support = TenantRoleId.New();
        TenantRoleId billing = TenantRoleId.New();

        TenantInvitation invitation = Issue(support, billing);

        Assert.Equal([support, billing], invitation.RoleIds);
    }

    [Fact]
    public void Asking_for_the_same_role_twice_grants_it_once()
    {
        // The same intent expressed twice. Refusing it would be pedantry a client
        // has to write code to avoid, and the stored result is identical.
        TenantRoleId support = TenantRoleId.New();

        Assert.Equal([support], Issue(support, support).RoleIds);
    }

    [Fact]
    public void An_invitation_granting_nothing_is_legitimate()
    {
        // Somebody who may invite but not hand out authority can still bring a
        // colleague in; an administrator grants access afterwards.
        Assert.Empty(Issue().RoleIds);
    }

    [Fact]
    public void Every_invitation_role_belongs_to_the_invitation_and_its_tenant()
    {
        // The third copy of the tenant is what lets the database hold the
        // invitation and the role to the same workspace.
        TenantRoleId support = TenantRoleId.New();

        InvitationRoleGrant grant = Assert.Single(Issue(support).Roles);

        Assert.Equal(Workspace, grant.TenantId);
        Assert.Equal(support, grant.RoleId);
    }

    [Fact]
    public void It_must_expire_after_it_was_created()
    {
        Assert.Throws<DomainValidationException>(() => TenantInvitation.Issue(
            Workspace,
            EmailAddress.Create("person@example.test"),
            Inviter,
            Hash(),
            [],
            Now,
            Now));
    }

    [Fact]
    public void It_must_name_a_tenant_an_address_and_an_inviter()
    {
        Assert.Throws<DomainValidationException>(() => TenantInvitation.Issue(
            default,
            EmailAddress.Create("person@example.test"),
            Inviter,
            Hash(),
            [],
            Now,
            Expiry));

        Assert.Throws<DomainValidationException>(() => TenantInvitation.Issue(
            Workspace,
            default,
            Inviter,
            Hash(),
            [],
            Now,
            Expiry));

        Assert.Throws<DomainValidationException>(() => TenantInvitation.Issue(
            Workspace,
            EmailAddress.Create("person@example.test"),
            default,
            Hash(),
            [],
            Now,
            Expiry));
    }

    // ------------------------------------------------------------------ expiry

    [Fact]
    public void It_stops_being_usable_the_moment_it_expires()
    {
        // Decided by the clock, not by the stored status: a row still marked
        // pending is unusable whether or not anything has recorded that.
        TenantInvitation invitation = Issue();

        Assert.True(invitation.IsUsableAt(Expiry.AddTicks(-1)));
        Assert.False(invitation.IsUsableAt(Expiry));
        Assert.Equal(TenantInvitationStatus.Pending, invitation.Status);
    }

    [Fact]
    public void An_aged_out_invitation_can_be_recorded_as_expired()
    {
        TenantInvitation invitation = Issue();

        invitation.Expire(Expiry);

        Assert.Equal(TenantInvitationStatus.Expired, invitation.Status);
    }

    [Fact]
    public void A_live_invitation_cannot_be_recorded_as_expired()
    {
        // Otherwise closing the open-invitation slot would be a way to cancel
        // somebody else's invitation without revoking it.
        Assert.Throws<DomainStateException>(() => Issue().Expire(Now));
    }

    // -------------------------------------------------------------- acceptance

    [Fact]
    public void Accepting_records_who_accepted_and_when()
    {
        TenantInvitation invitation = Issue();
        UserId acceptor = UserId.New();

        invitation.Accept(acceptor, Now);

        Assert.Equal(TenantInvitationStatus.Accepted, invitation.Status);
        Assert.Equal(acceptor, invitation.AcceptedByUserId);
        Assert.Equal(Now, invitation.AcceptedAt);
    }

    [Fact]
    public void It_cannot_be_accepted_twice()
    {
        // Single use. Without this a shared link would admit everybody who saw it
        // who happened to hold the invited address.
        TenantInvitation invitation = Issue();

        invitation.Accept(UserId.New(), Now);

        Assert.Throws<DomainStateException>(() => invitation.Accept(UserId.New(), Now));
    }

    [Fact]
    public void It_cannot_be_accepted_after_it_was_revoked()
    {
        TenantInvitation invitation = Issue();

        invitation.Revoke(Now);

        Assert.Throws<DomainStateException>(() => invitation.Accept(UserId.New(), Now));
    }

    [Fact]
    public void It_cannot_be_accepted_after_it_expired()
    {
        Assert.Throws<DomainStateException>(() => Issue().Accept(UserId.New(), Expiry));
    }

    [Fact]
    public void Acceptance_must_name_the_account_accepting()
    {
        Assert.Throws<DomainValidationException>(() => Issue().Accept(default, Now));
    }

    // -------------------------------------------------------------- revocation

    [Fact]
    public void Revoking_records_when()
    {
        TenantInvitation invitation = Issue();

        invitation.Revoke(Now);

        Assert.Equal(TenantInvitationStatus.Revoked, invitation.Status);
        Assert.Equal(Now, invitation.RevokedAt);
    }

    [Fact]
    public void An_aged_out_invitation_can_still_be_withdrawn()
    {
        // An administrator closing a stale-looking entry should not be told the
        // operation is invalid.
        TenantInvitation invitation = Issue();

        invitation.Revoke(Expiry.AddDays(1));

        Assert.Equal(TenantInvitationStatus.Revoked, invitation.Status);
    }

    [Fact]
    public void An_accepted_invitation_cannot_be_revoked()
    {
        // Revoking must not be able to undo somebody's membership by the back
        // door.
        TenantInvitation invitation = Issue();

        invitation.Accept(UserId.New(), Now);

        Assert.Throws<DomainStateException>(() => invitation.Revoke(Now));
    }

    [Fact]
    public void It_cannot_be_revoked_twice()
    {
        TenantInvitation invitation = Issue();

        invitation.Revoke(Now);

        Assert.Throws<DomainStateException>(() => invitation.Revoke(Now));
    }

    // --------------------------------------------------------------- the token

    [Fact]
    public void The_aggregate_exposes_a_digest_and_has_no_way_to_hold_a_raw_token()
    {
        // Structural, not a rule: there is no property, field or constructor
        // parameter that could carry the secret, so no code path can log,
        // serialise or store one.
        Assert.DoesNotContain(
            typeof(TenantInvitation).GetProperties(),
            property => property.PropertyType == typeof(string));
    }

    private static TenantInvitation Issue(params TenantRoleId[] roleIds) =>
        TenantInvitation.Issue(
            Workspace,
            EmailAddress.Create("person@example.test"),
            Inviter,
            Hash(),
            roleIds,
            Now,
            Expiry);

    private static InvitationTokenHash Hash() =>
        InvitationTokenHash.From(new byte[InvitationTokenHash.Length]);
}
