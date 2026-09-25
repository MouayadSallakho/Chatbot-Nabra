using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using StackExchange.Redis;

namespace OmniAI.Api.Health;

/// <summary>
/// Liveness and readiness, on the built-in health checks.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Liveness checks nothing.</strong> <c>/health/live</c> (and the Phase 1
/// <c>/health</c> it stands beside) answers "is this process up and serving". A
/// liveness probe that consulted the database would have an orchestrator restart
/// every instance during a database outage, which fixes nothing and loses every
/// in-flight request.
/// </para>
/// <para>
/// <strong>Readiness checks only what this process cannot serve without.</strong>
/// PostgreSQL always; Redis only when it is configured, because then public chat
/// protection and realtime tickets depend on it. Never OpenAI, Meta, Stripe or
/// SMTP: each of those failing is already a truthful, contained refusal on the
/// feature that needs it, and taking the whole instance out of rotation because a
/// third party is slow would turn one provider's outage into this platform's.
/// </para>
/// <para>
/// <strong>Minimal answers.</strong> The body is one word per probe — no check
/// names, no exception text, no durations, no host names. Probes are anonymous,
/// so what they return is public; the reason a check failed is logged by the
/// health-check service, not published.
/// </para>
/// <para>
/// <strong>Bounded.</strong> Every check has a short timeout, so a hung dependency
/// makes the probe answer Unhealthy promptly instead of holding it open.
/// </para>
/// </remarks>
internal static class HealthSetup
{
    public const string LivePath = "/health/live";
    public const string ReadyPath = "/health/ready";

    private const string ReadyTag = "ready";

    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(3);

    public static IServiceCollection AddOmniAiHealth(this IServiceCollection services)
    {
        services.AddSingleton<ReadinessDatabase>();

        IHealthChecksBuilder checks = services.AddHealthChecks();

        checks.AddCheck<DatabaseReadinessCheck>("database", HealthStatus.Unhealthy, [ReadyTag], CheckTimeout);
        checks.AddCheck<RedisReadinessCheck>("redis", HealthStatus.Unhealthy, [ReadyTag], CheckTimeout);

        return services;
    }

    public static WebApplication MapOmniAiHealth(this WebApplication app)
    {
        // No check runs: the process answering is the whole answer.
        app.MapHealthChecks(LivePath, new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = WriteStatusAsync,
        })
            .AllowAnonymous();

        app.MapHealthChecks(ReadyPath, new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(ReadyTag),
            ResponseWriter = WriteStatusAsync,
        })
            .AllowAnonymous();

        return app;
    }

    /// <summary>One word, and a cache header so no proxy serves a stale answer.</summary>
    private static Task WriteStatusAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        context.Response.Headers.CacheControl = "no-store";

        return context.Response.WriteAsJsonAsync(new { status = report.Status.ToString() });
    }
}

/// <summary>
/// The readiness probe's own, strictly bounded route to PostgreSQL.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why not the application's DbContext.</strong> A health-check timeout
/// only cancels a token, and opening a connection to a server that accepts the
/// TCP handshake but never answers (a paused or overloaded database) does not
/// stop early for it: the probe waited out Npgsql's 15-second connect timeout
/// (measured in Phase 33). This data source is built from the same connection
/// string with every wait bounded — connect, command, and no wait for a
/// cancellation reply — so the probe answers within its budget.
/// </para>
/// <para>
/// A pool of at most two, so a probe storm cannot consume the database's
/// connections, and one data source for the life of the process.
/// </para>
/// </remarks>
internal sealed class ReadinessDatabase : IAsyncDisposable
{
    private const int BoundSeconds = 3;

    private readonly NpgsqlDataSource? _source;

    public ReadinessDatabase(IConfiguration configuration)
    {
        string? connectionString = configuration.GetConnectionString("OmniAiDatabase");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        NpgsqlConnectionStringBuilder bounded = new(connectionString)
        {
            Timeout = BoundSeconds,
            CommandTimeout = BoundSeconds,
            CancellationTimeout = -1,
            MaxPoolSize = 2,
            MinPoolSize = 0,
            ApplicationName = "omniai-readiness",
        };

        _source = NpgsqlDataSource.Create(bounded.ConnectionString);
    }

    /// <summary>Null when no database is configured, which is itself not ready.</summary>
    public NpgsqlDataSource? Source => _source;

    public ValueTask DisposeAsync() => _source?.DisposeAsync() ?? ValueTask.CompletedTask;
}

/// <summary>PostgreSQL answers a trivial query, within the probe's budget.</summary>
internal sealed class DatabaseReadinessCheck(ReadinessDatabase database) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (database.Source is not { } source)
        {
            return HealthCheckResult.Unhealthy("No database is configured.");
        }

        NpgsqlCommand command = source.CreateCommand("SELECT 1");

        // Not given the probe's token. Npgsql answers a cancellation by opening a
        // second connection to send a cancel request, and against a server that
        // is not answering that is one more bounded wait stacked on the first
        // (9 s measured). Instead the probe simply stops waiting at its budget,
        // while the query runs to its own CommandTimeout in the background; its
        // outcome is observed and the command disposed when it ends.
        Task<object?> query = command.ExecuteScalarAsync(CancellationToken.None);

        _ = query.ContinueWith(
            static (finished, state) =>
            {
                _ = finished.Exception;
                ((NpgsqlCommand)state!).Dispose();
            },
            command,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        // A failure or the budget running out propagates to the health-check
        // service, which logs it and reports Unhealthy; the body never carries it.
        await query.WaitAsync(cancellationToken).ConfigureAwait(false);

        return HealthCheckResult.Healthy();
    }
}

/// <summary>Redis answers a PING — only when this deployment uses Redis at all.</summary>
internal sealed class RedisReadinessCheck(IServiceProvider services) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        // Not configured is not unhealthy: public chat writes are then refused by
        // design, and nothing else needs Redis.
        if (services.GetService<IConnectionMultiplexer>() is not { } redis)
        {
            return HealthCheckResult.Healthy();
        }

        await redis.GetDatabase().PingAsync().WaitAsync(cancellationToken).ConfigureAwait(false);

        return HealthCheckResult.Healthy();
    }
}
