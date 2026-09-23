using OmniAI.Application.Authorization;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Conversations;
using OmniAI.Application.Conversations.Persistence;
using OmniAI.Application.Tests.Security;
using OmniAI.Application.Tests.WebChat;
using OmniAI.Domain.Customers;
using OmniAI.Domain;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;
using OmniAI.Testing;

namespace OmniAI.Application.Tests.Conversations;

/// <summary>
/// Taking a conversation over, routing it, and replying to it.
/// </summary>
public sealed class ConversationInboxTests
{
    private static readonly Guid Workspace = Guid.CreateVersion7();
    private static readonly UserId Caller = UserId.New();

    private static readonly DateTimeOffset Now =
        new(2026, 9, 14, 9, 0, 0, TimeSpan.Zero);

    private sealed record World(
        FakeConversationInboxStore Store,
        RecordingRealtimeNotifier Announcer,
        SerialisingConversationLock Lock,
        Conversation Conversation,
        ConversationHandoffHandler Handoff,
        ConversationAssignmentHandler Assignment,
        ReplyToConversationHandler Reply);

    private static World Build(TenantAuthorizer? authorizer = null, UserId? caller = null)
    {
        UserId user = caller ?? Caller;

        TenantAuthorizer authority = authorizer
            ?? TestAuthority.Owner(user, Workspace);

        Conversation conversation = Conversation.Start(
            TenantId.From(Workspace),
            CustomerId.New(),
            ChannelKind.WebChat,
            Now);

        FakeConversationInboxStore store = new FakeConversationInboxStore().Add(conversation);
        RecordingRealtimeNotifier announcer = new();
        SerialisingConversationLock gate = new();
        FixedTimeProvider clock = new(Now);

        return new World(
            store,
            announcer,
            gate,
            conversation,
            new ConversationHandoffHandler(authority, store, gate, announcer, clock),
            new ConversationAssignmentHandler(authority, store, announcer, clock),
            new ReplyToConversationHandler(
                authority,
                TestAccess.ForTenants(user, Workspace),
                store,
                gate,
                announcer,
                // These conversations are on website chat, where a stored reply
                // is already in front of the visitor. The dispatcher exists for
                // channels that need a delivery, and has nothing to do here.
                new NoOutboundDelivery(),
                clock));
    }

    private static SetConversationHandlingCommand Handing(
        World world,
        bool toHuman,
        ConcurrencyToken? version = null) =>
        new(
            Workspace,
            world.Conversation.Id.Value,
            toHuman,
            version ?? world.Store.VersionOf(world.Conversation.Id));

    // ---- Handoff ---------------------------------------------------------

    [Fact]
    public async Task Taking_over_stops_the_ai_and_announces_it()
    {
        World world = Build();

        Versioned<Conversation>? result =
            await world.Handoff.HandleAsync(Handing(world, toHuman: true));

        Assert.NotNull(result);
        Assert.Equal(ConversationHandling.Human, result.Value.Handling);
        Assert.Single(world.Announcer.Announced);
    }

    [Fact]
    public async Task Returning_hands_it_back()
    {
        World world = Build();

        await world.Handoff.HandleAsync(Handing(world, toHuman: true));

        Versioned<Conversation>? result =
            await world.Handoff.HandleAsync(Handing(world, toHuman: false));

        Assert.Equal(ConversationHandling.Ai, result!.Value.Handling);
    }

    [Fact]
    public async Task Repeating_an_action_succeeds_and_writes_nothing()
    {
        // Pressing the button the conversation is already on satisfies the
        // caller's intent. Saving anyway would burn a version and invalidate
        // every other open screen for no change at all.
        World world = Build();

        await world.Handoff.HandleAsync(Handing(world, toHuman: true));

        ConcurrencyToken before = world.Store.VersionOf(world.Conversation.Id);

        Versioned<Conversation>? again =
            await world.Handoff.HandleAsync(Handing(world, toHuman: true));

        Assert.NotNull(again);
        Assert.Equal(before, world.Store.VersionOf(world.Conversation.Id));
        Assert.Single(world.Announcer.Announced);
    }

