using OmniAI.Domain.Tenants;

namespace OmniAI.Domain.Tests.Tenants;

public sealed class TenantTests
{
    private static Tenant CreateTenant(string name = "Acme Trading", string slug = "acme") =>
        Tenant.Create(TenantName.Create(name), TenantSlug.Create(slug));

    [Fact]
    public void Create_produces_a_tenant_with_the_supplied_name_and_slug()
    {
        Tenant tenant = CreateTenant("Acme Trading", "acme-syria");

        Assert.Equal("Acme Trading", tenant.Name.Value);
        Assert.Equal("acme-syria", tenant.Slug.Value);
    }

    [Fact]
    public void Create_assigns_a_non_empty_identity()
    {
        Tenant tenant = CreateTenant();

        Assert.False(tenant.Id.IsEmpty);
    }

    [Fact]
    public void Create_assigns_a_distinct_identity_to_each_tenant()
    {
        Assert.NotEqual(CreateTenant().Id, CreateTenant().Id);
    }

    [Fact]
    public void Create_starts_the_tenant_active()
    {
        Assert.Equal(TenantStatus.Active, CreateTenant().Status);
    }

    [Fact]
    public void Create_normalises_the_name_and_slug()
    {
        Tenant tenant = CreateTenant("  Acme Trading  ", "  ACME-Syria  ");

        Assert.Equal("Acme Trading", tenant.Name.Value);
        Assert.Equal("acme-syria", tenant.Slug.Value);
    }

    [Fact]
    public void Create_rejects_a_null_name()
    {
        Assert.Throws<ArgumentNullException>(() => Tenant.Create(null!, TenantSlug.Create("acme")));
    }

    [Fact]
    public void Create_rejects_a_null_slug()
    {
        Assert.Throws<ArgumentNullException>(() => Tenant.Create(TenantName.Create("Acme"), null!));
    }

    [Fact]
    public void Rename_replaces_the_name()
    {
        Tenant tenant = CreateTenant("Acme Trading");

        tenant.Rename(TenantName.Create("Acme Global"));

        Assert.Equal("Acme Global", tenant.Name.Value);
    }

    [Fact]
    public void Rename_leaves_identity_slug_and_status_untouched()
    {
        Tenant tenant = CreateTenant("Acme Trading", "acme");
        TenantId originalId = tenant.Id;

        tenant.Rename(TenantName.Create("Acme Global"));

        Assert.Equal(originalId, tenant.Id);
        Assert.Equal("acme", tenant.Slug.Value);
        Assert.Equal(TenantStatus.Active, tenant.Status);
    }

    [Fact]
    public void Rename_rejects_a_null_name()
    {
        Tenant tenant = CreateTenant();

        Assert.Throws<ArgumentNullException>(() => tenant.Rename(null!));
    }

    [Fact]
    public void Rename_to_an_invalid_name_leaves_the_tenant_unchanged()
    {
        Tenant tenant = CreateTenant("Acme Trading");

        // The invalid value is rejected while constructing the name, so the
        // aggregate is never handed a value that could corrupt it.
        Assert.Throws<DomainValidationException>(() => tenant.Rename(TenantName.Create("   ")));
        Assert.Equal("Acme Trading", tenant.Name.Value);
    }

    [Fact]
    public void ChangeSlug_replaces_the_slug()
    {
        Tenant tenant = CreateTenant(slug: "acme");

        tenant.ChangeSlug(TenantSlug.Create("acme-syria"));

        Assert.Equal("acme-syria", tenant.Slug.Value);
    }

    [Fact]
    public void ChangeSlug_canonicalises_the_new_slug()
    {
        Tenant tenant = CreateTenant(slug: "acme");

        tenant.ChangeSlug(TenantSlug.Create("  ACME-Global "));

        Assert.Equal("acme-global", tenant.Slug.Value);
    }

    [Fact]
    public void ChangeSlug_leaves_identity_name_and_status_untouched()
    {
        Tenant tenant = CreateTenant("Acme Trading", "acme");
        TenantId originalId = tenant.Id;

        tenant.ChangeSlug(TenantSlug.Create("acme-syria"));

        Assert.Equal(originalId, tenant.Id);
        Assert.Equal("Acme Trading", tenant.Name.Value);
        Assert.Equal(TenantStatus.Active, tenant.Status);
    }

    [Fact]
    public void ChangeSlug_rejects_a_null_slug()
    {
        Tenant tenant = CreateTenant();

        Assert.Throws<ArgumentNullException>(() => tenant.ChangeSlug(null!));
    }

    [Theory]
    [InlineData("acme syria")]
    [InlineData("-acme")]
    [InlineData("ac--me")]
    [InlineData("")]
    public void ChangeSlug_to_an_invalid_slug_leaves_the_tenant_unchanged(string value)
    {
        Tenant tenant = CreateTenant(slug: "acme");

        Assert.Throws<DomainValidationException>(() => tenant.ChangeSlug(TenantSlug.Create(value)));
        Assert.Equal("acme", tenant.Slug.Value);
    }

    [Fact]
    public void Suspend_moves_an_active_tenant_to_suspended()
    {
        Tenant tenant = CreateTenant();

        tenant.Suspend();

        Assert.Equal(TenantStatus.Suspended, tenant.Status);
    }

    [Fact]
    public void Activate_moves_a_suspended_tenant_back_to_active()
    {
        Tenant tenant = CreateTenant();
        tenant.Suspend();

        tenant.Activate();

        Assert.Equal(TenantStatus.Active, tenant.Status);
    }

    [Fact]
    public void Suspend_is_idempotent()
    {
        Tenant tenant = CreateTenant();
        tenant.Suspend();

        tenant.Suspend();

        Assert.Equal(TenantStatus.Suspended, tenant.Status);
    }

    [Fact]
    public void Activate_is_idempotent()
    {
        Tenant tenant = CreateTenant();

        tenant.Activate();

        Assert.Equal(TenantStatus.Active, tenant.Status);
    }

    [Fact]
    public void Status_changes_leave_identity_name_and_slug_untouched()
    {
        Tenant tenant = CreateTenant("Acme Trading", "acme");
        TenantId originalId = tenant.Id;

        tenant.Suspend();
        tenant.Activate();

        Assert.Equal(originalId, tenant.Id);
        Assert.Equal("Acme Trading", tenant.Name.Value);
        Assert.Equal("acme", tenant.Slug.Value);
    }

    [Fact]
    public void Two_tenants_created_with_the_same_details_remain_distinct()
    {
        // Tenants are entities: identity, not attribute values, decides sameness.
        Tenant first = CreateTenant("Acme", "acme");
        Tenant second = CreateTenant("Acme", "acme");

        Assert.NotEqual(first.Id, second.Id);
        Assert.NotSame(first, second);
    }
}
