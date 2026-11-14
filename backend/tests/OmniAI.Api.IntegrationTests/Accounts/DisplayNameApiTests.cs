using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OmniAI.Api.IntegrationTests.AiAgents;
using OmniAI.Api.IntegrationTests.WebChat;

namespace OmniAI.Api.IntegrationTests.Accounts;

/// <summary>
/// Phase 39.1 (note 7): a member's display name, edited by its owner and read
/// beside the email wherever the API names a person.
/// </summary>
public sealed class DisplayNameApiTests(WebChatApiFixture fixture) : IClassFixture<WebChatApiFixture>
{
    private const string ChatRoot = "/api/public/webchat";

    private static int _visitors;

    // ---- the edit -------------------------------------------------------------

    [Fact]
    public async Task The_person_sets_and_clears_their_own_name_and_me_and_session_carry_it_beside_the_email()
    {
        HttpClient client = await fixture.CreateSignedInClientAsync();
        string email = await AiAgentApiFixture.CurrentEmailAsync(client);

        JsonElement before = await ReadAsync(client, "/api/auth/me");
        Assert.Equal(JsonValueKind.Null, before.GetProperty("displayName").ValueKind);

        Assert.Equal(HttpStatusCode.NoContent, (await SetAsync(client, "  Sara \t  Haddad  ")).StatusCode);

        JsonElement me = await ReadAsync(client, "/api/auth/me");
        Assert.Equal("Sara Haddad", me.GetProperty("displayName").GetString());
        Assert.Equal(email, me.GetProperty("email").GetString());

        JsonElement session = (await ReadAsync(client, "/api/auth/session")).GetProperty("user");
        Assert.Equal("Sara Haddad", session.GetProperty("displayName").GetString());
        Assert.Equal(email, session.GetProperty("email").GetString());

        foreach (string? clearing in new[] { null, "", "   " })
        {
            Assert.Equal(HttpStatusCode.NoContent, (await SetAsync(client, "Sara")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await SetAsync(client, clearing)).StatusCode);

            JsonElement cleared = await ReadAsync(client, "/api/auth/me");
            Assert.Equal(JsonValueKind.Null, cleared.GetProperty("displayName").ValueKind);
            Assert.Equal(email, cleared.GetProperty("email").GetString());
        }
    }

