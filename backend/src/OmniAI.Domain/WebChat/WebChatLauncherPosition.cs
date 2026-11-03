namespace OmniAI.Domain.WebChat;

/// <summary>Which bottom corner of the customer's page the launcher sits in.</summary>
/// <remarks>
/// An enumeration rather than a CSS value. A workspace choosing where its own
/// launcher sits is reasonable; a workspace supplying arbitrary positioning would
/// be supplying arbitrary CSS into a page it does not own.
/// </remarks>
public enum WebChatLauncherPosition
{
    /// <summary>The bottom-right corner. The convention almost every site follows.</summary>
    Right = 0,

    /// <summary>
    /// The bottom-left corner.
    /// </summary>
    /// <remarks>
    /// Worth offering because a site with its own bottom-right control — a cookie
    /// banner, a back-to-top button, another vendor's widget — has nowhere else to
    /// put this one.
    /// </remarks>
    Left = 1,

    /// <summary>
    /// The bottom corner where the host page's text starts: left on a
    /// left-to-right page, right on a right-to-left one (Phase 37.5a).
    /// </summary>
    /// <remarks>
    /// Resolved by the loader from the page it is embedded in (<c>dir</c> on
    /// <c>&lt;html&gt;</c>, else the computed direction), because only that page
    /// knows which way it reads. One workspace with an English site and an Arabic
    /// site keeps the launcher in the same reading position on both.
    /// </remarks>
    Start = 2,

    /// <summary>
    /// The bottom corner where the host page's text ends: right on a
    /// left-to-right page, left on a right-to-left one (Phase 37.5a).
    /// </summary>
    End = 3,
}
