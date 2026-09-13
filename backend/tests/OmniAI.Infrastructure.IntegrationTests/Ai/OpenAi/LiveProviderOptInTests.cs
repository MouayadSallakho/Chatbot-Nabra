using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OmniAI.Application.Ai;
using OmniAI.Application.Knowledge;
using OmniAI.Infrastructure.Ai;
using OmniAI.Infrastructure.Ai.OpenAi;
using OmniAI.Infrastructure.DependencyInjection;
using OmniAI.Infrastructure.Knowledge;

namespace OmniAI.Infrastructure.IntegrationTests.Ai.OpenAi;

/// <summary>
/// The Development guard against a key nobody meant to use.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Written because it happened.</strong> During Phase 18's browser
/// verification the API was started from a shell that already had
/// <c>OPENAI_API_KEY</c> in its environment for an unrelated project. The process
/// inherited it, the run that was meant to exercise the provider-unconfigured
/// path made a live request instead, and the only symptom was that it worked.
/// </para>
/// <para>
/// <strong>Both providers are asserted in every case.</strong> The text generator
/// and the embedding generator spend money independently; a guard that covered
/// one would leave the other able to make the same accidental call.
/// </para>
/// <para>
/// No process environment variable is set or read here, and no credential in this
/// file is real. Nothing in this class makes a network request.
/// </para>
/// </remarks>
public sealed class LiveProviderOptInTests
{
    private const string PlaceholderKey = "test-key-not-a-real-credential";

    /// <summary>Builds the runtime from in-memory settings alone.</summary>
    private static ServiceProvider Build(
        bool development,
        string? apiKey = null,
        string? optIn = null)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>(OpenAiOptions.ApiKeyVariable, apiKey),
                new KeyValuePair<string, string?>(OpenAiOptions.LiveProviderOptInVariable, optIn),
            ])
            .Build();

        ServiceCollection services = [];

        services.AddLogging();

        // Exactly what the composition root passes: Development, and nothing else.
        services.AddOmniAiRuntime(configuration, requireLiveProviderOptIn: development);

        return services.BuildServiceProvider();
    }

    /// <summary>Asserts that neither provider can reach OpenAI.</summary>
    private static void AssertUnconfigured(ServiceProvider provider)
    {
        Assert.IsType<UnconfiguredAiTextGenerator>(
            provider.GetRequiredService<IAiTextGenerator>());

        Assert.IsType<UnconfiguredTextEmbeddingGenerator>(
            provider.GetRequiredService<ITextEmbeddingGenerator>());
    }

    [Fact]
    public void Development_with_no_key_composes_neither_provider()
    {
        using ServiceProvider provider = Build(development: true);

        AssertUnconfigured(provider);
    }

    [Fact]
    public void Development_with_a_key_and_no_opt_in_still_composes_neither()
    {
        // The case that cost money. A key in the environment is not a decision
        // somebody made about this process.
        using ServiceProvider provider = Build(development: true, apiKey: PlaceholderKey);

        AssertUnconfigured(provider);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("true")]
    [InlineData("yes")]
    [InlineData("on")]
    [InlineData("1 ")]
    [InlineData("01")]
    public void Development_with_a_key_and_anything_but_one_still_composes_neither(string optIn)
    {
        // The guard's vocabulary is one value. "true" and "yes" are refused
        // deliberately: a guard that accepts whatever looks affirmative is a guard
        // somebody trips over by accident, which is the failure it exists for.
        using ServiceProvider provider = Build(
            development: true,
            apiKey: PlaceholderKey,
            optIn: optIn);

        AssertUnconfigured(provider);
    }

    [Fact]
    public void Development_with_a_key_and_the_opt_in_may_compose_the_live_adapters()
    {
        // Deliberate, and only then. Composed here; nothing in this test calls
        // either adapter, so no request is made.
        using ServiceProvider provider = Build(
            development: true,
            apiKey: PlaceholderKey,
            optIn: OpenAiOptions.LiveProviderOptInValue);

        Assert.IsType<OpenAiTextGenerator>(provider.GetRequiredService<IAiTextGenerator>());

        Assert.IsType<OpenAiTextEmbeddingGenerator>(
            provider.GetRequiredService<ITextEmbeddingGenerator>());
    }

    [Fact]
    public void The_opt_in_without_a_key_composes_nothing_live()
    {
        // The flag is permission, not a credential.
        using ServiceProvider provider = Build(
            development: true,
            optIn: OpenAiOptions.LiveProviderOptInValue);

        AssertUnconfigured(provider);
    }

    [Fact]
    public void Production_with_a_configured_key_needs_no_development_opt_in()
    {
        // A deployment that has deliberately configured a provider must not be
        // taken offline by somebody forgetting a second flag. This is a cost guard
        // for developer machines, not a security control for deployments.
        using ServiceProvider provider = Build(development: false, apiKey: PlaceholderKey);

        Assert.IsType<OpenAiTextGenerator>(provider.GetRequiredService<IAiTextGenerator>());

        Assert.IsType<OpenAiTextEmbeddingGenerator>(
            provider.GetRequiredService<ITextEmbeddingGenerator>());
    }

    [Fact]
    public void Production_ignores_the_development_opt_in_entirely()
    {
        // Present and refusing, or absent: outside Development the variable is not
        // consulted, so neither spelling changes what is composed.
        foreach (string? optIn in new string?[] { null, "0", "false", OpenAiOptions.LiveProviderOptInValue })
        {
            using ServiceProvider provider = Build(
                development: false,
                apiKey: PlaceholderKey,
                optIn: optIn);

            Assert.IsType<OpenAiTextGenerator>(provider.GetRequiredService<IAiTextGenerator>());
        }
    }

    [Fact]
    public void Production_with_no_key_still_refuses_explicitly()
    {
        // The guard changed nothing about the accepted behaviour on a deployment
        // that has no provider.
        using ServiceProvider provider = Build(development: false);

        AssertUnconfigured(provider);
    }

    [Fact]
    public void The_guarded_runtime_still_starts_and_still_reports_its_embedding_profile()
    {
        // Withholding the providers must not withhold the deployment's own facts:
        // the knowledge screens report readiness from the profile, and a developer
        // with no opt-in is exactly the person most likely to be looking at them.
        using ServiceProvider provider = Build(development: true, apiKey: PlaceholderKey);

        Assert.NotNull(provider.GetRequiredService<EmbeddingProfile>());
    }
}
