using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace OmniAI.Infrastructure.WhatsApp;

/// <summary>
/// Non-secret configuration for the Meta transport.
/// </summary>
/// <remarks>
/// <para>
/// Everything here is a deployment choice. None of it reaches the Domain, the
/// Application or the HTTP contract: the Application knows that a message goes to
/// a recipient and comes back accepted, refused or unknown, and knows nothing
/// about Graph, versions or bearer tokens.
/// </para>
/// <para>
/// <strong>No credential lives here.</strong> The access token belongs to a
/// workspace, is stored encrypted, and is passed into one method call. The app
/// secret and the webhook verify token are platform configuration read from the
/// environment at registration. Binding any of the three into a settings object
/// would put a secret into a type that is easy to log, easy to serialise into
/// diagnostics and easy to dump from a configuration endpoint.
/// </para>
/// </remarks>
internal sealed partial class WhatsAppTransportOptions
{
    /// <summary>Configuration section these values are bound from.</summary>
    public const string SectionName = "WhatsApp";

    /// <summary>The named <c>HttpClient</c> the sender resolves.</summary>
    public const string HttpClientName = "whatsapp-cloud-api";

    /// <summary>
    /// The Graph version every request is pinned to.
    /// </summary>
    /// <remarks>
    /// <strong>Pinned, never floating.</strong> Meta's unversioned paths follow
    /// whatever is current, so an unpinned deployment changes behaviour on a date
    /// chosen by somebody else. v26.0 is the version current when this was
    /// written (released 29 July 2026) and the one the request and response shapes
    /// here were read from; moving to a later one is a deliberate change with its
    /// own verification, which is exactly what a configuration value makes
    /// possible.
    /// </remarks>
    public const string DefaultGraphApiVersion = "v26.0";

    /// <summary>Where Graph lives.</summary>
    /// <remarks>
    /// Configurable so that local verification can point at a stub on loopback
    /// without a Meta account, and so that nothing in a test run can reach Meta by
    /// accident.
    /// </remarks>
    [Required]
    public Uri GraphBaseAddress { get; set; } = new("https://graph.facebook.com/");

    /// <summary>The pinned Graph version, as it appears in the path.</summary>
    [Required]
    public string GraphApiVersion { get; set; } = DefaultGraphApiVersion;

    /// <summary>
    /// How long one send may take.
    /// </summary>
    /// <remarks>
    /// Finite, because an unbounded wait is how a slow dependency becomes an
    /// outage: requests accumulate faster than they retire. A send that exceeds it
    /// is ambiguous rather than failed — the request may well have arrived — and
    /// is recorded as such.
    /// </remarks>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Whether these settings can be used.</summary>
    /// <remarks>
    /// Checked at startup rather than at the first send. A deployment configured
    /// with a malformed version should refuse to start, not discover it the first
    /// time a customer is waiting for a reply.
    /// </remarks>
    public void Validate()
    {
        if (GraphBaseAddress is null || !GraphBaseAddress.IsAbsoluteUri)
        {
            throw new InvalidOperationException(
                $"{SectionName}:GraphBaseAddress must be an absolute URL.");
        }

        if (GraphBaseAddress.Scheme != Uri.UriSchemeHttps && !GraphBaseAddress.IsLoopback)
        {
            // Plain HTTP would put a workspace's access token on the wire in
            // clear. Loopback is allowed because local verification runs against
            // a stub on this machine, where there is no wire.
            throw new InvalidOperationException(
                $"{SectionName}:GraphBaseAddress must use HTTPS unless it is on loopback.");
        }

        if (!VersionPattern().IsMatch(GraphApiVersion ?? string.Empty))
        {
            throw new InvalidOperationException(
                $"{SectionName}:GraphApiVersion must look like \"v26.0\".");
        }

        if (Timeout <= TimeSpan.Zero || Timeout > TimeSpan.FromMinutes(2))
        {
            throw new InvalidOperationException(
                $"{SectionName}:Timeout must be between zero and two minutes.");
        }
    }

    [GeneratedRegex(@"^v\d+\.\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();
}
