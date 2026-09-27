using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OmniAI.Domain.Billing;

namespace OmniAI.Infrastructure.Persistence.Configurations;

/// <summary>
/// What each plan is sold for.
/// </summary>
/// <remarks>
/// Platform-owned and not tenant-scoped, exactly like the plan it sells. The
/// amount is a whole number of minor units, so no price is ever a binary float.
/// </remarks>
internal sealed class BillingPlanOfferConfiguration : IEntityTypeConfiguration<BillingPlanOffer>
{
    public void Configure(EntityTypeBuilder<BillingPlanOffer> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(PersistenceSchema.BillingPlanOffersTable, table =>
        {
            // The database's own guarantee. A negative price is meaningless and
            // must be impossible however the row arrives.
            table.HasCheckConstraint(
                "ck_billing_plan_offers_amount_not_negative",
                "\"amount_minor_units\" >= 0");

            // Only the two intervals this platform sells. A weekly row would
            // reach code that has no idea how to renew or display it.
            table.HasCheckConstraint(
                "ck_billing_plan_offers_interval",
                "\"interval\" IN ('Monthly', 'Annual')");

            table.HasCheckConstraint(
                "ck_billing_plan_offers_trial_days",
                "\"trial_days\" IS NULL OR (\"trial_days\" >= 0 AND \"trial_days\" <= 365)");
        });

        builder.HasKey(offer => offer.Id);

        builder.Property(offer => offer.Id)
            .HasColumnName("id")
            .HasConversion(PersistenceConverters.BillingOfferId)
            .ValueGeneratedNever();

        builder.Property(offer => offer.PlanId)
            .HasColumnName("plan_id")
            .HasConversion(PersistenceConverters.PlanId)
            .IsRequired();

        builder.Property(offer => offer.Code)
            .HasColumnName("code")
            .HasConversion(PersistenceConverters.OfferCode)
            .HasMaxLength(OfferCode.MaxLength)
            .IsRequired();

        // The stable handle a purchase names.
        builder.HasIndex(offer => offer.Code)
            .IsUnique()
            .HasDatabaseName("ux_billing_plan_offers_code");

        builder.Property(offer => offer.Interval)
            .HasColumnName("interval")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property<long>("AmountMinorUnits")
            .HasColumnName("amount_minor_units")
            .IsRequired();

        builder.Property(offer => offer.Currency)
            .HasColumnName("currency")
            .HasConversion(PersistenceConverters.BillingCurrency)
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(offer => offer.TrialDays).HasColumnName("trial_days");

        builder.Property(offer => offer.IsActive).HasColumnName("is_active").IsRequired();

        builder.Property(offer => offer.CreatedAt).HasColumnName("created_at").IsRequired();

        // An offer whose plan has gone would be unsellable and unreadable.
        builder.HasOne<Domain.Plans.Plan>()
            .WithMany()
            .HasForeignKey(offer => offer.PlanId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Which provider price corresponds to which offer.
/// </summary>
/// <remarks>
/// Keyed by (offer, provider) so one offer can be sold through a second provider
/// later. The provider's own price is unique per provider, so two offers cannot
/// both claim it and a webhook's reverse lookup has exactly one answer.
/// </remarks>
internal sealed class BillingProviderPriceMappingConfiguration
    : IEntityTypeConfiguration<BillingProviderPriceMapping>
{
    public void Configure(EntityTypeBuilder<BillingProviderPriceMapping> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(PersistenceSchema.BillingProviderPriceMappingsTable);

        builder.HasKey(mapping => new { mapping.OfferId, mapping.Provider });

        builder.Property(mapping => mapping.OfferId)
            .HasColumnName("offer_id")
            .HasConversion(PersistenceConverters.BillingOfferId);

        builder.Property(mapping => mapping.Provider)
            .HasColumnName("provider")
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(mapping => mapping.ExternalPriceId)
            .HasColumnName("external_price_id")
            .HasConversion(PersistenceConverters.ProviderReference)
            .HasMaxLength(ProviderReference.MaxLength)
            .IsRequired();

        builder.HasIndex(mapping => new { mapping.Provider, mapping.ExternalPriceId })
            .IsUnique()
            .HasDatabaseName("ux_billing_price_mappings_provider_price");

        builder.Property(mapping => mapping.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasOne<BillingPlanOffer>()
            .WithMany()
            .HasForeignKey(mapping => mapping.OfferId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// A workspace's customer record with a provider.
/// </summary>
/// <remarks>
/// One per workspace per provider, enforced by the key. Nothing here is a
/// payment detail: an opaque reference is all this platform ever receives.
/// </remarks>
internal sealed class WorkspaceBillingCustomerConfiguration
    : IEntityTypeConfiguration<WorkspaceBillingCustomer>
{
    public void Configure(EntityTypeBuilder<WorkspaceBillingCustomer> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(PersistenceSchema.WorkspaceBillingCustomersTable);

        builder.HasKey(customer => new { customer.TenantId, customer.Provider });

        builder.Property(customer => customer.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(PersistenceConverters.TenantId);

        builder.Property(customer => customer.Provider)
            .HasColumnName("provider")
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(customer => customer.ProviderCustomerId)
            .HasColumnName("provider_customer_id")
            .HasConversion(PersistenceConverters.ProviderReference)
            .HasMaxLength(ProviderReference.MaxLength)
            .IsRequired();

        // One provider customer belongs to one workspace. Two workspaces sharing
        // one would mean one paying for the other's entitlements.
        builder.HasIndex(customer => new { customer.Provider, customer.ProviderCustomerId })
            .IsUnique()
            .HasDatabaseName("ux_workspace_billing_customers_provider_customer");

        builder.Property(customer => customer.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasOne<Domain.Tenants.Tenant>()
            .WithMany()
            .HasForeignKey(customer => customer.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// A workspace's commercial subscription.
/// </summary>
/// <remarks>
/// <strong>At most one non-terminal subscription per workspace, enforced by a
/// partial unique index.</strong> An application check could be passed by two
/// concurrent requests; a filtered index cannot. Cancelled rows stay for history
/// and are excluded from the constraint, so a workspace may subscribe again.
/// </remarks>
internal sealed class WorkspaceSubscriptionConfiguration
    : IEntityTypeConfiguration<WorkspaceSubscription>
{
    public void Configure(EntityTypeBuilder<WorkspaceSubscription> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(PersistenceSchema.WorkspaceSubscriptionsTable);

        builder.HasKey(subscription => subscription.Id);

        builder.Property(subscription => subscription.Id)
            .HasColumnName("id")
            .HasConversion(PersistenceConverters.WorkspaceSubscriptionId)
            .ValueGeneratedNever();

        builder.Property(subscription => subscription.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(PersistenceConverters.TenantId)
            .IsRequired();

        builder.Property(subscription => subscription.Provider)
            .HasColumnName("provider")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(subscription => subscription.OfferId)
            .HasColumnName("offer_id")
            .HasConversion(PersistenceConverters.BillingOfferId)
            .IsRequired();

        builder.Property(subscription => subscription.PlanId)
            .HasColumnName("plan_id")
            .HasConversion(PersistenceConverters.PlanId)
            .IsRequired();

        builder.Property(subscription => subscription.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(subscription => subscription.Interval)
            .HasColumnName("interval")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(subscription => subscription.ProviderSubscriptionId)
            .HasColumnName("provider_subscription_id")
            .HasConversion(PersistenceConverters.NullableProviderReference)
            .HasMaxLength(ProviderReference.MaxLength);

        builder.Property(subscription => subscription.ProviderStatus)
            .HasColumnName("provider_status")
            .HasMaxLength(64);

        builder.Property(subscription => subscription.CurrentPeriodStart)
            .HasColumnName("current_period_start");

        builder.Property(subscription => subscription.CurrentPeriodEnd)
            .HasColumnName("current_period_end");

        builder.Property(subscription => subscription.TrialEndsAt).HasColumnName("trial_ends_at");

        builder.Property(subscription => subscription.CancelAtPeriodEnd)
            .HasColumnName("cancel_at_period_end")
            .IsRequired();

        builder.Property(subscription => subscription.CanceledAt).HasColumnName("canceled_at");

        builder.Property(subscription => subscription.GraceEndsAt).HasColumnName("grace_ends_at");

        builder.Property(subscription => subscription.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(subscription => subscription.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        // The structural guarantee: one live subscription per workspace.
        builder.HasIndex(subscription => subscription.TenantId)
            .IsUnique()
            .HasFilter("\"status\" <> 'Canceled'")
            .HasDatabaseName("ux_workspace_subscriptions_current");

        // One provider subscription belongs to one local subscription, so a
        // webhook's lookup has exactly one answer.
        builder.HasIndex(subscription =>
                new { subscription.Provider, subscription.ProviderSubscriptionId })
            .IsUnique()
            .HasFilter("\"provider_subscription_id\" IS NOT NULL")
            .HasDatabaseName("ux_workspace_subscriptions_provider_subscription");

        builder.HasOne<Domain.Tenants.Tenant>()
            .WithMany()
            .HasForeignKey(subscription => subscription.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<BillingPlanOffer>()
            .WithMany()
            .HasForeignKey(subscription => subscription.OfferId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// One attempt to buy a subscription.
/// </summary>
/// <remarks>
/// The idempotency key is unique, which is what makes two rapid clicks one
/// checkout across every web server rather than one per process.
/// </remarks>
internal sealed class BillingCheckoutIntentConfiguration
    : IEntityTypeConfiguration<BillingCheckoutIntent>
{
    public void Configure(EntityTypeBuilder<BillingCheckoutIntent> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(PersistenceSchema.BillingCheckoutIntentsTable);

        builder.HasKey(intent => intent.Id);

        builder.Property(intent => intent.Id)
            .HasColumnName("id")
            .HasConversion(PersistenceConverters.BillingCheckoutIntentId)
            .ValueGeneratedNever();

        builder.Property(intent => intent.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(PersistenceConverters.TenantId)
            .IsRequired();

        builder.Property(intent => intent.Provider)
            .HasColumnName("provider")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(intent => intent.OfferId)
            .HasColumnName("offer_id")
            .HasConversion(PersistenceConverters.BillingOfferId)
            .IsRequired();

        builder.Property(intent => intent.IdempotencyKey)
            .HasColumnName("idempotency_key")
            .HasMaxLength(200)
            .IsRequired();

        builder.HasIndex(intent => intent.IdempotencyKey)
            .IsUnique()
            .HasDatabaseName("ux_billing_checkout_intents_idempotency");

        builder.Property(intent => intent.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(intent => intent.ProviderSessionId)
            .HasColumnName("provider_session_id")
            .HasConversion(PersistenceConverters.NullableProviderReference)
            .HasMaxLength(ProviderReference.MaxLength);

        builder.HasIndex(intent => new { intent.Provider, intent.ProviderSessionId })
            .IsUnique()
            .HasFilter("\"provider_session_id\" IS NOT NULL")
            .HasDatabaseName("ux_billing_checkout_intents_provider_session");

        builder.Property(intent => intent.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(intent => intent.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(intent => intent.CompletedAt).HasColumnName("completed_at");

        builder.HasOne<Domain.Tenants.Tenant>()
            .WithMany()
            .HasForeignKey(intent => intent.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// Verified provider events, stored before they are acted on.
/// </summary>
/// <remarks>
/// <strong>The provider's event id is unique per provider.</strong> That is the
/// whole of duplicate protection: a redelivery, whether a second later or
/// concurrently, loses the insert and produces no second transition. Nothing
/// depends on process memory.
/// </remarks>
internal sealed class BillingEventConfiguration : IEntityTypeConfiguration<BillingEvent>
{
    public void Configure(EntityTypeBuilder<BillingEvent> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(PersistenceSchema.BillingEventsTable);

        builder.HasKey(stored => stored.Id);

        builder.Property(stored => stored.Id)
            .HasColumnName("id")
            .HasConversion(PersistenceConverters.BillingEventId)
            .ValueGeneratedNever();

        builder.Property(stored => stored.Provider)
            .HasColumnName("provider")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(stored => stored.ProviderEventId)
            .HasColumnName("provider_event_id")
            .HasConversion(PersistenceConverters.ProviderReference)
            .HasMaxLength(ProviderReference.MaxLength)
            .IsRequired();

        builder.HasIndex(stored => new { stored.Provider, stored.ProviderEventId })
            .IsUnique()
            .HasDatabaseName("ux_billing_events_provider_event");

        builder.Property(stored => stored.EventType)
            .HasColumnName("event_type")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(stored => stored.ProviderSubscriptionId)
            .HasColumnName("provider_subscription_id")
            .HasConversion(PersistenceConverters.NullableProviderReference)
            .HasMaxLength(ProviderReference.MaxLength);

        builder.Property(stored => stored.ProviderCustomerId)
            .HasColumnName("provider_customer_id")
            .HasConversion(PersistenceConverters.NullableProviderReference)
            .HasMaxLength(ProviderReference.MaxLength);

        builder.Property(stored => stored.ProviderSessionId)
            .HasColumnName("provider_session_id")
            .HasConversion(PersistenceConverters.NullableProviderReference)
            .HasMaxLength(ProviderReference.MaxLength);

        builder.Property(stored => stored.ProviderCreatedAt)
            .HasColumnName("provider_created_at")
            .IsRequired();

        builder.Property(stored => stored.ReceivedAt).HasColumnName("received_at").IsRequired();

        builder.Property(stored => stored.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(stored => stored.Attempts).HasColumnName("attempts").IsRequired();
        builder.Property(stored => stored.ProcessedAt).HasColumnName("processed_at");

        // The platform's own words about why something could not be applied.
        // Never the provider's payload.
        builder.Property(stored => stored.Failure).HasColumnName("failure").HasMaxLength(400);

        // What the worker's claim scans.
        builder.HasIndex(stored => new { stored.Status, stored.ReceivedAt })
            .HasDatabaseName("ix_billing_events_status_received");

        // Phase 35: claim ownership. Infrastructure state, not a business fact, so
        // shadow properties rather than members of the aggregate. The token is a
        // concurrency token: a worker whose claim was taken over (its lease lapsed
        // and another instance claimed the event) saves with the old token and
        // matches nothing, so a stale owner can never settle an event.
        builder.Property<Guid?>(BillingEventClaim.TokenProperty)
            .HasColumnName("claim_token")
            .IsConcurrencyToken();

        builder.Property<DateTimeOffset?>(BillingEventClaim.ClaimedAtProperty)
            .HasColumnName("claimed_at");
    }
}

/// <summary>Names of the billing-event claim shadow properties.</summary>
internal static class BillingEventClaim
{
    public const string TokenProperty = "ClaimToken";

    public const string ClaimedAtProperty = "ClaimedAt";
}
