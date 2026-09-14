using OmniAI.Domain.Tenants;
using OmniAI.Domain.WhatsApp;

namespace OmniAI.Domain.Tests.WhatsApp;

/// <summary>The durable record of one inbound message.</summary>
public sealed class InboundWhatsAppMessageTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_text_message_waits_to_be_claimed()
    {
        InboundWhatsAppMessage message = Text("Hello");

        Assert.Equal(InboundWhatsAppStatus.Pending, message.Status);
        Assert.Equal(0, message.Attempts);
        Assert.Equal("Hello", message.Text);
    }

    [Fact]
    public void An_unsupported_type_is_recorded_with_no_text_and_never_queued()
    {
        InboundWhatsAppMessage message = InboundWhatsAppMessage.Ignored(
            WhatsAppConnectionId.New(),
            TenantId.New(),
            ProviderMessageId.Create("wamid.IMAGE=="),
            WhatsAppCustomerId.Create("447700900123"),
            Now,
            Now);

        // <strong>No invented text.</strong> An image recorded as "[image]"
        // would reach the AI as something the customer typed, and the reply would
        // be answering words nobody said. It is stored so a redelivery is
        // recognised, and born ignored so no worker picks it up.
        Assert.Null(message.Text);
        Assert.Equal(InboundWhatsAppStatus.Ignored, message.Status);
    }

    [Fact]
    public void Claiming_counts_the_attempt()
    {
        InboundWhatsAppMessage message = Text("Hello");

        message.Claim(Now);

        // Counted at the claim, not at the failure. A worker that dies mid-message
        // never reports anything, and a count kept at the failure would let that
        // message be retried for ever.
        Assert.Equal(InboundWhatsAppStatus.Claimed, message.Status);
        Assert.Equal(1, message.Attempts);

        message.Release(Now);
        message.Claim(Now);

        Assert.Equal(2, message.Attempts);
    }

    [Fact]
    public void A_processed_message_says_so()
    {
        InboundWhatsAppMessage message = Text("Hello");

        message.Claim(Now);
        message.MarkProcessed(Now);

        Assert.Equal(InboundWhatsAppStatus.Processed, message.Status);
    }

    private static InboundWhatsAppMessage Text(string body) => InboundWhatsAppMessage.ForText(
        WhatsAppConnectionId.New(),
        TenantId.New(),
        ProviderMessageId.Create("wamid.TEXT=="),
        WhatsAppCustomerId.Create("447700900123"),
        body,
        Now,
        Now);
}
