using System.ClientModel.Primitives;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using OmniAI.Api.IntegrationTests.Authorization;
using OmniAI.Api.IntegrationTests.WebChat;
using OmniAI.Application.Ai;
using OmniAI.Domain.Plans;
using OmniAI.Domain.Tenants;
using OmniAI.Infrastructure.Ai.OpenAi;
using OmniAI.Infrastructure.Persistence;

#pragma warning disable OPENAI001

namespace OmniAI.Api.IntegrationTests.Performance;

/// <summary>
/// The real application, the real OpenAI adapter and the real ledger, with a
/// socket that answers slowly enough for concurrency to be observable.
/// </summary>
/// <remarks>
/// <para>
/// Phase 34. What these suites exercise under contention is exactly what
/// production runs: HTTP, antiforgery, the Redis limiter, the PostgreSQL advisory
/// lock, the usage ledger's Started-before-dispatch, quota reservation, threshold
/// alerts. Only the provider's socket is replaced (see
/// <see cref="LatencyOpenAiHandler"/>), and the placeholder credential could not
/// authenticate anywhere.
/// </para>
/// <para>
/// Public chat protection runs with its production defaults. A scenario that
/// needs more traffic than one client or one workspace may send spreads it over
/// more simulated clients and more workspaces, the way real traffic arrives,
/// rather than raising a limit.
/// </para>
/// </remarks>
public class LoadApiFixture : RoleApiFixture, IDisposable
{
    internal const string PlaceholderKey = "test-key-not-a-real-credential";

    private readonly LatencyOpenAiHandler _handler = new();

    private int _peers;

    internal LatencyOpenAiHandler Provider => _handler;

    internal LatencyOpenAiHandler ResetProvider()
    {
        _handler.Reset();

        return _handler;
    }

    public void Dispose()
    {
        _handler.Dispose();
        GC.SuppressFinalize(this);
    }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        base.ConfigureTestServices(services);

        OpenAiOptions options = new();

