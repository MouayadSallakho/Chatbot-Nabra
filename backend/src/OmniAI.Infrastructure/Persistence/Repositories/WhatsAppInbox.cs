using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using OmniAI.Application.WhatsApp.Persistence;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.WhatsApp;

namespace OmniAI.Infrastructure.Persistence.Repositories;

/// <summary>
/// The durable inbound inbox, in PostgreSQL.
/// </summary>
/// <remarks>
/// <strong>Claiming is one statement.</strong> A select followed by an update
/// would let two instances read the same row before either wrote, and the same
/// customer message would be answered twice. Here the update is the read:
/// <c>FOR UPDATE SKIP LOCKED</c> picks rows no other worker is holding, the
/// <c>UPDATE</c> marks them in the same statement, and <c>RETURNING</c> hands
/// back exactly what was claimed.
/// <para>
/// <strong>A claim is a timestamp, not a lease.</strong> Nothing renews it and
/// nothing has to notice a crash: a worker that dies mid-message leaves a row
/// that simply looks old, and the sweeper returns anything claimed before a cut
/// off to the queue.
/// </para>
/// <para>
/// Written as SQL rather than through the change tracker because both of those
/// properties are properties of the statement. Expressed as entity operations
/// they would be properties of a comment.
/// </para>
/// </remarks>
internal sealed class WhatsAppInbox(OmniAiDbContext context) : IWhatsAppInbox
{
    private const string UniqueViolation = PostgresErrorCodes.UniqueViolation;

    private const string ProviderMessageIndex = "ux_whatsapp_inbound_provider_message";

    private readonly OmniAiDbContext _context =
        context ?? throw new ArgumentNullException(nameof(context));

    public async Task<bool> RecordAsync(
        InboundWhatsAppMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        _context.InboundWhatsAppMessages.Add(message);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException failure) when (IsRedelivery(failure))
        {
            // Meta redelivers whenever it is unsure the first attempt landed,
            // which is routine. The index settled the race; the loser detaches
            // and reports the duplicate as the ordinary event it is.
            _context.Entry(message).State = EntityState.Detached;

            return false;
        }

        return true;
    }

    public async Task<IReadOnlyList<ClaimedInboundMessage>> ClaimAsync(
        int batchSize,
        int maxAttempts,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxAttempts);

        // The connection is joined in so the worker can re-check routing without
        // a second round trip -- and re-check it must, because a connection can
        // be disabled between a webhook arriving and a worker reaching it.
        await using NpgsqlCommand command = await CommandAsync(
                """
                WITH claimed AS (
                    UPDATE whatsapp_inbound_messages
                       SET status = 'Claimed',
                           attempts = attempts + 1,
                           updated_at = @now
                     WHERE id IN (
                        SELECT waiting.id
                          FROM whatsapp_inbound_messages waiting
                         WHERE waiting.status = 'Pending'
                           AND waiting.attempts < @max_attempts
                           AND waiting.text IS NOT NULL
                         ORDER BY waiting.received_at
                         LIMIT @batch_size
                         FOR UPDATE SKIP LOCKED
                     )
                 RETURNING id, whatsapp_connection_id, tenant_id, provider_message_id,
                           customer_id, text, sent_at, attempts
                )
                SELECT claimed.id,
                       claimed.whatsapp_connection_id,
                       claimed.tenant_id,
                       connection.phone_number_id,
                       claimed.provider_message_id,
                       claimed.customer_id,
                       claimed.text,
                       claimed.sent_at,
                       claimed.attempts
                  FROM claimed
                  JOIN whatsapp_connections connection
                    ON connection.id = claimed.whatsapp_connection_id
                """,
                cancellationToken)
            .ConfigureAwait(false);

        command.Parameters.AddWithValue("now", now.ToUniversalTime());
        command.Parameters.AddWithValue("max_attempts", maxAttempts);
        command.Parameters.AddWithValue("batch_size", batchSize);

        List<ClaimedInboundMessage> claimed = [];

        await using DbDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            claimed.Add(new ClaimedInboundMessage(
                InboundWhatsAppMessageId.From(reader.GetGuid(0)),
                WhatsAppConnectionId.From(reader.GetGuid(1)),
                TenantId.From(reader.GetGuid(2)),
                PhoneNumberId.Create(reader.GetString(3)),
                ProviderMessageId.Create(reader.GetString(4)),
                WhatsAppCustomerId.Create(reader.GetString(5)),
                reader.GetString(6),
                reader.GetFieldValue<DateTimeOffset>(7),
                reader.GetInt32(8)));
        }

        return claimed;
    }

    public Task MarkProcessedAsync(
        InboundWhatsAppMessageId id,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        SettleAsync(id, InboundWhatsAppStatus.Processed, now, cancellationToken);

    public Task MarkFailedAsync(
        InboundWhatsAppMessageId id,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        SettleAsync(id, InboundWhatsAppStatus.Failed, now, cancellationToken);

    public Task ReleaseAsync(
        InboundWhatsAppMessageId id,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        SettleAsync(id, InboundWhatsAppStatus.Pending, now, cancellationToken);

    public async Task<int> ReleaseStaleClaimsAsync(
        DateTimeOffset claimedBefore,
        CancellationToken cancellationToken = default)
    {
        // The whole of crash recovery. The attempt count was already raised when
        // the row was claimed, so a message that repeatedly kills its worker runs
        // out of attempts rather than looping forever.
        await using NpgsqlCommand command = await CommandAsync(
                """
                UPDATE whatsapp_inbound_messages
                   SET status = 'Pending',
                       updated_at = @cutoff
                 WHERE status = 'Claimed'
                   AND updated_at < @cutoff
                """,
                cancellationToken)
            .ConfigureAwait(false);

        command.Parameters.AddWithValue("cutoff", claimedBefore.ToUniversalTime());

        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Moves a claimed row to its next state.
    /// </summary>
    /// <remarks>
    /// Conditional on the row still being claimed. A worker that was overtaken by
    /// the stale-claim sweeper -- because it stalled long enough to look dead --
    /// must not be able to settle a message another worker has since taken.
    /// </remarks>
    private async Task SettleAsync(
        InboundWhatsAppMessageId id,
        InboundWhatsAppStatus status,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = await CommandAsync(
                """
                UPDATE whatsapp_inbound_messages
                   SET status = @status,
                       updated_at = @now
                 WHERE id = @id
                   AND status = 'Claimed'
                """,
                cancellationToken)
            .ConfigureAwait(false);

        command.Parameters.AddWithValue("status", status.ToString());
        command.Parameters.AddWithValue("now", now.ToUniversalTime());
        command.Parameters.AddWithValue("id", id.Value);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Whether a failed insert was the idempotency index doing its job.</summary>
    private static bool IsRedelivery(DbUpdateException failure) =>
        failure.InnerException is PostgresException postgres
        && postgres.SqlState == UniqueViolation
        && postgres.ConstraintName == ProviderMessageIndex;

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
