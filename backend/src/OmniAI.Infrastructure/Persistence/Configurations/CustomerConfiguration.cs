using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OmniAI.Domain.Customers;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.WhatsApp;

namespace OmniAI.Infrastructure.Persistence.Configurations;

/// <summary>
/// The canonical customer directory.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Deliberately narrow.</strong> A workspace, an optional label, a merge
/// pointer and two timestamps. There is no column here for a note, a tag, a
/// stage, a score or a free-form JSON bag, and there must not be: each of those
/// is a product decision that would be made by accident the moment the column
/// existed.
/// </para>
/// <para>
/// <strong>No channel identifier is stored here.</strong> The phone number lives
/// with the WhatsApp identity and the session credential lives with the web chat
/// session. Copying either here would create a second place to get personal data
/// wrong, with its own retention and masking question.
/// </para>
/// </remarks>
internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable(PersistenceSchema.CustomersTable);

        builder.HasKey(customer => customer.Id);

        builder.Property(customer => customer.Id)
            .HasColumnName("id")
            .HasConversion(PersistenceConverters.CustomerId)
            .ValueGeneratedNever();

        builder.Property(customer => customer.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(PersistenceConverters.TenantId)
            .IsRequired();

        // Personal data, and untrusted text. Bounded in the Domain and bounded
        // again here, so a future path that bypasses the value object still
        // cannot write an unbounded string into a column every list renders.
        builder.Property(customer => customer.DisplayName)
            .HasColumnName("display_name")
            .HasConversion(PersistenceConverters.NullableCustomerDisplayName)
            .HasMaxLength(CustomerDisplayName.MaxLength);

        builder.Property(customer => customer.MergedIntoCustomerId)
            .HasColumnName("merged_into_customer_id")
            .HasConversion(PersistenceConverters.NullableCustomerId);

        builder.Property(customer => customer.MergedAt)
            .HasColumnName("merged_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(customer => customer.MergedBy)
            .HasColumnName("merged_by")
            .HasConversion(PersistenceConverters.NullableUserId);

        builder.Property(customer => customer.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(customer => customer.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(customer => customer.TenantId)
            .HasConstraintName("fk_customers_tenant")
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        // The merge target, by relation rather than by a bare id, so the database
        // refuses a pointer at a customer that does not exist. Restrict rather
        // than cascade: removing a surviving customer must not silently take the
        // records merged into it with it.
        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(customer => customer.MergedIntoCustomerId)
            .HasConstraintName("fk_customers_merged_into")
            .OnDelete(DeleteBehavior.Restrict);

        // The directory's own query: one workspace, newest first.
        builder.HasIndex(customer => customer.DisplayName)
            .HasDatabaseName("ix_customers_display_name_trgm")
            .HasMethod("gin")
            .HasOperators("gin_trgm_ops");

        builder.HasIndex(customer => new { customer.TenantId, customer.CreatedAt })
            .HasDatabaseName("ix_customers_tenant_created");

        // The alternate key on (tenant_id, id) is declared implicitly by the
        // composite foreign keys that reference it -- on conversations and on the
        // WhatsApp identities -- and PostgreSQL enforces it with a unique
        // constraint. Declaring a second unique index over the same pair here
        // would be the same guarantee written twice and maintained twice.

        builder.Property<uint>(PersistenceSchema.XminConcurrencyToken)
            .HasColumnName(PersistenceSchema.XminConcurrencyToken)
            .IsRowVersion();
    }
}

/// <summary>
/// Which WhatsApp number belongs to which canonical customer.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The key is the whole point.</strong> One row per (workspace, sender),
/// enforced by the primary key, so "who is this number?" has exactly one answer
/// that no race and no retry can duplicate. The concurrency safety of two
/// simultaneous first messages from the same person is this constraint and
/// nothing else — no lock in the application, which would only work in one
/// process.
/// </para>
/// <para>
/// The sender identity is stored once here and once per conversation on
/// <c>whatsapp_contacts</c>. That is not redundancy: the contact row answers
/// "which conversation is this number's current one", which changes constantly,
/// and this row answers "who is this number", which does not.
/// </para>
/// </remarks>
internal sealed class WhatsAppCustomerIdentityConfiguration
    : IEntityTypeConfiguration<WhatsAppCustomerIdentity>
{
    public void Configure(EntityTypeBuilder<WhatsAppCustomerIdentity> builder)
    {
        builder.ToTable(PersistenceSchema.WhatsAppCustomerIdentitiesTable);

        builder.HasKey(identity => new { identity.TenantId, identity.WhatsAppCustomerId });

        builder.Property(identity => identity.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(PersistenceConverters.TenantId)
            .IsRequired();

        // Personal data. Stored because it is the only thing that identifies this
        // person across conversations, and for no other reason.
        builder.Property(identity => identity.WhatsAppCustomerId)
            .HasColumnName("whatsapp_customer_id")
            .HasConversion(PersistenceConverters.WhatsAppCustomerId)
            .HasMaxLength(WhatsAppCustomerId.MaxLength)
            .IsRequired();

        builder.Property(identity => identity.CustomerId)
            .HasColumnName("customer_id")
            .HasConversion(PersistenceConverters.CustomerId)
            .IsRequired();

        builder.Property(identity => identity.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(identity => identity.TenantId)
            .HasConstraintName("fk_whatsapp_customer_identities_tenant")
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        // The composite foreign key is what makes cross-workspace identity
        // impossible in the database rather than merely unlikely in the code: the
        // customer this points at must be a customer of this very workspace.
        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(identity => new { identity.TenantId, identity.CustomerId })
            .HasPrincipalKey(customer => new { customer.TenantId, customer.Id })
            .HasConstraintName("fk_whatsapp_customer_identities_customer")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        // Merge repoints every identity of the source, so the customer side of
        // this relationship is queried directly.
        builder.HasIndex(identity => new { identity.TenantId, identity.CustomerId })
            .HasDatabaseName("ix_whatsapp_customer_identities_customer");
    }
}
