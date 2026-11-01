using OmniAI.Domain.Conversations;
using OmniAI.Domain.Customers;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Domain.Tests.Conversations;

/// <summary>
/// The history of who answers a conversation (Phase 37.4): an event exactly when
/// handling changes, and never otherwise.
/// </summary>
public sealed class ConversationHandlingEventTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

    private static readonly UserId Sara = UserId.From(Guid.CreateVersion7());

    private static Conversation Start() =>
        Conversation.Start(TenantId.New(), CustomerId.New(), ChannelKind.WebChat, Now);

    [Fact]
    public void Taking_over_records_who_when_and_where()
    {
        Conversation conversation = Start();

        ConversationHandlingEvent? change = conversation.TakeOverByHuman(Sara, Now.AddMinutes(1), 42);

        Assert.NotNull(change);
        Assert.Equal(ConversationHandlingEventKind.TakenOver, change.Kind);
        Assert.Equal(Sara, change.Actor);
        Assert.Equal(Now.AddMinutes(1), change.OccurredAt);
        Assert.Equal(42, change.AfterSequence);
        Assert.Equal(conversation.Id, change.ConversationId);
        Assert.Equal(conversation.TenantId, change.TenantId);
        Assert.False(change.Id.IsEmpty);
        Assert.Equal(ConversationHandling.Human, conversation.Handling);
    }

    [Fact]
    public void Returning_records_who_when_and_where()
    {
        Conversation conversation = Start();
        conversation.TakeOverByHuman(Sara, Now.AddMinutes(1), 42);

        ConversationHandlingEvent? change = conversation.ReturnToAi(Sara, Now.AddMinutes(2), 43);

        Assert.NotNull(change);
        Assert.Equal(ConversationHandlingEventKind.ReturnedToAi, change.Kind);
        Assert.Equal(43, change.AfterSequence);
        Assert.Equal(ConversationHandling.Ai, conversation.Handling);
    }

    [Fact]
    public void An_idempotent_takeover_records_nothing()
    {
        // The history says what happened, not what was clicked.
        Conversation conversation = Start();
        conversation.TakeOverByHuman(Sara, Now.AddMinutes(1), 42);

        Assert.Null(conversation.TakeOverByHuman(Sara, Now.AddMinutes(2), 42));
    }

    [Fact]
    public void Returning_a_conversation_the_ai_already_answers_records_nothing()
    {
        Assert.Null(Start().ReturnToAi(Sara, Now.AddMinutes(1), 0));
    }

    [Fact]
    public void A_conversation_with_no_message_yet_is_taken_over_at_position_zero()
    {
        ConversationHandlingEvent? change = Start().TakeOverByHuman(Sara, Now.AddMinutes(1), 0);

        Assert.Equal(0, change!.AfterSequence);
    }

    [Fact]
    public void A_change_without_an_actor_is_refused_and_changes_nothing()
    {
        // Checked before handling flips: a refused argument must never leave the
        // conversation handed over with nothing recorded.
        Conversation conversation = Start();

        Assert.Throws<DomainValidationException>(() =>
            conversation.TakeOverByHuman(default(UserId), Now.AddMinutes(1), 1));

        Assert.Equal(ConversationHandling.Ai, conversation.Handling);
    }

    [Fact]
    public void A_negative_position_is_refused_and_changes_nothing()
    {
        Conversation conversation = Start();

        Assert.Throws<DomainValidationException>(() =>
            conversation.TakeOverByHuman(Sara, Now.AddMinutes(1), -1));

        Assert.Equal(ConversationHandling.Ai, conversation.Handling);
    }

    [Fact]
    public void A_closed_conversation_cannot_be_taken_over()
    {
        Conversation conversation = Start();
        conversation.Close(Now.AddMinutes(1));

        Assert.Throws<DomainStateException>(() =>
            conversation.TakeOverByHuman(Sara, Now.AddMinutes(2), 1));
    }
}
