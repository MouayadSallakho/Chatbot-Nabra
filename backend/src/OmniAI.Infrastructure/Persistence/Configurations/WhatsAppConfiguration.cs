using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.WhatsApp;

namespace OmniAI.Infrastructure.Persistence.Configurations;

/// <summary>
/// A workspace's WhatsApp connection.
/// </summary>
/// <remarks>
/// The access token is deliberately absent. It lives in its own table, so the
/// row every list, every status check and every webhook lookup reads carries no
/// credential at all.
/// </remarks>
internal sealed class WhatsAppConnectionConfiguration
    : IEntityTypeConfiguration<WhatsAppConnection>
{
    public void Configure(EntityTypeBuilder<WhatsAppConnection> builder)
    {
        builder.ToTable(PersistenceSchema.WhatsAppConnectionsTable);

        builder.HasKey(connection => connection.Id);

        builder.Property(connection => connection.Id)
            .HasColumnName("id")
            .HasConversion(PersistenceConverters.WhatsAppConnectionId)
            .ValueGeneratedNever();

        builder.Property(connection => connection.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(PersistenceConverters.TenantId)
            .IsRequired();

        builder.Property(connection => connection.AiAgentId)
            .HasColumnName("ai_agent_id")
            .HasConversion(PersistenceConverters.AiAgentId)
            .IsRequired();

        builder.Property(connection => connection.WabaId)
            .HasColumnName("waba_id")
            .HasConversion(PersistenceConverters.WabaId)
            .HasMaxLength(WabaId.MaxLength)
            .IsRequired();

        builder.Property(connection => connection.PhoneNumberId)
            .HasColumnName("phone_number_id")
            .HasConversion(PersistenceConverters.PhoneNumberId)
            .HasMaxLength(PhoneNumberId.MaxLength)
            .IsRequired();

        builder.Property(connection => connection.DisplayPhoneNumber)
            .HasColumnName("display_phone_number")
            .HasMaxLength(32);

        builder.Property(connection => connection.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(connection => connection.HasCredential)
            .HasColumnName("has_credential")
            .IsRequired();

        builder.Property(connection => connection.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(connection => connection.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(connection => connection.TenantId)
            .HasConstraintName("fk_whatsapp_connections_tenant")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        // The agent a connection answers with must belong to the same workspace.
        // A composite key into the agent's own (id, tenant) pair would be the
        // stronger form; agents are keyed on id alone, so this is a plain
        // reference and the use case checks the workspace.
        builder.HasOne<AiAgent>()
            .WithMany()
            .HasForeignKey(connection => connection.AiAgentId)
            .HasConstraintName("fk_whatsapp_connections_agent")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        // <strong>Deployment-wide, not per workspace.</strong> An inbound
        // webhook is routed by phone number id and nothing else, so two
        // connections claiming one number would make routing ambiguous -- and
        // ambiguous here means one workspace's customers reaching another
        // workspace's inbox. The index is what makes that unrepresentable rather
        // than merely unlikely.
        builder.HasIndex(connection => connection.PhoneNumberId)
            .IsUnique()
            .HasDatabaseName("ux_whatsapp_connections_phone_number");

        builder.HasIndex(connection => connection.TenantId)
            .HasDatabaseName("ix_whatsapp_connections_tenant");

        // Declared rather than left to the scaffolder, which would create one
        // under its own name. It serves no query -- the list joins on the agent's
        // primary key -- but a restricting foreign key without an index makes
        // deleting an agent a scan of every connection on the deployment.
        builder.HasIndex(connection => connection.AiAgentId)
            .HasDatabaseName("ix_whatsapp_connections_agent");

        builder.Property<uint>(PersistenceSchema.XminConcurrencyToken)
            .HasColumnName(PersistenceSchema.XminConcurrencyToken)
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();
    }
}

/// <summary>
/// The protected access token for a connection.
/// </summary>
/// <remarks>
/// <strong>Its own table, and that is the whole design decision.</strong> A
/// credential column on the connection would be read by every list screen, every
/// status check and every webhook routing lookup — carrying ciphertext through
/// memory that has no use for it, and making "does any read return the token"
/// a question about every query rather than about one.
/// <para>
/// Here, exactly one repository method reads this table, and it is the one the
/// outbound worker calls immediately before sending.
/// </para>
/// </remarks>
internal sealed class WhatsAppCredentialConfiguration
    : IEntityTypeConfiguration<WhatsAppCredentialRow>
{
    public void Configure(EntityTypeBuilder<WhatsAppCredentialRow> builder)
    {
        builder.ToTable(PersistenceSchema.WhatsAppCredentialsTable);

        builder.HasKey(credential => credential.ConnectionId);

        builder.Property(credential => credential.ConnectionId)
            .HasColumnName("whatsapp_connection_id")
            .HasConversion(PersistenceConverters.WhatsAppConnectionId)
            .ValueGeneratedNever();

        // bytea. The ciphertext is opaque: a version byte, a nonce, a tag and
        // the encrypted token. Nothing in the database can read it, and nothing
        // in the database needs to.
        builder.Property(credential => credential.ProtectedToken)
            .HasColumnName("protected_token")
            .IsRequired();

        builder.Property(credential => credential.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne<WhatsAppConnection>()
            .WithOne()
            .HasForeignKey<WhatsAppCredentialRow>(credential => credential.ConnectionId)
            .HasConstraintName("fk_whatsapp_credentials_connection")
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();
    }
}

/// <summary>Which conversation a WhatsApp customer's messages join.</summary>
internal sealed class WhatsAppContactConfiguration : IEntityTypeConfiguration<WhatsAppContact>
{
    public void Configure(EntityTypeBuilder<WhatsAppContact> builder)
    {
        builder.ToTable(PersistenceSchema.WhatsAppContactsTable);

        builder.HasKey(contact => contact.Id);

        builder.Property(contact => contact.Id)
            .HasColumnName("id")
            .HasConversion(PersistenceConverters.WhatsAppContactId)
            .ValueGeneratedNever();

        builder.Property(contact => contact.ConnectionId)
            .HasColumnName("whatsapp_connection_id")
            .HasConversion(PersistenceConverters.WhatsAppConnectionId)
            .IsRequired();

        builder.Property(contact => contact.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(PersistenceConverters.TenantId)
            .IsRequired();

        // Personal data. Stored because sending a reply requires it, and for no
        // other reason -- no profile name, no locale, nothing Meta also sends.
        builder.Property(contact => contact.CustomerId)
            .HasColumnName("customer_id")
            .HasConversion(PersistenceConverters.WhatsAppCustomerId)
            .HasMaxLength(WhatsAppCustomerId.MaxLength)
            .IsRequired();

        builder.Property(contact => contact.ConversationId)
            .HasColumnName("conversation_id")
            .HasConversion(PersistenceConverters.ConversationId)
            .IsRequired();

        builder.Property(contact => contact.StartedAt)
            .HasColumnName("started_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(contact => contact.IsCurrent)
            .HasColumnName("is_current")
            .IsRequired();

        builder.HasOne<WhatsAppConnection>()
            .WithMany()
            .HasForeignKey(contact => contact.ConnectionId)
            .HasConstraintName("fk_whatsapp_contacts_connection")
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        builder.HasOne<Conversation>()
            .WithMany()
            .HasForeignKey(contact => contact.ConversationId)
            .HasConstraintName("fk_whatsapp_contacts_conversation")
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        // Finding the conversation a reply goes back to.
        builder.HasIndex(contact => contact.ConversationId)
            .HasDatabaseName("ix_whatsapp_contacts_conversation");

        // Every mapping of a connection, including superseded ones. The partial
        // unique index below covers only the current mapping, so it cannot serve
        // the cascade when a connection is removed.
        builder.HasIndex(contact => contact.ConnectionId)
            .HasDatabaseName("ix_whatsapp_contacts_connection");

        // One current conversation per customer per number. Partial, because
        // superseded mappings are kept: without the filter a customer could only
        // ever have one conversation in their life.
        builder.HasIndex(contact => new { contact.ConnectionId, contact.CustomerId })
            .IsUnique()
            .HasDatabaseName("ux_whatsapp_contacts_current")
            .HasFilter("is_current");
    }
}

/// <summary>Every webhook message the platform has accepted responsibility for.</summary>
/// <remarks>
/// <strong>This table is what lets the webhook answer Meta immediately.</strong>
/// Meta retries anything it does not get a prompt acknowledgement for, so the
/// request handler does the least it can: verify the signature, write the row,
/// reply. Everything expensive — the lock, retrieval, generation — happens later,
/// in a worker, at whatever pace the work actually takes.
/// <para>
/// The raw payload is deliberately not stored. What the platform needs is the
/// sender, the text and the provider's id; the rest is Meta's envelope, and
/// keeping it would mean retaining personal data nothing reads.
/// </para>
/// </remarks>
internal sealed class InboundWhatsAppMessageConfiguration
    : IEntityTypeConfiguration<InboundWhatsAppMessage>
{
    public void Configure(EntityTypeBuilder<InboundWhatsAppMessage> builder)
    {
        builder.ToTable(PersistenceSchema.InboundWhatsAppMessagesTable);

        builder.HasKey(message => message.Id);

        builder.Property(message => message.Id)
            .HasColumnName("id")
            .HasConversion(PersistenceConverters.InboundWhatsAppMessageId)
            .ValueGeneratedNever();

        builder.Property(message => message.ConnectionId)
            .HasColumnName("whatsapp_connection_id")
            .HasConversion(PersistenceConverters.WhatsAppConnectionId)
            .IsRequired();

        builder.Property(message => message.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(PersistenceConverters.TenantId)
            .IsRequired();

        builder.Property(message => message.ProviderMessageId)
            .HasColumnName("provider_message_id")
            .HasConversion(PersistenceConverters.ProviderMessageId)
            .HasMaxLength(ProviderMessageId.MaxLength)
            .IsRequired();

        builder.Property(message => message.CustomerId)
            .HasColumnName("customer_id")
            .HasConversion(PersistenceConverters.WhatsAppCustomerId)
            .HasMaxLength(WhatsAppCustomerId.MaxLength)
            .IsRequired();

        // Null for anything that is not text. An unsupported message is recorded
        // as having arrived and never given a body, so no later reader can mistake
        // a placeholder for something a customer typed.
        builder.Property(message => message.Text)
            .HasColumnName("text")
            .HasMaxLength(InboundWhatsAppMessage.MaxTextLength);

        builder.Property(message => message.SentAt)
            .HasColumnName("sent_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(message => message.ReceivedAt)
            .HasColumnName("received_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(message => message.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(message => message.Attempts)
            .HasColumnName("attempts")
            .IsRequired();

        builder.Property(message => message.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne<WhatsAppConnection>()
            .WithMany()
            .HasForeignKey(message => message.ConnectionId)
            .HasConstraintName("fk_whatsapp_inbound_connection")
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        // <strong>The inbound idempotency boundary.</strong> Meta redelivers a
        // webhook whenever it is unsure the first attempt landed, which is
        // routine rather than exceptional. Two redeliveries racing each other
        // both pass any check made in application code; this index is the one
        // place that can decide, and the loser's insert is caught and treated as
        // the duplicate it is.
        builder.HasIndex(message => new { message.ConnectionId, message.ProviderMessageId })
            .IsUnique()
            .HasDatabaseName("ux_whatsapp_inbound_provider_message");

        // The worker's claim query: pending work, oldest first.
        builder.HasIndex(message => new { message.Status, message.ReceivedAt })
            .HasDatabaseName("ix_whatsapp_inbound_status_received");

        // No xmin concurrency token. Claiming is an atomic conditional update
        // rather than a read-then-write, so there is no window for two workers to
        // disagree about -- and a token would make an honest claim fail merely
        // because another worker had looked at the row.
    }
}

/// <summary>Every message the platform owes a customer, and what became of it.</summary>
/// <remarks>
/// <strong>A reply is stored before it is sent.</strong> The transcript write and
/// the send are separate: the conversation is committed, a row lands here, and a
/// worker does the network call. A send inside the transcript's transaction would
/// either hold a row lock across somebody else's network or roll back after Meta
/// had already delivered the message.
/// <para>
/// The text is duplicated from the message rather than read through a join. What
/// was sent must stay what was sent: a later edit to the transcript must not
/// silently change the record of what the customer received.
/// </para>
/// </remarks>
internal sealed class WhatsAppDeliveryConfiguration : IEntityTypeConfiguration<WhatsAppDelivery>
{
    public void Configure(EntityTypeBuilder<WhatsAppDelivery> builder)
    {
        builder.ToTable(PersistenceSchema.WhatsAppDeliveriesTable);

        builder.HasKey(delivery => delivery.Id);

        builder.Property(delivery => delivery.Id)
            .HasColumnName("id")
            .HasConversion(PersistenceConverters.WhatsAppDeliveryId)
            .ValueGeneratedNever();

        builder.Property(delivery => delivery.ConnectionId)
            .HasColumnName("whatsapp_connection_id")
            .HasConversion(PersistenceConverters.WhatsAppConnectionId)
            .IsRequired();

        builder.Property(delivery => delivery.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(PersistenceConverters.TenantId)
            .IsRequired();

        builder.Property(delivery => delivery.ConversationId)
            .HasColumnName("conversation_id")
            .HasConversion(PersistenceConverters.ConversationId)
            .IsRequired();

        builder.Property(delivery => delivery.MessageId)
            .HasColumnName("message_id")
            .HasConversion(PersistenceConverters.MessageId)
            .IsRequired();

        builder.Property(delivery => delivery.Recipient)
            .HasColumnName("recipient")
            .HasConversion(PersistenceConverters.WhatsAppCustomerId)
            .HasMaxLength(WhatsAppCustomerId.MaxLength)
            .IsRequired();

        builder.Property(delivery => delivery.Text)
            .HasColumnName("text")
            .HasMaxLength(WhatsAppDelivery.MaxTextLength)
            .IsRequired();

        builder.Property(delivery => delivery.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        // Null until the provider names the message, and permanently null if it
        // never did. It is the only key a later status webhook can be matched on.
        builder.Property(delivery => delivery.ProviderMessageId)
            .HasColumnName("provider_message_id")
            .HasConversion(PersistenceConverters.NullableProviderMessageId)
            .HasMaxLength(ProviderMessageId.MaxLength);

        // A sentence for a colleague reading the inbox, never the provider's raw
        // body -- which echoes the message text and the request metadata.
        builder.Property(delivery => delivery.FailureReason)
            .HasColumnName("failure_reason")
            .HasMaxLength(WhatsAppDelivery.MaxReasonLength);

        builder.Property(delivery => delivery.Attempts)
            .HasColumnName("attempts")
            .IsRequired();

        builder.Property(delivery => delivery.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(delivery => delivery.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne<WhatsAppConnection>()
            .WithMany()
            .HasForeignKey(delivery => delivery.ConnectionId)
            .HasConstraintName("fk_whatsapp_deliveries_connection")
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        builder.HasOne<Conversation>()
            .WithMany()
            .HasForeignKey(delivery => delivery.ConversationId)
            .HasConstraintName("fk_whatsapp_deliveries_conversation")
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        builder.HasOne<Message>()
            .WithMany()
            .HasForeignKey(delivery => delivery.MessageId)
            .HasConstraintName("fk_whatsapp_deliveries_message")
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        // <strong>One message is delivered once.</strong> Two rows for one
        // message means the customer reads the same reply twice, and no amount of
        // care in the code that queues them rules that out under concurrency.
        builder.HasIndex(delivery => delivery.MessageId)
            .IsUnique()
            .HasDatabaseName("ux_whatsapp_deliveries_message");

        // Matching a status webhook back to the delivery it refers to. Not
        // unique: the column is null for everything not yet accepted, and a
        // plain unique index would still have to be scanned for the many rows
        // that share no id at all.
        builder.HasIndex(delivery => delivery.ProviderMessageId)
            .HasDatabaseName("ix_whatsapp_deliveries_provider_message");

        // The dispatcher's claim query, and the inbox's per-conversation read.
        builder.HasIndex(delivery => new { delivery.Status, delivery.CreatedAt })
            .HasDatabaseName("ix_whatsapp_deliveries_status_created");

        // The analytics predicate, added in Phase 22.
        //
        // The dispatcher asks "what is still pending", which leads with status.
        // A dashboard asks "what did this workspace send in this window", which
        // leads with the workspace -- and until now this table had no index on
        // its tenant column at all, so that question read every delivery row in
        // the deployment to answer for one workspace.
        builder.HasIndex(delivery => new { delivery.TenantId, delivery.CreatedAt })
            .HasDatabaseName("ix_whatsapp_deliveries_tenant_created");

        builder.HasIndex(delivery => delivery.ConversationId)
            .HasDatabaseName("ix_whatsapp_deliveries_conversation");

        // For the cascade when a connection is removed.
        builder.HasIndex(delivery => delivery.ConnectionId)
            .HasDatabaseName("ix_whatsapp_deliveries_connection");

        // No xmin concurrency token, for the same reason as the inbound table:
        // claiming is a conditional update, not a read-then-write.
    }
}
