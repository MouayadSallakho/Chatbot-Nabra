using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OmniAI.Domain.Accounts;
using OmniAI.Domain.Invitations;

namespace OmniAI.Infrastructure.Persistence.Configurations;

/// <summary>
/// Queued account emails (Phase 37.3, M2). No address, token, link, subject or body.
/// </summary>
internal sealed class AccountEmailConfiguration : IEntityTypeConfiguration<AccountEmail>
{
    public const string ClaimToken = "ClaimToken";
    public const string ClaimedAt = "ClaimedAt";

    public void Configure(EntityTypeBuilder<AccountEmail> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(PersistenceSchema.AccountEmailsTable, table =>
        {
            table.HasCheckConstraint("ck_account_emails_attempts", "attempts >= 0");

            // M2a: an account email names an account or an invitation, never both
            // and never neither.
            table.HasCheckConstraint(
                "ck_account_emails_recipient",
                "(user_id IS NULL) <> (invitation_id IS NULL)");
        });

        builder.HasKey(email => email.Id).HasName("pk_account_emails");

        builder.Property(email => email.Id)
            .HasColumnName("id")
            .HasConversion(PersistenceConverters.AccountEmailId)
            .ValueGeneratedNever();

        builder.Property(email => email.UserId)
            .HasColumnName("user_id")
            .HasConversion(PersistenceConverters.NullableUserId);

        builder.Property(email => email.InvitationId)
            .HasColumnName("invitation_id")
            .HasConversion(PersistenceConverters.NullableTenantInvitationId);

        builder.Property(email => email.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(PersistenceConverters.NullableTenantId);

        builder.Property(email => email.Kind)
            .HasColumnName("kind")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(email => email.RequestedAt)
            .HasColumnName("requested_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(email => email.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(email => email.AvailableAt)
            .HasColumnName("available_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(email => email.Attempts).HasColumnName("attempts").IsRequired();

        builder.Property<Guid?>(ClaimToken).HasColumnName("claim_token");

        builder.Property<DateTimeOffset?>(ClaimedAt)
            .HasColumnName("claimed_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(email => email.SentAt)
            .HasColumnName("sent_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(email => email.SettledAt)
            .HasColumnName("settled_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(email => email.FailureCode)
            .HasColumnName("failure_code")
            .HasMaxLength(AccountEmail.MaxFailureCodeLength);

        // The account. Removing it removes what was queued for it.
        builder.HasOne<UserReferenceRow>()
            .WithMany()
            .HasForeignKey(email => email.UserId)
            .HasConstraintName("fk_account_emails_user")
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired(false);

        // M2a: the invitation, through its (id, tenant_id) alternate key, so an
        // email can only belong to an invitation of the workspace it names.
        // Removing the invitation removes its email. EF's convention index serves
        // the key.
        builder.HasOne<TenantInvitation>()
            .WithMany()
            .HasForeignKey(email => new { email.InvitationId, email.TenantId })
            .HasPrincipalKey(invitation => new { invitation.Id, invitation.TenantId })
            .HasConstraintName("fk_account_emails_invitation")
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired(false);

        // The sender's query: the small set still to be sent or still in flight.
        builder.HasIndex(email => email.AvailableAt)
            .HasDatabaseName("ix_account_emails_claimable")
            .HasFilter("status IN ('Pending', 'Sending')");

        // "Is one already pending for this account?", and the account key.
        builder.HasIndex(email => new { email.UserId, email.RequestedAt })
            .HasDatabaseName("ix_account_emails_user_requested");
    }
}
