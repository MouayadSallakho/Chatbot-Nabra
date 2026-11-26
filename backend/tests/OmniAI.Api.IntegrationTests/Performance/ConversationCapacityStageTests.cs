using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Xunit.Abstractions;

namespace OmniAI.Api.IntegrationTests.Performance;

/// <summary>
/// Phase 34 capacity discovery for the conversation-turn pipeline: staged,
/// closed-loop concurrency until the first sustained degradation.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Opt-in, never part of an ordinary run.</strong> It takes minutes, its
/// numbers describe the machine it ran on, and it asserts nothing about speed —
/// only that no stage produced a response the contract does not allow. Enable with
/// <c>OMNIAI_PERF_CAPACITY=1</c>; tune with <c>OMNIAI_PERF_STAGES</c> (e.g.
/// <c>1,5,10,25,50</c>), <c>OMNIAI_PERF_STAGE_SECONDS</c> and
/// <c>OMNIAI_PERF_PROVIDER_MS</c>. Results are written as JSON lines under
/// <c>OMNIAI_PERF_OUT</c> when it is set.
/// </para>
/// <para>
/// <strong>Protection limits are raised in this fixture, and only here.</strong>
/// The question is where the pipeline itself (HTTP, lock, ledger, persistence,
/// connection pools) saturates. With production defaults a workspace may generate
/// 30 replies a minute and a client may send 30 requests a minute, so every stage
/// would measure the limiter instead. The limiter's own behaviour at its defaults
/// is proven by <see cref="ConversationConcurrencyLoadTests"/> and the Phase 16
/// suites; nothing here changes what a deployment runs.
/// </para>
/// </remarks>
[Trait("Category", "Capacity")]
[Collection(LoadScenarios.Name)]
public sealed class ConversationCapacityStageTests(CapacityApiFixture api, ITestOutputHelper output)
    : IClassFixture<CapacityApiFixture>
{
    [CapacityFact]
    public async Task Conversation_turn_pipeline_scales_until_its_first_sustained_degradation()
    {
        int[] stages = [.. (Environment.GetEnvironmentVariable("OMNIAI_PERF_STAGES") ?? "1,5,10,25,40,50,75,100")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(int.Parse)];
        int seconds = int.Parse(Environment.GetEnvironmentVariable("OMNIAI_PERF_STAGE_SECONDS") ?? "15", System.Globalization.CultureInfo.InvariantCulture);
        int providerMs = int.Parse(Environment.GetEnvironmentVariable("OMNIAI_PERF_PROVIDER_MS") ?? "200", System.Globalization.CultureInfo.InvariantCulture);

        LatencyOpenAiHandler provider = api.ResetProvider();
        provider.Latency = TimeSpan.FromMilliseconds(providerMs);

        string? outDir = Environment.GetEnvironmentVariable("OMNIAI_PERF_OUT");
        string? outFile = outDir is null ? null : Path.Combine(outDir, $"capacity-{DateTime.UtcNow:yyyyMMddHHmmss}-p{providerMs}.jsonl");

        List<LoadWorkspace> workspaces = [];

        // Warm-up: JIT, pools, the first-use paths. Measured separately, not reported as a stage.
        StageResult warm = await RunStageAsync(5, TimeSpan.FromSeconds(5), workspaces);
        output.WriteLine($"warm-up {warm}");

        double? baselineP95 = null;
        List<string> unexpected = [];

        foreach (int concurrency in stages)
        {
            provider.Reset();
            provider.Latency = TimeSpan.FromMilliseconds(providerMs);

            StageResult stage = await RunStageAsync(concurrency, TimeSpan.FromSeconds(seconds), workspaces);
            baselineP95 ??= stage.P95;

            bool degraded = stage.ErrorRate > 0.01 || stage.P95 > 3 * baselineP95.Value;
            output.WriteLine($"{(degraded ? "DEGRADED" : "healthy ")} {stage}");

            if (outFile is not null)
            {
                await File.AppendAllTextAsync(outFile, JsonSerializer.Serialize(stage with { Degraded = degraded }) + "\n");
            }

            unexpected.AddRange(stage.Statuses.Keys.Where(status => status is not (200 or 503 or 504 or 429)).Select(status => $"c={concurrency}: {status}"));

            bool stopOnErrorsOnly = Environment.GetEnvironmentVariable("OMNIAI_PERF_STOP") == "errors";

            if (stopOnErrorsOnly ? stage.ErrorRate > 0.01 : degraded)
            {
                break;
            }
        }

        // Whatever the overload did to individual requests, durable state must be
        // settled and consistent once the load stops.
        List<object?[]> integrity = await api.QueryAsync(
            """
            select
              (select count(*) from conversation_turns where status = 'Generating'),
              (select count(*) from ai_provider_usages where status = 'Started'),
              (select count(*) from ai_provider_usages where status = 'Succeeded'),
              (select count(*) from conversation_turns where status = 'Completed'),
              (select count(*) from conversation_turns where status = 'Failed'),
              (select count(*) from messages m where m.sender = 'Customer'
                 and not exists (select 1 from conversation_turns t where t.customer_message_id = m.id)),
              (select count(*) from conversation_turns where status = 'Completed' and reply_message_id is null)
            """);

        object?[] row = integrity[0];
        output.WriteLine(
            $"integrity: generating={row[0]} ledgerStarted={row[1]} ledgerSucceeded={row[2]} " +
            $"turnsCompleted={row[3]} turnsFailed={row[4]} customerMessagesWithoutTurn={row[5]} completedWithoutReply={row[6]}");

        // Only statuses the contract allows under overload; a 500 is a defect.
        Assert.Empty(unexpected);
        // Ledger rows still Started are not asserted: a settlement that could not get
        // a connection is left to the reconciler (Started -> Unknown), by design.
        Assert.Equal(0L, row[0]);
        Assert.Equal(0L, row[6]);
    }

    private async Task<StageResult> RunStageAsync(int concurrency, TimeSpan duration, List<LoadWorkspace> workspaces)
    {
        // Conversations per workspace (default ten), created on demand and reused.
        int perWorkspace = int.Parse(Environment.GetEnvironmentVariable("OMNIAI_PERF_PER_WORKSPACE") ?? "10", System.Globalization.CultureInfo.InvariantCulture);

        while (workspaces.Count < (concurrency + perWorkspace - 1) / perWorkspace)
        {
            workspaces.Add(await api.CreateWorkspaceAsync());
        }

        LoadConversation[] conversations = await Task.WhenAll(Enumerable.Range(0, concurrency)
            .Select(i => api.StartConversationAsync(workspaces[i / perWorkspace])));

        List<double> latencies = [];
        Dictionary<int, int> statuses = [];
        Lock sync = new();
        Stopwatch clock = Stopwatch.StartNew();

        await using ConnectionSampler sampler = api.SampleConnections();

        // The test host runs in this process, so its thread pool is observable here:
        // a growing queue with idle CPUs is starvation, not saturation.
        int maxThreads = 0;
        long maxQueued = 0;
        using CancellationTokenSource poolWatch = new();
        Task poolSampler = Task.Run(async () =>
        {
            while (!poolWatch.IsCancellationRequested)
            {
                maxThreads = Math.Max(maxThreads, ThreadPool.ThreadCount);
                maxQueued = Math.Max(maxQueued, ThreadPool.PendingWorkItemCount);
                await Task.Delay(50, CancellationToken.None);
            }
        });

        await Task.WhenAll(conversations.Select(async conversation =>
        {
            while (clock.Elapsed < duration)
            {
                TurnResult result = await LoadApiFixture.SendTurnAsync(conversation);

                lock (sync)
                {
                    latencies.Add(result.Elapsed.TotalMilliseconds);
                    statuses[(int)result.Status] = statuses.GetValueOrDefault((int)result.Status) + 1;
                }
            }
        }));

        clock.Stop();
        await poolWatch.CancelAsync();
        await poolSampler;

        double[] sorted = [.. latencies.Order()];
        int ok = statuses.GetValueOrDefault(200);

        return new StageResult(
            concurrency,
            Math.Round(clock.Elapsed.TotalSeconds, 1),
            sorted.Length,
            Math.Round(ok / clock.Elapsed.TotalSeconds, 1),
            Math.Round(ConversationConcurrencyLoadTests.Pct(sorted, 50)),
            Math.Round(ConversationConcurrencyLoadTests.Pct(sorted, 95)),
            Math.Round(ConversationConcurrencyLoadTests.Pct(sorted, 99)),
            Math.Round(sorted[^1]),
            sorted.Length == 0 ? 0 : Math.Round(1 - (ok / (double)sorted.Length), 4),
            statuses,
            api.Provider.MaxInFlight,
            sampler.Baseline ?? 0,
            sampler.PeakTotal,
            sampler.PeakActive,
            sampler.PeakLockConnections,
            maxThreads,
            maxQueued,
            Degraded: false);
    }
}

