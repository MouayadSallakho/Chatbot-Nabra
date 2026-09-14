using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OmniAI.Domain.Conversations;
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
        // and not a missing value: most of them are nobody's in particular.
        //
        // The composite foreign key that makes a cross-workspace assignment
        // unrepresentable -- (assigned_to, tenant_id) into the membership table's
        // own primary key -- is declared in the migration rather than here.
        // tenant_memberships is keyed on (user_id, tenant_id), and EF cannot
        // express a composite principal key for a single-column dependent
        // without modelling membership as a navigation on this aggregate, which
        // would put the workspace's entire membership graph inside a
        // conversation. A schema test asserts the constraint exists.
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

        // Every realistic query is tenant-scoped, and this is also the index that
        // makes a tenant-scoped listing cheap.
        builder.HasIndex(conversation => new { conversation.TenantId, conversation.LastActivityAt })
            .HasDatabaseName("ix_conversations_tenant_last_activity");

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
