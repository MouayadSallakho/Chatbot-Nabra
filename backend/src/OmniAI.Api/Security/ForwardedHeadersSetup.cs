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
/// Teaches this process the scheme the browser actually used.
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
    /// <strong>Only the scheme is taken.</strong> <c>X-Forwarded-For</c> and
    /// <c>X-Forwarded-Host</c> are deliberately not processed: nothing in this
    /// platform reads the client address or reconstructs absolute URLs, so
    /// accepting them would widen the trusted input surface for no benefit. The
    /// framework default for <c>ForwardedHeaders</c> is <c>None</c>, so this is
    /// an opt-in of exactly one header.
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

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedProto;

            if (proxies.Length == 0 && networks.Length == 0)
            {
                return;
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
        });

        return services;
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