    [Fact]
    public async Task Eighty_characters_pass_and_the_refusals_carry_their_codes()
    {
        HttpClient client = await fixture.CreateSignedInClientAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await SetAsync(client, new string('a', 80))).StatusCode);

        HttpResponseMessage tooLong = await SetAsync(client, new string('b', 81));
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);

        JsonElement problem = await BodyAsync(tooLong);
        Assert.Equal("account.display_name.too_long", problem.GetProperty("code").GetString());
        Assert.Equal(80, problem.GetProperty("limit").GetInt64());
        Assert.Equal("characters", problem.GetProperty("limitUnit").GetString());

        foreach (string bad in new[] { "Sara\u0007", "‎Sara", "Sara‮Haddad" })
        {
            HttpResponseMessage refused = await SetAsync(client, bad);
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Equal("account.display_name.control_characters", (await BodyAsync(refused)).GetProperty("code").GetString());
        }

        // Nothing refused was stored.
        Assert.Equal(new string('a', 80), (await ReadAsync(client, "/api/auth/me")).GetProperty("displayName").GetString());

        // The joiners Arabic and emoji need are kept.
        Assert.Equal(HttpStatusCode.NoContent, (await SetAsync(client, "ل‌ا")).StatusCode);
        Assert.Equal("ل‌ا", (await ReadAsync(client, "/api/auth/me")).GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task The_edit_needs_a_session_and_an_antiforgery_token_and_names_no_other_account()
    {
        HttpClient anonymous = fixture.CreateClient();
        await ApiAuthentication.BootstrapAntiforgeryAsync(anonymous);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SetAsync(anonymous, "Sara")).StatusCode);

        HttpClient signedIn = await fixture.CreateSignedInClientAsync();
        signedIn.DefaultRequestHeaders.Remove(ApiAuthentication.AntiforgeryHeader);
        Assert.Equal(HttpStatusCode.BadRequest, (await SetAsync(signedIn, "Sara")).StatusCode);

        // There is no route that names an account: one with a user id is not a route.
        HttpClient owner = await fixture.CreateSignedInClientAsync();
        HttpResponseMessage someoneElse = await owner.PutAsJsonAsync(
            WebChatApiFixture.Relative($"/api/me/display-name/{Guid.NewGuid()}"),
            new { displayName = "Not yours" });
        Assert.True(
            someoneElse.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed,
            $"A route naming an account answered {(int)someoneElse.StatusCode}.");
    }

    // ---- every read that names a person ------------------------------------------

    [Fact]
    public async Task Every_workspace_read_that_names_a_person_carries_the_name_beside_the_email()
    {
        (HttpClient owner, Guid tenantId, Guid ownerId) = await fixture.CreateOwnerAsync();
        string ownerEmail = await AiAgentApiFixture.CurrentEmailAsync(owner);
        Assert.Equal(HttpStatusCode.NoContent, (await SetAsync(owner, "Sara Haddad")).StatusCode);

        // A colleague without a name: null, with the email.
        string colleagueEmail = ApiAuthentication.UniqueEmail();
        (_, Guid colleagueId) = await fixture.CreateMemberWithEmailAsync(tenantId, colleagueEmail, "conversations.view", "conversations.reply");

        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Names");
        fixture.ResetGenerator().Reply = "The answer.";

        (Guid conversationId, HttpClient visitor) = await StartAsync(key, "Hello");

        string version = await VersionAsync(owner, tenantId, conversationId);
        version = await SendIfMatchAsync(owner, HttpMethod.Post, Conversation(tenantId, conversationId, "/take-over"), version, null);
        await SendIfMatchAsync(owner, HttpMethod.Put, Conversation(tenantId, conversationId, "/assignment"), version, new { member = ownerId });

        HttpResponseMessage replied = await owner.PostAsJsonAsync(
            Conversation(tenantId, conversationId, "/messages"),
            new { clientMessageId = Guid.NewGuid().ToString("N"), text = "A person here." });
        Assert.Equal(HttpStatusCode.OK, replied.StatusCode);

        // The reply's own answer leaves sentBy null, as it did before this phase;
        // the transcript read below is where the author is named.

        // The list row and the detail: who it is assigned to, and who took it over.
        JsonElement row = (await ReadAsync(owner, $"/api/tenants/{tenantId}/conversations?pageSize=50"))
            .GetProperty("items").EnumerateArray().Single(item => item.GetProperty("id").GetGuid() == conversationId);
        AssertNamed(row.GetProperty("assignedTo"), ownerEmail, "Sara Haddad");
        AssertNamed(row.GetProperty("takenOverBy"), ownerEmail, "Sara Haddad");

        JsonElement detail = await ReadAsync(owner, $"/api/tenants/{tenantId}/conversations/{conversationId}");
        AssertNamed(detail.GetProperty("assignedTo"), ownerEmail, "Sara Haddad");
        AssertNamed(detail.GetProperty("takenOverBy"), ownerEmail, "Sara Haddad");

        // The transcript: the reply's author and the takeover event's actor.
        JsonElement transcript = await ReadAsync(owner, $"/api/tenants/{tenantId}/conversations/{conversationId}/messages?tail=true&include=events");
        JsonElement human = transcript.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("sender").GetString() == "HumanAgent");
        AssertNamed(human.GetProperty("sentBy"), ownerEmail, "Sara Haddad");
        JsonElement takeover = transcript.GetProperty("events").EnumerateArray().Single(entry => entry.GetProperty("kind").GetString() == "TakenOver");
        AssertNamed(takeover.GetProperty("actor"), ownerEmail, "Sara Haddad");

        // The assignee list: the owner by name, the colleague with a null name.
        JsonElement[] assignees = [.. (await ReadAsync(owner, $"/api/tenants/{tenantId}/conversations/assignees")).EnumerateArray()];
        AssertNamed(assignees.Single(item => item.GetProperty("userId").GetGuid() == ownerId), ownerEmail, "Sara Haddad");
        AssertNamed(assignees.Single(item => item.GetProperty("userId").GetGuid() == colleagueId), colleagueEmail, null);

        // The team list and one member.
        JsonElement[] members = [.. (await ReadAsync(owner, $"/api/tenants/{tenantId}/members?pageSize=50")).GetProperty("items").EnumerateArray()];
        AssertNamed(members.Single(item => item.GetProperty("userId").GetGuid() == ownerId), ownerEmail, "Sara Haddad");
        AssertNamed(members.Single(item => item.GetProperty("userId").GetGuid() == colleagueId), colleagueEmail, null);
        AssertNamed(await ReadAsync(owner, $"/api/tenants/{tenantId}/members/{ownerId}"), ownerEmail, "Sara Haddad");

        // An invitation: the inviter's name beside the inviter's email, on the
        // creation and on the list.
        HttpResponseMessage invited = await owner.PostAsJsonAsync(
            WebChatApiFixture.Relative($"/api/tenants/{tenantId}/invitations"),
            new { email = ApiAuthentication.UniqueEmail(), roleIds = Array.Empty<Guid>() });
        Assert.Equal(HttpStatusCode.Created, invited.StatusCode);
        JsonElement created = (await BodyAsync(invited)).GetProperty("invitation");
        Assert.Equal(ownerEmail, created.GetProperty("invitedByEmail").GetString());
        Assert.Equal("Sara Haddad", created.GetProperty("invitedByDisplayName").GetString());

        JsonElement listed = (await ReadAsync(owner, $"/api/tenants/{tenantId}/invitations")).GetProperty("items")[0];
        Assert.Equal(ownerEmail, listed.GetProperty("invitedByEmail").GetString());
        Assert.Equal("Sara Haddad", listed.GetProperty("invitedByDisplayName").GetString());

        // The visitor never reads it (owner decision Q5): the public transcript
        // says nothing of the name or the email.
        string visitorTranscript = await visitor.GetStringAsync(WebChatApiFixture.Relative(
            $"{ChatRoot}/conversations/{conversationId}/messages"));
        Assert.DoesNotContain("Sara Haddad", visitorTranscript, StringComparison.Ordinal);
        Assert.DoesNotContain("displayName", visitorTranscript, StringComparison.Ordinal);
        Assert.DoesNotContain("@", visitorTranscript, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Another_workspace_never_reads_a_name_through_any_workspace_route()
    {
        (HttpClient owner, Guid tenantId, Guid ownerId) = await fixture.CreateOwnerAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await SetAsync(owner, "Private Name")).StatusCode);

        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Isolation");
        fixture.ResetGenerator().Reply = "The answer.";
        (Guid conversationId, _) = await StartAsync(key, "Hello");
        await SendIfMatchAsync(owner, HttpMethod.Post, Conversation(tenantId, conversationId, "/take-over"), await VersionAsync(owner, tenantId, conversationId), null);

        (HttpClient rival, Guid rivalTenantId, _) = await fixture.CreateOwnerAsync("Rival Names Trading");

        foreach (string path in new[]
        {
            $"/api/tenants/{tenantId}/members",
            $"/api/tenants/{tenantId}/members/{ownerId}",
            $"/api/tenants/{tenantId}/invitations",
            $"/api/tenants/{tenantId}/conversations",
            $"/api/tenants/{tenantId}/conversations/assignees",
            $"/api/tenants/{tenantId}/conversations/{conversationId}",
            $"/api/tenants/{tenantId}/conversations/{conversationId}/messages?include=events",
            $"/api/tenants/{rivalTenantId}/members/{ownerId}",
            $"/api/tenants/{rivalTenantId}/conversations/{conversationId}",
        })
        {
            HttpResponseMessage response = await rival.GetAsync(WebChatApiFixture.Relative(path));

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.DoesNotContain("Private Name", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        // Their own lists do not contain it either.
        foreach (string path in new[]
        {
            $"/api/tenants/{rivalTenantId}/members?pageSize=50",
            $"/api/tenants/{rivalTenantId}/conversations/assignees",
            $"/api/tenants/{rivalTenantId}/conversations?pageSize=50",
        })
        {
            Assert.DoesNotContain("Private Name", await rival.GetStringAsync(WebChatApiFixture.Relative(path)), StringComparison.Ordinal);
        }
    }

    // ---- helpers ----------------------------------------------------------------

    private sealed record Started(Guid ConversationId);

    private static void AssertNamed(JsonElement person, string email, string? displayName)
    {
        Assert.Equal(email, person.GetProperty("email").GetString());

        JsonElement name = person.GetProperty("displayName");

        if (displayName is null)
        {
            Assert.Equal(JsonValueKind.Null, name.ValueKind);
        }
        else
        {
            Assert.Equal(displayName, name.GetString());
        }
    }

    private static Task<HttpResponseMessage> SetAsync(HttpClient client, string? displayName) =>
        client.PutAsJsonAsync(WebChatApiFixture.Relative("/api/me/display-name"), new { displayName });

    private async Task<(Guid ConversationId, HttpClient Visitor)> StartAsync(string key, string firstMessage)
    {
        HttpClient visitor = await fixture.CreateVisitorAsync(
            new IPAddress([203, 0, 113, (byte)(Interlocked.Increment(ref _visitors) % 250 + 1)]));

        HttpResponseMessage started = await visitor.PostAsync(WebChatApiFixture.Relative($"{ChatRoot}/{key}/conversations"), content: null);
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);

        Guid conversationId = (await started.Content.ReadFromJsonAsync<Started>())!.ConversationId;

        HttpResponseMessage sent = await visitor.PostAsJsonAsync(
            WebChatApiFixture.Relative($"{ChatRoot}/conversations/{conversationId}/messages"),
            new { clientMessageId = Guid.NewGuid().ToString("N"), text = firstMessage });
        Assert.True(sent.IsSuccessStatusCode, $"A visitor message answered {(int)sent.StatusCode}.");

        return (conversationId, visitor);
    }

    private static async Task<string> VersionAsync(HttpClient owner, Guid tenantId, Guid conversationId)
    {
        HttpResponseMessage read = await owner.GetAsync(Conversation(tenantId, conversationId, string.Empty));
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        return read.Headers.ETag!.ToString();
    }

    private static async Task<string> SendIfMatchAsync(HttpClient client, HttpMethod method, Uri address, string version, object? body)
    {
        using HttpRequestMessage request = new(method, address);
        request.Headers.TryAddWithoutValidation("If-Match", version);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType());
        }

        HttpResponseMessage response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return response.Headers.ETag!.ToString();
    }

    private static Uri Conversation(Guid tenantId, Guid conversationId, string suffix) =>
        WebChatApiFixture.Relative($"/api/tenants/{tenantId}/conversations/{conversationId}{suffix}");

    private static async Task<JsonElement> ReadAsync(HttpClient client, string path)
    {
        HttpResponseMessage response = await client.GetAsync(WebChatApiFixture.Relative(path));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await BodyAsync(response);
    }

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
}
