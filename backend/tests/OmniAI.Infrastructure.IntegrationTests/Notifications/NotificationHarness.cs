using Microsoft.EntityFrameworkCore;
using OmniAI.Application.Notifications;
using OmniAI.Application.Reports;
using OmniAI.Domain.Notifications;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;
using OmniAI.Infrastructure.Identity;
using OmniAI.Infrastructure.Notifications;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Infrastructure.Persistence.Repositories;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Notifications;

/// <summary>Records every structural signal, so a test can say who was signalled and with what.</summary>
internal sealed class RecordingRealtime : INotificationRealtime
{
    private readonly List<(TenantId Tenant, UserId[] Recipients)> _signals = [];

    public IReadOnlyList<(TenantId Tenant, UserId[] Recipients)> Signals
    {
        get
        {
            lock (_signals)
            {
                return [.. _signals];
            }
        }
    }

    public Task NotificationsChangedAsync(
        TenantId tenantId,
        IReadOnlyCollection<UserId> recipients,
        CancellationToken cancellationToken = default)
    {
        lock (_signals)
        {
            _signals.Add((tenantId, [.. recipients]));
        }

        return Task.CompletedTask;
    }
}

/// <summary>A sender that records what it was given and answers as it is told to.</summary>
internal sealed class RecordingEmailSender(bool configured = true) : IOperationalEmailSender
{
    private readonly List<OperationalEmailMessage> _sent = [];

    public OperationalEmailResult Next { get; set; } = OperationalEmailResult.Delivered;

    public bool IsConfigured { get; } = configured;

    public IReadOnlyList<OperationalEmailMessage> Sent
    {
        get
        {
            lock (_sent)
            {
                return [.. _sent];
            }
        }
    }

    public Task<OperationalEmailResult> SendAsync(OperationalEmailMessage message, CancellationToken cancellationToken = default)
    {
        lock (_sent)
        {
            _sent.Add(message);
        }

        return Task.FromResult(Next);
    }
}

/// <summary>
/// The notification pipeline over real stores and a real PostgreSQL, with the
/// transport and the realtime channel replaced by recorders.
/// </summary>
internal sealed class NotificationHarness(PostgresTestDatabase database)
{
    public static readonly Uri AppBase = new("https://app.omniai.example/");

    private readonly PostgresTestDatabase _database = database ?? throw new ArgumentNullException(nameof(database));

    public RecordingRealtime Realtime { get; } = new();

    public RecordingEmailSender Email { get; set; } = new();

    public FixedTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);

    public NotificationOptions Options { get; } = new();

    /// <summary>Dispatches every occurrence waiting, the way the worker does. Returns how many.</summary>
    public async Task<int> DispatchAllAsync(int batchSize = 50)
    {
        IReadOnlyList<ClaimedOccurrence> claimed;

        await using (OmniAiDbContext context = _database.CreateContext())
        {
            claimed = await new NotificationOccurrenceStore(context)
                .ClaimAsync(batchSize, maxAttempts: 5, TimeSpan.FromMinutes(5), Clock.GetUtcNow());
        }

        foreach (ClaimedOccurrence occurrence in claimed)
        {
            await using OmniAiDbContext context = _database.CreateContext();

            NotificationWorkResult result = await Dispatcher(context).DispatchAsync(occurrence);

            Assert.True(result.Outcome == NotificationWorkOutcome.Done, result.Fault?.ToString());
        }

        return claimed.Count;
    }

    public NotificationDispatcher Dispatcher(OmniAiDbContext context)
    {
        NotificationDirectory directory = new(context);

        return new NotificationDispatcher(
            new NotificationOccurrenceStore(context),
            directory,
            directory,
            Authority(context),
            new NotificationStore(context),
            Email,
            Realtime,
            Options,
            Clock);
    }

    /// <summary>Sends every email that is due, the way the worker does. Returns the statuses recorded.</summary>
    public async Task<IReadOnlyList<NotificationEmailStatus?>> SendAllEmailsAsync(int maxAttempts = 3)
    {
        IReadOnlyList<ClaimedEmail> claimed;

        await using (OmniAiDbContext context = _database.CreateContext())
        {
            claimed = await new NotificationEmailOutbox(context).ClaimAsync(50, maxAttempts, Clock.GetUtcNow());
        }

        List<NotificationEmailStatus?> statuses = [];

        foreach (ClaimedEmail email in claimed)
        {
            await using OmniAiDbContext context = _database.CreateContext();

            statuses.Add(await EmailDispatcher(context).SendAsync(email, maxAttempts));
        }

        return statuses;
    }

    public OperationalEmailDispatcher EmailDispatcher(OmniAiDbContext context) =>
        new(
            new NotificationEmailOutbox(context),
            new NotificationStore(context),
            Authority(context),
            new IdentityAccountDirectory(context),
            new TenantRepository(context),
            Email,
            new OperationalEmailLinks(AppBase),
            Clock);

    public static ReportAuthority Authority(OmniAiDbContext context) =>
        new(new TenantMembershipRepository(context), new TenantRoleRepository(context));

    public async Task<List<Notification>> NotificationsAsync(TenantId tenantId, UserId? recipient = null)
    {
        await using OmniAiDbContext context = _database.CreateContext();

        return await context.Notifications
            .AsNoTracking()
            .Where(notification => notification.TenantId == tenantId
                && (recipient == null || notification.RecipientUserId == recipient))
            .OrderBy(notification => notification.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<NotificationOccurrence>> OccurrencesAsync(TenantId tenantId)
    {
        await using OmniAiDbContext context = _database.CreateContext();

        return await context.NotificationOccurrences
            .AsNoTracking()
            .Where(occurrence => occurrence.TenantId == tenantId)
            .OrderBy(occurrence => occurrence.OccurredAt)
            .ToListAsync();
    }

    public async Task<List<NotificationEmail>> EmailsAsync(TenantId tenantId)
    {
        await using OmniAiDbContext context = _database.CreateContext();

        return await context.NotificationEmails
            .AsNoTracking()
            .Where(email => email.TenantId == tenantId)
            .ToListAsync();
    }

    public async Task RecordAsync(TenantId tenantId, NotificationKind kind, string key, Guid? source = null)
    {
        await using OmniAiDbContext context = _database.CreateContext();

        await new NotificationOccurrenceStore(context)
            .RecordAsync(NotificationOccurrence.Record(tenantId, kind, key, source, Clock.GetUtcNow()));
    }

    /// <summary>Turns one member's email on for every category.</summary>
    public async Task EmailEverythingAsync(TenantId tenantId, UserId userId, bool on = true)
    {
        await using OmniAiDbContext context = _database.CreateContext();

        NotificationStore store = new(context);

        var current = await store.PreferencesAsync(tenantId, userId, Clock.GetUtcNow());
        current.Value.Choose(on, on, on, on, on, Clock.GetUtcNow());

        await store.SavePreferencesAsync(current.Value, current.Version);
    }
}
