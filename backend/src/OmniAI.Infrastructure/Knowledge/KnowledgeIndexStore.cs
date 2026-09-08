using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Knowledge;
using OmniAI.Application.Knowledge.Persistence;
using OmniAI.Domain.Knowledge;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Infrastructure.Persistence.Repositories;

namespace OmniAI.Infrastructure.Knowledge;

/// <summary>
/// Replaces a document's chunk set and its index metadata, atomically.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Chunks are written in SQL rather than through EF, deliberately.</strong>
/// Three reasons, and each one matters on its own. The vector column stays out of
/// the EF model entirely, so no query and no projection anywhere in the solution
/// can load or publish an embedding by accident. Replacing a set of chunks is one
/// <c>DELETE</c> and a handful of parameterised <c>INSERT</c>s rather than a
/// change-tracked graph of hundreds of entities. And the cast to <c>vector</c>
/// is written where somebody can see it.
/// </para>
/// <para>
/// <strong>One transaction, on one connection.</strong> The document update and
/// the chunk rewrite share the EF connection, so either both happen or neither
/// does. A failure part way through leaves the previous index intact -- never a
/// document marked ready sitting on half a chunk set.
/// </para>
/// <para>
/// <strong>The version is re-checked here, at the last possible moment.</strong>
/// The embeddings were made before this was called, which may have taken seconds.
/// If somebody edited the document in that window, EF's concurrency check fails
/// and the transaction is abandoned -- refusing to attach the vectors of one
/// draft to the text of another.
/// </para>
/// <para>
/// The provider is never called from inside this transaction. That is the whole
/// reason the embeddings are passed in already made.
/// </para>
/// </remarks>
internal sealed class KnowledgeIndexStore(OmniAiDbContext context) : IKnowledgeIndexStore
{
    private readonly OmniAiDbContext _context =
        context ?? throw new ArgumentNullException(nameof(context));

    public async Task<ConcurrencyToken> ReplaceIndexAsync(
        KnowledgeDocument document,
        ConcurrencyToken expected,
        IReadOnlyList<KnowledgeChunkWrite> chunks,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(chunks);

        PropertyEntry<KnowledgeDocument, uint> version = _context.Entry(document)
            .Property<uint>(PersistenceSchema.XminConcurrencyToken);

        if (!RowVersionTokens.TryDecode(expected, out uint expectedRowVersion))
        {
            throw KnowledgeDocumentVersionMismatchException.ForDocument(document.Id.Value);
        }

        version.OriginalValue = expectedRowVersion;

        KnowledgeDocumentRepository.StampFingerprint(_context.Entry(document), document);

        await using IDbContextTransaction transaction = await _context.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            // The concurrency check first, so a stale caller is refused before
            // several hundred rows are written and thrown away.
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException failure)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);

            throw KnowledgeDocumentVersionMismatchException.ForDocument(
                document.Id.Value, failure);
        }

        DbConnection connection = _context.Database.GetDbConnection();
        DbTransaction? active = transaction.GetDbTransaction();

        await DeleteChunksAsync(connection, active, document, cancellationToken)
            .ConfigureAwait(false);

        foreach (KnowledgeChunkWrite chunk in chunks)
        {
            await InsertChunkAsync(connection, active, document, chunk, cancellationToken)
                .ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return RowVersionTokens.Encode(version.CurrentValue);
    }

    private static async Task DeleteChunksAsync(
        DbConnection connection,
        DbTransaction? transaction,
        KnowledgeDocument document,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = Command(
            connection,
            transaction,
            """
            DELETE FROM knowledge_chunks
             WHERE tenant_id = @tenant_id
               AND knowledge_document_id = @document_id
            """);

        command.Parameters.AddWithValue("tenant_id", document.TenantId.Value);
        command.Parameters.AddWithValue("document_id", document.Id.Value);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertChunkAsync(
        DbConnection connection,
        DbTransaction? transaction,
        KnowledgeDocument document,
        KnowledgeChunkWrite chunk,
        CancellationToken cancellationToken)
    {
        // The tenant is written from the document rather than passed in
        // separately, so a chunk cannot be filed under a workspace its document
        // does not belong to. The composite foreign key would refuse it anyway;
        // this makes the attempt impossible rather than merely rejected.
        await using NpgsqlCommand command = Command(
            connection,
            transaction,
            """
            INSERT INTO knowledge_chunks (
                id, tenant_id, knowledge_document_id, ordinal, content,
                embedding, embedding_profile, created_at)
            VALUES (
                @id, @tenant_id, @document_id, @ordinal, @content,
                @embedding::vector, @profile, @created_at)
            """);

        command.Parameters.AddWithValue("id", Guid.CreateVersion7());
        command.Parameters.AddWithValue("tenant_id", document.TenantId.Value);
        command.Parameters.AddWithValue("document_id", document.Id.Value);
        command.Parameters.AddWithValue("ordinal", chunk.Ordinal);
        command.Parameters.AddWithValue("content", chunk.Text);
        command.Parameters.AddWithValue("embedding", VectorLiterals.Render(chunk.Embedding));
        command.Parameters.AddWithValue(
            "profile",
            document.IndexedProfile?.Value
            ?? throw new InvalidOperationException(
                "A chunk cannot be written for a document that has not been marked indexed."));
        command.Parameters.AddWithValue(
            "created_at",
            NpgsqlDbType.TimestampTz,
            document.IndexedAt ?? DateTimeOffset.UtcNow);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static NpgsqlCommand Command(
        DbConnection connection,
        DbTransaction? transaction,
        string sql)
    {
        NpgsqlCommand command = new(sql, (NpgsqlConnection)connection);

        if (transaction is NpgsqlTransaction npgsql)
        {
            command.Transaction = npgsql;
        }

        return command;
    }
}
