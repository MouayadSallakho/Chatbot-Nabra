using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OmniAI.Api.IntegrationTests.Authorization;
using OmniAI.Api.IntegrationTests.WebChat;

namespace OmniAI.Api.IntegrationTests.Customers;

/// <summary>
/// Who may read the customer directory, who may change it, and what it refuses
/// to disclose.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Every member here is a real restricted member.</strong> The roles are
/// ordinary custom roles of the shape the roles endpoint produces, so what these
/// tests exercise is the real authorization path rather than a bypass the
/// application knows about. No testing shortcut was added.
/// </para>
/// <para>
/// The customers these tests read were created by real product flows — a website
/// visitor starting a chat — rather than seeded, so the attribution they assert
/// is the attribution the runtime actually performs.
/// </para>
/// </remarks>
public sealed class CustomerApiTests(RoleApiFixture fixture) : IClassFixture<RoleApiFixture>
{
    private const string View = "customers.view";
    private const string Manage = "customers.manage";
    private const string Conversations = "conversations.view";

    // ------------------------------------------------------- reading

    [Fact]
    public async Task An_anonymous_caller_is_refused()
    {
        (_, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        HttpClient anonymous = fixture.CreateClient();

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync(Directory(tenantId))).StatusCode);
    }

    [Fact]
    public async Task A_member_of_another_workspace_is_told_nothing_exists()
    {
        // Not 403. A workspace id is not a secret, but whether it exists must not
        // be learnable by a stranger probing ids.
        (_, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (HttpClient stranger, _, _) = await fixture.CreateOwnerAsync();

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await stranger.GetAsync(Directory(tenantId))).StatusCode);
    }

    [Fact]
    public async Task A_member_without_the_permission_is_refused()
    {
        (_, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        HttpClient member = await fixture.CreateRestrictedMemberAsync(tenantId, Conversations);

        // 403, not 404: they belong here, so the workspace is not a secret from
        // them -- they simply may not read this. Holding conversations.view is not
        // holding customers.view, and nothing on this platform implies anything
        // else.
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await member.GetAsync(Directory(tenantId))).StatusCode);
    }

    [Fact]
    public async Task A_member_with_the_permission_reads_the_directory()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        await StartWebChatAsync(owner, tenantId);

        HttpClient member = await fixture.CreateRestrictedMemberAsync(tenantId, View);

        JsonElement page = await ReadAsync(member, Directory(tenantId));

        Assert.Equal(1, page.GetProperty("totalCount").GetInt32());

        JsonElement row = page.GetProperty("items")[0];

        // An anonymous visitor has no name, and the platform does not invent one.
        Assert.True(row.GetProperty("displayName").ValueKind == JsonValueKind.Null);
        Assert.Equal("Anonymous visitor", row.GetProperty("label").GetString());
        Assert.Equal(1, row.GetProperty("conversationCount").GetInt32());
        Assert.Equal("WebChat", row.GetProperty("channels")[0].GetString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task An_impossible_page_is_refused_rather_than_guessed(int page)
    {
        (_, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        HttpClient member = await fixture.CreateRestrictedMemberAsync(tenantId, View);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await member.GetAsync(
                WebChatApiFixture.Relative(
                    $"/api/tenants/{tenantId}/customers?page={page}"))).StatusCode);
    }

    [Fact]
    public async Task A_page_larger_than_the_ceiling_is_refused()
    {
        // The directory is the screen somebody is tempted to ask for all of.
        (_, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        HttpClient member = await fixture.CreateRestrictedMemberAsync(tenantId, View);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await member.GetAsync(
                WebChatApiFixture.Relative(
                    $"/api/tenants/{tenantId}/customers?pageSize=101"))).StatusCode);
    }

    // ------------------------------------ the conversation permission boundary

    [Fact]
    public async Task Customer_detail_carries_no_conversation_history_without_the_conversation_permission()
    {
        // The claim the whole design turns on: a customer directory must not be a
        // way around the permission that guards transcripts.
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        await StartWebChatAsync(owner, tenantId);

        HttpClient member = await fixture.CreateRestrictedMemberAsync(tenantId, View);

        Guid customerId = await FirstCustomerAsync(member, tenantId);

        JsonElement customer = await ReadAsync(member, Customer(tenantId, customerId));

        Assert.False(customer.GetProperty("canReadConversations").GetBoolean());
        Assert.Empty(customer.GetProperty("conversations").EnumerateArray());

        // The counts are still reported. Knowing somebody has one conversation is
        // not being able to read it, and withholding it would make the directory
        // useless to whoever administers customers without reading mail.
        Assert.Equal(1, customer.GetProperty("conversationCount").GetInt32());
    }

    [Fact]
    public async Task Customer_detail_carries_conversation_metadata_with_both_permissions()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        await StartWebChatAsync(owner, tenantId);

        HttpClient member = await fixture.CreateRestrictedMemberAsync(
            tenantId,
            View,
            Conversations);

        Guid customerId = await FirstCustomerAsync(member, tenantId);

        JsonElement customer = await ReadAsync(member, Customer(tenantId, customerId));

        Assert.True(customer.GetProperty("canReadConversations").GetBoolean());

        JsonElement conversation = Assert.Single(
            [.. customer.GetProperty("conversations").EnumerateArray()]);

        Assert.Equal("WebChat", conversation.GetProperty("channel").GetString());
        Assert.Equal("Open", conversation.GetProperty("status").GetString());

        // Metadata only. There is no field here that could carry what was said,
        // and the serialised body proves it rather than the shape implying it.
        Assert.DoesNotContain(
            "\"text\"",
            conversation.GetRawText(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task No_directory_response_carries_a_raw_channel_identifier_or_a_transcript()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        await StartWebChatAsync(owner, tenantId);

        HttpClient member = await fixture.CreateRestrictedMemberAsync(
            tenantId,
            View,
            Conversations);

        string directory = await (await member.GetAsync(Directory(tenantId))).Content
            .ReadAsStringAsync();

        Guid customerId = await FirstCustomerAsync(member, tenantId);

        string detail = await (await member.GetAsync(Customer(tenantId, customerId))).Content
            .ReadAsStringAsync();

        foreach (string body in (string[])[directory, detail])
        {
            // No session credential, no provider identifier, no employee address,
            // and no field that could carry what a customer said.
            Assert.DoesNotContain("\"text\"", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("accessToken", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("@", body, StringComparison.Ordinal);
        }
    }

    // ------------------------------------------------------------- mutations

    [Fact]
    public async Task Renaming_needs_the_manage_permission()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        await StartWebChatAsync(owner, tenantId);

        HttpClient reader = await fixture.CreateRestrictedMemberAsync(tenantId, View);

        Guid customerId = await FirstCustomerAsync(reader, tenantId);

        (HttpResponseMessage response, _) = await RenameAsync(
            reader,
            tenantId,
            customerId,
            "Dana",
            await EtagAsync(reader, tenantId, customerId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Renaming_without_a_version_is_refused_and_a_stale_one_loses()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        await StartWebChatAsync(owner, tenantId);

        Guid customerId = await FirstCustomerAsync(owner, tenantId);

        string first = await EtagAsync(owner, tenantId, customerId);

        // No If-Match at all: an unconditional overwrite of somebody else's edit.
        HttpResponseMessage unconditional = await owner.PutAsJsonAsync(
            Customer(tenantId, customerId).ToString() + "/display-name",
            new { displayName = "Dana" });

        Assert.Equal(HttpStatusCode.PreconditionRequired, unconditional.StatusCode);

        (HttpResponseMessage accepted, string second) =
            await RenameAsync(owner, tenantId, customerId, "Dana", first);

        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.NotEqual(first, second);

        // The version the first caller held is now stale.
        (HttpResponseMessage stale, _) =
            await RenameAsync(owner, tenantId, customerId, "Someone else", first);

        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
    }

    [Fact]
    public async Task A_name_is_stored_exactly_as_typed_and_never_interpreted()
    {
        // Escaping is the browser's job at render time. The API stores and returns
        // what somebody typed, so a test that the value survives is also the test
        // that nothing here silently rewrites it.
        const string Hostile = "<script>alert(1)</script>";

        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        await StartWebChatAsync(owner, tenantId);

        Guid customerId = await FirstCustomerAsync(owner, tenantId);

        (HttpResponseMessage response, _) = await RenameAsync(
            owner,
            tenantId,
            customerId,
            Hostile,
            await EtagAsync(owner, tenantId, customerId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        JsonElement customer = await ReadAsync(owner, Customer(tenantId, customerId));

        Assert.Equal(Hostile, customer.GetProperty("displayName").GetString());
        Assert.Equal(Hostile, customer.GetProperty("label").GetString());
    }

    [Fact]
    public async Task Merging_needs_the_manage_permission()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        Guid[] customers = await TwoCustomersAsync(owner, tenantId);

        HttpClient reader = await fixture.CreateRestrictedMemberAsync(tenantId, View);

        HttpResponseMessage response = await MergeAsync(
            reader,
            tenantId,
            customers[0],
            customers[1],
            await EtagAsync(reader, tenantId, customers[0]));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_merge_combines_the_two_and_hides_the_source_from_the_directory()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        Guid[] customers = await TwoCustomersAsync(owner, tenantId);

        HttpResponseMessage merged = await MergeAsync(
            owner,
            tenantId,
            customers[0],
            customers[1],
            await EtagAsync(owner, tenantId, customers[0]));

        Assert.Equal(HttpStatusCode.OK, merged.StatusCode);

        JsonElement result = JsonDocument.Parse(await merged.Content.ReadAsStringAsync())
            .RootElement;

        Assert.Equal(1, result.GetProperty("conversationsMoved").GetInt32());
        Assert.False(result.GetProperty("alreadyMerged").GetBoolean());

        // One row, not two: a directory that listed both halves of every merge
        // would undo the merge visually.
        JsonElement page = await ReadAsync(owner, Directory(tenantId));

        Assert.Equal(1, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(customers[1], page.GetProperty("items")[0].GetProperty("id").GetGuid());
        Assert.Equal(2, page.GetProperty("items")[0].GetProperty("conversationCount").GetInt32());

        // But the source still resolves, and says where its history went.
        JsonElement source = await ReadAsync(owner, Customer(tenantId, customers[0]));

        Assert.Equal(customers[1], source.GetProperty("mergedIntoCustomerId").GetGuid());
    }

    [Fact]
    public async Task Merging_into_a_customer_of_another_workspace_is_told_nothing_exists()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (HttpClient stranger, Guid otherTenantId, _) = await fixture.CreateOwnerAsync();

        Guid[] mine = await TwoCustomersAsync(owner, tenantId);

        await StartWebChatAsync(stranger, otherTenantId);

        Guid theirs = await FirstCustomerAsync(stranger, otherTenantId);

        HttpResponseMessage response = await MergeAsync(
            owner,
            tenantId,
            mine[0],
            theirs,
            await EtagAsync(owner, tenantId, mine[0]));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_repeated_merge_answers_done_rather_than_refusing()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        Guid[] customers = await TwoCustomersAsync(owner, tenantId);

        await MergeAsync(
            owner,
            tenantId,
            customers[0],
            customers[1],
            await EtagAsync(owner, tenantId, customers[0]));

        HttpResponseMessage again = await MergeAsync(
            owner,
            tenantId,
            customers[0],
            customers[1],
            await EtagAsync(owner, tenantId, customers[0]));

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);

        JsonElement result = JsonDocument.Parse(await again.Content.ReadAsStringAsync())
            .RootElement;

        Assert.True(result.GetProperty("alreadyMerged").GetBoolean());
        Assert.Equal(0, result.GetProperty("conversationsMoved").GetInt32());
    }

    [Fact]
    public async Task Renaming_a_merged_customer_answers_with_its_canonical_id()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        Guid[] customers = await TwoCustomersAsync(owner, tenantId);

        await MergeAsync(
            owner,
            tenantId,
            customers[0],
            customers[1],
            await EtagAsync(owner, tenantId, customers[0]));

        (HttpResponseMessage response, _) = await RenameAsync(
            owner,
            tenantId,
            customers[0],
            "Dana",
            await EtagAsync(owner, tenantId, customers[0]));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        JsonElement problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync())
            .RootElement;

        // The caller is handed the record to act on instead of a dead end.
        Assert.Equal(customers[1], problem.GetProperty("canonicalCustomerId").GetGuid());
    }

    // --------------------------------------------------------------- helpers

    private static Uri Directory(Guid tenantId) =>
        WebChatApiFixture.Relative($"/api/tenants/{tenantId}/customers");

    private static Uri Customer(Guid tenantId, Guid customerId) =>
        WebChatApiFixture.Relative($"/api/tenants/{tenantId}/customers/{customerId}");

    /// <summary>
    /// Starts a real public web chat, which is what creates a customer.
    /// </summary>
    /// <remarks>
    /// Through the real public endpoints, deliberately. A seeded customer would
    /// prove that the directory can read rows; this proves that the runtime
    /// creates the row, and creates exactly one.
    /// </remarks>
    [Fact]
    public async Task The_directory_labels_each_customer_as_the_inbox_does()
    {
        // Phase 38.7a (BA-1): labelCode and shortCode on the directory row and
        // the detail, equal to what the conversation reads give the same customer.
        // The English label stays, for older clients and exports.
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        Guid[] customers = await TwoCustomersAsync(owner, tenantId);
        Guid named = customers[0];
        Guid unnamed = customers[1];

        (HttpResponseMessage renamed, _) = await RenameAsync(
            owner, tenantId, named, "Layla", await EtagAsync(owner, tenantId, named));

        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);

        Dictionary<Guid, JsonElement> inbox = (await ReadAsync(owner, WebChatApiFixture.Relative(
                $"/api/tenants/{tenantId}/conversations?pageSize=50")))
            .GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("customer").Clone())
            .ToDictionary(customer => customer.GetProperty("id").GetGuid());

        Dictionary<Guid, JsonElement> rows = (await ReadAsync(owner, Directory(tenantId)))
            .GetProperty("items").EnumerateArray()
            .ToDictionary(item => item.GetProperty("id").GetGuid(), item => item.Clone());

        foreach ((Guid id, string code, string label) in new[]
        {
            (named, "Named", "Layla"),
            (unnamed, "AnonymousVisitor", "Anonymous visitor"),
        })
        {
            JsonElement detail = await ReadAsync(owner, Customer(tenantId, id));

            foreach (JsonElement read in new[] { rows[id], detail })
            {
                Assert.Equal(code, read.GetProperty("labelCode").GetString());
                Assert.Equal(label, read.GetProperty("label").GetString());
                Assert.Equal(inbox[id].GetProperty("labelCode").GetString(), read.GetProperty("labelCode").GetString());
                Assert.Equal(inbox[id].GetProperty("shortCode").GetString(), read.GetProperty("shortCode").GetString());
                Assert.Matches("^[0-9A-HJKMNP-TV-Z]{6}$", read.GetProperty("shortCode").GetString());
            }
        }

        // A reader with the directory permission only gets the same fields.
        HttpClient reader = await fixture.CreateRestrictedMemberAsync(tenantId, View);
        JsonElement restricted = (await ReadAsync(reader, Directory(tenantId)))
            .GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("id").GetGuid() == unnamed);

        Assert.Equal(rows[unnamed].GetProperty("shortCode").GetString(), restricted.GetProperty("shortCode").GetString());
    }

    private async Task StartWebChatAsync(HttpClient owner, Guid tenantId, string? name = null)
    {
        (string key, _) = await WebChatApiFixture.PublishChatAsync(
            owner,
            tenantId,
            name ?? $"Support {Guid.NewGuid():N}"[..20]);

        HttpClient visitor = await fixture.CreateVisitorAsync();

        HttpResponseMessage started = await visitor.PostAsync(
            WebChatApiFixture.Relative($"/api/public/webchat/{key}/conversations"),
            content: null);

        Assert.Equal(HttpStatusCode.OK, started.StatusCode);
    }

    /// <summary>Two customers in one workspace, each with one conversation.</summary>
    private async Task<Guid[]> TwoCustomersAsync(HttpClient owner, Guid tenantId)
    {
        await StartWebChatAsync(owner, tenantId);
        await StartWebChatAsync(owner, tenantId);

        JsonElement page = await ReadAsync(owner, Directory(tenantId));

        Assert.Equal(2, page.GetProperty("totalCount").GetInt32());

        return
        [
            .. page.GetProperty("items").EnumerateArray()
                .Select(item => item.GetProperty("id").GetGuid()),
        ];
    }

    private static async Task<Guid> FirstCustomerAsync(HttpClient client, Guid tenantId)
    {
        JsonElement page = await ReadAsync(client, Directory(tenantId));

        return page.GetProperty("items")[0].GetProperty("id").GetGuid();
    }

    private static async Task<string> EtagAsync(
        HttpClient client,
        Guid tenantId,
        Guid customerId)
    {
        HttpResponseMessage response = await client.GetAsync(Customer(tenantId, customerId));

        return response.Headers.ETag?.Tag ?? string.Empty;
    }

    private static async Task<(HttpResponseMessage Response, string ETag)> RenameAsync(
        HttpClient client,
        Guid tenantId,
        Guid customerId,
        string? displayName,
        string etag)
    {
        using HttpRequestMessage request = new(
            HttpMethod.Put,
            Customer(tenantId, customerId).ToString() + "/display-name")
        {
            Content = JsonContent.Create(new { displayName }),
        };

        request.Headers.TryAddWithoutValidation("If-Match", etag);

        HttpResponseMessage response = await client.SendAsync(request);

        return (response, response.Headers.ETag?.Tag ?? string.Empty);
    }

    private static async Task<HttpResponseMessage> MergeAsync(
        HttpClient client,
        Guid tenantId,
        Guid sourceId,
        Guid targetId,
        string etag)
    {
        using HttpRequestMessage request = new(
            HttpMethod.Post,
            Customer(tenantId, sourceId).ToString() + "/merge")
        {
            Content = JsonContent.Create(new { targetCustomerId = targetId }),
        };

        request.Headers.TryAddWithoutValidation("If-Match", etag);

        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> ReadAsync(HttpClient client, Uri uri)
    {
        HttpResponseMessage response = await client.GetAsync(uri);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }
}
