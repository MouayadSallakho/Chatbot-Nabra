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
    public static IServiceCollection AddOmniAiWhatsApp(
        this IServiceCollection services,
        IConfiguration configuration)
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

        AddTransport(services, configuration);
        AddWebhookSecurity(services, configuration);
        AddWorkers(services, configuration);

        return services;
    }

    /// <summary>The HTTP adapter, and the client it sends on.</summary>
    private static void AddTransport(IServiceCollection services, IConfiguration configuration)
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
        services.AddScoped<IWhatsAppMessageSender, MetaWhatsAppMessageSender>();

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
