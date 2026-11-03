namespace OmniAI.Api.Security;

/// <summary>
/// Marks an endpoint that only asks whether a session is good, and must not change
/// it (Phase 37.5b, row 29: <c>GET /api/auth/session</c>).
/// </summary>
/// <remarks>
/// <para>
/// <strong>A question must not write a cookie.</strong> Two things would otherwise
/// send <c>Set-Cookie</c> on such a read: the sliding expiration, which reissues a
/// cookie past half its life, and the per-request stamp check, which signs a
/// revoked session out. On a marked endpoint the cookie is neither renewed nor
/// deleted: a revoked or expired session is simply not authenticated for that
/// request, and the next ordinary request still signs it out as before.
/// </para>
/// <para>
/// A side effect worth having: a page that polls the check cannot keep a session
/// alive that its person stopped using.
/// </para>
/// <para>
/// Read during authentication, which runs after routing, so the endpoint is known.
/// </para>
/// </remarks>
internal sealed class SessionProbe
{
    /// <summary>The one instance endpoints carry as metadata.</summary>
    public static readonly SessionProbe Marker = new();

    private SessionProbe()
    {
    }

    /// <summary>Whether the request is for a marked endpoint.</summary>
    public static bool Applies(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);

        return http.GetEndpoint()?.Metadata.GetMetadata<SessionProbe>() is not null;
    }
}
