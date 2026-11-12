using System.Net;
using System.Net.Http.Json;

namespace OmniAI.Api.IntegrationTests.WebChat;

/// <summary>
/// Configuring the embeddable widget over HTTP.
/// </summary>
/// <remarks>
/// <strong>Every write is conditional, and that matters more here than
/// elsewhere.</strong> Removing a website is a security action. Applying one to a
/// version somebody else has already replaced would silently reinstate a website
/// the other person had just removed, or remove one they had just approved.
/// </remarks>
public sealed class WebChatWidgetApiTests(WebChatApiFixture fixture)
    : IClassFixture<WebChatApiFixture>
{
    private static Uri Widget(Guid tenantId, Guid endpointId) =>
        WebChatApiFixture.Relative(
            $"/api/tenants/{tenantId}/website-chat/{endpointId}/widget");

    private sealed record WidgetPayload(
        string? Greeting,
        string AccentColor,
        string LauncherPosition,
        string Theme);

    private sealed record ConfigurationPayload(
        Guid Id,
        string PublicKey,
        string Status,
        WidgetPayload Widget,
        IReadOnlyList<string> AllowedOrigins,
        int MaxAllowedOrigins,
        string InstallSnippet,
        string LoaderUrl);

    /// <summary>Reads the configuration and the version a write must present back.</summary>
    private static async Task<(ConfigurationPayload Configuration, string Tag)> ReadAsync(
        HttpClient client,
        Guid tenantId,
        Guid endpointId)
    {
        HttpResponseMessage response = await client.GetAsync(Widget(tenantId, endpointId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        ConfigurationPayload? payload =
            await response.Content.ReadFromJsonAsync<ConfigurationPayload>();

        Assert.NotNull(payload);

        string tag = response.Headers.ETag?.ToString() ?? throw new InvalidOperationException(
            "The widget read carried no entity tag.");

        return (payload, tag);
    }

    private static async Task<HttpResponseMessage> WriteAsync(
        HttpClient client,
        Guid tenantId,
        Guid endpointId,
        object body,
        string? tag)
    {
        HttpRequestMessage request = new(HttpMethod.Put, Widget(tenantId, endpointId))
        {
            Content = JsonContent.Create(body),
        };

        if (tag is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", tag);
        }

        return await client.SendAsync(request);
    }

    private static object Settings(params string[] origins) =>
        new
        {
            greeting = "Hi — ask us anything.",
            accentColor = "#123456",
            launcherPosition = "Left",
            theme = "Dark",
            allowedOrigins = origins,
        };

    // ---- Authorization ---------------------------------------------------

    [Fact]
    public async Task Reading_the_widget_needs_only_the_view_permission()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (_, Guid endpointId) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Widget r");

        HttpClient reader = await fixture.CreateRestrictedMemberAsync(tenantId, "website-chat.view");

        Assert.Equal(
            HttpStatusCode.OK,
            (await reader.GetAsync(Widget(tenantId, endpointId))).StatusCode);
    }

    [Fact]
    public async Task Reading_the_widget_does_not_imply_changing_it()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (_, Guid endpointId) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Widget w");

        HttpClient reader = await fixture.CreateRestrictedMemberAsync(tenantId, "website-chat.view");

        (_, string tag) = await ReadAsync(reader, tenantId, endpointId);

        HttpResponseMessage refused = await WriteAsync(
            reader,
            tenantId,
            endpointId,
            Settings("https://www.example.com"),
            tag);

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    [Fact]
    public async Task Somebody_elses_workspace_is_indistinguishable_from_no_workspace()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (_, Guid endpointId) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Widget o");

        (HttpClient outsider, _, _) = await fixture.CreateOwnerAsync("Rival Widgets");

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await outsider.GetAsync(Widget(tenantId, endpointId))).StatusCode);
    }

    // ---- The install snippet ---------------------------------------------

    [Fact]
    public async Task A_new_widget_publishes_its_snippet_and_the_platform_defaults()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (string publicKey, Guid endpointId) =
            await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Widget defaults");

        (ConfigurationPayload configuration, _) = await ReadAsync(owner, tenantId, endpointId);

        Assert.Null(configuration.Widget.Greeting);
        Assert.Equal("#2563eb", configuration.Widget.AccentColor);
        Assert.Equal("Right", configuration.Widget.LauncherPosition);
        Assert.Equal("System", configuration.Widget.Theme);
        Assert.Empty(configuration.AllowedOrigins);
        Assert.Equal(20, configuration.MaxAllowedOrigins);

        // The snippet is composed by the server from the address this deployment
        // is actually answering on, so it works on staging as well as in
        // production, and there is one definition of it.
        Assert.Contains("/widget/v1.js", configuration.LoaderUrl, StringComparison.Ordinal);
        Assert.Contains(configuration.LoaderUrl, configuration.InstallSnippet, StringComparison.Ordinal);
        Assert.Contains(
            $"data-nabra-key=\"{publicKey}\"",
            configuration.InstallSnippet,
            StringComparison.Ordinal);

        // Every business that installs the chat reads this text, so it carries
        // the public name only. The loader still accepts the older attribute for
        // snippets pasted before Phase 37.1.
        Assert.DoesNotContain("omniai", configuration.InstallSnippet, StringComparison.OrdinalIgnoreCase);

        // The snippet is public HTML. Nothing that opens anything belongs in it.
        Assert.DoesNotContain("credential", configuration.InstallSnippet, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(tenantId.ToString(), configuration.InstallSnippet, StringComparison.OrdinalIgnoreCase);
    }

    // ---- Conditional writes ----------------------------------------------

    [Fact]
    public async Task A_write_without_a_precondition_is_refused_as_required()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (_, Guid endpointId) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Widget 428");

        HttpResponseMessage refused = await WriteAsync(
            owner,
            tenantId,
            endpointId,
            Settings("https://www.example.com"),
            tag: null);

        Assert.Equal(HttpStatusCode.PreconditionRequired, refused.StatusCode);
    }

    [Fact]
    public async Task A_write_against_a_replaced_version_is_refused_and_changes_nothing()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (_, Guid endpointId) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Widget 412");

        (_, string first) = await ReadAsync(owner, tenantId, endpointId);

        // Somebody else saves first.
        Assert.Equal(
            HttpStatusCode.OK,
            (await WriteAsync(owner, tenantId, endpointId, Settings("https://theirs.example.com"), first))
                .StatusCode);

        HttpResponseMessage stale = await WriteAsync(
            owner,
            tenantId,
            endpointId,
            Settings("https://mine.example.com"),
            first);

        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);

        // The refused write reinstated nothing and removed nothing.
        (ConfigurationPayload after, _) = await ReadAsync(owner, tenantId, endpointId);

        Assert.Equal(["https://theirs.example.com"], after.AllowedOrigins);
    }

    [Fact]
    public async Task A_successful_write_returns_the_version_the_next_one_must_present()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (_, Guid endpointId) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Widget tag");

        (_, string first) = await ReadAsync(owner, tenantId, endpointId);

        HttpResponseMessage saved = await WriteAsync(
            owner,
            tenantId,
            endpointId,
            Settings("https://www.example.com"),
            first);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        string second = saved.Headers.ETag?.ToString() ?? throw new InvalidOperationException(
            "The widget write carried no entity tag.");

        Assert.NotEqual(first, second);

        Assert.Equal(
            HttpStatusCode.OK,
            (await WriteAsync(owner, tenantId, endpointId, Settings("https://www.example.com"), second))
                .StatusCode);
    }

    // ---- What may be saved -----------------------------------------------

    [Fact]
    public async Task The_saved_configuration_is_what_comes_back()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (_, Guid endpointId) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Widget save");

        (_, string tag) = await ReadAsync(owner, tenantId, endpointId);

        HttpResponseMessage saved = await WriteAsync(
            owner,
            tenantId,
            endpointId,
            Settings("HTTPS://WWW.Example.com:443/", "https://shop.example.com"),
            tag);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        (ConfigurationPayload configuration, _) = await ReadAsync(owner, tenantId, endpointId);

        Assert.Equal("Hi — ask us anything.", configuration.Widget.Greeting);
        Assert.Equal("#123456", configuration.Widget.AccentColor);
        Assert.Equal("Left", configuration.Widget.LauncherPosition);
        Assert.Equal("Dark", configuration.Widget.Theme);

        // Canonicalised on the way in, so the screen shows what is actually
        // stored and what the browser will be compared against.
        Assert.Equal(
            ["https://shop.example.com", "https://www.example.com"],
            configuration.AllowedOrigins.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_blank_greeting_is_stored_as_no_greeting()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (_, Guid endpointId) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Widget none");

        (_, string tag) = await ReadAsync(owner, tenantId, endpointId);

        Assert.Equal(
            HttpStatusCode.OK,
            (await WriteAsync(
                owner,
                tenantId,
                endpointId,
                new
                {
                    greeting = "   ",
                    accentColor = "#2563eb",
                    launcherPosition = "Right",
                    theme = "System",
                    allowedOrigins = Array.Empty<string>(),
                },
                tag)).StatusCode);

        (ConfigurationPayload configuration, _) = await ReadAsync(owner, tenantId, endpointId);

        Assert.Null(configuration.Widget.Greeting);
    }

    [Theory]
    [InlineData("https://*.example.com")]
    [InlineData("https://www.example.com/support")]
    [InlineData("http://www.example.com")]
    [InlineData("null")]
    [InlineData("example.com")]
    [InlineData("javascript:alert(1)")]
    public async Task A_website_that_is_not_an_origin_is_refused_with_the_reason(string candidate)
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (_, Guid endpointId) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Widget bad");

        (_, string tag) = await ReadAsync(owner, tenantId, endpointId);

        HttpResponseMessage refused = await WriteAsync(
            owner,
            tenantId,
            endpointId,
            Settings(candidate),
            tag);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        // The reader is told which line is wrong, not that the whole save failed.
        string problem = await refused.Content.ReadAsStringAsync();

        Assert.Contains(candidate, problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_appearance_value_outside_the_offered_set_is_refused()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (_, Guid endpointId) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Widget enum");

        (_, string tag) = await ReadAsync(owner, tenantId, endpointId);

        foreach (object body in new object[]
        {
            new { accentColor = "red", launcherPosition = "Right", theme = "System", allowedOrigins = Array.Empty<string>() },
            new { accentColor = "#2563eb", launcherPosition = "Middle", theme = "System", allowedOrigins = Array.Empty<string>() },
            new { accentColor = "#2563eb", launcherPosition = "Right", theme = "Neon", allowedOrigins = Array.Empty<string>() },
        })
        {
            Assert.Equal(
                HttpStatusCode.BadRequest,
                (await WriteAsync(owner, tenantId, endpointId, body, tag)).StatusCode);
        }
    }

    [Fact]
    public async Task More_websites_than_the_bound_allows_are_refused()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (_, Guid endpointId) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Widget many");

        (ConfigurationPayload configuration, string tag) =
            await ReadAsync(owner, tenantId, endpointId);

        string[] tooMany = [.. Enumerable
            .Range(0, configuration.MaxAllowedOrigins + 1)
            .Select(index => $"https://site{index}.example.com")];

        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await WriteAsync(owner, tenantId, endpointId, Settings(tooMany), tag)).StatusCode);
    }

    // ---- The loader asks for the host page's language (Phase 38.7a, BA-3) ----

    /// <summary>
    /// The frame address the served loader builds for a host page's
    /// <c>&lt;html lang&gt;</c>, with the loader's own rule applied.
    /// </summary>
    /// <remarks>
    /// The rule is read out of the script the API actually serves (its
    /// <c>FRAME_LANGUAGES</c> table, ECMAScript regular expressions) and applied
    /// with .NET's ECMAScript mode, and the address is checked to be built from it
    /// and nothing else. The solution has no JavaScript engine; the evidence of
    /// Phase 38.7a also runs the served file itself, in Node and in Chrome.
    /// </remarks>
    [Theory]
    [InlineData("ar", "ar")]
    [InlineData("ar-SA", "ar")]
    [InlineData("AR-eg", "ar")]
    [InlineData("en-GB", "en")]
    [InlineData("en", "en")]
    [InlineData("fr", null)]
    [InlineData("arabic", null)]
    [InlineData("", null)]
    public async Task The_loader_asks_the_frame_for_the_host_pages_language_and_nothing_else(
        string hostLang,
        string? expected)
    {
        HttpResponseMessage served = await fixture.CreateClient().GetAsync(
            WebChatApiFixture.Relative("/widget/v1.js"));

        Assert.Equal(HttpStatusCode.OK, served.StatusCode);

        string script = await served.Content.ReadAsStringAsync();

        string? language = null;

        foreach (System.Text.RegularExpressions.Match entry in System.Text.RegularExpressions.Regex.Matches(
            FrameLanguagesTable(script),
            @"\['(?<code>[a-z]{2})', /(?<pattern>[^/]+)/i\]"))
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(
                hostLang,
                entry.Groups["pattern"].Value,
                System.Text.RegularExpressions.RegexOptions.ECMAScript
                    | System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                language = entry.Groups["code"].Value;

                break;
            }
        }

        Assert.Equal(expected, language);

        // The address is the key plus that one word, and nothing else from the page.
        Assert.Contains(
            "var frameUrl = origin + '/embed/' + encodeURIComponent(publicKey) + (language ? '?lang=' + language : '');",
            script,
            StringComparison.Ordinal);
        Assert.Contains("var lang = document.documentElement.getAttribute('lang') || '';", script, StringComparison.Ordinal);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(script, "frame\\.src = "));

        // Old snippets still work: both attribute names are read, neither is new.
        Assert.Contains("script.getAttribute('data-nabra-key') || script.getAttribute('data-omniai-key')", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_loader_is_cached_for_an_hour_and_revalidated_by_its_tag()
    {
        // What a customer's site holds: a visitor keeps an older loader for at
        // most an hour, and then revalidates it for free.
        HttpClient client = fixture.CreateClient();
        HttpResponseMessage served = await client.GetAsync(WebChatApiFixture.Relative("/widget/v1.js"));

        Assert.Equal("public, max-age=3600", served.Headers.CacheControl?.ToString());
        Assert.NotNull(served.Headers.ETag);

        using HttpRequestMessage again = new(HttpMethod.Get, WebChatApiFixture.Relative("/widget/v1.js"));
        again.Headers.IfNoneMatch.Add(served.Headers.ETag!);

        Assert.Equal(HttpStatusCode.NotModified, (await client.SendAsync(again)).StatusCode);
    }

    private static string FrameLanguagesTable(string script)
    {
        System.Text.RegularExpressions.Match table = System.Text.RegularExpressions.Regex.Match(
            script,
            @"var FRAME_LANGUAGES = (?<table>\[.*\]);");

        Assert.True(table.Success, "The loader has no FRAME_LANGUAGES table.");

        return table.Groups["table"].Value;
    }
}
