using OmniAI.Application.Conversations;
using OmniAI.Application.Conversations.Persistence;
using OmniAI.Application.Security;
using OmniAI.Application.WhatsApp.Persistence;
using OmniAI.Domain.WhatsApp;

namespace OmniAI.Application.WhatsApp;

/// <summary>
/// Sends one queued message to WhatsApp, and records honestly what happened.
/// </summary>
/// <remarks>
/// <strong>Nothing here runs inside a database transaction.</strong> The claim
/// is committed, the send happens, the outcome is committed. A transaction
/// spanning the HTTP call would hold a row lock for the length of somebody
/// else's network, and would roll back a send that had already reached Meta.
/// <para>
/// <strong>Ambiguity is a first-class outcome.</strong> A timeout or a dropped
/// connection means the request may or may not have arrived. It is recorded as
/// unknown and never retried: sending again risks showing the customer the same
/// message twice, which is worse than a state an administrator has to look at.
/// Only a definitive provider rejection is a failure.
/// </para>
/// <para>
/// <strong>The service window is re-checked here, immediately before the
/// send.</strong> A delivery can sit in the outbox while the window closes —
/// a colleague typing a long reply, a worker catching up after an outage — and
/// the only check that means anything is the one taken at the moment of sending.
/// </para>
/// </remarks>
public sealed class DispatchWhatsAppDeliveryHandler(
    IWhatsAppConnectionRepository connections,
    IWhatsAppOutbox outbox,
    IConversationRepository conversations,
    IWhatsAppMessageSender sender,
    ISecretProtector secrets,
    CustomerServiceWindow window,
    IConversationRealtimeNotifier realtime,
    TimeProvider clock)
{
    private readonly IWhatsAppConnectionRepository _connections =
        connections ?? throw new ArgumentNullException(nameof(connections));

    private readonly IWhatsAppOutbox _outbox =
        outbox ?? throw new ArgumentNullException(nameof(outbox));

    private readonly IConversationRepository _conversations =
        conversations ?? throw new ArgumentNullException(nameof(conversations));

    private readonly IWhatsAppMessageSender _sender =
        sender ?? throw new ArgumentNullException(nameof(sender));

    private readonly ISecretProtector _secrets =
        secrets ?? throw new ArgumentNullException(nameof(secrets));

    private readonly CustomerServiceWindow _window =
        window ?? throw new ArgumentNullException(nameof(window));

    private readonly IConversationRealtimeNotifier _realtime =
        realtime ?? throw new ArgumentNullException(nameof(realtime));

    private readonly TimeProvider _clock =
        clock ?? throw new ArgumentNullException(nameof(clock));

    /// <summary>Sends one claimed delivery.</summary>
    public async Task HandleAsync(
        ClaimedDelivery claimed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claimed);

        // Re-checked here rather than trusted from when the message was queued.
        if (!await _window
            .IsOpenAsync(_conversations, claimed.ConversationId, cancellationToken)
            .ConfigureAwait(false))
        {
            await FailAsync(claimed, CustomerServiceWindow.ClosedReason, cancellationToken)
                .ConfigureAwait(false);

            return;
        }

        byte[]? protectedToken = await _connections
            .FindProtectedCredentialAsync(claimed.ConnectionId, cancellationToken)
            .ConfigureAwait(false);

        string? accessToken = _secrets.Unprotect(
            protectedToken,
            new SecretContext(
                SecretPurposes.WhatsAppAccessToken,
                claimed.TenantId.Value,
                claimed.ConnectionId.Value));

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            // No credential, or one this deployment cannot decrypt: a missing
            // master key, a rotated one, or a ciphertext that does not belong to
            // this connection. All of them mean the same thing to a colleague —
            // this cannot be sent until somebody re-enters the token — and none
            // of them is worth a retry.
            await FailAsync(
                    claimed,
                    "This connection has no usable access token. Enter it again in the "
                    + "WhatsApp settings.",
                    cancellationToken)
                .ConfigureAwait(false);

            return;
        }

        WhatsAppSendResult result = await _sender
            .SendTextAsync(
                claimed.PhoneNumberId,
                claimed.Recipient,
                claimed.Text,
                accessToken,
                cancellationToken)
            .ConfigureAwait(false);

        DateTimeOffset now = _clock.GetUtcNow();

        switch (result.Kind)
        {
            case WhatsAppSendKind.Accepted when result.ProviderMessageId is ProviderMessageId id:
                await _outbox.MarkSentAsync(claimed.Id, id, now, cancellationToken)
                    .ConfigureAwait(false);
                break;

            case WhatsAppSendKind.Accepted:
                // Accepted without an id. Nothing can match a later status
                // webhook to this delivery, so what happened next is unknowable.
                await _outbox
                    .MarkUnknownAsync(
                        claimed.Id,
                        "The provider accepted the message without naming it.",
                        now,
                        cancellationToken)
                    .ConfigureAwait(false);
                break;

            case WhatsAppSendKind.Rejected:
                await _outbox.MarkFailedAsync(claimed.Id, result.Reason, now, cancellationToken)
                    .ConfigureAwait(false);
                break;

            default:
                await _outbox.MarkUnknownAsync(claimed.Id, result.Reason, now, cancellationToken)
                    .ConfigureAwait(false);
                break;
        }

        // The inbox shows delivery state beside each message, so a change to it
        // is a change to what a colleague is looking at.
        await _realtime
            .AnnounceQuietlyAsync(claimed.TenantId, claimed.ConversationId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task FailAsync(
        ClaimedDelivery claimed,
        string reason,
        CancellationToken cancellationToken)
    {
        await _outbox
            .MarkFailedAsync(claimed.Id, reason, _clock.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);

        await _realtime
            .AnnounceQuietlyAsync(claimed.TenantId, claimed.ConversationId, cancellationToken)
            .ConfigureAwait(false);
    }
}
