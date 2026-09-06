using OmniAI.Application.Authorization;
using OmniAI.Application.Authorization.Persistence;
using OmniAI.Application.Concurrency;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Application.Tests.Security;

/// <summary>
/// An in-memory role store.
/// </summary>
/// <remarks>
/// It reproduces the properties the real repository is relied upon for: both
/// predicates are applied to assignments, roles are scoped to their tenant, and
/// the published order is Owner first then custom roles by normalised name. A
/// fake that ignored the user or the tenant would let a handler with no scoping
/// at all pass its tests, which would make it worse than no test.
/// <para>
/// The write methods throw rather than pretending to succeed. Nothing that uses
/// this double creates or edits roles -- those handlers have their own suite --
/// and a silently permissive stub would let a wrong call go unnoticed.
/// </para>
/// </remarks>
internal sealed class InMemoryTenantRoleRepository : ITenantRoleRepository
{
    private readonly List<TenantRole> _roles = [];
    private readonly List<TenantRoleAssignment> _assignments = [];

    /// <summary>Records a role in a tenant.</summary>
    public InMemoryTenantRoleRepository AddRole(TenantRole role)
    {
        ArgumentNullException.ThrowIfNull(role);

        _roles.Add(role);

        return this;
    }

    /// <summary>Gives a person a role.</summary>
    public InMemoryTenantRoleRepository Assign(UserId userId, TenantRole role)
    {
        ArgumentNullException.ThrowIfNull(role);

        _assignments.Add(TenantRoleAssignment.ForRole(userId, role, DateTimeOffset.UnixEpoch));

        return this;
    }

    public Task<IReadOnlyList<TenantRole>> ListAssignedRolesAsync(
        UserId userId,
        TenantId tenantId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<TenantRole> assigned =
        [
            .. _assignments
                .Where(assignment =>
                    assignment.UserId == userId && assignment.TenantId == tenantId)
                .Join(
                    _roles,
                    assignment => assignment.TenantRoleId,
                    role => role.Id,
                    (assignment, role) => role)
        ];

        return Task.FromResult(assigned);
    }

    public Task<IReadOnlyList<TenantRoleSummary>> ListAsync(
        TenantId tenantId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<TenantRoleSummary> summaries =
        [
            .. _roles
                .Where(role => role.TenantId == tenantId)
                .OrderBy(role => role.Kind is TenantRoleKind.Owner ? 0 : 1)
                .ThenBy(role => role.Name.Normalised, StringComparer.Ordinal)
                .ThenBy(role => role.Id.Value)
                .Select(role => new TenantRoleSummary(
                    role.Id.Value,
                    role.Name.Value,
                    role.Kind,
                    role.EffectivePermissions.Count,
                    role.CreatedAt,
                    role.UpdatedAt))
        ];

        return Task.FromResult(summaries);
    }

    public Task<Versioned<TenantRole>?> FindAsync(
        TenantId tenantId,
        TenantRoleId roleId,
        CancellationToken cancellationToken = default)
    {
        TenantRole? role = _roles.Find(candidate =>
            candidate.Id == roleId && candidate.TenantId == tenantId);

        return Task.FromResult(
            role is null ? null : new Versioned<TenantRole>(role, ConcurrencyToken.From("v1")));
    }

    public Task<bool> NameExistsAsync(
        TenantId tenantId,
        string normalisedName,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_roles.Exists(role =>
            role.TenantId == tenantId
            && string.Equals(role.Name.Normalised, normalisedName, StringComparison.Ordinal)));

    public Task<ConcurrencyToken> AddAsync(
        TenantRole role,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "This double is for authorisation tests. Role creation has its own suite.");

    public Task<ConcurrencyToken> UpdateAsync(
        TenantRole role,
        ConcurrencyToken expected,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "This double is for authorisation tests. Role mutation has its own suite.");
}

