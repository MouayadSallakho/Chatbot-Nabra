using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OmniAI.Application.Conversations;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Customers;
using OmniAI.Domain.Tenants;

namespace OmniAI.Infrastructure.Persistence.Repositories;

internal sealed class MessengerSimulatorReceipt
{
    public MessageId MessageId { get; init; }
    public DateTimeOffset ReceivedAt { get; init; }
}

internal sealed class MessengerReceiptConfiguration : IEntityTypeConfiguration<MessengerSimulatorReceipt>
{
    public void Configure(EntityTypeBuilder<MessengerSimulatorReceipt> builder)
    {
        builder.ToTable("messenger_simulator_receipts");
        builder.HasKey(row => row.MessageId);
        builder.Property(row => row.MessageId).HasColumnName("message_id").HasConversion(PersistenceConverters.MessageId).ValueGeneratedNever();
        builder.Property(row => row.ReceivedAt).HasColumnName("received_at");
        builder.HasOne<Message>().WithOne().HasForeignKey<MessengerSimulatorReceipt>(row => row.MessageId);
    }
}

internal sealed class MessengerSimulatorThread
{
    public TenantId TenantId { get; init; }
    public AiAgentId AgentId { get; init; }
    public Guid Visitor { get; init; }
    public ConversationId ConversationId { get; set; }
}

internal sealed class MessengerSimulatorConfiguration : IEntityTypeConfiguration<MessengerSimulatorThread>
{
    public void Configure(EntityTypeBuilder<MessengerSimulatorThread> builder)
    {
        builder.ToTable("messenger_simulator_threads");
        builder.HasKey(row => new { row.TenantId, row.AgentId, row.Visitor });
        builder.Property(row => row.TenantId).HasColumnName("tenant_id").HasConversion(PersistenceConverters.TenantId);
        builder.Property(row => row.AgentId).HasColumnName("agent_id").HasConversion(PersistenceConverters.AiAgentId);
        builder.Property(row => row.Visitor).HasColumnName("visitor");
        builder.Property(row => row.ConversationId).HasColumnName("conversation_id").HasConversion(PersistenceConverters.ConversationId);
        builder.HasOne<AiAgent>().WithMany().HasForeignKey(row => row.AgentId);
        builder.HasOne<Conversation>().WithMany().HasForeignKey(row => row.ConversationId);
        builder.HasIndex(row => row.ConversationId).IsUnique();
    }
}

internal sealed class MessengerSimulatorStore(OmniAiDbContext context, IConversationLock locks) : IMessengerSimulatorStore
{
    public async Task<IReadOnlyList<SimulatorAgent>> AgentsAsync(TenantId tenant, CancellationToken token) =>
        await context.AiAgents.AsNoTracking().Where(agent => agent.TenantId == tenant && agent.Status == AiAgentStatus.Active)
            .OrderBy(agent => agent.Id).Select(agent => new SimulatorAgent(agent.Id.Value, agent.Name.Value))
            .ToListAsync(token).ConfigureAwait(false);

    private IQueryable<MessengerSimulatorThread> Threads(SimulatorAddress address) =>
        context.Set<MessengerSimulatorThread>().Where(row => row.TenantId == address.Tenant && row.AgentId == address.Agent && row.Visitor == address.Visitor);

    public async Task<ConversationId> ResolveAsync(SimulatorAddress address, DateTimeOffset now, CancellationToken token)
    {
        // Serialise first contact on the simulated Page. The turn runner separately locks the conversation.
        await using var hold = await locks.AcquireAsync(address.Agent.Value, token).ConfigureAwait(false);
        var thread = await Threads(address).SingleOrDefaultAsync(token).ConfigureAwait(false);
        if (thread is not null)
        {
            return thread.ConversationId;
        }

        var customer = Customer.Create(address.Tenant, CustomerDisplayName.Create("Messenger · " + Guid.NewGuid().ToString("N")[..8]), now);
        var conversation = Conversation.Start(address.Tenant, customer.Id, ChannelKind.Messenger, now);
        context.Customers.Add(customer);
        context.Conversations.Add(conversation);
        context.Add(new MessengerSimulatorThread { TenantId = address.Tenant, AgentId = address.Agent,
            Visitor = address.Visitor, ConversationId = conversation.Id });
        await context.SaveChangesAsync(token).ConfigureAwait(false);
        return conversation.Id;
    }

    public async Task<SimulatorMailbox> ReceiveAsync(SimulatorAddress address, CancellationToken token)
    {
        var thread = await Threads(address).AsNoTracking().SingleOrDefaultAsync(token).ConfigureAwait(false);
        if (thread is null)
        {
            return new SimulatorMailbox(null, []);
        }

        // The simulator is a pull transport. A committed AI or human message is available
        // after a restart; concurrent or repeated pulls acknowledge each message only once.
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO messenger_simulator_receipts (message_id, received_at)
            SELECT m.id, now() FROM messages m
             WHERE m.conversation_id = {thread.ConversationId.Value} AND m.sender IN ('AiAgent', 'HumanAgent')
               AND NOT EXISTS (SELECT 1 FROM messenger_simulator_receipts r WHERE r.message_id = m.id)
             ORDER BY m.ingestion_sequence
            ON CONFLICT (message_id) DO NOTHING
            """, token).ConfigureAwait(false);

        // Never deliver system notes, author addresses, or an outbound message not yet collected.
        var rows = await context.Set<Message>().AsNoTracking()
            .Where(message => message.ConversationId == thread.ConversationId &&
                (message.Sender == MessageSender.Customer || context.Set<MessengerSimulatorReceipt>().Any(receipt => receipt.MessageId == message.Id)))
            .OrderByDescending(message => EF.Property<long>(message, PersistenceSchema.MessageIngestionSequence))
            .Take(40).Select(message => new { message.Id, message.Sender, message.Text, message.SentAt })
            .ToListAsync(token).ConfigureAwait(false);
        rows.Reverse();
        return new SimulatorMailbox(thread.ConversationId.Value,
            [.. rows.Select(row => new SimulatorMessage(row.Id.Value, row.Sender.ToString(), row.Text, row.SentAt))]);
    }
}
