using OmniAI.Api.WebChat;

namespace OmniAI.Api.Security;

/// <summary>
/// Baseline response headers, chosen per route.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What every response gets.</strong> <c>X-Content-Type-Options:
/// nosniff</c>, so a JSON body or the widget script is never reinterpreted as
/// something else; <c>Referrer-Policy: no-referrer</c>, because nothing this API
/// links to needs to know where the reader came from; and a
/// <c>Permissions-Policy</c> switching off powerful features no API response
/// needs.
/// </para>
/// <para>
/// <strong>What only the private API gets.</strong> Admin API responses, hubs and
/// probes are never meant to be rendered, let alone framed, so they also carry
/// <c>Content-Security-Policy: default-src 'none'; frame-ancestors 'none'</c> and
/// <c>X-Frame-Options: DENY</c>. A JSON response that a browser was somehow
/// tricked into rendering then runs nothing and cannot be framed for clickjacking.
/// </para>
/// <para>
/// <strong>What the public surface is spared.</strong> The widget loader
/// (<c>/widget/v1.js</c>) is a script included by customer websites, and the
/// public chat API answers the platform's own embed and hosted pages. Neither is
/// a document, so a framing policy would protect nothing — but a blanket policy
/// written later without this distinction could break the Phase 18 embed, so the
/// distinction is explicit here. The pages that are framed are served by the
/// frontend host, which owns their own CSP.
/// </para>
/// <para>
/// <strong>Never overrides.</strong> A header an endpoint set itself wins; this
/// only fills in what is missing, when the response starts.
/// </para>
/// </remarks>
internal sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    private const string PermissionsPolicy =
        "camera=(), microphone=(), geolocation=(), payment=(), usb=()";

    private const string PrivateContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";

    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        bool isPublic = IsPublicSurface(context.Request.Path);

        context.Response.OnStarting(() =>
        {
            IHeaderDictionary headers = context.Response.Headers;

            headers.TryAdd("X-Content-Type-Options", "nosniff");
            headers.TryAdd("Referrer-Policy", "no-referrer");
            headers.TryAdd("Permissions-Policy", PermissionsPolicy);

            if (!isPublic)
            {
                headers.TryAdd("Content-Security-Policy", PrivateContentSecurityPolicy);
                headers.TryAdd("X-Frame-Options", "DENY");
            }

            return Task.CompletedTask;
        });

        return next(context);
    }

    /// <summary>Routes a third-party page or the platform's own public pages consume.</summary>
    internal static bool IsPublicSurface(PathString path) =>
        path.StartsWithSegments(WidgetLoaderEndpoints.RoutePath, StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/api/public", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/hubs/public-chat", StringComparison.OrdinalIgnoreCase);
}

internal static class SecurityHeadersSetup
{
    /// <summary>
    /// Adds the baseline headers, and HSTS outside Development.
    /// </summary>
    /// <remarks>
    /// HSTS is the built-in middleware: it emits the header only on requests that
    /// really are HTTPS (after the forwarded scheme is restored) and never for
    /// localhost, so a development machine is not pinned to HTTPS by accident.
    /// </remarks>
    public static WebApplication UseOmniAiSecurityHeaders(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
        }

        app.UseMiddleware<SecurityHeadersMiddleware>();

        return app;
    }
}
