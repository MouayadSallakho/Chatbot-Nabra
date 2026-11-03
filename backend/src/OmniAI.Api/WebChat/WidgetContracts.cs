using OmniAI.Application.WebChat;

namespace OmniAI.Api.WebChat;

/// <summary>
/// What an approved widget is told about itself.
/// </summary>
/// <remarks>
/// The agent's display name, whether it is currently serving, and the four
/// appearance settings. Deliberately no workspace id, no agent id, no endpoint id,
/// no knowledge and no credential: a launcher and a panel need none of them, and a
/// value that never reaches the browser is a value that cannot leak from it.
/// <para>
/// Phase 37.5a adds the workspace's display name (owner decision D-3), so a
/// visitor knows whose chat it is, and <c>launcherPosition</c> may also be
/// <c>Start</c> or <c>End</c>, which the loader resolves from the host page's
/// direction.
/// </para>
/// </remarks>
public sealed record EmbeddedChatConfigResponse(
    string AgentName,
    bool IsAvailable,
    string? Greeting,
    string AccentColor,
    string LauncherPosition,
    string Theme,
    string WorkspaceName);

/// <summary>
/// A started embedded conversation.
/// </summary>
/// <remarks>
/// <strong>The one and only time the raw credential crosses the wire.</strong> The
/// response carries <c>Cache-Control: no-store</c>, the value is never written to
/// a log, never placed in a URL, and never sent to the embedding page. The server
/// keeps a SHA-256 digest and nothing else, so this response is also the only
/// moment the value exists anywhere but in the iframe that asked for it.
/// </remarks>
public sealed record StartedEmbeddedConversationResponse(
    Guid ConversationId,
    string AgentName,
    string Credential);

/// <summary>The appearance a workspace configured.</summary>
public sealed record WebChatWidgetResponse(
    string? Greeting,
    string AccentColor,
    string LauncherPosition,
    string Theme)
{
    public static WebChatWidgetResponse From(WebChatWidgetView widget)
    {
        ArgumentNullException.ThrowIfNull(widget);

        return new WebChatWidgetResponse(
            widget.Greeting,
            widget.AccentColor,
            widget.LauncherPosition,
            widget.Theme);
    }
}

/// <summary>
/// Everything the Website Chat configuration screen reads.
/// </summary>
/// <remarks>
/// <c>PublicKey</c> is here because the install snippet needs it and it is public
/// by design — it appears in the customer's HTML source, and it authorises nothing
/// on its own. No secret is published here or anywhere near here.
/// </remarks>
/// <param name="InstallSnippet">
/// The exact text to paste into a customer's website, built from the address this
/// deployment is actually answering on. Composed server-side so there is one
/// definition of it and the screen cannot drift from the documentation.
/// </param>
/// <param name="LoaderUrl">
/// Where the loader is served from, published separately so a customer writing a
/// Content-Security-Policy has the value for <c>script-src</c> and
/// <c>frame-src</c> without having to parse it out of the snippet.
/// </param>
public sealed record WebChatWidgetConfigurationResponse(
    Guid Id,
    string PublicKey,
    string Status,
    WebChatWidgetResponse Widget,
    IReadOnlyList<string> AllowedOrigins,
    int MaxAllowedOrigins,
    string InstallSnippet,
    string LoaderUrl)
{
    public static WebChatWidgetConfigurationResponse From(
        WebChatWidgetConfigurationView view,
        HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(view);

        return new WebChatWidgetConfigurationResponse(
            view.Id,
            view.PublicKey,
            view.Status,
            WebChatWidgetResponse.From(view.Widget),
            view.AllowedOrigins,
            view.MaxAllowedOrigins,
            WidgetInstall.Snippet(request, view.PublicKey),
            WidgetInstall.LoaderUrl(request));
    }
}

/// <summary>What replacing a widget's configuration requires.</summary>
/// <remarks>
/// The whole intended list of websites comes back, not a difference. The screen
/// read the current list, somebody added and removed, and this is what they now
/// want — computing the change on the client would mean computing it from a view
/// that may already be out of date, and an origin somebody thought they had
/// removed still being allowed is exactly the failure this list exists to prevent.
/// </remarks>
public sealed record UpdateWebChatWidgetRequest(
    string? Greeting,
    string? AccentColor,
    string? LauncherPosition,
    string? Theme,
    IReadOnlyList<string>? AllowedOrigins);
