using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Npgsql;
using OmniAI.Application.AiAgents;
using OmniAI.Application.AiAgents.ListAiAgents;
using OmniAI.Application.AiAgents.Persistence;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Tenants;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Tenants;

namespace OmniAI.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of the AI agent persistence contract.
/// </summary>
/// <remarks>
/// This class is where PostgreSQL stops. Everything above it -- the handlers, the
/// endpoints -- sees only <see cref="IAiAgentRepository"/>, the Application's own
/// exception types and an opaque <see cref="ConcurrencyToken"/>. Nothing leaves
/// here carrying a <c>DbUpdateException</c>, a
/// <c>DbUpdateConcurrencyException</c>, a <c>PostgresException</c>, a SQLSTATE, a
/// constraint name, SQL text or an <c>xmin</c> value.
/// </remarks>
internal sealed class AiAgentRepository(OmniAiDbContext context) : IAiAgentRepository
{
    /// <summary>PostgreSQL's SQLSTATE for a foreign key violation.</summary>
    private const string ForeignKeyViolation = PostgresErrorCodes.ForeignKeyViolation;

    private readonly OmniAiDbContext _context =
        context ?? throw new ArgumentNullException(nameof(context));

    /// <summary>
    /// Finds one agent, scoped to its tenant in the query itself, with the
    /// version it was read at.
    /// </summary>
    /// <remarks>
    /// <strong>Both predicates are in the SQL.</strong> The tenant is not checked
    /// after loading, and the row is not fetched by id and then compared: the
    /// <c>WHERE</c> clause carries <c>tenant_id</c> as well as <c>id</c>, so an
    /// agent belonging to another tenant is never read, never materialised and
    /// never tracked. That distinction matters -- a fetch-then-compare
    /// implementation would put another tenant's instruction text into this
    /// process's memory and would leak it the moment any code path returned the
    /// entity before reaching the comparison. Here there is nothing to leak,
    /// because nothing came back.
    /// <para>
    /// A version marker is never produced for an agent that was not found, so no
    /// caller can learn anything about another tenant's row from one.
    /// </para>
    /// </remarks>
    public async Task<Versioned<AiAgent>?> FindAsync(
        TenantId tenantId,
        AiAgentId agentId,
        CancellationToken cancellationToken = default)
    {
        AiAgent? agent = await _context.AiAgents
            .FirstOrDefaultAsync(
                candidate => candidate.Id == agentId && candidate.TenantId == tenantId,
                cancellationToken)
            .ConfigureAwait(false);

        return agent is null ? null : new Versioned<AiAgent>(agent, CurrentVersion(agent));
    }

