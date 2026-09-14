using OmniAI.Application.Conversations;
using OmniAI.Application.WhatsApp.Persistence;
using OmniAI.Domain.WhatsApp;

namespace OmniAI.Application.WhatsApp;

/// <summary>One inbound message, already parsed out of a verified payload.</summary>
/// <param name="Type">
/// Meta's type string, exactly as received. Anything but <c>"text"</c> is
/// recorded as ignored.
/// </param>
public sealed record InboundWebhookMessage(
    string PhoneNumberId,
    string ProviderMessageId,
    string From,
    string Type,
    string? Text,
    DateTimeOffset SentAt);

/// <summary>One delivery status, already parsed out of a verified payload.</summary>
public sealed record InboundWebhookStatus(
    string ProviderMessageId,
    string Status,
    string? Reason,
    DateTimeOffset At);

/// <summary>A verified webhook, reduced to what this platform acts on.</summary>
public sealed record WhatsAppWebhook(
    IReadOnlyList<InboundWebhookMessage> Messages,
    IReadOnlyList<InboundWebhookStatus> Statuses)
{
    /// <summary>A payload this platform takes no action on.</summary>
    public static WhatsAppWebhook Empty { get; } = new([], []);
}

/// <summary>What processing a webhook did. For tests and diagnostics only.</summary>
public sealed record WhatsAppWebhookOutcome(
    int Stored,
    int Duplicates,
    int Ignored,
    int Unrouted,
    int StatusesApplied);

