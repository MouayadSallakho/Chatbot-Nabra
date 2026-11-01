using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using OmniAI.Application.Accounts;
using OmniAI.Application.WebChat.Protection;
using OmniAI.Infrastructure.WebChat.Protection;

namespace OmniAI.Infrastructure.Accounts;

/// <summary>
/// The account limits in Redis, shared by every API instance (Phase 37.3).
/// </summary>
/// <remarks>
/// <para>
/// <strong>The public chat's limiter, with other counters.</strong> The reset
/// endpoints use the same Lua script (<see cref="RedisPublicChatProtection.DecisionScript"/>),
/// which checks every counter of a decision before incrementing any, atomically, so a
/// request refused by one counter costs nothing in the others.
/// </para>
/// <para>
/// <strong>Sign-in counts failures only</strong> (Revision 1). Three counters, each
/// with its own window: one address on one network, one network, and one address on
/// every network. Before the password is checked, <see cref="ReserveScript"/> reads
/// them and, when there is room, atomically reserves an in-flight slot in each, so a
/// burst of parallel guesses cannot all pass before the first is counted; failures
/// plus sign-ins in flight never exceed a limit. A failure turns its slot into a
/// counted failure (<see cref="FailureScript"/>); a success releases its slot and
/// clears its address's failures on its network (<see cref="SuccessScript"/>), so
/// successes count for nothing. A slot whose outcome is never recorded expires after
/// <see cref="InFlightLifetime"/>.
/// </para>
/// <para>
/// <strong>Unknown is not a bucket</strong> (public-chat-protection.md P11). A
/// request with no establishable client address is refused as unprotected (503),
/// never counted in a shared lane an attacker could have to themselves.
/// </para>
/// </remarks>
internal sealed class RedisAccountProtection(IConnectionMultiplexer redis, AccountProtectionOptions limits) : IAccountProtection
{
    private readonly IConnectionMultiplexer _redis = redis ?? throw new ArgumentNullException(nameof(redis));
    private readonly AccountProtectionOptions _limits = limits ?? throw new ArgumentNullException(nameof(limits));

    /// <summary>
    /// How long an in-flight sign-in keeps its slot if its outcome is never
    /// recorded (the process died mid-request). A sign-in takes well under a second.
    /// </summary>
    internal static readonly TimeSpan InFlightLifetime = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Refuses when failures, or failures plus sign-ins still in flight, have reached
    /// a counter's limit; otherwise reserves one in-flight slot in every counter.
    /// </summary>
    /// <remarks>
    /// KEYS: the n failure counters, then their n in-flight twins. ARGV: limit and
    /// window per counter, then the in-flight lifetime. Atomic, so a burst of parallel
    /// guesses cannot all pass before the first one is counted. A refusal caused only
    /// by requests still in flight asks the caller to retry in one second.
    /// </remarks>
    internal const string ReserveScript = """
        local n = #KEYS / 2
        for index = 1, n do
          local limit = tonumber(ARGV[2 * index - 1])
          local failures = tonumber(redis.call('GET', KEYS[index]) or '0')
          if failures >= limit then
            local remaining = redis.call('PTTL', KEYS[index])
            if remaining == nil or remaining < 0 then
              remaining = tonumber(ARGV[2 * index]) * 1000
            end
            return {0, remaining}
          end
          local inflight = tonumber(redis.call('GET', KEYS[n + index]) or '0')
          if failures + inflight >= limit then
            return {0, 1000}
          end
        end
        for index = 1, n do
          redis.call('INCR', KEYS[n + index])
          redis.call('EXPIRE', KEYS[n + index], tonumber(ARGV[2 * n + 1]))
        end
        return {1, 0}
        """;

    /// <summary>Turns one in-flight slot into a counted failure, in every counter.</summary>
    internal const string FailureScript = """
        local n = #KEYS / 2
        for index = 1, n do
          if tonumber(redis.call('GET', KEYS[n + index]) or '0') > 0 then
            redis.call('DECR', KEYS[n + index])
          end
          local count = redis.call('INCR', KEYS[index])
          if count == 1 then
            redis.call('EXPIRE', KEYS[index], tonumber(ARGV[2 * index]))
          end
        end
        return {1, 0}
        """;

    /// <summary>
    /// Releases the in-flight slots of a successful sign-in and clears its address's
    /// failures on its network (KEYS[1]). Nothing is counted.
    /// </summary>
    internal const string SuccessScript = """
        local n = #KEYS / 2
        for index = 1, n do
          if tonumber(redis.call('GET', KEYS[n + index]) or '0') > 0 then
            redis.call('DECR', KEYS[n + index])
          end
        end
        redis.call('DEL', KEYS[1])
        return {1, 0}
        """;

