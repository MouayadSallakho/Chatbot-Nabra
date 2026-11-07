using OmniAI.Application.Authorization;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Tenants;
using OmniAI.Application.Tenants.ChangeTimeZone;
using OmniAI.Application.Tests.Security;
using OmniAI.Application.Tests.WebChat;
using OmniAI.Domain;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Application.Tests.Tenants;

/// <summary>Phase 37.6, row 34: setting the workspace time zone.</summary>
public sealed class ChangeTenantTimeZoneHandlerTests
{
    private static readonly UserId Owner = UserId.New();
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_change_stores_the_zone_and_answers_a_new_version()
    {
        (InMemoryTenantRepository tenants, Tenant tenant) = Seeded();
        ConcurrencyToken before = tenants.VersionOf(tenant.Id);
        FixedWorkspaceTimeZones zones = new();

        Versioned<TenantView>? changed = await Handler(tenants, TestAuthority.Owner(Owner, tenant.Id.Value), zones)
            .HandleAsync(new ChangeTenantTimeZoneCommand(tenant.Id.Value, " Asia/Riyadh ", before));

        Assert.NotNull(changed);
        Assert.Equal("Asia/Riyadh", changed.Value.TimeZone);
        Assert.Equal("Asia/Riyadh", tenant.TimeZone.Name);
        Assert.NotEqual(before, changed.Version);
        Assert.Equal(1, tenants.TimeZoneChanges);
        Assert.Equal(1, zones.DatabaseQuestions);
    }

    [Fact]
    public async Task Choosing_the_current_zone_writes_nothing()
    {
        (InMemoryTenantRepository tenants, Tenant tenant) = Seeded();
        ConcurrencyToken version = tenants.VersionOf(tenant.Id);

        Versioned<TenantView>? unchanged = await Handler(tenants, TestAuthority.Owner(Owner, tenant.Id.Value))
            .HandleAsync(new ChangeTenantTimeZoneCommand(tenant.Id.Value, "UTC", version));

        Assert.Equal(version, unchanged!.Version);
        Assert.Equal("UTC", unchanged.Value.TimeZone);
        Assert.Equal(0, tenants.TimeZoneChanges);
    }

    [Fact]
    public async Task A_stale_version_is_refused_before_anything_changes()
    {
        (InMemoryTenantRepository tenants, Tenant tenant) = Seeded();
        ConcurrencyToken stale = tenants.VersionOf(tenant.Id);
        tenants.Touch(tenant.Id);

        await Assert.ThrowsAsync<TenantVersionMismatchException>(() =>
            Handler(tenants, TestAuthority.Owner(Owner, tenant.Id.Value))
                .HandleAsync(new ChangeTenantTimeZoneCommand(tenant.Id.Value, "Asia/Riyadh", stale)));

        Assert.Equal(WorkspaceTimeZone.Utc, tenant.TimeZone);
        Assert.Equal(0, tenants.TimeZoneChanges);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("asia/riyadh")]
    [InlineData("+03:00")]
    [InlineData("Mars/Olympus_Mons")]
    public async Task A_name_the_runtime_refuses_carries_its_code_and_is_checked_first(string? name)
    {
        (InMemoryTenantRepository tenants, Tenant tenant) = Seeded();
        FixedWorkspaceTimeZones zones = new();

        // An outsider: refused before authorisation, the same answer for anybody.
        DomainValidationException refused = await Assert.ThrowsAsync<DomainValidationException>(
            () => Handler(tenants, TestAuthority.Outsider(Owner), zones)
                .HandleAsync(new ChangeTenantTimeZoneCommand(tenant.Id.Value, name, tenants.VersionOf(tenant.Id))));

        Assert.Equal("workspace.time_zone.invalid", refused.Code);
        Assert.Equal(0, zones.DatabaseQuestions);
    }

    [Fact]
    public async Task A_name_postgresql_does_not_know_is_refused_with_the_same_code()
    {
        (InMemoryTenantRepository tenants, Tenant tenant) = Seeded();

        DomainValidationException refused = await Assert.ThrowsAsync<DomainValidationException>(
            () => Handler(
                    tenants,
                    TestAuthority.Owner(Owner, tenant.Id.Value),
                    new FixedWorkspaceTimeZones(databaseKnows: false))
                .HandleAsync(new ChangeTenantTimeZoneCommand(tenant.Id.Value, "Asia/Riyadh", tenants.VersionOf(tenant.Id))));

        Assert.Equal("workspace.time_zone.invalid", refused.Code);
        Assert.Equal(WorkspaceTimeZone.Utc, tenant.TimeZone);
    }

    [Fact]
    public async Task A_member_without_workspace_manage_is_refused_and_an_outsider_finds_nothing()
    {
        (InMemoryTenantRepository tenants, Tenant tenant) = Seeded();
        ChangeTenantTimeZoneCommand command = new(tenant.Id.Value, "Asia/Riyadh", tenants.VersionOf(tenant.Id));
        FixedWorkspaceTimeZones zones = new();

        await Assert.ThrowsAsync<PermissionDeniedException>(() => Handler(
                tenants,
                TestAuthority.With(Owner, tenant.Id.Value, PlatformPermissions.WorkspaceView),
                zones)
            .HandleAsync(command));

        Assert.Null(await Handler(tenants, TestAuthority.Outsider(Owner), zones).HandleAsync(command));
        Assert.Equal(WorkspaceTimeZone.Utc, tenant.TimeZone);

        // Neither of them got as far as asking PostgreSQL.
        Assert.Equal(0, zones.DatabaseQuestions);
    }

    [Fact]
    public async Task A_custom_role_holding_workspace_manage_may_change_it()
    {
        (InMemoryTenantRepository tenants, Tenant tenant) = Seeded();

        Versioned<TenantView>? changed = await Handler(
                tenants,
                TestAuthority.With(Owner, tenant.Id.Value, PlatformPermissions.WorkspaceManage))
            .HandleAsync(new ChangeTenantTimeZoneCommand(tenant.Id.Value, "Europe/Berlin", tenants.VersionOf(tenant.Id)));

        Assert.Equal("Europe/Berlin", changed!.Value.TimeZone);
    }

    private static (InMemoryTenantRepository Tenants, Tenant Tenant) Seeded()
    {
        Tenant tenant = Tenant.Create(TenantName.Create("Acme Trading"), TenantSlug.Create("acme"));

        return (new InMemoryTenantRepository().Seed(tenant), tenant);
    }

    private static ChangeTenantTimeZoneHandler Handler(
        InMemoryTenantRepository tenants,
        TenantAuthorizer authorizer,
        FixedWorkspaceTimeZones? zones = null) =>
        new(tenants, zones ?? new FixedWorkspaceTimeZones(), authorizer, new FakeTimeProvider(Now));
}
