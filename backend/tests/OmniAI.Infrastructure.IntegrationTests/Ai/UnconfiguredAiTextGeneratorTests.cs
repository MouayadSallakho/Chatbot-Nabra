using Microsoft.Extensions.DependencyInjection;
using OmniAI.Application.Ai;
using OmniAI.Infrastructure.DependencyInjection;

namespace OmniAI.Infrastructure.IntegrationTests.Ai;

/// <summary>
/// The AI text generator a deployment with no provider configured actually gets.
/// </summary>
/// <remarks>
/// Tested directly, because this is production behaviour rather than a stand-in:
/// it is what every deployment resolves today, and the properties asserted here
/// are the ones that stop an unconfigured platform from quietly inventing
/// answers.
/// </remarks>
public sealed class UnconfiguredAiTextGeneratorTests
{
    private static readonly AiTextGenerationRequest Request = AiTextGenerationRequest.Create(
        "Help customers choose suitable products.",
        [AiTurn.FromUser("Which laptop suits a student?")]);

    [Fact]
    public async Task It_refuses_every_request()
    {
        IAiTextGenerator generator = Resolve();

        await Assert.ThrowsAsync<AiRuntimeUnavailableException>(
            () => generator.GenerateAsync(Request));
    }

    [Fact]
    public async Task It_never_returns_a_result()
    {
        // Not a canned reply, not a template, not an echo of the caller's own
        // message. Anything returned successfully here would look exactly like a
        // real agent's answer to somebody evaluating whether their agent works.
        IAiTextGenerator generator = Resolve();

        AiRuntimeUnavailableException failure =
            await Assert.ThrowsAsync<AiRuntimeUnavailableException>(
                () => generator.GenerateAsync(Request));

        Assert.DoesNotContain(
            "Which laptop", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Its_failure_names_no_provider_and_no_configuration_key()
    {
        IAiTextGenerator generator = Resolve();

        AiRuntimeUnavailableException failure =
            await Assert.ThrowsAsync<AiRuntimeUnavailableException>(
                () => generator.GenerateAsync(Request));

        foreach (string forbidden in (string[])
            ["openai", "anthropic", "azure", "api key", "apikey", "OPENAI_API_KEY",
             "appsettings", "environment variable", "endpoint"])
        {
            Assert.DoesNotContain(forbidden, failure.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void The_registration_resolves_it_as_the_ai_text_generator()
    {
        // The composition root registers this and nothing else. A fake that
        // returned text would be indistinguishable from a working provider to
        // every layer above, which is exactly why none is registered.
        using ServiceProvider provider = new ServiceCollection()
            .AddOmniAiRuntime()
            .BuildServiceProvider();

        IAiTextGenerator generator = provider.GetRequiredService<IAiTextGenerator>();

        Assert.Equal("UnconfiguredAiTextGenerator", generator.GetType().Name);
    }

    [Fact]
    public void The_registration_is_a_singleton()
    {
        using ServiceProvider provider = new ServiceCollection()
            .AddOmniAiRuntime()
            .BuildServiceProvider();

        Assert.Same(
            provider.GetRequiredService<IAiTextGenerator>(),
            provider.GetRequiredService<IAiTextGenerator>());
    }

    [Fact]
    public void The_ai_runtime_registration_needs_no_database()
    {
        // Registered separately from persistence, because a deployment can have a
        // database and no AI provider -- which is precisely the state this is.
        // Building it without any connection string must therefore work.
        using ServiceProvider provider = new ServiceCollection()
            .AddOmniAiRuntime()
            .BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IAiTextGenerator>());
    }

    [Fact]
    public void The_infrastructure_assembly_references_no_ai_provider_sdk()
    {
        // Phase 8 implements no provider anywhere, including here. When one
        // arrives it will be a deliberate, reviewed addition rather than something
        // that appeared alongside the abstraction.
        string[] forbidden =
        [
            "OpenAI",
            "Azure.AI.OpenAI",
            "Anthropic",
            "Anthropic.SDK",
            "Betalgo.OpenAI",
            "OpenAI_API",
            "Microsoft.Extensions.AI",
            "Microsoft.SemanticKernel",
            "LangChain",
        ];

        string[] offenders =
        [
            .. Testing.AssemblyReferenceReader.ReferencedAssemblyNames("OmniAI.Infrastructure.dll")
                .Where(name => forbidden.Contains(name, StringComparer.OrdinalIgnoreCase))
        ];

        Assert.Empty(offenders);
    }

    private static IAiTextGenerator Resolve()
    {
        ServiceProvider provider = new ServiceCollection()
            .AddOmniAiRuntime()
            .BuildServiceProvider();

        return provider.GetRequiredService<IAiTextGenerator>();
    }
}
