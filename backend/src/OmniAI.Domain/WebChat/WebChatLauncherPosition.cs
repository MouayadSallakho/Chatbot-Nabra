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
}
