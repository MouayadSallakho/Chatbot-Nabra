using System.Data.Common;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using OmniAI.Application.Notifications;
using OmniAI.Domain.Notifications;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;
using OmniAI.Infrastructure.Persistence;

namespace OmniAI.Infrastructure.Notifications;

/// <summary>
/// Occurrences waiting to become notifications, in PostgreSQL.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The queue is the table.</strong> Claimed with <c>FOR UPDATE SKIP
/// LOCKED</c> and a fresh token per claim, so several instances take disjoint
/// work and one that dies leaves rows another resumes when the lease lapses.
/// Nothing lives only in process memory.
/// </para>
/// <para>
/// <strong>Completion is conditional on the claim.</strong> The occurrence row is
/// locked and its token compared before anything is written; a worker whose lease
/// lapsed writes nothing and is told so. Even if two did, each notification's
/// unique key would accept it once.
/// </para>
/// </remarks>
internal sealed class NotificationOccurrenceStore(OmniAiDbContext context)
    : INotificationOccurrenceStore, INotificationOccurrenceRecorder
{
    private readonly OmniAiDbContext _context = context ?? throw new ArgumentNullException(nameof(context));

    public Task RecordAsync(NotificationOccurrence occurrence, CancellationToken cancellationToken = default) =>
        NotificationOccurrenceSql.RecordAsync(_context, occurrence, cancellationToken);

    public async Task<IReadOnlyList<ClaimedOccurrence>> ClaimAsync(
        int batchSize,
        int maxAttempts,
        TimeSpan lease,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxAttempts);

        DateTimeOffset stale = now - lease;

        // Out of attempts and not currently held: closed, so a poisoned
        // occurrence stops being claimed rather than spinning forever.
        await using (NpgsqlCommand exhausted = await Command(
                """
                UPDATE notification_occurrences
                   SET processed_at = @now,
                       claim_token = NULL
                 WHERE processed_at IS NULL
                   AND attempts >= @max_attempts
                   AND (claim_token IS NULL OR claimed_at < @stale)
                """,
                cancellationToken)
            .ConfigureAwait(false))
        {
            exhausted.Parameters.AddWithValue("now", now.ToUniversalTime());
            exhausted.Parameters.AddWithValue("stale", stale.ToUniversalTime());
            exhausted.Parameters.AddWithValue("max_attempts", maxAttempts);

            await exhausted.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using NpgsqlCommand command = await Command(
                """
                UPDATE notification_occurrences
                   SET attempts = attempts + 1,
                       claim_token = gen_random_uuid(),
                       claimed_at = @now
                 WHERE id IN (
                    SELECT candidate.id
                      FROM notification_occurrences candidate
                     WHERE candidate.processed_at IS NULL
                       AND (candidate.claim_token IS NULL OR candidate.claimed_at < @stale)
                       AND candidate.attempts < @max_attempts
                     ORDER BY candidate.occurred_at, candidate.id
                     LIMIT @batch_size
                     FOR UPDATE SKIP LOCKED
                 )
                RETURNING id, tenant_id, kind, occurrence_key, source_id, claim_token, attempts
                """,
                cancellationToken)
            .ConfigureAwait(false);

        command.Parameters.AddWithValue("now", now.ToUniversalTime());
        command.Parameters.AddWithValue("stale", stale.ToUniversalTime());
        command.Parameters.AddWithValue("max_attempts", maxAttempts);
        command.Parameters.AddWithValue("batch_size", batchSize);

        List<ClaimedOccurrence> claimed = [];

        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            claimed.Add(new ClaimedOccurrence(
                NotificationOccurrenceId.From(reader.GetGuid(0)),
                TenantId.From(reader.GetGuid(1)),
                Enum.Parse<NotificationKind>(reader.GetString(2)),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetGuid(4),
                reader.GetGuid(5),
                reader.GetInt32(6)));
        }

        return claimed;
    }

    public async Task<IReadOnlyList<UserId>?> CompleteAsync(
        ClaimedOccurrence occurrence,
        OccurrenceOutcome outcome,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(occurrence);
        ArgumentNullException.ThrowIfNull(outcome);

        await using IDbContextTransaction transaction = await _context.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        await using (NpgsqlCommand held = await Command(
                """
                SELECT 1
                  FROM notification_occurrences
                 WHERE id = @id
                   AND claim_token = @token
                   AND processed_at IS NULL
                 FOR UPDATE
                """,
                cancellationToken)
            .ConfigureAwait(false))
        {
            held.Parameters.AddWithValue("id", occurrence.Id.Value);
            held.Parameters.AddWithValue("token", occurrence.ClaimToken);

            if (await held.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is null)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);

                return null;
            }
        }

        List<UserId> written = [];

        foreach (Notification notification in outcome.Notifications)
        {
            if (!await InsertNotificationAsync(notification, cancellationToken).ConfigureAwait(false))
            {
                // Already written by an earlier pass. Its email, if any, was
                // queued with it — so nothing more is queued now.
                continue;
            }

            written.Add(notification.RecipientUserId);

            if (outcome.EmailRecipients.Contains(notification.RecipientUserId))
            {
                await InsertEmailAsync(NotificationEmail.Queue(notification, now), cancellationToken).ConfigureAwait(false);
            }
        }

        await using (NpgsqlCommand done = await Command(
                """
                UPDATE notification_occurrences
                   SET processed_at = @now,
                       claim_token = NULL
                 WHERE id = @id
                """,
                cancellationToken)
            .ConfigureAwait(false))
        {
            done.Parameters.AddWithValue("now", now.ToUniversalTime());
            done.Parameters.AddWithValue("id", occurrence.Id.Value);

            await done.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return written;
    }

    public async Task<bool> ReleaseAsync(
        NotificationOccurrenceId id,
        Guid claimToken,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlCommand command = await Command(
                """
                UPDATE notification_occurrences
                   SET claim_token = NULL,
                       claimed_at = NULL
                 WHERE id = @id
                   AND claim_token = @token
                   AND processed_at IS NULL
                """,
                cancellationToken)
            .ConfigureAwait(false);

        command.Parameters.AddWithValue("id", id.Value);
        command.Parameters.AddWithValue("token", claimToken);

        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    /// <returns><see langword="false"/> when this recipient already has this notification.</returns>
    private async Task<bool> InsertNotificationAsync(Notification notification, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = await Command(
                """
                INSERT INTO notifications
                    (id, tenant_id, recipient_user_id, occurrence_id, kind, category, severity,
                     title, body, action_path, deduplication_key, created_at, read_at, expires_at)
                VALUES
                    (@id, @tenant_id, @recipient, @occurrence_id, @kind, @category, @severity,
                     @title, @body, @action_path, @dedup, @created_at, NULL, @expires_at)
                ON CONFLICT (tenant_id, recipient_user_id, deduplication_key) DO NOTHING
                """,
                cancellationToken)
            .ConfigureAwait(false);

        command.Parameters.AddWithValue("id", notification.Id.Value);
        command.Parameters.AddWithValue("tenant_id", notification.TenantId.Value);
        command.Parameters.AddWithValue("recipient", notification.RecipientUserId.Value);
        command.Parameters.AddWithValue("occurrence_id", notification.OccurrenceId.Value);
        command.Parameters.AddWithValue("kind", notification.Kind.ToString());
        command.Parameters.AddWithValue("category", notification.Category.ToString());
        command.Parameters.AddWithValue("severity", notification.Severity.ToString());
        command.Parameters.AddWithValue("title", notification.Title);
        command.Parameters.AddWithValue("body", notification.Body);
        command.Parameters.AddWithValue("action_path", notification.ActionPath is string path ? path : DBNull.Value);
        command.Parameters.AddWithValue("dedup", notification.DeduplicationKey);
        command.Parameters.AddWithValue("created_at", notification.CreatedAt.ToUniversalTime());
        command.Parameters.AddWithValue("expires_at", notification.ExpiresAt.ToUniversalTime());

        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    private async Task InsertEmailAsync(NotificationEmail email, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = await Command(
                """
                INSERT INTO notification_emails
                    (id, notification_id, tenant_id, recipient_user_id, category, status,
                     created_at, available_at, attempts)
                VALUES
                    (@id, @notification_id, @tenant_id, @recipient, @category, 'Pending',
                     @created_at, @available_at, 0)
                ON CONFLICT (notification_id) DO NOTHING
                """,
                cancellationToken)
            .ConfigureAwait(false);

        command.Parameters.AddWithValue("id", email.Id.Value);
        command.Parameters.AddWithValue("notification_id", email.NotificationId.Value);
        command.Parameters.AddWithValue("tenant_id", email.TenantId.Value);
        command.Parameters.AddWithValue("recipient", email.RecipientUserId.Value);
        command.Parameters.AddWithValue("category", email.Category.ToString());
        command.Parameters.AddWithValue("created_at", email.CreatedAt.ToUniversalTime());
        command.Parameters.AddWithValue("available_at", email.AvailableAt.ToUniversalTime());

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private Task<NpgsqlCommand> Command(string sql, CancellationToken cancellationToken) =>
        NotificationOccurrenceSql.CommandAsync(_context, sql, cancellationToken);
}
