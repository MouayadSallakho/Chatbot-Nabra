using OmniAI.Application.Authorization;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Conversations;
using OmniAI.Application.Conversations.Persistence;
using OmniAI.Application.Tests.Security;
using OmniAI.Application.Tests.WebChat;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Customers;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;
using OmniAI.Testing;

namespace OmniAI.Application.Tests.Conversations;

/// <summary>
/// Phase 37.4 inbox state: handoff history, read markers, and the refusals of
/// the new read parameters.
/// </summary>
public sealed class InboxStateTests
{
    private static readonly Guid Workspace = Guid.CreateVersion7();
    private static readonly UserId Caller = UserId.New();

    private static readonly ConversationHandlingEventKind[] TakenOverThenReturned =
        [ConversationHandlingEventKind.TakenOver, ConversationHandlingEventKind.ReturnedToAi];

    private static readonly DateTimeOffset Now =
        new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

    private static (FakeConversationInboxStore Store, Conversation Conversation, ConversationHandoffHandler Handoff)
        Handoff(TenantAuthorizer? authorizer = null)
    {
        Conversation conversation = Conversation.Start(
            TenantId.From(Workspace),
            CustomerId.New(),
            ChannelKind.WebChat,
            Now);

        FakeConversationInboxStore store = new FakeConversationInboxStore().Add(conversation);

        return (
            store,
            conversation,
            new ConversationHandoffHandler(
                authorizer ?? TestAuthority.Owner(Caller, Workspace),
                store,
                new SerialisingConversationLock(),
                new RecordingRealtimeNotifier(),
                new FixedTimeProvider(Now)));
    }

    // ---- Handoff history -------------------------------------------------

    [Fact]
    public async Task Taking_over_stores_one_event_naming_the_caller_after_the_newest_message()
    {
        (FakeConversationInboxStore store, Conversation conversation, ConversationHandoffHandler handoff) = Handoff();
        store.NewestSequence = 57;

        await handoff.HandleAsync(new SetConversationHandlingCommand(
            Workspace, conversation.Id.Value, ToHuman: true, store.VersionOf(conversation.Id)));

        ConversationHandlingEvent change = Assert.Single(store.HandlingEvents);

        Assert.Equal(ConversationHandlingEventKind.TakenOver, change.Kind);
        Assert.Equal(Caller, change.Actor);
        Assert.Equal(57, change.AfterSequence);
        Assert.Equal(Now, change.OccurredAt);
    }

    [Fact]
    public async Task Taking_over_and_returning_store_two_events_in_order()
    {
        (FakeConversationInboxStore store, Conversation conversation, ConversationHandoffHandler handoff) = Handoff();

        await handoff.HandleAsync(new SetConversationHandlingCommand(
            Workspace, conversation.Id.Value, ToHuman: true, store.VersionOf(conversation.Id)));
        await handoff.HandleAsync(new SetConversationHandlingCommand(
            Workspace, conversation.Id.Value, ToHuman: false, store.VersionOf(conversation.Id)));

        Assert.Equal(TakenOverThenReturned, store.HandlingEvents.Select(change => change.Kind));
    }

    [Fact]
    public async Task An_idempotent_press_stores_no_event()
    {
        (FakeConversationInboxStore store, Conversation conversation, ConversationHandoffHandler handoff) = Handoff();

        await handoff.HandleAsync(new SetConversationHandlingCommand(
            Workspace, conversation.Id.Value, ToHuman: true, store.VersionOf(conversation.Id)));
        await handoff.HandleAsync(new SetConversationHandlingCommand(
            Workspace, conversation.Id.Value, ToHuman: true, store.VersionOf(conversation.Id)));

        Assert.Single(store.HandlingEvents);
    }

    [Fact]
    public async Task A_stale_precondition_stores_no_event()
    {
        (FakeConversationInboxStore store, Conversation conversation, ConversationHandoffHandler handoff) = Handoff();
        ConcurrencyToken held = store.VersionOf(conversation.Id);
        store.BumpVersion(conversation.Id);

        await Assert.ThrowsAsync<ConversationVersionMismatchException>(() =>
            handoff.HandleAsync(new SetConversationHandlingCommand(
                Workspace, conversation.Id.Value, ToHuman: true, held)));

        Assert.Empty(store.HandlingEvents);
    }

    [Fact]
    public async Task A_member_without_handoff_permission_stores_no_event()
    {
        (FakeConversationInboxStore store, Conversation conversation, ConversationHandoffHandler handoff) =
            Handoff(TestAuthority.With(Caller, Workspace, PlatformPermissions.ConversationsView));

        await Assert.ThrowsAsync<PermissionDeniedException>(() =>
            handoff.HandleAsync(new SetConversationHandlingCommand(
                Workspace, conversation.Id.Value, ToHuman: true, store.VersionOf(conversation.Id))));

        Assert.Empty(store.HandlingEvents);
    }

    // ---- Read markers ----------------------------------------------------

    private static (FakeConversationInboxStore Store, Conversation Conversation, MarkConversationReadHandler Handler)
        Reader(TenantAuthorizer? authorizer = null)
    {
        Conversation conversation = Conversation.Start(
            TenantId.From(Workspace),
            CustomerId.New(),
            ChannelKind.WebChat,
            Now);

        FakeConversationInboxStore store = new FakeConversationInboxStore().Add(conversation);
        store.NewestSequence = 120;

        return (
            store,
            conversation,
            new MarkConversationReadHandler(
                authorizer ?? TestAuthority.Owner(Caller, Workspace),
                store,
                new FixedTimeProvider(Now)));
    }

