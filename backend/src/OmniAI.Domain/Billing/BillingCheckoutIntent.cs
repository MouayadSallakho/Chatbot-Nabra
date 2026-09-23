using OmniAI.Domain.Tenants;

namespace OmniAI.Domain.Billing;

/// <summary>How far a checkout attempt got.</summary>
public enum CheckoutIntentStatus
{
    /// <summary>A provider session exists and the customer may still complete it.</summary>
    Pending = 1,

    /// <summary>The provider reported the session completed.</summary>
    Completed = 2,

    /// <summary>The session expired or the customer abandoned it.</summary>
    Expired = 3,
}

/// <summary>
/// One attempt to buy a subscription.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Written before the provider is asked, so a duplicate cannot become a
/// second subscription.</strong> Two rapid clicks, a retried request, a browser
/// that resubmits — each of these is a second call arriving while the first is in
/// flight. A row claimed under a unique key is what makes the second one find the
/// first rather than start again, and it works across web servers, which
/// disabling a button does not.
/// </para>
/// <para>
/// <strong>It also correlates a signed provider event back to a workspace.</strong>
/// The webhook carries a provider session reference and nothing this platform
/// chose; the intent is where that reference becomes a tenant. A workspace is
/// never taken from anything the browser or the provider supplies as a tenant id.
/// </para>
/// <para>
/// The provider's checkout URL is deliberately not stored. It is short-lived,
/// it is the thing that carries somebody to a payment page, and nothing needs it
/// after the redirect.
/// </para>
/// </remarks>
public sealed class BillingCheckoutIntent
{
    /// <summary>How long a started checkout stays claimable.</summary>
    /// <remarks>
    /// Long enough for somebody to find their card, short enough that an
    /// abandoned attempt does not block a fresh one for the rest of the day.
    /// </remarks>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    private BillingCheckoutIntent(
        BillingCheckoutIntentId id,
        TenantId tenantId,
        BillingProvider provider,
        BillingOfferId offerId,
        string idempotencyKey,
        CheckoutIntentStatus status,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt)
    {
        Id = id;
        TenantId = tenantId;
        Provider = provider;
        OfferId = offerId;
        IdempotencyKey = idempotencyKey;
        Status = status;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    public BillingCheckoutIntentId Id { get; }

    public TenantId TenantId { get; }

    public BillingProvider Provider { get; }

    public BillingOfferId OfferId { get; }

    /// <summary>
    /// What makes two attempts the same attempt.
    /// </summary>
    /// <remarks>
    /// Server-generated from the workspace and the offer, never taken from the
    /// request: a client that chose its own key could defeat the protection by
    /// sending a new one each time, which is precisely what an accidental
    /// double-click does.
    /// </remarks>
    public string IdempotencyKey { get; }

    public CheckoutIntentStatus Status { get; private set; }

    /// <summary>The provider's session, once one has been created.</summary>
    public ProviderReference? ProviderSessionId { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset ExpiresAt { get; }

    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Whether this attempt can still be completed at a given instant.</summary>
    public bool IsUsableAt(DateTimeOffset now) =>
        Status == CheckoutIntentStatus.Pending && now < ExpiresAt;

    public static BillingCheckoutIntent Start(
        TenantId tenantId,
        BillingProvider provider,
        BillingOfferId offerId,
        DateTimeOffset now)
    {
        if (tenantId.IsEmpty)
        {
            throw new DomainValidationException("A checkout must belong to a workspace.");
        }

        if (offerId.IsEmpty)
        {
            throw new DomainValidationException("A checkout must name what is being bought.");
        }

        DateTimeOffset started = now == default
            ? throw new DomainValidationException("A checkout must record when it started.")
            : now.ToUniversalTime();

        return new BillingCheckoutIntent(
            BillingCheckoutIntentId.New(),
            tenantId,
            provider,
            offerId,
            KeyFor(tenantId, offerId, started),
            CheckoutIntentStatus.Pending,
            started,
            started.Add(Lifetime));
    }

    /// <summary>Rehydrates a stored intent. Persistence only.</summary>
    public static BillingCheckoutIntent Restore(
        BillingCheckoutIntentId id,
        TenantId tenantId,
        BillingProvider provider,
        BillingOfferId offerId,
        string idempotencyKey,
        CheckoutIntentStatus status,
        ProviderReference? providerSessionId,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        DateTimeOffset? completedAt) =>
        new(id, tenantId, provider, offerId, idempotencyKey, status, createdAt, expiresAt)
        {
            ProviderSessionId = providerSessionId,
            CompletedAt = completedAt,
        };

    /// <summary>Records the provider session this attempt became.</summary>
    public void Started(ProviderReference sessionId)
    {
        if (sessionId.IsEmpty)
        {
            throw new DomainValidationException("A started checkout must have a provider session.");
        }

        ProviderSessionId = sessionId;
    }

    /// <summary>Records that the provider says this attempt succeeded.</summary>
    /// <returns><see langword="false"/> when it was already concluded.</returns>
    public bool Complete(DateTimeOffset now)
    {
        if (Status != CheckoutIntentStatus.Pending)
        {
            return false;
        }

        Status = CheckoutIntentStatus.Completed;
        CompletedAt = now.ToUniversalTime();

        return true;
    }

    /// <summary>Records that this attempt can no longer be used.</summary>
    /// <returns><see langword="false"/> when it was already concluded.</returns>
    public bool Expire()
    {
        if (Status != CheckoutIntentStatus.Pending)
        {
            return false;
        }

        Status = CheckoutIntentStatus.Expired;

        return true;
    }

    /// <summary>
    /// The key two simultaneous attempts share.
    /// </summary>
    /// <remarks>
    /// Includes the hour so that an abandoned attempt does not block the same
    /// workspace from buying the same offer tomorrow, while two clicks a second
    /// apart collide as they should.
    /// </remarks>
    private static string KeyFor(TenantId tenantId, BillingOfferId offerId, DateTimeOffset at) =>
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{tenantId.Value:N}:{offerId.Value:N}:{at:yyyyMMddHH}");
}

/// <summary>
/// A workspace's customer record with a billing provider.
/// </summary>
/// <remarks>
/// <para>
/// <strong>An opaque reference and nothing else.</strong> No card, no CVV, no
/// payment method, no bank detail, no billing address. Those live with the
/// provider, behind hosted checkout and the hosted portal, and this platform is
/// deliberately not in a position to leak them because it never receives them.
/// </para>
/// <para>
/// <strong>Keyed by workspace and provider, not by email.</strong> An email
/// address changes, is shared between people, and belongs to a person rather than
/// an organisation; a workspace is the thing that actually holds the
/// subscription.
/// </para>
/// </remarks>
public sealed class WorkspaceBillingCustomer
{
    private WorkspaceBillingCustomer(
        TenantId tenantId,
        BillingProvider provider,
        ProviderReference providerCustomerId,
        DateTimeOffset createdAt)
    {
        TenantId = tenantId;
        Provider = provider;
        ProviderCustomerId = providerCustomerId;
        CreatedAt = createdAt;
    }

    public TenantId TenantId { get; }

    public BillingProvider Provider { get; }

    public ProviderReference ProviderCustomerId { get; }

    public DateTimeOffset CreatedAt { get; }

    public static WorkspaceBillingCustomer Create(
        TenantId tenantId,
        BillingProvider provider,
        ProviderReference providerCustomerId,
        DateTimeOffset createdAt)
    {
        if (tenantId.IsEmpty)
        {
            throw new DomainValidationException("A billing customer must belong to a workspace.");
        }

        return providerCustomerId.IsEmpty
            ? throw new DomainValidationException("A billing customer needs a provider reference.")
            : new WorkspaceBillingCustomer(
                tenantId,
                provider,
                providerCustomerId,
                createdAt.ToUniversalTime());
    }

    /// <summary>Rehydrates a stored customer. Persistence only.</summary>
    public static WorkspaceBillingCustomer Restore(
        TenantId tenantId,
        BillingProvider provider,
        ProviderReference providerCustomerId,
        DateTimeOffset createdAt) =>
        new(tenantId, provider, providerCustomerId, createdAt);
}
