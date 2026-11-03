using OmniAI.Application.Authorization;
using OmniAI.Application.Authorization.CreateTenantRole;
using OmniAI.Application.Authorization.GetMyTenantPermissions;
using OmniAI.Application.Authorization.GetPermissionCatalog;
using OmniAI.Application.Authorization.GetTenantRoleById;
using OmniAI.Application.Authorization.ListTenantRoles;
using OmniAI.Application.Authorization.RenameTenantRole;
using OmniAI.Application.Authorization.ReplaceTenantRolePermissions;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Security;
using OmniAI.Application.Tenants;
using OmniAI.Application.Tests.Security;
using OmniAI.Domain;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;
using OmniAI.Testing;

namespace OmniAI.Application.Tests.Authorization;

/// <summary>
/// The role administration use cases.
/// </summary>
/// <remarks>
/// Each handler is given a real <see cref="TenantAuthorizer"/> over real fakes,
/// never a stub, so a handler that stopped consulting it would fail here rather
/// than pass quietly.
/// </remarks>
public sealed class TenantRoleHandlerTests
{
    private static readonly UserId Alice = UserId.New();
    private static readonly Guid Workspace = Guid.CreateVersion7();
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Now.AddHours(2);

    // ---------------------------------------------------------------- create

    [Fact]
    public async Task A_custom_role_is_created_with_the_permissions_it_was_given()
    {
        RecordingRoleRepository roles = new();

        Versioned<TenantRoleView> created = await Creator(roles).HandleAsync(
            new CreateTenantRoleCommand(
                Workspace,
                "Support agent",
                ["ai-agents.view", "ai-agents.preview"]));

        Assert.Equal("Support agent", created.Value.Name);
        Assert.Equal(TenantRoleKind.Custom, created.Value.Kind);
        Assert.False(created.Value.IsSystem);
        Assert.Equal(
            ["ai-agents.preview", "ai-agents.view"],
            created.Value.Permissions.Select(code => code.Value));
    }

    [Fact]
    public async Task A_created_role_belongs_to_the_workspace_in_the_route()
    {
        RecordingRoleRepository roles = new();

        Versioned<TenantRoleView> created = await Creator(roles).HandleAsync(
            new CreateTenantRoleCommand(Workspace, "Support agent", null));

        // The command has no tenant field a caller could disagree with, so this
        // is really an assertion that the route's tenant is the one used.
        Assert.Equal(Workspace, created.Value.TenantId);
    }

    [Fact]
    public async Task A_created_role_carries_the_version_it_was_stored_at()
    {
        // So a client can edit it immediately without reading it back.
        RecordingRoleRepository roles = new();

        Versioned<TenantRoleView> created = await Creator(roles).HandleAsync(
            new CreateTenantRoleCommand(Workspace, "Support agent", null));

        Assert.False(created.Version.IsEmpty);
    }

    [Fact]
    public async Task A_role_may_be_created_granting_nothing()
    {
        RecordingRoleRepository roles = new();

        Versioned<TenantRoleView> created = await Creator(roles).HandleAsync(
            new CreateTenantRoleCommand(Workspace, "Placeholder", null));

        Assert.Empty(created.Value.Permissions);
    }

    [Fact]
    public async Task Duplicate_permission_codes_are_deduplicated()
    {
        // The documented contract, asserted at the boundary a client actually
        // reaches rather than only on the aggregate.
        RecordingRoleRepository roles = new();

        Versioned<TenantRoleView> created = await Creator(roles).HandleAsync(
            new CreateTenantRoleCommand(
                Workspace,
                "Support agent",
                ["roles.view", "roles.view", "ROLES.VIEW"]));

        Assert.Equal(["roles.view"], created.Value.Permissions.Select(code => code.Value));
    }

    [Fact]
    public async Task An_unrecognised_permission_code_is_refused_and_nothing_is_stored()
    {
        RecordingRoleRepository roles = new();

        await Assert.ThrowsAsync<DomainValidationException>(() => Creator(roles).HandleAsync(
            new CreateTenantRoleCommand(Workspace, "Invented", ["integrations.manage"])));

        Assert.Empty(roles.Added);
    }

