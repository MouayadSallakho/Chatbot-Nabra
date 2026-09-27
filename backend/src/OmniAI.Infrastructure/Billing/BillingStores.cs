using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using OmniAI.Application.Billing;
using OmniAI.Application.Billing.Persistence;
using OmniAI.Domain.Billing;
using OmniAI.Domain.Tenants;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Infrastructure.Persistence.Configurations;

namespace OmniAI.Infrastructure.Billing;

/// <summary>
/// What this platform sells, read from the database.
/// </summary>
/// <remarks>
/// <strong>The reason a browser never names a provider price.</strong> A purchase
/// names an internal offer; this is the only place that becomes something a
/// provider recognises. A caller who could supply the price directly could buy a
/// plan nobody put on sale.
/// </remarks>
internal sealed class BillingCatalog(OmniAiDbContext context) : IBillingCatalog
{
    private readonly OmniAiDbContext _context =
        context ?? throw new ArgumentNullException(nameof(context));

    public async Task<IReadOnlyList<BillingPlanOffer>> ActiveOffersAsync(
        CancellationToken cancellationToken = default) =>
        await _context.BillingPlanOffers
            .AsNoTracking()
            .Where(offer => offer.IsActive)
            .OrderBy(offer => offer.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    // Withdrawn offers are still readable. A workspace that bought one keeps a
    // subscription pointing at it, and a billing page that could not name what
    // somebody is paying for would be worse than useless.
    public async Task<BillingPlanOffer?> FindOfferAsync(
        BillingOfferId offerId,
        CancellationToken cancellationToken = default) =>
        await _context.BillingPlanOffers
            .AsNoTracking()
            .FirstOrDefaultAsync(offer => offer.Id == offerId, cancellationToken)
            .ConfigureAwait(false);

    public async Task<ProviderReference?> PriceForAsync(
        BillingOfferId offerId,
        BillingProvider provider,
        CancellationToken cancellationToken = default)
    {
        BillingProviderPriceMapping? mapping = await _context.BillingProviderPriceMappings
            .AsNoTracking()
            .FirstOrDefaultAsync(
                candidate => candidate.OfferId == offerId && candidate.Provider == provider,
                cancellationToken)
            .ConfigureAwait(false);

        return mapping?.ExternalPriceId;
    }

    public async Task<BillingPlanOffer?> OfferForPriceAsync(
        ProviderReference priceId,
        BillingProvider provider,
        CancellationToken cancellationToken = default)
    {
        BillingOfferId? offerId = await _context.BillingProviderPriceMappings
            .AsNoTracking()
            .Where(mapping =>
                mapping.Provider == provider && mapping.ExternalPriceId == priceId)
            .Select(mapping => (BillingOfferId?)mapping.OfferId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        // No mapping means this platform does not sell what the provider is
        // billing. The caller treats that as an anomaly; it must not become a
        // guess.
        return offerId is null
            ? null
            : await FindOfferAsync(offerId.Value, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>A workspace's subscription, read from the database.</summary>
internal sealed class WorkspaceSubscriptionStore(OmniAiDbContext context)
    : IWorkspaceSubscriptionStore
{
    private readonly OmniAiDbContext _context =
        context ?? throw new ArgumentNullException(nameof(context));

    public async Task<WorkspaceSubscription?> FindCurrentAsync(
        TenantId tenantId,
        CancellationToken cancellationToken = default) =>
        await _context.WorkspaceSubscriptions
            .AsNoTracking()
            .FirstOrDefaultAsync(
                subscription => subscription.TenantId == tenantId
                    && subscription.Status != SubscriptionStatus.Canceled,
                cancellationToken)
            .ConfigureAwait(false);

    public async Task<WorkspaceSubscription?> FindByProviderSubscriptionAsync(
        BillingProvider provider,
        ProviderReference providerSubscriptionId,
        CancellationToken cancellationToken = default) =>
        await _context.WorkspaceSubscriptions
            .AsNoTracking()
            .FirstOrDefaultAsync(
                subscription => subscription.Provider == provider
                    && subscription.ProviderSubscriptionId == providerSubscriptionId,
                cancellationToken)
            .ConfigureAwait(false);
}

/// <summary>A workspace's provider customer record, read and created.</summary>
internal sealed class WorkspaceBillingCustomerStore(OmniAiDbContext context)
    : IWorkspaceBillingCustomerStore
{
    private readonly OmniAiDbContext _context =
        context ?? throw new ArgumentNullException(nameof(context));

    public async Task<WorkspaceBillingCustomer?> FindAsync(
        TenantId tenantId,
        BillingProvider provider,
        CancellationToken cancellationToken = default) =>
        await _context.WorkspaceBillingCustomers
            .AsNoTracking()
            .FirstOrDefaultAsync(
                customer => customer.TenantId == tenantId && customer.Provider == provider,
                cancellationToken)
            .ConfigureAwait(false);

    public async Task<WorkspaceBillingCustomer> GetOrAddAsync(
        WorkspaceBillingCustomer customer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(customer);

        _context.WorkspaceBillingCustomers.Add(customer);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return customer;
        }
        catch (DbUpdateException failure) when (Violates(failure, TenantKey))
        {
            // Two first purchases at once. The key decided; the loser reads what
            // the winner wrote rather than leaving the workspace with two
            // provider customers and two places a card could be charged.
            _context.Entry(customer).State = EntityState.Detached;

            return await FindAsync(customer.TenantId, customer.Provider, cancellationToken)
                    .ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    "A billing customer conflicted but could not be read back.");
        }
        catch (DbUpdateException failure) when (Violates(failure, CustomerIndex))
        {
            // A different collision entirely: this provider customer already
            // belongs to another workspace. Treating it as the race above would
            // read back nothing and report a confusing "could not be read back",
            // hiding the fact that a provider handed out a reference somebody
            // else holds. Said plainly instead, and nothing is written.
            _context.Entry(customer).State = EntityState.Detached;

            throw new BillingProviderUnavailableException(
                "The payment provider returned a customer reference that already "
                + "belongs to another workspace.",
                failure);
        }
    }

    /// <summary>The composite primary key: one customer per workspace per provider.</summary>
    private const string TenantKey = "PK_workspace_billing_customers";

    /// <summary>One provider customer belongs to one workspace.</summary>
    private const string CustomerIndex = "ux_workspace_billing_customers_provider_customer";

    /// <summary>
    /// Whether this failure is that one named constraint.
    /// </summary>
    /// <remarks>
    /// Named rather than "any unique violation", because two different
    /// constraints on one table mean two different situations, and only one of
    /// them is a race worth recovering from silently.
    /// </remarks>
    private static bool Violates(DbUpdateException failure, string constraint) =>
        failure.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
        } violation
        && string.Equals(violation.ConstraintName, constraint, StringComparison.Ordinal);
}

/// <summary>Checkout attempts, claimed before any provider is called.</summary>
internal sealed class BillingCheckoutIntentStore(OmniAiDbContext context)
    : IBillingCheckoutIntentStore
{
    private readonly OmniAiDbContext _context =
        context ?? throw new ArgumentNullException(nameof(context));

    public async Task<(BillingCheckoutIntent Intent, bool IsNew)> ClaimAsync(
        BillingCheckoutIntent intent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);

        _context.BillingCheckoutIntents.Add(intent);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return (intent, true);
        }
        catch (DbUpdateException failure) when (IsDuplicateAttempt(failure))
        {
            // The double click, or the same click reaching two web servers. The
            // unique key is what makes this one checkout rather than two
            // subscriptions somebody has to be refunded for.
            _context.Entry(intent).State = EntityState.Detached;

            BillingCheckoutIntent existing = await _context.BillingCheckoutIntents
                    .FirstOrDefaultAsync(
                        candidate => candidate.IdempotencyKey == intent.IdempotencyKey,
                        cancellationToken)
                    .ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    "A checkout attempt conflicted but could not be read back.");

            return (existing, false);
        }
    }

    public async Task<BillingCheckoutIntent?> FindAsync(
        BillingCheckoutIntentId id,
        CancellationToken cancellationToken = default) =>
        await _context.BillingCheckoutIntents
            .FirstOrDefaultAsync(intent => intent.Id == id, cancellationToken)
            .ConfigureAwait(false);

    public async Task<BillingCheckoutIntent?> FindByProviderSessionAsync(
        BillingProvider provider,
        ProviderReference sessionId,
        CancellationToken cancellationToken = default) =>
        await _context.BillingCheckoutIntents
            .FirstOrDefaultAsync(
                intent => intent.Provider == provider && intent.ProviderSessionId == sessionId,
                cancellationToken)
            .ConfigureAwait(false);

    public async Task SaveAsync(
        BillingCheckoutIntent intent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);

        if (_context.Entry(intent).State == EntityState.Detached)
        {
            _context.BillingCheckoutIntents.Attach(intent);
            _context.Entry(intent).State = EntityState.Modified;
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The idempotency key: one attempt per workspace, offer and hour.</summary>
    private const string IdempotencyIndex = "ux_billing_checkout_intents_idempotency";

    /// <summary>
    /// Whether this is the duplicate click, rather than some other collision.
    /// </summary>
    /// <remarks>
    /// Named, because the same table also has a unique provider session index. A
    /// blanket catch would answer a violation of that one by reading back an
    /// intent that does not match, or by reporting a failure that says the wrong
    /// thing.
    /// </remarks>
    private static bool IsDuplicateAttempt(DbUpdateException failure) =>
        failure.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
        } violation
        && string.Equals(violation.ConstraintName, IdempotencyIndex, StringComparison.Ordinal);
}

