using OmniAI.Application.Authorization;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Tenants;
using OmniAI.Application.Tenants.RenameTenant;
using OmniAI.Application.Tests.Security;
using OmniAI.Application.Tests.WebChat;
using OmniAI.Domain;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Security;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Application.Tests.Tenants;

/// <summary>Phase 37.5a, row 20a: renaming a workspace (owner decision D-3).</summary>
public sealed class RenameTenantHandlerTests
{
    private static readonly UserId Owner = UserId.New();
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_rename_stores_the_new_name_and_one_audit_record_without_the_names()
    {
        (InMemoryTenantRepository tenants, Tenant tenant) = Seeded("Acme Trading");
        ConcurrencyToken before = tenants.VersionOf(tenant.Id);

        Versioned<TenantView>? renamed = await Handler(tenants, TestAuthority.Owner(Owner, tenant.Id.Value))
            .HandleAsync(new RenameTenantCommand(tenant.Id.Value, "  Acme Gulf  ", before));

        Assert.NotNull(renamed);
        Assert.Equal("Acme Gulf", renamed.Value.Name);
        Assert.NotEqual(before, renamed.Version);

        SecurityEvent audit = Assert.Single(tenants.Audits);
        Assert.Equal(SecurityEventKinds.WorkspaceRenamed, audit.Kind);
        Assert.Equal(Owner, audit.UserId);
        Assert.Equal(tenant.Id, audit.TenantId);
        Assert.Equal(Now, audit.OccurredAt);
        Assert.Null(audit.SubjectKey);
        Assert.Null(audit.ClientNetworkKey);
    }

    [Fact]
    public async Task Renaming_to_the_same_name_writes_nothing()
    {
        (InMemoryTenantRepository tenants, Tenant tenant) = Seeded("Acme Trading");
        ConcurrencyToken version = tenants.VersionOf(tenant.Id);

        Versioned<TenantView>? renamed = await Handler(tenants, TestAuthority.Owner(Owner, tenant.Id.Value))
            .HandleAsync(new RenameTenantCommand(tenant.Id.Value, "Acme Trading", version));

        Assert.Equal(version, renamed!.Version);
        Assert.Equal(0, tenants.RenameAttempts);
        Assert.Empty(tenants.Audits);
    }

    [Fact]
    public async Task A_stale_version_is_refused_before_anything_changes()
    {
        (InMemoryTenantRepository tenants, Tenant tenant) = Seeded("Acme Trading");
        ConcurrencyToken stale = tenants.VersionOf(tenant.Id);
        tenants.Touch(tenant.Id);

        await Assert.ThrowsAsync<TenantVersionMismatchException>(() => Handler(tenants, TestAuthority.Owner(Owner, tenant.Id.Value))
            .HandleAsync(new RenameTenantCommand(tenant.Id.Value, "Acme Gulf", stale)));

        Assert.Equal("Acme Trading", tenant.Name.Value);
        Assert.Empty(tenants.Audits);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Line\nbreak")]
    public async Task A_name_the_creation_rules_refuse_carries_its_code_and_is_checked_first(string? name)
    {
        (InMemoryTenantRepository tenants, Tenant tenant) = Seeded("Acme Trading");

        // An outsider: the name is refused before authorisation, the same answer
        // for anybody, so it says nothing about the workspace.
        DomainValidationException refused = await Assert.ThrowsAsync<DomainValidationException>(
            () => Handler(tenants, TestAuthority.Outsider(Owner))
                .HandleAsync(new RenameTenantCommand(tenant.Id.Value, name, tenants.VersionOf(tenant.Id))));

        Assert.Equal("workspace.name.invalid", refused.Code);
    }

    [Fact]
    public async Task A_member_without_workspace_manage_is_refused_and_an_outsider_finds_nothing()
    {
        (InMemoryTenantRepository tenants, Tenant tenant) = Seeded("Acme Trading");
        RenameTenantCommand command = new(tenant.Id.Value, "Taken", tenants.VersionOf(tenant.Id));

        await Assert.ThrowsAsync<PermissionDeniedException>(() => Handler(
                tenants,
                TestAuthority.With(Owner, tenant.Id.Value, PlatformPermissions.WorkspaceView))
            .HandleAsync(command));

        Assert.Null(await Handler(tenants, TestAuthority.Outsider(Owner)).HandleAsync(command));
        Assert.Equal("Acme Trading", tenant.Name.Value);
    }

    [Fact]
    public async Task A_custom_role_holding_workspace_manage_may_rename()
    {
        (InMemoryTenantRepository tenants, Tenant tenant) = Seeded("Acme Trading");

        Versioned<TenantView>? renamed = await Handler(
                tenants,
                TestAuthority.With(Owner, tenant.Id.Value, PlatformPermissions.WorkspaceManage))
            .HandleAsync(new RenameTenantCommand(tenant.Id.Value, "Managed", tenants.VersionOf(tenant.Id)));

        Assert.Equal("Managed", renamed!.Value.Name);
    }

    private static (InMemoryTenantRepository Tenants, Tenant Tenant) Seeded(string name)
    {
        Tenant tenant = Tenant.Create(TenantName.Create(name), TenantSlug.Create("acme"));

        return (new InMemoryTenantRepository().Seed(tenant), tenant);
    }

    private static RenameTenantHandler Handler(InMemoryTenantRepository tenants, TenantAuthorizer authorizer) =>
        new(tenants, authorizer, new FakeTimeProvider(Now));
}