    [Fact]
    public async Task A_malformed_permission_code_is_refused_the_same_way()
    {
        // Malformed and unrecognised produce the same failure and therefore the
        // same status, so a caller cannot enumerate the catalogue by probing.
        RecordingRoleRepository roles = new();

        await Assert.ThrowsAsync<DomainValidationException>(() => Creator(roles).HandleAsync(
            new CreateTenantRoleCommand(Workspace, "Invented", ["not a code"])));

        Assert.Empty(roles.Added);
    }

    [Fact]
    public async Task A_name_already_taken_in_this_workspace_is_a_conflict()
    {
        RecordingRoleRepository roles = new();
        roles.AddRole(Custom("Support agent"));

        await Assert.ThrowsAsync<TenantRoleNameAlreadyExistsException>(
            () => Creator(roles).HandleAsync(
                new CreateTenantRoleCommand(Workspace, "Support agent", null)));
    }

    [Fact]
    public async Task A_name_differing_only_in_case_is_the_same_name()
    {
        RecordingRoleRepository roles = new();
        roles.AddRole(Custom("Support agent"));

        await Assert.ThrowsAsync<TenantRoleNameAlreadyExistsException>(
            () => Creator(roles).HandleAsync(
                new CreateTenantRoleCommand(Workspace, "SUPPORT AGENT", null)));
    }

    [Fact]
    public async Task Creating_a_role_requires_roles_manage()
    {
        RecordingRoleRepository roles = new();

        CreateTenantRoleHandler handler = new(
            roles,
            TestAuthority.With(Alice, Workspace, PlatformPermissions.RolesView),
            new FixedTimeProvider(Now));

        await Assert.ThrowsAsync<PermissionDeniedException>(() => handler.HandleAsync(
            new CreateTenantRoleCommand(Workspace, "Support agent", null)));

        Assert.Empty(roles.Added);
    }

    [Fact]
    public async Task A_non_member_creating_a_role_is_told_the_workspace_is_not_there()
    {
        RecordingRoleRepository roles = new();

        CreateTenantRoleHandler handler = new(
            roles,
            TestAuthority.Outsider(Alice),
            new FixedTimeProvider(Now));

        await Assert.ThrowsAsync<TenantNotFoundException>(() => handler.HandleAsync(
            new CreateTenantRoleCommand(Workspace, "Support agent", null)));
    }

    [Fact]
    public async Task Authorisation_is_resolved_before_the_request_is_validated()
    {
        // Unlike the agent slice, where validation runs first. The difference is
        // deliberate: an agent's name rules are already public, but the
        // permission catalogue is published only to callers with roles.view.
        // Validating a permission list first would let a member without
        // roles.manage learn which codes this platform recognises by watching
        // which ones came back invalid.
        RecordingRoleRepository roles = new();

        CreateTenantRoleHandler handler = new(
            roles,
            TestAuthority.With(Alice, Workspace),
            new FixedTimeProvider(Now));

        await Assert.ThrowsAsync<PermissionDeniedException>(() => handler.HandleAsync(
            new CreateTenantRoleCommand(Workspace, "", ["integrations.manage"])));
    }

    [Fact]
    public async Task Creation_stamps_the_injected_clock()
    {
        RecordingRoleRepository roles = new();

        Versioned<TenantRoleView> created = await Creator(roles).HandleAsync(
            new CreateTenantRoleCommand(Workspace, "Support agent", null));

        Assert.Equal(Now, created.Value.CreatedAt);
        Assert.Equal(Now, created.Value.UpdatedAt);
    }

    // ---------------------------------------------------------------- rename

    [Fact]
    public async Task A_custom_role_is_renamed_when_the_version_matches()
    {
        RecordingRoleRepository roles = new();
        TenantRole role = roles.AddRole(Custom("Support agent"));

        Versioned<TenantRoleView>? renamed = await Renamer(roles).HandleAsync(
            new RenameTenantRoleCommand(
                Workspace,
                role.Id.Value,
                "Support supervisor",
                roles.Version));

        Assert.NotNull(renamed);
        Assert.Equal("Support supervisor", renamed.Value.Name);
        Assert.Equal(Later, renamed.Value.UpdatedAt);
    }

