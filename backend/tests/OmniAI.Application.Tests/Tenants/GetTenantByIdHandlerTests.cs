using OmniAI.Application.Security;
using OmniAI.Application.Tenants;
using OmniAI.Application.Tenants.GetTenantById;
using OmniAI.Application.Tests.Security;
using OmniAI.Domain;
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
            TestAuthority.Outsider(Member));

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
            TestAuthority.Outsider(Member));

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
            TestAuthority.Anonymous());

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
            TestAuthority.Outsider(Member));

        await Assert.ThrowsAsync<DomainValidationException>(
            () => handler.HandleAsync(new GetTenantByIdQuery(Guid.Empty)));
    }

    [Fact]
    public async Task A_null_query_is_a_programming_error()
    {
        GetTenantByIdHandler handler = new(
            new InMemoryTenantRepository(),
            TestAuthority.Outsider(Member));

        await Assert.ThrowsAsync<ArgumentNullException>(() => handler.HandleAsync(null!));
    }

    [Fact]
    public void The_handler_requires_its_dependencies()
    {
        Assert.Throws<ArgumentNullException>(
            () => new GetTenantByIdHandler(null!, TestAuthority.Outsider(Member)));

        Assert.Throws<ArgumentNullException>(
            () => new GetTenantByIdHandler(new InMemoryTenantRepository(), null!));
    }

    /// <summary>A handler over a store holding one tenant, for a member of it.</summary>
    private static GetTenantByIdHandler HandlerFor(Tenant stored, Tenant memberOf) =>
        new(
            new InMemoryTenantRepository().Seed(stored),
            TestAuthority.Owner(Member, memberOf.Id.Value));
}
