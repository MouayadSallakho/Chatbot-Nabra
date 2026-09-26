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

        // The widget's appearance lives on the endpoint rather than in a table of
        // its own: it is exactly one row's worth of settings, it is read on every
        // embedded bootstrap, and a join to fetch four columns would buy nothing.
        //
        // Each is a bounded value with a closed set of possibilities. There is no
        // column here that could carry CSS, markup or a script, and none may be
        // added -- a widget whose appearance is tenant-supplied markup is a widget
        // whose appearance is a stored cross-site scripting vector.
        builder.ComplexProperty(
            endpoint => endpoint.Widget,
            widget =>
            {
                widget.Property(settings => settings.Greeting)
                    .HasColumnName("widget_greeting")
                    .HasConversion(PersistenceConverters.NullableWebChatGreeting)
                    .HasMaxLength(WebChatGreeting.MaxLength);

                widget.Property(settings => settings.Accent)
                    .HasColumnName("widget_accent_color")
                    .HasConversion(PersistenceConverters.WebChatAccentColor)
                    .HasMaxLength(7)
                    .IsRequired();

                // Stored as the name, never the ordinal. A reordered enum would
                // silently repoint every existing row.
                widget.Property(settings => settings.Position)
                    .HasColumnName("widget_launcher_position")
                    .HasConversion<string>()
                    .HasMaxLength(16)
                    .IsRequired();

                widget.Property(settings => settings.Theme)
                    .HasColumnName("widget_theme")
                    .HasConversion<string>()
                    .HasMaxLength(16)
                    .IsRequired();
            });

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

        // The approved websites are part of this aggregate, so they load and save
        // with it and are covered by its entity tag. Changing the list moves the
        // endpoint's version, which is what makes the admin screen's precondition
        // mean something.
        builder.HasMany(endpoint => endpoint.AllowedOrigins)
            .WithOne()
            .HasForeignKey(allowed => new { allowed.EndpointId, allowed.TenantId })
            .HasPrincipalKey(endpoint => new { endpoint.Id, endpoint.TenantId })
            .HasConstraintName("fk_web_chat_allowed_origins_endpoint")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(endpoint => endpoint.AllowedOrigins)
            .HasField("_allowedOrigins")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .AutoInclude();
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

        // An embedded session always knows its origin and a hosted one never has
        // one: the two columns cannot disagree.
        builder.ToTable("web_chat_sessions", table =>
            table.HasCheckConstraint(
                "ck_web_chat_sessions_embedded_origin",
                "(client_mode = 'Embedded' AND embedded_origin IS NOT NULL) "
                + "OR (client_mode <> 'Embedded' AND embedded_origin IS NULL)"));

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

        // Stored rather than derived. The alternative -- computing the expiry from
        // created_at and the current configuration on every check -- would mean
        // changing the platform's session lifetime retroactively lengthened or
        // revoked credentials people were already holding, and would leave no
        // record of what any given session was actually promised.
        builder.Property(session => session.ExpiresAt)
            .HasColumnName("expires_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        // Which transport opened this session, and therefore which may use it.
        // Stored as the name so a row is readable in a query and so reordering the
        // enum cannot silently reclassify every existing session.
        builder.Property(session => session.Mode)
            .HasColumnName("client_mode")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        // The origin only -- never the page address. A path can carry an order
        // number, a search term or a customer identifier, and none of that is this
        // platform's business. Null for a hosted session, and the table's check
        // constraint keeps the two columns agreeing with each other.
        builder.Property(session => session.EmbeddedOrigin)
            .HasColumnName("embedded_origin")
            .HasConversion(PersistenceConverters.NullableWebChatOrigin)
            .HasMaxLength(WebChatOrigin.MaxLength);

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

        // Not for the access check, which finds a session by its conversation and
        // then reads the expiry from the row it already has. This exists for the
        // operational question -- how many public sessions are still live, which
        // ones have lapsed -- and for whatever eventually retires them. There is
        // deliberately no such worker in this release: nothing depends on expired
        // rows being removed, because an expired session is already refused by the
        // stored instant rather than by its absence.
        builder.HasIndex(session => session.ExpiresAt)
            .HasDatabaseName("ix_web_chat_sessions_expires_at");
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

/// <summary>
/// The websites a widget may be embedded on.
/// </summary>
/// <remarks>
/// <para>
/// A table rather than a JSON column on the endpoint. That is what lets the
/// database refuse a duplicate outright, and it is what makes the list something
/// a query can filter on rather than a string something has to parse.
/// </para>
/// <para>
/// <strong>The primary key is the tenant boundary and the uniqueness rule at
/// once.</strong> (endpoint, origin) makes the same website twice impossible; the
/// composite foreign key onto (id, tenant_id) makes an origin belonging to another
/// workspace's endpoint impossible. Neither depends on a handler remembering.
/// </para>
/// </remarks>
internal sealed class WebChatAllowedOriginConfiguration
    : IEntityTypeConfiguration<WebChatAllowedOrigin>
{
    public void Configure(EntityTypeBuilder<WebChatAllowedOrigin> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(PersistenceSchema.WebChatAllowedOriginsTable);

        builder.HasKey(allowed => new { allowed.EndpointId, allowed.Origin })
            .HasName("pk_web_chat_allowed_origins");

        builder.Property(allowed => allowed.EndpointId)
            .HasColumnName("web_chat_endpoint_id")
            .HasConversion(PersistenceConverters.WebChatEndpointId)
            .IsRequired();

        builder.Property(allowed => allowed.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(PersistenceConverters.TenantId)
            .IsRequired();

        builder.Property(allowed => allowed.Origin)
            .HasColumnName("origin")
            .HasConversion(PersistenceConverters.WebChatOrigin)
            .HasMaxLength(WebChatOrigin.MaxLength)
            .IsRequired();

        builder.Property(allowed => allowed.AllowedAt)
            .HasColumnName("allowed_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
    }
}
