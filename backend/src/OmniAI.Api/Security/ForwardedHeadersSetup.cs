using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

// Both namespaces define IPNetwork. The alias picks the one that is not obsolete:
// Microsoft.AspNetCore.HttpOverrides.IPNetwork is deprecated as of .NET 10 in
// favour of System.Net.IPNetwork, and the analyzer refuses it outright. The
// options object exposes both -- the obsolete KnownNetworks and the current
// KnownIPNetworks -- and only the latter is used.
using IPNetwork = System.Net.IPNetwork;

namespace OmniAI.Api.Security;

/// <summary>
/// Teaches this process the scheme the browser actually used, and who it was
/// talking to.
/// </summary>
/// <remarks>
/// <strong>The problem this solves.</strong> TLS is terminated at a reverse
/// proxy, so the connection Kestrel accepts is plain HTTP even when the browser
/// spoke HTTPS. Left alone, <c>Request.IsHttps</c> is <see langword="false"/> in
/// production, and everything that depends on it is wrong: the antiforgery
/// service refuses to issue a token at all under a <c>Secure</c> cookie policy,
/// and any code that reasons about transport security reasons from a false
/// premise.
/// <para>
/// The wrong fix is to stop requiring <c>Secure</c> on the cookies. That treats
/// the symptom by removing the guarantee: the browser would then be permitted to
/// send those cookies over plain HTTP, which is exactly what the attribute
/// exists to prevent. The right fix is to make the external scheme survive the
/// proxy hop, which is what this does.
/// </para>
/// <para>
/// <strong>Forwarded headers are attacker-controlled input.</strong> Anything
/// that can open a TCP connection to this process can set
/// <c>X-Forwarded-Proto: https</c>. The only thing separating a genuine proxy
/// from a liar is <em>where the connection came from</em>, so the middleware
/// accepts the header only from an address on the trusted list and otherwise
/// ignores it entirely.
/// </para>
/// <para>
/// <strong>The client address is now needed as well, and that is a change worth
/// justifying.</strong> Earlier phases deliberately did not process
/// <c>X-Forwarded-For</c>: nothing read the client address, so accepting the
/// header would have widened the trusted-input surface for no benefit. Public
/// chat protection changed that — a per-network rate limit needs to know which
/// network, and behind a TLS-terminating proxy every request otherwise appears to
/// come from the proxy itself, which would put the entire internet into one
/// bucket. So exactly one more header is processed, under exactly the same trust
/// rule.
/// </para>
/// <para>
/// <strong>What that rule means for a liar.</strong> A caller who reaches this
/// process without passing through a trusted proxy can send
/// <c>X-Forwarded-For: 1.2.3.4</c> and it changes nothing:
/// <c>Connection.RemoteIpAddress</c> stays the address the connection genuinely
/// came from, so they are rate-limited as themselves. Without the trust check
/// this header would be a one-line bypass of every per-network limit in the
/// platform — set a different value each time and every bucket is fresh.
/// </para>
/// <para>
/// <strong><c>X-Forwarded-Host</c> is still not processed.</strong> Nothing
/// reconstructs absolute URLs, so it would be trusted input in exchange for
/// nothing. Two headers is the whole opt-in.
/// </para>
/// </remarks>
internal static class ForwardedHeadersSetup
{
    /// <summary>Configuration section holding the trusted proxy addresses.</summary>
    /// <remarks>
    /// Ordinary non-secret deployment configuration -- an environment variable, a
    /// settings file, an orchestrator's config map. No address is compiled in:
    /// hard-coding a production host into source would put a deployment fact in
    /// the repository and make every environment need its own build.
    /// </remarks>
    public const string SectionName = "ForwardedHeaders";

    /// <summary>Individually trusted proxy addresses.</summary>
    public const string KnownProxiesKey = "KnownProxies";

    /// <summary>Trusted proxy networks, in CIDR form.</summary>
    public const string KnownNetworksKey = "KnownNetworks";

