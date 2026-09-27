using Npgsql;
using OmniAI.Infrastructure.Knowledge;
using OmniAI.Infrastructure.Notifications;
using OmniAI.Infrastructure.Persistence;

namespace OmniAI.Infrastructure.Reliability;

/// <summary>
/// Settles provider operations a crashed process left <c>Started</c>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Started → Unknown, and nothing else.</strong> Phase 23 writes Started
/// before a provider is called. If the process died after that, whether the
/// provider executed — and billed — the request is not knowable from here, so the
/// truthful state is Unknown. Never Failed (it may have run) and never Succeeded
/// (nothing was reported). No provider is called, no request is retried, and no
/// token count or cost is invented: those columns are left exactly as they were.
/// </para>
/// <para>
/// <strong>Quota is unchanged.</strong> Phase 24 counts every ledger row whatever
/// its status, so settling Started as Unknown neither frees nor consumes a slot.
/// </para>
/// <para>
/// <strong>Multi-instance safe.</strong> Candidates are chosen with
/// <c>FOR UPDATE SKIP LOCKED</c> and the update repeats <c>status = 'Started'</c>,
/// so two reconcilers never transition the same row and a row an in-flight
/// request settles first is left alone.
/// </para>
/// <para>
/// <strong>Bounded by a materialized batch.</strong> The candidates are chosen
/// once, in a <c>MATERIALIZED</c> CTE the update joins to. The shorter
/// <c>WHERE id IN (SELECT … LIMIT n FOR UPDATE SKIP LOCKED)</c> form is not
/// bounded: PostgreSQL may rescan the limited subquery and settle more than
/// <c>n</c> rows in one statement, which a Phase 33 test caught.
/// </para>
/// </remarks>
internal sealed class ProviderUsageReconciler(OmniAiDbContext context)
{
    private readonly OmniAiDbContext _context = context ?? throw new ArgumentNullException(nameof(context));

