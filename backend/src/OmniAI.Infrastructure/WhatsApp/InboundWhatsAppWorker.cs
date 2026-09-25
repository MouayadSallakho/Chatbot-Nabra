using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniAI.Application.WhatsApp;
using OmniAI.Application.WhatsApp.Persistence;
using OmniAI.Infrastructure.Reliability;

namespace OmniAI.Infrastructure.WhatsApp;

/// <summary>
/// Takes stored inbound messages through the conversation runtime.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This exists so the webhook can answer Meta immediately.</strong> Meta
/// redelivers anything it is not promptly acknowledged for, and a turn that runs
/// retrieval and a model takes longer than any webhook should. So the request
/// handler stores the message and returns, and this picks it up.
/// </para>
/// <para>
/// <strong>Several instances may run this at once.</strong> Nothing coordinates
/// them: claiming is one atomic statement, so two workers asking together get
/// disjoint sets, and a worker that dies leaves rows that look old and are swept
/// back into the queue. There is no leader, no lease to renew and nothing to
/// reconcile after a restart.
/// </para>
/// <para>
/// Nothing here logs message text, a phone number or a customer identifier. A
/// message is identified by its row id, which means something to somebody reading
/// the database and nothing to anybody else.
/// </para>
/// </remarks>
internal sealed partial class InboundWhatsAppWorker(
    IServiceScopeFactory scopes,
    IOptions<WhatsAppWorkerOptions> options,
    TimeProvider clock,
    ILogger<InboundWhatsAppWorker> logger)
    : BackgroundService
{
    private readonly IServiceScopeFactory _scopes =
        scopes ?? throw new ArgumentNullException(nameof(scopes));

    private readonly WhatsAppWorkerOptions _options =
        options?.Value ?? throw new ArgumentNullException(nameof(options));

    private readonly TimeProvider _clock =
        clock ?? throw new ArgumentNullException(nameof(clock));

    private readonly ILogger<InboundWhatsAppWorker> _logger =
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
                // The loop must outlive anything one pass can raise: a database
                // restart, a transient network fault. Dying here would silently
                // stop every workspace's WhatsApp channel until somebody noticed.
                LogPassFailed(failure);
            }

            if (handled == 0)
            {
                // Only when there was nothing to do. A full batch means more is
                // waiting, and waiting on purpose would be the worker choosing to
                // keep customers queued.
                await Delay(stoppingToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Claims a batch and takes each message through the runtime.</summary>
    /// <returns>How many were handled.</returns>
    private async Task<int> RunBatchAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = _scopes.CreateAsyncScope();

        IWhatsAppInbox inbox = scope.ServiceProvider.GetRequiredService<IWhatsAppInbox>();

        IReadOnlyList<ClaimedInboundMessage> claimed = await inbox
            .ClaimAsync(
                _options.BatchSize,
                _options.MaxAttempts,
                _clock.GetUtcNow(),
                cancellationToken)
            .ConfigureAwait(false);

        foreach (ClaimedInboundMessage message in claimed)
        {
            using System.Diagnostics.Activity work = OmniAiTracing.StartWork("whatsapp-inbound");

            // A scope per message, not per batch. Each one runs its own unit of
            // work against its own database context, so a failure part-way
            // through a batch cannot leave the next message's context holding
            // half-tracked entities from the last.
            await using AsyncServiceScope inner = _scopes.CreateAsyncScope();

            await HandleAsync(inner.ServiceProvider, message, cancellationToken)
                .ConfigureAwait(false);
        }

        return claimed.Count;
    }

    private async Task HandleAsync(
        IServiceProvider services,
        ClaimedInboundMessage message,
        CancellationToken cancellationToken)
    {
        IWhatsAppInbox inbox = services.GetRequiredService<IWhatsAppInbox>();

        try
        {
            await services
                .GetRequiredService<ProcessInboundWhatsAppHandler>()
                .HandleAsync(message, cancellationToken)
                .ConfigureAwait(false);

            await inbox
                .MarkProcessedAsync(message.Id, _clock.GetUtcNow(), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down. The claim is left standing and the sweeper returns
            // it, which is the same path a crash takes.
            throw;
        }
#pragma warning disable CA1031 // One message's failure must not end the batch.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            bool exhausted = message.Attempts >= _options.MaxAttempts;

            LogMessageFailed(message.Id.Value, message.Attempts, exhausted, failure);

            // Given back for another go, unless it has used up its attempts --
            // in which case it is recorded as failed, so it is visible as a thing
            // that did not work rather than as a row that quietly stopped moving.
            if (exhausted)
            {
                await inbox
                    .MarkFailedAsync(message.Id, _clock.GetUtcNow(), cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await inbox
                    .ReleaseAsync(message.Id, _clock.GetUtcNow(), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = _scopes.CreateAsyncScope();

        int released = await scope.ServiceProvider
            .GetRequiredService<IWhatsAppInbox>()
            .ReleaseStaleClaimsAsync(_clock.GetUtcNow() - _options.ClaimTimeout, cancellationToken)
            .ConfigureAwait(false);

        if (released > 0)
        {
            LogClaimsReturned(released);
        }
    }

    /// <summary>Waits, or returns early when the host is stopping.</summary>
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
    /// Source-generated rather than written as <c>LogError</c> calls, which is
    /// what the analyzer asks for and what keeps a hot loop from boxing its
    /// arguments on every pass.
    /// <para>
    /// <strong>Nothing here takes message text, a phone number or a customer
    /// identifier.</strong> A message is named by its row id, which means
    /// something to somebody reading the database and nothing to anybody else.
    /// </para>
    /// </remarks>
    [LoggerMessage(Level = LogLevel.Error, Message = "A WhatsApp inbound pass failed.")]
    private partial void LogPassFailed(Exception failure);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "WhatsApp inbound message {MessageId} failed on attempt {Attempt}. "
            + "Given up on: {Exhausted}.")]
    private partial void LogMessageFailed(
        Guid messageId,
        int attempt,
        bool exhausted,
        Exception failure);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Returned {Count} abandoned WhatsApp inbound messages to the queue.")]
    private partial void LogClaimsReturned(int count);
}
