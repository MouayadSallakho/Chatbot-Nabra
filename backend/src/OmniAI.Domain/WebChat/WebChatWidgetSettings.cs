using OmniAI.Domain.Users;

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
        WebChatTheme theme,
        WebChatGreeting? greetingEn,
        WebChatGreeting? greetingAr)
    {
        Greeting = greeting;
        Accent = accent;
        Position = position;
        Theme = theme;
        GreetingEn = greetingEn;
        GreetingAr = greetingAr;
    }

    /// <summary>
    /// The line shown before anybody has said anything, or nothing. Since Phase 39.1
    /// it is the fallback for a language without a greeting of its own.
    /// </summary>
    public WebChatGreeting? Greeting { get; }

    /// <summary>The greeting for English pages, or none (Phase 39.1, note 9).</summary>
    public WebChatGreeting? GreetingEn { get; }

    /// <summary>The greeting for Arabic pages, or none (Phase 39.1, note 9).</summary>
    public WebChatGreeting? GreetingAr { get; }

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
        WebChatTheme.System,
        greetingEn: null,
        greetingAr: null);

    /// <param name="greetingEn">The English page's own greeting (Phase 39.1), or none.</param>
    /// <param name="greetingAr">The Arabic page's own greeting (Phase 39.1), or none.</param>
    public static WebChatWidgetSettings Create(
        WebChatGreeting? greeting,
        WebChatAccentColor accent,
        WebChatLauncherPosition position,
        WebChatTheme theme,
        WebChatGreeting? greetingEn = null,
        WebChatGreeting? greetingAr = null)
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

        return new WebChatWidgetSettings(greeting, accent, position, theme, greetingEn, greetingAr);
    }

    /// <summary>
    /// The greeting a page in this language shows: its own when it has one, else
    /// the fallback <see cref="Greeting"/> (Phase 39.1).
    /// </summary>
    /// <param name="language">
    /// The page's language, or <see langword="null"/> when it named none this
    /// platform knows; then the fallback, exactly as before Phase 39.1.
    /// </param>
    public WebChatGreeting? GreetingFor(UserLanguage? language) => language switch
    {
        UserLanguage.English => GreetingEn ?? Greeting,
        UserLanguage.Arabic => GreetingAr ?? Greeting,
        _ => Greeting,
    };
}