        services.TryAddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.Replace(ServiceDescriptor.Singleton(options));
        services.Replace(ServiceDescriptor.Singleton(
            OpenAiClientFactory.Create(
                PlaceholderKey,
                options,
                new HttpClientPipelineTransport(new HttpClient(_handler)))));
        services.Replace(ServiceDescriptor.Singleton<IAiTextGenerator, OpenAiTextGenerator>());
    }

    /// <summary>A distinct documentation-range address per simulated visitor.</summary>
    public IPAddress NextPeer()
    {
        int n = Interlocked.Increment(ref _peers);

        return new IPAddress([198, 18, (byte)(n / 250 % 250), (byte)(n % 250 + 1)]);
    }

    /// <summary>Creates a workspace with an active agent and an active chat endpoint.</summary>
    public async Task<LoadWorkspace> CreateWorkspaceAsync(string instructions = "Answer briefly.")
    {
        (HttpClient owner, Guid tenantId, Guid ownerId) = await CreateOwnerAsync();

        HttpResponseMessage created = await owner.PostAsJsonAsync(
            WebChatApiFixture.Relative($"/api/tenants/{tenantId}/ai-agents"),
            new { name = "Load agent", instructions });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using JsonDocument agent = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        Guid agentId = agent.RootElement.GetProperty("id").GetGuid();

        HttpRequestMessage activate = new(
            HttpMethod.Post,
            WebChatApiFixture.Relative($"/api/tenants/{tenantId}/ai-agents/{agentId}/activate"));
        activate.Headers.TryAddWithoutValidation("If-Match", created.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, (await owner.SendAsync(activate)).StatusCode);

        HttpResponseMessage endpointCreated = await owner.PostAsJsonAsync(
            WebChatApiFixture.Relative($"/api/tenants/{tenantId}/website-chat"),
            new { aiAgentId = agentId });

        Assert.Equal(HttpStatusCode.Created, endpointCreated.StatusCode);

        using JsonDocument endpoint = JsonDocument.Parse(await endpointCreated.Content.ReadAsStringAsync());
        Guid endpointId = endpoint.RootElement.GetProperty("id").GetGuid();
        string publicKey = endpoint.RootElement.GetProperty("publicKey").GetString()!;

        HttpRequestMessage activateEndpoint = new(
            HttpMethod.Post,
            WebChatApiFixture.Relative($"/api/tenants/{tenantId}/website-chat/{endpointId}/activate"));
        activateEndpoint.Headers.TryAddWithoutValidation("If-Match", endpointCreated.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, (await owner.SendAsync(activateEndpoint)).StatusCode);

        return new LoadWorkspace(owner, tenantId, ownerId, publicKey, agentId);
    }

    /// <summary>Starts one conversation as a new visitor from its own address.</summary>
    public async Task<LoadConversation> StartConversationAsync(LoadWorkspace workspace, IPAddress? peer = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        HttpClient visitor = await CreateVisitorAsync(peer ?? NextPeer());

        HttpResponseMessage started = await visitor.PostAsJsonAsync(
            WebChatApiFixture.Relative($"/api/public/webchat/{workspace.PublicKey}/conversations"),
            new { });

        Assert.Equal(HttpStatusCode.OK, started.StatusCode);

        using JsonDocument body = JsonDocument.Parse(await started.Content.ReadAsStringAsync());
        Guid conversationId = body.RootElement.GetProperty("conversationId").GetGuid();

        return new LoadConversation(visitor, conversationId, conversationId.ToString("N"));
    }

    /// <summary>Sends one customer turn and times it.</summary>
    public static async Task<TurnResult> SendTurnAsync(
        LoadConversation conversation,
        string? clientMessageId = null,
        string text = "Where is my order?")
    {
        ArgumentNullException.ThrowIfNull(conversation);

        Stopwatch timer = Stopwatch.StartNew();

        using HttpResponseMessage response = await conversation.Visitor.PostAsJsonAsync(
            WebChatApiFixture.Relative($"/api/public/webchat/conversations/{conversation.Id}/messages"),
            new
            {
                clientMessageId = clientMessageId ?? Guid.NewGuid().ToString("N"),
                text = $"{text} [conv:{conversation.Key}]",
            });

        string content = await response.Content.ReadAsStringAsync();
        timer.Stop();

        bool replied = false;

        if (response.StatusCode == HttpStatusCode.OK)
        {
            using JsonDocument body = JsonDocument.Parse(content);
            replied = body.RootElement.TryGetProperty("reply", out JsonElement reply)
                && reply.ValueKind == JsonValueKind.Object;
        }

        return new TurnResult(response.StatusCode, replied, timer.Elapsed, content);
    }

    /// <summary>Moves a workspace onto a plan with a monthly provider-operation limit.</summary>
    public async Task LimitProviderOperationsAsync(Guid tenantId, int limit)
    {
        Plan plan = Plan.Create(
            PlanCode.Create($"p{Guid.NewGuid():N}"[..20]),
            PlanDisplayName.Create("Load threshold plan"),
            PlanLimit.From(null),
            PlanLimit.From(null),
            PlanLimit.From(null),
            PlanLimit.From(null),
            PlanLimit.From(null),
            PlanLimit.From(limit),
            DateTimeOffset.UtcNow);

        await using OmniAiDbContext context = CreateDbContext();

        context.Plans.Add(plan);
        await context.SaveChangesAsync();

        await context.WorkspacePlanAssignments
            .Where(assignment => assignment.TenantId == TenantId.From(tenantId))
            .ExecuteDeleteAsync();

        context.WorkspacePlanAssignments.Add(
            WorkspacePlanAssignment.Create(TenantId.From(tenantId), plan.Id, DateTimeOffset.UtcNow));
        await context.SaveChangesAsync();
    }

    /// <summary>Runs one read-only SQL query against this fixture's database.</summary>
    public async Task<List<object?[]>> QueryAsync(string sql, params (string Name, object Value)[] parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        await using NpgsqlConnection connection = new(Database.ConnectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = new(sql, connection);

        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        List<object?[]> rows = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            object?[] row = new object?[reader.FieldCount];

            for (int i = 0; i < reader.FieldCount; i++)
            {
                row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }

            rows.Add(row);
        }

        return rows;
    }

    /// <summary>
    /// Samples this database's server connections until disposed, keeping the peak.
    /// </summary>
    public ConnectionSampler SampleConnections() => new(Database.ConnectionString);
}

