using System.Globalization;
using OmniAI.Application.AiAgents;
using OmniAI.Application.AiAgents.ListAiAgents;
using OmniAI.Application.AiAgents.Persistence;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Tenants;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Tests.AiAgents;

/// <summary>
/// A hand-written stand-in for the AI agent repository.
/// </summary>
/// <remarks>
/// Written out rather than produced by a mocking framework, for the same reasons
/// as <c>InMemoryTenantRepository</c>: the contract has four methods, this fake
/// is shorter than the setup code a framework would need, and it can express the
/// things that actually matter -- that tenant scoping happens in the store rather
/// than being decided by the handler, and that a version mismatch is raised by
/// the store rather than predicted by the caller. No mocking package is added to
/// the solution.
/// <para>
/// The Domain is never faked. <c>AiAgent</c>, <c>AiAgentName</c> and
/// <c>AgentInstructions</c> are the real types, so these tests exercise the real
/// invariants and the real lifecycle rules.
/// </para>
/// <para>
/// <strong>Its <see cref="FindAsync"/> matches on tenant and id together</strong>,
/// exactly as the real repository puts both in the SQL predicate. A fake that
/// matched on id alone would let the cross-tenant tests pass against a handler
/// that had no tenant scoping at all, which would make them worse than useless --
/// so the property under test is preserved here rather than assumed.
/// </para>
/// <para>
/// <strong>Its versions behave like PostgreSQL's.</strong> A write that actually
/// changes the row produces a new version; a write that changes nothing leaves
/// the version alone, because <c>xmin</c> only moves when a row is written. That
/// distinction is load-bearing: it is what lets these tests prove an idempotent
/// call returns the same version, and it is why the precondition has to be
/// checked by the use case rather than left to the store.
/// </para>
/// <para>
/// The token contents are deliberately unlike the real encoding. Nothing above
/// Infrastructure may depend on what is inside one, and using an obviously
/// different shape here is what proves it.
/// </para>
/// </remarks>
internal sealed class InMemoryAiAgentRepository : IAiAgentRepository
{
    private readonly List<Row> _rows = [];

    /// <summary>Agents currently stored, in insertion order.</summary>
    public IReadOnlyList<AiAgent> Stored => [.. _rows.Select(row => row.Agent)];

    /// <summary>How many times <see cref="UpdateAsync"/> was called.</summary>
    /// <remarks>
    /// Lets a test tell "the handler decided not to save" from "the handler saved
    /// something that happened to be unchanged", and -- with the precondition now
    /// in front of it -- "the handler refused before saving".
    /// </remarks>
    public int UpdateAttempts { get; private set; }

    /// <summary>
    /// When true, <see cref="AddAsync"/> reports the owning tenant as missing.
    /// </summary>
    /// <remarks>
    /// Stands in for the foreign key firing. It drives the case the use case
    /// check cannot cover -- the tenant disappearing between the check and the
    /// insert.
    /// </remarks>
    public bool PretendTenantVanished { get; init; }

    /// <summary>
    /// When true, <see cref="UpdateAsync"/> reports a version mismatch even for a
    /// caller whose expected version looked current.
    /// </summary>
    /// <remarks>
    /// Stands in for the race the use case cannot see: the row changing between
    /// this request reading it and writing it. The Application suite cannot
    /// produce a real one, so it verifies the one thing that is its own
    /// responsibility -- that the handler lets the failure travel to the caller
    /// rather than swallowing it or retrying.
    /// </remarks>
    public bool PretendRaceDuringSave { get; init; }

    public Task<VersionedAiAgent?> FindAsync(
        TenantId tenantId,
        AiAgentId agentId,
        CancellationToken cancellationToken = default)
    {
        Row? row = _rows.Find(candidate =>
            candidate.Agent.Id == agentId && candidate.Agent.TenantId == tenantId);

        return Task.FromResult(row is null ? null : new VersionedAiAgent(row.Agent, row.Version));
    }

