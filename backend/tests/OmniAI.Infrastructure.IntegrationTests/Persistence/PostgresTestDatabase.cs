using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using OmniAI.Infrastructure.Persistence;

namespace OmniAI.Infrastructure.IntegrationTests.Persistence;

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

        await ExecuteOnMaintenanceDatabaseAsync(
            $"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)");
    }

    /// <summary>
    /// A new context over this test database. Each call is a separate unit of
    /// work with its own change tracker, which is what makes it possible to
    /// simulate two competing writers.
    /// </summary>
    public OmniAiDbContext CreateContext(RecordedSql? recorder = null)
    {
        DbContextOptionsBuilder<OmniAiDbContext> builder =
            new DbContextOptionsBuilder<OmniAiDbContext>()
                .UseNpgsql(ConnectionString);

        if (recorder is not null)
        {
            builder.AddInterceptors(recorder);
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
/// Records the SQL a context actually sends, so a test can assert on what was
/// <em>not</em> queried.
/// </summary>
/// <remarks>
/// Counting tracked entities would only show what EF materialised. This shows
/// the statements themselves, which is what makes "appending a message does not
/// read the message history" a structural claim rather than an inference.
/// </remarks>
public sealed class RecordedSql : DbCommandInterceptor
{
    private readonly List<string> _statements = [];

    public IReadOnlyList<string> Statements => _statements;

    public void Clear() => _statements.Clear();

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        Record(command);
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Record(command);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result)
    {
        Record(command);
        return base.ScalarExecuting(command, eventData, result);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result)
    {
        Record(command);
        return base.NonQueryExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Record(command);
        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    private void Record(DbCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        lock (_statements)
        {
            _statements.Add(command.CommandText);
        }
    }
}
