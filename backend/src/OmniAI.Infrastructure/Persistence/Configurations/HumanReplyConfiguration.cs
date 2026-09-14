using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;

namespace OmniAI.Infrastructure.Persistence.Configurations;

/// <summary>
/// Who sent each human reply, and the key that makes sending it twice harmless.
/// </summary>
/// <remarks>
/// The same shape as <c>conversation_turns</c>, deliberately: a small row written
/// in the same transaction as the message it describes, carrying a unique key the
/// database enforces. The platform already had one answer to "this write must
/// happen at most once", and a second, different answer would be one idea too
/// many.
/// <para>
/// It carries the workspace, which the message it points at does not. That is
/// what makes the composite foreign key below possible, and that key is the whole
/// reason this is a table rather than two nullable columns on <c>messages</c>.
/// </para>
/// </remarks>
internal sealed class HumanReplyConfiguration : IEntityTypeConfiguration<HumanReply>
{
    public void Configure(EntityTypeBuilder<HumanReply> builder)
    {
        builder.ToTable(PersistenceSchema.HumanRepliesTable);

        builder.HasKey(reply => reply.Id);

        builder.Property(reply => reply.Id)
            .HasColumnName("id")
            .HasConversion(PersistenceConverters.HumanReplyId)
            .ValueGeneratedNever();

        builder.Property(reply => reply.ConversationId)
            .HasColumnName("conversation_id")
            .HasConversion(PersistenceConverters.ConversationId)
            .IsRequired();

        builder.Property(reply => reply.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(PersistenceConverters.TenantId)
            .IsRequired();

        builder.Property(reply => reply.Sender)
            .HasColumnName("sender_user_id")
            .HasConversion(PersistenceConverters.UserId)
            .IsRequired();

        builder.Property(reply => reply.ClientMessageId)
            .HasColumnName("client_message_id")
            .HasConversion(PersistenceConverters.ClientMessageId)
            .HasMaxLength(ClientMessageId.MaxLength)
            .IsRequired();

        builder.Property(reply => reply.MessageId)
            .HasColumnName("message_id")
            .HasConversion(PersistenceConverters.MessageId)
            .IsRequired();

        builder.Property(reply => reply.SentAt)
            .HasColumnName("sent_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne<Conversation>()
            .WithMany()
            .HasForeignKey(reply => reply.ConversationId)
            .HasConstraintName("fk_human_replies_conversation")
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        builder.HasOne<Message>()
            .WithMany()
            .HasForeignKey(reply => reply.MessageId)
            .HasConstraintName("fk_human_replies_message")
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(reply => reply.TenantId)
            .HasConstraintName("fk_human_replies_tenant")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        // <strong>The idempotency boundary, and it lives here rather than in a
        // handler.</strong> Two concurrent requests carrying the same client
        // message id both pass any in-process check; the only place that can
        // settle the race is this index. The loser reads back the winner's
        // message and returns it, so a browser that retried a request whose
        // response was lost sends one reply and sees one reply.
        builder.HasIndex(reply => new { reply.ConversationId, reply.ClientMessageId })
            .IsUnique()
            .HasDatabaseName("ux_human_replies_client_message");

        // One message is one reply. Without this a bug could attribute two
        // authors to the same message, and the transcript would have no single
        // answer to who wrote it.
        builder.HasIndex(reply => reply.MessageId)
            .IsUnique()
            .HasDatabaseName("ux_human_replies_message");

        // The admin transcript joins on this to name the author of each reply.
        builder.HasIndex(reply => reply.ConversationId)
            .HasDatabaseName("ix_human_replies_conversation");

        // No concurrency token. The row is written once and never updated: a
        // reply that has been sent is a thing that happened, and editing it would
        // be editing what a customer already read.
    }
}
