using OmniAI.Application.Authorization;
using OmniAI.Application.Authorization.Persistence;
using OmniAI.Application.Concurrency;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Application.Tests.Authorization;

/// <summary>
/// A role store the role use cases can actually write to.
/// </summary>
/// <remarks>
/// Separate from the read-only double the authorisation tests use, and
/// deliberately so: those tests must not be able to write, and these must. A
/// single permissive fake shared by both would let an authorisation test pass
/// while quietly persisting something.
/// <para>
/// It reproduces the properties the real repository is relied upon for -- the
/// tenant predicate is applied, names are compared in their normalised form, the
/// published order is Owner first then custom roles by normalised name, and a
/// duplicate name is refused by the store rather than by the caller. A fake that
/// ignored any of those would let a handler defect through.
/// </para>
/// <para>
/// The version it hands out is fixed and opaque. What matters to these tests is
/// that the handler compares the caller's token against the one the store
/// reported, not what either is made of -- the real encoding is Infrastructure's,
/// and it is proven against PostgreSQL.
/// </para>
/// </remarks>
internal sealed class RecordingRoleRepository : ITenantRoleRepository
{
    private readonly List<TenantRole> _roles = [];
    private readonly List<TenantRole> _added = [];
    private readonly List<TenantRoleAssignment> _assignments = [];

    /// <summary>The version every stored role currently reports.</summary>
    public ConcurrencyToken Version { get; } = ConcurrencyToken.From("current");

    /// <summary>Roles written through <see cref="AddAsync"/>, in order.</summary>
    /// <remarks>
    /// Exposed so a test can assert that a refused creation stored nothing --
    /// which is the difference between a validation failure and a half-created
    /// role.
    /// </remarks>
    public IReadOnlyList<TenantRole> Added => _added;

    /// <summary>Seeds a role without going through a use case.</summary>
    public TenantRole AddRole(TenantRole role)
    {
        ArgumentNullException.ThrowIfNull(role);

        _roles.Add(role);

        return role;
    }

    /// <summary>Gives a person a role in the tenant that role belongs to.</summary>
    /// <remarks>
    /// Needed so one store can back both the authorizer and the use case under
    /// test. That combination is what makes self-escalation reproducible: editing
    /// a role the actor holds must be visible to the next authorisation question,
    /// exactly as it is in production where both read the same table.
    /// </remarks>
    public TenantRole Assign(UserId userId, TenantRole role)
    {
        ArgumentNullException.ThrowIfNull(role);

        _assignments.Add(TenantRoleAssignment.ForRole(userId, role, DateTimeOffset.UnixEpoch));

        return role;
    }

    /// <summary>Replaces everything somebody holds with one role.</summary>
    /// <remarks>
    /// For the acceptance tests, where the point is that an inviter's authority
    /// can change between issuing an invitation and somebody using it. Reassigning
    /// through a use case would need the very member-assignment machinery those
    /// tests are checking, so the state is arranged directly.
    /// </remarks>
    public TenantRole ReplaceAssignments(UserId userId, TenantRole role)
    {
        ArgumentNullException.ThrowIfNull(role);

        _assignments.RemoveAll(assignment => assignment.UserId == userId);
        _roles.RemoveAll(candidate => candidate.Id == role.Id);

        _roles.Add(role);
        _assignments.Add(TenantRoleAssignment.ForRole(userId, role, DateTimeOffset.UnixEpoch));

        return role;
    }

    /// <summary>Removes a role, as deleting one would.</summary>
    /// <remarks>
    /// Role deletion is not implemented, so this models the state rather than the
    /// operation: an invitation naming a role that has gone must not be
    /// deliverable, and that has to be provable before deletion exists.
    /// </remarks>
    public void Forget(TenantRole role)
    {
        ArgumentNullException.ThrowIfNull(role);

        _roles.RemoveAll(candidate => candidate.Id == role.Id);
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
                    role.UpdatedAt,
                    Holders(tenantId, role.Id)))
        ];

        return Task.FromResult(summaries);
    }

    public Task<int> CountMembersAsync(
        TenantId tenantId,
        TenantRoleId roleId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Holders(tenantId, roleId));

    public Task<IReadOnlyDictionary<TenantRoleId, string>> FindNamesAsync(
        TenantId tenantId,
        IReadOnlyCollection<TenantRoleId> roleIds,
        CancellationToken cancellationToken = default)
    {
        NameReads++;

        // Both predicates, as the real query applies them.
        IReadOnlyDictionary<TenantRoleId, string> names = _roles
            .Where(role => role.TenantId == tenantId && roleIds.Contains(role.Id))
            .ToDictionary(role => role.Id, role => role.Name.Value);

        return Task.FromResult(names);
    }

    /// <summary>How many times role names were read in a batch (Phase 37.5b).</summary>
    public int NameReads { get; private set; }

    private int Holders(TenantId tenantId, TenantRoleId roleId) =>
        _assignments.Count(assignment => assignment.TenantId == tenantId && assignment.TenantRoleId == roleId);

    public Task<Versioned<TenantRole>?> FindAsync(
        TenantId tenantId,
        TenantRoleId roleId,
        CancellationToken cancellationToken = default)
    {
        // Both predicates, exactly as the real query applies them. A fake that
        // matched on the id alone would let a handler reach another workspace's
        // role and still pass.
        TenantRole? role = _roles.Find(candidate =>
            candidate.Id == roleId && candidate.TenantId == tenantId);

        return Task.FromResult(
            role is null ? null : new Versioned<TenantRole>(role, Version));
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
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(role);

        // Stands in for the unique index: the store, not the caller, is the
        // authority on whether a name is taken.
        if (_roles.Exists(existing =>
            existing.TenantId == role.TenantId
            && string.Equals(
                existing.Name.Normalised,
                role.Name.Normalised,
                StringComparison.Ordinal)))
        {
            throw TenantRoleNameAlreadyExistsException.ForName(role.Name.Value);
        }

        _roles.Add(role);
        _added.Add(role);

        return Task.FromResult(Version);
    }

    public Task<ConcurrencyToken> UpdateAsync(
        TenantRole role,
        ConcurrencyToken expected,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(role);

        // The handler has already compared the caller's version against the one
        // this store reported; this reproduces the database's own predicate so a
        // handler that stopped checking would still be caught.
        if (expected != Version)
        {
            throw TenantRoleVersionMismatchException.ForRole(role.Id.Value);
        }

        return Task.FromResult(ConcurrencyToken.From("next"));
    }
}
