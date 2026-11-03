using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OmniAI.Api.IntegrationTests.WebChat;
using OmniAI.Api.IntegrationTests.WhatsApp;

namespace OmniAI.Api.IntegrationTests.AiAgents;

/// <summary>
/// Phase 37.5a, row 18: an agent's channels on the agent list and detail, and
/// only the kinds the reader may see.
/// </summary>
public sealed class AiAgentChannelsApiTests(WhatsAppApiFixture fixture)
    : IClassFixture<WhatsAppApiFixture>
{
    [Fact]
    public async Task The_owner_sees_both_channels_on_the_list_and_the_detail()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (Guid agentId, Guid webChatId, Guid whatsAppId) = await AgentOnBothAsync(owner, tenantId);

        foreach (JsonElement channels in new[]
        {
            (await ListItemAsync(owner, tenantId, agentId)).GetProperty("channels"),
            (await DetailAsync(owner, tenantId, agentId)).GetProperty("channels"),
        })
        {
            Assert.Equal(2, channels.GetArrayLength());

            JsonElement webChat = channels[0];
            Assert.Equal("WebChat", webChat.GetProperty("kind").GetString());
            Assert.Equal(webChatId, webChat.GetProperty("id").GetGuid());
            Assert.Equal("Draft", webChat.GetProperty("status").GetString());

            JsonElement whatsApp = channels[1];
            Assert.Equal("WhatsApp", whatsApp.GetProperty("kind").GetString());
            Assert.Equal(whatsAppId, whatsApp.GetProperty("id").GetGuid());
            Assert.Equal("Draft", whatsApp.GetProperty("status").GetString());

            // Identity and status only: nothing from the channel's own read.
            Assert.False(webChat.TryGetProperty("publicKey", out _));
            Assert.False(whatsApp.TryGetProperty("phoneNumberId", out _));
        }
    }

    [Theory]
    [InlineData("ai-agents.view", "")]
    [InlineData("ai-agents.view,website-chat.view", "WebChat")]
    [InlineData("ai-agents.view,whatsapp.view", "WhatsApp")]
    [InlineData("ai-agents.view,website-chat.view,whatsapp.view", "WebChat,WhatsApp")]
    public async Task A_reader_sees_only_the_channel_kinds_they_may_view(string permissions, string visible)
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (Guid agentId, _, _) = await AgentOnBothAsync(owner, tenantId);

        string[] kinds = visible.Split(',', StringSplitOptions.RemoveEmptyEntries);
        HttpClient reader = await fixture.CreateRestrictedMemberAsync(tenantId, permissions.Split(','));

        Assert.Equal(kinds, Kinds((await ListItemAsync(reader, tenantId, agentId)).GetProperty("channels")));
        Assert.Equal(kinds, Kinds((await DetailAsync(reader, tenantId, agentId)).GetProperty("channels")));
    }

    [Fact]
    public async Task An_agent_with_no_channel_reads_an_empty_list()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        Guid agentId = await WebChatApiFixture.CreateActiveAgentAsync(owner, tenantId, "Quiet");

        Assert.Equal(0, (await ListItemAsync(owner, tenantId, agentId)).GetProperty("channels").GetArrayLength());
        Assert.Equal(0, (await DetailAsync(owner, tenantId, agentId)).GetProperty("channels").GetArrayLength());
    }

    [Fact]
    public async Task Another_workspace_never_sees_this_workspaces_channels()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (Guid agentId, _, _) = await AgentOnBothAsync(owner, tenantId);

        (HttpClient other, Guid otherTenant, _) = await fixture.CreateOwnerAsync("Other company");
        Guid theirAgent = await WebChatApiFixture.CreateActiveAgentAsync(other, otherTenant, "Theirs");

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(Agent(tenantId, agentId))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(Agent(otherTenant, agentId))).StatusCode);
        Assert.Equal(0, (await ListItemAsync(other, otherTenant, theirAgent)).GetProperty("channels").GetArrayLength());
    }

    [Fact]
    public async Task The_writes_keep_their_seven_fields()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (Guid agentId, _, _) = await AgentOnBothAsync(owner, tenantId);

        HttpResponseMessage read = await owner.GetAsync(Agent(tenantId, agentId));
        HttpRequestMessage rename = new(HttpMethod.Put, WebChatApiFixture.Relative($"/api/tenants/{tenantId}/ai-agents/{agentId}/name"))
        {
            Content = JsonContent.Create(new { name = "Renamed" }),
        };
        rename.Headers.TryAddWithoutValidation("If-Match", read.Headers.ETag!.ToString());

        HttpResponseMessage renamed = await owner.SendAsync(rename);

        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.False((await ReadAsync(renamed)).TryGetProperty("channels", out _));
    }

    private static async Task<(Guid AgentId, Guid WebChatId, Guid WhatsAppId)> AgentOnBothAsync(
        HttpClient owner,
        Guid tenantId)
    {
        (Guid whatsAppId, _, Guid agentId) = await WhatsAppApiFixture.ConnectAsync(owner, tenantId, WhatsAppApiFixture.Number());

        HttpResponseMessage created = await owner.PostAsJsonAsync(
            WebChatApiFixture.Relative($"/api/tenants/{tenantId}/website-chat"),
            new { aiAgentId = agentId });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        return (agentId, (await ReadAsync(created)).GetProperty("id").GetGuid(), whatsAppId);
    }

    private static Uri Agent(Guid tenantId, Guid agentId) =>
        WebChatApiFixture.Relative($"/api/tenants/{tenantId}/ai-agents/{agentId}");

    private static async Task<JsonElement> DetailAsync(HttpClient client, Guid tenantId, Guid agentId)
    {
        HttpResponseMessage response = await client.GetAsync(Agent(tenantId, agentId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await ReadAsync(response);
    }

    private static async Task<JsonElement> ListItemAsync(HttpClient client, Guid tenantId, Guid agentId)
    {
        HttpResponseMessage response = await client.GetAsync(
            WebChatApiFixture.Relative($"/api/tenants/{tenantId}/ai-agents?page=1&pageSize=100"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await ReadAsync(response)).GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("id").GetGuid() == agentId);
    }

    private static string[] Kinds(JsonElement channels) =>
        [.. channels.EnumerateArray().Select(channel => channel.GetProperty("kind").GetString()!)];

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.Clone();
    }
}
