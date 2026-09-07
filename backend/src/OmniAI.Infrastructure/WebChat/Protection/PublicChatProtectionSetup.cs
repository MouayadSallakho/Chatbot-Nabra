using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using OmniAI.Application.WebChat.Protection;

namespace OmniAI.Infrastructure.WebChat.Protection;

/// <summary>
/// Registers the public-chat protection layer: its policy, its counter store and
/// its network-key source.
/// </summary>
/// <remarks>
/// Registration lives beside the implementations, so nothing above this project
/// knows the counters are in Redis, that the client is StackExchange.Redis, or
/// that the network key is an HMAC. The composition root only decides where
/// configuration comes from.
/// </remarks>
public static partial class PublicChatProtectionSetup
{
    /// <summary>
    /// Connection string naming the Redis that holds the protection counters.
    /// </summary>
    /// <remarks>
    /// Ordinary deployment configuration, and blank in <c>appsettings.json</c> for
    /// the same reason the database one is: a real endpoint in source control is a
    /// committed deployment fact, and a plausible default is worse because a
    /// misconfigured deployment would quietly count somewhere else.
    /// </remarks>
    public const string RedisConnectionName = "OmniAiProtectionRedis";

    /// <summary>
    /// Environment variable holding the network-key HMAC secret.
    /// </summary>
    /// <remarks>
    /// <strong>A flat variable rather than a key inside the policy
    /// section.</strong> Everything in <c>PublicChatProtection</c> is a tunable
    /// non-secret that belongs in <c>appsettings</c> and in a config map; this is
    /// the one value that must never appear in either. Keeping it out of the
    /// section makes that distinction impossible to miss, exactly as
    /// <c>OPENAI_API_KEY</c> does. A test asserts no settings file in the
    /// repository contains it.
    /// </remarks>
    public const string NetworkKeySecretVariable = "PUBLIC_CHAT_NETWORK_KEY_SECRET";

    /// <summary>
    /// The shortest secret this platform will accept.
    /// </summary>
    /// <remarks>
    /// Thirty-two characters. A short secret is a guessable secret, and a
    /// guessable HMAC key means the pseudonym can be reversed by anybody who
    /// obtains the counter names — which is the entire property it exists to
    /// provide. Refused rather than accepted with a warning: a warning in a log
    /// nobody reads is how a two-character key reaches production.
    /// </remarks>
    public const int MinimumSecretLength = 32;

    /// <summary>
    /// Adds the protection policy, the Redis counter store and the network-key
    /// source.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">
    /// Configuration carrying the non-secret <c>PublicChatProtection</c> section,
    /// the Redis connection string, and the network-key secret from the
    /// environment.
    /// </param>
    /// <param name="allowEphemeralNetworkKeySecret">
    /// Whether a missing secret may be replaced by a random per-process one.
    /// <para>
    /// True only in Development, and the composition root decides that — this
    /// project deliberately holds no opinion about which environment it is running
    /// in. It is <strong>not</strong> a protection bypass: every limit is still
    /// enforced in full, and the only difference is that the buckets a network
    /// falls into are renamed when the process restarts. What it buys is a
    /// developer who can run the hosted chat after cloning the repository without
    /// first being told about a setting.
    /// </para>
    /// <para>
    /// Nothing is compiled in, in either case. There is no default secret in this
    /// source file to be found and used in production by accident.
    /// </para>
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// The configured policy is not usable, or the secret is too short. Both are
    /// startup failures: a mistyped limit and a two-character key are deployment
    /// mistakes an operator must see, not runtime behaviour a customer discovers.
    /// </exception>
    public static IServiceCollection AddOmniAiPublicChatProtection(
        this IServiceCollection services,
        IConfiguration configuration,
        bool allowEphemeralNetworkKeySecret)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        PublicChatProtectionOptions limits = ReadLimits(configuration);

        // Checked now rather than on the first public request, so a mistyped
        // window is a deployment failure rather than a rate limit somebody's
        // customer runs into.
        limits.Validate();

        services.AddSingleton(limits);

        AddCounterStore(services, configuration.GetConnectionString(RedisConnectionName));
        AddNetworkKeys(
            services,
            configuration[NetworkKeySecretVariable],
            allowEphemeralNetworkKeySecret);

