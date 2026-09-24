using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using OmniAI.Application.Reports;
using OmniAI.Domain.Reports;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;
using OmniAI.Infrastructure.Persistence;

namespace OmniAI.Infrastructure.Reports;

/// <summary>
/// Report executions and their artifacts, in PostgreSQL.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Every settlement is conditional on the claim token.</strong> Claiming
/// stamps a fresh token on the row; completing, failing and releasing all say
/// <c>WHERE claim_token = @token</c>. A worker whose lease lapsed while it was
/// still working therefore cannot overwrite the result of the worker that took
/// over: its update matches nothing, and it is told so.
/// </para>
/// <para>
/// <strong>The workspace is in every read.</strong> An execution id from another
/// workspace finds nothing, exactly as an id that was never issued.
/// </para>
/// </remarks>
internal sealed class ReportExecutionStore(OmniAiDbContext context) : IReportExecutionStore
{
    private const string ClientRequestIndex = "ux_report_executions_client_request";

    private readonly OmniAiDbContext _context =
        context ?? throw new ArgumentNullException(nameof(context));

    public async Task<(ReportExecution Execution, bool Created)> AddOrGetAsync(
        ReportExecution execution,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(execution);

        _context.ReportExecutions.Add(execution);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return (execution, true);
        }
        catch (DbUpdateException failure) when (Violates(failure, ClientRequestIndex))
        {
            // The same client operation, again. The index decided; the loser reads
            // what the winner wrote, and no second report is queued.
            _context.Entry(execution).State = EntityState.Detached;

            ReportExecution existing = await _context.ReportExecutions
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        candidate => candidate.TenantId == execution.TenantId
                            && candidate.RequestedBy == execution.RequestedBy
                            && candidate.ClientRequestId == execution.ClientRequestId,
                        cancellationToken)
                    .ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    "A report request conflicted but could not be read back.");

            return (existing, false);
        }
    }

    public async Task<ReportExecutionRecord?> FindAsync(
        TenantId tenantId,
        ReportExecutionId id,
        CancellationToken cancellationToken = default)
    {
        ReportExecution? execution = await _context.ReportExecutions
            .AsNoTracking()
            .FirstOrDefaultAsync(
                candidate => candidate.Id == id && candidate.TenantId == tenantId,
                cancellationToken)
            .ConfigureAwait(false);

        if (execution is null)
        {
            return null;
        }

        IReadOnlyList<ReportExecutionRecord> records = await DescribeAsync(
                tenantId, [execution], cancellationToken)
            .ConfigureAwait(false);

        return records[0];
    }

    public async Task<(IReadOnlyList<ReportExecutionRecord> Items, int TotalCount)> ListAsync(
        TenantId tenantId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        IQueryable<ReportExecution> mine = _context.ReportExecutions
            .AsNoTracking()
            .Where(execution => execution.TenantId == tenantId);

        int total = await mine.CountAsync(cancellationToken).ConfigureAwait(false);

        List<ReportExecution> rows = await mine
            .OrderByDescending(execution => execution.RequestedAt)
            .ThenByDescending(execution => execution.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return (await DescribeAsync(tenantId, rows, cancellationToken).ConfigureAwait(false), total);
    }

    public Task<ReportArtifact?> FindArtifactAsync(
        TenantId tenantId,
        ReportExecutionId executionId,
        CancellationToken cancellationToken = default) =>
        _context.ReportArtifacts
            .AsNoTracking()
            .FirstOrDefaultAsync(
                artifact => artifact.ExecutionId == executionId && artifact.TenantId == tenantId,
                cancellationToken);

    public async Task<IReadOnlyList<ClaimedReportExecution>> ClaimAsync(
        int batchSize,
        int maxAttempts,
        TimeSpan lease,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxAttempts);

        DateTimeOffset stale = now - lease;

        // A worker that died holding its last attempt leaves a row nobody may
        // claim again. It is failed here, honestly, rather than left Running.
        await using (NpgsqlCommand exhausted = await CommandAsync(
                """
                UPDATE report_executions
                   SET status = 'Failed',
                       failure_code = @code,
                       failed_at = @now,
                       claim_token = NULL
                 WHERE status = 'Running'
                   AND claimed_at < @stale
                   AND attempts >= @max_attempts
                """,
                cancellationToken)
            .ConfigureAwait(false))
        {
            exhausted.Parameters.AddWithValue("code", ReportFailures.GenerationFailed);
            exhausted.Parameters.AddWithValue("now", now.ToUniversalTime());
            exhausted.Parameters.AddWithValue("stale", stale.ToUniversalTime());
            exhausted.Parameters.AddWithValue("max_attempts", maxAttempts);

            await exhausted.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using NpgsqlCommand command = await CommandAsync(
                """
                UPDATE report_executions
                   SET status = 'Running',
                       attempts = attempts + 1,
                       claim_token = gen_random_uuid(),
                       claimed_at = @now,
                       started_at = COALESCE(started_at, @now)
                 WHERE id IN (
                    SELECT candidate.id
                      FROM report_executions candidate
                     WHERE (candidate.status = 'Pending'
                            OR (candidate.status = 'Running' AND candidate.claimed_at < @stale))
                       AND candidate.attempts < @max_attempts
                     ORDER BY candidate.requested_at, candidate.id
                     LIMIT @batch_size
                     FOR UPDATE SKIP LOCKED
                 )
                RETURNING id, tenant_id, report_type, format, period_from, period_to,
                          requested_by, claim_token, attempts
                """,
                cancellationToken)
            .ConfigureAwait(false);

        command.Parameters.AddWithValue("now", now.ToUniversalTime());
        command.Parameters.AddWithValue("stale", stale.ToUniversalTime());
        command.Parameters.AddWithValue("max_attempts", maxAttempts);
        command.Parameters.AddWithValue("batch_size", batchSize);

        List<ClaimedReportExecution> claimed = [];

        await using DbDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            claimed.Add(new ClaimedReportExecution(
                ReportExecutionId.From(reader.GetGuid(0)),
                TenantId.From(reader.GetGuid(1)),
                Enum.Parse<ReportType>(reader.GetString(2)),
                Enum.Parse<ReportFormat>(reader.GetString(3)),
                ReportPeriod.FromInstants(
                    reader.GetFieldValue<DateTimeOffset>(4),
                    reader.GetFieldValue<DateTimeOffset>(5)),
                UserId.From(reader.GetGuid(6)),
                reader.GetGuid(7),
                reader.GetInt32(8)));
        }

        return claimed;
    }

    public async Task<bool> CompleteAsync(
        ReportExecutionId id,
        Guid claimToken,
        ReportArtifact artifact,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        await using IDbContextTransaction transaction = await _context.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        int completed;

        await using (NpgsqlCommand command = await CommandAsync(
                """
                UPDATE report_executions
                   SET status = 'Completed',
                       completed_at = @now,
                       claim_token = NULL
                 WHERE id = @id
                   AND status = 'Running'
                   AND claim_token = @token
                """,
                cancellationToken)
            .ConfigureAwait(false))
        {
            command.Parameters.AddWithValue("now", now.ToUniversalTime());
            command.Parameters.AddWithValue("id", id.Value);
            command.Parameters.AddWithValue("token", claimToken);

            completed = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (completed != 1)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);

            return false;
        }

        _context.ReportArtifacts.Add(artifact);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return true;
    }

    public async Task<bool> FailAsync(
        ReportExecutionId id,
        Guid claimToken,
        string failureCode,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlCommand command = await CommandAsync(
                """
                UPDATE report_executions
                   SET status = 'Failed',
                       failure_code = @code,
                       failed_at = @now,
                       claim_token = NULL
                 WHERE id = @id
                   AND status = 'Running'
                   AND claim_token = @token
                """,
                cancellationToken)
            .ConfigureAwait(false);

        command.Parameters.AddWithValue("code", failureCode);
        command.Parameters.AddWithValue("now", now.ToUniversalTime());
        command.Parameters.AddWithValue("id", id.Value);
        command.Parameters.AddWithValue("token", claimToken);

        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    public async Task<bool> ReleaseAsync(
        ReportExecutionId id,
        Guid claimToken,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlCommand command = await CommandAsync(
                """
                UPDATE report_executions
                   SET status = 'Pending',
                       claim_token = NULL,
                       claimed_at = NULL
                 WHERE id = @id
                   AND status = 'Running'
                   AND claim_token = @token
                """,
                cancellationToken)
            .ConfigureAwait(false);

        command.Parameters.AddWithValue("id", id.Value);
        command.Parameters.AddWithValue("token", claimToken);

        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    public async Task<IReadOnlyList<ReportArtifact>> ListExpiredArtifactsAsync(
        DateTimeOffset now,
        int batchSize,
        CancellationToken cancellationToken = default) =>
        await _context.ReportArtifacts
            .AsNoTracking()
            .Where(artifact => artifact.ExpiresAt <= now)
            .OrderBy(artifact => artifact.ExpiresAt)
            .ThenBy(artifact => artifact.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task MarkExpiredAsync(
        ReportArtifact artifact,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        await using IDbContextTransaction transaction = await _context.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        await _context.ReportArtifacts
            .Where(candidate => candidate.Id == artifact.Id)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        await _context.ReportExecutions
            .Where(execution =>
                execution.Id == artifact.ExecutionId
                && execution.TenantId == artifact.TenantId
                && execution.Status == ReportExecutionStatus.Completed)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(execution => execution.Status, ReportExecutionStatus.Expired)
                    .SetProperty(execution => execution.ExpiredAt, now),
                cancellationToken)
            .ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Adds each execution's artifact size and schedule name, in two bounded reads.</summary>
    private async Task<IReadOnlyList<ReportExecutionRecord>> DescribeAsync(
        TenantId tenantId,
        List<ReportExecution> executions,
        CancellationToken cancellationToken)
    {
        if (executions.Count == 0)
        {
            return [];
        }

        List<ReportExecutionId> ids = [.. executions.Select(execution => execution.Id)];

        var artifacts = await _context.ReportArtifacts
            .AsNoTracking()
            .Where(artifact => artifact.TenantId == tenantId && ids.Contains(artifact.ExecutionId))
            .Select(artifact => new { artifact.ExecutionId, artifact.ByteLength, artifact.ExpiresAt })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<ReportScheduleId> scheduleIds =
        [
            .. executions
                .Where(execution => execution.ScheduleId is not null)
                .Select(execution => execution.ScheduleId!.Value)
                .Distinct(),
        ];

        Dictionary<ReportScheduleId, string> names = scheduleIds.Count == 0
            ? []
            : await _context.ReportSchedules
                .AsNoTracking()
                .Where(schedule => schedule.TenantId == tenantId && scheduleIds.Contains(schedule.Id))
                .ToDictionaryAsync(schedule => schedule.Id, schedule => schedule.Name, cancellationToken)
                .ConfigureAwait(false);

        return
        [
            .. executions.Select(execution =>
            {
                var artifact = artifacts.FirstOrDefault(candidate => candidate.ExecutionId == execution.Id);

                return new ReportExecutionRecord(
                    execution,
                    execution.ScheduleId is ReportScheduleId scheduleId ? names.GetValueOrDefault(scheduleId) : null,
                    artifact?.ByteLength,
                    artifact?.ExpiresAt);
            }),
        ];
    }

    private static bool Violates(DbUpdateException failure, string constraint) =>
        failure.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
        } violation
        && string.Equals(violation.ConstraintName, constraint, StringComparison.Ordinal);

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
