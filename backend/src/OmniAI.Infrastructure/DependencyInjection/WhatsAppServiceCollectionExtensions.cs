using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OmniAI.Application.WhatsApp;
using OmniAI.Application.WhatsApp.Persistence;
using OmniAI.Infrastructure.Persistence.Repositories;
using OmniAI.Infrastructure.WhatsApp;

namespace OmniAI.Infrastructure.DependencyInjection;

/// <summary>
/// Registers the WhatsApp channel: storage, transport, webhook security, workers.
/// </summary>
/// <remarks>
/// Its own file rather than more lines in the persistence registration, because
/// this is one feature made of five different kinds of thing, and reading it in
/// one place is how somebody discovers that the workers exist at all.
/// </remarks>
public static class WhatsAppServiceCollectionExtensions
{
    /// <summary>
    /// Environment variable holding the Meta app secret.
    /// </summary>
    /// <remarks>
    /// <strong>Platform configuration, not workspace data.</strong> It identifies
    /// the Meta app this deployment is registered as, is the same for every
    /// workspace, and is never stored in the database, never editable from the
    /// admin and never returned by any API. It is read from the environment and
    /// handed straight to the authenticator, exactly as the OpenAI key is: binding
    /// it into an options object would put it into a type that is easy to log and
    /// easy to serialise into diagnostics.
    /// </remarks>
    public const string AppSecretVariable = "OMNIAI_WHATSAPP_APP_SECRET";

    /// <summary>Environment variable holding the webhook verify token.</summary>
    /// <remarks>
    /// A value this deployment chooses and then types into Meta's dashboard. It
    /// proves nothing about a payload; it only stops an unrelated party pointing
    /// their subscription at this deployment's webhook.
    /// </remarks>
    public const string VerifyTokenVariable = "OMNIAI_WHATSAPP_VERIFY_TOKEN";

    /// <summary>
    /// Adds the WhatsApp channel.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">
    /// Where the non-secret settings and the two secret variables are read from.
    /// </param>
    /// <param name="requireLiveProviderOptIn">
    /// <para>
    /// When <see langword="true"/>, a configuration pointing at a real provider
    /// host does not on its own compose the Meta transport:
    /// <see cref="WhatsAppTransportOptions.LiveProviderOptInVariable"/> must also
    /// be exactly <see cref="WhatsAppTransportOptions.LiveProviderOptInValue"/>.
    /// Without it, every send is refused in-process and no request is made.
    /// </para>
    /// <para>
    /// <strong>The composition root passes Development here and nothing
    /// else.</strong> A production deployment that has deliberately configured
    /// the channel must not go silent because somebody forgot a second flag —
    /// this is a guard for developer machines, not a security control for
    /// deployments. Its security counterpart, the webhook authenticator, is
    /// required everywhere and is not affected by this at all.
    /// </para>
    /// </param>
    public static IServiceCollection AddOmniAiWhatsApp(
        this IServiceCollection services,
        IConfiguration configuration,
        bool requireLiveProviderOptIn = false)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddScoped<IWhatsAppConnectionRepository, WhatsAppConnectionRepository>();
        services.AddScoped<IWhatsAppInbox, WhatsAppInbox>();
        services.AddScoped<IWhatsAppOutbox, WhatsAppOutbox>();
        services.AddScoped<IWhatsAppContactStore, WhatsAppContactStore>();

        // Meta's rule, expressed once. A singleton because it holds nothing but
        // the clock: the window is computed from the transcript on every call.
        services.AddSingleton<CustomerServiceWindow>();

        // The operator preflight. Registered rather than newed up at a call site
        // so that it is composed from the same graph the running application has
        // -- a readiness answer assembled from hand-built parts would be a
        // readiness answer about something other than this deployment. It has no
        // HTTP surface: see the type for why.
        services.AddScoped<WhatsAppRealValidationPreflight>();

        AddTransport(services, configuration, requireLiveProviderOptIn);
        AddWebhookSecurity(services, configuration);
        AddWorkers(services, configuration);

