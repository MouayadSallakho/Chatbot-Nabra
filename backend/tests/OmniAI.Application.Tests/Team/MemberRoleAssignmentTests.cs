using OmniAI.Application.Authorization;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Members;
using OmniAI.Application.Members.ReplaceTenantMemberRoles;
using OmniAI.Application.Tests.Authorization;
using OmniAI.Application.Tests.Security;
using OmniAI.Domain;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;
using OmniAI.Testing;

namespace OmniAI.Application.Tests.Team;

/// <summary>
/// Handing out access, and the ceiling that bounds it.
/// </summary>
/// <remarks>
/// This is the operation the whole phase exists to make safe: the only place in
/// the platform where one person's action changes what a different person may
/// do. Every rule here is checked against stored state, never against anything
/// the request supplied.
/// </remarks>
public sealed class MemberRoleAssignmentTests
{
    private static readonly Guid Workspace = Guid.CreateVersion7();
    private static readonly TenantId Tenant = TenantId.From(Workspace);
    private static readonly UserId Manager = UserId.New();
    private static readonly UserId Target = UserId.New();
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 9, 0, 0, TimeSpan.Zero);

    // --------------------------------------------------------- within the ceiling

    [Fact]
    public async Task A_manager_may_give_a_role_they_could_use_themselves()
    {
        TenantRole support = Custom("Support", PlatformPermissions.AiAgentsView);

        (var team, var roles) = Store(support);
        team.Add(Target, Tenant, "bob@example.test");

        Versioned<TenantMemberView>? updated = await Handler(
            team,
            roles,
            PlatformPermissions.MembersRolesManage,
            PlatformPermissions.AiAgentsView)
            .HandleAsync(Command(support.Id));

        Assert.NotNull(updated);
        Assert.Equal(["Support"], updated.Value.Roles.Select(role => role.Name));
        Assert.Equal([PlatformPermissions.AiAgentsView], updated.Value.Permissions);
    }

    [Fact]
    public async Task A_member_may_be_left_holding_nothing()
    {
        // A real state, not an edge case: somebody who belongs to the workspace
        // and has not been given anything to do in it yet.
        TenantRole support = Custom("Support", PlatformPermissions.AiAgentsView);

        (var team, var roles) = Store(support);
        team.Add(Target, Tenant, "bob@example.test", support);

        Versioned<TenantMemberView>? updated = await Handler(
            team,
            roles,
            PlatformPermissions.MembersRolesManage,
            PlatformPermissions.AiAgentsView)
            .HandleAsync(Command());

        Assert.NotNull(updated);
        Assert.Empty(updated.Value.Roles);
        Assert.Empty(updated.Value.Permissions);
    }

    [Fact]
    public async Task Authority_is_the_union_of_every_role_the_actor_holds()
    {
        // The manager's members.roles.manage and their ai-agents.edit live in
        // different roles. Reading only one would refuse a legitimate assignment.
        TenantRole editing = Custom("Editors", PlatformPermissions.AiAgentsEdit);

        (var team, var roles) = Store(editing);
        team.Add(Target, Tenant, "bob@example.test");

        TenantRole managing = Custom("Managers", PlatformPermissions.MembersRolesManage);
        TenantRole alsoEditing = Custom("Also editors", PlatformPermissions.AiAgentsEdit);

        InMemoryTenantRoleRepository authority = new InMemoryTenantRoleRepository()
            .AddRole(managing)
            .AddRole(alsoEditing);

        authority.Assign(Manager, managing).Assign(Manager, alsoEditing);

        TenantAuthorizer authorizer =
            new(TestAccess.ForTenants(Manager, Workspace), authority);

        Versioned<TenantMemberView>? updated =
            await new ReplaceTenantMemberRolesHandler(
                team,
                roles,
                authorizer,
                new FixedTimeProvider(Now))
            .HandleAsync(Command(editing.Id));

        Assert.NotNull(updated);
        Assert.Equal(["Editors"], updated.Value.Roles.Select(role => role.Name));
    }

    // ------------------------------------------------------------ the ceiling

    [Fact]
    public async Task A_manager_cannot_give_away_what_they_do_not_hold()
    {
        TenantRole senior = Custom("Senior", PlatformPermissions.AiAgentsEdit);

        (var team, var roles) = Store(senior);
        team.Add(Target, Tenant, "bob@example.test");

        PermissionDeniedException denied =
            await Assert.ThrowsAsync<PermissionDeniedException>(() =>
                Handler(team, roles, PlatformPermissions.MembersRolesManage)
                    .HandleAsync(Command(senior.Id)));

        Assert.Equal(PlatformPermissions.AiAgentsEdit.Value, denied.RequiredPermission);
        Assert.Empty(team.RolesOf(Target, Tenant));
    }

    [Fact]
    public async Task A_manager_cannot_touch_a_member_who_already_outranks_them()
    {
        // Not about what is being asked for -- about what the target already
        // holds. Rewriting a senior colleague's roles is taking their access
        // away, which is why narrowing is refused too.
        TenantRole senior = Custom("Senior", PlatformPermissions.AiAgentsEdit);
        TenantRole support = Custom("Support", PlatformPermissions.AiAgentsView);

        (var team, var roles) = Store(senior, support);
        team.Add(Target, Tenant, "bob@example.test", senior);

        await Assert.ThrowsAsync<PermissionDeniedException>(() =>
            Handler(
                team,
                roles,
                PlatformPermissions.MembersRolesManage,
                PlatformPermissions.AiAgentsView)
                .HandleAsync(Command(support.Id)));

        Assert.Equal(["Senior"], team.RolesOf(Target, Tenant).Select(role => role.Name.Value));
    }

    [Fact]
    public async Task A_refused_assignment_changes_nothing()
    {
        TenantRole senior = Custom("Senior", PlatformPermissions.AiAgentsEdit);
        TenantRole support = Custom("Support", PlatformPermissions.AiAgentsView);

        (var team, var roles) = Store(senior, support);
        team.Add(Target, Tenant, "bob@example.test", support);

        await Assert.ThrowsAsync<PermissionDeniedException>(() =>
            Handler(
                team,
                roles,
                PlatformPermissions.MembersRolesManage,
                PlatformPermissions.AiAgentsView)
                .HandleAsync(Command(senior.Id)));

        Assert.Equal(["Support"], team.RolesOf(Target, Tenant).Select(role => role.Name.Value));
    }

    // ------------------------------------------------------ protected members

    [Fact]
    public async Task Nobody_changes_their_own_access_here()
    {
        // Not paternalism: an administrator who removes their own last role in a
        // workspace where they are the only one has locked everybody out
        // permanently, and there is no recovery path.
        TenantRole support = Custom("Support", PlatformPermissions.AiAgentsView);

        (var team, var roles) = Store(support);
        team.Add(Manager, Tenant, "manager@example.test");

        await Assert.ThrowsAsync<ProtectedMembershipException>(() =>
            Handler(team, roles, PlatformPermissions.MembersRolesManage)
                .HandleAsync(new ReplaceTenantMemberRolesCommand(
                    Workspace,
                    Manager.Value,
                    [],
                    ConcurrencyToken.From("v1"))));
    }

    [Fact]
    public async Task The_owner_is_not_reassigned_from_team_management()
    {
        TenantRole owner = TenantRole.CreateOwner(Tenant, Now);
        TenantRole support = Custom("Support", PlatformPermissions.AiAgentsView);

        (var team, var roles) = Store(support);
        roles.AddRole(owner);
        team.Add(Target, Tenant, "owner@example.test", owner);

        // Attempted by an actor holding the whole catalogue, so the refusal is
        // about the target rather than about the caller's authority.
        await Assert.ThrowsAsync<ProtectedMembershipException>(() =>
            Handler(team, roles, [.. PlatformPermissions.AllCodes])
                .HandleAsync(Command(support.Id)));
    }

    [Fact]
    public async Task The_owner_role_cannot_be_handed_to_anybody()
    {
        TenantRole owner = TenantRole.CreateOwner(Tenant, Now);

        (var team, var roles) = Store();
        roles.AddRole(owner);
        team.Add(Target, Tenant, "bob@example.test");

        await Assert.ThrowsAsync<ProtectedMembershipException>(() =>
            Handler(team, roles, [.. PlatformPermissions.AllCodes])
                .HandleAsync(Command(owner.Id)));

        Assert.Empty(team.RolesOf(Target, Tenant));
    }

    // ------------------------------------------------------------ other cases

    [Fact]
    public async Task A_role_from_another_workspace_is_answered_like_one_that_never_existed()
    {
        (var team, var roles) = Store();
        team.Add(Target, Tenant, "bob@example.test");

        TenantRole elsewhere = TenantRole.CreateCustom(
            TenantId.From(Guid.CreateVersion7()),
            TenantRoleName.Create("Theirs"),
            [PlatformPermissions.AiAgentsView],
            Now);

        roles.AddRole(elsewhere);

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            Handler(team, roles, [.. PlatformPermissions.AllCodes])
                .HandleAsync(Command(elsewhere.Id)));
    }

    [Fact]
    public async Task A_stale_version_is_refused_and_changes_nothing()
    {
        TenantRole support = Custom("Support", PlatformPermissions.AiAgentsView);

        (var team, var roles) = Store(support);
        team.Add(Target, Tenant, "bob@example.test");

        await Assert.ThrowsAsync<TenantMemberVersionMismatchException>(() =>
            Handler(
                team,
                roles,
                PlatformPermissions.MembersRolesManage,
                PlatformPermissions.AiAgentsView)
                .HandleAsync(new ReplaceTenantMemberRolesCommand(
                    Workspace,
                    Target.Value,
                    [support.Id.Value],
                    ConcurrencyToken.From("v-old"))));

        Assert.Empty(team.RolesOf(Target, Tenant));
    }

    [Fact]
    public async Task The_precondition_is_answered_before_the_protected_rules()
    {
        // A caller working from a version somebody else replaced is being judged
        // against a role set they were never shown, so the stale answer is the
        // accurate one -- the same ordering the role slice records.
        TenantRole owner = TenantRole.CreateOwner(Tenant, Now);

        (var team, var roles) = Store();
        roles.AddRole(owner);
        team.Add(Target, Tenant, "owner@example.test", owner);

        await Assert.ThrowsAsync<TenantMemberVersionMismatchException>(() =>
            Handler(team, roles, [.. PlatformPermissions.AllCodes])
                .HandleAsync(new ReplaceTenantMemberRolesCommand(
                    Workspace,
                    Target.Value,
                    [],
                    ConcurrencyToken.From("v-old"))));
    }

    [Fact]
    public async Task A_non_member_is_answered_as_absence_rather_than_refusal()
    {
        (var team, var roles) = Store();
        team.Add(Target, Tenant, "bob@example.test");

        ReplaceTenantMemberRolesHandler handler = new(
            team,
            roles,
            TestAuthority.Outsider(Manager),
            new FixedTimeProvider(Now));

        Assert.Null(await handler.HandleAsync(Command()));
    }

    [Fact]
    public async Task A_member_this_workspace_does_not_have_is_absence_too()
    {
        // The same answer whether the account exists elsewhere or not at all, so
        // this cannot be used to discover who has registered.
        (var team, var roles) = Store();

        Assert.Null(
            await Handler(team, roles, [.. PlatformPermissions.AllCodes])
                .HandleAsync(Command()));
    }

    [Fact]
    public async Task A_member_without_the_permission_is_refused()
    {
        (var team, var roles) = Store();
        team.Add(Target, Tenant, "bob@example.test");

        await Assert.ThrowsAsync<PermissionDeniedException>(() =>
            Handler(team, roles, PlatformPermissions.MembersView).HandleAsync(Command()));
    }

    [Fact]
    public async Task An_anonymous_caller_is_told_to_sign_in()
    {
        (var team, var roles) = Store();

        ReplaceTenantMemberRolesHandler handler = new(
            team,
            roles,
            TestAuthority.Anonymous(),
            new FixedTimeProvider(Now));

        await Assert.ThrowsAsync<Application.Security.NotAuthenticatedException>(() =>
            handler.HandleAsync(Command()));
    }

    [Fact]
    public async Task A_successful_assignment_moves_the_version()
    {
        // Without this the assignments would change while the parent's version
        // stood still, and a client's ETag would still look current.
        TenantRole support = Custom("Support", PlatformPermissions.AiAgentsView);

        (var team, var roles) = Store(support);
        team.Add(Target, Tenant, "bob@example.test");

        Versioned<TenantMemberView>? updated = await Handler(
            team,
            roles,
            PlatformPermissions.MembersRolesManage,
            PlatformPermissions.AiAgentsView)
            .HandleAsync(Command(support.Id));

        Assert.NotNull(updated);
        Assert.NotEqual(ConcurrencyToken.From("v1"), updated.Version);
    }

    // ------------------------------------------------------------- arranging

    private static ReplaceTenantMemberRolesCommand Command(params TenantRoleId[] roleIds) =>
        new(
            Workspace,
            Target.Value,
            [.. roleIds.Select(id => id.Value)],
            ConcurrencyToken.From("v1"));

    private static TenantRole Custom(string name, params PermissionCode[] permissions) =>
        TenantRole.CreateCustom(Tenant, TenantRoleName.Create(name), permissions, Now);

    private static (InMemoryTeamRepository Team, RecordingRoleRepository Roles) Store(
        params TenantRole[] roles)
    {
        InMemoryTeamRepository team = new();
        RecordingRoleRepository store = new();

        foreach (TenantRole role in roles)
        {
            store.AddRole(role);
            team.Roles.Add(role);
        }

        return (team, store);
    }

    /// <summary>A handler whose actor holds exactly these permissions.</summary>
    private static ReplaceTenantMemberRolesHandler Handler(
        InMemoryTeamRepository team,
        RecordingRoleRepository roles,
        params PermissionCode[] held) =>
        new(team, roles, TestAuthority.With(Manager, Workspace, held), new FixedTimeProvider(Now));
}
