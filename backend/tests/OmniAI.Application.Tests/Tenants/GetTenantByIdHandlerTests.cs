using OmniAI.Application.Authorization;
using OmniAI.Application.Security;
using OmniAI.Application.Tenants;
using OmniAI.Application.Tenants.GetTenantById;
using OmniAI.Application.Tests.Security;
using OmniAI.Application.Tests.WebChat;
using OmniAI.Domain;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Application.Tests.Tenants;

public sealed class GetTenantByIdHandlerTests
{
    private static readonly UserId Member = UserId.New();

    [Fact]
    public async Task An_existing_tenant_is_returned_to_a_member()
    {
        Tenant stored = Tenant.Create(
            TenantName.Create("Acme Corporation"),
            TenantSlug.Create("acme"));

        GetTenantByIdHandler handler = HandlerFor(stored, memberOf: stored);

        TenantView? found = (await handler.HandleAsync(new GetTenantByIdQuery(stored.Id.Value)))?.Value;

        Assert.NotNull(found);
        Assert.Equal(stored.Id.Value, found.Id);
        Assert.Equal("Acme Corporation", found.Name);
        Assert.Equal("acme", found.Slug);
        Assert.Equal(TenantStatus.Active, found.Status);
    }

    [Fact]
    public async Task A_suspended_tenant_reports_its_status()
    {
        Tenant stored = Tenant.Create(TenantName.Create("Acme"), TenantSlug.Create("acme"));
        stored.Suspend();

        GetTenantByIdHandler handler = HandlerFor(stored, memberOf: stored);

        TenantView? found = (await handler.HandleAsync(new GetTenantByIdQuery(stored.Id.Value)))?.Value;

        Assert.Equal(TenantStatus.Suspended, found?.Status);
    }

    [Fact]
    public async Task An_unknown_tenant_produces_a_not_found_result()
    {
        // Absence is a result, not an exception: no type to catch, no stack
        // unwind, and nothing that would be logged as a server fault.
        GetTenantByIdHandler handler = new(
            new InMemoryTenantRepository(),
            TestAuthority.Outsider(Member),
            TimeProvider.System);

        TenantView? found = (await handler.HandleAsync(new GetTenantByIdQuery(Guid.CreateVersion7())))?.Value;

        Assert.Null(found);
    }

    [Fact]
    public async Task A_tenant_the_caller_does_not_belong_to_is_reported_as_missing()
    {
        // The central rule of this phase. The tenant is really there, in the
        // store, and the caller is really signed in -- and they are told exactly
        // what they would be told about an id that was never issued. Anything
        // else would let somebody confirm which workspaces exist by watching how
        // the refusals differ.
        Tenant somebodyElses = Tenant.Create(
            TenantName.Create("Rival Trading"),
            TenantSlug.Create("rival"));

        GetTenantByIdHandler handler = new(
            new InMemoryTenantRepository().Seed(somebodyElses),
            TestAuthority.Outsider(Member),
            TimeProvider.System);

        TenantView? found =
            (await handler.HandleAsync(new GetTenantByIdQuery(somebodyElses.Id.Value)))?.Value;

        Assert.Null(found);
    }

    [Fact]
    public async Task An_anonymous_caller_is_refused_rather_than_told_it_is_missing()
    {
        // 401 and 404 answer different questions, and the difference matters to
        // the client: one says "sign in and ask again", the other says "there is
        // nothing here". The use case refuses on its own, without relying on an
        // endpoint having been mapped with an authorization requirement.
        Tenant stored = Tenant.Create(TenantName.Create("Acme"), TenantSlug.Create("acme"));

        GetTenantByIdHandler handler = new(
            new InMemoryTenantRepository().Seed(stored),
            TestAuthority.Anonymous(),
            TimeProvider.System);

        await Assert.ThrowsAsync<NotAuthenticatedException>(
            () => handler.HandleAsync(new GetTenantByIdQuery(stored.Id.Value)));
    }

    [Fact]
    public async Task Another_tenants_id_does_not_return_the_stored_tenant()
    {
        Tenant stored = Tenant.Create(TenantName.Create("Acme"), TenantSlug.Create("acme"));

        GetTenantByIdHandler handler = HandlerFor(stored, memberOf: stored);

        Assert.Null(await handler.HandleAsync(new GetTenantByIdQuery(Guid.CreateVersion7())));
    }

    [Fact]
    public async Task An_empty_id_is_rejected_as_invalid_rather_than_reported_as_missing()
    {
        // An empty GUID is malformed input, not a tenant that happens not to
        // exist, so it must not be answered with "not found".
        GetTenantByIdHandler handler = new(
            new InMemoryTenantRepository(),
            TestAuthority.Outsider(Member),
            TimeProvider.System);

        await Assert.ThrowsAsync<DomainValidationException>(
            () => handler.HandleAsync(new GetTenantByIdQuery(Guid.Empty)));
    }

    [Fact]
    public async Task A_null_query_is_a_programming_error()
    {
        GetTenantByIdHandler handler = new(
            new InMemoryTenantRepository(),
            TestAuthority.Outsider(Member),
            TimeProvider.System);

        await Assert.ThrowsAsync<ArgumentNullException>(() => handler.HandleAsync(null!));
    }

