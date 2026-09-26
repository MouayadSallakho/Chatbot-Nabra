using System.Net;
using System.Text.Json;
using OmniAI.Api.IntegrationTests.WebChat;
using Xunit.Abstractions;

namespace OmniAI.Api.IntegrationTests.Performance;

/// <summary>
/// Phase 34: correctness of the conversation runtime under genuine contention.
/// </summary>
/// <remarks>
/// <para>
/// Everything below runs against real PostgreSQL (the advisory lock, the global
/// ingestion sequence, the unique turn index), real Redis (the limiter) and the
/// real OpenAI adapter with its ledger, over a socket that answers slowly on
/// purpose so that overlap is observable. Earlier suites proved serialisation
/// with an in-memory lock double; these are the first to prove it with the lock
/// production actually takes.
/// </para>
/// <para>
/// Each scenario logs what it measured (latency, peak connections, overlap) so a
/// run doubles as evidence, but asserts only invariants, never a speed.
/// </para>
/// </remarks>
[Trait("Category", "Load")]
[Collection(LoadScenarios.Name)]
public sealed class ConversationConcurrencyLoadTests(LoadApiFixture api, ITestOutputHelper output)
    : IClassFixture<LoadApiFixture>
{
    [Fact]
    public async Task Concurrent_distinct_turns_on_one_conversation_are_generated_one_at_a_time_in_order()
    {
        LatencyOpenAiHandler provider = api.ResetProvider();
        provider.Latency = TimeSpan.FromMilliseconds(250);

        LoadWorkspace workspace = await api.CreateWorkspaceAsync();
        LoadConversation conversation = await api.StartConversationAsync(workspace);

        TurnResult[] results;

        await using (ConnectionSampler sampler = api.SampleConnections())
        {
            results = await Task.WhenAll(Enumerable.Range(0, 8)
                .Select(i => LoadApiFixture.SendTurnAsync(conversation, text: $"Question {i}")));

            output.WriteLine(sampler.ToString());
        }

        output.WriteLine(Describe(results));

        Assert.All(results, result =>
        {
            Assert.Equal(HttpStatusCode.OK, result.Status);
            Assert.True(result.Replied);
        });

        // The invariant: never two generations for one conversation at once.
        Assert.Equal(1, provider.MaxInFlightFor(conversation.Key));
        Assert.Equal(8, provider.Calls);

        // Serialised generations cannot finish faster than one after another.
        Assert.True(results.Max(r => r.Elapsed) >= TimeSpan.FromMilliseconds(8 * 250 * 0.95));

        List<object?[]> transcript = await api.QueryAsync(
            "select sender, ingestion_sequence, sent_at from messages where conversation_id = @c order by ingestion_sequence",
            ("c", conversation.Id));

        // Each customer message is immediately followed by its own reply: the
        // customer message is written inside the lock, the reply before release.
        Assert.Equal(16, transcript.Count);

        for (int i = 0; i < transcript.Count; i++)
        {
            Assert.Equal(i % 2 == 0 ? "Customer" : "AiAgent", transcript[i][0]);
        }

        List<object?[]> turns = await api.QueryAsync(
            """
            select t.status, c.ingestion_sequence, r.ingestion_sequence
            from conversation_turns t
            join messages c on c.id = t.customer_message_id
            left join messages r on r.id = t.reply_message_id
            where t.conversation_id = @c
            """,
            ("c", conversation.Id));

        Assert.Equal(8, turns.Count);
        Assert.All(turns, turn =>
        {
            Assert.Equal("Completed", turn[0]);
            Assert.Equal((long)turn[1]! + 1, (long)turn[2]!);
        });

        List<object?[]> usages = await api.QueryAsync(
            "select status, total_tokens from ai_provider_usages where tenant_id = @t",
            ("t", workspace.TenantId));

        Assert.Equal(8, usages.Count);
        Assert.All(usages, usage =>
        {
            Assert.Equal("Succeeded", usage[0]);
            Assert.Equal(48L, usage[1]);
        });

        // LastActivityAt is the high-water mark of what the transcript holds.
        List<object?[]> activity = await api.QueryAsync(
            """
            select c.last_activity_at = (select max(sent_at) from messages m where m.conversation_id = c.id)
            from conversations c where c.id = @c
            """,
            ("c", conversation.Id));

        Assert.Equal(true, activity[0][0]);
    }

    [Fact]
    public async Task A_burst_of_one_message_retried_concurrently_is_generated_exactly_once()
    {
        LatencyOpenAiHandler provider = api.ResetProvider();
        provider.Latency = TimeSpan.FromMilliseconds(300);

        LoadWorkspace workspace = await api.CreateWorkspaceAsync();
        LoadConversation conversation = await api.StartConversationAsync(workspace);
        string clientMessageId = Guid.NewGuid().ToString("N");

        TurnResult[] results = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => LoadApiFixture.SendTurnAsync(conversation, clientMessageId)));

        output.WriteLine(Describe(results));

        // 200 for the one that generated (and any replay after it completed), 409
        // for a duplicate that arrived while it was still being answered. Never a
        // server error, never a second generation.
        Assert.All(results, result =>
            Assert.True(result.Status is HttpStatusCode.OK or HttpStatusCode.Conflict, result.Status.ToString()));
        Assert.Contains(results, result => result.Status == HttpStatusCode.OK);
        Assert.Equal(1, provider.Calls);

        Assert.Equal(2L, await ScalarAsync("select count(*) from messages where conversation_id = @c", conversation.Id));
        Assert.Equal(1L, await ScalarAsync("select count(*) from conversation_turns where conversation_id = @c", conversation.Id));
        Assert.Equal(1L, await ScalarAsync("select count(*) from ai_provider_usages where tenant_id = @c", workspace.TenantId));
    }

    [Fact]
    public async Task Different_conversations_generate_concurrently_rather_than_one_workspace_at_a_time()
    {
        LatencyOpenAiHandler provider = api.ResetProvider();
        provider.Latency = TimeSpan.FromMilliseconds(400);

        LoadWorkspace[] workspaces = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => api.CreateWorkspaceAsync()));
        LoadConversation[] conversations = await Task.WhenAll(workspaces
            .SelectMany(workspace => Enumerable.Range(0, 6).Select(_ => workspace))
            .Select(workspace => api.StartConversationAsync(workspace)));

        DateTimeOffset started = DateTimeOffset.UtcNow;
        TurnResult[] results = await Task.WhenAll(conversations.Select(c => LoadApiFixture.SendTurnAsync(c)));
        TimeSpan wall = DateTimeOffset.UtcNow - started;

        output.WriteLine($"{conversations.Length} conversations, wall {wall.TotalMilliseconds:F0} ms, max in flight {provider.MaxInFlight}");
        output.WriteLine(Describe(results));

        Assert.All(results, result => Assert.Equal(HttpStatusCode.OK, result.Status));
        Assert.Equal(24, provider.Calls);
        Assert.Equal(1, provider.MaxInFlightForAnyConversation);

        // Serialising the application (or a workspace) would need 24 x 400 ms (or
        // 6 x 400 ms per workspace). Half the conversations at once is a floor with
        // plenty of margin for a loaded CI machine.
        Assert.True(provider.MaxInFlight >= 12, $"max in flight {provider.MaxInFlight}");
        Assert.True(wall < TimeSpan.FromMilliseconds(6 * 400), $"wall {wall}");
    }

    [Fact]
    public async Task Concurrent_load_in_two_workspaces_never_crosses_between_them()
    {
        LatencyOpenAiHandler provider = api.ResetProvider();
        provider.Latency = TimeSpan.FromMilliseconds(100);

        string canaryA = $"CANARYA{Guid.NewGuid():N}";
        string canaryB = $"CANARYB{Guid.NewGuid():N}";

        LoadWorkspace a = await api.CreateWorkspaceAsync($"Answer briefly. {canaryA}");
        LoadWorkspace b = await api.CreateWorkspaceAsync($"Answer briefly. {canaryB}");

        LoadConversation[] inA = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => api.StartConversationAsync(a)));
        LoadConversation[] inB = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => api.StartConversationAsync(b)));

        TurnResult[] results = await Task.WhenAll(inA.Concat(inB).SelectMany(c => new[]
        {
            LoadApiFixture.SendTurnAsync(c, text: "First"),
            LoadApiFixture.SendTurnAsync(c, text: "Second"),
        }));

        Assert.All(results, result => Assert.Equal(HttpStatusCode.OK, result.Status));

        // Every request the provider saw carried its own workspace's instructions,
        // exactly as many times as that workspace generated.
        Assert.Equal(20, provider.Instructions.Count(i => i.Contains(canaryA, StringComparison.Ordinal)));
        Assert.Equal(20, provider.Instructions.Count(i => i.Contains(canaryB, StringComparison.Ordinal)));
        Assert.DoesNotContain(provider.Instructions, i =>
            i.Contains(canaryA, StringComparison.Ordinal) && i.Contains(canaryB, StringComparison.Ordinal));

        // The ledger attributes each workspace's operations to it.
        Assert.Equal(20L, await ScalarAsync("select count(*) from ai_provider_usages where tenant_id = @c", a.TenantId));
        Assert.Equal(20L, await ScalarAsync("select count(*) from ai_provider_usages where tenant_id = @c", b.TenantId));

        // Each owner's inbox lists its own ten conversations and none of the other's.
        string listA = await a.Owner.GetStringAsync(WebChatApiFixture.Relative($"/api/tenants/{a.TenantId}/conversations?page=1&pageSize=50"));
        string listB = await b.Owner.GetStringAsync(WebChatApiFixture.Relative($"/api/tenants/{b.TenantId}/conversations?page=1&pageSize=50"));

        Assert.All(inA, c => Assert.Contains(c.Id.ToString(), listA, StringComparison.OrdinalIgnoreCase));
        Assert.All(inB, c => Assert.DoesNotContain(c.Id.ToString(), listA, StringComparison.OrdinalIgnoreCase));
        Assert.All(inB, c => Assert.Contains(c.Id.ToString(), listB, StringComparison.OrdinalIgnoreCase));
        Assert.All(inA, c => Assert.DoesNotContain(c.Id.ToString(), listB, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(b.TenantId.ToString(), listA, StringComparison.OrdinalIgnoreCase);

        // Another workspace's conversation cannot be read through this one, nor by
        // its own route with the wrong owner.
        HttpResponseMessage crossed = await a.Owner.GetAsync(
            WebChatApiFixture.Relative($"/api/tenants/{a.TenantId}/conversations/{inB[0].Id}"));
        HttpResponseMessage foreign = await a.Owner.GetAsync(
            WebChatApiFixture.Relative($"/api/tenants/{b.TenantId}/conversations/{inB[0].Id}"));

        Assert.Equal(HttpStatusCode.NotFound, crossed.StatusCode);
        Assert.True(foreign.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden, foreign.StatusCode.ToString());

        // A visitor's credential for a conversation in A reads nothing of B's.
        HttpResponseMessage visitorCrossing = await inA[0].Visitor.GetAsync(
            WebChatApiFixture.Relative($"/api/public/webchat/conversations/{inB[0].Id}/messages"));

        Assert.NotEqual(HttpStatusCode.OK, visitorCrossing.StatusCode);
    }

    [Fact]
    public async Task Provider_timeouts_under_load_are_recorded_unknown_once_each_and_never_retried()
    {
        LatencyOpenAiHandler provider = api.ResetProvider();
        provider.Latency = TimeSpan.FromMilliseconds(150);
        provider.SimulateTimeout = true;

        LoadWorkspace workspace = await api.CreateWorkspaceAsync();
        LoadConversation[] conversations = await Task.WhenAll(
            Enumerable.Range(0, 12).Select(_ => api.StartConversationAsync(workspace)));

        TurnResult[] results = await Task.WhenAll(conversations.Select(c => LoadApiFixture.SendTurnAsync(c)));

        output.WriteLine(Describe(results));

        Assert.All(results, result => Assert.False(result.Replied));
        Assert.Equal(12, provider.Calls);

        List<object?[]> usages = await api.QueryAsync(
            "select status, total_tokens, estimated_cost from ai_provider_usages where tenant_id = @t",
            ("t", workspace.TenantId));

        Assert.Equal(12, usages.Count);
        Assert.All(usages, usage =>
        {
            Assert.Equal("Unknown", usage[0]);
            Assert.Null(usage[1]);
            Assert.Null(usage[2]);
        });

        // Resending the same message is refused, not re-dispatched.
        TurnResult again = await LoadApiFixture.SendTurnAsync(conversations[0], clientMessageId: "retry-probe");
        TurnResult replay = await LoadApiFixture.SendTurnAsync(conversations[0], clientMessageId: "retry-probe");

        Assert.Equal(HttpStatusCode.Conflict, replay.Status);
        Assert.Equal(13, provider.Calls);
        output.WriteLine($"fresh message after timeouts: {(int)again.Status}");
    }

    [Fact]
    public async Task Concurrent_turns_crossing_every_usage_threshold_alert_once_each_and_stop_at_the_limit()
    {
        LatencyOpenAiHandler provider = api.ResetProvider();
        provider.Latency = TimeSpan.FromMilliseconds(100);

        LoadWorkspace workspace = await api.CreateWorkspaceAsync();
        await api.LimitProviderOperationsAsync(workspace.TenantId, limit: 20);

        LoadConversation[] conversations = await Task.WhenAll(
            Enumerable.Range(0, 26).Select(_ => api.StartConversationAsync(workspace)));

        TurnResult[] results = await Task.WhenAll(conversations.Select(c => LoadApiFixture.SendTurnAsync(c)));

        output.WriteLine(Describe(results));

        Assert.Equal(20, results.Count(result => result.Status == HttpStatusCode.OK && result.Replied));
        Assert.All(
            results.Where(result => result.Status != HttpStatusCode.OK),
            result => Assert.Equal(HttpStatusCode.Conflict, result.Status));

        // Exactly the quota reached the provider; nothing above it was dispatched.
        Assert.Equal(20, provider.Calls);
        Assert.Equal(20L, await ScalarAsync("select count(*) from ai_provider_usages where tenant_id = @c", workspace.TenantId));

        List<object?[]> occurrences = await api.QueryAsync(
            "select kind from notification_occurrences where tenant_id = @t order by kind",
            ("t", workspace.TenantId));

        Assert.Equal(
            ["UsageCritical", "UsageExhausted", "UsageWarning"],
            occurrences.Select(row => (string)row[0]!).ToArray());

        // The dispatch worker fans each occurrence out to the owner exactly once.
        long notifications = 0;

        for (int attempt = 0; attempt < 60 && notifications < 3; attempt++)
        {
            await Task.Delay(500);
            notifications = await ScalarAsync("select count(*) from notifications where tenant_id = @c", workspace.TenantId);
        }

        Assert.Equal(3L, notifications);
        Assert.Equal(0L, await ScalarAsync(
            """
            select count(*) from (
              select recipient_user_id, occurrence_id from notifications where tenant_id = @c
              group by 1, 2 having count(*) > 1) duplicates
            """,
            workspace.TenantId));
    }

    [Fact]
    public async Task Waiters_for_one_conversation_are_refused_by_the_budget_without_server_errors()
    {
        LatencyOpenAiHandler provider = api.ResetProvider();
        provider.Latency = TimeSpan.FromMilliseconds(300);

        LoadWorkspace workspace = await api.CreateWorkspaceAsync();
        LoadConversation conversation = await api.StartConversationAsync(workspace);

        // Thirty turns from one visitor: the per-client request budget (30 / min)
        // admits all of them, and the per-conversation generation budget (10 / min)
        // is decided inside the lock, so twenty of them wait for the lock only to be
        // refused. That queue is what this scenario measures.
        TurnResult[] results;

        await using (ConnectionSampler sampler = api.SampleConnections())
        {
            results = await Task.WhenAll(Enumerable.Range(0, 30)
                .Select(i => LoadApiFixture.SendTurnAsync(conversation, text: $"Burst {i}")));

            output.WriteLine(sampler.ToString());
        }

        output.WriteLine(Describe(results));

        Assert.All(results, result => Assert.True(
            result.Status is HttpStatusCode.OK or HttpStatusCode.TooManyRequests,
            result.Status.ToString()));
        Assert.Equal(10, results.Count(result => result.Status == HttpStatusCode.OK));
        Assert.Equal(10, provider.Calls);
        Assert.Equal(1, provider.MaxInFlightFor(conversation.Key));
    }

    private async Task<long> ScalarAsync(string sql, Guid id) =>
        (long)(await api.QueryAsync(sql, ("c", id)))[0][0]!;

    internal static string Describe(IReadOnlyCollection<TurnResult> results)
    {
        double[] ms = [.. results.Select(r => r.Elapsed.TotalMilliseconds).Order()];

        string statuses = string.Join(", ", results
            .GroupBy(r => (int)r.Status)
            .OrderBy(g => g.Key)
            .Select(g => $"{g.Key}x{g.Count()}"));

        return $"n={ms.Length} statuses [{statuses}] p50={Pct(ms, 50):F0}ms p95={Pct(ms, 95):F0}ms max={ms[^1]:F0}ms";
    }

    internal static double Pct(double[] sorted, double p) =>
        sorted[Math.Clamp((int)Math.Ceiling(p / 100 * sorted.Length) - 1, 0, sorted.Length - 1)];
}
