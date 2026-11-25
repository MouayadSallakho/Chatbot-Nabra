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
    // Receipt is written by the simulated Page independently of the sender's confirmation.
    public DateTimeOffset? ReceivedAt { get; set; }
    public SimulatorSendStatus Status { get; set; }
}

internal sealed class MessengerReceiptConfiguration : IEntityTypeConfiguration<MessengerSimulatorReceipt>
{
    public void Configure(EntityTypeBuilder<MessengerSimulatorReceipt> builder)
    {
        builder.ToTable("messenger_simulator_receipts");
        builder.HasKey(row => row.MessageId);
        builder.Property(row => row.MessageId).HasColumnName("message_id").HasConversion(PersistenceConverters.MessageId).ValueGeneratedNever();
        builder.Property(row => row.ReceivedAt).HasColumnName("received_at");
        builder.Property(row => row.Status).HasColumnName("status").HasConversion<string>()
            .HasMaxLength(16).HasDefaultValue(SimulatorSendStatus.Confirmed);
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

internal sealed class SocialSimulatorStore(OmniAiDbContext context, IConversationLock locks, IConversationRealtimeNotifier notifier) : ISocialSimulatorStore
{
    public async Task<IReadOnlyList<SimulatorAgent>> AgentsAsync(TenantId tenant, CancellationToken token) =>
        await context.AiAgents.AsNoTracking().Where(agent => agent.TenantId == tenant && agent.Status == AiAgentStatus.Active)
            .OrderBy(agent => agent.Id).Select(agent => new SimulatorAgent(agent.Id.Value, agent.Name.Value))
            .ToListAsync(token).ConfigureAwait(false);

    private IQueryable<ConversationId?> ConversationFor(SimulatorAddress address) =>
        address.Channel == ChannelKind.Instagram
            ? context.Set<InstagramSimulatorThread>().Where(r => r.TenantId == address.Tenant && r.AgentId == address.Agent && r.Visitor == address.Visitor)
                .Select(r => (ConversationId?)r.ConversationId)
            : context.Set<MessengerSimulatorThread>().Where(r => r.TenantId == address.Tenant && r.AgentId == address.Agent && r.Visitor == address.Visitor)
                .Select(r => (ConversationId?)r.ConversationId);

    public async Task<ConversationId> ResolveAsync(SimulatorAddress address, DateTimeOffset now, CancellationToken token)
    {
        // Serialise first contact on the simulated Page. The turn runner separately locks the conversation.
        await using var hold = await locks.AcquireAsync(address.Agent.Value, token).ConfigureAwait(false);
        var existing = await ConversationFor(address).SingleOrDefaultAsync(token).ConfigureAwait(false);
        if (existing.HasValue)
        {
            return existing.Value;
        }

        var customer = Customer.Create(address.Tenant, CustomerDisplayName.Create($"{address.Channel} · {Guid.NewGuid().ToString("N")[..8]}"), now);
        var conversation = Conversation.Start(address.Tenant, customer.Id, address.Channel, now);
        context.Customers.Add(customer);
        context.Conversations.Add(conversation);
        if (address.Channel == ChannelKind.Instagram)
        {
            context.Add(new InstagramSimulatorThread { TenantId = address.Tenant, AgentId = address.Agent,
                Visitor = address.Visitor, ConversationId = conversation.Id });
        }
        else
        {
            context.Add(new MessengerSimulatorThread { TenantId = address.Tenant, AgentId = address.Agent,
                Visitor = address.Visitor, ConversationId = conversation.Id });
        }
        await context.SaveChangesAsync(token).ConfigureAwait(false);
        return conversation.Id;
    }

    public async Task<IReadOnlyDictionary<Guid, string>> DeliveryStatusesAsync(TenantId tenant, ConversationId conversation, CancellationToken token)
    {
        var rows = await (from thread in context.Set<MessengerSimulatorThread>().AsNoTracking()
                          join message in context.Set<Message>() on thread.ConversationId equals message.ConversationId
                          join receipt in context.Set<MessengerSimulatorReceipt>() on message.Id equals receipt.MessageId
                          where thread.TenantId == tenant && thread.ConversationId == conversation
                          orderby EF.Property<long>(message, PersistenceSchema.MessageIngestionSequence) descending
                          select new { message.Id, receipt.Status }).Take(40).ToListAsync(token).ConfigureAwait(false);
        return rows.ToDictionary(row => row.Id.Value, row => row.Status.ToString());
    }

    public async Task<SimulatorMailbox> ReceiveAsync(SimulatorAddress address, CancellationToken token,
        SimulatorScenario scenario = SimulatorScenario.Accept, Guid? recover = null)
    {
        var thread = await ConversationFor(address).SingleOrDefaultAsync(token).ConfigureAwait(false);
        if (thread is null)
        {
            return new SimulatorMailbox(null, [], []);
        }

        var conversation = thread.Value;
        if (address.Channel == ChannelKind.Instagram)
        {
            var history = await context.Set<Message>().AsNoTracking()
                .Where(m => m.ConversationId == conversation && (m.Sender == MessageSender.Customer ||
                    m.Sender == MessageSender.AiAgent || m.Sender == MessageSender.HumanAgent))
                .OrderByDescending(m => EF.Property<long>(m, PersistenceSchema.MessageIngestionSequence))
                .Take(40).Select(m => new { m.Id, m.Sender, m.Text, m.SentAt }).ToListAsync(token).ConfigureAwait(false);
            history.Reverse();
            return new SimulatorMailbox(conversation.Value,
                [.. history.Select(m => new SimulatorMessage(m.Id.Value, m.Sender.ToString(), m.Text, m.SentAt))], []);
        }

        var receipts = context.Set<MessengerSimulatorReceipt>();
        var outbound = context.Set<Message>().AsNoTracking().Where(message => message.ConversationId == conversation &&
            (message.Sender == MessageSender.AiAgent || message.Sender == MessageSender.HumanAgent));

        await using var hold = await locks.AcquireAsync(conversation.Value, token).ConfigureAwait(false);
        bool changed = false;
        if (recover is Guid target)
        {
            var message = await outbound.SingleOrDefaultAsync(m => m.Id == MessageId.From(target), token).ConfigureAwait(false);
            if (message is not null)
            {
                var issue = await receipts.AsNoTracking().SingleOrDefaultAsync(r => r.MessageId == message.Id, token).ConfigureAwait(false);
                if (issue?.Status == SimulatorSendStatus.Refused)
                {
                    await DispatchAsync(address, message, SimulatorScenario.Accept, token).ConfigureAwait(false);
                    changed = true;
                }
                else if (issue?.Status == SimulatorSendStatus.Unconfirmed && issue.ReceivedAt.HasValue)
                {
                    await receipts.Where(r => r.MessageId == message.Id)
                        .ExecuteUpdateAsync(update => update.SetProperty(r => r.Status, SimulatorSendStatus.Confirmed), token)
                        .ConfigureAwait(false);
                    changed = true;
                }
            }
        }
        else
        {
            var pending = await outbound.Where(m => !receipts.Any(r => r.MessageId == m.Id))
                .OrderBy(m => EF.Property<long>(m, PersistenceSchema.MessageIngestionSequence))
                .Take(40).ToListAsync(token).ConfigureAwait(false);
            foreach (var message in pending)
            {
                await DispatchAsync(address, message, scenario, token).ConfigureAwait(false);
                changed = true;
            }
        }

        if (changed)
        {
            await notifier.ConversationChangedAsync(address.Tenant, conversation, token).ConfigureAwait(false);
        }

        var issues = await (from message in outbound
                            join receipt in receipts.AsNoTracking() on message.Id equals receipt.MessageId
                            where receipt.Status != SimulatorSendStatus.Confirmed
                            orderby EF.Property<long>(message, PersistenceSchema.MessageIngestionSequence)
                            select new { message.Id, message.Sender, message.Text, receipt.Status })
            .Take(40).ToListAsync(token).ConfigureAwait(false);

        // Never deliver system notes, author addresses, or an outbound message not yet collected.
        var rows = await context.Set<Message>().AsNoTracking()
            .Where(message => message.ConversationId == conversation &&
                (message.Sender == MessageSender.Customer || context.Set<MessengerSimulatorReceipt>().Any(receipt => receipt.MessageId == message.Id && receipt.ReceivedAt != null)))
            .OrderByDescending(message => EF.Property<long>(message, PersistenceSchema.MessageIngestionSequence))
            .Take(40).Select(message => new { message.Id, message.Sender, message.Text, message.SentAt })
            .ToListAsync(token).ConfigureAwait(false);
        rows.Reverse();
        return new SimulatorMailbox(conversation.Value,
            [.. rows.Select(row => new SimulatorMessage(row.Id.Value, row.Sender.ToString(), row.Text, row.SentAt))],
            [.. issues.Select(row => new SimulatorDeliveryIssue(row.Id.Value, row.Sender.ToString(), row.Text, row.Status.ToString()))]);
    }

    private async Task DispatchAsync(SimulatorAddress address, Message message, SimulatorScenario scenario, CancellationToken token)
    {
        // A crash on either side of the send leaves an uncertain attempt, never an automatic replay.
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO messenger_simulator_receipts (message_id, status) VALUES ({message.Id.Value}, 'Unconfirmed')
            ON CONFLICT (message_id) DO UPDATE SET status = 'Unconfirmed'
            WHERE messenger_simulator_receipts.status = 'Refused'
            """, token).ConfigureAwait(false);
        var receipt = context.Set<MessengerSimulatorReceipt>().Where(r => r.MessageId == message.Id);
        string recipient = SocialSimulator.CustomerId(address.Agent.Value, address.Visitor);
        var receiver = new SimulatedMessengerPage(address.Agent.Value, recipient, message, scenario,
            ct => receipt.ExecuteUpdateAsync(update => update.SetProperty(r => r.ReceivedAt, DateTimeOffset.UtcNow), ct));
        using var transport = new HttpMessageInvoker(receiver);
        var status = await MessengerPageTextSender.SendAsync(transport, address.Agent.Value, recipient, message.Text, token)
            .ConfigureAwait(false);
        await receipt.ExecuteUpdateAsync(update => update.SetProperty(r => r.Status, status), token).ConfigureAwait(false);
    }
}
