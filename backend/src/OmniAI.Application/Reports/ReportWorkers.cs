using OmniAI.Application.Tenants.Persistence;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Reports;

namespace OmniAI.Application.Reports;

/// <summary>How one claimed execution ended.</summary>
public enum ReportGenerationOutcome
{
    Completed = 1,
    Failed = 2,

    /// <summary>A transient failure; handed back to be tried again.</summary>
    Released = 3,

    /// <summary>The claim was lost to another worker; nothing was recorded.</summary>
    Superseded = 4,
}

/// <summary>How one claimed execution ended, and the fault behind a failure the platform did not expect.</summary>
/// <param name="Fault">For the worker's log only. Never stored and never shown.</param>
public sealed record ReportGenerationResult(ReportGenerationOutcome Outcome, Exception? Fault = null);

/// <summary>
/// Generates one claimed report, from start to finished file.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The requester's authority is checked again first.</strong> A report
/// is generated under the permissions of whoever asked for it — or, for a
/// schedule, whoever last saved it — as they stand now, not as they stood when
/// the request was queued.
/// </para>
/// <para>
/// <strong>Nothing half-made is ever downloadable.</strong> The file is written
/// through the artifact store's all-or-nothing write, and the execution is marked
/// completed in the same transaction that records the file. If the claim was lost
/// in the meantime, the file is deleted and nothing is recorded.
/// </para>
/// <para>
/// <strong>No provider is called.</strong> Every figure comes from rows this
/// platform already holds.
/// </para>
/// </remarks>
public sealed class ReportGenerator(
    IReportExecutionStore executions,
    IReportArtifactStore artifacts,
    ReportAuthority authority,
    ReportDocumentBuilder builder,
    IEnumerable<IReportRenderer> renderers,
    ITenantRepository tenants,
    ReportOptions options,
    TimeProvider clock)
{
    private readonly IReportExecutionStore _executions =
        executions ?? throw new ArgumentNullException(nameof(executions));

    private readonly IReportArtifactStore _artifacts =
        artifacts ?? throw new ArgumentNullException(nameof(artifacts));

    private readonly ReportAuthority _authority = authority ?? throw new ArgumentNullException(nameof(authority));

    private readonly ReportDocumentBuilder _builder = builder ?? throw new ArgumentNullException(nameof(builder));

    private readonly Dictionary<ReportFormat, IReportRenderer> _renderers =
        (renderers ?? throw new ArgumentNullException(nameof(renderers)))
        .ToDictionary(renderer => renderer.Format);

    private readonly ITenantRepository _tenants = tenants ?? throw new ArgumentNullException(nameof(tenants));

    private readonly ReportOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    private readonly TimeProvider _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    /// <param name="maxAttempts">After this many claims, a transient failure becomes a final one.</param>
    public async Task<ReportGenerationResult> GenerateAsync(
        ClaimedReportExecution claimed,
        int maxAttempts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claimed);

        if (!_artifacts.IsConfigured)
        {
            return new(await FailAsync(claimed, ReportFailures.StorageUnavailable, cancellationToken)
                .ConfigureAwait(false));
        }

        IReadOnlyList<PermissionCode>? held = await _authority
            .PermissionsOfAsync(claimed.TenantId, claimed.RequestedBy, cancellationToken)
            .ConfigureAwait(false);

        if (held is null || !ReportCatalog.Holds(held, ReportCatalog.ToGenerate(claimed.Type)))
        {
            return new(await FailAsync(claimed, ReportFailures.PermissionRevoked, cancellationToken)
                .ConfigureAwait(false));
        }

        StoredReportFile? stored = null;

        try
        {
            // The workspace's name, only for somebody entitled to read it.
            string? workspaceName = held.Contains(PlatformPermissions.WorkspaceView)
                ? (await _tenants.FindByIdAsync(claimed.TenantId, cancellationToken).ConfigureAwait(false))?.Name.Value
                : null;

            ReportDocument document = await _builder
                .BuildAsync(
                    claimed.TenantId,
                    claimed.Type,
                    claimed.Period,
                    workspaceName,
                    _clock.GetUtcNow(),
                    _options.RowLimitFor(claimed.Format),
                    cancellationToken)
                .ConfigureAwait(false);

            IReportRenderer renderer = _renderers[claimed.Format];

            stored = await _artifacts
                .WriteAsync(
                    (stream, token) => renderer.RenderAsync(document, stream, token),
                    _options.MaxArtifactBytes,
                    cancellationToken)
                .ConfigureAwait(false);

            DateTimeOffset now = _clock.GetUtcNow();

            ReportArtifact artifact = ReportArtifact.Create(
                claimed.TenantId,
                claimed.Id,
                claimed.Format,
                ReportCatalog.ContentType(claimed.Format),
                stored.ByteLength,
                stored.Sha256,
                stored.StorageKey,
                now,
                _options.Retention);

            if (await _executions
                .CompleteAsync(claimed.Id, claimed.ClaimToken, artifact, now, cancellationToken)
                .ConfigureAwait(false))
            {
                return new(ReportGenerationOutcome.Completed);
            }

            // Another worker holds the claim now. Its own file will be the one.
            await _artifacts.DeleteAsync(stored.StorageKey, CancellationToken.None).ConfigureAwait(false);

            return new(ReportGenerationOutcome.Superseded);
        }
        catch (ReportTooLargeException)
        {
            return new(await FailAsync(claimed, ReportFailures.TooLarge, cancellationToken).ConfigureAwait(false));
        }
        catch (ReportStorageUnavailableException)
        {
            return new(await FailAsync(claimed, ReportFailures.StorageUnavailable, cancellationToken)
                .ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await DiscardAsync(stored).ConfigureAwait(false);

            throw;
        }
#pragma warning disable CA1031 // A failure here is recorded on the execution, never lost with the worker.
        catch (Exception fault) when (claimed.Attempts >= maxAttempts)
        {
            await DiscardAsync(stored).ConfigureAwait(false);

            return new(
                await FailAsync(claimed, ReportFailures.GenerationFailed, cancellationToken).ConfigureAwait(false),
                fault);
        }
        catch (Exception fault)
#pragma warning restore CA1031
        {
            await DiscardAsync(stored).ConfigureAwait(false);

            await _executions
                .ReleaseAsync(claimed.Id, claimed.ClaimToken, cancellationToken)
                .ConfigureAwait(false);

            return new(ReportGenerationOutcome.Released, fault);
        }
    }

    private async Task<ReportGenerationOutcome> FailAsync(
        ClaimedReportExecution claimed,
        string code,
        CancellationToken cancellationToken)
    {
        bool failed = await _executions
            .FailAsync(claimed.Id, claimed.ClaimToken, code, _clock.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);

        return failed ? ReportGenerationOutcome.Failed : ReportGenerationOutcome.Superseded;
    }

    private async Task DiscardAsync(StoredReportFile? stored)
    {
        if (stored is not null)
        {
            await _artifacts.DeleteAsync(stored.StorageKey, CancellationToken.None).ConfigureAwait(false);
        }
    }
}

