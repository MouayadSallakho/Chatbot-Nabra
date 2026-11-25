using Microsoft.EntityFrameworkCore;
using Npgsql;
using OmniAI.Application.Conversations;
using OmniAI.Domain.Conversations;
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
        var a = new MessengerSimulatorStore(first, locks);
        var b = new MessengerSimulatorStore(second, locks);

        var ids = await Task.WhenAll(a.ResolveAsync(address, now, default), b.ResolveAsync(address, now, default));
        Assert.Equal(ids[0], ids[1]);
        var conversation = await first.Conversations.SingleAsync(row => row.Id == ids[0]);
        await world.SayAsync(conversation, (MessageSender.Customer, now), (MessageSender.AiAgent, now),
            (MessageSender.System, now), (MessageSender.HumanAgent, now));

        var outsider = await b.ReceiveAsync(address with { Visitor = Guid.NewGuid() }, default);
        Assert.Null(outsider.ConversationId);
        Assert.Empty(outsider.Messages);
        Assert.Empty(await second.Set<MessengerSimulatorReceipt>().ToListAsync());

        var pulls = await Task.WhenAll(a.ReceiveAsync(address, default), b.ReceiveAsync(address, default));
        Assert.Equal(pulls[0].Messages, pulls[1].Messages);
        Assert.Equal(3, pulls[0].Messages.Count);
        Assert.DoesNotContain(pulls[0].Messages, item => item.Sender == nameof(MessageSender.System));
        Assert.Equal(2, await second.Set<MessengerSimulatorReceipt>().CountAsync());
    }
}
