using OmniAI.Domain;
using OmniAI.Domain.Conversations;

namespace OmniAI.Domain.Tests.Conversations;

/// <summary>
/// The durable record of one customer message and the reply it asked for.
/// </summary>
public sealed class ConversationTurnTests
{
    private static readonly DateTimeOffset Noon =
        new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    private static ConversationTurn Begin() =>
        ConversationTurn.Begin(
            ConversationId.New(),
            ClientMessageId.Create("c1"),
            MessageId.New(),
            Noon);

    [Fact]
    public void A_new_turn_is_generating_and_has_no_reply_yet()
    {
        ConversationTurn turn = Begin();

        Assert.Equal(ConversationTurnStatus.Generating, turn.Status);
        Assert.Null(turn.ReplyMessageId);
        Assert.False(turn.IsSettled);
    }

    [Fact]
    public void Completing_records_the_reply()
    {
        ConversationTurn turn = Begin();
        MessageId reply = MessageId.New();

        turn.Complete(reply, Noon.AddSeconds(3));

        Assert.Equal(ConversationTurnStatus.Completed, turn.Status);
        Assert.Equal(reply, turn.ReplyMessageId);
        Assert.True(turn.IsSettled);
    }

    [Fact]
    public void Failing_settles_the_turn_and_names_no_reply()
    {
        // The customer's message is still stored; only the answer is missing.
        // Nothing here fabricates one.
        ConversationTurn turn = Begin();

        turn.Fail(Noon.AddSeconds(3));

        Assert.Equal(ConversationTurnStatus.Failed, turn.Status);
        Assert.Null(turn.ReplyMessageId);
        Assert.True(turn.IsSettled);
    }

    [Fact]
    public void A_settled_turn_cannot_be_settled_again()
    {
        // Terminal either way. A second completion would mean a second reply was
        // produced and paid for behind the first.
        ConversationTurn completed = Begin();
        completed.Complete(MessageId.New(), Noon);

        Assert.Throws<DomainStateException>(() => completed.Complete(MessageId.New(), Noon));
        Assert.Throws<DomainStateException>(() => completed.Fail(Noon));

        ConversationTurn failed = Begin();
        failed.Fail(Noon);

        Assert.Throws<DomainStateException>(() => failed.Complete(MessageId.New(), Noon));
        Assert.Throws<DomainStateException>(() => failed.Fail(Noon));
    }

    [Fact]
    public void A_completed_turn_must_name_its_reply()
    {
        ConversationTurn turn = Begin();

        Assert.Throws<DomainValidationException>(() => turn.Complete(default, Noon));
    }

    [Fact]
    public void The_change_instant_never_moves_backwards()
    {
        ConversationTurn turn = ConversationTurn.Begin(
            ConversationId.New(),
            ClientMessageId.Create("c1"),
            MessageId.New(),
            Noon.AddHours(2));

        turn.Complete(MessageId.New(), Noon);

        Assert.Equal(Noon.AddHours(2), turn.UpdatedAt);
    }

    [Fact]
    public void A_turn_must_name_a_conversation_a_client_id_and_a_message()
    {
        Assert.Throws<DomainValidationException>(() => ConversationTurn.Begin(
            default, ClientMessageId.Create("c1"), MessageId.New(), Noon));

        Assert.Throws<DomainValidationException>(() => ConversationTurn.Begin(
            ConversationId.New(), default, MessageId.New(), Noon));

        Assert.Throws<DomainValidationException>(() => ConversationTurn.Begin(
            ConversationId.New(), ClientMessageId.Create("c1"), default, Noon));
    }

    [Fact]
    public void A_stored_turn_is_rebuilt_exactly_as_it_was_written()
    {
        ConversationTurnId id = ConversationTurnId.New();
        MessageId reply = MessageId.New();

        ConversationTurn restored = ConversationTurn.FromStored(
            id,
            ConversationId.New(),
            ClientMessageId.Create("c9"),
            MessageId.New(),
            reply,
            ConversationTurnStatus.Completed,
            Noon,
            Noon.AddMinutes(1));

        Assert.Equal(id, restored.Id);
        Assert.Equal(reply, restored.ReplyMessageId);
        Assert.Equal(ConversationTurnStatus.Completed, restored.Status);
        Assert.True(restored.IsSettled);
    }
}

/// <summary>
/// The client's idempotency key.
/// </summary>
public sealed class ClientMessageIdTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_id_is_refused(string? value) =>
        Assert.Throws<DomainValidationException>(() => ClientMessageId.Create(value));

    [Fact]
    public void A_uuid_is_the_expected_shape_and_fits()
    {
        string uuid = Guid.CreateVersion7().ToString();

        Assert.Equal(uuid, ClientMessageId.Create(uuid).Value);
    }

    [Fact]
    public void An_over_long_id_is_refused_rather_than_cut()
    {
        // It arrives from an anonymous browser, so it is bounded -- and refusing
        // is the only honest answer, because a truncated idempotency key would
        // silently collide with somebody else's message.
        Assert.Throws<DomainValidationException>(
            () => ClientMessageId.Create(new string('a', ClientMessageId.MaxLength + 1)));

        Assert.Equal(
            ClientMessageId.MaxLength,
            ClientMessageId.Create(new string('a', ClientMessageId.MaxLength)).Value.Length);
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("has/slash")]
    [InlineData("has'quote")]
    [InlineData("has\nnewline")]
    public void An_id_holds_only_the_accepted_alphabet(string value) =>
        Assert.Throws<DomainValidationException>(() => ClientMessageId.Create(value));

    [Fact]
    public void Surrounding_whitespace_is_trimmed()
    {
        Assert.Equal("abc", ClientMessageId.Create("  abc  ").Value);
    }
}
