using OmniAI.Application.Security;
using OmniAI.Application.Tests.WebChat;
using OmniAI.Application.WhatsApp;
using OmniAI.Application.WhatsApp.Persistence;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.WhatsApp;
using OmniAI.Testing;

namespace OmniAI.Application.Tests.WhatsApp;

/// <summary>
/// What the platform does with each of the three outcomes a send can have.
/// </summary>
/// <remarks>
/// <strong>The third outcome is the reason this suite exists.</strong> Success
/// and failure are easy; a request that may or may not have arrived is the one
/// that decides whether a customer reads the same sentence twice.
/// </remarks>
public sealed class WhatsAppOutboundTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    private const string ConnectedNumber = "100200300400500";

    [Fact]
    public async Task An_accepted_send_records_the_provider_id()
    {
        World world = World.Build();

        await world.DispatchAsync();

        WhatsAppDelivery delivery = Assert.Single(world.WhatsApp.Deliveries);

        Assert.Equal(WhatsAppDeliveryStatus.Sent, delivery.Status);
        Assert.Equal("wamid.SENT==", delivery.ProviderMessageId?.Value);
        Assert.Single(world.Sender.Sent);
    }

    [Fact]
    public async Task A_definitive_refusal_is_a_failure_with_a_reason_a_colleague_can_read()
    {
        World world = World.Build();

        world.Sender.Result = WhatsAppSendResult.Rejected("WhatsApp refused the message.");

        await world.DispatchAsync();

        WhatsAppDelivery delivery = Assert.Single(world.WhatsApp.Deliveries);

        Assert.Equal(WhatsAppDeliveryStatus.Failed, delivery.Status);
        Assert.Equal("WhatsApp refused the message.", delivery.FailureReason);
    }

    [Fact]
    public async Task An_ambiguous_send_is_unknown_and_is_never_sent_again()
    {
        World world = World.Build();

        world.Sender.Result = WhatsAppSendResult.Ambiguous("The connection failed.");

        await world.DispatchAsync();

        WhatsAppDelivery delivery = Assert.Single(world.WhatsApp.Deliveries);

        Assert.Equal(WhatsAppDeliveryStatus.Unknown, delivery.Status);

        // The claim already counted the attempt, and the outbox allows one. A
        // second pass finds nothing, which is the mechanism -- not a convention
        // somebody has to remember when writing the worker.
        Assert.Empty(await world.WhatsApp.ClaimDeliveriesAsync(now: Noon));
        Assert.Single(world.Sender.Sent);
    }

    [Fact]
    public async Task A_send_accepted_without_an_id_is_unknown_rather_than_sent()
    {
        World world = World.Build();

        world.Sender.Result = new WhatsAppSendResult(
            WhatsAppSendKind.Accepted,
            ProviderMessageId: null,
            Reason: null);

        await world.DispatchAsync();

        // Nothing could ever match a delivery receipt to it, so claiming it was
        // sent would be claiming knowledge the platform will never have.
        Assert.Equal(WhatsAppDeliveryStatus.Unknown, Assert.Single(world.WhatsApp.Deliveries).Status);
    }

    [Fact]
    public async Task Outside_the_service_window_nothing_is_sent_at_all()
    {
        // The customer last wrote more than a day ago, so Meta allows only a
        // template -- which this phase does not implement. Trying anyway would
        // cost a round trip and report the refusal in Meta's words.
        World world = World.Build(lastCustomerMessageAt: Noon - TimeSpan.FromHours(25));

        await world.DispatchAsync();

        Assert.Empty(world.Sender.Sent);

        WhatsAppDelivery delivery = Assert.Single(world.WhatsApp.Deliveries);

        Assert.Equal(WhatsAppDeliveryStatus.Failed, delivery.Status);
        Assert.Equal(CustomerServiceWindow.ClosedReason, delivery.FailureReason);
    }

    [Fact]
    public async Task A_connection_whose_credential_cannot_be_read_fails_honestly()
    {
        World world = World.Build(decryptable: false);

        await world.DispatchAsync();

        Assert.Empty(world.Sender.Sent);

        WhatsAppDelivery delivery = Assert.Single(world.WhatsApp.Deliveries);

        Assert.Equal(WhatsAppDeliveryStatus.Failed, delivery.Status);
        Assert.Contains("access token", delivery.FailureReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_window_is_measured_from_the_customers_last_message_only()
    {
        // A business reply does not reopen the window. If it did, a workspace
        // could keep a conversation open indefinitely by talking to itself.
        CustomerServiceWindow window = new(new FixedTimeProvider(Noon));

        Assert.True(window.IsOpen(Noon - TimeSpan.FromHours(23)));
        Assert.False(window.IsOpen(Noon - TimeSpan.FromHours(25)));
        Assert.False(window.IsOpen(null));
    }

    private sealed class World
    {
        public required WhatsAppWorld WhatsApp { get; init; }

        public required RecordingWhatsAppSender Sender { get; init; }

        public required DispatchWhatsAppDeliveryHandler Handler { get; init; }

        public static World Build(
            DateTimeOffset? lastCustomerMessageAt = null,
            bool decryptable = true)
        {
            Harness harness = Harness.Build();

            WhatsAppWorld whatsapp = new();

            WhatsAppConnection connection = whatsapp.Serving(
                harness.Workspace,
                harness.Agent.Id,
                ConnectedNumber,
                Noon);

            Conversation conversation = Conversation.Start(
                harness.Workspace,
                ChannelKind.WhatsApp,
                Noon);

            harness.World.AddConversation(conversation);

            // A customer message in the transcript is what holds the window open.
            harness.World.SeedMessage(
                conversation.Id,
                MessageSender.Customer,
                "Hello",
                lastCustomerMessageAt ?? Noon - TimeSpan.FromMinutes(5));

            whatsapp.QueueAsync(WhatsAppDelivery.Queue(
                connection.Id,
                harness.Workspace,
                conversation.Id,
                MessageId.New(),
                WhatsAppCustomerId.Create("447700900123"),
                "Thanks for waiting.",
                Noon)).GetAwaiter().GetResult();

            RecordingWhatsAppSender sender = new();
            FixedTimeProvider clock = new(Noon);

            return new World
            {
                WhatsApp = whatsapp,
                Sender = sender,
                Handler = new DispatchWhatsAppDeliveryHandler(
                    whatsapp,
                    whatsapp,
                    harness.World,
                    sender,
                    new StubSecretProtector(decryptable),
                    new CustomerServiceWindow(clock),
                    harness.Announcer,
                    clock),
            };
        }

        public async Task DispatchAsync()
        {
            foreach (ClaimedDelivery claimed in await WhatsApp.ClaimDeliveriesAsync(now: Noon))
            {
                await Handler.HandleAsync(claimed);
            }
        }
    }
}

/// <summary>
/// A protector that either can or cannot read what it is given.
/// </summary>
/// <remarks>
/// The real one is exercised against real ciphertext in the Infrastructure suite.
/// What this suite needs is the two answers the dispatcher has to handle: a usable
/// token, and a deployment that cannot produce one.
/// </remarks>
internal sealed class StubSecretProtector(bool decryptable) : ISecretProtector
{
    public byte[] Protect(string secret, SecretContext context) => [1, 2, 3];

    public string? Unprotect(byte[]? protectedSecret, SecretContext context) =>
        decryptable && protectedSecret is not null ? "fake-local-test-token" : null;
}
