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
    /// Inserts a tenant and its first membership in one transaction.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>An explicit transaction, and it is genuinely needed.</strong> One
    /// <c>SaveChangesAsync</c> is already atomic, so at first sight adding two
    /// entities and saving once would be enough. It is not, because of what
    /// happens on the failure path: the slug race is caught here, the losing
    /// tenant is detached, and the caller is given a clean conflict. Without an
    /// enclosing transaction the membership would still be sitting in the change
    /// tracker as an <c>Added</c> row for a tenant that will never exist, and the
    /// next save on this context -- in the same request or a later one, if the
    /// context were reused -- would try to insert it and fail on the foreign key
    /// with an error nobody could explain. The transaction and the paired detach
    /// leave the unit of work exactly as it was before the attempt.
    /// </para>
    /// <para>
    /// <strong>Not an <c>IUnitOfWork</c>.</strong> This is one use case that needs
    /// two rows written together, so the transaction lives in the one operation
    /// that needs it. Introducing a general commit-orchestration abstraction would
    /// have meant changing every accepted repository so that none of them
    /// committed, in order to serve a single call site.
    /// </para>
    /// <para>
    /// The pairing is checked before anything is written. A membership naming a
    /// different tenant would be a programming error that the database would
    /// happily accept -- both foreign keys would resolve -- and it would silently
    /// grant somebody access to a workspace they did not create.
    /// </para>
    /// </remarks>
    public async Task AddAsync(
        Tenant tenant,
        TenantMembership membership,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(membership);

        if (membership.TenantId != tenant.Id)
        {
            throw new ArgumentException(
                "The membership must name the tenant being created.",
                nameof(membership));
        }

        await using IDbContextTransaction transaction = await _context.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        _context.Tenants.Add(tenant);
        _context.TenantMemberships.Add(membership);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException failure) when (IsSlugUniqueViolation(failure))
        {
            // The insert lost the race for this slug. Roll back so neither row
            // survives, then detach both so this unit of work is not left holding
            // rows it would try to insert again if the same context is reused, and
            // report the outcome in the Application's own vocabulary. The original
            // exception is kept as the inner exception for logs -- never for the
            // response body.
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);

            _context.Entry(tenant).State = EntityState.Detached;
            _context.Entry(membership).State = EntityState.Detached;

            throw TenantSlugAlreadyExistsException.ForSlug(tenant.Slug.Value, failure);
        }
    }

    /// <summary>
    /// Whether this failure is the tenant slug unique index rejecting a duplicate.
    /// </summary>
    /// <remarks>
    /// Matched on the constraint name as well as the SQLSTATE, so a future unique
    /// index on this table cannot be silently reported as a slug conflict. If a
    /// different constraint fails, this returns false and the original exception
    /// propagates as the unexpected error it is -- which is the correct outcome,
    /// because guessing would turn a real defect into a tidy 409.
    /// </remarks>
    private static bool IsSlugUniqueViolation(DbUpdateException failure) =>
        failure.InnerException is PostgresException postgres
        && postgres.SqlState == UniqueViolation
        && string.Equals(postgres.ConstraintName, "ix_tenants_slug", StringComparison.Ordinal);
}
