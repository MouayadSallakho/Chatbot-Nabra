using System.ClientModel;
using System.ClientModel.Primitives;
using OpenAI.Responses;

// See OpenAiTextGenerator for the full justification. ResponsesClient and
// ResponsesClientOptions are [Experimental("OPENAI001")] in the official SDK
// 2.13.0; the suppression is scoped to this file, which exists only to build
// that client.
#pragma warning disable OPENAI001

namespace OmniAI.Infrastructure.Ai.OpenAi;

/// <summary>
/// Builds the configured OpenAI Responses client.
/// </summary>
/// <remarks>
/// Separate from the adapter so that "how the client is configured" is one
/// readable place rather than something buried in a registration lambda. Two
/// settings here are load-bearing enough to be worth finding easily: retries are
/// switched off, and the network timeout is finite.
/// </remarks>
internal static class OpenAiClientFactory
{
    /// <summary>
    /// Creates a Responses client for the supplied credential and options.
    /// </summary>
    /// <param name="apiKey">The API key. Used, never stored, never logged.</param>
    /// <param name="options">Non-secret provider configuration.</param>
    /// <param name="transport">
    /// An optional transport. Production passes none and gets the SDK's own
    /// HTTP stack; tests pass one backed by a canned handler, which exercises the
    /// real SDK serialisation and deserialisation without a network.
    /// </param>
    public static ResponsesClient Create(
        string apiKey,
        OpenAiOptions options,
        PipelineTransport? transport = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentNullException.ThrowIfNull(options);

        ResponsesClientOptions clientOptions = new()
        {
            // ZERO automatic retries.
            //
            // The SDK retries 408, 429 and 5xx by default. That default is
            // reasonable in general and wrong for this platform right now: no
            // retry policy has been designed, and a hidden one would mean a
            // single preview quietly becoming several billed calls, an
            // "unavailable" answer arriving after three timeouts rather than one,
            // and load multiplying during exactly the incidents where that hurts
            // most. Resilience will be added deliberately, once real provider
            // failure behaviour has been observed rather than guessed at.
            //
            // ClientRetryPolicy(0) still runs the pipeline once -- it disables
            // retrying, not the request.
            RetryPolicy = new ClientRetryPolicy(maxRetries: 0),

            // A finite wait. An unbounded one is how a slow dependency becomes an
            // outage: requests accumulate faster than they retire. The caller's
            // own cancellation token still propagates and still wins.
            NetworkTimeout = options.Timeout,
        };

        if (transport is not null)
        {
            clientOptions.Transport = transport;
        }

        // ApiKeyCredential holds the secret. It is passed straight in and is
        // never written to a field of ours, a log, an exception or an options
        // object.
        return new ResponsesClient(new ApiKeyCredential(apiKey), clientOptions);
    }
}

#pragma warning restore OPENAI001