    /// <returns>How many operations were settled as Unknown.</returns>
    public async Task<int> ReconcileAsync(DateTimeOffset startedBefore, DateTimeOffset now, int batchSize, CancellationToken cancellationToken = default)
    {
        await using NpgsqlCommand command = await NotificationOccurrenceSql.CommandAsync(
                _context,
                """
                WITH stale AS MATERIALIZED (
                    SELECT candidate.id
                      FROM ai_provider_usages candidate
                     WHERE candidate.status = 'Started'
                       AND candidate.started_at < @cutoff
                     ORDER BY candidate.started_at, candidate.id
                     LIMIT @batch_size
                     FOR UPDATE SKIP LOCKED
                )
                UPDATE ai_provider_usages usage
                   SET status = 'Unknown', completed_at = @now
                  FROM stale
                 WHERE usage.id = stale.id
                   AND usage.status = 'Started'
                """,
                cancellationToken)
            .ConfigureAwait(false);

        command.Parameters.AddWithValue("now", now.ToUniversalTime());
        command.Parameters.AddWithValue("cutoff", startedBefore.ToUniversalTime());
        command.Parameters.AddWithValue("batch_size", batchSize);

        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Settles Knowledge indexing attempts a crashed process left <c>Started</c>, and
/// raises each one's alert exactly once.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Truthful, not optimistic.</strong> If the document's index was written
/// at or after the attempt started, the attempt evidently got as far as writing
/// and is settled Succeeded, with no alert. Otherwise nothing was written, so the
/// attempt is Unknown / <c>interrupted</c> — the provider may have received some
/// batches — and the workspace's knowledge managers are told, through the same
/// Phase 32 occurrence the explicit failure path uses, keyed by the attempt.
/// </para>
/// <para>
/// <strong>No automatic retry.</strong> Embedding costs money; a member indexes
/// again through the normal workflow if they want to.
/// </para>
/// </remarks>
internal sealed class KnowledgeAttemptReconciler(OmniAiDbContext context)
{
    private readonly OmniAiDbContext _context = context ?? throw new ArgumentNullException(nameof(context));

    /// <returns>How many attempts were settled.</returns>
    public async Task<int> ReconcileAsync(DateTimeOffset startedBefore, DateTimeOffset now, int batchSize, CancellationToken cancellationToken = default)
    {
        await using NpgsqlCommand command = await NotificationOccurrenceSql.CommandAsync(
                _context,
                """
                WITH stale AS MATERIALIZED (
                    SELECT candidate.id
                      FROM knowledge_index_attempts candidate
                     WHERE candidate.status = 'Started'
                       AND candidate.started_at < @cutoff
                     ORDER BY candidate.started_at, candidate.id
                     LIMIT @batch_size
                     FOR UPDATE SKIP LOCKED
                ),
                judged AS (
                    SELECT attempt.id,
                           EXISTS (
                               SELECT 1
                                 FROM knowledge_documents document
                                WHERE document.id = attempt.document_id
                                  AND document.tenant_id = attempt.tenant_id
                                  AND document.indexed_at >= attempt.started_at
                           ) AS written
                      FROM knowledge_index_attempts attempt
                      JOIN stale ON stale.id = attempt.id
                ),
                settled AS (
                    UPDATE knowledge_index_attempts attempt
                       SET status = CASE WHEN judged.written THEN 'Succeeded' ELSE 'Unknown' END,
                           failure_code = CASE WHEN judged.written THEN NULL ELSE @code END,
                           completed_at = @now
                      FROM judged
                     WHERE attempt.id = judged.id
                       AND attempt.status = 'Started'
                    RETURNING attempt.id, attempt.tenant_id, attempt.document_id, NOT judged.written AS alert
                )
                """ + KnowledgeIndexAttemptStore.RecordAlert + """
                SELECT count(*) FROM settled
                """,
                cancellationToken)
            .ConfigureAwait(false);

        command.Parameters.AddWithValue("now", now.ToUniversalTime());
        command.Parameters.AddWithValue("cutoff", startedBefore.ToUniversalTime());
        command.Parameters.AddWithValue("batch_size", batchSize);
        command.Parameters.AddWithValue("code", Domain.Knowledge.KnowledgeIndexAttemptFailures.Interrupted);

        return (int)(long)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? 0L);
    }
}

/// <summary>
/// Settles conversation turns a crashed or restarted process left
/// <c>Generating</c> (Phase 35).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Generating → Failed: "no reply was produced", which is true.</strong>
/// A reply is stored in the same transaction that completes its turn, so a turn
/// still Generating has no reply. The customer's message stays exactly where it
/// is. A turn that somehow names a reply is settled Completed instead; the CASE is
/// there so the statement can never contradict the row it reads.
/// </para>
/// <para>
/// <strong>No provider is called and nothing is regenerated.</strong> Whether the
/// provider billed the interrupted request is the ledger's to record (its own
/// reconciler settles it Unknown). A resend with the same client message id is
/// then told the turn failed, instead of "in progress" for ever, and is not
/// answered again: the existing rule that a failed turn is never retried, because
/// a reply costs money and may have been half-produced.
/// </para>
/// <para>
/// Sources: a process that died mid-turn, and a turn whose request was cancelled
/// (a client disconnect or shutdown), which the runner deliberately does not settle.
/// </para>
/// <para>
/// <strong>Safe against a live turn.</strong> The threshold is validated beyond the
/// longest a turn can legitimately stay open. A completion that nevertheless lands
/// afterwards overwrites Failed with Completed and its stored reply: late, but
/// truthful, and never a second reply.
/// </para>
/// <para>
/// Multi-instance safe and bounded exactly like the other reconcilers:
/// <c>MATERIALIZED</c> candidates with <c>FOR UPDATE SKIP LOCKED</c>, and the
/// update repeats <c>status = 'Generating'</c>.
/// </para>
/// </remarks>
internal sealed class ConversationTurnReconciler(OmniAiDbContext context)
{
    private readonly OmniAiDbContext _context = context ?? throw new ArgumentNullException(nameof(context));

    /// <returns>How many turns were settled.</returns>
    public async Task<int> ReconcileAsync(DateTimeOffset startedBefore, DateTimeOffset now, int batchSize, CancellationToken cancellationToken = default)
    {
        await using NpgsqlCommand command = await NotificationOccurrenceSql.CommandAsync(
                _context,
                """
                WITH stale AS MATERIALIZED (
                    SELECT candidate.id
                      FROM conversation_turns candidate
                     WHERE candidate.status = 'Generating'
                       AND candidate.created_at < @cutoff
                     ORDER BY candidate.created_at, candidate.id
                     LIMIT @batch_size
                     FOR UPDATE SKIP LOCKED
                )
                UPDATE conversation_turns turn
                   SET status = CASE WHEN turn.reply_message_id IS NULL THEN 'Failed' ELSE 'Completed' END,
                       updated_at = GREATEST(turn.updated_at, @now)
                  FROM stale
                 WHERE turn.id = stale.id
                   AND turn.status = 'Generating'
                """,
                cancellationToken)
            .ConfigureAwait(false);

        command.Parameters.AddWithValue("now", now.ToUniversalTime());
        command.Parameters.AddWithValue("cutoff", startedBefore.ToUniversalTime());
        command.Parameters.AddWithValue("batch_size", batchSize);

        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
