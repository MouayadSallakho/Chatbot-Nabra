using OmniAI.Domain.Billing;

namespace OmniAI.Application.Billing;

/// <summary>
/// What a billing provider currently says about a subscription.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The provider's answer, normalised, with no provider types in
/// it.</strong> Stripe's own objects stay inside the adapter; everything above
/// works with this. That is what makes a second provider an adapter rather than a
/// rewrite — and it is also why nothing outside Infrastructure has a compile-time
/// dependency on Stripe.
/// </para>
/// <para>
/// <c>RawStatus</c> is carried for diagnostics: it is stored so an operator can
/// see which provider state produced a normalised one, and nothing branches on
/// it outside the adapter that set it.
/// </para>
/// </remarks>
public sealed record ProviderSubscriptionSnapshot(
    ProviderReference SubscriptionId,
    ProviderReference CustomerId,

    /// <summary>The price this subscription is actually billing against.</summary>
    ProviderReference PriceId,
    SubscriptionStatus Status,
    string RawStatus,
    DateTimeOffset? CurrentPeriodStart,
    DateTimeOffset? CurrentPeriodEnd,
    DateTimeOffset? TrialEndsAt,
    bool CancelAtPeriodEnd,
    DateTimeOffset? CanceledAt);

/// <summary>Where a customer should be sent to complete a purchase.</summary>
/// <remarks>
/// The URL is used once, immediately, and never stored: it carries somebody to a
/// payment page and is short-lived by design.
/// </remarks>
public sealed record ProviderCheckoutSession(ProviderReference SessionId, string Url);

/// <summary>What a checkout session became.</summary>
public sealed record ProviderCheckoutResult(
    ProviderReference SessionId,
    ProviderReference? CustomerId,
    ProviderReference? SubscriptionId,
    bool IsComplete);

/// <summary>What the platform asks a provider to start a subscription purchase.</summary>
/// <remarks>
/// <strong>Every value here is chosen by the server.</strong> The price comes
/// from an internal offer's mapping, the URLs from platform configuration, and
/// the idempotency key from the checkout intent. Nothing a browser sent reaches
/// the provider as authority.
/// </remarks>
public sealed record ProviderCheckoutRequest(
    ProviderReference PriceId,
    ProviderReference? ExistingCustomerId,
    string SuccessUrl,
    string CancelUrl,
    string IdempotencyKey,
    int? TrialDays,

    /// <summary>Correlates the provider's event back to this checkout attempt.</summary>
    BillingCheckoutIntentId IntentId);

/// <summary>
/// The payment platform, as the rest of this application sees it.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Deliberately small.</strong> Four operations: make a customer, start a
/// checkout, read a subscription, open the customer's own portal. Everything else
/// a payment provider can do — invoices, payment methods, cancellation, plan
/// changes — happens in the provider's hosted portal, where it is their problem
/// and their compliance surface rather than this platform's.
/// </para>
/// <para>
/// <strong>Reads and writes are different risks.</strong> Reading a subscription
/// is safe to retry; creating one is not, so every mutating call carries an
/// idempotency key the provider honours.
/// </para>
/// </remarks>
public interface IBillingProvider
{
    /// <summary>Which provider this is. Stored alongside every reference it returns.</summary>
    BillingProvider Provider { get; }

    /// <summary>Creates the provider-side customer for a workspace.</summary>
    /// <remarks>
    /// Carries no personal data beyond what the workspace already is: a name and
    /// the workspace's own identifier. No email is used as identity.
    /// </remarks>
    Task<ProviderReference> CreateCustomerAsync(
        string workspaceName,
        Guid tenantId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>Starts a hosted checkout for a subscription.</summary>
    Task<ProviderCheckoutSession> CreateSubscriptionCheckoutAsync(
        ProviderCheckoutRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Reads what a checkout session became.</summary>
    Task<ProviderCheckoutResult?> GetCheckoutAsync(
        ProviderReference sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the provider's current view of a subscription.</summary>
    /// <returns><see langword="null"/> when the provider has no such subscription.</returns>
    Task<ProviderSubscriptionSnapshot?> GetSubscriptionAsync(
        ProviderReference subscriptionId,
        CancellationToken cancellationToken = default);

    /// <summary>Opens a short-lived session in the provider's own billing portal.</summary>
    Task<string> CreatePortalSessionAsync(
        ProviderReference customerId,
        string returnUrl,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The billing provider could not be reached or would not answer.
/// </summary>
/// <remarks>
/// A transport failure, a timeout, or the provider refusing. Never a reason to
/// grant or withdraw a plan: it means this platform does not currently know, and
/// the honest response is to say so and change nothing.
/// </remarks>
public sealed class BillingProviderUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>
/// This deployment is not configured to take payments.
/// </summary>
/// <remarks>
/// No secret key, no webhook secret, or no redirect URLs. A deployment in this
/// state runs perfectly well and simply cannot sell anything — which is the right
/// behaviour for every environment that is not production, and a safe failure in
/// one that is.
/// </remarks>
public sealed class BillingNotConfiguredException(string reason)
    : Exception($"Billing is not available on this deployment: {reason}");
