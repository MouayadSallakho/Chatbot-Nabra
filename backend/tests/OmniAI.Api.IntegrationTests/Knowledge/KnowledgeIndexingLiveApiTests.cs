using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OmniAI.Api.IntegrationTests.AiAgents;
using OmniAI.Api.IntegrationTests.Authorization;
using OmniAI.Api.IntegrationTests.Realtime;
using OmniAI.Application.Ai.Usage;
using OmniAI.Application.Knowledge;
using OmniAI.Domain.Knowledge;

namespace OmniAI.Api.IntegrationTests.Knowledge;

/// <summary>
/// Phase 38.7a over HTTP and the workspace hub: an index attempt outlives the
/// request that asked for it (BA-10), and knowledge viewers hear it start and
/// settle (BA-11).
/// </summary>
/// <remarks>
/// The embedding provider here answers real 1,536-wide vectors, but only when a
/// test lets it: that is how an attempt is held open while its request is
/// abandoned, a second request is refused, or a listener hears the start.
/// </remarks>
public sealed class KnowledgeIndexingLiveApiTests(KnowledgeIndexingApiFixture fixture)
    : IClassFixture<KnowledgeIndexingApiFixture>
{
    private static readonly TimeSpan Arrival = TimeSpan.FromSeconds(20);

    /// <summary>How long a listener is given to hear something it must not.</summary>
    private static readonly TimeSpan Silence = TimeSpan.FromSeconds(2);

    // ---- BA-10 ------------------------------------------------------------------

    [Fact]
    public async Task An_abandoned_index_request_still_settles_and_leaves_its_index()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (Guid id, string tag) = await TypedAsync(owner, tenantId);

        TaskCompletionSource provider = new(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Embeddings.Reset(hold: provider.Task);

        try
        {
            using CancellationTokenSource browser = new();
            Task<HttpResponseMessage> pressed = IndexAsync(owner, tenantId, id, tag, browser.Token);

            await fixture.Embeddings.Reached.WaitAsync(Arrival);

            // The tab that pressed Index is reloaded. (The test host's client
            // completes a cancelled call only once the server's handler ends, so
            // it is collected at the very end.)
            await browser.CancelAsync();
            await Task.Delay(TimeSpan.FromMilliseconds(200));

            // The work never held the browser's token.
            Assert.False(fixture.Embeddings.LastToken.IsCancellationRequested);

            // Meanwhile the single flight still refuses a second attempt.
            HttpResponseMessage second = await IndexAsync(owner, tenantId, id, tag, CancellationToken.None);

            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
            Assert.Equal("knowledge.indexing.in_progress", await CodeAsync(second));

            provider.SetResult();

            // The attempt settles, and the next read shows it with the index.
            JsonElement settled = await WaitForAttemptAsync(owner, tenantId, id, "Succeeded");

            Assert.Equal("Ready", settled.GetProperty("readiness").GetString());
            Assert.Equal(JsonValueKind.Null, settled.GetProperty("lastIndexAttempt").GetProperty("failureCode").ValueKind);

            try
            {
                using HttpResponseMessage abandoned = await pressed.WaitAsync(Arrival);
            }
            catch (OperationCanceledException)
            {
                // What the abandoned caller sees is not under test: nobody is there.
            }
        }
        finally
        {
            provider.TrySetResult();
            fixture.Embeddings.Reset();
        }
    }

    [Fact]
    public async Task A_caller_still_waiting_gets_the_same_answer_as_before()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (Guid id, string tag) = await TypedAsync(owner, tenantId);

        fixture.Embeddings.Reset();

        HttpResponseMessage indexed = await IndexAsync(owner, tenantId, id, tag, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, indexed.StatusCode);
        Assert.NotNull(indexed.Headers.ETag);

        JsonElement body = JsonDocument.Parse(await indexed.Content.ReadAsStringAsync()).RootElement;

        Assert.False(body.GetProperty("alreadyCurrent").GetBoolean());
        Assert.True(body.GetProperty("chunkCount").GetInt32() > 0);
    }

    // ---- BA-11 ------------------------------------------------------------------

    [Fact]
    public async Task A_knowledge_viewer_hears_an_attempt_start_and_settle()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (Guid id, string tag) = await TypedAsync(owner, tenantId);

        string email = ApiAuthentication.UniqueEmail();
        await fixture.CreateMemberWithEmailAsync(tenantId, email, "knowledge.view");

        await using InboxHubClient viewer = await InboxHubClient.SignInAndConnectAsync(fixture.Application, email);

        Assert.Null(await viewer.InvokeAsync("SubscribeKnowledge", tenantId));

        TaskCompletionSource provider = new(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Embeddings.Reset(hold: provider.Task);

        try
        {
            Task<HttpResponseMessage> pressed = IndexAsync(owner, tenantId, id, tag, CancellationToken.None);

            // Started: heard while the provider is still being waited on.
            Assert.True(await viewer.WaitForKnowledgeAsync(id, Arrival));
            Assert.False(pressed.IsCompleted);

            provider.SetResult();

            Assert.Equal(HttpStatusCode.OK, (await pressed).StatusCode);

            // Settled.
            Assert.True(await viewer.WaitForKnowledgeAsync(id, Arrival));
            Assert.False(await viewer.WaitForKnowledgeAsync(id, Silence));
        }
        finally
        {
            provider.TrySetResult();
            fixture.Embeddings.Reset();
        }
    }

    [Fact]
    public async Task A_member_without_knowledge_view_and_an_outsider_are_refused_and_hear_nothing()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (Guid id, string tag) = await TypedAsync(owner, tenantId);
        (HttpClient rival, _, _) = await fixture.CreateOwnerAsync("Rival Knowledge Trading");

        string inboxOnly = ApiAuthentication.UniqueEmail();
        await fixture.CreateMemberWithEmailAsync(tenantId, inboxOnly, "conversations.view");

        await using InboxHubClient member = await InboxHubClient.SignInAndConnectAsync(fixture.Application, inboxOnly);
        await using InboxHubClient outsider = await InboxHubClient.SignInAndConnectAsync(
            fixture.Application,
            await AiAgentApiFixture.CurrentEmailAsync(rival));

        // Refused with the inbox's own message, for both. (SignalR prefixes a hub
        // exception's message with its own sentence.)
        Assert.Contains("HubException: No such workspace.", await member.InvokeAsync("SubscribeKnowledge", tenantId) ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("HubException: No such workspace.", await outsider.InvokeAsync("SubscribeKnowledge", tenantId) ?? string.Empty, StringComparison.Ordinal);

        // The member's inbox subscription is a different group: it carries no
        // knowledge event either.
        Assert.Null(await member.InvokeAsync("Subscribe", tenantId));

        fixture.Embeddings.Reset();

        Assert.Equal(HttpStatusCode.OK, (await IndexAsync(owner, tenantId, id, tag, CancellationToken.None)).StatusCode);

        Assert.False(await member.WaitForKnowledgeAsync(id, Silence));
        Assert.False(await outsider.WaitForKnowledgeAsync(id, Silence));
    }

    // ---- Helpers ------------------------------------------------------------------

    private static Uri Document(Guid tenantId, Guid id) =>
        WebChat.WebChatApiFixture.Relative($"/api/tenants/{tenantId}/knowledge/{id}");

    private static async Task<(Guid Id, string Tag)> TypedAsync(HttpClient owner, Guid tenantId)
    {
        HttpResponseMessage created = await owner.PostAsJsonAsync(
            WebChat.WebChatApiFixture.Relative($"/api/tenants/{tenantId}/knowledge"),
            new { title = "Returns policy", content = "Returns are accepted within thirty days of delivery." });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        Guid id = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        return (id, created.Headers.ETag!.ToString());
    }

    private static async Task<HttpResponseMessage> IndexAsync(
        HttpClient client,
        Guid tenantId,
        Guid id,
        string tag,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, $"{Document(tenantId, id)}/index");
        request.Headers.IfMatch.Add(EntityTagHeaderValue.Parse(tag));

        return await client.SendAsync(request, cancellationToken);
    }

    private static async Task<JsonElement> WaitForAttemptAsync(HttpClient owner, Guid tenantId, Guid id, string status)
    {
        using CancellationTokenSource timeout = new(Arrival);

        while (true)
        {
            HttpResponseMessage read = await owner.GetAsync(Document(tenantId, id), timeout.Token);
            JsonElement document = JsonDocument.Parse(await read.Content.ReadAsStringAsync(timeout.Token)).RootElement.Clone();

            if (document.GetProperty("lastIndexAttempt") is { ValueKind: JsonValueKind.Object } attempt
                && attempt.GetProperty("status").GetString() == status)
            {
                return document;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token);
        }
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString();
}

