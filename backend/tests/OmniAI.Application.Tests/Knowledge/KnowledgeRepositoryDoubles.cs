using System.Globalization;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Knowledge;
using OmniAI.Application.Knowledge.ListKnowledgeDocuments;
using OmniAI.Application.Knowledge.Persistence;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Knowledge;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Tests.Knowledge;

/// <summary>
/// Stored documents, in memory, with real version tokens.
/// </summary>
/// <remarks>
/// The versions are real in the sense that matters: each write checks the token
/// the caller supplied against the one the row is at, and advances it. That is
/// what makes the precondition tests meaningful without a database.
/// </remarks>
internal sealed class InMemoryKnowledgeDocumentRepository : IKnowledgeDocumentRepository
{
    private readonly List<Row> _rows = [];

    /// <summary>How many times a write was attempted, successful or not.</summary>
    public int UpdateAttempts { get; private set; }

    public IReadOnlyList<KnowledgeDocument> Stored => [.. _rows.Select(row => row.Document)];

    public void Seed(KnowledgeDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        _rows.Add(new Row(document, Token(1)));
    }

    public ConcurrencyToken VersionOf(KnowledgeDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return _rows.Find(row => row.Document.Id == document.Id)?.Version
            ?? throw new InvalidOperationException("That document was never seeded.");
    }

    public Task<Versioned<KnowledgeDocument>?> FindAsync(
        TenantId tenantId,
        KnowledgeDocumentId documentId,
        CancellationToken cancellationToken = default)
    {
        Row? row = _rows.Find(candidate =>
            candidate.Document.Id == documentId && candidate.Document.TenantId == tenantId);

        return Task.FromResult(
            row is null ? null : new Versioned<KnowledgeDocument>(row.Document, row.Version));
    }

    public Task<KnowledgeDocumentPage> ListAsync(
        TenantId tenantId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        List<KnowledgeDocument> owned =
        [
            .. _rows
                .Select(row => row.Document)
                .Where(document => document.TenantId == tenantId)
                .OrderByDescending(document => document.UpdatedAt)
                .ThenBy(document => document.Id.Value),
        ];

        if (owned.Count == 0)
        {
            return Task.FromResult(KnowledgeDocumentPage.Empty(page, pageSize));
        }

        EmbeddingProfileId profile = EmbeddingProfileId.Create("test:fake:8");

        List<KnowledgeDocumentSummary> items =
        [
            .. owned
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(document => new KnowledgeDocumentSummary(
                    document.Id.Value,
                    document.TenantId.Value,
                    document.Title.Value,
                    document.IsEnabled,
                    document.ReadinessFor(profile),
                    document.Content.Value.Length,
                    document.CreatedAt,
                    document.UpdatedAt,
                    document.IndexedAt,
                    // Manual documents, in this double. The file-backed case is
                    // covered against the real repository, which is the only
                    // place the left join it needs can be exercised.
                    SourceFileType: null)),
        ];

        return Task.FromResult(new KnowledgeDocumentPage(items, page, pageSize, owned.Count));
    }

    public Task<IReadOnlyList<KnowledgeDocument>> ListAllAsync(
        TenantId tenantId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<KnowledgeDocument> owned =
        [
            .. _rows
                .Select(row => row.Document)
                .Where(document => document.TenantId == tenantId)
                .OrderBy(document => document.Title.Value, StringComparer.Ordinal)
                .ThenBy(document => document.Id.Value)
                .Take(limit),
        ];

        return Task.FromResult(owned);
    }

    public Task<ConcurrencyToken> AddAsync(
        KnowledgeDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        Row row = new(document, Token(1));

        _rows.Add(row);

        return Task.FromResult(row.Version);
    }

