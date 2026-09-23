using System.Collections.Concurrent;
using OmniAI.Application.Billing;
using OmniAI.Domain.Billing;

namespace OmniAI.Testing;

/// <summary>
/// A payment provider that answers from memory.
/// </summary>
/// <remarks>
/// <para>
/// <strong>No Stripe account, no key, no card, no network.</strong> Every
/// mandatory verification for this phase runs against this: nothing reaches
/// api.stripe.com, nothing is charged, and no real customer payment data exists
/// anywhere in this suite. What is under test is this platform's own behaviour —
/// idempotency, tenant attribution, entitlement policy, ordering — and none of
/// that is made more true by talking to a payment company.
/// </para>
/// <para>
/// <strong>Deterministic.</strong> References are generated from a counter rather
/// than randomly, so a test can name what it expects the provider to have
/// created. It also records how many times each mutating call arrived, which is
/// what makes "two clicks produce one checkout" assertable at all.
/// </para>
/// <para>
/// <strong>The signature check is not faked.</strong> Webhook verification uses
/// Stripe's own library against a locally generated signature, so the security
/// tests exercise the real HMAC, the real timestamp tolerance and the real
/// constant-time comparison. This double sits below that, on the outbound side.
/// </para>
/// </remarks>
public sealed class FakeBillingProvider : IBillingProvider
{
    private readonly ConcurrentDictionary<string, ProviderCheckoutResult> _checkouts = new();
    private readonly ConcurrentDictionary<string, ProviderSubscriptionSnapshot> _subscriptions = new();
    private readonly ConcurrentDictionary<string, string> _byIdempotencyKey = new();

    private int _sequence;

    public BillingProvider Provider => BillingProvider.Stripe;

    /// <summary>How many customers this provider was asked to create.</summary>
    public int CustomersCreated { get; private set; }

    /// <summary>How many checkout sessions were actually created.</summary>
    /// <remarks>
    /// The number a duplicate-click test asserts on. An idempotency key that was
    /// seen before returns the same session and does not increment this.
    /// </remarks>
    public int CheckoutsCreated => _checkouts.Count;

    /// <summary>How many portal sessions were opened.</summary>
    public int PortalSessionsCreated { get; private set; }

    /// <summary>What every call should do instead of succeeding, if anything.</summary>
    /// <remarks>
    /// For the tests that assert a provider outage is reported honestly rather
    /// than swallowed into a half-created subscription.
    /// </remarks>
    public bool IsUnavailable { get; set; }

    public Task<ProviderReference> CreateCustomerAsync(
        string workspaceName,
        Guid tenantId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        Refuse();

        CustomersCreated++;

        return Task.FromResult(ProviderReference.Create($"cus_test_{Next()}"));
    }

    public Task<ProviderCheckoutSession> CreateSubscriptionCheckoutAsync(
        ProviderCheckoutRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Refuse();

        // Stripe's own idempotency behaviour: the same key returns the same
        // session rather than making a second one. Without this the double-click
        // test would pass on the platform's intent and fail on reality.
        string sessionId = _byIdempotencyKey.GetOrAdd(
            request.IdempotencyKey,
            _ => $"cs_test_{Next()}");

        _checkouts.TryAdd(
            sessionId,
            new ProviderCheckoutResult(
                ProviderReference.Create(sessionId),
                request.ExistingCustomerId ?? ProviderReference.Create($"cus_test_{Next()}"),
                SubscriptionId: null,
                IsComplete: false));

        return Task.FromResult(new ProviderCheckoutSession(
            ProviderReference.Create(sessionId),
            $"https://checkout.test.local/{sessionId}"));
    }

    public Task<ProviderCheckoutResult?> GetCheckoutAsync(
        ProviderReference sessionId,
        CancellationToken cancellationToken = default)
    {
        Refuse();

        return Task.FromResult(_checkouts.GetValueOrDefault(sessionId.Value));
    }

    public Task<ProviderSubscriptionSnapshot?> GetSubscriptionAsync(
        ProviderReference subscriptionId,
        CancellationToken cancellationToken = default)
    {
        Refuse();

        return Task.FromResult(_subscriptions.GetValueOrDefault(subscriptionId.Value));
    }

    public Task<string> CreatePortalSessionAsync(
        ProviderReference customerId,
        string returnUrl,
        CancellationToken cancellationToken = default)
    {
        Refuse();

        PortalSessionsCreated++;

        return Task.FromResult($"https://portal.test.local/{customerId.Value}");
    }

    /// <summary>
    /// Says that a checkout produced a subscription, as a real provider would.
    /// </summary>
    /// <remarks>
    /// Separate from creating the session, because that is how it really works: a
    /// customer pays some time after being sent to the page, and the platform
    /// finds out by asking.
    /// </remarks>
    public ProviderReference CompleteCheckout(
        ProviderReference sessionId,
        ProviderReference priceId,
        SubscriptionStatus status = SubscriptionStatus.Active,
        string rawStatus = "active",
        DateTimeOffset? periodEnd = null)
    {
        ProviderCheckoutResult existing = _checkouts[sessionId.Value];

        ProviderReference subscriptionId = ProviderReference.Create($"sub_test_{Next()}");

        _checkouts[sessionId.Value] = existing with
        {
            SubscriptionId = subscriptionId,
            IsComplete = true,
        };

        SetSubscription(
            subscriptionId,
            existing.CustomerId ?? ProviderReference.Create($"cus_test_{Next()}"),
            priceId,
            status,
            rawStatus,
            periodEnd);

        return subscriptionId;
    }

    /// <summary>What the provider currently says about a subscription.</summary>
    /// <remarks>
    /// The platform reads this rather than trusting an event's contents, which is
    /// what the out-of-order tests exercise: they move this and then deliver
    /// events in whatever sequence they like.
    /// </remarks>
    public void SetSubscription(
        ProviderReference subscriptionId,
        ProviderReference customerId,
        ProviderReference priceId,
        SubscriptionStatus status,
        string rawStatus,
        DateTimeOffset? periodEnd = null,
        DateTimeOffset? trialEndsAt = null,
        bool cancelAtPeriodEnd = false,
        DateTimeOffset? canceledAt = null) =>
        _subscriptions[subscriptionId.Value] = new ProviderSubscriptionSnapshot(
            subscriptionId,
            customerId,
            priceId,
            status,
            rawStatus,
            periodEnd?.AddDays(-30),
            periodEnd,
            trialEndsAt,
            cancelAtPeriodEnd,
            canceledAt);

    /// <summary>Makes a subscription unknown, as a deleted one would be.</summary>
    public void Forget(ProviderReference subscriptionId) =>
        _subscriptions.TryRemove(subscriptionId.Value, out _);

    private void Refuse()
    {
        if (IsUnavailable)
        {
            throw new BillingProviderUnavailableException("the test provider is unavailable");
        }
    }

    private int Next() => Interlocked.Increment(ref _sequence);
}
