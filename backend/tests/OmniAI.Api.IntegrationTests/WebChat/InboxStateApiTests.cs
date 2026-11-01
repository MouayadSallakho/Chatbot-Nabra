using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace OmniAI.Api.IntegrationTests.WebChat;

/// <summary>
/// Phase 37.4 over HTTP: who a conversation is with, newest-first transcripts,
/// what happened in a transcript, "needs a person", and unread state.
/// </summary>
/// <remarks>
/// Every conversation here was produced by a visitor talking to the real public
/// API, and every failure by the recording provider refusing, so what the admin
/// reads is what the runtime actually stored.
/// </remarks>
public sealed class InboxStateApiTests(WebChatApiFixture fixture)
    : IClassFixture<WebChatApiFixture>
{
    private const string ChatRoot = "/api/public/webchat";
    private const string Crockford = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    private static readonly string[] TakenOverThenReturned = ["TakenOver", "ReturnedToAi"];

    // Each visitor gets its own network (documentation range), so this class's
    // conversations do not add up against one client's public-chat limits.
    private static int _visitors;

    // ---- Who the customer is ----------------------------------------------

    [Fact]
    public async Task A_row_names_an_unnamed_visitor_by_code_and_a_named_one_by_name()
    {
        (HttpClient owner, Guid tenantId, string key) = await WorkspaceAsync();
        Guid conversationId = (await StartAsync(key, "Where is my order?")).ConversationId;

        JsonElement row = await RowAsync(owner, tenantId, conversationId);
        JsonElement customer = row.GetProperty("customer");

        Assert.Equal(JsonValueKind.Null, customer.GetProperty("displayName").ValueKind);
        Assert.Equal("AnonymousVisitor", customer.GetProperty("labelCode").GetString());
        Assert.Equal(JsonValueKind.Null, customer.GetProperty("reference").ValueKind);

        string shortCode = customer.GetProperty("shortCode").GetString()!;

        Assert.Equal(6, shortCode.Length);
        Assert.All(shortCode, character => Assert.Contains(character, Crockford));

        // The same code on the detail, and the same customer.
        JsonElement detail = await ReadJsonAsync(owner, Conversation(tenantId, conversationId));

        Assert.Equal(shortCode, detail.GetProperty("customer").GetProperty("shortCode").GetString());

        Guid customerId = customer.GetProperty("id").GetGuid();

        await RenameAsync(owner, tenantId, customerId, "Layla Haddad");

        customer = (await RowAsync(owner, tenantId, conversationId)).GetProperty("customer");

        Assert.Equal("Layla Haddad", customer.GetProperty("displayName").GetString());
        Assert.Equal("Named", customer.GetProperty("labelCode").GetString());
        Assert.Equal(shortCode, customer.GetProperty("shortCode").GetString());
    }

    [Fact]
    public async Task Two_visitors_get_different_codes()
    {
        (HttpClient owner, Guid tenantId, string key) = await WorkspaceAsync();
        Guid first = (await StartAsync(key, "Hello")).ConversationId;
        Guid second = (await StartAsync(key, "Hello again")).ConversationId;

        string firstCode = (await RowAsync(owner, tenantId, first)).GetProperty("customer").GetProperty("shortCode").GetString()!;
        string secondCode = (await RowAsync(owner, tenantId, second)).GetProperty("customer").GetProperty("shortCode").GetString()!;

        Assert.NotEqual(firstCode, secondCode);
    }

    [Fact]
    public async Task A_merged_customer_shows_the_canonical_record()
    {
        (HttpClient owner, Guid tenantId, string key) = await WorkspaceAsync();
        Guid sourceConversation = (await StartAsync(key, "First visit")).ConversationId;
        Guid targetConversation = (await StartAsync(key, "Second visit")).ConversationId;

        Guid source = (await RowAsync(owner, tenantId, sourceConversation)).GetProperty("customer").GetProperty("id").GetGuid();
        Guid target = (await RowAsync(owner, tenantId, targetConversation)).GetProperty("customer").GetProperty("id").GetGuid();

        await RenameAsync(owner, tenantId, target, "Omar");

        HttpResponseMessage sourceRead = await owner.GetAsync(Customer(tenantId, source));
        using HttpRequestMessage merge = new(HttpMethod.Post, new Uri($"{Customer(tenantId, source)}/merge", UriKind.Relative))
        {
            Content = JsonContent.Create(new { targetCustomerId = target }),
        };
        merge.Headers.TryAddWithoutValidation("If-Match", sourceRead.Headers.ETag!.ToString());

        Assert.Equal(HttpStatusCode.OK, (await owner.SendAsync(merge)).StatusCode);

        JsonElement moved = (await RowAsync(owner, tenantId, sourceConversation)).GetProperty("customer");

        Assert.Equal(target, moved.GetProperty("id").GetGuid());
        Assert.Equal("Omar", moved.GetProperty("displayName").GetString());
    }

    // ---- Newest page first ------------------------------------------------

    [Fact]
    public async Task A_transcript_reads_newest_first_and_pages_back_to_the_start()
    {
        (HttpClient owner, Guid tenantId, string key) = await WorkspaceAsync();
        (Guid conversationId, HttpClient visitor) = await StartAsync(key, "message 1");

        for (int index = 2; index <= 6; index++)
        {
            await SendAsync(visitor, conversationId, $"message {index}");
        }

        // Twelve messages: six questions, six answers.
        List<long> ascending = [.. (await ReadJsonAsync(owner, Messages(tenantId, conversationId, "limit=200")))
            .GetProperty("items").EnumerateArray().Select(item => item.GetProperty("sequence").GetInt64())];

        Assert.Equal(12, ascending.Count);

        JsonElement tail = await ReadJsonAsync(owner, Messages(tenantId, conversationId, "tail=true&limit=5"));
        List<long> collected = Sequences(tail);

        // Ascending inside the page, the newest five, nothing later.
        Assert.Equal(ascending[^5..], collected);
        Assert.Equal(JsonValueKind.Null, tail.GetProperty("nextCursor").ValueKind);

        JsonElement page = tail;

        while (page.GetProperty("previousCursor").ValueKind != JsonValueKind.Null)
        {
            long before = page.GetProperty("previousCursor").GetInt64();

            page = await ReadJsonAsync(owner, Messages(tenantId, conversationId, $"before={before}&limit=5"));

            List<long> earlier = Sequences(page);

            Assert.True(earlier.Count > 0);
            Assert.All(earlier, sequence => Assert.True(sequence < before));
            Assert.Equal(earlier[^1], page.GetProperty("nextCursor").GetInt64());

            collected.InsertRange(0, earlier);
        }

        // Every message exactly once, in ingestion order: the same as reading forwards.
        Assert.Equal(ascending, collected);
    }

    [Fact]
    public async Task The_forward_read_is_unchanged_and_says_what_is_earlier()
    {
        (HttpClient owner, Guid tenantId, string key) = await WorkspaceAsync();
        (Guid conversationId, HttpClient visitor) = await StartAsync(key, "one");
        await SendAsync(visitor, conversationId, "two");

        JsonElement first = await ReadJsonAsync(owner, Messages(tenantId, conversationId, "limit=2"));

        Assert.Equal(2, first.GetProperty("items").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, first.GetProperty("previousCursor").ValueKind);
        Assert.False(first.TryGetProperty("events", out _), "events appeared without include=events.");

        long after = first.GetProperty("nextCursor").GetInt64();
        JsonElement second = await ReadJsonAsync(owner, Messages(tenantId, conversationId, $"after={after}&limit=2"));

        Assert.Equal(Sequences(second)[0], second.GetProperty("previousCursor").GetInt64());
    }

    [Theory]
    [InlineData("after=1&tail=true", "transcript.cursor.conflict")]
    [InlineData("after=1&before=9", "transcript.cursor.conflict")]
    [InlineData("include=notes", "transcript.include.invalid")]
    public async Task Contradictory_or_unknown_transcript_parameters_are_refused_with_a_code(
        string query,
        string code)
    {
        (HttpClient owner, Guid tenantId, string key) = await WorkspaceAsync();
        Guid conversationId = (await StartAsync(key, "Hello")).ConversationId;

        HttpResponseMessage response = await owner.GetAsync(Messages(tenantId, conversationId, query));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, await CodeAsync(response));
    }

    [Fact]
    public async Task An_empty_conversation_reads_as_an_empty_newest_page()
    {
        (HttpClient owner, Guid tenantId, string key) = await WorkspaceAsync();
        HttpClient visitor = await NewVisitorAsync();
        HttpResponseMessage started = await visitor.PostAsync(
            WebChatApiFixture.Relative($"{ChatRoot}/{key}/conversations"),
            content: null);
        Guid conversationId = (await started.Content.ReadFromJsonAsync<Started>())!.ConversationId;

        JsonElement tail = await ReadJsonAsync(owner, Messages(tenantId, conversationId, "tail=true&include=events"));

        Assert.Equal(0, tail.GetProperty("items").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, tail.GetProperty("previousCursor").ValueKind);
        Assert.Equal(JsonValueKind.Null, tail.GetProperty("nextCursor").ValueKind);
        Assert.Equal(0, tail.GetProperty("events").GetArrayLength());
    }

    // ---- What happened in the transcript ----------------------------------

    [Fact]
    public async Task A_failed_turn_is_marked_once_after_its_customer_message()
    {
        (HttpClient owner, Guid tenantId, string key) = await WorkspaceAsync();
        (Guid conversationId, HttpClient visitor) = await StartAsync(key, "Hello");

        fixture.Generator.PretendUnavailable = true;
        await SendAsync(visitor, conversationId, "Is anybody there?", expectFailure: true);
        fixture.Generator.PretendUnavailable = false;

        // A later answer does not hide the earlier failure.
        await SendAsync(visitor, conversationId, "Hello again");

        JsonElement page = await ReadJsonAsync(owner, Messages(tenantId, conversationId, "include=events"));
        JsonElement failed = Assert.Single(page.GetProperty("events").EnumerateArray());

        long customerMessage = page.GetProperty("items").EnumerateArray()
            .First(item => item.GetProperty("text").GetString() == "Is anybody there?")
            .GetProperty("sequence").GetInt64();

        Assert.Equal("AiTurnFailed", failed.GetProperty("kind").GetString());
        Assert.Equal(customerMessage, failed.GetProperty("afterSequence").GetInt64());
        Assert.Equal("generation_failed", failed.GetProperty("reason").GetString());
        Assert.Equal(JsonValueKind.Null, failed.GetProperty("actor").ValueKind);

        // The provider's own words are nowhere in it.
        Assert.DoesNotContain("unavailable", failed.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Takeover_and_return_are_events_with_who_and_where_and_never_public()
    {
        (HttpClient owner, Guid tenantId, string key) = await WorkspaceAsync();
        (Guid conversationId, HttpClient visitor) = await StartAsync(key, "Hello");

        string version = await HandoffAsync(owner, tenantId, conversationId, "take-over", await VersionAsync(owner, tenantId, conversationId));

        // Pressed again: nothing changed, so nothing is recorded.
        await HandoffAsync(owner, tenantId, conversationId, "take-over", version);

        // A message while a person handles it: a turn without a reply, which is
        // not a failure.
        await SendAsync(visitor, conversationId, "Thanks, waiting for a person");

        await HandoffAsync(owner, tenantId, conversationId, "return-to-ai", await VersionAsync(owner, tenantId, conversationId));

        JsonElement page = await ReadJsonAsync(owner, Messages(tenantId, conversationId, "tail=true&include=events"));
        List<JsonElement> events = [.. page.GetProperty("events").EnumerateArray()];

        Assert.Equal(TakenOverThenReturned, events.Select(entry => entry.GetProperty("kind").GetString()));

        List<long> sequences = Sequences(page);

        // Taken over after the AI's answer (the second message); returned after
        // the customer's message that arrived while a person handled it.
        Assert.Equal(sequences[1], events[0].GetProperty("afterSequence").GetInt64());
        Assert.Equal(sequences[2], events[1].GetProperty("afterSequence").GetInt64());
        Assert.All(events, entry => Assert.Contains("@", entry.GetProperty("actor").GetProperty("email").GetString()!, StringComparison.Ordinal));

        // The public transcript has no events, whatever it is asked for.
        string visitorTranscript = await visitor.GetStringAsync(WebChatApiFixture.Relative(
            $"{ChatRoot}/conversations/{conversationId}/messages?include=events"));

        Assert.DoesNotContain("TakenOver", visitorTranscript, StringComparison.Ordinal);
        Assert.DoesNotContain("\"events\"", visitorTranscript, StringComparison.Ordinal);
        Assert.DoesNotContain("@", visitorTranscript, StringComparison.Ordinal);
    }

    // ---- Needs a person -----------------------------------------------------

    [Fact]
    public async Task Needs_a_person_lists_failed_turns_and_handed_over_conversations_the_customer_wrote_last()
    {
        (HttpClient owner, Guid tenantId, string key) = await WorkspaceAsync();

        // Answered by the AI: not waiting on anybody.
        Guid answered = (await StartAsync(key, "Opening hours?")).ConversationId;

        // The AI's turn failed: waiting.
        (Guid failed, HttpClient failedVisitor) = await StartAsync(key, "Hello");
        fixture.Generator.PretendUnavailable = true;
        await SendAsync(failedVisitor, failed, "Refund please", expectFailure: true);
        fixture.Generator.PretendUnavailable = false;

        // A person handles it and the customer wrote last: waiting.
        (Guid waiting, HttpClient waitingVisitor) = await StartAsync(key, "Hello");
        await HandoffAsync(owner, tenantId, waiting, "take-over", await VersionAsync(owner, tenantId, waiting));
        await SendAsync(waitingVisitor, waiting, "Still there?");

        // A person handles it and answered last: not waiting.
        (Guid replied, HttpClient repliedVisitor) = await StartAsync(key, "Hello");
        await HandoffAsync(owner, tenantId, replied, "take-over", await VersionAsync(owner, tenantId, replied));
        await SendAsync(repliedVisitor, replied, "Question");
        await ReplyAsync(owner, tenantId, replied, "Answered.");

        JsonElement list = await ReadJsonAsync(owner, WebChatApiFixture.Relative(
            $"/api/tenants/{tenantId}/conversations?filter=needs-person"));

        HashSet<Guid> ids = [.. list.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid())];

        Assert.True(ids.SetEquals([failed, waiting]), "needs-person returned the wrong conversations.");
        Assert.Equal(2, list.GetProperty("total").GetInt32());
        Assert.DoesNotContain(answered, ids);
        Assert.DoesNotContain(replied, ids);

        // Paged like the full list.
        JsonElement firstPage = await ReadJsonAsync(owner, WebChatApiFixture.Relative(
            $"/api/tenants/{tenantId}/conversations?filter=needs-person&pageSize=1"));

        Assert.Single(firstPage.GetProperty("items").EnumerateArray());
        Assert.Equal(2, firstPage.GetProperty("total").GetInt32());

        // Without the filter, everything, exactly as before.
        JsonElement all = await ReadJsonAsync(owner, WebChatApiFixture.Relative($"/api/tenants/{tenantId}/conversations"));

        Assert.Equal(4, all.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task A_message_left_unanswered_by_a_return_to_the_ai_needs_a_person_until_somebody_answers()
    {
        // Revision 1: the customer wrote while a person handled it, and it was
        // handed back to the AI before anybody replied. The AI never answers
        // retroactively, so without this the message would wait for ever.
        (HttpClient owner, Guid tenantId, string key) = await WorkspaceAsync();

        // The categories of the first definition, unchanged.
        Guid answered = (await StartAsync(key, "Opening hours?")).ConversationId;
        (Guid failed, HttpClient failedVisitor) = await StartAsync(key, "Hello");
        fixture.Generator.PretendUnavailable = true;
        await SendAsync(failedVisitor, failed, "Refund please", expectFailure: true);
        fixture.Generator.PretendUnavailable = false;

        // Two gap conversations: one the AI will answer, one a person will.
        (Guid byAi, HttpClient byAiVisitor) = await LeftUnansweredAsync(owner, tenantId, key);
        (Guid byPerson, _) = await LeftUnansweredAsync(owner, tenantId, key);

        Assert.True((await NeedsPersonAsync(owner, tenantId)).SetEquals([failed, byAi, byPerson]));
        Assert.Equal(3, await NeedsPersonTotalAsync(owner, tenantId));

        // The customer writes again and the AI answers: no longer waiting.
        await SendAsync(byAiVisitor, byAi, "Are you still there?");

        // A person takes it back (still waiting, now on the first branch), then replies.
        await HandoffAsync(owner, tenantId, byPerson, "take-over", await VersionAsync(owner, tenantId, byPerson));
        Assert.Contains(byPerson, await NeedsPersonAsync(owner, tenantId));
        await ReplyAsync(owner, tenantId, byPerson, "Sorry for the wait.");

        HashSet<Guid> after = await NeedsPersonAsync(owner, tenantId);

        Assert.True(after.SetEquals([failed]), "Only the failed turn should still be waiting.");
        Assert.Equal(1, await NeedsPersonTotalAsync(owner, tenantId));
        Assert.DoesNotContain(answered, after);
    }

    [Fact]
    public async Task An_unknown_filter_is_refused_with_a_code()
    {
        (HttpClient owner, Guid tenantId, _) = await WorkspaceAsync();

        HttpResponseMessage response = await owner.GetAsync(WebChatApiFixture.Relative(
            $"/api/tenants/{tenantId}/conversations?filter=mine"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("conversations.filter.invalid", await CodeAsync(response));
    }

    // ---- Unread -------------------------------------------------------------

    [Fact]
    public async Task Unread_is_per_member_and_marking_read_is_monotonic_and_clamped()
    {
        (HttpClient owner, Guid tenantId, string key) = await WorkspaceAsync();
        HttpClient colleague = await fixture.CreateRestrictedMemberAsync(tenantId, "conversations.view");
        Guid conversationId = (await StartAsync(key, "Hello")).ConversationId;

        long newest = Sequences(await ReadJsonAsync(owner, Messages(tenantId, conversationId, "tail=true")))[^1];

        // Nobody has read it: unread for both, with no marker.
        Assert.True((await RowAsync(owner, tenantId, conversationId)).GetProperty("unread").GetBoolean());
        JsonElement before = await ReadJsonAsync(owner, Conversation(tenantId, conversationId));
        Assert.True(before.GetProperty("unread").GetBoolean());
        Assert.Equal(JsonValueKind.Null, before.GetProperty("lastReadSequence").ValueKind);

        // Far beyond the newest message: clamped to it.
        Assert.Equal(HttpStatusCode.NoContent, (await MarkReadAsync(owner, tenantId, conversationId, newest + 1_000_000)).StatusCode);

        JsonElement after = await ReadJsonAsync(owner, Conversation(tenantId, conversationId));
        Assert.False(after.GetProperty("unread").GetBoolean());
        Assert.Equal(newest, after.GetProperty("lastReadSequence").GetInt64());
        Assert.False((await RowAsync(owner, tenantId, conversationId)).GetProperty("unread").GetBoolean());

        // Backwards changes nothing.
        Assert.Equal(HttpStatusCode.NoContent, (await MarkReadAsync(owner, tenantId, conversationId, 0)).StatusCode);
        Assert.Equal(newest, (await ReadJsonAsync(owner, Conversation(tenantId, conversationId))).GetProperty("lastReadSequence").GetInt64());

        // The colleague's own state is untouched.
        Assert.True((await RowAsync(colleague, tenantId, conversationId)).GetProperty("unread").GetBoolean());
    }

    [Fact]
    public async Task A_reply_clears_its_senders_unread_and_leaves_a_colleagues()
    {
        (HttpClient owner, Guid tenantId, string key) = await WorkspaceAsync();
        HttpClient colleague = await fixture.CreateRestrictedMemberAsync(tenantId, "conversations.view");
        (Guid conversationId, HttpClient visitor) = await StartAsync(key, "Hello");

        // The colleague reads everything so far.
        long newest = Sequences(await ReadJsonAsync(colleague, Messages(tenantId, conversationId, "tail=true")))[^1];
        await MarkReadAsync(colleague, tenantId, conversationId, newest);
        Assert.False((await RowAsync(colleague, tenantId, conversationId)).GetProperty("unread").GetBoolean());

        await HandoffAsync(owner, tenantId, conversationId, "take-over", await VersionAsync(owner, tenantId, conversationId));
        await SendAsync(visitor, conversationId, "Can a person help?");
        await ReplyAsync(owner, tenantId, conversationId, "Of course.");

        Assert.False((await RowAsync(owner, tenantId, conversationId)).GetProperty("unread").GetBoolean());
        Assert.True((await RowAsync(colleague, tenantId, conversationId)).GetProperty("unread").GetBoolean());
    }

    [Theory]
    [InlineData("{\"sequence\":-1}")]
    [InlineData("{}")]
    public async Task A_negative_or_missing_sequence_is_refused_with_a_code(string body)
    {
        (HttpClient owner, Guid tenantId, string key) = await WorkspaceAsync();
        Guid conversationId = (await StartAsync(key, "Hello")).ConversationId;

        HttpResponseMessage response = await owner.PostAsync(
            Read(tenantId, conversationId),
            new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("conversations.read.sequence.invalid", await CodeAsync(response));
    }

    // ---- Isolation and permissions ------------------------------------------

    [Fact]
    public async Task Another_workspace_gets_nothing_from_any_new_read()
    {
        (HttpClient owner, Guid tenantId, string key) = await WorkspaceAsync();
        (Guid conversationId, HttpClient visitor) = await StartAsync(key, "Hello");
        fixture.Generator.PretendUnavailable = true;
        await SendAsync(visitor, conversationId, "Refund please", expectFailure: true);
        fixture.Generator.PretendUnavailable = false;

        (HttpClient rival, Guid rivalTenantId, _) = await fixture.CreateOwnerAsync("Rival Inbox Trading");

        // Through their own workspace's routes: not found.
        Assert.Equal(HttpStatusCode.NotFound, (await rival.GetAsync(Messages(rivalTenantId, conversationId, "tail=true&include=events"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await rival.GetAsync(Conversation(rivalTenantId, conversationId))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await MarkReadAsync(rival, rivalTenantId, conversationId, 1)).StatusCode);

        // Their own needs-a-person list does not contain ours.
        JsonElement theirs = await ReadJsonAsync(rival, WebChatApiFixture.Relative(
            $"/api/tenants/{rivalTenantId}/conversations?filter=needs-person"));
        Assert.Equal(0, theirs.GetProperty("total").GetInt32());

        // Through ours: not a member, so not found.
        Assert.Equal(HttpStatusCode.NotFound, (await rival.GetAsync(WebChatApiFixture.Relative(
            $"/api/tenants/{tenantId}/conversations?filter=needs-person"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await rival.GetAsync(Messages(tenantId, conversationId, "tail=true&include=events"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await MarkReadAsync(rival, tenantId, conversationId, 1)).StatusCode);
    }

    [Fact]
    public async Task A_member_without_conversations_view_is_refused_every_new_read()
    {
        (HttpClient owner, Guid tenantId, string key) = await WorkspaceAsync();
        Guid conversationId = (await StartAsync(key, "Hello")).ConversationId;
        HttpClient blind = await fixture.CreateRestrictedMemberAsync(tenantId, "customers.view");

        Assert.Equal(HttpStatusCode.Forbidden, (await blind.GetAsync(WebChatApiFixture.Relative(
            $"/api/tenants/{tenantId}/conversations?filter=needs-person"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await blind.GetAsync(Messages(tenantId, conversationId, "tail=true&include=events"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await MarkReadAsync(blind, tenantId, conversationId, 1)).StatusCode);
    }

    // ---- Helpers --------------------------------------------------------------

    private sealed record Started(Guid ConversationId);

    private async Task<(HttpClient Owner, Guid TenantId, string Key)> WorkspaceAsync()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Inbox");

        fixture.ResetGenerator().Reply = "The answer.";

        return (owner, tenantId, key);
    }

    private async Task<(Guid ConversationId, HttpClient Visitor)> StartAsync(string key, string firstMessage)
    {
        HttpClient visitor = await NewVisitorAsync();

        HttpResponseMessage started = await visitor.PostAsync(
            WebChatApiFixture.Relative($"{ChatRoot}/{key}/conversations"),
            content: null);

        Assert.Equal(HttpStatusCode.OK, started.StatusCode);

        Guid conversationId = (await started.Content.ReadFromJsonAsync<Started>())!.ConversationId;

        await SendAsync(visitor, conversationId, firstMessage);

        return (conversationId, visitor);
    }

    private Task<HttpClient> NewVisitorAsync() =>
        fixture.CreateVisitorAsync(new IPAddress([198, 51, 100, (byte)(Interlocked.Increment(ref _visitors) % 250 + 1)]));

    private static async Task SendAsync(
        HttpClient visitor,
        Guid conversationId,
        string text,
        bool expectFailure = false)
    {
        HttpResponseMessage sent = await visitor.PostAsJsonAsync(
            WebChatApiFixture.Relative($"{ChatRoot}/conversations/{conversationId}/messages"),
            new { clientMessageId = Guid.NewGuid().ToString("N"), text });

        if (expectFailure)
        {
            Assert.False(sent.IsSuccessStatusCode, "The turn was expected to fail.");
        }
        else
        {
            Assert.True(sent.IsSuccessStatusCode, $"A visitor message answered {(int)sent.StatusCode}.");
        }
    }

    /// <summary>Taken over, the customer writes, handed back to the AI with no reply.</summary>
    private async Task<(Guid ConversationId, HttpClient Visitor)> LeftUnansweredAsync(
        HttpClient owner,
        Guid tenantId,
        string key)
    {
        (Guid conversationId, HttpClient visitor) = await StartAsync(key, "Hello");

        await HandoffAsync(owner, tenantId, conversationId, "take-over", await VersionAsync(owner, tenantId, conversationId));
        await SendAsync(visitor, conversationId, "I need a person, please");
        await HandoffAsync(owner, tenantId, conversationId, "return-to-ai", await VersionAsync(owner, tenantId, conversationId));

        return (conversationId, visitor);
    }

    private static async Task<HashSet<Guid>> NeedsPersonAsync(HttpClient owner, Guid tenantId)
    {
        JsonElement list = await ReadJsonAsync(owner, WebChatApiFixture.Relative(
            $"/api/tenants/{tenantId}/conversations?filter=needs-person&pageSize=50"));

        return [.. list.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid())];
    }

    private static async Task<int> NeedsPersonTotalAsync(HttpClient owner, Guid tenantId) =>
        (await ReadJsonAsync(owner, WebChatApiFixture.Relative(
            $"/api/tenants/{tenantId}/conversations?filter=needs-person&pageSize=1")))
        .GetProperty("total").GetInt32();

    private static async Task ReplyAsync(HttpClient owner, Guid tenantId, Guid conversationId, string text)
    {
        HttpResponseMessage sent = await owner.PostAsJsonAsync(
            WebChatApiFixture.Relative($"/api/tenants/{tenantId}/conversations/{conversationId}/messages"),
            new { clientMessageId = Guid.NewGuid().ToString("N"), text });

        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
    }

    private static async Task<string> VersionAsync(HttpClient owner, Guid tenantId, Guid conversationId)
    {
        HttpResponseMessage read = await owner.GetAsync(Conversation(tenantId, conversationId));

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        return read.Headers.ETag!.ToString();
    }

    private static async Task<string> HandoffAsync(
        HttpClient owner,
        Guid tenantId,
        Guid conversationId,
        string action,
        string version)
    {
        using HttpRequestMessage request = new(
            HttpMethod.Post,
            WebChatApiFixture.Relative($"/api/tenants/{tenantId}/conversations/{conversationId}/{action}"));

        request.Headers.TryAddWithoutValidation("If-Match", version);

        HttpResponseMessage response = await owner.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return response.Headers.ETag!.ToString();
    }

    private static async Task RenameAsync(HttpClient owner, Guid tenantId, Guid customerId, string name)
    {
        HttpResponseMessage read = await owner.GetAsync(Customer(tenantId, customerId));

        using HttpRequestMessage rename = new(HttpMethod.Put, new Uri($"{Customer(tenantId, customerId)}/display-name", UriKind.Relative))
        {
            Content = JsonContent.Create(new { displayName = name }),
        };

        rename.Headers.TryAddWithoutValidation("If-Match", read.Headers.ETag!.ToString());

        Assert.Equal(HttpStatusCode.OK, (await owner.SendAsync(rename)).StatusCode);
    }

    private static Task<HttpResponseMessage> MarkReadAsync(HttpClient client, Guid tenantId, Guid conversationId, long sequence) =>
        client.PostAsJsonAsync(Read(tenantId, conversationId), new { sequence });

    private static async Task<JsonElement> RowAsync(HttpClient client, Guid tenantId, Guid conversationId)
    {
        JsonElement page = await ReadJsonAsync(client, WebChatApiFixture.Relative(
            $"/api/tenants/{tenantId}/conversations?pageSize=50"));

        return page.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("id").GetGuid() == conversationId);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpClient client, Uri address)
    {
        HttpResponseMessage response = await client.GetAsync(address);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString();

    private static List<long> Sequences(JsonElement page) =>
        [.. page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("sequence").GetInt64())];

    private static Uri Conversation(Guid tenantId, Guid conversationId) =>
        WebChatApiFixture.Relative($"/api/tenants/{tenantId}/conversations/{conversationId}");

    private static Uri Messages(Guid tenantId, Guid conversationId, string query) =>
        WebChatApiFixture.Relative($"/api/tenants/{tenantId}/conversations/{conversationId}/messages?{query}");

    private static Uri Read(Guid tenantId, Guid conversationId) =>
        WebChatApiFixture.Relative($"/api/tenants/{tenantId}/conversations/{conversationId}/read");

    private static Uri Customer(Guid tenantId, Guid customerId) =>
        WebChatApiFixture.Relative($"/api/tenants/{tenantId}/customers/{customerId}");
}
