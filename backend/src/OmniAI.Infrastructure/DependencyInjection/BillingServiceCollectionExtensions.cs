using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OmniAI.Application.Billing;
using OmniAI.Application.Billing.Persistence;
using OmniAI.Infrastructure.Billing;
using OmniAI.Infrastructure.Billing.Stripe;

namespace OmniAI.Infrastructure.DependencyInjection;

/// <summary>
/// Registers subscriptions: the catalogue, the stores, the provider, the workers.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Provider secrets are platform configuration, not tenant data.</strong>
/// The secret key and the webhook secret are read from the environment and handed
/// straight to the adapter that needs them. They are never stored in a tenant
/// table, never editable from any screen, never returned by any API and never
/// bound into a type that diagnostics would serialise — exactly as the OpenAI key
/// and the Meta app secret are handled.
/// </para>
/// <para>
/// <strong>A deployment with no keys still starts, and still works.</strong>
/// Everything except the provider adapter is registered unconditionally, so the
/// billing screen renders, reports that nothing is for sale and refuses checkout.
/// Failing at startup instead would take down a running platform over a setting
/// that only purchasing needs.
/// </para>
/// </remarks>
public static class BillingServiceCollectionExtensions
{
    public static IServiceCollection AddOmniAiBilling(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        BillingOptions options = new();

        configuration.GetSection(BillingOptions.Section).Bind(options);

        services.AddSingleton(options);

        services.AddScoped<IBillingCatalog, BillingCatalog>();
        services.AddScoped<IWorkspaceSubscriptionStore, WorkspaceSubscriptionStore>();
        services.AddScoped<IWorkspaceBillingCustomerStore, WorkspaceBillingCustomerStore>();
        services.AddScoped<IBillingCheckoutIntentStore, BillingCheckoutIntentStore>();
        services.AddScoped<IBillingEventInbox, BillingEventInbox>();

        // Holds a transaction across the subscription write and the plan
        // assignment, so it shares the caller's scoped DbContext deliberately --
        // that is what makes the two one atomic step.
        services.AddScoped<IBillingSubscriptionService, BillingSubscriptionService>();
        services.AddScoped<FallbackPlanResolver>();

        AddProvider(services, configuration);
        AddWorkers(services, configuration);

        return services;
    }

    /// <summary>
    /// The payment provider, and the thing that checks its signatures.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The verifier is registered whether or not a secret key exists: a
    /// deployment that can receive webhooks but cannot call the API is a real
    /// configuration, and the verifier refuses everything when it has no webhook
    /// secret rather than waving it through.
    /// </para>
    /// <para>
    /// <strong><see cref="StripeOptions.ApiBase"/> exists so that verification
    /// never needs a Stripe account.</strong> A local run points it at a
    /// deterministic stub; a real deployment leaves it empty, which means Stripe's
    /// own address.
    /// </para>
    /// </remarks>
    private static void AddProvider(IServiceCollection services, IConfiguration configuration)
    {
        StripeOptions stripe = new()
        {
            SecretKey = configuration[StripeOptions.SecretKeyVariable] ?? string.Empty,
            WebhookSecret = configuration[StripeOptions.WebhookSecretVariable] ?? string.Empty,
            ApiBase = configuration[StripeOptions.ApiBaseVariable] ?? string.Empty,
        };

        services.AddSingleton(stripe);
        services.AddSingleton<IBillingWebhookVerifier, StripeWebhookVerifier>();

        // Singleton because it holds one HTTP client and no request state. A
        // deployment with no key gets one that refuses every call, rather than
        // nothing at all -- an unregistered provider would stop the application
        // starting, and a platform whose conversations and agents depend on
        // having a Stripe key would be a worse thing than one that cannot sell.
        services.AddSingleton<IBillingProvider>(stripe.IsConfigured
            ? new StripeBillingProvider(stripe)
            : new UnconfiguredBillingProvider());
    }

    private static void AddWorkers(IServiceCollection services, IConfiguration configuration)
    {
        BillingWorkerOptions workers = new();

        configuration.GetSection(BillingWorkerOptions.SectionName).Bind(workers);

        workers.Validate();

        services.Configure<BillingWorkerOptions>(
            configuration.GetSection(BillingWorkerOptions.SectionName));

        services.AddHostedService<BillingEventWorker>();
        services.AddHostedService<PastDueGraceWorker>();
    }
}
