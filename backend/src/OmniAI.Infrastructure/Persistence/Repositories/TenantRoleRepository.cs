using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Npgsql;
using OmniAI.Application.Authorization;
using OmniAI.Application.Authorization.Persistence;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Tenants;
using OmniAI.Domain;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of the role and authorisation persistence contract.
/// </summary>
/// <remarks>
/// This class is where PostgreSQL stops. Everything above it -- the authorizer,
/// the handlers, the endpoints -- sees only
/// <see cref="ITenantRoleRepository"/>, the Application's own exception types and
/// an opaque <see cref="ConcurrencyToken"/>. Nothing leaves here carrying a
/// <c>DbUpdateException</c>, a <c>DbUpdateConcurrencyException</c>, a
/// <c>PostgresException</c>, a SQLSTATE, a constraint name, SQL text or an
/// <c>xmin</c> value.
/// </remarks>
internal sealed class TenantRoleRepository(OmniAiDbContext context) : ITenantRoleRepository
{
    /// <summary>PostgreSQL's SQLSTATE for a unique constraint violation.</summary>
    private const string UniqueViolation = PostgresErrorCodes.UniqueViolation;

    /// <summary>PostgreSQL's SQLSTATE for a foreign key violation.</summary>
    private const string ForeignKeyViolation = PostgresErrorCodes.ForeignKeyViolation;

    private readonly OmniAiDbContext _context =
        context ?? throw new ArgumentNullException(nameof(context));

