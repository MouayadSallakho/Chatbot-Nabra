using System.Text.Json;
using OmniAI.Api.Conversations;
using OmniAI.Domain;

namespace OmniAI.Api.IntegrationTests.Conversations;

public sealed class InstagramDirectEnvelopeTests
{
    private static readonly Guid Agent = Guid.Parse("acdc0000-0000-0000-0000-000000000001");

    [Fact]
    public void Only_customer_text_becomes_a_turn_and_keeps_its_opaque_id()
    {
        const string fixture = """
            {"object":"instagram","entry":[{"id":"ig-account-acdc0000000000000000000000000001","messaging":[
              {"message":{"mid":"photo","attachments":[{"type":"image"}]}},
              {"message":{"mid":"removed","text":"old","is_deleted":true}},
              {"message":{"mid":"out","text":"reply","is_echo":true}},
              {"message":{"mid":"unknown","text":"unsupported","is_unsupported":true}},
              {"reaction":{"mid":"another","action":"react","emoji":"❤️"}},
              {"sender":{"id":"customer"},"recipient":{"id":"ig-account-acdc0000000000000000000000000001"},
               "message":{"mid":"ig.opaque/1+","text":"مرحباً"}}
            ]}]}
            """;
        var envelope = JsonSerializer.Deserialize<InstagramDirectEnvelope>(fixture, JsonSerializerOptions.Web)!;
        var dm = Assert.Single(envelope.ReadTexts(Agent));
        Assert.Equal("customer", dm.SenderId);
        Assert.Equal("ig.opaque/1+", dm.EventId);
        Assert.Equal("مرحباً", dm.Text);
    }

    [Theory]
    [InlineData("take_thread_control", "10002", true)]
    [InlineData("pass_thread_control", "10001", false)]
    public void Handover_uses_the_new_app_and_event_timestamp(string action, string app, bool external)
    {
        string json = $$$"""
            {"object":"instagram","entry":[{"id":"ig-account-acdc0000000000000000000000000001","changes":[
              {"field":"messaging_handover","value":{"sender":{"id":"visitor"},"recipient":{"id":"ig-account-acdc0000000000000000000000000001"},
               "timestamp":1731933000000,"{{{action}}}":{"new_owner_app_id":"{{{app}}}"}
              }}]}]}
            """;
        var envelope = JsonSerializer.Deserialize<InstagramHandoverEnvelope>(json, JsonSerializerOptions.Web)!;
        var notice = envelope.Read(Agent);
        Assert.Equal("visitor", notice.SenderId);
        Assert.Equal(1731933000000, notice.Timestamp);
        Assert.Equal(external, notice.ExternalOwner);
        foreach (var invalid in new[] { json.Replace("messaging_handover", "messages", StringComparison.Ordinal),
            json.Replace("1731933000000", "0", StringComparison.Ordinal),
            json.Replace(action, "request_thread_control", StringComparison.Ordinal) })
        {
            var wrong = JsonSerializer.Deserialize<InstagramHandoverEnvelope>(invalid, JsonSerializerOptions.Web)!;
            var error = Assert.Throws<DomainValidationException>(() => wrong.Read(Agent));
            Assert.Equal("instagram.simulator.invalid_handover", error.Code);
        }
    }

    [Theory]
    [InlineData("""{"object":"page","entry":[]}""")]
    [InlineData("""{"object":"instagram","entry":[null]}""")]
    [InlineData("""{"object":"instagram","entry":[{"id":"another-account","messaging":[]}]}""")]
    public void Invalid_professional_account_events_are_refused(string fixture)
    {
        var envelope = JsonSerializer.Deserialize<InstagramDirectEnvelope>(fixture, JsonSerializerOptions.Web)!;
        var error = Assert.Throws<DomainValidationException>(() => envelope.ReadTexts(Agent));
        Assert.Equal("instagram.simulator.invalid_event", error.Code);
    }
}