    [Fact]
    public async Task A_stale_version_is_refused()
    {
        World world = Build();

        ConcurrencyToken stale = world.Store.VersionOf(world.Conversation.Id);

        // Somebody else wrote in between.
        world.Store.BumpVersion(world.Conversation.Id);

        await Assert.ThrowsAsync<ConversationVersionMismatchException>(
            () => world.Handoff.HandleAsync(Handing(world, toHuman: true, stale)));

        // And nothing happened: a refused precondition changes nothing.
        Assert.Equal(ConversationHandling.Ai, world.Conversation.Handling);
        Assert.Empty(world.Announcer.Announced);
    }

    [Fact]
    public async Task Handing_over_takes_the_conversation_lock()
    {
        // The same lock generation holds. Without it a takeover can land in the
        // middle of a paid-for provider call and contradict the reply it is
        // about to append.
        World world = Build();

        await world.Handoff.HandleAsync(Handing(world, toHuman: true));

        Assert.Contains(world.Conversation.Id.Value, world.Lock.Acquired);
    }

    [Fact]
    public async Task A_conversation_of_another_workspace_is_not_found()
    {
        World world = Build();

        Versioned<Conversation>? result = await world.Handoff.HandleAsync(
            new SetConversationHandlingCommand(
                Guid.CreateVersion7(),
                world.Conversation.Id.Value,
                ToHuman: true,
                world.Store.VersionOf(world.Conversation.Id)));

        Assert.Null(result);
    }

    [Fact]
    public async Task A_member_without_the_handoff_permission_is_refused()
    {
        World world = Build(
            TestAuthority.With(Caller, Workspace, PlatformPermissions.ConversationsView));

        await Assert.ThrowsAsync<PermissionDeniedException>(
            () => world.Handoff.HandleAsync(Handing(world, toHuman: true)));
    }

    [Fact]
    public async Task A_member_who_cannot_read_conversations_is_refused()
    {
        // A member of the workspace is told they lack the permission; that is
        // the platform's three-outcome rule, and it is deliberately different
        // from what an outsider is told.
        World world = Build(TestAuthority.With(Caller, Workspace));

        await Assert.ThrowsAsync<PermissionDeniedException>(
            () => world.Handoff.HandleAsync(Handing(world, toHuman: true)));
    }

    [Fact]
    public async Task Somebody_who_belongs_to_nothing_is_not_found()
    {
        // 404, not 403. Whether a workspace exists is not something an outsider
        // gets to learn by being refused differently.
        World world = Build(TestAuthority.Outsider(Caller));

        Assert.Null(await world.Handoff.HandleAsync(Handing(world, toHuman: true)));
    }

    // ---- Human reply -----------------------------------------------------

    private static ReplyToConversationCommand Replying(World world, string key, string text) =>
        new(Workspace, world.Conversation.Id.Value, key, text);

    [Fact]
    public async Task A_reply_is_stored_as_a_human_agent_message()
    {
        World world = Build();

        await world.Handoff.HandleAsync(Handing(world, toHuman: true));

        ConversationMessageRecord? reply = await world.Reply.HandleAsync(
            Replying(world, "r1", "We refunded it this morning."));

        Assert.NotNull(reply);
        Assert.Equal(MessageSender.HumanAgent, reply.Sender);
        Assert.Equal("We refunded it this morning.", reply.Text);
    }

    [Fact]
    public async Task The_sender_is_the_authenticated_user_not_anything_sent()
    {
        // The command has no field for a sender, which is the point: there is
        // nothing a browser could put one in.
        World world = Build();

        await world.Handoff.HandleAsync(Handing(world, toHuman: true));

        ConversationMessageRecord? reply = await world.Reply.HandleAsync(
            Replying(world, "r1", "Hello"));

        Assert.NotNull(reply!.SentBy);
        Assert.Equal(Caller.Value, reply.SentBy.UserId);
    }

