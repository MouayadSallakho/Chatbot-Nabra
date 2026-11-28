using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Npgsql;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Notifications;
using OmniAI.Domain.Notifications;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;
using OmniAI.Infrastructure.Persistence;

namespace OmniAI.Infrastructure.Notifications;

/// <summary>
/// Notifications and preferences, in PostgreSQL, always addressed by workspace and member.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Every predicate names both.</strong> A notification id alone finds
/// nothing: the recipient and the workspace are in every read and every update,
/// so another member's id — in this workspace or any other — behaves exactly
/// like an id that was never issued.
/// </para>
/// <para>
/// <strong>Server-side counts and bounded pages.</strong> The unread badge is a
/// <c>COUNT</c> over a partial index of unread rows; the list is one page, newest
/// first, with the id as the tie-break so two rows created in the same
/// microsecond never swap places between pages.
/// </para>
/// </remarks>
internal sealed class NotificationStore(OmniAiDbContext context) : INotificationStore, INotificationRetentionStore
{
    /// <summary>
    /// The version of preferences that have never been saved.
    /// </summary>
    /// <remarks>
    /// Zero is never a live <c>xmin</c> — PostgreSQL reserves it — so it can only
    /// mean "nothing stored yet". Saving against it inserts; if another tab
    /// inserted first, the primary key refuses the second and it becomes a 412.
    /// </remarks>
    private const uint Unsaved = 0;

    private readonly OmniAiDbContext _context = context ?? throw new ArgumentNullException(nameof(context));

    public async Task<(IReadOnlyList<Notification> Items, int TotalCount)> ListAsync(
        TenantId tenantId,
        UserId recipient,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Notification> mine = Mine(tenantId, recipient);

        int total = await mine.CountAsync(cancellationToken).ConfigureAwait(false);

        List<Notification> items = await mine
            .OrderByDescending(notification => notification.CreatedAt)
            .ThenByDescending(notification => notification.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return (items, total);
    }

    public Task<int> CountUnreadAsync(TenantId tenantId, UserId recipient, CancellationToken cancellationToken = default) =>
        Mine(tenantId, recipient).CountAsync(notification => notification.ReadAt == null, cancellationToken);

    public async Task<bool> MarkReadAsync(
        TenantId tenantId,
        UserId recipient,
        NotificationId id,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        int marked = await Mine(tenantId, recipient)
            .Where(notification => notification.Id == id && notification.ReadAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(notification => notification.ReadAt, now.ToUniversalTime()),
                cancellationToken)
            .ConfigureAwait(false);

        // Nothing changed either because it was already read — which is success —
        // or because it is not this member's, which is absence.
        return marked == 1
            || await Mine(tenantId, recipient)
                .AnyAsync(notification => notification.Id == id, cancellationToken)
                .ConfigureAwait(false);
    }

    public Task<int> MarkAllReadAsync(
        TenantId tenantId,
        UserId recipient,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        Mine(tenantId, recipient)
            .Where(notification => notification.ReadAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(notification => notification.ReadAt, now.ToUniversalTime()),
                cancellationToken);

    public async Task<Versioned<NotificationPreferences>> PreferencesAsync(
        TenantId tenantId,
        UserId userId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        NotificationPreferences? stored = await _context.NotificationPreferences
            .FirstOrDefaultAsync(
                preferences => preferences.TenantId == tenantId && preferences.UserId == userId,
                cancellationToken)
            .ConfigureAwait(false);

        return stored is null
            ? new Versioned<NotificationPreferences>(
                NotificationPreferences.Default(tenantId, userId, now),
                RowVersionTokens.Encode(Unsaved))
            : new Versioned<NotificationPreferences>(stored, RowVersionTokens.Encode(RowVersion(stored).CurrentValue));
    }

    public async Task<ConcurrencyToken> SavePreferencesAsync(
        NotificationPreferences preferences,
        ConcurrencyToken expected,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        if (!RowVersionTokens.TryDecode(expected, out uint version))
        {
            throw new NotificationPreferencesVersionMismatchException();
        }

        bool tracked = _context.Entry(preferences).State != EntityState.Detached;

        // What the caller read and what is stored must agree about whether a row
        // exists at all, before the versions are compared.
        if (tracked != (version != Unsaved))
        {
            throw new NotificationPreferencesVersionMismatchException();
        }

        if (tracked)
        {
            RowVersion(preferences).OriginalValue = version;
        }
        else
        {
            _context.NotificationPreferences.Add(preferences);
        }

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new NotificationPreferencesVersionMismatchException();
        }
        catch (DbUpdateException failure) when (failure.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
        })
        {
            // Two first saves at once. The other one won.
            throw new NotificationPreferencesVersionMismatchException();
        }

        return RowVersionTokens.Encode(RowVersion(preferences).CurrentValue);
    }

    public async Task<NotificationPreferences> PreferencesForSendingAsync(
        TenantId tenantId,
        UserId userId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        await _context.NotificationPreferences
            .AsNoTracking()
            .FirstOrDefaultAsync(
                preferences => preferences.TenantId == tenantId && preferences.UserId == userId,
                cancellationToken)
            .ConfigureAwait(false)
        ?? NotificationPreferences.Default(tenantId, userId, now);

    public Task<Notification?> FindAsync(
        TenantId tenantId,
        NotificationId id,
        CancellationToken cancellationToken = default) =>
        _context.Notifications
            .AsNoTracking()
            .FirstOrDefaultAsync(
                notification => notification.Id == id && notification.TenantId == tenantId,
                cancellationToken);

    public async Task<int> DeleteExpiredNotificationsAsync(
        DateTimeOffset now,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlCommand command = await NotificationOccurrenceSql.CommandAsync(
                _context,
                """
                DELETE FROM notifications
                 WHERE id IN (
                    SELECT candidate.id
                      FROM notifications candidate
                     WHERE candidate.expires_at <= @now
                     ORDER BY candidate.expires_at, candidate.id
                     LIMIT @batch_size
                 )
                """,
                cancellationToken)
            .ConfigureAwait(false);

        command.Parameters.AddWithValue("now", now.ToUniversalTime());
        command.Parameters.AddWithValue("batch_size", batchSize);

        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> DeleteProcessedOccurrencesAsync(
        DateTimeOffset before,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlCommand command = await NotificationOccurrenceSql.CommandAsync(
                _context,
                """
                DELETE FROM notification_occurrences
                 WHERE id IN (
                    SELECT candidate.id
                      FROM notification_occurrences candidate
                     WHERE candidate.processed_at IS NOT NULL
                       AND candidate.occurred_at < @before
                       AND (candidate.kind <> 'FirstReplyEscalated' OR NOT EXISTS (
                           SELECT 1 FROM conversations c WHERE c.tenant_id = candidate.tenant_id AND c.id = candidate.source_id))
                     ORDER BY candidate.occurred_at, candidate.id
                     LIMIT @batch_size
                 )
                """,
                cancellationToken)
            .ConfigureAwait(false);

        command.Parameters.AddWithValue("before", before.ToUniversalTime());
        command.Parameters.AddWithValue("batch_size", batchSize);

        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private IQueryable<Notification> Mine(TenantId tenantId, UserId recipient) =>
        _context.Notifications
            .AsNoTracking()
            .Where(notification => notification.TenantId == tenantId && notification.RecipientUserId == recipient);

    private PropertyEntry<NotificationPreferences, uint> RowVersion(NotificationPreferences preferences) =>
        _context.Entry(preferences).Property<uint>(PersistenceSchema.XminConcurrencyToken);
}
