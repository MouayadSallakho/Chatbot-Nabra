using OmniAI.Application.WebChat.Hosted;
using OmniAI.Domain.Conversations;

namespace OmniAI.Application.Tests.WebChat;

/// <summary>
/// What a customer turn does while a person is handling the conversation.
/// </summary>
/// <remarks>
/// <strong>The point of every test here is a number that must be zero.</strong>
/// Human mode is not a different way of answering; it is the absence of
/// answering. Retrieval costs money, generation costs money, and a workspace
/// that took a conversation over by hand must not be billed for the AI it
/// deliberately stopped using.
/// </remarks>
public sealed class HumanModeRuntimeTests
{
    private static Harness Build() => Harness.Build();

    private static SendWebChatMessageCommand Sending(
        Guid conversationId,
        string? accessToken,
        string? clientMessageId,
        string? text) =>
        Harness.Sending(conversationId, accessToken, clientMessageId, text);

    [Fact]
    public async Task A_customer_message_is_stored_and_nothing_is_spent()
    {
        Harness harness = Build();
        StartedWebChatSession session = await harness.StartAsync();

        harness.World.Handling = ConversationHandling.Human;

        WebChatTurnResult? turn = await harness.Send.HandleAsync(Sending(
            session.Conversation.ConversationId,
            session.AccessToken,
            "m1",
            "Has my refund gone through?"));

        Assert.NotNull(turn);
        Assert.Equal("Has my refund gone through?", turn.CustomerMessage.Text);

        // The three facts this whole mode exists for.
        Assert.Null(turn.Reply);
        Assert.Equal(0, harness.Generator.Calls);
        Assert.Equal(0, harness.Embeddings.Calls);

        // Stored, and stored alone. No invented reply, no placeholder.
        Assert.Equal(
            [(MessageSender.Customer, "Has my refund gone through?")],
            harness.World.Transcript(ConversationId.From(session.Conversation.ConversationId)));
    }

    [Fact]
    public async Task The_result_says_a_person_is_handling_it()
    {
        // Without this the visitor's client cannot tell a turn that was left for
        // a colleague from one that failed, and would have to choose between
        // showing an error for a message that arrived perfectly and showing
        // nothing at all.
        Harness harness = Build();
        StartedWebChatSession session = await harness.StartAsync();

        harness.World.Handling = ConversationHandling.Human;

        WebChatTurnResult? turn = await harness.Send.HandleAsync(Sending(
            session.Conversation.ConversationId, session.AccessToken, "m1", "Hello?"));

        Assert.Equal(nameof(ConversationHandling.Human), turn!.Handling);
    }

    [Fact]
    public async Task An_ai_turn_still_says_so()
    {
        Harness harness = Build();
        StartedWebChatSession session = await harness.StartAsync();

        WebChatTurnResult? turn = await harness.Send.HandleAsync(Sending(
            session.Conversation.ConversationId, session.AccessToken, "m1", "Hello?"));

        Assert.Equal(nameof(ConversationHandling.Ai), turn!.Handling);
        Assert.NotNull(turn.Reply);
    }

    [Fact]
    public async Task No_generation_budget_is_consumed()
    {
        // The budget counts generation attempts. There is not going to be one,
        // and charging for it would make taking a conversation over cost money.
        Harness harness = Build();
        StartedWebChatSession session = await harness.StartAsync();

        harness.World.Handling = ConversationHandling.Human;

        await harness.Send.HandleAsync(Sending(
            session.Conversation.ConversationId, session.AccessToken, "m1", "Hello?"));

        Assert.Equal(0, harness.Protection.Generations);
        Assert.DoesNotContain("generation", harness.Protection.Calls);
    }

    [Fact]
    public async Task The_per_client_request_limit_still_applies()
    {
        // Human mode must not become a way around Phase 16. Only the part that
        // meters spending is skipped; the part that limits how fast a stranger
        // can write is not.
        Harness harness = Build();
        StartedWebChatSession session = await harness.StartAsync();

        harness.World.Handling = ConversationHandling.Human;

        await harness.Send.HandleAsync(Sending(
            session.Conversation.ConversationId, session.AccessToken, "m1", "Hello?"));

        Assert.Contains("request", harness.Protection.Calls);
    }

    [Fact]
    public async Task The_workspace_is_told_a_customer_wrote()
    {
        // Whoever took the conversation over is looking at a screen. The
        // announcement is what puts the new message on it without them doing
        // anything -- and it carries an identifier and nothing else.
        Harness harness = Build();
        StartedWebChatSession session = await harness.StartAsync();

        harness.World.Handling = ConversationHandling.Human;

        await harness.Send.HandleAsync(Sending(
            session.Conversation.ConversationId, session.AccessToken, "m1", "Hello?"));

        Assert.Contains(
            harness.Announcer.Announced,
            announced => announced.Conversation.Value == session.Conversation.ConversationId);
    }

