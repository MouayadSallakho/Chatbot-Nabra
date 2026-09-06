using OmniAI.Application.Tenants;
using OmniAI.Application.Tenants.Persistence;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Application.Tests.Tenants;

/// <summary>
/// A hand-written stand-in for the tenant repository.
/// </summary>
/// <remarks>
/// Written out rather than produced by a mocking framework. The contract has
/// three methods, this fake is shorter than the setup code a framework would
/// need, and it can express the one thing that actually matters here -- that the
/// slug conflict is raised by the store rather than decided by the handler --
/// without any configuration DSL. No mocking package is added to the solution.
/// <para>
/// The Domain is never faked. <c>Tenant</c>, <c>TenantName</c> and
/// <c>TenantSlug</c> are the real types, so these tests exercise the real
/// invariants.
/// </para>
/// </remarks>
internal sealed class InMemoryTenantRepository : ITenantRepository
{
    private readonly List<Tenant> _stored = [];
    private readonly List<TenantMembership> _memberships = [];
    private readonly List<TenantRole> _ownerRoles = [];
    private readonly List<TenantRoleAssignment> _ownerAssignments = [];

    /// <summary>Tenants currently stored, in insertion order.</summary>
    public IReadOnlyList<Tenant> Stored => _stored;

    /// <summary>Memberships currently stored, in insertion order.</summary>
    /// <remarks>
    /// Exposed so a test can assert the thing that actually matters about
    /// workspace creation: that the creator ended up a member, and that a
    /// rejected creation left neither row behind.
    /// </remarks>
    public IReadOnlyList<TenantMembership> Memberships => _memberships;

    /// <summary>Owner roles created, in insertion order.</summary>
    /// <remarks>
    /// Exposed so a test can assert that provisioning made one -- and that a
    /// rejected creation left none. A workspace with no Owner has no full
    /// authority in it and no way to acquire one, since no caller can mint a
    /// system role.
    /// </remarks>
    public IReadOnlyList<TenantRole> OwnerRoles => _ownerRoles;

    /// <summary>Owner assignments created, in insertion order.</summary>
    public IReadOnlyList<TenantRoleAssignment> OwnerAssignments => _ownerAssignments;

    /// <summary>How many times <see cref="AddAsync"/> was called.</summary>
    public int AddAttempts { get; private set; }

    /// <summary>
    /// When true, <see cref="SlugExistsAsync"/> always reports the slug as free.
    /// </summary>
    /// <remarks>
    /// Lets a test drive the case the advisory check cannot catch: two callers
    /// both pass it and the store has to be the one that rejects the loser. That
    /// is the behaviour the real unique index provides, and it is the reason the
    /// check is documented as advisory.
    /// </remarks>
    public bool PretendSlugIsFree { get; init; }

    public Task<Tenant?> FindByIdAsync(TenantId id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_stored.Find(tenant => tenant.Id == id));

    public Task<bool> SlugExistsAsync(TenantSlug slug, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(slug);

        return Task.FromResult(!PretendSlugIsFree && ContainsSlug(slug));
    }

    /// <summary>
    /// Stores the whole workspace, or none of it.
    /// </summary>
    /// <remarks>
    /// The all-or-nothing behaviour is reproduced rather than assumed: the
    /// conflict is raised before any list is touched, so a test can assert that a
    /// losing creation leaves no orphan membership, role or assignment without
    /// needing a database to prove it. The real transaction is proven separately,
    /// against PostgreSQL.
    /// <para>
    /// There is no consistency check here any more, and its absence is the point.
    /// Phase 12's version verified that the membership named the tenant being
    /// created, because two loose arguments could disagree. <c>NewWorkspace</c>
    /// derives all four rows from the tenant and the creator, so a mismatched set
    /// cannot be built and there is nothing left for a fake to catch.
    /// </para>
    /// </remarks>
    public Task AddAsync(
        NewWorkspace workspace,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        AddAttempts++;

        // Stands in for the unique index: the store, not the caller, is the
        // authority on whether a slug is taken.
        if (ContainsSlug(workspace.Tenant.Slug))
        {
            throw TenantSlugAlreadyExistsException.ForSlug(workspace.Tenant.Slug.Value);
        }

        _stored.Add(workspace.Tenant);
        _memberships.Add(workspace.Membership);
        _ownerRoles.Add(workspace.OwnerRole);
        _ownerAssignments.Add(workspace.OwnerAssignment);

        return Task.CompletedTask;
    }

    /// <summary>Seeds a tenant without going through the use case.</summary>
    public InMemoryTenantRepository Seed(Tenant tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        _stored.Add(tenant);

        return this;
    }

    private bool ContainsSlug(TenantSlug slug) =>
        _stored.Exists(stored => stored.Slug == slug);
}