/// <summary>
/// Turns due schedules into queued executions.
/// </summary>
/// <remarks>
/// <para>
/// <strong>One occurrence per completed period, whatever happens.</strong> The
/// period is derived from the calendar boundary, not from when this ran, and it is
/// the occurrence's unique key. A crash, a restart or two instances racing produce
/// the same occurrence, which the database accepts once.
/// </para>
/// <para>
/// <strong>An owner who lost their authority stops the schedule.</strong> The
/// owner's permissions are read fresh at every occurrence. If they are no longer a
/// member, or no longer hold what the report needs, the schedule is switched off
/// with a reason instead of generating under authority nobody holds.
/// </para>
/// <para>
/// <strong>Missed occurrences are not replayed one by one.</strong> A deployment
/// that was down for three weeks generates the most recent completed period once
/// and moves on, rather than producing a burst of stale reports.
/// </para>
/// </remarks>
public sealed class ReportScheduleRunner(
    IReportScheduleStore schedules,
    ReportAuthority authority,
    TimeProvider clock)
{
    private readonly IReportScheduleStore _schedules =
        schedules ?? throw new ArgumentNullException(nameof(schedules));

    private readonly ReportAuthority _authority = authority ?? throw new ArgumentNullException(nameof(authority));

    private readonly TimeProvider _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    /// <returns>How many schedules were looked at.</returns>
    public async Task<int> RunDueAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _clock.GetUtcNow();

        await using IDueReportScheduleBatch batch = await _schedules
            .ClaimDueAsync(now, batchSize, cancellationToken)
            .ConfigureAwait(false);

        foreach (ReportSchedule schedule in batch.Schedules)
        {
            bool entitled = await _authority
                .StillHoldsAsync(
                    schedule.TenantId,
                    schedule.OwnerUserId,
                    ReportCatalog.ToSchedule(schedule.Type),
                    cancellationToken)
                .ConfigureAwait(false);

            if (!entitled)
            {
                await batch
                    .DisableAsync(schedule, ReportSchedule.OwnerLostPermission, now, cancellationToken)
                    .ConfigureAwait(false);

                continue;
            }

            DateTimeOffset boundary = ReportScheduleCalendar.PreviousBoundary(schedule.Frequency, now);

            await batch
                .EnqueueAsync(
                    schedule,
                    ReportExecution.ForSchedule(
                        schedule,
                        ReportScheduleCalendar.PeriodEndingAt(schedule.Frequency, boundary),
                        now),
                    ReportScheduleCalendar.NextBoundary(schedule.Frequency, now),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await batch.CommitAsync(cancellationToken).ConfigureAwait(false);

        return batch.Schedules.Count;
    }
}

/// <summary>
/// Removes generated files whose retention has ended.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Bytes first, then the record.</strong> The file is deleted before its
/// row, so a crash in between leaves a row pointing at nothing — which the next
/// pass tidies up, and which a download already refuses because the row has
/// expired — rather than a file nobody knows exists.
/// </para>
/// <para>
/// <strong>Safe to run twice.</strong> Deleting a missing file and expiring an
/// already-expired execution are both no-ops, so two instances sweeping at once do
/// the same work twice rather than conflicting. Only artifacts past their
/// retention are ever touched: an execution still being generated has no artifact
/// row to find.
/// </para>
/// </remarks>
public sealed class ReportRetention(
    IReportExecutionStore executions,
    IReportArtifactStore artifacts,
    TimeProvider clock)
{
    /// <summary>How old an unfinished write must be before it counts as abandoned.</summary>
    public static readonly TimeSpan AbandonedWriteAge = TimeSpan.FromDays(1);

    private readonly IReportExecutionStore _executions =
        executions ?? throw new ArgumentNullException(nameof(executions));

    private readonly IReportArtifactStore _artifacts =
        artifacts ?? throw new ArgumentNullException(nameof(artifacts));

    private readonly TimeProvider _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    /// <returns>How many artifacts were removed.</returns>
    public async Task<int> SweepAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _clock.GetUtcNow();

        IReadOnlyList<ReportArtifact> expired = await _executions
            .ListExpiredArtifactsAsync(now, batchSize, cancellationToken)
            .ConfigureAwait(false);

        foreach (ReportArtifact artifact in expired)
        {
            if (_artifacts.IsConfigured)
            {
                await _artifacts.DeleteAsync(artifact.StorageKey, cancellationToken).ConfigureAwait(false);
            }

            await _executions.MarkExpiredAsync(artifact, now, cancellationToken).ConfigureAwait(false);
        }

        if (_artifacts.IsConfigured)
        {
            await _artifacts
                .SweepAbandonedWritesAsync(now - AbandonedWriteAge, cancellationToken)
                .ConfigureAwait(false);
        }

        return expired.Count;
    }

    /// <summary>
    /// Removes finished files no artifact row references, once they are old
    /// enough that no worker can still be committing them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>The crash window this closes.</strong> A file is finalized, then its
    /// row is written; a process that dies in between leaves bytes nothing points
    /// at. Only files older than <see cref="OrphanGrace"/> are considered — far
    /// longer than the seconds between finalizing a file and committing its row —
    /// and only if no artifact row references the key at the moment of deciding.
    /// </para>
    /// <para>
    /// <strong>Only the store's own keys.</strong> Candidates come from the
    /// artifact store, which returns only names of its own key shape inside its own
    /// root; unfinished writes are a separate directory the store sweeps on its
    /// own schedule. Deleting a missing file is a no-op, so two instances sweeping
    /// at once is harmless.
    /// </para>
    /// </remarks>
    /// <returns>How many orphaned files were removed, and the key to continue after.</returns>
    public async Task<(int Removed, string? NextAfter)> SweepOrphansAsync(
        string? afterKey,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        if (!_artifacts.IsConfigured)
        {
            return (0, null);
        }

        IReadOnlyList<string> keys = await _artifacts
            .ListFinalizedAsync(_clock.GetUtcNow() - OrphanGrace, afterKey, batchSize, cancellationToken)
            .ConfigureAwait(false);

        if (keys.Count == 0)
        {
            return (0, null);
        }

        IReadOnlySet<string> referenced = await _executions
            .ReferencedKeysAsync(keys, cancellationToken)
            .ConfigureAwait(false);

        int removed = 0;

        foreach (string key in keys.Where(key => !referenced.Contains(key)))
        {
            await _artifacts.DeleteAsync(key, cancellationToken).ConfigureAwait(false);
            removed++;
        }

        return (removed, keys.Count < batchSize ? null : keys[^1]);
    }

    /// <summary>
    /// Makes artifacts whose file has disappeared truthfully non-downloadable.
    /// </summary>
    /// <remarks>
    /// Storage lost or replaced outside the platform leaves rows that advertise a
    /// Download that can only fail. Each row whose file is gone has its execution
    /// marked Expired — the existing Phase 31 state for "the file is no longer
    /// available" — and the row removed. Nothing is regenerated.
    /// </remarks>
    /// <returns>How many were reconciled, and the id to continue after.</returns>
    public async Task<(int Reconciled, Guid? NextAfter)> ReconcileMissingAsync(
        Guid? afterId,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        if (!_artifacts.IsConfigured)
        {
            return (0, null);
        }

        IReadOnlyList<ReportArtifact> artifacts = await _executions
            .ListArtifactsAsync(afterId, batchSize, cancellationToken)
            .ConfigureAwait(false);

        int reconciled = 0;
        DateTimeOffset now = _clock.GetUtcNow();

        foreach (ReportArtifact artifact in artifacts)
        {
            // A file younger than the grace may belong to a row committed a moment
            // ago on another instance; its bytes were written first, so a missing
            // file here is genuinely missing.
            if (!await _artifacts.ExistsAsync(artifact.StorageKey, cancellationToken).ConfigureAwait(false))
            {
                await _executions.MarkExpiredAsync(artifact, now, cancellationToken).ConfigureAwait(false);
                reconciled++;
            }
        }

        return (reconciled, artifacts.Count < batchSize ? null : artifacts[^1].Id.Value);
    }

    /// <summary>How old an unreferenced finished file must be before it counts as an orphan.</summary>
    public static readonly TimeSpan OrphanGrace = TimeSpan.FromHours(6);
}
