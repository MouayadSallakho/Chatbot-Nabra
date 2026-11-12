using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace OmniAI.Api.IntegrationTests.WhatsApp;

/// <summary>
/// A WhatsApp customer reaching a workspace, and a colleague answering them.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The whole path, end to end, entirely on this machine.</strong> A
/// signed webhook arrives, the durable inbox records it, a background worker
/// takes it through the ordinary conversation runtime, the conversation appears
/// in the shared admin inbox, a person takes it over and replies, and the reply
/// is queued and sent through a local stub that records it.
/// </para>
/// <para>
/// <strong>Nothing reaches Meta and nothing reaches an AI provider.</strong> The
/// signature is computed from an invented app secret; the sender is a local
/// recording stub; the AI provider is deliberately unconfigured, which is the
/// state this suite runs every other conversation test in.
/// </para>
/// </remarks>
public sealed class WhatsAppEndToEndApiTests(WhatsAppApiFixture fixture)
    : IClassFixture<WhatsAppApiFixture>
{
    private const string Customer = "447700900123";

    [Fact]
    public async Task An_inbound_message_becomes_a_whatsapp_conversation_a_colleague_can_answer()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        fixture.Stub.Reset();

        string number = WhatsAppApiFixture.Number();

        await WhatsAppApiFixture.ServingAsync(owner, tenantId, number, "End to end");

        HttpClient meta = fixture.CreateClient();

        // ---- the customer writes ------------------------------------------

        string inbound = Payload(number, "wamid.E2E.ONE==", "Do you deliver on Sundays?");

        Assert.Equal(
            HttpStatusCode.OK,
            (await WhatsAppApiFixture.PostWebhookAsync(
                meta,
                inbound,
                WhatsAppApiFixture.Sign(inbound))).StatusCode);

        // The webhook returns before any work is done, so the conversation
        // appears once the background worker has taken the message through the
        // runtime.
        Guid conversationId = await WaitForConversationAsync(owner, tenantId);

        // ---- what the shared inbox shows ----------------------------------

        JsonElement conversation = await ReadJsonAsync(
            owner,
            WebChat.WebChatApiFixture.Relative(
                $"/api/tenants/{tenantId}/conversations/{conversationId}"));

        Assert.Equal("WhatsApp", conversation.GetProperty("channel").GetString());

        JsonElement facts = await ReadJsonAsync(
            owner,
            WebChat.WebChatApiFixture.Relative(
                $"/api/tenants/{tenantId}/whatsapp/conversations/{conversationId}"));

        string reference = facts.GetProperty("customerReference").GetString()!;

        // Masked, and masked everywhere: a colleague can tell who they are
        // talking to, and a screenshot is not a phone number.
        Assert.EndsWith("0123", reference, StringComparison.Ordinal);
        Assert.DoesNotContain("447700900", reference, StringComparison.Ordinal);

        // The same masked number on the inbox row and the detail (Phase 37.4),
        // and the raw number nowhere in either response.
        HttpResponseMessage listed = await owner.GetAsync(
            WebChat.WebChatApiFixture.Relative($"/api/tenants/{tenantId}/conversations"));
        string listJson = await listed.Content.ReadAsStringAsync();
        JsonElement row = JsonDocument.Parse(listJson).RootElement.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("id").GetGuid() == conversationId);
        JsonElement customer = row.GetProperty("customer");

        Assert.Equal(reference, customer.GetProperty("reference").GetString());
        Assert.Equal("WhatsAppCustomer", customer.GetProperty("labelCode").GetString());
        Assert.Equal(6, customer.GetProperty("shortCode").GetString()!.Length);
        Assert.Equal(reference, conversation.GetProperty("customer").GetProperty("reference").GetString());
        Assert.DoesNotContain(Customer, listJson, StringComparison.Ordinal);
        Assert.DoesNotContain(Customer, conversation.GetRawText(), StringComparison.Ordinal);

        // The customer's words are in the transcript even though no AI provider
        // is configured on this host.
        Assert.Contains(
            "Do you deliver on Sundays?",
            await ReadTranscriptAsync(owner, tenantId, conversationId),
            StringComparison.Ordinal);

        // ---- a colleague takes over and answers ---------------------------

        await TakeOverAsync(owner, tenantId, conversationId);

        HttpResponseMessage replied = await owner.PostAsJsonAsync(
            WebChat.WebChatApiFixture.Relative(
                $"/api/tenants/{tenantId}/conversations/{conversationId}/messages"),
            new
            {
                clientMessageId = $"reply-{Guid.NewGuid():N}",
                text = "Yes, we deliver every day until 6pm.",
            });

        Assert.Equal(HttpStatusCode.OK, replied.StatusCode);

        // ---- exactly one send, through the local stub ---------------------

        RecordingMetaStub.Send send = await WaitForSendAsync();

        Assert.Equal(number, send.From);
        Assert.Equal(Customer, send.To);
        Assert.Equal("Yes, we deliver every day until 6pm.", send.Text);

        // The credential the worker decrypted is the one the administrator
        // stored, which is the round trip through AES-256-GCM working.
        Assert.Equal(WhatsAppApiFixture.AccessToken, send.AccessToken);

        // <strong>One reply is one send.</strong> The admin never calls Meta and
        // the worker never sends twice.
        Assert.Single(fixture.Stub.Sends);

        // ---- the provider reports on it -----------------------------------

        await StatusAsync(meta, number, send.ProviderMessageId, "sent");
        await StatusAsync(meta, number, send.ProviderMessageId, "delivered");
        await StatusAsync(meta, number, send.ProviderMessageId, "read");

        // A duplicate and one that arrives late. Both are ordinary provider
        // behaviour, and neither may move what a colleague is looking at.
        await StatusAsync(meta, number, send.ProviderMessageId, "read");
        await StatusAsync(meta, number, send.ProviderMessageId, "delivered");

        Assert.Equal(
            "Read",
            await WaitForDeliveryStatusAsync(owner, tenantId, conversationId, "Read"));

        // And a receipt created no transcript message: the transcript is the
        // customer's message and the colleague's reply, and nothing else.
        Assert.DoesNotContain(
            "read",
            await ReadTranscriptAsync(owner, tenantId, conversationId),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_second_message_from_the_same_customer_joins_the_same_conversation()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        string number = WhatsAppApiFixture.Number();

        await WhatsAppApiFixture.ServingAsync(owner, tenantId, number, "Same thread");

        HttpClient meta = fixture.CreateClient();

        await SendAsync(meta, number, "wamid.E2E.A==", "Hello");

        Guid first = await WaitForConversationAsync(owner, tenantId);

        await SendAsync(meta, number, "wamid.E2E.B==", "Are you there?");

        // A person messaging a business is one continuing conversation, not a
        // new thread per message.
        await WaitAsync(async () =>
            (await ReadTranscriptAsync(owner, tenantId, first))
                .Contains("Are you there?", StringComparison.Ordinal));

        Assert.Equal(first, await WaitForConversationAsync(owner, tenantId));
    }

    [Fact]
    public async Task The_directory_labels_a_whatsapp_customer_as_the_inbox_does()
    {
        // Phase 38.7a (BA-1): the directory's labelCode and shortCode are the
        // conversation reads', for a WhatsApp-only customer and, after a merge,
        // for one who has used two channels.
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        string number = WhatsAppApiFixture.Number();

        await WhatsAppApiFixture.ServingAsync(owner, tenantId, number, "Directory label");

        await SendAsync(fixture.CreateClient(), number, "wamid.E2E.LABEL==", "Hello");

        Guid conversationId = await WaitForConversationAsync(owner, tenantId);

        JsonElement inboxCustomer = (await ReadJsonAsync(
                owner,
                WebChat.WebChatApiFixture.Relative($"/api/tenants/{tenantId}/conversations/{conversationId}")))
            .GetProperty("customer");
        Guid customerId = inboxCustomer.GetProperty("id").GetGuid();

        JsonElement row = await DirectoryRowAsync(owner, tenantId, customerId);
        JsonElement detail = await ReadJsonAsync(
            owner,
            WebChat.WebChatApiFixture.Relative($"/api/tenants/{tenantId}/customers/{customerId}"));

        foreach (JsonElement read in new[] { row, detail })
        {
            Assert.Equal("WhatsAppCustomer", read.GetProperty("labelCode").GetString());
            Assert.Equal(inboxCustomer.GetProperty("labelCode").GetString(), read.GetProperty("labelCode").GetString());
            Assert.Equal(inboxCustomer.GetProperty("shortCode").GetString(), read.GetProperty("shortCode").GetString());
            Assert.Equal("WhatsApp customer", read.GetProperty("label").GetString());
        }

        // A website visitor of the same workspace, merged into the WhatsApp
        // customer: one record that has used two channels.
        (string key, _) = await WebChat.WebChatApiFixture.PublishChatAsync(owner, tenantId, "Website");
        HttpClient visitor = await fixture.CreateVisitorAsync();
        HttpResponseMessage started = await visitor.PostAsync(
            WebChat.WebChatApiFixture.Relative($"/api/public/webchat/{key}/conversations"),
            content: null);

        Assert.Equal(HttpStatusCode.OK, started.StatusCode);

        Guid websiteConversation = JsonDocument.Parse(await started.Content.ReadAsStringAsync())
            .RootElement.GetProperty("conversationId").GetGuid();
        Guid websiteCustomer = (await ReadJsonAsync(
                owner,
                WebChat.WebChatApiFixture.Relative($"/api/tenants/{tenantId}/conversations/{websiteConversation}")))
            .GetProperty("customer").GetProperty("id").GetGuid();

        HttpResponseMessage source = await owner.GetAsync(
            WebChat.WebChatApiFixture.Relative($"/api/tenants/{tenantId}/customers/{websiteCustomer}"));
        using HttpRequestMessage merge = new(
            HttpMethod.Post,
            WebChat.WebChatApiFixture.Relative($"/api/tenants/{tenantId}/customers/{websiteCustomer}/merge"))
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { targetCustomerId = customerId }),
        };

        merge.Headers.TryAddWithoutValidation("If-Match", source.Headers.ETag?.ToString());

        HttpResponseMessage merged = await owner.SendAsync(merge);

        Assert.Equal(HttpStatusCode.OK, merged.StatusCode);

        JsonElement both = await DirectoryRowAsync(owner, tenantId, customerId);
        JsonElement bothInbox = (await ReadJsonAsync(
                owner,
                WebChat.WebChatApiFixture.Relative($"/api/tenants/{tenantId}/conversations/{websiteConversation}")))
            .GetProperty("customer");

        Assert.Equal("UnnamedCustomer", both.GetProperty("labelCode").GetString());
        Assert.Equal(bothInbox.GetProperty("labelCode").GetString(), both.GetProperty("labelCode").GetString());
        Assert.Equal(bothInbox.GetProperty("shortCode").GetString(), both.GetProperty("shortCode").GetString());
    }

    private static async Task<JsonElement> DirectoryRowAsync(HttpClient owner, Guid tenantId, Guid customerId) =>
        (await ReadJsonAsync(
            owner,
            WebChat.WebChatApiFixture.Relative($"/api/tenants/{tenantId}/customers?pageSize=100")))
        .GetProperty("items").EnumerateArray()
        .Single(item => item.GetProperty("id").GetGuid() == customerId);

    private static async Task SendAsync(
        HttpClient meta,
        string number,
        string providerMessageId,
        string text)
    {
        string body = Payload(number, providerMessageId, text);

        Assert.Equal(
            HttpStatusCode.OK,
            (await WhatsAppApiFixture.PostWebhookAsync(meta, body, WhatsAppApiFixture.Sign(body)))
                .StatusCode);
    }

    private static async Task StatusAsync(
        HttpClient meta,
        string number,
        string providerMessageId,
        string status)
    {
        string body = StatusPayload(number, providerMessageId, status);

        Assert.Equal(
            HttpStatusCode.OK,
            (await WhatsAppApiFixture.PostWebhookAsync(meta, body, WhatsAppApiFixture.Sign(body)))
                .StatusCode);
    }

    private static async Task TakeOverAsync(HttpClient owner, Guid tenantId, Guid conversationId)
    {
        HttpResponseMessage read = await owner.GetAsync(
            WebChat.WebChatApiFixture.Relative(
                $"/api/tenants/{tenantId}/conversations/{conversationId}"));

        HttpResponseMessage taken = await WhatsAppApiFixture.PostAsync(
            owner,
            WebChat.WebChatApiFixture.Relative(
                $"/api/tenants/{tenantId}/conversations/{conversationId}/take-over"),
            read.Headers.ETag?.ToString());

        Assert.Equal(HttpStatusCode.OK, taken.StatusCode);
    }

    private async Task<RecordingMetaStub.Send> WaitForSendAsync()
    {
        await WaitAsync(() => Task.FromResult(fixture.Stub.Sends.Count > 0));

        return fixture.Stub.Sends[0];
    }

    private static async Task<Guid> WaitForConversationAsync(HttpClient owner, Guid tenantId)
    {
        Guid found = Guid.Empty;

        await WaitAsync(async () =>
        {
            JsonElement page = await ReadJsonAsync(
                owner,
                WebChat.WebChatApiFixture.Relative($"/api/tenants/{tenantId}/conversations"));

            foreach (JsonElement item in page.GetProperty("items").EnumerateArray())
            {
                if (item.GetProperty("channel").GetString() == "WhatsApp")
                {
                    found = item.GetProperty("id").GetGuid();

                    return true;
                }
            }

            return false;
        });

        return found;
    }

    private static async Task<string> WaitForDeliveryStatusAsync(
        HttpClient owner,
        Guid tenantId,
        Guid conversationId,
        string expected)
    {
        string seen = string.Empty;

        await WaitAsync(async () =>
        {
            JsonElement facts = await ReadJsonAsync(
                owner,
                WebChat.WebChatApiFixture.Relative(
                    $"/api/tenants/{tenantId}/whatsapp/conversations/{conversationId}"));

            foreach (JsonProperty entry in facts.GetProperty("deliveryStatuses").EnumerateObject())
            {
                seen = entry.Value.GetString() ?? string.Empty;

                if (seen == expected)
                {
                    return true;
                }
            }

            return false;
        });

        return seen;
    }

    private static async Task<string> ReadTranscriptAsync(
        HttpClient owner,
        Guid tenantId,
        Guid conversationId)
    {
        HttpResponseMessage response = await owner.GetAsync(
            WebChat.WebChatApiFixture.Relative(
                $"/api/tenants/{tenantId}/conversations/{conversationId}/messages"));

        return await response.Content.ReadAsStringAsync();
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpClient client, Uri address)
    {
        HttpResponseMessage response = await client.GetAsync(address);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using JsonDocument document =
            JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.Clone();
    }

    /// <summary>
    /// Waits for something a background worker will do.
    /// </summary>
    /// <remarks>
    /// Polling with a ceiling rather than a fixed sleep: the workers poll on
    /// their own schedule, so a fixed wait would be either slow or flaky
    /// depending on where in their cycle the test landed.
    /// </remarks>
    private static async Task WaitAsync(Func<Task<bool>> condition)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(30);

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(200);
        }

        Assert.Fail("The background workers did not reach the expected state in time.");
    }

    private static string Payload(string phoneNumberId, string providerMessageId, string text) =>
        """
        {"object":"whatsapp_business_account","entry":[{"id":"111222333444555",
        "changes":[{"field":"messages","value":{"messaging_product":"whatsapp",
        "metadata":{"display_phone_number":"+44 7700 900000","phone_number_id":"NUMBER"},
        "contacts":[{"profile":{"name":"A Customer"},"wa_id":"CUSTOMER"}],
        "messages":[{"from":"CUSTOMER","id":"MESSAGE",
        "timestamp":"WHEN","text":{"body":"TEXT"},"type":"text"}]}}]}]}
        """
            .Replace("NUMBER", phoneNumberId, StringComparison.Ordinal)
            .Replace("CUSTOMER", Customer, StringComparison.Ordinal)
            .Replace("MESSAGE", providerMessageId, StringComparison.Ordinal)
            .Replace("WHEN", Timestamp(), StringComparison.Ordinal)
            .Replace("TEXT", text, StringComparison.Ordinal);

    private static string StatusPayload(
        string phoneNumberId,
        string providerMessageId,
        string status) =>
        """
        {"object":"whatsapp_business_account","entry":[{"id":"111222333444555",
        "changes":[{"field":"messages","value":{"messaging_product":"whatsapp",
        "metadata":{"display_phone_number":"+44 7700 900000","phone_number_id":"NUMBER"},
        "statuses":[{"id":"MESSAGE","status":"STATUS","timestamp":"WHEN",
        "recipient_id":"CUSTOMER"}]}}]}]}
        """
            .Replace("NUMBER", phoneNumberId, StringComparison.Ordinal)
            .Replace("CUSTOMER", Customer, StringComparison.Ordinal)
            .Replace("MESSAGE", providerMessageId, StringComparison.Ordinal)
            .Replace("STATUS", status, StringComparison.Ordinal)
            .Replace("WHEN", Timestamp(), StringComparison.Ordinal);

    private static string Timestamp() =>
        DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
}
