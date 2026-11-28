using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniAI.Application.Notifications;
using OmniAI.Domain.Notifications;
using OmniAI.Infrastructure.Reliability;
using OmniAI.Infrastructure.Reports;

namespace OmniAI.Infrastructure.Notifications;

/// <summary>How the notification workers pace themselves. Deployment settings only.</summary>
internal sealed class NotificationWorkerOptions
{
    public const string SectionName = "Notifications:Workers";

    /// <summary>How long the dispatcher waits after finding nothing to do.</summary>
    public TimeSpan DispatchInterval { get; set; } = TimeSpan.FromSeconds(2);

    public int DispatchBatchSize { get; set; } = 20;

    /// <summary>How many times one occurrence may be claimed before it is closed.</summary>
    public int DispatchMaxAttempts { get; set; } = 5;

    public TimeSpan DispatchLease { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How long the email sender waits after finding nothing to do.</summary>
    public TimeSpan EmailInterval { get; set; } = TimeSpan.FromSeconds(5);

    public int EmailBatchSize { get; set; } = 10;

    /// <summary>How many times an email that certainly was not sent may be tried.</summary>
    public int EmailMaxAttempts { get; set; } = 3;

    /// <summary>
    /// How long an email may sit in Sending before it is declared Unknown.
    /// </summary>
    /// <remarks>Far longer than one SMTP timeout, so a slow send is never mistaken for a dead one.</remarks>
    public TimeSpan EmailSendingLease { get; set; } = TimeSpan.FromMinutes(10);

    public TimeSpan RetentionInterval { get; set; } = TimeSpan.FromHours(1);

    public void Validate()
    {
        Require(DispatchInterval > TimeSpan.Zero && DispatchInterval <= TimeSpan.FromMinutes(5), "DispatchInterval must be between zero and five minutes.");
        Require(DispatchBatchSize is >= 1 and <= 200, "DispatchBatchSize must be between 1 and 200.");
        Require(DispatchMaxAttempts is >= 1 and <= 20, "DispatchMaxAttempts must be between 1 and 20.");
        Require(DispatchLease >= TimeSpan.FromSeconds(30) && DispatchLease <= TimeSpan.FromHours(1), "DispatchLease must be between 30 seconds and one hour.");
        Require(EmailInterval > TimeSpan.Zero && EmailInterval <= TimeSpan.FromMinutes(5), "EmailInterval must be between zero and five minutes.");
        Require(EmailBatchSize is >= 1 and <= 100, "EmailBatchSize must be between 1 and 100.");
        Require(EmailMaxAttempts is >= 1 and <= 10, "EmailMaxAttempts must be between 1 and 10.");
        Require(EmailSendingLease >= TimeSpan.FromMinutes(3) && EmailSendingLease <= TimeSpan.FromHours(6), "EmailSendingLease must be between three minutes and six hours.");
        Require(RetentionInterval > TimeSpan.Zero && RetentionInterval <= TimeSpan.FromDays(1), "RetentionInterval must be between zero and one day.");
    }

    private static void Require(bool valid, string message)
    {
        if (!valid)
        {
            throw new InvalidOperationException($"{SectionName}:{message}");
        }
    }
}

/// <summary>
/// Turns occurrences into notifications, outside any request.
/// </summary>
/// <remarks>
/// Nothing sensitive is logged: an occurrence is identified by its id and kind.
/// Titles, bodies, workspace names and recipients never reach a log line.
/// </remarks>
internal sealed partial class NotificationDispatchWorker(
    IServiceScopeFactory scopes,
    IOptions<NotificationWorkerOptions> options,
    TimeProvider clock,
    ILogger<NotificationDispatchWorker> logger)
    : BackgroundService
{
    private readonly NotificationWorkerOptions _options = options.Value;

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
#pragma warning disable CA1031 // A dispatcher that can die is notifications that silently stop.
            catch (Exception failure)
#pragma warning restore CA1031
            {
                LogPassFailed(logger, failure);
            }

            if (handled == 0
                && !await ReportWorkerDelay.WaitAsync(_options.DispatchInterval, clock, stoppingToken).ConfigureAwait(false))
            {
                return;
            }
        }
    }

    private DateTimeOffset _nextEscalationScan;

    private async Task<int> RunBatchAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ClaimedOccurrence> claimed;

        await using (AsyncServiceScope scope = scopes.CreateAsyncScope())
        {
            var now = clock.GetUtcNow();
            if (now >= _nextEscalationScan)
            {
                // Even a failed scan must leave subsequent passes free to deliver other notifications.
                _nextEscalationScan = now.AddSeconds(30);
                int queued = await FirstReplyEscalations.EnqueueAsync(
                    scope.ServiceProvider.GetRequiredService<Persistence.OmniAiDbContext>(), now,
                    _options.DispatchBatchSize, cancellationToken).ConfigureAwait(false);
                if (queued == _options.DispatchBatchSize)
                {
                    _nextEscalationScan = now;
                }
            }
            claimed = await scope.ServiceProvider
                .GetRequiredService<INotificationOccurrenceStore>()
                .ClaimAsync(_options.DispatchBatchSize, _options.DispatchMaxAttempts, _options.DispatchLease, clock.GetUtcNow(), cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (ClaimedOccurrence occurrence in claimed)
        {
            using System.Diagnostics.Activity work = OmniAiTracing.StartWork("notification-dispatch");

            await using AsyncServiceScope scope = scopes.CreateAsyncScope();

            NotificationWorkResult result = await scope.ServiceProvider
                .GetRequiredService<NotificationDispatcher>()
                .DispatchAsync(occurrence, cancellationToken)
                .ConfigureAwait(false);

            OmniAiMetrics.WorkItem("notification-dispatch", result.Outcome.ToString());

            if (result.Fault is not null)
            {
                LogDispatchFailed(logger, occurrence.Id.Value, occurrence.Kind, result.Fault);
            }
        }

        return claimed.Count;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "A notification dispatch pass failed.")]
    private static partial void LogPassFailed(ILogger logger, Exception failure);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notification occurrence {OccurrenceId} ({Kind}) could not be dispatched and was handed back.")]
    private static partial void LogDispatchFailed(ILogger logger, Guid occurrenceId, NotificationKind kind, Exception failure);
}

