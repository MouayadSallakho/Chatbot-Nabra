using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OmniAI.Application.Billing;
using OmniAI.Application.Plans;
using OmniAI.Domain.Billing;
using OmniAI.Domain.Plans;
using OmniAI.Domain.Tenants;
using OmniAI.Infrastructure.Persistence;

namespace OmniAI.Infrastructure.Billing;

/// <summary>
/// Moves a subscription and the workspace's plan together.
/// </summary>
/// <remarks>
/// <para>
/// <strong>One transaction, under one lock per workspace.</strong> Two provider
/// events for the same workspace can arrive at once — a payment failure and a
/// subscription update, or a redelivery racing the original. Serialising them on
/// the workspace means the last one to run sees what the previous one wrote,
/// rather than both reading the same old state and one silently winning.
/// </para>
/// <para>
/// <strong>The plan assignment is written in the same transaction as the
/// subscription.</strong> Otherwise there is a window in which the subscription
/// says Active and Phase 24 still enforces the fallback — a workspace refused
/// something it is paying for, for as long as the gap lasts.
/// </para>
/// <para>
/// <strong>Nothing is ever deleted.</strong> A downgrade lowers what may be
/// created. Members, agents, documents and channels stay exactly where they are,
/// and Phase 24 refuses only new creation while usage sits above the new limit.
/// </para>
/// </remarks>
internal sealed class BillingSubscriptionService(
    OmniAiDbContext context,
    IBillingCatalog catalog,
    FallbackPlanResolver fallback,
    BillingOptions options,
    TimeProvider clock) : IBillingSubscriptionService
{
    private readonly OmniAiDbContext _context =
        context ?? throw new ArgumentNullException(nameof(context));

    private readonly IBillingCatalog _catalog =
        catalog ?? throw new ArgumentNullException(nameof(catalog));

    private readonly FallbackPlanResolver _fallback =
        fallback ?? throw new ArgumentNullException(nameof(fallback));

    private readonly BillingOptions _options =
        options ?? throw new ArgumentNullException(nameof(options));

    private readonly TimeProvider _clock =
        clock ?? throw new ArgumentNullException(nameof(clock));

    public async Task<BillingApplyOutcome> ApplyAsync(
        TenantId tenantId,
        BillingProvider provider,
        ProviderSubscriptionSnapshot snapshot,
        bool mayBegin = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        await using IDbContextTransaction transaction = await _context.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        await LockAsync(tenantId, cancellationToken).ConfigureAwait(false);

        WorkspaceSubscription? subscription = await _context.WorkspaceSubscriptions
            .FirstOrDefaultAsync(
                candidate => candidate.TenantId == tenantId
                    && candidate.Status != SubscriptionStatus.Canceled,
                cancellationToken)
            .ConfigureAwait(false);

        if (subscription is null && !mayBegin)
        {
            // An event about a workspace with no subscription. Nothing to move,
            // and inventing one from a provider reference would let anybody who
            // can name a subscription grant a plan.
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);

            return BillingApplyOutcome.NoSubscription;
        }

        // What the provider is billing must map to something this platform sells.
        // An unknown price grants nothing: guessing a plan from it, or trusting
        // provider metadata to name one, would make entitlements settable by
        // anybody who can create a price.
        BillingPlanOffer? offer = await _catalog
            .OfferForPriceAsync(snapshot.PriceId, provider, cancellationToken)
            .ConfigureAwait(false);

        if (subscription is null)
        {
            if (offer is null)
            {
                // A completed checkout billing something this platform does not
                // sell. Creating a subscription would mean choosing a plan with
                // nothing to choose it from, so nothing is created at all.
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);

                return BillingApplyOutcome.UnknownPrice;
            }

            subscription = WorkspaceSubscription.Begin(
                tenantId,
                provider,
                offer.Id,
                offer.PlanId,
                offer.Interval,
                _clock.GetUtcNow());

            _context.WorkspaceSubscriptions.Add(subscription);
        }
        else if (offer is not null && offer.Id != subscription.OfferId)
        {
            // The customer changed what they buy in the provider's portal.
            subscription.MoveTo(offer.Id, offer.PlanId, offer.Interval, _clock.GetUtcNow());
        }

        subscription.Apply(
            snapshot.Status,
            snapshot.RawStatus,
            snapshot.SubscriptionId,
            snapshot.CurrentPeriodStart,
            snapshot.CurrentPeriodEnd,
            snapshot.TrialEndsAt,
            snapshot.CancelAtPeriodEnd,
            snapshot.CanceledAt,
            _options.PastDueGracePeriod,
            _clock.GetUtcNow());

        await AssignAsync(tenantId, subscription, cancellationToken).ConfigureAwait(false);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        // The lifecycle was applied either way. An unknown price only means the
        // plan could not be confirmed from it -- which is worth a human's
        // attention, and is never worth guessing at.
        return offer is null ? BillingApplyOutcome.UnknownPrice : BillingApplyOutcome.Applied;
    }

    public async Task<bool> SuspendAsync(
        TenantId tenantId,
        CancellationToken cancellationToken = default)
    {
        await using IDbContextTransaction transaction = await _context.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        await LockAsync(tenantId, cancellationToken).ConfigureAwait(false);

        WorkspaceSubscription? subscription = await _context.WorkspaceSubscriptions
            .FirstOrDefaultAsync(
                candidate => candidate.TenantId == tenantId
                    && candidate.Status == SubscriptionStatus.PastDue,
                cancellationToken)
            .ConfigureAwait(false);

        // Re-checked under the lock. The claim that selected this workspace was
        // taken outside it, and a payment may have succeeded since.
        if (subscription is null || !subscription.Suspend(_clock.GetUtcNow()))
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);

            return false;
        }

        await AssignAsync(tenantId, subscription, cancellationToken).ConfigureAwait(false);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return true;
    }

    /// <summary>
    /// Points the workspace's plan assignment at whatever its subscription now warrants.
    /// </summary>
    /// <remarks>
    /// The single place subscription state becomes entitlement. Trialing, Active
    /// and PastDue-within-grace keep the subscribed plan; everything else falls
    /// back to the configured commercial plan.
    /// </remarks>
    private async Task AssignAsync(
        TenantId tenantId,
        WorkspaceSubscription subscription,
        CancellationToken cancellationToken)
    {
        PlanId planId = subscription.GrantsPlan
            ? subscription.PlanId
            : (await _fallback.ResolveAsync(cancellationToken).ConfigureAwait(false)).Id;

        WorkspacePlanAssignment? assignment = await _context.WorkspacePlanAssignments
            .FirstOrDefaultAsync(candidate => candidate.TenantId == tenantId, cancellationToken)
            .ConfigureAwait(false);

        if (assignment is not null && assignment.PlanId == planId)
        {
            return;
        }

        // The assignment is keyed by workspace, so moving plan means replacing
        // the row rather than accumulating history nobody reads.
        if (assignment is not null)
        {
            _context.WorkspacePlanAssignments.Remove(assignment);

            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        _context.WorkspacePlanAssignments.Add(
            WorkspacePlanAssignment.Create(tenantId, planId, _clock.GetUtcNow()));
    }

    /// <summary>
    /// Serialises billing writes for one workspace.
    /// </summary>
    /// <remarks>
    /// Transaction-scoped, so it is released by the commit or the rollback and
    /// cannot leak. Its own namespace, so it never collides with the entitlement
    /// lock a plan reservation takes.
    /// </remarks>
    private async Task LockAsync(TenantId tenantId, CancellationToken cancellationToken) =>
        await _context.Database
            .ExecuteSqlRawAsync(
                "SELECT pg_advisory_xact_lock({0})",
                [KeyFor(tenantId)],
                cancellationToken)
            .ConfigureAwait(false);

    private static long KeyFor(TenantId tenantId)
    {
        Span<byte> digest = stackalloc byte[32];

        SHA256.HashData(
            Encoding.UTF8.GetBytes(string.Create(
                CultureInfo.InvariantCulture,
                $"billing:{tenantId.Value:D}")),
            digest);

        return BitConverter.ToInt64(digest[..8]);
    }
}
