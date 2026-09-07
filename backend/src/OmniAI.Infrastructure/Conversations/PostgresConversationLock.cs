using System.Globalization;
using Npgsql;
using OmniAI.Application.Conversations;

namespace OmniAI.Infrastructure.Conversations;

/// <summary>
/// Serialises one conversation's generation using a PostgreSQL advisory lock.
/// </summary>
/// <remarks>
/// <strong>Why the database and not the process.</strong> The platform will run
/// more than one API instance. An in-process lock is correct until the second
/// instance starts and then fails silently — no error, no failing test, just two
/// replies produced against the same history. PostgreSQL is already the one thing
/// every instance shares, so it is where a boundary between them can actually
/// hold.
/// <para>
/// <strong>Why an advisory lock and not a row lock.</strong> A row lock requires
/// an open transaction, and this hold has to span a call to a third-party service
/// that may take many seconds. Holding a transaction open across that would pin a
/// connection, hold row locks, and keep a snapshot alive for as long as somebody
/// else's service felt like taking — which under load is how a database runs out
/// of connections and how vacuum stops keeping up. A session-level advisory lock
/// holds no rows and blocks no other work.
/// </para>
/// <para>
/// <strong>Crash safety comes from the connection, not from this code.</strong>
/// The lock is taken on a dedicated connection which is closed when the hold is
/// disposed. If this process dies mid-generation the connection dies with it, and
/// PostgreSQL releases every advisory lock that session held. There is no lease to
/// expire, no heartbeat to miss, and no state that can be left stuck — which is
/// exactly the failure a home-grown "lock row with an expiry" table would have.
/// </para>
/// <para>
/// <strong>Scope is one conversation.</strong> The key is derived from the
/// conversation id, so unrelated visitors never wait for one another. Because
/// PostgreSQL's advisory keys are 64 bits and a conversation id is 128, two
/// conversations could in principle share a key; the only consequence is that
/// those two would take turns, which is a rare and harmless loss of concurrency
/// rather than a correctness problem. Nothing anywhere depends on two different
/// conversations being able to run at the same instant.
/// </para>
/// </remarks>
internal sealed class PostgresConversationLock(NpgsqlDataSource dataSource) : IConversationLock
{
    private readonly NpgsqlDataSource _dataSource =
        dataSource ?? throw new ArgumentNullException(nameof(dataSource));

    public async Task<IAsyncDisposable> AcquireAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        NpgsqlConnection connection = await _dataSource
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            await using NpgsqlCommand command = connection.CreateCommand();

            // Blocking, not try-and-fail. Two different messages sent at once to
            // the same conversation must both be answered, one after the other --
            // refusing the second would lose a customer's message rather than
            // serialise it. The wait is bounded by the caller's cancellation
            // token, so a visitor who has closed the tab stops queueing.
            command.CommandText = "SELECT pg_advisory_lock(@key)";
            command.Parameters.AddWithValue("key", KeyFor(conversationId));

            // No command timeout: the wait is as long as the conversation ahead of
            // it takes, and cancellation is what ends it early.
            command.CommandTimeout = 0;

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            return new Hold(connection, KeyFor(conversationId));
        }
        catch
        {
            // Nothing was acquired, so the connection is simply returned. Written
            // explicitly because the hold is what would otherwise own it.
            await connection.DisposeAsync().ConfigureAwait(false);

            throw;
        }
    }

    /// <summary>
    /// Folds a conversation id into the 64 bits an advisory lock key has.
    /// </summary>
    /// <remarks>
    /// XOR of the two halves. Conversation ids are UUIDv7, so their high bits are
    /// a timestamp and their low bits are random; combining both means two
    /// conversations created in the same millisecond are still separated by the
    /// random half.
    /// </remarks>
    private static long KeyFor(Guid conversationId)
    {
        Span<byte> bytes = stackalloc byte[16];

        if (!conversationId.TryWriteBytes(bytes))
        {
            throw new InvalidOperationException("A conversation id could not be read as bytes.");
        }

        long high = BitConverter.ToInt64(bytes[..8]);
        long low = BitConverter.ToInt64(bytes[8..]);

        return high ^ low;
    }

    /// <summary>
    /// The hold. Disposing releases the lock and returns the connection.
    /// </summary>
    /// <remarks>
    /// The explicit unlock is a courtesy so a pooled connection is not returned
    /// still holding a lock; closing the session would release it anyway. Both
    /// paths matter: the unlock covers the ordinary case, and the close covers the
    /// case where this code never runs at all.
    /// </remarks>
    private sealed class Hold(NpgsqlConnection connection, long key) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using NpgsqlCommand command = connection.CreateCommand();

                command.CommandText = "SELECT pg_advisory_unlock(@key)";
                command.Parameters.AddWithValue("key", key);

                // Deliberately not cancellable. A caller that has been cancelled
                // still has to let go, and passing a cancelled token here would
                // skip the unlock on exactly the path that most needs it.
                await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (NpgsqlException)
            {
                // The connection is already broken, which means the session is
                // gone, which means PostgreSQL has released the lock. Nothing to
                // recover and nothing to report: the invariant this class exists
                // to keep is intact either way.
            }
            finally
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>The lock key, for diagnostics and tests.</summary>
    internal static string DescribeKey(Guid conversationId) =>
        KeyFor(conversationId).ToString(CultureInfo.InvariantCulture);
}
