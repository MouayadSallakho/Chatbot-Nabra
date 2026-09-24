using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using OmniAI.Api.IntegrationTests.Analytics;
using OmniAI.Api.Realtime;
using OmniAI.Application.Ai;
using OmniAI.Application.Authorization;
using OmniAI.Application.Authorization.Persistence;
using OmniAI.Application.Security;
using OmniAI.Application.Tenants;
using OmniAI.Application.Tenants.Persistence;
using OmniAI.Domain.Notifications;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Testing;

namespace OmniAI.Api.IntegrationTests.Notifications;

/// <summary>A hosted application with report storage and fast notification workers.</summary>
public sealed class NotificationApiFixture : AnalyticsApiFixture
{
    public string StorageRoot { get; } = Path.Combine(Path.GetTempPath(), $"omniai-notification-api-{Guid.NewGuid():N}");

    internal RecordingAiTextGenerator Generator { get; } = new();

    protected override IReadOnlyDictionary<string, string> HostSettings =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Reports:Storage:RootPath"] = StorageRoot,
            ["Reports:Workers:IdleInterval"] = "00:00:00.200",
            ["Notifications:Workers:DispatchInterval"] = "00:00:00.200",
        };

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);

        services.Replace(ServiceDescriptor.Singleton<IAiTextGenerator>(Generator));
    }
}

/// <summary>
/// The notification HTTP surface: always the caller's own, and nothing else.
/// </summary>
public sealed class NotificationApiTests(NotificationApiFixture fixture) : IClassFixture<NotificationApiFixture>
{
    private const string TranscriptCanary = "CANARY-TRANSCRIPT-5d1e";

    // ------------------------------------------------------------ access