    [Fact]
    public void The_handler_requires_its_dependencies()
    {
        Assert.Throws<ArgumentNullException>(
            () => new GetTenantByIdHandler(null!, TestAuthority.Outsider(Member), TimeProvider.System));

        Assert.Throws<ArgumentNullException>(
            () => new GetTenantByIdHandler(new InMemoryTenantRepository(), null!, TimeProvider.System));

        Assert.Throws<ArgumentNullException>(
            () => new GetTenantByIdHandler(new InMemoryTenantRepository(), TestAuthority.Outsider(Member), null!));
    }

    // ---- Phase 38.7b, BA-9: the schedules a zone change would move ----------

    [Fact]
    public async Task A_manager_without_reports_view_is_told_how_many_schedules_a_zone_change_moves()
    {
        Tenant stored = Tenant.Create(TenantName.Create("Acme"), TenantSlug.Create("acme"));
        FakeTimeProvider clock = new(new DateTimeOffset(2026, 11, 13, 9, 0, 0, TimeSpan.Zero));
        InMemoryTenantRepository tenants = new InMemoryTenantRepository()
            .Seed(stored)
            .WithSchedulesToRezone(stored.Id, 3);

        GetTenantByIdHandler handler = new(
            tenants,
            TestAuthority.With(Member, stored.Id.Value, PlatformPermissions.WorkspaceView, PlatformPermissions.WorkspaceManage),
            clock);

        TenantView? found = (await handler.HandleAsync(new GetTenantByIdQuery(stored.Id.Value)))?.Value;

        Assert.Equal(3, found?.ScheduleCount);

        // Counted as of now, which is the instant a change would be made at.
        Assert.Equal(clock.GetUtcNow(), tenants.ScheduleCountAskedAt);
    }

    [Fact]
    public async Task A_manager_of_a_workspace_without_schedules_is_told_zero()
    {
        Tenant stored = Tenant.Create(TenantName.Create("Acme"), TenantSlug.Create("acme"));

        TenantView? found = (await HandlerFor(stored, memberOf: stored)
            .HandleAsync(new GetTenantByIdQuery(stored.Id.Value)))?.Value;

        Assert.Equal(0, found?.ScheduleCount);
    }

    [Fact]
    public async Task A_member_without_workspace_manage_learns_nothing_about_schedules()
    {
        Tenant stored = Tenant.Create(TenantName.Create("Acme"), TenantSlug.Create("acme"));
        InMemoryTenantRepository tenants = new InMemoryTenantRepository()
            .Seed(stored)
            .WithSchedulesToRezone(stored.Id, 3);

        GetTenantByIdHandler handler = new(
            tenants,
            TestAuthority.With(Member, stored.Id.Value, PlatformPermissions.WorkspaceView, PlatformPermissions.ReportsView),
            TimeProvider.System);

        TenantView? found = (await handler.HandleAsync(new GetTenantByIdQuery(stored.Id.Value)))?.Value;

        Assert.NotNull(found);
        Assert.Null(found.ScheduleCount);

        // Not even asked: the count is never computed for somebody who may not have it.
        Assert.Null(tenants.ScheduleCountAskedAt);
    }

    [Fact]
    public async Task A_member_without_workspace_view_is_still_refused_with_a_permission_error()
    {
        Tenant stored = Tenant.Create(TenantName.Create("Acme"), TenantSlug.Create("acme"));

        GetTenantByIdHandler handler = new(
            new InMemoryTenantRepository().Seed(stored),
            TestAuthority.With(Member, stored.Id.Value, PlatformPermissions.WorkspaceManage),
            TimeProvider.System);

        await Assert.ThrowsAsync<PermissionDeniedException>(
            () => handler.HandleAsync(new GetTenantByIdQuery(stored.Id.Value)));
    }

    [Fact]
    public async Task The_count_is_the_workspace_s_own()
    {
        Tenant mine = Tenant.Create(TenantName.Create("Acme"), TenantSlug.Create("acme"));
        Tenant theirs = Tenant.Create(TenantName.Create("Rival"), TenantSlug.Create("rival"));
        InMemoryTenantRepository tenants = new InMemoryTenantRepository()
            .Seed(mine)
            .Seed(theirs)
            .WithSchedulesToRezone(theirs.Id, 7);

        GetTenantByIdHandler handler = new(tenants, TestAuthority.Owner(Member, mine.Id.Value), TimeProvider.System);

        Assert.Equal(0, (await handler.HandleAsync(new GetTenantByIdQuery(mine.Id.Value)))?.Value.ScheduleCount);
        Assert.Null(await handler.HandleAsync(new GetTenantByIdQuery(theirs.Id.Value)));
    }

    /// <summary>A handler over a store holding one tenant, for a member of it.</summary>
    private static GetTenantByIdHandler HandlerFor(Tenant stored, Tenant memberOf) =>
        new(
            new InMemoryTenantRepository().Seed(stored),
            TestAuthority.Owner(Member, memberOf.Id.Value),
            TimeProvider.System);
}
