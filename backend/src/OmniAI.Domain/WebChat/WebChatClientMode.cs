namespace OmniAI.Domain.WebChat;

/// <summary>
/// How a visitor reached a web chat conversation.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is an authorisation boundary, not a label.</strong> The two modes
/// authenticate through genuinely different mechanisms, and a session opened under
/// one must never be usable through the other: a hosted cookie arriving at an
/// embedded route, or an embedded credential arriving at a hosted one, is refused
/// before anything else is considered.
/// </para>
/// <para>
/// Without it, the weaker of the two transports would silently become the
/// strength of both. The hosted cookie is ambient and path-scoped and is
/// protected by antiforgery; the embedded credential is explicit and is protected
/// by not being ambient at all. Mixing them would mean an attacker only had to
/// defeat whichever was easier.
/// </para>
/// </remarks>
public enum WebChatClientMode
{
    /// <summary>
    /// The page OmniAI hosts at <c>/chat/{publicKey}</c>.
    /// </summary>
    /// <remarks>
    /// Authenticated by the HttpOnly, path-scoped conversation cookie introduced
    /// in Phase 15 and narrowed in Phase 16. First, so an unset column on a row
    /// written before this existed reads as the mode that row actually was.
    /// </remarks>
    Hosted = 0,

    /// <summary>
    /// The widget embedded in an iframe on a customer's own website.
    /// </summary>
    /// <remarks>
    /// Authenticated by an explicit credential the iframe holds and sends in a
    /// request header. Deliberately not a cookie: a browser that blocks
    /// third-party cookies would otherwise break every embedded widget, and the
    /// fix for that must not be to ask customers to weaken their browser.
    /// </remarks>
    Embedded = 1,
}
