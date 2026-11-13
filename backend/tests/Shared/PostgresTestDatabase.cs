using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using OmniAI.Domain.Plans;
using OmniAI.Domain.Tenants;
using OmniAI.Infrastructure.Persistence;

namespace OmniAI.Testing;

/// <summary>
/// A disposable PostgreSQL database, created fresh for one test class and dropped
/// afterwards.
/// </summary>
/// <remarks>
/// These tests run against a real PostgreSQL server. That is deliberate and not
/// negotiable: the whole point of this layer is PostgreSQL-specific behaviour --
/// <c>uuid</c> columns, <c>timestamptz</c>, identity columns, the <c>xmin</c>
/// system column, unique indexes and foreign keys. The EF Core in-memory provider
/// implements none of it, so passing against it would prove nothing.
/// <para>
/// The server is located through the <c>OMNIAI_TEST_POSTGRES</c> environment
/// variable, which must hold a maintenance connection string for a throwaway
/// server. If it is absent the fixture fails loudly rather than quietly
/// substituting a fake, so a green run always means PostgreSQL was exercised.
/// No credential is compiled in or committed.
/// </para>
/// Each test class gets its own randomly named database, so classes can run in
/// parallel without seeing each other's rows.
/// <para>
/// Compiled into both the Infrastructure and the API integration test projects
/// from <c>backend/tests/Shared</c>, following the convention already used for
/// <c>AssemblyReferenceReader</c>. One provisioning implementation rather than
/// two: a second copy of "create a database, migrate it, drop it" is exactly the
/// kind of duplicate that drifts until the two suites are testing different
/// schemas.
/// </para>
/// </remarks>
public sealed class PostgresTestDatabase : IAsyncLifetime
{
    private const string ConnectionEnvironmentVariable = "OMNIAI_TEST_POSTGRES";

    private string _maintenanceConnectionString = string.Empty;
    private string _databaseName = string.Empty;

    /// <summary>Connection string for this test class's own database.</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        _maintenanceConnectionString =
            Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable)
            ?? throw new InvalidOperationException(
                $"Environment variable {ConnectionEnvironmentVariable} is not set. " +
                "The Infrastructure integration tests require a real, disposable PostgreSQL " +
                "server; they will not fall back to a fake provider. Set it to a maintenance " +
                "connection string for a throwaway server, for example a test container or a " +
                "local ephemeral cluster. Never point it at a production database.");

        _databaseName = $"omniai_test_{Guid.NewGuid():N}";

        await ExecuteOnMaintenanceDatabaseAsync($"CREATE DATABASE \"{_databaseName}\"");

        ConnectionString = new NpgsqlConnectionStringBuilder(_maintenanceConnectionString)
        {
            Database = _databaseName,
        }.ConnectionString;

        // Apply the real migration, so the schema under test is the schema the
        // migration produces -- not one EnsureCreated inferred from the model.
        await using OmniAiDbContext context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_databaseName.Length == 0)
        {
            return;
        }

        NpgsqlConnection.ClearAllPools();

        await DropDatabaseAsync(_maintenanceConnectionString, _databaseName);
    }

    /// <summary>
    /// Drops a throwaway database, retrying a transient failure a bounded number
    /// of times.
    /// </summary>
    /// <remarks>
    /// Under a full run's load the drop has failed once with a transient
    /// <see cref="NpgsqlException"/>, which failed the class's cleanup although
    /// every test passed. Only a transient error is retried, and the last
    /// attempt's error is thrown unchanged, so a real problem still fails the run.
    /// </remarks>
    public static async Task DropDatabaseAsync(string maintenanceConnectionString, string databaseName)
    {
        const int MaxAttempts = 3;
        TimeSpan delay = TimeSpan.FromMilliseconds(250);

        for (int attempt = 1; ; attempt++)
        {
            try
            {
                await using NpgsqlConnection connection = new(maintenanceConnectionString);
                await connection.OpenAsync();

                await using NpgsqlCommand command = new(
                    $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)",
                    connection);
                await command.ExecuteNonQueryAsync();
                return;
            }
            catch (NpgsqlException exception) when (exception.IsTransient && attempt < MaxAttempts)
            {
                await Task.Delay(delay);
            }
        }
    }

    /// <summary>
    /// A new context over this test database. Each call is a separate unit of
    /// work with its own change tracker, which is what makes it possible to
    /// simulate two competing writers.
    /// </summary>
    public OmniAiDbContext CreateContext(params IInterceptor[] interceptors)
    {
        DbContextOptionsBuilder<OmniAiDbContext> builder =
            new DbContextOptionsBuilder<OmniAiDbContext>()
                .UseNpgsql(ConnectionString);

        // Every workspace gets a plan, exactly as onboarding gives it one.
        builder.AddInterceptors(new AssignsLegacyPlanInterceptor());

        if (interceptors.Length > 0)
        {
            builder.AddInterceptors(interceptors);
        }

        return new OmniAiDbContext(builder.Options);
    }

    /// <summary>Runs a statement against this test database and returns one scalar.</summary>
    public async Task<object?> ExecuteScalarAsync(string sql)
    {
        await using NpgsqlConnection connection = new(ConnectionString);
        await connection.OpenAsync();

        await using NpgsqlCommand command = new(sql, connection);
        return await command.ExecuteScalarAsync();
    }

    private async Task ExecuteOnMaintenanceDatabaseAsync(string sql)
    {
        await using NpgsqlConnection connection = new(_maintenanceConnectionString);
        await connection.OpenAsync();

        await using NpgsqlCommand command = new(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}

/// <summary>
/// Gives every workspace a plan the moment one is inserted.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This makes fixtures match production, not diverge from it.</strong> In
/// the running application a workspace cannot exist without a plan: onboarding
/// writes the assignment in the same transaction as the tenant, and the migration
/// backfilled every workspace that predates plans. Fixtures that insert a
/// <c>Tenant</c> aggregate directly bypass onboarding, so without this they would
/// produce something the application never produces — a workspace with no
/// entitlements at all.
/// </para>
/// <para>
/// <strong>It does not soften any rule under test.</strong> The plan assigned is
/// the unlimited legacy one, so entitlements are not what these suites are
/// measuring; the suites that <em>do</em> measure entitlements assign their own
/// finite plan explicitly and overwrite this one. And the service's refusal to
/// treat a missing assignment as unlimited stays fully exercised — its own tests
/// delete the assignment and assert the refusal.
/// </para>
/// </remarks>
public sealed class AssignsLegacyPlanInterceptor : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Assign(eventData.Context);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Assign(eventData.Context);

        return base.SavingChanges(eventData, result);
    }

    private static void Assign(DbContext? context)
    {
        if (context is not OmniAiDbContext database)
        {
            return;
        }

        List<Tenant> added =
        [
            .. database.ChangeTracker
                .Entries<Tenant>()
                .Where(entry => entry.State == EntityState.Added)
                .Select(entry => entry.Entity),
        ];

        if (added.Count == 0)
        {
            return;
        }

        // A fixture that assigned its own plan in the same unit of work keeps it.
        HashSet<TenantId> alreadyAssigned =
        [
            .. database.ChangeTracker
                .Entries<WorkspacePlanAssignment>()
                .Select(entry => entry.Entity.TenantId),
        ];

        foreach (Tenant tenant in added.Where(tenant => !alreadyAssigned.Contains(tenant.Id)))
        {
            database.WorkspacePlanAssignments.Add(
                WorkspacePlanAssignment.Create(
                    tenant.Id,
                    SeededPlans.Legacy,
                    DateTimeOffset.UtcNow));
        }
    }
}
