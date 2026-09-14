using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Domain.Tests.Conversations;

/// <summary>
/// Who answers a conversation, and who it is routed to.
/// </summary>
public sealed class ConversationHandoffTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 14, 9, 0, 0, TimeSpan.Zero);

    private static Conversation Start() =>
        Conversation.Start(TenantId.New(), ChannelKind.WebChat, Now);

    [Fact]
    public void A_new_conversation_is_answered_by_the_ai()
    {
        // Not because nothing else is possible, but because a conversation
        // arriving is not a reason for a person to look at it.
        Assert.Equal(ConversationHandling.Ai, Start().Handling);
        Assert.Null(Start().AssignedTo);
    }

    [Fact]
    public void Taking_over_stops_the_ai_answering()
    {
        Conversation conversation = Start();

        Assert.True(conversation.TakeOverByHuman(Now.AddMinutes(1)));
        Assert.Equal(ConversationHandling.Human, conversation.Handling);
    }

    [Fact]
    public void Taking_over_twice_is_not_an_error()
    {
        // Two people pressing the button within a second of each other both
        // meant the same thing. The second is told nothing changed, not that
        // something went wrong.
        Conversation conversation = Start();

        conversation.TakeOverByHuman(Now.AddMinutes(1));

        Assert.False(conversation.TakeOverByHuman(Now.AddMinutes(2)));
        Assert.Equal(ConversationHandling.Human, conversation.Handling);
    }

    [Fact]
    public void Returning_to_the_ai_hands_it_back()
    {
        Conversation conversation = Start();

        conversation.TakeOverByHuman(Now.AddMinutes(1));

        Assert.True(conversation.ReturnToAi(Now.AddMinutes(2)));
        Assert.Equal(ConversationHandling.Ai, conversation.Handling);
    }

    [Fact]
    public void Returning_an_ai_conversation_changes_nothing()
    {
        Conversation conversation = Start();

        Assert.False(conversation.ReturnToAi(Now.AddMinutes(1)));
        Assert.Equal(ConversationHandling.Ai, conversation.Handling);
    }

    [Fact]
    public void A_full_lifecycle_can_be_repeated()
    {
        // Taking a conversation over, handing it back, and doing it again is an
        // ordinary afternoon rather than a state machine that wears out.
        Conversation conversation = Start();

        for (int round = 1; round <= 3; round++)
        {
            Assert.True(conversation.TakeOverByHuman(Now.AddMinutes(round * 2)));
            Assert.True(conversation.ReturnToAi(Now.AddMinutes((round * 2) + 1)));
        }

        Assert.Equal(ConversationHandling.Ai, conversation.Handling);
    }

    [Fact]
    public void Handling_moves_the_activity_mark_forward_only()
    {
        Conversation conversation = Start();

        conversation.TakeOverByHuman(Now.AddMinutes(5));

        DateTimeOffset mark = conversation.LastActivityAt;

        // An action stamped earlier than the mark is still applied; the mark is
        // a high-water mark and never regresses.
        conversation.ReturnToAi(Now.AddMinutes(1));

        Assert.Equal(ConversationHandling.Ai, conversation.Handling);
        Assert.Equal(mark, conversation.LastActivityAt);
    }

    [Fact]
    public void A_closed_conversation_cannot_be_taken_over()
    {
        Conversation conversation = Start();

        conversation.Close(Now.AddMinutes(1));

        Assert.Throws<DomainStateException>(() => conversation.TakeOverByHuman(Now.AddMinutes(2)));
        Assert.Throws<DomainStateException>(() => conversation.ReturnToAi(Now.AddMinutes(2)));
    }

    [Fact]
    public void Assignment_does_not_change_who_is_answering()
    {
        // The two are independent facts. Routing a conversation to a colleague
        // does not stop the AI answering it, and taking it over does not claim
        // it for anybody.
        Conversation conversation = Start();

        Assert.True(conversation.AssignTo(UserId.New(), Now.AddMinutes(1)));
        Assert.Equal(ConversationHandling.Ai, conversation.Handling);

        conversation.TakeOverByHuman(Now.AddMinutes(2));

        Assert.NotNull(conversation.AssignedTo);
    }

    [Fact]
    public void Assigning_the_same_member_again_changes_nothing()
    {
        Conversation conversation = Start();
        UserId member = UserId.New();

        conversation.AssignTo(member, Now.AddMinutes(1));

        Assert.False(conversation.AssignTo(member, Now.AddMinutes(2)));
    }

    [Fact]
    public void Assignment_can_be_removed()
    {
        Conversation conversation = Start();

        conversation.AssignTo(UserId.New(), Now.AddMinutes(1));

        Assert.True(conversation.Unassign(Now.AddMinutes(2)));
        Assert.Null(conversation.AssignedTo);

        // Unclaiming something already unclaimed is a no-op, not a fault.
        Assert.False(conversation.Unassign(Now.AddMinutes(3)));
    }

    [Fact]
    public void Unassigning_leaves_a_person_handling_it()
    {
        // Releasing a conversation says "not mine", not "give it back to the
        // robot". Conflating the two would hand a customer back to an AI in the
        // middle of a conversation somebody escalated on purpose.
        Conversation conversation = Start();

        conversation.TakeOverByHuman(Now.AddMinutes(1));
        conversation.AssignTo(UserId.New(), Now.AddMinutes(2));
        conversation.Unassign(Now.AddMinutes(3));

        Assert.Equal(ConversationHandling.Human, conversation.Handling);
    }

    [Fact]
    public void An_assignment_must_name_somebody()
    {
        Conversation conversation = Start();

        Assert.Throws<DomainValidationException>(
            () => conversation.AssignTo(default, Now.AddMinutes(1)));
    }
}
