using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniAI.Infrastructure.Reports;

namespace OmniAI.Infrastructure.Reliability;

/// <summary>
/// How long work may stay in flight before a reconciler may call it stale.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Derived from the real bounds, and checked against them.</strong> One
/// provider operation lasts at most one provider timeout (the OpenAI and
/// embedding clients have no retries of their own). One indexing attempt makes at
/// most <c>ceil(MaxChunks / MaxBatchSize)</c> sequential embedding calls. Startup
/// refuses a threshold that is not comfortably beyond those bounds, so a slow but
/// legitimate request can never be reconciled while it is still running.
/// </para>
/// <para>
/// Defaults: 15 minutes for provider operations (30 s default timeout), 30 minutes
/// for indexing attempts (13 calls × 30 s ≈ 6.5 minutes at the defaults).
/// </para>
/// </remarks>
internal sealed class ReliabilityOptions
{
    public const string SectionName = "Reliability";

    /// <summary>The margin every stale threshold must leave beyond its computed bound.</summary>
    public static readonly TimeSpan Margin = TimeSpan.FromMinutes(5);

    public TimeSpan ProviderOperationStaleAfter { get; set; } = TimeSpan.FromMinutes(15);

    public TimeSpan KnowledgeAttemptStaleAfter { get; set; } = TimeSpan.FromMinutes(30);

    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(1);

    public int BatchSize { get; set; } = 100;

    /// <param name="providerTimeout">The longest single provider request this deployment allows.</param>
    /// <param name="indexingBound">The longest a real indexing attempt can legitimately take.</param>
    public void Validate(TimeSpan providerTimeout, TimeSpan indexingBound)
    {
        if (ProviderOperationStaleAfter < (providerTimeout * 2) + Margin)
        {
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(ProviderOperationStaleAfter)} must be at least twice the provider timeout plus {Margin.TotalMinutes} minutes.");
        }

        if (KnowledgeAttemptStaleAfter < indexingBound + Margin)
        {
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(KnowledgeAttemptStaleAfter)} must exceed the longest indexing attempt ({indexingBound}) by {Margin.TotalMinutes} minutes.");
        }

        if (Interval <= TimeSpan.Zero || Interval > TimeSpan.FromHours(1))
        {
            throw new InvalidOperationException($"{SectionName}:{nameof(Interval)} must be between zero and one hour.");
        }

        if (BatchSize is < 1 or > 1000)
        {
            throw new InvalidOperationException($"{SectionName}:{nameof(BatchSize)} must be between 1 and 1000.");
        }
    }
}

/// <summary>
/// Settles work a crashed or restarted process left in flight.
/// </summary>
/// <remarks>
/// <para>
/// Two reconcilers, each isolated from the other's failure: provider operations
/// left Started become Unknown, and indexing attempts left Started are settled and
/// alerted on exactly once. Both are single conditional statements with
/// <c>FOR UPDATE SKIP LOCKED</c>, so every instance can run this at once.
/// </para>
/// <para>
/// <strong>No paid work, ever.</strong> Nothing here calls a provider or retries a
/// request; recovery only makes the records truthful.
/// </para>
/// <para>
/// A full batch means more may be waiting, so the next pass starts at once; the
/// worker otherwise sleeps its interval, and stops cleanly with the host.
/// </para>
/// </remarks>
internal sealed partial class StaleWorkReconciliationWorker(
    IServiceScopeFactory scopes,
    IOptions<ReliabilityOptions> options,
    TimeProvider clock,
    ILogger<StaleWorkReconciliationWorker> logger)
    : BackgroundService
{
    private readonly ReliabilityOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            int providers = await RunAsync(
                    ReconciledKinds.ProviderUsage,
                    (scope, now, token) => scope.GetRequiredService<ProviderUsageReconciler>()
                        .ReconcileAsync(now - _options.ProviderOperationStaleAfter, now, _options.BatchSize, token),
                    stoppingToken)
                .ConfigureAwait(false);

            int attempts = await RunAsync(
                    ReconciledKinds.KnowledgeAttempt,
                    (scope, now, token) => scope.GetRequiredService<KnowledgeAttemptReconciler>()
                        .ReconcileAsync(now - _options.KnowledgeAttemptStaleAfter, now, _options.BatchSize, token),
                    stoppingToken)
                .ConfigureAwait(false);

            if (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            bool more = providers >= _options.BatchSize || attempts >= _options.BatchSize;

            if (!more && !await ReportWorkerDelay.WaitAsync(_options.Interval, clock, stoppingToken).ConfigureAwait(false))
            {
                return;
            }
        }
    }

    private async Task<int> RunAsync(
        string kind,
        Func<IServiceProvider, DateTimeOffset, CancellationToken, Task<int>> reconcile,
        CancellationToken stoppingToken)
    {
        long started = Stopwatch.GetTimestamp();

        try
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();

            int settled = await reconcile(scope.ServiceProvider, clock.GetUtcNow(), stoppingToken).ConfigureAwait(false);

            if (settled > 0)
            {
                LogSettled(logger, kind, settled, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                OmniAiMetrics.Reconciled(kind, settled);
            }

            return settled;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return 0;
        }
#pragma warning disable CA1031 // One reconciler failing must not stop the other, or the worker.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            LogPassFailed(logger, kind, failure);
            OmniAiMetrics.WorkItem("stale-work-reconciliation", "failed");

            return 0;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reconciled {Count} stale {Kind} records in {ElapsedMs} ms.")]
    private static partial void LogSettled(ILogger logger, string kind, int count, double elapsedMs);

    [LoggerMessage(Level = LogLevel.Error, Message = "A {Kind} reconciliation pass failed.")]
    private static partial void LogPassFailed(ILogger logger, string kind, Exception failure);
}
