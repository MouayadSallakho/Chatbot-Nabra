using OmniAI.Application.Authorization;
using OmniAI.Application.Billing.Persistence;
using OmniAI.Application.Tenants;
using OmniAI.Application.Tenants.Persistence;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Billing;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Billing;

/// <summary>
/// Buy a subscription to one internal offer.
/// </summary>
/// <remarks>
/// <strong>The offer id is the only thing the browser chooses.</strong> Not the
/// amount, not the currency, not the provider's price, not where the customer is
/// sent afterwards. Everything else is resolved server-side from the catalogue
/// and from configuration.
/// </remarks>
public sealed record StartBillingCheckoutCommand(Guid TenantId, Guid OfferId);

/// <summary>Where to send the customer to pay.</summary>
public sealed record BillingCheckoutStarted(string Url);

/// <summary>
/// Why a checkout could not be started.
/// </summary>
/// <remarks>
/// Each of these is a different thing for a person to do about it, so each is a
/// distinct code rather than one generic failure — and a client branches on the
/// code, never on the sentence.
/// </remarks>
public enum BillingCheckoutRefusal
{
    /// <summary>No such offer in this platform's catalogue.</summary>
    UnknownOffer = 1,

    /// <summary>The offer exists but is no longer sold.</summary>
    OfferNotAvailable = 2,

    /// <summary>The workspace already has a subscription. It manages it in the portal.</summary>
    AlreadySubscribed = 3,

    /// <summary>No provider price maps this offer, so it cannot be bought here.</summary>
    OfferNotPurchasable = 4,
}

/// <summary>A checkout that could not be started, and why.</summary>
public sealed class BillingCheckoutRefusedException(BillingCheckoutRefusal reason, string message)
    : Exception(message)
{
    public BillingCheckoutRefusal Reason { get; } = reason;

    /// <summary>A stable code a client branches on.</summary>
    public string Code => $"billing.checkout.{Reason.ToString().ToLowerInvariant()}";
}

