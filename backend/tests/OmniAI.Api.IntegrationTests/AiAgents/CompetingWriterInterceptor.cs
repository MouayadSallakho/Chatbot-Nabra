using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace OmniAI.Api.IntegrationTests.AiAgents;

/// <summary>
/// Drops one out-of-band write into the window between the API reading a row and
/// writing it back.
/// </summary>
/// <remarks>
/// Test scaffolding for a race that is otherwise not reproducible on demand. Two
/// genuinely parallel HTTP requests may or may not overlap in the way that
/// produces a stale write, so a test built on them either passes for the wrong
/// reason or fails intermittently. This makes the overlap certain: when armed, it
/// runs a single UPDATE on its own connection at the moment
/// <c>SaveChanges</c> begins -- after the request has read the row and computed
/// its change, before the resulting UPDATE reaches PostgreSQL.
/// <para>
/// It simulates the other writer, and nothing else. The endpoint, the handler,
/// the repository, EF Core, Npgsql and the <c>xmin</c> predicate are all the real
/// ones, and the conflict PostgreSQL then reports is a real conflict -- the row
/// really was modified by a different transaction on a different connection.
/// </para>
/// <para>
/// It disarms itself after firing, so exactly one request in a test sees the
/// competing write.
/// </para>
/// </remarks>
public sealed class CompetingWriterInterceptor : SaveChangesInterceptor
{
    private string? _connectionString;
    private string? _sql;

    /// <summary>True once the competing write has actually run.</summary>
    public bool Fired { get; private set; }

    /// <summary>Arms a single competing write.</summary>
    public void Arm(string connectionString, string sql)
    {
        _connectionString = connectionString;
        _sql = sql;
        Fired = false;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        // Exchange, so this fires exactly once even if two saves race.
        string? sql = Interlocked.Exchange(ref _sql, null);

        if (sql is not null && _connectionString is not null)
        {
            await using NpgsqlConnection connection = new(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using NpgsqlCommand command = new(sql, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);

            Fired = true;
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken)
            .ConfigureAwait(false);
    }
}
