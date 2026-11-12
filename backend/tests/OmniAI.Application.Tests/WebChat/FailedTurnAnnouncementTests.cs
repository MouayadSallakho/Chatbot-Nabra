using OmniAI.Application.Ai;
using OmniAI.Application.WebChat;
using OmniAI.Application.WebChat.Hosted;
using OmniAI.Domain.Conversations;

namespace OmniAI.Application.Tests.WebChat;

/// <summary>
/// An AI turn tells the inbox twice: when the customer's message is stored, and
/// when the turn settles, whether with a reply or a failure.
/// </summary>
/// <remarks>
/// <strong>The customer's message is a fact before the provider is called.</strong>
/// It is committed first and survives a failed reply, so an open inbox has
/// something new to show from that moment (Phase 38.7a, BA-13), and something
/// else to show once the turn is settled. Without the first announcement a
/// colleague saw nothing until the provider answered or gave up.
/// <para>
/// Each test counts announcements for one conversation only, because starting a
/// conversation is announced too and is not what these tests are about.
/// </para>
/// </remarks>
public sealed class FailedTurnAnnouncementTests
{
    [Fact]
    public async Task A_failed_reply_is_announced_when_stored_and_again_when_failed()
    {
        Harness harness = Harness.Build(
            RecordingGenerator.Failing(() => new AiRuntimeUnavailableException()));
        StartedWebChatSession session = await harness.StartAsync();
        ConversationId conversation = ConversationId.From(session.Conversation.ConversationId);
        int before = AnnouncementsFor(harness, conversation);

        // What had been committed at the moment of each announcement.
        List<(ConversationTurnStatus Turn, int Messages, int ProviderCalls)> seen = [];
        harness.Announcer.Observe = _ => seen.Add((
            harness.World.Turns.Single().Status,
            harness.World.Transcript(conversation).Count,
            harness.Generator.Calls));

        await Assert.ThrowsAsync<AiRuntimeUnavailableException>(() => harness.Send.HandleAsync(
            Harness.Sending(conversation.Value, session.AccessToken, "m1", "Anyone there?")));

        Assert.Equal(before + 2, AnnouncementsFor(harness, conversation));

        // First: the message is stored, the turn is open, the provider not yet
        // asked. Then: the turn is failed, and still only the customer's message.
        Assert.Equal(
            [(ConversationTurnStatus.Generating, 1, 0), (ConversationTurnStatus.Failed, 1, 1)],
            seen);
    }

    [Fact]
    public async Task A_reply_is_announced_when_stored_and_again_when_answered()
    {
        Harness harness = Harness.Build();
        StartedWebChatSession session = await harness.StartAsync();
        ConversationId conversation = ConversationId.From(session.Conversation.ConversationId);
        int before = AnnouncementsFor(harness, conversation);

        List<(ConversationTurnStatus Turn, int Messages, int ProviderCalls)> seen = [];
        harness.Announcer.Observe = _ => seen.Add((
            harness.World.Turns.Single().Status,
            harness.World.Transcript(conversation).Count,
            harness.Generator.Calls));

        await harness.Send.HandleAsync(
            Harness.Sending(conversation.Value, session.AccessToken, "m1", "Hello?"));

        Assert.Equal(before + 2, AnnouncementsFor(harness, conversation));
        Assert.Equal(
            [(ConversationTurnStatus.Generating, 1, 0), (ConversationTurnStatus.Completed, 2, 1)],
            seen);
    }

    [Fact]
    public async Task A_message_to_a_person_is_announced_once_after_it_settles()
    {
        // The human path has nothing to wait for, so it keeps its one
        // announcement, made after the turn is settled without a reply.
        Harness harness = Harness.Build();
        StartedWebChatSession session = await harness.StartAsync();
        ConversationId conversation = ConversationId.From(session.Conversation.ConversationId);
        harness.World.Handling = ConversationHandling.Human;
        int before = AnnouncementsFor(harness, conversation);

        List<ConversationTurnStatus> seen = [];
        harness.Announcer.Observe = _ => seen.Add(harness.World.Turns.Single().Status);

        await harness.Send.HandleAsync(
            Harness.Sending(conversation.Value, session.AccessToken, "m1", "Hello?"));

        Assert.Equal(before + 1, AnnouncementsFor(harness, conversation));
        Assert.Equal([ConversationTurnStatus.Completed], seen);
        Assert.Equal(0, harness.Generator.Calls);
    }

    [Fact]
    public async Task A_replay_of_the_failed_attempt_is_not_announced_again()
    {
        Harness harness = Harness.Build(
            RecordingGenerator.Failing(() => new AiRuntimeUnavailableException()));
        StartedWebChatSession session = await harness.StartAsync();
        ConversationId conversation = ConversationId.From(session.Conversation.ConversationId);

        SendWebChatMessageCommand command =
            Harness.Sending(conversation.Value, session.AccessToken, "m1", "Anyone there?");

        await Assert.ThrowsAsync<AiRuntimeUnavailableException>(
            () => harness.Send.HandleAsync(command));

        int afterFirst = AnnouncementsFor(harness, conversation);

        // The same key again: refused as already failed, before anything is
        // written, spent or announced.
        await Assert.ThrowsAsync<WebChatTurnAlreadyFailedException>(
            () => harness.Send.HandleAsync(command));

        Assert.Equal(afterFirst, AnnouncementsFor(harness, conversation));
        Assert.Equal(1, harness.Generator.Calls);
    }

