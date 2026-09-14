using System.Globalization;
using System.Net;
using System.Text.Json;

namespace OmniAI.Api.IntegrationTests.WhatsApp;

/// <summary>
/// The webhook route, over real HTTP.
/// </summary>
/// <remarks>
/// <strong>This is the platform's only anonymous write surface.</strong> Anybody
/// on the internet can POST here; the only thing separating them from a
/// workspace's inbox is an HMAC over the request body. Every test below is about
/// that separation holding.
/// <para>
/// No Meta account is used. The signature is computed in the test from an
/// invented app secret, which is exactly what Meta does with a real one.
/// </para>
/// </remarks>
public sealed class WhatsAppWebhookApiTests(WhatsAppApiFixture fixture)
    : IClassFixture<WhatsAppApiFixture>
{
    // ---- GET verification -------------------------------------------------

    [Fact]
    public async Task The_subscription_challenge_is_echoed_when_the_token_is_right()
    {
        HttpClient client = fixture.CreateClient();

        HttpResponseMessage response = await client.GetAsync(Verify(
            "subscribe",
            WhatsAppApiFixture.VerifyToken,
            "1158201444"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Meta compares the body byte for byte. A JSON wrapper or a trailing
        // newline fails the subscription with no useful diagnostic anywhere.
        Assert.Equal("1158201444", await response.Content.ReadAsStringAsync());
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_wrong_verify_token_is_refused_and_echoes_nothing()
    {
        HttpClient client = fixture.CreateClient();

        HttpResponseMessage response = await client.GetAsync(Verify(
            "subscribe",
            "not-the-token",
            "1158201444"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // The challenge must not come back. Echoing arbitrary text to an
        // unauthenticated caller is a reflection primitive, and it would also
        // make the route indistinguishable from a successful subscription.
        Assert.DoesNotContain(
            "1158201444",
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, "challenge")]
    [InlineData("unsubscribe", "challenge")]
    [InlineData("subscribe", null)]
    public async Task A_challenge_missing_any_required_field_is_refused(
        string? mode,
        string? challenge)
    {
        HttpClient client = fixture.CreateClient();

        HttpResponseMessage response = await client.GetAsync(
            Verify(mode, WhatsAppApiFixture.VerifyToken, challenge));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---- POST signature ---------------------------------------------------

    [Fact]
    public async Task A_correctly_signed_payload_is_accepted()
    {
        HttpClient client = fixture.CreateClient();

        string body = Payload(WhatsAppApiFixture.Number(), "wamid.SIGNED==");

        HttpResponseMessage response = await WhatsAppApiFixture.PostWebhookAsync(
            client,
            body,
            WhatsAppApiFixture.Sign(body));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_payload_with_no_signature_is_refused()
    {
        HttpClient client = fixture.CreateClient();

        string body = Payload(WhatsAppApiFixture.Number(), "wamid.UNSIGNED==");

        HttpResponseMessage response =
            await WhatsAppApiFixture.PostWebhookAsync(client, body, signature: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("sha256=")]
    [InlineData("sha256=zz")]
    [InlineData("sha1=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task A_malformed_signature_is_refused(string signature)
    {
        HttpClient client = fixture.CreateClient();

        string body = Payload(WhatsAppApiFixture.Number(), "wamid.MALFORMED==");

        HttpResponseMessage response =
            await WhatsAppApiFixture.PostWebhookAsync(client, body, signature);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_signature_over_a_different_body_is_refused()
    {
        HttpClient client = fixture.CreateClient();

        string signed = Payload(WhatsAppApiFixture.Number(), "wamid.ORIGINAL==");
        string sent = Payload(WhatsAppApiFixture.Number(), "wamid.SWAPPED==");

        HttpResponseMessage response = await WhatsAppApiFixture.PostWebhookAsync(
            client,
            sent,
            WhatsAppApiFixture.Sign(signed));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Changing_one_character_of_the_body_invalidates_the_signature()
    {
        HttpClient client = fixture.CreateClient();

        string body = Payload(WhatsAppApiFixture.Number(), "wamid.MUTATED==");
        string signature = WhatsAppApiFixture.Sign(body);

        // <strong>The reason the raw bytes are what gets verified.</strong> A
        // route that parsed the payload and re-serialised it before checking
        // would accept this: the documents mean nearly the same thing, and
        // "nearly the same thing" is not what an HMAC covers.
        string mutated = body.Replace("Hello there", "Hello there!", StringComparison.Ordinal);

        HttpResponseMessage response =
            await WhatsAppApiFixture.PostWebhookAsync(client, mutated, signature);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_unsigned_payload_creates_nothing_at_all()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        string number = WhatsAppApiFixture.Number();

        await WhatsAppApiFixture.ServingAsync(owner, tenantId, number, "Unsigned check");

        HttpClient anonymous = fixture.CreateClient();

        string body = Payload(number, "wamid.FORGED==");

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await WhatsAppApiFixture.PostWebhookAsync(anonymous, body, signature: null))
                .StatusCode);

        // No inbound row for it, and therefore no conversation, no AI and no
        // outbound work: the check happens before the payload is parsed, so
        // nothing downstream ever saw this request.
        Assert.Equal(0L, await CountAsync("whatsapp_inbound_messages", "wamid.FORGED=="));
    }

    [Fact]
    public async Task A_signed_payload_for_an_unknown_number_creates_nothing()
    {
        HttpClient client = fixture.CreateClient();

        string body = Payload("999888777666555", "wamid.NOBODY==");

        HttpResponseMessage response = await WhatsAppApiFixture.PostWebhookAsync(
            client,
            body,
            WhatsAppApiFixture.Sign(body));

        // Acknowledged, because it really is from Meta and answering no would
        // make it resend for hours. Nothing is created, because no workspace
        // claims that number.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0L, await CountAsync("whatsapp_inbound_messages", "wamid.NOBODY=="));
    }

    [Fact]
    public async Task A_signed_payload_for_a_connection_that_is_not_serving_creates_nothing()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        string number = WhatsAppApiFixture.Number();

        // Created, never given a credential, never activated.
        await WhatsAppApiFixture.ConnectAsync(owner, tenantId, number, "Draft check");

        HttpClient anonymous = fixture.CreateClient();

        string body = Payload(number, "wamid.DRAFT==");

        Assert.Equal(
            HttpStatusCode.OK,
            (await WhatsAppApiFixture.PostWebhookAsync(anonymous, body, WhatsAppApiFixture.Sign(body)))
                .StatusCode);

        Assert.Equal(0L, await CountAsync("whatsapp_inbound_messages", "wamid.DRAFT=="));
    }

    [Fact]
    public async Task A_redelivered_payload_stores_one_inbound_message()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        string number = WhatsAppApiFixture.Number();

        await WhatsAppApiFixture.ServingAsync(owner, tenantId, number, "Redelivery");

        HttpClient anonymous = fixture.CreateClient();

        string body = Payload(number, "wamid.TWICE==");
        string signature = WhatsAppApiFixture.Sign(body);

        await WhatsAppApiFixture.PostWebhookAsync(anonymous, body, signature);
        HttpResponseMessage second =
            await WhatsAppApiFixture.PostWebhookAsync(anonymous, body, signature);

        // Meta redelivers whenever it is unsure the first attempt landed. One
        // customer message must mean one stored message.
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(1L, await CountAsync("whatsapp_inbound_messages", "wamid.TWICE=="));
    }

    [Fact]
    public async Task An_unsupported_message_type_is_acknowledged_and_stored_without_text()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        string number = WhatsAppApiFixture.Number();

        await WhatsAppApiFixture.ServingAsync(owner, tenantId, number, "Image check");

        HttpClient anonymous = fixture.CreateClient();

        string body = """
            {"object":"whatsapp_business_account","entry":[{"id":"111222333444555",
            "changes":[{"field":"messages","value":{"messaging_product":"whatsapp",
            "metadata":{"display_phone_number":"+44 7700 900000","phone_number_id":"NUMBER"},
            "messages":[{"from":"447700900123","id":"wamid.PICTURE==","timestamp":"1789000000",
            "type":"image","image":{"id":"media-id","mime_type":"image/jpeg"}}]}}]}]}
            """.Replace("NUMBER", number, StringComparison.Ordinal);

        Assert.Equal(
            HttpStatusCode.OK,
            (await WhatsAppApiFixture.PostWebhookAsync(anonymous, body, WhatsAppApiFixture.Sign(body)))
                .StatusCode);

        // Stored so a redelivery is recognised, and with no text at all -- never
        // a placeholder that the AI could answer as though the customer typed it.
        Assert.Equal(1L, await CountAsync("whatsapp_inbound_messages", "wamid.PICTURE=="));

        Assert.Equal(
            0L,
            await fixture.Database.ExecuteScalarAsync(
                "SELECT count(*) FROM whatsapp_inbound_messages "
                + "WHERE provider_message_id = 'wamid.PICTURE==' AND text IS NOT NULL")
                as long? ?? -1L);
    }

    [Fact]
    public async Task A_body_that_is_signed_but_not_json_is_acknowledged_and_ignored()
    {
        HttpClient client = fixture.CreateClient();

        const string Body = "this is not json";

        HttpResponseMessage response = await WhatsAppApiFixture.PostWebhookAsync(
            client,
            Body,
            WhatsAppApiFixture.Sign(Body));

        // A valid signature says the bytes came from Meta, not that they contain
        // what this platform expects. Nothing to act on is not an error.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_webhook_needs_no_session_and_no_antiforgery_token()
    {
        // A bare client: no cookies, no sign-in, no antiforgery header. That is
        // the whole point of the route -- and the reason its authentication is an
        // HMAC rather than something a browser would carry.
        HttpClient anonymous = fixture.CreateClient();

        string body = Payload(WhatsAppApiFixture.Number(), "wamid.ANON==");

        HttpResponseMessage response = await WhatsAppApiFixture.PostWebhookAsync(
            anonymous,
            body,
            WhatsAppApiFixture.Sign(body));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<long> CountAsync(string table, string? providerMessageId = null)
    {
        string predicate = providerMessageId is null
            ? string.Empty
            : $" WHERE provider_message_id = '{providerMessageId}'";

        return await fixture.Database.ExecuteScalarAsync($"SELECT count(*) FROM {table}{predicate}")
            as long? ?? -1L;
    }

    private static Uri Verify(string? mode, string? token, string? challenge)
    {
        List<string> query = [];

        if (mode is not null)
        {
            query.Add($"hub.mode={Uri.EscapeDataString(mode)}");
        }

        if (token is not null)
        {
            query.Add($"hub.verify_token={Uri.EscapeDataString(token)}");
        }

        if (challenge is not null)
        {
            query.Add($"hub.challenge={Uri.EscapeDataString(challenge)}");
        }

        return new Uri(
            $"/api/whatsapp/webhook?{string.Join('&', query)}",
            UriKind.Relative);
    }

    /// <summary>One inbound text message, in Meta's documented envelope.</summary>
    private static string Payload(string phoneNumberId, string providerMessageId) =>
        """
        {"object":"whatsapp_business_account","entry":[{"id":"111222333444555",
        "changes":[{"field":"messages","value":{"messaging_product":"whatsapp",
        "metadata":{"display_phone_number":"+44 7700 900000","phone_number_id":"NUMBER"},
        "contacts":[{"profile":{"name":"A Customer"},"wa_id":"447700900123"}],
        "messages":[{"from":"447700900123","id":"MESSAGE",
        "timestamp":"WHEN","text":{"body":"Hello there"},"type":"text"}]}}]}]}
        """
            .Replace("NUMBER", phoneNumberId, StringComparison.Ordinal)
            .Replace("MESSAGE", providerMessageId, StringComparison.Ordinal)
            .Replace("WHEN", Timestamp(), StringComparison.Ordinal);

    private static string Timestamp() =>
        DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
}
