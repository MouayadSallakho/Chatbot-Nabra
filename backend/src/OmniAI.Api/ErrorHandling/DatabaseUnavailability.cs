using System.IO;
using System.Net.Sockets;
using Npgsql;

namespace OmniAI.Api.ErrorHandling;

/// <summary>
/// Recognises a request that failed because the database could not be reached or
/// could not accept another connection -- not because anything was wrong with the
/// request or with the code serving it.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why this exists (Phase 34).</strong> Under load a process can ask the
/// server for more connections than it allows (<c>53300 too_many_connections</c>),
/// or wait out its own pool (<c>The connection pool has been exhausted</c>); during
/// an outage the connection is refused or times out. All of these used to reach
/// the default handler and answer <c>500</c> -- telling the caller and the operator
/// that the server had a bug, when what it had was a dependency that was briefly
/// unavailable. The honest answer is <c>503</c>, the same one readiness gives.
/// </para>
/// <para>
/// <strong>Deliberately narrow.</strong> Only connection-level unavailability:
/// SQLSTATE class 08 (connection exception), class 53 (insufficient resources,
/// including <c>53300</c>), <c>57P01</c>-<c>57P03</c> (shutting down / cannot connect
/// now), and Npgsql's own client-side failures whose cause is a timeout, a socket
/// or a broken stream. A deadlock (<c>40P01</c>), a serialization failure
/// (<c>40001</c>), a cancelled statement (<c>57014</c>), a constraint violation or any
/// other server error is <em>not</em> recognised: those are Npgsql-"transient" too,
/// but they can be caused by the code, and mapping them to 503 would hide a real
/// defect behind an availability message.
/// </para>
/// <para>
/// Nothing from the exception reaches the caller. The operator's log gets the
/// SQLSTATE (or the client-side cause) and nothing else: no host, port, database
/// name, user or statement.
/// </para>
/// </remarks>
internal static class DatabaseUnavailability
{
    /// <summary>
    /// True when <paramref name="exception"/> (or anything it wraps) is a
    /// connection-level database unavailability; <paramref name="reason"/> is then a
    /// short, secret-free description for the log.
    /// </summary>
    public static bool Recognises(Exception exception, out string reason)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case PostgresException server when IsUnavailableState(server.SqlState):
                    reason = $"database unavailable (SQLSTATE {server.SqlState})";

                    return true;

                case PostgresException:
                    // A server-side answer that is not about availability decides
                    // the matter: whatever it wraps, this was not an outage.
                    reason = string.Empty;

                    return false;

                case NpgsqlException client when client.InnerException is TimeoutException:
                    reason = "database unavailable (connection timeout or pool exhausted)";

                    return true;

                case NpgsqlException client when client.InnerException is SocketException or IOException:
                    reason = "database unavailable (connection failed)";

                    return true;

                default:
                    break;
            }
        }

        reason = string.Empty;

        return false;
    }

    private static bool IsUnavailableState(string sqlState) =>
        sqlState.StartsWith("08", StringComparison.Ordinal)
        || sqlState.StartsWith("53", StringComparison.Ordinal)
        || sqlState is "57P01" or "57P02" or "57P03";
}
