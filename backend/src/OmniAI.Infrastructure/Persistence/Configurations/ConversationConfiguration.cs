using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Customers;
using OmniAI.Domain.Tenants;

namespace OmniAI.Infrastructure.Persistence.Configurations;

internal sealed class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("conversations");

        builder.HasKey(conversation => conversation.Id);

        builder.Property(conversation => conversation.Id)
            .HasColumnName("id")
            .HasConversion(PersistenceConverters.ConversationId)
            .ValueGeneratedNever();

        builder.Property(conversation => conversation.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(PersistenceConverters.TenantId)
            .IsRequired();

        builder.Property(conversation => conversation.CustomerId)
            .HasColumnName("customer_id")
            .HasConversion(PersistenceConverters.CustomerId)
            .IsRequired();

        builder.Property(conversation => conversation.Channel)
            .HasColumnName("channel")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(conversation => conversation.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(conversation => conversation.Handling)
            .HasColumnName("handling")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        // Nullable, because unassigned is the ordinary state of a conversation
        // and not a missing value: most of them are nobody's in particular. The
        // composite key that makes a cross-workspace assignment unrepresentable
        // is declared below.
        builder.Property(conversation => conversation.AssignedTo)
            .HasColumnName("assigned_to")
            .HasConversion(PersistenceConverters.NullableUserId);

        // timestamptz stores an absolute instant. The Domain has already
        // normalised every value to UTC, so nothing here depends on the server's
        // or the client's time zone.
        builder.Property(conversation => conversation.StartedAt)
            .HasColumnName("started_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(conversation => conversation.LastActivityAt)
            .HasColumnName("last_activity_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        // Restrict, not Cascade: a tenant that still owns conversations must not
        // be deletable by accident, and certainly not silently. Removing a
        // tenant's data is a deliberate operation with its own rules, which do
        // not exist yet -- so the database refuses until they do.
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(conversation => conversation.TenantId)
            .HasConstraintName("fk_conversations_tenant")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        // The composite key is the point: the customer a conversation belongs to
        // must be a customer of the same workspace. Expressed as a constraint
        // rather than as an application check, because an application check is
        // one forgotten call site away from a workspace reading another
        // workspace's conversation through its own customer directory.
        //
        // Restrict, for the same reason the tenant relationship is: a customer
        // with conversations is not something to remove by accident, and Phase 30
        // deliberately has no customer deletion at all.
        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(conversation => new { conversation.TenantId, conversation.CustomerId })
            .HasPrincipalKey(customer => new { customer.TenantId, customer.Id })
            .HasConstraintName("fk_conversations_customer")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        // An assignee must be a member of this conversation's workspace:
        // (assigned_to, tenant_id) into the membership table's own primary key,
        // both columns read from this row. Optional, because assigned_to is
        // nullable and PostgreSQL does not check a key with a null part. No
        // navigation: the workspace's membership graph does not belong inside a
        // conversation. Restrict, so a member holding conversations cannot be
        // removed out from under them.
        //
        // Its index is the partial one below rather than EF's automatic one.
        builder.HasOne<TenantMembership>()
            .WithMany()
            .HasForeignKey(conversation => new { conversation.AssignedTo, conversation.TenantId })
            .HasPrincipalKey(membership => new { membership.UserId, membership.TenantId })
            .HasConstraintName("fk_conversations_assigned_member")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false)
            .WithoutConventionIndex();

        // "Assigned to me" and the membership key's own check. Partial, because
        // most conversations are unassigned and have nothing to index.
        builder.HasIndex(conversation => conversation.AssignedTo)
            .HasDatabaseName("ix_conversations_assigned_to")
            .HasFilter("assigned_to IS NOT NULL");

        // One customer's history, newest first -- the customer detail page's only
        // query, and the ordering the merge has to preserve.
        builder.HasIndex(conversation => new
            {
                conversation.TenantId,
                conversation.CustomerId,
                conversation.LastActivityAt,
            })
            .HasDatabaseName("ix_conversations_tenant_customer_activity");

        // Every realistic query is tenant-scoped, and this is also the index that
        // makes a tenant-scoped listing cheap.
        builder.HasIndex(conversation => new { conversation.TenantId, conversation.LastActivityAt })
            .HasDatabaseName("ix_conversations_tenant_last_activity");

        // The analytics entry point, added in Phase 22.
        //
        // Every dashboard query begins by narrowing conversations to one
        // workspace and one window, and a window is on `started_at` -- when the
        // conversation began -- not on `last_activity_at`, which is when it was
        // last touched. The index above leads with the wrong second column for
        // that question, so without this one a dashboard scans every conversation
        // the deployment holds.
        builder.HasIndex(conversation => new { conversation.TenantId, conversation.StartedAt })
            .HasDatabaseName("ix_conversations_tenant_started");

        // The "needs a person" list (Phase 37.4, M6). Its handling branch is
        // "a person is handling it", read in the list's own order; the other
        // branch (the AI's last turn failed) is reached through the newest
        // message, whose index already exists.
        builder.HasIndex(conversation => new
            {
                conversation.TenantId,
                conversation.Handling,
                conversation.LastActivityAt,
            })
            .HasDatabaseName("ix_conversations_tenant_handling_activity");

        builder.Property<uint>(PersistenceSchema.XminConcurrencyToken)
            .HasColumnName(PersistenceSchema.XminConcurrencyToken)
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        // The aggregate exposes a read-only view, so EF must reach the backing
        // list directly rather than through the property getter.
        builder.HasMany(conversation => conversation.Messages)
            .WithOne()
            .HasForeignKey(message => message.ConversationId)
            .HasConstraintName("fk_messages_conversation")
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        builder.Navigation(conversation => conversation.Messages)
            .HasField("_messages")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