    [Fact]
    public async Task A_repeat_of_the_same_message_replays_the_same_answer()
    {
        // The turn is settled, so a retry replays rather than starting again --
        // and replaying a human-mode turn means the customer's own message and
        // no reply, exactly as the first attempt returned.
        Harness harness = Build();
        StartedWebChatSession session = await harness.StartAsync();

        harness.World.Handling = ConversationHandling.Human;

        WebChatTurnResult? first = await harness.Send.HandleAsync(Sending(
            session.Conversation.ConversationId, session.AccessToken, "m1", "Hello?"));

        WebChatTurnResult? second = await harness.Send.HandleAsync(Sending(
            session.Conversation.ConversationId, session.AccessToken, "m1", "Hello?"));

        Assert.Equal(first!.CustomerMessage.Id, second!.CustomerMessage.Id);
        Assert.Null(second.Reply);
        Assert.Equal(nameof(ConversationHandling.Human), second.Handling);

        // One stored message, not two.
        Assert.Single(harness.World.Transcript(
            ConversationId.From(session.Conversation.ConversationId)));

        Assert.Equal(0, harness.Generator.Calls);
        Assert.Equal(0, harness.Embeddings.Calls);
    }

    [Fact]
    public async Task Returning_to_the_ai_answers_nothing_that_already_arrived()
    {
        // The property that matters most in this file. Messages sent while a
        // person was handling the conversation are never answered by the AI --
        // not on return, not ever. Generating replies to them would answer
        // questions a colleague may already have dealt with by phone, minutes
        // late, in a transcript the customer is still reading.
        Harness harness = Build();
        StartedWebChatSession session = await harness.StartAsync();

        harness.World.Handling = ConversationHandling.Human;

        await harness.Send.HandleAsync(Sending(
            session.Conversation.ConversationId, session.AccessToken, "m1", "First while human"));

        await harness.Send.HandleAsync(Sending(
            session.Conversation.ConversationId, session.AccessToken, "m2", "Second while human"));

        Assert.Equal(0, harness.Generator.Calls);

        // Handed back. Nothing runs on its own.
        harness.World.Handling = ConversationHandling.Ai;

        Assert.Equal(0, harness.Generator.Calls);

        // Only a genuinely new turn starts the AI again.
        WebChatTurnResult? resumed = await harness.Send.HandleAsync(Sending(
            session.Conversation.ConversationId, session.AccessToken, "m3", "And now?"));

        Assert.Equal(1, harness.Generator.Calls);
        Assert.NotNull(resumed!.Reply);

        // The two earlier customer messages are still there, still unanswered.
        Assert.Equal(
            [
                (MessageSender.Customer, "First while human"),
                (MessageSender.Customer, "Second while human"),
                (MessageSender.Customer, "And now?"),
                (MessageSender.AiAgent, "The answer."),
            ],
            harness.World.Transcript(ConversationId.From(session.Conversation.ConversationId)));
    }

    [Fact]
    public async Task Resuming_the_ai_uses_the_ordinary_retrieval_runtime()
    {
        // No separate RAG path for a resumed conversation. The next turn is an
        // ordinary turn.
        Harness harness = Build();
        StartedWebChatSession session = await harness.StartAsync();

        harness.World.Handling = ConversationHandling.Human;

        await harness.Send.HandleAsync(Sending(
            session.Conversation.ConversationId, session.AccessToken, "m1", "While human"));

        harness.World.Handling = ConversationHandling.Ai;

        await harness.Send.HandleAsync(Sending(
            session.Conversation.ConversationId, session.AccessToken, "m2", "After return"));

        // The provider saw the whole history, including what arrived while a
        // person was handling it: the customer said those things, and hiding
        // them would make the agent answer without context it has.
        Assert.Equal(1, harness.Generator.Calls);

        IReadOnlyList<string> turns =
            [.. harness.Generator.Requests[0].Turns.Select(turn => turn.Text)];

        Assert.Contains("While human", turns);
        Assert.Contains("After return", turns);
    }

    [Fact]
    public async Task A_failed_announcement_does_not_fail_the_turn()
    {
        // Realtime is best effort. A message that was stored and not announced
        // is a message the next read finds; a message refused because an
        // announcement failed is a message the customer never sent.
        Harness harness = Build();
        StartedWebChatSession session = await harness.StartAsync();

        harness.World.Handling = ConversationHandling.Human;
        harness.Announcer.Failure = new InvalidOperationException("backplane down");

        WebChatTurnResult? turn = await harness.Send.HandleAsync(Sending(
            session.Conversation.ConversationId, session.AccessToken, "m1", "Hello?"));

        Assert.NotNull(turn);
        Assert.Single(harness.World.Transcript(
            ConversationId.From(session.Conversation.ConversationId)));
    }
}
