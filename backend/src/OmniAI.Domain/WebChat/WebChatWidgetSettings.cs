namespace OmniAI.Domain.WebChat;

/// <summary>
/// How a workspace's widget looks on its own website.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Deliberately four settings, and deliberately not a theming system.</strong>
/// A greeting, an accent colour, which corner the launcher sits in, and whether to
/// follow the visitor's light or dark preference. Every one of them is a bounded
/// value with a closed set of possibilities.
/// </para>
/// <para>
/// <strong>What is absent is the point.</strong> No custom CSS, no custom HTML, no
/// custom JavaScript, no uploaded logo, no font. Every one of those is a way for a
/// workspace to put content it controls inside a frame this platform is
/// responsible for, and a widget whose appearance is tenant-supplied markup is a
/// widget whose appearance is a stored cross-site scripting vector. The
/// restriction is not a gap to be filled later without thought; filling it means
/// designing a safe way to do it.
/// </para>
/// </remarks>
public sealed record WebChatWidgetSettings
{
    private WebChatWidgetSettings(
        WebChatGreeting? greeting,
        WebChatAccentColor accent,
        WebChatLauncherPosition position,
        WebChatTheme theme)
    {
        Greeting = greeting;
        Accent = accent;
        Position = position;
        Theme = theme;
    }

    /// <summary>The line shown before anybody has said anything, or nothing.</summary>
    public WebChatGreeting? Greeting { get; }

    public WebChatAccentColor Accent { get; }

    public WebChatLauncherPosition Position { get; }

    public WebChatTheme Theme { get; }

    /// <summary>
    /// What a workspace gets before it has chosen anything.
    /// </summary>
    /// <remarks>
    /// A real, usable widget rather than an unconfigured one: no greeting, the
    /// platform accent, bottom-right, and following the visitor's own preference.
    /// An endpoint created before this feature existed reads back as exactly this.
    /// </remarks>
    public static WebChatWidgetSettings Default { get; } = new(
        greeting: null,
        WebChatAccentColor.Default,
        WebChatLauncherPosition.Right,
        WebChatTheme.System);

    public static WebChatWidgetSettings Create(
        WebChatGreeting? greeting,
        WebChatAccentColor accent,
        WebChatLauncherPosition position,
        WebChatTheme theme)
    {
        ArgumentNullException.ThrowIfNull(accent);

        if (!Enum.IsDefined(position))
        {
            throw new DomainValidationException(
                "The launcher position must be Left, Right, Start or End.")
            {
                Code = "website-chat.launcher_position.invalid",
            };
        }

        if (!Enum.IsDefined(theme))
        {
            throw new DomainValidationException(
                "The widget theme must be System, Light or Dark.")
            {
                Code = "website-chat.theme.invalid",
            };
        }

        return new WebChatWidgetSettings(greeting, accent, position, theme);
    }
}