    /// <summary>
    /// Registers the forwarded-header options.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>The scheme and the client address, and nothing else.</strong>
    /// <c>X-Forwarded-Host</c> remains unprocessed, because nothing in this
    /// platform reconstructs absolute URLs and accepting it would widen the
    /// trusted input surface for no benefit. The framework default for
    /// <c>ForwardedHeaders</c> is <c>None</c>, so this is an opt-in of exactly
    /// two headers.
    /// </para>
    /// <para>
    /// <strong>The two headers are processed by two separate passes, and that is
    /// the most important decision in this file.</strong> The obvious
    /// implementation — one pass opted in to both — was written first and a test
    /// immediately proved it unsafe: the middleware abandons <em>all</em> of its
    /// work at the first <c>X-Forwarded-For</c> entry it cannot parse, so a proxy
    /// forwarding one malformed address also lost the scheme, and every request to
    /// the deployment was then refused as insecure transport. A single
    /// misconfigured upstream would have taken the whole site down.
    /// <para>
    /// Two passes decouple them. The scheme is restored first and on its own, so
    /// nothing about an address can cost a request its transport-security
    /// decision; the client address is then resolved by a second pass whose worst
    /// case is that the address stays the proxy's. A security decision must not
    /// depend on the well-formedness of an unrelated header.
    /// </para>
    /// <para>
    /// <strong>The order is not interchangeable.</strong> Scheme first, address
    /// second. The address pass rewrites
    /// <c>Connection.RemoteIpAddress</c> to the client's, and the trust check both
    /// passes make is against that address — so running them the other way round
    /// would have the scheme pass comparing the <em>client's</em> address to the
    /// trusted proxy list, find it untrusted, and silently discard the scheme.
    /// </para>
    /// <para>
    /// <c>RequireHeaderSymmetry</c> is left at the framework default of
    /// <see langword="false"/> on both. With one header each there is nothing for
    /// it to compare, so it can no longer discard anything — which is a second
    /// reason the split is worth having.
    /// </para>
    /// </para>
    /// <para>
    /// <strong>The default trust boundary is loopback, and it comes from the
    /// framework.</strong> <c>KnownProxies</c> starts as <c>[::1]</c> and
    /// <c>KnownIPNetworks</c> as <c>[127.0.0.0/8]</c>, which is precisely the
    /// same-host reverse proxy this platform expects. Nothing off the machine is
    /// trusted unless somebody says so in configuration.
    /// </para>
    /// <para>
    /// <strong>Configured trust replaces the defaults rather than adding to
    /// them.</strong> An operator who names their proxies has made a deliberate
    /// statement about the boundary, and silently keeping loopback trusted
    /// alongside it would mean the configuration did not say what it appeared to
    /// say. A typo therefore narrows trust and the application fails safe (see
    /// <see cref="SecureTransportMiddleware"/>) rather than widening it.
    /// </para>
    /// <para>
    /// <c>ForwardLimit</c> is left at the framework default of 1: one proxy hop,
    /// which is the topology this platform has. A second proxy is a deployment
    /// change that must be made deliberately, not absorbed silently.
    /// </para>
    /// <para>
    /// <strong>One hop is also what makes the client address trustworthy.</strong>
    /// With a limit of one, the middleware takes the last entry of
    /// <c>X-Forwarded-For</c> — the address the trusted proxy itself observed —
    /// and ignores everything to its left, which is whatever the caller chose to
    /// prepend. Raising the limit would start believing entries written by
    /// somebody upstream of the proxy, which is the classic spoofable
    /// configuration.
    /// </para>
    /// <para>
    /// <strong>A malformed value fails safe rather than open.</strong> If the
    /// header cannot be parsed the address pass leaves the peer address alone, so
    /// the resolved client is the proxy — everybody behind it then shares one rate
    /// limit, which is more restrictive than intended rather than less. A
    /// degradation in fairness, never in protection, and thanks to the split it
    /// costs the request nothing else.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddOmniAiForwardedHeaders(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        IConfigurationSection section = configuration.GetSection(SectionName);

        IPAddress[] proxies = ReadProxies(section);
        IPNetwork[] networks = ReadNetworks(section);

        services.AddSingleton(new ForwardedHeaderPolicy(
            Options(ForwardedHeaders.XForwardedProto, proxies, networks),
            Options(ForwardedHeaders.XForwardedFor, proxies, networks)));