    public Task<AccountProtectionDecision> CheckSignInAsync(
        string subjectKey,
        ClientNetworkKey? client,
        CancellationToken cancellationToken = default) =>
        SignInAsync(ReserveScript, subjectKey, client, cancellationToken);

    public async Task RecordSignInFailureAsync(
        string subjectKey,
        ClientNetworkKey? client,
        CancellationToken cancellationToken = default) =>
        await SignInAsync(FailureScript, subjectKey, client, cancellationToken).ConfigureAwait(false);

    public async Task RecordSignInSuccessAsync(
        string subjectKey,
        ClientNetworkKey? client,
        CancellationToken cancellationToken = default) =>
        await SignInAsync(SuccessScript, subjectKey, client, cancellationToken).ConfigureAwait(false);

    /// <summary>Runs a sign-in script over the three failure counters and their in-flight twins.</summary>
    private Task<AccountProtectionDecision> SignInAsync(
        string script,
        string subjectKey,
        ClientNetworkKey? client,
        CancellationToken cancellationToken)
    {
        string network = Network(client);

        Counter[] counters =
        [
            new(Key("signin:fail:address-network", subjectKey + ":" + network), _limits.SignInFailuresPerAddressOnNetwork),
            new(Key("signin:fail:network", network), _limits.SignInFailuresPerNetwork),
            new(Key("signin:fail:address", subjectKey), _limits.SignInFailuresPerAddress),
        ];

        RedisKey[] keys = new RedisKey[counters.Length * 2];
        RedisValue[] values = new RedisValue[(counters.Length * 2) + 1];

        for (int index = 0; index < counters.Length; index++)
        {
            keys[index] = counters[index].Key;
            keys[counters.Length + index] = counters[index].Key + ":inflight";
            values[index * 2] = counters[index].Window.Limit;
            values[(index * 2) + 1] = (long)Math.Ceiling(counters[index].Window.Window.TotalSeconds);
        }

        values[^1] = (long)InFlightLifetime.TotalSeconds;

        return EvaluateAsync(script, keys, values, cancellationToken);
    }

    public Task<AccountProtectionDecision> AuthorisePasswordResetRequestAsync(
        string subjectKey,
        ClientNetworkKey? client,
        CancellationToken cancellationToken = default) =>
        DecideAsync(
            [
                new(Key("forgot:address", subjectKey), _limits.ResetRequestsPerAddress),
                new(Key("forgot:network", Network(client)), _limits.ResetRequestsPerNetwork),
            ],
            cancellationToken);

    public Task<AccountProtectionDecision> AuthorisePasswordResetAsync(
        ClientNetworkKey? client,
        CancellationToken cancellationToken = default) =>
        DecideAsync(
            [
                new(Key("reset:network", Network(client)), _limits.ResetsPerNetwork),
            ],
            cancellationToken);

    private async Task<AccountProtectionDecision> DecideAsync(Counter[] counters, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        RedisKey[] keys = new RedisKey[counters.Length];
        RedisValue[] values = new RedisValue[counters.Length * 2];

        for (int index = 0; index < counters.Length; index++)
        {
            keys[index] = counters[index].Key;
            values[index * 2] = counters[index].Window.Limit;
            values[(index * 2) + 1] = (long)Math.Ceiling(counters[index].Window.Window.TotalSeconds);
        }

        return await EvaluateAsync(RedisPublicChatProtection.DecisionScript, keys, values, cancellationToken).ConfigureAwait(false);
    }

    private async Task<AccountProtectionDecision> EvaluateAsync(
        string script,
        RedisKey[] keys,
        RedisValue[] values,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        RedisResult result;

        try
        {
            result = await _redis.GetDatabase()
                .ScriptEvaluateAsync(script, keys, values)
                .ConfigureAwait(false);
        }
        catch (Exception failure) when (RedisPublicChatProtection.IsProtectionStoreFailure(failure, cancellationToken))
        {
            throw new AccountProtectionUnavailableException(
                "The account protection store could not be reached, so the request was refused rather than "
                + "served without a rate limit. " + failure.GetType().Name);
        }

        long[]? answer = (long[]?)result;

        if (answer is not { Length: 2 })
        {
            throw new AccountProtectionUnavailableException(
                "The account protection store returned an answer this platform could not read.");
        }

        return answer[0] == 1
            ? AccountProtectionDecision.Allow
            : AccountProtectionDecision.Reject(TimeSpan.FromMilliseconds(answer[1]));
    }

    private static string Network(ClientNetworkKey? client) =>
        client?.Value
        ?? throw new AccountProtectionUnavailableException(
            "The client's network could not be established, so no network limit can be applied.");

    private string Key(string scope, string value) =>
        string.Create(CultureInfo.InvariantCulture, $"{_limits.KeyPrefix}:{scope}:{value}");

    private readonly record struct Counter(string Key, RateLimitWindow Window);
}