/// <summary>
/// Who currently owns a claimed billing event (Phase 35).
/// </summary>
/// <remarks>
/// <para>
/// <strong>The claim token is the ownership.</strong> Every claim writes a new
/// one; a worker holds the token it was granted, read from the row it claimed.
/// Everything that acts for the event proves it still holds that token, in the
/// database, at the moment it writes — never from memory.
/// </para>
/// <para>
/// Two checks, because the event causes two writes. The subscription change is
/// fenced by <see cref="FenceAsync"/> inside its own transaction; the settlement
/// is fenced by the token being the row's concurrency token.
/// </para>
/// </remarks>
internal static class BillingEventOwnership
{
    /// <summary>
    /// The claim token a tracked event was loaded with.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The event is not tracked by this context, so there is no claim to prove.
    /// </exception>
    public static Guid? TokenOf(OmniAiDbContext context, BillingEvent billingEvent)
    {
        EntityEntry<BillingEvent> entry = context.Entry(billingEvent);

        if (entry.State == EntityState.Detached)
        {
            throw new InvalidOperationException(
                "A billing event must be loaded by the same unit of work that acts on it.");
        }

        return entry.Property<Guid?>(BillingEventClaim.TokenProperty).OriginalValue;
    }

    /// <summary>
    /// Proves, inside the caller's transaction, that the event is still this
    /// worker's — and keeps it so until that transaction ends.
    /// </summary>
    /// <remarks>
    /// <c>FOR UPDATE</c> on the event row: a reclaim is an UPDATE of that row, so
    /// it cannot land between this check and the caller's commit (the claim skips
    /// locked rows, so it simply does not take this one yet). The caller must
    /// already have a transaction open, or the lock would end with this statement.
    /// </remarks>
    /// <exception cref="BillingEventOwnershipLostException">
    /// The event has been claimed again since this worker took it.
    /// </exception>
    public static async Task FenceAsync(
        OmniAiDbContext context,
        BillingEvent billingEvent,
        CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "The ownership fence must run inside the transaction it protects.");
        }

        Guid? token = TokenOf(context, billingEvent);

        List<int> owned = token is { } held
            ? await context.Database
                .SqlQueryRaw<int>(
                    $$"""
                     SELECT 1 AS "Value"
                       FROM {{PersistenceSchema.BillingEventsTable}}
                      WHERE id = {0} AND status = 'Claimed' AND claim_token = {1}
                        FOR UPDATE
                     """,
                    billingEvent.Id.Value,
                    held)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false)
            : [];

        if (owned.Count == 0)
        {
            throw new BillingEventOwnershipLostException();
        }
    }
}

