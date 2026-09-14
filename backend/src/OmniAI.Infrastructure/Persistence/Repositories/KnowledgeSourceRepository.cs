using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Knowledge;
using OmniAI.Application.Knowledge.Persistence;
using OmniAI.Domain.Knowledge;
using OmniAI.Domain.Tenants;

namespace OmniAI.Infrastructure.Persistence.Repositories;

/// <summary>
/// Knowledge sources, in PostgreSQL.
/// </summary>
/// <remarks>
/// <strong>A source and its document are written together or not at all.</strong>
/// Both writes go through one <c>SaveChanges</c>, which EF runs in a single
/// transaction — so a crash between them is not a state this can reach. A source
/// describing a file whose text was never stored, or a document whose provenance
/// was lost, are both unrepairable afterwards.
/// </remarks>
internal sealed class KnowledgeSourceRepository(OmniAiDbContext context)
    : IKnowledgeSourceRepository
{
    private readonly OmniAiDbContext _context =
        context ?? throw new ArgumentNullException(nameof(context));

    public async Task<KnowledgeSource?> FindAsync(
        TenantId tenantId,
        KnowledgeDocumentId documentId,
        CancellationToken cancellationToken = default)
    {
        // The workspace is in the predicate rather than checked afterwards. A
        // document id from somewhere else finds nothing, which is a lookup that
        // cannot leak rather than one relying on a later line being right.
        return await _context.KnowledgeSources
            .FirstOrDefaultAsync(
                source => source.KnowledgeDocumentId == documentId
                    && source.TenantId == tenantId,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<Guid, KnowledgeSource>> ListForAsync(
        TenantId tenantId,
        IReadOnlyCollection<KnowledgeDocumentId> documentIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documentIds);

        if (documentIds.Count == 0)
        {
            return new Dictionary<Guid, KnowledgeSource>();
        }

        List<Guid> ids = [.. documentIds.Select(id => id.Value)];

        List<KnowledgeSource> sources = await _context.KnowledgeSources
            .AsNoTracking()
            .Where(source =>
                source.TenantId == tenantId
                && ids.Contains(source.KnowledgeDocumentId.Value))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return sources.ToDictionary(source => source.KnowledgeDocumentId.Value);
    }

    public async Task<ConcurrencyToken> AddAsync(
        KnowledgeDocument document,
        KnowledgeSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(source);

        _context.KnowledgeDocuments.Add(document);
        _context.KnowledgeSources.Add(source);

        // The same stamp the document repository applies, for the same reason:
        // the fingerprint is a column so retrieval can compare it inside the
        // database, and a document written down another path without it would be
        // a row no query could reason about.
        KnowledgeDocumentRepository.StampFingerprint(_context.Entry(document), document);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return VersionOf(document);
    }

    public async Task<ConcurrencyToken> ReplaceAsync(
        KnowledgeDocument document,
        KnowledgeSource source,
        ConcurrencyToken expected,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(source);

        // The version the caller read goes back into the tracked document, so it
        // lands in the UPDATE's WHERE clause and the database decides whether the
        // row is still what they believed. The source is carried along in the
        // same transaction; the document's version is the one that speaks for
        // both, because it is the one an entity tag was issued for.
        RowVersion(document).OriginalValue =
            RowVersionTokens.TryDecode(expected, out uint rowVersion) ? rowVersion : 0u;

        KnowledgeDocumentRepository.StampFingerprint(_context.Entry(document), document);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw KnowledgeDocumentVersionMismatchException.ForDocument(document.Id.Value);
        }

        return VersionOf(document);
    }

    private ConcurrencyToken VersionOf(KnowledgeDocument document) =>
        RowVersionTokens.Encode(RowVersion(document).CurrentValue);

    private PropertyEntry<KnowledgeDocument, uint> RowVersion(KnowledgeDocument document) =>
        _context.Entry(document).Property<uint>(PersistenceSchema.XminConcurrencyToken);
}
