using System.Globalization;
using Npgsql;

namespace OmniAI.Infrastructure.Persistence;

/// <summary>
/// How many PostgreSQL connections this process may open, from which pool, and
/// how long any database wait may last.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why this exists (Phase 35, from Phase 34 evidence).</strong> Every
/// instance runs three independent Npgsql pools, each defaulting to 100
/// connections, against a server whose default limit is also 100. Under load
/// that produced <c>53300 too many clients</c> and pool starvation, and with the
/// database unresponsive an ordinary request waited about 60 s. Production must
/// therefore never rely on Npgsql's defaults: every pool is sized here, every
/// wait is bounded here, and the sum is checked against a declared budget.
/// </para>
/// <para>
/// <strong>The pools, and why they stay separate.</strong>
/// </para>
/// <list type="bullet">
///   <item><description><b>Application</b> — EF Core: requests, workers, the
///   usage ledger's own scope, Data Protection keys. Short-lived use.</description></item>
///   <item><description><b>Conversation locks</b> — one session-level advisory
///   lock per AI turn in flight, held across the provider call. Kept separate
///   deliberately: shared with the application pool, long-held provider turns
///   could take every connection and starve ordinary Admin traffic. Its size is
///   the number of generations one instance may have in flight.</description></item>
///   <item><description><b>Readiness</b> — the probe's own bounded pool of
///   <see cref="ReadinessPoolSize"/>, so a probe can answer while the other two
///   are exhausted.</description></item>
/// </list>
/// <para>
/// <strong>The budget.</strong> Operators declare the connections the
/// application may use in total (<c>Database:Budget:Connections</c> — the
/// server's <c>max_connections</c> minus the superuser reserve and an allowance
/// for migrations, monitoring and administration) and the most instances that
/// will ever run at once, rolling deploys included
/// (<c>Database:Budget:MaxInstances</c>). Outside Development both are required,
/// and startup refuses a configuration whose pools cannot fit:
/// <c>MaxInstances × (Application + ConversationLocks + Readiness) ≤ Connections</c>.
/// </para>
/// <para>
/// No value here is a secret; the connection string (which holds the password)
/// is never part of this type and never appears in its messages.
/// </para>
/// </remarks>
public sealed class DatabaseConnectionPolicy
{
    /// <summary>Configuration section these values are bound from.</summary>
    public const string SectionName = "Database";

    /// <summary>The readiness probe's fixed pool (see HealthSetup).</summary>
    public const int ReadinessPoolSize = 2;

    /// <summary>EF Core pool: requests, workers, ledger, Data Protection.</summary>
    public int ApplicationPoolSize { get; set; } = 25;

    /// <summary>Advisory-lock pool: AI generations one instance may have in flight.</summary>
    public int ConversationLockPoolSize { get; set; } = 15;

    /// <summary>
    /// Opening a connection. Short: a healthy server answers in milliseconds, and
    /// a longer wait only delays the truthful 503.
    /// </summary>
    public int ConnectTimeoutSeconds { get; set; } = 5;

    /// <summary>
    /// Ordinary statements. The slowest statement measured under Phase 34 load was
    /// under 100 ms; 15 s leaves two orders of magnitude of headroom while bounding
    /// an unresponsive server.
    /// </summary>
    public int CommandTimeoutSeconds { get; set; } = 15;

    /// <summary>
    /// How long a timed-out statement waits for the server to acknowledge its
    /// cancellation before the connection is abandoned.
    /// </summary>
    public int CancellationTimeoutMilliseconds { get; set; } = 2_000;

    /// <summary>
    /// Report generation's statements, which read whole periods (up to the report
    /// row limits). A deliberate longer class rather than a longer global default.
    /// </summary>
    public int LongCommandTimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// How long a turn may wait for its conversation's lock behind another turn
    /// (which itself lasts at most one provider timeout). Also bounds the wait
    /// against an unresponsive server, which previously had no limit.
    /// </summary>
    public int ConversationLockWaitSeconds { get; set; } = 60;

    /// <summary>Connections the whole application may hold (see remarks). Required outside Development.</summary>
    public int? BudgetConnections { get; set; }

    /// <summary>Most instances running at once, including during a rolling deploy. Required outside Development.</summary>
    public int? BudgetMaxInstances { get; set; }

    /// <summary>Connections one instance can open at most.</summary>
    public int PerInstanceConnections => ApplicationPoolSize + ConversationLockPoolSize + ReadinessPoolSize;

