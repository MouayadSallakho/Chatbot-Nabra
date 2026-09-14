using OmniAI.Domain.WhatsApp;

namespace OmniAI.Application.WhatsApp;

/// <summary>What happened when the platform tried to send.</summary>
public enum WhatsAppSendKind
{
    /// <summary>The provider accepted it and named it.</summary>
    Accepted = 1,

    /// <summary>
    /// The provider definitively refused it. Sending again would be refused
    /// again.
    /// </summary>
    Rejected = 2,

    /// <summary>
    /// It is not known whether the provider received it.
    /// </summary>
    /// <remarks>
    /// A timeout, a dropped connection, a server error that may have been raised
    /// after the message was queued. Nothing retries this: the customer may
    /// already have it.
    /// </remarks>
    Ambiguous = 3,
}

/// <summary>The outcome of one send attempt.</summary>
/// <param name="Reason">
/// A short sentence for a colleague. Never the provider's raw body, which can
/// contain the message text and request metadata.
/// </param>
public sealed record WhatsAppSendResult(
    WhatsAppSendKind Kind,
    ProviderMessageId? ProviderMessageId,
    string? Reason)
{
    /// <summary>The provider accepted it.</summary>
    public static WhatsAppSendResult Accepted(ProviderMessageId id) =>
        new(WhatsAppSendKind.Accepted, id, Reason: null);

    /// <summary>The provider refused it, and would refuse it again.</summary>
    public static WhatsAppSendResult Rejected(string reason) =>
        new(WhatsAppSendKind.Rejected, ProviderMessageId: null, reason);

    /// <summary>Nobody knows whether it arrived.</summary>
    public static WhatsAppSendResult Ambiguous(string reason) =>
        new(WhatsAppSendKind.Ambiguous, ProviderMessageId: null, reason);
}

/// <summary>
/// Sends one text message through the provider.
/// </summary>
/// <remarks>
/// <strong>The Application knows none of the transport.</strong> No Graph
/// address, no API version, no bearer header, no provider JSON. What it knows is
/// that a message goes to a recipient from a number, using a credential, and
/// comes back accepted, refused, or unknown.
/// <para>
/// <strong>Three outcomes rather than success and failure</strong>, because the
/// third is real and acting as though it were one of the other two is how a
/// customer gets the same message twice.
/// </para>
/// <para>
/// Implementations never retry. A retry decision needs to know whether the first
/// attempt could have arrived, and only the caller — holding the durable
/// delivery row — is in a position to reason about that.
/// </para>
/// </remarks>
public interface IWhatsAppMessageSender
{
    /// <summary>Sends a text message.</summary>
    /// <param name="accessToken">
    /// The decrypted credential. Held for the duration of one call and never
    /// logged.
    /// </param>
    Task<WhatsAppSendResult> SendTextAsync(
        PhoneNumberId sendFrom,
        WhatsAppCustomerId recipient,
        string text,
        string accessToken,
        CancellationToken cancellationToken = default);
}
