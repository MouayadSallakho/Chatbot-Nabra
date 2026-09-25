using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniAI.Application.Billing;
using OmniAI.Application.Billing.Persistence;
using OmniAI.Domain.Billing;
using OmniAI.Domain.Tenants;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Infrastructure.Reliability;

namespace OmniAI.Infrastructure.Billing;

/// <summary>
/// How the billing workers pace themselves.
/// </summary>
/// <remarks>
/// Deployment settings only. None of them changes what happens to a
/// subscription — only how soon.
/// </remarks>
internal sealed class BillingWorkerOptions
{
    public const string SectionName = "Billing:Workers";

    /// <summary>How long a worker waits after finding nothing to do.</summary>
    public TimeSpan IdleInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>How many events one pass claims.</summary>
    public int BatchSize { get; set; } = 20;

    /// <summary>
    /// How many times one event may be attempted before it is left alone.
    /// </summary>
    /// <remarks>
    /// Attempts are for transient failures — the provider being briefly
    /// unreachable, a lock contended. Anything the platform can state a reason
    /// for is settled as unprocessable on the first pass and never counted here.
    /// </remarks>
    public int MaxAttempts { get; set; } = 8;

    /// <summary>How often expired grace periods are looked for.</summary>
    /// <remarks>
    /// Minutes rather than seconds. Grace is measured in days, and the cost of
    /// noticing its end a minute late is nothing.
    /// </remarks>
    public TimeSpan GraceSweepInterval { get; set; } = TimeSpan.FromMinutes(5);

    public void Validate()
    {
        if (IdleInterval <= TimeSpan.Zero || IdleInterval > TimeSpan.FromMinutes(5))
        {
            throw new InvalidOperationException(
                $"{SectionName}:IdleInterval must be between zero and five minutes.");
        }

        if (BatchSize is < 1 or > 500)
        {
            throw new InvalidOperationException(
                $"{SectionName}:BatchSize must be between 1 and 500.");
        }

        if (MaxAttempts is < 1 or > 100)
        {
            throw new InvalidOperationException(
                $"{SectionName}:MaxAttempts must be between 1 and 100.");
        }

        if (GraceSweepInterval <= TimeSpan.Zero || GraceSweepInterval > TimeSpan.FromHours(6))
        {
            throw new InvalidOperationException(
                $"{SectionName}:GraceSweepInterval must be between zero and six hours.");
        }
    }
}

