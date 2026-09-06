using OmniAI.Application.Security;
using OmniAI.Application.Tenants;
using OmniAI.Application.Tenants.ListMyTenants;
using OmniAI.Application.Tests.Security;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Application.Tests.Tenants;

/// <summary>
/// The list of workspaces the admin application is allowed to offer.
/// </summary>
public sealed class ListMyTenantsHandlerTests
{
    private static readonly UserId Alice = UserId.New();
    private static readonly UserId Bob = UserId.New();

    private static readonly TenantView Acme =
        new(Guid.CreateVersion7(), "Acme Trading", "acme", TenantStatus.Active);

    private static readonly TenantView Rival =
        new(Guid.CreateVersion7(), "Rival Trading", "rival", TenantStatus.Active);

    [Fact]
    public async Task The_callers_own_workspaces_are_returned()
    {
        InMemoryTenantMembershipRepository memberships = new InMemoryTenantMembershipRepository()
            .Grant(Alice, Acme);

        ListMyTenantsHandler handler = new(TestAccess.Outsider(Alice), memberships);

        IReadOnlyList<TenantView> found = await handler.HandleAsync();

        Assert.Equal([Acme], found);
    }

    [Fact]
    public async Task Somebody_elses_workspaces_are_not_returned()
    {
        // The property that makes this endpoint safe to be the source of the
        // frontend's tenant ids: it cannot hand out one the caller has no
        // membership for, so an id the browser later sends is one it was given.
        InMemoryTenantMembershipRepository memberships = new InMemoryTenantMembershipRepository()
            .Grant(Alice, Acme)
            .Grant(Bob, Rival);

        ListMyTenantsHandler handler = new(TestAccess.Outsider(Alice), memberships);

        IReadOnlyList<TenantView> found = await handler.HandleAsync();

        Assert.Equal([Acme], found);
        Assert.DoesNotContain(Rival, found);
    }

    [Fact]
    public async Task Somebody_with_no_workspaces_gets_an_empty_list_rather_than_a_failure()
    {
        // The state of everybody the moment after they register. It is ordinary,
        // so it is a result rather than an error.
        ListMyTenantsHandler handler =
            new(TestAccess.Outsider(Alice), new InMemoryTenantMembershipRepository());

        Assert.Empty(await handler.HandleAsync());
    }

    [Fact]
    public async Task The_order_is_oldest_membership_first()
    {
        // Deterministic, so the workspace switcher does not reshuffle itself
        // between page loads. Oldest first, so somebody's original workspace keeps
        // its position as they add more.
        DateTimeOffset first = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        InMemoryTenantMembershipRepository memberships = new InMemoryTenantMembershipRepository()
            .Grant(Alice, Rival, first.AddDays(1))
            .Grant(Alice, Acme, first);

        ListMyTenantsHandler handler = new(TestAccess.Outsider(Alice), memberships);

        Assert.Equal([Acme, Rival], await handler.HandleAsync());
    }

    [Fact]
    public async Task An_anonymous_caller_is_refused()
    {
        ListMyTenantsHandler handler =
            new(TestAccess.Anonymous(), new InMemoryTenantMembershipRepository());

        await Assert.ThrowsAsync<NotAuthenticatedException>(() => handler.HandleAsync());
    }

    [Fact]
    public void The_handler_requires_its_dependencies()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ListMyTenantsHandler(null!, new InMemoryTenantMembershipRepository()));

        Assert.Throws<ArgumentNullException>(
            () => new ListMyTenantsHandler(TestAccess.Outsider(Alice), null!));
    }
}