/// <summary>
/// Turns a verified webhook into durable rows, and returns.
/// </summary>
/// <remarks>
/// <strong>Nothing expensive happens here.</strong> Meta retries a webhook it
/// does not get a prompt success from, so running retrieval and a provider call
/// before answering would guarantee timeouts and redeliveries — and a redelivery
/// of work already half-done is how a customer gets two replies. This routes,
/// writes and leaves.
/// <para>
/// <strong>The signature is verified before this is reached.</strong> By the
/// time a payload arrives here it is known to have come from Meta; what it is
/// still not trusted for is deciding which workspace it belongs to. That comes
/// from the phone number id in Meta's own metadata, looked up against this
/// platform's connections, and from nothing else in the body.
/// </para>
/// <para>
/// <strong>Everything unroutable is acknowledged.</strong> An unknown number, a
/// disabled connection, a message type this phase does not accept — none is an
/// error, and answering Meta with a failure would only make it send the same
/// thing again for hours. They create no conversation and invoke no AI.
/// </para>
/// </remarks>
public sealed class ReceiveWhatsAppWebhookHandler(
    IWhatsAppConnectionRepository connections,
    IWhatsAppInbox inbox,
    IWhatsAppOutbox outbox,
    IConversationRealtimeNotifier realtime,
    TimeProvider clock)
{
    private readonly IWhatsAppConnectionRepository _connections =
        connections ?? throw new ArgumentNullException(nameof(connections));

    private readonly IWhatsAppInbox _inbox = inbox ?? throw new ArgumentNullException(nameof(inbox));

    private readonly IWhatsAppOutbox _outbox =
        outbox ?? throw new ArgumentNullException(nameof(outbox));

    private readonly IConversationRealtimeNotifier _realtime =
        realtime ?? throw new ArgumentNullException(nameof(realtime));

    private readonly TimeProvider _clock =
        clock ?? throw new ArgumentNullException(nameof(clock));

    /// <summary>Records everything actionable in a verified webhook.</summary>
    public async Task<WhatsAppWebhookOutcome> HandleAsync(
        WhatsAppWebhook webhook,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(webhook);

        int stored = 0;
        int duplicates = 0;
        int ignored = 0;
        int unrouted = 0;
        int applied = 0;

        DateTimeOffset now = _clock.GetUtcNow();

        foreach (InboundWebhookMessage message in webhook.Messages)
        {
            switch (await RecordAsync(message, now, cancellationToken).ConfigureAwait(false))
            {
                case RecordResult.Stored:
                    stored++;
                    break;
                case RecordResult.Duplicate:
                    duplicates++;
                    break;
                case RecordResult.Ignored:
                    ignored++;
                    break;
                default:
                    unrouted++;
                    break;
            }
        }

        foreach (InboundWebhookStatus status in webhook.Statuses)
        {
            if (await ApplyAsync(status, cancellationToken).ConfigureAwait(false))
            {
                applied++;
            }
        }

        return new WhatsAppWebhookOutcome(stored, duplicates, ignored, unrouted, applied);
    }

    private enum RecordResult
    {
        Stored,
        Duplicate,
        Ignored,
        Unrouted,
    }

    private async Task<RecordResult> RecordAsync(
        InboundWebhookMessage message,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Malformed identifiers are shrugged off rather than raised. The request
        // was signed, so it really is from Meta; the right answer is to route it
        // nowhere and acknowledge.
        if (!PhoneNumberId.TryCreate(message.PhoneNumberId, out PhoneNumberId number)
            || !ProviderMessageId.TryCreate(message.ProviderMessageId, out ProviderMessageId id)
            || !WhatsAppCustomerId.TryCreate(message.From, out WhatsAppCustomerId customer))
        {
            return RecordResult.Unrouted;
        }

        WhatsAppRoute? route = await _connections
            .FindRouteAsync(number, cancellationToken)
            .ConfigureAwait(false);

        // An unknown number, or one whose connection is a draft, disabled, or has
        // no credential. Nothing is created and no AI is invoked.
        if (route is null || !route.IsServing)
        {
            return RecordResult.Unrouted;
        }

        bool isText = string.Equals(message.Type, "text", StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(message.Text);

        // An unsupported type is stored as ignored rather than dropped, so a
        // redelivery is recognised and does nothing a second time. It carries no
        // text: inventing one would put words in a customer's mouth and hand
        // them to an AI.
        InboundWhatsAppMessage record = isText
            ? InboundWhatsAppMessage.ForText(
                route.ConnectionId,
                route.TenantId,
                id,
                customer,
                message.Text,
                message.SentAt,
                now)
            : InboundWhatsAppMessage.Ignored(
                route.ConnectionId,
                route.TenantId,
                id,
                customer,
                message.SentAt,
                now);

        bool written = await _inbox.RecordAsync(record, cancellationToken).ConfigureAwait(false);

        return written
            ? isText ? RecordResult.Stored : RecordResult.Ignored
            : RecordResult.Duplicate;
    }

    private async Task<bool> ApplyAsync(
        InboundWebhookStatus status,
        CancellationToken cancellationToken)
    {
        if (!ProviderMessageId.TryCreate(status.ProviderMessageId, out ProviderMessageId id)
            || !TryMap(status.Status, out WhatsAppDeliveryStatus reported))
        {
            return false;
        }

        WhatsAppStatusOutcome outcome = await _outbox
            .ApplyProviderStatusAsync(id, reported, status.Reason, status.At, cancellationToken)
            .ConfigureAwait(false);

        if (!outcome.Changed)
        {
            // Unknown id, duplicate event, or one that arrived out of order.
            // None of the three is an error and none announces anything.
            return false;
        }

        // A delivery receipt changes what a colleague sees beside a message, so
        // the inbox is told. It adds nothing to the transcript: a receipt is not
        // a message, and creating one would put a line in a conversation that
        // nobody said.
        await _realtime
            .AnnounceQuietlyAsync(outcome.TenantId, outcome.ConversationId, cancellationToken)
            .ConfigureAwait(false);

        return true;
    }

    /// <summary>
    /// Maps Meta's status word to this platform's.
    /// </summary>
    /// <remarks>
    /// Only the four that mean something here. Anything else — a word Meta adds
    /// later, or one this phase does not model — is not guessed at.
    /// </remarks>
    private static bool TryMap(string? reported, out WhatsAppDeliveryStatus status)
    {
        switch (reported)
        {
            case "sent":
                status = WhatsAppDeliveryStatus.Sent;
                return true;
            case "delivered":
                status = WhatsAppDeliveryStatus.Delivered;
                return true;
            case "read":
                status = WhatsAppDeliveryStatus.Read;
                return true;
            case "failed":
                status = WhatsAppDeliveryStatus.Failed;
                return true;
            default:
                status = default;
                return false;
        }
    }
}
