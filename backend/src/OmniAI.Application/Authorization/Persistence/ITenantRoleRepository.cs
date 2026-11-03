using OmniAI.Application.Concurrency;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Application.Authorization.Persistence;

/// <summary>
/// The only way the authorisation and role use cases reach storage.
/// </summary>
/// <remarks>
/// Six methods, because the authorised use cases need six things and nothing
/// more. It is deliberately <em>not</em> an <c>IRepository&lt;T&gt;</c>, for the
/// reasons already recorded for the tenant and agent repositories: a generic
/// repository would have to expose either <c>IQueryable</c> or a specification
/// language to stay useful, and both hand the caller the storage model back under
/// a different name. Nothing here mentions <c>IQueryable</c>, <c>DbSet</c>,
/// <c>DbContext</c>, EF Core, Npgsql, PostgreSQL or <c>xmin</c>, and the
/// Application project references none of them.
/// <para>
/// <strong>The tenant is a required argument of every method that names a
/// role.</strong> There is no <c>FindAsync(roleId)</c>, so no call site can reach
/// a role by identity alone. That matters because a role id appears in a URL: a
/// caller who knows one -- or was shown one in another workspace -- still cannot
/// reach it through a route naming a different tenant, because the query has no
/// shape that omits the tenant predicate. It is the same rule the AI agent
/// repository follows, and for the same reason.
/// </para>
/// <para>
/// <strong>Writes are narrow on purpose.</strong> There is an add and an update,
/// and no delete: role deletion is not implemented in this phase, so there is no
/// method for it. Adding one "for completeness" would create a path with no use
/// case behind it, and role deletion has real unanswered questions -- what
/// happens to the members holding it -- that belong to the phase that introduces
/// member administration.
/// </para>
/// <para>
/// <strong>Assignments are read here and written only by tenant
/// provisioning.</strong> <see cref="ListAssignedRolesAsync"/> is the read that
/// every authorisation check performs. There is no <c>AssignAsync</c>, because no
/// authorised use case assigns a role to anybody: the workspace creator is given
/// Owner in the same transaction that creates the workspace, through
/// <c>ITenantRepository.AddAsync</c>. A general assignment method here would be a
/// second way to grant authority, reachable without creating a workspace, and it
/// is exactly the surface that must wait for the member-administration phase.
/// </para>
/// </remarks>
public interface ITenantRoleRepository
{
    /// <summary>
    /// The roles this person holds in this workspace.
    /// </summary>
    /// <param name="userId">The person asking.</param>
    /// <param name="tenantId">The workspace the question is about.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Their roles, with each role's stored grants loaded. Empty when they hold
    /// none -- which is an ordinary state, not a failure: a member with no roles
    /// belongs to the workspace and can do nothing in it.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <strong>This is the authorisation hot path, and it is answered by the
    /// database every time.</strong> Nothing is cached, and no permission is
    /// derived from a claim baked into the session cookie. A cached or claimed
    /// permission set would keep granting authority after a role was changed, for
    /// as long as the cache or the cookie lived -- which turns "we removed that
    /// permission" into "we removed that permission, eventually". One indexed read
    /// per request is a small price for an answer that is correct at the moment it
    /// is asked, and the volume is small enough that it is not worth trading.
    /// </para>
    /// <para>
    /// <strong>It returns roles rather than permission codes.</strong> An
    /// implementation that unioned the codes itself would have to know that an
    /// Owner role means "everything", and that knowledge would then live in
    /// Infrastructure -- beside a query, where a future edit could quietly narrow
    /// it. Returning the aggregates keeps <c>TenantRole.EffectivePermissions</c>
    /// the single definition of what a role permits.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<TenantRole>> ListAssignedRolesAsync(
        UserId userId,
        TenantId tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every role in a workspace, in the platform's published order.
    /// </summary>
    /// <returns>
    /// A summary per role: enough for a list, without the permission sets. Empty
    /// is not an expected answer -- every workspace has an Owner -- but it is
    /// returned rather than treated as a fault, because a query is not the right
    /// place to assert an invariant it did not create.
    /// </returns>
    /// <remarks>
    /// Summaries, not aggregates: a list shows a name, a kind and a count, and
    /// loading every role's grants to render a number would fetch rows nothing
    /// displays. The count for an Owner comes from the catalogue, not from rows,
    /// because Owner has none.
    /// </remarks>
    Task<IReadOnlyList<TenantRoleSummary>> ListAsync(
        TenantId tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// How many members of the workspace hold one role (Phase 37.5b, row 23).
    /// </summary>
    /// <remarks>
    /// Counted from the assignments, scoped by the workspace in the query itself,
    /// on <c>ix_tenant_role_assignments_role</c>. A role of another workspace counts
    /// zero, never another workspace's members.
    /// </remarks>
    Task<int> CountMembersAsync(
        TenantId tenantId,
        TenantRoleId roleId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The names of several of the workspace's roles at once, by id (Phase 37.5b).
    /// </summary>
    /// <returns>
    /// One entry per id that names a role of this workspace; an id of another
    /// workspace, or of no role, is simply absent.
    /// </returns>
    /// <remarks>
    /// One statement for a whole page, so a list that shows role names per row
    /// does not read each role separately.
    /// </remarks>
    Task<IReadOnlyDictionary<TenantRoleId, string>> FindNamesAsync(
        TenantId tenantId,
        IReadOnlyCollection<TenantRoleId> roleIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds one role within a workspace, together with the version it was read
    /// at.
    /// </summary>
    /// <returns>
    /// The role and its version, or <see langword="null"/> when this workspace has
    /// no role with that id -- whether because no such role exists at all, or
    /// because it exists and belongs to somebody else.
    /// </returns>
    /// <remarks>
    /// The two absences are deliberately indistinguishable, and the implementation
    /// must make them so by filtering on the tenant in the query itself rather
    /// than by fetching the role and comparing owners afterwards. A caller cannot
    /// tell "no such role" from "not yours", so a role id from another workspace
    /// cannot become an alternative route to modifying it.
    /// <para>
    /// The role's grants are loaded, because every caller of this method either
    /// publishes the permission set or replaces it.
    /// </para>
    /// </remarks>
    Task<Versioned<TenantRole>?> FindAsync(
        TenantId tenantId,
        TenantRoleId roleId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether this workspace already has a role with this normalised name.
    /// </summary>
    /// <remarks>
    /// A courtesy, not the guarantee. It lets a create attempt answer "that name
    /// is taken" without provoking a constraint violation in the ordinary case,
    /// but two concurrent creations can both pass it. The unique index is what
    /// actually decides, and <see cref="AddAsync"/> reports the loser.
    /// <para>
    /// Takes the normalised form rather than the display name, because normalised
    /// is what the index is built on. Passing the display name would make this
    /// check answer a different question from the one the database answers.
    /// </para>
    /// </remarks>
    Task<bool> NameExistsAsync(
        TenantId tenantId,
        string normalisedName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a new role and commits.
    /// </summary>
    /// <returns>The version the stored role now has.</returns>
    /// <remarks>
    /// Creation is unconditional -- there is no previous version to state -- so it
    /// takes no expected version. The version it returns is what lets a client
    /// immediately make a conditional write without reading the role back.
    /// </remarks>
    /// <exception cref="TenantRoleNameAlreadyExistsException">
    /// The name is already taken in this workspace. Raised from the unique index,
    /// which is what makes this outcome race-safe: two concurrent callers that
    /// both passed <see cref="NameExistsAsync"/> still produce exactly one stored
    /// role and exactly one conflict.
    /// </exception>
    /// <exception cref="Tenants.TenantNotFoundException">
    /// The owning workspace does not exist. Raised from the foreign key, as
    /// defence in depth rather than as the primary check.
    /// </exception>
    Task<ConcurrencyToken> AddAsync(
        TenantRole role,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits changes to a role, but only if it is still at the version the
    /// caller expected.
    /// </summary>
    /// <param name="role">The role, loaded through <see cref="FindAsync"/> and mutated.</param>
    /// <param name="expected">
    /// The version the caller believed it was changing -- typically read minutes
    /// earlier by a person looking at a screen, and <strong>not</strong> the
    /// version this request happened to load.
    /// </param>
    /// <returns>The new version, when the write succeeded.</returns>
    /// <remarks>
    /// <para>
    /// <strong>The expected version must reach the database, not merely be
    /// compared in memory.</strong> An implementation that checked the expected
    /// version against the loaded one and then wrote using the loaded version
    /// would still lose an update to a transaction that commits in between.
    /// </para>
    /// <para>
    /// <strong>A permission replacement is all or nothing.</strong> Removing the
    /// old grants and inserting the new ones is one commit, so a failure leaves
    /// the role with exactly the set it had. A role that briefly grants the
    /// intersection of two sets -- or, worse, nothing -- is not a state any
    /// caller asked for.
    /// </para>
    /// </remarks>
    /// <exception cref="TenantRoleVersionMismatchException">
    /// The role is no longer at the expected version. The write is refused rather
    /// than applied, so whoever changed it keeps their change.
    /// </exception>
    /// <exception cref="TenantRoleNameAlreadyExistsException">
    /// A rename collided with a name another role in this workspace already holds.
    /// </exception>
    Task<ConcurrencyToken> UpdateAsync(
        TenantRole role,
        ConcurrencyToken expected,
        CancellationToken cancellationToken = default);
}
