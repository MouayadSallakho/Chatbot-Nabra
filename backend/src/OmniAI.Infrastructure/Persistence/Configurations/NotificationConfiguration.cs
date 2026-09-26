using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OmniAI.Domain.Notifications;
using OmniAI.Domain.Tenants;

namespace OmniAI.Infrastructure.Persistence.Configurations;

/// <summary>
/// Occurrences: the durable record that an authoritative transition happened.
/// </summary>
/// <remarks>
/// Unique on (workspace, kind, key): the same transition recorded twice — a
/// replayed webhook, a retried statement, two instances racing — is one row.
/// No payload column exists to put anything else in.
/// </remarks>
internal sealed class NotificationOccurrenceConfiguration : IEntityTypeConfiguration<NotificationOccurrence>
{
    public const string ClaimToken = "ClaimToken";
    public const string ClaimedAt = "ClaimedAt";

    public void Configure(EntityTypeBuilder<NotificationOccurrence> builder)
    {
        builder.ToTable(PersistenceSchema.NotificationOccurrencesTable, table =>
            table.HasCheckConstraint("ck_notification_occurrences_attempts", "attempts >= 0"));

        builder.HasKey(occurrence => occurrence.Id);

        builder.Property(occurrence => occurrence.Id)
            .HasColumnName("id")
            .HasConversion(PersistenceConverters.NotificationOccurrenceId)
            .ValueGeneratedNever();

        builder.Property(occurrence => occurrence.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(PersistenceConverters.TenantId)
            .IsRequired();

        builder.Property(occurrence => occurrence.Kind)
            .HasColumnName("kind")
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(occurrence => occurrence.OccurrenceKey)
            .HasColumnName("occurrence_key")
            .HasMaxLength(NotificationOccurrence.MaxKeyLength)
            .IsRequired();

        builder.Property(occurrence => occurrence.SourceId).HasColumnName("source_id");

        builder.Property(occurrence => occurrence.OccurredAt)
            .HasColumnName("occurred_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(occurrence => occurrence.ProcessedAt)
            .HasColumnName("processed_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(occurrence => occurrence.Attempts).HasColumnName("attempts").IsRequired();

        builder.Property<Guid?>(ClaimToken).HasColumnName("claim_token");

        builder.Property<DateTimeOffset?>(ClaimedAt)
            .HasColumnName("claimed_at")
            .HasColumnType("timestamp with time zone");

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(occurrence => occurrence.TenantId)
            .HasConstraintName("fk_notification_occurrences_tenant")
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        builder.HasIndex(occurrence => new { occurrence.TenantId, occurrence.Kind, occurrence.OccurrenceKey })
            .HasDatabaseName("ux_notification_occurrences_key")
            .IsUnique();

        // The dispatcher's query: the few rows still to be done.
        builder.HasIndex(occurrence => occurrence.OccurredAt)
            .HasDatabaseName("ix_notification_occurrences_pending")
            .HasFilter("processed_at IS NULL");
    }
}

/// <summary>Per-member notifications.</summary>
internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable(PersistenceSchema.NotificationsTable, table =>
        {
            table.HasCheckConstraint("ck_notifications_expiry", "expires_at > created_at");

            // Defence in depth for the Domain rule: an action is an internal
            // route — one leading slash, then lower-case letters, digits, hyphens
            // and single slashes. No scheme, host, query or fragment can be stored.
            table.HasCheckConstraint(
                "ck_notifications_action_path",
                "action_path IS NULL OR action_path ~ '^/([a-z0-9-]+(/[a-z0-9-]+)*)?$'");
        });

        builder.HasKey(notification => notification.Id);

        builder.Property(notification => notification.Id)
            .HasColumnName("id")
            .HasConversion(PersistenceConverters.NotificationId)
            .ValueGeneratedNever();

        builder.Property(notification => notification.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(PersistenceConverters.TenantId)
            .IsRequired();

        builder.Property(notification => notification.RecipientUserId)
            .HasColumnName("recipient_user_id")
            .HasConversion(PersistenceConverters.UserId)
            .IsRequired();

        builder.Property(notification => notification.OccurrenceId)
            .HasColumnName("occurrence_id")
            .HasConversion(PersistenceConverters.NotificationOccurrenceId)
            .IsRequired();

        builder.Property(notification => notification.Kind)
            .HasColumnName("kind")
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(notification => notification.Category)
            .HasColumnName("category")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(notification => notification.Severity)
            .HasColumnName("severity")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(notification => notification.Title)
            .HasColumnName("title")
            .HasMaxLength(Notification.MaxTitleLength)
            .IsRequired();

        builder.Property(notification => notification.Body)
            .HasColumnName("body")
            .HasMaxLength(Notification.MaxBodyLength)
            .IsRequired();

        builder.Property(notification => notification.ActionPath)
            .HasColumnName("action_path")
            .HasMaxLength(NotificationActionPath.MaxLength);

        builder.Property(notification => notification.DeduplicationKey)
            .HasColumnName("deduplication_key")
            .HasMaxLength(Notification.MaxDeduplicationKeyLength)
            .IsRequired();

        builder.Property(notification => notification.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(notification => notification.ReadAt)
            .HasColumnName("read_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(notification => notification.ExpiresAt)
            .HasColumnName("expires_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Ignore(notification => notification.IsRead);

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(notification => notification.TenantId)
            .HasConstraintName("fk_notifications_tenant")
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        // The recipient is a real account. Cascade, as Phase 32 declared it: a
        // member's notifications are theirs and go with the account. No index of
        // its own, as in Phase 32; the recipient indexes below lead with the
        // workspace. See UserReferenceRow for why the key targets it.
        builder.HasOne<UserReferenceRow>()
            .WithMany()
            .HasForeignKey(notification => notification.RecipientUserId)
            .HasConstraintName("fk_notifications_recipient_user")
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired()
            .WithoutConventionIndex();

        // One notification per recipient per occurrence. This is the index that
        // turns a replayed dispatch into a no-op.
        builder.HasIndex(notification => new { notification.TenantId, notification.RecipientUserId, notification.DeduplicationKey })
            .HasDatabaseName("ux_notifications_recipient_dedup")
            .IsUnique();

        // The notification centre: one member's rows, newest first.
        builder.HasIndex(notification => new { notification.TenantId, notification.RecipientUserId, notification.CreatedAt, notification.Id })
            .HasDatabaseName("ix_notifications_recipient_created")
            .IsDescending(false, false, true, true);

        // The unread badge: a count over the member's unread rows only.
        builder.HasIndex(notification => new { notification.TenantId, notification.RecipientUserId })
            .HasDatabaseName("ix_notifications_recipient_unread")
            .HasFilter("read_at IS NULL");

        builder.HasIndex(notification => notification.ExpiresAt)
            .HasDatabaseName("ix_notifications_expires");
    }
}

/// <summary>Email preferences: one row per member per workspace, once they have chosen.</summary>
internal sealed class NotificationPreferencesConfiguration : IEntityTypeConfiguration<NotificationPreferences>
{
    public void Configure(EntityTypeBuilder<NotificationPreferences> builder)
    {
        builder.ToTable(PersistenceSchema.NotificationPreferencesTable);

        builder.HasKey(preferences => new { preferences.TenantId, preferences.UserId });

        builder.Property(preferences => preferences.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(PersistenceConverters.TenantId)
            .ValueGeneratedNever();

        builder.Property(preferences => preferences.UserId)
            .HasColumnName("user_id")
            .HasConversion(PersistenceConverters.UserId)
            .ValueGeneratedNever();

        builder.Property(preferences => preferences.EmailReports).HasColumnName("email_reports").IsRequired();
        builder.Property(preferences => preferences.EmailUsage).HasColumnName("email_usage").IsRequired();
        builder.Property(preferences => preferences.EmailBilling).HasColumnName("email_billing").IsRequired();
        builder.Property(preferences => preferences.EmailKnowledge).HasColumnName("email_knowledge").IsRequired();
        builder.Property(preferences => preferences.EmailChannelHealth).HasColumnName("email_channel_health").IsRequired();

        builder.Property(preferences => preferences.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(preferences => preferences.TenantId)
            .HasConstraintName("fk_notification_preferences_tenant")
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        // The member the preferences belong to. Cascade, as Phase 32 declared it.
        // The primary key leads with the workspace, and Phase 32 kept no index
        // for this key; nor does the model.
        builder.HasOne<UserReferenceRow>()
            .WithMany()
            .HasForeignKey(preferences => preferences.UserId)
            .HasConstraintName("fk_notification_preferences_user")
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired()
            .WithoutConventionIndex();

        builder.Property<uint>(PersistenceSchema.XminConcurrencyToken)
            .HasColumnName(PersistenceSchema.XminConcurrencyToken)
            .IsRowVersion();
    }
}

/// <summary>Queued operational emails. No address, body or credential is stored.</summary>
internal sealed class NotificationEmailConfiguration : IEntityTypeConfiguration<NotificationEmail>
{
    public const string ClaimToken = "ClaimToken";
    public const string ClaimedAt = "ClaimedAt";

    public void Configure(EntityTypeBuilder<NotificationEmail> builder)
    {
        builder.ToTable(PersistenceSchema.NotificationEmailsTable, table =>
            table.HasCheckConstraint("ck_notification_emails_attempts", "attempts >= 0"));

        builder.HasKey(email => email.Id);

        builder.Property(email => email.Id)
            .HasColumnName("id")
            .HasConversion(PersistenceConverters.NotificationEmailId)
            .ValueGeneratedNever();

        builder.Property(email => email.NotificationId)
            .HasColumnName("notification_id")
            .HasConversion(PersistenceConverters.NotificationId)
            .IsRequired();

        builder.Property(email => email.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(PersistenceConverters.TenantId)
            .IsRequired();

        builder.Property(email => email.RecipientUserId)
            .HasColumnName("recipient_user_id")
            .HasConversion(PersistenceConverters.UserId)
            .IsRequired();

        builder.Property(email => email.Category)
            .HasColumnName("category")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(email => email.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(email => email.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(email => email.AvailableAt)
            .HasColumnName("available_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(email => email.Attempts).HasColumnName("attempts").IsRequired();

        builder.Property(email => email.SentAt)
            .HasColumnName("sent_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(email => email.SettledAt)
            .HasColumnName("settled_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(email => email.FailureCode)
            .HasColumnName("failure_code")
            .HasMaxLength(NotificationEmail.MaxFailureCodeLength);

        builder.Property<Guid?>(ClaimToken).HasColumnName("claim_token");

        builder.Property<DateTimeOffset?>(ClaimedAt)
            .HasColumnName("claimed_at")
            .HasColumnType("timestamp with time zone");

        // Composite, so an email can only ever belong to a notification of its
        // own workspace. Removing the notification removes its email.
        builder.HasOne<Notification>()
            .WithMany()
            .HasForeignKey(email => new { email.TenantId, email.NotificationId })
            .HasPrincipalKey(notification => new { notification.TenantId, notification.Id })
            .HasConstraintName("fk_notification_emails_notification")
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        // The recipient account. Cascade and unindexed, as Phase 32 declared it.
        builder.HasOne<UserReferenceRow>()
            .WithMany()
            .HasForeignKey(email => email.RecipientUserId)
            .HasConstraintName("fk_notification_emails_recipient_user")
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired()
            .WithoutConventionIndex();

        // One email per notification: a replayed dispatch cannot queue a second.
        builder.HasIndex(email => email.NotificationId)
            .HasDatabaseName("ux_notification_emails_notification")
            .IsUnique();

        // The sender's query: the small set still to be sent or still in flight.
        builder.HasIndex(email => new { email.Status, email.AvailableAt })
            .HasDatabaseName("ix_notification_emails_claimable")
            .HasFilter("status IN ('Pending', 'Sending')");
    }
}
