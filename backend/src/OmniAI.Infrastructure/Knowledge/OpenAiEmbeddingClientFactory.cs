using System.ClientModel;
using System.ClientModel.Primitives;
using OpenAI;
using OpenAI.Embeddings;

namespace OmniAI.Infrastructure.Knowledge;

/// <summary>Builds the embeddings client.</summary>
/// <remarks>
/// A twin of the responses client factory, and deliberately so: the same zero
/// retries, the same finite network timeout, the same treatment of the
/// credential. Two clients built to different rules would mean one of the two
/// capabilities behaved in a way nobody had decided on.
/// </remarks>
internal static class OpenAiEmbeddingClientFactory
{
    public static EmbeddingClient Create(
        string apiKey,
        EmbeddingOptions options,
        PipelineTransport? transport = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentNullException.ThrowIfNull(options);

        OpenAIClientOptions clientOptions = new()
        {
            // ZERO automatic retries. The SDK retries 408, 429 and 5xx by default.
            // That default is reasonable in general and wrong here: no retry
            // policy has been designed, and a hidden one would mean a single index
            // quietly becoming several billed calls. Zero disables retrying, not
            // the request.
            RetryPolicy = new ClientRetryPolicy(maxRetries: 0),

            // A finite wait. An unbounded one is how a slow dependency becomes an
            // outage. The caller's own cancellation token still propagates and
            // still wins.
            NetworkTimeout = options.Timeout,
        };

        if (transport is not null)
        {
            clientOptions.Transport = transport;
        }

        // The credential is passed straight in and is never written to a field of
        // ours, a log, an exception or an options object.
        return new EmbeddingClient(options.Model, new ApiKeyCredential(apiKey), clientOptions);
    }
}
