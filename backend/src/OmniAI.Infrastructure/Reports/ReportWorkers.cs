using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniAI.Application.Reports;
using OmniAI.Infrastructure.Reliability;

namespace OmniAI.Infrastructure.Reports;

/// <summary>
/// How the report workers pace themselves.
/// </summary>
/// <remarks>
/// Deployment settings only. None of them changes what a report contains —
/// only how soon it appears.
/// </remarks>
internal sealed class ReportWorkerOptions
{
    public const string SectionName = "Reports:Workers";

    /// <summary>How long the generation worker waits after finding nothing to do.</summary>
    public TimeSpan IdleInterval { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>How many executions one pass claims.</summary>
    public int BatchSize { get; set; } = 4;

    /// <summary>How many times one execution may be claimed before it is failed.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>
    /// How long a claim is honoured before another worker may take it over.
    /// </summary>
    /// <remarks>
    /// Far longer than any bounded report takes, so a slow generation is not
    /// duplicated; short enough that a crashed instance's work resumes the same
    /// day. The claim token makes a late original harmless either way.
    /// </remarks>
    public TimeSpan Lease { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>How often due schedules are looked for.</summary>
    public TimeSpan ScheduleInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>How often expired files are removed.</summary>
    public TimeSpan RetentionInterval { get; set; } = TimeSpan.FromHours(1);

    public void Validate()
    {
        if (IdleInterval <= TimeSpan.Zero || IdleInterval > TimeSpan.FromMinutes(5))
        {
            throw new InvalidOperationException($"{SectionName}:IdleInterval must be between zero and five minutes.");
        }

        if (BatchSize is < 1 or > 50)
        {
            throw new InvalidOperationException($"{SectionName}:BatchSize must be between 1 and 50.");
        }

        if (MaxAttempts is < 1 or > 10)
        {
            throw new InvalidOperationException($"{SectionName}:MaxAttempts must be between 1 and 10.");
        }

        if (Lease < TimeSpan.FromMinutes(1) || Lease > TimeSpan.FromHours(6))
        {
            throw new InvalidOperationException($"{SectionName}:Lease must be between one minute and six hours.");
        }

        if (ScheduleInterval <= TimeSpan.Zero || ScheduleInterval > TimeSpan.FromHours(1))
        {
            throw new InvalidOperationException($"{SectionName}:ScheduleInterval must be between zero and one hour.");
        }

        if (RetentionInterval <= TimeSpan.Zero || RetentionInterval > TimeSpan.FromDays(1))
        {
            throw new InvalidOperationException($"{SectionName}:RetentionInterval must be between zero and one day.");
        }
    }
}

/// <summary>
/// Generates queued reports, outside any request.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Claimed with <c>FOR UPDATE SKIP LOCKED</c>.</strong> Several instances
/// take disjoint executions, and one that dies leaves work another resumes once
/// the lease lapses. The queue is the table: nothing is held in process memory
/// that a restart could lose.
/// </para>
/// <para>
/// <strong>Nothing sensitive is logged.</strong> An execution is identified by its
/// id. Report contents, workspace names and storage keys never reach a log line.
/// </para>
/// </remarks>
internal sealed partial class ReportGenerationWorker(
    IServiceScopeFactory scopes,
    IOptions<ReportWorkerOptions> options,
    TimeProvider clock,
    ILogger<ReportGenerationWorker> logger)
    : BackgroundService
{
    private readonly ReportWorkerOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            int handled = 0;

            try
            {
                handled = await RunBatchAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // A worker loop that can die is a report queue that stops.
            catch (Exception failure)
#pragma warning restore CA1031
            {
                LogPassFailed(logger, failure);
            }

            if (handled == 0 && !await ReportWorkerDelay.WaitAsync(_options.IdleInterval, clock, stoppingToken).ConfigureAwait(false))
            {
                return;
            }
        }
    }

    private async Task<int> RunBatchAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ClaimedReportExecution> claimed;

        await using (AsyncServiceScope scope = scopes.CreateAsyncScope())
        {
            claimed = await scope.ServiceProvider
                .GetRequiredService<IReportExecutionStore>()
                .ClaimAsync(_options.BatchSize, _options.MaxAttempts, _options.Lease, clock.GetUtcNow(), cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (ClaimedReportExecution execution in claimed)
        {
            using System.Diagnostics.Activity work = OmniAiTracing.StartWork("report-generation");

            // A scope each: one report's queries and transaction are nobody else's.
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();

            long started = System.Diagnostics.Stopwatch.GetTimestamp();

            ReportGenerationResult result = await scope.ServiceProvider
                .GetRequiredService<ReportGenerator>()
                .GenerateAsync(execution, _options.MaxAttempts, cancellationToken)
                .ConfigureAwait(false);

            OmniAiMetrics.ReportGenerated(
                result.Outcome.ToString(),
                System.Diagnostics.Stopwatch.GetElapsedTime(started));
            OmniAiMetrics.WorkItem("report-generation", result.Outcome.ToString());

            if (result.Fault is not null)
            {
                LogGenerationFailed(logger, execution.Id.Value, result.Outcome, result.Fault);
            }
        }

        return claimed.Count;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "A report generation pass failed.")]
    private static partial void LogPassFailed(ILogger logger, Exception failure);

    [LoggerMessage(Level = LogLevel.Error, Message = "Report execution {ExecutionId} failed unexpectedly ({Outcome}).")]
    private static partial void LogGenerationFailed(
        ILogger logger,
        Guid executionId,
        ReportGenerationOutcome outcome,
        Exception failure);
}

/// <summary>Queues each schedule's occurrence when its boundary passes.</summary>
internal sealed partial class ReportScheduleWorker(
    IServiceScopeFactory scopes,
    IOptions<ReportWorkerOptions> options,
    TimeProvider clock,
    ILogger<ReportScheduleWorker> logger)
    : BackgroundService
{
    private const int BatchSize = 25;

    private readonly ReportWorkerOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            int handled = 0;

            try
            {
                await using AsyncServiceScope scope = scopes.CreateAsyncScope();

                handled = await scope.ServiceProvider
                    .GetRequiredService<ReportScheduleRunner>()
                    .RunDueAsync(BatchSize, stoppingToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // A schedule worker that can die is schedules that silently stop.
            catch (Exception failure)
#pragma warning restore CA1031
            {
                LogPassFailed(logger, failure);
            }

            // A full batch means more may be due; anything less means caught up.
            if (handled < BatchSize
                && !await ReportWorkerDelay.WaitAsync(_options.ScheduleInterval, clock, stoppingToken).ConfigureAwait(false))
            {
                return;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "A report schedule pass failed.")]
    private static partial void LogPassFailed(ILogger logger, Exception failure);
}

/// <summary>Removes generated files whose retention has ended.</summary>
internal sealed partial class ReportRetentionWorker(
    IServiceScopeFactory scopes,
    IOptions<ReportWorkerOptions> options,
    TimeProvider clock,
    ILogger<ReportRetentionWorker> logger)
    : BackgroundService
{
    private const int BatchSize = 100;

    private readonly ReportWorkerOptions _options = options.Value;

    // Where the orphan and missing-file scans resume. Held for the life of the
    // process only: losing them on restart just starts the scan from the top.
    private string? _orphanCursor;
    private Guid? _missingCursor;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            int removed = 0;

            try
            {
                await using AsyncServiceScope scope = scopes.CreateAsyncScope();

                ReportRetention retention = scope.ServiceProvider.GetRequiredService<ReportRetention>();

                removed = await retention.SweepAsync(BatchSize, stoppingToken).ConfigureAwait(false);

                if (removed > 0)
                {
                    LogRemoved(logger, removed);
                }

                (int orphans, _orphanCursor) = await retention
                    .SweepOrphansAsync(_orphanCursor, BatchSize, stoppingToken)
                    .ConfigureAwait(false);

                (int missing, _missingCursor) = await retention
                    .ReconcileMissingAsync(_missingCursor, BatchSize, stoppingToken)
                    .ConfigureAwait(false);

                if (orphans > 0 || missing > 0)
                {
                    LogReconciled(logger, orphans, missing);
                    OmniAiMetrics.Reconciled(ReconciledKinds.ReportOrphan, orphans);
                    OmniAiMetrics.Reconciled(ReconciledKinds.ReportMissingFile, missing);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // A retention worker that can die is business data kept forever by accident.
            catch (Exception failure)
#pragma warning restore CA1031
            {
                LogPassFailed(logger, failure);
            }

            if (removed < BatchSize
                && !await ReportWorkerDelay.WaitAsync(_options.RetentionInterval, clock, stoppingToken).ConfigureAwait(false))
            {
                return;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Report storage reconciled: {Orphans} orphaned files removed, {Missing} artifacts with missing files expired.")]
    private static partial void LogReconciled(ILogger logger, int orphans, int missing);

    [LoggerMessage(Level = LogLevel.Information, Message = "Removed {Count} expired report files.")]
    private static partial void LogRemoved(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "A report retention pass failed.")]
    private static partial void LogPassFailed(ILogger logger, Exception failure);
}

internal static class ReportWorkerDelay
{
    /// <returns><see langword="false"/> when the host is stopping.</returns>
    public static async Task<bool> WaitAsync(TimeSpan delay, TimeProvider clock, CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(delay, clock, stoppingToken).ConfigureAwait(false);

            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