    [Fact]
    public async Task A_stale_version_refuses_the_rename()
    {
        RecordingRoleRepository roles = new();
        TenantRole role = roles.AddRole(Custom("Support agent"));

        await Assert.ThrowsAsync<TenantRoleVersionMismatchException>(
            () => Renamer(roles).HandleAsync(new RenameTenantRoleCommand(
                Workspace,
                role.Id.Value,
                "Support supervisor",
                ConcurrencyToken.From("stale"))));

        Assert.Equal("Support agent", role.Name.Value);
    }

    [Fact]
    public async Task The_owner_role_cannot_be_renamed()
    {
        RecordingRoleRepository roles = new();
        TenantRole owner = roles.AddRole(TenantRole.CreateOwner(TenantId.From(Workspace), Now));

        await Assert.ThrowsAsync<DomainStateException>(
            () => Renamer(roles).HandleAsync(new RenameTenantRoleCommand(
                Workspace,
                owner.Id.Value,
                "Superuser",
                roles.Version)));
    }

    [Fact]
    public async Task A_stale_version_is_reported_before_the_owner_protection()
    {
        // The caller's precondition genuinely failed. Answering the protection
        // rule first would describe a role they have never seen -- and would
        // confirm, to somebody holding an out-of-date version, exactly which role
        // they had hold of.
        RecordingRoleRepository roles = new();
        TenantRole owner = roles.AddRole(TenantRole.CreateOwner(TenantId.From(Workspace), Now));

        await Assert.ThrowsAsync<TenantRoleVersionMismatchException>(
            () => Renamer(roles).HandleAsync(new RenameTenantRoleCommand(
                Workspace,
                owner.Id.Value,
                "Superuser",
                ConcurrencyToken.From("stale"))));
    }

    [Fact]
    public async Task Renaming_a_role_from_another_workspace_finds_nothing()
    {
        RecordingRoleRepository roles = new();

        Versioned<TenantRoleView>? renamed = await Renamer(roles).HandleAsync(
            new RenameTenantRoleCommand(
                Workspace,
                Guid.CreateVersion7(),
                "Support supervisor",
                roles.Version));

        Assert.Null(renamed);
    }

    [Fact]
    public async Task Renaming_requires_roles_manage()
    {
        RecordingRoleRepository roles = new();
        TenantRole role = roles.AddRole(Custom("Support agent"));

        RenameTenantRoleHandler handler = new(
            roles,
            TestAuthority.With(Alice, Workspace, PlatformPermissions.RolesView),
            new FixedTimeProvider(Later));

        await Assert.ThrowsAsync<PermissionDeniedException>(() => handler.HandleAsync(
            new RenameTenantRoleCommand(Workspace, role.Id.Value, "Renamed", roles.Version)));
    }

    // ---------------------------------------------------------- permissions

    [Fact]
    public async Task A_permission_set_is_replaced_whole()
    {
        RecordingRoleRepository roles = new();
        TenantRole role = roles.AddRole(
            Custom("Support agent", PlatformPermissions.AiAgentsView, PlatformPermissions.AiAgentsEdit));

        Versioned<TenantRoleView>? updated = await Replacer(roles).HandleAsync(
            new ReplaceTenantRolePermissionsCommand(
                Workspace,
                role.Id.Value,
                ["roles.view"],
                roles.Version));

        Assert.NotNull(updated);
        Assert.Equal(["roles.view"], updated.Value.Permissions.Select(code => code.Value));
    }

    [Fact]
    public async Task An_empty_permission_set_removes_everything()
    {
        RecordingRoleRepository roles = new();
        TenantRole role = roles.AddRole(Custom("Support agent", PlatformPermissions.AiAgentsView));

        Versioned<TenantRoleView>? updated = await Replacer(roles).HandleAsync(
            new ReplaceTenantRolePermissionsCommand(
                Workspace,
                role.Id.Value,
                [],
                roles.Version));

        Assert.NotNull(updated);
        Assert.Empty(updated.Value.Permissions);
    }

