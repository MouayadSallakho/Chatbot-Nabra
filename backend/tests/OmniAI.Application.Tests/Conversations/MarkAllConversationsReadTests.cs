using OmniAI.Application.Authorization;
using OmniAI.Application.Conversations;
using OmniAI.Application.Tests.Security;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;
using OmniAI.Testing;

namespace OmniAI.Application.Tests.Conversations;

/// <summary>
/// Phase 39.1 (note 6): mark all read, for the caller only, announced to the
/// caller's own sessions only when something moved, and never failed by the
/// announcement.
/// </summary>
public sealed class MarkAllConversationsReadTests
{
    private static readonly Guid Workspace = Guid.CreateVersion7();
    private static readonly UserId Caller = UserId.New();

    private static readonly DateTimeOffset Now =
        new(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);

    private static (FakeConversationInboxStore Store, RecordingReadRealtime Realtime, MarkAllConversationsReadHandler Handler)
        Create(TenantAuthorizer? authorizer = null, bool realtimeThrows = false)
    {
        FakeConversationInboxStore store = new();
        RecordingReadRealtime realtime = new() { Throws = realtimeThrows };

        return (
            store,
            realtime,
            new MarkAllConversationsReadHandler(
                authorizer ?? TestAuthority.Owner(Caller, Workspace),
                store,
                realtime,
                new FixedTimeProvider(Now)));
    }

    [Fact]
    public async Task It_marks_for_the_caller_and_tells_only_the_caller_once_something_moved()
    {
        (FakeConversationInboxStore store, RecordingReadRealtime realtime, MarkAllConversationsReadHandler handler) = Create();
        store.UnreadForAll = 7;

        Assert.Equal(7, await handler.CountUnreadAsync(new CountUnreadConversationsQuery(Workspace)));
        Assert.Equal(7, await handler.MarkAllAsync(new MarkAllConversationsReadCommand(Workspace)));

        (TenantId tenant, UserId member) = Assert.Single(store.MarkedAll);
        Assert.Equal(TenantId.From(Workspace), tenant);
        Assert.Equal(Caller, member);

        (TenantId announcedTenant, UserId announcedMember) = Assert.Single(realtime.Announced);
        Assert.Equal(TenantId.From(Workspace), announcedTenant);
        Assert.Equal(Caller, announcedMember);
    }

    [Fact]
    public async Task A_second_call_marks_nothing_and_announces_nothing()
    {
        (FakeConversationInboxStore store, RecordingReadRealtime realtime, MarkAllConversationsReadHandler handler) = Create();
        store.UnreadForAll = 3;

        Assert.Equal(3, await handler.MarkAllAsync(new MarkAllConversationsReadCommand(Workspace)));
        Assert.Equal(0, await handler.MarkAllAsync(new MarkAllConversationsReadCommand(Workspace)));
        Assert.Equal(0, await handler.CountUnreadAsync(new CountUnreadConversationsQuery(Workspace)));

        Assert.Single(realtime.Announced);
    }

    [Fact]
    public async Task A_failing_announcement_never_fails_the_write()
    {
        (FakeConversationInboxStore store, RecordingReadRealtime realtime, MarkAllConversationsReadHandler handler) =
            Create(realtimeThrows: true);
        store.UnreadForAll = 2;

        Assert.Equal(2, await handler.MarkAllAsync(new MarkAllConversationsReadCommand(Workspace)));
        Assert.Single(realtime.Announced);
    }

    [Fact]
    public async Task A_member_without_conversations_view_is_refused_and_nothing_is_touched()
    {
        (FakeConversationInboxStore store, RecordingReadRealtime realtime, MarkAllConversationsReadHandler handler) =
            Create(TestAuthority.With(Caller, Workspace, PlatformPermissions.CustomersView));
        store.UnreadForAll = 4;

        await Assert.ThrowsAsync<PermissionDeniedException>(() =>
            handler.MarkAllAsync(new MarkAllConversationsReadCommand(Workspace)));
        await Assert.ThrowsAsync<PermissionDeniedException>(() =>
            handler.CountUnreadAsync(new CountUnreadConversationsQuery(Workspace)));

        Assert.Empty(store.MarkedAll);
        Assert.Empty(realtime.Announced);
    }

    [Fact]
    public async Task An_outsider_is_told_nothing_exists_and_nothing_is_touched()
    {
        (FakeConversationInboxStore store, RecordingReadRealtime realtime, MarkAllConversationsReadHandler handler) =
            Create(TestAuthority.Outsider(Caller));
        store.UnreadForAll = 4;

        Assert.Null(await handler.MarkAllAsync(new MarkAllConversationsReadCommand(Workspace)));
        Assert.Null(await handler.CountUnreadAsync(new CountUnreadConversationsQuery(Workspace)));

        Assert.Empty(store.MarkedAll);
        Assert.Empty(realtime.Announced);
    }

    private sealed class RecordingReadRealtime : IConversationReadRealtime
    {
        public bool Throws { get; init; }

        public List<(TenantId TenantId, UserId Member)> Announced { get; } = [];

        public Task ConversationsReadAsync(TenantId tenantId, UserId member, CancellationToken cancellationToken = default)
        {
            Announced.Add((tenantId, member));

            return Throws
                ? throw new InvalidOperationException("The backplane is unreachable.")
                : Task.CompletedTask;
        }
    }
}
