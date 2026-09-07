using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OmniAI.Application.WebChat.Protection;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.WebChat;
using OmniAI.Infrastructure.WebChat.Protection;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.WebChat;

/// <summary>
/// What a deployment gets for each way it can configure — or fail to configure —
/// public chat protection.
/// </summary>
/// <remarks>
/// <strong>Every branch here is a real deployment state, and each has to fail in
/// the right direction.</strong> A missing setting must never mean "no limits": it
/// must mean the public writes refuse, the admin keeps working, and the log names
/// what is absent. A mistyped setting must be a startup failure rather than a
/// limit somebody's customer discovers.
/// </remarks>
public sealed class PublicChatProtectionSetupTests(RedisTestServer redis)
    : IClassFixture<RedisTestServer>
{
    private static readonly string Secret =
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    // ---- A fully configured deployment ------------------------------------

    [Fact]
    public async Task A_configured_deployment_gets_the_redis_limiter()
    {
        await using ServiceProvider services = Build(new Dictionary<string, string?>
        {
            [$"ConnectionStrings:{PublicChatProtectionSetup.RedisConnectionName}"] =
                redis.ConnectionString,
            [PublicChatProtectionSetup.NetworkKeySecretVariable] = Secret,
            ["PublicChatProtection:KeyPrefix"] = redis.KeyPrefix,
        });

        IPublicChatProtection protection =
            services.GetRequiredService<IPublicChatProtection>();

        // Named rather than merely non-null: the property that matters is that a
        // configured deployment is not silently running the refusing stand-in.
        Assert.Equal("RedisPublicChatProtection", protection.GetType().Name);

        Assert.True(
            (await protection.AuthoriseRequestAsync(ClientNetworkKey.From("setup"))).Allowed);

        await redis.ClearAsync();
    }

    [Fact]
    public void A_configured_deployment_gets_a_working_key_source()
    {
        using ServiceProvider services = Build(new Dictionary<string, string?>
        {
            [PublicChatProtectionSetup.NetworkKeySecretVariable] = Secret,
        });

        ClientNetworkKey key = services.GetRequiredService<IClientNetworkKeys>()
            .Derive(IPAddress.Parse("203.0.113.7"));

        Assert.False(key.IsEmpty);
    }

    // ---- Missing configuration -------------------------------------------

    [Fact]
    public async Task With_no_protection_store_configured_every_public_write_is_refused()
    {
        // The application still starts, and the capability refuses explicitly --
        // the same shape this platform already uses for a missing database
        // connection string and a missing AI provider key. Allowing every request
        // would mean one forgotten setting exposed an unmetered, paid, anonymous
        // surface to the internet.
        await using ServiceProvider services = Build(new Dictionary<string, string?>
        {
            [PublicChatProtectionSetup.NetworkKeySecretVariable] = Secret,
        });

        IPublicChatProtection protection =
            services.GetRequiredService<IPublicChatProtection>();

        Assert.Equal("UnprotectedPublicChat", protection.GetType().Name);

        PublicChatProtectionUnavailableException failure =
            await Assert.ThrowsAsync<PublicChatProtectionUnavailableException>(
                () => protection.AuthoriseRequestAsync(ClientNetworkKey.From("nothing")));

        // The operator is told the setting to add.
        Assert.Contains(
            PublicChatProtectionSetup.RedisConnectionName,
            failure.Reason,
            StringComparison.Ordinal);

        await Assert.ThrowsAsync<PublicChatProtectionUnavailableException>(
            () => protection.AuthoriseConversationStartAsync(
                ClientNetworkKey.From("nothing"),
                WebChatEndpointId.New()));

        await Assert.ThrowsAsync<PublicChatProtectionUnavailableException>(
            () => protection.AuthoriseGenerationAsync(TenantId.New(), ConversationId.New()));
    }

    [Fact]
    public void With_no_secret_and_no_ephemeral_allowance_the_key_source_refuses()
    {
        // Outside Development. Fail closed, at the point of use, naming the
        // variable -- rather than taking a whole deployment's administration down
        // over a setting only the public chat needs.
        using ServiceProvider services = Build(
            new Dictionary<string, string?>(),
            allowEphemeral: false);

        PublicChatProtectionUnavailableException failure =
            Assert.Throws<PublicChatProtectionUnavailableException>(() =>
                services.GetRequiredService<IClientNetworkKeys>()
                    .Derive(IPAddress.Parse("203.0.113.7")));

        Assert.Contains(
            PublicChatProtectionSetup.NetworkKeySecretVariable,
            failure.Reason,
            StringComparison.Ordinal);
    }

    [Fact]
    public void With_no_secret_and_an_ephemeral_allowance_keys_still_derive()
    {
        // Development only, and not a protection bypass: the limits are enforced in
        // full and the only difference is that the buckets are renamed on restart.
        using ServiceProvider services = Build(
            new Dictionary<string, string?>(),
            allowEphemeral: true);

        Assert.False(services.GetRequiredService<IClientNetworkKeys>()
            .Derive(IPAddress.Parse("203.0.113.7"))
            .IsEmpty);
    }

    [Fact]
    public void An_ephemeral_secret_is_not_a_fixed_secret_in_disguise()
    {
        // Nothing is compiled in. Two processes with no configured secret must not
        // agree on a key, or the "ephemeral" secret would be a committed one.
        using ServiceProvider first = Build(
            new Dictionary<string, string?>(),
            allowEphemeral: true);

        using ServiceProvider second = Build(
            new Dictionary<string, string?>(),
            allowEphemeral: true);

        IPAddress address = IPAddress.Parse("203.0.113.7");

        Assert.NotEqual(
            first.GetRequiredService<IClientNetworkKeys>().Derive(address),
            second.GetRequiredService<IClientNetworkKeys>().Derive(address));
    }

    // ---- Bad configuration is a startup failure --------------------------

    [Fact]
    public void A_secret_that_is_too_short_is_refused_at_startup()
    {
        // A guessable HMAC key means the pseudonym can be reversed by anybody who
        // obtains the counter names, which is the entire property it provides.
        // Refused rather than accepted with a warning: a warning in a log nobody
        // reads is how a two-character key reaches production.
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
            Build(new Dictionary<string, string?>
            {
                [PublicChatProtectionSetup.NetworkKeySecretVariable] = "too-short",
            }));

        Assert.Contains(
            PublicChatProtectionSetup.NetworkKeySecretVariable,
            failure.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_mistyped_limit_is_refused_at_startup()
    {
        // Not silently ignored. A deployment that meant to cap generation at thirty
        // a minute and mistyped it must not run at whatever this code prefers.
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
            Build(new Dictionary<string, string?>
            {
                [PublicChatProtectionSetup.NetworkKeySecretVariable] = Secret,
                ["PublicChatProtection:RequestsPerClient:Limit"] = "thirty",
            }));

        Assert.Contains("whole number", failure.Message, StringComparison.Ordinal);
        Assert.Contains("RequestsPerClient", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_impossible_policy_is_refused_at_startup()
    {
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
            Build(new Dictionary<string, string?>
            {
                [PublicChatProtectionSetup.NetworkKeySecretVariable] = Secret,
                ["PublicChatProtection:RequestsPerClient:Limit"] = "0",
            }));

        Assert.Contains("at least one", failure.Message, StringComparison.Ordinal);
    }

    // ---- Configuration is actually read ----------------------------------

    [Fact]
    public void Every_limit_can_be_overridden_by_ordinary_configuration()
    {
        // The whole policy has to be tunable per deployment without a release, and
        // through the flat double-underscore form an environment variable uses.
        using ServiceProvider services = Build(new Dictionary<string, string?>
        {
            [PublicChatProtectionSetup.NetworkKeySecretVariable] = Secret,
            ["PublicChatProtection:RequestsPerClient:Limit"] = "7",
            ["PublicChatProtection:RequestsPerClient:WindowSeconds"] = "11",
            ["PublicChatProtection:ConversationStartsPerClient:Limit"] = "3",
            ["PublicChatProtection:ConversationStartsPerEndpoint:Limit"] = "9",
            ["PublicChatProtection:MessagesPerConversation:Limit"] = "4",
            ["PublicChatProtection:GenerationsPerWorkspaceBurst:Limit"] = "5",
            ["PublicChatProtection:GenerationsPerWorkspaceSustained:Limit"] = "50",
            ["PublicChatProtection:GenerationsPerWorkspaceDaily:Limit"] = "500",
            ["PublicChatProtection:SessionLifetimeSeconds"] = "3600",
            ["PublicChatProtection:KeyPrefix"] = "custom:prefix",
        });

        PublicChatProtectionOptions limits =
            services.GetRequiredService<PublicChatProtectionOptions>();

        Assert.Equal(new RateLimitWindow(7, TimeSpan.FromSeconds(11)), limits.RequestsPerClient);
        Assert.Equal(3, limits.ConversationStartsPerClient.Limit);
        Assert.Equal(9, limits.ConversationStartsPerEndpoint.Limit);
        Assert.Equal(4, limits.MessagesPerConversation.Limit);
        Assert.Equal(5, limits.GenerationsPerWorkspaceBurst.Limit);
        Assert.Equal(50, limits.GenerationsPerWorkspaceSustained.Limit);
        Assert.Equal(500, limits.GenerationsPerWorkspaceDaily.Limit);
        Assert.Equal(TimeSpan.FromHours(1), limits.SessionLifetime);
        Assert.Equal("custom:prefix", limits.KeyPrefix);
    }

    [Fact]
    public void A_deployment_that_configures_nothing_gets_the_shipped_defaults()
    {
        using ServiceProvider services = Build(new Dictionary<string, string?>
        {
            [PublicChatProtectionSetup.NetworkKeySecretVariable] = Secret,
        });

        PublicChatProtectionOptions limits =
            services.GetRequiredService<PublicChatProtectionOptions>();

        Assert.Equal(new PublicChatProtectionOptions().RequestsPerClient, limits.RequestsPerClient);
        Assert.Equal(TimeSpan.FromHours(24), limits.SessionLifetime);
    }

    // ---- The secret is not in the repository -----------------------------

    [Fact]
    public void No_settings_file_in_the_repository_carries_the_network_key_secret()
    {
        // The same guard the OpenAI key has, for the same reason: this is the one
        // value in the protection configuration that must never be committed, and
        // keeping it out of the policy section is only half of making that true.
        DirectoryInfo root = Repository();

        string[] offenders =
        [
            .. Directory.EnumerateFiles(root.FullName, "appsettings*.json", SearchOption.AllDirectories)
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Where(path => File.ReadAllText(path).Contains(
                    PublicChatProtectionSetup.NetworkKeySecretVariable,
                    StringComparison.Ordinal)),
        ];

        Assert.Empty(offenders);
    }

    [Fact]
    public void The_committed_redis_connection_string_is_blank()
    {
        // A real endpoint in source control is a committed deployment fact, and a
        // plausible-looking default is worse: a misconfigured deployment would
        // quietly count somewhere else.
        string settings = Path.Combine(
            Repository().FullName,
            "backend",
            "src",
            "OmniAI.Api",
            "appsettings.json");

        Assert.True(File.Exists(settings), $"{settings} was not found.");

        // Read as JSON rather than through the configuration builder, so this test
        // needs no JSON configuration provider package of its own -- and so it
        // asserts on the committed file itself rather than on a merged view of it.
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(settings));

        string? committed = document.RootElement
            .GetProperty("ConnectionStrings")
            .GetProperty(PublicChatProtectionSetup.RedisConnectionName)
            .GetString();

        Assert.Equal(string.Empty, committed);
    }

    // ---- Arrangement -----------------------------------------------------

    private static ServiceProvider Build(
        Dictionary<string, string?> settings,
        bool allowEphemeral = false)
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        ServiceCollection services = new();

        services.AddLogging();
        services.AddOmniAiPublicChatProtection(configuration, allowEphemeral);

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// The repository root, found by walking up from the test assembly.
    /// </summary>
    private static DirectoryInfo Repository()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".git")))
        {
            directory = directory.Parent;
        }

        return directory
            ?? throw new InvalidOperationException("The repository root could not be located.");
    }
}
