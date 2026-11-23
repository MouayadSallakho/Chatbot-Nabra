using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OmniAI.Api.IntegrationTests.WebChat;

namespace OmniAI.Api.IntegrationTests.WhatsApp;

/// <summary>
/// Phase 39.1 (note 18): the customer directory's list and detail carry the
/// masked number the conversation reads already show, and never the number.
/// </summary>
public sealed class CustomerReferenceApiTests(WhatsAppApiFixture fixture)
    : IClassFixture<WhatsAppApiFixture>
{
    private const string FirstCustomer = "447700900123";
    private const string SecondCustomer = "447700900456";

    [Fact]
    public async Task A_whatsapp_customer_reads_the_conversation_s_masked_number_on_the_list_and_the_detail()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        string number = WhatsAppApiFixture.Number();
        await WhatsAppApiFixture.ServingAsync(owner, tenantId, number, "Reference");

        await InboundAsync(number, FirstCustomer, "wamid.REF.ONE==");
        Guid conversationId = (await WaitForWhatsAppConversationsAsync(owner, tenantId, 1))[0];

        JsonElement inboxCustomer = (await ReadAsync(owner, $"/api/tenants/{tenantId}/conversations/{conversationId}")).GetProperty("customer");
        string reference = inboxCustomer.GetProperty("reference").GetString()!;
        Guid customerId = inboxCustomer.GetProperty("id").GetGuid();

        Assert.Equal("••••0123", reference);
        string inbox = $"/api/tenants/{tenantId}/conversations";
        foreach (string key in new[] { reference, "0123", inboxCustomer.GetProperty("shortCode").GetString()!.ToLowerInvariant() })
        {
            JsonElement hits = await ReadAsync(owner, $"{inbox}?channel=WhatsApp&status=Open&unassigned=true&search={Uri.EscapeDataString(key)}");
            Assert.Equal(conversationId, Assert.Single(hits.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        }
        foreach (string query in new[] { $"search={FirstCustomer}", "search=44770090", "channel=WebChat", "status=Closed" })
        {
            Assert.Equal(0, (await ReadAsync(owner, $"{inbox}?{query}")).GetProperty("total").GetInt32());
        }

        (JsonElement row, string listJson) = await DirectoryRowAsync(owner, tenantId, customerId);
        Assert.Equal(reference, row.GetProperty("reference").GetString());

        HttpResponseMessage detailResponse = await owner.GetAsync(WebChatApiFixture.Relative($"/api/tenants/{tenantId}/customers/{customerId}"));
        string detailJson = await detailResponse.Content.ReadAsStringAsync();
        Assert.Equal(reference, JsonDocument.Parse(detailJson).RootElement.GetProperty("reference").GetString());

        // The number itself is in neither response, nor its first digits.
        foreach (string json in new[] { listJson, detailJson })
        {
            Assert.DoesNotContain(FirstCustomer, json, StringComparison.Ordinal);
            Assert.DoesNotContain("447700900", json, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_web_visitor_has_a_null_reference()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Website");

        HttpClient visitor = await fixture.CreateVisitorAsync(new IPAddress([203, 0, 113, 77]));
        HttpResponseMessage started = await visitor.PostAsync(WebChatApiFixture.Relative($"/api/public/webchat/{key}/conversations"), content: null);
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);

        Guid conversationId = JsonDocument.Parse(await started.Content.ReadAsStringAsync()).RootElement.GetProperty("conversationId").GetGuid();
        Guid customerId = (await ReadAsync(owner, $"/api/tenants/{tenantId}/conversations/{conversationId}"))
            .GetProperty("customer").GetProperty("id").GetGuid();

        (JsonElement row, _) = await DirectoryRowAsync(owner, tenantId, customerId);
        Assert.Equal(JsonValueKind.Null, row.GetProperty("reference").ValueKind);

        JsonElement detail = await ReadAsync(owner, $"/api/tenants/{tenantId}/customers/{customerId}");
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("reference").ValueKind);
    }

    [Fact]
    public async Task A_customer_with_two_numbers_shows_the_newest_one()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        string number = WhatsAppApiFixture.Number();
        await WhatsAppApiFixture.ServingAsync(owner, tenantId, number, "Two numbers");

        await InboundAsync(number, FirstCustomer, "wamid.REF.TWO.A==");
        Guid first = (await WaitForWhatsAppConversationsAsync(owner, tenantId, 1))[0];

        await InboundAsync(number, SecondCustomer, "wamid.REF.TWO.B==");
        Guid second = (await WaitForWhatsAppConversationsAsync(owner, tenantId, 2)).Single(id => id != first);

        Guid older = (await ReadAsync(owner, $"/api/tenants/{tenantId}/conversations/{first}")).GetProperty("customer").GetProperty("id").GetGuid();
        Guid newer = (await ReadAsync(owner, $"/api/tenants/{tenantId}/conversations/{second}")).GetProperty("customer").GetProperty("id").GetGuid();

        // The newer customer merged into the older: one record with both numbers,
        // the second one created last.
        HttpResponseMessage source = await owner.GetAsync(WebChatApiFixture.Relative($"/api/tenants/{tenantId}/customers/{newer}"));
        using HttpRequestMessage merge = new(HttpMethod.Post, WebChatApiFixture.Relative($"/api/tenants/{tenantId}/customers/{newer}/merge"))
        {
            Content = JsonContent.Create(new { targetCustomerId = older }),
        };
        merge.Headers.TryAddWithoutValidation("If-Match", source.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, (await owner.SendAsync(merge)).StatusCode);

        JsonElement detail = await ReadAsync(owner, $"/api/tenants/{tenantId}/customers/{older}");
        Assert.Equal(2, detail.GetProperty("identities").GetArrayLength());
        Assert.Equal("••••0456", detail.GetProperty("reference").GetString());

        (JsonElement row, string listJson) = await DirectoryRowAsync(owner, tenantId, older);
        Assert.Equal("••••0456", row.GetProperty("reference").GetString());
        Assert.DoesNotContain(SecondCustomer, listJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Another_workspace_never_reads_the_reference_and_the_permission_is_unchanged()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        string number = WhatsAppApiFixture.Number();
        await WhatsAppApiFixture.ServingAsync(owner, tenantId, number, "Isolation");

        await InboundAsync(number, FirstCustomer, "wamid.REF.ISO==");
        Guid conversationId = (await WaitForWhatsAppConversationsAsync(owner, tenantId, 1))[0];
        Guid customerId = (await ReadAsync(owner, $"/api/tenants/{tenantId}/conversations/{conversationId}"))
            .GetProperty("customer").GetProperty("id").GetGuid();

        (HttpClient rival, Guid rivalTenantId, _) = await fixture.CreateOwnerAsync("Rival Reference Trading");

        Assert.Equal(HttpStatusCode.NotFound, (await rival.GetAsync(WebChatApiFixture.Relative($"/api/tenants/{tenantId}/customers"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await rival.GetAsync(WebChatApiFixture.Relative($"/api/tenants/{tenantId}/customers/{customerId}"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await rival.GetAsync(WebChatApiFixture.Relative($"/api/tenants/{rivalTenantId}/customers/{customerId}"))).StatusCode);
        Assert.DoesNotContain("0123", await rival.GetStringAsync(WebChatApiFixture.Relative($"/api/tenants/{rivalTenantId}/customers?pageSize=100")), StringComparison.Ordinal);

        HttpClient inboxOnly = await fixture.CreateRestrictedMemberAsync(tenantId, "conversations.view");
        Assert.Equal(HttpStatusCode.Forbidden, (await inboxOnly.GetAsync(WebChatApiFixture.Relative($"/api/tenants/{tenantId}/customers"))).StatusCode);

        HttpClient viewer = await fixture.CreateRestrictedMemberAsync(tenantId, "customers.view");
        (JsonElement row, _) = await DirectoryRowAsync(viewer, tenantId, customerId);
        Assert.Equal("••••0123", row.GetProperty("reference").GetString());
    }

    // ---- helpers ----------------------------------------------------------------

    private async Task InboundAsync(string number, string customer, string providerMessageId)
    {
        string body = """
            {"object":"whatsapp_business_account","entry":[{"id":"111222333444555",
            "changes":[{"field":"messages","value":{"messaging_product":"whatsapp",
            "metadata":{"display_phone_number":"+44 7700 900000","phone_number_id":"NUMBER"},
            "contacts":[{"profile":{"name":"A Customer"},"wa_id":"CUSTOMER"}],
            "messages":[{"from":"CUSTOMER","id":"MESSAGE",
            "timestamp":"WHEN","text":{"body":"Hello"},"type":"text"}]}}]}]}
            """
            .Replace("NUMBER", number, StringComparison.Ordinal)
            .Replace("CUSTOMER", customer, StringComparison.Ordinal)
            .Replace("MESSAGE", providerMessageId, StringComparison.Ordinal)
            .Replace("WHEN", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

        Assert.Equal(
            HttpStatusCode.OK,
            (await WhatsAppApiFixture.PostWebhookAsync(fixture.CreateClient(), body, WhatsAppApiFixture.Sign(body))).StatusCode);
    }

    private static async Task<List<Guid>> WaitForWhatsAppConversationsAsync(HttpClient owner, Guid tenantId, int count)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(30);

        while (true)
        {
            JsonElement page = await ReadAsync(owner, $"/api/tenants/{tenantId}/conversations?pageSize=50");
            List<Guid> found =
            [
                .. page.GetProperty("items").EnumerateArray()
                    .Where(item => item.GetProperty("channel").GetString() == "WhatsApp")
                    .Select(item => item.GetProperty("id").GetGuid()),
            ];

            if (found.Count >= count)
            {
                return found;
            }

            Assert.True(DateTimeOffset.UtcNow < deadline, $"Only {found.Count} WhatsApp conversations appeared.");
            await Task.Delay(100);
        }
    }

    private static async Task<(JsonElement Row, string Json)> DirectoryRowAsync(HttpClient client, Guid tenantId, Guid customerId)
    {
        HttpResponseMessage response = await client.GetAsync(WebChatApiFixture.Relative($"/api/tenants/{tenantId}/customers?pageSize=100"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string json = await response.Content.ReadAsStringAsync();
        JsonElement row = JsonDocument.Parse(json).RootElement.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("id").GetGuid() == customerId)
            .Clone();

        return (row, json);
    }

    private static async Task<JsonElement> ReadAsync(HttpClient client, string path)
    {
        HttpResponseMessage response = await client.GetAsync(WebChatApiFixture.Relative(path));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }
}
