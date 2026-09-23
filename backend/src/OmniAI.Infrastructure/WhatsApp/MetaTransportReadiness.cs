using System.Text.RegularExpressions;
using OmniAI.Application.WhatsApp;

namespace OmniAI.Infrastructure.WhatsApp;

/// <summary>
/// Describes this deployment's outbound WhatsApp configuration, without
/// disclosing it.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What leaves this type is four booleans and a version string.</strong>
/// Not the base address, which names the host somebody chose; not the app secret
/// or any credential, which are not here at all. A readiness answer that repeats
/// its inputs is a configuration dump, and configuration dumps are how a
/// diagnostic surface becomes a reconnaissance one.
/// </para>
/// <para>
/// Whether live sending is permitted is decided once, at registration, and passed
/// in. Recomputing it here would mean the guard's rule lived in two places, and
/// the first time they disagreed the preflight would cheerfully report that
/// sending works while every send was being refused.
/// </para>
/// </remarks>
internal sealed partial class MetaTransportReadiness(
    WhatsAppTransportOptions options,
    bool liveSendingAllowed)
    : IWhatsAppProviderReadiness
{
    public WhatsAppProviderReadiness Describe() =>
        new(
            AddressIsUsable: IsAddressUsable(options.GraphBaseAddress),
            ApiVersion: options.GraphApiVersion ?? string.Empty,
            VersionIsUsable: VersionPattern().IsMatch(options.GraphApiVersion ?? string.Empty),
            TargetsLiveProvider: options.TargetsLiveProvider,
            LiveSendingAllowed: liveSendingAllowed);

    /// <summary>
    /// Whether a credential may be sent to this address.
    /// </summary>
    /// <remarks>
    /// The same rule the options validate at startup, stated as a question rather
    /// than as a throw: HTTPS, or loopback, where there is no wire to read.
    /// Startup already refuses anything else, so a deployment that reaches this
    /// answering <see langword="false"/> is one whose options were built by hand.
    /// </remarks>
    private static bool IsAddressUsable(Uri? address) =>
        address is not null
        && address.IsAbsoluteUri
        && (address.Scheme == Uri.UriSchemeHttps || address.IsLoopback);

    [GeneratedRegex(@"^v\d+\.\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();
}
