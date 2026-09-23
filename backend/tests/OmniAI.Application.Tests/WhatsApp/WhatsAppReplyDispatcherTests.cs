using OmniAI.Application.Tests.WebChat;
using OmniAI.Application.WhatsApp;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Customers;
using OmniAI.Domain.WhatsApp;
using OmniAI.Testing;

namespace OmniAI.Application.Tests.WhatsApp;

/// <summary>
/// What happens to a colleague's reply after it is stored.
/// </summary>
/// <remarks>
/// <strong>The inbox composer is the same one for every channel.</strong> A
/// person types a reply and presses send; whether that reply also has to travel
/// to Meta is decided here, and the admin never calls Meta itself.
/// </remarks>
public sealed class WhatsAppReplyDispatcherTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    private const string ConnectedNumber = "100200300400500";

    [Fact]
    public async Task A_reply_on_a_website_conversation_queues_nothing()
    {
        World world = World.Build(onWhatsApp: false);

        await world.Dispatcher.DispatchAsync(
            world.Harness.Workspace,
            world.Conversation.Id,
            MessageId.New(),
            "Happy to help.");

        // The visitor's browser is already reading the transcript. A delivery row
        // here would be a message queued for a channel that does not exist.
        Assert.Empty(world.WhatsApp.Deliveries);
    }

    [Fact]
    public async Task A_reply_on_a_whatsapp_conversation_queues_exactly_one_delivery()
    {
        World world = World.Build(onWhatsApp: true);

        MessageId reply = MessageId.New();

        await world.Dispatcher.DispatchAsync(
            world.Harness.Workspace,
            world.Conversation.Id,
            reply,
            "Happy to help.");

        WhatsAppDelivery delivery = Assert.Single(world.WhatsApp.Deliveries);

        Assert.Equal(reply, delivery.MessageId);
        Assert.Equal(WhatsAppDeliveryStatus.Pending, delivery.Status);
    }

    [Fact]
    public async Task Dispatching_the_same_reply_twice_queues_one_delivery()
    {
        World world = World.Build(onWhatsApp: true);

        MessageId reply = MessageId.New();

        await world.Dispatcher.DispatchAsync(
            world.Harness.Workspace,
            world.Conversation.Id,
            reply,
            "Happy to help.");

        await world.Dispatcher.DispatchAsync(
            world.Harness.Workspace,
            world.Conversation.Id,
            reply,
            "Happy to help.");

        // One message is one delivery. A retried request must not show the
        // customer the same sentence twice.
        Assert.Single(world.WhatsApp.Deliveries);
    }

    [Fact]
    public async Task Outside_the_window_the_reply_is_kept_and_marked_undelivered()
    {
        World world = World.Build(onWhatsApp: true, lastCustomerMessageAt: Noon.AddHours(-30));

        await world.Dispatcher.DispatchAsync(
            world.Harness.Workspace,
            world.Conversation.Id,
            MessageId.New(),
            "Sorry for the delay.");

        WhatsAppDelivery delivery = Assert.Single(world.WhatsApp.Deliveries);

        // The colleague's words are in the transcript and the row says plainly
        // that they did not reach the customer. Silently dropping the delivery
        // would leave them believing it had.
        Assert.Equal(WhatsAppDeliveryStatus.Failed, delivery.Status);
        Assert.Equal(CustomerServiceWindow.ClosedReason, delivery.FailureReason);
    }

    private sealed class World
    {
        public required Harness Harness { get; init; }

        public required WhatsAppWorld WhatsApp { get; init; }

        public required Conversation Conversation { get; init; }

        public required WhatsAppReplyDispatcher Dispatcher { get; init; }

        public static World Build(bool onWhatsApp, DateTimeOffset? lastCustomerMessageAt = null)
        {
            Harness harness = Harness.Build();
            WhatsAppWorld whatsapp = new();

            Conversation conversation = Conversation.Start(
                harness.Workspace,
                CustomerId.New(),
                onWhatsApp ? ChannelKind.WhatsApp : ChannelKind.WebChat,
                Noon);

            harness.World.AddConversation(conversation);

            harness.World.SeedMessage(
                conversation.Id,
                MessageSender.Customer,
                "Hello",
                lastCustomerMessageAt ?? Noon.AddMinutes(-5));

            if (onWhatsApp)
            {
                whatsapp.Serving(harness.Workspace, harness.Agent.Id, ConnectedNumber, Noon);
                whatsapp.NextConversation = () => conversation.Id;

                whatsapp.ResolveConversationAsync(
                        whatsapp.FindRouteAsync(PhoneNumberId.Create(ConnectedNumber))
                            .GetAwaiter()
                            .GetResult()!,
                        WhatsAppCustomerId.Create("447700900123"),
                        Noon)
                    .GetAwaiter()
                    .GetResult();
            }

            FixedTimeProvider clock = new(Noon);

            return new World
            {
                Harness = harness,
                WhatsApp = whatsapp,
                Conversation = conversation,
                Dispatcher = new WhatsAppReplyDispatcher(
                    whatsapp,
                    whatsapp,
                    new CustomerServiceWindow(clock),
                    harness.World,
                    clock),
            };
        }
    }
}