    [Fact]
    public async Task A_replay_of_an_answered_message_is_not_announced_again()
    {
        Harness harness = Harness.Build();
        StartedWebChatSession session = await harness.StartAsync();
        ConversationId conversation = ConversationId.From(session.Conversation.ConversationId);

        SendWebChatMessageCommand command =
            Harness.Sending(conversation.Value, session.AccessToken, "m1", "Hello?");

        await harness.Send.HandleAsync(command);

        int afterFirst = AnnouncementsFor(harness, conversation);

        // A redelivery of something already answered replays the stored answer.
        WebChatTurnResult? replayed = await harness.Send.HandleAsync(command);

        Assert.NotNull(replayed?.Reply);
        Assert.Equal(afterFirst, AnnouncementsFor(harness, conversation));
        Assert.Equal(1, harness.Generator.Calls);
    }

    [Fact]
    public async Task A_duplicate_refused_while_the_turn_runs_is_not_announced()
    {
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Harness harness = Harness.Build(RecordingGenerator.Gated(gate));
        StartedWebChatSession session = await harness.StartAsync();
        ConversationId conversation = ConversationId.From(session.Conversation.ConversationId);

        SendWebChatMessageCommand command =
            Harness.Sending(conversation.Value, session.AccessToken, "m1", "Hello?");

        TaskCompletionSource stored = new(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Announcer.Observe = _ => stored.TrySetResult();

        int before = AnnouncementsFor(harness, conversation);
        Task<WebChatTurnResult?> first = harness.Send.HandleAsync(command);

        // The first turn has stored its message and is waiting on the provider.
        await stored.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(before + 1, AnnouncementsFor(harness, conversation));

        // The same key meanwhile is refused as in progress, and announces nothing.
        await Assert.ThrowsAsync<WebChatTurnInProgressException>(
            () => harness.Send.HandleAsync(command));

        Assert.Equal(before + 1, AnnouncementsFor(harness, conversation));

        gate.SetResult();
        await first;

        Assert.Equal(before + 2, AnnouncementsFor(harness, conversation));
        Assert.Equal(1, harness.Generator.Calls);
    }

    [Fact]
    public async Task A_failing_announcer_never_replaces_the_original_failure()
    {
        Harness harness = Harness.Build(
            RecordingGenerator.Failing(() => new AiRuntimeTimeoutException()));
        StartedWebChatSession session = await harness.StartAsync();
        ConversationId conversation = ConversationId.From(session.Conversation.ConversationId);

        harness.Announcer.Failure = new InvalidOperationException("backplane down");

        // The caller sees the provider's failure, not the announcer's.
        await Assert.ThrowsAsync<AiRuntimeTimeoutException>(() => harness.Send.HandleAsync(
            Harness.Sending(conversation.Value, session.AccessToken, "m1", "Hello?")));

        // And the outcome is the one a working announcer would have left: the
        // message kept, the turn failed, nothing invented.
        Assert.Equal(
            [(MessageSender.Customer, "Hello?")],
            harness.World.Transcript(conversation));
        Assert.Equal(ConversationTurnStatus.Failed, Assert.Single(harness.World.Turns).Status);

        // A replay is still refused as already failed, not retried.
        harness.Announcer.Failure = null;

        await Assert.ThrowsAsync<WebChatTurnAlreadyFailedException>(() => harness.Send.HandleAsync(
            Harness.Sending(conversation.Value, session.AccessToken, "m1", "Hello?")));

        Assert.Equal(1, harness.Generator.Calls);
    }

    [Fact]
    public async Task A_failing_announcer_changes_nothing_about_a_reply()
    {
        // Both announcements of a successful turn throw; the visitor still gets
        // the reply, and the stored outcome is the same.
        Harness harness = Harness.Build(RecordingGenerator.Replying("The answer."));
        StartedWebChatSession session = await harness.StartAsync();
        ConversationId conversation = ConversationId.From(session.Conversation.ConversationId);

        harness.Announcer.Failure = new InvalidOperationException("backplane down");

        WebChatTurnResult? result = await harness.Send.HandleAsync(
            Harness.Sending(conversation.Value, session.AccessToken, "m1", "Hello?"));

        Assert.Equal("The answer.", result?.Reply?.Text);
        Assert.Equal(
            [(MessageSender.Customer, "Hello?"), (MessageSender.AiAgent, "The answer.")],
            harness.World.Transcript(conversation));
        Assert.Equal(ConversationTurnStatus.Completed, Assert.Single(harness.World.Turns).Status);
        Assert.Equal(1, harness.Generator.Calls);
    }

    private static int AnnouncementsFor(Harness harness, ConversationId conversation) =>
        harness.Announcer.Announced.Count(announced => announced.Conversation == conversation);
}
