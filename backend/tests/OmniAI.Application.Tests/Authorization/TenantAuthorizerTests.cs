using OmniAI.Application.Authorization;
using OmniAI.Application.Security;
using OmniAI.Application.Tests.Security;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Application.Tests.Authorization;

/// <summary>
/// The boundary every tenant-scoped use case goes through.
/// </summary>
/// <remarks>
/// These tests exercise the three-valued decision directly, rather than only
/// through the convenience wrapper the handlers use. The distinction between "not
/// a member" and "a member without the permission" is the thing most easily lost
/// in a refactor, and it is the thing that decides whether a stranger learns which
/// workspaces exist.
/// </remarks>
public sealed class TenantAuthorizerTests
{
    private static readonly UserId Alice = UserId.New();
    private static readonly Guid Workspace = Guid.CreateVersion7();
    private static readonly Guid Elsewhere = Guid.CreateVersion7();

    [Fact]
    public async Task An_owner_is_granted_every_permission_in_the_catalogue()
    {
        TenantAuthorizer authorizer = TestAuthority.Owner(Alice, Workspace);

        foreach (PermissionCode permission in PlatformPermissions.AllCodes)
        {
            Assert.Equal(
                TenantAccessDecision.Granted,
                await authorizer.AuthorizeAsync(TenantId.From(Workspace), permission));
        }
    }

    [Fact]
    public async Task A_member_holding_the_permission_is_granted()
    {
        TenantAuthorizer authorizer =
            TestAuthority.With(Alice, Workspace, PlatformPermissions.AiAgentsView);

        Assert.Equal(
            TenantAccessDecision.Granted,
            await authorizer.AuthorizeAsync(
                TenantId.From(Workspace),
                PlatformPermissions.AiAgentsView));
    }

    [Fact]
    public async Task A_member_lacking_the_permission_is_denied_not_reported_as_missing()
    {
        // The distinction this whole type exists to preserve. A member has
        // already proved they belong here, so telling them the workspace does not
        // exist would be a lie -- and telling them which permission is missing is
        // something they can act on.
        TenantAuthorizer authorizer =
            TestAuthority.With(Alice, Workspace, PlatformPermissions.AiAgentsView);

        Assert.Equal(
            TenantAccessDecision.PermissionDenied,
            await authorizer.AuthorizeAsync(
                TenantId.From(Workspace),
                PlatformPermissions.AiAgentsEdit));
    }

    [Fact]
    public async Task A_member_with_no_roles_is_denied_rather_than_granted_anything()
    {
        // A real state: somebody belongs to a workspace and has been given
        // nothing to do in it. A design that treated "no roles" as "unrestricted"
        // would be the worst possible default.
        TenantAuthorizer authorizer = TestAuthority.With(Alice, Workspace);

        Assert.Equal(
            TenantAccessDecision.PermissionDenied,
            await authorizer.AuthorizeAsync(
                TenantId.From(Workspace),
                PlatformPermissions.WorkspaceView));
    }

    [Fact]
    public async Task A_non_member_is_reported_as_not_a_member_whatever_the_permission()
    {
        // And it is the same answer for a workspace that does not exist, because
        // the authorizer does not know which it was either. That is what stops a
        // tenant id from being probeable.
        TenantAuthorizer authorizer = TestAuthority.Outsider(Alice);

        Assert.Equal(
            TenantAccessDecision.NotAMember,
            await authorizer.AuthorizeAsync(
                TenantId.From(Workspace),
                PlatformPermissions.WorkspaceView));
    }

    [Fact]
    public async Task Membership_of_one_workspace_grants_nothing_in_another()
    {
        TenantAuthorizer authorizer = TestAuthority.Owner(Alice, Workspace);

        Assert.Equal(
            TenantAccessDecision.NotAMember,
            await authorizer.AuthorizeAsync(
                TenantId.From(Elsewhere),
                PlatformPermissions.WorkspaceView));
    }

    [Fact]
    public async Task An_anonymous_caller_is_refused_rather_than_decided_about()
    {
        // Thrown rather than returned as a decision, because it is not a decision
        // about access: the caller has not said who they are, and doing so may
        // change the answer entirely. A 404 here would tell an unauthenticated
        // client its session was fine and the resource was gone.
        TenantAuthorizer authorizer = TestAuthority.Anonymous();

        await Assert.ThrowsAsync<NotAuthenticatedException>(() => authorizer.AuthorizeAsync(
            TenantId.From(Workspace),
            PlatformPermissions.WorkspaceView));
    }

