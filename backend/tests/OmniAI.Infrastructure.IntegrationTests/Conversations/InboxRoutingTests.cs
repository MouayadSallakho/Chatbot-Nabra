using Microsoft.EntityFrameworkCore;
using OmniAI.Application.Conversations;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;
using OmniAI.Infrastructure.IntegrationTests.Analytics;
using OmniAI.Infrastructure.IntegrationTests.Persistence;
using OmniAI.Infrastructure.Persistence.Repositories;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Conversations;

public sealed class InboxRoutingTests(PostgresTestDatabase database) : IClassFixture<PostgresTestDatabase>
{
    [Theory]
    [InlineData("مرحباً، أحتاج مساعدة", "ar")]
    [InlineData("Hello, can you help?", "en")]
    [InlineData("12345 📎", null)]
    [InlineData("Bonjour", null)]
    [InlineData("", null)]
    public void Recognises_supported_first_messages(string text, string? expected) =>
        Assert.Equal(expected, RoutingLanguage.Detect(text));

    [Fact]
    public async Task A_member_with_view_only_is_skipped_and_the_first_turn_is_kept()
    {
        var world = await new AnalyticsWorld(database).StartAsync();
        var now = DateTimeOffset.UtcNow;
        var tenant = world.Tenant.Id;
        var role = TenantRole.CreateCustom(tenant, TenantRoleName.Create("Observers"), [PlatformPermissions.ConversationsView], now);
        await using var db = database.CreateContext();
        await new TenantRoleRepository(db).AddAsync(role);
        var user = await TestIdentity.SeedUserAsync(database);
        db.TenantMemberships.Add(TenantMembership.Create(user, tenant, now));
        db.TenantRoleAssignments.Add(TenantRoleAssignment.ForRole(user, role, now));
        await db.SaveChangesAsync();
        Assert.True(await new InboxRoutingStore(db).SetAsync(tenant, ChannelKind.WebChat, "ar", role.Id.Value, default));
        var agent = await world.AgentAsync("Support", now);
        var conversation = await world.WebChatAsync(agent, now);
        var turn = await new WebChatRuntimeStore(db).BeginTurnAsync(conversation.Id, ClientMessageId.Create("first-message"), "مرحبا", now);
        Assert.NotNull(turn);
        db.ChangeTracker.Clear();
        Assert.Null((await db.Conversations.SingleAsync(c => c.Id == conversation.Id)).AssignedTo);
        Assert.Equal("مرحبا", (await db.Set<Message>().SingleAsync(m => m.ConversationId == conversation.Id)).Text);
    }

    [Fact]
    public async Task Concurrent_first_turns_share_the_load_and_subsequent_messages_preserve_manual_unassignment()
    {
        var world = await new AnalyticsWorld(database).StartAsync();
        var now = DateTimeOffset.UtcNow;
        var tenant = world.Tenant.Id;
        var agent = await world.AgentAsync("Support", now);
        var role = TenantRole.CreateCustom(tenant, TenantRoleName.Create("English support"),
            [PlatformPermissions.ConversationsView, PlatformPermissions.ConversationsReply], now);
        await using var context = database.CreateContext();
        await new TenantRoleRepository(context).AddAsync(role);
        for (int i = 0; i < 2; i++)
        {
            var member = await TestIdentity.SeedUserAsync(database);
            context.TenantMemberships.Add(TenantMembership.Create(member, tenant, now));
            context.TenantRoleAssignments.Add(TenantRoleAssignment.ForRole(member, role, now));
        }
        await context.SaveChangesAsync();
        var settings = new InboxRoutingStore(context);
        Assert.True(await settings.SetAsync(tenant, ChannelKind.WebChat, "en", role.Id.Value, default));
        Assert.False(await settings.SetAsync(TenantId.New(), ChannelKind.WebChat, "en", role.Id.Value, default));
        var first = await world.WebChatAsync(agent, now);
        var second = await world.WebChatAsync(agent, now);
        async Task Send(ConversationId id)
        {
            await using var db = database.CreateContext();
            Assert.NotNull(await new WebChatRuntimeStore(db).BeginTurnAsync(id, ClientMessageId.Create(Guid.NewGuid().ToString("N")), "Please help", now));
        }
        await Task.WhenAll(Send(first.Id), Send(second.Id));
        var routed = await context.Conversations.Where(c => c.TenantId == tenant).ToListAsync();
        Assert.Equal(2, routed.Select(c => c.AssignedTo).Distinct().Count());
        Assert.All(routed, c => Assert.NotNull(c.AssignedTo));
        routed[0].Unassign(now);
        await context.SaveChangesAsync();
        await Send(routed[0].Id);
        await context.Entry(routed[0]).ReloadAsync();
        Assert.Null(routed[0].AssignedTo);
    }
}
