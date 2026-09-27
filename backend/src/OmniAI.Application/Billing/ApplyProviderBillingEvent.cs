using OmniAI.Application.Billing.Persistence;
using OmniAI.Domain.Billing;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Billing;

/// <summary>
/// Turns one stored provider event into local state.
/// </summary>
/// <remarks>
/// <para>
/// <strong>An event is a signal to go and look, never a fact to apply.</strong>
/// Every path here ends in reading the provider's current snapshot and applying
/// that. This is what makes ordering irrelevant: a payment failure delivered
/// twenty minutes late is answered with "what is true now", which may be that the
/// customer has since paid. Replaying charges or checkouts to repair ordering
/// would move money to fix a bookkeeping problem.
/// </para>
/// <para>
/// <strong>The workspace is never taken from the event.</strong> It is resolved
/// from a subscription this platform already owns, or from a checkout intent this
/// platform itself created. A provider reference that matches nothing resolves to
/// no workspace at all, rather than to a plausible one.
/// </para>
/// <para>
/// <strong>An event type this phase does not act on is a success.</strong>
/// Providers send far more than this platform needs, and answering the rest with
/// a failure would produce retries forever.
/// </para>
/// </remarks>
public sealed class ApplyProviderBillingEvent(
    IBillingProvider provider,
    IBillingEventInbox inbox,
    IWorkspaceSubscriptionStore subscriptions,
    IBillingCheckoutIntentStore intents,
    IBillingSubscriptionService lifecycle,
    TimeProvider clock)
{
    /// <summary>
    /// The event types this phase acts on.
    /// </summary>
    /// <remarks>
    /// Checkout completion is what creates a subscription. The subscription
    /// events cover every later transition — activation, trial end, plan change,
    /// arrears, cancellation — because each of them is answered by re-reading the
    /// subscription rather than by interpreting the event.
    /// </remarks>
    private static readonly HashSet<string> Actionable = new(StringComparer.Ordinal)
    {
        "checkout.session.completed",
        "checkout.session.async_payment_succeeded",
        "customer.subscription.created",
        "customer.subscription.updated",
        "customer.subscription.deleted",
        "customer.subscription.paused",
        "customer.subscription.resumed",
        "customer.subscription.trial_will_end",
        "invoice.paid",
        "invoice.payment_succeeded",
        "invoice.payment_failed",
    };

    private readonly IBillingProvider _provider =
        provider ?? throw new ArgumentNullException(nameof(provider));

    private readonly IBillingEventInbox _inbox =
        inbox ?? throw new ArgumentNullException(nameof(inbox));

    private readonly IWorkspaceSubscriptionStore _subscriptions =
        subscriptions ?? throw new ArgumentNullException(nameof(subscriptions));

    private readonly IBillingCheckoutIntentStore _intents =
        intents ?? throw new ArgumentNullException(nameof(intents));

    private readonly IBillingSubscriptionService _lifecycle =
        lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));

    private readonly TimeProvider _clock =
        clock ?? throw new ArgumentNullException(nameof(clock));

    public async Task HandleAsync(
        BillingEvent stored,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stored);

        if (!Actionable.Contains(stored.EventType))
        {
            await SettleAsync(stored, null, cancellationToken).ConfigureAwait(false);

            return;
        }

        string? refusal = stored.ProviderSessionId is { } sessionId
            ? await FromCheckoutAsync(stored, sessionId, cancellationToken).ConfigureAwait(false)
            : await FromSubscriptionAsync(stored, cancellationToken).ConfigureAwait(false);

        await SettleAsync(stored, refusal, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// A checkout this platform started, now completed.
    /// </summary>
    /// <remarks>
    /// <strong>The only path that may create a subscription.</strong> The intent
    /// is what ties a provider session to a workspace, and it exists because this
    /// platform wrote it before calling the provider. Reaching a success URL
    /// proves nothing and is not involved.
    /// </remarks>
    private async Task<string?> FromCheckoutAsync(
        BillingEvent stored,
        ProviderReference sessionId,
        CancellationToken cancellationToken)
    {
        BillingCheckoutIntent? intent = await _intents
            .FindByProviderSessionAsync(_provider.Provider, sessionId, cancellationToken)
            .ConfigureAwait(false);

        if (intent is null)
        {
            // A session this platform did not start. Nothing identifies a
            // workspace, and there is no safe guess to make.
            return "This checkout session does not belong to any workspace on this deployment.";
        }

        // Read from the provider rather than from the event: the event says a
        // session completed, and what came of it is the provider's to state.
        ProviderCheckoutResult? checkout = await _provider
            .GetCheckoutAsync(sessionId, cancellationToken)
            .ConfigureAwait(false);

        if (checkout?.SubscriptionId is not { } providerSubscriptionId || !checkout.IsComplete)
        {
            return "The provider does not report this checkout as a completed subscription.";
        }

        string? refusal = await ApplyAsync(
                stored,
                intent.TenantId,
                providerSubscriptionId,
                mayBegin: true,
                cancellationToken)
            .ConfigureAwait(false);

        if (refusal is null)
        {
            intent.Complete(_clock.GetUtcNow());

            await _intents.SaveAsync(intent, cancellationToken).ConfigureAwait(false);
        }

        return refusal;
    }

    /// <summary>A subscription this platform already knows about.</summary>
    private async Task<string?> FromSubscriptionAsync(
        BillingEvent stored,
        CancellationToken cancellationToken)
    {
        if (stored.ProviderSubscriptionId is not { } providerSubscriptionId)
        {
            // Actionable in principle, but naming nothing this platform can act
            // on. An invoice with no subscription is one example.
            return null;
        }

        WorkspaceSubscription? subscription = await _subscriptions
            .FindByProviderSubscriptionAsync(
                _provider.Provider,
                providerSubscriptionId,
                cancellationToken)
            .ConfigureAwait(false);

        return subscription is null
            ? "This subscription does not belong to any workspace on this deployment."
            : await ApplyAsync(
                    stored,
                    subscription.TenantId,
                    providerSubscriptionId,
                    mayBegin: false,
                    cancellationToken)
                .ConfigureAwait(false);
    }

    private async Task<string?> ApplyAsync(
        BillingEvent stored,
        TenantId tenantId,
        ProviderReference providerSubscriptionId,
        bool mayBegin,
        CancellationToken cancellationToken)
    {
        ProviderSubscriptionSnapshot? snapshot = await _provider
            .GetSubscriptionAsync(providerSubscriptionId, cancellationToken)
            .ConfigureAwait(false);

        if (snapshot is null)
        {
            return "The provider no longer has this subscription.";
        }

        BillingApplyOutcome outcome = await _lifecycle
            .ApplyAsync(tenantId, _provider.Provider, snapshot, mayBegin, stored, cancellationToken)
            .ConfigureAwait(false);

        return outcome switch
        {
            BillingApplyOutcome.Applied => null,

            // Recorded rather than guessed at. Somebody has to connect this price
            // to an offer; until they do, no plan is granted from it.
            BillingApplyOutcome.UnknownPrice =>
                "The provider is billing a price this platform does not sell, "
                + "so no plan could be confirmed from it.",
            _ => "This workspace has no subscription for this event to apply to.",
        };
    }

    /// <summary>
    /// Records what became of the event.
    /// </summary>
    /// <remarks>
    /// A refusal is terminal, not retried. Every refusal above is a statement
    /// about the world rather than a transient failure, and re-reading the same
    /// provider a thousand times would not change any of them. Transient failures
    /// throw instead, and the worker leaves the row claimed for another attempt.
    /// </remarks>
    private async Task SettleAsync(
        BillingEvent stored,
        string? refusal,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = _clock.GetUtcNow();

        if (refusal is null)
        {
            stored.Processed(now);
        }
        else
        {
            stored.Unprocessable(refusal, now);
        }

        await _inbox.SaveAsync(stored, cancellationToken).ConfigureAwait(false);
    }
}
