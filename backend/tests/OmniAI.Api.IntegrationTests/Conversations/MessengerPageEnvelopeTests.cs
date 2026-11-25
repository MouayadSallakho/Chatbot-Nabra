using System.Text.Json;
using OmniAI.Api.Conversations;
using OmniAI.Domain;

namespace OmniAI.Api.IntegrationTests.Conversations;

public sealed class MessengerPageEnvelopeTests
{
    private static readonly Guid Page = Guid.Parse("aba00000-0000-0000-0000-000000000001");
    private const string Fixture = """
        {"object":"page","entry":[
          {"id":"aba00000000000000000000000000001","messaging":[
            {"sender":{"id":"visitor"},"recipient":{"id":"aba00000000000000000000000000001"},
             "timestamp":1731933000123,"message":{"mid":"m.opaque/1+","text":"مرحباً"}},
            {"sender":{"id":"aba00000000000000000000000000001"},"recipient":{"id":"visitor"},
             "message":{"is_echo":true,"mid":"m.echo","text":"Reply"}},
            {"delivery":{"mids":["m.echo"]}},{"read":{"watermark":1731933000124}},
            {"message":{"mid":"m.photo","attachments":[{"type":"image"}]}}
          ]},
          {"id":"aba00000000000000000000000000001","messaging":[
            {"sender":{"id":"visitor"},"recipient":{"id":"aba00000000000000000000000000001"},
             "message":{"mid":"m.opaque/2+","text":"Second"}}
          ]}
        ]}
        """;

    [Fact]
    public void Page_fixture_preserves_order_and_opaque_ids_and_skips_non_customer_text()
    {
        var envelope = JsonSerializer.Deserialize<MessengerPageEnvelope>(Fixture, JsonSerializerOptions.Web)!;
        var texts = envelope.ReadTexts(Page);
        Assert.Collection(texts,
            first => { Assert.Equal("visitor", first.SenderId); Assert.Equal("m.opaque/1+", first.EventId); Assert.Equal("مرحباً", first.Text); },
            second => { Assert.Equal("m.opaque/2+", second.EventId); Assert.Equal("Second", second.Text); });
    }

    [Theory]
    [InlineData("""{"object":"instagram","entry":[]}""")]
    [InlineData("""{"object":"page","entry":[null]}""")]
    [InlineData("""{"object":"page","entry":[{"id":"other","messaging":[]}]}""")]
    [InlineData("""{"object":"page","entry":[{"id":"aba00000000000000000000000000001","messaging":[{"sender":{"id":"visitor"},"recipient":{"id":"other"},"message":{"mid":"m.1","text":"Hi"}}]}]}""")]
    public void Invalid_routes_are_refused_before_processing(string json)
    {
        var envelope = JsonSerializer.Deserialize<MessengerPageEnvelope>(json, JsonSerializerOptions.Web)!;
        var error = Assert.Throws<DomainValidationException>(() => envelope.ReadTexts(Page));
        Assert.Equal("messenger.simulator.invalid_page_event", error.Code);
    }
}
