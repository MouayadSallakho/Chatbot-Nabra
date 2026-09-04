using Microsoft.EntityFrameworkCore;
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

    public async Task AddAsync(Tenant tenant, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        _context.Tenants.Add(tenant);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException failure) when (IsSlugUniqueViolation(failure))
        {
            // The insert lost the race for this slug. Detach it so this unit of
            // work is not left holding a row it will try to insert again if the
            // same context is reused, then report the outcome in the
            // Application's own vocabulary. The original exception is kept as the
            // inner exception for logs -- never for the response body.
            _context.Entry(tenant).State = EntityState.Detached;

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