    public Task<ConcurrencyToken> UpdateAsync(
        KnowledgeDocument document,
        ConcurrencyToken expected,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        UpdateAttempts++;

        int index = _rows.FindIndex(candidate => candidate.Document.Id == document.Id);

        if (index < 0)
        {
            throw KnowledgeDocumentVersionMismatchException.ForDocument(document.Id.Value);
        }

        if (_rows[index].Version != expected)
        {
            throw KnowledgeDocumentVersionMismatchException.ForDocument(document.Id.Value);
        }

        ConcurrencyToken next = Advance(_rows[index].Version);

        _rows[index] = new Row(document, next);

        return Task.FromResult(next);
    }

    /// <summary>Moves the row on, as another writer committing would.</summary>
    public void SimulateCompetingWrite(KnowledgeDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        int index = _rows.FindIndex(candidate => candidate.Document.Id == document.Id);

        _rows[index] = new Row(_rows[index].Document, Advance(_rows[index].Version));
    }

    private static ConcurrencyToken Token(int value) =>
        ConcurrencyToken.From(value.ToString(CultureInfo.InvariantCulture));

    private static ConcurrencyToken Advance(ConcurrencyToken current) =>
        Token(int.Parse(current.Value, CultureInfo.InvariantCulture) + 1);

    private sealed record Row(KnowledgeDocument Document, ConcurrencyToken Version);
}

/// <summary>
/// An index store that records what it was asked to write.
/// </summary>
/// <remarks>
/// It performs the same version check the real one performs, so the Application
/// suite can prove the handler passes the caller's token through. What it cannot
/// model is atomicity, which is a database property and is proved against real
/// PostgreSQL.
/// </remarks>
internal sealed class RecordingKnowledgeIndexStore(InMemoryKnowledgeDocumentRepository documents)
    : IKnowledgeIndexStore
{
    private readonly InMemoryKnowledgeDocumentRepository _documents =
        documents ?? throw new ArgumentNullException(nameof(documents));

    /// <summary>How many times a whole chunk set was committed.</summary>
    public int Replacements { get; private set; }

    /// <summary>The chunks of the most recent replacement.</summary>
    public IReadOnlyList<KnowledgeChunkWrite> LastChunks { get; private set; } = [];

    public async Task<ConcurrencyToken> ReplaceIndexAsync(
        KnowledgeDocument document,
        ConcurrencyToken expected,
        IReadOnlyList<KnowledgeChunkWrite> chunks,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunks);

        ConcurrencyToken version = await _documents
            .UpdateAsync(document, expected, cancellationToken)
            .ConfigureAwait(false);

        Replacements++;
        LastChunks = [.. chunks];

        return version;
    }
}

/// <summary>
/// Assignments, in memory.
/// </summary>
internal sealed class InMemoryAgentKnowledgeRepository : IAgentKnowledgeRepository
{
    private readonly List<AgentKnowledgeAssignment> _rows = [];

    public IReadOnlyList<AgentKnowledgeAssignment> Stored => _rows;

    public Task<IReadOnlyList<KnowledgeDocumentId>> ListAssignedAsync(
        TenantId tenantId,
        AiAgentId aiAgentId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<KnowledgeDocumentId> assigned =
        [
            .. _rows
                .Where(row => row.TenantId == tenantId && row.AiAgentId == aiAgentId)
                .Select(row => row.KnowledgeDocumentId)
                .OrderBy(id => id.Value),
        ];

        return Task.FromResult(assigned);
    }

    public Task<ConcurrencyToken> ReplaceAsync(
        AiAgent agent,
        ConcurrencyToken expected,
        IReadOnlyList<KnowledgeDocumentId> documentIds,
        DateTimeOffset assignedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(documentIds);

        _rows.RemoveAll(row => row.TenantId == agent.TenantId && row.AiAgentId == agent.Id);

        foreach (KnowledgeDocumentId documentId in documentIds)
        {
            _rows.Add(AgentKnowledgeAssignment.Create(
                agent.TenantId,
                agent.Id,
                documentId,
                assignedAt));
        }

        return Task.FromResult(expected);
    }
}