    [Fact]
    public async Task Marking_read_moves_only_forward_and_never_past_the_newest_message()
    {
        (FakeConversationInboxStore store, Conversation conversation, MarkConversationReadHandler handler) = Reader();

        Assert.True(await handler.HandleAsync(new MarkConversationReadCommand(Workspace, conversation.Id.Value, 100)));
        Assert.True(await handler.HandleAsync(new MarkConversationReadCommand(Workspace, conversation.Id.Value, 90)));

        Assert.Equal(100, store.ReadMarkers[(conversation.Id, Caller)]);

        Assert.True(await handler.HandleAsync(new MarkConversationReadCommand(Workspace, conversation.Id.Value, 9_999)));

        Assert.Equal(120, store.ReadMarkers[(conversation.Id, Caller)]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1L)]
    public async Task A_missing_or_negative_sequence_is_refused_with_a_code(long? sequence)
    {
        (_, Conversation conversation, MarkConversationReadHandler handler) = Reader();

        ConversationQueryInvalidException refused = await Assert.ThrowsAsync<ConversationQueryInvalidException>(() =>
            handler.HandleAsync(new MarkConversationReadCommand(Workspace, conversation.Id.Value, sequence)));

        Assert.Equal("conversations.read.sequence.invalid", refused.Code);
    }

    [Fact]
    public async Task A_member_without_view_permission_cannot_mark_read()
    {
        (FakeConversationInboxStore store, Conversation conversation, MarkConversationReadHandler handler) =
            Reader(TestAuthority.With(Caller, Workspace, PlatformPermissions.ConversationsReply));

        await Assert.ThrowsAsync<PermissionDeniedException>(() =>
            handler.HandleAsync(new MarkConversationReadCommand(Workspace, conversation.Id.Value, 1)));

        Assert.Empty(store.ReadMarkers);
    }

    [Fact]
    public async Task A_stranger_is_told_nothing_exists()
    {
        (FakeConversationInboxStore store, Conversation conversation, MarkConversationReadHandler handler) =
            Reader(TestAuthority.Outsider(Caller));

        Assert.False(await handler.HandleAsync(new MarkConversationReadCommand(Workspace, conversation.Id.Value, 1)));
        Assert.Empty(store.ReadMarkers);
    }

    [Fact]
    public async Task A_reply_marks_the_conversation_read_for_its_sender_only()
    {
        (FakeConversationInboxStore store, Conversation conversation, ConversationHandoffHandler handoff) = Handoff();

        await handoff.HandleAsync(new SetConversationHandlingCommand(
            Workspace, conversation.Id.Value, ToHuman: true, store.VersionOf(conversation.Id)));

        ReplyToConversationHandler reply = new(
            TestAuthority.Owner(Caller, Workspace),
            TestAccess.ForTenants(Caller, Workspace),
            store,
            new SerialisingConversationLock(),
            new RecordingRealtimeNotifier(),
            new NoOutboundDelivery(),
            new FixedTimeProvider(Now));

        ConversationMessageRecord? stored = await reply.HandleAsync(
            new ReplyToConversationCommand(Workspace, conversation.Id.Value, "r1", "We refunded it."));

        Assert.Equal(stored!.IngestionSequence, store.ReadMarkers[(conversation.Id, Caller)]);
        Assert.Single(store.ReadMarkers);
    }

    // ---- Refusals of the new read parameters -----------------------------

    private static ReadConversationsHandler ReadHandler() =>
        new(new FakeWebChatWorld(), TestAuthority.Owner(Caller, Workspace), new ConversationReadPolicy());

    [Fact]
    public async Task An_unknown_filter_is_refused_with_its_code()
    {
        ConversationQueryInvalidException refused = await Assert.ThrowsAsync<ConversationQueryInvalidException>(() =>
            ReadHandler().ListAsync(new ListConversationsQuery(Workspace, 1, 20, "unassigned")));

        Assert.Equal("conversations.filter.invalid", refused.Code);
    }

    [Theory]
    [InlineData(true, null)]
    [InlineData(false, 10L)]
    [InlineData(true, 10L)]
    public async Task Reading_forwards_and_backwards_at_once_is_refused(bool tail, long? before)
    {
        ConversationQueryInvalidException refused = await Assert.ThrowsAsync<ConversationQueryInvalidException>(() =>
            ReadHandler().ListMessagesAsync(new ListConversationMessagesQuery(
                Workspace, Guid.CreateVersion7(), AfterSequence: 5, Limit: 10, before, tail)));

        Assert.Equal("transcript.cursor.conflict", refused.Code);
    }

    [Theory]
    [InlineData("event")]
    [InlineData("events,notes")]
    public async Task An_unknown_include_is_refused_with_its_code(string include)
    {
        ConversationQueryInvalidException refused = await Assert.ThrowsAsync<ConversationQueryInvalidException>(() =>
            ReadHandler().ListMessagesAsync(new ListConversationMessagesQuery(
                Workspace, Guid.CreateVersion7(), null, 10, Include: include)));

        Assert.Equal("transcript.include.invalid", refused.Code);
    }
}