/// <summary>The real host, with an embedding provider a test can hold open.</summary>
public sealed class KnowledgeIndexingApiFixture : RoleApiFixture
{
    internal HoldingEmbeddingProvider Embeddings { get; } = new();

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        base.ConfigureTestServices(services);

        services.Replace(ServiceDescriptor.Singleton<ITextEmbeddingGenerator>(Embeddings));
    }
}

/// <summary>
/// Answers real vectors of the stored width, after an optional hold, and remembers
/// the token each call was given.
/// </summary>
internal sealed class HoldingEmbeddingProvider : ITextEmbeddingGenerator
{
    private const int Width = 1_536;

    private TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private Task? _hold;

    public EmbeddingProfile Profile { get; } = EmbeddingProfile.Create(
        EmbeddingProfileId.Create($"test:holding:{Width}"),
        Width,
        maxBatchSize: 32,
        maxInputCharacters: 8_000);

    /// <summary>Completes when a call first reaches the provider.</summary>
    public Task Reached => _reached.Task;

    public CancellationToken LastToken { get; private set; }

    public void Reset(Task? hold = null)
    {
        _hold = hold;
        _reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        LastToken = default;
    }

    public async Task<IReadOnlyList<EmbeddingVector>> GenerateAsync(
        IReadOnlyList<string> inputs,
        AiUsageContext usage,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        LastToken = cancellationToken;
        _reached.TrySetResult();

        if (_hold is Task hold)
        {
            await hold.WaitAsync(cancellationToken);
        }

        return
        [
            .. inputs.Select((_, index) => EmbeddingVector.Create(
                [.. Enumerable.Range(0, Width).Select(dimension => dimension == index % Width ? 1f : 0.001f)],
                Width)),
        ];
    }
}
