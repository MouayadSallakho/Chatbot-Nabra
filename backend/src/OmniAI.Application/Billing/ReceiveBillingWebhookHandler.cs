using OmniAI.Application.Billing.Persistence;
using OmniAI.Domain.Billing;

namespace OmniAI.Application.Billing;

/// <summary>
/// What a verified provider event tells this platform.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Normalised, and deliberately thin.</strong> A handful of opaque
/// references, a type and a timestamp. Everything else the provider sent —
/// amounts, invoices, payment methods, customer details, the whole raw document —
/// is read for these fields and then discarded.
/// </para>
/// <para>
/// <strong>None of it is treated as authority.</strong> An event says something
/// may have changed; what actually changed is read back from the provider
/// afterwards. That is what makes late and out-of-order delivery harmless.
/// </para>
/// </remarks>
public sealed record BillingWebhookEvent(
    ProviderReference EventId,
    string EventType,
    ProviderReference? SubscriptionId,
    ProviderReference? CustomerId,
    ProviderReference? SessionId,
    DateTimeOffset CreatedAt);

/// <summary>
/// Proves a webhook came from the provider.
/// </summary>
/// <remarks>
/// <strong>Verification takes the raw bytes.</strong> Not a parsed document, not
/// a re-serialised copy — the exact body as received. Signing one document and
/// acting on another is the standard way a signed-webhook route is defeated, and
/// an interface that accepted a parsed object could not avoid it.
/// </remarks>
public interface IBillingWebhookVerifier
{
    BillingProvider Provider { get; }

    /// <summary>Whether this deployment has the secret needed to verify anything.</summary>
    /// <remarks>
    /// False means every webhook is refused. A deployment that cannot check
    /// signatures must not accept events, because accepting them unchecked would
    /// let anybody grant themselves a plan.
    /// </remarks>
    bool CanVerify { get; }

    /// <summary>
    /// Reads a verified event, or nothing.
    /// </summary>
    /// <returns>
    /// <see langword="null"/> when the signature is missing, malformed, stale or
    /// does not match the body. The caller cannot tell which, and neither can the
    /// sender.
    /// </returns>
    BillingWebhookEvent? Verify(byte[] rawBody, string? signatureHeader);
}

/// <summary>
/// Stores a verified event and returns.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The webhook writes; a worker acts.</strong> Providers expect an
/// acknowledgement within seconds and redeliver anything slower. Taking a
/// workspace lock, calling back to the provider and reassigning a plan does not
/// belong inside that window, so the endpoint's whole job is to make the event
/// durable.
/// </para>
/// <para>
/// <strong>A duplicate is a success.</strong> Providers redeliver, sometimes
/// concurrently. The unique provider event id means the second copy writes
/// nothing, and answering it with a failure would only cause a third.
/// </para>
/// </remarks>
public sealed class ReceiveBillingWebhookHandler(IBillingEventInbox inbox, TimeProvider clock)
{
    private readonly IBillingEventInbox _inbox =
        inbox ?? throw new ArgumentNullException(nameof(inbox));

    private readonly TimeProvider _clock =
        clock ?? throw new ArgumentNullException(nameof(clock));

    /// <returns>Whether this event was new. Either way the caller answers success.</returns>
    public async Task<bool> HandleAsync(
        BillingProvider provider,
        BillingWebhookEvent verified,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(verified);

        BillingEvent stored = BillingEvent.Receive(
            provider,
            verified.EventId,
            verified.EventType,
            verified.CreatedAt,
            _clock.GetUtcNow());

        stored.Concerns(verified.SubscriptionId, verified.CustomerId, verified.SessionId);

        return await _inbox.ReceiveAsync(stored, cancellationToken).ConfigureAwait(false);
    }
}
