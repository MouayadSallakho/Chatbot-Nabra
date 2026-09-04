using OmniAI.Application.Concurrency;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.AiAgents.Persistence;

/// <summary>
/// The only way the AI agent use cases reach storage.
/// </summary>
/// <remarks>
/// Three methods, because the six authorised use cases need three things and
/// nothing more: find one agent, store a new one, save changes to an existing
/// one. It is deliberately <em>not</em> an <c>IRepository&lt;T&gt;</c>, and a
/// second repository existing is not a reason to introduce one -- a generic
/// repository would have to expose either <c>IQueryable</c> or a specification
/// language to stay useful, and both hand the caller the storage model back
/// under a different name. Nothing here mentions <c>IQueryable</c>,
/// <c>DbSet</c>, <c>DbContext</c>, EF Core, Npgsql, PostgreSQL or <c>xmin</c>,
/// and the Application project references none of them.
/// <para>
/// <strong>Tenant scoping is in the signature, not in a convention.</strong>
/// <see cref="FindAsync"/> cannot be called without naming a tenant, so there is
/// no overload, no default and no code path that fetches an agent by identity
/// alone. That matters because <c>AiAgentId</c> values -- and now version
/// markers -- appear in URLs, headers, logs and client state: a caller who knows
/// one must still not be able to reach it through another tenant's route. Making
/// the tenant a required argument means the isolation rule cannot be forgotten at
/// a call site -- the code would not compile.
/// </para>
/// <para>
/// <strong>Every read and write carries a version.</strong> Reads return the
/// version the agent was read at; writes return the version they produced; and a
/// mutation must state which version it believed it was changing. That is what
/// makes a conditional write possible at all, and it is why the aggregate alone
/// is never enough to save with.
/// </para>
/// <para>
/// <strong>There is still no separate unit of work.</strong> Every one of the six
/// authorised use cases mutates at most one aggregate, and <c>SaveChanges</c> is
/// already atomic, so an <c>IUnitOfWork</c> today would be a second abstraction
/// that does nothing but forward a call -- and would still have to be reworked
/// when real multi-aggregate transactions arrive, because their boundaries are
/// not known yet. <see cref="AddAsync"/> and <see cref="UpdateAsync"/> therefore
/// commit, and say so. The trigger for revisiting this is concrete: the first use
/// case that must write two aggregates, or an aggregate and an outbox,
/// atomically.
/// </para>
/// </remarks>
public interface IAiAgentRepository
{
    /// <summary>
    /// Finds one agent within a tenant, together with the version it was read at.
    /// </summary>
    /// <param name="tenantId">The tenant that must own the agent.</param>
    /// <param name="agentId">The agent's identity.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The agent and its version, or <see langword="null"/> when this tenant has
    /// no agent with that id -- whether because no such agent exists at all, or
    /// because it exists and belongs to somebody else.
    /// </returns>
    /// <remarks>
    /// The two absences are deliberately indistinguishable, and the
    /// implementation must make them so by filtering on the tenant in the query
    /// itself rather than by fetching the agent and then comparing owners. A
    /// caller cannot tell "no such agent" from "not yours", so the API cannot
    /// leak the existence of another tenant's agent even by accident.
    /// <para>
    /// Absence is returned, not thrown. "No such agent" is an ordinary answer to
    /// a lookup, and making it an exception would turn a normal outcome into a
    /// stack unwind and an error-level log.
    /// </para>
    /// </remarks>
    Task<VersionedAiAgent?> FindAsync(
        TenantId tenantId,
        AiAgentId agentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a new agent and commits.
    /// </summary>
    /// <returns>The version the stored agent now has.</returns>
    /// <remarks>
    /// Creation is unconditional -- there is no previous version to state -- so
    /// it takes no expected version. The version it returns is what lets the
    /// caller immediately make a conditional write without reading the agent
    /// back.
    /// </remarks>
    /// <exception cref="Tenants.TenantNotFoundException">
    /// The owning tenant does not exist. Raised from the foreign key, which is
    /// defence in depth rather than the primary check: the use case verifies the
    /// tenant first so the ordinary "no such tenant" request never reaches the
    /// database as a constraint violation. This arm exists for the case the check
    /// cannot cover -- the tenant disappearing between the check and the insert --
    /// so that even then the caller is told what happened instead of receiving an
    /// unexplained failure.
    /// </exception>
    Task<ConcurrencyToken> AddAsync(AiAgent agent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits changes to an agent, but only if it is still at the version the
    /// caller expected.
    /// </summary>
    /// <param name="agent">The agent, loaded through <see cref="FindAsync"/> and mutated.</param>
    /// <param name="expected">
    /// The version the caller believed it was changing. This is the caller's
    /// version -- typically read minutes earlier by a person looking at a screen
    /// -- and <strong>not</strong> the version this request happened to load.
    /// </param>
    /// <returns>The new version, when the write succeeded.</returns>
    /// <remarks>
    /// <para>
    /// <strong>The expected version must reach the database, not merely be
    /// compared in memory.</strong> An implementation that checked the expected
    /// version against the loaded one and then wrote using the loaded version
    /// would still lose an update to a transaction that commits in between. The
    /// value supplied here is what the storage engine must test the row against.
    /// </para>
    /// <para>
    /// A no-op when the aggregate was not actually modified, which is the normal
    /// outcome of an idempotent lifecycle call: activating an already active agent
    /// changes nothing, so there is nothing to write. The precondition is still
    /// the caller's responsibility to have satisfied -- a write that changes
    /// nothing must not be a way to bypass it.
    /// </para>
    /// </remarks>
    /// <exception cref="AiAgentVersionMismatchException">
    /// The agent is no longer at the expected version. The write is refused rather
    /// than applied, so whoever changed it keeps their change.
    /// </exception>
    Task<ConcurrencyToken> UpdateAsync(
        AiAgent agent,
        ConcurrencyToken expected,
        CancellationToken cancellationToken = default);
}