    [Fact]
    public async Task An_unrecognised_code_leaves_the_previous_set_intact()
    {
        // No partial replacement. The whole set is validated before a single
        // grant is removed, so a rejected request cannot leave a role granting
        // nothing.
        RecordingRoleRepository roles = new();
        TenantRole role = roles.AddRole(Custom("Support agent", PlatformPermissions.AiAgentsView));

        await Assert.ThrowsAsync<DomainValidationException>(
            () => Replacer(roles).HandleAsync(new ReplaceTenantRolePermissionsCommand(
                Workspace,
                role.Id.Value,
                ["roles.view", "integrations.manage"],
                roles.Version)));

        Assert.Equal([PlatformPermissions.AiAgentsView], role.EffectivePermissions);
    }

    [Fact]
    public async Task The_owner_permissions_cannot_be_replaced()
    {
        RecordingRoleRepository roles = new();
        TenantRole owner = roles.AddRole(TenantRole.CreateOwner(TenantId.From(Workspace), Now));

        await Assert.ThrowsAsync<DomainStateException>(
            () => Replacer(roles).HandleAsync(new ReplaceTenantRolePermissionsCommand(
                Workspace,
                owner.Id.Value,
                ["roles.view"],
                roles.Version)));

        Assert.Equal(PlatformPermissions.AllCodes, owner.EffectivePermissions);
    }

    [Fact]
    public async Task A_stale_version_refuses_the_replacement()
    {
        RecordingRoleRepository roles = new();
        TenantRole role = roles.AddRole(Custom("Support agent", PlatformPermissions.AiAgentsView));

        await Assert.ThrowsAsync<TenantRoleVersionMismatchException>(
            () => Replacer(roles).HandleAsync(new ReplaceTenantRolePermissionsCommand(
                Workspace,
                role.Id.Value,
                ["roles.view"],
                ConcurrencyToken.From("stale"))));

        Assert.Equal([PlatformPermissions.AiAgentsView], role.EffectivePermissions);
    }

    [Fact]
    public async Task Replacing_permissions_requires_roles_manage()
    {
        RecordingRoleRepository roles = new();
        TenantRole role = roles.AddRole(Custom("Support agent"));

        ReplaceTenantRolePermissionsHandler handler = new(
            roles,
            TestAuthority.With(Alice, Workspace, PlatformPermissions.RolesView),
            new FixedTimeProvider(Later));

        await Assert.ThrowsAsync<PermissionDeniedException>(() => handler.HandleAsync(
            new ReplaceTenantRolePermissionsCommand(
                Workspace,
                role.Id.Value,
                ["roles.view"],
                roles.Version)));
    }

    // ------------------------------------------------------------ read side

    [Fact]
    public async Task The_role_list_puts_owner_first_then_custom_roles_by_name()
    {
        RecordingRoleRepository roles = new();
        roles.AddRole(Custom("Support agent"));
        roles.AddRole(Custom("Analyst"));
        roles.AddRole(TenantRole.CreateOwner(TenantId.From(Workspace), Now));

        ListTenantRolesHandler handler = new(roles, TestAuthority.Owner(Alice, Workspace));

        IReadOnlyList<TenantRoleSummary> listed =
            await handler.HandleAsync(new ListTenantRolesQuery(Workspace));

        Assert.Equal(["Owner", "Analyst", "Support agent"], listed.Select(role => role.Name));
    }

    [Fact]
    public async Task The_owner_row_reports_the_whole_catalogue_as_its_permission_count()
    {
        // Counting stored rows would report zero -- true of the table and false
        // about the role, and the most misleading thing this list could say.
        RecordingRoleRepository roles = new();
        roles.AddRole(TenantRole.CreateOwner(TenantId.From(Workspace), Now));

        ListTenantRolesHandler handler = new(roles, TestAuthority.Owner(Alice, Workspace));

        TenantRoleSummary owner =
            Assert.Single(await handler.HandleAsync(new ListTenantRolesQuery(Workspace)));

        Assert.Equal(PlatformPermissions.AllCodes.Count, owner.PermissionCount);
    }

