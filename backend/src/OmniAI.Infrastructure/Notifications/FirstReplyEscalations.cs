using Microsoft.EntityFrameworkCore;
using OmniAI.Application.Notifications;
using OmniAI.Infrastructure.Persistence;

namespace OmniAI.Infrastructure.Notifications;

internal static class FirstReplyEscalations
{
    // Opening turn and subsequent replies follow ingestion order, as in the overdue inbox.
    private const string Due = """
        FROM tenants t JOIN conversations c ON c.tenant_id = t.id
        CROSS JOIN LATERAL (
            SELECT m.sent_at, m.ingestion_sequence FROM messages m
            WHERE m.conversation_id = c.id AND m.sender = 'Customer'
            ORDER BY m.ingestion_sequence LIMIT 1
        ) opening
        WHERE t.escalation_delay_minutes IS NOT NULL
          AND c.status = 'Open' AND c.assigned_to IS NULL
          AND opening.sent_at <= @now - make_interval(mins => t.first_reply_target_minutes + t.escalation_delay_minutes)
          AND NOT EXISTS (SELECT 1 FROM messages r WHERE r.conversation_id = c.id
                          AND r.ingestion_sequence > opening.ingestion_sequence AND r.sender IN ('AiAgent','HumanAgent'))
        """;

    public static async Task<int> EnqueueAsync(OmniAiDbContext db, DateTimeOffset now, int limit, CancellationToken token)
    {
        const string sql = """
            INSERT INTO notification_occurrences (id, tenant_id, kind, occurrence_key, source_id, occurred_at, attempts)
            SELECT gen_random_uuid(), t.id, 'FirstReplyEscalated', c.id::text, c.id, @now, 0
            """ + "\n" + Due + "\n" + """
              AND NOT EXISTS (SELECT 1 FROM notification_occurrences n WHERE n.tenant_id = t.id
                              AND n.kind = 'FirstReplyEscalated' AND n.occurrence_key = c.id::text)
            ORDER BY opening.sent_at, c.id LIMIT @limit
            ON CONFLICT (tenant_id, kind, occurrence_key) DO NOTHING
            """;
        await using var command = await NotificationOccurrenceSql.CommandAsync(db, sql, token).ConfigureAwait(false);
        command.Parameters.AddWithValue("now", now.ToUniversalTime());
        command.Parameters.AddWithValue("limit", limit);
        return await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    // Caller holds the occurrence claim in the same transaction. Serialize with assignment, replies and rule edits,
    // then check on a fresh statement snapshot so changes made while waiting for the locks are visible.
    public static async Task<bool> StillDueAsync(OmniAiDbContext db, ClaimedOccurrence occurrence, DateTimeOffset now, CancellationToken token)
    {
        if (occurrence.SourceId is not Guid conversation)
        {
            return false;
        }
        var tenant = occurrence.TenantId.Value;
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM tenants WHERE id = {tenant} FOR SHARE", token).ConfigureAwait(false);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM conversations WHERE tenant_id = {tenant} AND id = {conversation} FOR UPDATE", token).ConfigureAwait(false);
        await using var check = await NotificationOccurrenceSql.CommandAsync(db,
            "SELECT 1 " + Due + " AND t.id = @tenant AND c.id = @conversation", token).ConfigureAwait(false);
        check.Parameters.AddWithValue("now", now.ToUniversalTime());
        check.Parameters.AddWithValue("tenant", tenant);
        check.Parameters.AddWithValue("conversation", conversation);
        return await check.ExecuteScalarAsync(token).ConfigureAwait(false) is not null;
    }
}
