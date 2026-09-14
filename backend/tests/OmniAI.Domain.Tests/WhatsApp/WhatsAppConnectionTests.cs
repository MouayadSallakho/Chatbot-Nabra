using OmniAI.Domain;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.WhatsApp;

namespace OmniAI.Domain.Tests.WhatsApp;

/// <summary>What a WhatsApp connection refuses to become.</summary>
public sealed class WhatsAppConnectionTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_new_connection_is_a_draft_with_no_credential()
    {
        WhatsAppConnection connection = Connected();

        // Created switched off. A number that starts serving the moment it is
        // typed would answer customers from an agent nobody has reviewed.
        Assert.Equal(WhatsAppConnectionStatus.Draft, connection.Status);
        Assert.False(connection.HasCredential);
        Assert.False(connection.IsServing);
    }

    [Fact]
    public void It_cannot_be_put_into_service_without_a_credential()
    {
        WhatsAppConnection connection = Connected();

        // The failure this prevents is silent: a connection Meta delivers to and
        // that can never answer, which looks live in the admin and is a customer
        // talking to nobody.
        DomainStateException refused =
            Assert.Throws<DomainStateException>(() => connection.Activate(Now));

        Assert.Contains("access token", refused.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void It_serves_only_once_it_is_active_and_holds_a_credential()
    {
        WhatsAppConnection connection = Connected();

        connection.RecordCredentialStored(Now);

        Assert.False(connection.IsServing);

        connection.Activate(Now);

        Assert.True(connection.IsServing);
    }

    [Fact]
    public void A_live_connection_cannot_be_reconfigured_underneath_its_customers()
    {
        WhatsAppConnection connection = Connected();

        connection.RecordCredentialStored(Now);
        connection.Activate(Now);

        // Changing the number or the agent while messages are arriving would
        // reroute a conversation mid-sentence. Taking it out of service first is
        // a decision somebody makes, and this is what makes them make it.
        Assert.Throws<DomainStateException>(() => connection.Reconfigure(
            AiAgentId.New(),
            WabaId.Create("123456789012345"),
            PhoneNumberId.Create("987654321098765"),
            "+1 555 0100",
            Now));
    }

    [Fact]
    public void A_disabled_connection_can_be_reconfigured_and_put_back()
    {
        WhatsAppConnection connection = Connected();

        connection.RecordCredentialStored(Now);
        connection.Activate(Now);
        connection.Disable(Now);

        AiAgentId replacement = AiAgentId.New();

        connection.Reconfigure(
            replacement,
            WabaId.Create("123456789012345"),
            PhoneNumberId.Create("987654321098765"),
            "+1 555 0100",
            Now);

        Assert.Equal(replacement, connection.AiAgentId);

        // The credential survives reconfiguration, so a workspace correcting a
        // typo does not have to find its token again.
        connection.Activate(Now);

        Assert.True(connection.IsServing);
    }

    [Fact]
    public void The_aggregate_has_no_way_to_hold_an_access_token()
    {
        // The central security property, asserted structurally rather than by
        // reading the class. If a token cannot be stored on the aggregate, no
        // response built from the aggregate can leak one -- including responses
        // nobody has written yet.
        Assert.DoesNotContain(
            typeof(WhatsAppConnection).GetProperties(),
            property => property.Name.Contains("Token", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Secret", StringComparison.OrdinalIgnoreCase));
    }

    private static WhatsAppConnection Connected() => WhatsAppConnection.Create(
        TenantId.New(),
        AiAgentId.New(),
        WabaId.Create("111222333444555"),
        PhoneNumberId.Create("555444333222111"),
        "+1 555 0199",
        Now);
}
