using System.Globalization;

namespace OmniAI.Domain.WebChat;

/// <summary>
/// One web origin a widget may be embedded on, in canonical form.
/// </summary>
/// <remarks>
/// <para>
/// An origin is <c>scheme + host + optional non-default port</c> and nothing
/// else. Not a URL: no path, no query, no fragment, no credentials. That is what
/// the browser actually reports in <c>postMessage</c>'s <c>event.origin</c>, and
/// comparing against anything richer would compare against something the browser
/// never sends.
/// </para>
/// <para>
/// <strong>Exact matches only.</strong> No <c>*</c>, no <c>*.example.com</c>, no
/// regular expressions, no path prefixes. Wildcard subdomain matching sounds
/// convenient and is how these lists go wrong: a single forgotten subdomain
/// hosting user-generated content turns the whole estate into an allowed embedder.
/// A workspace that genuinely needs three subdomains lists three origins.
/// </para>
/// <para>
/// <strong>Canonicalisation is the security property, not a tidiness one.</strong>
/// <c>HTTPS://Example.COM:443</c> and <c>https://example.com</c> are the same
/// origin, and a list that stored both as distinct strings would let one be
/// revoked while the other kept working. Everything is folded to one form on the
/// way in: lowercase scheme and host, the default port dropped, IDN converted to
/// punycode, and IPv6 written the one way the URI parser writes it.
/// </para>
/// <para>
/// <strong>What this is, and what it is not.</strong> This is a browser embedding
/// policy. It decides which pages a browser will let initialise the widget, and it
/// is enforced because the browser — not the page — reports the origin. It is
/// <em>not</em> protection against a script that speaks HTTP directly: anything
/// can send any header it likes. Abuse and cost are bounded by the distributed
/// protection layer, which every embedded request passes through exactly as the
/// hosted one does.
/// </para>
/// </remarks>
public sealed record WebChatOrigin
{
    /// <summary>
    /// The longest canonical origin accepted.
    /// </summary>
    /// <remarks>
    /// A hostname is bounded at 253 characters and a scheme and port add a little
    /// more. 255 is comfortably above anything real and keeps the column bounded.
    /// </remarks>
    public const int MaxLength = 255;

    private const string Http = "http";
    private const string Https = "https";

    private WebChatOrigin(string value) => Value = value;

    /// <summary>The canonical form: <c>scheme://host[:port]</c>, lowercase.</summary>
    public string Value { get; }

    /// <summary>Whether this origin is a loopback address or localhost.</summary>
    /// <remarks>
    /// The only case where plain HTTP is accepted. A developer running a customer
    /// site on their own machine has no certificate and needs none; anything
    /// reachable by somebody else does.
    /// </remarks>
    public bool IsLoopback { get; private init; }

    /// <summary>Parses and canonicalises an origin, or throws.</summary>
    public static WebChatOrigin Create(string? value)
    {
        if (!TryCreate(value, out WebChatOrigin? origin, out string? problem))
        {
            throw new DomainValidationException(problem);
        }

        return origin;
    }

    /// <summary>
    /// Parses and canonicalises an origin without throwing.
    /// </summary>
    /// <remarks>
    /// The non-throwing form exists because the embedded runtime compares an
    /// origin a browser reported on every single request. An unparseable value
    /// there is an ordinary refusal, not an exceptional condition, and raising for
    /// it would turn a rejected embed into noise in the logs.
    /// </remarks>
    public static bool TryCreate(
        string? value,
        out WebChatOrigin origin,
        out string problem)
    {
        origin = null!;
        problem = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            problem = "A website origin is required.";

            return false;
        }

        string trimmed = value.Trim();

        if (trimmed.Length > MaxLength)
        {
            problem = string.Create(
                CultureInfo.InvariantCulture,
                $"A website origin must be at most {MaxLength} characters.");

            return false;
        }

        // "null" is what a browser reports for a sandboxed or data: document. It
        // is a real value that arrives in event.origin, it is shared by every such
        // document on the web, and allowing it would allow all of them at once.
        if (trimmed.Equals("null", StringComparison.OrdinalIgnoreCase))
        {
            problem =
                "The opaque origin 'null' cannot be allowed. It is what a browser reports for "
                + "sandboxed and data: documents, and every one of them would match it.";

            return false;
        }

        if (trimmed.Contains('*', StringComparison.Ordinal))
        {
            problem =
                "Wildcards are not supported. List each website address exactly, for example "
                + "https://example.com and https://shop.example.com on separate lines.";

            return false;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? parsed))
        {
            problem =
                "That is not a website address. Use the scheme and domain only, for example "
                + "https://example.com.";

            return false;
        }

        if (parsed.Scheme is not (Http or Https))
        {
            problem = "A website origin must start with https:// or http://.";

            return false;
        }

        // Uri normalises an empty path to "/", so that is the only path an origin
        // may carry. Anything else means somebody pasted a page address.
        if (parsed.AbsolutePath is not ("" or "/"))
        {
            problem =
                "Remove the path. An origin is the scheme and domain only, for example "
                + "https://example.com rather than https://example.com/contact.";

            return false;
        }

        if (parsed.Query.Length > 0)
        {
            problem = "Remove the query string. An origin is the scheme and domain only.";

            return false;
        }

        if (parsed.Fragment.Length > 0)
        {
            problem = "Remove the fragment. An origin is the scheme and domain only.";

            return false;
        }

        if (parsed.UserInfo.Length > 0)
        {
            problem = "Remove the username and password. An origin carries neither.";

            return false;
        }

        if (parsed.IdnHost.Length == 0)
        {
            problem = "That website address has no host.";

            return false;
        }

        bool loopback = IsLoopbackHost(parsed);

        // HTTP is a downgrade everywhere a third party can reach: the page, the
        // widget and everything the visitor types would travel in clear. The
        // exception is a developer's own machine, which no third party can reach.
        if (parsed.Scheme == Http && !loopback)
        {
            problem =
                "Only https:// is accepted for a public website. Plain http:// is allowed only "
                + "for localhost during development.";

            return false;
        }

        origin = new WebChatOrigin(Canonicalise(parsed)) { IsLoopback = loopback };

        return true;
    }

    /// <summary>
    /// Builds the one written form of a parsed origin.
    /// </summary>
    /// <remarks>
    /// <c>IdnHost</c> does the punycode conversion and the lowercasing, and it is
    /// also what writes an IPv6 literal back in its bracketed canonical form.
    /// <c>IsDefaultPort</c> is what drops <c>:443</c> from an https origin — the
    /// browser never sends it, so storing it would guarantee a mismatch.
    /// </remarks>
    private static string Canonicalise(Uri parsed)
    {
        string scheme = parsed.Scheme.ToLowerInvariant();
        string host = parsed.IdnHost.ToLowerInvariant();

        // An IPv6 literal has to keep its brackets, or the result is ambiguous
        // with a port separator.
        if (parsed.HostNameType == UriHostNameType.IPv6)
        {
            host = $"[{host}]";
        }

        return parsed.IsDefaultPort
            ? $"{scheme}://{host}"
            : string.Create(CultureInfo.InvariantCulture, $"{scheme}://{host}:{parsed.Port}");
    }

    private static bool IsLoopbackHost(Uri parsed)
    {
        if (parsed.IsLoopback)
        {
            return true;
        }

        // Uri.IsLoopback covers 127.0.0.1, [::1] and the literal "localhost", but
        // not a subdomain of localhost, which some development setups use.
        return parsed.IdnHost.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);
    }

    public override string ToString() => Value;
}
