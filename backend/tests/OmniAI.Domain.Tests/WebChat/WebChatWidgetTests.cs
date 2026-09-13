using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.WebChat;

namespace OmniAI.Domain.Tests.WebChat;

/// <summary>
/// What a workspace may choose about an embedded widget, and where it may run.
/// </summary>
/// <remarks>
/// <strong>Appearance is four values and not a stylesheet.</strong> The widget
/// runs on a visitor's machine, inside a frame this platform serves. A colour and
/// a corner cannot do anything; a font URL, a logo upload or a block of CSS would
/// each be a way for one tenant's settings screen to decide what loads on another
/// company's customers' browsers.
/// </remarks>
public sealed class WebChatWidgetTests
{
    private static readonly DateTimeOffset Noon =
        new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

    private static readonly TenantId Workspace = TenantId.New();
    private static readonly AiAgentId Agent = AiAgentId.New();

    private static WebChatEndpoint NewEndpoint() =>
        WebChatEndpoint.Create(
            Workspace,
            Agent,
            WebChatPublicKey.Create(new string('a', WebChatPublicKey.Length)),
            Noon);

    private static WebChatOrigin Origin(string value) => WebChatOrigin.Create(value);

    // ------------------------------------------------------------- appearance --

    [Fact]
    public void A_new_endpoint_has_the_platform_defaults()
    {
        WebChatEndpoint endpoint = NewEndpoint();

        Assert.Null(endpoint.Widget.Greeting);
        Assert.Equal(WebChatAccentColor.DefaultValue, endpoint.Widget.Accent.Value);
        Assert.Equal(WebChatLauncherPosition.Right, endpoint.Widget.Position);
        Assert.Equal(WebChatTheme.System, endpoint.Widget.Theme);
        Assert.Empty(endpoint.AllowedOrigins);
    }