    /// <summary>
    /// Reads one page of a tenant's agents, most recently changed first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>The tenant predicate is in the SQL, and it is the first thing in
    /// the query.</strong> Rows belonging to another tenant are never read,
    /// never counted and never materialised -- the count is filtered too, so a
    /// caller cannot even learn how many agents somebody else has from a total
    /// that looks too large.
    /// </para>
    /// <para>
    /// <strong>The projection is the point of this method.</strong> Selecting
    /// specific columns means <c>instructions</c> -- up to twenty thousand
    /// characters per row -- never leaves PostgreSQL. It also keeps the change
    /// tracker empty: <c>AsNoTracking</c> is implied by projecting to a
    /// non-entity type, so a page of twenty agents costs no tracked state for a
    /// read that will never write.
    /// </para>
    /// <para>
    /// <strong>A total order, not just a useful one.</strong> Ordering by
    /// <c>updated_at</c> alone would leave rows sharing a timestamp in whatever
    /// order the database happened to return them, and two agents created in the
    /// same transaction share one exactly. Under paging that is not cosmetic: an
    /// unstable order makes consecutive pages overlap or skip rows. The id
    /// tie-break makes the order deterministic.
    /// </para>
    /// <para>
    /// Two round trips -- one count, one page. At the expected sizes that is
    /// cheaper to reason about than a windowed count, and it keeps the page query
    /// to the columns a list actually shows.
    /// </para>
    /// </remarks>
    public async Task<AiAgentPage> ListAsync(
        TenantId tenantId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        IQueryable<AiAgent> owned = _context.AiAgents
            .Where(candidate => candidate.TenantId == tenantId);

        int totalCount = await owned.CountAsync(cancellationToken).ConfigureAwait(false);

        if (totalCount == 0)
        {
            return AiAgentPage.Empty(page, pageSize);
        }

        List<AiAgentSummary> items = await owned
            .OrderByDescending(candidate => candidate.UpdatedAt)
            .ThenBy(candidate => candidate.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(candidate => new AiAgentSummary(
                candidate.Id.Value,
                candidate.TenantId.Value,
                candidate.Name.Value,
                candidate.Status,
                candidate.CreatedAt,
                candidate.UpdatedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new AiAgentPage(items, page, pageSize, totalCount);
    }

    public async Task<ConcurrencyToken> AddAsync(
        AiAgent agent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);

        _context.AiAgents.Add(agent);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException failure) when (IsTenantForeignKeyViolation(failure))
        {
            // The owning tenant is not there. The use case checks first, so a
            // normal "no such tenant" request never gets this far; what reaches
            // here is the case the check cannot cover -- the tenant vanishing in
            // the window between the check and the insert. Detach the row so this
            // unit of work is not left holding an insert it would retry if the
            // same context were reused, then report the outcome in the
            // Application's own vocabulary. The original exception is kept as the
            // inner exception for logs -- never for the response body.
            _context.Entry(agent).State = EntityState.Detached;

            throw TenantNotFoundException.ForId(agent.TenantId.Value, failure);
        }

        // PostgreSQL stamped the row on insert and EF read the value back, so the
        // caller can make a conditional write immediately without a second query.
        return CurrentVersion(agent);
    }

    public async Task<ConcurrencyToken> UpdateAsync(
        AiAgent agent,
        ConcurrencyToken expected,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);

        PropertyEntry<AiAgent, uint> version = RowVersion(agent);

        // The heart of the conditional write.
        //
        // EF would otherwise use the version this request happened to load, which
        // is precisely the bug: a request that reads current state can never
        // conflict with it, so an edit computed from a screen somebody opened
        // minutes ago would overwrite whatever happened since. Substituting the
        // caller's expected version makes PostgreSQL test the row against what the
        // caller actually saw.
        //
        // A value this platform did not produce cannot match any row, so it is
        // refused here rather than being sent to the database as a number that
        // might coincidentally match something.
        if (!RowVersionTokens.TryDecode(expected, out uint expectedRowVersion))
        {
            throw AiAgentVersionMismatchException.ForAgent(agent.Id.Value);
        }

        version.OriginalValue = expectedRowVersion;

        try
        {
            // A no-op when the aggregate refused to change -- an idempotent
            // activation, a rename to the current name. EF finds no modified
            // property, issues no UPDATE, and there is no predicate to fail. The
            // precondition for that case is enforced by the use case before it
            // gets here; see AiAgentConcurrency.
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
            // meant, which is exactly the loss this mechanism exists to prevent.
            throw AiAgentVersionMismatchException.ForAgent(agent.Id.Value, failure);
        }

        return CurrentVersion(agent);
    }

    /// <summary>The opaque token for the version this instance is currently at.</summary>
    private ConcurrencyToken CurrentVersion(AiAgent agent) =>
        RowVersionTokens.Encode(RowVersion(agent).CurrentValue);

    /// <summary>
    /// The tracked <c>xmin</c> shadow property for an agent.
    /// </summary>
    /// <remarks>
    /// Shadow, so the value lives in the change tracker rather than on the
    /// aggregate. That is what lets the Domain stay free of any version, row
    /// version or ETag property while the database still gets a concurrency
    /// predicate.
    /// </remarks>
    private PropertyEntry<AiAgent, uint> RowVersion(AiAgent agent) =>
        _context.Entry(agent).Property<uint>(PersistenceSchema.XminConcurrencyToken);

    /// <summary>
    /// Whether this failure is the tenant foreign key rejecting an agent whose
    /// owner does not exist.
    /// </summary>
    /// <remarks>
    /// Matched on the constraint name as well as the SQLSTATE, so a future
    /// foreign key on this table cannot be silently reported as a missing tenant.
    /// If a different constraint fails, this returns false and the original
    /// exception propagates as the unexpected error it is -- which is the correct
    /// outcome, because guessing would turn a real defect into a tidy 404.
    /// </remarks>
    private static bool IsTenantForeignKeyViolation(DbUpdateException failure) =>
        failure.InnerException is PostgresException postgres
        && postgres.SqlState == ForeignKeyViolation
        && string.Equals(postgres.ConstraintName, "fk_ai_agents_tenant", StringComparison.Ordinal);
}
