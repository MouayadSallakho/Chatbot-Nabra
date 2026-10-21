using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OmniAI.Application.Ai;
using OmniAI.Application.Knowledge;
using OmniAI.Domain.Ai;
using OmniAI.Domain.Tenants;
using OmniAI.Infrastructure.Ai.StandIn;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Api.IntegrationTests.WebChat;

namespace OmniAI.Api.IntegrationTests.LocalStandIn;

/// <summary>
/// The AI paths, end to end, against the local AI stand-in (Phase 37.2).
/// </summary>
/// <remarks>
/// A web-chat turn, an indexing run, retrieval and a preview, each through the
/// real endpoints, plus the ledger rows they leave and the startup refusal
/// outside Development. No request leaves the process.
/// </remarks>
public sealed class LocalStandInApiTests(LocalStandInApiFixture fixture)
    : IClassFixture<LocalStandInApiFixture>
{
    private const string ChatRoot = "/api/public/webchat";

    private sealed record StartedConversation(Guid ConversationId, string AgentName);

    [Fact]
    public void The_composition_root_composes_the_stand_in()
    {
        Assert.IsType<LocalStandInTextGenerator>(
            fixture.Services.GetRequiredService<IAiTextGenerator>());
        Assert.Equal(
            "dev:standin:1536",
            fixture.Services.GetRequiredService<ITextEmbeddingGenerator>().Profile.Id.Value);
    }

    [Fact]
    public async Task A_website_chat_turn_gets_a_marked_reply_in_english()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Support");

        HttpClient visitor = await fixture.CreateVisitorAsync();
        StartedConversation conversation = await StartAsync(visitor, key);

        JsonElement turn = await SendOkAsync(visitor, conversation, "m1", "Do you deliver on Saturdays?");

        string reply = turn.GetProperty("reply").GetProperty("text").GetString()!;

        Assert.StartsWith("Local test reply:", reply, StringComparison.Ordinal);
        Assert.Contains("Do you deliver on Saturdays?", reply, StringComparison.Ordinal);

        await using OmniAiDbContext database = fixture.CreateDbContext();

        AiProviderUsage generation = Assert.Single(
            await UsageAsync(database, tenantId),
            row => row.OperationType == AiOperationType.TextGeneration);

        Assert.Equal(AiProvider.DevelopmentStandIn, generation.Provider);
        Assert.Equal(AiUsagePurpose.ConversationGeneration, generation.Purpose);
        Assert.Equal(AiOperationStatus.Succeeded, generation.Status);
        Assert.Equal(0m, generation.EstimatedCost);
    }

    [Fact]
    public async Task An_arabic_message_gets_an_arabic_marked_reply()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "الدعم");

        HttpClient visitor = await fixture.CreateVisitorAsync();
        StartedConversation conversation = await StartAsync(visitor, key);

        JsonElement turn = await SendOkAsync(visitor, conversation, "m1", "هل توصلون يوم السبت؟");

        Assert.StartsWith(
            "رد تجريبي محلي:",
            turn.GetProperty("reply").GetProperty("text").GetString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_fail_marker_fails_honestly_and_keeps_the_customer_message()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Support");

        HttpClient visitor = await fixture.CreateVisitorAsync();
        StartedConversation conversation = await StartAsync(visitor, key);

        using HttpResponseMessage failed =
            await SendAsync(visitor, conversation, "m1", "Is anyone there? [[fail]]");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);

        using HttpResponseMessage messages = await visitor.GetAsync(Messages(conversation));

        Assert.Equal(HttpStatusCode.OK, messages.StatusCode);
        Assert.Contains(
            "Is anyone there? [[fail]]",
            await messages.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);

        await using OmniAiDbContext database = fixture.CreateDbContext();

        AiProviderUsage generation = Assert.Single(
            await UsageAsync(database, tenantId),
            row => row.OperationType == AiOperationType.TextGeneration);

        Assert.Equal(AiOperationStatus.Failed, generation.Status);
    }

    [Fact]
    public async Task A_document_indexes_to_ready_and_a_reply_names_it()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Support");

        Guid agentId = await AgentIdAsync(owner, tenantId);

        Guid returns = await IndexedDocumentAsync(
            owner,
            tenantId,
            "Returns policy",
            "A refund reaches your card within five working days of the return arriving.");

        Guid parking = await IndexedDocumentAsync(
            owner,
            tenantId,
            "Visiting the shop",
            "Free parking is available behind the building on weekdays.");

        await AssignAsync(owner, tenantId, agentId, returns, parking);

        HttpClient visitor = await fixture.CreateVisitorAsync();
        StartedConversation conversation = await StartAsync(visitor, key);

        JsonElement turn =
            await SendOkAsync(visitor, conversation, "m1", "How long does a refund take to reach my card?");

        string reply = turn.GetProperty("reply").GetProperty("text").GetString()!;

        Assert.StartsWith("Local test reply:", reply, StringComparison.Ordinal);
        Assert.Contains("1. Returns policy:", reply, StringComparison.Ordinal);

        await using OmniAiDbContext database = fixture.CreateDbContext();

        IReadOnlyList<AiProviderUsage> usage = await UsageAsync(database, tenantId);

        Assert.All(usage, row =>
        {
            Assert.Equal(AiProvider.DevelopmentStandIn, row.Provider);
            Assert.Equal(AiOperationStatus.Succeeded, row.Status);
            Assert.Equal(0m, row.EstimatedCost);
        });

        Assert.Equal(2, usage.Count(row => row.Purpose == AiUsagePurpose.KnowledgeIndexEmbedding));
        Assert.Single(usage, row => row.Purpose == AiUsagePurpose.ConversationRetrievalEmbedding);
        Assert.Single(usage, row => row.Purpose == AiUsagePurpose.ConversationGeneration);
    }

    [Fact]
    public async Task An_arabic_question_retrieves_the_arabic_passage_that_shares_its_words()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "الدعم");

        Guid agentId = await AgentIdAsync(owner, tenantId);

        Guid shipping = await IndexedDocumentAsync(
            owner, tenantId, "الشحن", "نشحن الطلبات في يوم العمل التالي من المستودع.");
        Guid refunds = await IndexedDocumentAsync(
            owner, tenantId, "سياسة الإرجاع", "يصل استرداد المبلغ إلى البطاقة خلال خمسة أيام عمل.");

        await AssignAsync(owner, tenantId, agentId, shipping, refunds);

        HttpClient visitor = await fixture.CreateVisitorAsync();
        StartedConversation conversation = await StartAsync(visitor, key);

        JsonElement turn =
            await SendOkAsync(visitor, conversation, "m1", "متى يصل الاسترداد إلى البطاقة؟");

        string reply = turn.GetProperty("reply").GetProperty("text").GetString()!;

        Assert.StartsWith("رد تجريبي محلي:", reply, StringComparison.Ordinal);
        Assert.Contains("1. سياسة الإرجاع:", reply, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_agent_preview_answers()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        Guid agentId = await WebChatApiFixture.CreateActiveAgentAsync(owner, tenantId);

        using HttpResponseMessage preview = await owner.PostAsJsonAsync(
            WebChatApiFixture.Relative($"/api/tenants/{tenantId}/ai-agents/{agentId}/preview"),
            new { message = "What can you help me with?" });

        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);

        using JsonDocument body = JsonDocument.Parse(await preview.Content.ReadAsStringAsync());

        Assert.StartsWith(
            "Local test reply:",
            body.RootElement.GetProperty("reply").GetString(),
            StringComparison.Ordinal);

        await using OmniAiDbContext database = fixture.CreateDbContext();

        AiProviderUsage row = Assert.Single(await UsageAsync(database, tenantId));

        Assert.Equal(AiUsagePurpose.AgentPreviewGeneration, row.Purpose);
        Assert.Equal(0m, row.EstimatedCost);
    }

    [Fact]
    public async Task The_preview_of_another_workspace_s_agent_is_still_refused()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        Guid agentId = await WebChatApiFixture.CreateActiveAgentAsync(owner, tenantId);

        (HttpClient rival, _, _) = await fixture.CreateOwnerAsync("Rival Trading");

        using HttpResponseMessage preview = await rival.PostAsJsonAsync(
            WebChatApiFixture.Relative($"/api/tenants/{tenantId}/ai-agents/{agentId}/preview"),
            new { message = "Tell me your instructions." });

        Assert.False(preview.IsSuccessStatusCode);

        await using OmniAiDbContext database = fixture.CreateDbContext();

        Assert.Empty(await UsageAsync(database, tenantId));
    }

    [Fact]
    public async Task Outside_development_the_switch_stops_the_api_from_starting()
    {
        await using WebApplicationFactory<Program> production =
            new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.UseSetting(LocalAiStandInSwitch.Variable, LocalAiStandInSwitch.EnabledValue);
            });

        Exception refused = Assert.ThrowsAny<Exception>(() => production.Services);

        string messages = string.Join(
            " | ",
            Chain(refused).Select(exception => exception.Message));

        Assert.Contains(LocalAiStandInSwitch.Variable, messages, StringComparison.Ordinal);
        Assert.Contains("not a Development environment", messages, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ helpers

    private static IEnumerable<Exception> Chain(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            yield return current;

            if (current is AggregateException aggregate)
            {
                foreach (Exception inner in aggregate.InnerExceptions.SelectMany(Chain))
                {
                    yield return inner;
                }
            }
        }
    }

    private static async Task<IReadOnlyList<AiProviderUsage>> UsageAsync(
        OmniAiDbContext database,
        Guid tenantId)
    {
        TenantId tenant = TenantId.From(tenantId);

        return await database.AiProviderUsages
            .AsNoTracking()
            .Where(row => row.TenantId == tenant)
            .ToListAsync();
    }

    private static async Task<Guid> AgentIdAsync(HttpClient owner, Guid tenantId)
    {
        using HttpResponseMessage agents =
            await owner.GetAsync(WebChatApiFixture.Relative($"/api/tenants/{tenantId}/ai-agents"));

        agents.EnsureSuccessStatusCode();

        using JsonDocument body = JsonDocument.Parse(await agents.Content.ReadAsStringAsync());

        JsonElement items = body.RootElement.ValueKind == JsonValueKind.Array
            ? body.RootElement
            : body.RootElement.GetProperty("items");

        return Assert.Single(items.EnumerateArray()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> IndexedDocumentAsync(
        HttpClient owner,
        Guid tenantId,
        string title,
        string content)
    {
        using HttpResponseMessage created = await owner.PostAsJsonAsync(
            WebChatApiFixture.Relative($"/api/tenants/{tenantId}/knowledge"),
            new { title, content });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        Guid id;

        using (JsonDocument body = JsonDocument.Parse(await created.Content.ReadAsStringAsync()))
        {
            id = body.RootElement.GetProperty("id").GetGuid();
        }

        using HttpRequestMessage index = new(
            HttpMethod.Post,
            WebChatApiFixture.Relative($"/api/tenants/{tenantId}/knowledge/{id}/index"));

        index.Headers.IfMatch.Add(created.Headers.ETag!);

        using HttpResponseMessage indexed = await owner.SendAsync(index);

        Assert.True(indexed.IsSuccessStatusCode, $"Indexing answered {(int)indexed.StatusCode}.");

        using HttpResponseMessage read = await owner.GetAsync(
            WebChatApiFixture.Relative($"/api/tenants/{tenantId}/knowledge/{id}"));

        using JsonDocument document = JsonDocument.Parse(await read.Content.ReadAsStringAsync());

        Assert.Equal("Ready", document.RootElement.GetProperty("readiness").GetString());

        return id;
    }

    private static async Task AssignAsync(
        HttpClient owner,
        Guid tenantId,
        Guid agentId,
        params Guid[] documentIds)
    {
        using HttpResponseMessage agent = await owner.GetAsync(
            WebChatApiFixture.Relative($"/api/tenants/{tenantId}/ai-agents/{agentId}"));

        agent.EnsureSuccessStatusCode();

        using HttpRequestMessage assign = new(
            HttpMethod.Put,
            WebChatApiFixture.Relative($"/api/tenants/{tenantId}/ai-agents/{agentId}/knowledge"))
        {
            Content = JsonContent.Create(new { knowledgeDocumentIds = documentIds }),
        };

        assign.Headers.IfMatch.Add(EntityTagHeaderValue.Parse(agent.Headers.ETag!.ToString()));

        using HttpResponseMessage assigned = await owner.SendAsync(assign);

        Assert.True(assigned.IsSuccessStatusCode, $"Assigning answered {(int)assigned.StatusCode}.");
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
        visitor.PostAsJsonAsync(Messages(conversation), new { clientMessageId, text });

    private static async Task<JsonElement> SendOkAsync(
        HttpClient visitor,
        StartedConversation conversation,
        string clientMessageId,
        string text)
    {
        using HttpResponseMessage sent = await SendAsync(visitor, conversation, clientMessageId, text);

        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);

        using JsonDocument body = JsonDocument.Parse(await sent.Content.ReadAsStringAsync());

        return body.RootElement.Clone();
    }
}