/// <summary>
/// The durable inbox of verified provider events.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The unique provider event id is the whole of duplicate
/// protection.</strong> Providers redeliver, sometimes concurrently. An
/// in-memory guard would be per-process and per-restart; a unique index is
/// neither, so the second copy loses its insert and causes no second transition.
/// </para>
/// <para>
/// <strong>Nothing raw is stored.</strong> The signature was verified against the
/// exact bytes received and those bytes are then discarded. What remains is the
/// handful of normalised fields the platform acts on, so a webhook cannot become
/// a way to accumulate payment data this product never wanted to hold.
/// </para>
/// </remarks>
internal sealed class BillingEventInbox(OmniAiDbContext context) : IBillingEventInbox
{
    private readonly OmniAiDbContext _context =
        context ?? throw new ArgumentNullException(nameof(context));

    public async Task<bool> ReceiveAsync(
        BillingEvent billingEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(billingEvent);

        _context.BillingEvents.Add(billingEvent);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException failure) when (IsAlreadyReceived(failure))
        {
            _context.Entry(billingEvent).State = EntityState.Detached;

            return false;
        }

        return true;
    }

    public async Task<IReadOnlyList<BillingEvent>> ClaimAsync(
        int batchSize,
        int maxAttempts,
        DateTimeOffset now,
        TimeSpan lease,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxAttempts);

        // One statement, so several instances asking at the same moment get
        // disjoint sets. Phase 35: a Claimed event is claimable again only once its
        // claim is older than the lease (before, the next poll of any instance could
        // take an event still being processed). A worker that dies leaves its
        // events to come back after the lease. The bounded MATERIALIZED form: the
        // IN (... LIMIT ...) form was shown in Phase 33 not to respect its limit.
        await using NpgsqlCommand command = await CommandAsync(
                $"""
                 WITH candidate AS MATERIALIZED (
                     SELECT id
                       FROM {PersistenceSchema.BillingEventsTable}
                      WHERE attempts < @max_attempts
                        AND (status = 'Pending'
                             OR (status = 'Claimed' AND (claimed_at IS NULL OR claimed_at < @lease_cutoff)))
                      ORDER BY received_at
                      LIMIT @batch_size
                      FOR UPDATE SKIP LOCKED
                 )
                 UPDATE {PersistenceSchema.BillingEventsTable} e
                    SET status = 'Claimed',
                        attempts = e.attempts + 1,
                        claim_token = gen_random_uuid(),
                        claimed_at = @now
                   FROM candidate
                  WHERE e.id = candidate.id
                 RETURNING e.id
                 """,
                cancellationToken)
            .ConfigureAwait(false);

        command.Parameters.AddWithValue("max_attempts", maxAttempts);
        command.Parameters.AddWithValue("batch_size", batchSize);
        command.Parameters.AddWithValue("now", now.ToUniversalTime());
        command.Parameters.AddWithValue("lease_cutoff", (now - lease).ToUniversalTime());

        List<Guid> claimed = [];

        await using (DbDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                claimed.Add(reader.GetGuid(0));
            }
        }

        if (claimed.Count == 0)
        {
            return [];
        }

        // Read back through the model so the worker holds tracked aggregates
        // rather than rows. The claim above already made these this worker's.
        List<BillingEventId> ids = [.. claimed.Select(BillingEventId.From)];

        List<BillingEvent> events = await _context.BillingEvents
            .Where(stored => ids.Contains(stored.Id))
            .OrderBy(stored => stored.ReceivedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return events;
    }

    public async Task SaveAsync(
        BillingEvent billingEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(billingEvent);

        // Phase 35: only an event loaded by this unit of work carries the claim
        // token it was loaded with. Attaching a detached one would make its
        // "original" token whatever it holds now -- null once settled -- and the
        // concurrency check would then match a row another worker already settled.
        if (_context.Entry(billingEvent).State == EntityState.Detached)
        {
            throw new BillingEventOwnershipLostException(
                "A billing event can only be settled by the unit of work that loaded it under its claim.");
        }

        // Settled or handed back: nobody owns it any more. The concurrency token
        // still checks the ORIGINAL claim token, so a stale owner's save matches no
        // row and is refused instead of overwriting the new owner's work.
        if (billingEvent.Status != BillingEventStatus.Claimed)
        {
            EntityEntry<BillingEvent> entry = _context.Entry(billingEvent);
            entry.Property<Guid?>(BillingEventClaim.TokenProperty).CurrentValue = null;
            entry.Property<DateTimeOffset?>(BillingEventClaim.ClaimedAtProperty).CurrentValue = null;
        }

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException lost)
        {
            // Nothing was written. Detached so this context cannot retry the stale
            // write by accident on a later save.
            _context.Entry(billingEvent).State = EntityState.Detached;

            throw new BillingEventOwnershipLostException(
                "The billing event was claimed by another worker before it could be settled.",
                lost);
        }
    }

    /// <summary>The provider event id: one row per event, per provider.</summary>
    private const string EventIndex = "ux_billing_events_provider_event";

    /// <summary>Whether this is a redelivery, rather than some other collision.</summary>
    private static bool IsAlreadyReceived(DbUpdateException failure) =>
        failure.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
        } violation
        && string.Equals(violation.ConstraintName, EventIndex, StringComparison.Ordinal);

    private async Task<NpgsqlCommand> CommandAsync(
        string sql,
        CancellationToken cancellationToken)
    {
        DbConnection connection = _context.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        NpgsqlCommand command = new(sql, (NpgsqlConnection)connection);

        if (_context.Database.CurrentTransaction?.GetDbTransaction() is NpgsqlTransaction active)
        {
            command.Transaction = active;
        }

        return command;
    }
}