/// <summary>
/// Sends queued operational emails.
/// </summary>
/// <remarks>
/// Each pass first declares emails a crashed instance left in Sending as Unknown,
/// then claims and sends. Addresses, subjects and bodies are never logged — an
/// email is identified by its id and the outcome by its status.
/// </remarks>
internal sealed partial class OperationalEmailWorker(
    IServiceScopeFactory scopes,
    IOptions<NotificationWorkerOptions> options,
    TimeProvider clock,
    ILogger<OperationalEmailWorker> logger)
    : BackgroundService
{
    private readonly NotificationWorkerOptions _options = options.Value;

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
#pragma warning disable CA1031 // An email worker that can die is operational email that silently stops.
            catch (Exception failure)
#pragma warning restore CA1031
            {
                LogPassFailed(logger, failure);
            }

            if (handled == 0
                && !await ReportWorkerDelay.WaitAsync(_options.EmailInterval, clock, stoppingToken).ConfigureAwait(false))
            {
                return;
            }
        }
    }

    private async Task<int> RunBatchAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ClaimedEmail> claimed;
        DateTimeOffset now = clock.GetUtcNow();

        await using (AsyncServiceScope scope = scopes.CreateAsyncScope())
        {
            INotificationEmailOutbox queue = scope.ServiceProvider.GetRequiredService<INotificationEmailOutbox>();

            int abandoned = await queue
                .AbandonStaleAsync(now - _options.EmailSendingLease, now, cancellationToken)
                .ConfigureAwait(false);

            if (abandoned > 0)
            {
                LogAbandoned(logger, abandoned);
            }

            claimed = await queue
                // Owned-but-undispatched emails whose owner stopped (claim older
                // than the lease) are claimed again (Phase 35).
                .ClaimAsync(_options.EmailBatchSize, _options.EmailMaxAttempts, now, now - _options.EmailSendingLease, cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (ClaimedEmail email in claimed)
        {
            using System.Diagnostics.Activity work = OmniAiTracing.StartWork("operational-email");

            await using AsyncServiceScope scope = scopes.CreateAsyncScope();

            NotificationEmailStatus? status = await scope.ServiceProvider
                .GetRequiredService<OperationalEmailDispatcher>()
                .SendAsync(email, _options.EmailMaxAttempts, cancellationToken)
                .ConfigureAwait(false);

            OmniAiMetrics.Email(status?.ToString() ?? "Superseded");

            if (status is NotificationEmailStatus.Failed or NotificationEmailStatus.Unknown)
            {
                LogNotDelivered(logger, email.Id.Value, status.Value);
            }
        }

        return claimed.Count;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "An operational email pass failed.")]
    private static partial void LogPassFailed(ILogger logger, Exception failure);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Count} operational emails were left sending by a stopped worker and are now Unknown.")]
    private static partial void LogAbandoned(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Operational email {EmailId} ended {Status}.")]
    private static partial void LogNotDelivered(ILogger logger, Guid emailId, NotificationEmailStatus status);
}

/// <summary>Removes notifications and occurrences the retention policy no longer keeps.</summary>
internal sealed partial class NotificationRetentionWorker(
    IServiceScopeFactory scopes,
    IOptions<NotificationWorkerOptions> options,
    TimeProvider clock,
    ILogger<NotificationRetentionWorker> logger)
    : BackgroundService
{
    private const int BatchSize = 500;

    private readonly NotificationWorkerOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            int removed = 0;

            try
            {
                await using AsyncServiceScope scope = scopes.CreateAsyncScope();

                removed = await scope.ServiceProvider
                    .GetRequiredService<NotificationRetention>()
                    .SweepAsync(BatchSize, stoppingToken)
                    .ConfigureAwait(false);

                if (removed > 0)
                {
                    LogRemoved(logger, removed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // A retention worker that can die is notifications kept forever by accident.
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Removed {Count} expired notification rows.")]
    private static partial void LogRemoved(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "A notification retention pass failed.")]
    private static partial void LogPassFailed(ILogger logger, Exception failure);
}