        return services;
    }

    /// <summary>The HTTP adapter, and the client it sends on.</summary>
    private static void AddTransport(
        IServiceCollection services,
        IConfiguration configuration,
        bool requireLiveProviderOptIn)
    {
        WhatsAppTransportOptions options = new();

        configuration.GetSection(WhatsAppTransportOptions.SectionName).Bind(options);

        // Validated at startup. A deployment with a malformed Graph version
        // should refuse to start rather than discover it the first time a
        // customer is waiting for a reply.
        options.Validate();

        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(options));

        services
            .AddHttpClient(
                WhatsAppTransportOptions.HttpClientName,
                client =>
                {
                    client.BaseAddress = Normalise(options.GraphBaseAddress);

                    // A finite wait, for the same reason the OpenAI client has
                    // one: an unbounded one is how a slow dependency becomes an
                    // outage. Exceeding it is reported as ambiguous, never as
                    // failed -- the request may well have arrived.
                    client.Timeout = options.Timeout;
                })
            .ConfigureHttpClient(static client =>
            {
                // No default Authorization header. The credential belongs to a
                // workspace and this client is shared between all of them, so it
                // is set per request and nowhere else.
                client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            });

        // <strong>Nothing adds a resilience handler here, and that is
        // deliberate.</strong> A transparent retry inside the transport would
        // turn one ambiguous send into several, and the customer would read the
        // same reply twice. Whether to try again is a decision that needs the
        // durable delivery row, and it is made by the caller.
        //
        // The client above is registered either way. Building it costs nothing at
        // rest, and leaving it out of the guarded branch would mean the guarded
        // and unguarded deployments differ in more than one place -- which is how
        // a configuration error hides until the flag is finally set.
        bool withheld = IsLiveSendingWithheld(configuration, options, requireLiveProviderOptIn);

        if (withheld)
        {
            // Pointed at a real provider, in Development, with nobody having said
            // so. Every send is refused in-process: no request, no credential on
            // a wire, nothing arriving in a stranger's WhatsApp.
            services.AddScoped<IWhatsAppMessageSender, LiveMetaDisabledMessageSender>();
        }
        else
        {
            services.AddScoped<IWhatsAppMessageSender, MetaWhatsAppMessageSender>();
        }

        // The preflight asks this rather than recomputing the rule, so that
        // "sending is permitted" cannot drift away from what was actually
        // composed a few lines above.
        services.AddSingleton<IWhatsAppProviderReadiness>(
            new MetaTransportReadiness(options, liveSendingAllowed: !withheld));

        // Reading the provider's envelope belongs on the same side of the
        // boundary as writing it. A singleton because it holds nothing: it is a
        // pure function from bytes to the handful of facts the platform acts on.
        services.AddSingleton<IWhatsAppWebhookParser, MetaWebhookParser>();
    }

    /// <summary>The two platform secrets the webhook route is defended with.</summary>
    private static void AddWebhookSecurity(
        IServiceCollection services,
        IConfiguration configuration)
    {
        string? appSecret = configuration[AppSecretVariable];
        string? verifyToken = configuration[VerifyTokenVariable];

        if (string.IsNullOrWhiteSpace(appSecret) || string.IsNullOrWhiteSpace(verifyToken))
        {
            // Refuses every request rather than accepting unverified ones. A
            // deployment that cannot tell a real webhook from an invented one
            // must accept neither: anybody who learned a phone number id could
            // otherwise put words into a workspace's inbox.
            services.TryAddSingleton<IWhatsAppWebhookAuthenticator>(
                new UnconfiguredWebhookAuthenticator(
                    $"Set {AppSecretVariable} and {VerifyTokenVariable} to accept WhatsApp "
                    + "webhooks. Until then every webhook request is refused."));

            return;
        }

        services.TryAddSingleton<IWhatsAppWebhookAuthenticator>(
            new MetaWebhookAuthenticator(appSecret, verifyToken));
    }

    /// <summary>The two background loops that move messages.</summary>
    private static void AddWorkers(IServiceCollection services, IConfiguration configuration)
    {
        WhatsAppWorkerOptions options = new();

        configuration.GetSection(WhatsAppWorkerOptions.SectionName).Bind(options);

        options.Validate();

        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(options));

        services.AddHostedService<InboundWhatsAppWorker>();
        services.AddHostedService<WhatsAppDeliveryWorker>();
    }

    /// <summary>
    /// Whether this deployment may put a real request on the wire.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three facts, and all three must hold before sending is withheld: the host
    /// is guarded (Development), the address is not on this machine, and nobody
    /// opted in. A loopback stub is unaffected, so the whole of local
    /// verification -- signed webhooks, the inbox, handoff, delivery receipts --
    /// keeps working without anybody setting anything.
    /// </para>
    /// <para>
    /// The opt-in is read through <see cref="IConfiguration"/> rather than
    /// <c>Environment.GetEnvironmentVariable</c>, exactly as the OpenAI one is, so
    /// a test can prove every combination without mutating process-wide state
    /// shared with every other test running in parallel.
    /// </para>
    /// </remarks>
    private static bool IsLiveSendingWithheld(
        IConfiguration configuration,
        WhatsAppTransportOptions options,
        bool requireLiveProviderOptIn)
    {
        if (!requireLiveProviderOptIn || !options.TargetsLiveProvider)
        {
            return false;
        }

        return !string.Equals(
            configuration[WhatsAppTransportOptions.LiveProviderOptInVariable],
            WhatsAppTransportOptions.LiveProviderOptInValue,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A base address a relative path can be appended to.
    /// </summary>
    /// <remarks>
    /// <c>HttpClient</c> discards everything after the last slash when combining,
    /// so an address without a trailing one silently loses its last path segment.
    /// It is a small thing that produces a 404 nobody can explain.
    /// </remarks>
    private static Uri Normalise(Uri address) =>
        address.AbsoluteUri.EndsWith('/') ? address : new Uri(address.AbsoluteUri + "/");
}
