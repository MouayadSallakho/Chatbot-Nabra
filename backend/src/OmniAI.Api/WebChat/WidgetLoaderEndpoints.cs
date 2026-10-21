using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace OmniAI.Api.WebChat;

/// <summary>
/// Serves the loader script a customer's website includes.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Versioned in the path, and that is a compatibility promise.</strong>
/// A customer pastes a snippet into their site once and may never touch it again.
/// <c>/widget/v1.js</c> must keep working for them forever; a change that would
/// break an existing installation becomes <c>/widget/v2.js</c> and leaves v1
/// alone.
/// </para>
/// <para>
/// <strong>An embedded resource rather than a file on disk.</strong> There is no
/// static-file middleware in this application and this does not introduce one:
/// serving a directory means serving whatever ends up in it, and one compiled-in
/// script has exactly one thing it can return.
/// </para>
/// <para>
/// <strong>No CORS is involved and none is needed.</strong> A
/// <c>&lt;script src&gt;</c> is not a cross-origin read — the browser executes it
/// without asking this API for permission. The API's own authority stays closed:
/// nothing here adds an <c>Access-Control-Allow-Origin</c> header, and the
/// customer's page never calls the conversation API at all. The iframe does, from
/// the OmniAI origin, where it is same-origin.
/// </para>
/// </remarks>
internal static class WidgetLoaderEndpoints
{
    /// <summary>The resource compiled into this assembly.</summary>
    private const string ResourceName = "OmniAI.Api.Widget.v1.js";

    /// <summary>The published address. Part of every installed snippet.</summary>
    internal const string RoutePath = "/widget/v1.js";

    /// <summary>
    /// How long a browser may keep the loader.
    /// </summary>
    /// <remarks>
    /// An hour. Long enough that a busy site is not re-fetching it constantly,
    /// short enough that a fix reaches every installation the same day without
    /// anybody having to change their snippet. The entity tag makes the
    /// revalidation itself free.
    /// </remarks>
    private const int MaxAgeSeconds = 3600;

    private static readonly Lazy<LoaderScript> Script = new(Load, isThreadSafe: true);

    public static IEndpointRouteBuilder MapWidgetLoaderEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(RoutePath, Serve)
            .WithTags("Embedded chat")
            .AllowAnonymous()
            .WithName("GetWebChatWidgetLoader");

        return endpoints;
    }

    private static IResult Serve(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);

        LoaderScript script = Script.Value;

        // A strong tag over the script's own bytes, so a revalidation costs a 304
        // rather than the file. It is content-derived, so two deployments of the
        // same build agree without any coordination.
        http.Response.Headers.ETag = script.ETag;
        http.Response.Headers.CacheControl = string.Create(
            CultureInfo.InvariantCulture,
            $"public, max-age={MaxAgeSeconds}");

        if (http.Request.Headers.IfNoneMatch.Count > 0
            && http.Request.Headers.IfNoneMatch.ToString().Contains(script.ETag, StringComparison.Ordinal))
        {
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }

        return Results.Bytes(script.Bytes, "application/javascript; charset=utf-8");
    }

    /// <summary>Reads the script once, at first request.</summary>
    private static LoaderScript Load()
    {
        using Stream? stream = typeof(WidgetLoaderEndpoints).Assembly
            .GetManifestResourceStream(ResourceName);

        if (stream is null)
        {
            throw new InvalidOperationException(
                $"The widget loader resource '{ResourceName}' is missing from this build. It is "
                + "compiled in as an embedded resource; check the Api project's EmbeddedResource "
                + "item.");
        }

        using MemoryStream buffer = new();

        stream.CopyTo(buffer);

        byte[] bytes = buffer.ToArray();

        return new LoaderScript(
            bytes,
            $"\"{Convert.ToHexStringLower(SHA256.HashData(bytes))[..32]}\"");
    }

    private sealed record LoaderScript(byte[] Bytes, string ETag);
}

/// <summary>
/// Where the loader tells a customer's page to fetch it from.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Derived from the request, never compiled in.</strong> A snippet is
/// pasted into somebody else's website and has to name an absolute address. That
/// address is whatever this deployment is actually answering on — a hardcoded
/// production domain would produce a snippet that silently does nothing on a
/// staging deployment, and a snippet nobody could test is a snippet nobody should
/// trust.
/// </para>
/// <para>
/// It reads the same forwarded scheme and host the rest of the pipeline already
/// established in Phase 12 and re-checked in Phase 16, so a deployment behind a
/// reverse proxy names the public address rather than its own container.
/// </para>
/// </remarks>
internal static class WidgetInstall
{
    /// <summary>The absolute address of the loader for this deployment.</summary>
    public static string LoaderUrl(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return $"{request.Scheme}://{request.Host.ToUriComponent()}{WidgetLoaderEndpoints.RoutePath}";
    }

    /// <summary>
    /// The snippet a customer pastes into their website.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Built server-side so there is exactly one definition of it, and so the
    /// admin screen displays the same text the documentation describes.
    /// </para>
    /// <para>
    /// <strong>The public key is the only thing in it, and it is public.</strong>
    /// It names which widget a page wants; it authorises nothing by itself. What
    /// decides whether the widget runs is the allowlist, checked against an origin
    /// the browser reports rather than one the page claims.
    /// </para>
    /// </remarks>
    public static string Snippet(HttpRequest request, string publicKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicKey);

        StringBuilder snippet = new();

        snippet.Append("<script async src=\"");
        snippet.Append(LoaderUrl(request));
        snippet.Append("\" data-nabra-key=\"");
        snippet.Append(publicKey);
        snippet.Append("\"></script>");

        return snippet.ToString();
    }
}
