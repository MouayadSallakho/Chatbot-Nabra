using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Reports;
using OmniAI.Domain.Reports;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;
using OmniAI.Infrastructure.Persistence;

namespace OmniAI.Infrastructure.Reports;

/// <summary>
/// Report schedules, in PostgreSQL.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Versions are the row's <c>xmin</c>.</strong> The same opaque token
/// every other conditional resource here uses: the version a caller read goes
/// into the <c>UPDATE</c>'s <c>WHERE</c> clause, so the database decides whether
/// the row is still what they believed. The schedule worker advancing
/// <c>next_run_at</c> is a change too, and an editor holding the older version is
/// told so rather than silently overwriting it.
/// </para>
/// <para>
/// <strong>Due schedules are locked, not flagged.</strong> See
/// <see cref="ClaimDueAsync"/>.
/// </para>
/// </remarks>
internal sealed class ReportScheduleStore(OmniAiDbContext context) : IReportScheduleStore
{
    private readonly OmniAiDbContext _context =
        context ?? throw new ArgumentNullException(nameof(context));

    public async Task<ConcurrencyToken> AddAsync(
        ReportSchedule schedule,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        _context.ReportSchedules.Add(schedule);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return VersionOf(schedule);
    }

    public async Task<Versioned<ReportSchedule>?> FindAsync(
        TenantId tenantId,
        ReportScheduleId id,
        CancellationToken cancellationToken = default)
    {
        ReportSchedule? schedule = await _context.ReportSchedules
            .FirstOrDefaultAsync(
                candidate => candidate.Id == id && candidate.TenantId == tenantId,
                cancellationToken)
            .ConfigureAwait(false);

        return schedule is null ? null : new Versioned<ReportSchedule>(schedule, VersionOf(schedule));
    }

    public async Task<IReadOnlyList<ReportScheduleRecord>> ListAsync(
        TenantId tenantId,
        CancellationToken cancellationToken = default)
    {
        // The version is projected rather than read from the change tracker: an
        // untracked entity keeps no shadow values, and a list whose every version
        // was zero would hand out ETags that could never match.
        var rows = await _context.ReportSchedules
            .AsNoTracking()
            .Where(schedule => schedule.TenantId == tenantId)
            .OrderBy(schedule => schedule.Name)
            .ThenBy(schedule => schedule.Id)
            .Take(500)
            .Select(schedule => new
            {
                Schedule = schedule,
                Version = EF.Property<uint>(schedule, PersistenceSchema.XminConcurrencyToken),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (rows.Count == 0)
        {
            return [];
        }

        List<ReportScheduleId?> ids = [.. rows.Select(row => (ReportScheduleId?)row.Schedule.Id)];

        // The latest occurrence of each, in one read. A workspace has at most a
        // few dozen schedules, and each contributes its single newest row.
        List<ReportExecution> latest = await _context.ReportExecutions
            .AsNoTracking()
            .Where(execution => execution.TenantId == tenantId && ids.Contains(execution.ScheduleId))
            .GroupBy(execution => execution.ScheduleId)
            .Select(group => group
                .OrderByDescending(execution => execution.RequestedAt)
                .ThenByDescending(execution => execution.Id)
                .First())
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. rows.Select(row => new ReportScheduleRecord(
                new Versioned<ReportSchedule>(row.Schedule, RowVersionTokens.Encode(row.Version)),
                latest.FirstOrDefault(execution => execution.ScheduleId == row.Schedule.Id))),
        ];
    }

    public Task<int> CountAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        _context.ReportSchedules.CountAsync(schedule => schedule.TenantId == tenantId, cancellationToken);

    public async Task<ConcurrencyToken> UpdateAsync(
        ReportSchedule schedule,
        ConcurrencyToken expected,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        if (!RowVersionTokens.TryDecode(expected, out uint version))
        {
            throw new ReportScheduleVersionMismatchException();
        }

        RowVersion(schedule).OriginalValue = version;

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ReportScheduleVersionMismatchException();
        }

        return VersionOf(schedule);
    }

