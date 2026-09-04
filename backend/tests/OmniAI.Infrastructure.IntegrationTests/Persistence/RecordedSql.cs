using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace OmniAI.Infrastructure.IntegrationTests.Persistence;

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
