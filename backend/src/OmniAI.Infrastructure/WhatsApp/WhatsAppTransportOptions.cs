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
    /// The Development-only opt-in that permits real Meta traffic.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>The same failure the OpenAI guard exists for, on a channel where
    /// it is worse.</strong> A developer machine that has been pointed at Graph
    /// once keeps that setting in a file or a shell, and anything started from
    /// there inherits it. With OpenAI the accident costs money; here it puts a
    /// message into a real person's WhatsApp, which cannot be unsent.
    /// </para>
    /// <para>
    /// So in Development a real Graph host is necessary but not sufficient: this
    /// variable must also be exactly <see cref="LiveProviderOptInValue"/>, which
    /// is a thing somebody can only do on purpose. Outside Development it is not
    /// consulted at all — a deployment that has deliberately configured the
    /// channel must not be taken off the air by somebody forgetting a flag.
    /// </para>
    /// <para>
    /// Deliberately its own variable rather than a shared one. The two providers
    /// are opted into for different reasons, at different times, by people
    /// weighing different risks, and one flag that turns on both is a flag that
    /// gets set for the cheaper reason.
    /// </para>
    /// </remarks>
    public const string LiveProviderOptInVariable = "OMNIAI_LIVE_META";

    /// <summary>The only value of that variable which permits live traffic.</summary>
    /// <remarks>
    /// An exact match. Not "true", not "yes", not "any non-empty value": a guard
    /// whose vocabulary is generous is a guard somebody defeats by accident,
    /// which is the failure it exists to prevent.
    /// </remarks>
    public const string LiveProviderOptInValue = "1";

    /// <summary>Where Meta's Graph API actually lives.</summary>
    /// <remarks>
    /// Recorded so the expected host is stated somewhere rather than implied by a
    /// default, and so documentation and tests can name it without repeating a
    /// literal. It is not used as an allow-list: see
    /// <see cref="TargetsLiveProvider"/>, which treats <em>every</em> non-loopback
    /// host as live, because a guard that only recognises one hostname is a guard
    /// a regional alias walks straight past.
    /// </remarks>
    public const string MetaGraphHost = "graph.facebook.com";

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

    /// <summary>
    /// Whether this configuration points at a real provider rather than at
    /// something on this machine.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Loopback is the whole test, and it is deliberately the safe way
    /// round.</strong> A stub on <c>127.0.0.1</c> is, by construction, something
    /// running on this machine that somebody started; anything else is a host on
    /// the internet that this deployment would be handing a workspace's access
    /// token to. Asking "is it Meta?" instead would leave every typo, every
    /// regional alias and every proxy on the permitted side of the guard.
    /// </para>
    /// <para>
    /// The consequence is that a non-loopback stub counts as live and needs the
    /// opt-in. That is the correct inconvenience: local verification runs on
    /// loopback, and anything else is a decision worth making on purpose.
    /// </para>
    /// </remarks>
    public bool TargetsLiveProvider => GraphBaseAddress is not null && !GraphBaseAddress.IsLoopback;

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
