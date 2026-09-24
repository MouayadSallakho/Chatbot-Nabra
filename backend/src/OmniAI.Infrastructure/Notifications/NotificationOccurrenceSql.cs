using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using OmniAI.Domain.Notifications;
using OmniAI.Infrastructure.Persistence;

namespace OmniAI.Infrastructure.Notifications;

/// <summary>
/// Records an occurrence on the connection and transaction a store is already using.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is how a notification rides on its transition.</strong> The
/// report store, the billing service, the entitlement gate and the WhatsApp outbox
/// call this inside the transaction that makes the change — or, where the change
/// is a single statement, write the occurrence in that same statement. Either way
/// the transition and its occurrence commit together or not at all.
/// </para>
/// <para>
/// <c>ON CONFLICT DO NOTHING</c> on the occurrence's unique key: recording the same
/// transition twice is one row and no error, so a retried statement cannot fail a
/// business write.
/// </para>
/// </remarks>
internal static class NotificationOccurrenceSql
{
    public static async Task RecordAsync(
        OmniAiDbContext context,
        NotificationOccurrence occurrence,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(occurrence);

        await using NpgsqlCommand command = await CommandAsync(
                context,
                """
                INSERT INTO notification_occurrences
                    (id, tenant_id, kind, occurrence_key, source_id, occurred_at, attempts)
                VALUES (@id, @tenant_id, @kind, @key, @source_id, @occurred_at, 0)
                ON CONFLICT (tenant_id, kind, occurrence_key) DO NOTHING
                """,
                cancellationToken)
            .ConfigureAwait(false);

        command.Parameters.AddWithValue("id", occurrence.Id.Value);
        command.Parameters.AddWithValue("tenant_id", occurrence.TenantId.Value);
        command.Parameters.AddWithValue("kind", occurrence.Kind.ToString());
        command.Parameters.AddWithValue("key", occurrence.OccurrenceKey);
        command.Parameters.AddWithValue("source_id", occurrence.SourceId is Guid source ? source : DBNull.Value);
        command.Parameters.AddWithValue("occurred_at", occurrence.OccurredAt.ToUniversalTime());

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>A command on the context's own connection and, if one is open, its transaction.</summary>
    public static async Task<NpgsqlCommand> CommandAsync(
        OmniAiDbContext context,
        string sql,
        CancellationToken cancellationToken)
    {
        DbConnection connection = context.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        NpgsqlCommand command = new(sql, (NpgsqlConnection)connection);

        if (context.Database.CurrentTransaction?.GetDbTransaction() is NpgsqlTransaction active)
        {
            command.Transaction = active;
        }

        return command;
    }
}
