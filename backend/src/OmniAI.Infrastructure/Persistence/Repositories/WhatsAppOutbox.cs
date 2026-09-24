using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using OmniAI.Application.WhatsApp.Persistence;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.WhatsApp;

namespace OmniAI.Infrastructure.Persistence.Repositories;

/// <summary>
/// The durable outbound outbox, in PostgreSQL.
/// </summary>
/// <remarks>
/// <strong>A message is queued in the same transaction as the reply it carries,
/// and sent outside it.</strong> That is the whole shape: the transcript and the
/// intent to deliver commit together, so a crash cannot lose a reply the customer
/// was promised, and the network call happens afterwards, where it cannot hold a
/// row lock or be rolled back after Meta has already delivered it.
/// <para>
/// <strong>A stale send is abandoned, never re-queued.</strong> A worker that
/// died after calling Meta may have had its message accepted. Sending again would
/// show the customer the same words twice, which is worse than a row somebody has
/// to look at, so the row is marked unknown and stays that way.
/// </para>
/// </remarks>
internal sealed class WhatsAppOutbox(OmniAiDbContext context) : IWhatsAppOutbox
{
    private const string UniqueViolation = PostgresErrorCodes.UniqueViolation;

    private const string MessageIndex = "ux_whatsapp_deliveries_message";

    /// <summary>What a delivery abandoned by a dead worker says for itself.</summary>
    private const string AbandonedReason =
        "The delivery worker stopped while this message was being sent. Whether it "
        + "reached the customer is unknown, so it was not sent again.";

    private readonly OmniAiDbContext _context =
        context ?? throw new ArgumentNullException(nameof(context));

    public async Task<bool> QueueAsync(
        WhatsAppDelivery delivery,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        _context.WhatsAppDeliveries.Add(delivery);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException failure) when (IsAlreadyQueued(failure))
        {
            // One message, one delivery. A replayed turn or a retried worker
            // reaching here is exactly what this index exists to catch, and what
            // stops a customer reading the same reply twice.
            _context.Entry(delivery).State = EntityState.Detached;

            return false;
        }

