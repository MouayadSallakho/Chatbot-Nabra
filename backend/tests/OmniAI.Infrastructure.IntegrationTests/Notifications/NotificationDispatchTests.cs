using Microsoft.EntityFrameworkCore;
using OmniAI.Application.Notifications;
using OmniAI.Application.Reports;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Notifications;
using OmniAI.Domain.Reports;
using OmniAI.Domain.Users;
using OmniAI.Domain.WhatsApp;
using OmniAI.Infrastructure.IntegrationTests.Reports;
using OmniAI.Infrastructure.Notifications;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Infrastructure.Persistence.Repositories;
using OmniAI.Infrastructure.Reports;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Notifications;

/// <summary>
/// Authoritative transitions becoming notifications, against a real PostgreSQL.
/// </summary>
/// <remarks>
/// Each integration is driven through the store that owns the transition — the
/// report store, the WhatsApp outbox — so the occurrence is proven to be written
/// by the change itself, and then dispatched exactly as the worker would.
/// </remarks>
public sealed class NotificationDispatchTests(PostgresTestDatabase database)
    : IClassFixture<PostgresTestDatabase>
{
    // ---------------------------------------------------------------- reports

    [Fact]
    public async Task A_completed_report_notifies_its_requester_and_nobody_else()
    {
        ReportWorld world = await ReportWorld.CreateAsync(database);
        UserId requester = await world.MemberAsync(PlatformPermissions.ReportsView, PlatformPermissions.ReportsGenerate, PlatformPermissions.AnalyticsView);
        UserId colleague = await world.MemberAsync(PlatformPermissions.ReportsView, PlatformPermissions.AnalyticsView);
        NotificationHarness harness = new(database);

        ReportExecution execution = await world.PendingAsync(requester: requester);
        await CompleteAsync(execution);

        // The occurrence was written by the completion itself.
        NotificationOccurrence occurrence = Assert.Single(await harness.OccurrencesAsync(world.Tenant));
        Assert.Equal(NotificationKind.ReportCompleted, occurrence.Kind);
        Assert.Equal(execution.Id.Value, occurrence.SourceId);

        Assert.Equal(1, await harness.DispatchAllAsync());

        Notification notification = Assert.Single(await harness.NotificationsAsync(world.Tenant));
        Assert.Equal(requester, notification.RecipientUserId);
        Assert.Equal(NotificationCategory.Reports, notification.Category);
        Assert.Equal(NotificationSeverity.Information, notification.Severity);
        Assert.Equal("/reports", notification.ActionPath);
        Assert.Contains("Channel usage", notification.Body, StringComparison.Ordinal);

        // Being a member with reports.view is not enough: it was not their report.
        Assert.Empty(await harness.NotificationsAsync(world.Tenant, colleague));
        Assert.Empty(await harness.NotificationsAsync(world.Tenant, world.Owner));

        (_, UserId[] signalled) = Assert.Single(harness.Realtime.Signals);
        Assert.Equal([requester], signalled);
    }

    [Fact]
    public async Task A_failed_report_notifies_its_requester_with_a_warning()
    {
        ReportWorld world = await ReportWorld.CreateAsync(database);
        NotificationHarness harness = new(database);

        ReportExecution manual = await world.PendingAsync();
        ClaimedReportExecution claimed = await ClaimAsync(manual);

        await using (OmniAiDbContext context = database.CreateContext())
        {
            Assert.True(await new ReportExecutionStore(context)
                .FailAsync(manual.Id, claimed.ClaimToken, ReportFailures.TooLarge, DateTimeOffset.UtcNow));
        }

        await harness.DispatchAllAsync();

        Notification failed = Assert.Single(await harness.NotificationsAsync(world.Tenant));
        Assert.Equal(NotificationKind.ReportFailed, failed.Kind);
        Assert.Equal(NotificationSeverity.Warning, failed.Severity);
        Assert.Equal(world.Owner, failed.RecipientUserId);
    }

    [Fact]
    public async Task A_scheduled_occurrence_notifies_the_schedule_owner_once_even_when_processing_replays()
    {
        ReportWorld world = await ReportWorld.CreateAsync(database);
        UserId owner = await world.MemberAsync(
            PlatformPermissions.ReportsView, PlatformPermissions.ReportsGenerate, PlatformPermissions.ReportsSchedule, PlatformPermissions.AnalyticsView);
        NotificationHarness harness = new(database);

        ReportSchedule schedule = ReportSchedule.Create(
            world.Tenant, "Weekly channels", ReportType.ChannelUsage, ReportFormat.Csv, ReportFrequency.Weekly, enabled: true, owner, DateTimeOffset.UtcNow);

        await using (OmniAiDbContext context = database.CreateContext())
        {
            await new ReportScheduleStore(context).AddAsync(schedule);
        }

        ReportPeriod period = ReportScheduleCalendar.PeriodEndingAt(
            ReportFrequency.Weekly, ReportScheduleCalendar.PreviousBoundary(ReportFrequency.Weekly, DateTimeOffset.UtcNow));

        ReportExecution occurrence = ReportExecution.ForSchedule(schedule, period, DateTimeOffset.UtcNow);

        await using (OmniAiDbContext context = database.CreateContext())
        {
            context.ReportExecutions.Add(occurrence);
            await context.SaveChangesAsync();
        }

        await CompleteAsync(occurrence);

        // Occurrence processing replayed: dispatched, then dispatched again.
        await harness.DispatchAllAsync();
        await harness.DispatchAllAsync();

        Notification notification = Assert.Single(await harness.NotificationsAsync(world.Tenant));
        Assert.Equal(NotificationKind.ScheduledReportCompleted, notification.Kind);
        Assert.Equal(owner, notification.RecipientUserId);

        // The schedule's workspace-authored name never reaches the text.
        Assert.DoesNotContain("Weekly channels", notification.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Replaying_the_same_completion_and_dispatch_produces_one_notification()
    {
        ReportWorld world = await ReportWorld.CreateAsync(database);
        NotificationHarness harness = new(database);

        ReportExecution execution = await world.PendingAsync();
        await CompleteAsync(execution);

        // The same transition recorded again, as a retried statement would.
        await harness.RecordAsync(world.Tenant, NotificationKind.ReportCompleted, $"{execution.Id.Value:D}:Completed", execution.Id.Value);

        Assert.Single(await harness.OccurrencesAsync(world.Tenant));

        await harness.DispatchAllAsync();
        Assert.Equal(0, await harness.DispatchAllAsync());

        Assert.Single(await harness.NotificationsAsync(world.Tenant));
    }

    [Fact]
    public async Task Two_workers_racing_one_occurrence_past_a_lapsed_lease_write_it_once()
    {
        ReportWorld world = await ReportWorld.CreateAsync(database);
        NotificationHarness harness = new(database);

        ReportExecution execution = await world.PendingAsync();
        await CompleteAsync(execution);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        ClaimedOccurrence first;
        ClaimedOccurrence second;

        await using (OmniAiDbContext context = database.CreateContext())
        {
            first = Assert.Single(await new NotificationOccurrenceStore(context).ClaimAsync(10, 5, TimeSpan.FromMinutes(1), now));
        }

        // The first worker stalls; its lease lapses and a second one takes over.
        await using (OmniAiDbContext context = database.CreateContext())
        {
            second = Assert.Single(await new NotificationOccurrenceStore(context).ClaimAsync(10, 5, TimeSpan.FromMinutes(1), now.AddMinutes(2)));
        }

        Assert.NotEqual(first.ClaimToken, second.ClaimToken);

        NotificationWorkResult[] results = await Task.WhenAll(
            DispatchInOwnContextAsync(harness, first),
            DispatchInOwnContextAsync(harness, second));

        Assert.Contains(results, result => result.Outcome == NotificationWorkOutcome.Superseded);
        Assert.Single(await harness.NotificationsAsync(world.Tenant));
    }

    [Fact]
    public async Task A_requester_who_lost_reports_view_is_not_told()
    {
        ReportWorld world = await ReportWorld.CreateAsync(database);
        UserId requester = await world.MemberAsync(PlatformPermissions.ReportsView, PlatformPermissions.ReportsGenerate, PlatformPermissions.AnalyticsView);
        NotificationHarness harness = new(database);

        ReportExecution execution = await world.PendingAsync(requester: requester);
        await CompleteAsync(execution);
        await world.RevokeAsync(requester);

        await harness.DispatchAllAsync();

        Assert.Empty(await harness.NotificationsAsync(world.Tenant));
        Assert.Empty(harness.Realtime.Signals);

        // Processed all the same: an occurrence with nobody to tell is done, not stuck.
        Assert.NotNull(Assert.Single(await harness.OccurrencesAsync(world.Tenant)).ProcessedAt);
    }

    // ---------------------------------------------------------------- WhatsApp

    [Fact]
    public async Task A_delivery_becoming_unknown_alerts_channel_administrators_only_and_carries_no_number()
    {
        ReportWorld world = await ReportWorld.CreateAsync(database);
        await world.SeedActivityAsync();
        UserId admin = await world.MemberAsync(PlatformPermissions.WhatsAppManage, PlatformPermissions.WhatsAppView);
        UserId viewer = await world.MemberAsync(PlatformPermissions.WhatsAppView, PlatformPermissions.ConversationsView);
        NotificationHarness harness = new(database);

        WhatsAppDelivery delivery = await DeliveryAsync(world);

        await using (OmniAiDbContext context = database.CreateContext())
        {
            await new WhatsAppOutbox(context).MarkUnknownAsync(delivery.Id, "timeout", DateTimeOffset.UtcNow);
        }

        await harness.DispatchAllAsync();

        List<Notification> notifications = await harness.NotificationsAsync(world.Tenant);

        // The Owner holds everything, the administrator holds whatsapp.manage;
        // somebody who may only read the channel is not alerted.
        Assert.Equal(
            new[] { world.Owner, admin }.OrderBy(user => user.Value),
            notifications.Select(notification => notification.RecipientUserId).OrderBy(user => user.Value));
        Assert.DoesNotContain(notifications, notification => notification.RecipientUserId == viewer);

        Notification alert = notifications[0];
        Assert.Equal(NotificationKind.WhatsAppDeliveryUnknown, alert.Kind);
        Assert.Equal(NotificationSeverity.Warning, alert.Severity);
        Assert.Equal($"/channels/whatsapp/{delivery.ConnectionId.Value:D}", alert.ActionPath);

        foreach (Notification notification in notifications)
        {
            string visible = notification.Title + notification.Body + notification.ActionPath;

            Assert.DoesNotContain(ReportWorld.RawNumberCanary, visible, StringComparison.Ordinal);
            Assert.DoesNotContain(ReportWorld.MessageCanary, visible, StringComparison.Ordinal);
            Assert.DoesNotContain("timeout", visible, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Ordinary_delivery_outcomes_alert_nobody()
    {
        ReportWorld world = await ReportWorld.CreateAsync(database);
        await world.SeedActivityAsync();
        NotificationHarness harness = new(database);

        WhatsAppDelivery delivery = await DeliveryAsync(world);

        await using (OmniAiDbContext context = database.CreateContext())
        {
            await new WhatsAppOutbox(context).MarkSentAsync(
                delivery.Id, ProviderMessageId.Create("wamid.CANARY-PROVIDER-ID"), DateTimeOffset.UtcNow);
        }

        Assert.Empty(await harness.OccurrencesAsync(world.Tenant));
    }

    [Fact]
    public async Task A_send_abandoned_by_a_dead_worker_is_an_alert_too()
    {
        ReportWorld world = await ReportWorld.CreateAsync(database);
        await world.SeedActivityAsync();
        NotificationHarness harness = new(database);

        WhatsAppDelivery delivery = await DeliveryAsync(world);

        await using (OmniAiDbContext context = database.CreateContext())
        {
            await context.WhatsAppDeliveries
                .Where(candidate => candidate.Id == delivery.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.Status, WhatsAppDeliveryStatus.Sending));

            await context.Database.ExecuteSqlRawAsync(
                "UPDATE whatsapp_deliveries SET updated_at = now() - interval '2 hours' WHERE id = {0}", delivery.Id.Value);

            Assert.True(await new WhatsAppOutbox(context).AbandonStaleSendsAsync(DateTimeOffset.UtcNow.AddHours(-1)) >= 1);
        }

        NotificationOccurrence occurrence = Assert.Single(await harness.OccurrencesAsync(world.Tenant));
        Assert.Equal(NotificationKind.WhatsAppDeliveryUnknown, occurrence.Kind);
        Assert.Equal(delivery.Id.Value, occurrence.SourceId);
    }

    // --------------------------------------------------------------- knowledge

    [Fact]
    public async Task A_knowledge_failure_reaches_knowledge_managers_and_says_nothing_of_the_document()
    {
        ReportWorld world = await ReportWorld.CreateAsync(database);
        await world.SeedActivityAsync();
        UserId manager = await world.MemberAsync(PlatformPermissions.KnowledgeManage, PlatformPermissions.KnowledgeView);
        UserId reader = await world.MemberAsync(PlatformPermissions.KnowledgeView);
        NotificationHarness harness = new(database);

        Guid document;

        await using (OmniAiDbContext context = database.CreateContext())
        {
            document = (await context.KnowledgeDocuments.FirstAsync(candidate => candidate.TenantId == world.Tenant)).Id.Value;
        }

        // Two separate failed attempts are two occurrences, not one.
        await harness.RecordAsync(world.Tenant, NotificationKind.KnowledgeIndexingFailed, $"{document:D}:{Guid.NewGuid():D}", document);
        await harness.RecordAsync(world.Tenant, NotificationKind.KnowledgeIndexingFailed, $"{document:D}:{Guid.NewGuid():D}", document);

        await harness.DispatchAllAsync();

        List<Notification> notifications = await harness.NotificationsAsync(world.Tenant, manager);

        Assert.Equal(2, notifications.Count);
        Assert.Empty(await harness.NotificationsAsync(world.Tenant, reader));
        Assert.All(notifications, notification =>
        {
            Assert.Equal($"/knowledge/{document:D}", notification.ActionPath);
            Assert.DoesNotContain(ReportWorld.KnowledgeCanary, notification.Body, StringComparison.Ordinal);
            Assert.DoesNotContain("Policy", notification.Body, StringComparison.Ordinal);
        });
    }

    // ------------------------------------------------------------------- email

    [Fact]
    public async Task Email_is_queued_only_for_members_who_turned_the_category_on()
    {
        ReportWorld world = await ReportWorld.CreateAsync(database);
        UserId opted = await world.MemberAsync(PlatformPermissions.BillingView);
        UserId quiet = await world.MemberAsync(PlatformPermissions.BillingView);
        NotificationHarness harness = new(database);

        await harness.EmailEverythingAsync(world.Tenant, opted);
        await harness.RecordAsync(world.Tenant, NotificationKind.BillingPastDue, "sub:PastDue:1");

        await harness.DispatchAllAsync();

        // Everybody holding billing.view is told in the app ...
        Assert.Equal(3, (await harness.NotificationsAsync(world.Tenant)).Count);

        // ... and only the one who asked for it is emailed.
        NotificationEmail email = Assert.Single(await harness.EmailsAsync(world.Tenant));
        Assert.Equal(opted, email.RecipientUserId);
        Assert.Equal(NotificationEmailStatus.Pending, email.Status);
        Assert.Single(await harness.NotificationsAsync(world.Tenant, quiet));
    }

    [Fact]
    public async Task A_deployment_without_email_queues_none_and_still_notifies_in_the_app()
    {
        ReportWorld world = await ReportWorld.CreateAsync(database);
        NotificationHarness harness = new(database) { Email = new RecordingEmailSender(configured: false) };

        await harness.EmailEverythingAsync(world.Tenant, world.Owner);
        await harness.RecordAsync(world.Tenant, NotificationKind.UsageWarning, "2026-09");

        await harness.DispatchAllAsync();

        Assert.Single(await harness.NotificationsAsync(world.Tenant));
        Assert.Empty(await harness.EmailsAsync(world.Tenant));
    }

    // ----------------------------------------------------------------- helpers

    private async Task<ClaimedReportExecution> ClaimAsync(ReportExecution execution)
    {
        await using OmniAiDbContext context = database.CreateContext();

        IReadOnlyList<ClaimedReportExecution> claimed = await new ReportExecutionStore(context)
            .ClaimAsync(50, 3, TimeSpan.FromMinutes(10), DateTimeOffset.UtcNow);

        return Assert.Single(claimed, candidate => candidate.Id == execution.Id);
    }

    private async Task CompleteAsync(ReportExecution execution)
    {
        ClaimedReportExecution claimed = await ClaimAsync(execution);

        await using OmniAiDbContext context = database.CreateContext();

        ReportArtifact artifact = ReportArtifact.Create(
            execution.TenantId,
            execution.Id,
            execution.Format,
            ReportCatalog.ContentType(execution.Format),
            10,
            new string('a', 64),
            $"{Guid.NewGuid():N}",
            DateTimeOffset.UtcNow,
            TimeSpan.FromDays(30));

        Assert.True(await new ReportExecutionStore(context)
            .CompleteAsync(execution.Id, claimed.ClaimToken, artifact, DateTimeOffset.UtcNow));
    }

    private async Task<NotificationWorkResult> DispatchInOwnContextAsync(NotificationHarness harness, ClaimedOccurrence occurrence)
    {
        await using OmniAiDbContext context = database.CreateContext();

        return await harness.Dispatcher(context).DispatchAsync(occurrence);
    }

    private async Task<WhatsAppDelivery> DeliveryAsync(ReportWorld world)
    {
        await using OmniAiDbContext context = database.CreateContext();

        return await context.WhatsAppDeliveries.AsNoTracking().FirstAsync(candidate => candidate.TenantId == world.Tenant);
    }
}
