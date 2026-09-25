using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniAI.Application.WhatsApp;
using OmniAI.Application.WhatsApp.Persistence;
using OmniAI.Infrastructure.Reliability;

namespace OmniAI.Infrastructure.WhatsApp;

/// <summary>
/// Sends queued messages to WhatsApp customers.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A send is attempted once.</strong> Unlike the inbound worker, this one
/// has no retry: an attempt that ended ambiguously may already have reached the
/// customer, and trying again would risk showing them the same words twice. The
/// row is settled as sent, failed or unknown, and an unknown one is a thing a
/// person looks at rather than a thing the machine keeps poking.
/// </para>
/// <para>
/// <strong>A crash leaves the row mid-send, and that is the honest state.</strong>
/// The sweeper does not re-queue those; it marks them unknown, for the same
/// reason.
/// </para>
/// <para>
/// Nothing here logs the message, the recipient or the credential. A delivery is
/// identified by its row id.
/// </para>
/// </remarks>
internal sealed partial class WhatsAppDeliveryWorker(
    IServiceScopeFactory scopes,
    IOptions<WhatsAppWorkerOptions> options,
    TimeProvider clock,
    ILogger<WhatsAppDeliveryWorker> logger)
    : BackgroundService
{
    private readonly IServiceScopeFactory _scopes =
        scopes ?? throw new ArgumentNullException(nameof(scopes));

    private readonly WhatsAppWorkerOptions _options =
        options?.Value ?? throw new ArgumentNullException(nameof(options));

    private readonly TimeProvider _clock =
        clock ?? throw new ArgumentNullException(nameof(clock));

    private readonly ILogger<WhatsAppDeliveryWorker> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        DateTimeOffset nextSweep = _clock.GetUtcNow();

        while (!stoppingToken.IsCancellationRequested)
        {
            int handled = 0;

            try
            {
                if (_clock.GetUtcNow() >= nextSweep)
                {
                    await SweepAsync(stoppingToken).ConfigureAwait(false);

                    nextSweep = _clock.GetUtcNow() + _options.SweepInterval;
                }

                handled = await RunBatchAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // A worker loop that can die is a channel that stops.
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

        // One attempt each, which is why the claim asks for a max of one. The
        // filter is the same shape as the inbound worker's so that the two read
        // alike, but here it means "never a second time".
        IReadOnlyList<ClaimedDelivery> claimed = await scope.ServiceProvider
            .GetRequiredService<IWhatsAppOutbox>()
            .ClaimAsync(_options.BatchSize, maxAttempts: 1, _clock.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);

        foreach (ClaimedDelivery delivery in claimed)
        {
            using System.Diagnostics.Activity work = OmniAiTracing.StartWork("whatsapp-delivery");

            await using AsyncServiceScope inner = _scopes.CreateAsyncScope();

            await HandleAsync(inner.ServiceProvider, delivery, cancellationToken)
                .ConfigureAwait(false);
        }

        return claimed.Count;
    }

    private async Task HandleAsync(
        IServiceProvider services,
        ClaimedDelivery delivery,
        CancellationToken cancellationToken)
    {
        try
        {
            await services
                .GetRequiredService<DispatchWhatsAppDeliveryHandler>()
                .HandleAsync(delivery, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // One delivery's failure must not end the batch.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            // Deliberately not settled here. The row stays mid-send and the
            // sweeper marks it unknown, which is what it is: the handler failed
            // somewhere, and whether that was before or after Meta saw the
            // message is not something this catch block can know.
            LogDeliveryFailed(delivery.Id.Value, failure);
        }
    }

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = _scopes.CreateAsyncScope();

        int abandoned = await scope.ServiceProvider
            .GetRequiredService<IWhatsAppOutbox>()
            .AbandonStaleSendsAsync(_clock.GetUtcNow() - _options.ClaimTimeout, cancellationToken)
            .ConfigureAwait(false);

        if (abandoned > 0)
        {
            LogSendsAbandoned(abandoned);
        }
    }

    private async Task Delay(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(_options.IdleInterval, _clock, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutdown during the idle wait is the ordinary way this ends.
        }
    }

    /// <summary>
    /// The worker's log messages.
    /// </summary>
    /// <remarks>
    /// Source-generated, and deliberately narrow: a delivery is named by its row
    /// id. The message, the recipient and the credential never appear.
    /// </remarks>
    [LoggerMessage(Level = LogLevel.Error, Message = "A WhatsApp delivery pass failed.")]
    private partial void LogPassFailed(Exception failure);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "WhatsApp delivery {DeliveryId} failed while being sent.")]
    private partial void LogDeliveryFailed(Guid deliveryId, Exception failure);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Marked {Count} WhatsApp deliveries unknown after their sender stopped.")]
    private partial void LogSendsAbandoned(int count);
}
