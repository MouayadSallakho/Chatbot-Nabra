using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OmniAI.Application.Ai;
using OmniAI.Infrastructure.Ai.OpenAi;
using OmniAI.Infrastructure.DependencyInjection;

namespace OmniAI.Infrastructure.IntegrationTests.Ai.OpenAi;

/// <summary>
/// Which AI runtime a deployment gets, and why.
/// </summary>
/// <remarks>
/// <para>
/// The key's presence is the whole decision, so both branches are proven here
/// from in-memory configuration. No process environment variable is set or read
/// by these tests: doing that would be global mutable state shared with every
/// other test running in parallel, and it would make the result depend on whether
/// the developer running them happens to have a key configured.
/// </para>
/// <para>
/// Every credential used here is an obvious placeholder. No real key is read.
/// </para>
/// </remarks>
public sealed class OpenAiRegistrationTests
{
    private const string PlaceholderKey = "test-key-not-a-real-credential";

    [Fact]
    public void With_no_key_the_runtime_refuses_explicitly()
    {
        // The accepted Phase 8 behaviour, unchanged.
        Assert.Equal("UnconfiguredAiTextGenerator", ResolveGeneratorName(Configure(apiKey: null)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_key_counts_as_no_key(string apiKey)
    {
        // A key set to an empty string is a misconfiguration, not a credential.
        // Treating it as configured would produce 401s from the provider instead
        // of the clear "not configured on this deployment" answer.
        Assert.Equal("UnconfiguredAiTextGenerator", ResolveGeneratorName(Configure(apiKey)));
    }

    [Fact]
    public void With_a_key_the_openai_adapter_is_registered()
    {
        Assert.Equal("OpenAiTextGenerator", ResolveGeneratorName(Configure(PlaceholderKey)));
    }

    [Fact]
    public void The_application_still_starts_with_no_key()
    {
        // A developer who has not configured OpenAI must not be met with an
        // application that refuses to run.
        using ServiceProvider provider = Build(Configure(apiKey: null));

        Assert.NotNull(provider.GetRequiredService<IAiTextGenerator>());
    }

    [Fact]
    public void The_openai_generator_is_a_singleton()
    {
        // The client owns an HTTP stack. Building one per request would exhaust
        // sockets under any real load.
        using ServiceProvider provider = Build(Configure(PlaceholderKey));

        Assert.Same(
            provider.GetRequiredService<IAiTextGenerator>(),
            provider.GetRequiredService<IAiTextGenerator>());
    }

    [Fact]
    public void The_model_defaults_when_nothing_configures_it()
    {
        using ServiceProvider provider = Build(Configure(PlaceholderKey));

        Assert.Equal(OpenAiOptions.DefaultModel, provider.GetRequiredService<OpenAiOptions>().Model);
    }

    [Fact]
    public void Configuration_overrides_the_model_and_the_bounds()
    {
        using ServiceProvider provider = Build(Configure(
            PlaceholderKey,
            ("OpenAI:Model", "gpt-configured"),
            ("OpenAI:MaxOutputTokens", "222"),
            ("OpenAI:TimeoutSeconds", "11")));

        OpenAiOptions options = provider.GetRequiredService<OpenAiOptions>();

        Assert.Equal("gpt-configured", options.Model);
        Assert.Equal(222, options.MaxOutputTokens);
        Assert.Equal(TimeSpan.FromSeconds(11), options.Timeout);
    }

    [Fact]
    public void The_documented_defaults_are_what_the_options_hold()
    {
        OpenAiOptions options = new();

        Assert.Equal(OpenAiOptions.DefaultModel, options.Model);
        Assert.Equal(800, options.MaxOutputTokens);
        Assert.Equal(TimeSpan.FromSeconds(30), options.Timeout);
    }

    [Theory]
    [InlineData("OpenAI:MaxOutputTokens", "not-a-number")]
    [InlineData("OpenAI:TimeoutSeconds", "soon")]
    public void An_unparseable_setting_fails_at_startup(string key, string value)
    {
        // Not silently replaced with a default. A deployment that meant to cap
        // spending at 200 tokens, and mistyped it, must not quietly run at
        // whatever this code prefers.
        Assert.Throws<InvalidOperationException>(
            () => new ServiceCollection().AddOmniAiRuntime(Configure(PlaceholderKey, (key, value))));
    }

    [Theory]
    [InlineData("OpenAI:Model", " ")]
    [InlineData("OpenAI:MaxOutputTokens", "0")]
    [InlineData("OpenAI:MaxOutputTokens", "-1")]
    [InlineData("OpenAI:TimeoutSeconds", "0")]
    [InlineData("OpenAI:TimeoutSeconds", "-5")]
    public void An_unusable_setting_is_caught_at_startup_or_defaulted(string key, string value)
    {
        // A blank model falls back to the default -- there is nothing to
        // misinterpret. A non-positive bound is a mistake with real consequences
        // and fails loudly, where an operator sees it, rather than on the first
        // customer request.
        IConfiguration configuration = Configure(PlaceholderKey, (key, value));

        if (key.EndsWith("Model", StringComparison.Ordinal))
        {
            using ServiceProvider provider = Build(configuration);

            Assert.Equal(OpenAiOptions.DefaultModel, provider.GetRequiredService<OpenAiOptions>().Model);

            return;
        }

        Assert.Throws<InvalidOperationException>(
            () => new ServiceCollection().AddOmniAiRuntime(configuration));
    }

    [Fact]
    public void The_api_key_is_never_bound_into_the_options()
    {
        // The options object is the easy thing to log, serialise into diagnostics
        // or return from a configuration endpoint. The secret must not be in it.
        using ServiceProvider provider = Build(Configure(PlaceholderKey));

        OpenAiOptions options = provider.GetRequiredService<OpenAiOptions>();

        Assert.DoesNotContain(
            nameof(OpenAiOptions.ApiKeyVariable),
            options.GetType().GetProperties().Select(property => property.Name),
            StringComparer.Ordinal);

        foreach (string value in (string[])[options.Model])
        {
            Assert.DoesNotContain(PlaceholderKey, value, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void No_settings_file_in_the_repository_carries_an_api_key()
    {
        // The key must come from the environment and never from configuration
        // committed to the repository. This walks the real settings files rather
        // than trusting that nobody will add one.
        DirectoryInfo? root = new(AppContext.BaseDirectory);

        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, ".git")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);

        string[] settings =
        [
            .. Directory.EnumerateFiles(root.FullName, "appsettings*.json", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(root.FullName, "launchSettings.json", SearchOption.AllDirectories))
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        ];

        Assert.NotEmpty(settings);

        foreach (string path in settings)
        {
            string content = File.ReadAllText(path);

            Assert.DoesNotContain(OpenAiOptions.ApiKeyVariable, content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("sk-", content, StringComparison.Ordinal);
        }
    }

    // ------------------------------------------------------------------- helpers

    private static IConfiguration Configure(string? apiKey, params (string Key, string Value)[] settings)
    {
        Dictionary<string, string?> values = settings
            .ToDictionary(setting => setting.Key, setting => (string?)setting.Value, StringComparer.Ordinal);

        if (apiKey is not null)
        {
            values[OpenAiOptions.ApiKeyVariable] = apiKey;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    /// <summary>
    /// A provider with the AI runtime registered.
    /// </summary>
    /// <remarks>
    /// Logging is supplied explicitly because a bare <c>ServiceCollection</c> has
    /// none, whereas the hosted application always does. The adapter takes an
    /// <c>ILogger</c> so that provider failures are recorded for operators; that
    /// dependency is satisfied by the host in production and by this line here.
    /// </remarks>
    private static ServiceProvider Build(IConfiguration configuration) =>
        new ServiceCollection()
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddOmniAiRuntime(configuration)
            .BuildServiceProvider();

    private static string ResolveGeneratorName(IConfiguration configuration)
    {
        using ServiceProvider provider = Build(configuration);

        return provider.GetRequiredService<IAiTextGenerator>().GetType().Name;
    }
}
