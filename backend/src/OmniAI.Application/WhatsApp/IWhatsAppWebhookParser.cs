namespace OmniAI.Application.WhatsApp;

/// <summary>
/// Reads a verified webhook body into the few facts this platform acts on.
/// </summary>
/// <remarks>
/// <para>
/// <strong>An abstraction so that the provider's JSON stops at the
/// Infrastructure boundary</strong>, exactly as its URL, its version and its
/// bearer header do. The Application knows that a webhook carries messages and
/// delivery statuses; it does not know that they arrive wrapped in entries,
/// changes, values and metadata, and it should not have to change if that
/// wrapping does.
/// </para>
/// <para>
/// <strong>Called only after the signature has been checked.</strong> Parsing
/// first would mean acting on the shape of a document nobody has authenticated,
/// and the order of those two steps is the whole security of this route.
/// </para>
/// </remarks>
public interface IWhatsAppWebhookParser
{
    /// <summary>
    /// Reads what is actionable out of a verified body.
    /// </summary>
    /// <remarks>
    /// Never throws for a body it does not understand. A payload this platform
    /// has no interpretation for — a notification about something it did not
    /// subscribe to, a field added by the provider, a shape from a newer version
    /// — comes back empty and is acknowledged. The alternative is answering with
    /// a failure, which makes the provider send the same thing again for hours.
    /// </remarks>
    WhatsAppWebhook Parse(ReadOnlySpan<byte> rawBody);
}
