using Microsoft.EntityFrameworkCore;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Notifications;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Notifications;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;
using OmniAI.Infrastructure.IntegrationTests.Reports;
using OmniAI.Infrastructure.Notifications;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Notifications;

/// <summary>
/// The notification centre's reads and writes, per member, against a real PostgreSQL.
/// </summary>
public sealed class NotificationStoreTests(PostgresTestDatabase database)
    : IClassFixture<PostgresTestDatabase>
{
    [Fact]
    public async Task A_member_sees_only_their_own_notifications_newest_first_in_bounded_pages()
    {
        ReportWorld world = await ReportWorld.CreateAsync(database);
        UserId colleague = await world.MemberAsync(PlatformPermissions.BillingView);
        NotificationHarness harness = new(database);

        for (int index = 0; index < 5; index++)
        {
            harness.Clock.Advance(TimeSpan.FromSeconds(1));
            await harness.RecordAsync(world.Tenant, NotificationKind.BillingPastDue, $"sub:PastDue:{index}");
            await harness.DispatchAllAsync();
        }

        await using OmniAiDbContext context = database.CreateContext();
        NotificationStore store = new(context);

        (IReadOnlyList<Notification> first, int total) = await store.ListAsync(world.Tenant, world.Owner, 1, 2);
        (IReadOnlyList<Notification> second, _) = await store.ListAsync(world.Tenant, world.Owner, 2, 2);
        (IReadOnlyList<Notification> third, _) = await store.ListAsync(world.Tenant, world.Owner, 3, 2);

        Assert.Equal(5, total);
        Assert.Equal(2, first.Count);
        Assert.Single(third);

        List<Notification> all = [.. first, .. second, .. third];

        Assert.Equal(5, all.Select(notification => notification.Id).Distinct().Count());
        Assert.Equal(all.OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id).Select(n => n.Id), all.Select(n => n.Id));
        Assert.All(all, notification => Assert.Equal(world.Owner, notification.RecipientUserId));

        Assert.Equal(5, await store.CountUnreadAsync(world.Tenant, world.Owner));
        Assert.Equal(5, await store.CountUnreadAsync(world.Tenant, colleague));
    }

    [Fact]
    public async Task Reading_is_idempotent_and_mark_all_touches_only_the_caller()
    {
        ReportWorld world = await ReportWorld.CreateAsync(database);
        UserId colleague = await world.MemberAsync(PlatformPermissions.BillingView);
        NotificationHarness harness = new(database);

        await harness.RecordAsync(world.Tenant, NotificationKind.BillingPastDue, "sub:PastDue:a");
        await harness.RecordAsync(world.Tenant, NotificationKind.BillingSuspended, "sub:Suspended:b");
        await harness.DispatchAllAsync();

        Notification mine = (await harness.NotificationsAsync(world.Tenant, world.Owner))[0];
        Notification theirs = (await harness.NotificationsAsync(world.Tenant, colleague))[0];

        await using OmniAiDbContext context = database.CreateContext();
        NotificationStore store = new(context);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        Assert.True(await store.MarkReadAsync(world.Tenant, world.Owner, mine.Id, now));
        Assert.True(await store.MarkReadAsync(world.Tenant, world.Owner, mine.Id, now.AddMinutes(1)));

        // Reading it again did not move the first read.
        Notification read = (await harness.NotificationsAsync(world.Tenant, world.Owner)).Single(n => n.Id == mine.Id);
        Assert.Equal(now.ToUniversalTime(), read.ReadAt!.Value, TimeSpan.FromMilliseconds(1));

        // Somebody else's notification is absent, not forbidden, and untouched.
        Assert.False(await store.MarkReadAsync(world.Tenant, world.Owner, theirs.Id, now));
        Assert.Null((await harness.NotificationsAsync(world.Tenant, colleague)).Single(n => n.Id == theirs.Id).ReadAt);

        Assert.Equal(1, await store.MarkAllReadAsync(world.Tenant, world.Owner, now));
        Assert.Equal(0, await store.MarkAllReadAsync(world.Tenant, world.Owner, now));

        Assert.Equal(0, await store.CountUnreadAsync(world.Tenant, world.Owner));
        Assert.Equal(2, await store.CountUnreadAsync(world.Tenant, colleague));
    }

    [Fact]
    public async Task Another_workspace_cannot_see_count_or_mark_a_notification()
    {
        ReportWorld world = await ReportWorld.CreateAsync(database);
        ReportWorld rival = await ReportWorld.CreateAsync(database, "Rival Trading");
        NotificationHarness harness = new(database);

        await harness.RecordAsync(world.Tenant, NotificationKind.UsageCritical, "2026-09");
        await harness.DispatchAllAsync();

        Notification notification = Assert.Single(await harness.NotificationsAsync(world.Tenant));

        await using OmniAiDbContext context = database.CreateContext();
        NotificationStore store = new(context);

        // The same member id in the wrong workspace, and the other workspace's owner.
        Assert.False(await store.MarkReadAsync(rival.Tenant, world.Owner, notification.Id, DateTimeOffset.UtcNow));
        Assert.False(await store.MarkReadAsync(rival.Tenant, rival.Owner, notification.Id, DateTimeOffset.UtcNow));
        Assert.Equal(0, await store.CountUnreadAsync(rival.Tenant, rival.Owner));
        Assert.Equal(0, (await store.ListAsync(rival.Tenant, rival.Owner, 1, 50)).TotalCount);
        Assert.Null(await store.FindAsync(rival.Tenant, notification.Id));
    }

    [Fact]
    public async Task A_notification_cannot_name_a_recipient_in_another_workspace_s_email()
    {
        ReportWorld world = await ReportWorld.CreateAsync(database);
        ReportWorld rival = await ReportWorld.CreateAsync(database, "Rival Trading");
        NotificationHarness harness = new(database);

        await harness.RecordAsync(world.Tenant, NotificationKind.UsageCritical, "2026-09");
        await harness.DispatchAllAsync();

        Notification notification = Assert.Single(await harness.NotificationsAsync(world.Tenant));

        // An email row pointing at this notification but claiming the other
        // workspace is refused by the composite foreign key.
        await using OmniAiDbContext context = database.CreateContext();

        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO notification_emails (id, notification_id, tenant_id, recipient_user_id, category, status, created_at, available_at, attempts)
            VALUES (gen_random_uuid(), {0}, {1}, {2}, 'Usage', 'Pending', now(), now(), 0)
            """,
            notification.Id.Value,
            rival.Tenant.Value,
            rival.Owner.Value));
    }

    [Fact]
    public async Task Preferences_start_with_email_off_and_are_saved_against_the_version_read()
    {
        ReportWorld world = await ReportWorld.CreateAsync(database);
        UserId colleague = await world.MemberAsync(PlatformPermissions.ReportsView);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        Versioned<NotificationPreferences> initial;

        await using (OmniAiDbContext context = database.CreateContext())
        {
            initial = await new NotificationStore(context).PreferencesAsync(world.Tenant, world.Owner, now);
        }

        foreach (NotificationCategory category in Enum.GetValues<NotificationCategory>())
        {
            Assert.False(initial.Value.EmailsFor(category));
        }

        // Two tabs both saving the member's first choice: one wins, one is stale.
        Task<ConcurrencyToken> first = SaveAsync(world.Tenant, world.Owner, initial.Version, reports: true);
        Task<ConcurrencyToken> second = SaveAsync(world.Tenant, world.Owner, initial.Version, reports: false);

        Exception?[] outcomes = await Outcomes(first, second);

        Assert.Single(outcomes, outcome => outcome is null);
        Assert.Single(outcomes, outcome => outcome is NotificationPreferencesVersionMismatchException);

        ConcurrencyToken current;

        await using (OmniAiDbContext context = database.CreateContext())
        {
            current = (await new NotificationStore(context).PreferencesAsync(world.Tenant, world.Owner, now)).Version;
        }

        // Saving against the stale "nothing stored" version is refused; against the current one it works.
        await Assert.ThrowsAsync<NotificationPreferencesVersionMismatchException>(
            () => SaveAsync(world.Tenant, world.Owner, initial.Version, reports: true));

        ConcurrencyToken next = await SaveAsync(world.Tenant, world.Owner, current, reports: true, billing: true);
        Assert.NotEqual(current, next);

        await Assert.ThrowsAsync<NotificationPreferencesVersionMismatchException>(
            () => SaveAsync(world.Tenant, world.Owner, current, reports: false));

        // Nobody else's preferences moved.
        await using (OmniAiDbContext context = database.CreateContext())
        {
            NotificationPreferences theirs = await new NotificationStore(context).PreferencesForSendingAsync(world.Tenant, colleague, now);

            Assert.False(theirs.EmailReports);
            Assert.False(theirs.EmailBilling);
        }
    }

    [Fact]
    public async Task Retention_removes_only_expired_notifications_their_emails_and_old_occurrences()
    {
        ReportWorld world = await ReportWorld.CreateAsync(database);
        NotificationHarness harness = new(database);

        await harness.EmailEverythingAsync(world.Tenant, world.Owner);

        await harness.RecordAsync(world.Tenant, NotificationKind.UsageWarning, "2026-06");
        await harness.DispatchAllAsync();

        harness.Clock.Advance(TimeSpan.FromDays(80));

        await harness.RecordAsync(world.Tenant, NotificationKind.UsageWarning, "2026-08");
        await harness.DispatchAllAsync();

        Assert.Equal(2, (await harness.NotificationsAsync(world.Tenant)).Count);
        Assert.Equal(2, (await harness.EmailsAsync(world.Tenant)).Count);

        // Twenty days later the first is 100 days old and the second 20.
        harness.Clock.Advance(TimeSpan.FromDays(20));

        await using (OmniAiDbContext context = database.CreateContext())
        {
            NotificationRetention retention = new(new NotificationStore(context), harness.Options, harness.Clock);

            Assert.True(await retention.SweepAsync(500) >= 2);

            // A second pass, as a second instance would run it, finds nothing left.
            Assert.Equal(0, await retention.SweepAsync(500));
        }

        Notification kept = Assert.Single(await harness.NotificationsAsync(world.Tenant));
        Assert.Contains("70%", kept.Title, StringComparison.Ordinal);
        Assert.Single(await harness.EmailsAsync(world.Tenant));
        Assert.Single(await harness.OccurrencesAsync(world.Tenant));

        // The source workspace is untouched.
        await using OmniAiDbContext check = database.CreateContext();
        Assert.True(await check.Tenants.AnyAsync(tenant => tenant.Id == world.Tenant));
    }

    private async Task<ConcurrencyToken> SaveAsync(
        TenantId tenantId,
        UserId userId,
        ConcurrencyToken expected,
        bool reports = false,
        bool billing = false)
    {
        await using OmniAiDbContext context = database.CreateContext();

        NotificationStore store = new(context);
        Versioned<NotificationPreferences> stored = await store.PreferencesAsync(tenantId, userId, DateTimeOffset.UtcNow);

        stored.Value.Choose(reports, usage: false, billing, knowledge: false, channelHealth: false, DateTimeOffset.UtcNow);

        return await store.SavePreferencesAsync(stored.Value, expected);
    }

    private static async Task<Exception?[]> Outcomes(params Task<ConcurrencyToken>[] attempts)
    {
        Exception?[] outcomes = new Exception?[attempts.Length];

        for (int index = 0; index < attempts.Length; index++)
        {
            try
            {
                await attempts[index];
            }
#pragma warning disable CA1031 // The test is about which exception each attempt ended with.
            catch (Exception failure)
#pragma warning restore CA1031
            {
                outcomes[index] = failure;
            }
        }

        return outcomes;
    }
}