public sealed record LoadWorkspace(HttpClient Owner, Guid TenantId, Guid OwnerId, string PublicKey, Guid AgentId);

public sealed record LoadConversation(HttpClient Visitor, Guid Id, string Key);

public sealed record TurnResult(HttpStatusCode Status, bool Replied, TimeSpan Elapsed, string Body);

/// <summary>Polls pg_stat_activity for one database and records the peak.</summary>
public sealed class ConnectionSampler : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;

    public ConnectionSampler(string connectionString)
    {
        // A pool of its own, sized one, so the sampler never competes with the
        // application's pools and counts exactly one connection of its own.
        string sampling = new NpgsqlConnectionStringBuilder(connectionString)
        {
            MaxPoolSize = 1,
            ApplicationName = "omniai-load-sampler",
        }.ConnectionString;

        _loop = Task.Run(async () =>
        {
            await using NpgsqlConnection connection = new(sampling);
            await connection.OpenAsync(_stop.Token);

            while (!_stop.IsCancellationRequested)
            {
                await using NpgsqlCommand command = new(
                    """
                    select count(*),
                           count(*) filter (where state = 'active'),
                           count(*) filter (where wait_event_type = 'Lock'),
                           (select count(*) from pg_locks l
                              join pg_stat_activity a on a.pid = l.pid
                             where l.locktype = 'advisory' and l.granted
                               and a.datname = current_database())
                    from pg_stat_activity
                    where datname = current_database() and pid <> pg_backend_pid()
                    """,
                    connection);

                await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(_stop.Token))
                {
                    await reader.ReadAsync(_stop.Token);
                    int total = (int)reader.GetInt64(0);
                    Baseline ??= total;
                    PeakTotal = Math.Max(PeakTotal, total);
                    PeakActive = Math.Max(PeakActive, (int)reader.GetInt64(1));
                    PeakLockWaits = Math.Max(PeakLockWaits, (int)reader.GetInt64(2));
                    PeakAdvisoryHeld = Math.Max(PeakAdvisoryHeld, (int)reader.GetInt64(3));
                    PeakLockConnections = Math.Max(
                        PeakLockConnections, (int)reader.GetInt64(2) + (int)reader.GetInt64(3));
                }

                try
                {
                    await Task.Delay(25, _stop.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        });
    }

    public int PeakTotal { get; private set; }

    public int PeakActive { get; private set; }

    /// <summary>Backends waiting on a heavyweight lock (advisory locks included).</summary>
    public int PeakLockWaits { get; private set; }

    /// <summary>Advisory locks held at once (one per conversation being generated).</summary>
    public int PeakAdvisoryHeld { get; private set; }

    /// <summary>Connections holding or waiting for a conversation lock at once.</summary>
    public int PeakLockConnections { get; private set; }

    /// <summary>Server connections at the first sample (pooled idle ones included).</summary>
    public int? Baseline { get; private set; }

    public override string ToString() =>
        $"connections baseline {Baseline} peak {PeakTotal} (active {PeakActive}); " +
        $"lock connections peak {PeakLockConnections} (held {PeakAdvisoryHeld}, waiting {PeakLockWaits})";

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();

        try
        {
            await _loop;
        }
        catch (OperationCanceledException)
        {
        }

        _stop.Dispose();
    }
}

#pragma warning restore OPENAI001
