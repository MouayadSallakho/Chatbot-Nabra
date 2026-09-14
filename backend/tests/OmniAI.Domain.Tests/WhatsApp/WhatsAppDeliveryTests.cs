using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.WhatsApp;

namespace OmniAI.Domain.Tests.WhatsApp;

/// <summary>
/// What a delivery does with the statuses a provider reports.
/// </summary>
/// <remarks>
/// <strong>Delivery receipts arrive out of order and more than once.</strong>
/// That is not an edge case to be defended against; it is how the provider
/// behaves on an ordinary day, because "delivered" and "read" can be seconds
/// apart and travel different paths. Everything here is about a colleague never
/// watching a message go backwards.
/// </remarks>
public sealed class WhatsAppDeliveryTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_queued_delivery_starts_pending_and_unsettled()
    {
        WhatsAppDelivery delivery = Queued();

        Assert.Equal(WhatsAppDeliveryStatus.Pending, delivery.Status);
        Assert.False(delivery.IsSettled);
        Assert.Null(delivery.ProviderMessageId);
    }

    [Fact]
    public void Statuses_move_forward_in_the_provider_order()
    {
        WhatsAppDelivery delivery = Sent();

        Assert.True(delivery.ApplyProviderStatus(WhatsAppDeliveryStatus.Delivered, null, Now));
        Assert.True(delivery.ApplyProviderStatus(WhatsAppDeliveryStatus.Read, null, Now));

        Assert.Equal(WhatsAppDeliveryStatus.Read, delivery.Status);
    }

    [Fact]
    public void A_status_that_arrives_late_never_regresses_the_delivery()
    {
        WhatsAppDelivery delivery = Sent();

        delivery.ApplyProviderStatus(WhatsAppDeliveryStatus.Read, null, Now);

        // "delivered" overtaken by "read" is routine. Applying it would tell a
        // colleague the customer had not read a message they had.
        Assert.False(delivery.ApplyProviderStatus(WhatsAppDeliveryStatus.Delivered, null, Now));
        Assert.Equal(WhatsAppDeliveryStatus.Read, delivery.Status);
    }

    [Fact]
    public void The_same_status_twice_changes_nothing()
    {
        WhatsAppDelivery delivery = Sent();

        Assert.True(delivery.ApplyProviderStatus(WhatsAppDeliveryStatus.Delivered, null, Now));
        Assert.False(delivery.ApplyProviderStatus(WhatsAppDeliveryStatus.Delivered, null, Now));
    }

    [Fact]
    public void A_failure_applies_even_over_a_later_state()
    {
        WhatsAppDelivery delivery = Sent();

        delivery.ApplyProviderStatus(WhatsAppDeliveryStatus.Read, null, Now);

        // Failure is not a step in the sequence; it is the provider saying this
        // message is not going to arrive. It must be able to overwrite an
        // optimistic state, or the inbox keeps claiming a customer read something
        // that was never delivered.
        Assert.True(delivery.ApplyProviderStatus(
            WhatsAppDeliveryStatus.Failed,
            "Message undeliverable",
            Now));

        Assert.Equal(WhatsAppDeliveryStatus.Failed, delivery.Status);
        Assert.Equal("Message undeliverable", delivery.FailureReason);
    }

    [Fact]
    public void A_repeated_failure_is_idempotent()
    {
        WhatsAppDelivery delivery = Sent();

        Assert.True(delivery.ApplyProviderStatus(WhatsAppDeliveryStatus.Failed, "gone", Now));
        Assert.False(delivery.ApplyProviderStatus(WhatsAppDeliveryStatus.Failed, "gone", Now));
    }

    [Fact]
    public void An_ambiguous_send_settles_as_unknown_and_stays_there()
    {
        WhatsAppDelivery delivery = Queued();

        delivery.Claim(Now);
        delivery.MarkUnknown("The connection failed.", Now);

        // Settled means nothing will try again. The customer may already have the
        // message, and a second attempt is worse than a state somebody has to
        // look at.
        Assert.Equal(WhatsAppDeliveryStatus.Unknown, delivery.Status);
        Assert.True(delivery.IsSettled);
    }

    private static WhatsAppDelivery Queued() => WhatsAppDelivery.Queue(
        WhatsAppConnectionId.New(),
        TenantId.New(),
        ConversationId.New(),
        MessageId.New(),
        WhatsAppCustomerId.Create("447700900123"),
        "Thanks for getting in touch.",
        Now);

    private static WhatsAppDelivery Sent()
    {
        WhatsAppDelivery delivery = Queued();

        delivery.Claim(Now);
        delivery.MarkSent(ProviderMessageId.Create("wamid.TEST=="), Now);

        return delivery;
    }
}