        return true;
    }

    public async Task<IReadOnlyList<ClaimedDelivery>> ClaimAsync(
        int batchSize,
        int maxAttempts,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxAttempts);

        // Claimed as "Sending" rather than left pending with a flag, because the
        // state a crashed worker leaves behind has to be distinguishable from the
        // state a message waits in. Sending is the only state that means "may
        // already have reached the customer".
        await using NpgsqlCommand command = await CommandAsync(
                """
                WITH claimed AS (
                    UPDATE whatsapp_deliveries
                       SET status = 'Sending',
                           attempts = attempts + 1,
                           updated_at = @now
                     WHERE id IN (
                        SELECT waiting.id
                          FROM whatsapp_deliveries waiting
                         WHERE waiting.status = 'Pending'
                           AND waiting.attempts < @max_attempts
                         ORDER BY waiting.created_at
                         LIMIT @batch_size
                         FOR UPDATE SKIP LOCKED
                     )
                 RETURNING id, whatsapp_connection_id, tenant_id, conversation_id,
                           recipient, text
                )
                SELECT claimed.id,
                       claimed.whatsapp_connection_id,
                       claimed.tenant_id,
                       claimed.conversation_id,
                       connection.phone_number_id,
                       claimed.recipient,
                       claimed.text
                  FROM claimed
                  JOIN whatsapp_connections connection
                    ON connection.id = claimed.whatsapp_connection_id
                """,
                cancellationToken)
            .ConfigureAwait(false);

        command.Parameters.AddWithValue("now", now.ToUniversalTime());
        command.Parameters.AddWithValue("max_attempts", maxAttempts);
        command.Parameters.AddWithValue("batch_size", batchSize);

        List<ClaimedDelivery> claimed = [];

        await using DbDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            claimed.Add(new ClaimedDelivery(
                WhatsAppDeliveryId.From(reader.GetGuid(0)),
                WhatsAppConnectionId.From(reader.GetGuid(1)),
                TenantId.From(reader.GetGuid(2)),
                ConversationId.From(reader.GetGuid(3)),
                PhoneNumberId.Create(reader.GetString(4)),
                WhatsAppCustomerId.Create(reader.GetString(5)),
                reader.GetString(6)));
        }

        return claimed;
    }

    public async Task MarkSentAsync(
        WhatsAppDeliveryId id,
        ProviderMessageId providerMessageId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlCommand command = await CommandAsync(
                """
                UPDATE whatsapp_deliveries
                   SET status = 'Sent',
                       provider_message_id = @provider_message_id,
                       failure_reason = NULL,
                       updated_at = @now
                 WHERE id = @id
                   AND status = 'Sending'
                """,
                cancellationToken)
            .ConfigureAwait(false);

        command.Parameters.AddWithValue("provider_message_id", providerMessageId.Value);
        command.Parameters.AddWithValue("now", now.ToUniversalTime());
        command.Parameters.AddWithValue("id", id.Value);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task MarkFailedAsync(
        WhatsAppDeliveryId id,
        string? reason,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        SettleAsync(id, WhatsAppDeliveryStatus.Failed, reason, now, cancellationToken);

    public Task MarkUnknownAsync(
        WhatsAppDeliveryId id,
        string? reason,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        SettleAsync(id, WhatsAppDeliveryStatus.Unknown, reason, now, cancellationToken);

    public async Task<WhatsAppStatusOutcome> ApplyProviderStatusAsync(
        ProviderMessageId providerMessageId,
        WhatsAppDeliveryStatus reported,
        string? reason,
        DateTimeOffset at,
        CancellationToken cancellationToken = default)
    {
        // <strong>The row is locked before it is read.</strong> Meta can deliver
        // "delivered" and "read" close enough together that two requests overlap,
        // and whether the pair ends up at "read" or back at "delivered" would
        // otherwise depend on which transaction committed last. The lock makes the
        // two events sequential, and the aggregate's ranking then makes the order
        // they arrive in irrelevant.
        await using IDbContextTransaction transaction = await _context.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        Guid? locked = await LockByProviderMessageAsync(providerMessageId, cancellationToken)
            .ConfigureAwait(false);

        if (locked is not Guid deliveryId)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);

            // A status for something this deployment never sent: another
            // environment sharing the number, or a message from before the
            // connection existed. Acknowledged and dropped.
            return WhatsAppStatusOutcome.Unmatched;
        }

        WhatsAppDelivery delivery = await _context.WhatsAppDeliveries
            .FirstAsync(
                candidate => candidate.Id == WhatsAppDeliveryId.From(deliveryId),
                cancellationToken)
            .ConfigureAwait(false);

        bool changed = delivery.ApplyProviderStatus(reported, reason, at);

        if (changed)
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return new WhatsAppStatusOutcome(
            Matched: true,
            changed,
            delivery.TenantId,
            delivery.ConversationId);
    }

    public async Task<int> AbandonStaleSendsAsync(
        DateTimeOffset claimedBefore,
        CancellationToken cancellationToken = default)
    {
        // Every delivery given up on here becomes Unknown, and each is an
        // operational alert — recorded in the same statement, so the state and
        // its notification cannot part company.
        await using NpgsqlCommand command = await CommandAsync(
                """
                WITH moved AS (
                    UPDATE whatsapp_deliveries
                       SET status = 'Unknown',
                           failure_reason = @reason,
                           updated_at = @cutoff
                     WHERE status = 'Sending'
                       AND updated_at < @cutoff
                    RETURNING id, tenant_id
                )
                """ + RecordUnknown + """
                SELECT count(*) FROM moved
                """,
                cancellationToken)
            .ConfigureAwait(false);

        command.Parameters.AddWithValue("reason", AbandonedReason);
        command.Parameters.AddWithValue("cutoff", claimedBefore.ToUniversalTime());
        command.Parameters.AddWithValue("now", claimedBefore.ToUniversalTime());

        return (int)(long)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? 0L);
    }

    public async Task<IReadOnlyDictionary<Guid, WhatsAppDeliveryStatus>> ListStatusesAsync(
        ConversationId conversationId,
        CancellationToken cancellationToken = default)
    {
        List<KeyValuePair<Guid, WhatsAppDeliveryStatus>> rows = await _context.WhatsAppDeliveries
            .AsNoTracking()
            .Where(delivery => delivery.ConversationId == conversationId)
            .Select(delivery => new KeyValuePair<Guid, WhatsAppDeliveryStatus>(
                delivery.MessageId.Value,
                delivery.Status))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.ToDictionary();
    }

    /// <summary>
    /// Takes a row lock on the delivery a provider message id refers to.
    /// </summary>
    /// <returns>Its id, or <see langword="null"/> when nothing matches.</returns>
    /// <remarks>
    /// A separate statement rather than a locking hint on the entity query,
    /// because EF has no way to express <c>FOR UPDATE</c> and the lock is the
    /// point of the read.
    /// </remarks>
    private async Task<Guid?> LockByProviderMessageAsync(
        ProviderMessageId providerMessageId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = await CommandAsync(
                """
                SELECT id
                  FROM whatsapp_deliveries
                 WHERE provider_message_id = @provider_message_id
                 FOR UPDATE
                """,
                cancellationToken)
            .ConfigureAwait(false);

        command.Parameters.AddWithValue("provider_message_id", providerMessageId.Value);

        object? found = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

        return found is Guid id ? id : null;
    }

    /// <summary>
    /// Records a WhatsApp-delivery-Unknown occurrence for every row of a
    /// <c>moved</c> CTE. Keyed by delivery: a delivery becomes Unknown once.
    /// </summary>
    private const string RecordUnknown = """
        ,
        recorded AS (
            INSERT INTO notification_occurrences
                (id, tenant_id, kind, occurrence_key, source_id, occurred_at, attempts)
            SELECT gen_random_uuid(), tenant_id, 'WhatsAppDeliveryUnknown', id::text, id, @now, 0
              FROM moved
            ON CONFLICT (tenant_id, kind, occurrence_key) DO NOTHING
        )

        """;

    /// <summary>The same, only for rows the settlement actually made Unknown.</summary>
    private const string RecordUnknownSettled = """
        ,
        recorded AS (
            INSERT INTO notification_occurrences
                (id, tenant_id, kind, occurrence_key, source_id, occurred_at, attempts)
            SELECT gen_random_uuid(), tenant_id, 'WhatsAppDeliveryUnknown', id::text, id, @now, 0
              FROM moved
             WHERE status = 'Unknown'
            ON CONFLICT (tenant_id, kind, occurrence_key) DO NOTHING
        )

        """;

    /// <summary>Settles a delivery a worker is holding.</summary>
    /// <remarks>
    /// Conditional on the row still being in flight, so a worker the sweeper has
    /// already given up on cannot overwrite the outcome recorded for it.
    /// </remarks>
    private async Task SettleAsync(
        WhatsAppDeliveryId id,
        WhatsAppDeliveryStatus status,
        string? reason,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Settling as Unknown records its operational alert in the same
        // statement; any other outcome records nothing.
        await using NpgsqlCommand command = await CommandAsync(
                """
                WITH moved AS (
                    UPDATE whatsapp_deliveries
                       SET status = @status,
                           failure_reason = @reason,
                           updated_at = @now
                     WHERE id = @id
                       AND status IN ('Pending', 'Sending')
                    RETURNING id, tenant_id, status
                )
                """ + RecordUnknownSettled + """
                SELECT count(*) FROM moved
                """,
                cancellationToken)
            .ConfigureAwait(false);

        command.Parameters.AddWithValue("status", status.ToString());
        command.Parameters.AddWithValue("reason", Truncate(reason) ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("now", now.ToUniversalTime());
        command.Parameters.AddWithValue("id", id.Value);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Keeps a reason inside the column the aggregate also keeps it inside.
    /// </summary>
    /// <remarks>
    /// The aggregate truncates for the same reason, and this path does not go
    /// through the aggregate. A reason that overflows is a message a colleague
    /// never sees, so it is shortened rather than allowed to fail the write.
    /// </remarks>
    private static string? Truncate(string? reason) =>
        reason is null || reason.Length <= WhatsAppDelivery.MaxReasonLength
            ? reason
            : reason[..WhatsAppDelivery.MaxReasonLength];

    /// <summary>Whether a failed insert was the one-delivery-per-message index.</summary>
    private static bool IsAlreadyQueued(DbUpdateException failure) =>
        failure.InnerException is PostgresException postgres
        && postgres.SqlState == UniqueViolation
        && postgres.ConstraintName == MessageIndex;

    /// <summary>A command on the context's own connection and transaction.</summary>
    private async Task<NpgsqlCommand> CommandAsync(string sql, CancellationToken cancellationToken)
    {
        DbConnection connection = _context.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        NpgsqlCommand command = new(sql, (NpgsqlConnection)connection);

        if (_context.Database.CurrentTransaction?.GetDbTransaction() is NpgsqlTransaction active)
        {
            command.Transaction = active;
        }

        return command;
    }
}