/// <summary>
/// Starts a hosted checkout for a subscription.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This platform never sees a card.</strong> It creates a session with
/// the provider and hands back a URL; the customer enters their details on the
/// provider's own page. That is the whole of the PCI boundary, and it holds
/// because there is no code here that could receive a card number.
/// </para>
/// <para>
/// <strong>A duplicate request finds the first one.</strong> The intent is
/// claimed under a unique key before the provider is called, so two clicks
/// produce one provider session and one subscription rather than two of each.
/// </para>
/// <para>
/// <strong>Reaching the success URL proves nothing.</strong> This creates a
/// session; the subscription only becomes real when the provider says so through
/// a verified event, which is deliberately a different code path.
/// </para>
/// </remarks>
public sealed class StartBillingCheckoutHandler(
    IBillingProvider provider,
    IBillingCatalog catalog,
    IWorkspaceSubscriptionStore subscriptions,
    IWorkspaceBillingCustomerStore customers,
    IBillingCheckoutIntentStore intents,
    ITenantRepository tenants,
    BillingOptions options,
    TenantAuthorizer authorizer,
    TimeProvider clock)
{
    private readonly IBillingProvider _provider =
        provider ?? throw new ArgumentNullException(nameof(provider));

    private readonly IBillingCatalog _catalog =
        catalog ?? throw new ArgumentNullException(nameof(catalog));

    private readonly IWorkspaceSubscriptionStore _subscriptions =
        subscriptions ?? throw new ArgumentNullException(nameof(subscriptions));

    private readonly IWorkspaceBillingCustomerStore _customers =
        customers ?? throw new ArgumentNullException(nameof(customers));

    private readonly IBillingCheckoutIntentStore _intents =
        intents ?? throw new ArgumentNullException(nameof(intents));

    private readonly ITenantRepository _tenants =
        tenants ?? throw new ArgumentNullException(nameof(tenants));

    private readonly BillingOptions _options =
        options ?? throw new ArgumentNullException(nameof(options));

    private readonly TenantAuthorizer _authorizer =
        authorizer ?? throw new ArgumentNullException(nameof(authorizer));

    private readonly TimeProvider _clock =
        clock ?? throw new ArgumentNullException(nameof(clock));

    /// <exception cref="TenantNotFoundException">Not a member, or no such workspace.</exception>
    /// <exception cref="PermissionDeniedException">A member without billing.manage.</exception>
    /// <exception cref="BillingCheckoutRefusedException">The purchase cannot proceed.</exception>
    /// <exception cref="BillingNotConfiguredException">This deployment cannot sell.</exception>
    /// <exception cref="BillingProviderUnavailableException">The provider did not answer.</exception>
    public async Task<BillingCheckoutStarted> HandleAsync(
        StartBillingCheckoutCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        TenantId tenantId = TenantId.From(command.TenantId);

        // Authority before anything commercial is revealed. Somebody who may not
        // spend this workspace's money must not learn what it would cost, what
        // it is already on, or whether an offer exists.
        if (!await _authorizer
            .RequireAsync(tenantId, PlatformPermissions.BillingManage, cancellationToken)
            .ConfigureAwait(false))
        {
            throw TenantNotFoundException.ForId(tenantId.Value);
        }

        if (!_options.IsConfigured)
        {
            throw new BillingNotConfiguredException(
                "no fallback plan or return address is configured");
        }

        Tenant tenant =
            await _tenants.FindByIdAsync(tenantId, cancellationToken).ConfigureAwait(false)
            ?? throw TenantNotFoundException.ForId(tenantId.Value);

        // One subscription per workspace. A second is not started here; changes
        // and cancellation happen in the provider's own portal.
        if (await _subscriptions.FindCurrentAsync(tenantId, cancellationToken)
            .ConfigureAwait(false) is not null)
        {
            throw new BillingCheckoutRefusedException(
                BillingCheckoutRefusal.AlreadySubscribed,
                "This workspace already has a subscription. Manage it from the billing portal.");
        }

        BillingOfferId offerId = BillingOfferId.From(command.OfferId);

        BillingPlanOffer offer =
            await _catalog.FindOfferAsync(offerId, cancellationToken).ConfigureAwait(false)
            ?? throw new BillingCheckoutRefusedException(
                BillingCheckoutRefusal.UnknownOffer,
                "That is not something this platform sells.");

        if (!offer.IsActive)
        {
            throw new BillingCheckoutRefusedException(
                BillingCheckoutRefusal.OfferNotAvailable,
                "That offer is no longer available.");
        }

        // The provider's price comes from the mapping, never from the request.
        ProviderReference priceId = await _catalog
            .PriceForAsync(offerId, _provider.Provider, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new BillingCheckoutRefusedException(
                BillingCheckoutRefusal.OfferNotPurchasable,
                "That offer cannot be bought on this deployment.");

        DateTimeOffset now = _clock.GetUtcNow();

        // Claimed before the provider is called. If a second request is already
        // in flight this returns that one, and the provider is asked once.
        (BillingCheckoutIntent intent, bool isNew) = await _intents
            .ClaimAsync(
                BillingCheckoutIntent.Start(tenantId, _provider.Provider, offerId, now),
                cancellationToken)
            .ConfigureAwait(false);

        if (!isNew && !intent.IsUsableAt(now))
        {
            // An earlier attempt for this workspace and offer has concluded. The
            // customer is looking at a stale page; the honest answer is that
            // their subscription may already exist.
            throw new BillingCheckoutRefusedException(
                BillingCheckoutRefusal.AlreadySubscribed,
                "A checkout for this offer has already been completed or has expired. "
                + "Reload the billing page to see where it got to.");
        }

        WorkspaceBillingCustomer customer = await CustomerAsync(tenant, intent, cancellationToken)
            .ConfigureAwait(false);

        ProviderCheckoutSession session = await _provider
            .CreateSubscriptionCheckoutAsync(
                new ProviderCheckoutRequest(
                    priceId,
                    customer.ProviderCustomerId,

                    // From configuration. A return address the browser supplied
                    // would be an open redirect that a payment provider is
                    // willing to send a customer to.
                    Return("billing?checkout=complete"),
                    Return("billing?checkout=cancelled"),
                    intent.IdempotencyKey,
                    offer.TrialDays,
                    intent.Id),
                cancellationToken)
            .ConfigureAwait(false);

        intent.Started(session.SessionId);

        await _intents.SaveAsync(intent, cancellationToken).ConfigureAwait(false);

        return new BillingCheckoutStarted(session.Url);
    }

    /// <summary>The workspace's provider customer, made once and reused.</summary>
    private async Task<WorkspaceBillingCustomer> CustomerAsync(
        Tenant tenant,
        BillingCheckoutIntent intent,
        CancellationToken cancellationToken)
    {
        WorkspaceBillingCustomer? existing = await _customers
            .FindAsync(tenant.Id, _provider.Provider, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return existing;
        }

        // The idempotency key is the intent's, so a retry of the same checkout
        // does not create a second provider customer.
        ProviderReference created = await _provider
            .CreateCustomerAsync(
                tenant.Name.Value,
                tenant.Id.Value,
                $"customer:{intent.IdempotencyKey}",
                cancellationToken)
            .ConfigureAwait(false);

        return await _customers
            .GetOrAddAsync(
                WorkspaceBillingCustomer.Create(
                    tenant.Id,
                    _provider.Provider,
                    created,
                    _clock.GetUtcNow()),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private string Return(string path) =>
        $"{_options.ReturnBaseUrl.TrimEnd('/')}/{path.TrimStart('/')}";
}
