using OmniAI.Application.Tenants;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Infrastructure.Persistence.Repositories;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// The membership queries against real PostgreSQL.
/// </summary>
/// <remarks>
/// The Application suite proves the handlers consult the guard; only these can
/// prove the guard's answer comes from a query that actually filters by user, and
/// that the promised order survives into SQL rather than being an accident of the
/// query plan.
/// </remarks>
public sealed class TenantMembershipRepositoryTests(PostgresTestDatabase database)
    : IClassFixture<PostgresTestDatabase>
{
    [Fact]
    public async Task A_member_is_recognised()
    {
        UserId user = await TestIdentity.SeedUserAsync(database);
        Tenant tenant = await SeedWorkspaceAsync(user);

        await using OmniAiDbContext context = database.CreateContext();

        Assert.True(await new TenantMembershipRepository(context)
            .ExistsAsync(user, tenant.Id));
    }

    [Fact]
    public async Task A_person_with_no_membership_is_not_recognised()
    {
        UserId owner = await TestIdentity.SeedUserAsync(database);
        UserId stranger = await TestIdentity.SeedUserAsync(database);
        Tenant tenant = await SeedWorkspaceAsync(owner);

        await using OmniAiDbContext context = database.CreateContext();

        Assert.False(await new TenantMembershipRepository(context)
            .ExistsAsync(stranger, tenant.Id));
    }

    [Fact]
    public async Task A_membership_of_one_tenant_does_not_admit_another()
    {
        UserId user = await TestIdentity.SeedUserAsync(database);
        await SeedWorkspaceAsync(user);
        Tenant other = await SeedWorkspaceAsync(await TestIdentity.SeedUserAsync(database));

        await using OmniAiDbContext context = database.CreateContext();

        Assert.False(await new TenantMembershipRepository(context)
            .ExistsAsync(user, other.Id));
    }

    [Fact]
    public async Task A_tenant_that_does_not_exist_is_simply_not_a_membership()
    {
        // No exception, no special case: an id nobody was ever issued produces the
        // same "no" as somebody else's workspace, which is what keeps the two
        // indistinguishable to a caller.
        UserId user = await TestIdentity.SeedUserAsync(database);

        await using OmniAiDbContext context = database.CreateContext();

        Assert.False(await new TenantMembershipRepository(context)
            .ExistsAsync(user, TenantId.New()));
    }

    [Fact]
    public async Task Only_the_callers_own_workspaces_are_listed()
    {
        UserId alice = await TestIdentity.SeedUserAsync(database);
        UserId bob = await TestIdentity.SeedUserAsync(database);

        Tenant hers = await SeedWorkspaceAsync(alice, "Acme Trading");
        Tenant his = await SeedWorkspaceAsync(bob, "Rival Trading");

        await using OmniAiDbContext context = database.CreateContext();

        IReadOnlyList<TenantView> found =
            await new TenantMembershipRepository(context).ListTenantsAsync(alice);

        Assert.Equal([hers.Id.Value], found.Select(tenant => tenant.Id));
        Assert.DoesNotContain(his.Id.Value, found.Select(tenant => tenant.Id));
    }

    [Fact]
    public async Task The_listing_carries_the_tenants_own_fields()
    {
        UserId user = await TestIdentity.SeedUserAsync(database);
        Tenant tenant = await SeedWorkspaceAsync(user, "Acme Trading");

        await using OmniAiDbContext context = database.CreateContext();

        TenantView found = Assert.Single(
            await new TenantMembershipRepository(context).ListTenantsAsync(user));

        Assert.Equal(tenant.Id.Value, found.Id);
        Assert.Equal("Acme Trading", found.Name);
        Assert.Equal(tenant.Slug.Value, found.Slug);
        Assert.Equal(TenantStatus.Active, found.Status);
    }

    [Fact]
    public async Task Somebody_with_no_workspaces_gets_an_empty_list()
    {
        UserId user = await TestIdentity.SeedUserAsync(database);

        await using OmniAiDbContext context = database.CreateContext();

        Assert.Empty(await new TenantMembershipRepository(context).ListTenantsAsync(user));
    }

    [Fact]
    public async Task The_order_is_oldest_membership_first_and_survives_into_sql()
    {
        // The ordering is applied after the join for exactly this reason: a sort
        // placed before one is not guaranteed to reach the generated SQL, which
        // would leave the promised order true only by accident.
        UserId user = await TestIdentity.SeedUserAsync(database);

        DateTimeOffset first = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        Tenant third = await SeedWorkspaceAsync(user, "Third", first.AddDays(2));
        Tenant firstWorkspace = await SeedWorkspaceAsync(user, "First", first);
        Tenant second = await SeedWorkspaceAsync(user, "Second", first.AddDays(1));

        await using OmniAiDbContext context = database.CreateContext();

        IReadOnlyList<TenantView> found =
            await new TenantMembershipRepository(context).ListTenantsAsync(user);

        Assert.Equal(
            [firstWorkspace.Id.Value, second.Id.Value, third.Id.Value],
            found.Select(tenant => tenant.Id));
    }

    [Fact]
    public async Task Memberships_created_at_the_same_instant_are_still_totally_ordered()
    {
        // Two workspaces created in one transaction share a timestamp exactly.
        // Without the identity tie-break the switcher would reshuffle itself
        // between page loads for no visible reason.
        UserId user = await TestIdentity.SeedUserAsync(database);

        DateTimeOffset shared = new(2026, 2, 2, 12, 0, 0, TimeSpan.Zero);

        Tenant one = await SeedWorkspaceAsync(user, "One", shared);
        Tenant two = await SeedWorkspaceAsync(user, "Two", shared);

        Guid[] expected = [one.Id.Value, two.Id.Value];
        Array.Sort(expected);

        await using OmniAiDbContext context = database.CreateContext();
        TenantMembershipRepository repository = new(context);

        Guid[] first = [.. (await repository.ListTenantsAsync(user)).Select(tenant => tenant.Id)];
        Guid[] again = [.. (await repository.ListTenantsAsync(user)).Select(tenant => tenant.Id)];

        Assert.Equal(expected, first);
        Assert.Equal(first, again);
    }

    /// <summary>Creates a tenant and a membership for it.</summary>
    private async Task<Tenant> SeedWorkspaceAsync(
        UserId owner,
        string name = "Acme Trading",
        DateTimeOffset? joined = null)
    {
        Tenant tenant = Tenant.Create(
            TenantName.Create(name),
            TenantSlug.Create($"t{Guid.NewGuid():N}"[..20]));

        await using OmniAiDbContext context = database.CreateContext();

        context.Tenants.Add(tenant);
        context.TenantMemberships.Add(TenantMembership.Create(
            owner,
            tenant.Id,
            joined ?? DateTimeOffset.UtcNow));

        await context.SaveChangesAsync();

        return tenant;
    }
}