        return services;
    }

    /// <summary>
    /// Registers the counter store, or the refusal that stands in for it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong><c>AbortOnConnectFail</c> is off, deliberately.</strong> With it on,
    /// building the multiplexer throws when Redis is not yet up, which would make
    /// the API refuse to start behind a Redis that is still booting — and, worse,
    /// would never reconnect after an outage without a restart. With it off the
    /// application starts, commands fail while Redis is away, and the client
    /// reconnects by itself when it returns. That is what makes an outage
    /// recoverable without touching the API, and it costs nothing in safety
    /// because a failed command is a refused request.
    /// </para>
    /// <para>
    /// <strong>Built lazily.</strong> Constructing the multiplexer in the factory
    /// rather than eagerly means a deployment with an unreachable Redis still
    /// starts and still serves its admin, and the failure appears where the
    /// protection is used.
    /// </para>
    /// </remarks>
    private static void AddCounterStore(IServiceCollection services, string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddSingleton<IPublicChatProtection>(_ => new UnprotectedPublicChat(
                "No public chat protection store is configured, so public web chat writes are "
                + $"refused. Set ConnectionStrings:{RedisConnectionName}."));

            return;
        }

        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            ConfigurationOptions options = ConfigurationOptions.Parse(connectionString);

            options.AbortOnConnectFail = false;

            // Named so an operator looking at Redis can tell which application's
            // connections these are.
            options.ClientName = "omniai-public-chat-protection";

            return ConnectionMultiplexer.Connect(options);
        });

        services.AddSingleton<IPublicChatProtection>(provider => new RedisPublicChatProtection(
            provider.GetRequiredService<IConnectionMultiplexer>(),
            provider.GetRequiredService<PublicChatProtectionOptions>()));
    }

    /// <summary>Registers the network-key source.</summary>
    private static void AddNetworkKeys(
        IServiceCollection services,
        string? secret,
        bool allowEphemeral)
    {
        if (!string.IsNullOrWhiteSpace(secret))
        {
            if (secret.Trim().Length < MinimumSecretLength)
            {
                throw new InvalidOperationException(
                    $"{NetworkKeySecretVariable} must be at least "
                    + $"{MinimumSecretLength.ToString(CultureInfo.InvariantCulture)} characters. "
                    + "A guessable key lets anybody who obtains the rate-limit counters recover "
                    + "visitors' network addresses from them, which is the one thing the key "
                    + "exists to prevent.");
            }

            services.AddSingleton<IClientNetworkKeys>(
                _ => HmacClientNetworkKeys.From(secret.Trim()));

            return;
        }

        if (!allowEphemeral)
        {
            services.AddSingleton<IClientNetworkKeys>(_ => HmacClientNetworkKeys.Unavailable(
                "No public chat network key secret is configured, so no trustworthy rate-limit "
                + "identity can be derived and public web chat writes are refused. Set the "
                + $"{NetworkKeySecretVariable} environment variable."));

            return;
        }

        services.AddSingleton<IClientNetworkKeys>(provider =>
        {
            // Random, per process, never written anywhere. Restarting renames the
            // buckets; it does not remove them.
            string ephemeral = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

            LogEphemeralSecret(
                provider
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger(typeof(PublicChatProtectionSetup).FullName!),
                NetworkKeySecretVariable);

            return HmacClientNetworkKeys.From(ephemeral);
        });
    }

    /// <summary>
    /// Says out loud that the network keys will not survive a restart.
    /// </summary>
    /// <remarks>
    /// Warning rather than Information. The rate limits are fully enforced, so
    /// nothing is unsafe — but a developer chasing why a limit reset needs this to
    /// be the first thing they find, and anybody who somehow reached this branch
    /// outside Development needs to notice immediately.
    /// <para>
    /// Source-generated rather than a <c>LogWarning</c> call, matching the rest of
    /// the platform: the template is compiled and the argument is not boxed into a
    /// <c>params</c> array when the level is disabled.
    /// </para>
    /// </remarks>
    [LoggerMessage(
        EventId = 1100,
        Level = LogLevel.Warning,
        Message = "No {Variable} is configured. A random per-process key is being used to "
            + "derive rate-limit identities: the limits are enforced in full, but the buckets "
            + "are renamed on every restart. Configure the variable outside development.")]
    private static partial void LogEphemeralSecret(ILogger logger, string variable);

    /// <summary>
    /// Reads the policy from configuration, explicitly.
    /// </summary>
    /// <remarks>
    /// Read by hand rather than through the configuration binder, matching the
    /// OpenAI options: it avoids a package for a handful of settings, and it means
    /// an unrecognised key is visibly not read rather than silently ignored by a
    /// reflective bind. A present-but-unparseable value is an error, never a
    /// silent fallback — a deployment that meant to cap generation at thirty a
    /// minute and mistyped it must not run at whatever this code happens to
    /// prefer.
    /// </remarks>
    private static PublicChatProtectionOptions ReadLimits(IConfiguration configuration)
    {
        IConfigurationSection section =
            configuration.GetSection(PublicChatProtectionOptions.SectionName);

        PublicChatProtectionOptions defaults = new();

        return new PublicChatProtectionOptions
        {
            ConversationStartsPerClient = Window(
                section,
                nameof(PublicChatProtectionOptions.ConversationStartsPerClient),
                defaults.ConversationStartsPerClient),
            ConversationStartsPerEndpoint = Window(
                section,
                nameof(PublicChatProtectionOptions.ConversationStartsPerEndpoint),
                defaults.ConversationStartsPerEndpoint),
            RequestsPerClient = Window(
                section,
                nameof(PublicChatProtectionOptions.RequestsPerClient),
                defaults.RequestsPerClient),
            MessagesPerConversation = Window(
                section,
                nameof(PublicChatProtectionOptions.MessagesPerConversation),
                defaults.MessagesPerConversation),
            GenerationsPerWorkspaceBurst = Window(
                section,
                nameof(PublicChatProtectionOptions.GenerationsPerWorkspaceBurst),
                defaults.GenerationsPerWorkspaceBurst),
            GenerationsPerWorkspaceSustained = Window(
                section,
                nameof(PublicChatProtectionOptions.GenerationsPerWorkspaceSustained),
                defaults.GenerationsPerWorkspaceSustained),
            GenerationsPerWorkspaceDaily = Window(
                section,
                nameof(PublicChatProtectionOptions.GenerationsPerWorkspaceDaily),
                defaults.GenerationsPerWorkspaceDaily),
            SessionLifetime = Seconds(
                section,
                nameof(PublicChatProtectionOptions.SessionLifetime),
                defaults.SessionLifetime),
            KeyPrefix = Text(
                section,
                nameof(PublicChatProtectionOptions.KeyPrefix))
                ?? defaults.KeyPrefix,
        };
    }

    /// <summary>
    /// One configured limit-and-window pair.
    /// </summary>
    /// <remarks>
    /// Shaped as <c>Limit</c> and <c>WindowSeconds</c> beneath the setting's own
    /// name, so overriding one dimension of one limit is a single environment
    /// variable — <c>PublicChatProtection__RequestsPerClient__Limit</c> — and the
    /// two halves cannot be configured for different limits.
    /// </remarks>
    private static RateLimitWindow Window(
        IConfigurationSection section,
        string name,
        RateLimitWindow fallback)
    {
        IConfigurationSection own = section.GetSection(name);

        return new RateLimitWindow(
            Number(own, "Limit", fallback.Limit, $"{name}:Limit"),
            TimeSpan.FromSeconds(Number(
                own,
                "WindowSeconds",
                (int)Math.Ceiling(fallback.Window.TotalSeconds),
                $"{name}:WindowSeconds")));
    }

    /// <summary>A configured duration in whole seconds.</summary>
    private static TimeSpan Seconds(
        IConfigurationSection section,
        string name,
        TimeSpan fallback) =>
        TimeSpan.FromSeconds(Number(
            section,
            name + "Seconds",
            (int)Math.Ceiling(fallback.TotalSeconds),
            name + "Seconds"));

    /// <summary>
    /// A configured whole number, or the supplied default.
    /// </summary>
    /// <param name="section">The section the value sits directly in.</param>
    /// <param name="key">The key within that section.</param>
    /// <param name="fallback">Used only when the value is absent or blank.</param>
    /// <param name="path">
    /// The setting's path beneath the protection section, for the message an
    /// operator reads. Passed explicitly because the section a value is read from
    /// is not always the section it is named after.
    /// </param>
    private static int Number(
        IConfigurationSection section,
        string key,
        int fallback,
        string path)
    {
        string? value = Text(section, key);

        if (value is null)
        {
            return fallback;
        }

        return int.TryParse(value, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : throw new InvalidOperationException(
                $"{PublicChatProtectionOptions.SectionName}:{path} must be a whole number.");
    }

    /// <summary>A configured string, or null when absent or blank.</summary>
    private static string? Text(IConfigurationSection section, string key)
    {
        string? value = section[key];

        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
