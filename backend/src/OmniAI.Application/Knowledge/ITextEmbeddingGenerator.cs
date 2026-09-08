namespace OmniAI.Application.Knowledge;

/// <summary>
/// Turns text into vectors.
/// </summary>
/// <remarks>
/// <para>
/// The whole provider boundary for embeddings, and deliberately as small as
/// <see cref="Ai.IAiTextGenerator"/>: a profile describing what this deployment
/// is set up for, and one method that answers a bounded batch. No credential, no
/// endpoint, no model name, no SDK type and no retry policy appears in it, so
/// changing provider is a change to one adapter rather than to everything that
/// indexes or searches.
/// </para>
/// <para>
/// <strong>Batching is the caller's job, and the contract says so.</strong> An
/// implementation that quietly split an oversized batch into several requests
/// would multiply a caller's costs invisibly. Given more inputs than the profile
/// allows, an implementation refuses.
/// </para>
/// <para>
/// <strong>Nothing here retries.</strong> A failed embedding call is reported as
/// what it was, once. Retrying a paid call on the caller's behalf is a policy
/// decision nobody has taken, and a hidden one turns a single index into several
/// bills.
/// </para>
/// <para>
/// The returned list has exactly one vector per input, in the same order.
/// </para>
/// </remarks>
public interface ITextEmbeddingGenerator
{
    /// <summary>How this deployment is configured to produce vectors.</summary>
    /// <remarks>
    /// Readable whether or not a provider is actually reachable, so readiness can
    /// be reported honestly on a deployment with no credential.
    /// </remarks>
    EmbeddingProfile Profile { get; }

    /// <summary>Embeds one bounded batch of inputs.</summary>
    /// <exception cref="EmbeddingUnavailableException">
    /// No provider is configured, or it refused to serve the request.
    /// </exception>
    /// <exception cref="EmbeddingTimeoutException">The provider did not answer in time.</exception>
    /// <exception cref="EmbeddingResponseInvalidException">
    /// The provider answered with something unusable.
    /// </exception>
    Task<IReadOnlyList<EmbeddingVector>> GenerateAsync(
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken = default);
}
