using Microsoft.EntityFrameworkCore;
using Npgsql;
using OmniAI.Application.Conversations;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;
using OmniAI.Infrastructure.Conversations;
using OmniAI.Infrastructure.IntegrationTests.Analytics;
using OmniAI.Infrastructure.Persistence.Repositories;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Conversations;

public sealed class MessengerSimulatorStoreTests(PostgresTestDatabase database) : IClassFixture<PostgresTestDatabase>
{
    [Fact]
    public async Task Parallel_first_contact_shares_one_thread_and_only_its_visitor_collects_public_replies()
    {
        var world = await new AnalyticsWorld(database).StartAsync();
        var now = DateTimeOffset.UtcNow;
        var agent = await world.AgentAsync("Support", now);
        var address = new SimulatorAddress(world.Tenant.Id, agent.Id, Guid.NewGuid());
        await using var source = NpgsqlDataSource.Create(database.ConnectionString);
        var locks = new PostgresConversationLock(source);
        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        var a = new MessengerSimulatorStore(first, locks, new TestNotifier());
        var b = new MessengerSimulatorStore(second, locks, new TestNotifier());

        var ids = await Task.WhenAll(a.ResolveAsync(address, now, default), b.ResolveAsync(address, now, default));
        Assert.Equal(ids[0], ids[1]);
        var conversation = await first.Conversations.SingleAsync(row => row.Id == ids[0]);
        await world.SayAsync(conversation, (MessageSender.Customer, now), (MessageSender.AiAgent, now),
            (MessageSender.System, now), (MessageSender.HumanAgent, now));

        var outsider = await b.ReceiveAsync(address with { Visitor = Guid.NewGuid() }, default);
        Assert.Null(outsider.ConversationId);
        Assert.Empty(outsider.Messages);
        var receipts = from receipt in second.Set<MessengerSimulatorReceipt>()
                       join message in second.Set<Message>() on receipt.MessageId equals message.Id
                       where message.ConversationId == conversation.Id
                       select receipt;
        Assert.Empty(await receipts.ToListAsync());

        var pulls = await Task.WhenAll(a.ReceiveAsync(address, default), b.ReceiveAsync(address, default));
        Assert.Equal(pulls[0].Messages, pulls[1].Messages);
        Assert.Equal(3, pulls[0].Messages.Count);
        Assert.DoesNotContain(pulls[0].Messages, item => item.Sender == nameof(MessageSender.System));
        Assert.Equal(2, await receipts.CountAsync());
    }

    [Theory]
    [InlineData(MessageSender.AiAgent, SimulatorScenario.Refuse)]
    [InlineData(MessageSender.HumanAgent, SimulatorScenario.Refuse)]
    [InlineData(MessageSender.AiAgent, SimulatorScenario.LoseConfirmation)]
    [InlineData(MessageSender.HumanAgent, SimulatorScenario.LoseConfirmation)]
    public async Task Recovery_is_scoped_repeatable_and_never_resends_an_uncertain_reply(
        MessageSender sender, SimulatorScenario scenario)
    {
        var world = await new AnalyticsWorld(database).StartAsync();
        var now = DateTimeOffset.UtcNow;
        var agent = await world.AgentAsync("Support", now);
        var address = new SimulatorAddress(world.Tenant.Id, agent.Id, Guid.NewGuid());
        await using var source = NpgsqlDataSource.Create(database.ConnectionString);
        var locks = new PostgresConversationLock(source);
        await using var context = database.CreateContext();
        var store = new MessengerSimulatorStore(context, locks, new TestNotifier());
        var conversationId = await store.ResolveAsync(address, now, default);
        var conversation = await context.Conversations.SingleAsync(row => row.Id == conversationId);
        await world.SayAsync(conversation, (sender, now));
        var first = await store.ReceiveAsync(address, default, scenario);
        var issue = Assert.Single(first.DeliveryIssues);
        bool accepted = scenario == SimulatorScenario.LoseConfirmation;
        Assert.Equal(accepted ? "Unconfirmed" : "Refused", issue.Status);
        Assert.Equal(accepted ? 1 : 0, first.Messages.Count);
        var otherAddress = address with { Visitor = Guid.NewGuid() };
        await store.ResolveAsync(otherAddress, now, default);
        await store.ReceiveAsync(otherAddress, default, recover: issue.Id);

        var polled = await store.ReceiveAsync(address, default);
        Assert.Equal(issue, Assert.Single(polled.DeliveryIssues));
        Assert.Equal(issue.Status, (await store.DeliveryStatusesAsync(address.Tenant, conversationId, default))[issue.Id]);
        Assert.Empty(await store.DeliveryStatusesAsync(TenantId.New(), conversationId, default));
        var row = context.Set<MessengerSimulatorReceipt>().AsNoTracking().Where(r => r.MessageId == MessageId.From(issue.Id));
        var originalReceipt = (await row.SingleAsync()).ReceivedAt;
        Assert.Equal(accepted, originalReceipt.HasValue);

        await using var fresh = database.CreateContext();
        var restarted = new MessengerSimulatorStore(fresh, locks, new TestNotifier());
        await Task.WhenAll(store.ReceiveAsync(address, default, recover: issue.Id),
            restarted.ReceiveAsync(address, default, recover: issue.Id));
        var final = await restarted.ReceiveAsync(address, default);
        Assert.Empty(final.DeliveryIssues);
        Assert.Equal(issue.Id, Assert.Single(final.Messages).Id);
        var receipt = await row.SingleAsync();
        Assert.Equal(SimulatorSendStatus.Confirmed, receipt.Status);
        Assert.NotNull(receipt.ReceivedAt);
        if (accepted)
        {
            Assert.Equal(originalReceipt, receipt.ReceivedAt);
        }
    }

    [Fact]
    public async Task Checking_an_uncertain_attempt_without_a_receiver_receipt_leaves_it_on_hold()
    {
        var world = await new AnalyticsWorld(database).StartAsync();
        var now = DateTimeOffset.UtcNow;
        var agent = await world.AgentAsync("Support", now);
        var address = new SimulatorAddress(world.Tenant.Id, agent.Id, Guid.NewGuid());
        await using var source = NpgsqlDataSource.Create(database.ConnectionString);
        await using var context = database.CreateContext();
        var store = new MessengerSimulatorStore(context, new PostgresConversationLock(source), new TestNotifier());
        var id = await store.ResolveAsync(address, now, default);
        var conversation = await context.Conversations.SingleAsync(c => c.Id == id);
        await world.SayAsync(conversation, (MessageSender.HumanAgent, now));
        var message = await context.Set<Message>().SingleAsync(m => m.ConversationId == id);

        // The sender process stopped after claiming the message and before receiving a result.
        context.Add(new MessengerSimulatorReceipt { MessageId = message.Id, Status = SimulatorSendStatus.Unconfirmed });
        await context.SaveChangesAsync();
        var box = await store.ReceiveAsync(address, default, recover: message.Id.Value);
        Assert.Empty(box.Messages);
        Assert.Equal("Unconfirmed", Assert.Single(box.DeliveryIssues).Status);
        Assert.Null((await context.Set<MessengerSimulatorReceipt>().AsNoTracking().SingleAsync(r => r.MessageId == message.Id)).ReceivedAt);
    }

    private sealed class TestNotifier : IConversationRealtimeNotifier
    {
        public Task ConversationChangedAsync(TenantId tenantId, ConversationId conversationId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
