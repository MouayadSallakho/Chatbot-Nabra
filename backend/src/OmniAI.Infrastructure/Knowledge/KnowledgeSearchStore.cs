using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using OmniAI.Application.Knowledge;
using OmniAI.Application.Knowledge.Persistence;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Knowledge;
using OmniAI.Domain.Tenants;
using OmniAI.Infrastructure.Persistence;

namespace OmniAI.Infrastructure.Knowledge;

/// <summary>
/// Vector search over a workspace's indexed knowledge, in PostgreSQL.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Every filter is in the SQL, and none of them is optional.</strong> The
/// workspace, the agent assignment, the document being enabled, the index
/// describing the document's current text, and the profile matching on both the
/// document and the chunk. A row that fails any one of them is not returned, is
/// not ranked, and never reaches the code that decides what to send a model.
/// </para>
/// <para>
/// <strong>Similarity is not, and can never be, the boundary.</strong> Two
/// businesses in the same industry write similar policies; a search that ranked
/// across everything and trusted the ranking to keep them apart would leak on the
/// first ordinary day rather than on an unlucky one. The workspace predicate is
/// what separates them, and it is applied by the database.
/// </para>
/// <para>
/// <strong>The fingerprint comparison is the interesting one.</strong>
/// <c>d.indexed_fingerprint = d.content_fingerprint</c> is what stops an edited
/// document answering from vectors that describe an older draft: the moment
/// somebody saves a change, the two stop matching and every chunk of that
/// document leaves the result set until it is indexed again. No status column, no
/// background job and no cache invalidation is involved.
/// </para>
/// <para>
/// <c>&lt;=&gt;</c> is pgvector's cosine distance operator, which is what the
/// HNSW index on the column is built for. The same operator appears in the
/// <c>ORDER BY</c> so the index can be used.
/// </para>
/// </remarks>
internal sealed class KnowledgeSearchStore(OmniAiDbContext context) : IKnowledgeSearchStore
{
    private const string EligibilityPredicate = """
             WHERE c.tenant_id = @tenant_id
               AND a.ai_agent_id = @agent_id
               AND d.is_enabled
               AND d.indexed_fingerprint IS NOT NULL
               AND d.indexed_fingerprint = d.content_fingerprint
               AND d.indexed_profile = @profile
               AND c.embedding_profile = @profile
        """;

    private const string EligibleChunks = """
            FROM knowledge_chunks c
            JOIN knowledge_documents d
              ON d.id = c.knowledge_document_id
             AND d.tenant_id = c.tenant_id
            JOIN ai_agent_knowledge_documents a
              ON a.knowledge_document_id = d.id
             AND a.tenant_id = d.tenant_id
        """;

    private readonly OmniAiDbContext _context =
        context ?? throw new ArgumentNullException(nameof(context));

    public async Task<bool> HasEligibleChunksAsync(
        TenantId tenantId,
        AiAgentId aiAgentId,
        EmbeddingProfileId profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        // EXISTS rather than COUNT: the question is whether there is anything at
        // all, and a workspace with fifty thousand chunks should not be counted to
        // find that out.
        return await OnRequestConnectionAsync(
                $"""
                SELECT EXISTS (
                  SELECT 1
                {EligibleChunks}
                {EligibilityPredicate}
                )
                """,
                async command =>
                {
                    Bind(command, tenantId, aiAgentId, profile);

                    object? answer = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

                    return answer is true;
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<KnowledgeMatch>> SearchAsync(
        TenantId tenantId,
        AiAgentId aiAgentId,
        EmbeddingProfileId profile,
        EmbeddingVector query,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        return await OnRequestConnectionAsync<IReadOnlyList<KnowledgeMatch>>(
                $"""
                SELECT d.id, d.title, c.ordinal, c.content,
                       c.embedding <=> @query::vector AS distance
                {EligibleChunks}
                {EligibilityPredicate}
                 ORDER BY c.embedding <=> @query::vector, d.id, c.ordinal
                 LIMIT @limit
                """,
                async command =>
                {
                    Bind(command, tenantId, aiAgentId, profile);

                    command.Parameters.AddWithValue("query", VectorLiterals.Render(query));
                    command.Parameters.AddWithValue("limit", limit);

                    List<KnowledgeMatch> matches = [];

                    await using DbDataReader reader = await command
                        .ExecuteReaderAsync(cancellationToken)
                        .ConfigureAwait(false);

                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        matches.Add(new KnowledgeMatch(
                            reader.GetGuid(0),
                            reader.GetString(1),
                            reader.GetInt32(2),
                            reader.GetString(3),
                            reader.GetDouble(4)));
                    }

                    return matches;
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static void Bind(
        NpgsqlCommand command,
        TenantId tenantId,
        AiAgentId aiAgentId,
        EmbeddingProfileId profile)
    {
        command.Parameters.AddWithValue("tenant_id", tenantId.Value);
        command.Parameters.AddWithValue("agent_id", aiAgentId.Value);
        command.Parameters.AddWithValue("profile", profile.Value);
    }

    /// <summary>
    /// Runs one command on the request's own connection, and gives the connection
    /// back as soon as it is done.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The EF connection rather than a fresh one, so a search taking place inside
    /// a request that is already holding a transaction sees that transaction, and
    /// so this does not open a second connection per turn.
    /// </para>
    /// <para>
    /// <strong>Opened and closed through EF, never directly (Phase 34).</strong> The
    /// connection used to be opened on the <c>DbConnection</c> itself, which EF does
    /// not track, so it stayed open -- checked out of the pool -- for the rest of
    /// the request. For a customer turn that is the whole provider call, during
    /// which the turn also holds its conversation-lock connection and the ledger
    /// asks for one more. Under load that exhausted PostgreSQL's connection limit,
    /// and with bounded pools it starved them: turns held one pool's connections
    /// while waiting for the other's, until the pool timeout (client-side
    /// starvation, not a database deadlock). <c>OpenConnectionAsync</c>/<c>CloseConnectionAsync</c> are
    /// reference-counted: inside a transaction (which EF opened) the connection
    /// stays open and enlisted; otherwise it returns to the pool here.
    /// </para>
    /// </remarks>
    private async Task<T> OnRequestConnectionAsync<T>(
        string sql,
        Func<NpgsqlCommand, Task<T>> run,
        CancellationToken cancellationToken)
    {
        await _context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using NpgsqlCommand command = new(sql, (NpgsqlConnection)_context.Database.GetDbConnection());

            if (_context.Database.CurrentTransaction?.GetDbTransaction() is NpgsqlTransaction active)
            {
                command.Transaction = active;
            }

            return await run(command).ConfigureAwait(false);
        }
        finally
        {
            await _context.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }
}
