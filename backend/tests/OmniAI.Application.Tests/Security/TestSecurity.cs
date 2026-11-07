using OmniAI.Application.Security;
using OmniAI.Application.Tenants;
using OmniAI.Application.Tenants.Persistence;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Application.Tests.Security;

/// <summary>
/// A caller, for tests that need one.
/// </summary>
/// <remarks>
/// Hand-written rather than mocked, like every other double in this suite. The
/// interface has two members and one of them is derived from the other, so a
/// mocking framework would need more setup code than this whole file.
/// </remarks>
internal sealed class FakeCurrentUser : ICurrentUser
{
    private FakeCurrentUser(UserId? id) => Id = id;

    public bool IsAuthenticated => Id is not null;

    public UserId? Id { get; }

    /// <summary>A signed-in caller.</summary>
    public static FakeCurrentUser SignedInAs(UserId id) => new(id);

    /// <summary>Nobody.</summary>
    /// <remarks>
    /// Used to prove that the use cases refuse anonymous callers on their own,
    /// rather than relying on an endpoint having been mapped with an
    /// authorization requirement. That distinction is the whole point of checking
    /// access below HTTP.
    /// </remarks>
    public static FakeCurrentUser Anonymous() => new(null);
}

/// <summary>
/// An in-memory membership store.
/// </summary>
/// <remarks>
/// It reproduces the two properties the real repository is relied upon for: the
/// user predicate is applied, and the returned order is deterministic. A fake
/// that ignored the user would let a handler with no scoping at all pass its
/// tests, which would make it worse than no test.
/// </remarks>
internal sealed class InMemoryTenantMembershipRepository : ITenantMembershipRepository
{
    private readonly List<(TenantMembership Membership, TenantView Tenant)> _rows = [];

    /// <summary>Records a membership and the tenant it points at.</summary>
    public InMemoryTenantMembershipRepository Grant(UserId userId, TenantView tenant, DateTimeOffset? at = null)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        _rows.Add((
            TenantMembership.Create(
                userId,
                TenantId.From(tenant.Id),
                at ?? DateTimeOffset.UnixEpoch.AddSeconds(_rows.Count)),
            tenant));

        return this;
    }

    /// <summary>Takes somebody's memberships away.</summary>
    /// <remarks>
    /// There is no product surface that removes a member, and deliberately so.
    /// This exists because a security question genuinely depends on it: an
    /// invitation must not outlive the membership that issued it, and proving
    /// that needs an inviter who has left.
    /// </remarks>
    public InMemoryTenantMembershipRepository Forget(UserId userId)
    {
        _rows.RemoveAll(row => row.Membership.UserId == userId);

        return this;
    }

    public Task<bool> ExistsAsync(
        UserId userId,
        TenantId tenantId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_rows.Exists(row =>
            row.Membership.UserId == userId && row.Membership.TenantId == tenantId));

    public Task<IReadOnlyList<TenantView>> ListTenantsAsync(
        UserId userId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<TenantView> tenants =
        [
            .. _rows
                .Where(row => row.Membership.UserId == userId)
                .OrderBy(row => row.Membership.CreatedAt)
                .ThenBy(row => row.Membership.TenantId.Value)
                .Select(row => row.Tenant)
        ];

        return Task.FromResult(tenants);
    }
}

/// <summary>
/// A membership store that says yes to everything.
/// </summary>
/// <remarks>
/// Only ever reached through <see cref="TestAccess.Anywhere"/>. It exists so a
/// test about renaming an agent does not have to be a test about membership as
/// well; see that method for why this does not weaken the suite.
/// </remarks>
internal sealed class PermissiveTenantMembershipRepository : ITenantMembershipRepository
{
    public Task<bool> ExistsAsync(
        UserId userId,
        TenantId tenantId,
        CancellationToken cancellationToken = default) => Task.FromResult(true);

    /// <summary>
    /// Always empty.
    /// </summary>
    /// <remarks>
    /// Nothing that uses this double lists workspaces, and inventing a plausible
    /// list would be a fixture nobody asserts against. The real listing behaviour
    /// is covered by <see cref="InMemoryTenantMembershipRepository"/> and by the
    /// PostgreSQL suite.
    /// </remarks>
    public Task<IReadOnlyList<TenantView>> ListTenantsAsync(
        UserId userId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TenantView>>([]);
}

/// <summary>
/// Builds a <see cref="TenantAccessGuard"/> for a test.
/// </summary>
/// <remarks>
/// The guard is a real instance over real fakes, never itself a double. A stubbed
/// guard would let every handler test pass while proving nothing about whether
/// the handler consults it, which is exactly the property under test.
/// </remarks>
internal static class TestAccess
{
    /// <summary>A guard for somebody who belongs to the given tenants.</summary>
    public static TenantAccessGuard For(UserId userId, params TenantView[] tenants)
    {
        ArgumentNullException.ThrowIfNull(tenants);

        InMemoryTenantMembershipRepository memberships = new();

        foreach (TenantView tenant in tenants)
        {
            memberships.Grant(userId, tenant);
        }

        return new TenantAccessGuard(FakeCurrentUser.SignedInAs(userId), memberships);
    }

    /// <summary>A guard for somebody who belongs to the given tenant ids.</summary>
    public static TenantAccessGuard ForTenants(UserId userId, params Guid[] tenantIds)
    {
        ArgumentNullException.ThrowIfNull(tenantIds);

        return For(
            userId,
            [.. tenantIds.Select(id => new TenantView(id, "Workspace", "workspace", TenantStatus.Active, "UTC"))]);
    }

    /// <summary>A guard for a caller who is signed in but belongs to nothing.</summary>
    public static TenantAccessGuard Outsider(UserId userId) =>
        new(FakeCurrentUser.SignedInAs(userId), new InMemoryTenantMembershipRepository());

    /// <summary>
    /// A guard that admits the caller to every tenant.
    /// </summary>
    /// <remarks>
    /// <strong>For tests that are not about membership.</strong> A rename test
    /// exists to prove renaming works; making it also arrange a membership row for
    /// whichever random tenant id it invented would bury the thing it actually
    /// asserts. This keeps those tests about their own subject.
    /// <para>
    /// It is safe to use precisely because it is not the only guard in play: each
    /// handler has a dedicated test that gives it a real
    /// <see cref="Outsider"/> guard and proves the handler refuses. If a handler
    /// ever stopped consulting the guard, that test would fail even though every
    /// test using this one would still pass.
    /// </para>
    /// </remarks>
    public static TenantAccessGuard Anywhere() =>
        new(FakeCurrentUser.SignedInAs(UserId.New()), new PermissiveTenantMembershipRepository());

    /// <summary>A guard for a request that carries no identity at all.</summary>
    public static TenantAccessGuard Anonymous() =>
        new(FakeCurrentUser.Anonymous(), new InMemoryTenantMembershipRepository());
}
