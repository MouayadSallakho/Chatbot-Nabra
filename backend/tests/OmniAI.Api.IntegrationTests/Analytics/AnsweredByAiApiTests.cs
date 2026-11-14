using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OmniAI.Api.IntegrationTests.WebChat;

namespace OmniAI.Api.IntegrationTests.Analytics;

/// <summary>
/// Phase 39.1 (BA39-1, owner decision Q7): the conversations started in the period
/// that the AI answered with no person stepping in, beside today's summary and in
/// the previous period's.
/// </summary>
/// <remarks>
/// Every conversation here is produced by a visitor on the real public API, every
/// AI reply by the recording generator, and every takeover and reply by the admin
/// API, so the figure is computed over rows the runtime actually wrote.
/// </remarks>
public sealed class AnsweredByAiApiTests(WebChatApiFixture fixture) : IClassFixture<WebChatApiFixture>
{
    private const string ChatRoot = "/api/public/webchat";

    private static int _visitors;

    [Fact]
    public async Task Only_a_conversation_the_ai_answered_with_nobody_stepping_in_counts()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Counted");
        fixture.ResetGenerator().Reply = "The answer.";

        // Counted: the AI answered, nobody stepped in.
        await StartAsync(key, "Answered by the AI");

        // A person replied.
        Guid replied = await StartAsync(key, "A person replies");
        await HandoffAsync(owner, tenantId, replied, "take-over");
        HttpResponseMessage reply = await owner.PostAsJsonAsync(
            WebChatApiFixture.Relative($"/api/tenants/{tenantId}/conversations/{replied}/messages"),
            new { clientMessageId = Guid.NewGuid().ToString("N"), text = "A person here." });
        Assert.Equal(HttpStatusCode.OK, reply.StatusCode);

        // A person took it over and handed it back without a word: still stepped in.
        Guid returned = await StartAsync(key, "Taken over and returned");
        await HandoffAsync(owner, tenantId, returned, "take-over");
        await HandoffAsync(owner, tenantId, returned, "return-to-ai");

        // With a person now.
        Guid held = await StartAsync(key, "Held by a person");
        await HandoffAsync(owner, tenantId, held, "take-over");

        // The AI never answered: its turn failed.
        fixture.Generator.PretendUnavailable = true;

        try
        {
            await StartAsync(key, "No answer", expectFailure: true);
        }
        finally
        {
            fixture.Generator.PretendUnavailable = false;
        }

        JsonElement body = await DashboardAsync(owner, tenantId, "range=7d&compare=previous");
        JsonElement summary = body.GetProperty("summary");

        Assert.Equal(5, summary.GetProperty("conversationsStarted").GetInt64());
        Assert.Equal(1, summary.GetProperty("conversationsAnsweredByAi").GetInt64());

        // The previous period carries the same figure; nothing started there.
        JsonElement previous = body.GetProperty("previous").GetProperty("summary");
        Assert.Equal(0, previous.GetProperty("conversationsAnsweredByAi").GetInt64());
        Assert.Equal(0, previous.GetProperty("conversationsStarted").GetInt64());

        // Without compare=previous the member is in the summary and no "previous" appears.
        JsonElement plain = await DashboardAsync(owner, tenantId, "range=7d");
        Assert.Equal(1, plain.GetProperty("summary").GetProperty("conversationsAnsweredByAi").GetInt64());
        Assert.False(plain.TryGetProperty("previous", out _));
    }

    [Fact]
    public async Task An_empty_workspace_answers_zero_and_another_workspace_counts_only_its_own()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Ours");
        fixture.ResetGenerator().Reply = "The answer.";

        await StartAsync(key, "Ours, answered");

        (HttpClient rival, Guid rivalTenantId, _) = await fixture.CreateOwnerAsync("Rival Analytics Trading");

        Assert.Equal(0, (await DashboardAsync(rival, rivalTenantId, "range=30d")).GetProperty("summary").GetProperty("conversationsAnsweredByAi").GetInt64());
        Assert.Equal(1, (await DashboardAsync(owner, tenantId, "range=30d")).GetProperty("summary").GetProperty("conversationsAnsweredByAi").GetInt64());

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await rival.GetAsync(WebChatApiFixture.Relative($"/api/tenants/{tenantId}/analytics/dashboard"))).StatusCode);
    }

    // ---- helpers --------------------------------------------------------------

    private sealed record Started(Guid ConversationId);

    private async Task<Guid> StartAsync(string key, string text, bool expectFailure = false)
    {
        HttpClient visitor = await fixture.CreateVisitorAsync(
            new IPAddress([203, 0, 113, (byte)(Interlocked.Increment(ref _visitors) % 250 + 1)]));

        HttpResponseMessage started = await visitor.PostAsync(WebChatApiFixture.Relative($"{ChatRoot}/{key}/conversations"), content: null);
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);

        Guid conversationId = (await started.Content.ReadFromJsonAsync<Started>())!.ConversationId;

        HttpResponseMessage sent = await visitor.PostAsJsonAsync(
            WebChatApiFixture.Relative($"{ChatRoot}/conversations/{conversationId}/messages"),
            new { clientMessageId = Guid.NewGuid().ToString("N"), text });

        Assert.Equal(!expectFailure, sent.IsSuccessStatusCode);

        return conversationId;
    }

    private static async Task HandoffAsync(HttpClient owner, Guid tenantId, Guid conversationId, string action)
    {
        HttpResponseMessage read = await owner.GetAsync(WebChatApiFixture.Relative($"/api/tenants/{tenantId}/conversations/{conversationId}"));

        using HttpRequestMessage request = new(HttpMethod.Post, WebChatApiFixture.Relative($"/api/tenants/{tenantId}/conversations/{conversationId}/{action}"));
        request.Headers.TryAddWithoutValidation("If-Match", read.Headers.ETag!.ToString());

        Assert.Equal(HttpStatusCode.OK, (await owner.SendAsync(request)).StatusCode);
    }

    private static async Task<JsonElement> DashboardAsync(HttpClient client, Guid tenantId, string query)
    {
        HttpResponseMessage response = await client.GetAsync(WebChatApiFixture.Relative($"/api/tenants/{tenantId}/analytics/dashboard?{query}"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }
}
