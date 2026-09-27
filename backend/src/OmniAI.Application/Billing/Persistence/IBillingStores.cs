using OmniAI.Domain.Billing;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Billing.Persistence;

/// <summary>
/// Where a workspace's subscription lives.
/// </summary>
/// <remarks>
/// Purpose-specific, like every other persistence contract here: no generic
/// repository, no query language, and only the questions this product actually
/// asks.
/// </remarks>
public interface IWorkspaceSubscriptionStore
{
    /// <summary>
    /// The subscription currently governing a workspace, if any.
    /// </summary>
    /// <remarks>
    /// At most one exists, which the database enforces rather than this query
    /// assuming.
    /// </remarks>
    Task<WorkspaceSubscription?> FindCurrentAsync(
        TenantId tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>The subscription a provider reference belongs to, if any.</summary>
    /// <remarks>
    /// How a webhook finds its way from a provider identifier to a workspace.
    /// The tenant is never taken from anything the provider sends.
    /// </remarks>
    Task<WorkspaceSubscription?> FindByProviderSubscriptionAsync(
        BillingProvider provider,
        ProviderReference providerSubscriptionId,
        CancellationToken cancellationToken = default);
}

/// <summary>Where a workspace's provider customer record lives.</summary>
public interface IWorkspaceBillingCustomerStore
{
    Task<WorkspaceBillingCustomer?> FindAsync(
        TenantId tenantId,
        BillingProvider provider,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a customer, or returns the one that already existed.
    /// </summary>
    /// <remarks>
    /// Two simultaneous first purchases would otherwise create two provider
    /// customers for one workspace. The unique key decides; the loser reads back
    /// what the winner wrote.
    /// </remarks>
    Task<WorkspaceBillingCustomer> GetOrAddAsync(
        WorkspaceBillingCustomer customer,
        CancellationToken cancellationToken = default);
}

/// <summary>Where checkout attempts live.</summary>
public interface IBillingCheckoutIntentStore
{
    /// <summary>
    /// Claims an attempt, or hands back the one already in flight.
    /// </summary>
    /// <remarks>
    /// <strong>This is the idempotency.</strong> The unique key is the workspace,
    /// the offer and the hour; two clicks a second apart collide and the second
    /// receives the first attempt rather than starting another. It works across
    /// web servers, which a disabled button does not.
    /// </remarks>
    Task<(BillingCheckoutIntent Intent, bool IsNew)> ClaimAsync(
        BillingCheckoutIntent intent,
        CancellationToken cancellationToken = default);

    Task<BillingCheckoutIntent?> FindAsync(
        BillingCheckoutIntentId id,
        CancellationToken cancellationToken = default);

    /// <summary>The attempt a provider session belongs to, if any.</summary>
    Task<BillingCheckoutIntent?> FindByProviderSessionAsync(
        BillingProvider provider,
        ProviderReference sessionId,
        CancellationToken cancellationToken = default);

    Task SaveAsync(BillingCheckoutIntent intent, CancellationToken cancellationToken = default);
}

/// <summary>
/// The durable queue of verified provider events.
/// </summary>
/// <remarks>
/// The webhook writes here and returns; a worker reads. That is what lets the
/// endpoint acknowledge within the provider's timeout while the actual work —
/// fetching a subscription, taking a lock, reassigning a plan — happens
/// afterwards.
/// </remarks>
public interface IBillingEventInbox
{
    /// <summary>
    /// Stores a verified event, or reports that it was already stored.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when this provider event id is already known, in
    /// which case nothing was written.
    /// </returns>
    Task<bool> ReceiveAsync(BillingEvent billingEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes a batch of events for this worker and nobody else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One atomic statement, so several instances asking together get disjoint
    /// sets.
    /// </para>
    /// <para>
    /// <strong>A claim is a lease (Phase 35).</strong> An event another worker has
    /// claimed is not claimable again until that claim is older than
    /// <paramref name="lease"/> — before, a second instance could take an event that
    /// was still being processed. Once the lease has lapsed the event is claimed
    /// again with a new owner, and the old owner can no longer settle it.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<BillingEvent>> ClaimAsync(
        int batchSize,
        int maxAttempts,
        DateTimeOffset now,
        TimeSpan lease,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records what became of an event this worker holds.
    /// </summary>
    /// <exception cref="BillingEventOwnershipLostException">
    /// The claim this event was loaded under is no longer the current one: the
    /// lease lapsed and another worker took it. Nothing was written.
    /// </exception>
    Task SaveAsync(BillingEvent billingEvent, CancellationToken cancellationToken = default);
}

/// <summary>
/// The worker holding a billing event is no longer its owner (Phase 35).
/// </summary>
/// <remarks>
/// Its lease lapsed and another worker claimed the event. The stale worker must
/// stop: it may not apply subscription state or settle the event, because the new
/// owner is doing both. Not a failure to retry.
/// </remarks>
public sealed class BillingEventOwnershipLostException : Exception
{
    public BillingEventOwnershipLostException()
        : base("This worker's claim on the billing event is no longer current.")
    {
    }

    public BillingEventOwnershipLostException(string message)
        : base(message)
    {
    }

    public BillingEventOwnershipLostException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
