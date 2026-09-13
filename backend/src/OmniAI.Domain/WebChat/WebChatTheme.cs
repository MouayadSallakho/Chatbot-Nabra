namespace OmniAI.Domain.WebChat;

/// <summary>How the widget chooses between its light and dark appearance.</summary>
public enum WebChatTheme
{
    /// <summary>
    /// Follow the visitor's own operating-system preference.
    /// </summary>
    /// <remarks>
    /// The default, and the right one: the visitor already told their device what
    /// they want, and a widget that ignores it is the only dark rectangle on a
    /// light page or the reverse.
    /// </remarks>
    System = 0,

    /// <summary>Always light, whatever the visitor's device prefers.</summary>
    Light = 1,

    /// <summary>Always dark, whatever the visitor's device prefers.</summary>
    Dark = 2,
}
