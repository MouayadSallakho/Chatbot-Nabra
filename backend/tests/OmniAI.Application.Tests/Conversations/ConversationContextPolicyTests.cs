using OmniAI.Application.Ai;
using OmniAI.Application.Conversations;
using OmniAI.Application.Conversations.Persistence;
using OmniAI.Domain.Conversations;

namespace OmniAI.Application.Tests.Conversations;

/// <summary>
/// How much of a conversation is replayed to the AI, and which part.
/// </summary>
/// <remarks>
/// A conversation is unbounded and a request is not, so something has to be left
/// out. These pin what: the newest whole messages, in the order the platform
/// ingested them, with nothing cut in half and nothing reordered.
/// </remarks>
public sealed class ConversationContextPolicyTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    private static ConversationMessageRecord Message(
        long sequence,
        MessageSender sender,
        string text,
        DateTimeOffset? sentAt = null) =>
        new(Guid.CreateVersion7(), sender, text, sentAt ?? Noon, sequence);

    private static List<ConversationMessageRecord> Exchange(int pairs)
    {
        List<ConversationMessageRecord> history = [];

        for (int index = 0; index < pairs; index++)
        {
            history.Add(Message(history.Count + 1, MessageSender.Customer, $"Question {index}"));
            history.Add(Message(history.Count + 1, MessageSender.AiAgent, $"Answer {index}"));
        }

        return history;
    }

    [Fact]
    public void A_short_conversation_is_sent_whole()
    {
        ConversationContextPolicy policy = new();

        IReadOnlyList<AiTurn> turns = policy.SelectTurns(Exchange(2));

        Assert.Equal(
            ["Question 0", "Answer 0", "Question 1", "Answer 1"],
            turns.Select(turn => turn.Text));
    }

    [Fact]
    public void Senders_map_to_the_roles_a_provider_understands()
    {
        ConversationContextPolicy policy = new();

        IReadOnlyList<AiTurn> turns = policy.SelectTurns(Exchange(1));

        Assert.Equal([AiTurnRole.User, AiTurnRole.Assistant], turns.Select(turn => turn.Role));
    }

    [Fact]
    public void The_message_count_bound_keeps_the_newest_and_drops_the_oldest()
    {
        ConversationContextPolicy policy = new() { MaxMessages = 4 };

        IReadOnlyList<AiTurn> turns = policy.SelectTurns(Exchange(5));

        Assert.Equal(4, turns.Count);
        Assert.Equal(
            ["Question 3", "Answer 3", "Question 4", "Answer 4"],
            turns.Select(turn => turn.Text));
    }

    [Fact]
    public void The_character_bound_keeps_the_newest_and_drops_the_oldest()
    {
        ConversationContextPolicy policy = new() { MaxTotalCharacters = 25 };

        IReadOnlyList<ConversationMessageRecord> history =
        [
            Message(1, MessageSender.Customer, new string('a', 20)),
            Message(2, MessageSender.AiAgent, new string('b', 20)),
            Message(3, MessageSender.Customer, new string('c', 20)),
        ];

        IReadOnlyList<AiTurn> turns = policy.SelectTurns(history);

        AiTurn kept = Assert.Single(turns);
        Assert.Equal(new string('c', 20), kept.Text);
    }

    [Fact]
    public void Either_bound_alone_is_enough_to_cut_the_window()
    {
        // A count alone lets forty long messages through; a character count alone
        // lets a thousand short ones through. The tighter bound wins on any given
        // conversation, which is why both are applied.
        List<ConversationMessageRecord> manyShort = Exchange(30);

        Assert.Equal(
            10,
            new ConversationContextPolicy { MaxMessages = 10, MaxTotalCharacters = 100_000 }
                .SelectTurns(manyShort)
                .Count);

        IReadOnlyList<ConversationMessageRecord> fewLong =
        [
            Message(1, MessageSender.Customer, new string('a', 5_000)),
            Message(2, MessageSender.AiAgent, new string('b', 5_000)),
            Message(3, MessageSender.Customer, new string('c', 5_000)),
        ];

        Assert.Equal(
            2,
            new ConversationContextPolicy { MaxMessages = 40, MaxTotalCharacters = 10_000 }
                .SelectTurns(fewLong)
                .Count);
    }

    [Fact]
    public void Nothing_is_ever_truncated()
    {
        // The one thing this policy must never do. A half-message changes what
        // somebody said, and a model asked to continue from a sentence that stops
        // mid-word answers something nobody said either.
        ConversationContextPolicy policy = new() { MaxTotalCharacters = 10 };

        IReadOnlyList<ConversationMessageRecord> history =
        [
            Message(1, MessageSender.Customer, new string('a', 500)),
        ];

        AiTurn only = Assert.Single(policy.SelectTurns(history));

        Assert.Equal(500, only.Text.Length);
    }

    [Fact]
    public void The_newest_message_survives_a_bound_it_exceeds_on_its_own()
    {
        // Otherwise a long question would be dropped and the provider asked to
        // answer the conversation without it.
        ConversationContextPolicy policy = new() { MaxTotalCharacters = 10 };

        IReadOnlyList<ConversationMessageRecord> history =
        [
            Message(1, MessageSender.Customer, "Older"),
            Message(2, MessageSender.Customer, new string('z', 400)),
        ];

        AiTurn only = Assert.Single(policy.SelectTurns(history));

        Assert.Equal(new string('z', 400), only.Text);
    }

    [Fact]
    public void Order_is_ingestion_order_even_when_timestamps_disagree()
    {
        // The Phase 3 invariant. Ingestion order is the platform's order; SentAt
        // is display only and, in a distributed omnichannel system, routinely
        // arrives out of sequence.
        ConversationContextPolicy policy = new();

        IReadOnlyList<ConversationMessageRecord> history =
        [
            Message(1, MessageSender.Customer, "Ingested first", Noon),
            Message(2, MessageSender.AiAgent, "Ingested second", Noon.AddHours(-3)),
            Message(3, MessageSender.Customer, "Ingested third", Noon.AddHours(-6)),
        ];

        Assert.Equal(
            ["Ingested first", "Ingested second", "Ingested third"],
            policy.SelectTurns(history).Select(turn => turn.Text));
    }

    [Fact]
    public void A_window_cut_by_a_bound_is_still_in_ingestion_order()
    {
        ConversationContextPolicy policy = new() { MaxMessages = 2 };

        IReadOnlyList<ConversationMessageRecord> history =
        [
            Message(1, MessageSender.Customer, "One", Noon),
            Message(2, MessageSender.AiAgent, "Two", Noon.AddHours(-5)),
            Message(3, MessageSender.Customer, "Three", Noon.AddHours(-9)),
        ];

        Assert.Equal(["Two", "Three"], policy.SelectTurns(history).Select(turn => turn.Text));
    }

    [Fact]
    public void System_notes_are_never_replayed()
    {
        // They are the platform talking about the conversation, not somebody
        // talking in it. Replaying one would let internal audit text be read as a
        // customer's words -- and as an instruction to the model.
        ConversationContextPolicy policy = new();

        IReadOnlyList<ConversationMessageRecord> history =
        [
            Message(1, MessageSender.Customer, "Hello"),
            Message(2, MessageSender.System, "Conversation reopened by an administrator."),
            Message(3, MessageSender.AiAgent, "Hi"),
        ];

        Assert.Equal(["Hello", "Hi"], policy.SelectTurns(history).Select(turn => turn.Text));
    }

    [Fact]
    public void Human_agent_messages_are_not_replayed_while_handoff_does_not_exist()
    {
        ConversationContextPolicy policy = new();

        IReadOnlyList<ConversationMessageRecord> history =
        [
            Message(1, MessageSender.Customer, "Hello"),
            Message(2, MessageSender.HumanAgent, "A colleague replying by hand"),
        ];

        AiTurn only = Assert.Single(policy.SelectTurns(history));

        Assert.Equal("Hello", only.Text);
    }

    [Fact]
    public void A_message_too_long_to_be_a_turn_is_dropped_rather_than_shortened()
    {
        // Defensive: nothing the public surface accepts can exceed a turn's own
        // limit. A future channel could ingest one, and shortening somebody's
        // words to make them fit is exactly what this policy must not do.
        ConversationContextPolicy policy = new() { MaxTotalCharacters = int.MaxValue };

        IReadOnlyList<ConversationMessageRecord> history =
        [
            Message(1, MessageSender.Customer, new string('x', AiTurn.MaxTextLength + 1)),
            Message(2, MessageSender.Customer, "Readable"),
        ];

        AiTurn only = Assert.Single(policy.SelectTurns(history));

        Assert.Equal("Readable", only.Text);
    }

    [Fact]
    public void An_empty_history_selects_nothing_rather_than_inventing_a_turn()
    {
        Assert.Empty(new ConversationContextPolicy().SelectTurns([]));
    }

    [Fact]
    public void The_default_bounds_are_the_documented_ones()
    {
        // Pinned because they are a cost decision, not an implementation detail:
        // changing them changes what every reply costs and how much a customer can
        // refer back to.
        ConversationContextPolicy policy = new();

        Assert.Equal(40, policy.MaxMessages);
        Assert.Equal(24_000, policy.MaxTotalCharacters);
    }
}
