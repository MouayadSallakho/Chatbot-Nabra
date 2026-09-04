using OmniAI.Application.Tenants;
using OmniAI.Application.Tenants.GetTenantById;
using OmniAI.Domain;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Tests.Tenants;

public sealed class GetTenantByIdHandlerTests
{
    [Fact]
    public async Task An_existing_tenant_is_returned()
    {
        Tenant stored = Tenant.Create(
            TenantName.Create("Acme Corporation"),
            TenantSlug.Create("acme"));

        InMemoryTenantRepository tenants = new InMemoryTenantRepository().Seed(stored);
        GetTenantByIdHandler handler = new(tenants);

        TenantView? found = await handler.HandleAsync(new GetTenantByIdQuery(stored.Id.Value));

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

        GetTenantByIdHandler handler = new(new InMemoryTenantRepository().Seed(stored));

        TenantView? found = await handler.HandleAsync(new GetTenantByIdQuery(stored.Id.Value));

        Assert.Equal(TenantStatus.Suspended, found?.Status);
    }

    [Fact]
    public async Task An_unknown_tenant_produces_a_not_found_result()
    {
        // Absence is a result, not an exception: no type to catch, no stack
        // unwind, and nothing that would be logged as a server fault.
        GetTenantByIdHandler handler = new(new InMemoryTenantRepository());

        TenantView? found = await handler.HandleAsync(new GetTenantByIdQuery(Guid.CreateVersion7()));

        Assert.Null(found);
    }

    [Fact]
    public async Task Another_tenants_id_does_not_return_the_stored_tenant()
    {
        Tenant stored = Tenant.Create(TenantName.Create("Acme"), TenantSlug.Create("acme"));

        GetTenantByIdHandler handler = new(new InMemoryTenantRepository().Seed(stored));

        Assert.Null(await handler.HandleAsync(new GetTenantByIdQuery(Guid.CreateVersion7())));
    }

    [Fact]
    public async Task An_empty_id_is_rejected_as_invalid_rather_than_reported_as_missing()
    {
        // An empty GUID is malformed input, not a tenant that happens not to
        // exist, so it must not be answered with "not found".
        GetTenantByIdHandler handler = new(new InMemoryTenantRepository());

        await Assert.ThrowsAsync<DomainValidationException>(
            () => handler.HandleAsync(new GetTenantByIdQuery(Guid.Empty)));
    }

    [Fact]
    public async Task A_null_query_is_a_programming_error()
    {
        GetTenantByIdHandler handler = new(new InMemoryTenantRepository());

        await Assert.ThrowsAsync<ArgumentNullException>(() => handler.HandleAsync(null!));
    }

    [Fact]
    public void The_handler_requires_a_repository()
    {
        Assert.Throws<ArgumentNullException>(() => new GetTenantByIdHandler(null!));
    }
}
