using Microsoft.EntityFrameworkCore;
using OmniAI.Domain.Tenants;
using OmniAI.Infrastructure.Persistence;

namespace OmniAI.Infrastructure.IntegrationTests.Persistence;

public sealed class TenantPersistenceTests(PostgresTestDatabase database)
    : IClassFixture<PostgresTestDatabase>
{
    [Fact]
    public async Task A_tenant_round_trips_with_every_field_intact()
    {
        Tenant tenant = Tenant.Create(
            TenantName.Create("Acme Trading L.L.C."),
            TenantSlug.Create("acme-trading"));

        TenantId originalId = tenant.Id;

        await using (OmniAiDbContext write = database.CreateContext())
        {
            write.Tenants.Add(tenant);
            await write.SaveChangesAsync();
        }

        await using OmniAiDbContext read = database.CreateContext();
        Tenant reloaded = await read.Tenants.SingleAsync(t => t.Id == originalId);

        // Reconstitution must not mint a new identity.
        Assert.Equal(originalId, reloaded.Id);
        Assert.Equal("Acme Trading L.L.C.", reloaded.Name.Value);
        Assert.Equal("acme-trading", reloaded.Slug.Value);
        Assert.Equal(TenantStatus.Active, reloaded.Status);
    }

    [Fact]
    public async Task A_strongly_typed_tenant_id_survives_the_round_trip_as_a_native_uuid()
    {
        Tenant tenant = Tenant.Create(TenantName.Create("Acme"), TenantSlug.Create("uuid-check"));
        await AddAsync(tenant);

        // The column is a real uuid, not text: proven by asking PostgreSQL.
        object? columnType = await database.ExecuteScalarAsync(
            "SELECT data_type FROM information_schema.columns " +
            "WHERE table_name = 'tenants' AND column_name = 'id'");

        Assert.Equal("uuid", columnType as string);

        await using OmniAiDbContext read = database.CreateContext();
        Tenant reloaded = await read.Tenants.SingleAsync(t => t.Id == tenant.Id);

        Assert.Equal(tenant.Id.Value, reloaded.Id.Value);
    }

    [Fact]
    public async Task Value_objects_are_stored_as_scalar_columns_not_json()
    {
        Tenant tenant = Tenant.Create(TenantName.Create("Scalar Check"), TenantSlug.Create("scalar-check"));
        await AddAsync(tenant);

        object? nameType = await database.ExecuteScalarAsync(
            "SELECT data_type FROM information_schema.columns " +
            "WHERE table_name = 'tenants' AND column_name = 'name'");
        object? slugType = await database.ExecuteScalarAsync(
            "SELECT data_type FROM information_schema.columns " +
            "WHERE table_name = 'tenants' AND column_name = 'slug'");

        Assert.Equal("character varying", nameType as string);
        Assert.Equal("character varying", slugType as string);

        // And the stored value is the plain string, readable without a JSON path.
        object? storedName = await database.ExecuteScalarAsync(
            "SELECT name FROM tenants WHERE slug = 'scalar-check'");

        Assert.Equal("Scalar Check", storedName as string);
    }

    [Fact]
    public async Task A_suspended_status_round_trips_and_is_stored_by_name()
    {
        Tenant tenant = Tenant.Create(TenantName.Create("Acme"), TenantSlug.Create("status-check"));
        tenant.Suspend();
        await AddAsync(tenant);

        object? stored = await database.ExecuteScalarAsync(
            "SELECT status FROM tenants WHERE slug = 'status-check'");

        // Stored by name, so the schema does not silently depend on the
        // compiler's enum ordinals.
        Assert.Equal("Suspended", stored as string);

        await using OmniAiDbContext read = database.CreateContext();
        Tenant reloaded = await read.Tenants.SingleAsync(t => t.Id == tenant.Id);

        Assert.Equal(TenantStatus.Suspended, reloaded.Status);
    }

    [Fact]
    public async Task A_duplicate_slug_is_rejected_by_the_database()
    {
        // The uniqueness the Domain deliberately cannot guarantee on its own.
        Tenant first = Tenant.Create(TenantName.Create("First"), TenantSlug.Create("shared-slug"));
        Tenant second = Tenant.Create(TenantName.Create("Second"), TenantSlug.Create("shared-slug"));

        await AddAsync(first);

        await using OmniAiDbContext context = database.CreateContext();
        context.Tenants.Add(second);

        DbUpdateException failure = await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());

        Assert.Contains("ix_tenants_slug", failure.InnerException?.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Two_tenants_with_different_slugs_both_persist()
    {
        await AddAsync(Tenant.Create(TenantName.Create("Alpha"), TenantSlug.Create("alpha-co")));
        await AddAsync(Tenant.Create(TenantName.Create("Beta"), TenantSlug.Create("beta-co")));

        await using OmniAiDbContext read = database.CreateContext();

        Assert.Equal(2, await read.Tenants
            .CountAsync(t => t.Slug == TenantSlug.Create("alpha-co")
                          || t.Slug == TenantSlug.Create("beta-co")));
    }

    private async Task AddAsync(Tenant tenant)
    {
        await using OmniAiDbContext context = database.CreateContext();
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();
    }
}
