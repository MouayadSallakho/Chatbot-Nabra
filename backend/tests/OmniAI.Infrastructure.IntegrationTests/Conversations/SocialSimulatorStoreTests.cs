using Microsoft.EntityFrameworkCore;
using Npgsql;
using OmniAI.Application.Conversations;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Users;
using OmniAI.Application.Conversations.Persistence;
using OmniAI.Domain.Tenants;
using OmniAI.Infrastructure.Conversations;
using OmniAI.Infrastructure.IntegrationTests.Analytics;
using OmniAI.Infrastructure.Persistence.Repositories;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Conversations;

public sealed class SocialSimulatorStoreTests(PostgresTestDatabase database) : IClassFixture<PostgresTestDatabase>
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
        var a = new SocialSimulatorStore(first, locks, new TestNotifier());
        var b = new SocialSimulatorStore(second, locks, new TestNotifier());

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
    [InlineData(ChannelKind.Messenger, MessageSender.AiAgent, SimulatorScenario.Refuse)]
    [InlineData(ChannelKind.Messenger, MessageSender.HumanAgent, SimulatorScenario.Refuse)]
    [InlineData(ChannelKind.Messenger, MessageSender.AiAgent, SimulatorScenario.LoseConfirmation)]
    [InlineData(ChannelKind.Messenger, MessageSender.HumanAgent, SimulatorScenario.LoseConfirmation)]
    [InlineData(ChannelKind.Instagram, MessageSender.AiAgent, SimulatorScenario.Refuse)]
    [InlineData(ChannelKind.Instagram, MessageSender.HumanAgent, SimulatorScenario.Refuse)]
    [InlineData(ChannelKind.Instagram, MessageSender.AiAgent, SimulatorScenario.LoseConfirmation)]
    [InlineData(ChannelKind.Instagram, MessageSender.HumanAgent, SimulatorScenario.LoseConfirmation)]
    public async Task Recovery_is_scoped_repeatable_and_never_resends_an_uncertain_reply(
        ChannelKind channel, MessageSender sender, SimulatorScenario scenario)
    {
        var world = await new AnalyticsWorld(database).StartAsync();
        var now = DateTimeOffset.UtcNow;
        var agent = await world.AgentAsync("Support", now);
        var address = new SimulatorAddress(world.Tenant.Id, agent.Id, Guid.NewGuid(), channel);
        await using var source = NpgsqlDataSource.Create(database.ConnectionString);
        var locks = new PostgresConversationLock(source);
        await using var context = database.CreateContext();
        var store = new SocialSimulatorStore(context, locks, new TestNotifier());
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
        var statuses = channel == ChannelKind.Instagram
            ? (await store.InstagramStateAsync(address.Tenant, conversationId, default)).Statuses
            : await store.DeliveryStatusesAsync(address.Tenant, conversationId, default);
        Assert.Equal(issue.Status, statuses[issue.Id]);
        Assert.Empty(await store.DeliveryStatusesAsync(TenantId.New(), conversationId, default));
        var row = context.Set<MessengerSimulatorReceipt>().AsNoTracking().Where(r => r.MessageId == MessageId.From(issue.Id));
        var originalReceipt = (await row.SingleAsync()).ReceivedAt;
        Assert.Equal(accepted, originalReceipt.HasValue);
        if (channel == ChannelKind.Instagram)
        {
            await store.SetOwnershipAsync(address, new(true, 0), default);
            var held = await store.ReceiveAsync(address, default, recover: issue.Id);
            if (accepted)
            {
                // Inspecting an existing receipt sends nothing, even under external control.
                Assert.Empty(held.DeliveryIssues);
                Assert.Equal(originalReceipt, (await row.SingleAsync()).ReceivedAt);
            }
            else
            {
                Assert.Empty(held.Messages);
                Assert.Equal(issue, Assert.Single(held.DeliveryIssues));
            }
            await store.SetOwnershipAsync(address, new(false, 1), default);
        }

        await using var fresh = database.CreateContext();
        var restarted = new SocialSimulatorStore(fresh, locks, new TestNotifier());
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

    [Theory]
    [InlineData(ChannelKind.Messenger)]
    [InlineData(ChannelKind.Instagram)]
    public async Task Checking_an_uncertain_attempt_without_a_receiver_receipt_leaves_it_on_hold(ChannelKind channel)
    {
        var world = await new AnalyticsWorld(database).StartAsync();
        var now = DateTimeOffset.UtcNow;
        var agent = await world.AgentAsync("Support", now);
        var address = new SimulatorAddress(world.Tenant.Id, agent.Id, Guid.NewGuid(), channel);
        await using var source = NpgsqlDataSource.Create(database.ConnectionString);
        await using var context = database.CreateContext();
        var store = new SocialSimulatorStore(context, new PostgresConversationLock(source), new TestNotifier());
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

    [Fact]
    public async Task Instagram_owns_its_threads_and_shows_only_public_messages_to_the_scoped_visitor()
    {
        var world = await new AnalyticsWorld(database).StartAsync();
        var now = DateTimeOffset.UtcNow;
        var agent = await world.AgentAsync("Support", now);
        var address = new SimulatorAddress(world.Tenant.Id, agent.Id, Guid.NewGuid(), ChannelKind.Instagram);
        await using var source = NpgsqlDataSource.Create(database.ConnectionString);
        var locks = new PostgresConversationLock(source);
        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        var a = new SocialSimulatorStore(first, locks, new TestNotifier());
        var b = new SocialSimulatorStore(second, locks, new TestNotifier());

        var ids = await Task.WhenAll(a.ResolveAsync(address, now, default), b.ResolveAsync(address, now, default));
        Assert.Equal(ids[0], ids[1]);
        var messenger = address with { Channel = ChannelKind.Messenger };
        Assert.NotEqual(ids[0], await a.ResolveAsync(messenger, now, default));
        Assert.NotEqual(SocialSimulator.CustomerId(agent.Id.Value, address.Visitor),
            SocialSimulator.CustomerId(agent.Id.Value, address.Visitor, ChannelKind.Instagram));

        var dm = await first.Conversations.SingleAsync(c => c.Id == ids[0]);
        Assert.Equal(ChannelKind.Instagram, dm.Channel);
        var inbox = new ConversationRepository(second);
        var viewer = UserId.New();
        var detail = await inbox.FindAsync(address.Tenant, dm.Id, viewer);
        Assert.Equal("Support", detail!.Value.AiAgentName);
        var list = await inbox.ListAsync(address.Tenant, 1, 10, ConversationListFilter.All, viewer);
        Assert.Equal("Support", Assert.Single(list.Items, row => row.Id == dm.Id.Value).AiAgentName);
        await world.SayAsync(dm, (MessageSender.Customer, now), (MessageSender.AiAgent, now),
            (MessageSender.System, now), (MessageSender.HumanAgent, now));
        var mailbox = await b.ReceiveAsync(address, default);
        Assert.Collection(mailbox.Messages, customer => Assert.Equal("Customer", customer.Sender),
            ai => Assert.Equal("AiAgent", ai.Sender), staff => Assert.Equal("HumanAgent", staff.Sender));
        Assert.Empty((await b.ReceiveAsync(messenger, default)).Messages);
        Assert.Null((await b.ReceiveAsync(address with { Visitor = Guid.NewGuid() }, default)).ConversationId);
        Assert.Null((await b.ReceiveAsync(address with { Tenant = TenantId.New() }, default)).ConversationId);
    }

    [Fact]
    public async Task External_control_survives_replays_and_never_flushes_a_reply_pending_before_transfer()
    {
        var world = await new AnalyticsWorld(database).StartAsync();
        var now = DateTimeOffset.UtcNow;
        var agent = await world.AgentAsync("Support", now);
        var address = new SimulatorAddress(world.Tenant.Id, agent.Id, Guid.NewGuid(), ChannelKind.Instagram);
        await using var source = NpgsqlDataSource.Create(database.ConnectionString);
        await using var context = database.CreateContext();
        var store = new SocialSimulatorStore(context, new PostgresConversationLock(source), new TestNotifier());
        var id = await store.ResolveAsync(address, now, default);
        var dm = await context.Conversations.SingleAsync(c => c.Id == id);
        await world.SayAsync(dm, (MessageSender.HumanAgent, now));
        Assert.False(await store.SetOwnershipAsync(address with { Visitor = Guid.NewGuid() }, new(true, 0), default));
        Assert.True(await store.SetOwnershipAsync(address, new(true, 0), default));
        await using var fresh = database.CreateContext();
        var restarted = new SocialSimulatorStore(fresh, new PostgresConversationLock(source), new TestNotifier());
        var paused = await restarted.ReceiveAsync(address, default);
        Assert.Equal(new SimulatorOwnership(true, 1), paused.Ownership);
        Assert.Empty(paused.Messages);
        Assert.Equal("Withheld", Assert.Single((await restarted.InstagramStateAsync(address.Tenant, id, default)).Statuses).Value);
        await restarted.SetOwnershipAsync(address, new(false, 1), default);
        await store.SetOwnershipAsync(address, new(true, 0), default);
        var resumed = await store.ReceiveAsync(address, default);
        Assert.Empty(resumed.Messages);
        Assert.Equal(new SimulatorOwnership(false, 2), resumed.Ownership);
    }

    [Fact]
    public async Task Ownership_notifications_use_event_time_and_serialize_concurrent_redelivery()
    {
        var world = await new AnalyticsWorld(database).StartAsync();
        var now = DateTimeOffset.UtcNow;
        var agent = await world.AgentAsync("Support", now);
        var address = new SimulatorAddress(world.Tenant.Id, agent.Id, Guid.NewGuid(), ChannelKind.Instagram);
        await using var source = NpgsqlDataSource.Create(database.ConnectionString);
        var locks = new PostgresConversationLock(source);
        await using var a = database.CreateContext();
        await using var b = database.CreateContext();
        var notifier = new TestNotifier();
        var first = new SocialSimulatorStore(a, locks, notifier);
        var second = new SocialSimulatorStore(b, locks, notifier);
        await first.ResolveAsync(address, now, default);
        var takeover = new InstagramHandover("visitor", true, 100);
        var results = await Task.WhenAll(first.ApplyHandoverAsync(address, takeover, default), second.ApplyHandoverAsync(address, takeover, default));
        Assert.Contains("Applied", results);
        Assert.Contains("Duplicate", results);
        Assert.Equal(1, notifier.Changes);
        Assert.Equal("Stale", await second.ApplyHandoverAsync(address, new("visitor", false, 99), default));
        Assert.Equal("Conflict", await second.ApplyHandoverAsync(address, new("visitor", false, 100), default));
        Assert.Equal(new SimulatorOwnership(true, 1), (await second.ReceiveAsync(address, default)).Ownership);
        Assert.Equal("Applied", await second.ApplyHandoverAsync(address, new("visitor", false, 105), default));
        // A newer notification with the same owner still advances the ordering boundary.
        Assert.Equal("Applied", await first.ApplyHandoverAsync(address, new("visitor", false, 110), default));
        Assert.Equal("Stale", await second.ApplyHandoverAsync(address, new("visitor", true, 108), default));
        Assert.Equal("Stale", await first.ApplyHandoverAsync(address, takeover, default));
        var current = await second.ReceiveAsync(address, default);
        Assert.Equal(110, current.LastHandoverTimestamp);
        Assert.Equal(new SimulatorOwnership(false, 2), current.Ownership);
        Assert.Equal(2, notifier.Changes);
        Assert.Equal("Conflict", await first.ApplyHandoverAsync(address, new("visitor", true, 110), default));
        Assert.Equal(new SimulatorOwnership(true, 3), (await second.ReceiveAsync(address, default)).Ownership);
    }

    private sealed class TestNotifier : IConversationRealtimeNotifier
    {
        public int Changes { get; private set; }
        public Task ConversationChangedAsync(TenantId tenantId, ConversationId conversationId, CancellationToken cancellationToken = default)
        {
            Changes++;
            return Task.CompletedTask;
        }
    }
}
