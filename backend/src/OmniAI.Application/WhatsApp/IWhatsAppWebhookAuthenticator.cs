namespace OmniAI.Application.WhatsApp;

/// <summary>
/// Decides whether a webhook request really came from Meta.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is the only thing standing between the public internet and a
/// workspace's conversations.</strong> The webhook route cannot be
/// authenticated the way every other route is: there is no session, no cookie
/// and no user. What arrives is an anonymous POST that anybody can send, and the
/// only evidence of who sent it is a signature over the body.
/// </para>
/// <para>
/// <strong>The secrets behind both checks are platform configuration, not
/// workspace data.</strong> They belong to the Meta app this deployment is
/// registered as, are the same for every workspace, and are never stored in the
/// database, never editable from the admin, and never returned by any API.
/// </para>
/// <para>
/// An implementation that has not been configured must refuse everything. A
/// deployment missing its app secret is not a deployment that should accept
/// unverified messages; it is one that should accept none.
/// </para>
/// </remarks>
public interface IWhatsAppWebhookAuthenticator
{
    /// <summary>
    /// Whether this deployment can verify anything at all.
    /// </summary>
    /// <remarks>
    /// Read by a startup check so a misconfigured deployment is visible in a log
    /// line rather than discovered as silence on a channel somebody thought was
    /// live.
    /// </remarks>
    bool IsConfigured { get; }

    /// <summary>
    /// Whether Meta's subscription challenge presented the right token.
    /// </summary>
    /// <remarks>
    /// Compared in constant time. The comparison is against a shared secret, and
    /// an early exit on the first differing byte is how a caller learns it one
    /// character at a time.
    /// </remarks>
    bool IsVerifyTokenValid(string? presented);

    /// <summary>
    /// Whether the signature matches this exact body.
    /// </summary>
    /// <param name="rawBody">
    /// <strong>The bytes as received</strong>, before any parsing. A signature is
    /// over what was sent, so re-serialising a parsed payload and signing that
    /// checks a different document — one an attacker may have arranged to differ
    /// from the one that will actually be acted on.
    /// </param>
    /// <param name="header">
    /// The <c>X-Hub-Signature-256</c> header, in the provider's own form.
    /// </param>
    bool IsSignatureValid(ReadOnlySpan<byte> rawBody, string? header);
}