public sealed record StageResult(
    int Concurrency,
    double Seconds,
    int Requests,
    double TurnsPerSecond,
    double P50,
    double P95,
    double P99,
    double Max,
    double ErrorRate,
    Dictionary<int, int> Statuses,
    int ProviderMaxInFlight,
    int ConnectionsBaseline,
    int ConnectionsPeak,
    int ConnectionsPeakActive,
    int LockConnectionsPeak,
    int ThreadPoolThreadsMax,
    long ThreadPoolQueueMax,
    bool Degraded)
{
    public override string ToString() =>
        $"c={Concurrency} req={Requests} ok/s={TurnsPerSecond} p50={P50} p95={P95} p99={P99} max={Max} " +
        $"err={ErrorRate:P2} statuses=[{string.Join(",", Statuses.OrderBy(s => s.Key).Select(s => $"{s.Key}x{s.Value}"))}] " +
        $"providerInFlight={ProviderMaxInFlight} conn(base/peak/active)={ConnectionsBaseline}/{ConnectionsPeak}/{ConnectionsPeakActive} lockConn={LockConnectionsPeak} threads={ThreadPoolThreadsMax} queued={ThreadPoolQueueMax}";
}

/// <summary>The load fixture with public chat protection raised out of the way.</summary>
public sealed class CapacityApiFixture : LoadApiFixture
{
    private const string High = "1000000";

