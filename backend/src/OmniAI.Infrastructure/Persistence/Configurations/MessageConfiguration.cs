using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OmniAI.Domain.Conversations;

namespace OmniAI.Infrastructure.Persistence.Configurations;

internal sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("messages");

        // MessageId remains the identity. The ingestion sequence below orders
        // rows; it never identifies them.
        builder.HasKey(message => message.Id);

        builder.Property(message => message.Id)
            .HasColumnName("id")
            .HasConversion(PersistenceConverters.MessageId)
            .ValueGeneratedNever();

        builder.Property(message => message.ConversationId)
            .HasColumnName("conversation_id")
            .HasConversion(PersistenceConverters.ConversationId)
            .IsRequired();

        builder.Property(message => message.Sender)
            .HasColumnName("sender")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(message => message.ContentType)
            .HasColumnName("content_type")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(message => message.Text)
            .HasColumnName("text")
            .HasMaxLength(Message.MaxTextLength)
            .IsRequired();

        builder.Property(message => message.Attachments).HasColumnName("attachments").HasColumnType("text[]");

        builder.Property(message => message.SentAt)
            .HasColumnName("sent_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasIndex(message => message.Text)
            .HasDatabaseName("ix_messages_text_trgm")
            .HasMethod("gin")
            .HasOperators("gin_trgm_ops");

        // Ingestion order.
        //
        // Message.SentAt is deliberately not monotonic, so reload cannot order by
        // it without reordering history. MessageId cannot serve either: a UUIDv7
        // orders only to the millisecond, and messages appended in the same
        // millisecond sort arbitrarily below that.
        //
        // The value is allocated from the message_ingestion_sequence sequence by
        // OmniAiDbContext immediately before insert. Two earlier designs were
        // rejected -- a self-filling identity column, whose values did not follow
        // append order because EF Core does not order inserts within a batch, and
        // the message's index in the aggregate's collection, which was correct
        // but only if the entire history had been loaded first. See
        // OmniAiDbContext.PendingMessagesInAppendOrder.
        //
        // The numbers are global rather than per-conversation, so within one
        // conversation they are sparse. Only relative order is meaningful and
        // gaps are expected: a rolled-back transaction consumes values it never
        // stores, because sequences do not roll back.
        builder.Property<long>(PersistenceSchema.MessageIngestionSequence)
            .HasColumnName("ingestion_sequence")
            .IsRequired();

        // Unique, not merely indexed. The sequence cannot hand the same value out
        // twice, so this is not there to catch the allocator misbehaving; it is
        // there so that any future code path which sets the column itself cannot
        // make one conversation's order ambiguous without the write failing. It
        // is also the index that makes ordered reload of one conversation a
        // single range scan.
        builder.HasIndex(
                nameof(Message.ConversationId),
                PersistenceSchema.MessageIngestionSequence)
            .IsUnique()
            .HasDatabaseName("ix_messages_conversation_ingestion");

        // No concurrency token: a message is immutable once written, so there is
        // no update to lose.
    }
}