    [Fact]
    public async Task Listing_roles_requires_roles_view()
    {
        RecordingRoleRepository roles = new();

        ListTenantRolesHandler handler = new(roles, TestAuthority.With(Alice, Workspace));

        await Assert.ThrowsAsync<PermissionDeniedException>(
            () => handler.HandleAsync(new ListTenantRolesQuery(Workspace)));
    }

    [Fact]
    public async Task A_non_member_listing_roles_is_told_the_workspace_is_not_there()
    {
        ListTenantRolesHandler handler =
            new(new RecordingRoleRepository(), TestAuthority.Outsider(Alice));

        await Assert.ThrowsAsync<TenantNotFoundException>(
            () => handler.HandleAsync(new ListTenantRolesQuery(Workspace)));
    }

    [Fact]
    public async Task A_role_is_read_back_with_its_permissions_and_version()
    {
        RecordingRoleRepository roles = new();
        TenantRole role = roles.AddRole(Custom("Support agent", PlatformPermissions.AiAgentsView));

        GetTenantRoleByIdHandler handler = new(roles, TestAuthority.Owner(Alice, Workspace));

        Versioned<TenantRoleDetailView>? found =
            await handler.HandleAsync(new GetTenantRoleByIdQuery(Workspace, role.Id.Value));

        Assert.NotNull(found);
        Assert.Equal("Support agent", found.Value.Role.Name);
        Assert.Equal([PlatformPermissions.AiAgentsView], found.Value.Role.Permissions);
        Assert.Equal(roles.Version, found.Version);
        Assert.Equal(0, found.Value.MemberCount);
    }

    [Fact]
    public async Task A_role_read_says_how_many_members_hold_it()
    {
        // Phase 37.5b, row 23: counted from the assignments of this workspace only.
        RecordingRoleRepository roles = new();
        TenantRole role = roles.AddRole(Custom("Support agent", PlatformPermissions.AiAgentsView));
        roles.Assign(UserId.From(Guid.CreateVersion7()), role);
        roles.Assign(UserId.From(Guid.CreateVersion7()), role);

        GetTenantRoleByIdHandler handler = new(roles, TestAuthority.Owner(Alice, Workspace));

        Versioned<TenantRoleDetailView>? found =
            await handler.HandleAsync(new GetTenantRoleByIdQuery(Workspace, role.Id.Value));
        IReadOnlyList<TenantRoleSummary> listed = await new ListTenantRolesHandler(roles, TestAuthority.Owner(Alice, Workspace))
            .HandleAsync(new ListTenantRolesQuery(Workspace));

        Assert.Equal(2, found!.Value.MemberCount);
        Assert.Equal(2, Assert.Single(listed, summary => summary.Id == role.Id.Value).MemberCount);
    }

    [Fact]
    public async Task Reading_the_owner_returns_the_whole_catalogue()
    {
        RecordingRoleRepository roles = new();
        TenantRole owner = roles.AddRole(TenantRole.CreateOwner(TenantId.From(Workspace), Now));

        GetTenantRoleByIdHandler handler = new(roles, TestAuthority.Owner(Alice, Workspace));

        Versioned<TenantRoleDetailView>? found =
            await handler.HandleAsync(new GetTenantRoleByIdQuery(Workspace, owner.Id.Value));

        Assert.NotNull(found);
        Assert.True(found.Value.Role.IsSystem);
        Assert.Equal(PlatformPermissions.AllCodes, found.Value.Role.Permissions);
    }