    [Fact]
    public async Task Require_returns_false_for_a_non_member_and_throws_for_a_denied_member()
    {
        // The two failures leave by different doors, which is what lets a handler
        // answer 404 for one and 403 for the other with one line of code.
        Assert.False(await TestAuthority.Outsider(Alice)
            .RequireAsync(TenantId.From(Workspace), PlatformPermissions.WorkspaceView));

        PermissionDeniedException denied = await Assert.ThrowsAsync<PermissionDeniedException>(
            () => TestAuthority.With(Alice, Workspace)
                .RequireAsync(TenantId.From(Workspace), PlatformPermissions.WorkspaceView));

        Assert.Equal(PlatformPermissions.WorkspaceView.Value, denied.RequiredPermission);
    }

    [Fact]
    public async Task Require_returns_true_when_the_permission_is_held()
    {
        Assert.True(await TestAuthority.Owner(Alice, Workspace)
            .RequireAsync(TenantId.From(Workspace), PlatformPermissions.WorkspaceView));
    }

    [Fact]
    public async Task Effective_permissions_are_the_union_of_the_members_roles()
    {
        // Union, not intersection and not "the first role wins". Two roles each
        // carrying part of what somebody needs must add up.
        TenantRole viewing = TenantRole.CreateCustom(
            TenantId.From(Workspace),
            TenantRoleName.Create("Viewer"),
            [PlatformPermissions.AiAgentsView],
            DateTimeOffset.UnixEpoch);

        TenantRole previewing = TenantRole.CreateCustom(
            TenantId.From(Workspace),
            TenantRoleName.Create("Tester"),
            [PlatformPermissions.AiAgentsPreview, PlatformPermissions.AiAgentsView],
            DateTimeOffset.UnixEpoch);

        InMemoryTenantRoleRepository roles = new InMemoryTenantRoleRepository()
            .AddRole(viewing)
            .AddRole(previewing)
            .Assign(Alice, viewing)
            .Assign(Alice, previewing);

        TenantAuthorizer authorizer =
            new(TestAccess.ForTenants(Alice, Workspace), roles);

        // Deduplicated -- ai-agents.view is in both roles -- and in catalogue
        // order, so two members with the same authority get identical answers.
        Assert.Equal(
            [PlatformPermissions.AiAgentsView, PlatformPermissions.AiAgentsPreview],
            await authorizer.EffectivePermissionsAsync(TenantId.From(Workspace)));
    }

    [Fact]
    public async Task Effective_permissions_for_an_owner_are_the_whole_catalogue()
    {
        Assert.Equal(
            PlatformPermissions.AllCodes,
            await TestAuthority.Owner(Alice, Workspace)
                .EffectivePermissionsAsync(TenantId.From(Workspace)));
    }

    [Fact]
    public async Task Effective_permissions_for_a_member_with_no_roles_are_empty_not_null()
    {
        // Empty and null mean different things here: a member who has been given
        // nothing, and somebody the workspace does not know. The admin
        // application has to tell those apart to explain itself.
        Assert.Empty(
            (await TestAuthority.With(Alice, Workspace)
                .EffectivePermissionsAsync(TenantId.From(Workspace)))!);
    }

    [Fact]
    public async Task Effective_permissions_for_a_non_member_are_null()
    {
        Assert.Null(await TestAuthority.Outsider(Alice)
            .EffectivePermissionsAsync(TenantId.From(Workspace)));
    }

    [Fact]
    public async Task Effective_permissions_require_a_signed_in_caller()
    {
        await Assert.ThrowsAsync<NotAuthenticatedException>(
            () => TestAuthority.Anonymous().EffectivePermissionsAsync(TenantId.From(Workspace)));
    }

    [Fact]
    public void The_current_user_is_available_without_a_workspace()
    {
        // Creating a workspace and listing one's own workspaces need an identity
        // rather than a permission -- neither happens inside a tenant.
        Assert.Equal(Alice, TestAuthority.Outsider(Alice).RequireCurrentUser());

        Assert.Throws<NotAuthenticatedException>(
            () => TestAuthority.Anonymous().RequireCurrentUser());
    }

    [Fact]
    public void The_authorizer_requires_its_dependencies()
    {
        Assert.Throws<ArgumentNullException>(
            () => new TenantAuthorizer(null!, new InMemoryTenantRoleRepository()));

        Assert.Throws<ArgumentNullException>(
            () => new TenantAuthorizer(TestAccess.Outsider(Alice), null!));
    }
}