    protected override IReadOnlyDictionary<string, string> HostSettings =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Optional: the pool bound under evaluation, applied the way an operator
            // does from Phase 35 — through the connection policy, which overrides
            // any pool size in the connection string. Both pools get the value.
            ["Database:Pools:ApplicationPoolSize"] =
                Environment.GetEnvironmentVariable("OMNIAI_PERF_MAX_POOL") ?? "25",
            ["Database:Pools:ConversationLockPoolSize"] =
                Environment.GetEnvironmentVariable("OMNIAI_PERF_MAX_POOL") ?? "15",
            // Optional: evaluate the log level a deployment runs with (Phase 34 finding:
            // before Phase 34 the shipped defaults logged every SQL command at Information).
            ["Logging:LogLevel:Microsoft.EntityFrameworkCore"] =
                Environment.GetEnvironmentVariable("OMNIAI_PERF_EF_LOG_LEVEL") ?? "Warning",
            ["PublicChatProtection:ConversationStartsPerClient:Limit"] = High,
            ["PublicChatProtection:ConversationStartsPerEndpoint:Limit"] = High,
            ["PublicChatProtection:RequestsPerClient:Limit"] = High,
            ["PublicChatProtection:MessagesPerConversation:Limit"] = High,
            ["PublicChatProtection:GenerationsPerWorkspaceBurst:Limit"] = High,
            ["PublicChatProtection:GenerationsPerWorkspaceSustained:Limit"] = High,
            ["PublicChatProtection:GenerationsPerWorkspaceDaily:Limit"] = High,
        };
}

/// <summary>A fact that runs only when <c>OMNIAI_PERF_CAPACITY=1</c>.</summary>
public sealed class CapacityFactAttribute : FactAttribute
{
    public CapacityFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("OMNIAI_PERF_CAPACITY") != "1")
        {
            Skip = "Capacity discovery runs only when OMNIAI_PERF_CAPACITY=1.";
        }
    }
}