/// <summary>Refuses every decision: no store is configured, so nothing is served unlimited.</summary>
internal sealed class UnprotectedAccounts(string reason) : IAccountProtection
{
    public Task<AccountProtectionDecision> CheckSignInAsync(
        string subjectKey,
        ClientNetworkKey? client,
        CancellationToken cancellationToken = default) =>
        throw new AccountProtectionUnavailableException(reason);

    public Task RecordSignInFailureAsync(
        string subjectKey,
        ClientNetworkKey? client,
        CancellationToken cancellationToken = default) =>
        throw new AccountProtectionUnavailableException(reason);

    public Task RecordSignInSuccessAsync(
        string subjectKey,
        ClientNetworkKey? client,
        CancellationToken cancellationToken = default) =>
        throw new AccountProtectionUnavailableException(reason);

    public Task<AccountProtectionDecision> AuthorisePasswordResetRequestAsync(
        string subjectKey,
        ClientNetworkKey? client,
        CancellationToken cancellationToken = default) =>
        throw new AccountProtectionUnavailableException(reason);

    public Task<AccountProtectionDecision> AuthorisePasswordResetAsync(
        ClientNetworkKey? client,
        CancellationToken cancellationToken = default) =>
        throw new AccountProtectionUnavailableException(reason);
}

/// <summary>Registers the account limiter and the address keys.</summary>
public static class AccountProtectionSetup
{
    /// <summary>
    /// After <see cref="PublicChatProtectionSetup.AddOmniAiPublicChatProtection"/>,
    /// whose Redis connection and key secret it shares.
    /// </summary>
    public static IServiceCollection AddOmniAiAccountProtection(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        AccountProtectionOptions limits = ReadLimits(configuration.GetSection(AccountProtectionOptions.SectionName));
        limits.Validate();
        services.AddSingleton(limits);

        services.AddSingleton<IAccountSubjectKeys>(provider =>
            new HmacAccountSubjectKeys(provider.GetRequiredService<NetworkKeySecret>()));

        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString(PublicChatProtectionSetup.RedisConnectionName)))
        {
            services.AddSingleton<IAccountProtection>(_ => new UnprotectedAccounts(
                "No account protection store is configured, so sign-in and password reset are refused. "
                + $"Set ConnectionStrings:{PublicChatProtectionSetup.RedisConnectionName}."));
        }
        else
        {
            services.AddSingleton<IAccountProtection>(provider => new RedisAccountProtection(
                provider.GetRequiredService<IConnectionMultiplexer>(),
                provider.GetRequiredService<AccountProtectionOptions>()));
        }

        return services;
    }

    private static AccountProtectionOptions ReadLimits(IConfigurationSection section)
    {
        AccountProtectionOptions defaults = new();

        return new AccountProtectionOptions
        {
            SignInFailuresPerAddressOnNetwork = Window(section, nameof(AccountProtectionOptions.SignInFailuresPerAddressOnNetwork), defaults.SignInFailuresPerAddressOnNetwork),
            SignInFailuresPerNetwork = Window(section, nameof(AccountProtectionOptions.SignInFailuresPerNetwork), defaults.SignInFailuresPerNetwork),
            SignInFailuresPerAddress = Window(section, nameof(AccountProtectionOptions.SignInFailuresPerAddress), defaults.SignInFailuresPerAddress),
            ResetRequestsPerAddress = Window(section, nameof(AccountProtectionOptions.ResetRequestsPerAddress), defaults.ResetRequestsPerAddress),
            ResetRequestsPerNetwork = Window(section, nameof(AccountProtectionOptions.ResetRequestsPerNetwork), defaults.ResetRequestsPerNetwork),
            ResetsPerNetwork = Window(section, nameof(AccountProtectionOptions.ResetsPerNetwork), defaults.ResetsPerNetwork),
            KeyPrefix = string.IsNullOrWhiteSpace(section[nameof(AccountProtectionOptions.KeyPrefix)])
                ? defaults.KeyPrefix
                : section[nameof(AccountProtectionOptions.KeyPrefix)]!.Trim(),
        };
    }

    private static RateLimitWindow Window(IConfigurationSection section, string name, RateLimitWindow fallback)
    {
        IConfigurationSection own = section.GetSection(name);

        return new RateLimitWindow(
            Number(own["Limit"], fallback.Limit, $"{name}:Limit"),
            TimeSpan.FromSeconds(Number(own["WindowSeconds"], (int)Math.Ceiling(fallback.Window.TotalSeconds), $"{name}:WindowSeconds")));
    }

    private static int Number(string? value, int fallback, string path)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return int.TryParse(value.Trim(), CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : throw new InvalidOperationException(
                $"{AccountProtectionOptions.SectionName}:{path} must be a whole number.");
    }
}
