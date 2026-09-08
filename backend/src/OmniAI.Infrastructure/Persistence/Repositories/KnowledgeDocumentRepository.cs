using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Npgsql;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Knowledge;
using OmniAI.Application.Knowledge.ListKnowledgeDocuments;
using OmniAI.Application.Knowledge.Persistence;
using OmniAI.Application.Tenants;
using OmniAI.Domain.Knowledge;
using OmniAI.Domain.Tenants;

namespace OmniAI.Infrastructure.Persistence.Repositories;

/// <summary>Stored knowledge documents, over EF Core and PostgreSQL.</summary>
/// <remarks>
/// The <c>xmin</c> system column is the concurrency token, exactly as it is for
/// tenants, roles and agents. It is a shadow property and never a real column: a
/// physical column called <c>xmin</c> would shadow PostgreSQL's own and silently
/// disable every precondition built on it. A schema test asserts it.
/// </remarks>
internal sealed class KnowledgeDocumentRepository(
    OmniAiDbContext context,
    ITextEmbeddingGenerator embeddings) : IKnowledgeDocumentRepository
{
    private readonly OmniAiDbContext _context =
        context ?? throw new ArgumentNullException(nameof(context));

    private readonly ITextEmbeddingGenerator _embeddings =
        embeddings ?? throw new ArgumentNullException(nameof(embeddings));

    public async Task<Versioned<KnowledgeDocument>?> FindAsync(
        TenantId tenantId,
        KnowledgeDocumentId documentId,
        CancellationToken cancellationToken = default)
    {
        KnowledgeDocument? document = await _context.KnowledgeDocuments
            .FirstOrDefaultAsync(
                candidate => candidate.Id == documentId && candidate.TenantId == tenantId,
                cancellationToken)
            .ConfigureAwait(false);

        return document is null
            ? null
            : new Versioned<KnowledgeDocument>(document, CurrentVersion(document));
    }

    public async Task<KnowledgeDocumentPage> ListAsync(
        TenantId tenantId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        IQueryable<KnowledgeDocument> owned = _context.KnowledgeDocuments
            .Where(candidate => candidate.TenantId == tenantId);

        int totalCount = await owned.CountAsync(cancellationToken).ConfigureAwait(false);

        if (totalCount == 0)
        {
            return KnowledgeDocumentPage.Empty(page, pageSize);
        }

        // Projected in the database, and content is deliberately reduced to its
        // length rather than selected. A workspace with two hundred documents of a
        // hundred thousand characters would otherwise move twenty megabytes across
        // the wire to render a list of titles.
        List<KnowledgeDocumentRow> rows = await owned
            .OrderByDescending(candidate => candidate.UpdatedAt)
            .ThenBy(candidate => candidate.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(candidate => new KnowledgeDocumentRow(
                candidate.Id.Value,
                candidate.TenantId.Value,
                candidate.Title.Value,
                candidate.IsEnabled,
                candidate.Content.Value.Length,
                candidate.IndexedFingerprint,
                candidate.IndexedProfile,
                candidate.CreatedAt,
                candidate.UpdatedAt,
                candidate.IndexedAt,
                EF.Property<byte[]>(candidate, PersistenceSchema.KnowledgeContentFingerprint)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        EmbeddingProfileId profile = _embeddings.Profile.Id;

        return new KnowledgeDocumentPage(
            [.. rows.Select(row => row.ToSummary(profile))],
            page,
            pageSize,
            totalCount);
    }

    public async Task<IReadOnlyList<KnowledgeDocument>> ListAllAsync(
        TenantId tenantId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        return await _context.KnowledgeDocuments
            .Where(candidate => candidate.TenantId == tenantId)
            .OrderBy(candidate => candidate.Title)
            .ThenBy(candidate => candidate.Id)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ConcurrencyToken> AddAsync(
        KnowledgeDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        _context.KnowledgeDocuments.Add(document);

        StampFingerprint(document);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException failure) when (IsTenantForeignKeyViolation(failure))
        {
            _context.Entry(document).State = EntityState.Detached;

            throw TenantNotFoundException.ForId(document.TenantId.Value, failure);
        }

        return CurrentVersion(document);
    }

    public async Task<ConcurrencyToken> UpdateAsync(
        KnowledgeDocument document,
        ConcurrencyToken expected,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        PropertyEntry<KnowledgeDocument, uint> version = RowVersion(document);

        if (!RowVersionTokens.TryDecode(expected, out uint expectedRowVersion))
        {
            throw KnowledgeDocumentVersionMismatchException.ForDocument(document.Id.Value);
        }

        version.OriginalValue = expectedRowVersion;

        StampFingerprint(document);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException failure)
        {
            throw KnowledgeDocumentVersionMismatchException.ForDocument(
                document.Id.Value, failure);
        }

        return CurrentVersion(document);
    }

    /// <summary>
    /// Writes the fingerprint of whatever content the document is holding.
    /// </summary>
    /// <remarks>
    /// Derived here rather than trusted from anywhere, so the stored column and
    /// the stored content can never disagree. It has to be a column because the
    /// retrieval query compares it against the indexed fingerprint inside the
    /// database, which is what keeps an edited document out of every search until
    /// somebody indexes it again.
    /// </remarks>
    internal static void StampFingerprint(
        EntityEntry<KnowledgeDocument> entry,
        KnowledgeDocument document) =>
        entry.Property<byte[]>(PersistenceSchema.KnowledgeContentFingerprint).CurrentValue =
            document.Fingerprint.ToArray();

    private void StampFingerprint(KnowledgeDocument document) =>
        StampFingerprint(_context.Entry(document), document);

    private ConcurrencyToken CurrentVersion(KnowledgeDocument document) =>
        RowVersionTokens.Encode(RowVersion(document).CurrentValue);

    private PropertyEntry<KnowledgeDocument, uint> RowVersion(KnowledgeDocument document) =>
        _context.Entry(document).Property<uint>(PersistenceSchema.XminConcurrencyToken);

    private static bool IsTenantForeignKeyViolation(DbUpdateException failure) =>
        failure.InnerException is PostgresException postgres
        && postgres.SqlState == PostgresErrorCodes.ForeignKeyViolation
        && string.Equals(
            postgres.ConstraintName,
            "fk_knowledge_documents_tenant",
            StringComparison.Ordinal);

    /// <summary>
    /// The columns a list row needs, before readiness is worked out.
    /// </summary>
    /// <remarks>
    /// A separate shape because readiness is derived from a comparison EF cannot
    /// translate -- the fingerprint is a value object with structural equality,
    /// and the profile comes from the running deployment rather than from a
    /// column. Reading the four raw values and deciding in memory is honest;
    /// pushing a half-translated comparison into SQL would not be.
    /// </remarks>
    private sealed record KnowledgeDocumentRow(
        Guid Id,
        Guid TenantId,
        string Title,
        bool IsEnabled,
        int ContentLength,
        ContentFingerprint? IndexedFingerprint,
        EmbeddingProfileId? IndexedProfile,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        DateTimeOffset? IndexedAt,
        byte[] ContentFingerprint)
    {
        public KnowledgeDocumentSummary ToSummary(EmbeddingProfileId profile) =>
            new(
                Id,
                TenantId,
                Title,
                IsEnabled,
                Readiness(profile),
                ContentLength,
                CreatedAt,
                UpdatedAt,
                IndexedAt);

        private KnowledgeReadiness Readiness(EmbeddingProfileId profile)
        {
            if (!IsEnabled)
            {
                return KnowledgeReadiness.Disabled;
            }

            bool current = IndexedFingerprint is not null
                && IndexedProfile == profile
                && IndexedFingerprint == Domain.Knowledge.ContentFingerprint.From(
                    ContentFingerprint);

            return current ? KnowledgeReadiness.Ready : KnowledgeReadiness.NeedsIndex;
        }
    }
}
