using OmniAI.Application.Security;
using OmniAI.Application.Tenants;
using OmniAI.Application.Tests.Security;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Application.Tests.Tenants;

/// <summary>
/// The one place that decides whether a caller may touch a tenant.
/// </summary>
/// <remarks>
/// Tested on its own, without HTTP, because that is where it has to be correct.
/// An endpoint's authorization requirement proves somebody is signed in; this is
/// what decides <em>which</em> workspace they may address, and it must hold even
/// if a route were mapped with no requirement at all.
/// </remarks>
public sealed class TenantAccessGuardTests
{
    private static readonly UserId Alice = UserId.New();
    private static readonly UserId Bob = UserId.New();

    private static readonly TenantView Workspace =
        new(Guid.CreateVersion7(), "Acme Trading", "acme", TenantStatus.Active);

    [Fact]
    public async Task A_member_is_admitted()
    {
        TenantAccessGuard guard = TestAccess.For(Alice, Workspace);

        Assert.True(await guard.IsCurrentUserMemberAsync(TenantId.From(Workspace.Id)));
    }

    [Fact]
    public async Task A_signed_in_stranger_is_refused_without_an_exception()
    {
        // False rather than a throw, because each use case turns this into its own
        // ordinary not-found answer. An exception here would have to carry a
        // status, which is a decision the Application layer does not make.
        TenantAccessGuard guard = TestAccess.Outsider(Bob);

        Assert.False(await guard.IsCurrentUserMemberAsync(TenantId.From(Workspace.Id)));
    }

    [Fact]
    public async Task One_persons_membership_does_not_admit_another()
    {
        // The property that makes the whole model work: membership is per person,
        // and holding one for a workspace says nothing about anybody else.
        InMemoryTenantMembershipRepository memberships =
            new InMemoryTenantMembershipRepository().Grant(Alice, Workspace);

        TenantAccessGuard bob = new(FakeCurrentUser.SignedInAs(Bob), memberships);

        Assert.False(await bob.IsCurrentUserMemberAsync(TenantId.From(Workspace.Id)));
    }

    [Fact]
    public async Task Membership_of_one_workspace_does_not_admit_another()
    {
        TenantView other = new(Guid.CreateVersion7(), "Rival", "rival", TenantStatus.Active);

        TenantAccessGuard guard = TestAccess.For(Alice, Workspace);

        Assert.False(await guard.IsCurrentUserMemberAsync(TenantId.From(other.Id)));
    }

    [Fact]
    public async Task An_anonymous_caller_is_refused_by_exception_rather_than_by_answer()
    {
        // 401 and 404 mean different things to a client, so the guard must not
        // collapse "you have not said who you are" into "it is not there".
        TenantAccessGuard guard = TestAccess.Anonymous();

        await Assert.ThrowsAsync<NotAuthenticatedException>(
            () => guard.IsCurrentUserMemberAsync(TenantId.From(Workspace.Id)));
    }

    [Fact]
    public void An_anonymous_caller_has_no_identity_to_hand_out()
    {
        TenantAccessGuard guard = TestAccess.Anonymous();

        Assert.Throws<NotAuthenticatedException>(() => guard.RequireCurrentUser());
    }

    [Fact]
    public void A_signed_in_caller_is_identified()
    {
        TenantAccessGuard guard = TestAccess.Outsider(Alice);

        Assert.Equal(Alice, guard.RequireCurrentUser());
    }

    [Fact]
    public void The_guard_requires_its_dependencies()
    {
        Assert.Throws<ArgumentNullException>(
            () => new TenantAccessGuard(null!, new InMemoryTenantMembershipRepository()));

        Assert.Throws<ArgumentNullException>(
            () => new TenantAccessGuard(FakeCurrentUser.SignedInAs(Alice), null!));
    }

    [Fact]
    public void An_authenticated_caller_reports_an_identity_and_an_anonymous_one_does_not()
    {
        // IsAuthenticated must be derived from having a usable id, not asserted
        // separately -- a principal that claims to be authenticated but carries no
        // id cannot be matched to a membership and must not be treated as signed
        // in.
        Assert.True(FakeCurrentUser.SignedInAs(Alice).IsAuthenticated);
        Assert.Equal(Alice, FakeCurrentUser.SignedInAs(Alice).Id);

        Assert.False(FakeCurrentUser.Anonymous().IsAuthenticated);
        Assert.Null(FakeCurrentUser.Anonymous().Id);
    }
}
