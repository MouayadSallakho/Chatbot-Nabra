using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OmniAI.Application.Conversations;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Infrastructure.Persistence.Repositories;

internal sealed class InboxRoute
{
    public TenantId Tenant { get; init; }
    public ChannelKind Channel { get; init; }
    public required string Language { get; init; }
    public TenantRoleId Role { get; init; }
}

internal sealed class InboxRouteConfiguration : IEntityTypeConfiguration<InboxRoute>
{
    public void Configure(EntityTypeBuilder<InboxRoute> builder)
    {
        builder.ToTable("inbox_routes");
        builder.HasKey(r => new { r.Tenant, r.Channel, r.Language });
        builder.Property(r => r.Tenant).HasColumnName("tenant_id").HasConversion(PersistenceConverters.TenantId);
        builder.Property(r => r.Channel).HasColumnName("channel").HasConversion<string>().HasMaxLength(32);
        builder.Property(r => r.Language).HasColumnName("language").HasMaxLength(2);
        builder.Property(r => r.Role).HasColumnName("role_id").HasConversion(PersistenceConverters.TenantRoleId);
        builder.HasOne<TenantRole>().WithMany().HasForeignKey(r => new { r.Role, r.Tenant })
            .HasPrincipalKey(r => new { r.Id, r.TenantId }).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class InboxRoutingStore(OmniAiDbContext db) : IInboxRoutingStore
{
    public async Task<RoutingSettings> ReadAsync(TenantId tenant, CancellationToken token)
    {
        var roles = await db.TenantRoles.AsNoTracking().Where(r => r.TenantId == tenant)
            .ToListAsync(token).ConfigureAwait(false);
        var rules = await db.Set<InboxRoute>().AsNoTracking().Where(r => r.Tenant == tenant)
            .OrderBy(r => r.Channel).ThenBy(r => r.Language).ToListAsync(token).ConfigureAwait(false);
        var schedule = await db.Set<InboxSchedule>().AsNoTracking().Include(s => s.Destination)
            .SingleOrDefaultAsync(s => s.Tenant == tenant, token).ConfigureAwait(false);
        return new RoutingSettings([.. rules.Select(r => new RoutingRule(r.Channel.ToString(), r.Language, r.Role.Value))],
            [.. roles.Select(r => new RoutingTeam(r.Id.Value, r.Name.Value)).OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)],
            schedule?.Hours() ?? RoutingHours.Default, await TargetAsync(db, tenant, token).ConfigureAwait(false));
    }

    internal static Task<int> TargetAsync(OmniAiDbContext db, TenantId tenant, CancellationToken token) =>
        db.Tenants.Where(t => t.Id == tenant).Select(t => EF.Property<int>(t, "FirstReplyTargetMinutes")).SingleAsync(token);

    public Task SetTargetAsync(TenantId tenant, int minutes, CancellationToken token) =>
        db.Tenants.Where(t => t.Id == tenant).ExecuteUpdateAsync(
            updates => updates.SetProperty(t => EF.Property<int>(t, "FirstReplyTargetMinutes"), minutes), token);

    public async Task<bool> SetHoursAsync(TenantId tenant, RoutingHours hours, CancellationToken token)
    {
        hours.Validate();
        var minutes = Enumerable.Repeat(-1, 14).ToArray();
        foreach (var shift in hours.Shifts)
        {
            minutes[2 * shift.Day] = shift.StartMinute;
            minutes[2 * shift.Day + 1] = shift.EndMinute;
        }
        await using var transaction = await db.Database.BeginTransactionAsync(token).ConfigureAwait(false);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO inbox_schedules (tenant_id, enabled, minutes) VALUES ({tenant.Value}, {hours.Enabled}, {minutes})
            ON CONFLICT (tenant_id) DO UPDATE SET enabled = excluded.enabled, minutes = excluded.minutes
            """, token).ConfigureAwait(false);
        if (hours.AfterHoursRoleId is { } role)
        {
            int changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO inbox_after_hours_teams (tenant_id, role_id)
                SELECT tenant_id, id FROM tenant_roles WHERE tenant_id = {tenant.Value} AND id = {role}
                ON CONFLICT (tenant_id) DO UPDATE SET role_id = excluded.role_id
                """, token).ConfigureAwait(false);
            if (changed == 0)
            {
                return false;
            }
        }
        else
        {
            await db.Set<AfterHoursTeam>().Where(t => t.Tenant == tenant).ExecuteDeleteAsync(token).ConfigureAwait(false);
        }
        await transaction.CommitAsync(token).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> SetAsync(TenantId tenant, ChannelKind channel, string language, Guid? role, CancellationToken token)
    {
        if (role is null)
        {
            await db.Set<InboxRoute>().Where(r => r.Tenant == tenant && r.Channel == channel && r.Language == language)
                .ExecuteDeleteAsync(token).ConfigureAwait(false);
            return true;
        }
        int affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO inbox_routes (tenant_id, channel, language, role_id)
            SELECT tenant_id, {channel.ToString()}, {language}, id FROM tenant_roles WHERE tenant_id = {tenant.Value} AND id = {role.Value}
            ON CONFLICT (tenant_id, channel, language) DO UPDATE SET role_id = excluded.role_id
            """, token).ConfigureAwait(false);
        return affected == 1;
    }

    internal static async Task<TenantRoleId?> FirstMessageRoleAsync(OmniAiDbContext db, Conversation conversation, string text, DateTimeOffset sentAt,
        CancellationToken token)
    {
        if (conversation.AssignedTo is not null || conversation.Handling == ConversationHandling.Human ||
            await db.Set<Message>().AnyAsync(m => m.ConversationId == conversation.Id && m.Sender == MessageSender.Customer, token).ConfigureAwait(false))
        {
            return null;
        }
        var schedule = await (from s in db.Set<InboxSchedule>().AsNoTracking().Include(s => s.Destination)
                              join t in db.Tenants on s.Tenant equals t.Id
                              where s.Tenant == conversation.TenantId && s.Enabled
                              select new { Schedule = s, t.TimeZone }).SingleOrDefaultAsync(token).ConfigureAwait(false);
        if (schedule is not null && !schedule.Schedule.Hours().IsOpen(sentAt, schedule.TimeZone))
        {
            return schedule.Schedule.Destination?.Role;
        }
        if (RoutingLanguage.Detect(text) is not { } language)
        {
            return null;
        }
        var rule = await db.Set<InboxRoute>().AsNoTracking().SingleOrDefaultAsync(r => r.Tenant == conversation.TenantId &&
            r.Channel == conversation.Channel && r.Language == language, token).ConfigureAwait(false);
        return rule?.Role;
    }

    internal static async Task AssignAsync(OmniAiDbContext db, Conversation conversation, TenantRoleId role, DateTimeOffset now, CancellationToken token)
    {
        // Only matching first turns queue here. Workloads are counted after the previous assignment commits.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtext('inbox-routing'), hashtext({conversation.TenantId.Value.ToString()}))", token).ConfigureAwait(false);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM conversations WHERE id = {conversation.Id.Value} FOR UPDATE", token).ConfigureAwait(false);
        await db.Entry(conversation).ReloadAsync(token).ConfigureAwait(false);
        if (conversation.AssignedTo is not null || conversation.Handling == ConversationHandling.Human)
        {
            return;
        }
        var eligible = await new ConversationInboxStore(db).ListEligibleForRoleAsync(conversation.TenantId, role, token).ConfigureAwait(false);
        if (eligible.Count == 0)
        {
            return;
        }
        var ids = eligible.Select(member => UserId.From(member.UserId)).ToList();
        var candidate = await db.TenantRoleAssignments.Where(a => a.TenantId == conversation.TenantId &&
                a.TenantRoleId == role && ids.Contains(a.UserId))
            .OrderBy(a => db.Conversations.Count(c => c.TenantId == conversation.TenantId && c.Status == ConversationStatus.Open &&
                c.AssignedTo == a.UserId)).ThenBy(a => a.UserId).FirstOrDefaultAsync(token).ConfigureAwait(false);
        if (candidate is not null)
        {
            // Membership may have been revoked while workloads were read. Either keep the
            // role membership through the commit, or accept this turn in the general inbox.
            var retained = await db.Database.SqlQuery<int>($"""
                SELECT 1 AS "Value" FROM tenant_role_assignments a
                JOIN tenant_memberships m ON m.tenant_id = a.tenant_id AND m.user_id = a.user_id
                WHERE a.tenant_id = {conversation.TenantId.Value} AND a.tenant_role_id = {role.Value} AND a.user_id = {candidate.UserId.Value}
                FOR KEY SHARE OF a, m
                """).ToListAsync(token).ConfigureAwait(false);
            if (retained.Count != 0)
            {
                conversation.AssignTo(candidate.UserId, now);
            }
        }
    }
}
