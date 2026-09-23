using System.Globalization;
using OmniAI.Application.Billing;
using OmniAI.Domain.Billing;
using Stripe;

namespace OmniAI.Infrastructure.Billing.Stripe;

/// <summary>
/// Stripe, behind the platform's own billing contract.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This file is the only place Stripe types exist.</strong> Everything
/// above it works with <see cref="ProviderSubscriptionSnapshot"/> and the rest of
/// the provider-independent shapes, so adding a second provider means writing
/// another adapter rather than touching the subscription domain, the handlers,
/// the entitlement layer or the UI.
/// </para>
/// <para>
/// <strong>The API version is pinned by the SDK, not left to drift.</strong>
/// Stripe.net 52.4.2 targets API version <c>2026-08-26.dahlia</c>. Depending on
/// "latest" would mean a field moving under a running deployment — which has
/// already happened once to this integration's shape: current period start and
/// end now live on each subscription <em>item</em> rather than on the
/// subscription, and this adapter reads them from where they actually are.
/// </para>
/// <para>
/// <strong>Mutating calls carry an idempotency key; reads do not need one.</strong>
/// Creating a customer or a checkout session twice would cost somebody money, so
/// each is sent with a server-generated key Stripe honours. Reading a
/// subscription is safe to repeat, which is why reconciliation is built on reads.
/// </para>
/// </remarks>
internal sealed class StripeBillingProvider : IBillingProvider
{
    private readonly CustomerService _customers;
    private readonly global::Stripe.Checkout.SessionService _sessions;
    private readonly SubscriptionService _subscriptions;
    private readonly global::Stripe.BillingPortal.SessionService _portal;

    public StripeBillingProvider(StripeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.IsConfigured)
        {
            throw new BillingNotConfiguredException("no Stripe secret key is configured");
        }

        // A client per adapter rather than the library's global static key.
        // Global mutable configuration is shared with every other consumer in the
        // process, including tests running side by side.
        StripeClient client = new(new StripeClientOptions
        {
            ApiKey = options.SecretKey,

            // Empty means Stripe's own address. A local verification run points
            // this at a deterministic stub, which is what lets every mandatory
            // test run with no Stripe account and no real money.
            ApiBase = string.IsNullOrWhiteSpace(options.ApiBase)
                ? StripeClient.DefaultApiBase
                : options.ApiBase,
        });

