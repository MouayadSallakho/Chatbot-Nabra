using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OmniAI.Application.Ai;
using OmniAI.Testing;

namespace OmniAI.Api.IntegrationTests.AiAgents;

/// <summary>
/// The AI agent API with a deterministic text generator in place of the
/// unconfigured one.
/// </summary>
/// <remarks>
/// <para>
/// Everything the base fixture provides -- the real endpoints, the real handlers,
/// the real repositories, real EF Core and a real PostgreSQL database -- with
/// exactly one service replaced. That single substitution is what lets the whole
/// preview slice be proven over HTTP without a provider, an API key, an account
/// or a network call, and it is the reason a provider-independent contract was
/// worth having in the first place.
/// </para>
/// <para>
/// <strong>Replaced, not added.</strong>
/// <see cref="ServiceCollectionDescriptorExtensions.Replace"/> removes the
/// production registration rather than shadowing it, so there is no chance of the
/// real <c>UnconfiguredAiTextGenerator</c> being resolved instead and no
/// ambiguity about which one is in play.
/// </para>
/// <para>
/// <strong>The fake never leaves the test host.</strong> The composition root
/// registers <c>UnconfiguredAiTextGenerator</c> and nothing else; this
/// substitution exists here, in a test project, and no production code path can
/// reach it. <see cref="AiAgentUnconfiguredProviderApiTests"/> covers the
/// behaviour a real deployment gets, using the base fixture with nothing
/// replaced.
/// </para>
/// </remarks>
public sealed class AiAgentPreviewApiFixture : AiAgentApiFixture
{
    private readonly RecordingAiTextGenerator _generator = new();

    /// <summary>
    /// The generator the hosted application will use, for configuring replies and
    /// asserting on what it received.
    /// </summary>
    internal RecordingAiTextGenerator Generator => _generator;

    /// <summary>Returns the generator to its initial state.</summary>
    /// <remarks>
    /// Internal, like the generator itself: the fake is visible to this test
    /// assembly and to nothing else.
    /// <para>
    /// Called at the start of each test, because a class fixture is shared across
    /// the class and a call count left by one test would otherwise be read by the
    /// next.
    /// </para>
    /// </remarks>
    internal RecordingAiTextGenerator ResetGenerator()
    {
        _generator.Reset();

        return _generator;
    }

    protected override void ConfigureTestServices(IServiceCollection services) =>
        services.Replace(ServiceDescriptor.Singleton<IAiTextGenerator>(_generator));
}