    /// <remarks>
    /// <para>
    /// <strong><c>FOR UPDATE SKIP LOCKED</c>, held for the whole batch.</strong>
    /// Two instances asking at once get disjoint schedules, and a schedule is
    /// locked from the moment it is read until its occurrence is queued and its
    /// next run recorded — so the two cannot be separated by a crash.
    /// </para>
    /// <para>
    /// The schedules are read as tracked entities inside the transaction, so the
    /// batch's changes to them are ordinary EF updates that bump the row version
    /// like any other edit. <c>xmin</c> is named explicitly because a system
    /// column is never part of <c>*</c>.
    /// </para>
    /// </remarks>
    public async Task<IDueReportScheduleBatch> ClaimDueAsync(
        DateTimeOffset now,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        IDbContextTransaction transaction = await _context.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            List<ReportSchedule> due = await _context.ReportSchedules
                .FromSql($"""
                    SELECT report_schedules.*, report_schedules.xmin
                      FROM report_schedules
                     WHERE enabled
                       AND next_run_at <= {now.ToUniversalTime()}
                     ORDER BY next_run_at, id
                     LIMIT {batchSize}
                     FOR UPDATE SKIP LOCKED
                    """)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            // The zones of the workspaces in the batch, in one read (Phase 37.6).
            List<TenantId> workspaces = [.. due.Select(schedule => schedule.TenantId).Distinct()];

            Dictionary<TenantId, WorkspaceTimeZone> zones = workspaces.Count == 0
                ? []
                : await _context.Tenants
                    .AsNoTracking()
                    .Where(tenant => workspaces.Contains(tenant.Id))
                    .Select(tenant => new { tenant.Id, tenant.TimeZone })
                    .ToDictionaryAsync(row => row.Id, row => row.TimeZone, cancellationToken)
                    .ConfigureAwait(false);

            return new DueBatch(_context, transaction, due, zones);
        }
        catch
        {
            await transaction.DisposeAsync().ConfigureAwait(false);

            throw;
        }
    }

    private PropertyEntry<ReportSchedule, uint> RowVersion(ReportSchedule schedule) =>
        _context.Entry(schedule).Property<uint>(PersistenceSchema.XminConcurrencyToken);

    private ConcurrencyToken VersionOf(ReportSchedule schedule) =>
        RowVersionTokens.Encode(_context.Entry(schedule).Property<uint>(PersistenceSchema.XminConcurrencyToken).CurrentValue);

    /// <summary>One transaction's worth of due schedules.</summary>
    private sealed class DueBatch(
        OmniAiDbContext context,
        IDbContextTransaction transaction,
        IReadOnlyList<ReportSchedule> schedules,
        IReadOnlyDictionary<TenantId, WorkspaceTimeZone> zones) : IDueReportScheduleBatch
    {
        private bool _committed;

        public IReadOnlyList<ReportSchedule> Schedules { get; } = schedules;

        public WorkspaceTimeZone TimeZoneOf(ReportSchedule schedule)
        {
            ArgumentNullException.ThrowIfNull(schedule);

            return zones.GetValueOrDefault(schedule.TenantId) ?? WorkspaceTimeZone.Utc;
        }

        public async Task<bool> EnqueueAsync(
            ReportSchedule schedule,
            ReportExecution occurrence,
            DateTimeOffset nextRunAt,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(schedule);
            ArgumentNullException.ThrowIfNull(occurrence);

            // ON CONFLICT on the occurrence key: the same schedule and period is
            // one execution, however many times this runs. Written as SQL because
            // EF has no way to say "insert unless it exists" without a failed
            // statement aborting the surrounding transaction.
            int inserted = await context.Database
                .ExecuteSqlAsync(
                    $"""
                    INSERT INTO report_executions
                        (id, tenant_id, report_type, format, period_from, period_to, time_zone, language, status,
                         requested_by, requested_at, client_request_id, schedule_id, attempts)
                    VALUES
                        ({occurrence.Id.Value}, {occurrence.TenantId.Value}, {occurrence.Type.ToString()},
                         {occurrence.Format.ToString()}, {occurrence.PeriodFrom.ToUniversalTime()},
                         {occurrence.PeriodTo.ToUniversalTime()}, {occurrence.TimeZone.Name},
                         {UserLanguages.Code(occurrence.Language)}, 'Pending',
                         {occurrence.RequestedBy.Value}, {occurrence.RequestedAt.ToUniversalTime()}, NULL,
                         {schedule.Id.Value}, 0)
                    ON CONFLICT (schedule_id, period_from) WHERE schedule_id IS NOT NULL DO NOTHING
                    """,
                    cancellationToken)
                .ConfigureAwait(false);

            context.Entry(schedule).Property(candidate => candidate.NextRunAt).CurrentValue = nextRunAt;

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return inserted == 1;
        }

        public async Task DisableAsync(
            ReportSchedule schedule,
            string reason,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(schedule);

            context.Entry(schedule).Property(candidate => candidate.Enabled).CurrentValue = false;
            context.Entry(schedule).Property(candidate => candidate.DisabledReason).CurrentValue = reason;
            context.Entry(schedule).Property(candidate => candidate.UpdatedAt).CurrentValue = now;

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            _committed = true;
        }

        public async ValueTask DisposeAsync()
        {
            if (!_committed)
            {
                await transaction.RollbackAsync().ConfigureAwait(false);
            }

            await transaction.DisposeAsync().ConfigureAwait(false);

            // Nothing tracked here may outlive the transaction it was locked in.
            foreach (ReportSchedule schedule in Schedules)
            {
                context.Entry(schedule).State = EntityState.Detached;
            }
        }
    }
}
