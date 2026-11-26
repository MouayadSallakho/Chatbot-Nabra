using System.Globalization;
using Microsoft.EntityFrameworkCore;
using OmniAI.Application.Conversations;
using OmniAI.Domain;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;
using OmniAI.Infrastructure.IntegrationTests.Analytics;
using OmniAI.Infrastructure.IntegrationTests.Persistence;
using OmniAI.Infrastructure.Persistence.Repositories;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Conversations;

public sealed class InboxBusinessHoursTests(PostgresTestDatabase database) : IClassFixture<PostgresTestDatabase>
{
    [Theory]
    [InlineData("2026-11-28T18:59:59Z", false)]
    [InlineData("2026-11-28T19:00:00Z", true)]
    [InlineData("2026-11-28T22:00:00Z", true)]
    [InlineData("2026-11-28T22:59:59Z", true)]
    [InlineData("2026-11-28T23:00:00Z", false)]
    public void Saturday_overnight_shift_uses_workspace_time(string utc, bool open)
    {
        var schedule = new RoutingHours(true, [new RoutingShift(6, 22 * 60, 2 * 60)], null);
        Assert.Equal(open, schedule.IsOpen(DateTimeOffset.Parse(utc, CultureInfo.InvariantCulture), WorkspaceTimeZone.Create("Asia/Riyadh")));
    }

    [Theory]
    [InlineData("2026-03-29T00:59:59Z", false)]
    [InlineData("2026-03-29T01:00:00Z", true)]
    [InlineData("2026-10-25T00:30:00Z", true)]
    [InlineData("2026-10-25T01:30:00Z", true)]
    [InlineData("2026-10-25T03:00:00Z", false)]
    public void Daylight_transitions_follow_local_wall_time(string utc, bool open)
    {
        var schedule = new RoutingHours(true, [new RoutingShift(0, 120, 240)], null);
        Assert.Equal(open, schedule.IsOpen(DateTimeOffset.Parse(utc, CultureInfo.InvariantCulture), WorkspaceTimeZone.Create("Europe/Berlin")));
    }

    [Fact]
    public void Invalid_intervals_are_rejected()
    {
        foreach (var shifts in new RoutingShift[][] {
            [new(7, 0, 1)], [new(0, -1, 1)], [new(0, 3, 1441)], [new(0, 3, 3)],
            [new(0, 0, 3), new(0, 5, 7)] })
        {
            Assert.Throws<DomainValidationException>(() => new RoutingHours(true, shifts, null).Validate());
        }
    }

    [Fact]
    public async Task New_first_turn_uses_current_workspace_zone_and_rejects_another_tenants_role_atomically()
    {
        var world = await new AnalyticsWorld(database).StartAsync();
        var now = new DateTimeOffset(2026, 11, 26, 9, 0, 0, TimeSpan.Zero);
        var tenant = world.Tenant.Id;
        await using var db = database.CreateContext();
        var store = new InboxRoutingStore(db);
        var workspace = await db.Tenants.SingleAsync(t => t.Id == tenant);
        workspace.ChangeTimeZone(WorkspaceTimeZone.Create("Asia/Riyadh"));
        await db.SaveChangesAsync();
        var role = TenantRole.CreateCustom(tenant, TenantRoleName.Create("After hours"),
            [PlatformPermissions.ConversationsView, PlatformPermissions.ConversationsReply], now);
        await new TenantRoleRepository(db).AddAsync(role);
        var member = await TestIdentity.SeedUserAsync(database);
        db.TenantMemberships.Add(TenantMembership.Create(member, tenant, now));
        db.TenantRoleAssignments.Add(TenantRoleAssignment.ForRole(member, role, now));
        await db.SaveChangesAsync();
        var hours = new RoutingHours(true, [new RoutingShift(4, 540, 720)], role.Id.Value);
        Assert.True(await store.SetHoursAsync(tenant, hours, default));
        var outsider = await new AnalyticsWorld(database).StartAsync();
        Assert.False(await store.SetHoursAsync(outsider.Tenant.Id, hours, default));
        Assert.False((await store.ReadAsync(outsider.Tenant.Id, default)).Hours.Enabled);
        var agent = await world.AgentAsync("Support", now);
        async Task<Guid?> Recipient(DateTimeOffset sentAt)
        {
            var c = await world.WebChatAsync(agent, sentAt);
            await using var read = database.CreateContext();
            await new WebChatRuntimeStore(read).BeginTurnAsync(c.Id, ClientMessageId.Create("photo"), "", sentAt, ["SampleImage"]);
            return (await read.Conversations.SingleAsync(x => x.Id == c.Id)).AssignedTo?.Value;
        }
        Assert.Null(await Recipient(now.AddSeconds(-1)));
        Assert.Equal(member.Value, await Recipient(now));
        workspace.ChangeTimeZone(WorkspaceTimeZone.Utc);
        await db.SaveChangesAsync();
        Assert.Null(await Recipient(now));
        Assert.True(await store.SetHoursAsync(tenant, hours with { Shifts = [], AfterHoursRoleId = null }, default));
        Assert.Null(await Recipient(now));
    }
}
