using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;

namespace OmniAI.Infrastructure.Persistence.Configurations;

/// <summary>
/// Takeovers and returns to the AI (Phase 37.4, migration M4).
/// </summary>
/// <remarks>
/// The same shape as <c>conversation_human_replies</c>, deliberately: a small row
/// written in the same transaction as the change it records, carrying the
/// workspace so the actor's membership can be a composite key into it. Its own
/// table rather than <c>System</c> messages, because every public transport reads
/// messages and none reads this.
/// </remarks>
internal sealed class ConversationHandlingEventConfiguration
    : IEntityTypeConfiguration<ConversationHandlingEvent>
{
    public void Configure(EntityTypeBuilder<ConversationHandlingEvent> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            PersistenceSchema.ConversationHandlingEventsTable,
            table => table.HasCheckConstraint(
                "ck_conversation_handling_events_after_sequence",
                "after_sequence >= 0"));

        builder.HasKey(change => change.Id);

        builder.Property(change => change.Id)
            .HasColumnName("id")
            .HasConversion(PersistenceConverters.ConversationHandlingEventId)
            .ValueGeneratedNever();

        builder.Property(change => change.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(PersistenceConverters.TenantId)
            .IsRequired();

        builder.Property(change => change.ConversationId)
            .HasColumnName("conversation_id")
            .HasConversion(PersistenceConverters.ConversationId)
            .IsRequired();

        builder.Property(change => change.Kind)
            .HasColumnName("kind")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(change => change.Actor)
            .HasColumnName("actor_user_id")
            .HasConversion(PersistenceConverters.UserId)
            .IsRequired();

        builder.Property(change => change.OccurredAt)
            .HasColumnName("occurred_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(change => change.AfterSequence)
            .HasColumnName("after_sequence")
            .IsRequired();

        // Restrict: a workspace that still has history is not removable by
        // accident. Unindexed, as the reply table's tenant key: these rows are
        // always reached through their conversation.
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(change => change.TenantId)
            .HasConstraintName("fk_conversation_handling_events_tenant")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired()
            .WithoutConventionIndex();

        builder.HasOne<Conversation>()
            .WithMany()
            .HasForeignKey(change => change.ConversationId)
            .HasConstraintName("fk_conversation_handling_events_conversation")
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        // The actor must be a member of the conversation's workspace: the same
        // composite guarantee as a reply's sender. Restrict, so who did it cannot
        // vanish with a membership; unindexed, as fk_human_replies_sender_member,
        // because nothing looks events up by actor.
        builder.HasOne<TenantMembership>()
            .WithMany()
            .HasForeignKey(change => new { change.Actor, change.TenantId })
            .HasPrincipalKey(membership => new { membership.UserId, membership.TenantId })
            .HasConstraintName("fk_conversation_handling_events_actor_member")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired()
            .WithoutConventionIndex();

        // The transcript reads a span of one conversation by position.
        builder.HasIndex(change => new { change.ConversationId, change.AfterSequence })
            .HasDatabaseName("ix_conversation_handling_events_conversation");

        // No concurrency token: written once, never updated.
    }
}