        _customers = new CustomerService(client);
        _sessions = new global::Stripe.Checkout.SessionService(client);
        _subscriptions = new SubscriptionService(client);
        _portal = new global::Stripe.BillingPortal.SessionService(client);
    }

    public BillingProvider Provider => BillingProvider.Stripe;

    public async Task<ProviderReference> CreateCustomerAsync(
        string workspaceName,
        Guid tenantId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        // No email. A workspace is the customer, and an email address belongs to
        // a person who may leave it.
        CustomerCreateOptions create = new()
        {
            Name = workspaceName,
            Metadata = new Dictionary<string, string>
            {
                ["omniai_tenant_id"] = tenantId.ToString("D", CultureInfo.InvariantCulture),
            },
        };

        Customer customer = await CallAsync(
                token => _customers.CreateAsync(
                    create,
                    new RequestOptions { IdempotencyKey = idempotencyKey },
                    token),
                cancellationToken)
            .ConfigureAwait(false);

        return ProviderReference.Create(customer.Id);
    }

    public async Task<ProviderCheckoutSession> CreateSubscriptionCheckoutAsync(
        ProviderCheckoutRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        global::Stripe.Checkout.SessionCreateOptions create = new()
        {
            Mode = "subscription",
            Customer = request.ExistingCustomerId?.Value,
            LineItems =
            [
                new global::Stripe.Checkout.SessionLineItemOptions { Price = request.PriceId.Value, Quantity = 1 },
            ],

            // Server-owned, from configuration. A browser-supplied return address
            // would be an open redirect that Stripe itself would send the
            // customer to.
            SuccessUrl = request.SuccessUrl,
            CancelUrl = request.CancelUrl,

            // Carried so a verified event can be traced back to the attempt that
            // started it. Correlation only -- the tenant is never taken from here.
            Metadata = new Dictionary<string, string>
            {
                ["omniai_checkout_intent"] = request.IntentId.ToString(),
            },
        };

        if (request.TrialDays is int days and > 0)
        {
            create.SubscriptionData = new global::Stripe.Checkout.SessionSubscriptionDataOptions { TrialPeriodDays = days };
        }

        global::Stripe.Checkout.Session session = await CallAsync(
                token => _sessions.CreateAsync(
                    create,
                    new RequestOptions { IdempotencyKey = request.IdempotencyKey },
                    token),
                cancellationToken)
            .ConfigureAwait(false);

        return new ProviderCheckoutSession(
            ProviderReference.Create(session.Id),
            session.Url);
    }

    public async Task<ProviderCheckoutResult?> GetCheckoutAsync(
        ProviderReference sessionId,
        CancellationToken cancellationToken = default)
    {
        global::Stripe.Checkout.Session? session = await ReadAsync(
                token => _sessions.GetAsync(sessionId.Value, cancellationToken: token),
                cancellationToken)
            .ConfigureAwait(false);

        return session is null
            ? null
            : new ProviderCheckoutResult(
                ProviderReference.Create(session.Id),
                ProviderReference.CreateOrNull(session.CustomerId),
                ProviderReference.CreateOrNull(session.SubscriptionId),
                string.Equals(session.Status, "complete", StringComparison.Ordinal));
    }

    public async Task<ProviderSubscriptionSnapshot?> GetSubscriptionAsync(
        ProviderReference subscriptionId,
        CancellationToken cancellationToken = default)
    {
        Subscription? subscription = await ReadAsync(
                token => _subscriptions.GetAsync(subscriptionId.Value, cancellationToken: token),
                cancellationToken)
            .ConfigureAwait(false);

        if (subscription is null)
        {
            return null;
        }

        SubscriptionItem? item = subscription.Items?.Data?.FirstOrDefault();

        return new ProviderSubscriptionSnapshot(
            ProviderReference.Create(subscription.Id),
            ProviderReference.Create(subscription.CustomerId),

            // The price is on the item. A subscription with no item is not
            // something this platform sells, and an empty reference would be
            // mapped to no offer and refused, which is the right outcome.
            item?.Price?.Id is string priceId
                ? ProviderReference.Create(priceId)
                : ProviderReference.Create("unknown"),
            StripeSubscriptionStatus.Normalise(subscription.Status),
            subscription.Status,

            // Moved onto the item in current API versions. Reading them from the
            // subscription would silently produce nulls on every period.
            item?.CurrentPeriodStart,
            item?.CurrentPeriodEnd,
            subscription.TrialEnd,
            subscription.CancelAtPeriodEnd,
            subscription.CanceledAt);
    }

    public async Task<string> CreatePortalSessionAsync(
        ProviderReference customerId,
        string returnUrl,
        CancellationToken cancellationToken = default)
    {
        global::Stripe.BillingPortal.Session session = await CallAsync(
                token => _portal.CreateAsync(
                    new global::Stripe.BillingPortal.SessionCreateOptions { Customer = customerId.Value, ReturnUrl = returnUrl },
                    cancellationToken: token),
                cancellationToken)
            .ConfigureAwait(false);

        return session.Url;
    }

    /// <summary>
    /// A mutating call, made exactly once.
    /// </summary>
    /// <remarks>
    /// No retry. Every call routed through here creates something that costs
    /// money, and a retry without the provider's own idempotency protection is
    /// how somebody gets charged twice. The key is supplied by the caller and
    /// Stripe enforces it; this wrapper only translates failure.
    /// </remarks>
    private static async Task<T> CallAsync<T>(
        Func<CancellationToken, Task<T>> call,
        CancellationToken cancellationToken)
    {
        try
        {
            return await call(cancellationToken).ConfigureAwait(false);
        }
        catch (StripeException failure)
        {
            // The message is Stripe's and safe; the exception itself does not
            // cross the Application boundary, so no SDK type escapes.
            throw new BillingProviderUnavailableException(
                "The billing provider refused the request.", failure);
        }
        catch (HttpRequestException failure)
        {
            throw new BillingProviderUnavailableException(
                "The billing provider could not be reached.", failure);
        }
        catch (TaskCanceledException failure) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BillingProviderUnavailableException(
                "The billing provider did not answer in time.", failure);
        }
    }

    /// <summary>
    /// A read, which is safe to repeat.
    /// </summary>
    /// <remarks>
    /// A missing object is null rather than an exception: an event about a
    /// subscription Stripe no longer has is an ordinary outcome, and the worker
    /// records it rather than retrying forever.
    /// </remarks>
    private static async Task<T?> ReadAsync<T>(
        Func<CancellationToken, Task<T>> call,
        CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            return await call(cancellationToken).ConfigureAwait(false);
        }
        catch (StripeException failure) when (failure.StripeError?.Type == "invalid_request_error")
        {
            return null;
        }
        catch (StripeException failure)
        {
            throw new BillingProviderUnavailableException(
                "The billing provider refused the request.", failure);
        }
        catch (HttpRequestException failure)
        {
            throw new BillingProviderUnavailableException(
                "The billing provider could not be reached.", failure);
        }
        catch (TaskCanceledException failure) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BillingProviderUnavailableException(
                "The billing provider did not answer in time.", failure);
        }
    }
}