    [Fact]
    public async Task Reading_a_role_requires_roles_view_and_is_refused_before_the_lookup()
    {
        // The refusal is identical whether the id is real or invented, because
        // nothing is looked up before it. Checking the permission after the
        // lookup would turn a permission somebody lacks into a way to enumerate
        // the role ids they cannot see.
        RecordingRoleRepository roles = new();
        TenantRole real = roles.AddRole(Custom("Support agent"));

        GetTenantRoleByIdHandler handler = new(roles, TestAuthority.With(Alice, Workspace));

        await Assert.ThrowsAsync<PermissionDeniedException>(
            () => handler.HandleAsync(new GetTenantRoleByIdQuery(Workspace, real.Id.Value)));

        await Assert.ThrowsAsync<PermissionDeniedException>(
            () => handler.HandleAsync(
                new GetTenantRoleByIdQuery(Workspace, Guid.CreateVersion7())));
    }

    [Fact]
    public async Task A_non_member_reading_a_role_finds_nothing()
    {
        RecordingRoleRepository roles = new();
        TenantRole real = roles.AddRole(Custom("Support agent"));

        GetTenantRoleByIdHandler handler = new(roles, TestAuthority.Outsider(Alice));

        Assert.Null(await handler.HandleAsync(
            new GetTenantRoleByIdQuery(Workspace, real.Id.Value)));
    }

    // -------------------------------------------------------------- catalog

    [Fact]
    public async Task The_permission_catalogue_is_published_to_a_caller_with_roles_view()
    {
        GetPermissionCatalogHandler handler = new(
            TestAuthority.With(Alice, Workspace, PlatformPermissions.RolesView));

        IReadOnlyList<PermissionDefinition>? catalogue =
            await handler.HandleAsync(new GetPermissionCatalogQuery(Workspace));

        Assert.Equal(PlatformPermissions.All, catalogue);
    }

    [Fact]
    public async Task The_permission_catalogue_requires_roles_view()
    {
        GetPermissionCatalogHandler handler = new(TestAuthority.With(Alice, Workspace));

        await Assert.ThrowsAsync<PermissionDeniedException>(
            () => handler.HandleAsync(new GetPermissionCatalogQuery(Workspace)));
    }

    [Fact]
    public async Task A_non_member_gets_nothing_from_the_catalogue()
    {
        GetPermissionCatalogHandler handler = new(TestAuthority.Outsider(Alice));

        Assert.Null(await handler.HandleAsync(new GetPermissionCatalogQuery(Workspace)));
    }

    // ----------------------------------------------------- my own permissions

    [Fact]
    public async Task A_member_can_read_their_own_permissions_without_any_permission()
    {
        // Requiring one would be circular: a member with nothing granted could
        // not be told they have nothing granted, and the admin application could
        // not explain why it is showing them nothing.
        GetMyTenantPermissionsHandler handler = new(TestAuthority.With(Alice, Workspace));

        Assert.Empty((await handler.HandleAsync(new GetMyTenantPermissionsQuery(Workspace)))!);
    }

    [Fact]
    public async Task An_owner_reads_the_whole_catalogue_as_their_permissions()
    {
        GetMyTenantPermissionsHandler handler = new(TestAuthority.Owner(Alice, Workspace));

        Assert.Equal(
            PlatformPermissions.AllCodes,
            await handler.HandleAsync(new GetMyTenantPermissionsQuery(Workspace)));
    }

    [Fact]
    public async Task A_non_member_reading_their_permissions_gets_nothing()
    {
        GetMyTenantPermissionsHandler handler = new(TestAuthority.Outsider(Alice));

        Assert.Null(await handler.HandleAsync(new GetMyTenantPermissionsQuery(Workspace)));
    }

    // ------------------------------------------------------------- plumbing

