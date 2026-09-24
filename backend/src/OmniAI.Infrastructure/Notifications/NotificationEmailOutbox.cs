using System.Data.Common;
using Npgsql;
using OmniAI.Application.Notifications;
using OmniAI.Domain.Notifications;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;
using OmniAI.Infrastructure.Persistence;

namespace OmniAI.Infrastructure.Notifications;

/// <summary>
/// Queued operational emails, in PostgreSQL.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Claimed into Sending, settled by token.</strong> A claim moves a row to
/// Sending with a fresh token; every settlement says <c>WHERE claim_token =
/// @token</c>, so a worker that lost its claim cannot overwrite the outcome of the
/// one that took over.
/// </para>
/// <para>
/// <strong>A lapsed Sending row becomes Unknown, never Pending.</strong> A worker
/// that died mid-send may have handed the message over first. Putting it back in
/// the queue would risk a duplicate in somebody's inbox; saying the outcome is
/// unknown is the truth.
/// </para>
/// </remarks>
internal sealed class NotificationEmailOutbox(OmniAiDbContext context) : INotificationEmailOutbox
{
    private readonly OmniAiDbContext _context = context ?? throw new ArgumentNullException(nameof(context));

    public async Task<IReadOnlyList<ClaimedEmail>> ClaimAsync(
        int batchSize,
        int maxAttempts,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        await using NpgsqlCommand command = await Command(
                """
                UPDATE notification_emails
                   SET status = 'Sending',
                       attempts = attempts + 1,
                       claim_token = gen_random_uuid(),
                       claimed_at = @now
                 WHERE id IN (
                    SELECT candidate.id
                      FROM notification_emails candidate
                     WHERE candidate.status = 'Pending'
                       AND candidate.available_at <= @now
                       AND candidate.attempts < @max_attempts
                     ORDER BY candidate.available_at, candidate.id
                     LIMIT @batch_size
                     FOR UPDATE SKIP LOCKED
                 )
                RETURNING id, notification_id, tenant_id, recipient_user_id, category, claim_token, attempts
                """,
                cancellationToken)
            .ConfigureAwait(false);

        command.Parameters.AddWithValue("now", now.ToUniversalTime());
        command.Parameters.AddWithValue("max_attempts", maxAttempts);
        command.Parameters.AddWithValue("batch_size", batchSize);

        List<ClaimedEmail> claimed = [];

        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            claimed.Add(new ClaimedEmail(
                NotificationEmailId.From(reader.GetGuid(0)),
                NotificationId.From(reader.GetGuid(1)),
                TenantId.From(reader.GetGuid(2)),
                UserId.From(reader.GetGuid(3)),
                Enum.Parse<NotificationCategory>(reader.GetString(4)),
                reader.GetGuid(5),
                reader.GetInt32(6)));
        }

        return claimed;
    }

    public async Task<bool> SettleAsync(
        NotificationEmailId id,
        Guid claimToken,
        NotificationEmailStatus status,
        string? failureCode,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (status is NotificationEmailStatus.Pending or NotificationEmailStatus.Sending)
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "Only a final outcome settles an email.");
        }

        await using NpgsqlCommand command = await Command(
                """
                UPDATE notification_emails
                   SET status = @status,
                       failure_code = @code,
                       sent_at = CASE WHEN @status = 'Sent' THEN @now ELSE NULL END,
                       settled_at = @now,
                       claim_token = NULL
                 WHERE id = @id
                   AND status = 'Sending'
                   AND claim_token = @token
                """,
                cancellationToken)
            .ConfigureAwait(false);

        command.Parameters.AddWithValue("status", status.ToString());
        command.Parameters.AddWithValue("code", Bounded(failureCode) is string code ? code : DBNull.Value);
        command.Parameters.AddWithValue("now", now.ToUniversalTime());
        command.Parameters.AddWithValue("id", id.Value);
        command.Parameters.AddWithValue("token", claimToken);

        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    public async Task<bool> RetryLaterAsync(
        NotificationEmailId id,
        Guid claimToken,
        string failureCode,
        DateTimeOffset availableAt,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlCommand command = await Command(
                """
                UPDATE notification_emails
                   SET status = 'Pending',
                       failure_code = @code,
                       available_at = @available_at,
                       claim_token = NULL,
                       claimed_at = NULL
                 WHERE id = @id
                   AND status = 'Sending'
                   AND claim_token = @token
                """,
                cancellationToken)
            .ConfigureAwait(false);

        command.Parameters.AddWithValue("code", Bounded(failureCode) ?? NotificationEmailFailures.ConnectFailed);
        command.Parameters.AddWithValue("available_at", availableAt.ToUniversalTime());
        command.Parameters.AddWithValue("id", id.Value);
        command.Parameters.AddWithValue("token", claimToken);

        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    public async Task<int> AbandonStaleAsync(
        DateTimeOffset claimedBefore,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlCommand command = await Command(
                """
                UPDATE notification_emails
                   SET status = 'Unknown',
                       failure_code = @code,
                       settled_at = @now,
                       claim_token = NULL
                 WHERE status = 'Sending'
                   AND claimed_at < @before
                """,
                cancellationToken)
            .ConfigureAwait(false);

        command.Parameters.AddWithValue("code", NotificationEmailFailures.Abandoned);
        command.Parameters.AddWithValue("now", now.ToUniversalTime());
        command.Parameters.AddWithValue("before", claimedBefore.ToUniversalTime());

        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string? Bounded(string? code) =>
        code is null || code.Length <= NotificationEmail.MaxFailureCodeLength
            ? code
            : code[..NotificationEmail.MaxFailureCodeLength];

    private Task<NpgsqlCommand> Command(string sql, CancellationToken cancellationToken) =>
        NotificationOccurrenceSql.CommandAsync(_context, sql, cancellationToken);
}
