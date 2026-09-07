using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.WebChat;

namespace OmniAI.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps a website chat endpoint.
/// </summary>
internal sealed class WebChatEndpointConfiguration : IEntityTypeConfiguration<WebChatEndpoint>
{
    public void Configure(EntityTypeBuilder<WebChatEndpoint> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("web_chat_endpoints");

        builder.HasKey(endpoint => endpoint.Id);

        builder.Property(endpoint => endpoint.Id)
            .HasColumnName("id")
            .HasConversion(PersistenceConverters.WebChatEndpointId)
            .ValueGeneratedNever();

        builder.Property(endpoint => endpoint.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(PersistenceConverters.TenantId)
            .IsRequired();

        builder.Property(endpoint => endpoint.AiAgentId)
            .HasColumnName("ai_agent_id")
            .HasConversion(PersistenceConverters.AiAgentId)
            .IsRequired();

        // Stored in the clear, and indexed for equality. This value is published
        // in every hosted chat URL; hashing it would buy nothing and would only
        // make the lookup an index on a digest of something already public.
        builder.Property(endpoint => endpoint.PublicKey)
            .HasColumnName("public_key")
            .HasConversion(PersistenceConverters.WebChatPublicKey)
            .HasMaxLength(WebChatPublicKey.Length)
            .IsRequired();

        builder.Property(endpoint => endpoint.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(endpoint => endpoint.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(endpoint => endpoint.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        // The row version behind an endpoint's ETag. PostgreSQL's own transaction
        // stamp, mapped as a shadow property -- never a column this platform
        // declares. A physical column of this name would shadow the system one and
        // silently disable every concurrency check built on it.
        builder.Property<uint>(PersistenceSchema.XminConcurrencyToken)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(endpoint => endpoint.TenantId)
            .HasConstraintName("fk_web_chat_endpoints_tenant")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        // A composite key onto the agent's alternate key, so the endpoint and the
        // agent must belong to the same workspace. The same construction role
        // assignments use: a cross-tenant pairing is not something to validate
        // against, it is a row the database will not accept.
        builder.HasOne<AiAgent>()
            .WithMany()
            .HasForeignKey(endpoint => new { endpoint.AiAgentId, endpoint.TenantId })
            .HasPrincipalKey(agent => new { agent.Id, agent.TenantId })
            .HasConstraintName("fk_web_chat_endpoints_agent")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        // Not to prevent duplicates -- id is already the primary key. PostgreSQL
        // requires a unique constraint on the columns a foreign key references,
        // and web_chat_sessions references exactly this pair so a session cannot
        // name an endpoint from one workspace while claiming another. Declared
        // rather than left to EF's inference, so the constraint has a name that
        // matches the rest of the schema.
        builder.HasAlternateKey(endpoint => new { endpoint.Id, endpoint.TenantId })
            .HasName("ak_web_chat_endpoints_id_tenant");

        // Globally unique. A public key names exactly one endpoint across the
        // whole deployment, which is what lets the public lookup take no tenant.
        builder.HasIndex(endpoint => endpoint.PublicKey)
            .IsUnique()
            .HasDatabaseName("ux_web_chat_endpoints_public_key");

        // One chat link per agent per workspace, so a workspace cannot accumulate
        // several public URLs for the same agent that all have to be remembered
        // and withdrawn separately.
        builder.HasIndex(endpoint => new { endpoint.TenantId, endpoint.AiAgentId })
            .IsUnique()
            .HasDatabaseName("ux_web_chat_endpoints_tenant_agent");
    }
}

/// <summary>
/// Maps one anonymous visitor's claim on one conversation.
/// </summary>
internal sealed class WebChatSessionConfiguration : IEntityTypeConfiguration<WebChatSession>
{
    public void Configure(EntityTypeBuilder<WebChatSession> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("web_chat_sessions");

        // The conversation is the identity, which is what makes a second claim on
        // somebody else's conversation unrepresentable rather than merely refused.
        builder.HasKey(session => session.ConversationId);

        builder.Property(session => session.ConversationId)
            .HasColumnName("conversation_id")
            .HasConversion(PersistenceConverters.ConversationId)
            .ValueGeneratedNever();

        builder.Property(session => session.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(PersistenceConverters.TenantId)
            .IsRequired();

        builder.Property(session => session.EndpointId)
            .HasColumnName("web_chat_endpoint_id")
            .HasConversion(PersistenceConverters.WebChatEndpointId)
            .IsRequired();

        builder.Property(session => session.AiAgentId)
            .HasColumnName("ai_agent_id")
            .HasConversion(PersistenceConverters.AiAgentId)
            .IsRequired();

        // The digest, as bytes. There is deliberately no column anywhere in this
        // model that could hold the raw secret, and no converter that could write
        // one -- the only type the property has is the digest.
        builder.Property(session => session.AccessTokenHash)
            .HasColumnName("access_token_hash")
            .HasConversion(PersistenceConverters.WebChatAccessTokenHash)
            .HasColumnType("bytea")
            .IsRequired();

        builder.Property(session => session.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne<Conversation>()
            .WithMany()
            .HasForeignKey(session => session.ConversationId)
            .HasConstraintName("fk_web_chat_sessions_conversation")
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        // Both composite, both reading the same tenant_id, so a session cannot
        // name an endpoint from one workspace while claiming another.
        builder.HasOne<WebChatEndpoint>()
            .WithMany()
            .HasForeignKey(session => new { session.EndpointId, session.TenantId })
            .HasPrincipalKey(endpoint => new { endpoint.Id, endpoint.TenantId })
            .HasConstraintName("fk_web_chat_sessions_endpoint")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.HasOne<AiAgent>()
            .WithMany()
            .HasForeignKey(session => new { session.AiAgentId, session.TenantId })
            .HasPrincipalKey(agent => new { agent.Id, agent.TenantId })
            .HasConstraintName("fk_web_chat_sessions_agent")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        // Unique, so two conversations cannot share a credential. Without this,
        // a digest collision -- however it arose -- would mean one secret opened
        // two conversations.
        builder.HasIndex(session => session.AccessTokenHash)
            .IsUnique()
            .HasDatabaseName("ux_web_chat_sessions_access_token_hash");
    }
}

/// <summary>
/// Maps the durable record of one turn.
/// </summary>
internal sealed class ConversationTurnConfiguration : IEntityTypeConfiguration<ConversationTurn>
{
    public void Configure(EntityTypeBuilder<ConversationTurn> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("conversation_turns");

        builder.HasKey(turn => turn.Id);

        builder.Property(turn => turn.Id)
            .HasColumnName("id")
            .HasConversion(PersistenceConverters.ConversationTurnId)
            .ValueGeneratedNever();

        builder.Property(turn => turn.ConversationId)
            .HasColumnName("conversation_id")
            .HasConversion(PersistenceConverters.ConversationId)
            .IsRequired();

        builder.Property(turn => turn.ClientMessageId)
            .HasColumnName("client_message_id")
            .HasConversion(PersistenceConverters.ClientMessageId)
            .HasMaxLength(ClientMessageId.MaxLength)
            .IsRequired();

        builder.Property(turn => turn.CustomerMessageId)
            .HasColumnName("customer_message_id")
            .HasConversion(PersistenceConverters.MessageId)
            .IsRequired();

        builder.Property(turn => turn.ReplyMessageId)
            .HasColumnName("reply_message_id")
            .HasConversion(PersistenceConverters.NullableMessageId);

        builder.Property(turn => turn.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(turn => turn.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(turn => turn.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne<Conversation>()
            .WithMany()
            .HasForeignKey(turn => turn.ConversationId)
            .HasConstraintName("fk_conversation_turns_conversation")
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        builder.HasOne<Message>()
            .WithMany()
            .HasForeignKey(turn => turn.CustomerMessageId)
            .HasConstraintName("fk_conversation_turns_customer_message")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        // <strong>The idempotency boundary, and it lives here rather than in a
        // handler.</strong> Two concurrent requests carrying the same client
        // message id both pass any in-process check; the only place that can
        // settle the race is this index. A duplicate loses the insert and is told
        // its message is already being answered, which is what stops a retry from
        // buying a second reply.
        builder.HasIndex(turn => new { turn.ConversationId, turn.ClientMessageId })
            .IsUnique()
            .HasDatabaseName("ux_conversation_turns_client_message");

        builder.HasIndex(turn => turn.CustomerMessageId)
            .IsUnique()
            .HasDatabaseName("ux_conversation_turns_customer_message");
    }
}