    [Theory]
    [InlineData("#fff", "#ffffff")]
    [InlineData("#FFF", "#ffffff")]
    [InlineData("#2563EB", "#2563eb")]
    [InlineData("  #2563eb  ", "#2563eb")]
    public void An_accent_is_stored_in_one_form(string candidate, string expected)
    {
        Assert.Equal(expected, WebChatAccentColor.Create(candidate).Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("red")]
    [InlineData("rgb(0 0 0)")]
    [InlineData("#12345")]
    [InlineData("#gggggg")]
    [InlineData("#2563eb; background: url(evil)")]
    [InlineData("var(--anything)")]
    public void An_accent_that_is_not_a_colour_is_refused(string candidate)
    {
        // The value reaches a CSS custom property in the visitor's browser. Six
        // hex digits cannot introduce a declaration; anything else might.
        Assert.Throws<DomainValidationException>(() => WebChatAccentColor.Create(candidate));
    }

    [Fact]
    public void A_blank_greeting_is_no_greeting_rather_than_an_empty_one()
    {
        Assert.Null(WebChatGreeting.CreateOrNone(null));
        Assert.Null(WebChatGreeting.CreateOrNone(""));
        Assert.Null(WebChatGreeting.CreateOrNone("   "));
    }

    [Fact]
    public void A_greeting_is_trimmed_and_bounded()
    {
        Assert.Equal("Hello", WebChatGreeting.CreateOrNone("  Hello  ")!.Value);

        Assert.Throws<DomainValidationException>(
            () => WebChatGreeting.CreateOrNone(new string('a', WebChatGreeting.MaxLength + 1)));
    }

    [Fact]
    public void A_greeting_may_contain_line_breaks_and_nothing_else_invisible()
    {
        // Two lines is an ordinary greeting, and carriage returns are normalised
        // so the stored value does not depend on which machine typed it.
        Assert.NotNull(WebChatGreeting.CreateOrNone("Hello\nAsk us anything."));
        Assert.Equal("a\nb", WebChatGreeting.CreateOrNone("a\r\nb")!.Value);

        // Anything else in the control range would corrupt every log line and
        // every rendering of it.
        Assert.Throws<DomainValidationException>(() => WebChatGreeting.CreateOrNone("Hello\u0007"));
        Assert.Throws<DomainValidationException>(
            () => WebChatGreeting.CreateOrNone("Hello\u001b[31m"));
    }

    [Fact]
    public void A_position_or_theme_outside_the_enum_is_refused()
    {
        // A cast integer is the way an invalid enum reaches a domain type, and it
        // would otherwise be written to the database as a name nothing can read.
        Assert.Throws<DomainValidationException>(
            () => WebChatWidgetSettings.Create(
                null,
                WebChatAccentColor.Default,
                (WebChatLauncherPosition)7,
                WebChatTheme.System));

        Assert.Throws<DomainValidationException>(
            () => WebChatWidgetSettings.Create(
                null,
                WebChatAccentColor.Default,
                WebChatLauncherPosition.Right,
                (WebChatTheme)9));
    }

    // ---------------------------------------------------------- the allowlist --

    [Fact]
    public void Configuring_records_the_websites_that_were_asked_for()
    {
        WebChatEndpoint endpoint = NewEndpoint();

        endpoint.Configure(
            WebChatWidgetSettings.Default,
            [Origin("https://a.example.com"), Origin("https://b.example.com")],
            Noon);

        Assert.Equal(
            ["https://a.example.com", "https://b.example.com"],
            endpoint.AllowedOrigins.Select(allowed => allowed.Origin.Value));
    }

    [Fact]
    public void One_website_written_twice_is_stored_once()
    {
        // Two spellings of one website are one website. Storing both would make
        // removing it an act that has to be performed twice.
        WebChatEndpoint endpoint = NewEndpoint();

        endpoint.Configure(
            WebChatWidgetSettings.Default,
            [Origin("https://www.example.com"), Origin("HTTPS://WWW.example.com:443/")],
            Noon);

        Assert.Single(endpoint.AllowedOrigins);
    }

    [Fact]
    public void A_website_that_survives_a_change_keeps_the_day_it_was_approved()
    {
        WebChatEndpoint endpoint = NewEndpoint();

        endpoint.Configure(WebChatWidgetSettings.Default, [Origin("https://a.example.com")], Noon);

        DateTimeOffset approved = endpoint.AllowedOrigins[0].AllowedAt;

        endpoint.Configure(
            WebChatWidgetSettings.Create(
                WebChatGreeting.CreateOrNone("Hi"),
                WebChatAccentColor.Create("#123456"),
                WebChatLauncherPosition.Left,
                WebChatTheme.Dark),
            [Origin("https://a.example.com"), Origin("https://b.example.com")],
            Noon.AddDays(1));

        Assert.Equal(approved, endpoint.AllowedOrigins.Single(
            allowed => allowed.Origin.Value == "https://a.example.com").AllowedAt);
    }

    [Fact]
    public void Configuring_replaces_the_whole_list_rather_than_adding_to_it()
    {
        WebChatEndpoint endpoint = NewEndpoint();

        endpoint.Configure(WebChatWidgetSettings.Default, [Origin("https://old.example.com")], Noon);
        endpoint.Configure(
            WebChatWidgetSettings.Default,
            [Origin("https://new.example.com")],
            Noon.AddMinutes(1));

        Assert.Equal(
            ["https://new.example.com"],
            endpoint.AllowedOrigins.Select(allowed => allowed.Origin.Value));

        Assert.False(endpoint.Allows(Origin("https://old.example.com")));
    }

    [Fact]
    public void More_websites_than_the_bound_allows_are_refused_outright()
    {
        WebChatEndpoint endpoint = NewEndpoint();

        List<WebChatOrigin> tooMany = Enumerable
            .Range(0, WebChatEndpoint.MaxAllowedOrigins + 1)
            .Select(index => Origin($"https://site{index}.example.com"))
            .ToList();

        Assert.Throws<DomainValidationException>(
            () => endpoint.Configure(WebChatWidgetSettings.Default, tooMany, Noon));

        // Nothing was half-applied.
        Assert.Empty(endpoint.AllowedOrigins);
    }

    [Fact]
    public void Exactly_the_bound_is_allowed()
    {
        WebChatEndpoint endpoint = NewEndpoint();

        List<WebChatOrigin> full = Enumerable
            .Range(0, WebChatEndpoint.MaxAllowedOrigins)
            .Select(index => Origin($"https://site{index}.example.com"))
            .ToList();

        endpoint.Configure(WebChatWidgetSettings.Default, full, Noon);

        Assert.Equal(WebChatEndpoint.MaxAllowedOrigins, endpoint.AllowedOrigins.Count);
    }

    [Fact]
    public void Allows_is_exact_equality_and_nothing_cleverer()
    {
        WebChatEndpoint endpoint = NewEndpoint();

        endpoint.Configure(WebChatWidgetSettings.Default, [Origin("https://www.example.com")], Noon);

        Assert.True(endpoint.Allows(Origin("https://www.example.com")));
        Assert.True(endpoint.Allows(Origin("HTTPS://WWW.Example.com/")));

        // None of these is the approved website, and no prefix, suffix or
        // subdomain rule may make any of them pass.
        Assert.False(endpoint.Allows(Origin("https://evil.www.example.com")));
        Assert.False(endpoint.Allows(Origin("https://www.example.com.evil.test")));
        Assert.False(endpoint.Allows(Origin("https://www.example.com:8443")));
        Assert.False(endpoint.Allows(Origin("https://example.com")));
        Assert.False(endpoint.Allows(null));
    }

    [Fact]
    public void A_widget_with_no_approved_website_allows_nothing()
    {
        Assert.False(NewEndpoint().Allows(Origin("https://www.example.com")));
    }

    [Fact]
    public void Configuring_moves_the_version_the_next_conditional_write_must_present()
    {
        WebChatEndpoint endpoint = NewEndpoint();

        endpoint.Configure(WebChatWidgetSettings.Default, [], Noon.AddHours(1));

        Assert.Equal(Noon.AddHours(1), endpoint.UpdatedAt);
    }
}
