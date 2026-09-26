using System.ClientModel.Primitives;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OmniAI.Api.IntegrationTests.WebChat;
using OmniAI.Application.Knowledge;
using OmniAI.Infrastructure.Knowledge;
using Xunit.Abstractions;

namespace OmniAI.Api.IntegrationTests.Performance;

/// <summary>
/// Phase 34: Knowledge indexing and retrieval under concurrency, with the real
/// embedding adapter and ledger over a deterministic socket.
/// </summary>
[Trait("Category", "Load")]
[Collection(LoadScenarios.Name)]
public sealed class KnowledgeLoadTests(KnowledgeLoadApiFixture api, ITestOutputHelper output)
    : IClassFixture<KnowledgeLoadApiFixture>
{
    [Fact]
    public async Task Concurrent_indexing_settles_every_attempt_once_and_an_unchanged_document_costs_nothing()
    {
        api.ResetProvider();
        LatencyEmbeddingHandler embeddings = api.ResetEmbeddings();

        LoadWorkspace workspace = await api.CreateWorkspaceAsync();
        (Guid Id, string ETag)[] documents = await Task.WhenAll(Enumerable.Range(0, 24)
            .Select(i => CreateDocumentAsync(workspace, $"Policy {i}", Paragraphs($"topic{i}", 5_000))));

        Stopwatch timer = Stopwatch.StartNew();
        HttpStatusCode[] statuses;

        await using (ConnectionSampler sampler = api.SampleConnections())
        {
            statuses = await Task.WhenAll(documents.Select(d => IndexAsync(workspace, d)));
            output.WriteLine(sampler.ToString());
        }

        timer.Stop();
        output.WriteLine($"24 documents indexed in {timer.ElapsedMilliseconds} ms; embedding calls {embeddings.Calls}, inputs {embeddings.Inputs}, max in flight {embeddings.MaxInFlight}");

        Assert.All(statuses, status => Assert.Equal(HttpStatusCode.OK, status));

        List<object?[]> attempts = await api.QueryAsync(
            "select status, count(*) from knowledge_index_attempts where tenant_id = @t group by 1",
            ("t", workspace.TenantId));
        Assert.Equal("Succeeded", Assert.Single(attempts)[0]);
        Assert.Equal(24L, attempts[0][1]);

        // One embedding batch per document (each is far under the batch size), and
        // exactly one ledger row per batch, settled.
        List<object?[]> ledger = await api.QueryAsync(
            "select status, count(*) from ai_provider_usages where tenant_id = @t and operation_type = 'Embedding' group by 1",
            ("t", workspace.TenantId));
        Assert.Equal(embeddings.Calls, (int)(long)Assert.Single(ledger)[1]!);
        Assert.Equal("Succeeded", ledger[0][0]);

        long chunks = (long)(await api.QueryAsync(
            "select count(*) from knowledge_chunks where tenant_id = @t", ("t", workspace.TenantId)))[0][0]!;
        Assert.Equal(embeddings.Inputs, (int)chunks);

        // Re-indexing an unchanged document: no attempt, no provider call.
        int callsBefore = embeddings.Calls;
        (Guid id, _) = documents[0];
        string etag = await ETagAsync(workspace, id);
        Assert.Equal(HttpStatusCode.OK, await IndexAsync(workspace, (id, etag)));
        Assert.Equal(callsBefore, embeddings.Calls);
        Assert.Equal(24L, (await api.QueryAsync(
            "select count(*) from knowledge_index_attempts where tenant_id = @t", ("t", workspace.TenantId)))[0][0]);
    }

    [Fact]
    public async Task Indexing_one_document_several_times_at_once_never_duplicates_its_chunks()
    {
        api.ResetProvider();
        LatencyEmbeddingHandler embeddings = api.ResetEmbeddings();
        embeddings.Latency = TimeSpan.FromMilliseconds(300);

        LoadWorkspace workspace = await api.CreateWorkspaceAsync();
        (Guid Id, string ETag) document = await CreateDocumentAsync(workspace, "Shared", Paragraphs("shared", 6_000));

        HttpStatusCode[] statuses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => IndexAsync(workspace, document)));
        output.WriteLine($"statuses [{string.Join(",", statuses.Select(s => (int)s))}], embedding calls {embeddings.Calls}");

        Assert.All(statuses, status => Assert.True((int)status < 500, status.ToString()));
        Assert.Contains(HttpStatusCode.OK, statuses);

        List<object?[]> perOrdinal = await api.QueryAsync(
            "select ordinal, count(*) from knowledge_chunks where knowledge_document_id = @d group by 1 having count(*) > 1",
            ("d", document.Id));
        Assert.Empty(perOrdinal);

        // Every call the provider received is accounted for, and nothing is left open.
        List<object?[]> ledger = await api.QueryAsync(
            "select count(*), count(*) filter (where status = 'Started') from ai_provider_usages where tenant_id = @t and operation_type = 'Embedding'",
            ("t", workspace.TenantId));
        Assert.Equal((long)embeddings.Calls, ledger[0][0]);
        Assert.Equal(0L, ledger[0][1]);

        // Phase 34 finding, recorded rather than hidden: every concurrent request
        // for the same version records its own attempt and pays for its own batch
        // before the version check; the winner is Succeeded and each loser settles
        // truthfully as Failed / document_changed (no alert, no retry). Accounting
        // is exact; the cost is multiplied by duplicate concurrent requests.
        List<object?[]> attempts = await api.QueryAsync(
            "select status, coalesce(failure_code, ''), count(*) from knowledge_index_attempts where document_id = @d group by 1, 2 order by 1",
            ("d", document.Id));
        output.WriteLine(string.Join("; ", attempts.Select(row => $"{row[0]}/{row[1]} x{row[2]}")));

        Assert.DoesNotContain(attempts, row => (string)row[0]! == "Started");
        Assert.Equal(1L, attempts.Where(row => (string)row[0]! == "Succeeded").Sum(row => (long)row[2]!));
        Assert.Equal(
            (long)statuses.Count(status => status == HttpStatusCode.PreconditionFailed),
            attempts.Where(row => (string)row[1]! == "document_changed").Sum(row => (long)row[2]!));
    }

    [Fact]
    public async Task Retrieval_under_concurrent_turns_grounds_each_workspace_only_in_its_own_eligible_knowledge()
    {
        LatencyOpenAiHandler provider = api.ResetProvider();
        provider.Latency = TimeSpan.FromMilliseconds(200);
        api.ResetEmbeddings().Latency = TimeSpan.FromMilliseconds(50);

        string canaryA = $"ZEBRAA{Guid.NewGuid():N}"[..16];
        string canaryB = $"ZEBRAB{Guid.NewGuid():N}"[..16];
        string canaryDisabled = $"ZEBRAD{Guid.NewGuid():N}"[..16];
        string canaryUnindexed = $"ZEBRAU{Guid.NewGuid():N}"[..16];

        LoadWorkspace a = await PrepareKnowledgeAsync(canaryA, canaryDisabled, canaryUnindexed);
        LoadWorkspace b = await PrepareKnowledgeAsync(canaryB, null, null);

        LoadConversation[] inA = await Task.WhenAll(Enumerable.Range(0, 15).Select(_ => api.StartConversationAsync(a)));
        LoadConversation[] inB = await Task.WhenAll(Enumerable.Range(0, 15).Select(_ => api.StartConversationAsync(b)));

        TurnResult[] results = await Task.WhenAll(inA.Concat(inB)
            .Select(c => LoadApiFixture.SendTurnAsync(c, text: "What is the refund policy for returns?")));

        output.WriteLine(ConversationConcurrencyLoadTests.Describe(results));
        Assert.All(results, result => Assert.Equal(HttpStatusCode.OK, result.Status));

        string[] bodies = [.. provider.Bodies];
        Assert.Equal(30, bodies.Length);

        // Grounded: each workspace's turns carried its own knowledge ...
        Assert.Equal(15, bodies.Count(body => body.Contains(canaryA, StringComparison.Ordinal)));
        Assert.Equal(15, bodies.Count(body => body.Contains(canaryB, StringComparison.Ordinal)));

        // ... and never the other's, a disabled document's, or an unindexed one's.
        Assert.DoesNotContain(bodies, body => body.Contains(canaryA, StringComparison.Ordinal) && body.Contains(canaryB, StringComparison.Ordinal));
        Assert.DoesNotContain(bodies, body => body.Contains(canaryDisabled, StringComparison.Ordinal));
        Assert.DoesNotContain(bodies, body => body.Contains(canaryUnindexed, StringComparison.Ordinal));

        // The knowledge context stays bounded however much is indexed.
        output.WriteLine($"largest provider request {bodies.Max(body => body.Length)} characters");
        Assert.All(bodies, body => Assert.True(body.Length < 64_000, $"request of {body.Length} characters"));
    }

    private async Task<LoadWorkspace> PrepareKnowledgeAsync(string canary, string? disabledCanary, string? unindexedCanary)
    {
        LoadWorkspace workspace = await api.CreateWorkspaceAsync();
        List<Guid> assigned = [];

        for (int i = 0; i < 6; i++)
        {
            (Guid id, string etag) = await CreateDocumentAsync(
                workspace,
                $"Refund policy {i}",
                $"Refund policy returns {canary}. " + Paragraphs($"refund{i}", 3_000));
            Assert.Equal(HttpStatusCode.OK, await IndexAsync(workspace, (id, etag)));
            assigned.Add(id);
        }

        if (disabledCanary is not null)
        {
            (Guid id, string etag) = await CreateDocumentAsync(workspace, "Old refund policy", $"Refund policy returns {disabledCanary}.");
            Assert.Equal(HttpStatusCode.OK, await IndexAsync(workspace, (id, etag)));
            string current = await ETagAsync(workspace, id);
            HttpRequestMessage disable = new(HttpMethod.Post, WebChatApiFixture.Relative($"/api/tenants/{workspace.TenantId}/knowledge/{id}/disable"));
            disable.Headers.TryAddWithoutValidation("If-Match", current);
            Assert.Equal(HttpStatusCode.OK, (await workspace.Owner.SendAsync(disable)).StatusCode);
            assigned.Add(id);
        }

        if (unindexedCanary is not null)
        {
            (Guid id, _) = await CreateDocumentAsync(workspace, "Draft refund policy", $"Refund policy returns {unindexedCanary}.");
            assigned.Add(id);
        }

        HttpResponseMessage agent = await workspace.Owner.GetAsync(
            WebChatApiFixture.Relative($"/api/tenants/{workspace.TenantId}/ai-agents/{workspace.AgentId}"));
        HttpRequestMessage assign = new(HttpMethod.Put, WebChatApiFixture.Relative($"/api/tenants/{workspace.TenantId}/ai-agents/{workspace.AgentId}/knowledge"))
        {
            Content = JsonContent.Create(new { knowledgeDocumentIds = assigned }),
        };
        assign.Headers.TryAddWithoutValidation("If-Match", agent.Headers.ETag!.ToString());
        HttpResponseMessage assigned1 = await workspace.Owner.SendAsync(assign);
        Assert.True(assigned1.IsSuccessStatusCode, assigned1.StatusCode.ToString());

        return workspace;
    }

    private static async Task<(Guid Id, string ETag)> CreateDocumentAsync(LoadWorkspace workspace, string title, string content)
    {
        HttpResponseMessage created = await workspace.Owner.PostAsJsonAsync(
            WebChatApiFixture.Relative($"/api/tenants/{workspace.TenantId}/knowledge"),
            new { title, content });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using JsonDocument body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());

        return (body.RootElement.GetProperty("id").GetGuid(), created.Headers.ETag!.ToString());
    }

    private static async Task<string> ETagAsync(LoadWorkspace workspace, Guid id) =>
        (await workspace.Owner.GetAsync(WebChatApiFixture.Relative($"/api/tenants/{workspace.TenantId}/knowledge/{id}")))
            .Headers.ETag!.ToString();

    private static async Task<HttpStatusCode> IndexAsync(LoadWorkspace workspace, (Guid Id, string ETag) document)
    {
        HttpRequestMessage index = new(
            HttpMethod.Post,
            WebChatApiFixture.Relative($"/api/tenants/{workspace.TenantId}/knowledge/{document.Id}/index"));
        index.Headers.TryAddWithoutValidation("If-Match", document.ETag);

        return (await workspace.Owner.SendAsync(index)).StatusCode;
    }

    private static string Paragraphs(string topic, int characters)
    {
        string sentence = $"This synthetic paragraph about {topic} exists for load testing only. ";

        return string.Concat(Enumerable.Repeat(sentence, (characters / sentence.Length) + 1))[..characters];
    }
}

/// <summary>The load fixture plus the real embedding adapter over a deterministic socket.</summary>
public sealed class KnowledgeLoadApiFixture : LoadApiFixture
{
    private readonly LatencyEmbeddingHandler _embeddings = new();

    internal LatencyEmbeddingHandler ResetEmbeddings()
    {
        _embeddings.Reset();

        return _embeddings;
    }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);

        EmbeddingOptions options = new();

        services.Replace(ServiceDescriptor.Singleton(options));
        services.Replace(ServiceDescriptor.Singleton(OpenAiEmbeddingClientFactory.Create(
            PlaceholderKey,
            options,
            new HttpClientPipelineTransport(new HttpClient(_embeddings)))));
        services.Replace(ServiceDescriptor.Singleton<ITextEmbeddingGenerator, OpenAiTextEmbeddingGenerator>());
    }
}
