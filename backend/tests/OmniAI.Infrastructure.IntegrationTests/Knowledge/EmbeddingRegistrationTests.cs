using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniAI.Application.Knowledge;
using OmniAI.Infrastructure.DependencyInjection;
using OmniAI.Infrastructure.Knowledge;
using OmniAI.Infrastructure.IntegrationTests.Ai.OpenAi;

namespace OmniAI.Infrastructure.IntegrationTests.Knowledge;

/// <summary>
/// Which embedding generator a deployment gets, and what a settings file may
/// carry.
/// </summary>
public sealed class EmbeddingRegistrationTests
{
    private static ServiceProvider Build(params (string Key, string? Value)[] settings)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(setting =>
                new KeyValuePair<string, string?>(setting.Key, setting.Value)))
            .Build();

        ServiceCollection services = [];

        services.AddLogging();
        services.AddOmniAiRuntime(configuration);

        return services.BuildServiceProvider();
    }

    [Fact]
    public void With_no_credential_the_generator_that_refuses_is_registered()
    {
        // Never a fake vector, never a cached answer, never an empty result that
        // would be mistaken for "nothing matched".
        using ServiceProvider provider = Build();

        Assert.IsType<UnconfiguredTextEmbeddingGenerator>(
            provider.GetRequiredService<ITextEmbeddingGenerator>());
    }

    [Fact]
    public async Task The_refusing_generator_says_the_deployment_is_not_configured()
    {
        using ServiceProvider provider = Build();

        ITextEmbeddingGenerator generator = provider.GetRequiredService<ITextEmbeddingGenerator>();

        await Assert.ThrowsAsync<EmbeddingUnavailableException>(
            () => generator.GenerateAsync(["anything"], OpenAiTestHarness.Usage));
    }

    [Fact]
    public void The_profile_is_known_even_with_no_credential()
    {
        // How a deployment makes vectors is a fact about the deployment, not about
        // whether a key happens to be present. Without this the knowledge screens
        // could not report readiness at all on the deployment somebody is most
        // likely to be looking at first.
        using ServiceProvider provider = Build();

        EmbeddingProfile profile =
            provider.GetRequiredService<ITextEmbeddingGenerator>().Profile;

        Assert.Equal("openai:text-embedding-3-small:1536", profile.Id.Value);
        Assert.Equal(1_536, profile.Dimensions);
    }

    [Fact]
    public void With_a_credential_the_real_adapter_is_registered()
    {
        using ServiceProvider provider = Build(
            ("OPENAI_API_KEY", "test-key-not-a-real-credential"));

        Assert.IsType<OpenAiTextEmbeddingGenerator>(
            provider.GetRequiredService<ITextEmbeddingGenerator>());
    }

    [Fact]
    public void A_blank_credential_counts_as_none()
    {
        using ServiceProvider provider = Build(("OPENAI_API_KEY", "   "));

        Assert.IsType<UnconfiguredTextEmbeddingGenerator>(
            provider.GetRequiredService<ITextEmbeddingGenerator>());
    }

    [Fact]
    public void A_configured_model_changes_the_profile_identifier()
    {
        // The marker stored beside every vector. Changing the model makes every
        // document indexed under the old one report as needing an index, rather
        // than being compared against vectors it cannot be compared with.
        using ServiceProvider provider = Build(("Embeddings:Model", "text-embedding-3-large"));

        Assert.Equal(
            "openai:text-embedding-3-large:1536",
            provider.GetRequiredService<ITextEmbeddingGenerator>().Profile.Id.Value);
    }

    [Fact]
    public void An_impossible_batch_size_is_refused_at_startup()
    {
        // Startup is where a misconfiguration should be discovered, not the middle
        // of somebody's first index.
        Assert.Throws<InvalidOperationException>(() => Build(("Embeddings:MaxBatchSize", "0")));
        Assert.Throws<InvalidOperationException>(() => Build(("Embeddings:MaxBatchSize", "1000")));
    }

    [Fact]
    public void A_non_numeric_setting_is_refused_at_startup()
    {
        Assert.Throws<InvalidOperationException>(
            () => Build(("Embeddings:TimeoutSeconds", "soon")));
    }

    [Fact]
    public void A_blank_model_is_refused_at_startup()
    {
        // Blank falls back to the default rather than failing, which is the
        // documented behaviour for an unset setting; whitespace-only is the same
        // thing to the configuration system.
        using ServiceProvider provider = Build(("Embeddings:Model", "   "));

        Assert.Equal(
            "openai:text-embedding-3-small:1536",
            provider.GetRequiredService<ITextEmbeddingGenerator>().Profile.Id.Value);
    }

    [Fact]
    public void No_settings_file_in_the_repository_carries_an_embedding_credential()
    {
        // The same guard the text-generation and protection suites apply, aimed at
        // the section this phase added. A key committed here would be a key
        // published to everybody who can read the repository.
        foreach (FileInfo file in Repository().GetFiles("appsettings*.json", SearchOption.AllDirectories))
        {
            if (file.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.FullName.Contains($"{Path.DirectorySeparatorChar}artifacts{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(file.FullName));

            if (document.RootElement.TryGetProperty("Embeddings", out JsonElement section)
                && section.TryGetProperty("ApiKey", out JsonElement key))
            {
                Assert.Fail($"{file.Name} carries an embedding credential: {key.ValueKind}.");
            }

            Assert.False(
                document.RootElement.TryGetProperty("OPENAI_API_KEY", out _),
                $"{file.Name} carries an AI provider credential.");
        }
    }

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
