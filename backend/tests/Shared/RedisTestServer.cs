using System.Security.Cryptography;
using StackExchange.Redis;

namespace OmniAI.Testing;

/// <summary>
/// A real Redis, with a key namespace of this test class's own.
/// </summary>
/// <remarks>
/// <strong>Real, and not negotiable.</strong> Everything worth testing about the
/// protection layer is Redis-specific: that a Lua script evaluates atomically,
/// that <c>INCR</c> and <c>EXPIRE</c> together produce an anchored window, that
/// <c>PTTL</c> gives an honest retry hint, that two application instances sharing
/// a server see one another's counts, and that keys really do disappear. An
/// in-memory fake would implement whatever this platform assumed and would pass
/// while the assumption was wrong — which for a distributed limiter is the only
/// failure that matters.
/// <para>
/// The server is located through <c>OMNIAI_TEST_REDIS</c>, which must hold a
/// connection string for a throwaway instance. If it is absent the fixture fails
/// loudly rather than substituting a fake, so a green run always means Redis was
/// exercised. No credential is compiled in.
/// </para>
/// <para>
/// <strong>Isolation is by key prefix, not by database or by flushing.</strong>
/// Test classes run in parallel and share the server. Selecting a numbered
/// database would run out at sixteen and is unavailable on a cluster;
/// <c>FLUSHDB</c> would delete another class's counters mid-assertion. A unique
/// prefix per instance, handed to the application as ordinary configuration,
/// keeps every class in its own namespace and is also how a deployment would
/// separate two environments sharing a server.
/// </para>
/// <para>
/// <strong>It holds no connection of its own.</strong> Each helper opens one and
/// closes it, and <see cref="ConnectAsync"/> hands the caller a client they own.
/// That is not incidental tidiness: the multi-instance tests need two genuinely
/// independent clients against one server — which is exactly the shape of two API
/// instances — and a shared field would have quietly made them one.
/// </para>
/// </remarks>
public sealed class RedisTestServer
{
    private const string ConnectionEnvironmentVariable = "OMNIAI_TEST_REDIS";

    public RedisTestServer()
    {
        ConnectionString =
            Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable)
            ?? throw new InvalidOperationException(
                $"Environment variable {ConnectionEnvironmentVariable} is not set. The public "
                + "chat protection tests require a real, disposable Redis; they will not fall "
                + "back to an in-memory fake, because a fake would implement whatever this "
                + "platform assumed rather than what Redis actually does. Set it to a "
                + "connection string for a throwaway instance, for example a local container. "
                + "Never point it at a production instance.");

        KeyPrefix = $"omniai:test:{Guid.NewGuid():N}";

        // Random per fixture, so nothing secret-shaped is committed to the
        // repository and every class derives its own network keys. The tests that
        // care about key derivation build their own key source explicitly.
        NetworkKeySecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    }

    /// <summary>How to reach the throwaway server.</summary>
    public string ConnectionString { get; }

    /// <summary>The namespace this test class's counters live in.</summary>
    public string KeyPrefix { get; }

    /// <summary>
    /// A network-key secret for this fixture, generated rather than committed.
    /// </summary>
    public string NetworkKeySecret { get; }

    /// <summary>
    /// A new client for this server, which the caller owns and must dispose.
    /// </summary>
    public static async Task<ConnectionMultiplexer> ConnectAsync(string connectionString)
    {
        ConfigurationOptions options = ConfigurationOptions.Parse(connectionString);

        options.AbortOnConnectFail = false;

        return await ConnectionMultiplexer.ConnectAsync(options);
    }

    /// <summary>A new client for this server, which the caller owns.</summary>
    public Task<ConnectionMultiplexer> ConnectAsync() => ConnectAsync(ConnectionString);

    /// <summary>
    /// Every key in this fixture's namespace.
    /// </summary>
    /// <remarks>
    /// Used to prove what the protection layer does and does not store: that
    /// counters exist while a window is open, that they carry a bounded TTL, and
    /// that no key contains an address, a credential or a customer's words.
    /// </remarks>
    public async Task<IReadOnlyList<string>> KeysAsync()
    {
        await using ConnectionMultiplexer client = await ConnectAsync();

        List<string> keys = [];

        foreach (System.Net.EndPoint endpoint in client.GetEndPoints())
        {
            IServer server = client.GetServer(endpoint);

            if (server.IsReplica)
            {
                continue;
            }

            await foreach (RedisKey key in server.KeysAsync(pattern: $"{KeyPrefix}*"))
            {
                keys.Add(key.ToString());
            }
        }

        return keys;
    }

    /// <summary>How long a key has left, or null when it has none or is gone.</summary>
    public async Task<TimeSpan?> TimeToLiveAsync(string key)
    {
        await using ConnectionMultiplexer client = await ConnectAsync();

        return await client.GetDatabase().KeyTimeToLiveAsync(key);
    }

    /// <summary>The current value of one counter, or null when it does not exist.</summary>
    public async Task<long?> CounterAsync(string key)
    {
        await using ConnectionMultiplexer client = await ConnectAsync();

        RedisValue value = await client.GetDatabase().StringGetAsync(key);

        return value.IsNull ? null : (long)value;
    }

    /// <summary>Removes this fixture's keys, so one test does not see another's.</summary>
    /// <remarks>
    /// Scoped to this prefix rather than a <c>FLUSHDB</c>, which would delete the
    /// counters of every class running in parallel.
    /// </remarks>
    public async Task ClearAsync()
    {
        IReadOnlyList<string> keys = await KeysAsync();

        if (keys.Count == 0)
        {
            return;
        }

        await using ConnectionMultiplexer client = await ConnectAsync();

        await client.GetDatabase()
            .KeyDeleteAsync([.. keys.Select(key => new RedisKey(key))]);
    }
}