    [Fact]
    public async Task Replying_to_an_ai_conversation_is_refused_rather_than_taking_it_over()
    {
        // Inferring a takeover from a keystroke would mean somebody who typed in
        // the wrong tab silently stopped an automation for a customer nobody is
        // watching.
        World world = Build();

        await Assert.ThrowsAsync<ConversationNotHandledByHumanException>(
            () => world.Reply.HandleAsync(Replying(world, "r1", "Hello")));

        Assert.Equal(ConversationHandling.Ai, world.Conversation.Handling);
        Assert.Equal(0, world.Store.Written);
    }

    [Fact]
    public async Task A_member_without_the_reply_permission_is_refused()
    {
        World world = Build(TestAuthority.With(
            Caller,
            Workspace,
            PlatformPermissions.ConversationsView,
            PlatformPermissions.ConversationsHandoff));

        await world.Handoff.HandleAsync(Handing(world, toHuman: true));

        await Assert.ThrowsAsync<PermissionDeniedException>(
            () => world.Reply.HandleAsync(Replying(world, "r1", "Hello")));
    }

    [Fact]
    public async Task The_same_key_twice_stores_one_message()
    {
        World world = Build();

        await world.Handoff.HandleAsync(Handing(world, toHuman: true));

        ConversationMessageRecord? first =
            await world.Reply.HandleAsync(Replying(world, "r1", "Hello"));

        ConversationMessageRecord? second =
            await world.Reply.HandleAsync(Replying(world, "r1", "Hello"));

        Assert.Equal(first!.Id, second!.Id);
        Assert.Equal(1, world.Store.Written);

        // The replay announces nothing: the message it names was announced when
        // it was first written, and every listener has already re-read it.
        Assert.Equal(2, world.Announcer.Announced.Count);
    }

    [Fact]
    public async Task Two_concurrent_duplicates_store_one_message()
    {
        // The real race. Both callers are held inside the store, released
        // together, and exactly one of them writes.
        World world = Build();

        await world.Handoff.HandleAsync(Handing(world, toHuman: true));

        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        world.Store.PauseBeforeWrite = gate;

        Task<ConversationMessageRecord?> first =
            world.Reply.HandleAsync(Replying(world, "same", "Hello"));

        Task<ConversationMessageRecord?> second =
            world.Reply.HandleAsync(Replying(world, "same", "Hello"));

        gate.SetResult();

        ConversationMessageRecord?[] results = await Task.WhenAll(first, second);

        Assert.Equal(1, world.Store.Written);
        Assert.Equal(results[0]!.Id, results[1]!.Id);
    }

    [Fact]
    public async Task A_reply_takes_the_conversation_lock()
    {
        World world = Build();

        await world.Handoff.HandleAsync(Handing(world, toHuman: true));
        await world.Reply.HandleAsync(Replying(world, "r1", "Hello"));

        // Once for the handoff and once for the reply.
        Assert.Equal(2, world.Lock.Acquired.Count);
    }

    [Fact]
    public async Task A_reply_carries_an_ingestion_sequence()
    {
        World world = Build();

        await world.Handoff.HandleAsync(Handing(world, toHuman: true));

        ConversationMessageRecord? one = await world.Reply.HandleAsync(
            Replying(world, "r1", "First"));

        ConversationMessageRecord? two = await world.Reply.HandleAsync(
            Replying(world, "r2", "Second"));

        Assert.True(two!.IngestionSequence > one!.IngestionSequence);
    }

    [Fact]
    public async Task A_reply_moves_the_activity_mark()
    {
        World world = Build();

        await world.Handoff.HandleAsync(Handing(world, toHuman: true));

        DateTimeOffset before = world.Conversation.LastActivityAt;

        await world.Reply.HandleAsync(Replying(world, "r1", "Hello"));

        Assert.True(world.Conversation.LastActivityAt >= before);
    }

    [Fact]
    public async Task A_malformed_key_is_refused_before_anything_is_locked()
    {
        World world = Build();

        await world.Handoff.HandleAsync(Handing(world, toHuman: true));

        int locksBefore = world.Lock.Acquired.Count;

        await Assert.ThrowsAsync<DomainValidationException>(
            () => world.Reply.HandleAsync(Replying(world, "not a valid key!", "Hello")));

        Assert.Equal(locksBefore, world.Lock.Acquired.Count);
    }

