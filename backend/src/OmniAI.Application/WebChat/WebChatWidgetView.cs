using OmniAI.Domain.WebChat;

namespace OmniAI.Application.WebChat;

/// <summary>
/// A widget's appearance, as an administrator reads and writes it.
/// </summary>
/// <remarks>
/// Plain values rather than domain types, so the HTTP layer has nothing to
/// translate and the shape of the contract is visible at a glance. The enums
/// travel as the words they are spelled with, never as ordinals: a client that
/// switched on <c>2</c> would break silently the day somebody reordered them.
/// </remarks>
public sealed record WebChatWidgetView(
    string? Greeting,
    string AccentColor,
    string LauncherPosition,
    string Theme)
{
    public static WebChatWidgetView From(WebChatWidgetSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new WebChatWidgetView(
            settings.Greeting?.Value,
            settings.Accent.Value,
            settings.Position.ToString(),
            settings.Theme.ToString());
    }
}

/// <summary>
/// Everything the Website Chat configuration screen needs.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The public key is here and no secret is.</strong> The key identifies
/// which widget a page is asking for and is meant to be readable by anybody who
/// views the customer's HTML source; it authorises nothing on its own. What stops
/// a stranger using it is the allowlist and, beyond that, the distributed
/// protection every public request passes through.
/// </para>
/// <para>
/// <c>InstallOrigin</c> is where the loader is served from, derived from the
/// request this deployment is actually answering rather than hardcoded. A
/// deployment behind a different hostname produces a snippet that works there,
/// which a compiled-in production domain would not.
/// </para>
/// </remarks>
public sealed record WebChatWidgetConfigurationView(
    Guid Id,
    string PublicKey,
    string Status,
    WebChatWidgetView Widget,
    IReadOnlyList<string> AllowedOrigins,
    int MaxAllowedOrigins);
