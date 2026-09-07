using System.Net;
using OmniAI.Application.WebChat.Protection;

namespace OmniAI.Api.Security;

/// <summary>
/// The bridge from a connection to a rate-limit identity.
/// </summary>
/// <remarks>
/// <strong>The whole point is where it reads from.</strong> It reads
/// <c>Connection.RemoteIpAddress</c> — a fact about the TCP connection — and
/// never a header. By the time this runs, <c>UseForwardedHeaders</c> has already
/// replaced that address with the one a <em>trusted</em> proxy reported, or left
/// it alone because the peer was not trusted. So an untrusted caller who sends
/// <c>X-Forwarded-For: 1.2.3.4</c> is identified as themselves, and there is no
/// code path here that could read the header directly and undo that.
/// <para>
/// <strong>It lives in the API because only the API has a connection.</strong>
/// The Application is handed a finished pseudonym, so no use case names an
/// address, an <see cref="HttpContext"/> or a header — which is also what keeps
/// the layer guards green.
/// </para>
/// <para>
/// <strong>Nothing is logged from here.</strong> The address is turned into a
/// pseudonym and dropped. There is deliberately no diagnostic in this file that
/// could put a visitor's address into a log line beside the key derived from it,
/// which would defeat the pseudonymisation for anybody reading that log.
/// </para>
/// </remarks>
internal static class ClientNetworkIdentity
{
    /// <summary>
    /// The pseudonymous network this request came from, if one can be established.
    /// </summary>
    /// <returns>
    /// The key, or <see langword="null"/> when the connection reports no remote
    /// address at all.
    /// </returns>
    /// <remarks>
    /// <strong>Null rather than a placeholder.</strong> An absent address happens
    /// in real hosts — an in-memory test server, a Unix socket, a connection that
    /// has already gone away — and the caller must decide what to do about it.
    /// Substituting a constant here would have created one shared bucket that
    /// every such request fell into, which is precisely the unmetered lane a
    /// per-network limit exists to prevent; the public write handlers therefore
    /// refuse instead.
    /// </remarks>
    public static ClientNetworkKey? Resolve(HttpContext http, IClientNetworkKeys keys)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(keys);

        IPAddress? address = http.Connection.RemoteIpAddress;

        return address is null ? null : keys.Derive(address);
    }
}