/// <summary>
/// Builds a <see cref="TenantAuthorizer"/> for a test.
/// </summary>
/// <remarks>
/// The authorizer is a real instance over real fakes, never itself a double. A
/// stubbed authorizer would let every handler test pass while proving nothing
/// about whether the handler consults it, which is exactly the property under
/// test -- and for this type a permissive stub is a platform-wide authorisation
/// bypass.
/// <para>
/// Every arrangement below routes through real <c>TenantRole</c> aggregates
/// rather than hand-built permission lists, so even the doubles exercise the real
/// definition of what an Owner permits.
/// </para>
/// </remarks>
internal static class TestAuthority
{
    /// <summary>An Owner of the given tenants, holding every platform permission.</summary>
    public static TenantAuthorizer Owner(UserId userId, params Guid[] tenantIds)
    {
        ArgumentNullException.ThrowIfNull(tenantIds);

        InMemoryTenantRoleRepository roles = new();

        foreach (Guid tenantId in tenantIds)
        {
            TenantRole owner = TenantRole.CreateOwner(
                TenantId.From(tenantId),
                DateTimeOffset.UnixEpoch);

            roles.AddRole(owner).Assign(userId, owner);
        }

        return new TenantAuthorizer(TestAccess.ForTenants(userId, tenantIds), roles);
    }

    /// <summary>
    /// A member of one tenant holding a custom role with exactly these
    /// permissions.
    /// </summary>
    /// <remarks>
    /// The restricted caller every escalation test needs. Passing no permissions
    /// produces somebody who belongs to the workspace and may do nothing in it,
    /// which is a real state the platform has to handle rather than an edge case.
    /// </remarks>
    public static TenantAuthorizer With(
        UserId userId,
        Guid tenantId,
        params PermissionCode[] permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        TenantRole role = TenantRole.CreateCustom(
            TenantId.From(tenantId),
            TenantRoleName.Create("Limited"),
            permissions,
            DateTimeOffset.UnixEpoch);

        InMemoryTenantRoleRepository roles = new InMemoryTenantRoleRepository()
            .AddRole(role)
            .Assign(userId, role);

        return new TenantAuthorizer(TestAccess.ForTenants(userId, tenantId), roles);
    }

    /// <summary>A caller who is signed in but belongs to nothing.</summary>
    public static TenantAuthorizer Outsider(UserId userId) =>
        new(TestAccess.Outsider(userId), new InMemoryTenantRoleRepository());

    /// <summary>A request that carries no identity at all.</summary>
    public static TenantAuthorizer Anonymous() =>
        new(TestAccess.Anonymous(), new InMemoryTenantRoleRepository());

    /// <summary>
    /// An authorizer that permits everything, everywhere.
    /// </summary>
    /// <remarks>
    /// <strong>For tests that are not about authorisation.</strong> A rename test
    /// exists to prove renaming works; making it also arrange a membership, an
    /// Owner role and an assignment for whichever random tenant id it invented
    /// would bury the thing it actually asserts.
    /// <para>
    /// It is safe to use precisely because it is not the only authorizer in play:
    /// each handler has dedicated tests giving it an <see cref="Outsider"/> and a
    /// restricted <see cref="With"/> authorizer and proving it refuses. If a
    /// handler ever stopped consulting the authorizer, those would fail even
    /// though every test using this one would still pass.
    /// </para>
    /// </remarks>
    public static TenantAuthorizer Anywhere() =>
        new(TestAccess.Anywhere(), new EverythingGranted());

    /// <summary>A role store that reports an Owner for whatever is asked.</summary>
    private sealed class EverythingGranted : ITenantRoleRepository
    {
        public Task<IReadOnlyList<TenantRole>> ListAssignedRolesAsync(
            UserId userId,
            TenantId tenantId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TenantRole>>(
                [TenantRole.CreateOwner(tenantId, DateTimeOffset.UnixEpoch)]);

        public Task<IReadOnlyList<TenantRoleSummary>> ListAsync(
            TenantId tenantId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TenantRoleSummary>>([]);

        public Task<Versioned<TenantRole>?> FindAsync(
            TenantId tenantId,
            TenantRoleId roleId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Versioned<TenantRole>?>(null);

        public Task<bool> NameExistsAsync(
            TenantId tenantId,
            string normalisedName,
            CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<ConcurrencyToken> AddAsync(
            TenantRole role,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("This double never writes.");

        public Task<ConcurrencyToken> UpdateAsync(
            TenantRole role,
            ConcurrencyToken expected,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("This double never writes.");
    }
}