    [Fact]
    public async Task An_anonymous_caller_is_refused_everywhere()
    {
        (_, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        HttpClient anonymous = fixture.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(List(tenantId))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Unread(tenantId))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Preferences(tenantId))).StatusCode);
    }

    [Fact]
    public async Task A_stranger_is_told_the_workspace_is_not_there()
    {
        (_, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (HttpClient stranger, _, _) = await fixture.CreateOwnerAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(List(tenantId))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(Unread(tenantId))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsync($"{List(tenantId)}/read-all", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(Preferences(tenantId))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SavePreferencesAsync(stranger, tenantId, "\"AAAAAA\"", reports: true)).StatusCode);
    }

    [Fact]
    public async Task A_page_larger_than_the_bound_is_refused()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync($"{List(tenantId)}?pageSize=51")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync($"{List(tenantId)}?page=0")).StatusCode);
    }

    // ---------------------------------------------------------- the flow

    [Fact]
    public async Task A_finished_report_reaches_its_requester_s_bell_and_nobody_else_s()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (HttpClient colleague, _) = await fixture.CreateMemberWithEmailAsync(tenantId, Email(), "reports.view", "analytics.view");
        await fixture.SeedAsync(tenantId, "Support", customerMessages: 2, aiMessages: 1, text: TranscriptCanary);

        using HttpResponseMessage accepted = await owner.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/reports",
            new { reportType = "ConversationActivity", format = "Csv", range = "7d", clientRequestId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);

        await WaitForUnreadAsync(owner, tenantId, 1);

        using JsonDocument page = await ReadAsync(await owner.GetAsync(List(tenantId)));
        JsonElement item = Assert.Single(page.RootElement.GetProperty("items").EnumerateArray());

        Assert.Equal("ReportCompleted", item.GetProperty("kind").GetString());
        Assert.Equal("Reports", item.GetProperty("category").GetString());
        Assert.Equal("Information", item.GetProperty("severity").GetString());
        Assert.Equal("/reports", item.GetProperty("actionPath").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("readAt").ValueKind);
        Assert.Equal(1, page.RootElement.GetProperty("unreadCount").GetInt32());

        // Nothing in the payload that belongs to the report's data or to anybody else.
        string raw = page.RootElement.GetRawText();
        Assert.DoesNotContain(TranscriptCanary, raw, StringComparison.Ordinal);
        Assert.DoesNotContain("recipient", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("storage", raw, StringComparison.OrdinalIgnoreCase);

        // A colleague who may read reports was not the requester.
        Assert.Equal(0, await UnreadAsync(colleague, tenantId));

        Guid id = item.GetProperty("id").GetGuid();

        // Somebody else's notification is absent to them, whatever their permissions.
        Assert.Equal(HttpStatusCode.NotFound, (await colleague.PostAsync(Read(tenantId, id), null)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync(Read(tenantId, id), null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync(Read(tenantId, id), null)).StatusCode);
        Assert.Equal(0, await UnreadAsync(owner, tenantId));
    }

    [Fact]
    public async Task Mark_all_read_changes_only_the_caller_s_rows()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (HttpClient analyst, _) = await fixture.CreateMemberWithEmailAsync(tenantId, Email(), "analytics.view");

        await RecordAsync(tenantId, NotificationKind.UsageWarning, "2026-09");
        await RecordAsync(tenantId, NotificationKind.UsageCritical, "2026-09");

        await WaitForUnreadAsync(owner, tenantId, 2);
        await WaitForUnreadAsync(analyst, tenantId, 2);

        using JsonDocument marked = await ReadAsync(await analyst.PostAsync($"{List(tenantId)}/read-all", null));
        Assert.Equal(2, marked.RootElement.GetProperty("marked").GetInt32());

        Assert.Equal(0, await UnreadAsync(analyst, tenantId));
        Assert.Equal(2, await UnreadAsync(owner, tenantId));

        using JsonDocument again = await ReadAsync(await analyst.PostAsync($"{List(tenantId)}/read-all", null));
        Assert.Equal(0, again.RootElement.GetProperty("marked").GetInt32());
    }

    [Fact]
    public async Task A_notification_id_from_another_workspace_is_not_found()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (HttpClient rival, Guid rivalTenant, _) = await fixture.CreateOwnerAsync("Rival Trading");

        await RecordAsync(tenantId, NotificationKind.BillingPastDue, "sub:PastDue:api");
        await WaitForUnreadAsync(owner, tenantId, 1);

        using JsonDocument page = await ReadAsync(await owner.GetAsync(List(tenantId)));
        Guid id = page.RootElement.GetProperty("items")[0].GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await rival.PostAsync(Read(rivalTenant, id), null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await rival.PostAsync(Read(tenantId, id), null)).StatusCode);
        Assert.Equal(0, await UnreadAsync(rival, rivalTenant));
        Assert.Equal(1, await UnreadAsync(owner, tenantId));
    }

    // ------------------------------------------------------- preferences

    [Fact]
    public async Task Preferences_are_the_caller_s_own_and_need_the_version_they_read()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (HttpClient colleague, _) = await fixture.CreateMemberWithEmailAsync(tenantId, Email(), "reports.view");

        using HttpResponseMessage read = await owner.GetAsync(Preferences(tenantId));
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        string etag = read.Headers.ETag!.Tag;

        using (JsonDocument initial = await ReadAsync(read))
        {
            JsonElement email = initial.RootElement.GetProperty("email");

            foreach (string category in new[] { "reports", "usage", "billing", "knowledge", "channelHealth" })
            {
                Assert.False(email.GetProperty(category).GetBoolean());
            }

            // This host has no SMTP server configured, and says so.
            Assert.False(initial.RootElement.GetProperty("emailConfigured").GetBoolean());
        }

        Assert.Equal((HttpStatusCode)428, (await SavePreferencesAsync(owner, tenantId, null, reports: true)).StatusCode);

        using HttpResponseMessage saved = await SavePreferencesAsync(owner, tenantId, etag, reports: true);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.NotEqual(etag, saved.Headers.ETag!.Tag);

        // The version this tab read is now stale.
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await SavePreferencesAsync(owner, tenantId, etag, reports: false)).StatusCode);

        // Every category must be stated: there is no partial update.
        using HttpResponseMessage partial = await owner.SendAsync(new HttpRequestMessage(HttpMethod.Put, Preferences(tenantId))
        {
            Content = JsonContent.Create(new { email = new { reports = true } }),
            Headers = { { "If-Match", saved.Headers.ETag!.Tag } },
        });
        Assert.Equal(HttpStatusCode.BadRequest, partial.StatusCode);

        // The colleague's preferences did not move.
        using JsonDocument theirs = await ReadAsync(await colleague.GetAsync(Preferences(tenantId)));
        Assert.False(theirs.RootElement.GetProperty("email").GetProperty("reports").GetBoolean());
    }

    // ---------------------------------------------------------- realtime

    [Fact]
    public async Task The_realtime_signal_goes_only_to_each_recipient_s_own_group_and_carries_only_the_workspace()
    {
        RecordingHubContext hub = new();
        SignalRNotificationRealtime realtime = new(hub, NullLogger<SignalRNotificationRealtime>.Instance);

        TenantId tenant = TenantId.From(Guid.NewGuid());
        UserId first = UserId.New();
        UserId second = UserId.New();

        await realtime.NotificationsChangedAsync(tenant, [first, second]);

        (IReadOnlyList<string> groups, string method, object?[] args) = Assert.Single(hub.Sent);

        Assert.Equal("notificationsChanged", method);
        Assert.Equal(
            new[] { RealtimeGroups.ForMember(tenant.Value, first.Value), RealtimeGroups.ForMember(tenant.Value, second.Value) }.Order(),
            groups.Order());

        // The workspace id and nothing else: no title, no body, no id, no count.
        object? payload = Assert.Single(args);
        Assert.Equal(tenant.Value, payload);

        // Nobody else's group, and never the whole workspace's.
        Assert.DoesNotContain(RealtimeGroups.ForTenant(tenant.Value), groups);

        await realtime.NotificationsChangedAsync(tenant, []);
        Assert.Single(hub.Sent);
    }

    [Fact]
    public async Task Only_a_member_may_listen_and_only_to_their_own_group()
    {
        (_, Guid tenantId, Guid ownerId) = await fixture.CreateOwnerAsync();
        (_, _, Guid strangerId) = await fixture.CreateOwnerAsync("Elsewhere");

        RecordingGroups memberGroups = new();
        await using (AsyncServiceScope scope = fixture.Services.CreateAsyncScope())
        {
            using WorkspaceInboxHub hub = Hub(scope, ownerId, memberGroups);

            await hub.SubscribeNotifications(tenantId);
        }

        Assert.Equal([RealtimeGroups.ForMember(tenantId, ownerId)], memberGroups.Added);

        RecordingGroups strangerGroups = new();
        await using (AsyncServiceScope scope = fixture.Services.CreateAsyncScope())
        {
            using WorkspaceInboxHub hub = Hub(scope, strangerId, strangerGroups);

            await Assert.ThrowsAsync<HubException>(() => hub.SubscribeNotifications(tenantId));
        }

        Assert.Empty(strangerGroups.Added);
    }

    // ----------------------------------------------------------------- helpers

    private static string List(Guid tenantId) => $"/api/tenants/{tenantId}/notifications";

    private static string Unread(Guid tenantId) => $"{List(tenantId)}/unread-count";

    private static string Preferences(Guid tenantId) => $"{List(tenantId)}/preferences";

    private static string Read(Guid tenantId, Guid id) => $"{List(tenantId)}/{id}/read";

    private static string Email() => $"member-{Guid.NewGuid():N}@example.test";

    private static async Task<HttpResponseMessage> SavePreferencesAsync(HttpClient client, Guid tenantId, string? etag, bool reports)
    {
        HttpRequestMessage request = new(HttpMethod.Put, Preferences(tenantId))
        {
            Content = JsonContent.Create(new
            {
                email = new { reports, usage = false, billing = false, knowledge = false, channelHealth = false },
            }),
        };

        if (etag is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", etag);
        }

        return await client.SendAsync(request);
    }

    private static async Task<int> UnreadAsync(HttpClient client, Guid tenantId)
    {
        using JsonDocument body = await ReadAsync(await client.GetAsync(Unread(tenantId)));

        return body.RootElement.GetProperty("unreadCount").GetInt32();
    }

    private static async Task WaitForUnreadAsync(HttpClient client, Guid tenantId, int expected)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(30);

        while (await UnreadAsync(client, tenantId) < expected)
        {
            Assert.True(DateTimeOffset.UtcNow < deadline, $"No {expected} unread notifications within 30 seconds.");

            await Task.Delay(100);
        }
    }

    private static async Task<JsonDocument> ReadAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private async Task RecordAsync(Guid tenantId, NotificationKind kind, string key)
    {
        await using OmniAiDbContext context = fixture.CreateDbContext();

        context.NotificationOccurrences.Add(
            NotificationOccurrence.Record(TenantId.From(tenantId), kind, key, null, DateTimeOffset.UtcNow));

        await context.SaveChangesAsync();
    }

    private static WorkspaceInboxHub Hub(AsyncServiceScope scope, Guid userId, RecordingGroups groups)
    {
        TenantAuthorizer authorizer = new(
            new TenantAccessGuard(
                new FixedCurrentUser(UserId.From(userId)),
                scope.ServiceProvider.GetRequiredService<ITenantMembershipRepository>()),
            scope.ServiceProvider.GetRequiredService<ITenantRoleRepository>());

        return new WorkspaceInboxHub(authorizer)
        {
            Context = new FakeCallerContext(),
            Groups = groups,
        };
    }

    private sealed class FixedCurrentUser(UserId id) : ICurrentUser
    {
        public bool IsAuthenticated => true;

        public UserId? Id { get; } = id;
    }

    private sealed class FakeCallerContext : HubCallerContext
    {
        public override string ConnectionId { get; } = Guid.NewGuid().ToString("N");

        public override string? UserIdentifier => null;

        public override System.Security.Claims.ClaimsPrincipal? User => null;

        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();

        public override Microsoft.AspNetCore.Http.Features.IFeatureCollection Features { get; } =
            new Microsoft.AspNetCore.Http.Features.FeatureCollection();

        public override CancellationToken ConnectionAborted => CancellationToken.None;

        public override void Abort()
        {
        }
    }

    private sealed class RecordingGroups : IGroupManager
    {
        public List<string> Added { get; } = [];

        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            Added.Add(groupName);

            return Task.CompletedTask;
        }

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    /// <summary>Records what would have been sent, to whom, and with what arguments.</summary>
    private sealed class RecordingHubContext : IHubContext<WorkspaceInboxHub>
    {
        private readonly RecordingClients _clients = new();

        public List<(IReadOnlyList<string> Groups, string Method, object?[] Args)> Sent => _clients.Sent;

        public IHubClients Clients => _clients;

        public IGroupManager Groups { get; } = new RecordingGroups();

        private sealed class RecordingClients : IHubClients
        {
            public List<(IReadOnlyList<string> Groups, string Method, object?[] Args)> Sent { get; } = [];

            public IClientProxy Groups(IReadOnlyList<string> groupNames) => new Proxy(this, groupNames);

            public IClientProxy Group(string groupName) => new Proxy(this, [groupName]);

            public IClientProxy All => throw new InvalidOperationException("Nothing may be broadcast to everybody.");

            public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new InvalidOperationException();

            public IClientProxy Client(string connectionId) => throw new InvalidOperationException();

            public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw new InvalidOperationException();

            public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new InvalidOperationException();

            public IClientProxy User(string userId) => throw new InvalidOperationException();

            public IClientProxy Users(IReadOnlyList<string> userIds) => throw new InvalidOperationException();
        }

        private sealed class Proxy(RecordingClients owner, IReadOnlyList<string> groups) : IClientProxy
        {
            public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
            {
                owner.Sent.Add((groups, method, args));

                return Task.CompletedTask;
            }
        }
    }
}