    /// <summary>
    /// The roles this person holds in this workspace, with their grants.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Both predicates are in the SQL.</strong> The user and the tenant
    /// are joined and filtered in the query, so an assignment belonging to
    /// somebody else is never read, never materialised and never tracked. That
    /// distinction matters: a fetch-then-filter implementation would put another
    /// person's authority into this process's memory and would leak it the moment
    /// any code path returned early.
    /// </para>
    /// <para>
    /// <strong>Grants are included, and that is not an over-fetch.</strong> The
    /// authorizer asks each role what it permits, and for a custom role that
    /// answer is its grants. Loading the roles without them would either produce
    /// an empty permission set -- silently denying everything -- or trigger a lazy
    /// load per role, which this context does not enable. An Owner has no grants
    /// at all, so the common case reads nothing extra.
    /// </para>
    /// <para>
    /// <c>AsNoTracking</c>, because this is a read that will never write. It also
    /// keeps an authorisation check from leaving tracked state behind in a unit of
    /// work that is about to save something else -- which for role aggregates
    /// would mean an unrelated request could accidentally persist them.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<TenantRole>> ListAssignedRolesAsync(
        UserId userId,
        TenantId tenantId,
        CancellationToken cancellationToken = default) =>
        await _context.TenantRoleAssignments
            .AsNoTracking()
            .Where(assignment =>
                assignment.UserId == userId && assignment.TenantId == tenantId)
            .Join(
                _context.TenantRoles.AsNoTracking().Include(role => role.Grants),
                assignment => assignment.TenantRoleId,
                role => role.Id,
                (assignment, role) => role)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// Every role in a workspace, Owner first and then custom roles by name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>The order is part of the contract, not an implementation
    /// detail.</strong> Owner first because it is the role a reader is looking for
    /// when they open the page, then custom roles by their normalised name so the
    /// order does not depend on capitalisation. The role id is the final
    /// tie-break: two roles cannot share a normalised name within a tenant, so it
    /// is unreachable in practice -- and without it the order would be
    /// "deterministic except in a case the database decides", which is not a
    /// contract.
    /// </para>
    /// <para>
    /// <strong>Owner's permission count comes from the catalogue, not from
    /// rows.</strong> An Owner stores no grants, so counting them would report
    /// zero -- a number that is true of the table and false about the role. The
    /// count is computed after materialisation for exactly that reason; pushing it
    /// into SQL would mean encoding "Owner means everything" in a query, where a
    /// later edit could quietly narrow it.
    /// </para>
    /// <para>
    /// The grant counts for custom roles are computed in the database, so no
    /// permission row is transferred to render a number.
    /// </para>
    /// <para>
    /// <strong>The member count is a correlated count in the same statement</strong>
    /// (Phase 37.5b), on <c>ix_tenant_role_assignments_role</c>, scoped by the
    /// role's own workspace: one statement for the whole list, however many roles.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<TenantRoleSummary>> ListAsync(
        TenantId tenantId,
        CancellationToken cancellationToken = default)
    {
        var rows = await _context.TenantRoles
            .AsNoTracking()
            .Where(role => role.TenantId == tenantId)
            .OrderBy(role => role.Kind == TenantRoleKind.Owner ? 0 : 1)
            .ThenBy(role => EF.Property<string>(role, PersistenceSchema.NormalisedRoleName))
            .ThenBy(role => role.Id)
            .Select(role => new
            {
                role.Id,
                Name = role.Name.Value,
                role.Kind,
                GrantCount = role.Grants.Count,
                MemberCount = _context.TenantRoleAssignments.Count(assignment =>
                    assignment.TenantRoleId == role.Id && assignment.TenantId == role.TenantId),
                role.CreatedAt,
                role.UpdatedAt,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. rows.Select(row => new TenantRoleSummary(
                row.Id.Value,
                row.Name,
                row.Kind,
                row.Kind is TenantRoleKind.Owner
                    ? PlatformPermissions.AllCodes.Count
                    : row.GrantCount,
                row.CreatedAt,
                row.UpdatedAt,
                row.MemberCount))
        ];
    }

    public Task<int> CountMembersAsync(
        TenantId tenantId,
        TenantRoleId roleId,
        CancellationToken cancellationToken = default) =>
        _context.TenantRoleAssignments
            .AsNoTracking()
            .CountAsync(
                assignment => assignment.TenantRoleId == roleId && assignment.TenantId == tenantId,
                cancellationToken);

    public async Task<IReadOnlyDictionary<TenantRoleId, string>> FindNamesAsync(
        TenantId tenantId,
        IReadOnlyCollection<TenantRoleId> roleIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roleIds);

        if (roleIds.Count == 0)
        {
            return new Dictionary<TenantRoleId, string>();
        }

        TenantRoleId[] named = [.. roleIds.Distinct()];

        var rows = await _context.TenantRoles
            .AsNoTracking()
            .Where(role => role.TenantId == tenantId && named.Contains(role.Id))
            .Select(role => new { role.Id, Name = role.Name.Value })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.ToDictionary(row => row.Id, row => row.Name);
    }

    /// <summary>
    /// Finds one role, scoped to its tenant in the query itself, with the version
    /// it was read at.
    /// </summary>
    /// <remarks>
    /// <strong>Both predicates are in the SQL.</strong> The tenant is not checked
    /// after loading, and the row is not fetched by id and then compared: the
    /// <c>WHERE</c> clause carries <c>tenant_id</c> as well as <c>id</c>, so a
    /// role belonging to another workspace is never read, never materialised and
    /// never tracked. A caller holding a role id from elsewhere therefore gets the
    /// same not-found as one holding an invented id, and no version marker is ever
    /// produced for a role that was not found.
    /// <para>
    /// Tracked, unlike the reads above, because every caller of this method either
    /// publishes the role or mutates and saves it.
    /// </para>
    /// </remarks>
    public async Task<Versioned<TenantRole>?> FindAsync(
        TenantId tenantId,
        TenantRoleId roleId,
        CancellationToken cancellationToken = default)
    {
        TenantRole? role = await _context.TenantRoles
            .Include(candidate => candidate.Grants)
            .FirstOrDefaultAsync(
                candidate => candidate.Id == roleId && candidate.TenantId == tenantId,
                cancellationToken)
            .ConfigureAwait(false);

        return role is null ? null : new Versioned<TenantRole>(role, CurrentVersion(role));
    }

    /// <summary>
    /// Whether this workspace already has a role with this normalised name.
    /// </summary>
    /// <remarks>
    /// Compares the normalised column, which is what the unique index is built
    /// on, so this check answers the same question the database will. Comparing
    /// the display name would answer a different one and would let
    /// <c>support</c> through beside <c>Support</c> only for the insert to be
    /// refused later.
    /// </remarks>
    public async Task<bool> NameExistsAsync(
        TenantId tenantId,
        string normalisedName,
        CancellationToken cancellationToken = default) =>
        await _context.TenantRoles
            .AsNoTracking()
            .AnyAsync(
                role => role.TenantId == tenantId
                    && EF.Property<string>(role, PersistenceSchema.NormalisedRoleName)
                        == normalisedName,
                cancellationToken)
            .ConfigureAwait(false);

    public async Task<ConcurrencyToken> AddAsync(
        TenantRole role,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(role);

        _context.TenantRoles.Add(role);
        TenantRoles.SetNormalisedName(_context, role);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException failure) when (IsNameUniqueViolation(failure))
        {
            Detach(role);

            throw TenantRoleNameAlreadyExistsException.ForName(role.Name.Value, failure);
        }
        catch (DbUpdateException failure) when (IsOwnerUniqueViolation(failure))
        {
            // Unreachable through any published endpoint: no caller can ask for a
            // system role, and provisioning writes the only Owner a workspace ever
            // gets. It is translated rather than left to escape because an
            // unexplained 500 from a partial-unique index is the worst possible
            // way to learn that something has started minting Owners.
            Detach(role);

            throw new DomainStateException(
                "This workspace already has an Owner role. A workspace has exactly one, "
                + "created with it.",
                failure);
        }
        catch (DbUpdateException failure) when (IsTenantForeignKeyViolation(failure))
        {
            // The owning workspace is not there. The use case checks membership
            // first, so a normal request never gets this far; what reaches here is
            // the case the check cannot cover -- the tenant disappearing between
            // the check and the insert.
            Detach(role);

            throw TenantNotFoundException.ForId(role.TenantId.Value, failure);
        }

        // PostgreSQL stamped the row on insert and EF read the value back, so the
        // caller can make a conditional write immediately without a second query.
        return CurrentVersion(role);
    }

    public async Task<ConcurrencyToken> UpdateAsync(
        TenantRole role,
        ConcurrencyToken expected,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(role);

        PropertyEntry<TenantRole, uint> version = RowVersion(role);

        // The heart of the conditional write.
        //
        // EF would otherwise use the version this request happened to load, which
        // is precisely the bug: a request that reads current state can never
        // conflict with it, so a permission set computed from a screen somebody
        // opened minutes ago would overwrite whatever happened since. Substituting
        // the caller's expected version makes PostgreSQL test the row against what
        // the caller actually saw.
        //
        // A value this platform did not produce cannot match any row, so it is
        // refused here rather than sent to the database as a number that might
        // coincidentally match something.
        if (!RowVersionTokens.TryDecode(expected, out uint expectedRowVersion))
        {
            throw TenantRoleVersionMismatchException.ForRole(role.Id.Value);
        }

        version.OriginalValue = expectedRowVersion;

        // Keep the two halves of the name in step.
        TenantRoles.SetNormalisedName(_context, role);

        // Force the parent row into the UPDATE even when only its grants changed.
        //
        // Without this, replacing a permission set would write child rows and
        // leave tenant_roles untouched -- so EF would issue no UPDATE against it,
        // there would be no concurrency predicate to fail, and two administrators
        // replacing permissions inside the same window would both succeed with one
        // of them silently overwritten. Both aggregate operations already stamp
        // UpdatedAt, so in practice the row is modified anyway; marking it
        // explicitly means the guarantee does not depend on two clock readings
        // differing.
        _context.Entry(role).Property(candidate => candidate.UpdatedAt).IsModified = true;

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException failure)
        {
            // The row is not at the expected version. Either somebody changed it
            // long before this request arrived, or a transaction committed while
            // this one was running. PostgreSQL matched no row, so nothing was
            // written and the other writer's state stands.
            //
            // Deliberately no reload and no retry. Reapplying this change to state
            // the caller has not seen would overwrite whatever the other writer
            // meant -- and with permissions, that means quietly restoring authority
            // somebody deliberately removed.
            throw TenantRoleVersionMismatchException.ForRole(role.Id.Value, failure);
        }
        catch (DbUpdateException failure) when (IsNameUniqueViolation(failure))
        {
            throw TenantRoleNameAlreadyExistsException.ForName(role.Name.Value, failure);
        }

        return CurrentVersion(role);
    }

    /// <summary>Removes a rejected role and its grants from the unit of work.</summary>
    /// <remarks>
    /// The grants go too. Detaching only the role would leave its grant rows
    /// pending as <c>Added</c> for a role that will never exist, and the next save
    /// on this context would fail on a foreign key with an error nobody could
    /// explain -- the same reasoning the tenant repository records for its own
    /// rollback path.
    /// </remarks>
    private void Detach(TenantRole role)
    {
        foreach (RolePermissionGrant grant in role.Grants)
        {
            _context.Entry(grant).State = EntityState.Detached;
        }

        _context.Entry(role).State = EntityState.Detached;
    }

    /// <summary>The opaque token for the version this instance is currently at.</summary>
    private ConcurrencyToken CurrentVersion(TenantRole role) =>
        RowVersionTokens.Encode(RowVersion(role).CurrentValue);

    /// <summary>
    /// The tracked <c>xmin</c> shadow property for a role.
    /// </summary>
    /// <remarks>
    /// Shadow, so the value lives in the change tracker rather than on the
    /// aggregate. That is what lets the Domain stay free of any version, row
    /// version or ETag property while the database still gets a concurrency
    /// predicate.
    /// </remarks>
    private PropertyEntry<TenantRole, uint> RowVersion(TenantRole role) =>
        _context.Entry(role).Property<uint>(PersistenceSchema.XminConcurrencyToken);

    /// <summary>
    /// Whether this failure is the tenant-scoped role name index rejecting a
    /// duplicate.
    /// </summary>
    /// <remarks>
    /// Matched on the constraint name as well as the SQLSTATE, so another unique
    /// index on this table cannot be silently reported as a name conflict. If a
    /// different constraint fails, this returns false and the original exception
    /// propagates as the unexpected error it is -- guessing would turn a real
    /// defect into a tidy 409.
    /// </remarks>
    private static bool IsNameUniqueViolation(DbUpdateException failure) =>
        IsConstraintViolation(failure, UniqueViolation, "ux_tenant_roles_tenant_name");

    private static bool IsOwnerUniqueViolation(DbUpdateException failure) =>
        IsConstraintViolation(failure, UniqueViolation, "ux_tenant_roles_owner");

    private static bool IsTenantForeignKeyViolation(DbUpdateException failure) =>
        IsConstraintViolation(failure, ForeignKeyViolation, "fk_tenant_roles_tenant");

    private static bool IsConstraintViolation(
        DbUpdateException failure,
        string sqlState,
        string constraintName) =>
        failure.InnerException is PostgresException postgres
        && postgres.SqlState == sqlState
        && string.Equals(postgres.ConstraintName, constraintName, StringComparison.Ordinal);
}
