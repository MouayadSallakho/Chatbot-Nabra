using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using OmniAI.Application.Tenants;
using OmniAI.Application.Tenants.Persistence;
using OmniAI.Domain.Tenants;

namespace OmniAI.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of the tenant persistence contract.
/// </summary>
/// <remarks>
/// This class is where PostgreSQL stops. Everything above it -- the handlers, the
/// endpoints -- sees only <see cref="ITenantRepository"/> and the Application's
/// own exception type. Nothing leaves here carrying a <c>DbUpdateException</c>, a
/// <c>PostgresException</c>, a SQLSTATE, a constraint name or SQL text.
/// </remarks>
internal sealed class TenantRepository(OmniAiDbContext context) : ITenantRepository
{
    /// <summary>PostgreSQL's SQLSTATE for a unique constraint violation.</summary>
    private const string UniqueViolation = PostgresErrorCodes.UniqueViolation;

    private readonly OmniAiDbContext _context =
        context ?? throw new ArgumentNullException(nameof(context));

    public async Task<Tenant?> FindByIdAsync(
        TenantId id,
        CancellationToken cancellationToken = default) =>
        await _context.Tenants
            .FirstOrDefaultAsync(tenant => tenant.Id == id, cancellationToken)
            .ConfigureAwait(false);

    public async Task<bool> SlugExistsAsync(
        TenantSlug slug,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(slug);

        // AnyAsync, not a fetch: the caller only needs the answer, and this
        // becomes an index probe on ix_tenants_slug.
        return await _context.Tenants
            .AnyAsync(tenant => tenant.Slug == slug, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Inserts a workspace and everything that makes it usable, in one
    /// transaction.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>An explicit transaction, and it is genuinely needed.</strong> One
    /// <c>SaveChangesAsync</c> is already atomic, so at first sight adding four
    /// entities and saving once would be enough. It is not, because of what
    /// happens on the failure path: the slug race is caught here, the losing rows
    /// are detached, and the caller is given a clean conflict. Without an
    /// enclosing transaction the other three rows would still be sitting in the
    /// change tracker as <c>Added</c> rows for a tenant that will never exist, and
    /// the next save on this context -- in the same request or a later one, if the
    /// context were reused -- would try to insert them and fail on a foreign key
    /// with an error nobody could explain. The transaction and the paired detach
    /// leave the unit of work exactly as it was before the attempt.
    /// </para>
    /// <para>
    /// <strong>Not an <c>IUnitOfWork</c>.</strong> This is still the one use case
    /// that needs several rows written together, so the transaction lives in the
    /// one operation that needs it. Introducing a general commit-orchestration
    /// abstraction would mean changing every accepted repository so that none of
    /// them committed, in order to serve a single call site.
    /// </para>
    /// <para>
    /// <strong>Nothing is checked for consistency here, because nothing can be
    /// inconsistent.</strong> Phase 12's version of this method verified that the
    /// membership named the tenant being created, since four loose arguments could
    /// disagree. <see cref="NewWorkspace"/> replaced them, and it derives the
    /// membership, the Owner role and the assignment from the tenant and the
    /// creator -- so a mismatched set cannot be constructed and there is no run-time
    /// check left to write. The database's composite foreign keys remain the
    /// guarantee regardless of what any caller believes.
    /// </para>
    /// <para>
    /// The insertion order matters to the change tracker's fixup, not to
    /// correctness: EF orders the statements itself from the foreign keys it
    /// knows about, and the assignment's two composite keys mean it can only be
    /// written after both the membership and the role.
    /// </para>
    /// </remarks>
    public async Task AddAsync(
        NewWorkspace workspace,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        await using IDbContextTransaction transaction = await _context.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        _context.Tenants.Add(workspace.Tenant);
        _context.TenantMemberships.Add(workspace.Membership);
        _context.TenantRoles.Add(workspace.OwnerRole);
        _context.TenantRoleAssignments.Add(workspace.OwnerAssignment);

        // The Owner role stores no permission grants, so there is no normalised
        // name to set from a grants collection -- but its name column pair still
        // needs both halves. See TenantRoleConfiguration for why the normalised
        // form is a shadow property rather than a computed column.
        TenantRoles.SetNormalisedName(_context, workspace.OwnerRole);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException failure) when (IsSlugUniqueViolation(failure))
        {
            // The insert lost the race for this slug. Roll back so none of the
            // four rows survives, then detach all of them so this unit of work is
            // not left holding inserts it would retry if the same context were
            // reused, and report the outcome in the Application's own vocabulary.
            // The original exception is kept as the inner exception for logs --
            // never for the response body.
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);

            _context.Entry(workspace.Tenant).State = EntityState.Detached;
            _context.Entry(workspace.Membership).State = EntityState.Detached;
            _context.Entry(workspace.OwnerRole).State = EntityState.Detached;
            _context.Entry(workspace.OwnerAssignment).State = EntityState.Detached;

            throw TenantSlugAlreadyExistsException.ForSlug(workspace.Tenant.Slug.Value, failure);
        }
    }

    private static bool IsSlugUniqueViolation(DbUpdateException failure) =>
        failure.InnerException is PostgresException postgres
        && postgres.SqlState == UniqueViolation
        && string.Equals(postgres.ConstraintName, "ix_tenants_slug", StringComparison.Ordinal);
}
