using OmniAI.Application.Authorization;
using OmniAI.Application.Authorization.CreateTenantRole;
using OmniAI.Application.Authorization.RenameTenantRole;
using OmniAI.Application.Authorization.ReplaceTenantRolePermissions;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Tests.Security;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;
using OmniAI.Testing;

namespace OmniAI.Application.Tests.Authorization;

/// <summary>
/// The delegation ceiling: an actor may only hand out authority they already
/// hold.
/// </summary>
/// <remarks>
/// <strong>Why this suite exists.</strong> <c>roles.manage</c> says somebody may
/// perform role administration. It must not mean they may manufacture
/// capabilities they do not possess. The two are easy to conflate, and the
/// conflation is invisible until you notice that a role can be assigned to the
/// person editing it: adding a permission to a role you hold grants it to
/// yourself immediately, with no assignment row changing anywhere.
/// <para>
/// Every test here builds the actor's authority from real <see cref="TenantRole"/>
/// aggregates in the same store the use case writes to, so an edit is visible to
/// the next authorisation question exactly as it is in production.
/// </para>
/// </remarks>
public sealed class RoleDelegationTests
{
    private static readonly UserId Bob = UserId.New();
    private static readonly Guid Workspace = Guid.CreateVersion7();
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 9, 0, 0, TimeSpan.Zero);

    // ------------------------------------------------- the escalation itself

    [Fact]
    public async Task Bob_cannot_add_a_capability_he_lacks_to_the_role_he_holds()
    {
        // The whole point. Role Manager is Bob's own role, so a permission added
        // to it is a permission added to Bob.
        (RecordingRoleRepository roles, TenantRole manager) = ManagerHeldByBob();

        ReplaceTenantRolePermissionsHandler replace = Replacer(roles);

        PermissionDeniedException denied =
            await Assert.ThrowsAsync<PermissionDeniedException>(() =>
                replace.HandleAsync(new ReplaceTenantRolePermissionsCommand(
                    Workspace,
                    manager.Id.Value,
                    ["roles.manage", "ai-agents.edit"],
                    roles.Version)));

        Assert.Equal(PlatformPermissions.AiAgentsEdit.Value, denied.RequiredPermission);
    }

    [Fact]
    public async Task A_refused_escalation_leaves_the_role_exactly_as_it_was()
    {
        // A response that says 403 while the row already changed would be worse
        // than no check at all.
        (RecordingRoleRepository roles, TenantRole manager) = ManagerHeldByBob();

        await Assert.ThrowsAsync<PermissionDeniedException>(() =>
            Replacer(roles).HandleAsync(new ReplaceTenantRolePermissionsCommand(
                Workspace,
                manager.Id.Value,
                ["roles.manage", "ai-agents.edit"],
                roles.Version)));

        Assert.Equal([PlatformPermissions.RolesManage], manager.EffectivePermissions);
    }

    [Fact]
    public async Task Bob_still_cannot_edit_an_agent_after_the_attempt()
    {
        // Re-evaluated through the authorizer, not by reading the role back. What
        // matters is Bob's effective authority, which is the thing the escalation
        // was reaching for.
        (RecordingRoleRepository roles, TenantRole manager) = ManagerHeldByBob();
        TenantAuthorizer authorizer = AuthorizerFor(roles);

        await Assert.ThrowsAsync<PermissionDeniedException>(() =>
            new ReplaceTenantRolePermissionsHandler(roles, authorizer, new FixedTimeProvider(Now))
                .HandleAsync(new ReplaceTenantRolePermissionsCommand(
                    Workspace,
                    manager.Id.Value,
                    ["roles.manage", "ai-agents.edit"],
                    roles.Version)));

        IReadOnlyList<PermissionCode>? effective =
            await authorizer.EffectivePermissionsAsync(TenantId.From(Workspace));

        Assert.Equal([PlatformPermissions.RolesManage], effective);

        await Assert.ThrowsAsync<PermissionDeniedException>(() =>
            authorizer.RequireAsync(TenantId.From(Workspace), PlatformPermissions.AiAgentsEdit));
    }

    // ------------------------------------------------------ create-then-hold

    [Fact]
    public async Task Bob_cannot_create_a_role_carrying_more_than_he_holds()
    {
        (RecordingRoleRepository roles, _) = ManagerHeldByBob();

        PermissionDeniedException denied =
            await Assert.ThrowsAsync<PermissionDeniedException>(() =>
                Creator(roles).HandleAsync(new CreateTenantRoleCommand(
                    Workspace,
                    "Escalation",
                    ["roles.manage", "ai-agents.lifecycle"])));

        Assert.Equal(PlatformPermissions.AiAgentsLifecycle.Value, denied.RequiredPermission);
    }

    [Fact]
    public async Task A_refused_creation_stores_no_role_at_all()
    {
        (RecordingRoleRepository roles, _) = ManagerHeldByBob();

        await Assert.ThrowsAsync<PermissionDeniedException>(() =>
            Creator(roles).HandleAsync(new CreateTenantRoleCommand(
                Workspace,
                "Escalation",
                ["ai-agents.lifecycle"])));

        Assert.Empty(roles.Added);
        Assert.DoesNotContain(
            await roles.ListAsync(TenantId.From(Workspace)),
            summary => summary.Name == "Escalation");
    }

    // ------------------------------------------------ higher-authority roles

    [Fact]
    public async Task Bob_cannot_touch_a_role_that_already_outranks_him()
    {
        // Not about what he is asking for -- about what the target already
        // carries. Being able to edit it at all would mean taking it over.
        (RecordingRoleRepository roles, _) = ManagerHeldByBob(PlatformPermissions.AiAgentsView);

        TenantRole senior = roles.AddRole(TenantRole.CreateCustom(
            TenantId.From(Workspace),
            TenantRoleName.Create("Senior"),
            [PlatformPermissions.AiAgentsEdit, PlatformPermissions.AiAgentsLifecycle],
            Now));

        await Assert.ThrowsAsync<PermissionDeniedException>(() =>
            Replacer(roles).HandleAsync(new ReplaceTenantRolePermissionsCommand(
                Workspace,
                senior.Id.Value,
                ["ai-agents.view"],
                roles.Version)));

        await Assert.ThrowsAsync<PermissionDeniedException>(() =>
            Renamer(roles).HandleAsync(new RenameTenantRoleCommand(
                Workspace,
                senior.Id.Value,
                "Renamed",
                roles.Version)));

        Assert.Equal("Senior", senior.Name.Value);
        Assert.Equal(
            [PlatformPermissions.AiAgentsEdit, PlatformPermissions.AiAgentsLifecycle],
            senior.EffectivePermissions);
    }

    // -------------------------------------------------------- the union rule

    [Fact]
    public async Task Authority_is_the_union_of_every_role_the_actor_holds()
    {
        // Bob's roles.manage and his ai-agents.edit live in different roles.
        // Reading only the role that carries roles.manage would refuse this, and
        // would be wrong: he genuinely holds both.
        RecordingRoleRepository roles = new();

        TenantRole managing = roles.AddRole(Custom("Role manager", PlatformPermissions.RolesManage));
        TenantRole editing = roles.AddRole(Custom("Agent editor", PlatformPermissions.AiAgentsEdit));

        roles.Assign(Bob, managing);
        roles.Assign(Bob, editing);

        Versioned<TenantRoleView> created = await Creator(roles).HandleAsync(
            new CreateTenantRoleCommand(
                Workspace,
                "Deputy",
                ["roles.manage", "ai-agents.edit"]));

        Assert.Equal(
            [PlatformPermissions.AiAgentsEdit, PlatformPermissions.RolesManage],
            created.Value.Permissions);
    }

    // ---------------------------------------------------- legitimate use

    [Fact]
    public async Task Bob_may_delegate_within_what_he_holds()
    {
        // The rule is a ceiling, not an Owner-only restriction. A delegated
        // administrator must still be able to do the job.
        (RecordingRoleRepository roles, _) = ManagerHeldByBob(
            PlatformPermissions.AiAgentsView,
            PlatformPermissions.AiAgentsPreview);

        Versioned<TenantRoleView> created = await Creator(roles).HandleAsync(
            new CreateTenantRoleCommand(
                Workspace,
                "Support",
                ["ai-agents.view", "ai-agents.preview"]));

        // A role publishes its permissions sorted, not in catalogue order.
        Assert.Equal(
            [PlatformPermissions.AiAgentsPreview, PlatformPermissions.AiAgentsView],
            created.Value.Permissions);

        Versioned<TenantRoleView>? narrowed = await Replacer(roles).HandleAsync(
            new ReplaceTenantRolePermissionsCommand(
                Workspace,
                created.Value.Id,
                ["ai-agents.view"],
                roles.Version));

        Assert.NotNull(narrowed);
        Assert.Equal([PlatformPermissions.AiAgentsView], narrowed.Value.Permissions);
    }

    [Fact]
    public async Task A_role_granting_nothing_is_within_everybody_s_authority()
    {
        // The empty set is a subset of every set, including an actor who holds
        // only roles.manage. Refusing it would be an arithmetic accident.
        (RecordingRoleRepository roles, _) = ManagerHeldByBob();

        Versioned<TenantRoleView> created = await Creator(roles).HandleAsync(
            new CreateTenantRoleCommand(Workspace, "Placeholder", []));

        Assert.Empty(created.Value.Permissions);
    }

    // ----------------------------------------------------------------- Owner

    [Fact]
    public async Task An_owner_may_delegate_the_entire_catalogue()
    {
        // Owner needs no special case: their effective permissions are the whole
        // catalogue, so the subset rule admits everything on its own.
        RecordingRoleRepository roles = new();

        TenantRole owner = roles.AddRole(
            TenantRole.CreateOwner(TenantId.From(Workspace), Now));

        roles.Assign(Bob, owner);

        Versioned<TenantRoleView> created = await Creator(roles).HandleAsync(
            new CreateTenantRoleCommand(
                Workspace,
                "Everything",
                [.. PlatformPermissions.AllCodes.Select(code => code.Value)]));

        Assert.Equal([.. PlatformPermissions.AllCodes.Order()], created.Value.Permissions);

        Versioned<TenantRoleView>? edited = await Replacer(roles).HandleAsync(
            new ReplaceTenantRolePermissionsCommand(
                Workspace,
                created.Value.Id,
                ["ai-agents.edit"],
                roles.Version));

        Assert.NotNull(edited);
        Assert.Equal([PlatformPermissions.AiAgentsEdit], edited.Value.Permissions);
    }

    [Fact]
    public async Task An_unknown_code_is_still_a_validation_failure_not_a_denial()
    {
        // The distinction the contract turns on: a capability this platform does
        // not have is a malformed request, and one it has but the actor cannot
        // hand out is a refusal. Collapsing them would either leak the catalogue
        // or misdescribe the problem.
        RecordingRoleRepository roles = new();

        TenantRole owner = roles.AddRole(
            TenantRole.CreateOwner(TenantId.From(Workspace), Now));

        roles.Assign(Bob, owner);

        await Assert.ThrowsAsync<Domain.DomainValidationException>(() =>
            Creator(roles).HandleAsync(new CreateTenantRoleCommand(
                Workspace,
                "Invented",
                ["integrations.manage"])));
    }

    // ------------------------------------------------------------- arranging

    /// <summary>Bob, holding one custom role with <c>roles.manage</c> and extras.</summary>
    private static (RecordingRoleRepository Roles, TenantRole Manager) ManagerHeldByBob(
        params PermissionCode[] extras)
    {
        RecordingRoleRepository roles = new();

        TenantRole manager = roles.AddRole(Custom(
            "Role manager",
            [PlatformPermissions.RolesManage, .. extras]));

        roles.Assign(Bob, manager);

        return (roles, manager);
    }

    private static TenantRole Custom(string name, params PermissionCode[] permissions) =>
        TenantRole.CreateCustom(
            TenantId.From(Workspace),
            TenantRoleName.Create(name),
            permissions,
            Now);

    private static TenantAuthorizer AuthorizerFor(RecordingRoleRepository roles) =>
        new(TestAccess.ForTenants(Bob, Workspace), roles);

    private static CreateTenantRoleHandler Creator(RecordingRoleRepository roles) =>
        new(roles, AuthorizerFor(roles), new FixedTimeProvider(Now));

    private static ReplaceTenantRolePermissionsHandler Replacer(RecordingRoleRepository roles) =>
        new(roles, AuthorizerFor(roles), new FixedTimeProvider(Now));

    private static RenameTenantRoleHandler Renamer(RecordingRoleRepository roles) =>
        new(roles, AuthorizerFor(roles), new FixedTimeProvider(Now));
}
