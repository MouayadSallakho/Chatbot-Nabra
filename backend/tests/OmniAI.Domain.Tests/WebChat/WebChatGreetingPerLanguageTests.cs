using OmniAI.Domain.Users;
using OmniAI.Domain.WebChat;

namespace OmniAI.Domain.Tests.WebChat;

/// <summary>
/// Phase 39.1 (note 9): a greeting per page language, with the single greeting as
/// the fallback.
/// </summary>
public sealed class WebChatGreetingPerLanguageTests
{
    private static WebChatWidgetSettings Settings(string? fallback, string? english, string? arabic) =>
        WebChatWidgetSettings.Create(
            WebChatGreeting.CreateOrNone(fallback),
            WebChatAccentColor.Default,
            WebChatLauncherPosition.Right,
            WebChatTheme.System,
            WebChatGreeting.CreateOrNone(english),
            WebChatGreeting.CreateOrNone(arabic));

    [Fact]
    public void Each_language_gets_its_own_greeting()
    {
        WebChatWidgetSettings widget = Settings("Hello", "Hi there", "أهلاً بك");

        Assert.Equal("Hi there", widget.GreetingFor(UserLanguage.English)!.Value);
        Assert.Equal("أهلاً بك", widget.GreetingFor(UserLanguage.Arabic)!.Value);
    }

    [Fact]
    public void A_language_without_its_own_falls_back_to_the_single_greeting()
    {
        WebChatWidgetSettings widget = Settings("Hello", english: null, arabic: "أهلاً بك");

        Assert.Equal("Hello", widget.GreetingFor(UserLanguage.English)!.Value);
        Assert.Equal("أهلاً بك", widget.GreetingFor(UserLanguage.Arabic)!.Value);
    }

    [Fact]
    public void No_language_is_exactly_the_greeting_before_phase_39_1()
    {
        WebChatWidgetSettings widget = Settings("Hello", "Hi there", "أهلاً بك");

        Assert.Equal("Hello", widget.GreetingFor(null)!.Value);
        Assert.Same(widget.Greeting, widget.GreetingFor(null));
    }

    [Fact]
    public void Nothing_set_anywhere_is_no_greeting_in_any_language()
    {
        WebChatWidgetSettings widget = Settings(null, null, null);

        Assert.Null(widget.GreetingFor(UserLanguage.English));
        Assert.Null(widget.GreetingFor(UserLanguage.Arabic));
        Assert.Null(widget.GreetingFor(null));
    }

    [Fact]
    public void The_four_argument_create_and_the_default_have_no_per_language_greeting()
    {
        WebChatWidgetSettings created = WebChatWidgetSettings.Create(
            WebChatGreeting.CreateOrNone("Hello"),
            WebChatAccentColor.Default,
            WebChatLauncherPosition.Right,
            WebChatTheme.System);

        Assert.Null(created.GreetingEn);
        Assert.Null(created.GreetingAr);
        Assert.Null(WebChatWidgetSettings.Default.GreetingEn);
        Assert.Null(WebChatWidgetSettings.Default.GreetingAr);
    }
}
