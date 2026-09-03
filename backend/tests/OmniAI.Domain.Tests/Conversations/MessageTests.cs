using System.Reflection;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;

namespace OmniAI.Domain.Tests.Conversations;

/// <summary>
/// Guards the immutability of message history. A message records something that
/// was said; if it can be edited, the transcript stops being evidence.
/// </summary>
public sealed class MessageTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_message_exposes_no_setters_at_all()
    {
        PropertyInfo[] settable =
        [
            .. typeof(Message)
                .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(property => property.SetMethod is not null)
        ];

        Assert.Empty(settable);
    }

    [Fact]
    public void A_message_cannot_be_constructed_outside_its_conversation()
    {
        // Every constructor is non-public, so a message cannot exist detached
        // from the conversation that owns it.
        Assert.Empty(typeof(Message).GetConstructors());
    }

    [Fact]
    public void A_message_keeps_the_values_it_was_created_with()
    {
        Conversation conversation = Conversation.Start(TenantId.New(), ChannelKind.WhatsApp, Start);

        Message message = conversation.AddTextMessage(
            MessageSender.Customer,
            "Original text",
            Start.AddMinutes(1));

        // Drive further activity through the aggregate, then re-check the message.
        conversation.AddTextMessage(MessageSender.AiAgent, "Reply", Start.AddMinutes(2));
        conversation.Close(Start.AddMinutes(3));

        Assert.Equal("Original text", message.Text);
        Assert.Equal(MessageSender.Customer, message.Sender);
        Assert.Equal(MessageContentType.Text, message.ContentType);
        Assert.Equal(Start.AddMinutes(1), message.SentAt);
        Assert.Equal(conversation.Id, message.ConversationId);
    }

    [Fact]
    public void A_message_records_its_instant_in_utc()
    {
        Conversation conversation = Conversation.Start(TenantId.New(), ChannelKind.WebChat, Start);
        DateTimeOffset withOffset = new(2026, 1, 1, 15, 30, 0, TimeSpan.FromHours(3));

        Message message = conversation.AddTextMessage(MessageSender.Customer, "Hello", withOffset);

        Assert.Equal(TimeSpan.Zero, message.SentAt.Offset);
        Assert.Equal(withOffset.UtcDateTime, message.SentAt.UtcDateTime);
    }
}