/// <summary>
/// Stripe's subscription statuses, in this platform's vocabulary.
/// </summary>
/// <remarks>
/// <para>
/// Stripe has eight; this platform has six, and the mapping is deliberate rather
/// than mechanical:
/// </para>
/// <list type="table">
/// <item><term>incomplete</term><description>PendingActivation — the first payment has not succeeded, so nothing is granted.</description></item>
/// <item><term>incomplete_expired</term><description>Canceled — terminal; the subscription never started.</description></item>
/// <item><term>trialing</term><description>Trialing — the provider says a trial is running, so the plan is granted.</description></item>
/// <item><term>active</term><description>Active.</description></item>
/// <item><term>past_due</term><description>PastDue — payment failed and this platform's grace period is running.</description></item>
/// <item><term>unpaid</term><description>Suspended — Stripe has given up retrying. Grace is over whatever this platform's clock says.</description></item>
/// <item><term>paused</term><description>Suspended — a trial ended with no payment method, so nothing is being paid for.</description></item>
/// <item><term>canceled</term><description>Canceled — terminal.</description></item>
/// </list>
/// <para>
/// Nothing collapses into Active that has not been paid for, and nothing
/// collapses into Canceled that might still recover.
/// </para>
/// </remarks>
internal static class StripeSubscriptionStatus
{
    public static SubscriptionStatus Normalise(string? status) => status switch
    {
        "incomplete" => SubscriptionStatus.PendingActivation,
        "incomplete_expired" => SubscriptionStatus.Canceled,
        "trialing" => SubscriptionStatus.Trialing,
        "active" => SubscriptionStatus.Active,
        "past_due" => SubscriptionStatus.PastDue,
        "unpaid" => SubscriptionStatus.Suspended,
        "paused" => SubscriptionStatus.Suspended,
        "canceled" => SubscriptionStatus.Canceled,

        // A status this SDK version does not know. Granting nothing is the only
        // safe reading of "the provider said something we do not understand".
        _ => SubscriptionStatus.PendingActivation,
    };
}

/// <summary>
/// The Stripe credentials, read from the environment.
/// </summary>
/// <remarks>
/// <strong>Platform secrets, and never tenant data.</strong> They live in the
/// process environment, never in a tenant table, never in
/// <c>appsettings.json</c>, never in the Angular bundle and never in a log line.
/// A deployment without them runs everything except selling.
/// </remarks>
public sealed class StripeOptions
{
    public const string SecretKeyVariable = "STRIPE_SECRET_KEY";

    public const string WebhookSecretVariable = "STRIPE_WEBHOOK_SECRET";

    /// <summary>
    /// Where the adapter sends requests. Empty in every real deployment.
    /// </summary>
    /// <remarks>
    /// Set only to reach a local stub, so that Phase 25 can be verified end to
    /// end without a Stripe account, a real key or a real card.
    /// </remarks>
    public const string ApiBaseVariable = "STRIPE_API_BASE";

    public string SecretKey { get; set; } = string.Empty;

    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>
    /// Where the adapter sends requests. Overridden only to reach a local stub.
    /// </summary>
    /// <remarks>
    /// Empty in every real deployment, which means Stripe's own address. A local
    /// verification run points it at a deterministic stub so that no mandatory
    /// test needs a Stripe account, a real key or a real card.
    /// </remarks>
    public string ApiBase { get; set; } = string.Empty;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(SecretKey);

    public bool CanReceiveWebhooks => !string.IsNullOrWhiteSpace(WebhookSecret);
}