        return services;
    }

    /// <summary>
    /// One pass's options: one header, and the same trust boundary as the other.
    /// </summary>
    /// <remarks>
    /// The trust list is identical for both passes deliberately. They are two
    /// passes to keep one header's failure from affecting the other, not to give
    /// the two headers different levels of trust — an address believed from a
    /// peer whose scheme is not would be incoherent.
    /// </remarks>
    private static ForwardedHeadersOptions Options(
        ForwardedHeaders header,
        IPAddress[] proxies,
        IPNetwork[] networks)
    {
        ForwardedHeadersOptions options = new()
        {
            ForwardedHeaders = header,
        };

        if (proxies.Length == 0 && networks.Length == 0)
        {
            // The framework's own loopback defaults, which are precisely the
            // same-host reverse proxy this platform expects.
            return options;
        }

        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();

        foreach (IPAddress proxy in proxies)
        {
            options.KnownProxies.Add(proxy);
        }

        foreach (IPNetwork network in networks)
        {
            options.KnownIPNetworks.Add(network);
        }

        return options;
    }

    /// <summary>Reads the configured proxy addresses.</summary>
    /// <exception cref="InvalidOperationException">
    /// A value is not a valid IP address. Refused at startup rather than ignored:
    /// a mistyped proxy address that was quietly dropped would narrow the trust
    /// list without telling anybody, and the deployment would fail later with a
    /// symptom that looks nothing like a typo.
    /// </exception>
    private static IPAddress[] ReadProxies(IConfigurationSection section) =>
    [
        .. Values(section, KnownProxiesKey).Select(value =>
            IPAddress.TryParse(value, out IPAddress? address)
                ? address
                : throw new InvalidOperationException(
                    $"{SectionName}:{KnownProxiesKey} contains '{value}', which is not a valid IP address."))
    ];

    /// <summary>Reads the configured proxy networks, in CIDR form.</summary>
    /// <exception cref="InvalidOperationException">A value is not valid CIDR.</exception>
    private static IPNetwork[] ReadNetworks(IConfigurationSection section) =>
    [
        .. Values(section, KnownNetworksKey).Select(ParseNetwork)
    ];

    /// <summary>
    /// Parses a CIDR block such as <c>10.0.0.0/8</c>.
    /// </summary>
    /// <remarks>
    /// The <c>IPNetwork</c> here is <c>System.Net.IPNetwork</c>, aliased at the top
    /// of the file: the <c>Microsoft.AspNetCore.HttpOverrides</c> type of the same
    /// name is obsolete as of .NET 10 and the analyzer refuses it.
    /// </remarks>
    private static IPNetwork ParseNetwork(string value)
    {
        string[] parts = value.Split('/', StringSplitOptions.TrimEntries);

        if (parts.Length == 2
            && IPAddress.TryParse(parts[0], out IPAddress? address)
            && int.TryParse(parts[1], CultureInfo.InvariantCulture, out int prefix))
        {
            return new IPNetwork(address, prefix);
        }

        throw new InvalidOperationException(
            $"{SectionName}:{KnownNetworksKey} contains '{value}', which is not a valid CIDR network.");
    }

    /// <summary>The non-blank values configured under one key.</summary>
    private static IEnumerable<string> Values(IConfigurationSection section, string key) =>
        section.GetSection(key)
            .GetChildren()
            .Select(child => child.Value?.Trim() ?? string.Empty)
            .Where(value => value.Length > 0);
}

/// <summary>
/// The two forwarded-header passes this platform runs, in the order it runs them.
/// </summary>
/// <param name="Scheme">
/// Restores the scheme the browser used, and nothing else. Runs first, so no
/// address can cost a request its transport-security decision.
/// </param>
/// <param name="ClientAddress">
/// Resolves the client address, and nothing else. Runs second, because it rewrites
/// the peer address the trust check reads.
/// </param>
/// <remarks>
/// A type rather than two loose registrations, so the pair cannot be resolved
/// separately and applied in the wrong order — which is a silent, security-relevant
/// mistake rather than a visible one. See <see cref="ForwardedHeadersSetup"/> for
/// why there are two passes at all.
/// </remarks>
internal sealed record ForwardedHeaderPolicy(
    ForwardedHeadersOptions Scheme,
    ForwardedHeadersOptions ClientAddress);