    /// <summary>
    /// Reads the policy from configuration. Absent keys keep their defaults; a
    /// present but non-numeric value is refused, naming the key and never the value.
    /// </summary>
    public static DatabaseConnectionPolicy FromConfiguration(Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        DatabaseConnectionPolicy policy = new();

        policy.ApplicationPoolSize = Read(configuration, "Database:Pools:ApplicationPoolSize") ?? policy.ApplicationPoolSize;
        policy.ConversationLockPoolSize = Read(configuration, "Database:Pools:ConversationLockPoolSize") ?? policy.ConversationLockPoolSize;
        policy.ConnectTimeoutSeconds = Read(configuration, "Database:Timeouts:ConnectSeconds") ?? policy.ConnectTimeoutSeconds;
        policy.CommandTimeoutSeconds = Read(configuration, "Database:Timeouts:CommandSeconds") ?? policy.CommandTimeoutSeconds;
        policy.CancellationTimeoutMilliseconds = Read(configuration, "Database:Timeouts:CancellationMilliseconds") ?? policy.CancellationTimeoutMilliseconds;
        policy.LongCommandTimeoutSeconds = Read(configuration, "Database:Timeouts:LongCommandSeconds") ?? policy.LongCommandTimeoutSeconds;
        policy.ConversationLockWaitSeconds = Read(configuration, "Database:Timeouts:ConversationLockWaitSeconds") ?? policy.ConversationLockWaitSeconds;
        policy.BudgetConnections = Read(configuration, "Database:Budget:Connections");
        policy.BudgetMaxInstances = Read(configuration, "Database:Budget:MaxInstances");

        return policy;
    }

    private static int? Read(Microsoft.Extensions.Configuration.IConfiguration configuration, string key)
    {
        string? raw = configuration[key];

        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : throw new InvalidOperationException($"{key} must be a whole number.");
    }

    /// <summary>
    /// Refuses values that could not work, and — outside Development — a missing
    /// or exceeded budget.
    /// </summary>
    /// <param name="requireBudget">True outside Development.</param>
    public void Validate(bool requireBudget)
    {
        Require(ApplicationPoolSize, 1, 500, "Database:Pools:ApplicationPoolSize");
        Require(ConversationLockPoolSize, 1, 500, "Database:Pools:ConversationLockPoolSize");
        Require(ConnectTimeoutSeconds, 1, 60, "Database:Timeouts:ConnectSeconds");
        Require(CommandTimeoutSeconds, 1, 300, "Database:Timeouts:CommandSeconds");
        Require(CancellationTimeoutMilliseconds, 0, 30_000, "Database:Timeouts:CancellationMilliseconds");
        Require(LongCommandTimeoutSeconds, CommandTimeoutSeconds, 1_800, "Database:Timeouts:LongCommandSeconds");
        Require(ConversationLockWaitSeconds, 1, 600, "Database:Timeouts:ConversationLockWaitSeconds");

        if (BudgetConnections is null && BudgetMaxInstances is null)
        {
            if (requireBudget)
            {
                throw new InvalidOperationException(
                    "Database:Budget:Connections and Database:Budget:MaxInstances are required outside "
                    + "Development: without them nothing checks that every instance's connection pools "
                    + "fit the database. See docs/operations/production-deployment.md.");
            }

            return;
        }

        if (BudgetConnections is not int connections || BudgetMaxInstances is not int instances)
        {
            throw new InvalidOperationException(
                "Database:Budget:Connections and Database:Budget:MaxInstances must be set together.");
        }

        Require(connections, 1, 100_000, "Database:Budget:Connections");
        Require(instances, 1, 1_000, "Database:Budget:MaxInstances");

        long demand = (long)instances * PerInstanceConnections;

        if (demand > connections)
        {
            string arithmetic = string.Create(
                CultureInfo.InvariantCulture,
                $"{instances} instance(s) × ({ApplicationPoolSize} application + {ConversationLockPoolSize} conversation-lock + {ReadinessPoolSize} readiness) = {demand} connections, but Database:Budget:Connections is {connections}");

            throw new InvalidOperationException(
                "The database connection budget is exceeded: " + arithmetic + ". Lower "
                + "Database:Pools:ApplicationPoolSize or Database:Pools:ConversationLockPoolSize, "
                + "or raise the budget deliberately.");
        }
    }

    /// <summary>The EF Core connection string: application pool and ordinary timeouts.</summary>
    public string ApplicationConnectionString(string connectionString) =>
        Build(connectionString, ApplicationPoolSize, CommandTimeoutSeconds, "omniai-app");

    /// <summary>
    /// The advisory-lock connection string: its own pool, and a command timeout equal
    /// to the lock wait bound (the wait is the lock statement itself).
    /// </summary>
    public string ConversationLockConnectionString(string connectionString) =>
        Build(connectionString, ConversationLockPoolSize, ConversationLockWaitSeconds, "omniai-conversation-lock");

    private string Build(string connectionString, int poolSize, int commandTimeout, string applicationName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        NpgsqlConnectionStringBuilder builder = new(connectionString)
        {
            // Explicit, whatever the connection string said: the budget is only
            // meaningful if these are the values actually in force.
            MaxPoolSize = poolSize,
            MinPoolSize = 0,
            Timeout = ConnectTimeoutSeconds,
            CommandTimeout = commandTimeout,
            CancellationTimeout = CancellationTimeoutMilliseconds,
        };

        // Identifies the pool in pg_stat_activity, unless the operator named it.
        if (string.IsNullOrWhiteSpace(builder.ApplicationName))
        {
            builder.ApplicationName = applicationName;
        }

        return builder.ConnectionString;
    }

    private static void Require(int value, int minimum, int maximum, string key)
    {
        if (value < minimum || value > maximum)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"{key} must be between {minimum} and {maximum}; it is {value}."));
        }
    }
}
