using OmniAI.Application.Ai;
using OmniAI.Application.WebChat;
using OmniAI.Application.WebChat.Hosted;
using OmniAI.Domain.Conversations;

namespace OmniAI.Application.Tests.WebChat;

/// <summary>
/// A turn whose reply failed still tells the inbox that something changed.
/// </summary>
/// <remarks>
/// <strong>The customer's message is a fact before the provider is called.</strong>
/// It is committed first and survives a failed reply, so an open inbox has
/// something new to show either way. Without the announcement a colleague saw
/// the message only after reloading, which is exactly when a failed reply most
/// needs a person.
/// <para>
/// Each test counts announcements for one conversation only, because starting a
/// conversation is announced too and is not what these tests are about.
/// </para>
/// </remarks>
public sealed class FailedTurnAnnouncementTests
{
    [Fact]
    public async Task A_failed_reply_is_announced_once_after_the_turn_is_failed()
    {
        Harness harness = Harness.Build(
            RecordingGenerator.Failing(() => new AiRuntimeUnavailableException()));
        StartedWebChatSession session = await harness.StartAsync();
        ConversationId conversation = ConversationId.From(session.Conversation.ConversationId);
        int before = AnnouncementsFor(harness, conversation);

        // What had been committed at the moment the inbox was told.
        ConversationTurnStatus? statusWhenAnnounced = null;
        harness.Announcer.Observe = _ =>
            statusWhenAnnounced = harness.World.Turns.Single().Status;

        await Assert.ThrowsAsync<AiRuntimeUnavailableException>(() => harness.Send.HandleAsync(
            Harness.Sending(conversation.Value, session.AccessToken, "m1", "Anyone there?")));

        Assert.Equal(before + 1, AnnouncementsFor(harness, conversation));
        Assert.Equal(ConversationTurnStatus.Failed, statusWhenAnnounced);
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
    public async Task A_successful_reply_is_still_announced_exactly_once()
    {
        // The failure path must not have moved the success path's announcement.
        Harness harness = Harness.Build();
        StartedWebChatSession session = await harness.StartAsync();
        ConversationId conversation = ConversationId.From(session.Conversation.ConversationId);
        int before = AnnouncementsFor(harness, conversation);

        await harness.Send.HandleAsync(
            Harness.Sending(conversation.Value, session.AccessToken, "m1", "Hello?"));

        Assert.Equal(before + 1, AnnouncementsFor(harness, conversation));
    }

    private static int AnnouncementsFor(Harness harness, ConversationId conversation) =>
        harness.Announcer.Announced.Count(announced => announced.Conversation == conversation);
}
