using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using OmniAI.Api.IntegrationTests.AiAgents;
using OmniAI.Api.IntegrationTests.WebChat;

namespace OmniAI.Api.IntegrationTests.Realtime;

/// <summary>
/// A customer message whose AI reply failed reaches an open inbox without a
/// reload, on this instance and on any other.
/// </summary>
/// <remarks>
/// <strong>Everything here is the real pipeline.</strong> The visitor sends
/// through the hosted chat endpoint, the provider double refuses as an
/// unconfigured provider does, and a signed-in member listens on the real hub
/// over a WebSocket with the session cookie the real sign-in issued. The only
/// double is the AI provider, which is the one thing these tests must never
/// reach.
/// <para>
/// <strong>The second instance is the backplane proof.</strong> Two hosts share
/// one PostgreSQL and one Redis, exactly as two API containers behind a load
/// balancer do. The failure is raised on one and must reach a member connected
/// to the other, which only happens if the Redis backplane, and the MessagePack
/// serialisation it uses between instances, work.
/// </para>
/// </remarks>
public sealed class FailedTurnRealtimeApiTests(WebChatApiFixture fixture)
    : IClassFixture<WebChatApiFixture>
{
    private const string ChatRoot = "/api/public/webchat";

    private static readonly TimeSpan Arrival = TimeSpan.FromSeconds(20);

    /// <summary>How long a listener is given to hear something it must not.</summary>
    private static readonly TimeSpan Silence = TimeSpan.FromSeconds(2);

    private sealed record StartedConversation(Guid ConversationId, string AgentName);

    [Fact]
    public async Task A_failed_ai_turn_reaches_an_open_inbox()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Outage");

        await using InboxHubClient inbox = await InboxHubClient.SignInAndConnectAsync(
            fixture.Application,
            await AiAgentApiFixture.CurrentEmailAsync(owner));

        Assert.Null(await inbox.InvokeAsync("Subscribe", tenantId));

        fixture.ResetGenerator().PretendUnavailable = true;

        try
        {
            HttpClient visitor = await fixture.CreateVisitorAsync();
            StartedConversation conversation = await StartAsync(visitor, key);

            // Nothing about this conversation has been announced yet, so what
            // arrives below is the failed turn and nothing earlier.
            Assert.False(await inbox.WaitForChangeAsync(conversation.ConversationId, Silence));

            using HttpResponseMessage failed =
                await SendAsync(visitor, conversation, "m1", "Anyone there?");

            Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
            Assert.True(await inbox.WaitForChangeAsync(conversation.ConversationId, Arrival));

            // Repeating the same message is refused as already failed, and is not
            // announced a second time.
            using HttpResponseMessage replayed =
                await SendAsync(visitor, conversation, "m1", "Anyone there?");

            Assert.False(replayed.IsSuccessStatusCode);
            Assert.False(await inbox.WaitForChangeAsync(conversation.ConversationId, Silence));
        }
        finally
        {
            fixture.ResetGenerator();
        }
    }

    [Fact]
    public async Task Another_workspace_hears_nothing_of_it()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Outage");

        (HttpClient rival, Guid rivalTenantId, _) = await fixture.CreateOwnerAsync("Rival Trading");

        await using InboxHubClient inbox = await InboxHubClient.SignInAndConnectAsync(
            fixture.Application,
            await AiAgentApiFixture.CurrentEmailAsync(owner));

        await using InboxHubClient outsider = await InboxHubClient.SignInAndConnectAsync(
            fixture.Application,
            await AiAgentApiFixture.CurrentEmailAsync(rival));

        Assert.Null(await inbox.InvokeAsync("Subscribe", tenantId));
        Assert.Null(await outsider.InvokeAsync("Subscribe", rivalTenantId));

        // And the rival cannot simply ask for the other workspace's events.
        Assert.NotNull(await outsider.InvokeAsync("Subscribe", tenantId));

        fixture.ResetGenerator().PretendUnavailable = true;

        try
        {
            HttpClient visitor = await fixture.CreateVisitorAsync();
            StartedConversation conversation = await StartAsync(visitor, key);

            using HttpResponseMessage failed =
                await SendAsync(visitor, conversation, "m1", "Anyone there?");

            Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
            Assert.True(await inbox.WaitForChangeAsync(conversation.ConversationId, Arrival));
            Assert.False(await outsider.WaitForChangeAsync(conversation.ConversationId, Silence));
        }
        finally
        {
            fixture.ResetGenerator();
        }
    }

    [Fact]
    public async Task A_member_without_conversations_view_cannot_listen()
    {
        (_, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        string email = ApiAuthentication.UniqueEmail();
        await fixture.CreateMemberWithEmailAsync(tenantId, email, "analytics.view");

        await using InboxHubClient member =
            await InboxHubClient.SignInAndConnectAsync(fixture.Application, email);

        Assert.NotNull(await member.InvokeAsync("Subscribe", tenantId));
    }

    [Fact]
    public async Task A_failed_turn_on_one_instance_reaches_an_inbox_on_the_other()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Outage");

        await using WebApplicationFactory<Program> second = fixture.CreateSecondInstance();

        // Connected to the second instance only.
        await using InboxHubClient inbox = await InboxHubClient.SignInAndConnectAsync(
            second,
            await AiAgentApiFixture.CurrentEmailAsync(owner));

        Assert.Null(await inbox.InvokeAsync("Subscribe", tenantId));

        fixture.ResetGenerator().PretendUnavailable = true;

        try
        {
            // The visitor talks to the first instance, which raises the event.
            HttpClient visitor = await fixture.CreateVisitorAsync();
            StartedConversation conversation = await StartAsync(visitor, key);

            using HttpResponseMessage failed =
                await SendAsync(visitor, conversation, "m1", "Anyone there?");

            Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
            Assert.True(await inbox.WaitForChangeAsync(conversation.ConversationId, Arrival));
        }
        finally
        {
            fixture.ResetGenerator();
        }
    }

    private static Uri Messages(StartedConversation conversation) =>
        WebChatApiFixture.Relative(
            $"{ChatRoot}/conversations/{conversation.ConversationId}/messages");

    private static async Task<StartedConversation> StartAsync(HttpClient visitor, string key)
    {
        using HttpResponseMessage started = await visitor.PostAsync(
            WebChatApiFixture.Relative($"{ChatRoot}/{key}/conversations"),
            content: null);

        Assert.Equal(HttpStatusCode.OK, started.StatusCode);

        return await started.Content.ReadFromJsonAsync<StartedConversation>()
            ?? throw new InvalidOperationException("No conversation was returned.");
    }

    private static Task<HttpResponseMessage> SendAsync(
        HttpClient visitor,
        StartedConversation conversation,
        string clientMessageId,
        string text) =>
        visitor.PostAsJsonAsync(
            Messages(conversation),
            new { clientMessageId, text });
}
