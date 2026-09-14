using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace OmniAI.Api.IntegrationTests.WhatsApp;

/// <summary>
/// Administering WhatsApp connections over HTTP.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The credential is write-only, and this suite is where that is
/// proved.</strong> Every read path is checked for the token that was sent, and
/// the database is checked for it in plaintext.
/// </para>
/// <para>
/// No real WhatsApp credential exists anywhere here: the WABA id, the phone
/// number id and the access token are invented by the fixture.
/// </para>
/// </remarks>
public sealed class WhatsAppAdminApiTests(WhatsAppApiFixture fixture)
    : IClassFixture<WhatsAppApiFixture>
{
    // ---- Authorization ----------------------------------------------------

    [Fact]
    public async Task Signing_out_leaves_no_way_to_read_connections()
    {
        (_, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        HttpClient anonymous = fixture.CreateClient();

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync(WhatsAppApiFixture.Connections(tenantId))).StatusCode);
    }

    [Fact]
    public async Task Somebody_outside_the_workspace_is_told_it_does_not_exist()
    {
        (_, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (HttpClient outsider, _, _) = await fixture.CreateOwnerAsync("Other company");

        // Not 403. Telling a stranger that a workspace exists but is not theirs
        // is telling them it exists.
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await outsider.GetAsync(WhatsAppApiFixture.Connections(tenantId))).StatusCode);
    }

    [Fact]
    public async Task A_member_without_the_view_permission_is_refused()
    {
        (_, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        HttpClient member = await fixture.CreateRestrictedMemberAsync(tenantId, "workspace.view");

        // 403, not 404: they belong here and are entitled to know they need a
        // permission they have not got.
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await member.GetAsync(WhatsAppApiFixture.Connections(tenantId))).StatusCode);
    }

    [Fact]
    public async Task Viewing_does_not_imply_managing()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        Guid agentId = await WebChat.WebChatApiFixture.CreateActiveAgentAsync(
            owner,
            tenantId,
            "Read only");

        HttpClient reader = await fixture.CreateRestrictedMemberAsync(tenantId, "whatsapp.view");

        Assert.Equal(
            HttpStatusCode.OK,
            (await reader.GetAsync(WhatsAppApiFixture.Connections(tenantId))).StatusCode);

        HttpResponseMessage refused = await reader.PostAsJsonAsync(
            WhatsAppApiFixture.Connections(tenantId),
            new
            {
                aiAgentId = agentId,
                wabaId = "111222333444555",
                phoneNumberId = WhatsAppApiFixture.Number(),
            });

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    [Fact]
    public async Task Managing_does_not_imply_viewing_conversations()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        Guid connectionId = await WhatsAppApiFixture.ServingAsync(
            owner,
            tenantId,
            WhatsAppApiFixture.Number(),
            "Channel admin");

        HttpClient channelAdmin = await fixture.CreateRestrictedMemberAsync(
            tenantId,
            "whatsapp.view",
            "whatsapp.manage");

        // Administering the channel is not a licence to read what customers
        // said on it.
        HttpResponseMessage refused = await channelAdmin.GetAsync(
            WebChat.WebChatApiFixture.Relative(
                $"/api/tenants/{tenantId}/whatsapp/conversations/{Guid.CreateVersion7()}"));

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.NotEqual(Guid.Empty, connectionId);
    }

    // ---- The credential ---------------------------------------------------

    [Fact]
    public async Task A_new_connection_is_a_draft_with_no_credential()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        (Guid connectionId, _, _) = await WhatsAppApiFixture.ConnectAsync(
            owner,
            tenantId,
            WhatsAppApiFixture.Number(),
            "Draft state");

        WhatsAppApiFixture.ConnectionPayload payload = await ReadAsync(owner, tenantId, connectionId);

        Assert.Equal("Draft", payload.Status);
        Assert.False(payload.HasCredential);
    }

    [Fact]
    public async Task The_access_token_is_never_returned_by_any_read()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        Guid connectionId = await WhatsAppApiFixture.ServingAsync(
            owner,
            tenantId,
            WhatsAppApiFixture.Number(),
            "Write only");

        // The detail, the list, and the response to storing it: all three.
        foreach (Uri address in new[]
        {
            WhatsAppApiFixture.Connection(tenantId, connectionId),
            WhatsAppApiFixture.Connections(tenantId),
        })
        {
            string body = await (await owner.GetAsync(address)).Content.ReadAsStringAsync();

            Assert.DoesNotContain(
                WhatsAppApiFixture.AccessToken,
                body,
                StringComparison.Ordinal);

            Assert.DoesNotContain("accessToken", body, StringComparison.OrdinalIgnoreCase);
        }

        WhatsAppApiFixture.ConnectionPayload payload = await ReadAsync(owner, tenantId, connectionId);

        // What an administrator is told instead: whether one is configured.
        Assert.True(payload.HasCredential);
    }

    [Fact]
    public async Task The_stored_token_is_not_plaintext_in_the_database()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        await WhatsAppApiFixture.ServingAsync(
            owner,
            tenantId,
            WhatsAppApiFixture.Number(),
            "Encrypted");

        object? asText = await fixture.Database.ExecuteScalarAsync(
            "SELECT string_agg(encode(protected_token, 'escape'), ' ') FROM whatsapp_credentials");

        Assert.DoesNotContain(
            WhatsAppApiFixture.AccessToken,
            asText as string ?? string.Empty,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_blank_token_is_refused()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        (Guid connectionId, string tag, _) = await WhatsAppApiFixture.ConnectAsync(
            owner,
            tenantId,
            WhatsAppApiFixture.Number(),
            "Blank token");

        HttpResponseMessage refused = await WhatsAppApiFixture.PutAsync(
            owner,
            WhatsAppApiFixture.Credential(tenantId, connectionId),
            new { accessToken = "   " },
            tag);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    // ---- Lifecycle --------------------------------------------------------

    [Fact]
    public async Task A_connection_cannot_be_activated_without_a_credential()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        (Guid connectionId, string tag, _) = await WhatsAppApiFixture.ConnectAsync(
            owner,
            tenantId,
            WhatsAppApiFixture.Number(),
            "No token yet");

        HttpResponseMessage refused = await WhatsAppApiFixture.PostAsync(
            owner,
            Activate(tenantId, connectionId),
            tag);

        // A connection Meta delivers to and that can never answer looks live in
        // the admin and is a customer talking to nobody.
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
    }

    [Fact]
    public async Task Activating_and_disabling_move_the_status()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        Guid connectionId = await WhatsAppApiFixture.ServingAsync(
            owner,
            tenantId,
            WhatsAppApiFixture.Number(),
            "Lifecycle");

        Assert.Equal("Active", (await ReadAsync(owner, tenantId, connectionId)).Status);

        (_, string tag) = await ReadWithTagAsync(owner, tenantId, connectionId);

        HttpResponseMessage disabled = await WhatsAppApiFixture.PostAsync(
            owner,
            Disable(tenantId, connectionId),
            tag);

        Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);
        Assert.Equal("Disabled", (await ReadAsync(owner, tenantId, connectionId)).Status);
    }

    [Fact]
    public async Task Two_workspaces_cannot_connect_the_same_number()
    {
        (HttpClient first, Guid firstTenant, _) = await fixture.CreateOwnerAsync();
        (HttpClient second, Guid secondTenant, _) = await fixture.CreateOwnerAsync("Rival");

        string number = WhatsAppApiFixture.Number();

        await WhatsAppApiFixture.ConnectAsync(first, firstTenant, number, "Mine");

        Guid agentId = await WebChat.WebChatApiFixture.CreateActiveAgentAsync(
            second,
            secondTenant,
            "Theirs");

        HttpResponseMessage refused = await second.PostAsJsonAsync(
            WhatsAppApiFixture.Connections(secondTenant),
            new { aiAgentId = agentId, wabaId = "111222333444555", phoneNumberId = number });

        // 409, and the message must not say which workspace has it.
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);

        string body = await refused.Content.ReadAsStringAsync();

        Assert.DoesNotContain(firstTenant.ToString(), body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_agent_from_another_workspace_cannot_answer_on_this_number()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (HttpClient rival, Guid rivalTenant, _) = await fixture.CreateOwnerAsync("Rival");

        Guid theirAgent = await WebChat.WebChatApiFixture.CreateActiveAgentAsync(
            rival,
            rivalTenant,
            "Theirs");

        HttpResponseMessage refused = await owner.PostAsJsonAsync(
            WhatsAppApiFixture.Connections(tenantId),
            new
            {
                aiAgentId = theirAgent,
                wabaId = "111222333444555",
                phoneNumberId = WhatsAppApiFixture.Number(),
            });

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    // ---- Conditional writes -----------------------------------------------

    [Fact]
    public async Task A_write_without_if_match_is_refused_with_428()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        (Guid connectionId, _, Guid agentId) = await WhatsAppApiFixture.ConnectAsync(
            owner,
            tenantId,
            WhatsAppApiFixture.Number(),
            "Precondition");

        HttpResponseMessage refused = await WhatsAppApiFixture.PutAsync(
            owner,
            WhatsAppApiFixture.Connection(tenantId, connectionId),
            Settings(agentId, WhatsAppApiFixture.Number()),
            tag: null);

        // 428 rather than 400: the request is well-formed, and what is missing is
        // the caller stating which version they are changing.
        Assert.Equal(HttpStatusCode.PreconditionRequired, refused.StatusCode);
    }

    [Fact]
    public async Task A_write_against_a_stale_version_is_refused_with_412()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        (Guid connectionId, string original, Guid agentId) = await WhatsAppApiFixture.ConnectAsync(
            owner,
            tenantId,
            WhatsAppApiFixture.Number(),
            "Stale");

        // Somebody else changes it first, exactly as a second administrator in a
        // second tab would.
        HttpResponseMessage winner = await WhatsAppApiFixture.PutAsync(
            owner,
            WhatsAppApiFixture.Connection(tenantId, connectionId),
            Settings(agentId, WhatsAppApiFixture.Number()),
            original);

        Assert.Equal(HttpStatusCode.OK, winner.StatusCode);

        HttpResponseMessage loser = await WhatsAppApiFixture.PutAsync(
            owner,
            WhatsAppApiFixture.Connection(tenantId, connectionId),
            Settings(agentId, WhatsAppApiFixture.Number()),
            original);

        Assert.Equal(HttpStatusCode.PreconditionFailed, loser.StatusCode);
    }

    [Fact]
    public async Task Every_write_hands_back_a_new_entity_tag()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        (Guid connectionId, string created, Guid agentId) = await WhatsAppApiFixture.ConnectAsync(
            owner,
            tenantId,
            WhatsAppApiFixture.Number(),
            "New tag");

        HttpResponseMessage updated = await WhatsAppApiFixture.PutAsync(
            owner,
            WhatsAppApiFixture.Connection(tenantId, connectionId),
            Settings(agentId, WhatsAppApiFixture.Number()),
            created);

        string? next = updated.Headers.ETag?.ToString();

        Assert.NotNull(next);
        Assert.NotEqual(created, next);

        // And the new one works, so a client can chain edits without re-reading.
        Assert.Equal(
            HttpStatusCode.OK,
            (await WhatsAppApiFixture.PutAsync(
                owner,
                WhatsAppApiFixture.Credential(tenantId, connectionId),
                new { accessToken = WhatsAppApiFixture.AccessToken },
                next)).StatusCode);
    }

    [Fact]
    public async Task A_live_connection_cannot_be_reconfigured_underneath_its_customers()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        Guid connectionId = await WhatsAppApiFixture.ServingAsync(
            owner,
            tenantId,
            WhatsAppApiFixture.Number(),
            "Live");

        (WhatsAppApiFixture.ConnectionPayload payload, string tag) =
            await ReadWithTagAsync(owner, tenantId, connectionId);

        HttpResponseMessage refused = await WhatsAppApiFixture.PutAsync(
            owner,
            WhatsAppApiFixture.Connection(tenantId, connectionId),
            Settings(payload.AiAgentId, WhatsAppApiFixture.Number()),
            tag);

        // Changing the number or the agent while messages are arriving would
        // reroute a conversation mid-sentence.
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
    }

    private static object Settings(Guid agentId, string phoneNumberId) =>
        new
        {
            aiAgentId = agentId,
            wabaId = "111222333444555",
            phoneNumberId,
            displayPhoneNumber = "+44 7700 900001",
        };

    private static Uri Activate(Guid tenantId, Guid connectionId) =>
        WebChat.WebChatApiFixture.Relative(
            $"/api/tenants/{tenantId}/whatsapp/connections/{connectionId}/activate");

    private static Uri Disable(Guid tenantId, Guid connectionId) =>
        WebChat.WebChatApiFixture.Relative(
            $"/api/tenants/{tenantId}/whatsapp/connections/{connectionId}/disable");

    private static async Task<WhatsAppApiFixture.ConnectionPayload> ReadAsync(
        HttpClient client,
        Guid tenantId,
        Guid connectionId) =>
        (await ReadWithTagAsync(client, tenantId, connectionId)).Payload;

    private static async Task<(WhatsAppApiFixture.ConnectionPayload Payload, string Tag)>
        ReadWithTagAsync(HttpClient client, Guid tenantId, Guid connectionId)
    {
        HttpResponseMessage response = await client.GetAsync(
            WhatsAppApiFixture.Connection(tenantId, connectionId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        WhatsAppApiFixture.ConnectionPayload? payload =
            await response.Content.ReadFromJsonAsync<WhatsAppApiFixture.ConnectionPayload>();

        Assert.NotNull(payload);

        return (
            payload,
            response.Headers.ETag?.ToString()
            ?? throw new InvalidOperationException("The read carried no entity tag."));
    }
}
