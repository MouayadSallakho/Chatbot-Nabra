using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OmniAI.Application.Billing;
using OmniAI.Application.Billing.Persistence;
using OmniAI.Domain.Billing;
using OmniAI.Domain.Notifications;
using OmniAI.Domain.Plans;
using OmniAI.Domain.Tenants;
using OmniAI.Infrastructure.Billing;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Infrastructure.Plans;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Billing;

/// <summary>
/// Billing-event claim ownership across worker instances, against a real
/// PostgreSQL (Phase 35).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Each "worker" is its own DbContext</strong>, which is exactly what
/// separates two API instances as far as the database can tell: separate
/// connections, separate tracked state, nothing shared but the rows.
/// </para>
/// <para>
/// <strong>Interleavings are forced, not raced.</strong> Every step is awaited in
/// the order the scenario needs, and time is a value passed to the claim, so each
/// outcome is deterministic rather than a probability.
/// </para>
/// <para>
/// The provider is an in-process fake that answers one fixed snapshot. Phase 34
/// already showed the snapshot-driven apply is idempotent; what is proved here is
/// who is allowed to write.
/// </para>
/// </remarks>
public sealed class BillingEventLeaseTests(PostgresTestDatabase database)
    : IClassFixture<PostgresTestDatabase>
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(15);

    private const int Batch = 500;

    private const int MaxAttempts = 8;

    private readonly FixedTimeProvider _clock =
        new(new DateTimeOffset(2026, 9, 27, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task A_second_worker_cannot_take_an_event_whose_lease_is_still_running()
    {
        Fixture fixture = await ArrangeAsync("lease-active");
        BillingEventId id = await ReceiveAsync(fixture, "evt_lease_active");

        await using OmniAiDbContext a = database.CreateContext();
        await using OmniAiDbContext b = database.CreateContext();

        DateTimeOffset now = _clock.GetUtcNow();

        Assert.Contains(await ClaimAsync(a, now), claimed => claimed.Id == id);

        // One minute, then one second short of the lease: still A's.
        Assert.DoesNotContain(await ClaimAsync(b, now.AddMinutes(1)), claimed => claimed.Id == id);
        Assert.DoesNotContain(await ClaimAsync(b, now + Lease - TimeSpan.FromSeconds(1)), claimed => claimed.Id == id);
    }

    /// <remarks>
    /// The whole scenario the Phase 34 review asked for, in one sequence: A
    /// claims, the lease lapses, B reclaims, A (still alive, merely slow) tries to
    /// finish, B finishes. The subscription moves once, the event is B's result.
    /// </remarks>
    [Fact]
    public async Task A_worker_whose_lease_lapsed_changes_nothing_and_the_new_owner_applies_once()
    {
        Fixture fixture = await ArrangeAsync("lease-takeover");
        BillingEventId id = await ReceiveAsync(fixture, "evt_lease_takeover");

        await using OmniAiDbContext a = database.CreateContext();
        await using OmniAiDbContext b = database.CreateContext();

        DateTimeOffset claimedAt = _clock.GetUtcNow();

        BillingEvent heldByA = Assert.Single(await ClaimAsync(a, claimedAt), claimed => claimed.Id == id);

        // A stalls past its lease. B reclaims with a new token.
        BillingEvent heldByB = Assert.Single(
            await ClaimAsync(b, claimedAt + Lease + TimeSpan.FromSeconds(1)),
            claimed => claimed.Id == id);

        Assert.NotEqual(TokenOf(a, heldByA), TokenOf(b, heldByB));
        Assert.Equal(2, heldByB.Attempts);

        // A wakes up and tries to apply: refused inside the subscription
        // transaction, before anything is written.
        await Assert.ThrowsAsync<BillingEventOwnershipLostException>(
            () => HandlerFor(a, fixture).HandleAsync(heldByA));

        Assert.Equal(SubscriptionStatus.Active, await SubscriptionStatusAsync(fixture.TenantId));
        Assert.Equal(0, await OccurrencesAsync(fixture.TenantId, NotificationKind.BillingPastDue));

        // B applies legitimately.
        await HandlerFor(b, fixture).HandleAsync(heldByB);

        Assert.Equal(SubscriptionStatus.PastDue, await SubscriptionStatusAsync(fixture.TenantId));
        Assert.Equal(1, await OccurrencesAsync(fixture.TenantId, NotificationKind.BillingPastDue));

        BillingEvent settled = await EventAsync(id);

        Assert.Equal(BillingEventStatus.Processed, settled.Status);
        Assert.Null(await StoredTokenAsync(id));
    }

    /// <remarks>
    /// The settlement write on its own: even a stale owner that skipped the
    /// subscription step entirely (an event type this platform ignores) cannot
    /// mark the event done, or overwrite the result the new owner wrote.
    /// </remarks>
    [Fact]
    public async Task A_stale_owner_cannot_settle_or_overwrite_the_new_owners_result()
    {
        Fixture fixture = await ArrangeAsync("lease-settle");
        BillingEventId id = await ReceiveAsync(fixture, "evt_lease_settle");

        await using OmniAiDbContext a = database.CreateContext();
        await using OmniAiDbContext b = database.CreateContext();

        DateTimeOffset claimedAt = _clock.GetUtcNow();

        BillingEvent heldByA = Assert.Single(await ClaimAsync(a, claimedAt), claimed => claimed.Id == id);
        BillingEvent heldByB = Assert.Single(await ClaimAsync(b, claimedAt + Lease + TimeSpan.FromSeconds(1)), claimed => claimed.Id == id);

        // While B still holds it.
        heldByA.Unprocessable("stale owner's verdict", _clock.GetUtcNow());

        await Assert.ThrowsAsync<BillingEventOwnershipLostException>(
            () => new BillingEventInbox(a).SaveAsync(heldByA));

        Assert.Equal(BillingEventStatus.Claimed, (await EventAsync(id)).Status);
        Assert.Equal(TokenOf(b, heldByB), await StoredTokenAsync(id));

        // After B settled.
        heldByB.Processed(_clock.GetUtcNow());
        await new BillingEventInbox(b).SaveAsync(heldByB);

        // And A, trying once more after B's result is in, still cannot touch it.
        await using OmniAiDbContext late = database.CreateContext();

        Assert.DoesNotContain(
            await ClaimAsync(late, claimedAt + Lease + Lease + TimeSpan.FromMinutes(1)),
            claimed => claimed.Id == id);

        heldByA.Unprocessable("stale owner's verdict, again", _clock.GetUtcNow());

        await Assert.ThrowsAsync<BillingEventOwnershipLostException>(
            () => new BillingEventInbox(a).SaveAsync(heldByA));

        BillingEvent final = await EventAsync(id);

        Assert.Equal(BillingEventStatus.Processed, final.Status);
        Assert.Null(final.Failure);
    }

    /// <remarks>
    /// The fence holds the row until the subscription commits, so a reclaim that
    /// arrives at the worst moment — lease lapsed, apply in flight — skips the
    /// event instead of taking it out from under a write that is already happening.
    /// </remarks>
    [Fact]
    public async Task A_reclaim_cannot_land_while_the_owner_is_inside_its_fenced_write()
    {
        Fixture fixture = await ArrangeAsync("lease-fence");
        BillingEventId id = await ReceiveAsync(fixture, "evt_lease_fence");

        await using OmniAiDbContext a = database.CreateContext();
        await using OmniAiDbContext b = database.CreateContext();

        DateTimeOffset claimedAt = _clock.GetUtcNow();

        BillingEvent heldByA = Assert.Single(await ClaimAsync(a, claimedAt), claimed => claimed.Id == id);

        await using (IDbContextTransaction write = await a.Database.BeginTransactionAsync())
        {
            await BillingEventOwnership.FenceAsync(a, heldByA, CancellationToken.None);

            // Past the lease, but the row is locked by a write in progress.
            Assert.DoesNotContain(
                await ClaimAsync(b, claimedAt + Lease + TimeSpan.FromMinutes(1)),
                claimed => claimed.Id == id);

            await write.CommitAsync();
        }

        await using OmniAiDbContext c = database.CreateContext();

        Assert.Contains(
            await ClaimAsync(c, claimedAt + Lease + TimeSpan.FromMinutes(1)),
            claimed => claimed.Id == id);
    }

    [Fact]
    public async Task An_event_handed_back_after_a_transient_failure_is_retried_without_waiting_out_the_lease()
    {
        Fixture fixture = await ArrangeAsync("lease-release");
        BillingEventId id = await ReceiveAsync(fixture, "evt_lease_release");

        await using OmniAiDbContext a = database.CreateContext();
        await using OmniAiDbContext b = database.CreateContext();

        DateTimeOffset claimedAt = _clock.GetUtcNow();

        BillingEvent held = Assert.Single(await ClaimAsync(a, claimedAt), claimed => claimed.Id == id);

        held.Release("Transient failure (TimeoutException); will be retried.");
        await new BillingEventInbox(a).SaveAsync(held);

        BillingEvent retried = Assert.Single(await ClaimAsync(b, claimedAt.AddSeconds(5)), claimed => claimed.Id == id);

        // Counted once per claim, not once more for the hand-back.
        Assert.Equal(2, retried.Attempts);
    }

    /// <remarks>
    /// Idempotency is unchanged by the lease: a redelivered provider event is not
    /// stored again, and a second event re-reading the same provider state moves
    /// nothing and notifies nobody a second time.
    /// </remarks>
    [Fact]
    public async Task A_replayed_provider_event_stays_idempotent()
    {
        Fixture fixture = await ArrangeAsync("lease-replay");
        BillingEventId id = await ReceiveAsync(fixture, "evt_lease_replay");

        await using (OmniAiDbContext a = database.CreateContext())
        {
            BillingEvent held = Assert.Single(await ClaimAsync(a, _clock.GetUtcNow()), claimed => claimed.Id == id);

            await HandlerFor(a, fixture).HandleAsync(held);
        }

        await using (OmniAiDbContext redelivery = database.CreateContext())
        {
            Assert.False(await new BillingEventInbox(redelivery).ReceiveAsync(Event(fixture, "evt_lease_replay")));
        }

        BillingEventId second = await ReceiveAsync(fixture, "evt_lease_replay_followup");

        await using (OmniAiDbContext b = database.CreateContext())
        {
            BillingEvent held = Assert.Single(await ClaimAsync(b, _clock.GetUtcNow()), claimed => claimed.Id == second);

            await HandlerFor(b, fixture).HandleAsync(held);
        }

        await using OmniAiDbContext check = database.CreateContext();

        Assert.Equal(1, await check.BillingEvents.CountAsync(
            stored => stored.ProviderEventId == ProviderReference.Create("evt_lease_replay")));
        Assert.Equal(1, await check.WorkspaceSubscriptions.CountAsync(
            subscription => subscription.TenantId == fixture.TenantId));
        Assert.Equal(SubscriptionStatus.PastDue, await SubscriptionStatusAsync(fixture.TenantId));
        Assert.Equal(1, await OccurrencesAsync(fixture.TenantId, NotificationKind.BillingPastDue));
    }

    [Fact]
    public void The_lease_cannot_be_configured_shorter_than_the_slowest_legitimate_processing()
    {
        BillingWorkerOptions tooShort = new() { ClaimLease = BillingWorkerOptions.MinimumClaimLease - TimeSpan.FromSeconds(1) };

        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(tooShort.Validate);

        Assert.Contains("Billing:Workers:ClaimLease", refused.Message, StringComparison.Ordinal);

        new BillingWorkerOptions().Validate();
        new BillingWorkerOptions { ClaimLease = BillingWorkerOptions.MinimumClaimLease }.Validate();
    }

    private static async Task<IReadOnlyList<BillingEvent>> ClaimAsync(OmniAiDbContext context, DateTimeOffset now) =>
        await new BillingEventInbox(context).ClaimAsync(Batch, MaxAttempts, now, Lease);

    private static Guid? TokenOf(OmniAiDbContext context, BillingEvent billingEvent) =>
        BillingEventOwnership.TokenOf(context, billingEvent);

    private async Task<BillingEventId> ReceiveAsync(Fixture fixture, string providerEventId)
    {
        await using OmniAiDbContext context = database.CreateContext();

        BillingEvent billingEvent = Event(fixture, providerEventId);

        Assert.True(await new BillingEventInbox(context).ReceiveAsync(billingEvent));

        return billingEvent.Id;
    }

    private BillingEvent Event(Fixture fixture, string providerEventId)
    {
        BillingEvent billingEvent = BillingEvent.Receive(
            BillingProvider.Stripe,
            ProviderReference.Create(providerEventId),
            "customer.subscription.updated",
            _clock.GetUtcNow(),
            _clock.GetUtcNow());

        billingEvent.Concerns(fixture.SubscriptionId, fixture.CustomerId, sessionId: null);

        return billingEvent;
    }

    private async Task<BillingEvent> EventAsync(BillingEventId id)
    {
        await using OmniAiDbContext context = database.CreateContext();

        return await context.BillingEvents.AsNoTracking().SingleAsync(stored => stored.Id == id);
    }

    private async Task<Guid?> StoredTokenAsync(BillingEventId id)
    {
        await using OmniAiDbContext context = database.CreateContext();

        BillingEvent stored = await context.BillingEvents.SingleAsync(candidate => candidate.Id == id);

        return BillingEventOwnership.TokenOf(context, stored);
    }

    private async Task<SubscriptionStatus> SubscriptionStatusAsync(TenantId tenantId)
    {
        await using OmniAiDbContext context = database.CreateContext();

        return await context.WorkspaceSubscriptions
            .Where(subscription => subscription.TenantId == tenantId)
            .Select(subscription => subscription.Status)
            .SingleAsync();
    }

    private async Task<int> OccurrencesAsync(TenantId tenantId, NotificationKind kind)
    {
        await using OmniAiDbContext context = database.CreateContext();

        return await context.NotificationOccurrences
            .CountAsync(occurrence => occurrence.TenantId == tenantId && occurrence.Kind == kind);
    }

    private ApplyProviderBillingEvent HandlerFor(OmniAiDbContext context, Fixture fixture) =>
        new(
            new SnapshotProvider(Snapshot(fixture, SubscriptionStatus.PastDue, "past_due")),
            new BillingEventInbox(context),
            new WorkspaceSubscriptionStore(context),
            new BillingCheckoutIntentStore(context),
            LifecycleFor(context),
            _clock);

    private BillingSubscriptionService LifecycleFor(OmniAiDbContext context)
    {
        BillingOptions options = new()
        {
            FallbackPlanCode = "free",
            ReturnBaseUrl = "https://app.test.local",
            PastDueGracePeriod = TimeSpan.FromDays(7),
        };

        return new BillingSubscriptionService(
            context,
            new BillingCatalog(context),
            new FallbackPlanResolver(new PlanCatalog(context), options),
            options,
            _clock);
    }

    private ProviderSubscriptionSnapshot Snapshot(Fixture fixture, SubscriptionStatus status, string rawStatus) =>
        new(
            fixture.SubscriptionId,
            fixture.CustomerId,
            fixture.PriceId,
            status,
            rawStatus,
            _clock.GetUtcNow().AddDays(-1),
            _clock.GetUtcNow().AddDays(29),
            TrialEndsAt: null,
            CancelAtPeriodEnd: false,
            CanceledAt: null);

    /// <summary>
    /// A workspace with an Active subscription on a mapped price, so an event
    /// about it takes the "known subscription" path.
    /// </summary>
    private async Task<Fixture> ArrangeAsync(string slug)
    {
        Fixture fixture;

        await using (OmniAiDbContext context = database.CreateContext())
        {
            Plan starter = await context.Plans.SingleAsync(plan => plan.Code == PlanCode.Create("starter"));
            Plan development = await context.Plans.SingleAsync(plan => plan.Code == PlanCode.Create("development"));

            Tenant tenant = Tenant.Create(TenantName.Create($"Workspace {slug}"), TenantSlug.Create($"bl-{slug}"));

            context.Tenants.Add(tenant);
            context.WorkspacePlanAssignments.Add(
                WorkspacePlanAssignment.Create(tenant.Id, development.Id, _clock.GetUtcNow()));

            BillingPlanOffer offer = BillingPlanOffer.Create(
                starter.Id,
                OfferCode.Create($"bl-{slug}-monthly"),
                BillingInterval.Monthly,
                Money.Of(2900, BillingCurrency.Create("USD")),
                trialDays: null,
                _clock.GetUtcNow());

            ProviderReference priceId = ProviderReference.Create($"price_bl_{slug}");

            context.BillingPlanOffers.Add(offer);
            context.BillingProviderPriceMappings.Add(
                BillingProviderPriceMapping.Create(offer.Id, BillingProvider.Stripe, priceId, _clock.GetUtcNow()));

            await context.SaveChangesAsync();

            fixture = new Fixture(
                tenant.Id,
                priceId,
                ProviderReference.Create($"sub_bl_{slug}"),
                ProviderReference.Create($"cus_bl_{slug}"));
        }

        await using (OmniAiDbContext context = database.CreateContext())
        {
            Assert.Equal(
                BillingApplyOutcome.Applied,
                await LifecycleFor(context).ApplyAsync(
                    fixture.TenantId,
                    BillingProvider.Stripe,
                    Snapshot(fixture, SubscriptionStatus.Active, "active"),
                    mayBegin: true));
        }

        return fixture;
    }

    private sealed record Fixture(
        TenantId TenantId,
        ProviderReference PriceId,
        ProviderReference SubscriptionId,
        ProviderReference CustomerId);

    /// <summary>A provider that reports one fixed subscription state.</summary>
    private sealed class SnapshotProvider(ProviderSubscriptionSnapshot snapshot) : IBillingProvider
    {
        public BillingProvider Provider => BillingProvider.Stripe;

        public Task<ProviderSubscriptionSnapshot?> GetSubscriptionAsync(
            ProviderReference subscriptionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ProviderSubscriptionSnapshot?>(snapshot);

        public Task<ProviderCheckoutResult?> GetCheckoutAsync(
            ProviderReference sessionId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not used by these tests.");

        public Task<ProviderReference> CreateCustomerAsync(
            string workspaceName,
            Guid tenantId,
            string idempotencyKey,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not used by these tests.");

        public Task<ProviderCheckoutSession> CreateSubscriptionCheckoutAsync(
            ProviderCheckoutRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not used by these tests.");

        public Task<string> CreatePortalSessionAsync(
            ProviderReference customerId,
            string returnUrl,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not used by these tests.");
    }
}
