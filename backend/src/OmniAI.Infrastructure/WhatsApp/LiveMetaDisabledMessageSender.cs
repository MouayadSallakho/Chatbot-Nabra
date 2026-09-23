using OmniAI.Application.WhatsApp;
using OmniAI.Domain.WhatsApp;

namespace OmniAI.Infrastructure.WhatsApp;

/// <summary>
/// The sender a Development deployment gets when it is pointed at a real
/// provider without anybody having said so.
/// </summary>
/// <remarks>
/// <para>
/// <strong>It exists so that the refusal is structural rather than
/// conditional.</strong> A flag checked inside
/// <see cref="MetaWhatsAppMessageSender"/> would leave the real transport, the
/// real client and the real credential one mistaken branch away from Graph. This
/// type has no <c>HttpClient</c>, is handed no credential it could use and
/// contains no address, so there is no code path from here to Meta to get wrong.
/// </para>
/// <para>
/// <strong>Refused, not unknown.</strong> A send that never left the process
/// definitively did not arrive, and saying so lets the delivery settle as failed
/// with a reason a colleague can read. Calling it ambiguous would be a lie that
/// leaves a row nobody can ever resolve, and the one thing this guard must not do
/// is make the stored state less truthful than a real refusal would.
/// </para>
/// <para>
/// It is a developer-machine guard, so the reason names the variable rather than
/// blaming the deployment: the person reading it is the person who can fix it,
/// and they are usually two minutes from realising what happened.
/// </para>
/// </remarks>
internal sealed class LiveMetaDisabledMessageSender : IWhatsAppMessageSender
{
    /// <summary>What a colleague, and the delivery row, are told.</summary>
    /// <remarks>
    /// No host, no credential, no recipient. The sentence has to survive being
    /// stored, shown in the inbox and pasted into a chat.
    /// </remarks>
    internal const string Reason =
        "Live WhatsApp sending is switched off on this development deployment, so nothing was "
        + "sent and nothing reached WhatsApp. Set "
        + WhatsAppTransportOptions.LiveProviderOptInVariable
        + "="
        + WhatsAppTransportOptions.LiveProviderOptInValue
        + " to allow it, or point this deployment at a local stub.";

    public Task<WhatsAppSendResult> SendTextAsync(
        PhoneNumberId sendFrom,
        WhatsAppCustomerId recipient,
        string text,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        // Argument checking still happens, so that switching the guard off does
        // not suddenly surface validation failures that were being masked.
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(WhatsAppSendResult.Rejected(Reason));
    }
}