/// <summary>
/// Applies verified provider events, outside the webhook's response window.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Claimed with <c>FOR UPDATE SKIP LOCKED</c>, so several instances are
/// safe.</strong> Two web servers both running this worker take disjoint sets
/// rather than the same event twice, and an instance that dies leaves rows that
/// come back rather than rows that are lost.
/// </para>
/// <para>
/// <strong>Nothing here logs a payload.</strong> An event is identified by its
/// row id. Provider references, amounts and customer details never reach a log
/// line, because a log is the easiest place for payment data to end up
/// permanently and by accident.
/// </para>
/// </remarks>
internal sealed partial class BillingEventWorker(
    IServiceScopeFactory scopes,
    IOptions<BillingWorkerOptions> options,
    TimeProvider clock,
    ILogger<BillingEventWorker> logger)
    : BackgroundService
{
    private readonly IServiceScopeFactory _scopes =
        scopes ?? throw new ArgumentNullException(nameof(scopes));

    private readonly BillingWorkerOptions _options =
        options?.Value ?? throw new ArgumentNullException(nameof(options));

    private readonly TimeProvider _clock =
        clock ?? throw new ArgumentNullException(nameof(clock));

    private readonly ILogger<BillingEventWorker> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

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
#pragma warning disable CA1031 // A worker loop that can die is billing that stops.
            catch (Exception failure)
#pragma warning restore CA1031
            {
                LogPassFailed(failure);
            }

            if (handled == 0)
            {
                await Delay(stoppingToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<int> RunBatchAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = _scopes.CreateAsyncScope();

        IReadOnlyList<BillingEvent> claimed = await scope.ServiceProvider
            .GetRequiredService<IBillingEventInbox>()
            .ClaimAsync(
                _options.BatchSize,
                _options.MaxAttempts,
                _clock.GetUtcNow(),
                cancellationToken)
            .ConfigureAwait(false);

        foreach (BillingEvent stored in claimed)
        {
            using System.Diagnostics.Activity work = OmniAiTracing.StartWork("billing-event");

            // A scope each, because applying one event opens a transaction and
            // takes a workspace lock. Sharing a context across the batch would
            // make one workspace's contention everybody else's problem.
            await using AsyncServiceScope inner = _scopes.CreateAsyncScope();

            await HandleAsync(inner.ServiceProvider, stored.Id, cancellationToken)
                .ConfigureAwait(false);
        }

        return claimed.Count;
    }

    private async Task HandleAsync(
        IServiceProvider services,
        BillingEventId id,
        CancellationToken cancellationToken)
    {
        try
        {
            OmniAiDbContext context = services.GetRequiredService<OmniAiDbContext>();

            BillingEvent? stored = await context.BillingEvents
                .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken)
                .ConfigureAwait(false);

            if (stored is null)
            {
                return;
            }

            await services
                .GetRequiredService<ApplyProviderBillingEvent>()
                .HandleAsync(stored, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // One event's failure must not end the batch.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            // Left claimed, with its attempt already counted. The next pass picks
            // it up while attempts remain, which is the right answer for the
            // transient failures that reach here: a provider timeout, a contended
            // lock, a connection dropped mid-transaction.
            LogEventFailed(id.Value, failure);
        }
    }

    private async Task Delay(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(_options.IdleInterval, _clock, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutdown.
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "A billing event pass failed.")]
    private partial void LogPassFailed(Exception failure);

    [LoggerMessage(Level = LogLevel.Error, Message = "Billing event {EventId} could not be applied.")]
    private partial void LogEventFailed(Guid eventId, Exception failure);
}

/// <summary>
/// Ends grace for workspaces that have stopped paying.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This platform's clock, not the provider's.</strong> Stripe may retry a
/// failed payment for however long its own schedule says; how long this product
/// keeps serving an unpaid workspace is a decision made here, and a worker is
/// what makes it happen without waiting for an event that may never come.
/// </para>
/// <para>
/// <strong>Safe on several instances.</strong> Candidates are found with a
/// query, but each suspension is re-checked under the workspace's billing lock
/// before it is written — so two instances selecting the same workspace produce
/// one suspension, and a payment that succeeded in between is honoured rather
/// than overridden.
/// </para>
/// <para>
/// <strong>It downgrades, and destroys nothing.</strong> The workspace moves to
/// the configured fallback plan. Its members, agents, documents and channels are
/// all still there; only new creation above the lower limits is refused.
/// </para>
/// </remarks>
internal sealed partial class PastDueGraceWorker(
    IServiceScopeFactory scopes,
    IOptions<BillingWorkerOptions> options,
    BillingOptions billing,
    TimeProvider clock,
    ILogger<PastDueGraceWorker> logger)
    : BackgroundService
{
    private readonly IServiceScopeFactory _scopes =
        scopes ?? throw new ArgumentNullException(nameof(scopes));

    private readonly BillingWorkerOptions _options =
        options?.Value ?? throw new ArgumentNullException(nameof(options));

    private readonly BillingOptions _billing =
        billing ?? throw new ArgumentNullException(nameof(billing));

    private readonly TimeProvider _clock =
        clock ?? throw new ArgumentNullException(nameof(clock));

    private readonly ILogger<PastDueGraceWorker> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // A deployment that has not configured a fallback plan must not suspend
        // anybody: there would be nowhere to put them, and a workspace with no
        // plan is refused everything.
        if (!_billing.IsConfigured)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // A worker loop that can die is a grace period that never ends.
            catch (Exception failure)
#pragma warning restore CA1031
            {
                LogSweepFailed(failure);
            }

            try
            {
                await Task.Delay(_options.GraceSweepInterval, _clock, stoppingToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = _clock.GetUtcNow();

        await using AsyncServiceScope scope = _scopes.CreateAsyncScope();

        List<TenantId> expired = await scope.ServiceProvider
            .GetRequiredService<OmniAiDbContext>()
            .WorkspaceSubscriptions
            .AsNoTracking()
            .Where(subscription =>
                subscription.Status == SubscriptionStatus.PastDue
                && subscription.GraceEndsAt != null
                && subscription.GraceEndsAt <= now)
            .Select(subscription => subscription.TenantId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        int suspended = 0;

        foreach (TenantId tenantId in expired)
        {
            using System.Diagnostics.Activity work = OmniAiTracing.StartWork("past-due-grace");

            await using AsyncServiceScope inner = _scopes.CreateAsyncScope();

            // Re-checked under the lock inside the service. This selection was
            // made outside it, and a payment may have landed since.
            if (await inner.ServiceProvider
                .GetRequiredService<IBillingSubscriptionService>()
                .SuspendAsync(tenantId, cancellationToken)
                .ConfigureAwait(false))
            {
                suspended++;
            }
        }

        if (suspended > 0)
        {
            LogSuspended(suspended);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "A billing grace sweep failed.")]
    private partial void LogSweepFailed(Exception failure);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Suspended {Count} workspaces whose grace period ended.")]
    private partial void LogSuspended(int count);
}
