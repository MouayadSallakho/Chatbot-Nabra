using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace OmniAI.Api.IntegrationTests;

/// <summary>
/// Gives an in-memory request a connection to have come from.
/// </summary>
/// <remarks>
/// <strong>Why this is needed at all.</strong> <c>TestServer</c> synthesises
/// requests without a socket, so <c>Connection.RemoteIpAddress</c> is
/// <see langword="null"/>. From Phase 16 that is a meaningful state rather than an
/// inconvenience: a public write whose origin cannot be established is refused,
/// because an "unknown" rate-limit bucket would be one unmetered lane through
/// every per-network limit. A test host with no peer address therefore answers
/// 503 to every hosted-chat write — correctly, and uselessly for a suite that
/// wants to assert on anything else.
/// <para>
/// <strong>What it is not.</strong> It is not a bypass and cannot become one. It
/// lives in the test assembly, it sets a connection-level fact rather than
/// skipping a check, and the protection layer runs in full against whatever it
/// sets. The production code reads <c>Connection.RemoteIpAddress</c> and nothing
/// else; there is no configuration, environment check or header the application
/// itself consults to decide a client's identity.
/// </para>
/// <para>
/// <strong>Why a header may override it.</strong> Several properties can only be
/// stated as "two different networks": that limits partition by network, that one
/// visitor's flood does not refuse another's first message, that a forged
/// <c>X-Forwarded-For</c> does not create a fresh bucket. One TestServer serves
/// every client in a suite, so the only way to vary the connection is to let the
/// harness say what to pretend it was. <c>honourHeader</c> is off for the
/// production-proxy fixtures, where the whole point is that the peer address is
/// the fixture's and not the caller's.
/// </para>
/// </remarks>
internal sealed class SimulatedPeerStartupFilter(IPAddress fallback, bool honourHeader)
    : IStartupFilter
{
    /// <summary>
    /// Header a test may use to say which network a request came from.
    /// </summary>
    /// <remarks>
    /// Deliberately not <c>X-Forwarded-For</c>. That header is production input
    /// with a trust rule attached, and reusing it here would have made the
    /// spoofing tests meaningless — they need a real forged
    /// <c>X-Forwarded-For</c> arriving from a peer this middleware has already
    /// established.
    /// </remarks>
    public const string HeaderName = "X-Simulated-Peer";

    /// <summary>
    /// The address a request appears to come from when it says nothing.
    /// </summary>
    /// <remarks>
    /// <c>198.51.100.0/24</c> is TEST-NET-2 (RFC 5737), reserved for
    /// documentation and unroutable, so it can never be mistaken for somewhere
    /// real.
    /// </remarks>
    public static readonly IPAddress Default = IPAddress.Parse("198.51.100.10");

    /// <summary>Another unroutable address, for "a different network".</summary>
    public static readonly IPAddress Other = IPAddress.Parse("198.51.100.20");

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        ArgumentNullException.ThrowIfNull(next);

        return builder =>
        {
            builder.Use(async (context, proceed) =>
            {
                context.Connection.RemoteIpAddress = Peer(context);

                await proceed().ConfigureAwait(false);
            });

            next(builder);
        };
    }

    /// <summary>
    /// The address this request should appear to come from.
    /// </summary>
    /// <remarks>
    /// An unparseable value falls back rather than throwing: a typo in a test
    /// should fail that test's own assertion, not produce a 500 that looks like a
    /// product fault.
    /// </remarks>
    private IPAddress Peer(HttpContext context)
    {
        if (!honourHeader)
        {
            return fallback;
        }

        return context.Request.Headers.TryGetValue(HeaderName, out var claimed)
            && IPAddress.TryParse(claimed.ToString(), out IPAddress? parsed)
                ? parsed
                : fallback;
    }
}
