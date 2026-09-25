using Npgsql;
using OmniAI.Application.Knowledge.Persistence;
using OmniAI.Domain.Knowledge;
using OmniAI.Infrastructure.Notifications;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Infrastructure.Reliability;

namespace OmniAI.Infrastructure.Knowledge;

/// <summary>
/// Knowledge indexing attempts, in PostgreSQL.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Settlement and alert are one statement.</strong> A failed attempt and
/// its Phase 32 notification occurrence are written by the same CTE, so a crash
/// cannot record the failure without its alert, or the alert without the failure.
/// The occurrence key is the attempt id: one attempt, at most one occurrence,
/// whoever settles it.
/// </para>
/// <para>
/// <strong>Conditional on Started.</strong> Every settlement says
/// <c>WHERE status = 'Started'</c>, so the request that ran the attempt and the
/// stale-attempt reconciler can never both settle it, and a settled attempt never
/// moves again.
/// </para>
/// </remarks>
internal sealed class KnowledgeIndexAttemptStore(OmniAiDbContext context) : IKnowledgeIndexAttemptStore
{
    /// <summary>The alert for every row of a <c>settled</c> CTE whose outcome warrants one.</summary>
    internal const string RecordAlert = """
        ,
        recorded AS (
            INSERT INTO notification_occurrences
                (id, tenant_id, kind, occurrence_key, source_id, occurred_at, attempts)
            SELECT gen_random_uuid(), tenant_id, 'KnowledgeIndexingFailed',
                   document_id::text || ':' || id::text, document_id, @now, 0
              FROM settled
             WHERE alert
            ON CONFLICT (tenant_id, kind, occurrence_key) DO NOTHING
        )

        """;

    private readonly OmniAiDbContext _context = context ?? throw new ArgumentNullException(nameof(context));

    public async Task StartAsync(KnowledgeIndexAttempt attempt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        // Committed on its own, before any embedding request. It is the anchor.
        _context.KnowledgeIndexAttempts.Add(attempt);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _context.Entry(attempt).State = Microsoft.EntityFrameworkCore.EntityState.Detached;
        }
    }

    public async Task<bool> SucceedAsync(Guid attemptId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await using NpgsqlCommand command = await NotificationOccurrenceSql.CommandAsync(
                _context,
                """
                UPDATE knowledge_index_attempts
                   SET status = 'Succeeded', completed_at = @now
                 WHERE id = @id AND status = 'Started'
                """,
                cancellationToken)
            .ConfigureAwait(false);

        command.Parameters.AddWithValue("now", now.ToUniversalTime());
        command.Parameters.AddWithValue("id", attemptId);

        bool settled = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;

        if (settled)
        {
            OmniAiMetrics.KnowledgeAttempt(nameof(KnowledgeIndexAttemptStatus.Succeeded));
        }

        return settled;
    }

    public async Task<bool> SettleAsync(
        Guid attemptId,
        KnowledgeIndexAttemptStatus status,
        string failureCode,
        bool alert,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (status is not (KnowledgeIndexAttemptStatus.Failed or KnowledgeIndexAttemptStatus.Unknown))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "Only Failed or Unknown settle an attempt that did not succeed.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);

        await using NpgsqlCommand command = await NotificationOccurrenceSql.CommandAsync(
                _context,
                """
                WITH settled AS (
                    UPDATE knowledge_index_attempts
                       SET status = @status, failure_code = @code, completed_at = @now
                     WHERE id = @id AND status = 'Started'
                    RETURNING id, tenant_id, document_id, @alert AS alert
                )
                """ + RecordAlert + """
                SELECT count(*) FROM settled
                """,
                cancellationToken)
            .ConfigureAwait(false);

        command.Parameters.AddWithValue("status", status.ToString());
        command.Parameters.AddWithValue("code", failureCode.Length <= KnowledgeIndexAttemptFailures.MaxLength
            ? failureCode
            : failureCode[..KnowledgeIndexAttemptFailures.MaxLength]);
        command.Parameters.AddWithValue("now", now.ToUniversalTime());
        command.Parameters.AddWithValue("id", attemptId);
        command.Parameters.AddWithValue("alert", alert);

        bool settled = (long)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? 0L) == 1;

        if (settled)
        {
            OmniAiMetrics.KnowledgeAttempt(status.ToString());
        }

        return settled;
    }
}
