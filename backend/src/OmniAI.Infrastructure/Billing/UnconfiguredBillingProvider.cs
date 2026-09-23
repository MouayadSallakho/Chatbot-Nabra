using OmniAI.Application.Billing;
using OmniAI.Domain.Billing;

namespace OmniAI.Infrastructure.Billing;

/// <summary>
/// The provider on a deployment that has no provider keys.
/// </summary>
/// <remarks>
/// <para>
/// <strong>It refuses, rather than not existing.</strong> Leaving
/// <see cref="IBillingProvider"/> unregistered would stop the whole application
/// starting, because every billing handler asks for one — so a deployment with no
/// Stripe key could not serve the conversations, agents and knowledge it has
/// nothing to do with billing. Refusing at the point of use keeps the failure
/// where it belongs.
/// </para>
/// <para>
/// <strong>Every path throws the same thing.</strong> There is no partial
/// behaviour, no silent success and no fabricated reference: a caller either
/// checked <see cref="BillingOptions.IsConfigured"/> first and never arrived, or
/// is told plainly that this deployment cannot take subscriptions.
/// </para>
/// </remarks>
internal sealed class UnconfiguredBillingProvider : IBillingProvider
{
    private const string Reason = "no billing provider is configured on this deployment";

    /// <summary>
    /// Stripe, because that is the provider this platform integrates.
    /// </summary>
    /// <remarks>
    /// What is missing is the credentials, not the choice. Stored references are
    /// tagged with this, so a deployment that later gains a key finds its own
    /// rows rather than orphaned ones — though in practice there are none to
    /// find, since nothing here can create any.
    /// </remarks>
    public BillingProvider Provider => BillingProvider.Stripe;

    public Task<ProviderReference> CreateCustomerAsync(
        string workspaceName,
        Guid tenantId,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        throw new BillingNotConfiguredException(Reason);

    public Task<ProviderCheckoutSession> CreateSubscriptionCheckoutAsync(
        ProviderCheckoutRequest request,
        CancellationToken cancellationToken = default) =>
        throw new BillingNotConfiguredException(Reason);

    public Task<ProviderCheckoutResult?> GetCheckoutAsync(
        ProviderReference sessionId,
        CancellationToken cancellationToken = default) =>
        throw new BillingNotConfiguredException(Reason);

    public Task<ProviderSubscriptionSnapshot?> GetSubscriptionAsync(
        ProviderReference subscriptionId,
        CancellationToken cancellationToken = default) =>
        throw new BillingNotConfiguredException(Reason);

    public Task<string> CreatePortalSessionAsync(
        ProviderReference customerId,
        string returnUrl,
        CancellationToken cancellationToken = default) =>
        throw new BillingNotConfiguredException(Reason);
}
