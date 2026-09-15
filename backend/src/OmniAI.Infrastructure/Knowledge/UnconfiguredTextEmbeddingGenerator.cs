using OmniAI.Application.Ai.Usage;
using OmniAI.Application.Knowledge;

namespace OmniAI.Infrastructure.Knowledge;

/// <summary>
/// What is registered when no embedding provider is configured.
/// </summary>
/// <remarks>
/// <para>
/// <strong>It still knows the profile.</strong> That is the difference between
/// this and simply registering nothing: how a deployment is configured to make
/// vectors is a fact about the deployment, not about whether a credential happens
/// to be present. Because the profile is readable, the admin screens can be
/// honest -- every document correctly reports as needing an index, rather than
/// the whole feature failing to render.
/// </para>
/// <para>
/// <strong>It refuses rather than pretends.</strong> No fake vector, no cached
/// answer, no empty result that would be mistaken for "nothing matched". A
/// deployment with no provider produces a clear "not available on this
/// deployment", and a document never becomes ready without a real index behind
/// it.
/// </para>
/// </remarks>
internal sealed class UnconfiguredTextEmbeddingGenerator(EmbeddingProfile profile)
    : ITextEmbeddingGenerator
{
    public EmbeddingProfile Profile { get; } =
        profile ?? throw new ArgumentNullException(nameof(profile));

    /// <remarks>
    /// Refuses before dispatch and therefore writes no accounting row. See the
    /// unconfigured text generator.
    /// </remarks>
    public Task<IReadOnlyList<EmbeddingVector>> GenerateAsync(
        IReadOnlyList<string> inputs,
        AiUsageContext usage,
        CancellationToken cancellationToken = default) =>
        throw new EmbeddingUnavailableException();
}