    // ---- Assignment ------------------------------------------------------

    private static AssignConversationCommand Assigning(World world, Guid? member) =>
        new(Workspace, world.Conversation.Id.Value, member, world.Store.VersionOf(world.Conversation.Id));

    [Fact]
    public async Task An_eligible_member_can_be_assigned()
    {
        World world = Build();
        UserId member = UserId.New();

        world.Store.AddMember(TenantId.From(Workspace), member, "sam@acme.test", mayReply: true);

        Versioned<Conversation>? result =
            await world.Assignment.AssignAsync(Assigning(world, member.Value));

        Assert.Equal(member, result!.Value.AssignedTo);
    }

    [Fact]
    public async Task Assignment_does_not_change_who_is_answering()
    {
        World world = Build();
        UserId member = UserId.New();

        world.Store.AddMember(TenantId.From(Workspace), member, "sam@acme.test", mayReply: true);

        Versioned<Conversation>? result =
            await world.Assignment.AssignAsync(Assigning(world, member.Value));

        Assert.Equal(ConversationHandling.Ai, result!.Value.Handling);
    }

    [Fact]
    public async Task A_member_who_cannot_reply_is_refused()
    {
        // Routing a customer to somebody who cannot answer them is a silent way
        // to make that customer wait for ever.
        World world = Build();
        UserId member = UserId.New();

        world.Store.AddMember(TenantId.From(Workspace), member, "reader@acme.test", mayReply: false);

        await Assert.ThrowsAsync<ConversationAssigneeIneligibleException>(
            () => world.Assignment.AssignAsync(Assigning(world, member.Value)));
    }

    [Fact]
    public async Task Somebody_from_another_workspace_is_refused()
    {
        World world = Build();

        // Never added as a member here. The store answers no, and the database
        // would refuse the row even if it did not.
        await Assert.ThrowsAsync<ConversationAssigneeIneligibleException>(
            () => world.Assignment.AssignAsync(Assigning(world, Guid.CreateVersion7())));
    }

    [Fact]
    public async Task An_assignment_can_be_removed()
    {
        World world = Build();
        UserId member = UserId.New();

        world.Store.AddMember(TenantId.From(Workspace), member, "sam@acme.test", mayReply: true);

        await world.Assignment.AssignAsync(Assigning(world, member.Value));

        Versioned<Conversation>? cleared =
            await world.Assignment.AssignAsync(Assigning(world, null));

        Assert.Null(cleared!.Value.AssignedTo);
    }

    [Fact]
    public async Task Assigning_needs_its_own_permission()
    {
        World world = Build(TestAuthority.With(
            Caller,
            Workspace,
            PlatformPermissions.ConversationsView,
            PlatformPermissions.ConversationsReply));

        await Assert.ThrowsAsync<PermissionDeniedException>(
            () => world.Assignment.AssignAsync(Assigning(world, UserId.New().Value)));
    }

    [Fact]
    public async Task A_stale_assignment_is_refused()
    {
        World world = Build();
        UserId member = UserId.New();

        world.Store.AddMember(TenantId.From(Workspace), member, "sam@acme.test", mayReply: true);

        AssignConversationCommand stale = Assigning(world, member.Value);

        world.Store.BumpVersion(world.Conversation.Id);

        await Assert.ThrowsAsync<ConversationVersionMismatchException>(
            () => world.Assignment.AssignAsync(stale));
    }

    [Fact]
    public async Task The_eligible_list_is_only_people_who_can_reply()
    {
        World world = Build();

        world.Store.AddMember(TenantId.From(Workspace), UserId.New(), "can@acme.test", mayReply: true);
        world.Store.AddMember(TenantId.From(Workspace), UserId.New(), "cannot@acme.test", mayReply: false);

        IReadOnlyList<ConversationMessageAuthor>? offered =
            await world.Assignment.ListAssigneesAsync(new ListAssigneesQuery(Workspace));

        Assert.NotNull(offered);
        Assert.Single(offered);
        Assert.Equal("can@acme.test", offered[0].Email);
    }
}