    [Fact]
    public async Task Anonymous_callers_are_refused_by_every_role_use_case()
    {
        RecordingRoleRepository roles = new();
        TenantAuthorizer nobody = TestAuthority.Anonymous();

        await Assert.ThrowsAsync<NotAuthenticatedException>(
            () => new ListTenantRolesHandler(roles, nobody)
                .HandleAsync(new ListTenantRolesQuery(Workspace)));

        await Assert.ThrowsAsync<NotAuthenticatedException>(
            () => new GetTenantRoleByIdHandler(roles, nobody)
                .HandleAsync(new GetTenantRoleByIdQuery(Workspace, Guid.CreateVersion7())));

        await Assert.ThrowsAsync<NotAuthenticatedException>(
            () => new CreateTenantRoleHandler(roles, nobody, new FixedTimeProvider(Now))
                .HandleAsync(new CreateTenantRoleCommand(Workspace, "Support agent", null)));

        await Assert.ThrowsAsync<NotAuthenticatedException>(
            () => new GetPermissionCatalogHandler(nobody)
                .HandleAsync(new GetPermissionCatalogQuery(Workspace)));

        await Assert.ThrowsAsync<NotAuthenticatedException>(
            () => new GetMyTenantPermissionsHandler(nobody)
                .HandleAsync(new GetMyTenantPermissionsQuery(Workspace)));
    }

    [Fact]
    public async Task A_null_command_or_query_is_a_programming_error()
    {
        RecordingRoleRepository roles = new();
        TenantAuthorizer owner = TestAuthority.Owner(Alice, Workspace);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => new ListTenantRolesHandler(roles, owner).HandleAsync(null!));

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => new GetTenantRoleByIdHandler(roles, owner).HandleAsync(null!));

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => Creator(roles).HandleAsync(null!));

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => Renamer(roles).HandleAsync(null!));

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => Replacer(roles).HandleAsync(null!));
    }

    [Fact]
    public void Every_role_handler_requires_its_dependencies()
    {
        RecordingRoleRepository roles = new();
        TenantAuthorizer owner = TestAuthority.Owner(Alice, Workspace);
        FixedTimeProvider clock = new(Now);

        Assert.Throws<ArgumentNullException>(() => new ListTenantRolesHandler(null!, owner));
        Assert.Throws<ArgumentNullException>(() => new ListTenantRolesHandler(roles, null!));
        Assert.Throws<ArgumentNullException>(() => new GetTenantRoleByIdHandler(null!, owner));
        Assert.Throws<ArgumentNullException>(() => new GetTenantRoleByIdHandler(roles, null!));
        Assert.Throws<ArgumentNullException>(() => new CreateTenantRoleHandler(null!, owner, clock));
        Assert.Throws<ArgumentNullException>(() => new CreateTenantRoleHandler(roles, null!, clock));
        Assert.Throws<ArgumentNullException>(() => new CreateTenantRoleHandler(roles, owner, null!));
        Assert.Throws<ArgumentNullException>(() => new RenameTenantRoleHandler(null!, owner, clock));
        Assert.Throws<ArgumentNullException>(() => new RenameTenantRoleHandler(roles, null!, clock));
        Assert.Throws<ArgumentNullException>(() => new RenameTenantRoleHandler(roles, owner, null!));
        Assert.Throws<ArgumentNullException>(
            () => new ReplaceTenantRolePermissionsHandler(null!, owner, clock));
        Assert.Throws<ArgumentNullException>(
            () => new ReplaceTenantRolePermissionsHandler(roles, null!, clock));
        Assert.Throws<ArgumentNullException>(
            () => new ReplaceTenantRolePermissionsHandler(roles, owner, null!));
        Assert.Throws<ArgumentNullException>(() => new GetPermissionCatalogHandler(null!));
        Assert.Throws<ArgumentNullException>(() => new GetMyTenantPermissionsHandler(null!));
    }

    private static TenantRole Custom(string name, params PermissionCode[] permissions) =>
        TenantRole.CreateCustom(
            TenantId.From(Workspace),
            TenantRoleName.Create(name),
            permissions,
            Now);

    private static CreateTenantRoleHandler Creator(RecordingRoleRepository roles) =>
        new(roles, TestAuthority.Owner(Alice, Workspace), new FixedTimeProvider(Now));

    private static RenameTenantRoleHandler Renamer(RecordingRoleRepository roles) =>
        new(roles, TestAuthority.Owner(Alice, Workspace), new FixedTimeProvider(Later));

    private static ReplaceTenantRolePermissionsHandler Replacer(RecordingRoleRepository roles) =>
        new(roles, TestAuthority.Owner(Alice, Workspace), new FixedTimeProvider(Later));
}