    /// <summary>
    /// Reads a page of one tenant's agents, in the order the contract promises.
    /// </summary>
    /// <remarks>
    /// <strong>Filters by tenant, exactly as the real query does in SQL.</strong>
    /// A fake that returned every stored agent would let a cross-tenant test pass
    /// against a handler with no scoping at all, which would make it worse than
    /// no test -- so the property under test is preserved here rather than
    /// assumed.
    /// <para>
    /// The ordering is reproduced too, including the identity tie-break, so the
    /// Application suite can prove the handler asks for a deterministic page
    /// without needing a database to do it.
    /// </para>
    /// </remarks>
    public Task<AiAgentPage> ListAsync(
        TenantId tenantId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        List<AiAgent> owned = [.. _rows
            .Select(row => row.Agent)
            .Where(agent => agent.TenantId == tenantId)];

        if (owned.Count == 0)
        {
            return Task.FromResult(AiAgentPage.Empty(page, pageSize));
        }

        List<AiAgentSummary> items = [.. owned
            .OrderByDescending(agent => agent.UpdatedAt)
            .ThenBy(agent => agent.Id.Value)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(agent => new AiAgentSummary(
                agent.Id.Value,
                agent.TenantId.Value,
                agent.Name.Value,
                agent.Status,
                agent.CreatedAt,
                agent.UpdatedAt))];

        return Task.FromResult(new AiAgentPage(items, page, pageSize, owned.Count));
    }

    public Task<ConcurrencyToken> AddAsync(
        AiAgent agent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);

        if (PretendTenantVanished)
        {
            throw TenantNotFoundException.ForId(agent.TenantId.Value);
        }

        Row row = new(agent, 1);
        _rows.Add(row);

        return Task.FromResult(row.Version);
    }

    public Task<ConcurrencyToken> UpdateAsync(
        AiAgent agent,
        ConcurrencyToken expected,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);

        UpdateAttempts++;

        Row row = _rows.Find(candidate => candidate.Agent.Id == agent.Id)
            ?? throw new InvalidOperationException("The agent was never stored.");

        if (PretendRaceDuringSave || row.Version != expected)
        {
            throw AiAgentVersionMismatchException.ForAgent(agent.Id.Value);
        }

        // Only a write that changes something produces a new version, exactly as
        // xmin only moves when PostgreSQL actually writes the row.
        row.CommitIfChanged();

        return Task.FromResult(row.Version);
    }

    /// <summary>Seeds an agent without going through a use case.</summary>
    public InMemoryAiAgentRepository Seed(AiAgent agent)
    {
        ArgumentNullException.ThrowIfNull(agent);

        _rows.Add(new Row(agent, 1));

        return this;
    }

    /// <summary>The version an agent is currently at.</summary>
    public ConcurrencyToken VersionOf(AiAgent agent)
    {
        ArgumentNullException.ThrowIfNull(agent);

        return (_rows.Find(row => row.Agent.Id == agent.Id)
            ?? throw new InvalidOperationException("The agent was never stored.")).Version;
    }

    /// <summary>A stored row: the aggregate, its version, and what was last written.</summary>
    private sealed class Row
    {
        private int _version;
        private string _written;

        public Row(AiAgent agent, int version)
        {
            Agent = agent;
            _version = version;
            _written = Snapshot(agent);
        }

        public AiAgent Agent { get; }

        public ConcurrencyToken Version =>
            ConcurrencyToken.From(
                string.Create(CultureInfo.InvariantCulture, $"fake-version-{_version}"));

        public void CommitIfChanged()
        {
            string current = Snapshot(Agent);

            if (string.Equals(current, _written, StringComparison.Ordinal))
            {
                return;
            }

            _written = current;
            _version++;
        }

        private static string Snapshot(AiAgent agent) =>
            string.Create(
                CultureInfo.InvariantCulture,
                $"{agent.Name.Value}|{agent.Instructions.Value}|{agent.Status}|{agent.UpdatedAt:O}");
    }
}
