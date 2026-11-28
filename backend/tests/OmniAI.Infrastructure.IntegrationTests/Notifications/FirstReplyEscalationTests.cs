using OmniAI.Domain.Authorization;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Customers;
using OmniAI.Domain.Notifications;
using OmniAI.Infrastructure.IntegrationTests.Reports;
using OmniAI.Infrastructure.Notifications;
using OmniAI.Infrastructure.Persistence.Repositories;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Notifications;

public sealed class FirstReplyEscalationTests(PostgresTestDatabase database) : IClassFixture<PostgresTestDatabase>
{
    [Fact]
    public async Task Dispatch_rechecks_live_eligibility_and_notifies_only_workspace_inbox_viewers_once()
    {
        var world = await ReportWorld.CreateAsync(database);
        var viewer = await world.MemberAsync(PlatformPermissions.ConversationsView);
        var excluded = await world.MemberAsync(PlatformPermissions.BillingView);
        var harness = new NotificationHarness(database);
        var now = harness.Clock.GetUtcNow();
        await using var db = world.Context();

        Conversation Waiting()
        {
            var at = now.AddMinutes(-20);
            var c = Conversation.Start(world.Tenant, CustomerId.New(), ChannelKind.WebChat, at);
            c.AddTextMessage(MessageSender.Customer, "Help", at);
            db.AddOwner(c);
            db.Conversations.Add(c);
            return c;
        }

        var open = Waiting();
        var assigned = Waiting();
        var answered = Waiting();
        var closed = Waiting();
        await db.SaveChangesAsync();
        var settings = new InboxRoutingStore(db);
        Task<int> Scan(DateTimeOffset time) => FirstReplyEscalations.EnqueueAsync(db, time, 20, default);
        Assert.Equal(0, await Scan(now));
        await settings.SetEscalationAsync(world.Tenant, 5, default);
        Assert.Equal(0, await Scan(now.AddMilliseconds(-1)));
        Assert.Equal(4, await Scan(now));
        Assert.Equal(0, await Scan(now));
        assigned.AssignTo(viewer, now);
        answered.AddTextMessage(MessageSender.AiAgent, "Reply", now);
        closed.Close(now);
        await db.SaveChangesAsync();
        Assert.Equal(4, await harness.DispatchAllAsync());

        var alert = Assert.Single(await harness.NotificationsAsync(world.Tenant, viewer));
        Assert.Equal(NotificationKind.FirstReplyEscalated, alert.Kind);
        Assert.Equal($"/conversations/{open.Id.Value:D}", alert.ActionPath);
        Assert.Single(await harness.NotificationsAsync(world.Tenant, world.Owner));
        Assert.Empty(await harness.NotificationsAsync(world.Tenant, excluded));
        Assert.Empty(await harness.EmailsAsync(world.Tenant));
        Assert.Single(harness.Realtime.Signals);
        await new NotificationStore(db).DeleteProcessedOccurrencesAsync(now.AddDays(90), 100, default);
        Assert.Equal(0, await Scan(now));

        assigned.Unassign(now);
        await db.SaveChangesAsync();
        Assert.Equal(1, await Scan(now));
        await settings.SetEscalationAsync(world.Tenant, null, default);
        Assert.Equal(1, await harness.DispatchAllAsync());
        Assert.Single(await harness.NotificationsAsync(world.Tenant, viewer));
        Assert.Equal(0, await Scan(now));
    }
}
