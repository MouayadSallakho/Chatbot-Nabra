using OmniAI.Application.Tests.WebChat;
using OmniAI.Application.WhatsApp;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.WhatsApp;
using OmniAI.Testing;

namespace OmniAI.Application.Tests.WhatsApp;

/// <summary>
/// What a verified webhook does, and what it refuses to do.
/// </summary>
/// <remarks>
/// <strong>Everything here assumes the signature already passed.</strong> That
/// check lives at the HTTP edge and is asserted there. What is left is the second
/// half of the same security property: a payload known to be from Meta is still
/// not trusted to say which workspace it belongs to.
/// </remarks>
public sealed class WhatsAppWebhookTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    private const string ConnectedNumber = "100200300400500";

    [Fact]
    public async Task A_text_message_on_a_serving_number_is_stored_once()
    {
        World world = World.Build();

        WhatsAppWebhookOutcome outcome = await world.Handler
            .HandleAsync(Message("wamid.ONE=="));

        Assert.Equal(1, outcome.Stored);
        Assert.Single(world.WhatsApp.Inbound);
        Assert.Equal("Hello there", world.WhatsApp.Inbound[0].Text);
    }

    [Fact]
    public async Task The_same_provider_message_id_twice_is_stored_once()
    {
        World world = World.Build();

        await world.Handler.HandleAsync(Message("wamid.SAME=="));

        WhatsAppWebhookOutcome second = await world.Handler
            .HandleAsync(Message("wamid.SAME=="));

        // Meta redelivers whenever it is unsure the first attempt landed. One
        // customer message must mean one record, one turn and one reply.
        Assert.Equal(0, second.Stored);
        Assert.Equal(1, second.Duplicates);
        Assert.Single(world.WhatsApp.Inbound);
    }

    [Fact]
    public async Task An_unknown_number_creates_nothing_and_is_acknowledged()
    {
        World world = World.Build();

        WhatsAppWebhookOutcome outcome = await world.Handler.HandleAsync(
            new WhatsAppWebhook(
                [
                    new InboundWebhookMessage(
                        "999888777666555",
                        "wamid.STRANGER==",
                        "447700900123",
                        "text",
                        "Hello?",
                        Noon),
                ],
                []));

        // No workspace claims that number. Nothing is created, and Meta is still
        // told yes: answering no would make it resend this for hours.
        Assert.Equal(1, outcome.Unrouted);
        Assert.Empty(world.WhatsApp.Inbound);
    }

    [Fact]
    public async Task A_disabled_connection_creates_nothing()
    {
        World world = World.Build(serving: false);

        WhatsAppWebhookOutcome outcome = await world.Handler
            .HandleAsync(Message("wamid.OFF=="));

        Assert.Equal(1, outcome.Unrouted);
        Assert.Empty(world.WhatsApp.Inbound);
    }

    [Fact]
    public async Task An_image_is_recorded_without_text_and_never_answered()
    {
        World world = World.Build();

        WhatsAppWebhookOutcome outcome = await world.Handler.HandleAsync(
            new WhatsAppWebhook(
                [
                    new InboundWebhookMessage(
                        ConnectedNumber,
                        "wamid.IMAGE==",
                        "447700900123",
                        "image",
                        null,
                        Noon),
                ],
                []));

        // Stored so a redelivery is recognised; never given text, so nothing
        // downstream can mistake a placeholder for something a customer typed.
        Assert.Equal(1, outcome.Ignored);
        Assert.Null(Assert.Single(world.WhatsApp.Inbound).Text);
        Assert.Equal(InboundWhatsAppStatus.Ignored, world.WhatsApp.Inbound[0].Status);
    }

    [Fact]
    public async Task A_text_type_with_a_blank_body_is_ignored_rather_than_answered()
    {
        World world = World.Build();

        WhatsAppWebhookOutcome outcome = await world.Handler.HandleAsync(
            new WhatsAppWebhook(
                [
                    new InboundWebhookMessage(
                        ConnectedNumber,
                        "wamid.BLANK==",
                        "447700900123",
                        "text",
                        "   ",
                        Noon),
                ],
                []));

        Assert.Equal(1, outcome.Ignored);
        Assert.Null(Assert.Single(world.WhatsApp.Inbound).Text);
    }

    [Fact]
    public async Task A_status_for_a_message_this_deployment_never_sent_changes_nothing()
    {
        World world = World.Build();

        WhatsAppWebhookOutcome outcome = await world.Handler.HandleAsync(
            new WhatsAppWebhook(
                [],
                [new InboundWebhookStatus("wamid.FOREIGN==", "delivered", null, Noon)]));

        Assert.Equal(0, outcome.StatusesApplied);
        Assert.Empty(world.Announcer.Announced);
    }

    [Fact]
    public async Task A_delivery_receipt_creates_no_transcript_message()
    {
        World world = World.Build();

        await world.Handler.HandleAsync(
            new WhatsAppWebhook(
                [],
                [new InboundWebhookStatus("wamid.ANY==", "read", null, Noon)]));

        // A receipt is a fact about a message that already exists. Turning one
        // into a Message would put "read" into a customer's transcript.
        Assert.Empty(world.WhatsApp.Inbound);
        Assert.Empty(world.WhatsApp.Deliveries);
    }

    private static WhatsAppWebhook Message(string providerMessageId) =>
        new(
            [
                new InboundWebhookMessage(
                    ConnectedNumber,
                    providerMessageId,
                    "447700900123",
                    "text",
                    "Hello there",
                    Noon),
            ],
            []);

    private sealed class World
    {
        public required WhatsAppWorld WhatsApp { get; init; }

        public required RecordingRealtimeNotifier Announcer { get; init; }

        public required ReceiveWhatsAppWebhookHandler Handler { get; init; }

        public static World Build(bool serving = true)
        {
            WhatsAppWorld whatsapp = new();
            TenantId workspace = TenantId.New();
            AiAgentId agent = AiAgentId.New();

            if (serving)
            {
                whatsapp.Serving(workspace, agent, ConnectedNumber, Noon);
            }
            else
            {
                whatsapp.Draft(workspace, agent, ConnectedNumber, Noon);
            }

            RecordingRealtimeNotifier announcer = new();

            return new World
            {
                WhatsApp = whatsapp,
                Announcer = announcer,
                Handler = new ReceiveWhatsAppWebhookHandler(
                    whatsapp,
                    whatsapp,
                    whatsapp,
                    announcer,
                    new FixedTimeProvider(Noon)),
            };
        }
    }
}
